namespace Duel.Core.Sim
{
    /// <summary>
    /// PCG32 (XSH-RR) random stream. A value type so it lives inside the snapshot and rolls back
    /// with it. Independent streams are derived from one seed with <see cref="Create"/>.
    /// </summary>
    public struct Rng
    {
        public ulong State;
        public ulong Increment;

        const ulong Multiplier = 6364136223846793005UL;

        /// <summary>Creates stream <paramref name="stream"/> of the generator seeded with <paramref name="seed"/>.</summary>
        public static Rng Create(ulong seed, ulong stream)
        {
            var rng = new Rng { State = 0, Increment = (SplitMix64(stream) << 1) | 1UL };
            rng.NextUInt();
            rng.State += SplitMix64(seed ^ 0x9E3779B97F4A7C15UL);
            rng.NextUInt();
            return rng;
        }

        public uint NextUInt()
        {
            ulong old = State;
            State = unchecked(old * Multiplier + Increment);
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
        }

        /// <summary>Uniform integer in [0, count). Unbiased (Lemire with rejection).</summary>
        public int NextInt(int count)
        {
            if (count <= 1) return 0;
            uint n = (uint)count;
            ulong m = (ulong)NextUInt() * n;
            uint low = (uint)m;
            if (low < n)
            {
                uint threshold = (uint)(-(int)n) % n;
                while (low < threshold)
                {
                    m = (ulong)NextUInt() * n;
                    low = (uint)m;
                }
            }
            return (int)(m >> 32);
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive) => minInclusive + NextInt(maxExclusive - minInclusive);

        /// <summary>Uniform value in [0, 1) with 32 bits of resolution.</summary>
        public Fix64 NextFix() => Fix64.FromRaw(NextUInt());

        /// <summary>Uniform value in [min, max).</summary>
        public Fix64 Range(Fix64 min, Fix64 max) => min + (max - min) * NextFix();

        /// <summary>Uniform value in [-magnitude, magnitude).</summary>
        public Fix64 Symmetric(Fix64 magnitude) => Range(-magnitude, magnitude);

        /// <summary>True with probability <paramref name="p"/> (clamped to [0, 1]).</summary>
        public bool Chance(Fix64 p)
        {
            if (p <= Fix64.Zero) return false;
            if (p >= Fix64.One) return true;
            return NextFix() < p;
        }

        /// <summary>Approximately normal value with mean 0 and the given standard deviation (Irwin-Hall, n = 3).</summary>
        public Fix64 Gaussian(Fix64 sigma)
        {
            // Sum of three U(0,1) has mean 1.5 and variance 0.25 (sd 0.5).
            Fix64 sum = NextFix() + NextFix() + NextFix();
            return (sum - Fix64.FromRatio(3, 2)) * 2 * sigma;
        }

        public static ulong SplitMix64(ulong x)
        {
            x = unchecked(x + 0x9E3779B97F4A7C15UL);
            x = unchecked((x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL);
            x = unchecked((x ^ (x >> 27)) * 0x94D049BB133111EBUL);
            return x ^ (x >> 31);
        }

        public void Write(ref StateWriter w)
        {
            w.WriteUInt64(State);
            w.WriteUInt64(Increment);
        }

        public void Read(ref StateReader r)
        {
            State = r.ReadUInt64();
            Increment = r.ReadUInt64();
        }
    }
}
