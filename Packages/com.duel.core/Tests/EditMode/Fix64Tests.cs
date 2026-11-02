using System;
using System.Numerics;
using Duel.Core.Sim;
using NUnit.Framework;

namespace Duel.Core.Tests
{
    public class Fix64Tests
    {
        static readonly BigInteger One = BigInteger.One << 32;

        // Reference: round half away from zero of num/den.
        static long RoundDiv(BigInteger num, BigInteger den)
        {
            bool negative = (num.Sign < 0) ^ (den.Sign < 0);
            BigInteger n = BigInteger.Abs(num), d = BigInteger.Abs(den);
            BigInteger q = BigInteger.DivRem(n, d, out BigInteger r);
            if (r * 2 >= d) q += 1;
            if (negative) q = -q;
            if (q > long.MaxValue) return long.MaxValue;
            if (q < long.MinValue) return long.MinValue;
            return (long)q;
        }

        static long RefMul(long a, long b) => RoundDiv((BigInteger)a * b, One);

        static long RefDiv(long a, long b) => RoundDiv((BigInteger)a * One, b);

        static long RandomRaw(Rng rng, int maxIntBits)
        {
            ulong bits = ((ulong)rng.NextUInt() << 32) | rng.NextUInt();
            int shift = 64 - (maxIntBits + 32);
            long v = (long)(bits >> shift);
            return (rng.NextUInt() & 1) == 0 ? v : -v;
        }

        [Test]
        public void ParseIsExact()
        {
            Assert.AreEqual(1L << 32, Fix64.Parse("1").Raw);
            Assert.AreEqual(1L << 31, Fix64.Parse("0.5").Raw);
            Assert.AreEqual(-(1L << 31), Fix64.Parse("-0.5").Raw);
            Assert.AreEqual(17L << 32, Fix64.Parse("17").Raw);
            Assert.AreEqual(RoundDiv(new BigInteger(235) * One, 100), Fix64.Parse("2.35").Raw);
            Assert.AreEqual(RoundDiv(new BigInteger(-105) * One, 1000), Fix64.Parse("-0.105").Raw);
            Assert.AreEqual(RoundDiv(new BigInteger(981) * One, 100), Fix64.Parse("9.81").Raw);
            Assert.AreEqual(Fix64.Parse("12.000").Raw, Fix64.FromInt(12).Raw);
        }

        [TestCase("")]
        [TestCase("-")]
        [TestCase(".")]
        [TestCase("1e5")]
        [TestCase("1.2.3")]
        [TestCase("abc")]
        [TestCase("99999999999")]
        public void ParseRejectsBadText(string text)
        {
            Assert.IsFalse(Fix64.TryParse(text, out _));
        }

        [Test]
        public void MultiplicationMatchesReference()
        {
            var rng = Rng.Create(1234, 1);
            for (int i = 0; i < 20000; i++)
            {
                long a = RandomRaw(rng, 1 + rng.NextInt(16));
                long b = RandomRaw(rng, 1 + rng.NextInt(16));
                Assert.AreEqual(RefMul(a, b), (Fix64.FromRaw(a) * Fix64.FromRaw(b)).Raw, "a=" + a + " b=" + b);
            }
        }

        [Test]
        public void DivisionMatchesReference()
        {
            var rng = Rng.Create(99, 2);
            for (int i = 0; i < 20000; i++)
            {
                long a = RandomRaw(rng, 1 + rng.NextInt(20));
                long b = RandomRaw(rng, 1 + rng.NextInt(30));
                if (b == 0) continue;
                Assert.AreEqual(RefDiv(a, b), (Fix64.FromRaw(a) / Fix64.FromRaw(b)).Raw, "a=" + a + " b=" + b);
            }
        }

        [Test]
        public void DivisionByLargeDivisorUsesSlowPathCorrectly()
        {
            long a = 123456789L << 20;
            long b = (1L << 50) + 12345;
            Assert.AreEqual(RefDiv(a, b), (Fix64.FromRaw(a) / Fix64.FromRaw(b)).Raw);
            Assert.AreEqual(RefDiv(-a, b), (Fix64.FromRaw(-a) / Fix64.FromRaw(b)).Raw);
        }

        [Test]
        public void IntegerScalingAndDivision()
        {
            Fix64 x = Fix64.Parse("2.35");
            Assert.AreEqual(x.Raw * 3, (x * 3).Raw);
            Assert.AreEqual(x.Raw * -7, (x * -7).Raw);
            Assert.AreEqual(RoundDiv(x.Raw, 3), (x / 3).Raw);
            Assert.AreEqual(RoundDiv(-x.Raw, 3), (-x / 3).Raw);
            Assert.AreEqual(Fix64.MaxValue, Fix64.FromInt(int.MaxValue) * 4);
            Assert.AreEqual(Fix64.MaxValue, Fix64.MinValue * -1);
        }

        [Test]
        public void SqrtMatchesReference()
        {
            var rng = Rng.Create(7, 3);
            for (int i = 0; i < 5000; i++)
            {
                long a = Math.Abs(RandomRaw(rng, 1 + rng.NextInt(30)));
                BigInteger n = (BigInteger)a << 32;
                BigInteger r = ISqrt(n);
                if (n - r * r > r) r += 1;
                Assert.AreEqual((long)r, Fix64.Sqrt(Fix64.FromRaw(a)).Raw, "a=" + a);
            }
            Assert.AreEqual(Fix64.FromInt(3), Fix64.Sqrt(Fix64.FromInt(9)));
            Assert.AreEqual(Fix64.Zero, Fix64.Sqrt(-Fix64.One));
        }

        static BigInteger ISqrt(BigInteger n)
        {
            if (n.IsZero) return n;
            BigInteger x = (BigInteger)Math.Sqrt((double)n);
            while (x * x > n) x -= 1;
            while ((x + 1) * (x + 1) <= n) x += 1;
            return x;
        }

        [Test]
        public void SaturatesOnOverflow()
        {
            Fix64 big = Fix64.FromInt(int.MaxValue);
            Assert.AreEqual(Fix64.MaxValue, big + big);
            Assert.AreEqual(Fix64.MinValue, -big - big);
            Assert.AreEqual(Fix64.MaxValue, big * big);
            Assert.AreEqual(Fix64.MinValue, big * -big);
            Assert.AreEqual(Fix64.MaxValue, big / Fix64.Epsilon);
        }

        [Test]
        public void RoundingToIntegers()
        {
            Assert.AreEqual(3, Fix64.Parse("2.5").RoundToInt(), "half away from zero");
            Assert.AreEqual(-3, Fix64.Parse("-2.5").RoundToInt());
            Assert.AreEqual(-3, Fix64.Parse("-2.4").FloorToInt());
            Assert.AreEqual(-2, Fix64.Parse("-2.4").TruncateToInt());
            Assert.AreEqual(3, Fix64.Parse("2.01").CeilToInt());
            Assert.AreEqual(2, Fix64.Parse("2").CeilToInt());
        }

        [Test]
        public void ToStringIsInvariantAndRounded()
        {
            Assert.AreEqual("2.350", Fix64.Parse("2.35").ToString(3));
            Assert.AreEqual("-0.13", Fix64.Parse("-0.125").ToString(2), "exact half rounds away from zero");
            Assert.AreEqual("-0.10", Fix64.Parse("-0.105").ToString(2), "0.105 is stored just below the decimal value");
            Assert.AreEqual("0", Fix64.Parse("-0.0001").ToString(0));
            Assert.AreEqual("17.0000", Fix64.FromInt(17).ToString());
        }

        [Test]
        public void VectorLengthAndNormalize()
        {
            var v = new FixVec2(3, 4);
            Assert.AreEqual(Fix64.FromInt(5), v.Length);
            FixVec2 n = v.Normalized();
            Assert.AreEqual("0.600", n.X.ToString(3));
            Assert.AreEqual("0.800", n.Y.ToString(3));
            Assert.AreEqual(FixVec2.Zero, FixVec2.Zero.Normalized());
        }
    }
}
