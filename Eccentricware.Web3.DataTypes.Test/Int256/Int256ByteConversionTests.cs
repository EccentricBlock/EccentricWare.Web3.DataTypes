using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Big- and little-endian byte conversions of <see cref="int256"/>, including sign extension from short inputs.</summary>
[TestClass]
public sealed class Int256ByteConversionTests
{
    private static BigInteger Val(int256 v) => Int256TestValues.ToBig(v);

    // Limbs (1, 2, 3, 4): u3 = 4 is the most significant limb, so it comes first in big-endian order.
    private static readonly byte[] BigEndianOneTwoThreeFour =
    [
        0, 0, 0, 0, 0, 0, 0, 4,
        0, 0, 0, 0, 0, 0, 0, 3,
        0, 0, 0, 0, 0, 0, 0, 2,
        0, 0, 0, 0, 0, 0, 0, 1,
    ];

    // ---- constructor from bytes ----

    [TestMethod]
    public void Ctor_FromBytes_Empty_IsZero()
        => Assert.AreEqual(int256.Zero, new int256(ReadOnlySpan<byte>.Empty));

    [TestMethod]
    public void Ctor_FromBytes_SignExtends_Positive()
    {
        Assert.AreEqual(new BigInteger(127), Val(new int256(new byte[] { 0x7F })));
        Assert.AreEqual(new BigInteger(128), Val(new int256(new byte[] { 0x00, 0x80 })));
    }

    [TestMethod]
    public void Ctor_FromBytes_SignExtends_Negative()
    {
        Assert.AreEqual(int256.MinusOne, new int256(new byte[] { 0xFF }));
        Assert.AreEqual(new BigInteger(-128), Val(new int256(new byte[] { 0x80 })));
        Assert.AreEqual(new BigInteger(-32768), Val(new int256(new byte[] { 0x80, 0x00 })));
    }

    [TestMethod]
    public void Ctor_FromBytes_ShortInputs_UseExplicitSignExtensionLiterals()
    {
        Assert.AreEqual(new int256(255), new int256(new byte[] { 0x00, 0xFF }));
        Assert.AreEqual(int256.MinusOne, new int256(new byte[] { 0xFF, 0xFF }));
        Assert.AreEqual(new int256(256), new int256(new byte[] { 0x01, 0x00 }));
        Assert.AreEqual(new int256(-256), new int256(new byte[] { 0xFF, 0x00 }));
    }

    [TestMethod]
    public void Ctor_FromBytes_TooLong_ThrowsArgumentException()
    {
        foreach (int length in new[] { 33, 34, 64, 1000 })
            Assert.ThrowsExactly<ArgumentException>(() => _ = new int256(new byte[length]));
    }

    [TestMethod]
    public void Ctor_FromBytes_AllLengths_SignExtendLikeBigInteger()
    {
        var rng = new Random(256_040);
        for (int length = 0; length <= 32; length++)
        {
            foreach (bool negative in new[] { false, true })
            {
                byte[] bytes = new byte[length];
                rng.NextBytes(bytes);
                if (length > 0)
                    bytes[0] = (byte)(negative ? bytes[0] | 0x80 : bytes[0] & 0x7F);

                BigInteger expected = new(bytes, isUnsigned: false, isBigEndian: true);
                Assert.AreEqual(expected, Val(new int256(bytes)), $"length {length}, negative {negative}");
            }
        }
    }

    // ---- known encodings ----

    [TestMethod]
    public void BigEndianAndLittleEndianBytes_MatchHandWrittenVectors()
    {
        var value = new int256(1, 2, 3, 4);

        CollectionAssert.AreEqual(BigEndianOneTwoThreeFour, value.ToBigEndianBytes());
        CollectionAssert.AreEqual(BigEndianOneTwoThreeFour.Reverse().ToArray(), value.ToLittleEndianBytes());
        Assert.AreEqual(value, new int256(BigEndianOneTwoThreeFour));
    }

    [TestMethod]
    public void WriteBigEndian_KnownEncoding_ForOne_And_MinusOne()
    {
        Span<byte> be = stackalloc byte[32];

        int256.One.WriteBigEndian(be);
        Assert.IsTrue(be.Slice(0, 31).ToArray().All(b => b == 0x00));
        Assert.AreEqual(0x01, be[31]);

        int256.MinusOne.WriteBigEndian(be);
        Assert.IsTrue(be.ToArray().All(b => b == 0xFF));
    }

    [TestMethod]
    public void WriteLittleEndian_KnownEncoding_ForOne_And_MinValue()
    {
        Span<byte> le = stackalloc byte[32];

        int256.One.WriteLittleEndian(le);
        Assert.AreEqual(0x01, le[0]);
        Assert.IsTrue(le.Slice(1).ToArray().All(b => b == 0x00));

        int256.MinValue.WriteLittleEndian(le);
        Assert.AreEqual(0x80, le[31]);
        Assert.IsTrue(le.Slice(0, 31).ToArray().All(b => b == 0x00));
    }

    [TestMethod]
    public void WriteBigEndian_And_CtorFrom32Bytes_RoundTrips()
    {
        var original = new int256(0x0123456789ABCDEFUL, 0x0FEDCBA987654321UL, 0x1122334455667788UL, 0x99AABBCCDDEEFF00UL);

        Span<byte> be = stackalloc byte[32];
        original.WriteBigEndian(be);

        Assert.AreEqual(original, new int256(be));
    }

    [TestMethod]
    public void WriteLittleEndian_MatchesBigIntegerLittleEndian()
    {
        var original = Int256TestValues.FromBig(-1234567890123456789L);

        Span<byte> le = stackalloc byte[32];
        original.WriteLittleEndian(le);

        Assert.AreEqual(new BigInteger(-1234567890123456789L), new BigInteger(le, isUnsigned: false, isBigEndian: false));
    }

    // ---- sizes, destinations, fresh arrays ----

    [TestMethod]
    public void ToBytes_AreExactly32Bytes_AndLittleEndianIsTheReverse_ForBoundaryValues()
    {
        foreach (BigInteger v in Int256TestValues.BoundaryValues)
        {
            int256 x = Int256TestValues.FromBig(v);
            byte[] be = x.ToBigEndianBytes();
            byte[] le = x.ToLittleEndianBytes();

            Assert.HasCount(32, be);
            Assert.HasCount(32, le);
            CollectionAssert.AreEqual(be.Reverse().ToArray(), le, $"{v}");
            Assert.AreEqual(v, new BigInteger(be, isUnsigned: false, isBigEndian: true), $"{v} BE");
            Assert.AreEqual(v, new BigInteger(le, isUnsigned: false, isBigEndian: false), $"{v} LE");
            Assert.AreEqual(x, new int256(be), $"{v} round trip");
        }
    }

    [TestMethod]
    public void ToBytes_ReturnFreshArrays_ThatDoNotAliasEachOther()
    {
        var value = new int256(1, 2, 3, 4);
        byte[] first = value.ToBigEndianBytes();
        first[31] = 0xEE;

        CollectionAssert.AreEqual(BigEndianOneTwoThreeFour, value.ToBigEndianBytes());
        Assert.AreNotSame(value.ToLittleEndianBytes(), value.ToLittleEndianBytes());
    }

    [TestMethod]
    public void WriteEndian_DestinationTooSmall_ThrowsArgumentExceptionNamingDestination()
    {
        foreach (int length in new[] { 0, 1, 31 })
        {
            var be = Assert.ThrowsExactly<ArgumentException>(() => int256.Zero.WriteBigEndian(new byte[length]));
            var le = Assert.ThrowsExactly<ArgumentException>(() => int256.Zero.WriteLittleEndian(new byte[length]));
            Assert.AreEqual("destination", be.ParamName);
            Assert.AreEqual("destination", le.ParamName);
        }
    }

    [TestMethod]
    public void WriteEndian_LargerDestination_WritesOnly32BytesAndLeavesTheRest()
    {
        var value = new int256(1, 2, 3, 4);
        foreach (int length in new[] { 32, 33, 64 })
        {
            byte[] be = Enumerable.Repeat((byte)0xAA, length).ToArray();
            byte[] le = Enumerable.Repeat((byte)0xAA, length).ToArray();
            value.WriteBigEndian(be);
            value.WriteLittleEndian(le);

            CollectionAssert.AreEqual(BigEndianOneTwoThreeFour, be[..32]);
            CollectionAssert.AreEqual(BigEndianOneTwoThreeFour.Reverse().ToArray(), le[..32]);
            Assert.IsTrue(be[32..].All(b => b == 0xAA));
            Assert.IsTrue(le[32..].All(b => b == 0xAA));
        }
    }
}
