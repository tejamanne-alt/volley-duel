using Duel.Core.Sim;
using Duel.Core.Sim.Data;

namespace VolleyDuel.Sim
{
    /// <summary>Immutable gameplay tuning baked from tuning.json. Field names mirror the JSON keys.</summary>
    public sealed class Tuning
    {
        public readonly CourtRules Court;
        public readonly BallTuning Ball;
        public readonly RingTuning Rings;
        public readonly ContactTuning Contact;
        public readonly MovementTuning Movement;
        public readonly JumpTuning Jump;
        public readonly ReachTuning Reach;
        public readonly DiveTuning Dive;
        public readonly PassTuning Pass;
        public readonly SetTuning Set;
        public readonly AttackTuning Attack;
        public readonly BlockTuning Block;
        public readonly ServeTuning Serve;
        public readonly FeintTuning Feint;
        public readonly MeterTuning Meters;
        public readonly OnRunTuning OnRun;
        public readonly ScoringRules Scoring;
        public readonly FlowTuning Flow;
        public readonly ImpactTable Impacts;
        public readonly ConditionsTuning Conditions;

        /// <summary>Seconds per frame (1/60).</summary>
        public readonly Fix64 Dt;
        /// <summary>Gravity times dt: velocity lost per frame.</summary>
        public readonly Fix64 GravityDt;
        /// <summary>Half gravity times dt squared: drop per frame from gravity.</summary>
        public readonly Fix64 HalfGravityDt2;

        public Tuning(JsonNode j)
        {
            Court = new CourtRules(j["court"]);
            Ball = new BallTuning(j["ball"]);
            Rings = new RingTuning(j["rings"]);
            Contact = new ContactTuning(j["contact"]);
            Movement = new MovementTuning(j["movement"]);
            Jump = new JumpTuning(j["jump"]);
            Reach = new ReachTuning(j["reach"]);
            Dive = new DiveTuning(j["dive"]);
            Pass = new PassTuning(j["pass"]);
            Set = new SetTuning(j["set"]);
            Attack = new AttackTuning(j["attack"]);
            Block = new BlockTuning(j["block"]);
            Serve = new ServeTuning(j["serve"]);
            Feint = new FeintTuning(j["feint"]);
            Meters = new MeterTuning(j["meters"]);
            OnRun = new OnRunTuning(j["onRun"]);
            Scoring = new ScoringRules(j["scoring"]);
            Flow = new FlowTuning(j["flow"]);
            Impacts = new ImpactTable(j["impacts"]);
            Conditions = new ConditionsTuning(j["conditions"]);
            Dt = Fix64.FromRatio(1, 60);
            GravityDt = Ball.Gravity * Dt;
            HalfGravityDt2 = Ball.Gravity * Dt * Dt / 2;
        }
    }

    public sealed class CourtRules
    {
        public readonly Fix64 HalfLength, ServiceDepth, FreeZone, NetHeight, BallRadius, NetTouchDistance, GroundedNetGap, CenterLineTolerance;

        public CourtRules(JsonNode j)
        {
            HalfLength = j.Fix("halfLength");
            ServiceDepth = j.Fix("serviceDepth");
            FreeZone = j.Fix("freeZone");
            NetHeight = j.Fix("netHeight");
            BallRadius = j.Fix("ballRadius");
            NetTouchDistance = j.Fix("netTouchDistance");
            GroundedNetGap = j.Fix("groundedNetGap");
            CenterLineTolerance = j.Fix("centerLineTolerance");
        }

        /// <summary>Furthest a fighter may go from the net (end line plus free zone).</summary>
        public Fix64 OuterLimit => HalfLength + FreeZone;
    }

    public sealed class BallTuning
    {
        public readonly Fix64 Gravity, NetRestitution, NetCordOverChance, FloorRestitution, FloatWobbleMax, TopspinDipGravity;
        public readonly int FloatWobbleNudges, FloatWobbleFirstFrame, FloatWobbleInterval, PredictionHorizon;

        public BallTuning(JsonNode j)
        {
            Gravity = j.Fix("gravity");
            NetRestitution = j.Fix("netRestitution");
            NetCordOverChance = j.Fix("netCordOverChance");
            FloorRestitution = j.Fix("floorRestitution");
            FloatWobbleNudges = j.Int("floatWobbleNudges");
            FloatWobbleFirstFrame = j.Int("floatWobbleFirstFrame");
            FloatWobbleInterval = j.Int("floatWobbleInterval");
            FloatWobbleMax = j.Fix("floatWobbleMax");
            TopspinDipGravity = j.Fix("topspinDipGravity");
            PredictionHorizon = j.Int("predictionHorizon");
            if (FloatWobbleNudges > BallState.MaxNudges) throw j.Fail("floatWobbleNudges exceeds " + BallState.MaxNudges);
        }
    }

    public sealed class RingTuning
    {
        public readonly int PerfectBase, GoodBase, MinPerfect, MinGood, LateBand, LeadFrames, BufferFrames, LockFrames, DigStanceGoodBonus;
        public readonly Fix64 SimpleModeScale, DigStancePressureRelief;

        public RingTuning(JsonNode j)
        {
            PerfectBase = j.Int("perfectBase");
            GoodBase = j.Int("goodBase");
            MinPerfect = j.Int("minPerfect");
            MinGood = j.Int("minGood");
            LateBand = j.Int("lateBand");
            LeadFrames = j.Int("leadFrames");
            BufferFrames = j.Int("bufferFrames");
            LockFrames = j.Int("lockFrames");
            SimpleModeScale = j.Fix("simpleModeScale");
            DigStanceGoodBonus = j.Int("digStanceGoodBonus");
            DigStancePressureRelief = j.Fix("digStancePressureRelief");
        }

        /// <summary>Spec: Perfect is 4 frames, plus or minus 1 per 2 attribute points away from 5.</summary>
        public int PerfectWidth(int attribute)
        {
            int w = PerfectBase + (attribute - 5) / 2;
            return w < MinPerfect ? MinPerfect : w;
        }

        /// <summary>Spec: Good is 10 frames, plus or minus 1 per point.</summary>
        public int GoodWidth(int attribute)
        {
            int w = GoodBase + (attribute - 5);
            return w < MinGood ? MinGood : w;
        }
    }

    public sealed class ContactTuning
    {
        public readonly Fix64 TimingPerfect, TimingGood, TimingLate, AttributeSlope, PressureMax, PressureSpeedLow, PressureSpeedHigh,
            HeavyBallPressure, StaminaPenaltyMax, StaminaPenaltyBelow, Controlled, Loose, Shank, LooseAimError, LooseHeightError,
            ShankSpeedMin, ShankSpeedMax, BumpSetPenalty;

        public ContactTuning(JsonNode j)
        {
            TimingPerfect = j.Fix("timingPerfect");
            TimingGood = j.Fix("timingGood");
            TimingLate = j.Fix("timingLate");
            AttributeSlope = j.Fix("attributeSlope");
            PressureMax = j.Fix("pressureMax");
            PressureSpeedLow = j.Fix("pressureSpeedLow");
            PressureSpeedHigh = j.Fix("pressureSpeedHigh");
            HeavyBallPressure = j.Fix("heavyBallPressure");
            StaminaPenaltyMax = j.Fix("staminaPenaltyMax");
            StaminaPenaltyBelow = j.Fix("staminaPenaltyBelow");
            Controlled = j.Fix("controlled");
            Loose = j.Fix("loose");
            Shank = j.Fix("shank");
            LooseAimError = j.Fix("looseAimError");
            LooseHeightError = j.Fix("looseHeightError");
            ShankSpeedMin = j.Fix("shankSpeedMin");
            ShankSpeedMax = j.Fix("shankSpeedMax");
            BumpSetPenalty = j.Fix("bumpSetPenalty");
        }
    }

    public sealed class MovementTuning
    {
        public readonly Fix64 WalkSpeedBase, WalkSpeedPerPoint, BackpedalScale, DigStanceScale;
        public readonly int AccelFrames, DigStanceStick, Deadzone;

        public MovementTuning(JsonNode j)
        {
            WalkSpeedBase = j.Fix("walkSpeedBase");
            WalkSpeedPerPoint = j.Fix("walkSpeedPerPoint");
            AccelFrames = j.Int("accelFrames");
            BackpedalScale = j.Fix("backpedalScale");
            DigStanceScale = j.Fix("digStanceScale");
            DigStanceStick = j.Int("digStanceStick");
            Deadzone = j.Int("deadzone");
        }
    }

    public sealed class JumpTuning
    {
        public readonly Fix64 HeightBase, HeightPerPoint, BlockHeightScale, ApproachCarry, MaxDrift, FakeHopHeight;
        public readonly int StartupFrames, BlockStartupFrames, LandingRecovery, FakeHopRecovery;

        public JumpTuning(JsonNode j)
        {
            HeightBase = j.Fix("heightBase");
            HeightPerPoint = j.Fix("heightPerPoint");
            StartupFrames = j.Int("startupFrames");
            BlockStartupFrames = j.Int("blockStartupFrames");
            BlockHeightScale = j.Fix("blockHeightScale");
            ApproachCarry = j.Fix("approachCarry");
            MaxDrift = j.Fix("maxDrift");
            LandingRecovery = j.Int("landingRecovery");
            FakeHopHeight = j.Fix("fakeHopHeight");
            FakeHopRecovery = j.Int("fakeHopRecovery");
        }
    }

    public sealed class ReachTuning
    {
        public readonly Fix64 StandingReachRatio, HandForward, SpikeReachRadius, BumpHeight, BumpReach, SetHeightAboveHead, SetReach,
            MinContactHeight, BodyHalfWidth, CommitStepSpeed, NetReachOver;
        public readonly int TouchRecovery;

        public ReachTuning(JsonNode j)
        {
            StandingReachRatio = j.Fix("standingReachRatio");
            HandForward = j.Fix("handForward");
            SpikeReachRadius = j.Fix("spikeReachRadius");
            BumpHeight = j.Fix("bumpHeight");
            BumpReach = j.Fix("bumpReach");
            SetHeightAboveHead = j.Fix("setHeightAboveHead");
            SetReach = j.Fix("setReach");
            MinContactHeight = j.Fix("minContactHeight");
            BodyHalfWidth = j.Fix("bodyHalfWidth");
            CommitStepSpeed = j.Fix("commitStepSpeed");
            TouchRecovery = j.Int("touchRecovery");
            NetReachOver = j.Fix("netReachOver");
        }
    }

    public sealed class DiveTuning
    {
        public readonly Fix64 RangeBase, RangePerDigPoint, Speed;
        public readonly int Recovery;

        public DiveTuning(JsonNode j)
        {
            RangeBase = j.Fix("rangeBase");
            RangePerDigPoint = j.Fix("rangePerDigPoint");
            Speed = j.Fix("speed");
            Recovery = j.Int("recovery");
        }
    }

    public sealed class PassTuning
    {
        public readonly Fix64 TargetFromNet, PeakHeight, SaveDepth, SavePeak;

        public PassTuning(JsonNode j)
        {
            TargetFromNet = j.Fix("targetFromNet");
            PeakHeight = j.Fix("peakHeight");
            SaveDepth = j.Fix("saveDepth");
            SavePeak = j.Fix("savePeak");
        }
    }

    public sealed class SetTuning
    {
        public readonly Fix64 QuickPeakAboveNet, QuickTargetFromNet, QuickPowerScale, HighPeakAboveNet, HighTargetFromNet, MissedPowerScale;
        public readonly int QuickReadCut, HighBlockWiden, QuickStick;

        public SetTuning(JsonNode j)
        {
            QuickPeakAboveNet = j.Fix("quickPeakAboveNet");
            QuickTargetFromNet = j.Fix("quickTargetFromNet");
            QuickReadCut = j.Int("quickReadCut");
            QuickPowerScale = j.Fix("quickPowerScale");
            HighPeakAboveNet = j.Fix("highPeakAboveNet");
            HighTargetFromNet = j.Fix("highTargetFromNet");
            HighBlockWiden = j.Int("highBlockWiden");
            MissedPowerScale = j.Fix("missedPowerScale");
            QuickStick = j.Int("quickStick");
        }
    }

    public sealed class AttackTuning
    {
        public readonly Fix64 SpikeBaseSpeed, SpikeSpeedPerPower, ChargeBonus, TipSpeedMin, TipSpeedMax, RollSpeed, ShortFromNet, MidFromNet,
            DeepFromNet, TipFromNet, RollFromNet, NetClearance, StandingHitPowerScale, TipLift, RollLift;
        public readonly int ChargeFrames, AimStick;

        public AttackTuning(JsonNode j)
        {
            SpikeBaseSpeed = j.Fix("spikeBaseSpeed");
            SpikeSpeedPerPower = j.Fix("spikeSpeedPerPower");
            ChargeFrames = j.Int("chargeFrames");
            ChargeBonus = j.Fix("chargeBonus");
            TipSpeedMin = j.Fix("tipSpeedMin");
            TipSpeedMax = j.Fix("tipSpeedMax");
            RollSpeed = j.Fix("rollSpeed");
            ShortFromNet = j.Fix("shortFromNet");
            MidFromNet = j.Fix("midFromNet");
            DeepFromNet = j.Fix("deepFromNet");
            TipFromNet = j.Fix("tipFromNet");
            RollFromNet = j.Fix("rollFromNet");
            TipLift = j.Fix("tipLift");
            RollLift = j.Fix("rollLift");
            AimStick = j.Int("aimStick");
            NetClearance = j.Fix("netClearance");
            StandingHitPowerScale = j.Fix("standingHitPowerScale");
        }
    }

    public sealed class BlockTuning
    {
        public readonly Fix64 RangeFromNet, HandTopAboveReach, StuffSpeed, SoftPopSpeed, SoftBackSpeed, ToolDepthPastLine, ToolPeak;
        public readonly int PerfectHalf, GoodHalf, ContactHalf;

        public BlockTuning(JsonNode j)
        {
            RangeFromNet = j.Fix("rangeFromNet");
            PerfectHalf = j.Int("perfectHalf");
            GoodHalf = j.Int("goodHalf");
            ContactHalf = j.Int("contactHalf");
            HandTopAboveReach = j.Fix("handTopAboveReach");
            StuffSpeed = j.Fix("stuffSpeed");
            SoftPopSpeed = j.Fix("softPopSpeed");
            SoftBackSpeed = j.Fix("softBackSpeed");
            ToolDepthPastLine = j.Fix("toolDepthPastLine");
            ToolPeak = j.Fix("toolPeak");
        }
    }

    public sealed class ServeTuning
    {
        public readonly Fix64 StandFromNet, FloatTossHeight, FloatSpeedBase, FloatSpeedPerPower, FloatClearance, JumpTossHeight, JumpTossForward,
            JumpApproachDistance, JumpSpeedBase, JumpSpeedPerPower, JumpClearance, ShortFromNet, MidFromNet, DeepFromNet;
        public readonly int TimeoutFrames;

        public ServeTuning(JsonNode j)
        {
            StandFromNet = j.Fix("standFromNet");
            FloatTossHeight = j.Fix("floatTossHeight");
            FloatSpeedBase = j.Fix("floatSpeedBase");
            FloatSpeedPerPower = j.Fix("floatSpeedPerPower");
            FloatClearance = j.Fix("floatClearance");
            JumpTossHeight = j.Fix("jumpTossHeight");
            JumpTossForward = j.Fix("jumpTossForward");
            JumpApproachDistance = j.Fix("jumpApproachDistance");
            JumpSpeedBase = j.Fix("jumpSpeedBase");
            JumpSpeedPerPower = j.Fix("jumpSpeedPerPower");
            JumpClearance = j.Fix("jumpClearance");
            ShortFromNet = j.Fix("shortFromNet");
            MidFromNet = j.Fix("midFromNet");
            DeepFromNet = j.Fix("deepFromNet");
            TimeoutFrames = j.Int("timeoutFrames");
        }
    }

    public sealed class FeintTuning
    {
        public readonly int BiteStamina, BiteRecovery, TipBaitFrames, DumpBaitFrames, FakeBlockBaitFrames;

        public FeintTuning(JsonNode j)
        {
            BiteStamina = j.Int("biteStamina");
            BiteRecovery = j.Int("biteRecovery");
            TipBaitFrames = j.Int("tipBaitFrames");
            DumpBaitFrames = j.Int("dumpBaitFrames");
            FakeBlockBaitFrames = j.Int("fakeBlockBaitFrames");
        }
    }

    public sealed class MeterTuning
    {
        public readonly int SurgeMax, SurgeBar, GainKill, GainAce, GainStuff, GainPerfectDig, GainFloorSave, GainReadTip, GainJoust,
            GainBiteDrawn, GainScoredOn, CostAmped, CostDenial, CostFinisher;
        public readonly Fix64 SimpleSurgeScale, StaminaMax, CostJump, CostDive, CostJumpServe, CostBite, RefillPerSecond, RefillDeadBall,
            GassedRecoverAt, GassedJumpScale, GassedDiveScale, RefillPerFrame;

        public MeterTuning(JsonNode j)
        {
            SurgeMax = j.Int("surgeMax");
            SurgeBar = j.Int("surgeBar");
            GainKill = j.Int("gainKill");
            GainAce = j.Int("gainAce");
            GainStuff = j.Int("gainStuff");
            GainPerfectDig = j.Int("gainPerfectDig");
            GainFloorSave = j.Int("gainFloorSave");
            GainReadTip = j.Int("gainReadTip");
            GainJoust = j.Int("gainJoust");
            GainBiteDrawn = j.Int("gainBiteDrawn");
            GainScoredOn = j.Int("gainScoredOn");
            CostAmped = j.Int("costAmped");
            CostDenial = j.Int("costDenial");
            CostFinisher = j.Int("costFinisher");
            SimpleSurgeScale = j.Fix("simpleSurgeScale");
            StaminaMax = j.Fix("staminaMax");
            CostJump = j.Fix("costJump");
            CostDive = j.Fix("costDive");
            CostJumpServe = j.Fix("costJumpServe");
            CostBite = j.Fix("costBite");
            RefillPerSecond = j.Fix("refillPerSecond");
            RefillDeadBall = j.Fix("refillDeadBall");
            GassedRecoverAt = j.Fix("gassedRecoverAt");
            GassedJumpScale = j.Fix("gassedJumpScale");
            GassedDiveScale = j.Fix("gassedDiveScale");
            RefillPerFrame = RefillPerSecond / 60;
        }
    }

    public sealed class OnRunTuning
    {
        public readonly int Streak, BreakerSurge;
        public readonly Fix64 PerfectScale;

        public OnRunTuning(JsonNode j)
        {
            Streak = j.Int("streak");
            PerfectScale = j.Fix("perfectScale");
            BreakerSurge = j.Int("breakerSurge");
        }
    }

    public sealed class ScoringRules
    {
        public readonly int SetTarget, WinBy, SetCap, SetsToWin, FinisherPoints;

        public ScoringRules(JsonNode j)
        {
            SetTarget = j.Int("setTarget");
            WinBy = j.Int("winBy");
            SetCap = j.Int("setCap");
            SetsToWin = j.Int("setsToWin");
            FinisherPoints = j.Int("finisherPoints");
        }

        /// <summary>True when a side with <paramref name="own"/> points against <paramref name="other"/> has won the set.</summary>
        public bool SetWon(int own, int other) => own >= SetCap || (own >= SetTarget && own - other >= WinBy);

        /// <summary>True when winning the next rally (one point) would win the set.</summary>
        public bool SetPoint(int own, int other) => SetWon(own + 1, other);
    }

    public sealed class FlowTuning
    {
        public readonly int IntroFrames, DeadBallFrames, SetBreakFrames, SetPointSlowDiv;
        public readonly Fix64 ReceiveFromNet;

        public FlowTuning(JsonNode j)
        {
            IntroFrames = j.Int("introFrames");
            DeadBallFrames = j.Int("deadBallFrames");
            SetBreakFrames = j.Int("setBreakFrames");
            ReceiveFromNet = j.Fix("receiveFromNet");
            SetPointSlowDiv = j.Int("setPointSlowDiv");
        }
    }

    public sealed class ConditionsTuning
    {
        public readonly Fix64 SandDragScale, SnowAccelScale, WindMax;

        public ConditionsTuning(JsonNode j)
        {
            SandDragScale = j.Fix("sandDragScale");
            SnowAccelScale = j.Fix("snowAccelScale");
            WindMax = j.Fix("windMax");
        }
    }

    /// <summary>One row of the feel table. The sim applies hit-stop and slow motion; presentation reads camera and callout.</summary>
    public sealed class ImpactRecipe
    {
        public readonly Callout Moment;
        public readonly int HitStop, SlowDiv, SlowFrames;
        public readonly string Camera, CalloutText;

        public ImpactRecipe(Callout moment, JsonNode j)
        {
            Moment = moment;
            HitStop = j.Int("hitStop");
            SlowDiv = j.Int("slowDiv");
            SlowFrames = j.Int("slowFrames");
            Camera = j.Str("camera");
            CalloutText = j.Str("callout");
            if (SlowDiv < 1 || SlowDiv > 4) throw j.Fail("slowDiv must be 1-4");
        }
    }

    public sealed class ImpactTable
    {
        readonly ImpactRecipe[] byCallout;

        public ImpactTable(JsonNode j)
        {
            byCallout = new ImpactRecipe[(int)Callout.Count];
            Bind(j, "kill", Callout.Kill);
            Bind(j, "stuff", Callout.Stuffed);
            Bind(j, "ace", Callout.Ace);
            Bind(j, "floorSave", Callout.Pancake);
            Bind(j, "perfectDig", Callout.Dug);
            Bind(j, "tooled", Callout.Tooled);
            Bind(j, "readTip", Callout.ReadIt);
            Bind(j, "joust", Callout.Joust);
            Bind(j, "denial", Callout.Denied);
            Bind(j, "setPoint", Callout.SetPoint);
            Bind(j, "matchPoint", Callout.MatchPoint);
        }

        void Bind(JsonNode j, string key, Callout callout) => byCallout[(int)callout] = new ImpactRecipe(callout, j[key]);

        /// <summary>Recipe for a callout, or null when the callout has no impact recipe.</summary>
        public ImpactRecipe For(Callout callout) => (int)callout < byCallout.Length ? byCallout[(int)callout] : null;
    }
}
