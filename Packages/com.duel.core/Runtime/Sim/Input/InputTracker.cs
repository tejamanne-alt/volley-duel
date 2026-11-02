namespace Duel.Core.Sim
{
    /// <summary>
    /// Per-player input buffer that lives in the snapshot: press and release edges stamped with
    /// the world frame they take effect on, plus the last non-neutral stick direction. Games
    /// read it to implement an N-frame press buffer and "direction and button within K frames
    /// count as simultaneous".
    /// </summary>
    public struct InputTracker
    {
        public const int Never = int.MinValue / 2;
        const int ButtonCount = 6;
        const int FirstButtonBit = 6;

        public ushort Held;
        public sbyte StickX;
        public sbyte StickY;
        public sbyte LastNonNeutralStickX;
        public int LastNonNeutralFrame;
        public int Press0, Press1, Press2, Press3, Press4, Press5;
        public int Release0, Release1, Release2, Release3, Release4, Release5;

        public static InputTracker Create()
        {
            var t = new InputTracker
            {
                LastNonNeutralFrame = Never,
                Press0 = Never,
                Press1 = Never,
                Press2 = Never,
                Press3 = Never,
                Press4 = Never,
                Press5 = Never,
                Release0 = Never,
                Release1 = Never,
                Release2 = Never,
                Release3 = Never,
                Release4 = Never,
                Release5 = Never,
            };
            return t;
        }

        /// <summary>Records <paramref name="input"/> as taking effect on world frame <paramref name="frame"/>.</summary>
        public void Record(FrameInput input, int frame)
        {
            ushort now = (ushort)input.Buttons;
            ushort pressed = (ushort)(now & ~Held);
            ushort released = (ushort)(Held & ~now);
            for (int i = 0; i < ButtonCount; i++)
            {
                ushort bit = (ushort)(1 << (FirstButtonBit + i));
                if ((pressed & bit) != 0) SetPress(i, frame);
                if ((released & bit) != 0) SetRelease(i, frame);
            }
            Held = now;
            StickX = (sbyte)input.StickX;
            StickY = (sbyte)input.StickY;
            if (input.StickX != 0)
            {
                LastNonNeutralStickX = (sbyte)input.StickX;
                LastNonNeutralFrame = frame;
            }
        }

        public bool IsHeld(InputButtons button) => (Held & (ushort)button) != 0;

        /// <summary>World frame of the last unconsumed press, or <see cref="Never"/>.</summary>
        public int PressFrame(InputButtons button) => GetPress(IndexOf(button));

        public int ReleaseFrame(InputButtons button) => GetRelease(IndexOf(button));

        /// <summary>True when the button was pressed within the last <paramref name="window"/> frames and not consumed.</summary>
        public bool Buffered(InputButtons button, int frame, int window)
        {
            int p = GetPress(IndexOf(button));
            return p != Never && frame - p < window && frame >= p;
        }

        public void Consume(InputButtons button) => SetPress(IndexOf(button), Never);

        public void ConsumeRelease(InputButtons button) => SetRelease(IndexOf(button), Never);

        /// <summary>
        /// Stick X to use for a commitment at <paramref name="frame"/>: the current stick if
        /// non-neutral, otherwise the last non-neutral value if it is at most
        /// <paramref name="window"/> frames old.
        /// </summary>
        public int AimStickX(int frame, int window)
        {
            if (StickX != 0) return StickX;
            if (LastNonNeutralFrame != Never && frame - LastNonNeutralFrame <= window) return LastNonNeutralStickX;
            return 0;
        }

        static int IndexOf(InputButtons button)
        {
            ushort b = (ushort)button;
            for (int i = 0; i < ButtonCount; i++)
            {
                if (b == (ushort)(1 << (FirstButtonBit + i))) return i;
            }
            return -1;
        }

        int GetPress(int i)
        {
            switch (i)
            {
                case 0: return Press0;
                case 1: return Press1;
                case 2: return Press2;
                case 3: return Press3;
                case 4: return Press4;
                case 5: return Press5;
                default: return Never;
            }
        }

        void SetPress(int i, int v)
        {
            switch (i)
            {
                case 0: Press0 = v; break;
                case 1: Press1 = v; break;
                case 2: Press2 = v; break;
                case 3: Press3 = v; break;
                case 4: Press4 = v; break;
                case 5: Press5 = v; break;
            }
        }

        int GetRelease(int i)
        {
            switch (i)
            {
                case 0: return Release0;
                case 1: return Release1;
                case 2: return Release2;
                case 3: return Release3;
                case 4: return Release4;
                case 5: return Release5;
                default: return Never;
            }
        }

        void SetRelease(int i, int v)
        {
            switch (i)
            {
                case 0: Release0 = v; break;
                case 1: Release1 = v; break;
                case 2: Release2 = v; break;
                case 3: Release3 = v; break;
                case 4: Release4 = v; break;
                case 5: Release5 = v; break;
            }
        }

        public void Write(ref StateWriter w)
        {
            w.WriteUInt16(Held);
            w.WriteSByte(StickX);
            w.WriteSByte(StickY);
            w.WriteSByte(LastNonNeutralStickX);
            w.WriteInt32(LastNonNeutralFrame);
            w.WriteInt32(Press0); w.WriteInt32(Press1); w.WriteInt32(Press2);
            w.WriteInt32(Press3); w.WriteInt32(Press4); w.WriteInt32(Press5);
            w.WriteInt32(Release0); w.WriteInt32(Release1); w.WriteInt32(Release2);
            w.WriteInt32(Release3); w.WriteInt32(Release4); w.WriteInt32(Release5);
        }

        public void Read(ref StateReader r)
        {
            Held = r.ReadUInt16();
            StickX = r.ReadSByte();
            StickY = r.ReadSByte();
            LastNonNeutralStickX = r.ReadSByte();
            LastNonNeutralFrame = r.ReadInt32();
            Press0 = r.ReadInt32(); Press1 = r.ReadInt32(); Press2 = r.ReadInt32();
            Press3 = r.ReadInt32(); Press4 = r.ReadInt32(); Press5 = r.ReadInt32();
            Release0 = r.ReadInt32(); Release1 = r.ReadInt32(); Release2 = r.ReadInt32();
            Release3 = r.ReadInt32(); Release4 = r.ReadInt32(); Release5 = r.ReadInt32();
        }
    }
}
