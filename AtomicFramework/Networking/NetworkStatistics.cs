using Steamworks;
using System;

namespace AtomicFramework
{
    public readonly struct NetworkStatistics(SteamNetConnectionRealTimeStatus_t status)
    {
        public readonly int ping = status.m_nPing;
        public readonly float packetLoss = status.m_flConnectionQualityRemote == -1 ? 0 : Math.Clamp(
            1 - status.m_flConnectionQualityRemote,
            0,
            1);
        public readonly int bandwidth = status.m_nSendRateBytesPerSecond;
        public readonly int inFlight = status.m_cbPendingUnreliable + status.m_cbPendingReliable + status.m_cbSentUnackedReliable;
    }
}
