using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Every shift count against a BigInteger oracle, for operands that put bits on both sides of every limb boundary. The shift
/// operators read neighbouring limbs at an offset computed from the count, so an indexing, carry or sign-fill error shows up as
/// a mismatch at some count and limb pairing; sparse random limbs and a single bit at every position cover both.
/// </summary>
[TestClass]
public sealed class ShiftEveryCountTests
{
    private static readonly BigInteger Mask256 = (BigInteger.One << 256) - 1;

    [TestMethod]
    public void UInt256_SparseValues_EveryCount_MatchBigInteger()
    {
        var rng = new Random(256_100);
        for (int i = 0; i < 400; i++)
        {
            uint256 x = UInt256TestValues.SparseValue(rng);
            BigInteger v = UInt256TestValues.ToBig(x);
            for (int s = 0; s <= 257; s++)
            {
                Assert.AreEqual(s >= 256 ? 0 : (v << s) & Mask256, UInt256TestValues.ToBig(x << s), $"{v} << {s}");
                Assert.AreEqual(s >= 256 ? 0 : v >> s, UInt256TestValues.ToBig(x >> s), $"{v} >> {s}");
            }
        }
    }

    [TestMethod]
    public void UInt256_ASingleBitAtEveryPosition_EveryCount_MatchBigInteger()
    {
        for (int bit = 0; bit < 256; bit++)
        {
            BigInteger v = BigInteger.One << bit;
            uint256 x = UInt256TestValues.FromBig(v);
            for (int s = 1; s <= 256; s++)
            {
                Assert.AreEqual(s >= 256 ? 0 : (v << s) & Mask256, UInt256TestValues.ToBig(x << s), $"2^{bit} << {s}");
                Assert.AreEqual(s >= 256 ? 0 : v >> s, UInt256TestValues.ToBig(x >> s), $"2^{bit} >> {s}");
            }
        }
    }

    [TestMethod]
    public void Int256_SparseValues_EveryCount_MatchBigInteger()
    {
        var rng = new Random(256_101);
        for (int i = 0; i < 400; i++)
        {
            int256 x = Int256TestValues.SparseValue(rng);
            BigInteger v = Int256TestValues.ToBig(x);
            for (int s = 0; s <= 257; s++)
            {
                // << is logical (wraps mod 2^256); >> is arithmetic, which is BigInteger's floor shift.
                Assert.AreEqual(Int256TestValues.Wrap(s >= 256 ? 0 : v << s), Int256TestValues.ToBig(x << s), $"{v} << {s}");
                Assert.AreEqual(s >= 256 ? (v < 0 ? -1 : 0) : v >> s, Int256TestValues.ToBig(x >> s), $"{v} >> {s}");
            }
        }
    }

    [TestMethod]
    public void Int256_PositiveAndNegativeSingleBits_EveryCount_MatchBigInteger()
    {
        for (int bit = 0; bit < 256; bit++)
        {
            foreach (BigInteger v in bit == 255 ? new[] { -Int256TestValues.Two255 } : new[] { BigInteger.One << bit, -(BigInteger.One << bit), -(BigInteger.One << bit) - 1 })
            {
                int256 x = Int256TestValues.FromBig(v);
                for (int s = 1; s <= 256; s++)
                {
                    Assert.AreEqual(Int256TestValues.Wrap(s >= 256 ? 0 : v << s), Int256TestValues.ToBig(x << s), $"{v} << {s}");
                    Assert.AreEqual(s >= 256 ? (v < 0 ? -1 : 0) : v >> s, Int256TestValues.ToBig(x >> s), $"{v} >> {s}");
                }
            }
        }
    }
}
