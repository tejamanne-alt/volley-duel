using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    /// <summary>
    /// What one player can legitimately see of the match. The CPU reads only this view (after its
    /// reaction delay), and presentation uses the same rules, so neither can reveal what a human
    /// could not see: future randomness, an opponent's locked-in choice before it shows, a fake hop
    /// before it reads as fake, or a decoy's true flight.
    /// </summary>
    public static class Visibility
    {
        public static GameState ForViewer(in GameState truth, int viewer)
        {
            GameState v = truth;
            int other = 1 - viewer;

            // Future randomness and wobble the eye cannot know yet.
            v.MatchRng = default;
            v.Ball.Nudge0 = Fix64.Zero;
            v.Ball.Nudge1 = Fix64.Zero;
            v.Ball.Nudge2 = Fix64.Zero;
            v.Ball.PendingBlock = BlockOutcome.None;
            v.Ball.PendingBlockFrame = int.MinValue / 2;

            // A decoy flight (Kalari Twist, Amped) fools the defender until it fades.
            if (v.Ball.LaunchSide != viewer && truth.World < v.Ball.DecoyUntil) v.Ball.Vel = v.Ball.DecoyVel;

            ref FighterState o = ref GameState.Fighter(ref v, other);
            HideOpponent(ref o, truth.World);

            // The viewer's own block ring shows only from its reveal frame.
            ref FighterState me = ref GameState.Fighter(ref v, viewer);
            if (truth.World < me.BlockRingReveal) me.BlockRingCenter = int.MinValue / 2;
            return v;
        }

        static void HideOpponent(ref FighterState o, int world)
        {
            // Their buttons, rings and bait windows are not on this player's screen.
            o.Input = InputTracker.Create();
            o.Ring = RingKind.None;
            o.RingCenter = int.MinValue / 2;
            o.RingReachable = false;
            o.RingDive = false;
            o.BlockRingCenter = int.MinValue / 2;
            o.BlockRingReveal = int.MaxValue / 2;
            o.Bait = FeintKind.None;
            o.BaitStart = int.MinValue / 2;
            o.BaitEnd = int.MinValue / 2;
            // A swing looks like a spike until its choice locks.
            if (o.CommitKind != TouchKind.None && world < o.CommitLock)
            {
                if (o.CommitKind == TouchKind.Tip || o.CommitKind == TouchKind.Roll) o.CommitKind = TouchKind.Spike;
                if (o.Choice == AttackChoice.Tip || o.Choice == AttackChoice.Roll) o.Choice = AttackChoice.Spike;
                if (o.CommitKind == TouchKind.Dump) o.CommitKind = TouchKind.Set;
                o.CommitAim = 0;
            }
            // A fake hop reads as a block until it lands; a mirage reads as a real block.
            if (o.Jump == JumpKind.FakeHop) o.Jump = JumpKind.Block;
            if (world < o.DecoyUntil && o.ActiveTrick == TrickKind.Decoy)
            {
                o.Jump = JumpKind.Block;
                o.Action = FighterAction.Airborne;
                o.Pos.X = o.Pos.X.IsNegative ? -Fix64.FromRatio(4, 10) : Fix64.FromRatio(4, 10);
            }
            o.CommitGrade = TimingGrade.None;
        }
    }
}
