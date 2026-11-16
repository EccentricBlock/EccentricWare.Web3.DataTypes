using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="FunctionSelectorBinaryValueConverter"/>: four big-endian bytes (the selector itself), strict on read.</summary>
[TestClass]
public sealed class FunctionSelectorBinaryValueConverterTests
{
    private static readonly FunctionSelectorBinaryValueConverter Converter = new();

    [TestMethod]
    public void ToProvider_WritesTheSelectorBytes_BigEndian()
    {
        CollectionAssert.AreEqual(new byte[] { 0xa9, 0x05, 0x9c, 0xbb }, Converter.ConvertToProviderTyped(FunctionSelector.Erc20.Transfer));
        CollectionAssert.AreEqual(new byte[4], Converter.ConvertToProviderTyped(default));
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, Converter.ConvertToProviderTyped(new FunctionSelector(0xFFFFFFFFu)));
        CollectionAssert.AreEqual(new byte[] { 0x80, 0, 0, 0 }, Converter.ConvertToProviderTyped(new FunctionSelector(0x80000000u)));
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 1 }, Converter.ConvertToProviderTyped(new FunctionSelector(1u)));
    }

    [TestMethod]
    public void ToProvider_ReturnsAFreshArrayEveryCall()
    {
        byte[] first = Converter.ConvertToProviderTyped(FunctionSelector.Erc20.Transfer);
        first[0] = 0;

        Assert.AreEqual(0xa9, Converter.ConvertToProviderTyped(FunctionSelector.Erc20.Transfer)[0]);
    }

    [TestMethod]
    public void RoundTrip_PreservesBoundaryAndRandomValues()
    {
        var random = new Random(41);
        var values = new List<uint> { 0, 1, 0x7FFFFFFF, 0x80000000, 0xa9059cbb, uint.MaxValue };
        for (int i = 0; i < 1000; i++)
            values.Add(BitConverter.ToUInt32(ConverterTestData.RandomBytes(random, 4)));

        foreach (uint v in values)
            Assert.AreEqual(new FunctionSelector(v), Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(new FunctionSelector(v))));
    }

    [TestMethod]
    public void StoredBytes_SortLikeTheUnsignedSelector_ForEveryBoundaryPair()
    {
        // The reason for binary storage over the signed integer: 0x7FFFFFFF < 0x80000000 must hold in the column too.
        uint[] boundaries = [0, 1, 0x7FFFFFFF, 0x80000000, 0x80000001, 0xa9059cbb, 0xFFFFFFFE, 0xFFFFFFFF];
        foreach (uint a in boundaries)
            foreach (uint b in boundaries)
                Assert.AreEqual(Math.Sign(a.CompareTo(b)),
                    ConverterTestData.Sign(Converter.ConvertToProviderTyped(new FunctionSelector(a)), Converter.ConvertToProviderTyped(new FunctionSelector(b))),
                    $"{a:x8} vs {b:x8}");
    }

    [TestMethod]
    public void FromProvider_ReadsAnExactlySizedValue_AndNoOther()
    {
        Assert.AreEqual(FunctionSelector.Erc20.Transfer, Converter.ConvertFromProviderTyped([0xa9, 0x05, 0x9c, 0xbb]));

        // The selector's span constructor reads the first four of a longer value; the converter must not.
        Assert.ThrowsExactly<FormatException>(() => Converter.ConvertFromProviderTyped([0xa9, 0x05, 0x9c, 0xbb, 0x00]));
        Assert.ThrowsExactly<FormatException>(() => Converter.ConvertFromProviderTyped(new byte[36]));
        Assert.ThrowsExactly<FormatException>(() => Converter.ConvertFromProviderTyped([0xa9, 0x05, 0x9c]));
        Assert.ThrowsExactly<FormatException>(() => Converter.ConvertFromProviderTyped([]));
        Assert.ThrowsExactly<FormatException>(() => Converter.ConvertFromProviderTyped(new byte[1024 * 1024]));
    }

    [TestMethod]
    public void FromProvider_Null_ThrowsArgumentNullException()
        => Assert.AreEqual("bytes", Assert.ThrowsExactly<ArgumentNullException>(() => Converter.ConvertFromProviderTyped(null!)).ParamName);

    [TestMethod]
    public void Untyped_RoundTripsABoxedValue_AndPassesNullThrough()
    {
        object? provider = Converter.ConvertToProvider(FunctionSelector.Erc20.Transfer);

        Assert.IsInstanceOfType<byte[]>(provider);
        Assert.AreEqual(FunctionSelector.Erc20.Transfer, Converter.ConvertFromProvider(provider));
        Assert.IsNull(Converter.ConvertToProvider(null));
        Assert.IsNull(Converter.ConvertFromProvider(null));
    }

    // ----------------- hints -----------------

    [TestMethod]
    public void DefaultHints_DeclareFourBytes_AndAreTheHintsOfTheDefaultConverter()
    {
        Assert.AreEqual(4, FunctionSelectorBinaryValueConverter.StorageSize);
        Assert.AreEqual(4, FunctionSelectorBinaryValueConverter.DefaultHints.Size);
        Assert.AreSame(FunctionSelectorBinaryValueConverter.DefaultHints, Converter.MappingHints);
    }

    [TestMethod]
    public void MappingHints_CallerSizeWins_WithoutChangingTheSharedDefaults()
    {
        Assert.AreEqual(9, new FunctionSelectorBinaryValueConverter(new ConverterMappingHints(size: 9)).MappingHints?.Size);
        Assert.AreEqual(4, new FunctionSelectorBinaryValueConverter(new ConverterMappingHints(precision: 7)).MappingHints?.Size);
        Assert.AreEqual(4, FunctionSelectorBinaryValueConverter.DefaultHints.Size);
        Assert.IsNull(FunctionSelectorBinaryValueConverter.DefaultHints.Precision);
    }
}
