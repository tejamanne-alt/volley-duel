using System;
using System.Text;
using Duel.Core.AI;
using Duel.Core.Sim;
using VolleyDuel.AI;
using VolleyDuel.Sim;

namespace VolleyDuel.BalanceLab
{
    /// <summary>Counters gathered from the event stream of one or many matches.</summary>
    public sealed class MatchStats
    {
        public int Matches, Wins0, Frames, WorldFrames, Rallies, ReceiverWins, Sets;
        public int Kills, Aces, Stuffs, Tooled, Errors, Faults, Finishers, FinisherLanded, Denials, Jousts, ReadTips, Bites, FloorSaves, PerfectDigs;
        public int Contacts, Perfect, Good, Late, Controlled, Loose, Shank, Miss, Whiffs;
        public int RallyFrames, LongestRally;
        public readonly int[] FaultKinds = new int[8];
        public readonly int[] TouchKinds = new int[20];
        public readonly int[] PointReasons = new int[16];
        /// <summary>Points won per player (0/1) by reason: index player * 16 + reason.</summary>
        public readonly int[] WonBy = new int[32];
        /// <summary>Points lost per player to their own errors/faults: index player.</summary>
        public readonly int[] LostToOwnError = new int[2];
        /// <summary>Own errors per player by the touch that caused them: index player * 20 + TouchKind, +1 when out.</summary>
        public readonly int[] ErrorTouch = new int[80];

        public void Add(MatchStats o)
        {
            Matches += o.Matches; Wins0 += o.Wins0; Frames += o.Frames; WorldFrames += o.WorldFrames; Rallies += o.Rallies;
            ReceiverWins += o.ReceiverWins; Sets += o.Sets; Kills += o.Kills; Aces += o.Aces; Stuffs += o.Stuffs; Tooled += o.Tooled;
            Errors += o.Errors; Faults += o.Faults; Finishers += o.Finishers; FinisherLanded += o.FinisherLanded; Denials += o.Denials;
            Jousts += o.Jousts; ReadTips += o.ReadTips; Bites += o.Bites; FloorSaves += o.FloorSaves; PerfectDigs += o.PerfectDigs;
            Contacts += o.Contacts; Perfect += o.Perfect; Good += o.Good; Late += o.Late; Controlled += o.Controlled; Loose += o.Loose;
            Shank += o.Shank; Miss += o.Miss; Whiffs += o.Whiffs; RallyFrames += o.RallyFrames;
            if (o.LongestRally > LongestRally) LongestRally = o.LongestRally;
            for (int i = 0; i < FaultKinds.Length; i++) FaultKinds[i] += o.FaultKinds[i];
            for (int i = 0; i < TouchKinds.Length; i++) TouchKinds[i] += o.TouchKinds[i];
            for (int i = 0; i < PointReasons.Length; i++) PointReasons[i] += o.PointReasons[i];
            for (int i = 0; i < ErrorTouch.Length; i++) ErrorTouch[i] += o.ErrorTouch[i];
            for (int i = 0; i < WonBy.Length; i++) WonBy[i] += o.WonBy[i];
            LostToOwnError[0] += o.LostToOwnError[0];
            LostToOwnError[1] += o.LostToOwnError[1];
        }

        public double ReceiverRate => Rallies == 0 ? 0 : (double)ReceiverWins / Rallies;
        public double MinutesPerMatch => Matches == 0 ? 0 : Frames / 60.0 / 60.0 / Matches;
        public double SecondsPerRally => Rallies == 0 ? 0 : RallyFrames / 60.0 / Rallies;

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("matches {0}  p0 wins {1:P1}  sets {2}  rallies {3}  receiver wins {4:P1}\n", Matches, Matches == 0 ? 0 : (double)Wins0 / Matches, Sets, Rallies, ReceiverRate);
            sb.AppendFormat("match {0:F1} min  rally {1:F1} s (longest {2:F1} s)\n", MinutesPerMatch, SecondsPerRally, LongestRally / 60.0);
            sb.AppendFormat("points: kill {0} ace {1} stuff {2} tooled {3} error {4} fault {5} finisher {6}/{7} denied {8} joust {9}\n",
                Kills, Aces, Stuffs, Tooled, Errors, Faults, FinisherLanded, Finishers, Denials, Jousts);
            sb.AppendFormat("faults: out {0} net {1} 4hits {2} center {3} foot {4}\n", FaultKinds[(int)FaultKind.Out], FaultKinds[(int)FaultKind.NetTouch], FaultKinds[(int)FaultKind.FourHits], FaultKinds[(int)FaultKind.CenterLine], FaultKinds[(int)FaultKind.FootFault]);
            sb.AppendFormat("contacts {0}: perfect {1:P0} good {2:P0} late {3:P0} | controlled {4:P0} loose {5:P0} shank {6:P0} miss {7:P0} | whiffs {8}\n",
                Contacts, Frac(Perfect), Frac(Good), Frac(Late), Frac(Controlled), Frac(Loose), Frac(Shank), Frac(Miss), Whiffs);
            sb.Append("touches:");
            for (int i = 1; i < TouchKinds.Length; i++)
            {
                if (TouchKinds[i] > 0) sb.AppendFormat(" {0} {1}", (TouchKind)i, TouchKinds[i]);
            }
            sb.AppendFormat("\nhighlights: read tips {0} bites {1} floor saves {2} perfect digs {3}\n", ReadTips, Bites, FloorSaves, PerfectDigs);
            return sb.ToString();
        }

        double Frac(int n) => Contacts == 0 ? 0 : (double)n / Contacts;
    }

    public sealed class MatchSetup
    {
        public int Fighter0, Fighter1, Court;
        public uint Seed;
        public TierProfile Tier0, Tier1;
        public int SetsToWin = 2;
        public int FrameCap = 60 * 60 * 40;
        public bool RecordReplay;
        public ITraceSink Trace;
    }

    public interface ITraceSink
    {
        void Frame(in GameState state, SimEventBuffer events);
    }

    /// <summary>Runs one CPU-vs-CPU match through the real sim, exactly as a device would.</summary>
    public static class MatchRunner
    {
        public static MatchStats Run(SimTables tables, CpuTables cpu, MatchSetup setup, out GameState final, out Replay replay)
        {
            var config = MatchConfig.Create(setup.Fighter0, setup.Fighter1, setup.Court, setup.Seed, setup.SetsToWin);
            var sim = new VolleySim(tables, config);
            GameState state = sim.CreateInitialState();
            var c0 = new VolleyCpu(sim, cpu, 0, setup.Tier0, setup.Seed * 2654435761UL + 1);
            var c1 = new VolleyCpu(sim, cpu, 1, setup.Tier1, setup.Seed * 2654435761UL + 2);
            var events = new SimEventBuffer();
            var stats = new MatchStats { Matches = 1 };
            replay = setup.RecordReplay ? new Replay { Config = config.ToBytes(), SimVersion = SimVersion.Compute(tables) } : null;
            int rallyServer = -1;
            int rallyStart = 0;
            while (state.Phase != MatchPhase.MatchOver && state.Tick < setup.FrameCap)
            {
                FrameInput i0 = c0.Decide(state);
                FrameInput i1 = c1.Decide(state);
                events.Clear();
                if (state.Phase == MatchPhase.PreServe && rallyServer < 0)
                {
                    rallyServer = state.Server;
                    rallyStart = state.World;
                }
                state = sim.Step(state, i0, i1, events);
                if (replay != null) replay.Add(i0, i1);
                setup.Trace?.Frame(state, events);
                for (int e = 0; e < events.Count; e++)
                {
                    ref readonly SimEvent ev = ref events[e];
                    switch ((VolleyEvent)ev.Type)
                    {
                        case VolleyEvent.Point:
                            stats.Rallies++;
                            stats.PointReasons[ev.A]++;
                            if (ev.Actor <= 1)
                            {
                                stats.WonBy[ev.Actor * 16 + ev.A]++;
                                if (ev.A == (int)PointReason.Error || ev.A == (int)PointReason.Fault)
                                {
                                    stats.LostToOwnError[1 - ev.Actor]++;
                                    bool outBall = Math.Abs(state.Ball.Pos.X.RoundToInt()) >= 6;
                                    stats.ErrorTouch[(1 - ev.Actor) * 40 + (int)state.LastTouch * 2 + (outBall ? 1 : 0)]++;
                                }
                            }
                            int len = state.World - rallyStart;
                            stats.RallyFrames += len;
                            if (len > stats.LongestRally) stats.LongestRally = len;
                            if (rallyServer >= 0 && ev.Actor != rallyServer) stats.ReceiverWins++;
                            rallyServer = -1;
                            switch ((PointReason)ev.A)
                            {
                                case PointReason.Kill: stats.Kills++; break;
                                case PointReason.Ace: stats.Aces++; break;
                                case PointReason.Stuff: stats.Stuffs++; break;
                                case PointReason.Tooled: stats.Tooled++; break;
                                case PointReason.Error: stats.Errors++; break;
                                case PointReason.Fault: stats.Faults++; break;
                                case PointReason.Joust: stats.Jousts++; break;
                            }
                            break;
                        case VolleyEvent.Fault:
                            stats.FaultKinds[ev.A]++;
                            break;
                        case VolleyEvent.BallLanded:
                            if (ev.A == 0) stats.FaultKinds[(int)FaultKind.Out]++;
                            break;
                        case VolleyEvent.Contact:
                            {
                                stats.Contacts++;
                                stats.TouchKinds[ev.A]++;
                                var grade = (TimingGrade)(ev.B & 0xF);
                                var band = (ContactBand)((ev.B >> 4) & 0xF);
                                if (grade == TimingGrade.Perfect) stats.Perfect++;
                                else if (grade == TimingGrade.Good) stats.Good++;
                                else stats.Late++;
                                if (band == ContactBand.Controlled) stats.Controlled++;
                                else if (band == ContactBand.Loose) stats.Loose++;
                                else if (band == ContactBand.Shank) stats.Shank++;
                                else stats.Miss++;
                                break;
                            }
                        case VolleyEvent.Whiff: stats.Whiffs++; break;
                        case VolleyEvent.SetWon: stats.Sets++; break;
                        case VolleyEvent.FinisherStart: stats.Finishers++; break;
                        case VolleyEvent.FinisherScored: stats.FinisherLanded++; break;
                        case VolleyEvent.Denied: stats.Denials++; break;
                        case VolleyEvent.Bite: stats.Bites++; break;
                        case VolleyEvent.Callout:
                            if (ev.A == (int)Callout.ReadIt) stats.ReadTips++;
                            else if (ev.A == (int)Callout.Pancake) stats.FloorSaves++;
                            else if (ev.A == (int)Callout.Dug) stats.PerfectDigs++;
                            break;
                    }
                }
            }
            stats.Frames = state.Tick;
            stats.WorldFrames = state.World;
            if (state.Winner == 0) stats.Wins0 = 1;
            if (replay != null)
            {
                replay.FinalFrame = state.Tick;
                replay.FinalHash = sim.Hash(state);
            }
            final = state;
            return stats;
        }
    }
}
