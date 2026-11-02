using System;

namespace Duel.Core.Sim
{
    /// <summary>Button bits of <see cref="FrameInput"/>. Games give them meaning by phase of play.</summary>
    [Flags]
    public enum InputButtons : ushort
    {
        None = 0,
        /// <summary>Primary action (bump, set, spike, dig, serve contact).</summary>
        Hit = 1 << 6,
        Jump = 1 << 7,
        Feint = 1 << 8,
        Trick = 1 << 9,
        /// <summary>Modifier sent with Trick when the player swipes up from the button.</summary>
        Amp = 1 << 10,
        /// <summary>Finisher; doubles as Deny during the opponent's Finisher.</summary>
        Finisher = 1 << 11,
        All = Hit | Jump | Feint | Trick | Amp | Finisher,
    }

    /// <summary>
    /// One player's input for one sim frame, packed into 16 bits:
    /// bits 0-3 stick X (signed, -7..7, positive = toward the net),
    /// bits 4-5 stick Y (0 neutral, 1 up, 2 down), bits 6-11 buttons, bits 12-15 reserved (zero).
    /// Stick X is relative to the player's facing so the sim is mirror-symmetric; the input
    /// layer maps screen directions to forward/back.
    /// </summary>
    public readonly struct FrameInput : IEquatable<FrameInput>
    {
        public const int StickMax = 7;
        const ushort StickXMask = 0x000F;
        const ushort StickYMask = 0x0030;
        const ushort ButtonMask = (ushort)InputButtons.All;

        public readonly ushort Bits;

        public FrameInput(ushort bits)
        {
            Bits = (ushort)(bits & (StickXMask | StickYMask | ButtonMask));
        }

        public static readonly FrameInput Neutral = new FrameInput(0);

        public static FrameInput Create(int stickX, int stickY, InputButtons buttons)
        {
            if (stickX > StickMax) stickX = StickMax;
            if (stickX < -StickMax) stickX = -StickMax;
            int y = stickY > 0 ? 1 : (stickY < 0 ? 2 : 0);
            ushort bits = (ushort)((stickX & 0xF) | (y << 4) | ((ushort)buttons & ButtonMask));
            return new FrameInput(bits);
        }

        /// <summary>Stick X in -7..7; positive is toward the net.</summary>
        public int StickX
        {
            get
            {
                int raw = Bits & StickXMask;
                return raw >= 8 ? raw - 16 : raw;
            }
        }

        /// <summary>Stick Y: 1 up, -1 down, 0 neutral.</summary>
        public int StickY
        {
            get
            {
                int raw = (Bits & StickYMask) >> 4;
                return raw == 1 ? 1 : (raw == 2 ? -1 : 0);
            }
        }

        public InputButtons Buttons => (InputButtons)(Bits & ButtonMask);

        public bool Has(InputButtons button) => (Bits & (ushort)button) != 0;

        public FrameInput WithButtons(InputButtons buttons) => Create(StickX, StickY, buttons);

        public FrameInput WithStick(int stickX, int stickY) => Create(stickX, stickY, Buttons);

        public bool Equals(FrameInput other) => Bits == other.Bits;
        public override bool Equals(object obj) => obj is FrameInput other && Bits == other.Bits;
        public override int GetHashCode() => Bits;
        public static bool operator ==(FrameInput a, FrameInput b) => a.Bits == b.Bits;
        public static bool operator !=(FrameInput a, FrameInput b) => a.Bits != b.Bits;

        public override string ToString() => "stick(" + StickX + "," + StickY + ") " + Buttons;
    }
}
