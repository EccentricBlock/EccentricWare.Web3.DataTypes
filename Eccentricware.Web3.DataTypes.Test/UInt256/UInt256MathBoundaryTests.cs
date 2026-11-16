using EccentricWare.Web3.DataTypes.Utils;
using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Deterministic boundary, failure and constant tests for the internal multiply/divide core (BigInteger is the oracle).</summary>
[TestClass]
public sealed class UInt256MathBoundaryTests
{
    private static readonly BigInteger Two256 = UInt256TestValues.Two256;

    private static uint256 U(BigInteger value) => UInt256TestValues.FromBig(value);

    // ---- constants (independent of the constants under test) ----

    [TestMethod]
    public void TenPow19_IsTenToTheNineteenth_AndTheLargestPowerOfTenInAUlong()
    {
        ulong actual = UInt256Math.TenPow19;

        Assert.AreEqual(10_000_000_000_000_000_000UL, actual);
        Assert.AreEqual((ulong)BigInteger.Pow(10, 19), actual);
        Assert.IsGreaterThan((BigInteger)ulong.MaxValue, (BigInteger)actual * 10);   // the next power of ten no longer fits
    }

    [TestMethod]
    public void TenPow19Reciprocal_IsFloorOfMaxOver10Pow19MinusTwoPow64()
    {
        BigInteger expected = ((BigInteger.One << 128) - 1) / BigInteger.Pow(10, 19) - (BigInteger.One << 64);
        BigInteger actual = UInt256Math.TenPow19Reciprocal;

        Assert.AreEqual(expected, actual);
    }

    // ---- multiplication ----

    [TestMethod]
    public void Multiply_NamedBoundaryCases()
    {
        BigInteger p64 = BigInteger.One << 64, p128 = BigInteger.One << 128, p192 = BigInteger.One << 192, p255 = BigInteger.One << 255;
        (BigInteger a, BigInteger b, bool fits)[] cases =
        [
            (p128, p128, false),
            (p192, p64, false),                       // limb index sum is 4
            (p192, p64 - 1, true),
            (p128 - 1, p128 - 1, true),
            (p255, 2, false),
            (p255, 1, true),
            (Two256 - 1, Two256 - 1, false),
            (Two256 - 1, 0, true),
            (0, Two256 - 1, true),
            (Two256 - 1, 1, true),
            (p64 - 1, p64 - 1, true),                 // single-limb fast path: (1, 2^64 - 2, 0, 0)
        ];

        foreach (var (a, b, fits) in cases)
        {
            uint256 left = U(a), right = U(b);
            BigInteger product = a * b;

            bool ok = UInt256Math.TryMultiply(in left, in right, out uint256 result);

            Assert.AreEqual(fits, ok, $"{a} * {b}");
            Assert.AreEqual(fits, product < Two256, "test table is consistent with the oracle");
            Assert.AreEqual(fits ? product : BigInteger.Zero, UInt256TestValues.ToBig(result), $"{a} * {b}: result is the product, or zero on overflow");
            Assert.AreEqual(product % Two256, UInt256TestValues.ToBig(UInt256Math.MultiplyLow(in left, in right)), $"{a} * {b} (low)");
        }
    }

    [TestMethod]
    public void Multiply_SingleLimbSquare_PlacesTheHighHalfInLimbOne()
    {
        uint256 value = new(ulong.MaxValue);

        uint256 low = UInt256Math.MultiplyLow(in value, in value);

        Assert.AreEqual(new uint256(1UL, ulong.MaxValue - 1, 0, 0), low);
    }

    [TestMethod]
    public void Multiply_AllBoundaryPairs_MatchBigInteger()
    {
        foreach (BigInteger a in UInt256TestValues.Boundaries)
        {
            foreach (BigInteger b in UInt256TestValues.Boundaries)
            {
                uint256 left = U(a), right = U(b);
                BigInteger product = a * b;

                bool ok = UInt256Math.TryMultiply(in left, in right, out uint256 result);

                Assert.AreEqual(product < Two256, ok, $"{a} * {b}");
                Assert.AreEqual(ok ? product : BigInteger.Zero, UInt256TestValues.ToBig(result), $"{a} * {b}");
                Assert.AreEqual(product % Two256, UInt256TestValues.ToBig(UInt256Math.MultiplyLow(in left, in right)), $"{a} * {b} (low)");
            }
        }
    }

    [TestMethod]
    public void TryMultiplyAdd_EdgeCases()
    {
        static bool Run(BigInteger value, ulong factor, ulong addend, out BigInteger result)
        {
            uint256 v = UInt256TestValues.FromBig(value);
            ulong u0 = v.U0, u1 = v.U1, u2 = v.U2, u3 = v.U3;
            bool ok = UInt256Math.TryMultiplyAdd(ref u0, ref u1, ref u2, ref u3, factor, addend);
            result = UInt256TestValues.ToBig(new uint256(u0, u1, u2, u3));
            return ok;
        }

        BigInteger max = Two256 - 1;

        Assert.IsTrue(Run(max, 1, 0, out BigInteger unchanged));
        Assert.AreEqual(max, unchanged);

        Assert.IsFalse(Run(max, 1, 1, out _));
        Assert.IsFalse(Run(max, 2, 0, out _));

        Assert.IsTrue(Run(0, 0, 12345, out BigInteger onlyAddend));
        Assert.AreEqual(new BigInteger(12345), onlyAddend);

        // Exact overflow boundary: floor((2^256 - 1 - addend) / factor) fits, one more does not.
        foreach ((ulong factor, ulong addend) in new[] { (10UL, 7UL), (UInt256Math.TenPow19, 9UL), (ulong.MaxValue, ulong.MaxValue), (1UL << 63, 1UL) })
        {
            BigInteger largest = (max - addend) / factor;
            Assert.IsTrue(Run(largest, factor, addend, out BigInteger fits), $"{factor} {addend}");
            Assert.AreEqual(largest * factor + addend, fits);
            Assert.IsFalse(Run(largest + 1, factor, addend, out _), $"{factor} {addend} (one past)");
        }
    }

    // ---- division ----

    [TestMethod]
    public void DivRem64_Direct()
    {
        (ulong dividend, ulong divisor)[] cases =
        [
            (0, 1), (ulong.MaxValue, 1), (ulong.MaxValue, ulong.MaxValue), (ulong.MaxValue - 1, ulong.MaxValue), (10, 3), (ulong.MaxValue, 10),
        ];

        foreach (var (dividend, divisor) in cases)
        {
            ulong quotient = UInt256Math.DivRem64(dividend, divisor, out ulong remainder);

            Assert.AreEqual(new BigInteger(dividend) / divisor, new BigInteger(quotient), $"{dividend} / {divisor}");
            Assert.AreEqual(new BigInteger(dividend) % divisor, new BigInteger(remainder), $"{dividend} % {divisor}");
        }
    }

    [TestMethod]
    public void ZeroDivisor_ThrowsDivideByZero_ForEveryEntryPoint()
    {
        foreach (BigInteger dividend in new BigInteger[] { 5, BigInteger.One << 70, BigInteger.One << 140, Two256 - 1 })
        {
            uint256 value = U(dividend);
            Assert.ThrowsExactly<DivideByZeroException>(() => UInt256Math.DivRem(in value, 0UL, out _), $"{dividend} / 0UL");
            Assert.ThrowsExactly<DivideByZeroException>(() => UInt256Math.DivRem(in value, in uint256.Zero, out _, out _), $"{dividend} / uint256.Zero");
        }

        Assert.ThrowsExactly<DivideByZeroException>(() => UInt256Math.DivRem64(5, 0, out _));
        Assert.ThrowsExactly<DivideByZeroException>(() => UInt256Math.DivRem128By64(0, 5, 0, out _));
        Assert.ThrowsExactly<DivideByZeroException>(() => UInt256Math.DivRem128By64(3, 5, 0, out _));
    }

    [TestMethod]
    public void DivRem_ShapeEdges_MatchBigInteger()
    {
        BigInteger max = Two256 - 1, p255 = BigInteger.One << 255, p64 = BigInteger.One << 64;
        BigInteger[] divisors = [p64, p64 + 1, (BigInteger.One << 128) - 1, BigInteger.One << 128, (BigInteger.One << 192) + 5, p255, p255 + 1, max];

        foreach (BigInteger divisor in divisors)
        {
            BigInteger[] dividends = [divisor, divisor - 1, divisor + 1, divisor * 3 + (divisor - 1), max, p255, 0, 1];
            foreach (BigInteger dividend in dividends.Where(d => d < Two256))
            {
                uint256 u = U(dividend), v = U(divisor);

                UInt256Math.DivRem(in u, in v, out uint256 quotient, out uint256 remainder);

                Assert.AreEqual(BigInteger.Divide(dividend, divisor), UInt256TestValues.ToBig(quotient), $"{dividend} / {divisor}");
                Assert.AreEqual(BigInteger.Remainder(dividend, divisor), UInt256TestValues.ToBig(remainder), $"{dividend} % {divisor}");
            }
        }
    }

    [TestMethod]
    public void DivRem128ByTenPow19_BoundaryOperands_MatchBigInteger()
    {
        BigInteger t = BigInteger.Pow(10, 19);
        ulong[] highs = [0, 1, (ulong)(t / 2), (ulong)t - 2, (ulong)t - 1];
        ulong[] lows = [0, 1, (ulong)t - 1, (ulong)t, (ulong)t + 1, ulong.MaxValue];

        foreach (ulong high in highs)
        {
            foreach (ulong low in lows)
            {
                BigInteger n = ((BigInteger)high << 64) | low;

                ulong q = UInt256Math.DivRem128ByTenPow19(high, low, out ulong r);

                Assert.AreEqual(n / t, new BigInteger(q), $"{high}:{low}");
                Assert.AreEqual(n % t, new BigInteger(r), $"{high}:{low}");
            }
        }
    }
}
