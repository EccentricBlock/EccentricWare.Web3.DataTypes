using System.Numerics;
using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="Int256BytesValueConverter"/>: big-endian two's complement with the sign bit inverted, so byte order equals signed numeric order.</summary>
[TestClass]
public sealed class Int256BytesValueConverterTests
{
    private static readonly Int256BytesValueConverter Converter = new();

    // ----------------- layout (literal vectors) -----------------

    [TestMethod]
    public void ToProvider_StoresTheSignInverted_AtTheBoundaries()
    {
        byte[] Stored(int256 value) => Converter.ConvertToProviderTyped(value);

        CollectionAssert.AreEqual(new byte[32], Stored(int256.MinValue));
        CollectionAssert.AreEqual(ConverterTestData.Repeated(0xFF, 32), Stored(int256.MaxValue));
        CollectionAssert.AreEqual(Convert.FromHexString("80" + new string('0', 62)), Stored(int256.Zero));
        CollectionAssert.AreEqual(Convert.FromHexString("80" + new string('0', 60) + "01"), Stored(int256.One));
        CollectionAssert.AreEqual(Convert.FromHexString("7F" + new string('F', 62)), Stored(int256.MinusOne));
    }

    [TestMethod]
    public void ToProvider_NegativeValues_MatchPythonTwosComplementWithTheFirstBitFlipped()
    {
        // Vectors from an independent reference (python int.to_bytes two's complement, then first byte ^ 0x80).
        CollectionAssert.AreEqual(Convert.FromHexString("7f" + new string('f', 60) + "fe"), Converter.ConvertToProviderTyped(new int256(-2L)));
        CollectionAssert.AreEqual(Convert.FromHexString("7f" + new string('f', 46) + "8000000000000000"), Converter.ConvertToProviderTyped(new int256(long.MinValue)));
    }

    [TestMethod]
    public void ToProvider_ReturnsAFreshArrayEveryCall()
    {
        byte[] first = Converter.ConvertToProviderTyped(int256.One);
        first[0] = 0;

        Assert.AreEqual(0x80, Converter.ConvertToProviderTyped(int256.One)[0]);
    }

    // ----------------- round trip -----------------

    [TestMethod]
    public void RoundTrip_PreservesBoundaryAndRandomValues()
    {
        var random = new Random(256);
        var values = new List<int256> { int256.MinValue, int256.MaxValue, int256.Zero, default, int256.One, int256.MinusOne, new(long.MinValue), new(long.MaxValue), new(-123456789L) };
        for (int i = 0; i < 2000; i++)
            values.Add(new int256(ConverterTestData.RandomBytes(random, 32)));
        for (int i = 0; i < 500; i++)
            values.Add(new int256((long)random.Next(-1000, 1000)));

        foreach (int256 value in values)
            Assert.AreEqual(value, Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(value)));
    }

    [TestMethod]
    public void FromProvider_DoesNotChangeTheArrayItReads()
    {
        byte[] stored = Converter.ConvertToProviderTyped(new int256(-5L));
        byte[] copy = (byte[])stored.Clone();

        Converter.ConvertFromProviderTyped(stored);

        CollectionAssert.AreEqual(copy, stored);
    }

    [TestMethod]
    public void Untyped_RoundTripsABoxedValue_AndPassesNullThrough()
    {
        object? provider = Converter.ConvertToProvider(int256.MinValue);

        Assert.IsInstanceOfType<byte[]>(provider);
        Assert.AreEqual(int256.MinValue, Converter.ConvertFromProvider(provider));
        Assert.IsNull(Converter.ConvertToProvider(null));
        Assert.IsNull(Converter.ConvertFromProvider(null));
    }

    // ----------------- ordering -----------------

    [TestMethod]
    public void StoredBytes_SortLikeTheSignedValue_ForEveryBoundaryPairAndRandomPairs()
    {
        // Oracle: BigInteger over the plain two's-complement bytes (a different path from the sign-flipped storage).
        BigInteger Numeric(int256 v) => new(v.ToBigEndianBytes(), isUnsigned: false, isBigEndian: true);
        var random = new Random(5);
        var values = new List<int256> { int256.MinValue, new(long.MinValue), new(-2L), int256.MinusOne, int256.Zero, int256.One, new(long.MaxValue), int256.MaxValue };
        for (int i = 0; i < 300; i++)
            values.Add(random.Next(3) == 0 ? new int256((long)random.Next(-1000, 1000)) : new int256(ConverterTestData.RandomBytes(random, 32)));

        foreach (int256 a in values)
            foreach (int256 b in values.Take(50))
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
        Assert.AreEqual(32, Int256BytesValueConverter.StorageSize);
        Assert.AreEqual(32, new Int256BytesValueConverter().MappingHints?.Size);
        Assert.AreEqual(64, new Int256BytesValueConverter(new ConverterMappingHints(size: 64)).MappingHints?.Size);
        Assert.AreEqual(32, new Int256BytesValueConverter(new ConverterMappingHints(precision: 7)).MappingHints?.Size);
    }
}
