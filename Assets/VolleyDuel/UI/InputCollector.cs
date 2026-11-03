using Duel.Core.Sim;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VolleyDuel.UI
{
    /// <summary>
    /// Merges keyboard, gamepad and touch into one 16-bit FrameInput per tick. Stick X is relative to the
    /// net (positive = toward it), so a player on the right side pushes left to approach.
    /// Keyboard: A/D or arrows move, W/S up/down, J or Space HIT, K JUMP, L FEINT, U TRICK, I Amped TRICK,
    /// O FINISHER/DENY. Gamepad: left stick, South HIT, West JUMP, East FEINT, North TRICK, right
    /// shoulder Amp, either trigger FINISHER. Uses the Input System when its backend is on, and the
    /// legacy Input Manager otherwise (keyboard only), so input works under either project setting.
    /// </summary>
    public sealed class InputCollector
    {
        public TouchControls Touch;
        public int LocalPlayer;

        public FrameInput Sample()
        {
            float x = 0f, y = 0f;
            InputButtons b = InputButtons.None;
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1f;
                if (kb.jKey.isPressed || kb.spaceKey.isPressed) b |= InputButtons.Hit;
                if (kb.kKey.isPressed) b |= InputButtons.Jump;
                if (kb.lKey.isPressed) b |= InputButtons.Feint;
                if (kb.uKey.isPressed) b |= InputButtons.Trick;
                if (kb.iKey.isPressed) b |= InputButtons.Trick | InputButtons.Amp;
                if (kb.oKey.isPressed) b |= InputButtons.Finisher;
            }
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 s = pad.leftStick.ReadValue() + pad.dpad.ReadValue();
                if (Mathf.Abs(s.x) > Mathf.Abs(x)) x = s.x;
                if (Mathf.Abs(s.y) > Mathf.Abs(y)) y = s.y;
                if (pad.buttonSouth.isPressed) b |= InputButtons.Hit;
                if (pad.buttonWest.isPressed) b |= InputButtons.Jump;
                if (pad.buttonEast.isPressed) b |= InputButtons.Feint;
                if (pad.buttonNorth.isPressed) b |= InputButtons.Trick;
                if (pad.rightShoulder.isPressed) b |= InputButtons.Amp;
                if (pad.leftTrigger.isPressed || pad.rightTrigger.isPressed) b |= InputButtons.Finisher;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) y -= 1f;
            if (Input.GetKey(KeyCode.J) || Input.GetKey(KeyCode.Space)) b |= InputButtons.Hit;
            if (Input.GetKey(KeyCode.K)) b |= InputButtons.Jump;
            if (Input.GetKey(KeyCode.L)) b |= InputButtons.Feint;
            if (Input.GetKey(KeyCode.U)) b |= InputButtons.Trick;
            if (Input.GetKey(KeyCode.I)) b |= InputButtons.Trick | InputButtons.Amp;
            if (Input.GetKey(KeyCode.O)) b |= InputButtons.Finisher;
#endif
            if (Touch != null) Touch.Read(ref x, ref y, ref b);
            if ((b & InputButtons.Amp) != 0 && (b & InputButtons.Trick) == 0) b &= ~InputButtons.Amp;

            int sx = Mathf.RoundToInt(Mathf.Clamp(x, -1f, 1f) * FrameInput.StickMax);
            if (Mathf.Abs(x) < 0.15f) sx = 0;
            if (LocalPlayer == 1) sx = -sx;
            int sy = y > 0.5f ? 1 : (y < -0.5f ? -1 : 0);
            return FrameInput.Create(sx, sy, b);
        }
    }
}
