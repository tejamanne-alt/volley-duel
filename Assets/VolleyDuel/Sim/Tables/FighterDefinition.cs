using System;
using Duel.Core.Sim;
using Duel.Core.Sim.Data;

namespace VolleyDuel.Sim
{
    /// <summary>A fighter baked from fighters/*.json: attributes, traits, derived physical values and kit.</summary>
    public sealed class FighterDefinition
    {
        public readonly int Index;
        public readonly string Id, Name, ShortName, Nickname, Role, Origin, HomeCourt, IdentityColor, MasteryName, MasteryKind;
        public readonly Fix64 Height;
        public readonly int Power, Jump, Speed, Control, Dig, Block;

        // Traits (masteries and weaknesses expressed as data).
        public readonly bool HeavyBall, GoodBlocksStuff, GuaranteedFloorSave;
        public readonly Fix64 HeavyBallDigStamina, StaminaCostScale, BackpedalScale, BlockSpanScale, SetErrorScale;
        public readonly int ChargeFrames, LandingRecoveryExtra, DigPerfectBonus, LateLockExtra;

        // Derived physical values.
        public readonly Fix64 StandingReach, JumpHeight, BlockJumpHeight, WalkSpeed, DiveRange, SpikeSpeed, FloatServeSpeed, JumpServeSpeed;
        public readonly int PerfectDig, GoodDig, PerfectControl, GoodControl, PerfectPower, GoodPower, BlockPerfectHalf, BlockGoodHalf;

        public readonly TrickDefinition OffenseA, OffenseB, Defense;
        public readonly FinisherDefinition Finisher;

        public FighterDefinition(int index, JsonNode j, Tuning t)
        {
            Index = index;
            Id = j.Str("id");
            Name = j.Str("name");
            ShortName = j.Str("shortName");
            Nickname = j.Str("nickname");
            Role = j.Str("role", "");
            Origin = j.Str("origin", "");
            HomeCourt = j.Str("homeCourt", "");
            IdentityColor = j.Str("identityColor", "#FFFFFF");
            Height = j.Fix("height");
            JsonNode a = j["attributes"];
            Power = Attribute(a, "power");
            Jump = Attribute(a, "jump");
            Speed = Attribute(a, "speed");
            Control = Attribute(a, "control");
            Dig = Attribute(a, "dig");
            Block = Attribute(a, "block");
            JsonNode m = j["mastery"];
            MasteryName = m.Str("name");
            MasteryKind = m.Str("kind");

            JsonNode tr = j.Get("traits");
            HeavyBall = tr != null && tr.Bool("heavyBall", false);
            HeavyBallDigStamina = tr == null ? Fix64.Zero : tr.Fix("heavyBallDigStamina", Fix64.Zero);
            ChargeFrames = tr == null ? t.Attack.ChargeFrames : tr.Int("chargeFrames", t.Attack.ChargeFrames);
            LandingRecoveryExtra = tr == null ? 0 : tr.Int("landingRecoveryExtra", 0);
            StaminaCostScale = tr == null ? Fix64.One : tr.Fix("staminaCostScale", Fix64.One);
            BackpedalScale = tr == null ? t.Movement.BackpedalScale : tr.Fix("backpedalScale", t.Movement.BackpedalScale);
            BlockSpanScale = tr == null ? Fix64.One : tr.Fix("blockSpanScale", Fix64.One);
            GoodBlocksStuff = tr != null && tr.Bool("goodBlocksStuff", false);
            DigPerfectBonus = tr == null ? 0 : tr.Int("digPerfectBonus", 0);
            GuaranteedFloorSave = tr != null && tr.Bool("guaranteedFloorSave", false);
            LateLockExtra = tr == null ? 0 : tr.Int("lateLockExtra", 0);
            SetErrorScale = tr == null ? Fix64.One : tr.Fix("setErrorScale", Fix64.One);

            StandingReach = Height * t.Reach.StandingReachRatio;
            JumpHeight = t.Jump.HeightBase + t.Jump.HeightPerPoint * (Jump - 5);
            BlockJumpHeight = JumpHeight * t.Jump.BlockHeightScale;
            WalkSpeed = t.Movement.WalkSpeedBase + t.Movement.WalkSpeedPerPoint * (Speed - 5);
            DiveRange = t.Dive.RangeBase + t.Dive.RangePerDigPoint * (Dig - 5);
            SpikeSpeed = t.Attack.SpikeBaseSpeed + t.Attack.SpikeSpeedPerPower * (Power - 5);
            FloatServeSpeed = t.Serve.FloatSpeedBase + t.Serve.FloatSpeedPerPower * (Power - 5);
            JumpServeSpeed = t.Serve.JumpSpeedBase + t.Serve.JumpSpeedPerPower * (Power - 5);

            PerfectDig = t.Rings.PerfectWidth(Dig) + DigPerfectBonus;
            GoodDig = Math.Max(t.Rings.GoodWidth(Dig), PerfectDig);
            PerfectControl = t.Rings.PerfectWidth(Control);
            GoodControl = t.Rings.GoodWidth(Control);
            PerfectPower = t.Rings.PerfectWidth(Power);
            GoodPower = t.Rings.GoodWidth(Power);
            // Block ring: spec centers it on the hitter's contact with Perfect ±2 and Good ±5 at Block 5,
            // widening by one frame per 4 points (Perfect) and per 2 points (Good) like the contact rings.
            BlockPerfectHalf = Math.Max(1, t.Block.PerfectHalf + (Block - 5) / 4);
            BlockGoodHalf = Math.Max(BlockPerfectHalf + 1, t.Block.GoodHalf + (Block - 5) / 2);

            JsonNode tricks = j["tricks"];
            OffenseA = new TrickDefinition(TrickSlot.OffenseA, tricks["offenseA"], null);
            OffenseB = new TrickDefinition(TrickSlot.OffenseB, tricks["offenseB"], null);
            Defense = new TrickDefinition(TrickSlot.Defense, tricks["defense"], null);
            Finisher = new FinisherDefinition(j["finisher"]);

            int total = Power + Jump + Speed + Control + Dig + Block;
            if (total != 36) throw j.Fail("attributes must total 36 (budget rule), got " + total);
        }

        static int Attribute(JsonNode a, string key)
        {
            int v = a.Int(key);
            if (v < 1 || v > 10) throw a[key].Fail("must be 1-10");
            return v;
        }

        /// <summary>The trick of the given kind in this fighter's kit, or null.</summary>
        public TrickDefinition Trick(TrickKind kind)
        {
            if (OffenseA.Kind == kind) return OffenseA;
            if (OffenseB.Kind == kind) return OffenseB;
            if (Defense.Kind == kind) return Defense;
            return null;
        }

        public bool Has(TrickKind kind) => Trick(kind) != null;
    }

    /// <summary>A Signature Trick: frame data, flags and parameters. <see cref="Amped"/> is fully resolved.</summary>
    public sealed class TrickDefinition
    {
        public readonly TrickKind Kind;
        public readonly TrickSlot Slot;
        public readonly bool IsAmped;
        public readonly int Startup, Recovery, TimingShift, HangFrames, KnockbackFrames, ReadCut, DecoyFrames, RingShrink;
        public readonly MoveFlags Flags;
        public readonly Fix64 SpeedScale, MinFromNet, MaxFromNet, TargetFromNet, Drift, DriftScale, Range, DipScale, StaminaDrain;
        public readonly TrickDefinition Amped;

        public TrickDefinition(TrickSlot slot, JsonNode j, TrickDefinition baseline)
        {
            Slot = slot;
            IsAmped = baseline != null;
            if (baseline == null)
            {
                Kind = ParseEnum<TrickKind>(j["kind"]);
                Startup = j.Int("startup");
                Recovery = j.Int("recovery");
                TimingShift = j.Int("timingShift", 0);
                HangFrames = j.Int("hangFrames", 0);
                KnockbackFrames = j.Int("knockbackFrames", 0);
                ReadCut = j.Int("readCut", 0);
                DecoyFrames = j.Int("decoyFrames", 0);
                RingShrink = j.Int("ringShrink", 0);
                Flags = ParseFlags(j.Get("flags"));
                SpeedScale = j.Fix("speedScale", Fix64.One);
                MinFromNet = j.Fix("minFromNet", Fix64.Zero);
                MaxFromNet = j.Fix("maxFromNet", Fix64.Zero);
                TargetFromNet = j.Fix("targetFromNet", Fix64.Zero);
                Drift = j.Fix("drift", Fix64.Zero);
                DriftScale = j.Fix("driftScale", Fix64.One);
                Range = j.Fix("range", Fix64.Zero);
                DipScale = j.Fix("dipScale", Fix64.One);
                StaminaDrain = j.Fix("staminaDrain", Fix64.Zero);
                JsonNode amped = j.Get("amped");
                Amped = new TrickDefinition(slot, amped ?? JsonNode.Parse("{}"), this);
            }
            else
            {
                // Amped: spec says -3 frames start-up plus one added property. Unspecified fields inherit.
                Kind = baseline.Kind;
                Startup = Math.Max(1, baseline.Startup + j.Int("startupDelta", -3));
                Recovery = j.Int("recovery", baseline.Recovery);
                TimingShift = j.Int("timingShift", baseline.TimingShift);
                HangFrames = j.Int("hangFrames", baseline.HangFrames);
                KnockbackFrames = j.Int("knockbackFrames", baseline.KnockbackFrames);
                ReadCut = j.Int("readCut", baseline.ReadCut);
                DecoyFrames = j.Int("decoyFrames", baseline.DecoyFrames);
                RingShrink = j.Int("ringShrink", baseline.RingShrink);
                Flags = baseline.Flags | ParseFlags(j.Get("flags"));
                SpeedScale = j.Fix("speedScale", baseline.SpeedScale);
                MinFromNet = j.Fix("minFromNet", baseline.MinFromNet);
                MaxFromNet = j.Fix("maxFromNet", baseline.MaxFromNet);
                TargetFromNet = j.Fix("targetFromNet", baseline.TargetFromNet);
                Drift = j.Fix("drift", baseline.Drift);
                DriftScale = j.Fix("driftScale", baseline.DriftScale);
                Range = j.Fix("range", baseline.Range);
                DipScale = j.Fix("dipScale", baseline.DipScale);
                StaminaDrain = j.Fix("staminaDrain", baseline.StaminaDrain);
                Amped = this;
            }
        }

        public bool Has(MoveFlags flag) => (Flags & flag) != 0;

        static MoveFlags ParseFlags(JsonNode list)
        {
            if (list == null) return MoveFlags.None;
            MoveFlags flags = MoveFlags.None;
            foreach (JsonNode item in list.Items) flags |= ParseEnum<MoveFlags>(item);
            return flags;
        }

        internal static T ParseEnum<T>(JsonNode node) where T : struct
        {
            if (!Enum.TryParse(node.AsString(), false, out T value)) throw node.Fail("unknown " + typeof(T).Name + " '" + node.AsString() + "'");
            return value;
        }
    }

    public sealed class FinisherDefinition
    {
        public readonly FinisherKind Kind;
        public readonly FinisherTrigger Trigger;
        public readonly int Frames, DenialStart, DenialFrames;

        public FinisherDefinition(JsonNode j)
        {
            Kind = TrickDefinition.ParseEnum<FinisherKind>(j["kind"]);
            Trigger = TrickDefinition.ParseEnum<FinisherTrigger>(j["trigger"]);
            Frames = j.Int("frames");
            DenialStart = j.Int("denialStart");
            DenialFrames = j.Int("denialFrames");
            // Spec: Finishers run 120-180 frames with one 4-6-frame Denial window.
            if (Frames < 120 || Frames > 180) throw j.Fail("finisher frames must be 120-180");
            if (DenialFrames < 4 || DenialFrames > 6) throw j.Fail("denial window must be 4-6 frames");
            if (DenialStart < 1 || DenialStart + DenialFrames >= Frames) throw j.Fail("denial window must sit inside the finisher");
        }
    }

    /// <summary>A court: cosmetic only in Duel rules; Local Conditions read the flags.</summary>
    public sealed class CourtDefinition
    {
        public readonly int Index;
        public readonly string Id, Name, Theme, Look, FinisherMoment, Music, HomeOf, Floor;
        public readonly bool LaunchSet, Sand, Snow, Wind;

        public CourtDefinition(int index, JsonNode j)
        {
            Index = index;
            Id = j.Str("id");
            Name = j.Str("name");
            Theme = j.Str("theme");
            Look = j.Str("look");
            FinisherMoment = j.Str("finisherMoment");
            Music = j.Str("music");
            HomeOf = j.Str("homeOf", "");
            Floor = j.Str("floor");
            LaunchSet = j.Bool("launchSet", false);
            JsonNode c = j["conditions"];
            foreach (JsonNode item in c.Items)
            {
                string s = item.AsString();
                if (s == "sand") Sand = true;
                else if (s == "snow") Snow = true;
                else if (s == "wind") Wind = true;
                else throw item.Fail("unknown condition '" + s + "'");
            }
        }
    }
}
