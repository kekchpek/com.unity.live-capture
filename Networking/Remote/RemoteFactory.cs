using System;
using System.Collections.Generic;
using System.Net;
using UnityEngine;

namespace Unity.LiveCapture.Networking
{
    internal class RemoteFactory : IRemoteFactory
    {
        private readonly Dictionary<Guid, Remote> _createdRemotes = new Dictionary<Guid, Remote>();

        public RemoteFactory(params Remote[] staticCreatedRemotes)
        {
            foreach (var createdRemote in staticCreatedRemotes)
            {
                _createdRemotes.Add(createdRemote.ID, createdRemote);
            }
        }
        
        public bool TryGetCreated(Guid id, out Remote remote)
        {
            return _createdRemotes.TryGetValue(id, out remote);
        }

        public Remote Create(Guid id, IPEndPoint tcpEndPoint, IPEndPoint udpEndPoint)
        {
            if (!TryGetCreated(id, out var createdRemote))
            {
                var remote = new Remote(id, tcpEndPoint, udpEndPoint);
                _createdRemotes.Add(id, remote);
                return remote;
            }
            else
            {
                Debug.Assert(Equals(createdRemote.TcpEndPoint.Address, tcpEndPoint.Address));
                Debug.Assert(Equals(createdRemote.UdpEndPoint.Address, udpEndPoint.Address));
                return createdRemote;
            }
        }
    }
}