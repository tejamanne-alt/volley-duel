using System;
using System.Collections.Generic;

namespace VolleyDuel.Gates
{
    /// <summary>
    /// Deliberately breaks every determinism rule so the gate's self-test can prove the scan
    /// detects each category. Never reference this from game code.
    /// </summary>
    internal static class GateCanary
    {
        internal static float FloatField = 1.5f;

        internal static double Halve(int x) => x * 0.5;

        internal static long Clock() => DateTime.UtcNow.Ticks;

        internal static int Dice() => new Random(1).Next();

        internal static int Lookup(Dictionary<int, int> table) => table.Count;
    }
}
