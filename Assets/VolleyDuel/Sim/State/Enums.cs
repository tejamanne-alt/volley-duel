using System;

namespace VolleyDuel.Sim
{
    public enum MatchPhase : byte
    {
        Intro,
        PreServe,
        Rally,
        DeadBall,
        SetBreak,
        MatchOver,
        Finisher,
    }

    public enum FighterAction : byte
    {
        Neutral,
        DigStance,
        JumpStartup,
        Airborne,
        Landing,
        /// <summary>Committed to a grounded touch; waiting for the contact frame.</summary>
        Committed,
        Diving,
        Floor,
        Knockback,
        ServeHold,
        ServeToss,
        ServeApproach,
        /// <summary>Locked by a Finisher cinematic.</summary>
        Locked,
        /// <summary>Recovering after a grounded touch or a whiff.</summary>
        Recovery,
    }

    public enum JumpKind : byte
    {
        None,
        Approach,
        Block,
        FakeHop,
        JumpServe,
        Slide,
        CommitBlock,
        ShockwaveBlock,
    }

    public enum TouchKind : byte
    {
        None,
        Bump,
        Set,
        BumpSet,
        Save,
        Spike,
        Tip,
        Roll,
        Dump,
        StandingHit,
        FloatServe,
        JumpServe,
        Block,
        Joust,
        /// <summary>Scripted Finisher contact.</summary>
        Finisher,
    }

    public enum TimingGrade : byte
    {
        None,
        Perfect,
        Good,
        Late,
    }

    /// <summary>Contact model bands (spec): Q of 0.85+ controlled, 0.55-0.85 loose, 0.30-0.55 shank, below 0.30 a miss.</summary>
    public enum ContactBand : byte
    {
        Controlled,
        Loose,
        Shank,
        Miss,
    }

    public enum BallMode : byte
    {
        Held,
        Flight,
        Dead,
        /// <summary>Following a Finisher's scripted path.</summary>
        Scripted,
    }

    public enum BallSpin : byte
    {
        None,
        Float,
        Topspin,
    }

    [Flags]
    public enum BallFlags : ushort
    {
        None = 0,
        HeavyBall = 1 << 0,
        Unblockable = 1 << 1,
        CantStuff = 1 << 2,
        FreeBall = 1 << 3,
        /// <summary>Set from a missed set ring: only a weak hit (power x0.6) or a tip.</summary>
        WeakSet = 1 << 4,
        Serve = 1 << 5,
        Attack = 1 << 6,
        /// <summary>Any dig short of Perfect fails (Overpass Hammer).</summary>
        PerfectOnly = 1 << 7,
        QuickSet = 1 << 8,
        HighSet = 1 << 9,
        /// <summary>Set type and tempo stay hidden until the ball leaves the setter's hands.</summary>
        HideTelegraph = 1 << 10,
        /// <summary>Set-point serve flying at reduced speed until it crosses the net.</summary>
        SetPointSlow = 1 << 11,
        /// <summary>Receive ring shrunk (Amped Afterburner).</summary>
        RingShrink = 1 << 12,
        /// <summary>Last contact was a first-touch return that came back over the net (overpass).</summary>
        Overpass = 1 << 13,
    }

    public enum BlockOutcome : byte
    {
        None,
        Stuff,
        Soft,
        Tooled,
    }

    public enum FaultKind : byte
    {
        None,
        Out,
        NetTouch,
        FourHits,
        CenterLine,
        FootFault,
    }

    public enum PointReason : byte
    {
        None,
        Kill,
        Ace,
        Stuff,
        Tooled,
        /// <summary>The loser played the ball out or into their own floor.</summary>
        Error,
        Fault,
        Finisher,
        Joust,
        /// <summary>Ball landed in after a free ball or other non-attack contact.</summary>
        Landed,
    }

    /// <summary>Highlight callouts. The first group has impact recipes in tuning.json.</summary>
    public enum Callout : byte
    {
        None,
        Kill,
        Stuffed,
        Ace,
        Pancake,
        Dug,
        Tooled,
        ReadIt,
        Joust,
        Denied,
        SetPoint,
        MatchPoint,
        Count,
    }

    public enum RingKind : byte
    {
        None,
        Bump,
        Set,
        Spike,
        Serve,
        Block,
        Denial,
    }

    public enum SetType : byte
    {
        None,
        Quick,
        High,
        BumpSet,
    }

    public enum TrickKind : byte
    {
        None,
        Afterburner,
        PipeShot,
        ShockwaveBlock,
        Slide,
        OverpassHammer,
        CommitBlock,
        InkReturn,
        QuickStroke,
        Sweep,
        BlindSet,
        KalariTwist,
        Decoy,
    }

    public enum TrickSlot : byte
    {
        OffenseA,
        OffenseB,
        Defense,
    }

    [Flags]
    public enum MoveFlags : ushort
    {
        None = 0,
        Unblockable = 1 << 0,
        CantStuff = 1 << 1,
        PerfectOnly = 1 << 2,
        WinsJousts = 1 << 3,
        HideTelegraph = 1 << 4,
        LandInDigStance = 1 << 5,
        NoQuickPenalty = 1 << 6,
        LateAim = 1 << 7,
        GoodAtWorst = 1 << 8,
        DumpOption = 1 << 9,
    }

    public enum FinisherKind : byte
    {
        None,
        SoundBarrier,
        Citadel,
        Ippitsu,
        Maya,
    }

    public enum FinisherTrigger : byte
    {
        OwnServe,
        AfterOwnBlock,
        PerfectDig,
        SecondTouch,
    }

    public enum FeintKind : byte
    {
        None,
        /// <summary>A tip or roll: a fake spike.</summary>
        Tip,
        /// <summary>A second-touch dump: a fake set.</summary>
        Dump,
        FakeBlock,
        /// <summary>Arjun's mirage blocker.</summary>
        Decoy,
    }

    /// <summary>Attack choice of an airborne swing before it locks.</summary>
    public enum AttackChoice : byte
    {
        None,
        Spike,
        Tip,
        Roll,
        Trick,
    }
}
