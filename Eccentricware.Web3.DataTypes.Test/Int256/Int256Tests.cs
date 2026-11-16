using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Constants, properties, limb layout, long construction and equality of <see cref="int256"/>.</summary>
[TestClass]
public sealed class Int256Tests
{
    [TestMethod]
    public void Constants_ZeroOneMinusOne_MinMax_BasicProperties()
    {
        Assert.IsTrue(int256.Zero.IsZero);
        Assert.IsFalse(int256.Zero.IsNegative);
        Assert.AreEqual(0, int256.Zero.Sign);

        Assert.IsFalse(int256.One.IsZero);
        Assert.IsFalse(int256.One.IsNegative);
        Assert.AreEqual(1, int256.One.Sign);

        Assert.IsFalse(int256.MinusOne.IsZero);
        Assert.IsTrue(int256.MinusOne.IsNegative);
        Assert.AreEqual(-1, int256.MinusOne.Sign);

        Assert.IsTrue(int256.MinValue.IsNegative);
        Assert.IsFalse(int256.MinValue.IsZero);
        Assert.AreEqual(-1, int256.MinValue.Sign);

        Assert.IsFalse(int256.MaxValue.IsNegative);
        Assert.IsFalse(int256.MaxValue.IsZero);
        Assert.AreEqual(1, int256.MaxValue.Sign);
    }

    [TestMethod]
    public void Constants_HaveTheDocumentedLimbsAndValues()
    {
        Assert.AreEqual(BigInteger.Zero, Int256TestValues.ToBig(int256.Zero));
        Assert.AreEqual(BigInteger.One, Int256TestValues.ToBig(int256.One));
        Assert.AreEqual(BigInteger.MinusOne, Int256TestValues.ToBig(int256.MinusOne));
        Assert.AreEqual(-Int256TestValues.Two255, Int256TestValues.ToBig(int256.MinValue));
        Assert.AreEqual(Int256TestValues.Two255 - 1, Int256TestValues.ToBig(int256.MaxValue));

        Assert.AreEqual(0UL, int256.MinValue.U0 | int256.MinValue.U1 | int256.MinValue.U2);
        Assert.AreEqual(0x8000_0000_0000_0000UL, int256.MinValue.U3);
        Assert.AreEqual(0x7FFF_FFFF_FFFF_FFFFUL, int256.MaxValue.U3);
        Assert.AreEqual(ulong.MaxValue, int256.MaxValue.U0 & int256.MaxValue.U1 & int256.MaxValue.U2);
    }

    [TestMethod]
    public void LimbAccessors_ReturnTheLimbsInLeastSignificantFirstOrder()
    {
        var value = new int256(1, 2, 3, 4);
        Assert.AreEqual(1UL, value.U0);
        Assert.AreEqual(2UL, value.U1);
        Assert.AreEqual(3UL, value.U2);
        Assert.AreEqual(4UL, value.U3);
    }

    // ---- IsZero / IsNegative / Sign per limb ----

    [TestMethod]
    [DataRow(1UL, 0UL, 0UL, 0UL)]
    [DataRow(0UL, 1UL, 0UL, 0UL)]
    [DataRow(0UL, 0UL, 1UL, 0UL)]
    [DataRow(0UL, 0UL, 0UL, 1UL)]
    [DataRow(0UL, 0UL, 0UL, 0x7FFF_FFFF_FFFF_FFFFUL)]
    [DataRow(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, 0x7FFF_FFFF_FFFF_FFFFUL)]
    public void NonZeroPositive_InAnyLimb_IsPositive(ulong u0, ulong u1, ulong u2, ulong u3)
    {
        var value = new int256(u0, u1, u2, u3);
        Assert.IsFalse(value.IsZero);
        Assert.IsFalse(value.IsNegative);
        Assert.AreEqual(1, value.Sign);
    }

    [TestMethod]
    [DataRow(0UL, 0UL, 0UL, 0x8000_0000_0000_0000UL)]
    [DataRow(1UL, 0UL, 0UL, 0x8000_0000_0000_0000UL)]
    [DataRow(0UL, 0UL, 0UL, ulong.MaxValue)]
    [DataRow(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue)]
    public void SignBitSet_InTopLimb_IsNegative_EvenWhenLowerLimbsAreZero(ulong u0, ulong u1, ulong u2, ulong u3)
    {
        var value = new int256(u0, u1, u2, u3);
        Assert.IsFalse(value.IsZero);
        Assert.IsTrue(value.IsNegative);
        Assert.AreEqual(-1, value.Sign);
    }

    [TestMethod]
    public void SignBitInLowerLimbs_DoesNotMakeTheValueNegative()
    {
        Assert.IsFalse(new int256(0x8000_0000_0000_0000UL, 0, 0, 0).IsNegative);
        Assert.IsFalse(new int256(0, 0x8000_0000_0000_0000UL, 0, 0).IsNegative);
        Assert.IsFalse(new int256(0, 0, 0x8000_0000_0000_0000UL, 0).IsNegative);
        Assert.AreNotEqual(int256.MinusOne, new int256(0, 0, 0, 0x8000_0000_0000_0000UL));
    }

    // ---- constructor from long ----

    [TestMethod]
    [DataRow(0L)]
    [DataRow(1L)]
    [DataRow(-1L)]
    [DataRow(2L)]
    [DataRow(-2L)]
    [DataRow(long.MaxValue)]
    [DataRow(long.MinValue)]
    [DataRow(long.MinValue + 1)]
    [DataRow((long)int.MinValue)]
    [DataRow((long)int.MaxValue)]
    [DataRow((long)uint.MaxValue)]
    [DataRow(1L << 32)]
    [DataRow(-(1L << 32))]
    public void ConstructFromLong_SignExtendsToAllLimbs(long v)
    {
        int256 viaCtor = new(v);
        int256 viaImplicit = v;
        ulong fill = v < 0 ? ulong.MaxValue : 0UL;

        Assert.AreEqual(viaCtor, viaImplicit);
        Assert.AreEqual((ulong)v, viaCtor.U0);
        Assert.AreEqual(fill, viaCtor.U1);
        Assert.AreEqual(fill, viaCtor.U2);
        Assert.AreEqual(fill, viaCtor.U3);
        Assert.AreEqual((BigInteger)v, Int256TestValues.ToBig(viaCtor));
        Assert.AreEqual(Math.Sign(v), viaCtor.Sign);
        Assert.AreEqual(v < 0, viaCtor.IsNegative);

        // Big-endian layout checked against BitConverter, not against the type's own conversions.
        byte[] expectedLow = BitConverter.GetBytes(v);
        Array.Reverse(expectedLow);
        byte[] be = viaCtor.ToBigEndianBytes();
        CollectionAssert.AreEqual(expectedLow, be[24..]);
        Assert.IsTrue(be[..24].All(b => b == (v < 0 ? 0xFF : 0x00)));
    }

    [TestMethod]
    public void ImplicitConversion_FromNarrowerIntegers_Compiles_AndKeepsTheValue()
    {
        int256 fromInt = int.MinValue;
        int256 fromShort = short.MinValue;
        int256 fromByte = byte.MaxValue;
        Assert.AreEqual(new BigInteger(int.MinValue), Int256TestValues.ToBig(fromInt));
        Assert.AreEqual(new BigInteger(short.MinValue), Int256TestValues.ToBig(fromShort));
        Assert.AreEqual(new BigInteger(255), Int256TestValues.ToBig(fromByte));
    }

    // ---- equality ----

    [TestMethod]
    public void Equals_OperatorAndObjectEquality()
    {
        var a = new int256(123);
        var b = new int256(123);
        var c = new int256(-123);

        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);

        Assert.IsFalse(a == c);
        Assert.IsTrue(a != c);

        Assert.IsTrue(a.Equals(b));
        Assert.IsFalse(a.Equals(c));
        Assert.IsTrue(a.Equals((object)b));
        Assert.IsFalse(a.Equals((object)c));
        Assert.IsFalse(a.Equals(null));
    }

    [TestMethod]
    public void EqualsObject_ForeignTypes_IsFalse_EvenWithTheSameBits()
    {
        object five = new int256(5);
        Assert.IsFalse(five.Equals(5L));
        Assert.IsFalse(five.Equals(5));
        Assert.IsFalse(five.Equals(new uint256(5UL)));
        Assert.IsFalse(five.Equals(new BigInteger(5)));
        Assert.IsFalse(five.Equals("5"));
        Assert.IsTrue(five.Equals(new int256(5)));
    }

    [TestMethod]
    public void Equality_IsDecidedByEveryLimb()
    {
        var baseValue = new int256(1, 2, 3, 4);
        Assert.IsTrue(baseValue == new int256(1, 2, 3, 4));
        Assert.IsTrue(baseValue != new int256(0, 2, 3, 4));
        Assert.IsTrue(baseValue != new int256(1, 0, 3, 4));
        Assert.IsTrue(baseValue != new int256(1, 2, 0, 4));
        Assert.IsTrue(baseValue != new int256(1, 2, 3, 0));
        Assert.IsTrue(baseValue != new int256(1, 2, 3, 4 | 0x8000_0000_0000_0000UL));
    }
}
