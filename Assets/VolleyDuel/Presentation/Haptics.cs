using UnityEngine;

namespace VolleyDuel.Presentation
{
    /// <summary>
    /// Thin haptics hook. A Core Haptics and Android vibration plugin replaces the fallback later; until
    /// then mobile builds use the platform's single buzz for medium and heavy hits.
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
            if (!Enabled || strength == Strength.Light) return;
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }
    }
}
