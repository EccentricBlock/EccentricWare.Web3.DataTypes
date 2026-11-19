using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// One 32-byte ABI word read as four big-endian limbs (<see cref="W0"/> is the most significant) plus the strict scalar rules of
/// spec 2.4. Pure functions over four <see cref="ulong"/>s: no allocation, no BigInteger, no exception for any bit pattern.
/// </summary>
internal readonly struct ABIWord
{
    internal ABIWord(ulong w0, ulong w1, ulong w2, ulong w3)
    {
        W0 = w0;
        W1 = w1;
        W2 = w2;
        W3 = w3;
    }

    /// <summary>Bytes 0..7 (most significant).</summary>
    internal readonly ulong W0;

    /// <summary>Bytes 8..15.</summary>
    internal readonly ulong W1;

    /// <summary>Bytes 16..23.</summary>
    internal readonly ulong W2;

    /// <summary>Bytes 24..31 (least significant).</summary>
    internal readonly ulong W3;

    /// <summary>
    /// Reads the 32 bytes at <paramref name="offset"/>. False (and a default word) when they are not fully contained:
    /// containment is checked by subtraction, so a negative or huge offset never throws.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryLoad(ReadOnlySpan<byte> input, int offset, out ABIWord word)
    {
        if (offset < 0 || offset > input.Length || input.Length - offset < ABIWordSize)
        {
            word = default;
            return false;
        }

        ReadOnlySpan<byte> bytes = input.Slice(offset, ABIWordSize);
        word = new ABIWord(
            BinaryPrimitives.ReadUInt64BigEndian(bytes),
            BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(8)),
            BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(16)),
            BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(24)));
        return true;
    }

    /// <summary>Size of an ABI word in bytes.</summary>
    internal const int ABIWordSize = 32;

    /// <summary>
    /// The word as an unsigned 256-bit number narrowed to a non-negative <see cref="int"/>: the high three limbs must be zero and
    /// the low limb at most <see cref="int.MaxValue"/>. This is the one place a pointer, length or count word becomes a native
    /// index (spec 2.4); it never truncates.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetInt32(out int value)
    {
        if ((W0 | W1 | W2) != 0 || W3 > int.MaxValue)
        {
            value = 0;
            return false;
        }

        value = unchecked((int)W3);
        return true;
    }

    /// <summary>
    /// Checks a primitive word against the strict rules of its node. <see cref="ABIStatus.InvalidScalar"/>: bad width, sign
    /// extension, boolean or address prefix. <see cref="ABIStatus.InvalidPadding"/>: non-zero right padding of
    /// <c>bytes&lt;M&gt;</c> or of a function pointer. Composite kinds are a caller bug and report <see cref="ABIStatus.InvalidScalar"/>.
    /// </summary>
    internal ABIStatus CheckScalar(ABITypeKind kind, int width)
    {
        switch (kind)
        {
            case ABITypeKind.UInt:
            case ABITypeKind.UFixed:
                return HighBitsAreZero(ABIWordSize * 8 - width) ? ABIStatus.Success : ABIStatus.InvalidScalar;

            case ABITypeKind.Int:
            case ABITypeKind.Fixed:
                return IsSignExtended(width) ? ABIStatus.Success : ABIStatus.InvalidScalar;

            case ABITypeKind.Address:
                // 12 leading zero bytes: all of W0 and the high half of W1.
                return W0 == 0 && (W1 >> 32) == 0 ? ABIStatus.Success : ABIStatus.InvalidScalar;

            case ABITypeKind.Bool:
                return (W0 | W1 | W2) == 0 && W3 <= 1 ? ABIStatus.Success : ABIStatus.InvalidScalar;

            case ABITypeKind.FixedBytes:
                return LowBitsAreZero((ABIWordSize - width) * 8) ? ABIStatus.Success : ABIStatus.InvalidPadding;

            case ABITypeKind.Function:
                // 20-byte address + 4-byte selector, then eight zero bytes.
                return W3 == 0 ? ABIStatus.Success : ABIStatus.InvalidPadding;

            default:
                return ABIStatus.InvalidScalar;
        }
    }

    /// <summary>True when the top <paramref name="bits"/> bits (0 to 256) of the word are all zero.</summary>
    internal bool HighBitsAreZero(int bits) => HighBitsAreZero(W0, W1, W2, W3, bits);

    /// <summary>
    /// True when the word is the sign extension of a <paramref name="width"/>-bit two's-complement value: bits 255 down to
    /// <c>width</c> all equal bit <c>width - 1</c>. For width 256 there are no extension bits, so every word passes.
    /// </summary>
    internal bool IsSignExtended(int width)
    {
        int extension = ABIWordSize * 8 - width; // number of bits above the value's own bits
        ulong signLimb = (extension >> 6) switch { 0 => W0, 1 => W1, 2 => W2, _ => W3 };
        bool negative = ((signLimb >> (63 - (extension & 63))) & 1) != 0; // bit width - 1, counted from bit 255 downwards
        return negative
            ? HighBitsAreZero(~W0, ~W1, ~W2, ~W3, extension)
            : HighBitsAreZero(W0, W1, W2, W3, extension);
    }

    /// <summary>True when the lowest <paramref name="bits"/> bits (0 to 256) of the word are all zero.</summary>
    internal bool LowBitsAreZero(int bits) =>
        LimbSuffixIsZero(W3, bits)
        && LimbSuffixIsZero(W2, bits - 64)
        && LimbSuffixIsZero(W1, bits - 128)
        && LimbSuffixIsZero(W0, bits - 192);

    private static bool LimbSuffixIsZero(ulong limb, int bits)
    {
        if (bits <= 0)
        {
            return true;
        }

        return bits >= 64 ? limb == 0 : (limb & ((1UL << bits) - 1)) == 0;
    }

    private static bool HighBitsAreZero(ulong w0, ulong w1, ulong w2, ulong w3, int bits)
    {
        // Limb i covers the bits [64 * i, 64 * (i + 1)) counted from the top; take as many of its high bits as remain.
        return LimbPrefixIsZero(w0, bits)
            && LimbPrefixIsZero(w1, bits - 64)
            && LimbPrefixIsZero(w2, bits - 128)
            && LimbPrefixIsZero(w3, bits - 192);
    }

    private static bool LimbPrefixIsZero(ulong limb, int bits)
    {
        if (bits <= 0)
        {
            return true;
        }

        return bits >= 64 ? limb == 0 : (limb >> (64 - bits)) == 0;
    }

    /// <summary>
    /// Index of the first non-zero byte of <paramref name="padding"/>, or -1 when all are zero. The scalar reference of the padding check:
    /// the validator uses it directly below <see cref="ABIWordRun"/>'s vector threshold (a vector check does not pay for 1 to 7 bytes) and
    /// to locate the offset after the vector check has found a non-zero byte.
    /// </summary>
    internal static int FirstNonZero(ReadOnlySpan<byte> padding)
    {
        for (int i = 0; i < padding.Length; i++)
        {
            if (padding[i] != 0)
            {
                return i;
            }
        }

        return -1;
    }
}
