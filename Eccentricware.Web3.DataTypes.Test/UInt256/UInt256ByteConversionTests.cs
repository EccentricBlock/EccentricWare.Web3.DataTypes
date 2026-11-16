using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class UInt256ByteConversionTests
{
    private static byte[] Pattern(int length) => Enumerable.Range(0, length).Select(i => (byte)(i * 7 + 1)).ToArray();

    // ---- constructing from bytes ----

    [TestMethod]
    public void BigEndianConstructor_EveryLength_LeftPadsAndMatchesBigInteger()
    {
        for (int length = 0; length <= 32; length++)
        {
            byte[] bytes = Pattern(length);
            BigInteger expected = new(bytes, isUnsigned: true, isBigEndian: true);

            Assert.AreEqual(expected, UInt256TestValues.ToBig(new uint256(bytes)), $"length {length}");
        }
    }

    [TestMethod]
    public void FromLittleEndian_EveryLength_RightPadsAndMatchesBigInteger()
    {
        for (int length = 0; length <= 32; length++)
        {
            byte[] bytes = Pattern(length);
            BigInteger expected = new(bytes, isUnsigned: true, isBigEndian: false);

            Assert.AreEqual(expected, UInt256TestValues.ToBig(uint256.FromLittleEndian(bytes)), $"length {length}");
        }
    }

    [TestMethod]
    public void ThirtyTwoByteInputs_MapToLimbsAsDocumented()
    {
        byte[] ascending = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

        uint256 fromBigEndian = new(ascending);
        uint256 fromLittleEndian = uint256.FromLittleEndian(ascending);

        Assert.AreEqual((0x18191A1B1C1D1E1FUL, 0x1011121314151617UL, 0x08090A0B0C0D0E0FUL, 0x0001020304050607UL),
            (fromBigEndian.U0, fromBigEndian.U1, fromBigEndian.U2, fromBigEndian.U3));
        Assert.AreEqual((0x0706050403020100UL, 0x0F0E0D0C0B0A0908UL, 0x1716151413121110UL, 0x1F1E1D1C1B1A1918UL),
            (fromLittleEndian.U0, fromLittleEndian.U1, fromLittleEndian.U2, fromLittleEndian.U3));
    }

    [TestMethod]
    public void MoreThan32Bytes_Throws_WithTheParameterName()
    {
        foreach (int length in new[] { 33, 64 })
        {
            ArgumentException big = Assert.ThrowsExactly<ArgumentException>(() => _ = new uint256(new byte[length]));
            Assert.AreEqual("bigEndianBytes", big.ParamName);

            ArgumentException little = Assert.ThrowsExactly<ArgumentException>(() => _ = uint256.FromLittleEndian(new byte[length]));
            Assert.AreEqual("littleEndianBytes", little.ParamName);
        }
    }

    // ---- writing bytes ----

    [TestMethod]
    public void Write_KnownEncoding_BothEndiannesses()
    {
        uint256 v = new(0x1122334455667788UL, 0x99AABBCCDDEEFF00UL, 0x0123456789ABCDEFUL, 0x0FEDCBA987654321UL);
        byte[] expectedBigEndian = Convert.FromHexString("0FEDCBA987654321" + "0123456789ABCDEF" + "99AABBCCDDEEFF00" + "1122334455667788");

        byte[] bigEndian = new byte[32], littleEndian = new byte[32];
        v.WriteBigEndian(bigEndian);
        v.WriteLittleEndian(littleEndian);

        CollectionAssert.AreEqual(expectedBigEndian, bigEndian);
        CollectionAssert.AreEqual(expectedBigEndian.Reverse().ToArray(), littleEndian);
        Assert.AreEqual(v, new uint256(bigEndian));
        Assert.AreEqual(v, uint256.FromLittleEndian(littleEndian));
    }

    [TestMethod]
    public void Write_One_IsTheLowestByte()
    {
        byte[] bigEndian = new byte[32], littleEndian = new byte[32];
        uint256.One.WriteBigEndian(bigEndian);
        uint256.One.WriteLittleEndian(littleEndian);

        Assert.AreEqual(1, bigEndian[31]);
        Assert.IsTrue(bigEndian.Take(31).All(b => b == 0));
        Assert.AreEqual(1, littleEndian[0]);
        Assert.IsTrue(littleEndian.Skip(1).All(b => b == 0));
    }

    [TestMethod]
    public void Write_ToALargerDestination_LeavesTheTailUntouched()
    {
        foreach (bool bigEndian in new[] { true, false })
        {
            byte[] destination = Enumerable.Repeat((byte)0xAA, 40).ToArray();
            if (bigEndian) uint256.MaxValue.WriteBigEndian(destination); else uint256.MaxValue.WriteLittleEndian(destination);

            Assert.IsTrue(destination.Take(32).All(b => b == 0xFF), $"bigEndian={bigEndian}");
            Assert.IsTrue(destination.Skip(32).All(b => b == 0xAA), $"bigEndian={bigEndian}: bytes past 32 must stay untouched");
        }
    }

    [TestMethod]
    public void Write_ToATooSmallDestination_Throws_WithTheParameterName()
    {
        foreach (int length in new[] { 0, 31 })
        {
            Assert.AreEqual("destination", Assert.ThrowsExactly<ArgumentException>(() => uint256.One.WriteBigEndian(new byte[length])).ParamName);
            Assert.AreEqual("destination", Assert.ThrowsExactly<ArgumentException>(() => uint256.One.WriteLittleEndian(new byte[length])).ParamName);
        }
    }

    [TestMethod]
    public void ToByteArrays_ReturnFreshArrays_WithTheExpectedContent()
    {
        uint256 v = new(1UL, 2UL, 3UL, 4UL);
        byte[] expected = Convert.FromHexString("0000000000000004" + "0000000000000003" + "0000000000000002" + "0000000000000001");

        byte[] first = v.ToBigEndianBytes();
        first[0] = 0xFF;   // mutating the returned array must not affect later calls or the value

        CollectionAssert.AreEqual(expected, v.ToBigEndianBytes());
        CollectionAssert.AreEqual(expected.Reverse().ToArray(), v.ToLittleEndianBytes());
        Assert.AreEqual(new uint256(1UL, 2UL, 3UL, 4UL), v);
        Assert.AreNotSame(v.ToLittleEndianBytes(), v.ToLittleEndianBytes());
    }

    // ---- minimal encoding ----

    [TestMethod]
    public void MinimalBytesAndByteCount_BoundaryValues_MatchBigInteger()
    {
        foreach (BigInteger value in UInt256TestValues.Boundaries.Concat([255, 256, 65535, 65536, (BigInteger.One << 56) - 1, BigInteger.One << 56]))
        {
            uint256 v = UInt256TestValues.FromBig(value);
            int expectedCount = value.IsZero ? 0 : value.GetByteCount(isUnsigned: true);
            byte[] expectedBytes = value.IsZero ? [0] : value.ToByteArray(isUnsigned: true, isBigEndian: true);

            Assert.AreEqual(expectedCount, v.GetByteCount(), value.ToString());
            CollectionAssert.AreEqual(expectedBytes, v.ToMinimalBigEndianBytes(), value.ToString());
        }
    }

    [TestMethod]
    public void MinimalBytesAndByteCount_RandomSparseValues_MatchBigInteger()
    {
        var rng = new Random(12345);
        for (int i = 0; i < 300; i++)
        {
            uint256 v = UInt256TestValues.SparseValue(rng);
            BigInteger value = UInt256TestValues.ToBig(v);

            Assert.AreEqual(value.IsZero ? 0 : value.GetByteCount(isUnsigned: true), v.GetByteCount(), value.ToString());
            CollectionAssert.AreEqual(value.IsZero ? [0] : value.ToByteArray(isUnsigned: true, isBigEndian: true), v.ToMinimalBigEndianBytes(), value.ToString());
        }
    }

    [TestMethod]
    public void MinimalBytes_OfZero_IsAFreshSingleZeroByteArray()
    {
        byte[] first = uint256.Zero.ToMinimalBigEndianBytes();
        first[0] = 9;

        CollectionAssert.AreEqual(new byte[] { 0 }, uint256.Zero.ToMinimalBigEndianBytes());
        Assert.AreEqual(0, uint256.Zero.GetByteCount());
    }
}
