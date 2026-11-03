using System;
using Duel.Core.Net;

namespace Duel.Core.Transport.BLE
{
    /// <summary>
    /// The native side of BLE: CoreBluetooth on iOS (Swift, DllImport) and Android BLE (Kotlin,
    /// AndroidJavaObject). Platform callbacks arrive on radio threads and go into a lock-free queue;
    /// <see cref="TryDequeue"/> drains it on the main thread, so the transport and the game never see
    /// platform threads.
    /// </summary>
    public interface IBleBridge : IDisposable
    {
        /// <summary>False on Android phones without multiple-advertisement support: they can only join.</summary>
        bool CanAdvertise { get; }

        /// <summary>Host role: start the GATT server and advertise the service with the court code.</summary>
        void StartAdvertising(string localName, byte[] serviceData);

        void StopAdvertising();

        /// <summary>Guest role: scan for the service UUID.</summary>
        void StartScan();

        void StopScan();

        /// <summary>Guest role: connect, discover, subscribe to both characteristics, ask for a larger MTU and high priority.</summary>
        void Connect(string peerId);

        void Disconnect();

        /// <summary>INPUT: write without response (guest) or notify (host). Only the newest pending packet is kept under flow control.</summary>
        void SendInput(byte[] packet, int length);

        /// <summary>CONTROL: write with response (guest) or indicate (host), queued and sent in order.</summary>
        void SendControl(byte[] fragment, int length);

        bool TryDequeue(out BleEvent e);
    }

    /// <summary>
    /// <see cref="ITransport"/> over BLE (the primary transport). The host is the GATT peripheral and the
    /// guest is the central, as the spec requires. Input packets go out as single ATT payloads. Control
    /// messages are fragmented to the negotiated MTU and reassembled here, so the bridges stay thin.
    /// </summary>
    public sealed class BleTransport : ITransport
    {
        /// <summary>The default ATT MTU of 23 leaves 20 bytes per packet; bridges report more once negotiated.</summary>
        public const int DefaultAttPayload = 20;

        readonly IBleBridge bridge;
        readonly ControlFragmenter fragmenter = new ControlFragmenter();
        readonly ControlReassembler reassembler = new ControlReassembler();
        readonly byte[] inputBuffer = new byte[64];
        readonly Action<byte[]> sendFragment;
        TransportState state = TransportState.Idle;
        TransportState reported = TransportState.Idle;
        TransportStats stats;
        int controlPayload = DefaultAttPayload;
        int inputPayload = DefaultAttPayload;

        public BleTransport(IBleBridge bridge)
        {
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            sendFragment = f =>
            {
                stats.PacketsSent++;
                stats.BytesSent += f.Length;
                this.bridge.SendControl(f, f.Length);
            };
            stats.Mtu = DefaultAttPayload + 3;
        }

        public string Name => "BLE";

        public TransportState State => state;

        public TransportStats Stats => stats;

        public bool CanHost => bridge.CanAdvertise;

        /// <summary>Last adapter report: 1 on, 0 off, -1 unauthorized, -2 unsupported.</summary>
        public int AdapterState { get; private set; } = 1;

        /// <summary>Last error the bridge reported, for the lobby and the session log.</summary>
        public string LastError { get; private set; } = string.Empty;

        public int MaxPayload(TransportChannel channel) => channel == TransportChannel.Input ? inputPayload : ControlFragmenter.MaxMessageBytes;

        public event ReceiveHandler Received;
        public event Action<TransportState> StateChanged;
        public event Action<PeerInfo> PeerFound;

        public void Host(string courtCode, string displayName)
        {
            if (!bridge.CanAdvertise)
            {
                LastError = "This phone cannot host over Bluetooth; join a court instead.";
                state = TransportState.Failed;
                return;
            }
            ResetLink();
            bridge.StartAdvertising(CourtAdvert.LocalName(courtCode), CourtAdvert.ServiceData(courtCode));
            state = TransportState.Advertising;
        }

        public void StartScan()
        {
            bridge.StartScan();
            state = TransportState.Scanning;
        }

        public void StopScan()
        {
            bridge.StopScan();
            if (state == TransportState.Scanning) state = TransportState.Idle;
        }

        public void Join(PeerInfo peer)
        {
            ResetLink();
            bridge.StopScan();
            bridge.Connect(peer.Id);
            state = TransportState.Connecting;
        }

        public void Send(ReadOnlySpan<byte> data, TransportChannel channel)
        {
            if (state != TransportState.Connected) return;
            if (channel == TransportChannel.Input)
            {
                if (data.Length > inputPayload) throw new ArgumentException("input packet larger than one ATT payload");
                data.CopyTo(inputBuffer);
                stats.PacketsSent++;
                stats.BytesSent += data.Length;
                bridge.SendInput(inputBuffer, data.Length);
                return;
            }
            fragmenter.Split(data, controlPayload, sendFragment);
        }

        public void Poll()
        {
            while (bridge.TryDequeue(out BleEvent e)) Handle(e);
            if (state != reported)
            {
                reported = state;
                StateChanged?.Invoke(state);
            }
        }

        void Handle(in BleEvent e)
        {
            switch (e.Kind)
            {
                case BleEventKind.Adapter:
                    AdapterState = e.Value;
                    if (e.Value <= 0 && state != TransportState.Idle) state = TransportState.Failed;
                    break;
                case BleEventKind.PeerFound:
                    {
                        string code = CourtAdvert.Parse(e.Name, e.Data);
                        if (code == null) break;
                        PeerFound?.Invoke(new PeerInfo { Id = e.PeerId, CourtCode = code, DisplayName = code, SignalDbm = e.Value });
                        break;
                    }
                case BleEventKind.Connected:
                    SetPayloads(e.Value, e.Value2);
                    if (state == TransportState.Connecting || state == TransportState.Advertising)
                    {
                        state = TransportState.Connected;
                        // One guest per court: stop advertising once it is taken.
                        bridge.StopAdvertising();
                    }
                    break;
                case BleEventKind.Disconnected:
                    if (state == TransportState.Connected || state == TransportState.Connecting) state = TransportState.Disconnected;
                    break;
                case BleEventKind.MtuChanged:
                    SetPayloads(e.Value, e.Value2);
                    break;
                case BleEventKind.ConnectionInterval:
                    stats.ConnectionIntervalMicros = e.Value;
                    break;
                case BleEventKind.Received:
                    {
                        byte[] d = e.Data ?? Array.Empty<byte>();
                        stats.PacketsReceived++;
                        stats.BytesReceived += d.Length;
                        if (state != TransportState.Connected) break;
                        if (e.Channel == (byte)TransportChannel.Input)
                        {
                            Received?.Invoke(d, TransportChannel.Input);
                        }
                        else if (reassembler.Add(d, out ReadOnlySpan<byte> message))
                        {
                            Received?.Invoke(message, TransportChannel.Control);
                        }
                        break;
                    }
                case BleEventKind.Error:
                    LastError = e.Name ?? ("error " + e.Value);
                    break;
                case BleEventKind.Advertising:
                    if (e.Value < 0)
                    {
                        LastError = e.Name ?? "advertising failed";
                        if (state == TransportState.Advertising) state = TransportState.Failed;
                    }
                    break;
            }
        }

        void SetPayloads(int control, int input)
        {
            if (control > ControlFragmenter.HeaderBytes) controlPayload = control;
            if (input >= DefaultAttPayload) inputPayload = input;
            stats.Mtu = Math.Min(controlPayload, inputPayload) + 3;
        }

        void ResetLink()
        {
            fragmenter.Reset();
            reassembler.Reset();
            controlPayload = DefaultAttPayload;
            inputPayload = DefaultAttPayload;
            stats = default;
            stats.Mtu = DefaultAttPayload + 3;
        }

        public void Disconnect()
        {
            bridge.StopScan();
            bridge.StopAdvertising();
            bridge.Disconnect();
            state = TransportState.Disconnected;
        }

        public void Dispose()
        {
            Disconnect();
            bridge.Dispose();
        }
    }
}
