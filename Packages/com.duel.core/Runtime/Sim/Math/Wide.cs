namespace Duel.Core.Sim
{
    /// <summary>
    /// 128-bit unsigned helpers built from 64-bit halves. .NET Standard 2.1 (Unity) has no
    /// UInt128 and no Math.BigMul(ulong, ulong), so the fixed-point core does this by hand.
    /// </summary>
    internal static class Wide
    {
        /// <summary>Full 64x64 to 128-bit unsigned product.</summary>
        internal static void Mul64(ulong a, ulong b, out ulong hi, out ulong lo)
        {
            ulong aL = (uint)a, aH = a >> 32, bL = (uint)b, bH = b >> 32;
            ulong ll = aL * bL;
            ulong lh = aL * bH;
            ulong hl = aH * bL;
            ulong hh = aH * bH;
            ulong mid = (ll >> 32) + (uint)lh + (uint)hl;
            lo = (mid << 32) | (uint)ll;
            hi = hh + (lh >> 32) + (hl >> 32) + (mid >> 32);
        }

        /// <summary>(hi:lo) / d with remainder. Requires hi &lt; d so the quotient fits in 64 bits.</summary>
        internal static ulong DivRem(ulong hi, ulong lo, ulong d, out ulong rem)
        {
            ulong q = 0;
            ulong r = hi;
            for (int i = 63; i >= 0; i--)
            {
                bool carry = (r >> 63) != 0;
                r = (r << 1) | ((lo >> i) & 1UL);
                q <<= 1;
                if (carry || r >= d)
                {
                    r -= d;
                    q |= 1UL;
                }
            }
            rem = r;
            return q;
        }

        /// <summary>Integer square root of (hi:lo), rounded to nearest. Result must fit in 64 bits.</summary>
        internal static ulong Sqrt(ulong hi, ulong lo)
        {
            ulong numHi = hi, numLo = lo;
            ulong resHi = 0, resLo = 0;
            ulong bitHi = 1UL << 62, bitLo = 0;
            while (Greater(bitHi, bitLo, numHi, numLo)) Shr2(ref bitHi, ref bitLo);
            while (bitHi != 0 || bitLo != 0)
            {
                Add(resHi, resLo, bitHi, bitLo, out ulong tHi, out ulong tLo);
                if (!Greater(tHi, tLo, numHi, numLo))
                {
                    Sub(ref numHi, ref numLo, tHi, tLo);
                    Shr1(ref resHi, ref resLo);
                    Add(resHi, resLo, bitHi, bitLo, out resHi, out resLo);
                }
                else
                {
                    Shr1(ref resHi, ref resLo);
                }
                Shr2(ref bitHi, ref bitLo);
            }
            // res = floor(sqrt(N)), num = N - res^2. Round up when num > res.
            if (Greater(numHi, numLo, resHi, resLo)) resLo++;
            return resLo;
        }

        static bool Greater(ulong aHi, ulong aLo, ulong bHi, ulong bLo) => aHi > bHi || (aHi == bHi && aLo > bLo);

        static void Add(ulong aHi, ulong aLo, ulong bHi, ulong bLo, out ulong hi, out ulong lo)
        {
            lo = aLo + bLo;
            hi = aHi + bHi + (lo < aLo ? 1UL : 0UL);
        }

        static void Sub(ref ulong aHi, ref ulong aLo, ulong bHi, ulong bLo)
        {
            ulong lo = aLo - bLo;
            aHi = aHi - bHi - (aLo < bLo ? 1UL : 0UL);
            aLo = lo;
        }

        static void Shr1(ref ulong hi, ref ulong lo)
        {
            lo = (lo >> 1) | (hi << 63);
            hi >>= 1;
        }

        static void Shr2(ref ulong hi, ref ulong lo)
        {
            lo = (lo >> 2) | (hi << 62);
            hi >>= 2;
        }
    }
}
