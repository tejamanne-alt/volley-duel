using System;

namespace Duel.Core.Sim
{
    /// <summary>
    /// Signed Q32.32 fixed-point number stored in a 64-bit integer. Every operation is
    /// integer-only and rounds half away from zero, so results are bit-identical on every
    /// platform and runtime (Mono, IL2CPP, CoreCLR; ARM64 and x64).
    /// Overflow saturates to <see cref="MaxValue"/> / <see cref="MinValue"/>.
    /// </summary>
    public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>, IComparable
    {
        public const int FractionalBits = 32;
        public const long OneRaw = 1L << FractionalBits;
        const long HalfRaw = OneRaw >> 1;

        /// <summary>Raw Q32.32 bits. Value = Raw / 2^32.</summary>
        public readonly long Raw;

        Fix64(long raw)
        {
            Raw = raw;
        }

        public static readonly Fix64 Zero = new Fix64(0);
        public static readonly Fix64 One = new Fix64(OneRaw);
        public static readonly Fix64 Two = new Fix64(OneRaw * 2);
        public static readonly Fix64 Half = new Fix64(HalfRaw);
        public static readonly Fix64 MaxValue = new Fix64(long.MaxValue);
        public static readonly Fix64 MinValue = new Fix64(long.MinValue);
        /// <summary>Smallest positive value, 2^-32.</summary>
        public static readonly Fix64 Epsilon = new Fix64(1);

        public static Fix64 FromRaw(long raw) => new Fix64(raw);

        public static Fix64 FromInt(int value) => new Fix64((long)value << FractionalBits);

        /// <summary>numerator / denominator, rounded to nearest.</summary>
        public static Fix64 FromRatio(long numerator, long denominator) => FromInt64(numerator) / FromInt64(denominator);

        /// <summary>Exact conversion of a 64-bit integer; saturates outside ±2^31.</summary>
        public static Fix64 FromInt64(long value)
        {
            if (value > int.MaxValue) return MaxValue;
            if (value < int.MinValue) return MinValue;
            return new Fix64(value << FractionalBits);
        }

        public static implicit operator Fix64(int value) => FromInt(value);

        // ---------------------------------------------------------------- arithmetic

        public static Fix64 operator +(Fix64 a, Fix64 b)
        {
            long r = a.Raw + b.Raw;
            // Overflow iff both operands share a sign that differs from the result's.
            if (((a.Raw ^ r) & (b.Raw ^ r)) < 0) return a.Raw < 0 ? MinValue : MaxValue;
            return new Fix64(r);
        }

        public static Fix64 operator -(Fix64 a, Fix64 b)
        {
            long r = a.Raw - b.Raw;
            if (((a.Raw ^ b.Raw) & (a.Raw ^ r)) < 0) return a.Raw < 0 ? MinValue : MaxValue;
            return new Fix64(r);
        }

        public static Fix64 operator -(Fix64 a) => a.Raw == long.MinValue ? MaxValue : new Fix64(-a.Raw);

        public static Fix64 operator *(Fix64 a, Fix64 b)
        {
            long x = a.Raw, y = b.Raw;
            bool negative = (x ^ y) < 0;
            ulong ux = Magnitude(x), uy = Magnitude(y);
            Wide.Mul64(ux, uy, out ulong hi, out ulong lo);
            // Round half away from zero on the magnitude, then shift right by 32.
            ulong loRounded = lo + (ulong)HalfRaw;
            if (loRounded < lo) hi++;
            if (hi >= (1UL << 31)) return negative ? MinValue : MaxValue;
            ulong magnitude = (hi << 32) | (loRounded >> 32);
            return new Fix64(negative ? -(long)magnitude : (long)magnitude);
        }

        /// <summary>Exact scaling by an integer.</summary>
        public static Fix64 operator *(Fix64 a, int b)
        {
            if (b == 0 || a.Raw == 0) return Zero;
            if (b == -1) return -a;
            long r = a.Raw * b;
            if (r / b != a.Raw) return ((a.Raw < 0) ^ (b < 0)) ? MinValue : MaxValue;
            return new Fix64(r);
        }

        public static Fix64 operator *(int a, Fix64 b) => b * a;

        public static Fix64 operator /(Fix64 a, Fix64 b)
        {
            long x = a.Raw, y = b.Raw;
            if (y == 0) throw new DivideByZeroException("Fix64 division by zero");
            bool negative = (x ^ y) < 0;
            ulong ux = Magnitude(x), uy = Magnitude(y);
            ulong q, rem;
            if (uy < (1UL << 48))
            {
                // Long division in base 2^16: remainder stays below uy < 2^48, so shifting it by 16 never overflows.
                ulong q1 = ux / uy;
                if (q1 >= (1UL << 31)) return negative ? MinValue : MaxValue;
                rem = ux - q1 * uy;
                rem <<= 16;
                ulong q2 = rem / uy;
                rem -= q2 * uy;
                rem <<= 16;
                ulong q3 = rem / uy;
                rem -= q3 * uy;
                q = (q1 << 32) | (q2 << 16) | q3;
            }
            else
            {
                ulong hi = ux >> 32, lo = ux << 32;
                if (hi >= uy) return negative ? MinValue : MaxValue;
                q = Wide.DivRem(hi, lo, uy, out rem);
            }
            if (rem >= uy - rem) q++; // round half away from zero
            if (q > (ulong)long.MaxValue) return negative ? MinValue : MaxValue;
            return new Fix64(negative ? -(long)q : (long)q);
        }

        /// <summary>Division by an integer, rounded half away from zero.</summary>
        public static Fix64 operator /(Fix64 a, int b)
        {
            if (b == 0) throw new DivideByZeroException("Fix64 division by zero");
            bool negative = (a.Raw < 0) ^ (b < 0);
            ulong ua = Magnitude(a.Raw);
            ulong ub = b < 0 ? (ulong)(-(long)b) : (ulong)b;
            ulong q = ua / ub;
            ulong rem = ua - q * ub;
            if (rem >= ub - rem) q++;
            if (q > (ulong)long.MaxValue) return negative ? MinValue : MaxValue;
            return new Fix64(negative ? -(long)q : (long)q);
        }

        /// <summary>Remainder with the sign of the dividend (C# semantics).</summary>
        public static Fix64 operator %(Fix64 a, Fix64 b)
        {
            if (b.Raw == 0) throw new DivideByZeroException("Fix64 modulo by zero");
            if (b.Raw == -1) return Zero;
            return new Fix64(a.Raw % b.Raw);
        }

        static ulong Magnitude(long v) => v < 0 ? (ulong)(-v) : (ulong)v; // -long.MinValue wraps to 2^63, which is the right magnitude

        // ---------------------------------------------------------------- comparison

        public static bool operator ==(Fix64 a, Fix64 b) => a.Raw == b.Raw;
        public static bool operator !=(Fix64 a, Fix64 b) => a.Raw != b.Raw;
        public static bool operator <(Fix64 a, Fix64 b) => a.Raw < b.Raw;
        public static bool operator >(Fix64 a, Fix64 b) => a.Raw > b.Raw;
        public static bool operator <=(Fix64 a, Fix64 b) => a.Raw <= b.Raw;
        public static bool operator >=(Fix64 a, Fix64 b) => a.Raw >= b.Raw;

        public bool Equals(Fix64 other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fix64 other && Raw == other.Raw;
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Fix64 other) => Raw.CompareTo(other.Raw);

        public int CompareTo(object obj) => obj is Fix64 other ? Raw.CompareTo(other.Raw) : 1;

        // ---------------------------------------------------------------- helpers

        public bool IsZero => Raw == 0;
        public bool IsNegative => Raw < 0;
        public bool IsPositive => Raw > 0;

        public static Fix64 Abs(Fix64 v) => v.Raw < 0 ? -v : v;
        public static Fix64 Min(Fix64 a, Fix64 b) => a.Raw < b.Raw ? a : b;
        public static Fix64 Max(Fix64 a, Fix64 b) => a.Raw > b.Raw ? a : b;
        public static Fix64 Clamp(Fix64 v, Fix64 min, Fix64 max) => v.Raw < min.Raw ? min : (v.Raw > max.Raw ? max : v);
        public static Fix64 Clamp01(Fix64 v) => Clamp(v, Zero, One);
        public static int Sign(Fix64 v) => v.Raw > 0 ? 1 : (v.Raw < 0 ? -1 : 0);

        /// <summary>a + (b - a) * t.</summary>
        public static Fix64 Lerp(Fix64 a, Fix64 b, Fix64 t) => a + (b - a) * t;

        /// <summary>Inverse lerp clamped to [0, 1]; returns 0 when a == b.</summary>
        public static Fix64 InverseLerp(Fix64 a, Fix64 b, Fix64 v)
        {
            if (a == b) return Zero;
            return Clamp01((v - a) / (b - a));
        }

        public int FloorToInt() => (int)(Raw >> FractionalBits);

        public int CeilToInt() => (int)((Raw + (OneRaw - 1)) >> FractionalBits);

        /// <summary>Round half away from zero.</summary>
        public int RoundToInt() => Raw >= 0 ? (int)((Raw + HalfRaw) >> FractionalBits) : -(int)((-Raw + HalfRaw) >> FractionalBits);

        /// <summary>Truncate toward zero.</summary>
        public int TruncateToInt() => Raw >= 0 ? (int)(Raw >> FractionalBits) : -(int)((-Raw) >> FractionalBits);

        public Fix64 Floor() => new Fix64(Raw & ~(OneRaw - 1));

        /// <summary>Square root, rounded to nearest. Negative input returns zero.</summary>
        public static Fix64 Sqrt(Fix64 v)
        {
            if (v.Raw <= 0) return Zero;
            // sqrt(raw / 2^32) * 2^32 = sqrt(raw * 2^32): integer square root of a 96-bit value.
            ulong hi = (ulong)v.Raw >> 32;
            ulong lo = (ulong)v.Raw << 32;
            return new Fix64((long)Wide.Sqrt(hi, lo));
        }

        /// <summary>Square of the value.</summary>
        public Fix64 Squared() => this * this;

        // ---------------------------------------------------------------- text (data baking and debugging; integer-only)

        /// <summary>
        /// Parses a decimal string such as "17", "-0.105" or "2.35" exactly, rounding the last
        /// bit to nearest. Used to bake data tables; no floating point is involved.
        /// </summary>
        public static Fix64 Parse(string text)
        {
            if (!TryParse(text, out Fix64 value)) throw new FormatException("Not a decimal number: '" + text + "'");
            return value;
        }

        public static bool TryParse(string text, out Fix64 value)
        {
            value = Zero;
            if (string.IsNullOrEmpty(text)) return false;
            int i = 0;
            bool negative = false;
            if (text[0] == '-' || text[0] == '+')
            {
                negative = text[0] == '-';
                i = 1;
            }
            long whole = 0;
            int wholeDigits = 0;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9')
            {
                whole = whole * 10 + (text[i] - '0');
                if (whole > int.MaxValue) return false;
                wholeDigits++;
                i++;
            }
            ulong frac = 0;
            ulong scale = 1;
            int fracDigits = 0;
            bool roundUpExtra = false;
            if (i < text.Length && text[i] == '.')
            {
                i++;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9')
                {
                    if (fracDigits < 18)
                    {
                        frac = frac * 10 + (ulong)(text[i] - '0');
                        scale *= 10;
                        fracDigits++;
                    }
                    else if (fracDigits == 18)
                    {
                        roundUpExtra = text[i] >= '5';
                        fracDigits++;
                    }
                    i++;
                }
            }
            if (i != text.Length || (wholeDigits == 0 && fracDigits == 0)) return false;
            if (roundUpExtra) frac++;
            // frac / scale in Q0.32, rounded: (frac << 32) / scale with a 128-bit numerator.
            ulong numHi = frac >> 32;
            ulong numLo = frac << 32;
            ulong fracRaw = Wide.DivRem(numHi, numLo, scale, out ulong rem);
            if (rem >= scale - rem) fracRaw++;
            ulong magnitude = ((ulong)whole << 32) + fracRaw;
            if (magnitude > (ulong)long.MaxValue) return false;
            value = new Fix64(negative ? -(long)magnitude : (long)magnitude);
            return true;
        }

        /// <summary>Decimal text with up to <paramref name="decimals"/> fractional digits (rounded).</summary>
        public string ToString(int decimals)
        {
            if (decimals < 0) decimals = 0;
            if (decimals > 9) decimals = 9;
            ulong magnitude = Magnitude(Raw);
            ulong pow = 1;
            for (int d = 0; d < decimals; d++) pow *= 10;
            // magnitude * pow / 2^32 rounded, using a 128-bit product.
            Wide.Mul64(magnitude, pow, out ulong hi, out ulong lo);
            ulong rounded = lo + (ulong)HalfRaw;
            if (rounded < lo) hi++;
            ulong scaled = (hi << 32) | (rounded >> 32);
            ulong wholePart = scaled / pow;
            ulong fracPart = scaled % pow;
            var s = new System.Text.StringBuilder(24);
            if (Raw < 0 && scaled != 0) s.Append('-');
            s.Append(wholePart.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (decimals > 0)
            {
                s.Append('.');
                string f = fracPart.ToString(System.Globalization.CultureInfo.InvariantCulture);
                s.Append('0', decimals - f.Length);
                s.Append(f);
            }
            return s.ToString();
        }

        public override string ToString() => ToString(4);
    }
}
