using System.Numerics;
using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="Hash32ValueConverter"/>: 32 big-endian bytes, size hint, strict reads, byte order equal to value order.</summary>
[TestClass]
public sealed class Hash32ValueConverterTests
{
    private static readonly Hash32ValueConverter Converter = new();

    private static byte[] Ascending() => Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private static byte[] Repeated(byte value) => Enumerable.Repeat(value, 32).ToArray();

    // ----------------- layout -----------------

    [TestMethod]
    public void ToProvider_WritesTheBytesBigEndian_FirstByteIsTheMostSignificant()
    {
        // Limbs given most significant first; the expected array is the literal byte string 01 02 ... 20.
        var hash = new Hash32(0x0102030405060708UL, 0x090A0B0C0D0E0F10UL, 0x1112131415161718UL, 0x191A1B1C1D1E1F20UL);

        CollectionAssert.AreEqual(Ascending(), Converter.ConvertToProviderTyped(hash));
    }

    [TestMethod]
    public void ToProvider_FromBytesConstructor_StoresTheSameBytes()
    {
        CollectionAssert.AreEqual(Ascending(), Converter.ConvertToProviderTyped(new Hash32(Ascending())));
    }

    [TestMethod]
    public void ToProvider_Boundaries_ZeroAndAllOnes()
    {
        CollectionAssert.AreEqual(new byte[32], Converter.ConvertToProviderTyped(Hash32.Zero));
        CollectionAssert.AreEqual(new byte[32], Converter.ConvertToProviderTyped(default));
        CollectionAssert.AreEqual(Repeated(0xFF), Converter.ConvertToProviderTyped(new Hash32(Repeated(0xFF))));
    }

    [TestMethod]
    public void ToProvider_LeastSignificantBit_IsTheLastByte()
    {
        byte[] expected = new byte[32];
        expected[31] = 1;

        CollectionAssert.AreEqual(expected, Converter.ConvertToProviderTyped(new Hash32(0, 0, 0, 1)));
    }

    [TestMethod]
    public void ToProvider_ReturnsAFreshArrayEveryCall()
    {
        Hash32 hash = new(Ascending());

        byte[] first = Converter.ConvertToProviderTyped(hash);
        first[0] = 0xEE;
        byte[] second = Converter.ConvertToProviderTyped(hash);

        Assert.AreNotSame(first, second);
        Assert.AreEqual(1, second[0]);
    }

    // ----------------- round trip -----------------

    [TestMethod]
    public void RoundTrip_PreservesBoundaryValues_AndEachLimbOnItsOwn()
    {
        var values = new List<Hash32> { default, Hash32.Zero, new(Repeated(0xFF)), new(0, 0, 0, 1), new(1, 0, 0, 0), new(0, 1, 0, 0), new(0, 0, 1, 0) };
        values.Add(new Hash32(0x8000000000000000UL, 0, 0, 0));
        values.Add(new Hash32(0, 0, 0, ulong.MaxValue));

        foreach (Hash32 value in values)
            Assert.AreEqual(value, Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(value)));
    }

    [TestMethod]
    public void FromProvider_ReadsTheLiteralLayout()
    {
        Assert.AreEqual(new Hash32(0x0102030405060708UL, 0x090A0B0C0D0E0F10UL, 0x1112131415161718UL, 0x191A1B1C1D1E1F20UL),
            Converter.ConvertFromProviderTyped(Ascending()));
    }

    [TestMethod]
    public void FromProvider_DoesNotChangeTheArrayItReads()
    {
        byte[] stored = Ascending();

        Converter.ConvertFromProviderTyped(stored);

        CollectionAssert.AreEqual(Ascending(), stored);
    }

    [TestMethod]
    public void Untyped_RoundTripsABoxedValue_AndPassesNullThrough()
    {
        var hash = new Hash32(Ascending());

        object? provider = Converter.ConvertToProvider(hash);

        Assert.IsInstanceOfType<byte[]>(provider);
        Assert.AreEqual(hash, Converter.ConvertFromProvider(provider));
        Assert.IsNull(Converter.ConvertToProvider(null));
        Assert.IsNull(Converter.ConvertFromProvider(null));
        Assert.ThrowsExactly<InvalidCastException>(() => Converter.ConvertToProvider("not a hash"));
    }

    // ----------------- ordering -----------------

    [TestMethod]
    public void StoredBytes_SortLikeTheNumericValue()
    {
        // Oracle: BigInteger over the same bytes, not Hash32.CompareTo.
        var random = new Random(32);
        var hashes = new List<Hash32> { Hash32.Zero, new(Repeated(0xFF)), new(0, 0, 0, 1), new(0, 0, 1, 0), new(0, 1, 0, 0), new(1, 0, 0, 0), new(0x8000000000000000UL, 0, 0, 0), new(0x7FFFFFFFFFFFFFFFUL, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue) };
        for (int i = 0; i < 300; i++)
        {
            byte[] bytes = new byte[32];
            random.NextBytes(bytes);
            hashes.Add(new Hash32(bytes));
        }

        BigInteger Numeric(Hash32 h) => new(h.ToBigEndianBytes(), isUnsigned: true, isBigEndian: true);

        foreach (Hash32 a in hashes)
            foreach (Hash32 b in hashes.Take(60))
                Assert.AreEqual(Numeric(a).CompareTo(Numeric(b)),
                    Math.Sign(Converter.ConvertToProviderTyped(a).AsSpan().SequenceCompareTo(Converter.ConvertToProviderTyped(b))),
                    $"{a} vs {b}");
    }

    // ----------------- invalid data -----------------

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(31)]
    [DataRow(33)]
    [DataRow(64)]
    [DataRow(1024 * 1024)]
    public void FromProvider_WrongLength_ThrowsFormatException(int length)
        => Assert.ThrowsExactly<FormatException>(() => Converter.ConvertFromProviderTyped(new byte[length]));

    [TestMethod]
    public void FromProvider_Null_ThrowsArgumentNullException()
    {
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(() => Converter.ConvertFromProviderTyped(null!));

        Assert.AreEqual("bytes", ex.ParamName);
    }

    // ----------------- hints -----------------

    [TestMethod]
    public void MappingHints_DefaultToThirtyTwoBytes()
    {
        Assert.AreEqual(32, Hash32ValueConverter.StorageSize);
        Assert.AreEqual(32, new Hash32ValueConverter().MappingHints?.Size);
        Assert.AreEqual(32, new Hash32ValueConverter(null).MappingHints?.Size);
    }

    [TestMethod]
    public void MappingHints_CallerSizeWins_AndOtherHintsKeepTheDefaultSize()
    {
        Assert.AreEqual(64, new Hash32ValueConverter(new ConverterMappingHints(size: 64)).MappingHints?.Size);

        var precisionOnly = new Hash32ValueConverter(new ConverterMappingHints(precision: 7));
        Assert.AreEqual(32, precisionOnly.MappingHints?.Size);
        Assert.AreEqual(7, precisionOnly.MappingHints?.Precision);
    }

    [TestMethod]
    public void Converter_DeclaresTheClrAndProviderTypes()
    {
        Assert.AreEqual(typeof(Hash32), Converter.ModelClrType);
        Assert.AreEqual(typeof(byte[]), Converter.ProviderClrType);
    }
}
