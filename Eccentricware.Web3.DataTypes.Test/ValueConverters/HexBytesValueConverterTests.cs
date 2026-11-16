using System.Runtime.InteropServices;
using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="HexBytesValueConverter"/>: variable-length binary, zero copy in both directions.</summary>
[TestClass]
public sealed class HexBytesValueConverterTests
{
    private static readonly HexBytesValueConverter Converter = new();

    private static byte[] BackingArray(HexBytes value)
    {
        Assert.IsTrue(MemoryMarshal.TryGetArray(value.Memory, out ArraySegment<byte> segment));
        return segment.Array!;
    }

    // ----------------- layout and round trip -----------------

    [TestMethod]
    public void ToProvider_IsTheValueBytes()
    {
        CollectionAssert.AreEqual(new byte[] { 0xde, 0xad, 0xbe, 0xef }, Converter.ConvertToProviderTyped(HexBytes.Parse("0xdeadbeef")));
        CollectionAssert.AreEqual(new byte[] { 0x00 }, Converter.ConvertToProviderTyped(HexBytes.Parse("0x00")));
    }

    [TestMethod]
    public void ToProvider_EmptyAndDefault_AreNonNullZeroLengthArrays()
    {
        Assert.IsNotNull(Converter.ConvertToProviderTyped(HexBytes.Empty));
        Assert.HasCount(0, Converter.ConvertToProviderTyped(HexBytes.Empty));
        Assert.IsNotNull(Converter.ConvertToProviderTyped(default));
        Assert.HasCount(0, Converter.ConvertToProviderTyped(default));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(31)]
    [DataRow(32)]
    [DataRow(33)]
    [DataRow(4096)]
    [DataRow(4097)]
    [DataRow(65536)]
    public void RoundTrip_PreservesTheBytes_AtEveryLength(int length)
    {
        var value = new HexBytes(ConverterTestData.RandomBytes(new Random(length), length));

        HexBytes back = Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(value));

        Assert.AreEqual(value, back);
        Assert.AreEqual(length, back.Length);
    }

    [TestMethod]
    public void RoundTrip_Empty()
    {
        Assert.AreEqual(HexBytes.Empty, Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(HexBytes.Empty)));
        Assert.AreEqual(HexBytes.Empty, Converter.ConvertFromProviderTyped([]));
        Assert.IsTrue(Converter.ConvertFromProviderTyped([]).IsEmpty);
    }

    [TestMethod]
    public void Untyped_RoundTripsABoxedValue_AndPassesNullThrough()
    {
        HexBytes value = HexBytes.Parse("0xdeadbeef");
        object? provider = Converter.ConvertToProvider(value);

        Assert.IsInstanceOfType<byte[]>(provider);
        Assert.AreEqual(value, Converter.ConvertFromProvider(provider));
        Assert.IsNull(Converter.ConvertToProvider(null));
        Assert.IsNull(Converter.ConvertFromProvider(null));
    }

    [TestMethod]
    public void FromProvider_Null_ThrowsArgumentNullException()
        => Assert.AreEqual("bytes", Assert.ThrowsExactly<ArgumentNullException>(() => Converter.ConvertFromProviderTyped(null!)).ParamName);

    // ----------------- zero-copy contract -----------------

    [TestMethod]
    public void ToProvider_HandsTheValuesOwnArrayToTheProvider()
    {
        var value = new HexBytes(ConverterTestData.RandomBytes(new Random(2), 64 * 1024));

        Assert.AreSame(BackingArray(value), Converter.ConvertToProviderTyped(value));
    }

    [TestMethod]
    public void FromProvider_WrapsTheProviderArrayWithoutCopying()
    {
        byte[] stored = ConverterTestData.RandomBytes(new Random(3), 4096);

        HexBytes value = Converter.ConvertFromProviderTyped(stored);

        Assert.AreSame(stored, BackingArray(value));
    }

    [TestMethod]
    public void FromProvider_DocumentedHazard_MutatingTheProviderArrayChangesTheValue()
    {
        // Documents current behaviour stated in the converter remarks: the provider array is treated as read-only.
        byte[] stored = [1, 2, 3];
        HexBytes value = Converter.ConvertFromProviderTyped(stored);

        stored[0] = 9;

        Assert.AreEqual(9, value[0]);
    }

    [TestMethod]
    public void RoundTrip_Allocates_NothingProportionalToTheData()
    {
        var value = new HexBytes(ConverterTestData.RandomBytes(new Random(2), 64 * 1024));
        Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(value)); // compile and warm both delegates

        long smallest = long.MaxValue;
        for (int trial = 0; trial < 5; trial++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            HexBytes back = Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(value));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(back);
            smallest = Math.Min(smallest, allocated);
        }

        Assert.IsLessThan(1024, smallest, $"allocated {smallest} bytes for a 64 KiB value");
    }

    // ----------------- hints -----------------

    [TestMethod]
    public void MappingHints_AreOnlyWhatTheCallerSupplies()
    {
        Assert.IsNull(Converter.MappingHints);
        Assert.IsNull(new HexBytesValueConverter(null).MappingHints);
        Assert.AreEqual(5, new HexBytesValueConverter(new ConverterMappingHints(size: 5)).MappingHints?.Size);
    }
}
