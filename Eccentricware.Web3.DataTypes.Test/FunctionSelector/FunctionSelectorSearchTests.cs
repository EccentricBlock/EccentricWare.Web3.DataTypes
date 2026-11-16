namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>BinarySearchSorted, IsInSortedAllowList and ContainsSimd.</summary>
[TestClass]
public sealed class FunctionSelectorSearchTests
{
    [TestMethod]
    public void BinarySearchSorted_FindsAndNotFinds()
    {
        var sorted = new[]
        {
            new FunctionSelector(0x00000001u),
            new FunctionSelector(0x00000010u),
            new FunctionSelector(0x00ffffffu),
            new FunctionSelector(0x01000000u),
            new FunctionSelector(0xffffffffu),
        };

        Assert.AreEqual(0, FunctionSelector.BinarySearchSorted(sorted, new FunctionSelector(0x00000001u)));
        Assert.AreEqual(2, FunctionSelector.BinarySearchSorted(sorted, new FunctionSelector(0x00ffffffu)));
        Assert.AreEqual(4, FunctionSelector.BinarySearchSorted(sorted, new FunctionSelector(0xffffffffu)));

        Assert.AreEqual(-1, FunctionSelector.BinarySearchSorted(sorted, new FunctionSelector(0x00000002u)));
        Assert.AreEqual(-1, FunctionSelector.BinarySearchSorted(ReadOnlySpan<FunctionSelector>.Empty, new FunctionSelector(0x00000001u)));
    }

    [TestMethod]
    public void IsInSortedAllowList_UsesBinarySearchSemantics()
    {
        var allow = new[]
        {
            FunctionSelector.Erc20.Approve,
            FunctionSelector.Erc20.BalanceOf,
            FunctionSelector.Erc20.Transfer,
            FunctionSelector.Erc20.TransferFrom,
        }.OrderBy(s => s.Value).ToArray();

        Assert.IsTrue(FunctionSelector.IsInSortedAllowList(allow, FunctionSelector.Erc20.Transfer));
        Assert.IsFalse(FunctionSelector.IsInSortedAllowList(allow, FunctionSelector.Erc20.TotalSupply));
    }

    [TestMethod]
    public void ContainsSimd_FindsAndNotFinds_ForSmallAndLargeSpans()
    {
        var target = new FunctionSelector(0x11223344u);

        // Small (scalar fallback)
        var small = new[]
        {
            new FunctionSelector(0x00000000u),
            new FunctionSelector(0x01020304u),
            target,
            new FunctionSelector(0xffffffffu),
        };
        Assert.IsTrue(FunctionSelector.ContainsSimd(small, target));
        Assert.IsFalse(FunctionSelector.ContainsSimd(small, new FunctionSelector(0x55667788u)));

        // Large (may SIMD-accelerate depending on CPU/JIT)
        var large = Enumerable.Range(0, 256).Select(i => new FunctionSelector((uint)i)).ToArray();
        large[200] = target;

        Assert.IsTrue(FunctionSelector.ContainsSimd(large, target));
        Assert.IsFalse(FunctionSelector.ContainsSimd(large, new FunctionSelector(0xDEADBEEFu)));
    }

    [TestMethod]
    public void ContainsSimd_EmptyHaystack_ReturnsFalse()
    {
        bool ok = FunctionSelector.ContainsSimd(ReadOnlySpan<FunctionSelector>.Empty, new FunctionSelector(0x01020304u));
        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void ContainsSimd_FindsZeroSelector()
    {
        var haystack = new[]
        {
            new FunctionSelector(0x01020304u),
            FunctionSelector.Zero,
            new FunctionSelector(0xffffffffu),
        };

        Assert.IsTrue(FunctionSelector.ContainsSimd(haystack, FunctionSelector.Zero));
    }

    [TestMethod]
    public void BinarySearchSorted_SingleElement_Works()
    {
        var one = new[] { new FunctionSelector(0x01020304u) };

        Assert.AreEqual(0, FunctionSelector.BinarySearchSorted(one, new FunctionSelector(0x01020304u)));
        Assert.AreEqual(-1, FunctionSelector.BinarySearchSorted(one, new FunctionSelector(0x01020305u)));
    }

    [TestMethod]
    public void IsInSortedAllowList_Empty_ReturnsFalse()
    {
        Assert.IsFalse(FunctionSelector.IsInSortedAllowList(ReadOnlySpan<FunctionSelector>.Empty, FunctionSelector.Erc20.Transfer));
    }

    [TestMethod]
    public void ContainsSimd_MatchesLinearSearch_ForAllLengthsAndPositions()
    {
        var rng = new Random(77);
        for (int length = 0; length <= 150; length++)
        {
            var haystack = new FunctionSelector[length];
            var seen = new HashSet<uint>();
            for (int i = 0; i < length; i++)
            {
                uint v;
                do { v = (uint)rng.NextInt64(0, uint.MaxValue + 1L); } while (!seen.Add(v));
                haystack[i] = new FunctionSelector(v);
            }

            for (int position = 0; position < length; position++)
                Assert.IsTrue(FunctionSelector.ContainsSimd(haystack, haystack[position]), $"len={length} pos={position}");

            // Absent values, including near-misses differing by one bit in each byte of an existing value.
            uint absent;
            do { absent = (uint)rng.NextInt64(0, uint.MaxValue + 1L); } while (seen.Contains(absent));
            Assert.IsFalse(FunctionSelector.ContainsSimd(haystack, new FunctionSelector(absent)));

            if (length > 0)
            {
                for (int bit = 0; bit < 32; bit += 8)
                {
                    uint near = haystack[length / 2].Value ^ (1u << bit);
                    Assert.AreEqual(seen.Contains(near), FunctionSelector.ContainsSimd(haystack, new FunctionSelector(near)));
                }
            }
        }
    }

    [TestMethod]
    public void ContainsSimd_WorksOnOffsetSlices()
    {
        var haystack = Enumerable.Range(0, 200).Select(i => new FunctionSelector((uint)(i * 7919 + 1))).ToArray();

        Assert.IsTrue(FunctionSelector.ContainsSimd(haystack.AsSpan(33), haystack[199]));
        Assert.IsFalse(FunctionSelector.ContainsSimd(haystack.AsSpan(33), haystack[32]));
        Assert.IsFalse(FunctionSelector.ContainsSimd(haystack.AsSpan(0, 100), haystack[100]));
        Assert.IsTrue(FunctionSelector.ContainsSimd(haystack.AsSpan(0, 101), haystack[100]));
    }

    // ----------------- additional BinarySearchSorted coverage -----------------

    private static uint[] SortedDistinctValues(int count, int seed)
    {
        // Spread over the whole uint range, including values with the top bit set (a signed comparison would misorder them).
        var rng = new Random(seed);
        var set = new SortedSet<uint>();
        while (set.Count < count)
            set.Add((uint)rng.NextInt64(0, uint.MaxValue + 1L));
        return [.. set];
    }

    private static FunctionSelector[] AsSelectors(uint[] values) => values.Select(v => new FunctionSelector(v)).ToArray();

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(31)]
    [DataRow(64)]
    [DataRow(257)]
    [DataRow(5000)]
    public void BinarySearchSorted_FindsEveryElementAtItsIndex_AndMissesEveryGapAndBothEnds(int count)
    {
        uint[] values = SortedDistinctValues(count, seed: 1000 + count);
        FunctionSelector[] sorted = AsSelectors(values);

        for (int i = 0; i < values.Length; i++)
        {
            Assert.AreEqual(i, FunctionSelector.BinarySearchSorted(sorted, sorted[i]), $"count={count} index={i}");
            Assert.IsTrue(FunctionSelector.IsInSortedAllowList(sorted, sorted[i]));

            // Absent neighbours: just below and just above (when they are not themselves present).
            foreach (uint probe in new[] { values[i] - 1, values[i] + 1 })
            {
                if (Array.BinarySearch(values, probe) >= 0)
                    continue;
                Assert.AreEqual(-1, FunctionSelector.BinarySearchSorted(sorted, new FunctionSelector(probe)), $"count={count} probe={probe:x8}");
                Assert.IsFalse(FunctionSelector.IsInSortedAllowList(sorted, new FunctionSelector(probe)));
            }
        }

        if (count > 0 && values[0] > 0)
            Assert.AreEqual(-1, FunctionSelector.BinarySearchSorted(sorted, FunctionSelector.Zero), "below the first element");
        if (count > 0 && values[^1] < uint.MaxValue)
            Assert.AreEqual(-1, FunctionSelector.BinarySearchSorted(sorted, new FunctionSelector(uint.MaxValue)), "above the last element");
    }

    [TestMethod]
    public void BinarySearchSorted_OrdersUnsigned_ValuesWithTheTopBitSetSortAfterThoseWithout()
    {
        var sorted = AsSelectors([0x00000000u, 0x7FFFFFFFu, 0x80000000u, 0x80000001u, 0xFFFFFFFEu, 0xFFFFFFFFu]);

        for (int i = 0; i < sorted.Length; i++)
            Assert.AreEqual(i, FunctionSelector.BinarySearchSorted(sorted, sorted[i]), sorted[i].ToString());

        Assert.AreEqual(-1, FunctionSelector.BinarySearchSorted(sorted, new FunctionSelector(0x7FFFFFFEu)));
        Assert.AreEqual(-1, FunctionSelector.BinarySearchSorted(sorted, new FunctionSelector(0x80000002u)));
    }

    [TestMethod]
    public void BinarySearchSorted_WithDuplicates_ReturnsAnIndexOfAnEqualElement_AndMissesAbsentValues()
    {
        // The contract is "an index of the value", not the first or last one.
        uint[] values = [1, 5, 5, 5, 5, 5, 9, 9, 0x80000000, 0x80000000, 0xFFFFFFFF, 0xFFFFFFFF];
        FunctionSelector[] sorted = AsSelectors(values);

        foreach (uint present in new uint[] { 1, 5, 9, 0x80000000, 0xFFFFFFFF })
        {
            int index = FunctionSelector.BinarySearchSorted(sorted, new FunctionSelector(present));
            Assert.IsGreaterThanOrEqualTo(0, index, $"{present:x8}");
            Assert.AreEqual(present, sorted[index].Value);
        }

        foreach (uint absent in new uint[] { 0, 2, 4, 6, 8, 10, 0x7FFFFFFF, 0x80000001, 0xFFFFFFFE })
            Assert.AreEqual(-1, FunctionSelector.BinarySearchSorted(sorted, new FunctionSelector(absent)), $"{absent:x8}");
    }

    [TestMethod]
    public void BinarySearchSorted_OnOffsetSlices_ReturnsIndexesRelativeToTheSlice_AndIgnoresElementsOutsideIt()
    {
        FunctionSelector[] sorted = AsSelectors(SortedDistinctValues(100, seed: 5));
        Span<FunctionSelector> slice = sorted.AsSpan(20, 30);

        Assert.AreEqual(0, FunctionSelector.BinarySearchSorted(slice, sorted[20]));
        Assert.AreEqual(29, FunctionSelector.BinarySearchSorted(slice, sorted[49]));
        Assert.AreEqual(-1, FunctionSelector.BinarySearchSorted(slice, sorted[19]));
        Assert.AreEqual(-1, FunctionSelector.BinarySearchSorted(slice, sorted[50]));
    }

    // ----------------- additional ContainsSimd coverage -----------------

    [TestMethod]
    public void ContainsSimd_RejectsElementsThatMatchInThreeOfFourBytes_AtEveryLength()
    {
        // Each haystack element equals the needle except in one byte lane; none may match (lane-wise comparison bugs).
        var needle = new FunctionSelector(0x12345678u);
        uint[] nearMisses = [0x12345679u, 0x12345778u, 0x12355678u, 0x13345678u, 0x00000000u, 0xFFFFFFFFu];

        for (int length = 0; length <= 70; length++)
        {
            var haystack = Enumerable.Range(0, length).Select(i => new FunctionSelector(nearMisses[i % nearMisses.Length])).ToArray();
            Assert.IsFalse(FunctionSelector.ContainsSimd(haystack, needle), $"length={length}");
        }
    }

    [TestMethod]
    public void ContainsSimd_FindsNeedleAtFirstAndLastIndex_AndZeroAndMaxValues_AtEveryLength()
    {
        foreach (uint needleValue in new uint[] { 0u, 0xFFFFFFFFu, 0x80000000u })
        {
            var needle = new FunctionSelector(needleValue);
            for (int length = 1; length <= 70; length++)
            {
                var filler = new FunctionSelector(0x01010101u);
                var haystack = Enumerable.Repeat(filler, length).ToArray();
                Assert.IsFalse(FunctionSelector.ContainsSimd(haystack, needle), $"absent value={needleValue:x8} length={length}");

                haystack[0] = needle;
                Assert.IsTrue(FunctionSelector.ContainsSimd(haystack, needle), $"first value={needleValue:x8} length={length}");
                haystack[0] = filler;
                haystack[^1] = needle;
                Assert.IsTrue(FunctionSelector.ContainsSimd(haystack, needle), $"last value={needleValue:x8} length={length}");
            }
        }
    }

    [TestMethod]
    public void ContainsSimd_AgreesWithBinarySearch_OnSortedData()
    {
        uint[] values = SortedDistinctValues(300, seed: 42);
        FunctionSelector[] sorted = AsSelectors(values);
        var rng = new Random(43);

        for (int i = 0; i < 2000; i++)
        {
            uint probe = i % 2 == 0 ? values[rng.Next(values.Length)] : (uint)rng.NextInt64(0, uint.MaxValue + 1L);
            var selector = new FunctionSelector(probe);
            Assert.AreEqual(Array.BinarySearch(values, probe) >= 0, FunctionSelector.ContainsSimd(sorted, selector), probe.ToString("x8"));
            Assert.AreEqual(FunctionSelector.IsInSortedAllowList(sorted, selector), FunctionSelector.ContainsSimd(sorted, selector));
        }
    }
}
