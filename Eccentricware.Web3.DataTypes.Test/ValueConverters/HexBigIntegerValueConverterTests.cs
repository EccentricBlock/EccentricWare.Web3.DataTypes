using EccentricWare.Web3.DataTypes.ValueConverters;
using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="HexBigIntegerToBytesConverter"/> (EF Core value converter), moved unchanged from the former combined test class.</summary>
[TestClass]
public sealed class HexBigIntegerValueConverterTests
{
    [TestMethod]
    public void EfConverter_RoundTrips_SignedValuesAcrossByteBoundaries()
    {
        HexBigIntegerToBytesConverter converter = HexBigIntegerToBytesConverter.Default;
        BigInteger[] edges =
        [
            0, 1, -1, 127, 128, 255, 256, -127, -128, -129, -255, -256, -257,
            short.MaxValue, short.MinValue, int.MaxValue, int.MinValue, long.MaxValue, long.MinValue, ulong.MaxValue,
            BigInteger.Pow(2, 255), -BigInteger.Pow(2, 255), BigInteger.Pow(2, 256) - 1, BigInteger.Pow(2, 2000), -BigInteger.Pow(2, 2000) - 1,
        ];

        foreach (BigInteger edge in edges)
        {
            var value = new HexBigInteger(edge);
            byte[] provider = converter.ConvertToProviderTyped(value);
            Assert.AreEqual(edge, converter.ConvertFromProviderTyped(provider).Value, edge.ToString());
        }
    }

    [TestMethod]
    public void EfConverter_Zero_IsEmptyArray_AndNullOrEmptyReadAsZero()
    {
        HexBigIntegerToBytesConverter converter = HexBigIntegerToBytesConverter.Default;
        Assert.AreEqual(0, converter.ConvertToProviderTyped(HexBigInteger.Zero).Length);
        Assert.AreEqual(HexBigInteger.Zero, converter.ConvertFromProviderTyped([]));
        Assert.AreEqual(HexBigInteger.Zero, converter.ConvertFromProviderTyped(null!));
    }

    [TestMethod]
    public void EfConverter_Encoding_IsMinimalBigEndianTwosComplement()
    {
        HexBigIntegerToBytesConverter converter = HexBigIntegerToBytesConverter.Default;
        CollectionAssert.AreEqual(new byte[] { 0x7F }, converter.ConvertToProviderTyped(new HexBigInteger(127)));
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x80 }, converter.ConvertToProviderTyped(new HexBigInteger(128)));
        CollectionAssert.AreEqual(new byte[] { 0xFF }, converter.ConvertToProviderTyped(HexBigInteger.MinusOne));
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0x7F }, converter.ConvertToProviderTyped(new HexBigInteger(-129)));
    }
}
