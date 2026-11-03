using System;

namespace Duel.Core.Net
{
    /// <summary>Input packets ride an unreliable, low-latency channel; control messages a reliable, ordered one.</summary>
    public enum TransportChannel : byte
    {
        /// <summary>BLE: INPUT characteristic (write without response / notify). LAN: plain datagrams.</summary>
        Input = 0,
        /// <summary>BLE: CONTROL characteristic (write with response / indicate). LAN: acked and retransmitted.</summary>
        Control = 1,
    }

    public enum TransportState : byte
    {
        Idle,
        Advertising,
        Scanning,
        Connecting,
        Connected,
        Disconnected,
        Failed,
    }

    /// <summary>Link measurements a transport can provide (zero when unknown).</summary>
    public struct TransportStats
    {
        public int RttMicros;
        public int JitterMicros;
        public int LossPermille;
        public long BytesSent;
        public long BytesReceived;
        public int PacketsSent;
        public int PacketsReceived;
        /// <summary>BLE: negotiated ATT MTU; LAN: datagram budget.</summary>
        public int Mtu;
        /// <summary>BLE: connection interval in microseconds when the platform reports it.</summary>
        public int ConnectionIntervalMicros;
    }

    /// <summary>A nearby court found while scanning.</summary>
    public struct PeerInfo
    {
        /// <summary>Transport-specific address (BLE identifier, "ip:port" on LAN).</summary>
        public string Id;
        public string DisplayName;
        /// <summary>The host's 4-character court code.</summary>
        public string CourtCode;
        /// <summary>Signal strength in dBm when known (0 otherwise).</summary>
        public int SignalDbm;
    }

    public delegate void ReceiveHandler(ReadOnlySpan<byte> data, TransportChannel channel);

    /// <summary>
    /// A peer-to-peer link between two phones. No server is involved: BLE first, LAN UDP second, Wi-Fi Aware
    /// as an optional third, all behind this interface. Callbacks fire only from <see cref="Poll"/>, on the
    /// caller's thread, so game code never sees platform threads.
    /// </summary>
    public interface ITransport : IDisposable
    {
        string Name { get; }

        TransportState State { get; }

        TransportStats Stats { get; }

        /// <summary>False on phones that cannot advertise (some Android devices): they can only join.</summary>
        bool CanHost { get; }

        /// <summary>Largest payload <see cref="Send"/> accepts on the given channel.</summary>
        int MaxPayload(TransportChannel channel);

        /// <summary>Advertise a court with a 4-character code and wait for a guest.</summary>
        void Host(string courtCode, string displayName);

        void StartScan();

        void StopScan();

        void Join(PeerInfo peer);

        void Send(ReadOnlySpan<byte> data, TransportChannel channel);

        /// <summary>Pump: delivers queued packets and state changes through the events below.</summary>
        void Poll();

        void Disconnect();

        event ReceiveHandler Received;

        event Action<TransportState> StateChanged;

        event Action<PeerInfo> PeerFound;
    }
}
