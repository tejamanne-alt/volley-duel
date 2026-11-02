using System;
using System.Collections.Generic;
using Duel.Core.AI;
using Duel.Core.Sim;
using Duel.Core.Sim.Data;
using VolleyDuel.Sim;

namespace VolleyDuel.AI
{
    /// <summary>Per-fighter CPU play style (spec: Rafa jump-serves and hammers, Defne commits to blocks, Rin digs and counters, Arjun feints and dumps).</summary>
    public sealed class Personality
    {
        public readonly Fix64 JumpServe, Block, FakeBlock, Tip, Roll, QuickSet, Dump, ShortAim, DeepAim, Trick, DigDepth, ReceiveDepth;

        public Personality(JsonNode j)
        {
            JumpServe = j.Fix("jumpServe");
            Block = j.Fix("block");
            FakeBlock = j.Fix("fakeBlock");
            Tip = j.Fix("tip");
            Roll = j.Fix("roll");
            QuickSet = j.Fix("quickSet");
            Dump = j.Fix("dump");
            ShortAim = j.Fix("shortAim");
            DeepAim = j.Fix("deepAim");
            Trick = j.Fix("trick");
            DigDepth = j.Fix("digDepth");
            ReceiveDepth = j.Fix("receiveDepth");
        }
    }

    public sealed class SkillTuning
    {
        public readonly Fix64 ArriveTolerance, StickGain, BaseDefenseDepth, BlockSpot, ShortDigDepth, DeepDigDepth, JumpServeBackoff, BlockTimingScale, DenialTimingScale, AnticipationScale;
        public readonly int ServeDelayMin, ServeDelayMax, StanceLead, TempoVariation;

        public SkillTuning(JsonNode j)
        {
            ArriveTolerance = j.Fix("arriveTolerance");
            StickGain = j.Fix("stickGain");
            BaseDefenseDepth = j.Fix("baseDefenseDepth");
            BlockSpot = j.Fix("blockSpot");
            ShortDigDepth = j.Fix("shortDigDepth");
            DeepDigDepth = j.Fix("deepDigDepth");
            JumpServeBackoff = j.Fix("jumpServeBackoff");
            ServeDelayMin = j.Int("serveDelayMin");
            ServeDelayMax = j.Int("serveDelayMax");
            StanceLead = j.Int("stanceLead");
            TempoVariation = j.Int("tempoVariation");
            BlockTimingScale = j.Fix("blockTimingScale");
            DenialTimingScale = j.Fix("denialTimingScale");
            AnticipationScale = j.Fix("anticipationScale");
        }
    }

    /// <summary>CPU tables baked from cpu.json.</summary>
    public sealed class CpuTables
    {
        public readonly TierProfile[] Tiers;
        public readonly Personality[] Personalities;
        public readonly Fix64 HabitDecay, HabitMinimumEvidence, RivalStep, RivalStartBlend;
        public readonly SkillTuning Skill;

        public CpuTables(SimTables sim)
        {
            JsonNode j = sim.CpuData ?? throw new InvalidOperationException("manifest has no cpu data");
            var tiers = new List<TierProfile>();
            foreach (JsonNode t in j["tiers"].Items)
            {
                tiers.Add(new TierProfile
                {
                    Name = t.Str("name"),
                    ReactionFrames = t.Int("reaction"),
                    LandingReadError = t.Fix("landingRead"),
                    ExecutionSigma = t.Fix("execution"),
                    ReadsYou = t.Fix("readsYou"),
                    LearnsHabits = t.Bool("learns", false),
                    Surge = (SurgePolicy)Enum.Parse(typeof(SurgePolicy), t.Str("surge")),
                });
            }
            Tiers = tiers.ToArray();
            JsonNode p = j["personalities"];
            Personalities = new Personality[sim.Fighters.Length];
            for (int i = 0; i < sim.Fighters.Length; i++) Personalities[i] = new Personality(p[sim.Fighters[i].Id]);
            HabitDecay = j["habits"].Fix("decay");
            HabitMinimumEvidence = j["habits"].Fix("minimumEvidence");
            RivalStep = j["rival"].Fix("step");
            RivalStartBlend = j["rival"].Fix("startBlend");
            Skill = new SkillTuning(j["skill"]);
        }

        public int TierIndex(string name)
        {
            for (int i = 0; i < Tiers.Length; i++)
            {
                if (string.Equals(Tiers[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            throw new ArgumentException("Unknown CPU tier '" + name + "'");
        }
    }
}
