using System;
using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    public struct FinisherState
    {
        public FinisherKind Kind;
        public byte User;
        public int Start;
        public int End;
        public int DenialStart;
        public int DenialEnd;
        public bool Attempted;
        public bool Denied;
        public FixVec2 From;
        public FixVec2 To;

        public bool Active => Kind != FinisherKind.None;

        public void Write(ref StateWriter w)
        {
            w.WriteByte((byte)Kind);
            w.WriteByte(User);
            w.WriteInt32(Start);
            w.WriteInt32(End);
            w.WriteInt32(DenialStart);
            w.WriteInt32(DenialEnd);
            w.WriteBool(Attempted);
            w.WriteBool(Denied);
            w.WriteVec(From);
            w.WriteVec(To);
        }

        public void Read(ref StateReader r)
        {
            Kind = (FinisherKind)r.ReadByte();
            User = r.ReadByte();
            Start = r.ReadInt32();
            End = r.ReadInt32();
            DenialStart = r.ReadInt32();
            DenialEnd = r.ReadInt32();
            Attempted = r.ReadBool();
            Denied = r.ReadBool();
            From = r.ReadVec();
            To = r.ReadVec();
        }
    }

    /// <summary>
    /// The whole match as a value type. Copying it is a snapshot; <see cref="Write"/> is the
    /// canonical byte image used for hashing, rollback buffers and full-state resend.
    /// </summary>
    public struct GameState
    {
        /// <summary>Bump when the serialized layout changes.</summary>
        public const int LayoutVersion = 1;
        public const int MaxBytes = 2048;
        public const byte NoPlayer = 255;

        // Clocks.
        public int Tick;
        public int World;
        public int HitStop;
        public byte SlowDiv;
        public byte SlowPhase;
        public short SlowFrames;

        // Match.
        public MatchPhase Phase;
        public int PhaseStart;
        public byte SetIndex;
        public byte SetsWon0;
        public byte SetsWon1;
        public byte Score0;
        public byte Score1;
        public byte Server;
        public byte FirstServerThisSet;
        public byte Streak0;
        public byte Streak1;
        public bool RunArmed0;
        public bool RunArmed1;
        public byte LastPointWinner;
        public PointReason LastPointReason;
        public short RallyIndex;
        public int RallyStartWorld;
        public byte Winner;

        // Rally.
        public byte Possession;
        public byte Touches;
        public byte LastToucher;
        public TouchKind LastTouch;
        public byte LastAttacker;
        public int LastContactWorld;
        public byte TouchesSinceCross;
        /// <summary>Who last sent the ball over the net, and with which touch (kill/ace attribution).</summary>
        public byte LastSender;
        public TouchKind LastSendKind;

        public BallState Ball;
        public FighterState F0;
        public FighterState F1;
        public FinisherState Fin;
        public Rng MatchRng;

        public static ref FighterState Fighter(ref GameState s, int index)
        {
            if (index == 0) return ref s.F0;
            return ref s.F1;
        }

        public FighterState FighterAt(int index) => index == 0 ? F0 : F1;

        public int Score(int player) => player == 0 ? Score0 : Score1;

        public int SetsWon(int player) => player == 0 ? SetsWon0 : SetsWon1;

        public int Streak(int player) => player == 0 ? Streak0 : Streak1;

        public bool RunArmed(int player) => player == 0 ? RunArmed0 : RunArmed1;

        public void SetScore(int player, int value)
        {
            if (player == 0) Score0 = (byte)value;
            else Score1 = (byte)value;
        }

        public void SetStreak(int player, int value)
        {
            if (player == 0) Streak0 = (byte)value;
            else Streak1 = (byte)value;
        }

        public void SetRunArmed(int player, bool value)
        {
            if (player == 0) RunArmed0 = value;
            else RunArmed1 = value;
        }

        public void AddSetWon(int player)
        {
            if (player == 0) SetsWon0++;
            else SetsWon1++;
        }

        public int Write(Span<byte> destination)
        {
            var w = new StateWriter(destination);
            w.WriteInt32(LayoutVersion);
            w.WriteInt32(Tick);
            w.WriteInt32(World);
            w.WriteInt32(HitStop);
            w.WriteByte(SlowDiv);
            w.WriteByte(SlowPhase);
            w.WriteInt16(SlowFrames);
            w.WriteByte((byte)Phase);
            w.WriteInt32(PhaseStart);
            w.WriteByte(SetIndex);
            w.WriteByte(SetsWon0);
            w.WriteByte(SetsWon1);
            w.WriteByte(Score0);
            w.WriteByte(Score1);
            w.WriteByte(Server);
            w.WriteByte(FirstServerThisSet);
            w.WriteByte(Streak0);
            w.WriteByte(Streak1);
            w.WriteBool(RunArmed0);
            w.WriteBool(RunArmed1);
            w.WriteByte(LastPointWinner);
            w.WriteByte((byte)LastPointReason);
            w.WriteInt16(RallyIndex);
            w.WriteInt32(RallyStartWorld);
            w.WriteByte(Winner);
            w.WriteByte(Possession);
            w.WriteByte(Touches);
            w.WriteByte(LastToucher);
            w.WriteByte((byte)LastTouch);
            w.WriteByte(LastAttacker);
            w.WriteInt32(LastContactWorld);
            w.WriteByte(TouchesSinceCross);
            w.WriteByte(LastSender);
            w.WriteByte((byte)LastSendKind);
            Ball.Write(ref w);
            F0.Write(ref w);
            F1.Write(ref w);
            Fin.Write(ref w);
            MatchRng.Write(ref w);
            return w.Position;
        }

        public static GameState Read(ReadOnlySpan<byte> source)
        {
            var r = new StateReader(source);
            int version = r.ReadInt32();
            if (version != LayoutVersion) throw new InvalidOperationException("GameState layout " + version + " is not " + LayoutVersion);
            var s = new GameState
            {
                Tick = r.ReadInt32(),
                World = r.ReadInt32(),
                HitStop = r.ReadInt32(),
                SlowDiv = r.ReadByte(),
                SlowPhase = r.ReadByte(),
                SlowFrames = r.ReadInt16(),
                Phase = (MatchPhase)r.ReadByte(),
                PhaseStart = r.ReadInt32(),
                SetIndex = r.ReadByte(),
                SetsWon0 = r.ReadByte(),
                SetsWon1 = r.ReadByte(),
                Score0 = r.ReadByte(),
                Score1 = r.ReadByte(),
                Server = r.ReadByte(),
                FirstServerThisSet = r.ReadByte(),
                Streak0 = r.ReadByte(),
                Streak1 = r.ReadByte(),
                RunArmed0 = r.ReadBool(),
                RunArmed1 = r.ReadBool(),
                LastPointWinner = r.ReadByte(),
                LastPointReason = (PointReason)r.ReadByte(),
                RallyIndex = r.ReadInt16(),
                RallyStartWorld = r.ReadInt32(),
                Winner = r.ReadByte(),
                Possession = r.ReadByte(),
                Touches = r.ReadByte(),
                LastToucher = r.ReadByte(),
                LastTouch = (TouchKind)r.ReadByte(),
                LastAttacker = r.ReadByte(),
                LastContactWorld = r.ReadInt32(),
                TouchesSinceCross = r.ReadByte(),
                LastSender = r.ReadByte(),
                LastSendKind = (TouchKind)r.ReadByte(),
            };
            s.Ball.Read(ref r);
            s.F0.Read(ref r);
            s.F1.Read(ref r);
            s.Fin.Read(ref r);
            s.MatchRng.Read(ref r);
            return s;
        }
    }
}
