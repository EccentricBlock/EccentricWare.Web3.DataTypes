using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="InstructionDiscriminatorBinaryValueConverter"/>: eight big-endian bytes (the discriminator itself), strict on read.</summary>
[TestClass]
public sealed class InstructionDiscriminatorBinaryValueConverterTests
{
    private static readonly InstructionDiscriminatorBinaryValueConverter Converter = new();

    private static readonly byte[] AnchorBytes = [0xAF, 0xAF, 0x6D, 0x1F, 0x0D, 0x98, 0x9B, 0xED];

    [TestMethod]
    public void ToProvider_WritesTheBytesBigEndian()
    {
        var value = new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL);

        CollectionAssert.AreEqual(AnchorBytes, Converter.ConvertToProviderTyped(value));
        CollectionAssert.AreEqual(new byte[8], Converter.ConvertToProviderTyped(default));
        CollectionAssert.AreEqual(ConverterTestData.Repeated(0xFF, 8), Converter.ConvertToProviderTyped(new InstructionDiscriminator(ulong.MaxValue)));
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }, Converter.ConvertToProviderTyped(new InstructionDiscriminator(1UL)));
    }

    [TestMethod]
    public void ToProvider_ReturnsAFreshArrayEveryCall()
    {
        byte[] first = Converter.ConvertToProviderTyped(new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL));
        first[0] = 0;

        Assert.AreEqual(0xAF, Converter.ConvertToProviderTyped(new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL))[0]);
    }

    [TestMethod]
    public void RoundTrip_PreservesBoundaryAndRandomValues()
    {
        var random = new Random(8);
        var values = new List<ulong> { 0, 1, 0x7FFFFFFFFFFFFFFF, 0x8000000000000000, 0xAFAF6D1F0D989BED, ulong.MaxValue };
        for (int i = 0; i < 1000; i++)
            values.Add(BitConverter.ToUInt64(ConverterTestData.RandomBytes(random, 8)));

        foreach (ulong v in values)
            Assert.AreEqual(new InstructionDiscriminator(v), Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(new InstructionDiscriminator(v))));

        Assert.AreEqual(new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL), Converter.ConvertFromProviderTyped(AnchorBytes));
    }

    [TestMethod]
    public void StoredBytes_SortLikeTheUnsignedValue_ForEveryBoundaryPair()
    {
        ulong[] boundaries = [0, 1, 0x7FFFFFFFFFFFFFFF, 0x8000000000000000, 0x8000000000000001, 0xAFAF6D1F0D989BED, ulong.MaxValue - 1, ulong.MaxValue];
        foreach (ulong a in boundaries)
            foreach (ulong b in boundaries)
                Assert.AreEqual(Math.Sign(a.CompareTo(b)),
                    ConverterTestData.Sign(Converter.ConvertToProviderTyped(new InstructionDiscriminator(a)), Converter.ConvertToProviderTyped(new InstructionDiscriminator(b))),
                    $"{a:x16} vs {b:x16}");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(7)]
    [DataRow(9)]
    [DataRow(64)]
    [DataRow(1024 * 1024)]
    public void FromProvider_WrongLength_ThrowsFormatException(int length)
        => Assert.ThrowsExactly<FormatException>(() => Converter.ConvertFromProviderTyped(new byte[length]));

    [TestMethod]
    public void FromProvider_Null_ThrowsArgumentNullException()
        => Assert.AreEqual("bytes", Assert.ThrowsExactly<ArgumentNullException>(() => Converter.ConvertFromProviderTyped(null!)).ParamName);

    [TestMethod]
    public void Untyped_RoundTripsABoxedValue_AndPassesNullThrough()
    {
        var value = new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL);
        object? provider = Converter.ConvertToProvider(value);

        Assert.IsInstanceOfType<byte[]>(provider);
        CollectionAssert.AreEqual(AnchorBytes, (byte[])provider!);
        Assert.AreEqual(value, Converter.ConvertFromProvider(provider));
        Assert.IsNull(Converter.ConvertToProvider(null));
        Assert.IsNull(Converter.ConvertFromProvider(null));
    }

    [TestMethod]
    public void DefaultHints_DeclareEightBytes_AndAreTheHintsOfTheDefaultConverter()
    {
        Assert.AreEqual(8, InstructionDiscriminatorBinaryValueConverter.StorageSize);
        Assert.AreEqual(8, InstructionDiscriminatorBinaryValueConverter.DefaultHints.Size);
        Assert.AreSame(InstructionDiscriminatorBinaryValueConverter.DefaultHints, Converter.MappingHints);
    }

    [TestMethod]
    public void MappingHints_CallerSizeWins_WithoutChangingTheSharedDefaults()
    {
        Assert.AreEqual(16, new InstructionDiscriminatorBinaryValueConverter(new ConverterMappingHints(size: 16)).MappingHints?.Size);
        Assert.AreEqual(8, new InstructionDiscriminatorBinaryValueConverter(new ConverterMappingHints(precision: 7)).MappingHints?.Size);
        Assert.AreEqual(8, InstructionDiscriminatorBinaryValueConverter.DefaultHints.Size);
        Assert.IsNull(InstructionDiscriminatorBinaryValueConverter.DefaultHints.Precision);
    }
}
