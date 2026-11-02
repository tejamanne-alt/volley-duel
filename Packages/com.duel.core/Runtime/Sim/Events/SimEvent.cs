using System;

namespace Duel.Core.Sim
{
    /// <summary>
    /// Something the sim reports for presentation, audio, haptics and stats. Identified by
    /// (frame, type, actor, sequence) so a rollback-aware presentation can tell a re-simulated
    /// event it already played from a new one. Payload fields are game-defined.
    /// </summary>
    public struct SimEvent
    {
        public const byte NoActor = 255;

        public int Frame;
        public ushort Type;
        public byte Actor;
        public byte Sequence;
        public int A;
        public int B;
        public Fix64 X;
        public Fix64 Y;

        /// <summary>Unique key of this event within a match timeline.</summary>
        public ulong Key => ((ulong)(uint)Frame << 32) | ((ulong)Type << 16) | ((ulong)Actor << 8) | Sequence;

        public override string ToString() => "f" + Frame + " t" + Type + " a" + Actor + " #" + Sequence + " (" + A + "," + B + ") @" + X.ToString(2) + "," + Y.ToString(2);
    }

    /// <summary>Reusable, allocation-free list of events produced by one or more steps.</summary>
    public sealed class SimEventBuffer
    {
        SimEvent[] items = new SimEvent[64];
        int count;
        int sequenceFrame = int.MinValue;
        int sequence;

        public int Count => count;

        public ref readonly SimEvent this[int index]
        {
            get
            {
                if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index));
                return ref items[index];
            }
        }

        public void Clear()
        {
            count = 0;
            sequenceFrame = int.MinValue;
            sequence = 0;
        }

        public void Add(int frame, ushort type, byte actor, int a = 0, int b = 0, Fix64 x = default, Fix64 y = default)
        {
            if (frame != sequenceFrame)
            {
                sequenceFrame = frame;
                sequence = 0;
            }
            if (count == items.Length) Array.Resize(ref items, items.Length * 2);
            items[count++] = new SimEvent
            {
                Frame = frame,
                Type = type,
                Actor = actor,
                Sequence = (byte)(sequence < 255 ? sequence : 255),
                A = a,
                B = b,
                X = x,
                Y = y,
            };
            sequence++;
        }

        public void CopyTo(SimEventBuffer other)
        {
            for (int i = 0; i < count; i++)
            {
                ref readonly SimEvent e = ref items[i];
                other.Add(e.Frame, e.Type, e.Actor, e.A, e.B, e.X, e.Y);
            }
        }
    }
}
