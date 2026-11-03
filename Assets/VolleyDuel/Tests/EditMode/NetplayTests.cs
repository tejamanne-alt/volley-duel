using System.Collections.Generic;
using Duel.Core.AI;
using Duel.Core.Net;
using Duel.Core.Sim;
using NUnit.Framework;
using VolleyDuel.AI;
using VolleyDuel.Sim;

namespace VolleyDuel.Tests
{
    /// <summary>
    /// Volleyball over rollback: a host and a guest phone each run the real sim with a CPU player over a
    /// simulated BLE link (latency, jitter, loss). Both phones must agree on every confirmed checkpoint, and
    /// an offline replay of the inputs each phone committed must reproduce the same timeline.
    /// </summary>
    public class NetplayTests
    {
        [TestCase(11u, 0, 2)]
        [TestCase(12u, 1, 3)]
        public void ASetOverALossyBleLinkStaysInSyncAndMatchesTheOfflineReplay(uint seed, int fighter0, int fighter1)
        {
            SimTables tables = Rig.LoadTables();
            var cpu = new CpuTables(tables);
            MatchConfig config = MatchConfig.Create(fighter0, fighter1, (int)(seed % (uint)tables.Courts.Length), seed, 1);
            var link = new InMemoryLink(LinkProfile.Ble, seed);
            link.A.ConnectDirect();
            var simA = new VolleySim(tables, config);
            var simB = new VolleySim(tables, config);
            var net = new NetConfig();
            var host = new RollbackSession<GameState>(simA, simA.CreateInitialState(), 0, true, link.A, net, 2, link.Now);
            var guest = new RollbackSession<GameState>(simB, simB.CreateInitialState(), 1, false, link.B, net, 2, link.Now);
            var inputs0 = new SortedDictionary<int, FrameInput>();
            var inputs1 = new SortedDictionary<int, FrameInput>();
            host.LocalInputCommitted += (f, i) => inputs0[f] = i;
            guest.LocalInputCommitted += (f, i) => inputs1[f] = i;
            TierProfile tier = cpu.Tiers[cpu.TierIndex("pro")];
            var cpuA = new VolleyCpu(simA, cpu, 0, tier, seed * 3 + 1);
            var cpuB = new VolleyCpu(simB, cpu, 1, tier, seed * 3 + 2);

            int checkpoint = -1;
            for (int tick = 0; tick < 60 * 60 * 20; tick++)
            {
                link.Advance(net.TickMicros);
                host.Tick(cpuA.Decide(host.State), link.Now);
                guest.Tick(cpuB.Decide(guest.State), link.Now);
                if (checkpoint < 0 && simA.IsFinished(host.State) && simB.IsFinished(guest.State))
                {
                    checkpoint = (System.Math.Max(host.Frame, guest.Frame) / net.HashInterval + 1) * net.HashInterval;
                }
                if (checkpoint >= 0 && host.ConfirmedFrame >= checkpoint && guest.ConfirmedFrame >= checkpoint) break;
            }

            Assert.GreaterOrEqual(checkpoint, 0, "the set finished on both phones");
            Assert.AreEqual(0, host.Stats.Desyncs + guest.Stats.Desyncs, "no desyncs");
            Assert.Greater(host.Stats.Rollbacks + guest.Stats.Rollbacks, 0, "the link forced rollbacks");
            Assert.Greater(host.Stats.HashChecks, 100);
            Assert.IsTrue(host.TryGetConfirmedHash(checkpoint, out ulong hostHash));
            Assert.IsTrue(guest.TryGetConfirmedHash(checkpoint, out ulong guestHash));
            Assert.AreEqual(hostHash, guestHash, "both phones confirmed the same final state");

            // Offline replay of the committed inputs (frames never committed carry neutral input on both phones).
            var offline = new VolleySim(tables, config);
            GameState s = offline.CreateInitialState();
            var events = new SimEventBuffer();
            for (int f = 0; f < checkpoint; f++)
            {
                FrameInput a = inputs0.TryGetValue(f, out FrameInput x) ? x : FrameInput.Neutral;
                FrameInput b = inputs1.TryGetValue(f, out FrameInput y) ? y : FrameInput.Neutral;
                events.Clear();
                s = offline.Step(s, a, b, events);
            }
            Assert.AreEqual(hostHash, offline.Hash(s), "rollback timeline equals the offline replay");
            Assert.IsTrue(offline.IsFinished(s));
        }
    }
}
