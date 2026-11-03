using System;
using System.Collections.Generic;
using Duel.Core.Net;
using Duel.Core.Sim;
using Duel.Core.Transport.BLE;
using NUnit.Framework;

namespace Duel.Core.Tests
{
    /// <summary>
    /// The C# half of the BLE transport: advert parsing, the event wire format shared with Swift and
    /// Kotlin, control fragmentation at the default MTU, and full sessions over the loopback bridge.
    /// The radio half (DuelBle.swift, DuelBle.kt) needs the P0 device spike.
    /// </summary>
    public class BleTransportTests
    {
        [Test]
        public void CourtCodesTravelInTheLocalNameOrInServiceData()
        {
            Assert.AreEqual("VD-AB12", CourtAdvert.LocalName("ab12"));
            Assert.AreEqual("AB12", CourtAdvert.Parse("VD-AB12", null), "iOS host");
            Assert.AreEqual("K9Z0", CourtAdvert.Parse(null, new byte[] { (byte)'K', (byte)'9', (byte)'Z', (byte)'0' }), "Android host");
            Assert.IsNull(CourtAdvert.Parse("Somebody's headphones", null));
            Assert.IsNull(CourtAdvert.Parse(null, new byte[] { 1, 2, 3, 4 }));
            Assert.AreEqual("X700", CourtAdvert.Normalize("x7"), "padded to four characters");
        }

        [Test]
        public void EventsRoundTripThroughTheNativeWireFormat()
        {
            var e = new BleEvent { Kind = BleEventKind.PeerFound, Channel = 1, Value = -61, Value2 = 7, PeerId = "1A2B-é", Name = "VD-AB12", Data = new byte[] { 9, 8, 7 } };
            var buf = new byte[128];
            int n = BleEventCodec.Encode(e, buf);
            Assert.IsTrue(BleEventCodec.TryDecode(new ReadOnlySpan<byte>(buf, 0, n), out BleEvent d));
            Assert.AreEqual(e.Kind, d.Kind);
            Assert.AreEqual(e.Channel, d.Channel);
            Assert.AreEqual(-61, d.Value);
            Assert.AreEqual(7, d.Value2);
            Assert.AreEqual("1A2B-é", d.PeerId);
            Assert.AreEqual("VD-AB12", d.Name);
            CollectionAssert.AreEqual(e.Data, d.Data);
            Assert.IsFalse(BleEventCodec.TryDecode(new ReadOnlySpan<byte>(buf, 0, n - 1), out _), "truncated");

            // A Kotlin poll() batch: u16 length prefixes.
            var batch = new List<byte>();
            for (int i = 0; i < 3; i++)
            {
                batch.Add((byte)(n & 0xFF));
                batch.Add((byte)(n >> 8));
                for (int k = 0; k < n; k++) batch.Add(buf[k]);
            }
            int count = BleEventCodec.DecodeBatch(batch.ToArray(), x => Assert.AreEqual("VD-AB12", x.Name));
            Assert.AreEqual(3, count);
        }

        [Test]
        public void ControlMessagesSplitAtTheDefaultMtuAndReassemble()
        {
            var fragmenter = new ControlFragmenter();
            var reassembler = new ControlReassembler();
            var message = new byte[300];
            for (int i = 0; i < message.Length; i++) message[i] = (byte)(i * 13);
            var fragments = new List<byte[]>();
            fragmenter.Split(message, BleTransport.DefaultAttPayload, f => fragments.Add(f));
            Assert.AreEqual(16, fragments.Count, "300 bytes at 19 per fragment");
            foreach (byte[] f in fragments) Assert.LessOrEqual(f.Length, 20);
            byte[] rebuilt = null;
            foreach (byte[] f in fragments)
            {
                if (reassembler.Add(f, out ReadOnlySpan<byte> m)) rebuilt = m.ToArray();
            }
            CollectionAssert.AreEqual(message, rebuilt);

            // A message cut off by a reconnect is dropped, and the next one still arrives whole.
            var partial = new List<byte[]>();
            fragmenter.Split(message, 20, f => partial.Add(f));
            reassembler.Add(partial[0], out _);
            var next = new List<byte[]>();
            fragmenter.Split(new byte[] { 1, 2, 3 }, 20, f => next.Add(f));
            Assert.IsTrue(reassembler.Add(next[0], out ReadOnlySpan<byte> small));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, small.ToArray());

            // Zero-length control messages still produce one fragment.
            var empty = new List<byte[]>();
            fragmenter.Split(ReadOnlySpan<byte>.Empty, 20, f => empty.Add(f));
            Assert.AreEqual(1, empty.Count);
        }

        static void Pair(bool androidHost, int attPayload, out BleTransport host, out BleTransport guest, out LoopbackBleBridge hostBridge)
        {
            LoopbackBleBridge.CreatePair(out LoopbackBleBridge a, out LoopbackBleBridge b);
            a.AndroidStyleAdvert = androidHost;
            a.AttPayload = attPayload;
            b.AttPayload = attPayload;
            hostBridge = a;
            host = new BleTransport(a);
            guest = new BleTransport(b);
            host.Host("CT42", "Host");
            var found = new List<PeerInfo>();
            guest.PeerFound += p => found.Add(p);
            guest.StartScan();
            for (int i = 0; i < 4; i++)
            {
                host.Poll();
                guest.Poll();
            }
            Assert.AreEqual(1, found.Count, "court found by its advert");
            Assert.AreEqual("CT42", found[0].CourtCode);
            guest.Join(found[0]);
            for (int i = 0; i < 4; i++)
            {
                host.Poll();
                guest.Poll();
            }
            Assert.AreEqual(TransportState.Connected, host.State);
            Assert.AreEqual(TransportState.Connected, guest.State);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HostAndGuestFindEachOtherAndCarryBothChannels(bool androidHost)
        {
            Pair(androidHost, 20, out BleTransport host, out BleTransport guest, out _);
            var got = new List<string>();
            host.Received += (d, c) => got.Add(c + ":" + d.Length);
            var input = new byte[InputPacket.Size];
            guest.Send(input, TransportChannel.Input);
            guest.Send(new byte[200], TransportChannel.Control);
            host.Poll();
            CollectionAssert.AreEqual(new[] { "Input:20", "Control:200" }, got, "a 200-byte control message arrives whole over 20-byte payloads");
            Assert.AreEqual(23, host.Stats.Mtu);
            Assert.AreEqual(15000, host.Stats.ConnectionIntervalMicros);
        }

        [Test]
        public void APhoneThatCannotAdvertiseCanOnlyJoin()
        {
            LoopbackBleBridge.CreatePair(out LoopbackBleBridge a, out _);
            a.CanAdvertise = false;
            var t = new BleTransport(a);
            Assert.IsFalse(t.CanHost);
            t.Host("AB12", "x");
            Assert.AreEqual(TransportState.Failed, t.State);
            StringAssert.Contains("join", t.LastError);
        }

        [Test]
        public void GuestLeavingDisconnectsTheHost()
        {
            Pair(false, 20, out BleTransport host, out BleTransport guest, out _);
            guest.Disconnect();
            host.Poll();
            Assert.AreEqual(TransportState.Disconnected, host.State);
        }

        [Test]
        public void HandshakeAndRollbackOverBleStayInSync()
        {
            Pair(true, 182, out BleTransport hostLink, out BleTransport guestLink, out _);
            long now = 0;
            var hs0 = new DuelHandshake(hostLink, true, 0x99, 0x11110000u, new byte[] { 1 }, "Host");
            var hs1 = new DuelHandshake(guestLink, false, 0x99, 0x00002222u, new byte[] { 2 }, "Guest");
            for (int t = 0; t < 200 && (hs0.Phase < HandshakePhase.Ready || hs1.Phase < HandshakePhase.Ready); t++)
            {
                now += 16667;
                hs0.Poll(now);
                hs1.Poll(now);
            }
            Assert.AreEqual(HandshakePhase.Ready, hs0.Phase);
            Assert.AreEqual(HandshakePhase.Ready, hs1.Phase);
            Assert.AreEqual(0x11112222u, hs1.Result.Seed);

            var sim = new CounterSim();
            var a = new RollbackSession<CounterState>(sim, default, 0, true, hostLink, new NetConfig(), hs0.Result.InputDelay, now);
            var b = new RollbackSession<CounterState>(sim, default, 1, false, guestLink, new NetConfig(), hs1.Result.InputDelay, now);
            Rng ra = Rng.Create(9, 1), rb = Rng.Create(9, 2);
            FrameInput ia = FrameInput.Neutral, ib = FrameInput.Neutral;
            for (int t = 0; t < 900; t++)
            {
                now += 16667;
                if (ra.NextInt(5) == 0) ia = new FrameInput((ushort)ra.NextInt(4096));
                if (rb.NextInt(5) == 0) ib = new FrameInput((ushort)rb.NextInt(4096));
                a.Tick(ia, now);
                b.Tick(ib, now);
            }
            Assert.AreEqual(0, a.Stats.Desyncs + b.Stats.Desyncs);
            Assert.IsTrue(a.TryGetConfirmedHash(870, out ulong ha));
            Assert.IsTrue(b.TryGetConfirmedHash(870, out ulong hb));
            Assert.AreEqual(ha, hb);
        }
    }
}
