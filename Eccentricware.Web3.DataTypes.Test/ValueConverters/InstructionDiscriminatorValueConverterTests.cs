using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="InstructionDiscriminatorValueConverter"/>: a 64-bit integer with the sign bit inverted, so integer order equals unsigned order.</summary>
[TestClass]
public sealed class InstructionDiscriminatorValueConverterTests
{
    private static readonly InstructionDiscriminatorValueConverter Converter = new();

    private static readonly ulong[] Boundaries = [0, 1, 0x7FFFFFFFFFFFFFFE, 0x7FFFFFFFFFFFFFFF, 0x8000000000000000, 0x8000000000000001, 0xAFAF6D1F0D989BED, ulong.MaxValue - 1, ulong.MaxValue];

    [TestMethod]
    public void ToProvider_StoresTheSignInvertedValue_AtTheBoundaries()
    {
        Assert.AreEqual(long.MinValue, Converter.ConvertToProviderTyped(new InstructionDiscriminator(0UL)));
        Assert.AreEqual(-1L, Converter.ConvertToProviderTyped(new InstructionDiscriminator(0x7FFFFFFFFFFFFFFFUL)));
        Assert.AreEqual(0L, Converter.ConvertToProviderTyped(new InstructionDiscriminator(0x8000000000000000UL)));
        Assert.AreEqual(long.MaxValue, Converter.ConvertToProviderTyped(new InstructionDiscriminator(ulong.MaxValue)));
        Assert.AreEqual(long.MinValue, Converter.ConvertToProviderTyped(default));
    }

    [TestMethod]
    public void ToProvider_LiteralVector_AnchorInitializeDiscriminator()
    {
        // 0xAFAF6D1F0D989BED - 2^63 = 0x2FAF6D1F0D989BED = 3436085020846496749 (independent arithmetic).
        var value = new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL);

        Assert.AreEqual(3436085020846496749L, Converter.ConvertToProviderTyped(value));
        Assert.AreEqual(value, Converter.ConvertFromProviderTyped(3436085020846496749L));
    }

    [TestMethod]
    public void RoundTrip_AndStoredValuesSortLikeTheDiscriminator_ForEveryBoundaryPairAndRandomPairs()
    {
        var random = new Random(5);
        var values = new List<ulong>(Boundaries);
        for (int i = 0; i < 3000; i++)
            values.Add(BitConverter.ToUInt64(ConverterTestData.RandomBytes(random, 8)));

        foreach (ulong value in values)
            Assert.AreEqual(new InstructionDiscriminator(value), Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(new InstructionDiscriminator(value))));

        foreach (ulong a in values)
            foreach (ulong b in Boundaries.Concat(values.Skip(Boundaries.Length).Take(30)))
                Assert.AreEqual(Math.Sign(a.CompareTo(b)),
                    Math.Sign(Converter.ConvertToProviderTyped(new InstructionDiscriminator(a)).CompareTo(Converter.ConvertToProviderTyped(new InstructionDiscriminator(b)))),
                    $"{a:x16} vs {b:x16}");
    }

    [TestMethod]
    public void FromProvider_EveryIntegerIsAValidDiscriminator_AtTheExtremes()
    {
        Assert.AreEqual(InstructionDiscriminator.Zero, Converter.ConvertFromProviderTyped(long.MinValue));
        Assert.AreEqual(new InstructionDiscriminator(ulong.MaxValue), Converter.ConvertFromProviderTyped(long.MaxValue));
        Assert.AreEqual(new InstructionDiscriminator(0x8000000000000000UL), Converter.ConvertFromProviderTyped(0L));
    }

    [TestMethod]
    public void Untyped_RoundTripsABoxedValue()
    {
        var value = new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL);
        object? provider = Converter.ConvertToProvider(value);

        Assert.IsInstanceOfType<long>(provider);
        Assert.AreEqual(value, Converter.ConvertFromProvider(provider));
        Assert.IsNull(Converter.ConvertToProvider(null));
        Assert.IsNull(Converter.ConvertFromProvider(null));
    }

    [TestMethod]
    public void MappingHints_AreOnlyWhatTheCallerSupplies()
    {
        Assert.IsNull(Converter.MappingHints);
        Assert.IsNull(new InstructionDiscriminatorValueConverter(null).MappingHints);
        Assert.AreEqual(5, new InstructionDiscriminatorValueConverter(new ConverterMappingHints(size: 5)).MappingHints?.Size);
    }
}
