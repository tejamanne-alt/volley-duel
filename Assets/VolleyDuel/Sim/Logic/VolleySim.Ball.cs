using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    public enum NetResult : byte
    {
        None,
        Crossed,
        Bounced,
        /// <summary>The ball clipped the net cord; the outcome needs the match RNG.</summary>
        Cord,
    }

    public sealed partial class VolleySim
    {
        /// <summary>
        /// Moves a ball one world frame: float wobble, gravity (plus topspin dip once over the net), wind,
        /// and the net. Shared by the sim and every prediction, so predictions match the real flight exactly.
        /// </summary>
        public NetResult IntegrateBall(ref BallState b, int world)
        {
            if (b.Spin == BallSpin.Float)
            {
                int age = world - b.FlightStart - T.Ball.FloatWobbleFirstFrame;
                if (age >= 0 && age % T.Ball.FloatWobbleInterval == 0)
                {
                    int k = age / T.Ball.FloatWobbleInterval;
                    if (k < T.Ball.FloatWobbleNudges) b.Vel.X += b.Nudge(k);
                }
            }
            FixVec2 prev = b.Pos;
            b.Pos.X += b.Vel.X * T.Dt;
            b.Pos.Y += b.Vel.Y * T.Dt - T.HalfGravityDt2;
            b.Vel.Y -= T.GravityDt;
            if (b.CrossedNet && !b.DipGravity.IsZero)
            {
                b.Pos.Y -= b.DipGravity * T.Dt * T.Dt / 2;
                b.Vel.Y -= b.DipGravity * T.Dt;
            }
            if (!b.Wind.IsZero) b.Vel.X += b.Wind * T.Dt;

            if (SideOf(prev.X) == SideOf(b.Pos.X)) return NetResult.None;
            Fix64 span = prev.X - b.Pos.X;
            Fix64 t = span.IsZero ? Fix64.Zero : prev.X / span;
            Fix64 crossY = prev.Y + (b.Pos.Y - prev.Y) * t;
            Fix64 top = T.Court.NetHeight;
            Fix64 r = T.Court.BallRadius;
            if (crossY < top - r)
            {
                // Into the net body: dies and drops on the side it came from.
                b.Pos.X = prev.X.IsNegative ? -r : r;
                b.Pos.Y = crossY;
                b.Vel.X = -b.Vel.X * T.Ball.NetRestitution;
                b.Vel.Y = b.Vel.Y / 2;
                return NetResult.Bounced;
            }
            if (crossY < top + r) return NetResult.Cord;
            b.CrossedNet = true;
            return NetResult.Crossed;
        }

        void StepBall(ref GameState s)
        {
            ref BallState b = ref s.Ball;
            if (b.Mode != BallMode.Flight) return;
            if (b.PendingBlock != BlockOutcome.None && s.World >= b.PendingBlockFrame)
            {
                ApplyPendingBlock(ref s);
                if (s.Phase != MatchPhase.Rally) return;
            }
            FixVec2 prev = b.Pos;
            NetResult net = IntegrateBall(ref b, s.World);
            if (net == NetResult.Cord)
            {
                ResolveNetCord(ref s, prev);
                net = SideOf(prev.X) == SideOf(b.Pos.X) ? NetResult.Bounced : NetResult.Crossed;
            }
            if (net == NetResult.Bounced)
            {
                int speed = (b.Vel.Length * 100).RoundToInt();
                Emit(VolleyEvent.NetHit, s.LastToucher, speed, 0, b.Pos.X, b.Pos.Y);
            }
            else if (net == NetResult.Crossed)
            {
                OnNetCross(ref s);
            }
            if (b.Pos.Y <= T.Court.BallRadius) BallLanded(ref s);
        }

        void ResolveNetCord(ref GameState s, FixVec2 prev)
        {
            ref BallState b = ref s.Ball;
            bool over = s.MatchRng.Chance(T.Ball.NetCordOverChance);
            Fix64 r = T.Court.BallRadius;
            Fix64 pop = Fix64.Abs(b.Vel.Y) * Fix64.FromRatio(3, 10) + Fix64.FromRatio(6, 5);
            if (over)
            {
                b.Vel.X = b.Vel.X * Fix64.FromRatio(35, 100);
                b.Pos.X = prev.X.IsNegative ? r : -r;
                b.CrossedNet = true;
            }
            else
            {
                b.Vel.X = -b.Vel.X * Fix64.FromRatio(3, 10);
                b.Pos.X = prev.X.IsNegative ? -r : r;
            }
            b.Pos.Y = T.Court.NetHeight + r;
            b.Vel.Y = pop;
            Emit(VolleyEvent.NetCord, s.LastToucher, over ? 1 : 0, 0, b.Pos.X, b.Pos.Y);
        }

        void OnNetCross(ref GameState s)
        {
            int entered = SideOf(s.Ball.Pos.X);
            // Spec: three touches a side; crossing the net hands the opponent a fresh count.
            s.Possession = (byte)entered;
            s.Touches = 0;
            s.TouchesSinceCross = 0;
            s.LastSender = s.LastToucher;
            s.LastSendKind = s.LastTouch;
            if (s.LastTouch == TouchKind.Bump && s.Ball.Has(BallFlags.Serve) == false) s.Ball.Flags |= BallFlags.Overpass;
            if (s.Ball.Has(BallFlags.SetPointSlow))
            {
                s.Ball.Flags &= ~BallFlags.SetPointSlow;
                if (s.SlowFrames == 0) s.SlowDiv = 1;
            }
            Emit(VolleyEvent.NetCross, s.LastToucher, entered, 0, s.Ball.Pos.X, s.Ball.Pos.Y);
        }

        void BounceDeadBall(ref GameState s)
        {
            ref BallState b = ref s.Ball;
            if (b.Mode != BallMode.Dead) return;
            if (b.Vel.LengthSquared < Fix64.FromRatio(1, 100))
            {
                b.Vel = FixVec2.Zero;
                b.Pos.Y = T.Court.BallRadius;
                return;
            }
            b.Pos.X += b.Vel.X * T.Dt;
            b.Pos.Y += b.Vel.Y * T.Dt - T.HalfGravityDt2;
            b.Vel.Y -= T.GravityDt;
            if (b.Pos.Y <= T.Court.BallRadius)
            {
                b.Pos.Y = T.Court.BallRadius;
                b.Vel.Y = -b.Vel.Y * T.Ball.FloorRestitution;
                b.Vel.X = b.Vel.X * Fix64.FromRatio(8, 10);
                if (b.Vel.Y < Fix64.FromRatio(1, 2)) b.Vel.Y = Fix64.Zero;
            }
        }
    }
}
