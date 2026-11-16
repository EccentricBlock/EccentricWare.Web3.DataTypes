using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class UInt256ConstructorAndPropertyTests
{
    [TestMethod]
    public void Constants_HaveExpectedLimbs()
    {
        Assert.AreEqual((0UL, 0UL, 0UL, 0UL), (uint256.Zero.U0, uint256.Zero.U1, uint256.Zero.U2, uint256.Zero.U3));
        Assert.AreEqual((1UL, 0UL, 0UL, 0UL), (uint256.One.U0, uint256.One.U1, uint256.One.U2, uint256.One.U3));
        Assert.AreEqual((ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue),
            (uint256.MaxValue.U0, uint256.MaxValue.U1, uint256.MaxValue.U2, uint256.MaxValue.U3));
        Assert.AreEqual(UInt256TestValues.Two256 - 1, UInt256TestValues.ToBig(uint256.MaxValue));
    }

    [TestMethod]
    [DataRow(0UL)]
    [DataRow(1UL)]
    [DataRow(0x8000_0000_0000_0000UL)]
    [DataRow(ulong.MaxValue)]
    public void Constructor_FromUlong_PlacesValueInTheLowLimb(ulong value)
    {
        uint256 v = new(value);

        Assert.AreEqual((value, 0UL, 0UL, 0UL), (v.U0, v.U1, v.U2, v.U3));
    }

    [TestMethod]
    public void Constructor_FromLimbs_AssignsLeastSignificantFirst()
    {
        uint256 v = new(1UL, 2UL, 3UL, 4UL);

        Assert.AreEqual((1UL, 2UL, 3UL, 4UL), (v.U0, v.U1, v.U2, v.U3));
        Assert.AreEqual(1 + (new BigInteger(2) << 64) + (new BigInteger(3) << 128) + (new BigInteger(4) << 192), UInt256TestValues.ToBig(v));
    }

    [TestMethod]
    public void Constructor_FromUInt128Halves_PlacesEachHalfInOrder()
    {
        // UInt128(upper, lower): low = 0x0123456789ABCDEF_0FEDCBA987654321, high = 0x1122334455667788_99AABBCCDDEEFF00.
        UInt128 low = new(0x0123_4567_89AB_CDEFUL, 0x0FED_CBA9_8765_4321UL);
        UInt128 high = new(0x1122_3344_5566_7788UL, 0x99AA_BBCC_DDEE_FF00UL);

        uint256 v = new(low, high);

        Assert.AreEqual((0x0FED_CBA9_8765_4321UL, 0x0123_4567_89AB_CDEFUL, 0x99AA_BBCC_DDEE_FF00UL, 0x1122_3344_5566_7788UL), (v.U0, v.U1, v.U2, v.U3));
        Assert.AreEqual(low, v.Low128);
        Assert.AreEqual(high, v.High128);

        uint256 lowOnly = new(UInt128.MaxValue, UInt128.Zero);
        Assert.AreEqual((ulong.MaxValue, ulong.MaxValue, 0UL, 0UL), (lowOnly.U0, lowOnly.U1, lowOnly.U2, lowOnly.U3));

        uint256 highOnly = new(UInt128.Zero, UInt128.MaxValue);
        Assert.AreEqual((0UL, 0UL, ulong.MaxValue, ulong.MaxValue), (highOnly.U0, highOnly.U1, highOnly.U2, highOnly.U3));
    }

    // Every property is checked for values that populate exactly one limb, so a wrong limb in any reduction is caught.
    [TestMethod]
    [DataRow(0UL, 0UL, 0UL, 0UL, true, false, true, true)]
    [DataRow(1UL, 0UL, 0UL, 0UL, false, true, true, true)]
    [DataRow(2UL, 0UL, 0UL, 0UL, false, false, true, true)]
    [DataRow(ulong.MaxValue, 0UL, 0UL, 0UL, false, false, true, true)]
    [DataRow(0UL, 1UL, 0UL, 0UL, false, false, false, true)]
    [DataRow(0UL, 0UL, 1UL, 0UL, false, false, false, false)]
    [DataRow(0UL, 0UL, 0UL, 1UL, false, false, false, false)]
    [DataRow(ulong.MaxValue, ulong.MaxValue, 0UL, 0UL, false, false, false, true)]
    [DataRow(1UL, 1UL, 0UL, 0UL, false, false, false, true)]
    [DataRow(1UL, 0UL, 1UL, 0UL, false, false, false, false)]
    [DataRow(1UL, 0UL, 0UL, 1UL, false, false, false, false)]
    [DataRow(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, false, false, false, false)]
    public void Properties_ForSingleLimbShapes(ulong u0, ulong u1, ulong u2, ulong u3, bool isZero, bool isOne, bool fitsUlong, bool fitsUInt128)
    {
        uint256 v = new(u0, u1, u2, u3);

        Assert.AreEqual(isZero, v.IsZero);
        Assert.AreEqual(isOne, v.IsOne);
        Assert.AreEqual(fitsUlong, v.FitsInUlong);
        Assert.AreEqual(fitsUInt128, v.FitsInUInt128);
    }

    [TestMethod]
    public void LowAndHighViews_ComposeTheLimbs()
    {
        uint256 v = new(ulong.MaxValue, 1UL, 2UL, 3UL);

        Assert.AreEqual(ulong.MaxValue, v.Low64);
        Assert.AreEqual(new UInt128(1UL, ulong.MaxValue), v.Low128);
        Assert.AreEqual(new UInt128(3UL, 2UL), v.High128);

        uint256 twoPow64 = new(0, 1, 0, 0);
        Assert.AreEqual(0UL, twoPow64.Low64);
        Assert.AreEqual((UInt128)1 << 64, twoPow64.Low128);
        Assert.AreEqual(UInt128.Zero, twoPow64.High128);
    }
}
