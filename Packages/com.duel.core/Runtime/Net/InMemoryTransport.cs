using System;
using System.Collections.Generic;
using Duel.Core.Sim;

namespace Duel.Core.Net
{
    /// <summary>Simulated link conditions for soak tests and local development.</summary>
    public struct LinkProfile
    {
        public long LatencyMicros;
        public long JitterMicros;
        /// <summary>Chance (0-1000) that an input-channel packet is dropped. Control is reliable.</summary>
        public int LossPermille;

        public static LinkProfile Perfect => new LinkProfile();

        /// <summary>Roughly BLE between two phones: 15-45 ms each way (spec latency budget).</summary>
        public static LinkProfile Ble => new LinkProfile { LatencyMicros = 30000, JitterMicros = 15000, LossPermille = 30 };
    }

    /// <summary>
    /// Two transports joined by a simulated wire with latency, jitter and loss, driven by a virtual clock.
    /// Deterministic for a given seed, so netcode soak tests are reproducible.
    /// </summary>
    public sealed class InMemoryLink
    {
        public readonly InMemoryTransport A;
        public readonly InMemoryTransport B;
        long clock;
        public LinkProfile Profile;
        internal Rng Random;

        public InMemoryLink(LinkProfile profile, ulong seed)
        {
            Profile = profile;
            Random = Rng.Create(seed, 77);
            A = new InMemoryTransport(this, "link-a");
            B = new InMemoryTransport(this, "link-b");
            A.Other = B;
            B.Other = A;
        }

        public long Now => clock;

        public void Advance(long micros) => clock += micros;

        /// <summary>Cuts the wire (both directions) until <see cref="Restore"/>.</summary>
        public bool Cut { get; set; }

        public void Restore() => Cut = false;
    }

    public sealed class InMemoryTransport : ITransport
    {
        struct Packet
        {
            public long DeliverAt;
            public long Order;
            public byte[] Data;
            public TransportChannel Channel;
        }

        readonly InMemoryLink link;
        readonly List<Packet> inbox = new List<Packet>();
        long order;
        TransportStats stats;
        TransportState state = TransportState.Idle;
        TransportState reported = TransportState.Idle;
        internal InMemoryTransport Other;
        long lastControlDelivery;

        internal InMemoryTransport(InMemoryLink link, string name)
        {
            this.link = link;
            Name = name;
        }

        public string Name { get; }

        public TransportState State => state;

        public TransportStats Stats => stats;

        public bool CanHost => true;

        public int MaxPayload(TransportChannel channel) => channel == TransportChannel.Input ? InputPacket.Size : 512;

        public event ReceiveHandler Received;
        public event Action<TransportState> StateChanged;
        public event Action<PeerInfo> PeerFound;

        public void Host(string courtCode, string displayName)
        {
            state = TransportState.Advertising;
            HostCode = courtCode;
            HostName = displayName;
        }

        internal string HostCode;
        internal string HostName;

        public void StartScan()
        {
            state = TransportState.Scanning;
            if (Other.state == TransportState.Advertising)
            {
                PeerFound?.Invoke(new PeerInfo { Id = Other.Name, DisplayName = Other.HostName, CourtCode = Other.HostCode, SignalDbm = -50 });
            }
        }

        public void StopScan()
        {
            if (state == TransportState.Scanning) state = TransportState.Idle;
        }

        public void Join(PeerInfo peer)
        {
            state = TransportState.Connected;
            Other.state = TransportState.Connected;
        }

        /// <summary>Test shortcut: both ends connected immediately.</summary>
        public void ConnectDirect()
        {
            state = TransportState.Connected;
            Other.state = TransportState.Connected;
        }

        public void Send(ReadOnlySpan<byte> data, TransportChannel channel)
        {
            stats.PacketsSent++;
            stats.BytesSent += data.Length;
            if (state != TransportState.Connected || link.Cut) return;
            LinkProfile p = link.Profile;
            if (channel == TransportChannel.Input && p.LossPermille > 0 && link.Random.NextInt(1000) < p.LossPermille) return;
            long jitter = p.JitterMicros > 0 ? link.Random.NextInt((int)Math.Min(int.MaxValue, p.JitterMicros + 1)) : 0;
            long at = link.Now + p.LatencyMicros + jitter;
            if (channel == TransportChannel.Control)
            {
                // Reliable and ordered: never overtakes an earlier control message.
                if (at < Other.lastControlDelivery) at = Other.lastControlDelivery;
                Other.lastControlDelivery = at;
            }
            Other.inbox.Add(new Packet { DeliverAt = at, Order = Other.order++, Data = data.ToArray(), Channel = channel });
        }

        public void Poll()
        {
            if (state != reported)
            {
                reported = state;
                StateChanged?.Invoke(state);
            }
            if (inbox.Count == 0) return;
            inbox.Sort((x, y) => x.DeliverAt != y.DeliverAt ? x.DeliverAt.CompareTo(y.DeliverAt) : x.Order.CompareTo(y.Order));
            int delivered = 0;
            while (delivered < inbox.Count && inbox[delivered].DeliverAt <= link.Now)
            {
                Packet pk = inbox[delivered++];
                stats.PacketsReceived++;
                stats.BytesReceived += pk.Data.Length;
                Received?.Invoke(pk.Data, pk.Channel);
            }
            inbox.RemoveRange(0, delivered);
        }

        public void Disconnect()
        {
            state = TransportState.Disconnected;
        }

        public void Dispose() => Disconnect();
    }
}
