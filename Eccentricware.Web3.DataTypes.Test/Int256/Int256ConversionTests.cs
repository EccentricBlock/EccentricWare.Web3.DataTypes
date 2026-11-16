using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Conversions between <see cref="int256"/>, <see cref="long"/> and <see cref="BigInteger"/>, including range checks.</summary>
[TestClass]
public sealed class Int256ConversionTests
{
    private static BigInteger Val(int256 v) => Int256TestValues.ToBig(v);

    private static BigInteger P(int bits) => Int256TestValues.Pow2(bits);

    // ---- implicit from long ----

    [TestMethod]
    public void ImplicitFromLong_SignExtends()
    {
        int256 pos = (int256)123L;
        int256 neg = (int256)(-123L);

        Assert.IsFalse(pos.IsNegative);
        Assert.IsTrue(neg.IsNegative);

        Assert.AreEqual(new BigInteger(123), Val(pos));
        Assert.AreEqual(new BigInteger(-123), Val(neg));
    }

    // ---- explicit to long ----

    [TestMethod]
    public void ExplicitToLong_InRange_Works()
    {
        Assert.AreEqual(0L, (long)int256.Zero);
        Assert.AreEqual(1L, (long)int256.One);
        Assert.AreEqual(-1L, (long)int256.MinusOne);
        Assert.AreEqual(-42L, (long)new int256(-42L));
        Assert.AreEqual(long.MaxValue, (long)new int256(long.MaxValue));
        Assert.AreEqual(long.MinValue, (long)new int256(long.MinValue));
    }

    [TestMethod]
    public void ExplicitToLong_OutOfRange_Throws()
    {
        Assert.ThrowsExactly<OverflowException>(() => _ = (long)int256.MinValue);
        Assert.ThrowsExactly<OverflowException>(() => _ = (long)int256.MaxValue);
        Assert.ThrowsExactly<OverflowException>(() => _ = (long)Int256TestValues.FromBig(P(80)));
        Assert.ThrowsExactly<OverflowException>(() => _ = (long)Int256TestValues.FromBig((BigInteger)long.MaxValue + 1));
        Assert.ThrowsExactly<OverflowException>(() => _ = (long)Int256TestValues.FromBig((BigInteger)long.MinValue - 1));
    }

    [TestMethod]
    public void ExplicitToLong_AnyWrongHigherLimb_Throws()
    {
        // Each of limbs 1..3 is checked on its own; the in-range base values are -5 and 5.
        foreach (long baseValue in new[] { -5L, 5L })
        {
            var b = new int256(baseValue);
            ulong fill = baseValue < 0 ? ulong.MaxValue : 0UL;
            foreach (ulong wrong in new[] { 0UL, 1UL, ulong.MaxValue, 0x8000_0000_0000_0000UL }.Where(w => w != fill))
            {
                Assert.ThrowsExactly<OverflowException>(() => _ = (long)new int256(b.U0, wrong, b.U2, b.U3));
                Assert.ThrowsExactly<OverflowException>(() => _ = (long)new int256(b.U0, b.U1, wrong, b.U3));
                Assert.ThrowsExactly<OverflowException>(() => _ = (long)new int256(b.U0, b.U1, b.U2, wrong));
            }
        }
    }

    [TestMethod]
    public void ExplicitToLong_MatchesRangeCheck_ForBoundaryValues()
    {
        foreach (BigInteger v in Int256TestValues.BoundaryValues)
        {
            int256 x = Int256TestValues.FromBig(v);
            if (v >= long.MinValue && v <= long.MaxValue)
                Assert.AreEqual((long)v, (long)x, $"{v}");
            else
                Assert.ThrowsExactly<OverflowException>(() => _ = (long)x);
        }
    }

    // ---- to BigInteger ----

    [TestMethod]
    public void ToBigInteger_MatchesTheLimbOracle_ForBoundaryValues()
    {
        foreach (BigInteger v in Int256TestValues.BoundaryValues)
        {
            int256 x = Int256TestValues.FromBig(v);
            Assert.AreEqual(v, x.ToBigInteger(), $"{v}");
            Assert.AreEqual(v, (BigInteger)x, $"implicit {v}");
        }
    }

    [TestMethod]
    public void ToBigInteger_RoundTrip_SmallAndLarge()
    {
        var values = new[]
        {
            int256.Zero, int256.One, int256.MinusOne, int256.MinValue, int256.MaxValue,
            Int256TestValues.FromBig(P(200)), Int256TestValues.FromBig(-P(200)),
        };

        foreach (var v in values)
            Assert.AreEqual(v, (int256)v.ToBigInteger());
    }

    // ---- explicit from BigInteger ----

    [TestMethod]
    public void ExplicitFromBigInteger_Success_MatchesTheLimbOracle()
    {
        var inRange = new List<BigInteger>
        {
            0, 1, -1, P(255) - 1, -P(255), -(P(255) - 1), (BigInteger)long.MinValue - 1, (BigInteger)long.MaxValue + 1,
            P(64), -P(64), -P(64) - 1, P(191), -P(192), P(128) - 1, -P(128), P(250) + P(3),
        };
        inRange.AddRange(Int256TestValues.BoundaryValues);

        foreach (BigInteger v in inRange)
        {
            int256 x = (int256)v;
            Assert.AreEqual(v, Val(x), $"{v}");
            Assert.AreEqual(v, x.ToBigInteger(), $"round trip {v}");
        }
    }

    public static IEnumerable<object[]> OutOfRangeBigIntegers()
    {
        yield return new object[] { P(255) };                        // 2^255: first positive overflow (bytes[0] == 0x80)
        yield return new object[] { P(255) + 1 };
        yield return new object[] { P(256) - 1 };                    // 32 bytes, top bit set
        yield return new object[] { P(256) };                        // needs 33 bytes
        yield return new object[] { P(256) + 1 };
        yield return new object[] { P(1000) };
        yield return new object[] { -P(255) - 1 };                   // magnitude bytes[0] == 0x80 with a nonzero tail
        yield return new object[] { -(P(255) + P(247)) };            // 0x80 0x80 00..
        yield return new object[] { -((BigInteger)0x81 << 248) };    // magnitude bytes[0] == 0x81 (> 0x80 branch)
        yield return new object[] { -(P(256) - 1) };                 // magnitude bytes[0] == 0xFF
        yield return new object[] { -P(256) };                       // magnitude needs 33 bytes
        yield return new object[] { -P(256) - 1 };
        yield return new object[] { -P(1000) };
    }

    [TestMethod]
    [DynamicData(nameof(OutOfRangeBigIntegers))]
    public void ExplicitFromBigInteger_OutOfRange_ThrowsOverflowException(BigInteger value)
    {
        var ex = Assert.ThrowsExactly<OverflowException>(() => _ = (int256)value);
        Assert.IsFalse(string.IsNullOrEmpty(ex.Message));
    }

    [TestMethod]
    public void ExplicitFromBigInteger_RangeEdges_AcceptLastValueAndRejectNext()
    {
        Assert.AreEqual(int256.MaxValue, (int256)(P(255) - 1));
        Assert.AreEqual(int256.MinValue, (int256)(-P(255)));
        Assert.ThrowsExactly<OverflowException>(() => _ = (int256)P(255));
        Assert.ThrowsExactly<OverflowException>(() => _ = (int256)(-P(255) - 1));
        Assert.AreEqual(int256.Zero, (int256)BigInteger.Zero);
        Assert.AreEqual(int256.Zero, (int256)BigInteger.Negate(BigInteger.Zero));
    }
}
