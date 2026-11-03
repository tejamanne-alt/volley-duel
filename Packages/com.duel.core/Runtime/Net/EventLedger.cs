using System.Collections.Generic;
using Duel.Core.Sim;

namespace Duel.Core.Net
{
    /// <summary>
    /// Rollback-aware presentation support (spec): an event ledger keyed by frame, type, actor and sequence.
    /// After a resimulation, events that no longer happen are reported as orphaned (fade them out fast) and
    /// events that are new are reported with how late they are (spawn them with a time offset). Events seen
    /// again with the same key are not replayed.
    /// </summary>
    public sealed class EventLedger
    {
        readonly Dictionary<ulong, SimEvent> played = new Dictionary<ulong, SimEvent>();
        readonly List<ulong> scratch = new List<ulong>();
        readonly HashSet<ulong> seenThisTick = new HashSet<ulong>();

        /// <summary>Frames kept for comparison; must cover the rollback window.</summary>
        public int Window = 16;

        public readonly List<SimEvent> Spawned = new List<SimEvent>();
        public readonly List<SimEvent> Orphaned = new List<SimEvent>();

        /// <summary>
        /// Reconcile one tick's events. <paramref name="resimulatedFrom"/> is the first sim frame that was re-run
        /// this tick (-1 when there was no rollback). Events carry the post-step tick, so re-running frame f
        /// produces events stamped f + 1 and later; played events in that range that were not produced again are
        /// orphaned.
        /// </summary>
        public void Reconcile(SimEventBuffer events, int resimulatedFrom, int currentFrame)
        {
            Spawned.Clear();
            Orphaned.Clear();
            seenThisTick.Clear();
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                seenThisTick.Add(e.Key);
                if (played.ContainsKey(e.Key)) continue;
                played[e.Key] = e;
                Spawned.Add(e);
            }
            if (resimulatedFrom >= 0)
            {
                scratch.Clear();
                foreach (KeyValuePair<ulong, SimEvent> kv in played)
                {
                    if (kv.Value.Frame > resimulatedFrom && !seenThisTick.Contains(kv.Key)) scratch.Add(kv.Key);
                }
                foreach (ulong k in scratch)
                {
                    Orphaned.Add(played[k]);
                    played.Remove(k);
                }
            }
            scratch.Clear();
            foreach (KeyValuePair<ulong, SimEvent> kv in played)
            {
                if (kv.Value.Frame < currentFrame - Window) scratch.Add(kv.Key);
            }
            foreach (ulong k in scratch) played.Remove(k);
        }
    }
}
