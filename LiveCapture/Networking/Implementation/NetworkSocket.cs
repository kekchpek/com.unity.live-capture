using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Unity.LiveCapture.Networking
{
    /// <summary>
    /// Handles sending and receiving message for a single socket.
    /// </summary>
    /// <remarks>
    /// This class is thread safe.
    /// </remarks>
    internal class NetworkSocket : IDisposable, INetworkSocket
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct PacketHeader
        {
            public static readonly int Size = Marshal.SizeOf<PacketHeader>();

            public Guid SenderID;
            public Packet.Type Type;
            public int DataLength;
        }

        class SendState
        {
            public Packet Packet;
        }

        class ReceiveState
        {
            public int MessageLength;
            public int BytesReceived;
        }

        /// <summary>
        /// The limit is (2^16 - 1) - 20 byte IP header - 8 byte UDP header.
        /// </summary>
        /// <remarks>
        /// To avoid fragmenting packets which increases risk of packet loss, we could enforce packet sizes to be less than the MTU of
        /// ethernet (1500 bytes) or wifi (2312 bytes).
        /// </remarks>
        const int UdpMessageSizeMax = ushort.MaxValue - 20 - 8;

        /// <summary>
        /// The time in milliseconds reliable sockets will wait before throwing an exception if a send
        /// operation has not yet finished.
        /// </summary>
        const int ReliableSendTimeout = 10 * 1000;

        static readonly ConcurrentBag<SocketAsyncEventArgs> SendArgsPool = new ConcurrentBag<SocketAsyncEventArgs>();
        static readonly BufferPool BufferPool = new BufferPool(UdpMessageSizeMax);
        
        public event Action<Packet> PacketReceived;
        public event Action SocketError;

        private readonly NetworkBase _network;
        private readonly Socket _socket;
        private readonly bool _isShared;
        private readonly Action<Remote> _onInitialized;
        private readonly bool _isTcp;
        private readonly ChannelType _channelType;
        private readonly object _connectionLock = new object();
        private readonly SocketAsyncEventArgs _receiveArgs;

        private volatile bool _disposed;

        /// <summary>
        /// The address and port the socket is bound to.
        /// </summary>
        public Socket Socket
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(NetworkSocket));

                return _socket;
            }
        }

        /// <summary>
        /// The address and port the socket is bound to.
        /// </summary>
        public IPEndPoint LocalEndPoint
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(NetworkSocket));

                return _socket.LocalEndPoint as IPEndPoint;
            }
        }

        /// <summary>
        /// The remote address and port the socket is communicating with. Will be null
        /// if this socket communicates with many remotes.
        /// </summary>
        public IPEndPoint RemoteEndPoint
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(NetworkSocket));

                try
                {
                    return _socket.RemoteEndPoint as IPEndPoint;
                }
                catch (SocketException)
                {
                    // this will be thrown for UDP sockets that have not had Connect called on them
                    return null;
                }
            }
        }

        /// <summary>
        /// Has the socket been closed.
        /// </summary>
        public bool IsDisposed => _disposed;

        /// <summary>
        /// Creates a new <see cref="NetworkSocket"/> instance.
        /// </summary>
        /// <param name="network">The networking instance that manages this socket.</param>
        /// <param name="socket">The socket to take ownership of.</param>
        /// <param name="isShared">Indicates if the socket communicate with many remotes, as
        /// opposed to being owned by a single connection.</param>
        /// <param name="onInitialized">A callback executed when an initialization message is
        /// received on this socket to establish a new connection.</param>
        public NetworkSocket(NetworkBase network, Socket socket, bool isShared, Action<Remote> onInitialized = null)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _isShared = isShared;
            _onInitialized = onInitialized;

            switch (socket.ProtocolType)
            {
                case ProtocolType.Tcp:
                {
                    _isTcp = true;
                    _channelType = ChannelType.ReliableOrdered;

                    // Disable Nagle's Algorithm for tcp sockets. This helps to reduce latency when
                    // fewer, smaller message are being sent.
                    _socket.NoDelay = true;

                    // If a connection is idle for a long time, it may be closed by routers/firewalls.
                    // This option ensures that the connection keeps active.
                    _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                    // By default tcp sockets will persist after being closed in order to ensure all
                    // data has been send and received successfully, but this will block the port for a while.
                    // We need to disable this behaviour so the socket closes immediately.
                    _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, false);
                    _socket.LingerState = new LingerOption(true, 0);

                    // the default timeout is infinite, which is not ideal
                    _socket.SendTimeout = ReliableSendTimeout;
                    break;
                }
                case ProtocolType.Udp:
                {
                    _isTcp = false;
                    _channelType = ChannelType.UnreliableUnordered;

                    // On Mac we need to ensure the buffers are large enough to contain the
                    // largest size of message we want to be able to send to prevent errors.
                    _socket.ReceiveBufferSize = UdpMessageSizeMax;
                    _socket.SendBufferSize = UdpMessageSizeMax;

                    // By default we receive "Port Unreachable" ICMP messages for packets that fail to reach
                    // their destination, causing a ConnectionReset socket exception to be thrown. We don't want
                    // that exception to be thrown ideally, so we try to disable them on implementations for
                    // which it is supported.
                    // https://docs.microsoft.com/en-us/windows/win32/winsock/winsock-ioctls?redirectedfrom=MSDN
                    // SIO_UDP_CONNRESET = 0x9800000C
                    const int ioUdpConnectionReset = -1744830452;

                    try
                    {
                        _socket.IOControl(ioUdpConnectionReset, new byte[] { 0, 0, 0, 0 }, null);
                    }
                    catch (SocketException)
                    {
                    }

                    break;
                }
                default:
                    throw new ArgumentException($"Socket uses {socket.ProtocolType} protocol, but only TCP or UDP sockets are supported.", nameof(socket));
            }

            // don't allow binding this socket to a local port already in use, instead throw an error
            _socket.ExclusiveAddressUse = true;

            // Start receiving messages on the socket. This is safe to do, event if we are not connected
            // yet, as the receive loop will repeat until the connection is formed
            _receiveArgs = new SocketAsyncEventArgs();
            _receiveArgs.SetBuffer(new byte[UdpMessageSizeMax], 0, UdpMessageSizeMax);
            _receiveArgs.Completed += ReceiveComplete;
            _receiveArgs.UserToken = new ReceiveState();

            BeginReceive();
        }

        /// <summary>
        /// Closes the socket and clears all received packed.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            NetworkUtilities.DisposeSocket(_socket);
        }

        /// <summary>
        /// Sends a packet on this socket.
        /// </summary>
        /// <param name="packet">The packet to send.</param>
        /// <param name="synchronous">If true the caller is blocked until the message is received by the remote.</param>
        public void Send(Packet packet, bool synchronous)
        {
            try
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(NetworkSocket));

                var message = packet.Message;
                var data = message.Data;

                if (!_isTcp)
                {
                    var maxLength = UdpMessageSizeMax - PacketHeader.Size;

                    if (data.Length > maxLength)
                        throw new ArgumentException($"Message is {data.Length} bytes long but messages using {nameof(ChannelType.UnreliableUnordered)} are limited to {maxLength} bytes.");
                }

                var count = PacketHeader.Size + (int)data.Length;
                var buffer = BufferPool.Get(count);

                var header = new PacketHeader
                {
                    SenderID = _network.ID,
                    Type = packet.PacketType,
                    DataLength = (int)data.Length,
                };

                buffer.WriteStruct(ref header);
                data.Seek(0, SeekOrigin.Begin);
                data.Read(buffer, PacketHeader.Size, header.DataLength);

                if (RemoteFactoryStaticProvider.RemoteFactory.TryGetCreated(message.RemoteId, out var remote))
                {
                    // ReSharper disable once InconsistentlySynchronizedField
                    var endPoint = _channelType switch
                    {
                        ChannelType.ReliableOrdered => remote.TcpEndPoint,
                        ChannelType.UnreliableUnordered => remote.UdpEndPoint,
                        _ => throw new InvalidOperationException("Unknown channel type")
                    };

                    if (synchronous)
                    {
                        try
                        {
                            _socket.SendTo(buffer, 0, count, SocketFlags.None, endPoint);
                        }
                        catch (SocketException e)
                        {
                            HandleSendError(e.SocketErrorCode);
                        }
                        finally
                        {
                            BufferPool.Release(buffer);

                            packet.Message.Dispose();
                        }
                    }
                    else
                    {
                        if (!SendArgsPool.TryTake(out var args))
                        {
                            args = new SocketAsyncEventArgs();
                            args.Completed += SendComplete;
                            args.UserToken = new SendState();
                        }

                        var state = args.UserToken as SendState;
                        if (state == null)
                            throw new InvalidOperationException("Unexpected user token");
                        state.Packet = packet;

                        args.SetBuffer(buffer, 0, count);
                        args.RemoteEndPoint = endPoint;

                        // SendTo is not valid for connected sockets on all implementations.
                        // See error code WSAEISCONN here:
                        // https://docs.microsoft.com/en-us/windows/win32/winsock/windows-sockets-error-codes-2
                        var isAsync = _isShared ? _socket.SendToAsync(args) : _socket.SendAsync(args);

                        if (!isAsync)
                            SendComplete(_socket, args);
                    }
                }
            }
            catch
            {
                packet.Message.Dispose();
                throw;
            }
        }

        void SendComplete(object sender, SocketAsyncEventArgs args)
        {
            var state = args.UserToken as SendState;

            try
            {
                HandleSendError(args.SocketError);
            }
            finally
            {
                BufferPool.Release(args.Buffer);
                SendArgsPool.Add(args);

                state?.Packet.Message.Dispose();
            }
        }

        void HandleSendError(SocketError socketError)
        {
            var logError = false;

            switch (socketError)
            {
                case System.Net.Sockets.SocketError.Success:
                    break;

                // When the socket is disposed, suppress the error since it is expected that
                // the operation should not complete.
                case System.Net.Sockets.SocketError.Shutdown:
                case System.Net.Sockets.SocketError.Interrupted:
                case System.Net.Sockets.SocketError.OperationAborted:
                case System.Net.Sockets.SocketError.ConnectionAborted:
                case System.Net.Sockets.SocketError.Disconnecting:
                    break;

                // For TCP sockets this indicates the connection is no longer valid. For UDP
                // sockets this error indicates a datagram was not received and an ICMP
                // "Port Unreachable" response was received. We don't care if a UDP packet
                // was not received, so don't close the connection in that case.
                case System.Net.Sockets.SocketError.ConnectionReset:
                    if (_isTcp)
                    {
                        SocketError?.Invoke();
                        // fail silently since this is fairly common
                    }
                    break;

                // The message size error should only apply to UDP sockets. On Mac however,
                // this error is reported by TCP sockets sending large messages even though
                // the messages are correctly received.
                case System.Net.Sockets.SocketError.MessageSize:
                    if (!_isTcp)
                    {
                        SocketError?.Invoke();
                        logError = true;
                    }
                    break;

                default:
                    SocketError?.Invoke();
                    logError = true;
                    break;
            }

            if (logError)
                Debug.LogError($"Failed to send {_socket.ProtocolType} message with socket error: {socketError} ({(int)socketError})");
        }

        void BeginReceive()
        {
            var args = _receiveArgs;

            // clear the state to prepare for receiving a new message
            var state = args.UserToken as ReceiveState;
            if (state == null)
            {
                Debug.LogError("Unexpected state!");
                return;
            }
            state.BytesReceived = 0;
            state.MessageLength = -1;

            // Ensure we don't read past the header when using a stream protocol until we
            // know how much data to expect. This way we don't read into any following messages.
            if (_isTcp)
                args.SetBuffer(args.Buffer, 0, PacketHeader.Size);

            ContinueReceive(args);
        }

        void ContinueReceive(SocketAsyncEventArgs args)
        {
            // When this returns false it has completed synchronously and the receive
            // callback will not be called automatically.
            if (!_socket.ReceiveAsync(args))
                ReceiveComplete(_socket, args);
        }

        void ReceiveComplete(object sender, SocketAsyncEventArgs args)
        {
            try
            {
                switch (args.SocketError)
                {
                    case System.Net.Sockets.SocketError.Success:
                        break;

                    // if we are not connected yet, keep waiting until we are connected
                    case System.Net.Sockets.SocketError.NotConnected:
                    case System.Net.Sockets.SocketError.WouldBlock:
                        BeginReceive();
                        return;

                    // When the socket is disposed, suppress the error since it is expected that
                    // the operation should not complete.
                    case System.Net.Sockets.SocketError.Shutdown:
                    case System.Net.Sockets.SocketError.Interrupted:
                    case System.Net.Sockets.SocketError.OperationAborted:
                    case System.Net.Sockets.SocketError.ConnectionAborted:
                    case System.Net.Sockets.SocketError.ConnectionRefused:
                    case System.Net.Sockets.SocketError.Disconnecting:
                        return;

                    // For TCP sockets this indicates the connection is no longer valid. For UDP
                    // sockets this error indicates a datagram was not received and an ICMP
                    // "Port Unreachable" response was received. We don't care if a UDP packet
                    // was not received, so don't close the connection in that case.
                    case System.Net.Sockets.SocketError.ConnectionReset:
                        if (_isTcp)
                        {
                            SocketError?.Invoke();
                            return;
                        }
                        break;

                    default:
                        throw new SocketException((int)args.SocketError);
                }

                if (_isTcp && args.UserToken is ReceiveState state)
                {
                    // We must not assume the entire message has arrived yet, messages can be received in fragments
                    // of any size when using a stream socket type. First we need to receive the header so we know
                    // how much following data to read.
                    state.BytesReceived += args.BytesTransferred;

                    if (state.BytesReceived != state.MessageLength)
                    {
                        if (state.BytesReceived < PacketHeader.Size)
                        {
                            args.SetBuffer(args.Buffer, state.BytesReceived, PacketHeader.Size - state.BytesReceived);
                        }
                        else if (state.MessageLength < 0)
                        {
                            state.MessageLength = PacketHeader.Size + args.Buffer.ReadStruct<PacketHeader>().DataLength;

                            var buffer = args.Buffer;
                            if (buffer.Length < state.MessageLength)
                            {
                                buffer = new byte[state.MessageLength];
                                Buffer.BlockCopy(args.Buffer, 0, buffer, 0, state.BytesReceived);
                            }

                            args.SetBuffer(buffer, state.BytesReceived, state.MessageLength - state.BytesReceived);
                        }
                        else
                        {
                            args.SetBuffer(args.Buffer, state.BytesReceived, state.MessageLength - state.BytesReceived);
                        }

                        ContinueReceive(args);
                        return;
                    }
                }

                var header = args.Buffer.ReadStruct<PacketHeader>();

                if (header.Type == Packet.Type.Initialization)
                {
                    if (_onInitialized == null)
                        throw new Exception($"An initialization message was received but no initialization function was provided.");

                    var offset = PacketHeader.Size;
                    var versionData = args.Buffer.ReadStruct<VersionData>(offset);

                    var version = versionData.GetVersion();
                    if (version != _network.ProtocolVersion)
                        throw new Exception($"Cannot initialize connection, there is a protocol version mismatch (local={_network.ProtocolVersion} remote={version}).");

                    offset += Marshal.SizeOf<VersionData>();
                    var remoteData = args.Buffer.ReadStruct<RemoteData>(offset);

                    var remote = RemoteFactoryStaticProvider.RemoteFactory.Create(remoteData.ID, remoteData.GetTcpEndPoint(), remoteData.GetUdpEndPoint());
                    _onInitialized(remote);
                }
                else
                {
                    lock (_connectionLock)
                    {
                        var message = Message.Get(header.SenderID, _channelType, header.DataLength);
                        var packet = new Packet(message, header.Type);

                        var data = packet.Message.Data;
                        data.Write(args.Buffer, PacketHeader.Size, header.DataLength);
                        data.Seek(0, SeekOrigin.Begin);

                        PacketReceived?.Invoke(packet);
                    }
                }

                BeginReceive();
            }
            catch (ObjectDisposedException)
            {
                // suppress the exception thrown by this callback if the socket was disposed
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to receive {_socket.ProtocolType} message: {e}");
                SocketError?.Invoke();
            }
        }
    }
}
