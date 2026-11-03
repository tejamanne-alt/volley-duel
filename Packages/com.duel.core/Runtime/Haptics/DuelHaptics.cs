using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Duel.Core.Haptics
{
    public enum HapticStrength : byte
    {
        Light,
        Medium,
        Heavy,
    }

    /// <summary>
    /// Thin native haptics plugin (spec). On iOS it uses impact feedback through UIKit's feedback
    /// generators (Plugins/iOS/DuelHaptics.swift). On Android it uses the vibrator service (the
    /// VIBRATE permission is added at build time). Elsewhere it does nothing. It deliberately avoids
    /// Handheld.Vibrate, whose engine module newer Unity versions no longer ship.
    /// </summary>
    public static class DuelHaptics
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void duel_haptics_impact(int strength);
#endif

        public static void Impact(HapticStrength strength)
        {
#if UNITY_IOS && !UNITY_EDITOR
            duel_haptics_impact((int)strength);
#elif UNITY_ANDROID && !UNITY_EDITOR
            AndroidVibrate(strength);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator;
        static bool unavailable;

        static void AndroidVibrate(HapticStrength strength)
        {
            if (unavailable) return;
            try
            {
                if (vibrator == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    {
                        vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    }
                    if (vibrator == null)
                    {
                        unavailable = true;
                        return;
                    }
                }
                long millis = strength == HapticStrength.Heavy ? 40L : (strength == HapticStrength.Medium ? 22L : 10L);
                vibrator.Call("vibrate", millis);
            }
            catch (AndroidJavaException)
            {
                // Haptics are a nicety: a phone without a vibrator, or without the permission, plays on.
                unavailable = true;
            }
        }
#endif
    }
}
