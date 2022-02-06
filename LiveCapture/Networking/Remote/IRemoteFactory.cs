using System;
using System.Net;

namespace Unity.LiveCapture.Networking
{
    internal interface IRemoteFactory
    {
        bool TryGetCreated(Guid id, out Remote remote);
        Remote Create(Guid id, IPEndPoint tcpEndPoint, IPEndPoint udpEndPoint);
    }
}