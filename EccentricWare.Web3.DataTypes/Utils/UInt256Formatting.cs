using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EccentricWare.Web3.DataTypes.Utils;

/// <summary>Stack scratch for formatting a 256-bit value as ASCII (longest output: 78 decimal digits plus a sign).</summary>
[InlineArray(80)]
internal struct AsciiBuffer80
{
    private byte _element0;
}

/// <summary>Stack scratch for padding short byte sequences to 32 bytes (no localloc, so no stack-cookie prolog).</summary>
[InlineArray(32)]
internal struct Bytes32
{
    private byte _element0;
}

/// <summary>Stack scratch for the UTF-16 form of a formatted value.</summary>
[InlineArray(80)]
internal struct CharBuffer80
{
    private char _element0;
}

[InlineArray(5)]
internal struct Chunks5
{
    private ulong _element0;
}

/// <summary>
/// Allocation-free ASCII formatting of 256-bit values, shared by <see cref="uint256"/> and <see cref="int256"/>.
/// Callers copy or widen the returned ASCII into their destination (UTF-8 copy, vectorised widen to UTF-16, or a string).
/// </summary>
internal static class UInt256Formatting
{
    /// <summary>Longest decimal text: 78 digits for 2^256 - 1, 77 digits plus a sign for -2^255.</summary>
    public const int MaxDecimalLength = 78;

    /// <summary>Hex digits are written at offset 2 of the scratch so a "0x" prefix can overlay skipped zeros.</summary>
    private const int HexScratchLength = 66;

    private static ReadOnlySpan<byte> DigitPairs =>
        "00010203040506070809101112131415161718192021222324252627282930313233343536373839404142434445464748495051525354555657585960616263646566676869707172737475767778798081828384858687888990919293949596979899"u8;

    /// <summary>
    /// Formats the value as hex into <paramref name="scratch"/> (at least 66 bytes) and returns the used slice.
    /// Minimal form drops leading zero digits (keeping at least one); <paramref name="fullWidth"/> keeps all 64.
    /// The prefix, when requested, is always lowercase "0x".
    /// </summary>
    public static ReadOnlySpan<byte> FormatHex(Span<byte> scratch, ulong u0, ulong u1, ulong u2, ulong u3,
        bool prefix, bool uppercase, bool fullWidth)
    {
        // Limbs above the most significant non-zero one only produce digits that are skipped, so they are not encoded.
        int topLimb = fullWidth ? 3 : u3 != 0 ? 3 : u2 != 0 ? 2 : u1 != 0 ? 1 : 0;
        Span<byte> digits = scratch.Slice(2, 64);
        if (topLimb >= 3) HexCodec.Encode16(digits, u3, uppercase);
        if (topLimb >= 2) HexCodec.Encode16(digits.Slice(16), u2, uppercase);
        if (topLimb >= 1) HexCodec.Encode16(digits.Slice(32), u1, uppercase);
        HexCodec.Encode16(digits.Slice(48), u0, uppercase);

        int skip = 0;
        if (!fullWidth)
        {
            int leadingZeroBits = u3 != 0 ? BitOperations.LeadingZeroCount(u3)
                : u2 != 0 ? 64 + BitOperations.LeadingZeroCount(u2)
                : u1 != 0 ? 128 + BitOperations.LeadingZeroCount(u1)
                : u0 != 0 ? 192 + BitOperations.LeadingZeroCount(u0)
                : 255; // zero still prints one digit

            skip = Math.Min(leadingZeroBits >> 2, 63);
        }

        if (prefix)
        {
            // Overlay "0x" on the two bytes just before the first kept digit.
            scratch[skip] = (byte)'0';
            scratch[skip + 1] = (byte)'x';
            return scratch.Slice(skip, HexScratchLength - skip);
        }

        return scratch.Slice(2 + skip, 64 - skip);
    }

    /// <summary>
    /// Formats the magnitude as decimal into <paramref name="scratch"/> (at least 80 bytes), optionally with a leading '-',
    /// and returns the used slice (right-aligned in the scratch).
    /// </summary>
    public static ReadOnlySpan<byte> FormatDecimal(Span<byte> scratch, ulong u0, ulong u1, ulong u2, ulong u3, bool negative)
    {
        int end = scratch.Length;

        if ((u1 | u2 | u3) == 0 && u0 < UInt256Math.TenPow19)
        {
            // Common case: below 10^19 (one chunk); skip the chunk loop entirely.
            int count = CountDigits(u0);
            WriteDigits(scratch.Slice(end - count, count), u0);
            return Finish(scratch, end - count, negative);
        }

        // Peel off 19-digit chunks (10^19 is the largest power of ten in a ulong), least significant first.
        Chunks5 chunkStorage = default;
        Span<ulong> chunks = chunkStorage;
        int chunkCount = 0;
        int top = u3 != 0 ? 3 : u2 != 0 ? 2 : u1 != 0 ? 1 : 0;

        while (top > 0 || u0 >= UInt256Math.TenPow19)
        {
            ulong remainder = 0;
            if (top >= 3) u3 = UInt256Math.DivRem64(u3, UInt256Math.TenPow19, out remainder);
            if (top >= 2) u2 = UInt256Math.DivRem128ByTenPow19(remainder, u2, out remainder);
            if (top >= 1) u1 = UInt256Math.DivRem128ByTenPow19(remainder, u1, out remainder);
            u0 = UInt256Math.DivRem128ByTenPow19(remainder, u0, out remainder);

            chunks[chunkCount++] = remainder;
            top = u3 != 0 ? 3 : u2 != 0 ? 2 : u1 != 0 ? 1 : 0;
        }

        chunks[chunkCount++] = u0; // most significant chunk: no zero padding

        int position = end;
        for (int i = 0; i < chunkCount - 1; i++)
        {
            position -= 19;
            WriteChunk19(scratch.Slice(position, 19), chunks[i]);
        }

        ulong most = chunks[chunkCount - 1];
        int mostDigits = CountDigits(most);
        position -= mostDigits;
        WriteDigits(scratch.Slice(position, mostDigits), most);

        return Finish(scratch, position, negative);
    }

    /// <summary>Creates a string from ASCII bytes: vectorised widen into stack chars, then one allocation and copy.</summary>
    public static string CreateString(ReadOnlySpan<byte> ascii)
    {
        CharBuffer80 storage = default;
        Span<char> chars = storage;
        System.Text.Ascii.ToUtf16(ascii, chars, out int written);
        return new string(chars.Slice(0, written));
    }

    private static ReadOnlySpan<byte> Finish(Span<byte> scratch, int position, bool negative)
    {
        if (negative)
            scratch[--position] = (byte)'-';

        return scratch.Slice(position);
    }

    /// <summary>Number of decimal digits in <paramref name="value"/> (1 for zero).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CountDigits(ulong value)
    {
        // floor(log2(v) * log10(2)) equals floor(log10(v)) or is one less; one comparison settles which.
        int approximate = (BitOperations.Log2(value | 1) * 1233) >> 12;
        return approximate + 1 + (value >= ByteUtils.Pow10U64(approximate + 1) ? 1 : 0);
    }

    /// <summary>
    /// Writes <paramref name="value"/> (below 10^19) right-aligned into <paramref name="destination"/>, whose length must be
    /// exactly the digit count. Writes only the digits it needs (no padding scratch, no copy).
    /// </summary>
    private static void WriteDigits(Span<byte> destination, ulong value)
    {
        int end = destination.Length;

        while (value >= 100_000_000UL)
        {
            ulong quotient = value / 100_000_000UL;
            Write8(destination.Slice(end - 8, 8), (uint)(value - quotient * 100_000_000UL));
            end -= 8;
            value = quotient;
        }

        uint rest = (uint)value;
        while (rest >= 100)
        {
            uint quotient = rest / 100;
            WritePair(destination.Slice(end - 2, 2), rest - quotient * 100);
            end -= 2;
            rest = quotient;
        }

        if (rest >= 10)
            WritePair(destination.Slice(end - 2, 2), rest);
        else
            destination[end - 1] = (byte)('0' + rest);
    }

    /// <summary>Writes exactly 19 digits (value &lt; 10^19), zero-padded on the left.</summary>
    private static void WriteChunk19(Span<byte> destination, ulong value)
    {
        ulong high11 = value / 100_000_000UL;
        uint low8 = (uint)(value - high11 * 100_000_000UL);
        ulong top3 = high11 / 100_000_000UL;
        uint mid8 = (uint)(high11 - top3 * 100_000_000UL);

        Write8(destination.Slice(11, 8), low8);
        Write8(destination.Slice(3, 8), mid8);

        uint t = (uint)top3; // below 1000
        uint hundreds = t / 100;
        destination[0] = (byte)('0' + hundreds);
        WritePair(destination.Slice(1, 2), t - hundreds * 100);
    }

    private static void Write8(Span<byte> destination, uint value)
    {
        uint high = value / 10_000;
        uint low = value - high * 10_000;
        Write4(destination, high);
        Write4(destination.Slice(4), low);
    }

    private static void Write4(Span<byte> destination, uint value)
    {
        uint high = value / 100;
        WritePair(destination, high);
        WritePair(destination.Slice(2), value - high * 100);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WritePair(Span<byte> destination, uint value)
    {
        ReadOnlySpan<byte> pairs = DigitPairs;
        int index = (int)(value * 2);
        destination[0] = pairs[index];
        destination[1] = pairs[index + 1];
    }
}
