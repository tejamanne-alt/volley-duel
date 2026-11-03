using System;
using Duel.Core.Net;

namespace Duel.Core.Transport.WiFiAware
{
    /// <summary>
    /// Optional third transport, off by default. Wi-Fi Aware needs Apple's managed
    /// <c>com.apple.developer.wifi-aware</c> entitlement on iOS 26 (iPhone 12 and later) with PIN pairing
    /// through DeviceDiscoveryUI, plus WifiAwareManager publish/subscribe with the same DNS-SD service name
    /// on Android. Per the contract the entitlement is not requested without asking, so this class only
    /// reserves the slot behind <see cref="ITransport"/>.
    /// The spike gate: an iPhone and an Android phone discover, pair and hold a 60 Hz echo for 10 minutes
    /// with lower RTT and jitter than BLE. Ship it only if it passes.
    /// </summary>
    public sealed class WiFiAwareTransport : ITransport
    {
        /// <summary>True only in builds that define DUEL_WIFI_AWARE (after the entitlement is granted).</summary>
#if DUEL_WIFI_AWARE
        public const bool Enabled = true;
#else
        public const bool Enabled = false;
#endif

        public const string Reason = "Wi-Fi Aware is off: it needs the com.apple.developer.wifi-aware entitlement and the P0 spike.";

        TransportState state = TransportState.Idle;
        TransportState reported = TransportState.Idle;

        public string Name => "Wi-Fi Aware";

        public TransportState State => state;

        public TransportStats Stats => default;

        public bool CanHost => false;

        public string LastError { get; private set; } = string.Empty;

        public int MaxPayload(TransportChannel channel) => 0;

        public event ReceiveHandler Received
        {
            add { }
            remove { }
        }

        public event Action<TransportState> StateChanged;

        public event Action<PeerInfo> PeerFound
        {
            add { }
            remove { }
        }

        public void Host(string courtCode, string displayName) => Fail();

        public void StartScan() => Fail();

        public void StopScan()
        {
        }

        public void Join(PeerInfo peer) => Fail();

        public void Send(ReadOnlySpan<byte> data, TransportChannel channel)
        {
        }

        public void Poll()
        {
            if (state == reported) return;
            reported = state;
            StateChanged?.Invoke(state);
        }

        public void Disconnect() => state = TransportState.Disconnected;

        public void Dispose() => Disconnect();

        void Fail()
        {
            LastError = Reason;
            state = TransportState.Failed;
        }
    }
}
