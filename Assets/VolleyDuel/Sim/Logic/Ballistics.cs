using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    /// <summary>
    /// Closed-form launch solutions for a point mass under constant gravity, in fixed point.
    /// All solvers return false when no real solution exists.
    /// </summary>
    public static class Ballistics
    {
        /// <summary>
        /// Launch velocity of magnitude <paramref name="speed"/> from <paramref name="from"/> that
        /// passes through <paramref name="to"/>. <paramref name="low"/> picks the flatter, faster arc.
        /// </summary>
        public static bool SolveSpeed(FixVec2 from, FixVec2 to, Fix64 speed, Fix64 gravity, bool low, out FixVec2 velocity)
        {
            velocity = FixVec2.Zero;
            Fix64 dx = to.X - from.X;
            Fix64 dy = to.Y - from.Y;
            if (dx.IsZero) return false;
            // With u = t^2:  (g^2/4) u^2 + (dy g - v^2) u + (dx^2 + dy^2) = 0
            Fix64 a = gravity * gravity / 4;
            Fix64 b = dy * gravity - speed * speed;
            Fix64 c = dx * dx + dy * dy;
            Fix64 disc = b * b - 4 * a * c;
            if (disc.IsNegative) return false;
            Fix64 root = Fix64.Sqrt(disc);
            Fix64 u;
            if (low)
            {
                // Smaller root via Vieta for stability: u_low = 2c / (-b + sqrt(disc)).
                Fix64 den = -b + root;
                if (den <= Fix64.Zero) return false;
                u = 2 * c / den;
            }
            else
            {
                u = (-b + root) / (2 * a);
            }
            if (u <= Fix64.Zero) return false;
            Fix64 t = Fix64.Sqrt(u);
            if (t.IsZero) return false;
            velocity = new FixVec2(dx / t, dy / t + gravity * t / 2);
            return true;
        }

        /// <summary>
        /// Launch velocity whose arc peaks at height <paramref name="apex"/> and comes down through
        /// <paramref name="to"/>. If the target is not below the apex the arc peaks exactly at the target.
        /// </summary>
        public static bool SolveApex(FixVec2 from, Fix64 apex, FixVec2 to, Fix64 gravity, out FixVec2 velocity)
        {
            velocity = FixVec2.Zero;
            if (apex < from.Y) apex = from.Y;
            Fix64 rise = apex - from.Y;
            Fix64 vy = Fix64.Sqrt(2 * gravity * rise);
            Fix64 tUp = vy / gravity;
            Fix64 tDown = Fix64.Zero;
            if (to.Y < apex) tDown = Fix64.Sqrt(2 * (apex - to.Y) / gravity);
            Fix64 total = tUp + tDown;
            if (total.IsZero) return false;
            velocity = new FixVec2((to.X - from.X) / total, vy);
            return true;
        }

        /// <summary>
        /// Launch velocity of the gravity parabola through <paramref name="from"/>, <paramref name="via"/>
        /// and <paramref name="to"/> (all X distinct, via between the others). Used to clear the net by a margin.
        /// </summary>
        public static bool SolveThrough(FixVec2 from, FixVec2 via, FixVec2 to, Fix64 gravity, out FixVec2 velocity)
        {
            velocity = FixVec2.Zero;
            Fix64 d1 = via.X - from.X;
            Fix64 d2 = to.X - from.X;
            if (d1.IsZero || d2.IsZero || d1 == d2) return false;
            if (Fix64.Sign(d1) != Fix64.Sign(d2)) return false;
            // y - y0 = a d + b d^2, with b = -g / (2 vx^2).
            Fix64 s1 = (via.Y - from.Y) / d1;
            Fix64 s2 = (to.Y - from.Y) / d2;
            Fix64 b = (s2 - s1) / (d2 - d1);
            if (b >= Fix64.Zero) return false;
            Fix64 a = s1 - b * d1;
            Fix64 vx = Fix64.Sqrt(-gravity / (2 * b));
            if (d2.IsNegative) vx = -vx;
            velocity = new FixVec2(vx, a * vx);
            return true;
        }

        /// <summary>Height of a ballistic path when it reaches <paramref name="x"/>; false if it never does.</summary>
        public static bool HeightAt(FixVec2 from, FixVec2 velocity, Fix64 gravity, Fix64 x, out Fix64 height, out Fix64 time)
        {
            height = Fix64.Zero;
            time = Fix64.Zero;
            if (velocity.X.IsZero) return false;
            time = (x - from.X) / velocity.X;
            if (time.IsNegative) return false;
            height = from.Y + velocity.Y * time - gravity * time * time / 2;
            return true;
        }

        /// <summary>Time to fall from <paramref name="fromY"/> with vertical speed <paramref name="vy"/> to height <paramref name="y"/> on the way down.</summary>
        public static bool TimeToHeight(Fix64 fromY, Fix64 vy, Fix64 gravity, Fix64 y, out Fix64 time)
        {
            // fromY + vy t - g t^2 / 2 = y  ->  (g/2) t^2 - vy t + (y - fromY) = 0, larger root.
            time = Fix64.Zero;
            Fix64 a = gravity / 2;
            Fix64 c = y - fromY;
            Fix64 disc = vy * vy - 4 * a * c;
            if (disc.IsNegative) return false;
            time = (vy + Fix64.Sqrt(disc)) / (2 * a);
            return !time.IsNegative;
        }
    }
}
