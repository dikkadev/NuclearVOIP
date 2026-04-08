using BepInEx;

#if BEP6
using BepInEx.Unity.Mono;
#endif

namespace AtomicFramework
{
    public abstract class Mod : BaseUnityPlugin
    {
        public struct Options
        {
            public enum Runtime
            {
                NONE,
                TOGGLEABLE,
                RELOADABLE
            }

            public enum Multiplayer
            {
                CLIENT_ONLY,
                SERVER_ONLY,
                REQUIRES_HOST,
                REQUIRES_ALL
            }

            public string repository;
            public Multiplayer multiplayerOptions;
            public Runtime runtimeOptions;

            public Options()
            {
                repository = string.Empty;
                multiplayerOptions = Multiplayer.REQUIRES_HOST;
                runtimeOptions = Runtime.NONE;
            }
        }

        protected internal NetworkAPI? Networking;

        internal readonly Options options;

        protected Mod(Options options)
        {
            this.options = options;

            NetworkingManager.OnNetworkingAvail += _ => OnNetworking();
            if (NetworkingManager.instance != null)
                OnNetworking();
        }

        internal void OnNetworking()
        {
            Networking ??= new(Info.Metadata.GUID);
        }
    }
}
