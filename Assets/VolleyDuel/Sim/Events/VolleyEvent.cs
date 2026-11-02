namespace VolleyDuel.Sim
{
    /// <summary>
    /// Sim event types (SimEvent.Type). Payload conventions are listed per entry; X/Y carry a
    /// world position when one is meaningful.
    /// </summary>
    public enum VolleyEvent : ushort
    {
        None = 0,
        /// <summary>Match begins. A = first server.</summary>
        MatchStart,
        /// <summary>A set begins. A = set index, B = first server.</summary>
        SetStart,
        /// <summary>Server holds the ball. Actor = server.</summary>
        ServeReady,
        /// <summary>Serve toss. Actor = server, A = 1 for a jump serve.</summary>
        Toss,
        /// <summary>Committed to a touch. Actor, A = TouchKind, B = TimingGrade.</summary>
        Commit,
        /// <summary>Ball contact. Actor, A = TouchKind, B = grade | band &lt;&lt; 4 | trick &lt;&lt; 8.</summary>
        Contact,
        /// <summary>A swing or press that found no ball. Actor.</summary>
        Whiff,
        /// <summary>Jump start. Actor, A = JumpKind.</summary>
        Jump,
        Takeoff,
        Land,
        /// <summary>Dive started. Actor, X = target.</summary>
        Dive,
        /// <summary>Block contact. Actor = blocker, A = BlockOutcome, B = timing offset in frames.</summary>
        BlockTouch,
        /// <summary>Ball hit the net body. A = impact speed in cm/s.</summary>
        NetHit,
        /// <summary>Ball clipped the net cord. A = 1 when it continued over.</summary>
        NetCord,
        /// <summary>Ball crossed the net. A = side it entered.</summary>
        NetCross,
        /// <summary>Ball hit the floor. A = 1 in, 0 out; X = landing X.</summary>
        BallLanded,
        /// <summary>Rule fault. Actor = faulting player, A = FaultKind.</summary>
        Fault,
        /// <summary>Point scored. Actor = winner, A = PointReason, B = points awarded.</summary>
        Point,
        /// <summary>Set won. Actor = winner, A = set index, B = sets won.</summary>
        SetWon,
        /// <summary>Match won. Actor = winner.</summary>
        MatchWon,
        /// <summary>Highlight. Actor = beneficiary, A = Callout.</summary>
        Callout,
        /// <summary>Surge gained. Actor, A = amount, B = new total.</summary>
        SurgeGain,
        /// <summary>Surge spent. Actor, A = amount, B = new total.</summary>
        SurgeSpend,
        /// <summary>Bit on a feint. Actor = bitten player, A = FeintKind.</summary>
        Bite,
        /// <summary>Three straight points: On a Run. Actor.</summary>
        OnARun,
        /// <summary>On a Run broken. Actor = breaker, A = Surge awarded.</summary>
        RunBroken,
        /// <summary>Stamina hit zero. Actor.</summary>
        Gassed,
        /// <summary>Recovered from Gassed. Actor.</summary>
        Recovered,
        /// <summary>Signature Trick started. Actor, A = TrickKind, B = 1 if Amped.</summary>
        Trick,
        /// <summary>Shockwave knock-back applied. Actor = victim, A = frames.</summary>
        Knockback,
        /// <summary>Finisher cinematic begins. Actor, A = FinisherKind, B = total frames.</summary>
        FinisherStart,
        /// <summary>Denial window opens. Actor = defender, A = window frames.</summary>
        DenialWindow,
        /// <summary>Finisher denied. Actor = defender.</summary>
        Denied,
        /// <summary>Finisher landed. Actor = user, A = points.</summary>
        FinisherScored,
        /// <summary>Hit-stop started. A = frames.</summary>
        HitStop,
        /// <summary>Slow motion started. A = divisor, B = world frames (0 = until cleared).</summary>
        SlowMotion,
        /// <summary>A decoy appeared. Actor, A = 0 mirage blocker, 1 decoy ball flight.</summary>
        Decoy,
        /// <summary>Set or match point serve. Actor = side at set point, A = 1 for match point.</summary>
        SetPoint,
    }
}
