using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Duel.Core.Net;
using Duel.Core.Sim;
using Duel.Core.Transport.Lan;
using NUnit.Framework;

namespace Duel.Core.Tests
{
    /// <summary>LAN transport over real UDP sockets on loopback.</summary>
    public class LanTransportTests
    {
        static void PollUntil(System.Func<bool> done, int timeoutMs, params LanTransport[] transports)
        {
            var sw = Stopwatch.StartNew();
            while (!done() && sw.ElapsedMilliseconds < timeoutMs)
            {
                foreach (LanTransport t in transports) t.Poll();
                Thread.Sleep(1);
            }
        }

        static LanTransport NewHost()
        {
            var host = new LanTransport(null, 0, IPAddress.Loopback);
            host.Host("AB12", "Host");
            return host;
        }

        static LanTransport JoinGuest(LanTransport host)
        {
            var guest = new LanTransport(null, 0, IPAddress.Loopback);
            guest.Join(new PeerInfo { Id = LanTransport.FormatEndPoint(IPAddress.Loopback, host.LocalPort) });
            PollUntil(() => host.State == TransportState.Connected && guest.State == TransportState.Connected, 3000, host, guest);
            return guest;
        }

        static int FreeUdpPort()
        {
            using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                s.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return ((IPEndPoint)s.LocalEndPoint).Port;
            }
        }

        [Test]
        public void ConnectsOverLoopbackAndCarriesBothChannels()
        {
            using (LanTransport host = NewHost())
            using (LanTransport guest = JoinGuest(host))
            {
                Assert.AreEqual(TransportState.Connected, host.State);
                Assert.AreEqual(TransportState.Connected, guest.State);
                var got = new List<string>();
                host.Received += (d, c) => got.Add(c + ":" + d.Length + ":" + d[0]);
                var input = new byte[InputPacket.Size];
                input[0] = 42;
                guest.Send(input, TransportChannel.Input);
                guest.Send(new byte[] { 7, 1, 2 }, TransportChannel.Control);
                PollUntil(() => got.Count >= 2, 2000, host, guest);
                CollectionAssert.AreEquivalent(new[] { "Input:20:42", "Control:3:7" }, got);
            }
        }

        [Test]
        public void ControlChannelIsReliableAndOrderedUnderHeavyLoss()
        {
            using (LanTransport host = NewHost())
            using (LanTransport guest = JoinGuest(host))
            {
                host.DropPermille = 300;
                guest.DropPermille = 300;
                var order = new List<int>();
                host.Received += (d, c) =>
                {
                    if (c == TransportChannel.Control) order.Add(d[0] | (d[1] << 8));
                };
                const int count = 300;
                for (int i = 0; i < count; i++) guest.Send(new byte[] { (byte)i, (byte)(i >> 8), 0, 0 }, TransportChannel.Control);
                PollUntil(() => order.Count >= count, 15000, host, guest);
                Assert.AreEqual(count, order.Count, "every control message arrived despite 30% loss each way");
                for (int i = 0; i < count; i++) Assert.AreEqual(i, order[i], "in order");
            }
        }

        [Test]
        public void ASecondGuestIsTurnedAway()
        {
            using (LanTransport host = NewHost())
            using (LanTransport guest = JoinGuest(host))
            using (var late = new LanTransport(null, 0, IPAddress.Loopback))
            {
                late.Join(new PeerInfo { Id = LanTransport.FormatEndPoint(IPAddress.Loopback, host.LocalPort) });
                PollUntil(() => late.State == TransportState.Failed, 3000, host, guest, late);
                Assert.AreEqual(TransportState.Failed, late.State);
                Assert.AreEqual(TransportState.Connected, guest.State, "the first guest keeps the court");
            }
        }

        [Test]
        public void ByeDisconnectsThePeer()
        {
            using (LanTransport host = NewHost())
            using (LanTransport guest = JoinGuest(host))
            {
                guest.Disconnect();
                PollUntil(() => host.State == TransportState.Disconnected, 2000, host);
                Assert.AreEqual(TransportState.Disconnected, host.State);
            }
        }

        [Test]
        public void BeaconDiscoveryFindsTheCourtAndJoinsIt()
        {
            int port = FreeUdpPort();
            var hostDiscovery = new BroadcastDiscovery(port, new[] { new IPEndPoint(IPAddress.Loopback, port) }, broadcast: false);
            using (var host = new LanTransport(hostDiscovery, 0, IPAddress.Loopback))
            using (var guest = new LanTransport(new BroadcastDiscovery(port, null, broadcast: false), 0, IPAddress.Loopback))
            {
                host.Host("CT42", "Court host");
                var found = new List<PeerInfo>();
                guest.PeerFound += p => found.Add(p);
                guest.StartScan();
                PollUntil(() => found.Count > 0, 3000, host, guest);
                Assert.AreEqual(1, found.Count);
                Assert.AreEqual("CT42", found[0].CourtCode);
                Assert.AreEqual("Court host", found[0].DisplayName);
                guest.Join(found[0]);
                PollUntil(() => host.State == TransportState.Connected && guest.State == TransportState.Connected, 3000, host, guest);
                Assert.AreEqual(TransportState.Connected, guest.State);
            }
        }

        [Test]
        public void EndPointsParseAndFormat()
        {
            Assert.IsTrue(LanTransport.TryParseEndPoint("192.168.1.20:5000", out IPEndPoint e));
            Assert.AreEqual(5000, e.Port);
            Assert.IsTrue(LanTransport.TryParseEndPoint("10.0.0.3", out e));
            Assert.AreEqual(LanTransport.DefaultGamePort, e.Port);
            Assert.IsFalse(LanTransport.TryParseEndPoint("10.0.0.3:99999", out _));
            Assert.IsFalse(LanTransport.TryParseEndPoint("court", out _));
        }

        [Test]
        public void HandshakeAndRollbackOverLoopbackStayInSync()
        {
            using (LanTransport hostLink = NewHost())
            using (LanTransport guestLink = JoinGuest(hostLink))
            {
                hostLink.DropPermille = 50;
                guestLink.DropPermille = 50;
                var clock = Stopwatch.StartNew();
                long Now() => clock.ElapsedTicks * 1000000L / Stopwatch.Frequency;
                var hs0 = new DuelHandshake(hostLink, true, 0x1234, 0xAAAA0000u, new byte[] { 1 }, "Host");
                var hs1 = new DuelHandshake(guestLink, false, 0x1234, 0x0000BBBBu, new byte[] { 2 }, "Guest");
                var sw = Stopwatch.StartNew();
                while ((hs0.Phase < HandshakePhase.Ready || hs1.Phase < HandshakePhase.Ready) && sw.ElapsedMilliseconds < 5000)
                {
                    hs0.Poll(Now());
                    hs1.Poll(Now());
                    Thread.Sleep(1);
                }
                Assert.AreEqual(HandshakePhase.Ready, hs0.Phase);
                Assert.AreEqual(HandshakePhase.Ready, hs1.Phase);
                Assert.AreEqual(hs0.Result.Seed, hs1.Result.Seed);
                Assert.AreEqual(0xAAAABBBBu, hs0.Result.Seed);

                var sim = new CounterSim();
                var cfg = new NetConfig();
                var a = new RollbackSession<CounterState>(sim, default, 0, true, hostLink, cfg, hs0.Result.InputDelay, Now());
                var b = new RollbackSession<CounterState>(sim, default, 1, false, guestLink, cfg, hs1.Result.InputDelay, Now());
                Rng ra = Rng.Create(3, 1), rb = Rng.Create(3, 2);
                FrameInput ia = FrameInput.Neutral, ib = FrameInput.Neutral;
                int ticks = 0;
                while ((a.ConfirmedFrame < 1210 || b.ConfirmedFrame < 1210) && ticks < 20000)
                {
                    if (ra.NextInt(5) == 0) ia = new FrameInput((ushort)ra.NextInt(4096));
                    if (rb.NextInt(5) == 0) ib = new FrameInput((ushort)rb.NextInt(4096));
                    a.Tick(ia, Now());
                    b.Tick(ib, Now());
                    ticks++;
                    if ((ticks & 3) == 0) Thread.Sleep(1);
                }
                Assert.GreaterOrEqual(a.ConfirmedFrame, 1210);
                Assert.AreEqual(0, a.Stats.Desyncs + b.Stats.Desyncs);
                Assert.IsTrue(a.TryGetConfirmedHash(1200, out ulong ha));
                Assert.IsTrue(b.TryGetConfirmedHash(1200, out ulong hb));
                Assert.AreEqual(ha, hb);
            }
        }
    }
}
