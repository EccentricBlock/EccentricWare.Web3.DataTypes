using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static EccentricWare.Web3.DataTypes.Tests.Int256TestValues;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Type-level guarantees of <see cref="int256"/>: default value, size and layout, immutability, thread safety.</summary>
[TestClass]
public sealed class Int256StructInvariantTests
{
    [TestMethod]
    public void DefaultValue_IsZeroInEveryRespect()
    {
        int256 d = default;
        Assert.AreEqual(int256.Zero, d);
        Assert.IsTrue(d.IsZero);
        Assert.IsFalse(d.IsNegative);
        Assert.AreEqual(0, d.Sign);
        Assert.AreEqual("0", d.ToString());
        CollectionAssert.AreEqual(new byte[32], d.ToBigEndianBytes());
        Assert.AreEqual(0, d.CompareTo(int256.Zero));
    }

    [TestMethod]
    public void Size_Is32Bytes_WithoutReferences()
    {
        Assert.AreEqual(32, Unsafe.SizeOf<int256>());
        Assert.IsFalse(RuntimeHelpers.IsReferenceOrContainsReferences<int256>());
        Assert.IsTrue(typeof(int256).IsValueType);
    }

    [TestMethod]
    public void Layout_IsSequential_WithLimbsLeastSignificantFirst()
    {
        Assert.IsTrue(typeof(int256).IsLayoutSequential);

        // Consumers that reinterpret the struct see u0..u3 in memory order.
        int256[] source = [int256.MinValue, new int256(1, 2, 3, 4)];
        ulong[] limbs = MemoryMarshal.Cast<int256, ulong>(source).ToArray();
        CollectionAssert.AreEqual(new ulong[] { 0, 0, 0, 0x8000_0000_0000_0000UL, 1, 2, 3, 4 }, limbs);
    }

    [TestMethod]
    public void AllInstanceFields_AreReadOnly_AndExactlyFourLimbs()
    {
        FieldInfo[] fields = typeof(int256).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.HasCount(4, fields);
        Assert.IsTrue(fields.All(f => f.IsInitOnly && f.FieldType == typeof(ulong)));
        Assert.IsTrue(typeof(int256).GetCustomAttribute<System.Runtime.CompilerServices.IsReadOnlyAttribute>() is not null);
    }

    [TestMethod]
    public void Constants_AreUnchangedAfterABatteryOfOperations()
    {
        int256 min = int256.MinValue, max = int256.MaxValue, one = int256.One, minusOne = int256.MinusOne, zero = int256.Zero;

        _ = min + zero; _ = max - zero; _ = -max; _ = ~min; _ = min >> 7; _ = max << 7; _ = min & max; _ = min | max; _ = min ^ max;
        _ = int256.NegateUnchecked(min); _ = int256.AddUnchecked(max, one); _ = int256.SubtractUnchecked(min, one);
        _ = min.ToString("0x", null); _ = minusOne.ToBigEndianBytes();

        Assert.AreEqual(-Two255, ToBig(int256.MinValue));
        Assert.AreEqual(Two255 - 1, ToBig(int256.MaxValue));
        Assert.AreEqual(1, ToBig(int256.One));
        Assert.AreEqual(-1, ToBig(int256.MinusOne));
        Assert.AreEqual(0, ToBig(int256.Zero));
    }

    [TestMethod]
    public void ParallelUse_ParseFormatAndArithmetic_AgreesWithTheOracle()
    {
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 20_000, i =>
        {
            long v = (long)i * 1_000_003 - 10_000_000_000L;
            int256 x = new(v);
            int256 doubled = x + x;
            string text = doubled.ToString();
            if (text != (2 * (System.Numerics.BigInteger)v).ToString()
                || int256.Parse(text) != doubled
                || (x * x).IsNegative)
                failures.Add($"i={i}");
        });

        Assert.IsEmpty(failures);
    }
}
