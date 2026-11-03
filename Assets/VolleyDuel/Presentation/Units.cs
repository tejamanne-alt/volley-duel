using Duel.Core.Sim;
using UnityEngine;

namespace VolleyDuel.Presentation
{
    /// <summary>
    /// Fixed point to Unity units. Presentation may use float freely; it only reads the sim.
    /// The sim is 2D (X along the court, Y up); the view adds depth on Z for the side camera.
    /// </summary>
    public static class Units
    {
        const double RawToFloat = 1.0 / 4294967296.0;

        public static float F(Fix64 v) => (float)(v.Raw * RawToFloat);

        public static Vector3 World(FixVec2 p, float z = 0f) => new Vector3(F(p.X), F(p.Y), z);

        /// <summary>Parses "#RRGGBB" (fighter identity colors in data); grey when malformed.</summary>
        public static Color Hex(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color c)) return c;
            return new Color(0.6f, 0.6f, 0.6f);
        }
    }
}
