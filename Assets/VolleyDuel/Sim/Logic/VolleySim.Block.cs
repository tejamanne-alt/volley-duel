using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    public sealed partial class VolleySim
    {
        /// <summary>
        /// Called at a spike contact. If the defender is block-jumping at the net and the spike meets the
        /// hands, schedules the deflection for the frame the ball reaches the net. Spec: the block ring
        /// centers on the hitter's contact frame; Perfect (±2) stuffs, Good (±5) softens into a free ball
        /// on the blocker's side, later contact is tooled out for a point to the hitter.
        /// </summary>
        void CheckBlock(ref GameState s, int attacker)
        {
            if (s.Ball.Has(BallFlags.Unblockable)) return;
            int blocker = 1 - attacker;
            FighterState fb = s.FighterAt(blocker);
            if (!IsBlockJump(fb.Jump)) return;
            if (fb.Action != FighterAction.Airborne && fb.Action != FighterAction.JumpStartup) return;
            if (Fix64.Abs(fb.Pos.X) > T.Block.RangeFromNet) return;
            FighterDefinition db = Def(blocker);

            // Ball path to the net plane.
            BallState b = s.Ball;
            int crossFrame = 0;
            Fix64 crossY = Fix64.Zero;
            bool reaches = false;
            for (int k = 1; k <= 40; k++)
            {
                FixVec2 prev = b.Pos;
                IntegrateBall(ref b, s.World + k);
                if (SideOf(prev.X) != SideOf(b.Pos.X) || Fix64.Abs(b.Pos.X) <= T.Court.BallRadius)
                {
                    Fix64 span = prev.X - b.Pos.X;
                    Fix64 t = span.IsZero ? Fix64.Zero : prev.X / span;
                    crossY = prev.Y + (b.Pos.Y - prev.Y) * Fix64.Clamp01(t);
                    crossFrame = s.World + k;
                    reaches = true;
                    break;
                }
                if (b.Pos.Y <= T.Court.BallRadius) break;
            }
            if (!reaches) return;

            // Blocker's feet height at that frame.
            Fix64 feetY = BlockerFeetHeight(blocker, fb, s.World, crossFrame);
            if (feetY.IsNegative) return; // not off the floor by then
            Fix64 top = T.Court.NetHeight;
            Fix64 handTop = feetY + db.StandingReach + T.Block.HandTopAboveReach;
            Fix64 span2 = (handTop - top) * db.BlockSpanScale;
            handTop = top + span2;
            Fix64 r = T.Court.BallRadius;
            if (crossY - r > handTop) return;  // over the block
            if (crossY + r < top) return;      // into the net; the net handles it

            int delta = fb.ApexFrame - s.World;
            int perfectHalf = db.BlockPerfectHalf;
            int goodHalf = db.BlockGoodHalf;
            if (s.Ball.Has(BallFlags.HighSet))
            {
                int widen = (T.Set.HighBlockWiden + 1) / 2;
                perfectHalf += widen;
                goodHalf += widen;
            }
            // Commit Block covers both spike timings: hands still held over the net at the hitter's contact.
            // Whether the ball still meets them at the crossing is the spatial check above.
            bool hanging = fb.Jump == JumpKind.CommitBlock && fb.HangUntil >= s.World;
            int abs = delta < 0 ? -delta : delta;
            BlockOutcome outcome;
            if (hanging || abs <= perfectHalf) outcome = BlockOutcome.Stuff;
            else if (abs <= goodHalf) outcome = db.GoodBlocksStuff ? BlockOutcome.Stuff : BlockOutcome.Soft;
            else outcome = BlockOutcome.Tooled;
            if (outcome == BlockOutcome.Stuff && s.Ball.Has(BallFlags.CantStuff)) outcome = BlockOutcome.Soft;

            s.Ball.PendingBlock = outcome;
            s.Ball.PendingBlockFrame = crossFrame;
            s.Ball.PendingBlocker = (byte)blocker;
        }

        /// <summary>Predicted feet height of a block-jumping fighter at <paramref name="frame"/>; negative if grounded then.</summary>
        Fix64 BlockerFeetHeight(int blocker, FighterState fb, int now, int frame)
        {
            Fix64 y = fb.Pos.Y;
            Fix64 vy = fb.Vel.Y;
            int start = now;
            int hangUntil = fb.HangUntil;
            const int unset = int.MinValue / 2;
            if (fb.Action == FighterAction.JumpStartup)
            {
                if (frame < fb.ActionEnd) return -Fix64.One;
                vy = Fix64.Sqrt(2 * T.Ball.Gravity * fb.JumpHeight);
                start = fb.ActionEnd;
                y = Fix64.Zero;
                hangUntil = unset;
            }
            TrickDefinition commit = fb.Jump == JumpKind.CommitBlock ? TrickFor(blocker, TrickKind.CommitBlock, fb.ActiveTrickAmped) : null;
            for (int w = start + 1; w <= frame; w++)
            {
                // Mirrors StepAirborne, including Commit Block's hang at the apex.
                if (commit != null && hangUntil > w && vy <= Fix64.Zero) continue;
                y += vy * T.Dt - T.HalfGravityDt2;
                vy -= T.GravityDt;
                if (commit != null && hangUntil == unset && vy <= Fix64.Zero) hangUntil = w + commit.HangFrames;
                if (y < Fix64.Zero) return -Fix64.One;
            }
            return y;
        }

        void ApplyPendingBlock(ref GameState s)
        {
            ref BallState b = ref s.Ball;
            BlockOutcome outcome = b.PendingBlock;
            int blocker = b.PendingBlocker;
            int hitter = 1 - blocker;
            b.PendingBlock = BlockOutcome.None;
            ref FighterState fb = ref GameState.Fighter(ref s, blocker);
            FighterDefinition db = Def(blocker);
            Fix64 r = T.Court.BallRadius;
            var at = new FixVec2(FromNet(hitter, r + Fix64.FromRatio(2, 100)), Fix64.Max(b.Pos.Y, T.Court.NetHeight + r));
            int delta = fb.ApexFrame - s.LastContactWorld;
            FixVec2 v;
            BallFlags keep = b.Flags & BallFlags.HeavyBall;
            switch (outcome)
            {
                case BlockOutcome.Stuff:
                    Ballistics.SolveSpeed(at, new FixVec2(FromNet(hitter, Fix64.FromRatio(9, 10)), r), T.Block.StuffSpeed, T.Ball.Gravity, true, out v);
                    b.Pos = at;
                    Launch(ref s, blocker, v, BallFlags.None, BallSpin.None);
                    b.LaunchSide = (byte)blocker;
                    s.Possession = (byte)hitter;
                    break;
                case BlockOutcome.Soft:
                    {
                        var target = new FixVec2(FromNet(blocker, Fix64.FromRatio(16, 10)), db.Height + T.Reach.SetHeightAboveHead);
                        Ballistics.SolveApex(at, T.Court.NetHeight + Fix64.FromRatio(18, 10), target, T.Ball.Gravity, out v);
                        b.Pos = at;
                        Launch(ref s, blocker, v, BallFlags.FreeBall, BallSpin.None);
                        s.Possession = (byte)blocker;
                        break;
                    }
                default:
                    {
                        var target = new FixVec2(FromNet(blocker, T.Court.HalfLength + T.Block.ToolDepthPastLine), r);
                        Ballistics.SolveApex(at, T.Block.ToolPeak, target, T.Ball.Gravity, out v);
                        b.Pos = at;
                        Launch(ref s, blocker, v, BallFlags.None, BallSpin.None);
                        s.Possession = (byte)blocker;
                        break;
                    }
            }
            s.Touches = 0; // spec: a block touch is free
            s.LastToucher = (byte)blocker;
            s.LastTouch = TouchKind.Block;
            s.LastContactWorld = s.World;
            fb.BlockedLast = outcome != BlockOutcome.Tooled;
            Emit(VolleyEvent.BlockTouch, blocker, (int)outcome, delta, at.X, at.Y);
            ApplyBlockTrickEffects(ref s, blocker, hitter, outcome, delta);
            if (outcome == BlockOutcome.Stuff)
            {
                GainSurge(ref s, blocker, T.Meters.GainStuff);
                Impact(ref s, Callout.Stuffed, blocker, at);
            }
            else if (outcome == BlockOutcome.Tooled)
            {
                Impact(ref s, Callout.Tooled, hitter, at);
            }
        }
    }
}
