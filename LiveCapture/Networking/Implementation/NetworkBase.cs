using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityAuxiliaryTools.UnityExecutor;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.LiveCapture.Networking
{
    /// <summary>
    /// A class containing networking functionality shared by clients and servers.
    /// </summary>
    abstract class NetworkBase
    {
        private readonly IUnityExecutor _unityExecutor;

        readonly struct ConnectionEvent
        {
            public enum Type
            {
                /// <summary>
                /// A connection was established and needs set as the current connection for
                /// the corresponding remote.
                /// </summary>
                NewConnection,
                /// <summary>
                /// The connection needs to be closed immediately.
                /// </summary>
                TerminateConnection,
            }

            /// <summary>
            /// The connection this event applies to.
            /// </summary>
            public Connection Connection { get; }

            /// <summary>
            /// The type of connection event.
            /// </summary>
            public Type EventType { get; }

            /// <summary>
            /// Creates a new <see cref="ConnectionEvent"/> instance.
            /// </summary>
            /// <param name="connection">The connection this event applies to.</param>
            /// <param name="type">The type of connection event.</param>
            public ConnectionEvent(Connection connection, Type type)
            {
                Connection = connection;
                EventType = type;
            }
        }

        /// <summary>
        /// Is the networking started.
        /// </summary>
        protected volatile bool MIsRunning;

        private readonly ConcurrentDictionary<Remote, Connection> _remoteToConnection = new ConcurrentDictionary<Remote, Connection>();
        private readonly Dictionary<Guid, Action<Message>> _messageHandlers = new Dictionary<Guid, Action<Message>>();
        private readonly Dictionary<Guid, Queue<Message>> _bufferedMessages = new Dictionary<Guid, Queue<Message>>();

        /// <summary>
        /// The version of the networking protocol.
        /// </summary>
        public Version ProtocolVersion { get; } = new Version(0, 1, 1, 0);

        /// <summary>
        /// The networking channels which are supported by this network implementation.
        /// </summary>
        public ChannelType[] SupportedChannels { get; } =
        {
            ChannelType.ReliableOrdered,
            ChannelType.UnreliableUnordered,
        };

        /// <summary>
        /// The ID of this instance, used to identify this remote on the network for its entire life span.
        /// </summary>
        public Guid ID { get; } = Guid.NewGuid();

        /// <summary>
        /// Is the networking started.
        /// </summary>
        public bool IsRunning => MIsRunning;

        /// <summary>
        /// The number of connected remotes.
        /// </summary>
        public int RemoteCount => _remoteToConnection.Count;

        /// <summary>
        /// Gets a new list containing all the remotes that this instance can send messages
        /// to or receive from.
        /// </summary>
        public List<Remote> Remotes
        {
            get
            {
                var result = new List<Remote>(_remoteToConnection.Count);

                foreach (var remoteConnection in _remoteToConnection)
                    result.Add(remoteConnection.Key);

                return result;
            }
        }

        /// <summary>
        /// Invoked when the networking is successfully started.
        /// </summary>
        public event Action Started = delegate {};

        /// <summary>
        /// Invoked after the networking has been shut down.
        /// </summary>
        public event Action Stopped = delegate {};

        /// <summary>
        /// Invoked when a connection to a remote is established.
        /// </summary>
        public event Action<Remote> RemoteConnected = delegate {};

        /// <summary>
        /// Invoked when a remote has disconnected.
        /// </summary>
        /// <remarks>
        /// In case of a non-graceful disconnect, the networked instances will attempt to reconnect automatically.
        /// </remarks>
        public event Action<Remote, DisconnectStatus> RemoteDisconnected = delegate {};

        /// <summary>
        /// Creates a new <see cref="NetworkBase"/> instance.
        /// </summary>
        protected NetworkBase(IUnityExecutor unityExecutor)
        {
            _unityExecutor = unityExecutor ?? throw new ArgumentNullException(nameof(unityExecutor));
            // when using sockets, we need to be very careful to close them before trying to unload the domain
            Application.quitting += () => Stop(false);
#if UNITY_EDITOR
            EditorApplication.quitting += () => Stop(false);
            AssemblyReloadEvents.beforeAssemblyReload += () => Stop(false);
#endif
        }

        /// <summary>
        /// Checks if a remote is connected to this network instance.
        /// </summary>
        /// <param name="remote">The remote to check if connected.</param>
        /// <returns>True if the remote is connected; false otherwise.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="remote"/> is null.</exception>
        public bool IsConnected(Remote remote)
        {
            if (remote == null)
                throw new ArgumentNullException(nameof(remote));

            return _remoteToConnection.ContainsKey(remote);
        }

        /// <summary>
        /// Attempts to register a handler for the messages received from the provided remote.
        /// Only one handler can be registered for each remote at a time. The message handler for a
        /// remote is automatically deregistered when the remote disconnects, or when the networking
        /// is stopped. <see cref="Remote.All"/> is not valid here, each remote must have a handler
        /// registered explicitly.
        /// </summary>
        /// <param name="remote">The remote to receive messages from.</param>
        /// <param name="messageHandler">The handler for the received messages. It is responsible for
        /// disposing the received messages.</param>
        /// <param name="handleBufferedMessages">Will messages from the remote which have not been read yet
        /// immediately be passed to the new handler. If the messages are not handled, they are disposed of
        /// without being read.</param>
        /// <returns>True if the handler was successfully registered.</returns>
        public bool RegisterMessageHandler(Remote remote, Action<Message> messageHandler, bool handleBufferedMessages = true)
        {
            if (remote == null)
                throw new ArgumentNullException(nameof(remote));
            if (remote == Remote.All)
                throw new ArgumentException($"{nameof(Remote)}.{nameof(Remote.All)} cannot be used, message handlers must be registered per remote.", nameof(remote));
            if (!_remoteToConnection.ContainsKey(remote))
                throw new ArgumentException($"Remote {remote} is currently not connected to this instance.", nameof(remote));
            if (messageHandler == null)
                throw new ArgumentNullException(nameof(messageHandler));

            try
            {
                if (_messageHandlers.TryGetValue(remote.ID, out var currentHandler))
                    return currentHandler == messageHandler;

                _messageHandlers.Add(remote.ID, messageHandler);

                // if we are not handling buffered messages, they must be disposed and discarded
                if (_bufferedMessages.TryGetValue(remote.ID, out var messages))
                {
                    foreach (var message in messages)
                    {
                        if (handleBufferedMessages)
                        {
                            try
                            {
                                messageHandler.Invoke(message);
                            }
                            catch (Exception e)
                            {
                                Debug.LogException(e);
                            }
                        }
                        else
                        {
                            message.Dispose();
                        }
                    }

                    messages.Clear();
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to register message handler: {e}");
                return false;
            }
        }

        /// <summary>
        /// Attempts to deregister the handler for messages received from the provided remote.
        /// <see cref="Remote.All"/> is not valid here, each remote must be deregistered explicitly.
        /// </summary>
        /// <param name="remote">The remote to stop receiving messages from.</param>
        /// <returns>True if the provided handler was successfully deregistered.</returns>
        public bool DeregisterMessageHandler(Remote remote)
        {
            if (remote == null)
                throw new ArgumentNullException(nameof(remote));
            if (remote == Remote.All)
                throw new ArgumentException($"{nameof(Remote)}.{nameof(Remote.All)} cannot be used, message handlers must be deregistered per remote.", nameof(remote));
            if (!_remoteToConnection.ContainsKey(remote))
                throw new ArgumentException($"Remote {remote} is currently not connected to this instance.", nameof(remote));

            try
            {
                return !_messageHandlers.ContainsKey(remote.ID) || _messageHandlers.Remove(remote.ID);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to deregister message handler: {e}");
                return false;
            }
        }

        /// <summary>
        /// Sends a message over the network. This is thread safe. Messages are only guaranteed
        /// to be sent in order for messages sent from the same thread.
        /// </summary>
        /// <param name="message">The message to send. The caller is not responsible for disposing the
        /// message, and should immediately remove any references to the message to ensure full transfer
        /// of ownership, as the message will be pooled and reused after it has been sent. To efficiently
        /// send a message to all remotes, specify <see cref="Remote.All"/> as the remote when creating a
        /// message.</param>
        /// <returns>True if the message was successfully sent.</returns>
        public bool SendMessage(Message message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            try
            {
                var packet = new Packet(message, Packet.Type.Generic);
                if (RemoteFactoryStaticProvider.RemoteFactory.TryGetCreated(message.RemoteId, out var remote))
                {
                    if (remote == Remote.All)
                    {
                        foreach (var remoteConnection in _remoteToConnection)
                            remoteConnection.Value.Send(packet, false);
                    }
                    else
                    {
                        if (!_remoteToConnection.TryGetValue(remote, out var connection))
                            throw new Exception($"There is currently no connection to remote {remote}.");

                        connection.Send(packet, false);
                    }
                    return true;
                }
                else
                {
                    throw new Exception("Message addressed to unknown remote!");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to send message: {e}");
                return false;
            }
        }

        /// <summary>
        /// Called to invoke the events and callbacks.
        /// </summary>
        private void HandleConnectionEvent(ConnectionEvent e)
        {
            switch (e.EventType)
            {
                case ConnectionEvent.Type.NewConnection:
                {
                    var remote = e.Connection.Remote;

                    if (_remoteToConnection.TryGetValue(remote, out var oldConnection))
                        oldConnection.Close(DisconnectStatus.Reconnected);

                    _remoteToConnection.TryAdd(remote, e.Connection);

                    try
                    {
                        RemoteConnected?.Invoke(remote);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                    break;
                }
                case ConnectionEvent.Type.TerminateConnection:
                {
                    e.Connection.Close(DisconnectStatus.Error);
                    break;
                }
            }
        }

        /// <summary>
        /// Stop running the networking, closing any connections. All message handlers will be
        /// deregistered and buffered messages from remotes with no registered handler will be discarded.
        /// </summary>
        /// <param name="graceful">Finish send/receiving buffered messages and disconnect gracefully.
        /// This may block for many seconds in the worst case.</param>
        public virtual void Stop(bool graceful = true)
        {
            if (!MIsRunning)
                return;

            MIsRunning = false;

            DisconnectInternal(Remote.All, graceful);

            // ensure all the collections are reset
            _remoteToConnection.Clear();
            _bufferedMessages.Clear();
            _messageHandlers.Clear();

            try
            {
                Stopped?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            // truncate the GUID to keep it a readable length
            return $"{{{GetType().Name}, ID: {ID.ToString("N").Substring(0, 8)}}}";
        }

        /// <summary>
        /// Closes the connection to a given remote.
        /// </summary>
        /// <param name="remote">The remote to close the connection for.</param>
        /// <param name="graceful">Wait for the remote to acknowledge the disconnection.
        /// This may block for many seconds in the worst case.</param>
        internal void DisconnectInternal(Remote remote, bool graceful)
        {
            Debug.Log("DisconnectInternal");
            if (remote == Remote.All)
            {
                foreach (var remoteConnection in _remoteToConnection)
                    Disconnect(remoteConnection.Value, graceful);
            }
            else if (_remoteToConnection.TryGetValue(remote, out var connection))
            {
                Disconnect(connection, graceful);
            }
        }

        void Disconnect(Connection connection, bool graceful)
        {
            Debug.Log("Disconnect");
            try
            {
                // If disconnecting gracefully we need to send the disconnect message synchronously,
                // so we can guarantee it has been successfully received before we close the connection
                // to the remote.
                if (graceful)
                {
                    var message = Message.Get(connection.Remote.ID, ChannelType.ReliableOrdered);
                    var packet = new Packet(message, Packet.Type.Disconnect);

                    connection.Send(packet, true);
                }
                connection.Close(DisconnectStatus.Graceful);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to disconnect remote: {e}");
            }
        }

        /// <summary>
        /// Executes the registered message handler for a message.
        /// </summary>
        /// <param name="message">The message to receive.</param>
        internal void HandleMessage(Message message)
        {
            var remoteId = message.RemoteId;

            // Check if there is message handler which receives the messages
            // from this remote. If there is none, we should buffer them
            // until there is a handler.
            if (_messageHandlers.TryGetValue(remoteId, out var handler))
            {
                _unityExecutor.ExecuteOnFixedUpdate(() =>
                {
                    try
                    {
                        handler?.Invoke(message);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                });
            }
            else
            {
                if (!_bufferedMessages.TryGetValue(remoteId, out var messages))
                {
                    messages = new Queue<Message>();
                    _bufferedMessages.Add(remoteId, messages);
                }
                messages.Enqueue(message);
            }
        }

        /// <summary>
        /// Adds a new connection. This is thread-safe.
        /// </summary>
        /// <param name="connection">The new connection.</param>
        internal void RegisterConnection(Connection connection)
        {
            Debug.Log("RegisterConnection");
            // register the connection on the next update so we can invoke the remote connected event
            // on the correct thread and ensure there are no ordering issues compared to terminated
            // connections.
            HandleConnectionEvent(new ConnectionEvent(connection, ConnectionEvent.Type.NewConnection));
        }

        /// <summary>
        /// Removes a connection.
        /// </summary>
        /// <param name="connection">The connection to remove.</param>
        /// <param name="status">How the connection was terminated.</param>
        internal void DeregisterConnection(Connection connection, DisconnectStatus status)
        {
            Debug.Log("DeregisterConnection");
            var remote = connection.Remote;

            _remoteToConnection.TryRemove(remote, out _);

            if (_bufferedMessages.TryGetValue(remote.ID, out var bufferedMessages))
            {
                foreach (var message in bufferedMessages)
                    message.Dispose();

                bufferedMessages.Clear();
            }
            _bufferedMessages.Remove(remote.ID);
            _messageHandlers.Remove(remote.ID);

            try
            {
                RemoteDisconnected?.Invoke(remote, status);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Called to handle closing a connection that has encountered a fatal error.
        /// This is thread safe.
        /// </summary>
        /// <param name="connection">The connection to close.</param>
        internal void OnSocketError(Connection connection)
        {
            Debug.Log("SocketError");
            // We can't close connections on other threads, so we close the connection
            // on the next update.
            HandleConnectionEvent(new ConnectionEvent(connection, ConnectionEvent.Type.TerminateConnection));
        }

        /// <summary>
        /// Notify listeners that the networking is now running.
        /// </summary>
        protected void RaiseStartedEvent()
        {
            try
            {
                Started?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Sends the data used to initialize a connection to a remote on sockets
        /// that have just connected. This is thread-safe.
        /// </summary>
        /// <param name="tcp">A TCP socket that has just finished connecting.</param>
        /// <param name="udp">The UDP socket to use for this connection.</param>
        protected void DoHandshake(INetworkSocket tcp, INetworkSocket udp)
        {
            Debug.Log("DoHandshake");
            var remote = RemoteFactoryStaticProvider.RemoteFactory.Create(Guid.Empty, tcp.RemoteEndPoint, null);
            var message = Message.Get(remote.ID, ChannelType.ReliableOrdered);
            var packet = new Packet(message, Packet.Type.Initialization);

            message.Data.WriteStruct(new VersionData(ProtocolVersion));
            message.Data.WriteStruct(new RemoteData(ID, tcp.LocalEndPoint, udp.LocalEndPoint));

            tcp.Send(packet, false);
        }
    }
}
