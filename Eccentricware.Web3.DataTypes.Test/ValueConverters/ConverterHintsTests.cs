using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// <c>ConverterHints.Merge</c>: caller hints laid over a converter's defaults (size, precision, scale and unicode; the value generator factory hint is obsolete in EF Core 10 and not used by the library). The implementation delegates to EF's
/// <c>ConverterMappingHints.With</c>, whose own XML documentation claims already-specified hints are not overridden, yet
/// at runtime the argument wins. The library relies on the runtime behaviour, so these tests are the tripwire for an EF upgrade.
/// </summary>
[TestClass]
public sealed class ConverterHintsTests
{
    private static ConverterMappingHints Defaults() => new(size: 33);

    [TestMethod]
    public void Merge_WithNullCallerHints_ReturnsTheDefaultsInstance()
    {
        ConverterMappingHints defaults = Defaults();

        Assert.AreSame(defaults, ConverterHints.Merge(defaults, null));
    }

    [TestMethod]
    public void Merge_WithEmptyCallerHints_KeepsEveryDefault()
    {
        ConverterMappingHints merged = ConverterHints.Merge(Defaults(), new ConverterMappingHints());

        Assert.AreEqual(33, merged.Size);
        Assert.IsNull(merged.Precision);
        Assert.IsNull(merged.Scale);
        Assert.IsNull(merged.IsUnicode);
    }

    [TestMethod]
    public void Merge_CallerSizeWins_OverTheDefaultSize()
        => Assert.AreEqual(99, ConverterHints.Merge(Defaults(), new ConverterMappingHints(size: 99)).Size);

    [TestMethod]
    public void Merge_CallerHintsWithoutASize_KeepTheDefaultSizeAndAddTheirOwn()
    {
        ConverterMappingHints merged = ConverterHints.Merge(Defaults(), new ConverterMappingHints(precision: 7, scale: 2, unicode: false));

        Assert.AreEqual(33, merged.Size);
        Assert.AreEqual(7, merged.Precision);
        Assert.AreEqual(2, merged.Scale);
        Assert.AreEqual(false, merged.IsUnicode);
    }

    [TestMethod]
    public void Merge_CallerWinsOnEveryConflictingHint()
    {
        var defaults = new ConverterMappingHints(size: 10, precision: 5, scale: 1, unicode: true);
        var caller = new ConverterMappingHints(size: 20, precision: 6, scale: 3, unicode: false);

        ConverterMappingHints merged = ConverterHints.Merge(defaults, caller);

        Assert.AreEqual(20, merged.Size);
        Assert.AreEqual(6, merged.Precision);
        Assert.AreEqual(3, merged.Scale);
        Assert.AreEqual(false, merged.IsUnicode);
    }

    [TestMethod]
    public void Merge_DoesNotMutateTheDefaultsOrTheCallerHints()
    {
        ConverterMappingHints defaults = Defaults();
        var caller = new ConverterMappingHints(size: 99, precision: 7);

        ConverterMappingHints merged = ConverterHints.Merge(defaults, caller);

        Assert.AreNotSame(defaults, merged);
        Assert.AreNotSame(caller, merged);
        Assert.AreEqual(33, defaults.Size);
        Assert.IsNull(defaults.Precision);
        Assert.AreEqual(99, caller.Size);
        Assert.AreEqual(7, caller.Precision);
    }

    [TestMethod]
    public void Merge_RepeatedMergesOfSharedDefaults_DoNotLeakBetweenCallers()
    {
        // Converters share one static defaults instance; a caller's hints must not stick to it for the next converter.
        ConverterMappingHints shared = Defaults();

        ConverterHints.Merge(shared, new ConverterMappingHints(size: 99, unicode: true));
        ConverterMappingHints second = ConverterHints.Merge(shared, new ConverterMappingHints(precision: 3));

        Assert.AreEqual(33, second.Size);
        Assert.IsNull(second.IsUnicode);
        Assert.AreEqual(3, second.Precision);
    }
}
