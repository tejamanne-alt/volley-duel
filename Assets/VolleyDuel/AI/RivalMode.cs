using Duel.Core.AI;
using Duel.Core.Sim;

namespace VolleyDuel.AI
{
    /// <summary>
    /// Rival mode (spec): adjusts the CPU between sets to keep the player near a 50% win rate. The CPU's
    /// skill is a continuous position across the five tiers; each set nudges it toward the player's level.
    /// </summary>
    public sealed class RivalMode
    {
        readonly CpuTables tables;
        readonly RivalAdjuster adjuster;

        public RivalMode(CpuTables tables, int startTier)
        {
            this.tables = tables;
            int top = tables.Tiers.Length - 1;
            adjuster = new RivalAdjuster(Fix64.FromRatio(startTier, top), tables.RivalStep / top);
        }

        /// <summary>Continuous tier position, 0 = Rookie .. 4 = Legend.</summary>
        public Fix64 Skill => adjuster.Blend * (tables.Tiers.Length - 1);

        public TierProfile Current
        {
            get
            {
                Fix64 skill = Skill;
                int lo = skill.FloorToInt();
                int top = tables.Tiers.Length - 1;
                if (lo >= top) return tables.Tiers[top];
                if (lo < 0) return tables.Tiers[0];
                return TierProfile.Lerp(tables.Tiers[lo], tables.Tiers[lo + 1], skill - lo);
            }
        }

        /// <summary>Call at each set end with the player's point margin (positive when the player won the set).</summary>
        public void AfterSet(int playerMargin) => adjuster.AfterSet(playerMargin);

        /// <summary>Applies the current blend to a running CPU.</summary>
        public void Apply(VolleyCpu cpu) => cpu.Tier = Current;
    }
}
