using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    /// <summary>
    /// Build fingerprint exchanged in the handshake: sim code hash, data hash and state layout.
    /// Two phones play each other only when these match exactly.
    /// </summary>
    public static class SimVersion
    {
        public static ulong Compute(SimTables tables)
        {
            ulong h = StateHash.OffsetBasis;
            h = StateHash.Add(h, SimCodeHash.Value);
            h = StateHash.Add(h, tables.DataHash);
            h = StateHash.Add(h, (ulong)GameState.LayoutVersion);
            return h;
        }
    }
}
