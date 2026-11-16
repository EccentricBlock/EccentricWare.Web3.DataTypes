using System.Numerics;
using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.Utils;

/// <summary>
/// Base58 (Bitcoin/Solana alphabet) decoding into little-endian 64-bit limbs, ten digits per multiply.
/// </summary>
/// <remarks>
/// The value is held in 64-bit limbs (limb 0 least significant). Folding ten digits at a time costs one 64x64 multiply per
/// limb in use per ten digits (58^10 is the largest power of 58 below 2^64), instead of one 64-step byte multiply per digit.
/// This is the technique <see cref="ByteUtils.TryDecodeBase58ToUInt256BigEndian"/> uses with four fixed limbs; here the limb
/// count is a parameter so one routine serves 32-byte and 64-byte values.
/// </remarks>
internal static class Base58Decoder
{
    /// <summary>Digits folded per multiply: 58^10 (about 4.3e17) is the largest power of 58 below 2^64.</summary>
    private const int DigitsPerChunk = 10;

    /// <summary>Longest text that can decode to 64 bytes: 64 leading '1' (zero bytes) plus 88 digits (58^88 exceeds 2^512).</summary>
    private const int Max64Length = 64 + 88;

    /// <summary>
    /// Decodes canonical Base58 into 8 little-endian limbs (a 64-byte big-endian value). Canonical means the number of
    /// leading '1' characters equals the number of leading zero bytes. On failure <paramref name="limbs"/> is all zero.
    /// </summary>
    public static bool TryDecode64(ReadOnlySpan<byte> text, Span<ulong> limbs)
    {
        limbs.Clear();
        if ((uint)(text.Length - 1) >= Max64Length)
            return false;

        return TryDecodeCanonical(text, limbs, byteLength: 64);
    }

    /// <summary>Character overload. Characters above 0xFF are never part of the alphabet.</summary>
    public static bool TryDecode64(ReadOnlySpan<char> text, Span<ulong> limbs)
    {
        limbs.Clear();
        if ((uint)(text.Length - 1) >= Max64Length)
            return false;

        Span<byte> narrow = stackalloc byte[Max64Length];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c > 0xFF)
                return false;

            narrow[i] = (byte)c;
        }

        return TryDecodeCanonical(narrow.Slice(0, text.Length), limbs, byteLength: 64);
    }

    /// <summary>
    /// Decodes Base58 into 4 little-endian limbs (a 32-byte big-endian value) WITHOUT requiring canonical form: leading '1'
    /// characters are skipped, not counted. Fails on an invalid digit, empty input or a value of 2^256 or more.
    /// On failure <paramref name="limbs"/> is all zero. The input length is not capped; the scan stops at the first overflow.
    /// </summary>
    public static bool TryDecode32NonCanonical(ReadOnlySpan<byte> text, Span<ulong> limbs)
    {
        limbs.Clear();
        if (text.IsEmpty)
            return false;

        int leadingOnes = CountLeadingOnes(text);
        if (!TryAccumulate(text.Slice(leadingOnes), limbs, out _))
        {
            limbs.Clear();
            return false;
        }

        return true;
    }

    private static bool TryDecodeCanonical(ReadOnlySpan<byte> text, Span<ulong> limbs, int byteLength)
    {
        int leadingOnes = CountLeadingOnes(text);
        if (leadingOnes > byteLength)
            return false;

        if (!TryAccumulate(text.Slice(leadingOnes), limbs, out int used))
        {
            limbs.Clear();
            return false;
        }

        // The top used limb is non-zero (see TryAccumulate), so the significant byte count is exact.
        int significantBytes = used == 0 ? 0 : (used - 1) * 8 + (8 - BitOperations.LeadingZeroCount(limbs[used - 1]) / 8);
        if (byteLength - significantBytes != leadingOnes)
        {
            limbs.Clear();
            return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountLeadingOnes(ReadOnlySpan<byte> text)
    {
        int count = 0;
        while (count < text.Length && text[count] == (byte)'1')
            count++;

        return count;
    }

    /// <summary>
    /// Folds <paramref name="digits"/> (Base58 text without leading '1') into <paramref name="limbs"/>, which must start
    /// zeroed. Returns false on an invalid digit or when the value no longer fits the limbs. On success
    /// <paramref name="used"/> limbs are in use and the top one is non-zero: a carry limb is only appended when non-zero,
    /// and a multiply of a non-zero top limb can never leave it zero without a carry out.
    /// </summary>
    private static bool TryAccumulate(ReadOnlySpan<byte> digits, Span<ulong> limbs, out int used)
    {
        used = 0;
        ReadOnlySpan<byte> map = ByteUtils.Base58ReverseMap;

        // The first chunk takes the remainder so every later chunk is full.
        int chunkLength = digits.Length % DigitsPerChunk;
        if (chunkLength == 0)
            chunkLength = DigitsPerChunk;

        for (int index = 0; index < digits.Length; index += chunkLength, chunkLength = DigitsPerChunk)
        {
            ulong chunk = 0;
            ulong multiplier = 1;
            for (int k = 0; k < chunkLength; k++)
            {
                int digit = map[digits[index + k]];
                if (digit == 0xFF)
                    return false;

                chunk = chunk * 58 + (uint)digit;
                multiplier *= 58;
            }

            // limbs = limbs * multiplier + chunk. high <= multiplier - 1, so adding the 0/1 carry from the low half cannot
            // overflow.
            ulong carry = chunk;
            for (int j = 0; j < used; j++)
            {
                ulong high = Math.BigMul(limbs[j], multiplier, out ulong low);
                ulong sum = low + carry;
                limbs[j] = sum;
                carry = high + (sum < low ? 1UL : 0UL);
            }

            if (carry != 0)
            {
                if (used == limbs.Length)
                    return false;

                limbs[used++] = carry;
            }
        }

        return true;
    }

    /// <summary>Writes little-endian limbs as big-endian bytes (<c>destination.Length == limbs.Length * 8</c>).</summary>
    public static void WriteBigEndian(ReadOnlySpan<ulong> limbs, Span<byte> destination)
    {
        for (int j = 0; j < limbs.Length; j++)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(destination.Length - 8 * (j + 1), 8), limbs[j]);
    }
}
