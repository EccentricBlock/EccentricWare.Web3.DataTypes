using System.Numerics;
using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="UInt256BytesValueConverter"/>: 32 big-endian bytes, so byte order equals numeric order.</summary>
[TestClass]
public sealed class UInt256BytesValueConverterTests
{
    private static readonly UInt256BytesValueConverter Converter = new();

    private static byte[] FromBig(BigInteger value)
    {
        byte[] minimal = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        return [.. new byte[32 - minimal.Length], .. minimal];
    }

    private static byte[] Padded(string hexSuffix) => Convert.FromHexString(hexSuffix.PadLeft(64, '0'));

    // ----------------- layout (literal vectors) -----------------

    [TestMethod]
    public void ToProvider_WritesLiteralBigEndianLayouts()
    {
        CollectionAssert.AreEqual(new byte[32], Converter.ConvertToProviderTyped(uint256.Zero));
        CollectionAssert.AreEqual(new byte[32], Converter.ConvertToProviderTyped(default));
        CollectionAssert.AreEqual(Padded("01"), Converter.ConvertToProviderTyped(uint256.One));
        CollectionAssert.AreEqual(Padded("0102030405060708"), Converter.ConvertToProviderTyped(new uint256(0x0102030405060708UL)));
        CollectionAssert.AreEqual(ConverterTestData.Repeated(0xFF, 32), Converter.ConvertToProviderTyped(uint256.MaxValue));

        // Limbs are given least significant first: u3 = 4 is the first eight bytes, u0 = 1 the last eight.
        CollectionAssert.AreEqual(Convert.FromHexString("0000000000000004" + "0000000000000003" + "0000000000000002" + "0000000000000001"),
            Converter.ConvertToProviderTyped(new uint256(1, 2, 3, 4)));
    }

    [TestMethod]
    public void ToProvider_ReturnsAFreshArrayEveryCall()
    {
        byte[] first = Converter.ConvertToProviderTyped(uint256.One);
        first[31] = 0xEE;

        CollectionAssert.AreEqual(Padded("01"), Converter.ConvertToProviderTyped(uint256.One));
    }

    // ----------------- round trip -----------------

    [TestMethod]
    public void RoundTrip_PreservesBoundaryAndRandomValues()
    {
        var random = new Random(256);
        var values = new List<uint256> { uint256.Zero, default, uint256.One, uint256.MaxValue, new(0x0102030405060708UL), new(1, 2, 3, 4), new(0, 0, 0, 0x8000000000000000UL) };
        for (int i = 0; i < 500; i++)
            values.Add(new uint256(ConverterTestData.RandomBytes(random, 32)));

        foreach (uint256 value in values)
            Assert.AreEqual(value, Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(value)));
    }

    [TestMethod]
    public void FromProvider_ReadsLiteralBytes_AndLeavesTheArrayUntouched()
    {
        byte[] stored = Padded("01");

        Assert.AreEqual(uint256.One, Converter.ConvertFromProviderTyped(stored));
        CollectionAssert.AreEqual(Padded("01"), stored);
    }

    [TestMethod]
    public void Untyped_RoundTripsABoxedValue_AndPassesNullThrough()
    {
        object? provider = Converter.ConvertToProvider(uint256.MaxValue);

        Assert.IsInstanceOfType<byte[]>(provider);
        Assert.AreEqual(uint256.MaxValue, Converter.ConvertFromProvider(provider));
        Assert.IsNull(Converter.ConvertToProvider(null));
        Assert.IsNull(Converter.ConvertFromProvider(null));
    }

    // ----------------- ordering -----------------

    [TestMethod]
    public void StoredBytes_SortLikeTheNumericValue_AtLimbBoundariesAndForRandomPairs()
    {
        BigInteger Two(int power) => BigInteger.One << power;
        var values = new List<uint256> { uint256.Zero, uint256.One, uint256.MaxValue };
        foreach (int bit in new[] { 63, 64, 127, 128, 191, 192, 255 })
        {
            // 2^bit and the value just below it, built from bytes so the oracle is not the converter or the operators.
            values.Add(new uint256(FromBig(Two(bit))));
            values.Add(new uint256(FromBig(Two(bit) - 1)));
        }

        var random = new Random(7);
        for (int i = 0; i < 200; i++)
            values.Add(new uint256(ConverterTestData.RandomBytes(random, 32)));

        BigInteger Numeric(uint256 v) => new(Converter.ConvertToProviderTyped(v), isUnsigned: true, isBigEndian: true);

        foreach (uint256 a in values)
            foreach (uint256 b in values.Take(40))
                Assert.AreEqual(Numeric(a).CompareTo(Numeric(b)),
                    ConverterTestData.Sign(Converter.ConvertToProviderTyped(a), Converter.ConvertToProviderTyped(b)), $"{a} vs {b}");
    }

    // ----------------- invalid data -----------------

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(31)]
    [DataRow(33)]
    [DataRow(1024 * 1024)]
    public void FromProvider_WrongLength_ThrowsFormatException(int length)
        => Assert.ThrowsExactly<FormatException>(() => Converter.ConvertFromProviderTyped(new byte[length]));

    [TestMethod]
    public void FromProvider_Null_ThrowsArgumentNullException()
        => Assert.AreEqual("bytes", Assert.ThrowsExactly<ArgumentNullException>(() => Converter.ConvertFromProviderTyped(null!)).ParamName);

    // ----------------- hints -----------------

    [TestMethod]
    public void MappingHints_DefaultToThirtyTwoBytes_AndCallerSizeWins()
    {
        Assert.AreEqual(32, UInt256BytesValueConverter.StorageSize);
        Assert.AreEqual(32, new UInt256BytesValueConverter().MappingHints?.Size);
        Assert.AreEqual(64, new UInt256BytesValueConverter(new ConverterMappingHints(size: 64)).MappingHints?.Size);
        Assert.AreEqual(32, new UInt256BytesValueConverter(new ConverterMappingHints(precision: 7)).MappingHints?.Size);
    }
}
