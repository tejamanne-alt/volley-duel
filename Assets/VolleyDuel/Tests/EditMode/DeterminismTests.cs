using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Duel.Core.Sim;
using NUnit.Framework;
using VolleyDuel.AI;
using VolleyDuel.Sim;

namespace VolleyDuel.Tests
{
    public class DeterminismTests
    {
        static List<ulong> RunCpuMatch(uint seed, int frames, out GameState final)
        {
            SimTables tables = Rig.LoadTables();
            var cpu = new CpuTables(tables);
            var sim = new VolleySim(tables, MatchConfig.Create(0, 3, 2, seed));
            var c0 = new VolleyCpu(sim, cpu, 0, cpu.Tiers[4], seed);
            var c1 = new VolleyCpu(sim, cpu, 1, cpu.Tiers[2], seed + 1);
            GameState s = sim.CreateInitialState();
            var events = new SimEventBuffer();
            var hashes = new List<ulong>();
            for (int f = 0; f < frames; f++)
            {
                FrameInput a = c0.Decide(s), b = c1.Decide(s);
                events.Clear();
                s = sim.Step(s, a, b, events);
                hashes.Add(sim.Hash(s));
            }
            final = s;
            return hashes;
        }

        [Test]
        public void SameSeedSameMatchFrameByFrame()
        {
            List<ulong> a = RunCpuMatch(42, 3000, out GameState fa);
            List<ulong> b = RunCpuMatch(42, 3000, out GameState fb);
            CollectionAssert.AreEqual(a, b);
            Assert.Greater(fa.RallyIndex + fa.Score0 + fa.Score1, 0, "the match actually progressed");
        }

        [Test]
        public void DifferentSeedsDiverge()
        {
            List<ulong> a = RunCpuMatch(1, 1500, out GameState fa);
            List<ulong> b = RunCpuMatch(2, 1500, out GameState fb);
            Assert.AreNotEqual(a[a.Count - 1], b[b.Count - 1]);
        }

        [Test]
        public void RestoringASnapshotAndResimulatingReproducesTheFuture()
        {
            SimTables tables = Rig.LoadTables();
            var cpu = new CpuTables(tables);
            var sim = new VolleySim(tables, MatchConfig.Create(1, 2, 0, 9));
            var c0 = new VolleyCpu(sim, cpu, 0, cpu.Tiers[4], 3);
            var c1 = new VolleyCpu(sim, cpu, 1, cpu.Tiers[4], 4);
            GameState s = sim.CreateInitialState();
            var events = new SimEventBuffer();
            var inputs = new List<(FrameInput, FrameInput)>();
            var snapshots = new List<byte[]>();
            var buffer = new byte[GameState.MaxBytes];
            for (int f = 0; f < 2000; f++)
            {
                int n = sim.Serialize(s, buffer);
                snapshots.Add(buffer.AsSpan(0, n).ToArray());
                FrameInput a = c0.Decide(s), b = c1.Decide(s);
                inputs.Add((a, b));
                events.Clear();
                s = sim.Step(s, a, b, events);
            }
            ulong expected = sim.Hash(s);
            // Roll back to several points and resimulate, as the netcode does.
            foreach (int from in new[] { 0, 333, 1000, 1992 })
            {
                GameState r = sim.Deserialize(snapshots[from]);
                for (int f = from; f < inputs.Count; f++)
                {
                    events.Clear();
                    r = sim.Step(r, inputs[f].Item1, inputs[f].Item2, events);
                }
                Assert.AreEqual(expected, sim.Hash(r), "resimulated from frame " + from);
            }
        }

        [Test]
        public void SerializedStateFitsTheTwoKilobyteBudgetAndRoundTrips()
        {
            RunCpuMatch(5, 1200, out GameState s);
            var buffer = new byte[GameState.MaxBytes];
            int n = s.Write(buffer);
            Assert.LessOrEqual(n, 2048, "GameState is at most 2 KB");
            GameState back = GameState.Read(buffer.AsSpan(0, n));
            var again = new byte[GameState.MaxBytes];
            int m = back.Write(again);
            Assert.AreEqual(n, m);
            CollectionAssert.AreEqual(buffer.AsSpan(0, n).ToArray(), again.AsSpan(0, m).ToArray());
        }

        /// <summary>Every field must be serialized: mutate each one and the hash has to change.</summary>
        [Test]
        public void EveryStateFieldAffectsTheHash()
        {
            var rig = new Rig();
            GameState baseState = rig.S;
            ulong baseHash = rig.Sim.Hash(baseState);
            var missed = new List<string>();
            object boxed = baseState;
            Walk(typeof(GameState), "", (path, setter) =>
            {
                object copy = baseState;
                setter(copy);
                ulong h = rig.Sim.Hash((GameState)copy);
                if (h == baseHash) missed.Add(path);
            });
            Assert.IsEmpty(missed, "fields not covered by GameState.Write: " + string.Join(", ", missed));
        }

        delegate void Visit(string path, Action<object> mutate);

        static void Walk(Type type, string prefix, Visit visit)
        {
            foreach (FieldInfo f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                string path = prefix + f.Name;
                Type ft = f.FieldType;
                if (IsLeaf(ft))
                {
                    FieldInfo field = f;
                    visit(path, root => MutateLeaf(root, path));
                }
                else if (ft.IsValueType)
                {
                    Walk(ft, path + ".", visit);
                }
            }
        }

        static bool IsLeaf(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(Fix64);

        /// <summary>Follows a dotted path through boxed structs and changes the leaf value.</summary>
        static void MutateLeaf(object root, string path)
        {
            string[] parts = path.Split('.');
            var chain = new List<(object box, FieldInfo field)>();
            object current = root;
            for (int i = 0; i < parts.Length; i++)
            {
                FieldInfo fi = current.GetType().GetField(parts[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                chain.Add((current, fi));
                if (i < parts.Length - 1) current = fi.GetValue(current);
            }
            (object leafBox, FieldInfo leaf) = chain[chain.Count - 1];
            leaf.SetValue(leafBox, Changed(leaf.GetValue(leafBox)));
            for (int i = chain.Count - 2; i >= 0; i--) chain[i].field.SetValue(chain[i].box, chain[i + 1].box);
        }

        static object Changed(object v)
        {
            switch (v)
            {
                case bool b: return !b;
                case byte b: return (byte)(b ^ 1);
                case sbyte b: return (sbyte)(b ^ 1);
                case short b: return (short)(b ^ 1);
                case ushort b: return (ushort)(b ^ 1);
                case int b: return b ^ 1;
                case uint b: return b ^ 1u;
                case long b: return b ^ 1L;
                case ulong b: return b ^ 1UL;
                case Fix64 f: return Fix64.FromRaw(f.Raw ^ 1L);
                default:
                    if (v.GetType().IsEnum)
                    {
                        long raw = Convert.ToInt64(v);
                        return Enum.ToObject(v.GetType(), raw ^ 1L);
                    }
                    throw new NotSupportedException(v.GetType().Name);
            }
        }

        /// <summary>
        /// Gate: a recorded 10,000-frame CPU match (ball flight included) must hash identically everywhere:
        /// here, in the Unity editor, on an iPhone and on an Android phone.
        /// </summary>
        [Test]
        public void GoldenReplayHashesIdentically()
        {
            string path = GoldenPath();
            if (!File.Exists(path)) Assert.Inconclusive("Golden replay missing: run `dotnet run --project Headless/VolleyDuel.BalanceLab -- golden --write`");
            Replay replay = Replay.FromBytes(File.ReadAllBytes(path));
            SimTables tables = Rig.LoadTables();
            if (replay.SimVersion != SimVersion.Compute(tables))
            {
                Assert.Fail("Golden replay was recorded with another sim version. If the sim change is intended, re-record: `dotnet run --project Headless/VolleyDuel.BalanceLab -- golden --write`");
            }
            var sim = new VolleySim(tables, MatchConfig.FromBytes(replay.Config));
            GameState final = replay.Play(sim, sim.CreateInitialState());
            Assert.AreEqual(10000, replay.FrameCount);
            Assert.AreEqual(replay.FinalFrame, final.Tick);
            Assert.AreEqual(replay.FinalHash, sim.Hash(final));
        }

        static string GoldenPath()
        {
#if UNITY_5_3_OR_NEWER
            return Path.Combine(UnityEngine.Application.dataPath, "VolleyDuel", "Tests", "EditMode", "Golden", "golden-10000.replay");
#else
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "CLAUDE.md"))) dir = Path.GetDirectoryName(dir);
            return Path.Combine(dir ?? ".", "Assets", "VolleyDuel", "Tests", "EditMode", "Golden", "golden-10000.replay");
#endif
        }
    }

    public class VisibilityTests
    {
        [Test]
        public void FutureRandomnessAndPendingBlocksAreHidden()
        {
            var rig = new Rig();
            rig.S.Ball.Nudge1 = Fix64.One;
            rig.S.Ball.PendingBlock = BlockOutcome.Stuff;
            GameState v = Visibility.ForViewer(rig.S, 0);
            Assert.AreEqual(0UL, v.MatchRng.State);
            Assert.AreEqual(Fix64.Zero, v.Ball.Nudge1);
            Assert.AreEqual(BlockOutcome.None, v.Ball.PendingBlock);
        }

        [Test]
        public void ATipLooksLikeASpikeUntilItLocks()
        {
            var rig = new Rig();
            rig.S.World = 100;
            ref FighterState o = ref rig.F(1);
            o.CommitKind = TouchKind.Tip;
            o.Choice = AttackChoice.Tip;
            o.CommitLock = 105;
            GameState v = Visibility.ForViewer(rig.S, 0);
            Assert.AreEqual(TouchKind.Spike, v.F1.CommitKind);
            Assert.AreEqual(AttackChoice.Spike, v.F1.Choice);
            rig.S.World = 105;
            v = Visibility.ForViewer(rig.S, 0);
            Assert.AreEqual(TouchKind.Tip, v.F1.CommitKind, "visible once locked");
            GameState own = Visibility.ForViewer(rig.S, 1);
            Assert.AreEqual(TouchKind.Tip, own.F1.CommitKind, "you always know your own choice");
        }

        [Test]
        public void AFakeHopReadsAsABlockAndOpponentInputsAreHidden()
        {
            var rig = new Rig();
            rig.F(1).Jump = JumpKind.FakeHop;
            rig.F(1).Input.Record(FrameInput.Create(3, 0, InputButtons.Hit), 5);
            rig.F(1).Bait = FeintKind.FakeBlock;
            GameState v = Visibility.ForViewer(rig.S, 0);
            Assert.AreEqual(JumpKind.Block, v.F1.Jump);
            Assert.AreEqual(FeintKind.None, v.F1.Bait);
            Assert.AreEqual(0, v.F1.Input.Held);
        }
    }
}

namespace VolleyDuel.Tests
{
    public class RivalModeTests
    {
        [Test]
        public void RivalTightensWhenThePlayerWinsAndEasesWhenTheyLose()
        {
            var cpu = new CpuTables(Rig.LoadTables());
            var rival = new RivalMode(cpu, 2);
            Fix64 start = rival.Skill;
            rival.AfterSet(4);
            Assert.Greater(rival.Skill, start, "player won the set: the CPU gets sharper");
            int fasterReaction = rival.Current.ReactionFrames;
            rival.AfterSet(-4);
            rival.AfterSet(-4);
            Assert.Less(rival.Skill, start, "player lost: the CPU eases off");
            Assert.Greater(rival.Current.ReactionFrames, fasterReaction);
        }

        [Test]
        public void CpuTiersFollowTheSpecTable()
        {
            var cpu = new CpuTables(Rig.LoadTables());
            int[] reaction = { 30, 24, 18, 14, 12 };
            string[] read = { "1.20", "0.80", "0.50", "0.30", "0.15" };
            string[] readsYou = { "0.15", "0.30", "0.45", "0.60", "0.70" };
            bool[] learns = { false, false, true, true, true };
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(reaction[i], cpu.Tiers[i].ReactionFrames, cpu.Tiers[i].Name);
                Assert.AreEqual(read[i], cpu.Tiers[i].LandingReadError.ToString(2));
                Assert.AreEqual(readsYou[i], cpu.Tiers[i].ReadsYou.ToString(2));
                Assert.AreEqual(learns[i], cpu.Tiers[i].LearnsHabits);
            }
            Assert.GreaterOrEqual(cpu.Tiers[4].ReactionFrames, 12, "Legend never reacts faster than 12 frames");
        }
    }
}
