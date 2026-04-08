#if DEBUG
using AtomicFramework.Networking.Debug;
#endif

using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AtomicFramework
{
    public class NetworkChannel
    {
        internal readonly HSteamListenSocket socket;

        private readonly Dictionary<ulong, HSteamNetConnection> connections = [];

        public event Action<NetworkMessage>? OnMessage;
        public event Action<ulong>? OnConnected;
        public event Action<ulong>? OnDisconnect;
        public event Action<ulong, bool>? OnConnectionFailed;

        public delegate bool ConnectionFilter(ulong player);

        public ConnectionFilter? OnConnection;

        private readonly string GUID;

        public readonly ushort channel;

        internal NetworkChannel(HSteamListenSocket socket, string GUID, ushort channel)
        {
#if DEBUG
            TextDebug.WriteChannelStatus(GUID, channel, false);
#endif

            this.socket = socket;
            this.GUID = GUID;
            this.channel = channel;
        }

        public void Send(ulong player, byte[] message, bool fast = false)
        {
            if (connections.TryGetValue(player, out HSteamNetConnection conn))
            {
#if DEBUG
                TextDebug.WritePacket(GUID, channel, new(message, player), true);
#endif

                unsafe
                {
                    fixed (byte* buf = message)
                    {
                        SteamNetworkingSockets.SendMessageToConnection(conn, (IntPtr)buf, (uint)message.Length, fast ? 8 : 5, out _);
                    }
                }
            }
            else
                throw new IOException();
        }

        public void Connect(ulong address)
        {
            if (connections.ContainsKey(address))
                return;

#if DEBUG
            TextDebug.WriteConnecting(GUID, channel, address, false);
#endif

            void OnPort(ushort port)
            {
                SteamNetworkingIdentity identity = new();
                identity.SetSteamID64(address);

                HSteamNetConnection conn = SteamNetworkingSockets.ConnectP2P(ref identity, port, 0, []);
                NetworkingManager.instance!.connections[conn] = this;

                connections[address] = conn;
            }

            NetworkingManager.instance!.discovery.GetPort(address, GUID, channel, OnPort);
        }

        public void Disconnect(ulong player)
        {
            if (connections.TryGetValue(player, out HSteamNetConnection conn))
            {
                SteamNetworkingSockets.CloseConnection(conn, 1002, "Connection closed", true);
                connections.Remove(player);
            }
        }

        public NetworkStatistics GetStatistics(ulong player)
        {
            if (!connections.TryGetValue(player, out HSteamNetConnection conn))
                return default;

            SteamNetConnectionRealTimeStatus_t status = default;
            SteamNetConnectionRealTimeLaneStatus_t drop = default;
            SteamNetworkingSockets.GetConnectionRealTimeStatus(conn, ref status, 0, ref drop);

            return new(status);
        }

        internal void ReceiveMessage(NetworkMessage message)
        {
#if DEBUG
            TextDebug.WritePacket(GUID, channel, message, false);
#endif

            OnMessage?.Invoke(message);
        }

        internal bool ReceiveConnection(ulong player, HSteamNetConnection conn)
        {
            if (OnConnection?.Invoke(player) == true)
            {
#if DEBUG
                TextDebug.WriteConnectStatus(GUID, channel, player, false);
#endif

                connections[player] = conn;
                return true;
            }
            else
                return false;
        }

        internal void NotifyConnected(ulong player, HSteamNetConnection conn)
        {
#if DEBUG
            TextDebug.WriteConnectStatus(GUID, channel, player, false);
#endif

            OnConnected?.Invoke(player);
            connections[player] = conn;
        }

        internal void ReceiveDisconnect(ulong player)
        {
#if DEBUG
            TextDebug.WriteConnectStatus(GUID, channel, player, true);
#endif

            OnDisconnect?.Invoke(player);
            connections.Remove(player);
        }

        internal void ReceiveFailed(ulong player, bool refused)
        {
#if DEBUG
            TextDebug.WriteConnecting(GUID, channel, player, true);
#endif

            OnConnectionFailed?.Invoke(player, refused);
        }

        internal void Close()
        {
#if DEBUG
            TextDebug.WriteChannelStatus(GUID, channel, true);
#endif

            SteamNetworkingSockets.CloseListenSocket(socket);
            NetworkingManager.instance!.NotifyClosed(socket);

            foreach (HSteamNetConnection conn in connections.Values.ToArray())
                SteamNetworkingSockets.CloseConnection(conn, 1000, "Channel closed", true);
        }
    }
}
