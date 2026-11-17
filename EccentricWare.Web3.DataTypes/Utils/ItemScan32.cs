using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace EccentricWare.Web3.DataTypes.Utils;

/// <summary>
/// Searches arrays of 32-byte items (<see cref="uint256"/>, <see cref="Hash32"/>) for one needle. Callers must check
/// <see cref="Avx2.IsSupported"/> and keep a fallback.
/// </summary>
/// <remarks>
/// Comparing one whole item per step costs a load, a compare, a mask extraction and a branch for every item. Instead four items
/// are tested at a time on their first limb alone (their first eight bytes): four loads, two unpacks and one lane permute gather
/// the four first limbs into one vector, one compare finds the candidates, and only a candidate is compared in full. For random
/// data (hashes, addresses, amounts) a first limb almost never matches by accident, so the full compare almost never runs.
/// A scan that meets more than <see cref="MaxFalseCandidates"/> candidates that are not matches (items sharing the needle's first
/// limb, such as values that are all multiples of 2^64) stops filtering and finishes with one full compare per item, so
/// structured or hostile data costs at most that many wasted compares over the unfiltered scan.
/// </remarks>
internal static class ItemScan32
{
    private const int ItemBytes = 32;

    /// <summary>Candidates that turn out not to match, tolerated before the first-limb filter is abandoned for the rest of the scan.</summary>
    private const int MaxFalseCandidates = 8;

    /// <summary>Bit j of the result is set when item j of the four items at <paramref name="block"/> has first limb equal to the needle's.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FirstLimbCandidates(ref byte block, Vector256<ulong> needleFirstLimb)
    {
        Vector256<ulong> a = Avx2.UnpackLow(Vector256.LoadUnsafe(ref block).AsUInt64(), Vector256.LoadUnsafe(ref block, 32).AsUInt64());
        Vector256<ulong> b = Avx2.UnpackLow(Vector256.LoadUnsafe(ref block, 64).AsUInt64(), Vector256.LoadUnsafe(ref block, 96).AsUInt64());

        // a = [item0.limb0, item1.limb0 | item0.limb2, item1.limb2], b likewise for items 2 and 3: the low 128-bit halves hold the
        // four first limbs in item order.
        Vector256<ulong> firstLimbs = Avx2.Permute2x128(a, b, 0x20);
        return Avx2.MoveMask(Avx2.CompareEqual(firstLimbs, needleFirstLimb).AsDouble());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ItemEquals(ref byte start, int index, Vector256<ulong> needle)
        => Vector256.LoadUnsafe(ref start, (nuint)index * ItemBytes).AsUInt64() == needle;

    /// <summary>Index of the first item equal to <paramref name="needle"/>, or -1.</summary>
    public static int IndexOf(ref byte start, int count, Vector256<ulong> needle)
    {
        Vector256<ulong> firstLimb = Avx2.Permute4x64(needle, 0);

        int falseCandidates = 0;
        int i = 0;
        for (; i <= count - 4 && falseCandidates <= MaxFalseCandidates; i += 4)
        {
            int candidates = FirstLimbCandidates(ref Unsafe.Add(ref start, (nint)i * ItemBytes), firstLimb);
            while (candidates != 0)
            {
                int index = i + BitOperations.TrailingZeroCount(candidates);
                if (ItemEquals(ref start, index, needle))
                    return index;

                falseCandidates++;
                candidates &= candidates - 1;
            }
        }

        for (; i < count; i++)
        {
            if (ItemEquals(ref start, i, needle))
                return i;
        }

        return -1;
    }

    /// <summary>Number of items equal to <paramref name="needle"/>.</summary>
    public static int CountEquals(ref byte start, int count, Vector256<ulong> needle)
    {
        Vector256<ulong> firstLimb = Avx2.Permute4x64(needle, 0);

        int matches = 0;
        int falseCandidates = 0;
        int i = 0;
        for (; i <= count - 4 && falseCandidates <= MaxFalseCandidates; i += 4)
        {
            int candidates = FirstLimbCandidates(ref Unsafe.Add(ref start, (nint)i * ItemBytes), firstLimb);
            while (candidates != 0)
            {
                if (ItemEquals(ref start, i + BitOperations.TrailingZeroCount(candidates), needle))
                    matches++;
                else
                    falseCandidates++;

                candidates &= candidates - 1;
            }
        }

        for (; i < count; i++)
        {
            if (ItemEquals(ref start, i, needle))
                matches++;
        }

        return matches;
    }

    /// <summary>
    /// Writes the indices of matching items, in ascending order, into <paramref name="destination"/> and returns how many were
    /// written. Stops as soon as the destination is full.
    /// </summary>
    public static int FindAll(ref byte start, int count, Vector256<ulong> needle, Span<int> destination)
    {
        Vector256<ulong> firstLimb = Avx2.Permute4x64(needle, 0);

        int written = 0;
        int falseCandidates = 0;
        int i = 0;
        for (; i <= count - 4 && falseCandidates <= MaxFalseCandidates; i += 4)
        {
            int candidates = FirstLimbCandidates(ref Unsafe.Add(ref start, (nint)i * ItemBytes), firstLimb);
            while (candidates != 0)
            {
                int index = i + BitOperations.TrailingZeroCount(candidates);
                if (ItemEquals(ref start, index, needle))
                {
                    destination[written++] = index;
                    if (written == destination.Length)
                        return written;
                }
                else
                {
                    falseCandidates++;
                }

                candidates &= candidates - 1;
            }
        }

        for (; i < count; i++)
        {
            if (ItemEquals(ref start, i, needle))
            {
                destination[written++] = i;
                if (written == destination.Length)
                    return written;
            }
        }

        return written;
    }
}
