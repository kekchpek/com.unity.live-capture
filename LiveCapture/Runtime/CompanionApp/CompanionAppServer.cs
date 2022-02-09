using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityAuxiliaryTools.UnityExecutor;
using Unity.LiveCapture.Networking;
using Unity.LiveCapture.Networking.Discovery;
using UnityEngine;

namespace Unity.LiveCapture.CompanionApp
{
    /// <summary>
    /// The server used to communicate with the companion apps.
    /// </summary>
    [CreateServerMenuItem("Companion App Server")]
    public class CompanionAppServer
    {

        /// <summary>
        /// The server executes this event when a client has connected.
        /// </summary>
        public static event Action<ICompanionAppClient> ClientConnected = delegate {};

        /// <summary>
        /// The server executes this event when a client has disconnected.
        /// </summary>
        public static event Action<ICompanionAppClient> ClientDisconnected = delegate {};

        private struct ConnectHandler
        {
            public string Name;
            public DateTime Time;
            public Func<ICompanionAppClient, bool> Handler;
        }

        private static readonly Dictionary<string, Type> TypeToClientType = new Dictionary<string, Type>();
        private static readonly List<ConnectHandler> ClientConnectHandlers = new List<ConnectHandler>();

        /// <summary>
        /// Adds a callback used to take ownership of a client that has connected.
        /// </summary>
        /// <param name="handler">The callback function. It must return true if it takes ownership of a client.</param>
        /// <param name="name">The name of the client to prefer. If set, this handler has priority over clients that have the given name.</param>
        /// <param name="time">The time used to determine the priority of handlers when many are listening for the same
        /// client <paramref name="name"/>. More recent values have higher priority.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="handler"/> is null.</exception>
        public static void RegisterClientConnectHandler(Func<ICompanionAppClient, bool> handler, string name, DateTime time)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            DeregisterClientConnectHandler(handler);

            ClientConnectHandlers.Add(new ConnectHandler
            {
                Name = name,
                Time = time,
                Handler = handler,
            });
        }

        /// <summary>
        /// Removes a client connection callback.
        /// </summary>
        /// <param name="handler">The callback to remove.</param>>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="handler"/> is null.</exception>
        public static void DeregisterClientConnectHandler(Func<ICompanionAppClient, bool> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            for (var i = 0; i < ClientConnectHandlers.Count; i++)
            {
                if (ClientConnectHandlers[i].Handler == handler)
                {
                    ClientConnectHandlers.RemoveAt(i);
                }
            }
        }

        static CompanionAppServer()
        {
            foreach (var(type, attributes) in AttributeUtility.GetAllTypes<ClientAttribute>())
            {
                if (!typeof(CompanionAppClient).IsAssignableFrom(type))
                {
                    Debug.LogError($"{type.FullName} must be assignable from {nameof(CompanionAppClient)} to use the {nameof(ClientAttribute)} attribute.");
                    continue;
                }

                foreach (var attribute in attributes)
                {
                    TypeToClientType[attribute.Type] = type;
                }
            }
        }

        private readonly DiscoveryServer _discovery = new DiscoveryServer();
        private readonly NetworkServer _server = new NetworkServer(new GameObject().AddComponent<UnityExecutor>());
        private readonly Dictionary<Guid, ICompanionAppClient> _remoteToClient = new Dictionary<Guid, ICompanionAppClient>();

        /// <summary>
        /// Are clients able to connect to the server.
        /// </summary>
        public bool IsRunning => _server.IsRunning;

        /// <summary>
        /// The number of clients currently connected to the server.
        /// </summary>
        public int ClientCount => _remoteToClient.Count;

        /// <summary>
        /// Gets the currently connected clients.
        /// </summary>
        /// <returns>A new collection containing the client handles.</returns>
        public IEnumerable<ICompanionAppClient> GetClients()
        {
            return _remoteToClient.Values;
        }

        /// <summary>
        /// Start listening for clients connections.
        /// </summary>
        /// <param name="port">The port on which server should start</param>
        /// <returns>True if success</returns>
        public bool StartServer(int port)
        {
            if (!NetworkUtilities.IsPortAvailable(port))
            {
                Debug.LogError($"Unable to start server: Port {port} is in use by another program! Close the other program, or assign a free port using the Live Capture Window.");
                return false;
            }

            _server.Stopped += () =>
            {
                Debug.Log("Network server stopped");
            };
            _server.Started += () =>
            {
                Debug.Log("Network server started");
            };
            if (_server.StartServer(port))
            {
                // start server discovery
                var config = new ServerData(
                    "Live Capture",
                    Environment.MachineName,
                    _server.ID,
                    PackageUtility.GetVersion(LiveCaptureInfo.Version)
                );
                var endPoints = _server.EndPoints.ToArray();

                _discovery.Start(config, endPoints);
            }

            _server.RemoteConnected += OnClientConnected;
            _server.RemoteDisconnected += OnClientDisconnected;
            
            return true;
        }

        /// <summary>
        /// Disconnects all clients and stop listening for new connections.
        /// </summary>
        public void StopServer()
        {

            _server.RemoteConnected -= OnClientConnected;
            _server.RemoteDisconnected -= OnClientDisconnected;
            
            _server.Stop();
            _discovery.Stop();
        }

        void OnClientConnected(Remote remote)
        {
            _server.RegisterMessageHandler(remote, InitializeClient, false);
        }

        void OnClientDisconnected(Remote remote, DisconnectStatus status)
        {
            if (_remoteToClient.TryGetValue(remote.ID, out var client))
            {
                try
                {
                    ClientDisconnected.Invoke(client);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }

                _remoteToClient.Remove(remote.ID);
            }
        }

        void InitializeClient(Message message)
        {
            try
            {
                if (message.ChannelType != ChannelType.ReliableOrdered)
                {
                    return;
                }

                var streamReader = new StreamReader(message.Data, Encoding.UTF8);
                var json = streamReader.ReadToEnd();
                ClientInitialization data;

                try
                {
                    data = JsonUtility.FromJson<ClientInitialization>(json);
                }
                catch (Exception)
                {
                    Debug.LogError($"{nameof(CompanionAppServer)} failed to initialize client connection! Could not parse JSON: {json}");
                    return;
                }

                if (!TypeToClientType.TryGetValue(data.Type, out var clientType))
                {
                    Debug.LogError($"Unknown client type \"{data.Type}\" connected to {nameof(CompanionAppServer)}!");
                    return;
                }

                if (RemoteFactoryStaticProvider.RemoteFactory.TryGetCreated(message.RemoteId, out var remote))
                {
                    var client = Activator.CreateInstance(clientType, _server, remote, data) as CompanionAppClient;
                    client!.SendProtocol();

                    _remoteToClient.Add(remote.ID, client);

                    AssignOwner(client);

                    ClientConnected.Invoke(client);
                }
                else
                {
                    Debug.Log("Fail to determine remote");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                message.Dispose();
            }
        }

        static void AssignOwner(ICompanionAppClient client)
        {
            // connect to the registered handler that was most recently used with this client if possible
            foreach (var handler in ClientConnectHandlers.OrderByDescending(h => h.Time.Ticks))
            {
                try
                {
                    if (handler.Name == client.Name)
                    {
                        if (handler.Handler(client))
                            return;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            // fall back to the first free device that is compatible with the client
            foreach (var handler in ClientConnectHandlers)
            {
                try
                {
                    if (handler.Handler(client))
                        return;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
