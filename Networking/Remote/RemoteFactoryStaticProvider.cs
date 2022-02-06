namespace Unity.LiveCapture.Networking
{
    internal static class RemoteFactoryStaticProvider
    {
        private static IRemoteFactory _instance;
        public static IRemoteFactory RemoteFactory => _instance ??= new RemoteFactory(Remote.All);
    }
}