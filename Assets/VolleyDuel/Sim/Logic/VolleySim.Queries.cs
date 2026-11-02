using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    /// <summary>
    /// Pure "what if" queries over a state, for the CPU and presentation hints. They never change
    /// the state they are given and use the same physics as the step.
    /// </summary>
    public sealed partial class VolleySim
    {
        /// <summary>Frames from takeoff to the apex of a jump of the given height.</summary>
        public int FramesToApex(Fix64 jumpHeight) => (Fix64.Sqrt(2 * T.Ball.Gravity * jumpHeight) / T.GravityDt).CeilToInt();

        /// <summary>Jump height this fighter would get now (Gassed reduces it).</summary>
        public Fix64 CurrentJumpHeight(in GameState s, int player, bool block)
        {
            FighterDefinition d = Def(player);
            Fix64 h = block ? d.BlockJumpHeight : d.JumpHeight;
            if (s.FighterAt(player).Gassed) h = h * T.Meters.GassedJumpScale;
            return h;
        }

        /// <summary>
        /// If the player pressed JUMP (approach) at world frame <paramref name="jumpFrame"/> standing at
        /// <paramref name="takeoffX"/>, where and when would the spiking hand pass closest to the ball?
        /// </summary>
        public bool PlanJumpContact(in GameState s, int player, int jumpFrame, Fix64 takeoffX, out int contactFrame, out FixVec2 contactPoint, out Fix64 distanceSquared)
        {
            contactFrame = 0;
            contactPoint = FixVec2.Zero;
            distanceSquared = Fix64.MaxValue;
            if (s.Ball.Mode != BallMode.Flight) return false;
            BallState b = s.Ball;
            int takeoff = jumpFrame + T.Jump.StartupFrames;
            Fix64 vy = Fix64.Sqrt(2 * T.Ball.Gravity * CurrentJumpHeight(s, player, false));
            var feet = new FixVec2(takeoffX, Fix64.Zero);
            for (int w = s.World + 1; w <= takeoff + 90; w++)
            {
                if (IntegrateBall(ref b, w) == NetResult.Cord) break;
                if (w > takeoff)
                {
                    feet.Y += vy * T.Dt - T.HalfGravityDt2;
                    vy -= T.GravityDt;
                    if (feet.Y <= Fix64.Zero) break;
                    FixVec2 hand = SpikeHand(feet, player);
                    Fix64 d2 = FixVec2.DistanceSquared(hand, b.Pos);
                    if (d2 < distanceSquared)
                    {
                        distanceSquared = d2;
                        contactFrame = w;
                        contactPoint = b.Pos;
                    }
                }
                if (b.Pos.Y <= T.Court.BallRadius) break;
            }
            Fix64 r = T.Reach.SpikeReachRadius;
            return distanceSquared <= r * r;
        }

        /// <summary>Where the ball would first touch the floor if nothing touches it. False if it clips the cord first.</summary>
        public bool PredictLanding(in GameState s, out int frame, out Fix64 x)
        {
            frame = 0;
            x = Fix64.Zero;
            if (s.Ball.Mode != BallMode.Flight) return false;
            BallState b = s.Ball;
            for (int k = 1; k <= T.Ball.PredictionHorizon * 2; k++)
            {
                if (IntegrateBall(ref b, s.World + k) == NetResult.Cord) return false;
                if (b.Pos.Y <= T.Court.BallRadius)
                {
                    frame = s.World + k;
                    x = b.Pos.X;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Highest point the current flight will reach (or the current height if already falling).</summary>
        public Fix64 PredictApex(in GameState s, out int frame)
        {
            frame = s.World;
            BallState b = s.Ball;
            Fix64 best = b.Pos.Y;
            for (int k = 1; k <= 150 && b.Vel.Y.IsPositive; k++)
            {
                IntegrateBall(ref b, s.World + k);
                if (b.Pos.Y > best)
                {
                    best = b.Pos.Y;
                    frame = s.World + k;
                }
            }
            return best;
        }
    }
}
