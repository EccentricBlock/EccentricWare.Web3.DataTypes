using System.Runtime.Intrinsics;

namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// Validation of a run of adjacent 32-byte words that all have the same elementary type (the elements of a static array), and the bounded
/// padding scan of a dynamic <c>bytes</c>/<c>string</c> tail. Every strict rule of <see cref="ABIWord.CheckScalar"/> for these types is "these
/// byte positions are zero" (uintM, address, bool with bit 0 of its last byte free, bytesM, function) or "no rule" (uint256, int256, bytes32), so a run is accepted by one
/// masked OR-reduction. The scalar reference (<see cref="FirstRejected"/>, built on <see cref="ABIWord.CheckScalar"/>) defines the answer: a
/// rejected run is always re-scanned by it, so statuses and offsets cannot differ, and the validator charges the same work units.
/// </summary>
internal static class ABIWordRun
{
    private const int Word = ABIWord.ABIWordSize;

    /// <summary>
    /// Arrays shorter than this stay on the per-element loop of the validator: a run has a fixed set-up cost that two elements cannot repay
    /// (measured: break-even at two elements, a gain from three).
    /// </summary>
    internal const int MinimumRun = 3;

    // [00 x 32][FF x 32]: the 32 bytes that start at S are (32 - S) zero bytes followed by S bytes of 0xFF.
    private static ReadOnlySpan<byte> SuffixTable =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    ];

    // [FF x 32][00 x 32]: the 32 bytes that start at (32 - P) are P bytes of 0xFF followed by (32 - P) zero bytes.
    private static ReadOnlySpan<byte> PrefixTable =>
    [
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ];

    // bool: 31 leading zero bytes, and the last byte may be 0 or 1 (bit 0 is free).
    private static ReadOnlySpan<byte> BoolMask =>
    [
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE,
    ];

    /// <summary>True when no word of this type can be rejected (uint256, int256, bytes32 and their fixed-point kin): the whole run is accepted without a scan.</summary>
    internal static bool AcceptsEveryWord(ABITypeKind kind, int width) => kind switch
    {
        ABITypeKind.UInt or ABITypeKind.UFixed or ABITypeKind.Int or ABITypeKind.Fixed => width == 256,
        ABITypeKind.FixedBytes => width == 32,
        _ => false,
    };

    /// <summary>
    /// The 32-byte "these bytes must be zero" mask of a word type (a word is valid exactly when <c>word AND mask</c> is zero), or an empty span
    /// when the rule is not a plain mask (a narrow signed integer: the extension bytes depend on the sign) or the schema width is not byte-aligned.
    /// </summary>
    internal static ReadOnlySpan<byte> Mask(ABITypeKind kind, int width)
    {
        switch (kind)
        {
            case ABITypeKind.UInt:
            case ABITypeKind.UFixed:
                // width/8 value bytes at the end, the 32 - width/8 bytes before them must be zero.
                return width >= 8 && width <= 256 && (width & 7) == 0 ? PrefixTable.Slice(width / 8, Word) : default;
            case ABITypeKind.Address:
                return PrefixTable.Slice(Word - 12, Word);
            case ABITypeKind.Bool:
                return BoolMask;
            case ABITypeKind.FixedBytes:
                return width >= 1 && width <= 32 ? SuffixTable.Slice(Word - width, Word) : default;
            case ABITypeKind.Function:
                return SuffixTable.Slice(8, Word);
            default:
                return default;
        }
    }

    /// <summary>Scalar reference: the index of the first word that <see cref="ABIWord.CheckScalar"/> rejects (and its status), or -1.</summary>
    internal static int FirstRejected(ReadOnlySpan<byte> words, ABITypeKind kind, int width, out ABIStatus status)
    {
        status = ABIStatus.Success;
        for (int offset = 0; offset <= words.Length - Word; offset += Word)
        {
            // Cannot fail: a whole word starts at offset.
            ABIWord.TryLoad(words, offset, out ABIWord word);

            ABIStatus checkedWord = word.CheckScalar(kind, width);
            if (checkedWord != ABIStatus.Success)
            {
                status = checkedWord;
                return offset / Word;
            }
        }

        return -1;
    }

    /// <summary>
    /// The index of the first word of the run that <see cref="ABIWord.CheckScalar"/> rejects, with its status, or -1 when every word is
    /// accepted: the fast accept first, the scalar reference only to locate a failure (so the answer is the reference's, byte for byte).
    /// Types without a mask (narrow signed integers) go straight to the reference, once. A trailing partial word is reported as <see cref="ABIStatus.Truncated"/>
    /// (fail closed; the validator slices exactly <c>fit * 32</c> bytes).
    /// </summary>
    internal static int FindFirstRejected(ReadOnlySpan<byte> words, ABITypeKind kind, int width, out ABIStatus status)
    {
        status = ABIStatus.Success;
        if ((words.Length & (Word - 1)) != 0)
        {
            status = ABIStatus.Truncated; // fail closed: a trailing partial word cannot be vouched for (the validator never passes one)
            return words.Length / Word;
        }

        if (words.IsEmpty || AcceptsEveryWord(kind, width))
        {
            return -1;
        }

        ReadOnlySpan<byte> mask = Mask(kind, width);
        return !mask.IsEmpty && AllMasked(words, mask) ? -1 : FirstRejected(words, kind, width, out status);
    }

    /// <summary>The widest accelerated kernel; false (not "rejected") when no vector hardware is available, so the caller uses the scalar reference.</summary>
    private static bool AllMasked(ReadOnlySpan<byte> words, ReadOnlySpan<byte> mask)
    {
        if (Vector256.IsHardwareAccelerated)
        {
            return AllMasked256(words, mask);
        }

        return Vector128.IsHardwareAccelerated && AllMasked128(words, mask);
    }

    // Every load is bounds-checked: Vector.Create on a slice of exactly one 16- or 32-byte block, so an offset mistake throws instead of reading
    // outside the input. Both kernels return false before any load unless mask.Length >= 32 and words.Length is a multiple of 32, and the loop
    // conditions keep every slice inside words, so the checks never fail. The unchecked form (MemoryMarshal.GetReference + LoadUnsafe) was
    // about as fast on 32-element arrays and 4 to 10 percent faster on 4,096-element static arrays (pinned A/B); it was removed by the owner's decision of
    // 2026-11-19 (docs/abi-design-notes.md, rows 66 and 85), so the codec has no unchecked memory access. ABIWordRunGuardPageTests still
    // drive both kernels against a guard page.

    /// <summary>True when <c>word AND mask</c> is zero for every 32-byte word of <paramref name="words"/>. Reads only inside <paramref name="words"/> and the 32-byte mask.</summary>
    internal static bool AllMasked128(ReadOnlySpan<byte> words, ReadOnlySpan<byte> mask)
    {
        if (mask.Length < Word || (words.Length & (Word - 1)) != 0)
        {
            return false;
        }

        Vector128<byte> mask0 = Vector128.Create(mask.Slice(0, 16));
        Vector128<byte> mask1 = Vector128.Create(mask.Slice(16, 16));
        Vector128<byte> acc0 = Vector128<byte>.Zero;
        Vector128<byte> acc1 = Vector128<byte>.Zero;
        for (int i = 0; i < words.Length; i += Word)
        {
            ReadOnlySpan<byte> word = words.Slice(i, Word);
            acc0 |= Vector128.Create(word.Slice(0, 16)) & mask0;
            acc1 |= Vector128.Create(word.Slice(16, 16)) & mask1;
        }

        return (acc0 | acc1) == Vector128<byte>.Zero;
    }

    /// <summary>As <see cref="AllMasked128"/>, one 32-byte load per word and four words per iteration.</summary>
    internal static bool AllMasked256(ReadOnlySpan<byte> words, ReadOnlySpan<byte> mask)
    {
        if (mask.Length < Word || (words.Length & (Word - 1)) != 0)
        {
            return false;
        }

        Vector256<byte> wordMask = Vector256.Create(mask.Slice(0, Word));
        Vector256<byte> acc0 = Vector256<byte>.Zero;
        Vector256<byte> acc1 = Vector256<byte>.Zero;
        int i = 0;
        for (; i <= words.Length - 4 * Word; i += 4 * Word)
        {
            ReadOnlySpan<byte> block = words.Slice(i, 4 * Word);
            acc0 |= (Vector256.Create(block.Slice(0, Word)) & wordMask) | (Vector256.Create(block.Slice(Word, Word)) & wordMask);
            acc1 |= (Vector256.Create(block.Slice(2 * Word, Word)) & wordMask) | (Vector256.Create(block.Slice(3 * Word, Word)) & wordMask);
        }

        for (; i < words.Length; i += Word)
        {
            acc0 |= Vector256.Create(words.Slice(i, Word)) & wordMask;
        }

        return (acc0 | acc1) == Vector256<byte>.Zero;
    }

    // Padding of up to 7 bytes stays on the scalar loop (measured: no gain from the vector check below about 8 bytes, a loss at 1 to 3). The
    // loop sits behind this method rather than in the validator: with the loop inlined into ValidateBytes the 1 to 7 byte cases measured
    // 2 to 10 percent slower than the original, behind the call they are level with it.
    private const int PaddingVectorMinimum = 8;

    /// <summary>
    /// Offset, relative to <paramref name="paddingStart"/>, of the first non-zero byte of the <paramref name="padding"/> bytes that end the
    /// last data word of a dynamic <c>bytes</c>/<c>string</c> value, or -1 when all are zero. The padding is the last bytes of a whole 32-byte
    /// word that lies inside <paramref name="input"/>, so it is checked with two masked 16-byte loads of that word (bounds-checked, on slices of exactly that word); a failure (or any
    /// precondition that does not hold) is located by the scalar loop.
    /// </summary>
    internal static int FirstNonZeroPadding(ReadOnlySpan<byte> input, int paddingStart, int padding)
    {
        if (Vector128.IsHardwareAccelerated && padding >= PaddingVectorMinimum && padding < Word)
        {
            // unchecked: a negative or wrapped start must fail the unsigned range test below, not throw in a checked-arithmetic build.
            int wordStart = unchecked(paddingStart + padding - Word);
            if (input.Length >= Word && unchecked((uint)wordStart) <= (uint)(input.Length - Word))
            {
                // Bounds-checked loads on exact slices: the range test above makes the slicing checks redundant, and measured within noise of the
                // unchecked form (docs/abi-design-notes.md, row 66), so no unsafe load is needed here.
                ReadOnlySpan<byte> window = input.Slice(wordStart, Word);
                ReadOnlySpan<byte> table = SuffixTable.Slice(padding, Word);
                Vector128<byte> low = Vector128.Create(window) & Vector128.Create(table);
                Vector128<byte> high = Vector128.Create(window.Slice(16)) & Vector128.Create(table.Slice(16));
                if ((low | high) == Vector128<byte>.Zero)
                {
                    return -1;
                }
            }
        }

        return ABIWord.FirstNonZero(input.Slice(paddingStart, padding));
    }
}
