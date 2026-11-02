using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    /// <summary>One fighter's slice of the snapshot. Positions are feet; Y = 0 is the floor.</summary>
    public struct FighterState
    {
        public FixVec2 Pos;
        public FixVec2 Vel;
        public FighterAction Action;
        public JumpKind Jump;
        /// <summary>World frame the current action started.</summary>
        public int ActionStart;
        /// <summary>World frame the current timed action ends (startup, recovery, dive).</summary>
        public int ActionEnd;
        /// <summary>World frame of the current jump's apex (block timing).</summary>
        public int ApexFrame;
        /// <summary>Planned height of the current jump.</summary>
        public Fix64 JumpHeight;
        public Fix64 Stamina;
        public bool Gassed;
        public short Surge;
        public InputTracker Input;

        // Committed touch (grounded or airborne).
        public TouchKind CommitKind;
        public AttackChoice Choice;
        public TrickKind CommitTrick;
        public bool CommitAmped;
        public TimingGrade CommitGrade;
        /// <summary>World frame of the press (or HIT release for spikes) that committed.</summary>
        public int CommitPress;
        /// <summary>Planned contact world frame.</summary>
        public int CommitContact;
        /// <summary>World frame when attack choice and aim lock.</summary>
        public int CommitLock;
        public sbyte CommitAim;
        /// <summary>Spike charge frames held before the release.</summary>
        public byte Charge;
        public bool Charging;
        public int ChargeStart;
        public bool SwungThisJump;
        /// <summary>Dive target X (world) while diving.</summary>
        public Fix64 DiveTargetX;

        // Contact ring (the current opportunity), read by presentation and the CPU.
        public RingKind Ring;
        public int RingCenter;
        public byte RingPerfect;
        public byte RingGood;
        public bool RingReachable;
        public bool RingDive;
        public FixVec2 RingPoint;
        /// <summary>Ball flight (FlightStart) the ring was planned for; the ring stays open through its late window.</summary>
        public int RingFlight;

        // Block read ring: centered on the opponent hitter's ideal contact, shown from RingRevealFrame.
        public int BlockRingCenter;
        public int BlockRingReveal;

        // Rally bookkeeping.
        public bool FloorSaveUsed;
        public byte ExtraRecovery;
        public int KnockbackUntil;
        public FeintKind Bait;
        public int BaitStart;
        public int BaitEnd;
        public int DecoyUntil;
        public TrickKind ActiveTrick;
        public bool ActiveTrickAmped;
        public int TrickUntil;
        /// <summary>Frame until which Defne's Commit Block keeps her hands over the net.</summary>
        public int HangUntil;
        public bool LandInDigStance;
        /// <summary>Frame the next touch after this fighter's own block counts for Citadel.</summary>
        public bool BlockedLast;
        /// <summary>World frame of this fighter's last Perfect dig (Ippitsu trigger window).</summary>
        public int PerfectDigFrame;

        public void Write(ref StateWriter w)
        {
            w.WriteVec(Pos);
            w.WriteVec(Vel);
            w.WriteByte((byte)Action);
            w.WriteByte((byte)Jump);
            w.WriteInt32(ActionStart);
            w.WriteInt32(ActionEnd);
            w.WriteInt32(ApexFrame);
            w.WriteFix(JumpHeight);
            w.WriteFix(Stamina);
            w.WriteBool(Gassed);
            w.WriteInt16(Surge);
            Input.Write(ref w);
            w.WriteByte((byte)CommitKind);
            w.WriteByte((byte)Choice);
            w.WriteByte((byte)CommitTrick);
            w.WriteBool(CommitAmped);
            w.WriteByte((byte)CommitGrade);
            w.WriteInt32(CommitPress);
            w.WriteInt32(CommitContact);
            w.WriteInt32(CommitLock);
            w.WriteSByte(CommitAim);
            w.WriteByte(Charge);
            w.WriteBool(Charging);
            w.WriteInt32(ChargeStart);
            w.WriteBool(SwungThisJump);
            w.WriteFix(DiveTargetX);
            w.WriteByte((byte)Ring);
            w.WriteInt32(RingCenter);
            w.WriteByte(RingPerfect);
            w.WriteByte(RingGood);
            w.WriteBool(RingReachable);
            w.WriteBool(RingDive);
            w.WriteVec(RingPoint);
            w.WriteInt32(RingFlight);
            w.WriteInt32(BlockRingCenter);
            w.WriteInt32(BlockRingReveal);
            w.WriteBool(FloorSaveUsed);
            w.WriteByte(ExtraRecovery);
            w.WriteInt32(KnockbackUntil);
            w.WriteByte((byte)Bait);
            w.WriteInt32(BaitStart);
            w.WriteInt32(BaitEnd);
            w.WriteInt32(DecoyUntil);
            w.WriteByte((byte)ActiveTrick);
            w.WriteBool(ActiveTrickAmped);
            w.WriteInt32(TrickUntil);
            w.WriteInt32(HangUntil);
            w.WriteBool(LandInDigStance);
            w.WriteBool(BlockedLast);
            w.WriteInt32(PerfectDigFrame);
        }

        public void Read(ref StateReader r)
        {
            Pos = r.ReadVec();
            Vel = r.ReadVec();
            Action = (FighterAction)r.ReadByte();
            Jump = (JumpKind)r.ReadByte();
            ActionStart = r.ReadInt32();
            ActionEnd = r.ReadInt32();
            ApexFrame = r.ReadInt32();
            JumpHeight = r.ReadFix();
            Stamina = r.ReadFix();
            Gassed = r.ReadBool();
            Surge = r.ReadInt16();
            Input.Read(ref r);
            CommitKind = (TouchKind)r.ReadByte();
            Choice = (AttackChoice)r.ReadByte();
            CommitTrick = (TrickKind)r.ReadByte();
            CommitAmped = r.ReadBool();
            CommitGrade = (TimingGrade)r.ReadByte();
            CommitPress = r.ReadInt32();
            CommitContact = r.ReadInt32();
            CommitLock = r.ReadInt32();
            CommitAim = r.ReadSByte();
            Charge = r.ReadByte();
            Charging = r.ReadBool();
            ChargeStart = r.ReadInt32();
            SwungThisJump = r.ReadBool();
            DiveTargetX = r.ReadFix();
            Ring = (RingKind)r.ReadByte();
            RingCenter = r.ReadInt32();
            RingPerfect = r.ReadByte();
            RingGood = r.ReadByte();
            RingReachable = r.ReadBool();
            RingDive = r.ReadBool();
            RingPoint = r.ReadVec();
            RingFlight = r.ReadInt32();
            BlockRingCenter = r.ReadInt32();
            BlockRingReveal = r.ReadInt32();
            FloorSaveUsed = r.ReadBool();
            ExtraRecovery = r.ReadByte();
            KnockbackUntil = r.ReadInt32();
            Bait = (FeintKind)r.ReadByte();
            BaitStart = r.ReadInt32();
            BaitEnd = r.ReadInt32();
            DecoyUntil = r.ReadInt32();
            ActiveTrick = (TrickKind)r.ReadByte();
            ActiveTrickAmped = r.ReadBool();
            TrickUntil = r.ReadInt32();
            HangUntil = r.ReadInt32();
            LandInDigStance = r.ReadBool();
            BlockedLast = r.ReadBool();
            PerfectDigFrame = r.ReadInt32();
        }

        public bool Airborne => Action == FighterAction.Airborne;

        public bool Grounded => Action != FighterAction.Airborne;
    }
}
