using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Unity.LiveCapture.Networking
{
    /// <summary>
    /// Represents a connection to a remote.
    /// </summary>
    /// <remarks>
    /// The connection health is tested using a heartbeat, sending and receiving UDP packets periodically from the
    /// remote to ensure it is still reachable.
    /// </remarks>
    internal class Connection
    {
        /// <summary>
        /// The threshold for missed heartbeat messages before the connection is assumed to be dead.
        /// </summary>
        private const int HeartbeatDisconnectCount = 8;

        /// <summary>
        /// The duration in seconds between heartbeat messages.
        /// </summary>
        private static readonly TimeSpan HeartbeatPeriod = TimeSpan.FromSeconds(1.0);
        private static readonly TimeSpan CheckHeartbeatPeriod = TimeSpan.FromSeconds(0.1);

        public event Action<Message> MessageReceived;
        public event Action<DisconnectStatus> Closed;
        
        private readonly INetworkSocket _tcpSocket;
        private readonly INetworkSocket _udpSocket;
        
        private readonly CancellationTokenSource _heartbeatCancellationToken = new CancellationTokenSource();

        private DateTime? _lastHeartbeatTime;
        private bool _disposed;

        /// <summary>
        /// The remote at the other end of the connection.
        /// </summary>
        public Remote Remote { get; }

        /// <summary>
        /// Creates a new <see cref="Connection"/> instance.
        /// </summary>
        /// <param name="tcp">The local tcp socket connected to the remote.</param>
        /// <param name="udp">The local udp socket used to communicate with the remote.</param>
        /// <param name="remote">The remote at the other end of the connection.</param>
        public Connection(INetworkSocket tcp, INetworkSocket udp, Remote remote)
        {
            _tcpSocket = tcp ?? throw new ArgumentNullException(nameof(tcp));
            _udpSocket = udp ?? throw new ArgumentNullException(nameof(udp));
            Remote = remote ?? throw new ArgumentNullException(nameof(remote));

            _tcpSocket.PacketReceived += OnPackageReceived;
            _udpSocket.PacketReceived += OnPackageReceived;

            StartDoingHeartbeat(_heartbeatCancellationToken);
            
            StartCheckingHeartbeat(_heartbeatCancellationToken);
        }

        /// <summary>
        /// Disposes of this connection.
        /// </summary>
        /// <param name="status">How the connection was terminated.</param>
        public void Close(DisconnectStatus status)
        {
            if (_disposed)
                return;

            _disposed = true;

            _heartbeatCancellationToken.Cancel();

            Closed?.Invoke(status);

            // Remote the connection from sockets that are shared, and
            // dispose sockets that are exclusive to this connection.
            if (!_udpSocket.IsDisposed)
            {
                _udpSocket.PacketReceived -= OnPackageReceived;

                if (_udpSocket.RemoteEndPoint != null)
                    _udpSocket.Dispose();
            }
            if (!_tcpSocket.IsDisposed)
            {
                _udpSocket.PacketReceived -= OnPackageReceived;
                _tcpSocket.Dispose();
            }
        }

        /// <summary>
        /// Sends a message on this connection.
        /// </summary>
        /// <param name="packet">The packet to send.</param>
        /// <param name="synchronous">If true the caller is blocked until the message is received by the remote.</param>
        public void Send(Packet packet, bool synchronous)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Connection));
            switch (packet.Message.ChannelType)
            {
                case ChannelType.ReliableOrdered:
                    _tcpSocket.Send(packet, synchronous);
                    break;
                case ChannelType.UnreliableUnordered:
                    _udpSocket.Send(packet, synchronous);
                    break;
                default:
                    throw new ArgumentException($"Message channel {packet.Message.ChannelType} is not supported.");
            }
        }

        /// <summary>
        /// Checks the connection health.
        /// </summary>
        private async void StartCheckingHeartbeat(CancellationTokenSource cancellationToken)
        {
            // Check if any of the last few heartbeat messages were received. If we have not received
            // any for long enough, we can assume the connection is lost. Losing multiple packets sent
            // on a local network seconds apart is extremely unlikely on a modern network.

            while (true)
            {
                // An issue was encountered on iOS only where m_LastHeartbeat was not being correctly set
                // when assigned the value DateTime.Now in the constructor, instead being set to the default
                // DateTime value. To fix the issue we initialize the value here. It could be related to the
                // fact the constructor is called from the thread pool. This only occurs for the first
                // connection however.
                _lastHeartbeatTime ??= DateTime.Now;

                try
                {
                    await Task.Delay(CheckHeartbeatPeriod, cancellationToken.Token);
                }
                catch (TaskCanceledException)
                {
                }

                if (cancellationToken.IsCancellationRequested)
                    return;

                var timeSinceLastBeat = (DateTime.Now - _lastHeartbeatTime.Value).TotalSeconds;
                var disconnectDuration = HeartbeatDisconnectCount * HeartbeatPeriod.TotalSeconds;

                if (timeSinceLastBeat > disconnectDuration)
                    Close(DisconnectStatus.Timeout);
            }
        }

        private void OnPackageReceived(Packet packet)
        {
            var message = packet.Message;

            switch (packet.PacketType)
            {
                case Packet.Type.Generic:
                {
                    MessageReceived?.Invoke(message);
                    break;
                }
                case Packet.Type.Heartbeat:
                {
                    _lastHeartbeatTime = DateTime.Now;
                    message.Dispose();
                    break;
                }
                case Packet.Type.Disconnect:
                {
                    Close(DisconnectStatus.Graceful);
                    message.Dispose();
                    return;
                }
                case Packet.Type.Invalid:
                    Debug.LogWarning("Invalid package received");
                    break;
                case Packet.Type.Initialization:
                    break;
                default:
                    Debug.LogError($"A packet of type {packet.PacketType} ({(int)packet.PacketType}) was received but that type is never used!");
                    message.Dispose();
                    break;
            }
        }

        private async void StartDoingHeartbeat(CancellationTokenSource cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(HeartbeatPeriod, cancellationToken.Token);
                }
                catch (TaskCanceledException)
                {
                }

                if (!_heartbeatCancellationToken.IsCancellationRequested)
                {
                    var message = Message.Get(Remote.ID, ChannelType.UnreliableUnordered);
                    var packet = new Packet(message, Packet.Type.Heartbeat);

                    Send(packet, false);
                }
            }
        }
    }
}
