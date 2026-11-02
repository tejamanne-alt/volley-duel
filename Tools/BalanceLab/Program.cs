using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duel.Core.AI;
using Duel.Core.Sim;
using VolleyDuel.AI;
using VolleyDuel.Sim;

namespace VolleyDuel.BalanceLab
{
    /// <summary>
    /// Headless Balance Lab: CPU-vs-CPU matches through the real sim.
    ///   match  --p0 rafa --p1 rin --tier legend --seed 1 [--trace path.json]
    ///   lab    --tier legend --matches 400 [--threads 4]
    ///   gate   --tier legend --matches 1000            (exit 1 if a balance gate fails)
    ///   tiers  --matches 200                           (CPU win rates rise with each tier)
    ///   golden --frames 10000 [--write]                (determinism golden replay)
    ///   bench                                          (step and 8-frame resimulation cost)
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string command = args.Length > 0 ? args[0] : "help";
            var opts = ParseOptions(args.Skip(1).ToArray());
            SimTables tables = SimTables.Load(DirectoryDataSource.FindFrom(AppContext.BaseDirectory));
            var cpu = new CpuTables(tables);
            switch (command)
            {
                case "match": return RunMatch(tables, cpu, opts);
                case "lab": return RunLab(tables, cpu, opts, false);
                case "gate": return RunLab(tables, cpu, opts, true);
                case "tiers": return RunTiers(tables, cpu, opts);
                case "golden": return Golden.Run(tables, cpu, opts);
                case "bench": return Bench.Run(tables, cpu, opts);
                case "trace": return Trace.Run(tables, cpu, opts);
                default:
                    Console.WriteLine("commands: match, lab, gate, tiers, golden, bench, trace");
                    return command == "help" ? 0 : 2;
            }
        }

        public static Dictionary<string, string> ParseOptions(string[] args)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
                string key = args[i].Substring(2);
                string value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
                d[key] = value;
            }
            return d;
        }

        public static string Opt(Dictionary<string, string> o, string key, string fallback) => o.TryGetValue(key, out string v) ? v : fallback;

        public static int OptInt(Dictionary<string, string> o, string key, int fallback) => o.TryGetValue(key, out string v) ? int.Parse(v, System.Globalization.CultureInfo.InvariantCulture) : fallback;

        static int RunMatch(SimTables tables, CpuTables cpu, Dictionary<string, string> o)
        {
            var setup = new MatchSetup
            {
                Fighter0 = tables.FighterIndex(Opt(o, "p0", "rafa")),
                Fighter1 = tables.FighterIndex(Opt(o, "p1", "rafa")),
                Court = tables.CourtIndex(Opt(o, "court", "showcase-night")),
                Seed = (uint)OptInt(o, "seed", 1),
                Tier0 = cpu.Tiers[cpu.TierIndex(Opt(o, "tier0", Opt(o, "tier", "legend")))],
                Tier1 = cpu.Tiers[cpu.TierIndex(Opt(o, "tier1", Opt(o, "tier", "legend")))],
                SetsToWin = OptInt(o, "sets", 2),
                FrameCap = OptInt(o, "frames", 60 * 60 * 40),
            };
            if (o.ContainsKey("log")) setup.Trace = new EventLog(tables, OptInt(o, "log", 5));
            var sw = Stopwatch.StartNew();
            MatchStats stats = MatchRunner.Run(tables, cpu, setup, out GameState final, out Replay replay);
            sw.Stop();
            Console.WriteLine(tables.Fighters[setup.Fighter0].ShortName + " vs " + tables.Fighters[setup.Fighter1].ShortName +
                "  winner P" + final.Winner + "  sets " + final.SetsWon0 + "-" + final.SetsWon1 + "  last set " + final.Score0 + "-" + final.Score1 +
                "  phase " + final.Phase + "  (" + sw.ElapsedMilliseconds + " ms)");
            Console.Write(stats.Summary());
            return 0;
        }

        public struct PairResult
        {
            public int A, B;
            public MatchStats Stats;
            public double WinRateA;
        }

        static int RunLab(SimTables tables, CpuTables cpu, Dictionary<string, string> o, bool gate)
        {
            int matches = OptInt(o, "matches", 200);
            int threads = OptInt(o, "threads", Environment.ProcessorCount);
            TierProfile tier = cpu.Tiers[cpu.TierIndex(Opt(o, "tier", "legend"))];
            int n = tables.Fighters.Length;
            var pairs = new List<(int, int)>();
            for (int a = 0; a < n; a++)
            {
                for (int b = a; b < n; b++) pairs.Add((a, b));
            }
            var results = new PairResult[pairs.Count];
            var total = new MatchStats();
            var sw = Stopwatch.StartNew();
            var lockObj = new object();
            for (int p = 0; p < pairs.Count; p++)
            {
                (int a, int b) = pairs[p];
                var pairStats = new MatchStats();
                int winsA = 0;
                Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = threads }, m =>
                {
                    // Alternate sides so neither fighter always plays from the left.
                    bool swap = (m & 1) == 1;
                    var setup = new MatchSetup
                    {
                        Fighter0 = swap ? b : a,
                        Fighter1 = swap ? a : b,
                        Court = m % tables.Courts.Length,
                        Seed = (uint)(1000003 * (p + 1) + m * 7919 + OptInt(o, "seed", 0)),
                        Tier0 = tier,
                        Tier1 = tier,
                    };
                    MatchStats s = MatchRunner.Run(tables, cpu, setup, out GameState final, out Replay unused);
                    bool aWon = swap ? final.Winner == 1 : final.Winner == 0;
                    lock (lockObj)
                    {
                        pairStats.Add(s);
                        if (aWon) winsA++;
                    }
                });
                results[p] = new PairResult { A = a, B = b, Stats = pairStats, WinRateA = (double)winsA / matches };
                total.Add(pairStats);
                Console.WriteLine("{0,-6} vs {1,-6}  {2,6:P1}  receiver {3:P1}  {4:F1} min  rally {5:F1}s",
                    tables.Fighters[a].ShortName, tables.Fighters[b].ShortName, results[p].WinRateA, pairStats.ReceiverRate, pairStats.MinutesPerMatch, pairStats.SecondsPerRally);
            }
            sw.Stop();
            Console.WriteLine();
            Console.Write(total.Summary());
            Console.WriteLine("elapsed {0:F1} s for {1} matches", sw.Elapsed.TotalSeconds, matches * pairs.Count);
            Report.Write(tables, results, total, tier.Name, matches);

            if (!gate) return 0;
            bool ok = true;
            foreach (PairResult r in results)
            {
                if (r.A == r.B) continue;
                if (r.WinRateA < 0.45 || r.WinRateA > 0.55)
                {
                    Console.WriteLine("GATE FAIL: {0} vs {1} at {2:P1} (must be 45-55%)", tables.Fighters[r.A].ShortName, tables.Fighters[r.B].ShortName, r.WinRateA);
                    ok = false;
                }
            }
            if (total.ReceiverRate < 0.55 || total.ReceiverRate > 0.60)
            {
                Console.WriteLine("GATE FAIL: receiver wins {0:P1} of rallies (must be 55-60%)", total.ReceiverRate);
                ok = false;
            }
            Console.WriteLine(ok ? "BALANCE GATE: PASS" : "BALANCE GATE: FAIL");
            return ok ? 0 : 1;
        }

        static int RunTiers(SimTables tables, CpuTables cpu, Dictionary<string, string> o)
        {
            int matches = OptInt(o, "matches", 100);
            int threads = OptInt(o, "threads", Environment.ProcessorCount);
            bool ok = true;
            Console.WriteLine("Higher tier vs the tier below (mirror fighters, all four):");
            for (int t = 1; t < cpu.Tiers.Length; t++)
            {
                int wins = 0;
                var lockObj = new object();
                Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = threads }, m =>
                {
                    bool swap = (m & 1) == 1;
                    int f = m % tables.Fighters.Length;
                    var setup = new MatchSetup
                    {
                        Fighter0 = f,
                        Fighter1 = f,
                        Court = m % tables.Courts.Length,
                        Seed = (uint)(77 + t * 100003 + m),
                        Tier0 = swap ? cpu.Tiers[t - 1] : cpu.Tiers[t],
                        Tier1 = swap ? cpu.Tiers[t] : cpu.Tiers[t - 1],
                    };
                    MatchRunner.Run(tables, cpu, setup, out GameState final, out Replay unused);
                    bool upperWon = swap ? final.Winner == 1 : final.Winner == 0;
                    if (upperWon) Interlocked.Increment(ref wins);
                });
                double rate = (double)wins / matches;
                Console.WriteLine("  {0,-9} beats {1,-9} {2,6:P1}", cpu.Tiers[t].Name, cpu.Tiers[t - 1].Name, rate);
                if (rate <= 0.5) ok = false;
            }
            Console.WriteLine(ok ? "TIER GATE: PASS" : "TIER GATE: FAIL");
            return ok ? 0 : 1;
        }
    }
}
