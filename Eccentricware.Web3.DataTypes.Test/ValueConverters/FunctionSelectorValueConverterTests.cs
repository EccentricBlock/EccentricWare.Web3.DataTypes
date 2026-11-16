using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="FunctionSelectorValueConverter"/>: a 32-bit integer with the sign bit inverted, so integer order equals unsigned selector order.</summary>
[TestClass]
public sealed class FunctionSelectorValueConverterTests
{
    private static readonly FunctionSelectorValueConverter Converter = new();

    private static readonly uint[] Boundaries = [0, 1, 0x7FFFFFFE, 0x7FFFFFFF, 0x80000000, 0x80000001, 0xa9059cbb, 0xFFFFFFFE, 0xFFFFFFFF];

    [TestMethod]
    public void ToProvider_StoresTheSignInvertedValue_AtTheBoundaries()
    {
        Assert.AreEqual(int.MinValue, Converter.ConvertToProviderTyped(new FunctionSelector(0x00000000u)));
        Assert.AreEqual(-1, Converter.ConvertToProviderTyped(new FunctionSelector(0x7FFFFFFFu)));
        Assert.AreEqual(0, Converter.ConvertToProviderTyped(new FunctionSelector(0x80000000u)));
        Assert.AreEqual(int.MaxValue, Converter.ConvertToProviderTyped(new FunctionSelector(0xFFFFFFFFu)));
        Assert.AreEqual(int.MinValue, Converter.ConvertToProviderTyped(default));
    }

    [TestMethod]
    public void ToProvider_LiteralVector_Transfer()
    {
        // 0xa9059cbb - 2^31 = 0x29059cbb = 688233659 (independent arithmetic).
        Assert.AreEqual(688233659, Converter.ConvertToProviderTyped(FunctionSelector.Erc20.Transfer));
        Assert.AreEqual(FunctionSelector.Erc20.Transfer, Converter.ConvertFromProviderTyped(688233659));
    }

    [TestMethod]
    public void RoundTrip_AndStoredValuesSortLikeTheSelector_ForEveryBoundaryPairAndRandomPairs()
    {
        var random = new Random(4);
        var values = new List<uint>(Boundaries);
        for (int i = 0; i < 3000; i++)
            values.Add(BitConverter.ToUInt32(ConverterTestData.RandomBytes(random, 4)));

        foreach (uint value in values)
            Assert.AreEqual(new FunctionSelector(value), Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(new FunctionSelector(value))));

        foreach (uint a in values)
            foreach (uint b in Boundaries.Concat(values.Skip(Boundaries.Length).Take(30)))
                Assert.AreEqual(Math.Sign(a.CompareTo(b)),
                    Math.Sign(Converter.ConvertToProviderTyped(new FunctionSelector(a)).CompareTo(Converter.ConvertToProviderTyped(new FunctionSelector(b)))),
                    $"{a:x8} vs {b:x8}");
    }

    [TestMethod]
    public void FromProvider_EveryIntegerIsAValidSelector_AtTheExtremes()
    {
        Assert.AreEqual(FunctionSelector.Zero, Converter.ConvertFromProviderTyped(int.MinValue));
        Assert.AreEqual(new FunctionSelector(0xFFFFFFFFu), Converter.ConvertFromProviderTyped(int.MaxValue));
        Assert.AreEqual(new FunctionSelector(0x80000000u), Converter.ConvertFromProviderTyped(0));
    }

    [TestMethod]
    public void Untyped_RoundTripsABoxedValue()
    {
        object? provider = Converter.ConvertToProvider(FunctionSelector.Erc20.Transfer);

        Assert.IsInstanceOfType<int>(provider);
        Assert.AreEqual(FunctionSelector.Erc20.Transfer, Converter.ConvertFromProvider(provider));
        Assert.IsNull(Converter.ConvertToProvider(null));
        Assert.IsNull(Converter.ConvertFromProvider(null));
    }

    [TestMethod]
    public void MappingHints_AreOnlyWhatTheCallerSupplies()
    {
        Assert.IsNull(Converter.MappingHints);
        Assert.IsNull(new FunctionSelectorValueConverter(null).MappingHints);
        Assert.AreEqual(5, new FunctionSelectorValueConverter(new ConverterMappingHints(size: 5)).MappingHints?.Size);
    }
}
