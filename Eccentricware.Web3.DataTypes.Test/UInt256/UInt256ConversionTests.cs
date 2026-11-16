using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Implicit and explicit conversions between uint256 and the integer types, BigInteger and HexBigInteger.</summary>
[TestClass]
public sealed class UInt256ConversionTests
{
    private static readonly BigInteger Two256 = UInt256TestValues.Two256;

    private static uint256 U(BigInteger value) => UInt256TestValues.FromBig(value);

    private static BigInteger Big(uint256 value) => UInt256TestValues.ToBig(value);

    // ---- implicit from integers ----

    [TestMethod]
    public void ImplicitFromUlong()
    {
        foreach (ulong value in new[] { 0UL, 1UL, ulong.MaxValue })
        {
            uint256 converted = value;
            Assert.AreEqual((value, 0UL, 0UL, 0UL), (converted.U0, converted.U1, converted.U2, converted.U3));
        }
    }

    [TestMethod]
    public void ImplicitFromUint()
    {
        foreach (uint value in new[] { 0u, 1u, uint.MaxValue })
        {
            uint256 converted = value;
            Assert.AreEqual((value, 0UL, 0UL, 0UL), (converted.U0, converted.U1, converted.U2, converted.U3));
        }
    }

    [TestMethod]
    public void ImplicitFromNonNegativeInt_AndLong()
    {
        foreach (int value in new[] { 0, 1, int.MaxValue })
        {
            uint256 converted = value;
            Assert.AreEqual(new BigInteger(value), Big(converted));
        }

        foreach (long value in new[] { 0L, 1L, long.MaxValue })
        {
            uint256 converted = value;
            Assert.AreEqual(new BigInteger(value), Big(converted));
        }
    }

    [TestMethod]
    public void ImplicitFromNegativeInt_AndLong_ThrowsOverflow()
    {
        Assert.ThrowsExactly<OverflowException>(() => { uint256 _ = -1; });
        Assert.ThrowsExactly<OverflowException>(() => { uint256 _ = int.MinValue; });
        Assert.ThrowsExactly<OverflowException>(() => { uint256 _ = -1L; });
        Assert.ThrowsExactly<OverflowException>(() => { uint256 _ = long.MinValue; });
        Assert.ThrowsExactly<OverflowException>(() => { uint256 _ = (short)-1; });
    }

    // byte and ushort are ambiguous between the ulong and uint overloads (CS0457), so only signed small types convert implicitly.
    [TestMethod]
    public void ImplicitFromSignedSmallIntegerTypes_PicksAnOverload()
    {
        uint256 fromShort = (short)5;
        uint256 fromSbyte = (sbyte)5;

        Assert.AreEqual(new uint256(5UL), fromShort);
        Assert.AreEqual(new uint256(5UL), fromSbyte);
    }

    [TestMethod]
    public void ImplicitFromUInt128()
    {
        uint256 zero = UInt128.Zero;
        uint256 max = UInt128.MaxValue;
        uint256 twoPow64 = (UInt128)1 << 64;

        Assert.AreEqual(uint256.Zero, zero);
        Assert.AreEqual((ulong.MaxValue, ulong.MaxValue, 0UL, 0UL), (max.U0, max.U1, max.U2, max.U3));
        Assert.AreEqual((0UL, 1UL, 0UL, 0UL), (twoPow64.U0, twoPow64.U1, twoPow64.U2, twoPow64.U3));
    }

    // ---- explicit to integers ----

    [TestMethod]
    public void ExplicitToUlong_BoundariesAndOverflow()
    {
        Assert.AreEqual(0UL, (ulong)uint256.Zero);
        Assert.AreEqual(ulong.MaxValue, (ulong)new uint256(ulong.MaxValue));

        foreach (uint256 tooBig in new uint256[] { new(0, 1, 0, 0), new(0, 0, 1, 0), new(0, 0, 0, 1), new(1, 1, 0, 0), uint256.MaxValue })
            Assert.ThrowsExactly<OverflowException>(() => _ = (ulong)tooBig, tooBig.ToString());
    }

    [TestMethod]
    public void ExplicitToUInt128_BoundariesAndOverflow()
    {
        Assert.AreEqual(UInt128.Zero, (UInt128)uint256.Zero);
        Assert.AreEqual(UInt128.MaxValue, (UInt128)new uint256(ulong.MaxValue, ulong.MaxValue, 0, 0));
        Assert.AreEqual(new UInt128(123, 456), (UInt128)new uint256(456, 123, 0, 0));

        foreach (uint256 tooBig in new uint256[] { new(0, 0, 1, 0), new(0, 0, 0, 1), new(1, 1, 1, 0), uint256.MaxValue })
            Assert.ThrowsExactly<OverflowException>(() => _ = (UInt128)tooBig, tooBig.ToString());
    }

    // ---- BigInteger ----

    [TestMethod]
    public void BigInteger_RoundTrips_AtBoundaries()
    {
        foreach (BigInteger value in UInt256TestValues.Boundaries.Append(BigInteger.Parse("123456789012345678901234567890")))
        {
            uint256 converted = (uint256)value;

            Assert.AreEqual(UInt256TestValues.FromBig(value), converted, value.ToString());
            Assert.AreEqual(value, converted.ToBigInteger(), value.ToString());
            Assert.AreEqual(value, (BigInteger)converted, "implicit conversion to BigInteger");
            Assert.AreEqual(value, Big(converted));
        }
    }

    [TestMethod]
    public void ExplicitFromBigInteger_OutOfRange_ThrowsOverflow()
    {
        Assert.ThrowsExactly<OverflowException>(() => _ = (uint256)BigInteger.MinusOne);
        Assert.ThrowsExactly<OverflowException>(() => _ = (uint256)(-Two256));
        Assert.ThrowsExactly<OverflowException>(() => _ = (uint256)Two256);
        Assert.ThrowsExactly<OverflowException>(() => _ = (uint256)(Two256 * 1000));
        Assert.AreEqual(uint256.MaxValue, (uint256)(Two256 - 1));
    }

    // ---- HexBigInteger ----

    [TestMethod]
    public void HexBigInteger_RoundTrips_AtBoundaries()
    {
        foreach (BigInteger value in new BigInteger[] { 0, 255, Two256 - 1 })
        {
            HexBigInteger hex = U(value);

            Assert.AreEqual(value, hex.Value);
            Assert.AreEqual(U(value), (uint256)hex);
        }
    }

    [TestMethod]
    public void ExplicitFromHexBigInteger_OutOfRange_ThrowsOverflow()
    {
        Assert.ThrowsExactly<OverflowException>(() => _ = (uint256)HexBigInteger.MinusOne);
        Assert.ThrowsExactly<OverflowException>(() => _ = (uint256)new HexBigInteger(Two256));
    }
}
