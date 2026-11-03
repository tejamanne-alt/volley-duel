using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Duel.Core.Sim;
using VolleyDuel.AI;
using VolleyDuel.Sim;

namespace VolleyDuel.BalanceLab
{
    static class Paths
    {
        public static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "CLAUDE.md"))) dir = Path.GetDirectoryName(dir);
            return dir ?? Directory.GetCurrentDirectory();
        }

        public static string Artifacts()
        {
            string p = Path.Combine(RepoRoot(), "artifacts");
            Directory.CreateDirectory(p);
            return p;
        }
    }

    /// <summary>Writes the Balance Lab report to artifacts/balance.json.</summary>
    static class Report
    {
        public static void Write(SimTables tables, Program.PairResult[] results, MatchStats total, string tier, int matches)
        {
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            sb.Append("{\n  \"tier\": \"").Append(tier).Append("\",\n  \"matchesPerPairing\": ").Append(matches).Append(",\n");
            sb.Append("  \"simVersion\": \"0x").Append(SimVersion.Compute(tables).ToString("X16", inv)).Append("\",\n");
            sb.Append("  \"receiverRallyWinRate\": ").Append(total.ReceiverRate.ToString("F4", inv)).Append(",\n");
            sb.Append("  \"minutesPerMatch\": ").Append(total.MinutesPerMatch.ToString("F2", inv)).Append(",\n");
            sb.Append("  \"secondsPerRally\": ").Append(total.SecondsPerRally.ToString("F2", inv)).Append(",\n");
            sb.Append("  \"finisherLandRate\": ").Append((total.Finishers == 0 ? 0 : (double)total.FinisherLanded / total.Finishers).ToString("F4", inv)).Append(",\n");
            sb.Append("  \"pairings\": [\n");
            for (int i = 0; i < results.Length; i++)
            {
                Program.PairResult r = results[i];
                sb.Append("    { \"a\": \"").Append(tables.Fighters[r.A].Id).Append("\", \"b\": \"").Append(tables.Fighters[r.B].Id)
                  .Append("\", \"winRateA\": ").Append(r.WinRateA.ToString("F4", inv))
                  .Append(", \"receiverRate\": ").Append(r.Stats.ReceiverRate.ToString("F4", inv)).Append(" }")
                  .Append(i + 1 < results.Length ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            File.WriteAllText(Path.Combine(Paths.Artifacts(), "balance.json"), sb.ToString());
        }
    }

    /// <summary>
    /// Determinism golden replay: a recorded CPU match truncated to N frames (ball flight included),
    /// stored with its final hash. The EditMode test and device runs replay it and must match.
    /// </summary>
    static class Golden
    {
        public const string RelativePath = "Assets/VolleyDuel/Tests/EditMode/Golden/golden-10000.replay";

        public static int Run(SimTables tables, CpuTables cpu, Dictionary<string, string> o)
        {
            int frames = Program.OptInt(o, "frames", 10000);
            var config = MatchConfig.Create(tables.FighterIndex("rafa"), tables.FighterIndex("rin"), 0, 20261103u);
            var sim = new VolleySim(tables, config);
            GameState s = sim.CreateInitialState();
            var c0 = new VolleyCpu(sim, cpu, 0, cpu.Tiers[cpu.TierIndex("pro")], 11);
            var c1 = new VolleyCpu(sim, cpu, 1, cpu.Tiers[cpu.TierIndex("national")], 12);
            var replay = new Replay { Config = config.ToBytes(), SimVersion = SimVersion.Compute(tables) };
            var events = new SimEventBuffer();
            int flights = 0;
            for (int f = 0; f < frames; f++)
            {
                FrameInput i0 = c0.Decide(s), i1 = c1.Decide(s);
                events.Clear();
                s = sim.Step(s, i0, i1, events);
                replay.Add(i0, i1);
                for (int e = 0; e < events.Count; e++) if (events[e].Type == (ushort)VolleyEvent.Contact) flights++;
            }
            replay.FinalFrame = s.Tick;
            replay.FinalHash = sim.Hash(s);
            Console.WriteLine("golden: {0} frames, {1} ball contacts, score {2}-{3} sets {4}-{5}, hash 0x{6:X16}", frames, flights, s.Score0, s.Score1, s.SetsWon0, s.SetsWon1, replay.FinalHash);
            if (o.ContainsKey("write"))
            {
                string path = Path.Combine(Paths.RepoRoot(), RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, replay.ToBytes());
                Console.WriteLine("wrote " + RelativePath + " (" + new FileInfo(path).Length + " bytes)");
            }
            return 0;
        }
    }

    /// <summary>Measures sim step cost and the netcode budget: resimulating 8 frames.</summary>
    static class Bench
    {
        public static int Run(SimTables tables, CpuTables cpu, Dictionary<string, string> o)
        {
            var config = MatchConfig.Create(0, 2, 0, 5u);
            var sim = new VolleySim(tables, config);
            GameState s = sim.CreateInitialState();
            var c0 = new VolleyCpu(sim, cpu, 0, cpu.Tiers[4], 1);
            var c1 = new VolleyCpu(sim, cpu, 1, cpu.Tiers[4], 2);
            var events = new SimEventBuffer();
            var states = new List<GameState>();
            var inputs = new List<(FrameInput, FrameInput)>();
            for (int f = 0; f < 20000 && s.Phase != MatchPhase.MatchOver; f++)
            {
                FrameInput i0 = c0.Decide(s), i1 = c1.Decide(s);
                states.Add(s);
                inputs.Add((i0, i1));
                events.Clear();
                s = sim.Step(s, i0, i1, events);
            }
            // Warm up the JIT, then measure step cost alone (no CPU) and allocations per step.
            for (int f = 0; f < Math.Min(2000, states.Count); f++)
            {
                events.Clear();
                sim.Step(states[f], inputs[f].Item1, inputs[f].Item2, events);
            }
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            int gcBefore = GC.CollectionCount(0);
            var sw = Stopwatch.StartNew();
            int steps = 0;
            for (int rep = 0; rep < 3; rep++)
            {
                for (int f = 0; f < states.Count; f++)
                {
                    events.Clear();
                    sim.Step(states[f], inputs[f].Item1, inputs[f].Item2, events);
                    steps++;
                }
            }
            sw.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            int gcs = GC.CollectionCount(0) - gcBefore;
            double usPerStep = sw.Elapsed.TotalMilliseconds * 1000 / steps;
            // Every 8-frame window, as a rollback would resimulate it (restore + 8 steps + serialize).
            var buffer = new byte[GameState.MaxBytes];
            var windows = new List<double>();
            for (int f = 0; f + 8 < states.Count; f += 7)
            {
                var w = Stopwatch.StartNew();
                GameState r = states[f];
                for (int k = 0; k < 8; k++)
                {
                    events.Clear();
                    r = sim.Step(r, inputs[f + k].Item1, inputs[f + k].Item2, events);
                }
                sim.Serialize(r, buffer);
                w.Stop();
                windows.Add(w.Elapsed.TotalMilliseconds);
            }
            windows.Sort();
            double P(double q) => windows[Math.Min(windows.Count - 1, (int)(q * windows.Count))];
            Console.WriteLine("allocations: {0} bytes over {1} steps, {2} gen-0 collections", allocated, steps, gcs);
            Console.WriteLine("resimulate 8 frames over {0} windows: p50 {1:F3} ms, p99 {2:F3} ms, p99.9 {3:F3} ms, worst {4:F3} ms (desktop x64, shared CPU)", windows.Count, P(0.5), P(0.99), P(0.999), windows[windows.Count - 1]);
            int bytes = sim.Serialize(s, buffer);
            Console.WriteLine("step: {0:F2} us average over {1} steps", usPerStep, steps);
            Console.WriteLine("state: {0} bytes serialized (budget {1})", bytes, GameState.MaxBytes);
            return 0;
        }
    }

    /// <summary>Exports a CPU match as a compact JSON trace for Tools/ReplayViewer.</summary>
    static class Trace
    {
        public static int Run(SimTables tables, CpuTables cpu, Dictionary<string, string> o)
        {
            var sink = new JsonTrace(tables);
            var setup = new MatchSetup
            {
                Fighter0 = tables.FighterIndex(Program.Opt(o, "p0", "rafa")),
                Fighter1 = tables.FighterIndex(Program.Opt(o, "p1", "rin")),
                Court = tables.CourtIndex(Program.Opt(o, "court", "showcase-night")),
                Seed = (uint)Program.OptInt(o, "seed", 3),
                Tier0 = cpu.Tiers[cpu.TierIndex(Program.Opt(o, "tier", "legend"))],
                Tier1 = cpu.Tiers[cpu.TierIndex(Program.Opt(o, "tier", "legend"))],
                FrameCap = Program.OptInt(o, "frames", 60 * 60 * 40),
                Trace = sink,
            };
            MatchRunner.Run(tables, cpu, setup, out GameState final, out Replay unused);
            string path = Program.Opt(o, "out", Path.Combine(Paths.Artifacts(), "trace.json"));
            File.WriteAllText(path, sink.ToJson(setup));
            Console.WriteLine("trace: " + sink.FrameCount + " frames -> " + path);
            return 0;
        }
    }

    sealed class JsonTrace : ITraceSink
    {
        readonly SimTables tables;
        readonly StringBuilder frames = new StringBuilder();
        readonly StringBuilder events = new StringBuilder();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public int FrameCount;

        public JsonTrace(SimTables tables)
        {
            this.tables = tables;
        }

        static string F(Fix64 v) => v.ToString(3);

        public void Frame(in GameState s, SimEventBuffer ev)
        {
            if (FrameCount > 0) frames.Append(',');
            FrameCount++;
            FighterState a = s.F0, b = s.F1;
            frames.Append('[').Append(s.World).Append(',').Append((int)s.Phase).Append(',')
                .Append(F(s.Ball.Pos.X)).Append(',').Append(F(s.Ball.Pos.Y)).Append(',').Append((int)s.Ball.Mode).Append(',')
                .Append(F(a.Pos.X)).Append(',').Append(F(a.Pos.Y)).Append(',').Append((int)a.Action).Append(',').Append((int)a.Jump).Append(',')
                .Append(F(b.Pos.X)).Append(',').Append(F(b.Pos.Y)).Append(',').Append((int)b.Action).Append(',').Append((int)b.Jump).Append(',')
                .Append(s.Score0).Append(',').Append(s.Score1).Append(',').Append(s.SetsWon0).Append(',').Append(s.SetsWon1).Append(',')
                .Append(a.Surge).Append(',').Append(b.Surge).Append(',').Append(a.Stamina.RoundToInt()).Append(',').Append(b.Stamina.RoundToInt()).Append(',')
                .Append(s.Server).Append(',').Append(s.HitStop).Append(',')
                .Append((int)a.Ring).Append(',').Append(a.RingCenter - s.World).Append(',').Append((int)b.Ring).Append(',').Append(b.RingCenter - s.World).Append(',')
                .Append(F(a.RingPoint.X)).Append(',').Append(F(a.RingPoint.Y)).Append(',').Append(F(b.RingPoint.X)).Append(',').Append(F(b.RingPoint.Y)).Append(',')
                .Append((int)a.CommitKind).Append(',').Append((int)b.CommitKind).Append(',').Append(a.Charging ? 1 : 0).Append(',').Append(b.Charging ? 1 : 0).Append(',')
                .Append(a.RingPerfect).Append(',').Append(a.RingGood).Append(',').Append(b.RingPerfect).Append(',').Append(b.RingGood)
                .Append(']');
            for (int i = 0; i < ev.Count; i++)
            {
                ref readonly SimEvent e = ref ev[i];
                if (events.Length > 0) events.Append(',');
                events.Append('[').Append(FrameCount - 1).Append(',').Append(e.Type).Append(',').Append(e.Actor).Append(',').Append(e.A).Append(',').Append(e.B)
                    .Append(',').Append(F(e.X)).Append(',').Append(F(e.Y)).Append(']');
            }
        }

        public string ToJson(MatchSetup setup)
        {
            var sb = new StringBuilder();
            FighterDefinition f0 = tables.Fighters[setup.Fighter0], f1 = tables.Fighters[setup.Fighter1];
            sb.Append("{\"fighters\":[");
            Fighter(sb, f0);
            sb.Append(',');
            Fighter(sb, f1);
            sb.Append("],\"court\":\"").Append(tables.Courts[setup.Court].Name).Append("\",");
            sb.Append("\"net\":").Append(F(tables.Tuning.Court.NetHeight)).Append(",\"half\":").Append(F(tables.Tuning.Court.HalfLength)).Append(',');
            sb.Append("\"frame\":[\"world\",\"phase\",\"bx\",\"by\",\"bmode\",\"ax\",\"ay\",\"aact\",\"ajump\",\"cx\",\"cy\",\"cact\",\"cjump\",\"s0\",\"s1\",\"sets0\",\"sets1\",\"surge0\",\"surge1\",\"stam0\",\"stam1\",\"server\",\"hitstop\",\"ring0\",\"ringIn0\",\"ring1\",\"ringIn1\",\"rx0\",\"ry0\",\"rx1\",\"ry1\",\"commit0\",\"commit1\",\"charge0\",\"charge1\",\"perfect0\",\"good0\",\"perfect1\",\"good1\"],");
            sb.Append("\"event\":[\"frame\",\"type\",\"actor\",\"a\",\"b\",\"x\",\"y\"],");
            sb.Append("\"eventTypes\":[");
            string[] names = Enum.GetNames(typeof(VolleyEvent));
            for (int i = 0; i < names.Length; i++) sb.Append(i == 0 ? "" : ",").Append('"').Append(names[i]).Append('"');
            sb.Append("],\"callouts\":[");
            for (int i = 0; i < (int)Callout.Count; i++)
            {
                ImpactRecipe r = tables.Tuning.Impacts.For((Callout)i);
                sb.Append(i == 0 ? "" : ",").Append('"').Append(r != null ? r.CalloutText : "").Append('"');
            }
            sb.Append(']');
            Names(sb, "actions", typeof(FighterAction));
            Names(sb, "jumps", typeof(JumpKind));
            Names(sb, "touches", typeof(TouchKind));
            Names(sb, "phases", typeof(MatchPhase));
            sb.Append(",\"frames\":[").Append(frames).Append("],\"events\":[").Append(events).Append("]}");
            return sb.ToString();
        }

        static void Names(StringBuilder sb, string key, Type enumType)
        {
            sb.Append(",\"").Append(key).Append("\":[");
            string[] names = Enum.GetNames(enumType);
            for (int i = 0; i < names.Length; i++) sb.Append(i == 0 ? "" : ",").Append('"').Append(names[i]).Append('"');
            sb.Append(']');
        }

        static void Fighter(StringBuilder sb, FighterDefinition f)
        {
            sb.Append("{\"id\":\"").Append(f.Id).Append("\",\"name\":\"").Append(f.ShortName).Append("\",\"height\":").Append(f.Height.ToString(2))
              .Append(",\"reach\":").Append(f.StandingReach.ToString(2)).Append(",\"color\":\"").Append(f.IdentityColor).Append("\"}");
        }
    }
}

namespace VolleyDuel.BalanceLab
{
    /// <summary>Human-readable event log for the first N rallies (debugging the sim by eye).</summary>
    sealed class EventLog : ITraceSink
    {
        readonly SimTables tables;
        readonly int rallies;
        int points;

        public EventLog(SimTables tables, int rallies)
        {
            this.tables = tables;
            this.rallies = rallies;
        }

        public void Frame(in GameState s, SimEventBuffer ev)
        {
            if (points >= rallies) return;
            for (int i = 0; i < ev.Count; i++)
            {
                ref readonly SimEvent e = ref ev[i];
                var type = (VolleyEvent)e.Type;
                if (type == VolleyEvent.SurgeGain || type == VolleyEvent.HitStop) continue;
                string detail = e.A + "," + e.B;
                if (type == VolleyEvent.Contact || type == VolleyEvent.Commit || type == VolleyEvent.Whiff) detail = (TouchKind)e.A + " " + (TimingGrade)(e.B & 0xF) + (type == VolleyEvent.Contact ? " " + (ContactBand)((e.B >> 4) & 0xF) : "");
                else if (type == VolleyEvent.Point) detail = (PointReason)e.A + " -> " + s.Score0 + "-" + s.Score1;
                else if (type == VolleyEvent.Fault) detail = ((FaultKind)e.A).ToString();
                else if (type == VolleyEvent.Callout) detail = ((Callout)e.A).ToString();
                else if (type == VolleyEvent.Jump || type == VolleyEvent.Takeoff) detail = ((JumpKind)e.A).ToString();
                else if (type == VolleyEvent.BlockTouch) detail = (BlockOutcome)e.A + " dt=" + e.B;
                Console.WriteLine("  w{0,6} {1,-12} P{2} {3,-22} @({4},{5})  ball({6},{7}) v({8},{9})", s.World, type, e.Actor == 255 ? "-" : e.Actor.ToString(), detail, e.X.ToString(2), e.Y.ToString(2),
                    s.Ball.Pos.X.ToString(2), s.Ball.Pos.Y.ToString(2), s.Ball.Vel.X.ToString(1), s.Ball.Vel.Y.ToString(1));
                if (type == VolleyEvent.Point)
                {
                    points++;
                    Console.WriteLine("  ---- P0 x={0} {1}   P1 x={2} {3}", s.F0.Pos.X.ToString(2), s.F0.Action, s.F1.Pos.X.ToString(2), s.F1.Action);
                }
            }
        }
    }
}
