using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>EF Core value converter for <see cref="Signature"/>: 66-byte layout (type, 64 payload bytes, v), validation of stored data.</summary>
[TestClass]
public sealed class SignatureValueConverterTests
{
    private static readonly SignatureValueConverter Converter = new();

    /// <summary>Bytes 0x00..0x3F: every byte equals its offset, so any ordering or offset mistake shows up.</summary>
    private static byte[] Ascending64() => Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();

    private static byte[] Evm65(byte v) => [.. Ascending64(), v];

    private static Signature SampleEvm => Signature.FromEvmBytes(Evm65(0x40));

    private static Signature SampleSolana => Signature.FromSolanaBytes(Ascending64());

    private static Signature AllOnesEvm => Signature.FromEvmBytes(ConverterTestData.Repeated(0xFF, 65));

    private static Signature AllOnesSolana => Signature.FromSolanaBytes(ConverterTestData.Repeated(0xFF, 64));

    // ----------------- layout -----------------

    [TestMethod]
    public void Layout_IsTypeByte_ThenPayload_ThenV()
    {
        byte[] evm = Converter.ConvertToProviderTyped(SampleEvm);
        Assert.HasCount(66, evm);
        Assert.AreEqual((byte)SignatureType.Evm, evm[0]);
        CollectionAssert.AreEqual(Ascending64(), evm.AsSpan(1, 64).ToArray());
        Assert.AreEqual(0x40, evm[65]);

        byte[] solana = Converter.ConvertToProviderTyped(SampleSolana);
        Assert.HasCount(66, solana);
        Assert.AreEqual((byte)SignatureType.Solana, solana[0]);
        CollectionAssert.AreEqual(Ascending64(), solana.AsSpan(1, 64).ToArray());
        Assert.AreEqual(0, solana[65]);
    }

    [TestMethod]
    public void Layout_ZeroDefaultAndAllOnes_AreLiteralVectors()
    {
        byte[] zeroSolana = new byte[66];
        zeroSolana[0] = 1;

        CollectionAssert.AreEqual(new byte[66], Converter.ConvertToProviderTyped(Signature.ZeroEvm));
        CollectionAssert.AreEqual(new byte[66], Converter.ConvertToProviderTyped(default));
        CollectionAssert.AreEqual(zeroSolana, Converter.ConvertToProviderTyped(Signature.ZeroSolana));
        CollectionAssert.AreEqual(ConverterTestData.Concat([0], ConverterTestData.Repeated(0xFF, 65)), Converter.ConvertToProviderTyped(AllOnesEvm));
        CollectionAssert.AreEqual(ConverterTestData.Concat([1], ConverterTestData.Repeated(0xFF, 64), [0]), Converter.ConvertToProviderTyped(AllOnesSolana));
    }

    [TestMethod]
    public void ToProvider_ReturnsAFreshArrayEveryCall()
    {
        byte[] first = Converter.ConvertToProviderTyped(SampleEvm);
        first[2] = 0xEE;

        Assert.AreEqual(1, Converter.ConvertToProviderTyped(SampleEvm)[2]);
    }

    // ----------------- round trip -----------------

    [TestMethod]
    public void RoundTrips_BothTypes_IncludingZeroDefaultAndMaxValues()
    {
        var random = new Random(66);
        var values = new List<Signature> { Signature.ZeroEvm, Signature.ZeroSolana, default, AllOnesEvm, AllOnesSolana, SampleEvm, SampleSolana };
        for (int i = 0; i < 200; i++)
            values.Add(i % 2 == 0
                ? Signature.FromEvmBytes(ConverterTestData.RandomBytes(random, 65))
                : Signature.FromSolanaBytes(ConverterTestData.RandomBytes(random, 64)));

        foreach (Signature value in values)
        {
            Signature back = Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(value));
            Assert.AreEqual(value, back);
            Assert.AreEqual(value.Type, back.Type);
        }
    }

    [TestMethod]
    [DataRow((byte)0)]
    [DataRow((byte)1)]
    [DataRow((byte)27)]
    [DataRow((byte)28)]
    [DataRow((byte)255)]
    public void RoundTrip_EvmRecoveryByte_IsPreservedForAnyValue(byte v)
    {
        Signature value = Signature.FromEvmBytes(Evm65(v));

        Signature back = Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(value));

        Assert.AreEqual(value, back);
        Assert.AreEqual(v, back.V);
    }

    [TestMethod]
    public void Untyped_RoundTripsABoxedValue_AndPassesNullThrough()
    {
        object? provider = Converter.ConvertToProvider(SampleSolana);

        Assert.IsInstanceOfType<byte[]>(provider);
        Assert.AreEqual(SampleSolana, Converter.ConvertFromProvider(provider));
        Assert.IsNull(Converter.ConvertToProvider(null));
        Assert.IsNull(Converter.ConvertFromProvider(null));
    }

    // ----------------- ordering -----------------

    [TestMethod]
    public void StoredBytes_SortInTheSameOrderAsTheTypeOrder()
    {
        // The reason for the layout: ORDER BY on the binary column matches Signature.CompareTo.
        var random = new Random(67);
        var values = new List<Signature>();
        for (int i = 0; i < 600; i++)
            values.Add(i % 2 == 0
                ? Signature.FromEvmBytes(ConverterTestData.RandomBytes(random, 65))
                : Signature.FromSolanaBytes(ConverterTestData.RandomBytes(random, 64)));

        int compared = 0;
        for (int i = 0; i + 1 < values.Count; i++)
        {
            int expected = Math.Sign(values[i].CompareTo(values[i + 1]));
            int actual = ConverterTestData.Sign(Converter.ConvertToProviderTyped(values[i]), Converter.ConvertToProviderTyped(values[i + 1]));
            Assert.AreEqual(expected, actual, $"pair {i}");
            compared++;
        }
        Assert.AreEqual(599, compared, "the comparison loop must actually run");

        // Across types: the leading type byte decides, as in CompareTo.
        Assert.IsLessThan(0, AllOnesEvm.CompareTo(Signature.ZeroSolana));
        Assert.IsLessThan(0, ConverterTestData.Sign(Converter.ConvertToProviderTyped(AllOnesEvm), Converter.ConvertToProviderTyped(Signature.ZeroSolana)));

        // Same payload, different v: the trailing byte decides, as in CompareTo.
        Signature lowV = Signature.FromEvmBytes(Evm65(1));
        Signature highV = Signature.FromEvmBytes(Evm65(2));
        Assert.IsLessThan(0, lowV.CompareTo(highV));
        Assert.IsLessThan(0, ConverterTestData.Sign(Converter.ConvertToProviderTyped(lowV), Converter.ConvertToProviderTyped(highV)));
    }

    // ----------------- invalid data -----------------

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(64)]
    [DataRow(65)]
    [DataRow(67)]
    [DataRow(1024 * 1024)]
    public void FromProvider_WrongLength_ThrowsFormatException(int length)
        => Assert.ThrowsExactly<FormatException>(() => Converter.ConvertFromProviderTyped(new byte[length]));

    [TestMethod]
    public void FromProvider_Null_ThrowsArgumentNullException()
        => Assert.AreEqual("bytes", Assert.ThrowsExactly<ArgumentNullException>(() => Converter.ConvertFromProviderTyped(null!)).ParamName);

    [TestMethod]
    [DataRow((byte)2)]
    [DataRow((byte)3)]
    [DataRow((byte)255)]
    public void FromProvider_UnknownTypeByte_ThrowsFormatException(byte type)
    {
        byte[] stored = new byte[66];
        stored[0] = type;
        Assert.ThrowsExactly<FormatException>(() => Converter.ConvertFromProviderTyped(stored));
    }

    [TestMethod]
    public void FromProvider_SolanaRow_WithANonZeroVByte_IsRejectedAsCorrupt()
    {
        // A Solana signature has no v. A non-zero byte 65 means the row was not written by this converter; reading it
        // silently would drop data and let two different rows compare equal after the round trip.
        byte[] stored = Converter.ConvertToProviderTyped(SampleSolana);
        stored[65] = 1;
        Assert.ThrowsExactly<FormatException>(() => Converter.ConvertFromProviderTyped(stored));
    }

    // ----------------- hints -----------------

    [TestMethod]
    public void MappingHints_DefaultToSixtySixBytes_AndCallerSizeWins()
    {
        Assert.AreEqual(66, SignatureValueConverter.StorageSize);
        Assert.AreEqual(66, Converter.MappingHints?.Size);
        Assert.AreEqual(100, new SignatureValueConverter(new ConverterMappingHints(size: 100)).MappingHints?.Size);
        Assert.AreEqual(66, new SignatureValueConverter(new ConverterMappingHints(precision: 7)).MappingHints?.Size);
    }
}
