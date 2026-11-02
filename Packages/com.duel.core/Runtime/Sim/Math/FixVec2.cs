using System;

namespace Duel.Core.Sim
{
    /// <summary>2D vector of <see cref="Fix64"/>. X runs along the court, Y is up.</summary>
    public struct FixVec2 : IEquatable<FixVec2>
    {
        public Fix64 X;
        public Fix64 Y;

        public FixVec2(Fix64 x, Fix64 y)
        {
            X = x;
            Y = y;
        }

        public static readonly FixVec2 Zero = new FixVec2(Fix64.Zero, Fix64.Zero);

        public static FixVec2 operator +(FixVec2 a, FixVec2 b) => new FixVec2(a.X + b.X, a.Y + b.Y);
        public static FixVec2 operator -(FixVec2 a, FixVec2 b) => new FixVec2(a.X - b.X, a.Y - b.Y);
        public static FixVec2 operator -(FixVec2 a) => new FixVec2(-a.X, -a.Y);
        public static FixVec2 operator *(FixVec2 a, Fix64 s) => new FixVec2(a.X * s, a.Y * s);
        public static FixVec2 operator *(Fix64 s, FixVec2 a) => new FixVec2(a.X * s, a.Y * s);
        public static FixVec2 operator *(FixVec2 a, int s) => new FixVec2(a.X * s, a.Y * s);
        public static FixVec2 operator /(FixVec2 a, Fix64 s) => new FixVec2(a.X / s, a.Y / s);
        public static FixVec2 operator /(FixVec2 a, int s) => new FixVec2(a.X / s, a.Y / s);
        public static bool operator ==(FixVec2 a, FixVec2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(FixVec2 a, FixVec2 b) => !(a == b);

        public static Fix64 Dot(FixVec2 a, FixVec2 b) => a.X * b.X + a.Y * b.Y;

        public Fix64 LengthSquared => X * X + Y * Y;

        public Fix64 Length => Fix64.Sqrt(LengthSquared);

        public static Fix64 DistanceSquared(FixVec2 a, FixVec2 b) => (a - b).LengthSquared;

        public static Fix64 Distance(FixVec2 a, FixVec2 b) => (a - b).Length;

        /// <summary>Unit vector, or zero for a zero vector.</summary>
        public FixVec2 Normalized()
        {
            Fix64 len = Length;
            if (len.IsZero) return Zero;
            return new FixVec2(X / len, Y / len);
        }

        public static FixVec2 Lerp(FixVec2 a, FixVec2 b, Fix64 t) => new FixVec2(Fix64.Lerp(a.X, b.X, t), Fix64.Lerp(a.Y, b.Y, t));

        public bool Equals(FixVec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is FixVec2 other && Equals(other);
        public override int GetHashCode() => X.GetHashCode() * 31 + Y.GetHashCode();
        public override string ToString() => "(" + X.ToString(3) + ", " + Y.ToString(3) + ")";
    }
}
