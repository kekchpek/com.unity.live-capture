using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityAuxiliaryTools.UnityExecutor;
using UnityEngine;

namespace Unity.LiveCapture.Networking
{
    /// <summary>
    /// A client that can connect to a <see cref="NetworkServer"/> instance.
    /// </summary>
    internal class NetworkClient : NetworkBase
    {
        /// <summary>
        /// Used to keep track of the client state for connection attempts. This is used
        /// as opposed to having these fields in <see cref="NetworkClient"/> so that the
        /// old connection tasks that are being cancelled cannot modify the current state.
        /// </summary>
        private class ConnectState
        {
            public IPEndPoint RemoteEndPoint;
            public IPEndPoint LocalEndPoint;
            public INetworkSocket Udp;
            public INetworkSocket Tcp;
            public CancellationTokenSource CancellationToken;
        }

        private ConnectState _connectState;

        /// <summary>
        /// The IP and port the client is using to communicate, or null if the client is not running.
        /// </summary>
        public IPEndPoint LocalEndPoint => MIsRunning ? _connectState.LocalEndPoint : null;

        /// <summary>
        /// The IP and port of the server the client is connecting to, or null if the client is not running.
        /// </summary>
        public IPEndPoint ServerEndPoint => MIsRunning ? _connectState.RemoteEndPoint : null;

        /// <summary>
        /// Is this client currently trying to connect to a server.
        /// </summary>
        public bool IsConnecting => MIsRunning && !_connectState.CancellationToken.IsCancellationRequested;

        /// <summary>
        /// How long in milliseconds to wait between connection attempts.
        /// </summary>
        public int ConnectAttemptTimeout { get; set; } = 2000;


        /// <summary>
        /// Creates a new <see cref="NetworkClient"/> instance.
        /// </summary>
        public NetworkClient(IUnityExecutor unityExecutor) : base(unityExecutor)
        {
            RemoteDisconnected += OnDisconnect;
        }

        /// <summary>
        /// Connects to a server.
        /// </summary>
        /// <remarks>
        /// If the client is already running but a new server address is provided, the client will
        /// be restarted and connect to the new end point. The client will try to connect endlessly
        /// until connected or <see cref="Stop"/> is called, waiting <see cref="ConnectAttemptTimeout"/>
        /// ms between connection attempts.
        /// </remarks>
        /// <param name="serverIP">The remote address of the server.</param>
        /// <param name="serverPort">The TCP/UDP port on the server to connect to.</param>
        /// <param name="localPort">The local TCP/UDP port to connect using. Use 0 to let the
        /// system automatically assign free ports.</param>
        /// <returns>True if the server started successfully.</returns>
        public bool ConnectToServer(string serverIP, int serverPort, int localPort = 0)
        {
            if (!IPAddress.TryParse(serverIP, out var ip))
            {
                Debug.LogError($"Unable to start client: IPv4 address \"{serverIP}\" is not valid.");
                return false;
            }

            if (!NetworkUtilities.IsPortValid(serverPort, out var serverPortMessage))
            {
                Debug.LogError($"Unable to start client: Server port {serverPort} is not valid. {serverPortMessage}");
                return false;
            }

            if (localPort != 0)
            {
                if (!NetworkUtilities.IsPortValid(localPort, out var localPortMessage))
                {
                    Debug.LogError($"Unable to start client: Local port {localPort} is not valid. {localPortMessage}");
                    return false;
                }
                if (!string.IsNullOrEmpty(localPortMessage))
                {
                    Debug.Log($"Client: {localPortMessage}");
                }
            }

            var remoteEndPoint = new IPEndPoint(ip, serverPort);

            if (IsRunning)
            {
                if (remoteEndPoint.Equals(ServerEndPoint))
                    return true;

                Stop();
            }

            if (!StartInternal(remoteEndPoint, localPort))
                return false;

            MIsRunning = true;
            RaiseStartedEvent();
            return true;
        }

        /// <inheritdoc/>
        public override void Stop(bool graceful = true)
        {
            _connectState?.CancellationToken?.Cancel();

            base.Stop(graceful);

            if (_connectState != null)
            {
                _connectState.Udp?.Dispose();
                _connectState.Tcp?.Dispose();
                _connectState = null;
            }
        }

        private bool StartInternal(IPEndPoint remoteEndPoint, int localPort)
        {
            // We create a new instance so that if the client is stopped we can safely modify the state
            // without worrying about it being modified by the EndConnect callback.
            _connectState = new ConnectState
            {
                RemoteEndPoint = remoteEndPoint,
                LocalEndPoint = new IPEndPoint(NetworkUtilities.GetRoutingInterface(remoteEndPoint), localPort),
            };

            if (!CreateUdpSocket(_connectState))
            {
                Stop();
                return false;
            }

            ConnectAsync(_connectState);

            return true;
        }

        private bool CreateUdpSocket(ConnectState state)
        {
            try
            {
                var socket = NetworkUtilities.CreateSocket(ProtocolType.Udp);
                state.Udp = new NetworkSocket(this, socket, false);

                socket.Bind(state.LocalEndPoint);

                // UDP is connectionless, but by calling connect we configure the socket to reject
                // all packets received by the socket not from the server.
                socket.Connect(state.RemoteEndPoint);
            }
            catch (Exception e)
            {
                Debug.LogError($"Client: Failed to create UDP socket on {state.LocalEndPoint}. {e}");
                return false;
            }

            return true;
        }

        private bool CreateTcpSocket(ConnectState state)
        {
            try
            {
                var socket = NetworkUtilities.CreateSocket(ProtocolType.Tcp);
                state.Tcp = new NetworkSocket(this, socket, false, (remote) =>
                {
                    state.CancellationToken.Cancel();

                    var connection = new Connection(state.Tcp, state.Udp, remote);
                    connection.MessageReceived += HandleMessage;
                    DoHandshake(state.Tcp, state.Udp);
                    RegisterConnection(connection);
                    void Deregister(DisconnectStatus status)
                    {
                        DeregisterConnection(connection, status);
                        connection.Closed -= Deregister;
                        connection.MessageReceived -= HandleMessage;
                    }
                    connection.Closed += Deregister;
                });

                socket.Bind(state.LocalEndPoint);
            }
            catch (Exception e)
            {
                Debug.LogError($"Client: Failed to create TCP socket on {state.LocalEndPoint}. {e}");
                return false;
            }

            return true;
        }

        private async void ConnectAsync(ConnectState state)
        {
            state.CancellationToken = new CancellationTokenSource();

            while (!state.CancellationToken.IsCancellationRequested)
            {
                try
                {
                    // sockets that fail to connect need to be disposed and recreated, otherwise subsequent
                    // connection calls to connect will fail
                    state.Tcp?.Dispose();

                    if (CreateTcpSocket(state))
                        state.Tcp.Socket.BeginConnect(state.RemoteEndPoint, EndConnect, state);

                    await Task.Delay(ConnectAttemptTimeout, state.CancellationToken.Token);
                }
                catch (OperationCanceledException)
                {
                    // this is expected when the connection attempts are stopped
                }
                catch (Exception e)
                {
                    Debug.LogError($"Client: Failed to begin connection attempt. {e}");
                }
            }
        }

        private void EndConnect(IAsyncResult result)
        {
            var state = result.AsyncState as ConnectState;

            try
            {
                state.Tcp.Socket.EndConnect(result);
            }
            catch (ObjectDisposedException)
            {
                // This callback is invoked while the connecting socket is closing.
                // The call to EndConnect will throw this exception when that occurs.
            }
            catch (SocketException e)
            {
                switch (e.SocketErrorCode)
                {
                    case SocketError.ConnectionRefused:
                        Debug.LogWarning($"Client: {state.RemoteEndPoint} refused the connection.");
                        break;
                    default:
                        Debug.LogError($"Client: Failed to connect. {e}");
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Client: Failed to connect. {e}");
            }
        }

        private void OnDisconnect(Remote remote, DisconnectStatus status)
        {
            switch (status)
            {
                case DisconnectStatus.Graceful:
                    break;
                default:
                    // when there is a non-graceful disconnect we should try to reconnect to the
                    // server to avoid needing to stop and start the client.
                    StartInternal(_connectState.RemoteEndPoint, _connectState.LocalEndPoint.Port);
                    break;
            }
        }
    }
}
