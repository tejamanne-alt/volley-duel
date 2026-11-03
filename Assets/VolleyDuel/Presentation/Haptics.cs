using Duel.Core.Haptics;

namespace VolleyDuel.Presentation
{
    /// <summary>
    /// Haptics for impacts, through Duel Core's thin native plugin: impact feedback on iOS and the
    /// vibrator on Android. Settings can turn it off.
    /// </summary>
    public static class Haptics
    {
        public enum Strength : byte
        {
            Light,
            Medium,
            Heavy,
        }

        public static bool Enabled = true;

        public static void Pulse(Strength strength)
        {
            if (!Enabled) return;
            DuelHaptics.Impact((HapticStrength)strength);
        }
    }
}
