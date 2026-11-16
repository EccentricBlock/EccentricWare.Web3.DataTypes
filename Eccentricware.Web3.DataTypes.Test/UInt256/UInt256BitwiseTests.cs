using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class UInt256BitwiseTests
{
    private static readonly BigInteger Mask256 = UInt256TestValues.Two256 - 1;

    private static uint256 U(BigInteger value) => UInt256TestValues.FromBig(value);

    private static BigInteger Big(uint256 value) => UInt256TestValues.ToBig(value);

    private static IEnumerable<uint256> SampleValues(int seed)
    {
        foreach (BigInteger boundary in UInt256TestValues.Boundaries)
            yield return U(boundary);
        var rng = new Random(seed);
        for (int i = 0; i < 200; i++)
            yield return UInt256TestValues.SparseValue(rng);
    }

    [TestMethod]
    public void BitwiseOperators_MatchBigInteger()
    {
        uint256[] values = SampleValues(555).ToArray();
        for (int i = 0; i < values.Length; i++)
        {
            uint256 x = values[i], y = values[(i * 7 + 3) % values.Length];
            BigInteger bx = Big(x), by = Big(y);

            Assert.AreEqual(bx & by, Big(x & y));
            Assert.AreEqual(bx | by, Big(x | y));
            Assert.AreEqual(bx ^ by, Big(x ^ y));
            Assert.AreEqual(~bx & Mask256, Big(~x));
        }

        Assert.AreEqual(new uint256(0x05UL), new uint256(0x55UL) & new uint256(0x0FUL));
        Assert.AreEqual(new uint256(0x5FUL), new uint256(0x55UL) | new uint256(0x0FUL));
        Assert.AreEqual(new uint256(0x5AUL), new uint256(0x55UL) ^ new uint256(0x0FUL));
        Assert.AreEqual(uint256.MaxValue, ~uint256.Zero);
    }

    [TestMethod]
    public void Shifts_EveryCountUpTo300_MatchBigInteger()
    {
        uint256[] values = [uint256.One, uint256.MaxValue, new(0x0123_4567_89AB_CDEFUL, 0xFEDC_BA98_7654_3210UL, 0x8000_0000_0000_0001UL, 0x0000_0000_FFFF_0000UL), U(BigInteger.One << 255)];
        foreach (uint256 value in values)
        {
            BigInteger big = Big(value);
            for (int shift = 0; shift <= 300; shift++)
            {
                BigInteger expectedLeft = shift >= 256 ? 0 : (big << shift) & Mask256;
                BigInteger expectedRight = shift >= 256 ? 0 : big >> shift;

                Assert.AreEqual(expectedLeft, Big(value << shift), $"{value} << {shift}");
                Assert.AreEqual(expectedRight, Big(value >> shift), $"{value} >> {shift}");
            }

            Assert.AreEqual(uint256.Zero, value << int.MaxValue);
            Assert.AreEqual(uint256.Zero, value >> int.MaxValue);
        }
    }

    [TestMethod]
    public void Shifts_NegativeCount_Throws_WithTheParameterName()
    {
        foreach (int shift in new[] { -1, int.MinValue })
        {
            Assert.AreEqual("shift", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = uint256.One << shift).ParamName);
            Assert.AreEqual("shift", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = uint256.One >> shift).ParamName);
        }
    }

    [TestMethod]
    public void RightShift_IsLogical_NotArithmetic()
    {
        Assert.AreEqual(uint256.One, (uint256.One << 255) >> 255);
        Assert.AreEqual(U(BigInteger.One << 254), (uint256.One << 255) >> 1);
        Assert.AreEqual(uint256.One, uint256.MaxValue >> 255);
    }

    [TestMethod]
    public void BitCounts_MatchBigInteger()
    {
        foreach (uint256 value in SampleValues(666).Concat(Enumerable.Range(0, 256).SelectMany(k => new[] { U(BigInteger.One << k), U(Mask256 >> k) })))
        {
            BigInteger big = Big(value);
            int expectedLeading = 256 - (int)big.GetBitLength();
            int expectedTrailing = big.IsZero ? 256 : (int)BigInteger.TrailingZeroCount(big);
            int expectedPop = big.ToByteArray(isUnsigned: true).Sum(b => BitOperations.PopCount(b));

            Assert.AreEqual(expectedLeading, value.LeadingZeroCount(), $"leading zeros of {big}");
            Assert.AreEqual(expectedTrailing, value.TrailingZeroCount(), $"trailing zeros of {big}");
            Assert.AreEqual(expectedPop, value.PopCount(), $"pop count of {big}");
        }

        Assert.AreEqual((256, 256, 0), (uint256.Zero.LeadingZeroCount(), uint256.Zero.TrailingZeroCount(), uint256.Zero.PopCount()));
        Assert.AreEqual((0, 0, 256), (uint256.MaxValue.LeadingZeroCount(), uint256.MaxValue.TrailingZeroCount(), uint256.MaxValue.PopCount()));
    }
}
