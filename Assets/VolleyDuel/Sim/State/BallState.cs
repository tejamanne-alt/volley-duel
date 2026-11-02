using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    public struct BallState
    {
        public const int MaxNudges = 3;

        public FixVec2 Pos;
        public FixVec2 Vel;
        public BallMode Mode;
        public byte Holder;
        public BallSpin Spin;
        public BallFlags Flags;
        /// <summary>World frame of the last contact or toss.</summary>
        public int FlightStart;
        /// <summary>Side (0/1) of the fighter who last launched the ball.</summary>
        public byte LaunchSide;
        public bool CrossedNet;
        /// <summary>Extra downward acceleration applied once the ball has crossed the net (topspin dip).</summary>
        public Fix64 DipGravity;
        /// <summary>Float-serve wobble: horizontal velocity nudges applied at fixed flight frames.</summary>
        public Fix64 Nudge0, Nudge1, Nudge2;
        /// <summary>Horizontal acceleration from Local Conditions wind (zero in Duel rules).</summary>
        public Fix64 Wind;

        // Pending block deflection, applied when the spike reaches the block.
        public int PendingBlockFrame;
        public BlockOutcome PendingBlock;
        public byte PendingBlocker;

        // Kalari Twist (Amped): a decoy flight shown instead of the true one until DecoyUntil.
        public int DecoyUntil;
        public FixVec2 DecoyVel;

        public bool Has(BallFlags flag) => (Flags & flag) != 0;

        public Fix64 Nudge(int i) => i == 0 ? Nudge0 : (i == 1 ? Nudge1 : Nudge2);

        public void Write(ref StateWriter w)
        {
            w.WriteVec(Pos);
            w.WriteVec(Vel);
            w.WriteByte((byte)Mode);
            w.WriteByte(Holder);
            w.WriteByte((byte)Spin);
            w.WriteUInt16((ushort)Flags);
            w.WriteInt32(FlightStart);
            w.WriteByte(LaunchSide);
            w.WriteBool(CrossedNet);
            w.WriteFix(DipGravity);
            w.WriteFix(Nudge0);
            w.WriteFix(Nudge1);
            w.WriteFix(Nudge2);
            w.WriteFix(Wind);
            w.WriteInt32(PendingBlockFrame);
            w.WriteByte((byte)PendingBlock);
            w.WriteByte(PendingBlocker);
            w.WriteInt32(DecoyUntil);
            w.WriteVec(DecoyVel);
        }

        public void Read(ref StateReader r)
        {
            Pos = r.ReadVec();
            Vel = r.ReadVec();
            Mode = (BallMode)r.ReadByte();
            Holder = r.ReadByte();
            Spin = (BallSpin)r.ReadByte();
            Flags = (BallFlags)r.ReadUInt16();
            FlightStart = r.ReadInt32();
            LaunchSide = r.ReadByte();
            CrossedNet = r.ReadBool();
            DipGravity = r.ReadFix();
            Nudge0 = r.ReadFix();
            Nudge1 = r.ReadFix();
            Nudge2 = r.ReadFix();
            Wind = r.ReadFix();
            PendingBlockFrame = r.ReadInt32();
            PendingBlock = (BlockOutcome)r.ReadByte();
            PendingBlocker = r.ReadByte();
            DecoyUntil = r.ReadInt32();
            DecoyVel = r.ReadVec();
        }
    }
}
