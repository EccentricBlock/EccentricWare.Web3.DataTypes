using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class Hash32UInt256ConversionTests
{
    [TestMethod]
    public void ToUInt256_MatchesBigIntegerOfTheBigEndianBytes()
    {
        byte[] be = Hash32TestData.Bytes0To31();
        var expected = new BigInteger(be, isUnsigned: true, isBigEndian: true);

        Assert.AreEqual(expected, new Hash32(be).ToUInt256().ToBigInteger());
    }

    [TestMethod]
    public void ToUInt256_UsesBigEndianLimbOrder()
    {
        Assert.AreEqual(uint256.One, new Hash32(0, 0, 0, 1).ToUInt256());
        Assert.AreEqual(BigInteger.Pow(2, 192), new Hash32(1, 0, 0, 0).ToUInt256().ToBigInteger());
        Assert.AreEqual(BigInteger.Pow(2, 128), new Hash32(0, 1, 0, 0).ToUInt256().ToBigInteger());
        Assert.AreEqual(BigInteger.Pow(2, 64), new Hash32(0, 0, 1, 0).ToUInt256().ToBigInteger());
    }

    [TestMethod]
    public void ToUInt256_ZeroAndMax()
    {
        Assert.AreEqual(uint256.Zero, Hash32.Zero.ToUInt256());
        Assert.AreEqual(
            uint256.MaxValue,
            new Hash32(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue).ToUInt256());
    }

    [TestMethod]
    public void FromUInt256_MatchesBigIntegerOracle()
    {
        BigInteger value = (BigInteger.One << 200) + 0x0123456789ABCDEFUL;
        byte[] expectedBytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] padded = new byte[32];
        expectedBytes.CopyTo(padded, 32 - expectedBytes.Length);

        Assert.AreEqual(new Hash32(padded), Hash32.FromUInt256((uint256)value));
    }

    [TestMethod]
    public void FromUInt256_ZeroAndMax()
    {
        Assert.AreEqual(Hash32.Zero, Hash32.FromUInt256(uint256.Zero));
        Assert.AreEqual(
            new Hash32(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue),
            Hash32.FromUInt256(uint256.MaxValue));
    }

    [TestMethod]
    public void UInt256_Conversions_RoundTrip_ForRandomValues()
    {
        foreach (Hash32 original in Hash32TestData.RandomHashes(new Random(2024), 200))
            Assert.AreEqual(original, Hash32.FromUInt256(original.ToUInt256()));
    }
}
