using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Shifts of <see cref="int256"/>: logical left, arithmetic right, counts of 256 or more and negative counts.</summary>
[TestClass]
public sealed class Int256ShiftTests
{
    private static int256 Big(BigInteger v) => Int256TestValues.FromBig(v);

    private static BigInteger Val(int256 v) => Int256TestValues.ToBig(v);

    [TestMethod]
    public void ShiftLeft_Basic()
    {
        Assert.AreEqual(new int256(2), new int256(1) << 1);
        Assert.AreEqual(new int256(0), int256.Zero << 123);
    }

    [TestMethod]
    public void ShiftRight_Arithmetic_ForNegativeValues()
    {
        Assert.AreEqual(int256.MinusOne, int256.MinusOne >> 1);
        Assert.AreEqual(int256.MinusOne, int256.MinusOne >> 255);
        Assert.AreEqual(int256.MinusOne, int256.MinValue >> 255);
    }

    [TestMethod]
    public void ShiftRight_Arithmetic_ForPositiveValues()
    {
        Assert.AreEqual(int256.Zero, int256.One >> 1);
        Assert.AreEqual(int256.One, new int256(2) >> 1);
    }

    [TestMethod]
    public void Shift_CountOf256OrMore_ShiftsEverythingOut_LikeUInt256()
    {
        var positive = Big(123456);
        var negative = Big(-123456);

        // Left shifts discard every bit, whatever the sign.
        foreach (int count in new[] { 256, 257, 300, 512, int.MaxValue })
        {
            Assert.AreEqual(int256.Zero, positive << count, $"<< {count}");
            Assert.AreEqual(int256.Zero, negative << count, $"<< {count}");
            Assert.AreEqual(int256.Zero, int256.MinusOne << count, $"<< {count}");

            // Arithmetic right shifts: zero for non-negative values, all ones (-1) for negative ones, which is also
            // what shifting by 255 gives.
            Assert.AreEqual(int256.Zero, positive >> count, $">> {count}");
            Assert.AreEqual(int256.MinusOne, negative >> count, $">> {count}");
            Assert.AreEqual(int256.MinusOne, int256.MinValue >> count, $">> {count}");
            Assert.AreEqual(int256.Zero, int256.MaxValue >> count, $">> {count}");
        }

        Assert.AreEqual(int256.MinusOne, negative >> 255);
        Assert.AreEqual(int256.Zero, positive >> 255);
    }

    [TestMethod]
    public void Shift_NegativeCount_Throws_LikeUInt256()
    {
        var v = Big(123456);

        foreach (int count in new[] { -1, -64, -256, int.MinValue })
        {
            var left = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = v << count);
            var right = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = v >> count);
            Assert.AreEqual("shift", left.ParamName);
            Assert.AreEqual("shift", right.ParamName);
        }

        // The negative-count check also applies to the extremes.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = int256.MinValue << -1);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = int256.MinValue >> -1);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = int256.Zero << -1);
    }

    [TestMethod]
    public void Shift_ByZero_ReturnsTheValue_EvenForExtremes()
    {
        Assert.AreEqual(int256.MinValue, int256.MinValue << 0);
        Assert.AreEqual(int256.MaxValue, int256.MaxValue >> 0);
        Assert.AreEqual(int256.MinValue, int256.MinValue >> 0);
        Assert.AreEqual(int256.MaxValue, int256.MaxValue << 0);
    }

    [TestMethod]
    public void ShiftLeft_ToSignBit_CreatesMinValue()
    {
        Assert.AreEqual(int256.MinValue, int256.One << 255);
        Assert.AreEqual(int256.Zero, int256.One << 256);
        Assert.AreEqual(int256.MinValue, int256.MinusOne << 255);
        Assert.AreEqual(int256.Zero, int256.MinValue << 1);
    }

    [TestMethod]
    public void MinValue_ArithmeticRightShift_IsAMinusPowerOfTwo_ForEveryCount()
    {
        for (int s = 0; s <= 255; s++)
            Assert.AreEqual(-Int256TestValues.Pow2(255 - s), Val(int256.MinValue >> s), $">> {s}");
    }

    [TestMethod]
    public void MaxValue_RightShift_IsAMersenneNumber_ForEveryCount()
    {
        for (int s = 0; s <= 255; s++)
            Assert.AreEqual(Int256TestValues.Pow2(255 - s) - 1, Val(int256.MaxValue >> s), $">> {s}");
    }

    [TestMethod]
    public void BoundaryValues_ShiftByEveryCount_MatchBigInteger()
    {
        foreach (BigInteger v in Int256TestValues.BoundaryValues)
        {
            int256 x = Big(v);
            for (int s = 0; s <= 300; s++)
            {
                // << is logical (wraps mod 2^256); >> is arithmetic, i.e. BigInteger's floor shift.
                Assert.AreEqual(Int256TestValues.Wrap(v << s), Val(x << s), $"{v} << {s}");
                Assert.AreEqual(v >> s, Val(x >> s), $"{v} >> {s}");
            }
        }
    }

    [TestMethod]
    public void SparseRandomLimbs_ShiftAtLimbEdges_MatchBigInteger()
    {
        var rng = new Random(256_030);
        int[] counts = [0, 1, 31, 32, 63, 64, 65, 127, 128, 129, 191, 192, 193, 254, 255, 256, 257];
        for (int i = 0; i < 500; i++)
        {
            int256 x = Int256TestValues.SparseValue(rng);
            BigInteger v = Val(x);
            foreach (int s in counts)
            {
                Assert.AreEqual(Int256TestValues.Wrap(v << s), Val(x << s), $"{v} << {s}");
                Assert.AreEqual(v >> s, Val(x >> s), $"{v} >> {s}");
            }
        }
    }

    [TestMethod]
    public void LeftThenRightShift_RecoversTheLowBits_ForNonNegativeValues()
    {
        // (v << s) >> s == v when no set bit is shifted out, which only holds for s below the value's leading zeros.
        var v = Big(0x1234_5678_9ABC_DEF0);
        for (int s = 0; s <= 190; s++)
            Assert.AreEqual(v, (v << s) >> s, $"s={s}");
    }
}
