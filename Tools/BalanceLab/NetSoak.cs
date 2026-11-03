using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Duel.Core.AI;
using Duel.Core.Net;
using VolleyDuel.AI;
using VolleyDuel.Sim;

namespace VolleyDuel.BalanceLab
{
    /// <summary>
    /// Netcode soak (P2 gate stand-in): full Legend CPU matches between two separate sim instances, as on two
    /// phones, through rollback sessions over a simulated BLE link. The device gate (30 iPhone-Android
    /// matches over real BLE, zero desyncs) still needs hardware.
    /// </summary>
    static class NetSoak
    {
        public sealed class Result
        {
            public int Frames, Rollbacks, MaxDepth, Desyncs, Resyncs, HashChecks, Stalls, Waits, Finished;
            public readonly int[] Histogram = new int[10];
            public long RttSum;
            public int RttSamples;
        }

        public static Result RunMatch(SimTables tables, CpuTables cpu, int fighter0, int fighter1, uint seed, LinkProfile profile, int frameCap)
        {
            var link = new InMemoryLink(profile, seed);
            link.A.ConnectDirect();
            var config = MatchConfig.Create(fighter0, fighter1, (int)(seed % (uint)tables.Courts.Length), seed);
            var simA = new VolleySim(tables, config);
            var simB = new VolleySim(tables, config);
            var cfg = new NetConfig();
            var a = new RollbackSession<GameState>(simA, simA.CreateInitialState(), 0, true, link.A, cfg, 2, link.Now);
            var b = new RollbackSession<GameState>(simB, simB.CreateInitialState(), 1, false, link.B, cfg, 2, link.Now);
            TierProfile legend = cpu.Tiers[cpu.Tiers.Length - 1];
            var cpuA = new VolleyCpu(simA, cpu, 0, legend, seed * 3 + 1);
            var cpuB = new VolleyCpu(simB, cpu, 1, legend, seed * 3 + 2);
            int ticks = 0;
            int endFrame = -1;
            int checkpoint = -1;
            while (ticks < frameCap)
            {
                link.Advance(cfg.TickMicros);
                a.Tick(cpuA.Decide(a.State), link.Now);
                b.Tick(cpuB.Decide(b.State), link.Now);
                ticks++;
                if (endFrame < 0 && simA.IsFinished(a.State) && simB.IsFinished(b.State))
                {
                    // Run on until a hash checkpoint after the final whistle is confirmed on both phones.
                    endFrame = Math.Max(a.Frame, b.Frame);
                    checkpoint = (endFrame / cfg.HashInterval + 1) * cfg.HashInterval;
                }
                if (endFrame >= 0 && a.ConfirmedFrame >= checkpoint && b.ConfirmedFrame >= checkpoint) break;
            }
            bool same = checkpoint >= 0 && a.TryGetConfirmedHash(checkpoint, out ulong ha) && b.TryGetConfirmedHash(checkpoint, out ulong hb) && ha == hb;
            var r = new Result
            {
                Frames = a.Stats.Frames + b.Stats.Frames,
                Rollbacks = a.Stats.Rollbacks + b.Stats.Rollbacks,
                MaxDepth = Math.Max(a.Stats.MaxRollbackDepth, b.Stats.MaxRollbackDepth),
                Desyncs = a.Stats.Desyncs + b.Stats.Desyncs,
                Resyncs = a.Stats.Resyncs + b.Stats.Resyncs,
                HashChecks = a.Stats.HashChecks + b.Stats.HashChecks,
                Stalls = a.Stats.Stalls + b.Stats.Stalls,
                Waits = a.Stats.Waits + b.Stats.Waits,
                Finished = same ? 1 : 0,
                RttSum = a.Stats.RttMicros + (long)b.Stats.RttMicros,
                RttSamples = 2,
            };
            for (int i = 0; i < r.Histogram.Length; i++) r.Histogram[i] = a.Stats.RollbackHistogram[i] + b.Stats.RollbackHistogram[i];
            return r;
        }

        public static int Run(SimTables tables, CpuTables cpu, Dictionary<string, string> o)
        {
            int matches = Program.OptInt(o, "matches", 30);
            LinkProfile profile = LinkProfile.Ble;
            if (o.ContainsKey("latency")) profile.LatencyMicros = Program.OptInt(o, "latency", 30) * 1000L;
            if (o.ContainsKey("jitter")) profile.JitterMicros = Program.OptInt(o, "jitter", 15) * 1000L;
            if (o.ContainsKey("loss")) profile.LossPermille = Program.OptInt(o, "loss", 3) * 10;
            var results = new Result[matches];
            var sw = Stopwatch.StartNew();
            Parallel.For(0, matches, m =>
            {
                int f0 = m % tables.Fighters.Length;
                int f1 = (m / tables.Fighters.Length + m) % tables.Fighters.Length;
                results[m] = RunMatch(tables, cpu, f0, f1, (uint)(9001 + m * 31), profile, 60 * 60 * 40);
            });
            var total = new Result();
            foreach (Result r in results)
            {
                total.Frames += r.Frames;
                total.Rollbacks += r.Rollbacks;
                total.MaxDepth = Math.Max(total.MaxDepth, r.MaxDepth);
                total.Desyncs += r.Desyncs;
                total.Resyncs += r.Resyncs;
                total.HashChecks += r.HashChecks;
                total.Stalls += r.Stalls;
                total.Waits += r.Waits;
                total.Finished += r.Finished;
                total.RttSum += r.RttSum;
                total.RttSamples += r.RttSamples;
                for (int i = 0; i < r.Histogram.Length; i++) total.Histogram[i] += r.Histogram[i];
            }
            int p95 = Percentile(total, 95), p99 = Percentile(total, 99);
            int depth95 = DepthPercentile(total, 95);
            Console.WriteLine("netsoak: {0} matches over a simulated link (latency {1} ms + jitter {2} ms, {3}% input loss) in {4:F1} s",
                matches, profile.LatencyMicros / 1000, profile.JitterMicros / 1000, profile.LossPermille / 10.0, sw.Elapsed.TotalSeconds);
            Console.WriteLine("  finished identically on both peers: {0}/{1}", total.Finished, matches);
            Console.WriteLine("  desyncs {0} (resyncs {1}) over {2} hash checks", total.Desyncs, total.Resyncs, total.HashChecks);
            Console.WriteLine("  rollback: {0} of {1} frames, p95 {2}, p99 {3}, max {4} frames; stalls {5}, time-sync waits {6}; mean RTT {7} ms",
                total.Rollbacks, total.Frames, p95, p99, total.MaxDepth, total.Stalls, total.Waits, total.RttSum / Math.Max(1, total.RttSamples) / 1000);
            Console.WriteLine("  depth of a rollback when one happens: p95 {0} frames", depth95);
            Console.Write("  rollback histogram:");
            for (int i = 1; i < total.Histogram.Length; i++) Console.Write(" {0}:{1}", i, total.Histogram[i]);
            Console.WriteLine();
            bool ok = total.Desyncs == 0 && total.Finished == matches && p95 <= 4;
            Console.WriteLine(ok ? "NETCODE SOAK GATE: PASS" : "NETCODE SOAK GATE: FAIL");
            return ok ? 0 : 1;
        }

        /// <summary>Rollback depth percentile over rollbacks only (frames without one excluded).</summary>
        static int DepthPercentile(Result r, int percent)
        {
            int target = (int)(((long)r.Rollbacks * percent + 99) / 100);
            int seen = 0;
            for (int d = 1; d < r.Histogram.Length; d++)
            {
                seen += r.Histogram[d];
                if (seen >= target) return d;
            }
            return r.Histogram.Length - 1;
        }

        static int Percentile(Result r, int percent)
        {
            int total = r.Frames;
            int noRollback = total - r.Rollbacks;
            int target = (int)((long)total * percent / 100);
            if (target <= noRollback) return 0;
            int seen = noRollback;
            for (int d = 1; d < r.Histogram.Length; d++)
            {
                seen += r.Histogram[d];
                if (seen >= target) return d;
            }
            return r.Histogram.Length - 1;
        }
    }
}
