using System;
using System.Net;
using System.Net.Sockets;

namespace Unity.LiveCapture.Networking
{
    internal interface INetworkSocket
    {
        event Action<Packet> PacketReceived;
        event Action SocketError;
        
        public IPEndPoint RemoteEndPoint { get; }
        public IPEndPoint LocalEndPoint { get; }
        Socket Socket { get; }

        /// <summary>
        /// Has the socket been closed.
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Closes the socket and clears all received packed.
        /// </summary>
        void Dispose();

        void Send(Packet packet, bool synchronous);
    }
}