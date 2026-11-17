using System.Runtime.CompilerServices;
using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>
/// The single source of truth for the canonical RLP prefix rules on the writing side (spec 7.1), as
/// <see cref="RLPHeaderReader"/> is for reading. <see cref="RLPEncoder"/> and <see cref="RLPWriter"/> both size and emit
/// every item through these members; neither re-implements a prefix rule. Sizes are returned as <see cref="long"/> so a
/// caller can test for <see cref="int.MaxValue"/> overflow without wrapping. No member validates capacity: callers
/// prove the output region is large enough first.
/// </summary>
internal static class RLPHeaderWriter
{
    private const int MaxShortPayload = 55;
    private const byte StringBase = 0x80;
    private const byte ListBase = 0xc0;

    /// <summary>
    /// Header bytes (prefix plus length bytes) of a non-literal item with <paramref name="payloadLength"/> payload
    /// bytes: 1 up to 55, otherwise 1 plus the minimal big-endian byte count of the length (2..5 for an int). Requires
    /// <paramref name="payloadLength"/> to be non-negative.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int HeaderLength(int payloadLength) =>
        payloadLength <= MaxShortPayload ? 1 : 1 + ByteUtils.GetByteCount((uint)payloadLength);

    /// <summary>
    /// Total encoded size of a byte string: 1 for a single byte below <c>0x80</c> (the literal), otherwise header plus
    /// payload. Reads <c>payload[0]</c> only when the payload has exactly one byte.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ByteStringSize(ReadOnlySpan<byte> payload)
    {
        int length = payload.Length;
        if (length == 1 && payload[0] < 0x80)
        {
            return 1;
        }

        return (long)length + HeaderLength(length);
    }

    /// <summary>Total encoded size of a list whose already encoded children total <paramref name="payloadLength"/> bytes (non-negative).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ListSize(int payloadLength) => (long)payloadLength + HeaderLength(payloadLength);

    /// <summary>Encoded size of an unsigned integer: 1 for zero (<c>80</c>) and for 1..127 (the literal), otherwise 1 plus its byte count (2..9).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int UInt64Size(ulong value) => value < 0x80 ? 1 : 1 + ByteUtils.GetByteCount(value);

    /// <summary>
    /// Writes the header of a string or list with <paramref name="payloadLength"/> payload bytes at the start of
    /// <paramref name="destination"/> and returns the header length. <paramref name="listOrString"/> is the short-form
    /// base: <c>0x80</c> for strings, <c>0xc0</c> for lists. Long form: prefix <c>base + 55 + lengthOfLength</c> then
    /// the length big-endian, 1..4 bytes because the length is an int. No temporary buffer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteHeader(Span<byte> destination, int payloadLength, byte listOrString)
    {
        if (payloadLength <= MaxShortPayload)
        {
            destination[0] = (byte)(listOrString + payloadLength);
            return 1;
        }

        int lengthOfLength = ByteUtils.GetByteCount((uint)payloadLength);
        destination[0] = (byte)(listOrString + MaxShortPayload + lengthOfLength);
        for (int i = lengthOfLength; i >= 1; i--)
        {
            destination[i] = unchecked((byte)payloadLength); // keeps the low byte; the loop shifts the rest down
            payloadLength >>= 8;
        }

        return 1 + lengthOfLength;
    }

    /// <summary>Writes the header of a list; see <see cref="WriteHeader"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteListHeader(Span<byte> destination, int payloadLength) => WriteHeader(destination, payloadLength, ListBase);

    /// <summary>
    /// Writes one byte string into <paramref name="region"/>, which must be exactly
    /// <see cref="ByteStringSize"/> bytes long and must not overlap <paramref name="payload"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteByteString(Span<byte> region, ReadOnlySpan<byte> payload)
    {
        int length = payload.Length;
        if (length == 1 && payload[0] < 0x80)
        {
            region[0] = payload[0];
            return;
        }

        int header = WriteHeader(region, length, StringBase);
        payload.CopyTo(region.Slice(header));
    }

    /// <summary>
    /// Writes one unsigned integer into <paramref name="region"/>, which must be at least <see cref="UInt64Size"/>
    /// bytes long: zero is <c>80</c>, 1..127 the literal byte, otherwise <c>80 + n</c> then the n magnitude bytes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt64(Span<byte> region, ulong value)
    {
        if (value < 0x80)
        {
            region[0] = value == 0 ? StringBase : (byte)value;
            return;
        }

        int byteCount = ByteUtils.GetByteCount(value);
        region[0] = (byte)(StringBase + byteCount);
        for (int i = byteCount; i >= 1; i--)
        {
            region[i] = unchecked((byte)value); // keeps the low byte; the loop shifts the rest down
            value >>= 8;
        }
    }
}
