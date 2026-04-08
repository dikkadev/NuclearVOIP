using BepInEx;
using BepInEx.Logging;
using System.Linq;
using UnityEngine;

#if BEP5
using BepInEx.Bootstrap;
#elif BEP6
using BepInEx.Unity.Mono.Bootstrap;
#endif

namespace AtomicFramework
{
    // Minimal compatibility shim so vendored AtomicFramework code can compile
    // into NuclearVOIP without shipping a separate AtomicFramework plugin DLL.
    internal sealed class Plugin
    {
        private static readonly Plugin instance = new();

        internal static Plugin Instance => instance;

        internal static ManualLogSource Logger => NuclearVOIP.Plugin.Logger;

        internal PluginInfo[] PluginsEnabled()
        {
            return [.. PluginsLoaded().Where(info => info.Instance is MonoBehaviour behaviour && behaviour.enabled)];
        }

        internal static PluginInfo[] PluginsLoaded()
        {
#if BEP5
            return [.. Chainloader.PluginInfos.Values];
#elif BEP6
            return [.. UnityChainloader.Instance.Plugins.Values];
#else
#error Undefined bepinex version
#endif
        }
    }
}
