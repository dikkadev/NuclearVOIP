using BepInEx;
using HarmonyLib;
using Mirage;
using Mirage.SteamworksSocket;
using NuclearOption.Networking;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace AtomicFramework
{
    public static class LoadingManager
    {
        public class Cancelable
        {
            private bool canceled;

            public bool Canceled
            {
                get => canceled;
                set => canceled = canceled || value;
            }
        }

        private static readonly List<INetworkPlayer> pending = [];
        private static readonly Harmony harmony = new("xyz.tyknet.NuclearOption");

        public static event Action? GameLoaded;
        public static event Action? NetworkReady;
        public static event Action? ServerStarting;
        public static event Action? MissionLoaded;
        public static event Action? MissionUnloaded;
        public static event Action<ulong>? PlayerJoined;
        public static event Action<ulong>? PlayerLeft;
        public static event Action<ulong, Cancelable>? PrePlayerAuthenticating;
        public static event Action<ulong, Cancelable>? PlayerAuthenticating;

        static LoadingManager()
        {
            Plugin.Logger.LogDebug("Patching SteamManager");
            Type steamManager = typeof(SteamManager);
            harmony.Patch(
                steamManager.GetMethod("MarkInit", BindingFlags.Instance | BindingFlags.NonPublic),
                null,
                HookMethod(NetworkManagerPostfix)
            );

            Plugin.Logger.LogDebug("Patching NetworkManager");
            Type netManager = typeof(NetworkManagerNuclearOption);
            harmony.Patch(
                netManager.GetMethod("OnServerAuthenticated", BindingFlags.NonPublic | BindingFlags.Instance),
                HookMethod(ClientAuthenticatingCallback)
            );

            harmony.Patch(
                netManager.GetMethod("OnServerDisconnect", BindingFlags.NonPublic | BindingFlags.Instance),
                postfix: HookMethod(ServerDisconnectCallback)
            );

            Plugin.Logger.LogDebug("Patching MainMenu");
            Type mainMenu = typeof(MainMenu);
            harmony.Patch(
                mainMenu.GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance),
                postfix: HookMethod(MainMenuPostfix)
            );

            Plugin.Logger.LogDebug("Patching Server");
            Type server = typeof(Server);
            harmony.Patch(
                server.GetConstructors()[0],
                postfix: HookMethod(KillCallback)
            );

            Plugin.Logger.LogDebug("Patching Client");
            Type client = typeof(Client);
            harmony.Patch(
                client.GetMethod("Connect"),
                postfix: HookMethod(KillCallback)
            );
        }

        private static HarmonyMethod HookMethod(Delegate hook)
        {
            return new HarmonyMethod(hook.GetMethodInfo());
        }

        private static void MainMenuPostfix()
        {
            Plugin.Logger.LogDebug("Reached GameLoaded");
            GameLoaded?.Invoke();

            MethodBase original = harmony.GetPatchedMethods().Where(a => a.DeclaringType == typeof(MainMenu)).First();
            harmony.Unpatch(original, HookMethod(MainMenuPostfix).method);
        }

        private static void NetworkManagerPostfix(NetworkManagerNuclearOption __instance)
        {
            NetworkManagerNuclearOption.i.Client.Connected.AddListener(ClientConnectCallback);
            NetworkManagerNuclearOption.i.Client.Disconnected.AddListener(ClientDisconectCallback);
            NetworkManagerNuclearOption.i.Server.Started.AddListener(() => ServerStarting?.Invoke());

            Plugin.Logger.LogDebug("Reached NetworkReady");
            NetworkReady?.Invoke();
        }

        private static void MissionLoadCallback()
        {
            Plugin.Logger.LogDebug("Reached MissionLoaded");
            MissionLoaded?.Invoke();
        }

        private static void OnIdentity(NetworkIdentity identity)
        {
            identity.OnStartLocalPlayer.AddListener(MissionLoadCallback);
        }

        private static void ClientConnectCallback(INetworkPlayer player)
        {
            if (MissionManager.CurrentMission != null)
                player.OnIdentityChanged += OnIdentity;
        }

        private static void ClientDisconectCallback(ClientStoppedReason reason)
        {
            Plugin.Logger.LogDebug("Reached MissionUnloaded");
            MissionUnloaded?.Invoke();
        }

        private static bool ClientAuthenticatingCallback(INetworkPlayer player)
        {
            if (player.IsHost)
                return true;

            if (pending.Contains(player))
            {
                pending.Remove(player);
                return true;
            }

            Plugin.Logger.LogDebug("ClientAutheticatingCallback");
            if (player.ConnectionHandle is not SteamConnection conn)
            {
                Plugin.Logger.LogWarning("Non-Steam player detected. Cannot validate.");
                return true;
            }

            if (PrePlayerAuthenticating?.InvokeCancelable(conn.SteamID.m_SteamID) == false)
            {
                player.Disconnect();
                NetworkingManager.instance!.Kill(conn.SteamID.m_SteamID, NetworkingManager.KillReason.PREDISCOVERY);
                return false;
            }

            NetworkingManager.instance!.discovery.ConnectTo(conn.SteamID.m_SteamID);

            int checkpoint = 0;

            void Subscriber(ulong iplayer)
            {
                Plugin.Logger.LogDebug("Subscriber");
                if (iplayer == conn.SteamID.m_SteamID)
                {
                    NetworkingManager.instance!.discovery.ModsAvailable -= Subscriber;

                    PluginInfo[] enabled = [.. Plugin.Instance.PluginsEnabled()
                        .Where(plugin => plugin.Instance is Mod mod
                        && mod.options.multiplayerOptions == Mod.Options.Multiplayer.REQUIRES_ALL)];

                    string[] mods = NetworkingManager.instance!.discovery.GetMods(iplayer);

                    Plugin.Logger.LogDebug($"Got [{string.Join(", ", mods)}]   need   [{string.Join(", ", enabled.Select(a => a.Metadata.GUID))}]");

                    foreach (PluginInfo plugin in enabled)
                    {
                        if (mods.Contains(plugin.Metadata.GUID))
                            continue;

                        if (plugin.Instance is Mod mod)
                        {
                            if (mod.options.runtimeOptions != Mod.Options.Runtime.NONE)
                            {
                                mod.enabled = false;
                                continue;
                            }
                        }
                        else
                            continue;

                        NetworkingManager.instance.Kill(conn.SteamID.m_SteamID, NetworkingManager.KillReason.DISCOVERY);
                        Thread.Sleep(100);
                        player.Disconnect();

                        Plugin.Logger.LogDebug("Discovery.Subscriber.Kill");

                        return;
                    }

                    Plugin.Logger.LogDebug("Discovery.Subscriber.Passed");

                    if (Interlocked.Exchange(ref checkpoint, int.MaxValue) == int.MaxValue)
                    {
                        ContinueAuthentication(player);
                    }
                }
            }

            NetworkingManager.instance!.discovery.ConnectionResolved += resolve_for =>
            {
                if (resolve_for != conn.SteamID.m_SteamID)
                    return;

                NetworkingManager.instance!.discovery.ModsAvailable += Subscriber;

                NetworkingManager.instance!.discovery.GetRequired(conn.SteamID.m_SteamID, required =>
                {
                    Plugin.Logger.LogDebug("GetRequired");
                    PluginInfo[] loaded = [.. Plugin.PluginsLoaded()];
                    PluginInfo[] loadedAndRequired = [.. loaded.Where(plugin => required.Contains(plugin.Metadata.GUID))];

                    if (loadedAndRequired.Length == required.Length)
                    {
                        foreach (PluginInfo info in loadedAndRequired)
                        {
                            if (((MonoBehaviour)info.Instance).enabled)
                                continue;

                            if (info.Instance is Mod mod)
                            {
                                if (mod.options.runtimeOptions != Mod.Options.Runtime.NONE)
                                {
                                    mod.enabled = true;
                                    continue;
                                }
                            }

                            NetworkingManager.instance.Kill(conn.SteamID.m_SteamID, NetworkingManager.KillReason.DISCOVERY);
                            Thread.Sleep(100);
                            player.Disconnect();

                            Plugin.Logger.LogDebug("Plugin.Required.Disabled.Kill");

                            return;
                        }

                        Plugin.Logger.LogDebug("Plugin.Required.Passed");

                        if (Interlocked.Exchange(ref checkpoint, int.MaxValue) == int.MaxValue)
                        {
                            ContinueAuthentication(player);
                        }
                    }
                    else
                    {
                        Plugin.Logger.LogDebug("Discovery.Required.Kill");

                        NetworkingManager.instance.Kill(conn.SteamID.m_SteamID, NetworkingManager.KillReason.DISCOVERY);
                        Thread.Sleep(100);
                        player.Disconnect();
                    }
                });
            };

            return false;
        }

        private static void ServerDisconnectCallback(INetworkPlayer networkPlayer)
        {
            ulong id = (networkPlayer.ConnectionHandle as SteamConnection)?.SteamID.m_SteamID ?? 0;
            PlayerLeft?.Invoke(id);
        }

        private static void ContinueAuthentication(INetworkPlayer player)
        {
            Plugin.Logger.LogDebug("ContinueAuthetication");
            ulong id = (player.ConnectionHandle as SteamConnection)?.SteamID.m_SteamID ?? 0;

            if (PlayerAuthenticating?.InvokeCancelable(id) == false)
            {
                NetworkingManager.instance!.Kill(id, NetworkingManager.KillReason.POSTDISCOVERY);
                Thread.Sleep(100);
                player.Disconnect();
            }
            else
            {
                PlayerJoined?.Invoke(id);

                MethodInfo continuation = typeof(NetworkManagerNuclearOption).GetMethod("OnServerAuthenticated", BindingFlags.NonPublic | BindingFlags.Instance);

                pending.Add(player);

                continuation.Invoke(NetworkManagerNuclearOption.i, [player]);
            }
        }

        private static void KillCallback(Callback<SteamNetConnectionStatusChangedCallback_t> ___c_onConnectionChange)
        {
            ___c_onConnectionChange.Unregister();
        }

        internal static bool InvokeCancelable<T>(this Action<T, Cancelable> action, T param)
        {
            Cancelable cancelable = new();

#pragma warning disable IDE0220
            foreach (Action<T, Cancelable> del in action.GetInvocationList())
            {
                del.Invoke(param, cancelable);

                if (cancelable.Canceled)
                    return false;
            }
#pragma warning restore IDE0220

            return true;
        }
    }
}
