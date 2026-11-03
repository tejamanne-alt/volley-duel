using System;
using System.Collections.Generic;
using Duel.Core.Net;
using Duel.Core.Sim;
using NUnit.Framework;

namespace Duel.Core.Tests
{
    /// <summary>A tiny deterministic game for testing the netcode in isolation.</summary>
    public struct CounterState
    {
        public int Tick;
        public long A;
        public long B;
        public ulong Mix;
    }

    public sealed class CounterSim : ISimulation<CounterState>
    {
        public int MaxStateBytes => 32;

        public CounterState Step(in CounterState state, FrameInput p1, FrameInput p2, SimEventBuffer events)
        {
            CounterState s = state;
            s.Tick++;
            s.A += p1.Bits * ((s.Tick % 5) + 1);
            s.B += p2.Bits * ((s.Tick % 3) + 1);
            s.Mix = StateHash.Add(StateHash.Add(s.Mix, p1.Bits), p2.Bits);
            if (p1.Has(InputButtons.Hit)) events.Add(s.Tick, 1, 0, p1.Bits);
            if (p2.Has(InputButtons.Hit)) events.Add(s.Tick, 1, 1, p2.Bits);
            return s;
        }

        public int FrameOf(in CounterState state) => state.Tick;
        public bool IsFinished(in CounterState state) => false;

        public int Serialize(in CounterState state, Span<byte> destination)
        {
            var w = new StateWriter(destination);
            w.WriteInt32(state.Tick);
            w.WriteInt64(state.A);
            w.WriteInt64(state.B);
            w.WriteUInt64(state.Mix);
            return w.Position;
        }

        public CounterState Deserialize(ReadOnlySpan<byte> source)
        {
            var r = new StateReader(source);
            return new CounterState { Tick = r.ReadInt32(), A = r.ReadInt64(), B = r.ReadInt64(), Mix = r.ReadUInt64() };
        }

        public ulong Hash(in CounterState state) => SimulationHash.Of(this, state);
    }

    /// <summary>Two rollback sessions over a simulated link, with a recorded input timeline for reference checks.</summary>
    public sealed class NetPair
    {
        public readonly InMemoryLink Link;
        public readonly RollbackSession<CounterState> A;
        public readonly RollbackSession<CounterState> B;
        public readonly CounterSim Sim = new CounterSim();
        public readonly SortedDictionary<int, FrameInput> InputsA = new SortedDictionary<int, FrameInput>();
        public readonly SortedDictionary<int, FrameInput> InputsB = new SortedDictionary<int, FrameInput>();
        Rng rngA, rngB;
        FrameInput curA, curB;
        public int Ticks;
        public int SkipEveryB;

        public NetPair(LinkProfile profile, int delay = 2, ulong seed = 5)
        {
            Link = new InMemoryLink(profile, seed);
            Link.A.ConnectDirect();
            var cfg = new NetConfig();
            A = new RollbackSession<CounterState>(Sim, default, 0, true, Link.A, cfg, delay, Link.Now);
            B = new RollbackSession<CounterState>(Sim, default, 1, false, Link.B, cfg, delay, Link.Now);
            A.LocalInputCommitted += (f, i) => InputsA[f] = i;
            B.LocalInputCommitted += (f, i) => InputsB[f] = i;
            rngA = Rng.Create(seed, 1);
            rngB = Rng.Create(seed, 2);
        }

        public void Run(int ticks, bool active = true)
        {
            for (int t = 0; t < ticks; t++)
            {
                Link.Advance(16667);
                if (active && rngA.NextInt(6) == 0) curA = new FrameInput((ushort)rngA.NextInt(4096));
                if (active && rngB.NextInt(6) == 0) curB = new FrameInput((ushort)rngB.NextInt(4096));
                if (!active)
                {
                    curA = FrameInput.Neutral;
                    curB = FrameInput.Neutral;
                }
                A.Tick(curA, Link.Now);
                Ticks++;
                // Peer B's clock can run slightly slow to exercise time sync.
                if (SkipEveryB > 0 && Ticks % SkipEveryB == 0) continue;
                B.Tick(curB, Link.Now);
            }
        }

        /// <summary>Replays the committed inputs locally and returns the state hash at <paramref name="frame"/>.</summary>
        public ulong ReferenceHash(int frame)
        {
            CounterState s = default;
            var events = new SimEventBuffer();
            for (int f = 0; f < frame; f++)
            {
                FrameInput a = InputsA.TryGetValue(f, out FrameInput x) ? x : FrameInput.Neutral;
                FrameInput b = InputsB.TryGetValue(f, out FrameInput y) ? y : FrameInput.Neutral;
                events.Clear();
                s = Sim.Step(s, a, b, events);
            }
            return Sim.Hash(s);
        }
    }

    public class NetcodeTests
    {
        [Test]
        public void InputPacketIsTwentyBytesAndRoundTrips()
        {
            InputPacket p = NetProtocol.BuildInputPacket(1000, 990, f => new FrameInput((ushort)(f * 7 & 0xFFF)));
            var buf = new byte[InputPacket.Size];
            p.Write(buf);
            Assert.AreEqual(20, InputPacket.Size);
            Assert.IsTrue(InputPacket.TryRead(buf, out InputPacket q));
            Assert.AreEqual(p.LatestFrame, q.LatestFrame);
            Assert.AreEqual(p.AckFrame, q.AckFrame);
            for (int k = 0; k < 8; k++) Assert.AreEqual(p.Input(k), q.Input(k));
            Assert.AreEqual((ushort)((993 * 7) & 0xFFF), q.Input(0), "oldest of the last 8 frames first");
            Assert.IsFalse(InputPacket.TryRead(new byte[19], out _));
        }

        [TestCase(65530, 65540)]
        [TestCase(70000, 70003)]
        [TestCase(5, 2)]
        public void SixteenBitFramesExpandAcrossTheWrap(int actual, int reference)
        {
            Assert.AreEqual(actual, FrameWire.Expand(FrameWire.Encode(actual), reference));
        }

        [Test]
        public void PerfectLinkNeverRollsBack()
        {
            var pair = new NetPair(LinkProfile.Perfect);
            pair.Run(600);
            Assert.AreEqual(0, pair.A.Stats.Rollbacks);
            Assert.AreEqual(0, pair.A.Stats.Desyncs + pair.B.Stats.Desyncs);
            Assert.Greater(pair.A.Stats.HashChecks, 10);
        }

        [Test]
        public void BleLikeLinkStaysInSyncWithBoundedRollback()
        {
            var pair = new NetPair(LinkProfile.Ble);
            pair.Run(3600);
            pair.Run(120, active: false);
            Assert.AreEqual(0, pair.A.Stats.Desyncs + pair.B.Stats.Desyncs, "zero desyncs");
            Assert.Greater(pair.A.Stats.HashChecks, 100);
            Assert.Greater(pair.A.Stats.Rollbacks + pair.B.Stats.Rollbacks, 0, "the link forced rollbacks");
            Assert.LessOrEqual(pair.A.Stats.MaxRollbackDepth, 8, "spec: at most 8 frames of rollback");
            Assert.LessOrEqual(pair.A.Stats.RollbackPercentile(95), 4, "P2 gate: rollback p95 of 4 frames or less");
            Assert.Greater(pair.A.Frame, 3400, "barely slowed down");
            // Both peers must also match a plain local replay of the committed inputs.
            int checkpoint = (Math.Min(pair.A.ConfirmedFrame, pair.B.ConfirmedFrame) / 30 - 1) * 30;
            Assert.IsTrue(pair.A.TryGetConfirmedHash(checkpoint, out ulong ha));
            Assert.IsTrue(pair.B.TryGetConfirmedHash(checkpoint, out ulong hb));
            Assert.AreEqual(pair.ReferenceHash(checkpoint), ha);
            Assert.AreEqual(ha, hb);
        }

        [Test]
        public void SlowPeerMakesTheLeaderWait()
        {
            var pair = new NetPair(new LinkProfile { LatencyMicros = 20000 });
            pair.SkipEveryB = 50;
            pair.Run(3000);
            Assert.Greater(pair.A.Stats.Waits + pair.A.Stats.Stalls, 0, "the leading peer slows down");
            Assert.LessOrEqual(Math.Abs(pair.A.Frame - pair.B.Frame), 10);
            Assert.AreEqual(0, pair.A.Stats.Desyncs + pair.B.Stats.Desyncs);
        }

        [Test]
        public void DesyncIsDetectedAndRepairedByTheHostsState()
        {
            var pair = new NetPair(new LinkProfile { LatencyMicros = 20000, JitterMicros = 5000 });
            pair.Run(200);
            // Corrupt the guest: as if one float had crept into its sim.
            var corrupt = new CounterSim();
            int resynced = -1;
            pair.B.Resynced += f => resynced = f;
            typeof(RollbackSession<CounterState>).GetField("state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(pair.B, new CounterState { Tick = pair.B.Frame, A = 12345, B = 1, Mix = 7 });
            pair.Run(400);
            Assert.Greater(pair.A.Stats.Desyncs + pair.B.Stats.Desyncs, 0, "hash exchange caught it");
            Assert.GreaterOrEqual(resynced, 0, "guest adopted the host's state");
            int before = pair.A.Stats.Desyncs + pair.B.Stats.Desyncs;
            pair.Run(400);
            Assert.AreEqual(before, pair.A.Stats.Desyncs + pair.B.Stats.Desyncs, "in sync again after the repair");
        }

        [Test]
        public void SilencePausesAndAShortDropoutResumes()
        {
            var pair = new NetPair(LinkProfile.Perfect);
            pair.Run(120);
            pair.Link.Cut = true;
            pair.Run(60 * 4);
            Assert.AreEqual(SessionStatus.Paused, pair.A.Status, "3 s of silence pauses the match");
            int pausedAt = pair.A.Frame;
            pair.Link.Restore();
            pair.Run(120);
            Assert.AreNotEqual(SessionStatus.Paused, pair.A.Status);
            Assert.AreNotEqual(SessionStatus.NoContest, pair.A.Status);
            Assert.Greater(pair.A.Frame, pausedAt + 90, "play resumed at full speed (frames " + pausedAt + " -> " + pair.A.Frame + ", B " + pair.B.Frame + ", stalls " + pair.A.Stats.Stalls + "/" + pair.B.Stats.Stalls + ", waits " + pair.A.Stats.Waits + "/" + pair.B.Stats.Waits + ", rtt " + pair.A.Stats.RttMicros + ", confirmed " + pair.A.ConfirmedFrame + "/" + pair.B.ConfirmedFrame + ")");
            Assert.Greater(pair.B.Frame, pausedAt + 90);
            Assert.AreEqual(0, pair.A.Stats.Desyncs + pair.B.Stats.Desyncs);
        }

        [Test]
        public void TwentySecondsWithoutTheOtherPhoneIsNoContest()
        {
            var pair = new NetPair(LinkProfile.Perfect);
            pair.Run(60);
            pair.Link.Cut = true;
            pair.Run(60 * 21);
            Assert.AreEqual(SessionStatus.NoContest, pair.A.Status);
        }

        static (DuelHandshake host, DuelHandshake guest, InMemoryLink link) Handshake(ulong hostVersion, ulong guestVersion, long latency)
        {
            var link = new InMemoryLink(new LinkProfile { LatencyMicros = latency }, 3);
            var host = new DuelHandshake(link.A, true, hostVersion, 0xAAAA0000u, new byte[] { 1, 2 }, "Host", req => new byte[] { 1, req[0] });
            var guest = new DuelHandshake(link.B, false, guestVersion, 0x0000BBBBu, new byte[] { 9 }, "Guest");
            link.A.Host("AB12", "Host");
            link.B.StartScan();
            link.B.Join(new PeerInfo { Id = "link-a", CourtCode = "AB12" });
            for (int t = 0; t < 600 && (host.Phase < HandshakePhase.Ready || guest.Phase < HandshakePhase.Ready); t++)
            {
                link.Advance(16667);
                host.Poll(link.Now);
                guest.Poll(link.Now);
            }
            return (host, guest, link);
        }

        [Test]
        public void HandshakeAgreesSeedConfigAndDelay()
        {
            var (host, guest, link) = Handshake(42, 42, 25000);
            Assert.AreEqual(HandshakePhase.Ready, host.Phase);
            Assert.AreEqual(HandshakePhase.Ready, guest.Phase);
            Assert.AreEqual(0xAAAABBBBu, host.Result.Seed, "seed = host seed XOR guest seed");
            Assert.AreEqual(host.Result.Seed, guest.Result.Seed);
            CollectionAssert.AreEqual(new byte[] { 1, 9 }, host.Result.Config, "host merged the guest's request");
            CollectionAssert.AreEqual(host.Result.Config, guest.Result.Config);
            Assert.AreEqual(host.Result.InputDelay, guest.Result.InputDelay);
            Assert.That(host.Result.InputDelay, Is.InRange(1, 3));
            Assert.That(host.Result.RttMicros, Is.InRange(40000, 70000));
            Assert.AreEqual("Guest", host.Result.RemoteName);
        }

        [Test]
        public void HandshakeRefusesMismatchedSimVersions()
        {
            var (host, guest, link) = Handshake(42, 43, 10000);
            Assert.AreEqual(HandshakePhase.Rejected, host.Phase);
            Assert.AreEqual(RejectReason.SimVersion, host.Rejection);
            Assert.AreEqual(HandshakePhase.Rejected, guest.Phase);
        }

        [Test]
        public void EventLedgerReplaysNothingTwiceAndOrphansRolledBackEvents()
        {
            var ledger = new EventLedger();
            var buf = new SimEventBuffer();
            buf.Add(10, 1, 0);
            buf.Add(11, 2, 0);
            ledger.Reconcile(buf, -1, 11);
            Assert.AreEqual(2, ledger.Spawned.Count);
            // Rollback from frame 10 re-runs frames 10 and 11: event (11, type 2) no longer happens; (11, type 3) is new.
            buf.Clear();
            buf.Add(11, 3, 0);
            ledger.Reconcile(buf, 10, 12);
            Assert.AreEqual(1, ledger.Spawned.Count);
            Assert.AreEqual(3, ledger.Spawned[0].Type);
            Assert.AreEqual(1, ledger.Orphaned.Count);
            Assert.AreEqual(2, ledger.Orphaned[0].Type);
            // The event from frame 10 (before the re-run range) is untouched.
            buf.Clear();
            ledger.Reconcile(buf, -1, 13);
            Assert.AreEqual(0, ledger.Spawned.Count + ledger.Orphaned.Count);
        }
    }
}
