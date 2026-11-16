using EccentricWare.Web3.DataTypes.Utils;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Boundary and differential tests for <see cref="UInt256BulkSimd"/>. Every case runs against the public API and against the
/// private scalar and AVX2 implementations (bound by reflection), so both code paths are checked on any machine.
/// </summary>
[TestClass]
public sealed class UInt256BulkSimdTests
{
    private static readonly uint256 Needle = new(0x1111_1111_1111_1111UL, 0x2222_2222_2222_2222UL, 0x3333_3333_3333_3333UL, 0x4444_4444_4444_4444UL);

    private static uint256 Filler(int i) => new((ulong)(1000 + i), (ulong)(2000 + i), (ulong)(3000 + i), (ulong)(4000 + i));

    private static bool Same(uint256 a, uint256 b) => a.U0 == b.U0 && a.U1 == b.U1 && a.U2 == b.U2 && a.U3 == b.U3;

    /// <summary>Haystack of distinct filler values with the needle at each given index.</summary>
    private static uint256[] Build(int length, params int[] needleAt)
    {
        uint256[] haystack = new uint256[length];
        for (int i = 0; i < length; i++)
            haystack[i] = Filler(i);
        foreach (int index in needleAt)
            haystack[index] = Needle;
        return haystack;
    }

    private static uint256 FlipBit(uint256 value, int bit)
    {
        ulong mask = 1UL << (bit & 63);
        return (bit >> 6) switch
        {
            0 => new uint256(value.U0 ^ mask, value.U1, value.U2, value.U3),
            1 => new uint256(value.U0, value.U1 ^ mask, value.U2, value.U3),
            2 => new uint256(value.U0, value.U1, value.U2 ^ mask, value.U3),
            _ => new uint256(value.U0, value.U1, value.U2, value.U3 ^ mask),
        };
    }

    // Reference results computed with plain loops.
    private static int ExpectedIndexOf(uint256[] haystack, uint256 needle)
    {
        for (int i = 0; i < haystack.Length; i++)
            if (Same(haystack[i], needle)) return i;
        return -1;
    }

    private static int[] ExpectedAll(uint256[] haystack, uint256 needle)
        => Enumerable.Range(0, haystack.Length).Where(i => Same(haystack[i], needle)).ToArray();

    // ---- guards ----

    [TestMethod]
    public void PrivateImplementationMethods_StillExist_ForReflectionBinding()
    {
        foreach (string name in UInt256BulkSimdImplementations.PrivateMethodNames)
            Assert.IsNotNull(UInt256BulkSimdImplementations.Find(name),
                $"UInt256BulkSimd.{name} was renamed or removed; update UInt256BulkSimdImplementations and these tests.");
    }

    [TestMethod]
    public void Avx2Implementation_IsExercisedWhenSupported()
    {
        if (!Avx2.IsSupported)
            Assert.Inconclusive("AVX2 is not supported on this machine; only the scalar implementation was exercised.");

        Assert.IsNotNull(UInt256BulkSimdImplementations.Avx2Implementation);
        Assert.IsTrue(UInt256BulkSimdImplementations.Supported().Any(i => i.Name == "Avx2"));
    }

    // ---- IndexOf ----

    [TestMethod]
    public void IndexOf_EveryLengthAndPosition_ReturnsThatPosition()
    {
        // Lengths 0..40 cover empty, single, the unroll-by-2 pairs, and both odd and even remainders.
        foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
        {
            for (int length = 0; length <= 40; length++)
            {
                Assert.AreEqual(-1, impl.IndexOf(Build(length), in Needle), $"{impl.Name} miss, length {length}");
                for (int position = 0; position < length; position++)
                    Assert.AreEqual(position, impl.IndexOf(Build(length, position), in Needle), $"{impl.Name} length {length} position {position}");
            }
        }
    }

    [TestMethod]
    public void IndexOf_Duplicates_ReturnsTheFirst()
    {
        foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
        {
            for (int length = 2; length <= 21; length++)
            {
                for (int first = 0; first < length - 1; first++)
                {
                    Assert.AreEqual(first, impl.IndexOf(Build(length, first, first + 1), in Needle), $"{impl.Name} adjacent, length {length} first {first}");
                    Assert.AreEqual(first, impl.IndexOf(Build(length, first, length - 1), in Needle), $"{impl.Name} far, length {length} first {first}");
                }
            }

            uint256[] allEqual = Enumerable.Repeat(Needle, 7).ToArray();
            Assert.AreEqual(0, impl.IndexOf(allEqual, in Needle), impl.Name);
        }
    }

    // ---- single-limb / single-bit differences ----

    [TestMethod]
    public void NeedleDifferingInOneBit_NeverMatches_AllOperations()
    {
        foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
        {
            int[] destination = new int[8];
            for (int bit = 0; bit < 256; bit++)
            {
                uint256 flipped = FlipBit(Needle, bit);
                uint256[] lookalikes = Enumerable.Repeat(flipped, 7).ToArray();

                Assert.AreEqual(-1, impl.IndexOf(lookalikes, in Needle), $"{impl.Name} bit {bit}");
                Assert.AreEqual(0, impl.CountEquals(lookalikes, in Needle), $"{impl.Name} bit {bit}");
                Assert.AreEqual(0, impl.FindAllEquals(lookalikes, in Needle, destination), $"{impl.Name} bit {bit}");

                lookalikes[5] = Needle;
                Assert.AreEqual(5, impl.IndexOf(lookalikes, in Needle), $"{impl.Name} bit {bit} with exact copy");
                Assert.AreEqual(1, impl.CountEquals(lookalikes, in Needle), $"{impl.Name} bit {bit} with exact copy");
                Assert.AreEqual(1, impl.FindAllEquals(lookalikes, in Needle, destination), $"{impl.Name} bit {bit} with exact copy");
                Assert.AreEqual(5, destination[0]);
            }
        }
    }

    [TestMethod]
    public void RepeatedLimbPatterns_DoNotFalseMatchPermutedLimbs()
    {
        const ulong A = 0x0101_0101_0101_0101UL;
        uint256 needle = new(A, 0, A, 0);
        uint256[] permuted =
        [
            new(0, A, 0, A), new(A, A, 0, 0), new(A, 0, 0, A), new(0, 0, A, A),
            new(A, 0, A, A), new(A, A, A, 0), new(A, A, A, A), new(0, 0, 0, 0),
        ];

        foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
        {
            Assert.AreEqual(-1, impl.IndexOf(permuted, in needle), impl.Name);
            Assert.AreEqual(0, impl.CountEquals(permuted, in needle), impl.Name);

            uint256[] withExact = [.. permuted, needle];
            Assert.AreEqual(permuted.Length, impl.IndexOf(withExact, in needle), impl.Name);
            Assert.AreEqual(1, impl.CountEquals(withExact, in needle), impl.Name);
        }
    }

    [TestMethod]
    public void SpecialNeedles_ZeroOneAndMax()
    {
        uint256[] zeros = new uint256[9];
        uint256[] nonZero = Build(9);
        foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
        {
            Assert.AreEqual(0, impl.IndexOf(zeros, in uint256.Zero), impl.Name);
            Assert.AreEqual(9, impl.CountEquals(zeros, in uint256.Zero), impl.Name);
            Assert.AreEqual(-1, impl.IndexOf(nonZero, in uint256.Zero), impl.Name);
            Assert.AreEqual(0, impl.CountEquals(nonZero, in uint256.Zero), impl.Name);

            uint256[] extremes = [uint256.Zero, uint256.One, uint256.MaxValue, uint256.One, uint256.MaxValue];
            Assert.AreEqual(1, impl.IndexOf(extremes, in uint256.One), impl.Name);
            Assert.AreEqual(2, impl.IndexOf(extremes, in uint256.MaxValue), impl.Name);
            Assert.AreEqual(2, impl.CountEquals(extremes, in uint256.MaxValue), impl.Name);
            Assert.AreEqual(2, impl.CountEquals(extremes, in uint256.One), impl.Name);
        }
    }

    // ---- CountEquals ----

    [TestMethod]
    public void CountEquals_RemaindersOfTheUnrollByFour()
    {
        foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
        {
            for (int length = 0; length <= 13; length++)
            {
                Assert.AreEqual(length, impl.CountEquals(Enumerable.Repeat(Needle, length).ToArray(), in Needle), $"{impl.Name} all equal, length {length}");
                Assert.AreEqual(0, impl.CountEquals(Build(length), in Needle), $"{impl.Name} none, length {length}");

                int[] everySecond = Enumerable.Range(0, length).Where(i => i % 2 == 0).ToArray();
                Assert.AreEqual((length + 1) / 2, impl.CountEquals(Build(length, everySecond), in Needle), $"{impl.Name} every second, length {length}");
            }

            for (int position = 0; position < 11; position++)
                Assert.AreEqual(1, impl.CountEquals(Build(11, position), in Needle), $"{impl.Name} single match at {position}");
        }
    }

    [TestMethod]
    public void CountEquals_MatchesInEachLaneOfAnUnrolledBlock_AreCountedIndependently()
    {
        foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
        {
            for (int lane = 0; lane < 8; lane++)
                Assert.AreEqual(1, impl.CountEquals(Build(8, lane), in Needle), $"{impl.Name} lane {lane}");

            Assert.AreEqual(2, impl.CountEquals(Build(8, 0, 3), in Needle), impl.Name);
            Assert.AreEqual(2, impl.CountEquals(Build(8, 4, 7), in Needle), impl.Name);
            Assert.AreEqual(4, impl.CountEquals(Build(8, 0, 1, 2, 3), in Needle), impl.Name);
        }
    }

    // ---- FindAllEquals ----

    [TestMethod]
    public void FindAllEquals_FullDestination_StopsAtDestinationLength()
    {
        uint256[] haystack = Build(12, 1, 4, 5, 9);   // four matches
        foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
        {
            foreach ((int destinationLength, int[] expected) in new[]
            {
                (1, new[] { 1 }), (2, new[] { 1, 4 }), (3, new[] { 1, 4, 5 }), (4, new[] { 1, 4, 5, 9 }),
            })
            {
                int[] destination = Enumerable.Repeat(-7, destinationLength + 3).ToArray();
                int written = impl.FindAllEquals(haystack, in Needle, destination.AsSpan(0, destinationLength));

                Assert.AreEqual(destinationLength, written, $"{impl.Name} destination {destinationLength}");
                CollectionAssert.AreEqual(expected, destination[..written], impl.Name);
                CollectionAssert.AreEqual(new[] { -7, -7, -7 }, destination[destinationLength..], $"{impl.Name}: elements past the destination must stay untouched");
            }
        }
    }

    [TestMethod]
    public void FindAllEquals_LargerDestination_WritesOnlyTheMatches()
    {
        uint256[] haystack = Build(12, 1, 4, 5, 9);
        foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
        {
            int[] destination = Enumerable.Repeat(-7, 10).ToArray();

            int written = impl.FindAllEquals(haystack, in Needle, destination);

            Assert.AreEqual(4, written, impl.Name);
            CollectionAssert.AreEqual(new[] { 1, 4, 5, 9, -7, -7, -7, -7, -7, -7 }, destination, impl.Name);
        }
    }

    [TestMethod]
    public void FindAllEquals_EmptyHaystackOrDestination_ReturnsZeroAndTouchesNothing()
    {
        // The public API guards both; the private implementations do not accept an empty destination.
        int[] destination = Enumerable.Repeat(-7, 4).ToArray();

        Assert.AreEqual(0, UInt256BulkSimd.FindAllEquals(ReadOnlySpan<uint256>.Empty, in Needle, destination));
        CollectionAssert.AreEqual(new[] { -7, -7, -7, -7 }, destination);

        Assert.AreEqual(0, UInt256BulkSimd.FindAllEquals(Build(5, 0, 1, 2), in Needle, Span<int>.Empty));
        Assert.AreEqual(0, UInt256BulkSimd.FindAllEquals(ReadOnlySpan<uint256>.Empty, in Needle, Span<int>.Empty));
    }

    [TestMethod]
    public void EmptyHaystack_PublicApi_ReturnsNotFoundAndZero()
    {
        Assert.AreEqual(-1, UInt256BulkSimd.IndexOf(ReadOnlySpan<uint256>.Empty, in Needle));
        Assert.AreEqual(0, UInt256BulkSimd.CountEquals(ReadOnlySpan<uint256>.Empty, in Needle));
        Assert.IsFalse(UInt256BulkSimd.Contains(ReadOnlySpan<uint256>.Empty, in Needle));
        Assert.IsFalse(UInt256BulkSimd.Contains(ReadOnlySpan<uint256>.Empty, in uint256.Zero));
    }

    [TestMethod]
    public void Contains_PresentAndAbsent()
    {
        Assert.IsTrue(UInt256BulkSimd.Contains(Build(9, 8), in Needle));
        Assert.IsTrue(UInt256BulkSimd.Contains(Build(1, 0), in Needle));
        Assert.IsFalse(UInt256BulkSimd.Contains(Build(9), in Needle));
        Assert.IsFalse(UInt256BulkSimd.Contains(Build(9, 3), in uint256.MaxValue));
    }

    // ---- consistency and differential ----

    [TestMethod]
    public void RandomHaystacks_AllOperationsAgreeWithPlainLoops_OnEveryImplementation()
    {
        // Small value pool, including values that differ from the needle in one limb only, so duplicates are frequent.
        uint256[] pool =
        [
            Needle, Needle, uint256.Zero, uint256.MaxValue,
            new(Needle.U0 ^ 1, Needle.U1, Needle.U2, Needle.U3), new(Needle.U0, Needle.U1, Needle.U2, Needle.U3 ^ (1UL << 63)),
            new(Needle.U1, Needle.U0, Needle.U3, Needle.U2),
        ];
        var rng = new Random(20240601);

        foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
        {
            for (int length = 0; length <= 70; length++)
            {
                for (int trial = 0; trial < 20; trial++)
                {
                    uint256[] haystack = new uint256[length];
                    for (int i = 0; i < length; i++)
                        haystack[i] = pool[rng.Next(pool.Length)];

                    int[] expectedAll = ExpectedAll(haystack, Needle);
                    string context = $"{impl.Name} length {length} trial {trial}";

                    Assert.AreEqual(ExpectedIndexOf(haystack, Needle), impl.IndexOf(haystack, in Needle), context);
                    Assert.AreEqual(expectedAll.Length, impl.CountEquals(haystack, in Needle), context);

                    if (length > 0)
                    {
                        int[] destination = new int[length];
                        int written = impl.FindAllEquals(haystack, in Needle, destination);
                        Assert.AreEqual(expectedAll.Length, written, context);
                        CollectionAssert.AreEqual(expectedAll, destination[..written], context);
                    }
                }
            }
        }
    }

    [TestMethod]
    public void PublicApi_AgreesWithScalarImplementation_OnRandomWideValues()
    {
        var rng = new Random(77);
        UInt256BulkImplementation scalar = UInt256BulkSimdImplementations.Scalar;
        for (int trial = 0; trial < 200; trial++)
        {
            uint256[] haystack = Enumerable.Range(0, rng.Next(0, 60)).Select(_ => UInt256TestValues.SparseValue(rng)).ToArray();
            uint256 needle = haystack.Length > 0 && rng.Next(4) != 0 ? haystack[rng.Next(haystack.Length)] : UInt256TestValues.SparseValue(rng);

            Assert.AreEqual(scalar.IndexOf(haystack, in needle), UInt256BulkSimd.IndexOf(haystack, in needle));
            Assert.AreEqual(scalar.CountEquals(haystack, in needle), UInt256BulkSimd.CountEquals(haystack, in needle));
        }
    }

    // ---- input shapes ----

    [TestMethod]
    public void NeedleThatIsAnElementOfTheHaystack_IsFoundAtItsFirstOccurrence()
    {
        uint256[] haystack = Build(10, 6);
        haystack[8] = haystack[6];
        foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
        {
            Assert.AreEqual(6, impl.IndexOf(haystack, in haystack[8]), impl.Name);
            Assert.AreEqual(2, impl.CountEquals(haystack, in haystack[6]), impl.Name);
        }
    }

    [TestMethod]
    public void MisalignedHaystack_IsReadCorrectly()
    {
        // Spans that start at byte offsets that are not multiples of 8 or 32 exercise the unaligned vector load.
        uint256[] source = Build(11, 3, 9);
        byte[] bytes = new byte[11 * 32 + 8];
        int[] found = new int[4];
        foreach (int offset in new[] { 1, 3, 5, 7 })
        {
            for (int i = 0; i < source.Length; i++)
                source[i].WriteLittleEndian(bytes.AsSpan(offset + i * 32, 32));
            ReadOnlySpan<uint256> haystack = MemoryMarshal.Cast<byte, uint256>(bytes.AsSpan(offset, 11 * 32));

            foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
            {
                Assert.AreEqual(3, impl.IndexOf(haystack, in Needle), $"{impl.Name} offset {offset}");
                Assert.AreEqual(2, impl.CountEquals(haystack, in Needle), $"{impl.Name} offset {offset}");
                Assert.AreEqual(2, impl.FindAllEquals(haystack, in Needle, found), $"{impl.Name} offset {offset}");
                Assert.AreEqual(9, found[1]);
            }
        }
    }

    [TestMethod]
    public void SlicedArray_UsesSliceBounds()
    {
        uint256[] source = Build(20, 2, 15);
        foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
        {
            // Slice [3, 15): excludes the matches at 2 and 15.
            Assert.AreEqual(-1, impl.IndexOf(source.AsSpan(3, 12), in Needle), impl.Name);
            Assert.AreEqual(0, impl.CountEquals(source.AsSpan(3, 12), in Needle), impl.Name);
            Assert.AreEqual(12, impl.IndexOf(source.AsSpan(3, 13), in Needle), impl.Name);
        }
    }

    [TestMethod]
    public void StackAllocatedHaystack_Works()
    {
        Span<uint256> stack = stackalloc uint256[9];
        for (int i = 0; i < stack.Length; i++)
            stack[i] = Filler(i);
        stack[7] = Needle;

        Assert.AreEqual(7, UInt256BulkSimd.IndexOf(stack, in Needle));
        Assert.AreEqual(1, UInt256BulkSimd.CountEquals(stack, in Needle));
        Assert.IsTrue(UInt256BulkSimd.Contains(stack, in Needle));
    }

    [TestMethod]
    public void LargeHaystack_MatchAtTheEnd_AndAllEqual()
    {
        const int Length = 10_000;
        uint256[] haystack = Build(Length, Length - 1);
        uint256[] allEqual = Enumerable.Repeat(Needle, Length).ToArray();

        foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
        {
            Assert.AreEqual(Length - 1, impl.IndexOf(haystack, in Needle), impl.Name);
            Assert.AreEqual(1, impl.CountEquals(haystack, in Needle), impl.Name);
            Assert.AreEqual(Length, impl.CountEquals(allEqual, in Needle), impl.Name);

            int[] destination = new int[Length];
            Assert.AreEqual(Length, impl.FindAllEquals(allEqual, in Needle, destination), impl.Name);
            Assert.AreEqual(Length - 1, destination[^1], impl.Name);
        }
    }
}
