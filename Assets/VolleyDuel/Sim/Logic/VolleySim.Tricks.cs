using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    /// <summary>What a fighter committed to, for feint bait checks.</summary>
    public enum Commitment : byte
    {
        Block,
        Dive,
        Tip,
    }

    public sealed partial class VolleySim
    {
        // ------------------------------------------------------------------ feints

        /// <summary>
        /// Spec: fake spikes, fake sets and fake blocks each have a bait window. A commitment that starts
        /// inside the opponent's bait window bites: -15 Stamina and 8 extra frames of landing recovery.
        /// </summary>
        void CheckBite(ref GameState s, int committer, Commitment commitment)
        {
            int other = 1 - committer;
            ref FighterState feinter = ref GameState.Fighter(ref s, other);
            if (feinter.Bait == FeintKind.None) return;
            if (s.World < feinter.BaitStart || s.World > feinter.BaitEnd) return;
            bool bites;
            switch (commitment)
            {
                case Commitment.Block: bites = feinter.Bait == FeintKind.Tip || feinter.Bait == FeintKind.Dump; break;
                case Commitment.Dive: bites = feinter.Bait == FeintKind.Dump; break;
                default: bites = feinter.Bait == FeintKind.FakeBlock || feinter.Bait == FeintKind.Decoy; break;
            }
            if (!bites) return;
            FeintKind kind = feinter.Bait;
            feinter.Bait = FeintKind.None;
            ref FighterState f = ref GameState.Fighter(ref s, committer);
            int extra = f.ExtraRecovery + T.Feint.BiteRecovery;
            f.ExtraRecovery = (byte)(extra > 255 ? 255 : extra);
            SpendStamina(ref s, committer, Fix64.FromInt(T.Feint.BiteStamina));
            Emit(VolleyEvent.Bite, committer, (int)kind, 0, f.Pos.X, f.Pos.Y);
            GainSurge(ref s, other, T.Meters.GainBiteDrawn);
        }

        // ------------------------------------------------------------------ tricks (kits land in task 4)

        /// <summary>The trick definition (Amped when requested) if this fighter owns it.</summary>
        public TrickDefinition TrickFor(int player, TrickKind kind, bool amped)
        {
            if (kind == TrickKind.None) return null;
            TrickDefinition t = Def(player).Trick(kind);
            if (t == null) return null;
            return amped ? t.Amped : t;
        }

        bool TryGroundTrick(ref GameState s, int i) => false;

        bool TryServeTrick(ref GameState s, int i) => false;

        bool TryAirTrick(ref GameState s, int i) => false;

        Fix64 SlideDrift(ref GameState s, int i, Fix64 vy) => Fix64.Zero;

        void StartDecoyFlight(ref GameState s, int i, TrickDefinition trick, Fix64 speed)
        {
        }

        void ApplyBlockTrickEffects(ref GameState s, int blocker, int hitter, BlockOutcome outcome, int delta)
        {
        }

        bool TryJoust(ref GameState s) => false;

        bool TryFinisher(ref GameState s, int i) => false;

        void StepFinisher(ref GameState s)
        {
        }
    }
}
