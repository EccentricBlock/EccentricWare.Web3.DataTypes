namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>
/// Canonical RLP sizing and atomic single-item writes into caller storage (spec 7.1, 7.2). Every member is
/// allocation-free and never throws for any argument; failures are <see cref="RLPStatus"/> values. Use
/// <see cref="RLPWriter"/> to compose nested lists.
/// </summary>
/// <remarks>
/// Write order of checks, all before the first byte is written: argument and integer minimality, exact output length
/// (<see cref="RLPStatus.LengthOverflow"/>), destination capacity (<see cref="RLPStatus.DestinationTooSmall"/>), then
/// overlap between the source and the region that would be written (<see cref="RLPStatus.OverlappingBuffers"/>).
/// Capacity precedes overlap because the output region is undefined while the destination is too small. On any
/// failure <c>written</c> is 0 and the destination is untouched. In-place encoding (a destination that shares a byte
/// with the source region) is not supported.
/// </remarks>
public static class RLPEncoder
{
    /// <summary>
    /// Computes the encoded size of a byte string: 1 for a single byte below <c>0x80</c>, <c>1 + n</c> for
    /// <c>n &lt;= 55</c>, otherwise <c>1 + minimalBigEndianByteCount(n) + n</c>. An empty payload is a valid empty string
    /// (size 1).
    /// </summary>
    /// <param name="payload">The string content.</param>
    /// <param name="totalLength">The size in bytes; 0 on failure.</param>
    /// <returns><see cref="RLPStatus.LengthOverflow"/> when the total does not fit an <see cref="int"/>.</returns>
    public static RLPStatus TryGetByteStringLength(ReadOnlySpan<byte> payload, out int totalLength)
    {
        long size = RLPHeaderWriter.ByteStringSize(payload);
        if (size > int.MaxValue)
        {
            totalLength = 0;
            return RLPStatus.LengthOverflow;
        }

        totalLength = (int)size;
        return RLPStatus.Success;
    }

    /// <summary>
    /// Computes the encoded size of an unsigned integer as <see cref="TryWriteUInt64"/> writes it: 1 for zero (<c>80</c>)
    /// and for 1..127 (the literal byte), otherwise 1 plus the minimal big-endian byte count (2..9).
    /// </summary>
    /// <param name="value">The integer.</param>
    /// <returns>The exact number of bytes <see cref="TryWriteUInt64"/> and <see cref="RLPWriter.TryWriteUInt64"/> emit, 1..9.</returns>
    public static int GetUInt64EncodedLength(ulong value) => RLPHeaderWriter.UInt64Size(value);

    /// <summary>
    /// Computes the encoded size of an unsigned integer given as a big-endian magnitude, with the rules of
    /// <see cref="TryWriteUnsignedMagnitude"/>: an empty magnitude is zero (size 1), a non-empty magnitude must not
    /// start with <c>00</c>, a one-byte magnitude below <c>0x80</c> is the literal byte (size 1).
    /// </summary>
    /// <param name="minimalBigEndianMagnitude">The magnitude.</param>
    /// <param name="totalLength">The size in bytes; 0 on failure.</param>
    /// <returns><see cref="RLPStatus.NonCanonicalInteger"/> for a leading <c>00</c>, <see cref="RLPStatus.LengthOverflow"/> when the total does not fit an <see cref="int"/>.</returns>
    public static RLPStatus TryGetUnsignedMagnitudeLength(ReadOnlySpan<byte> minimalBigEndianMagnitude, out int totalLength)
    {
        if (minimalBigEndianMagnitude.Length != 0 && minimalBigEndianMagnitude[0] == 0)
        {
            totalLength = 0;
            return RLPStatus.NonCanonicalInteger;
        }

        return TryGetByteStringLength(minimalBigEndianMagnitude, out totalLength);
    }

    /// <summary>
    /// Computes the encoded size of a list whose already encoded children total
    /// <paramref name="encodedChildrenLength"/> bytes: <c>1 + n</c> for <c>n &lt;= 55</c>, otherwise
    /// <c>1 + minimalBigEndianByteCount(n) + n</c>.
    /// </summary>
    /// <param name="encodedChildrenLength">Total bytes of the encoded children; must not be negative.</param>
    /// <param name="totalLength">The size in bytes; 0 on failure.</param>
    /// <returns><see cref="RLPStatus.InvalidArgument"/> for a negative length, <see cref="RLPStatus.LengthOverflow"/> when the total does not fit an <see cref="int"/>.</returns>
    public static RLPStatus TryGetListLength(int encodedChildrenLength, out int totalLength)
    {
        totalLength = 0;
        if (encodedChildrenLength < 0)
        {
            return RLPStatus.InvalidArgument;
        }

        long size = RLPHeaderWriter.ListSize(encodedChildrenLength);
        if (size > int.MaxValue)
        {
            return RLPStatus.LengthOverflow;
        }

        totalLength = (int)size;
        return RLPStatus.Success;
    }

    /// <summary>
    /// Writes <paramref name="payload"/> as one canonical RLP byte string at the start of
    /// <paramref name="destination"/>. The empty payload writes <c>80</c>; a single byte below <c>0x80</c> writes
    /// itself.
    /// </summary>
    /// <param name="payload">The string content; must not overlap the output region.</param>
    /// <param name="destination">Caller storage.</param>
    /// <param name="written">The exact number of bytes written; 0 on failure.</param>
    public static RLPStatus TryWriteByteString(ReadOnlySpan<byte> payload, Span<byte> destination, out int written)
    {
        written = 0;
        long size = RLPHeaderWriter.ByteStringSize(payload);
        if (size > int.MaxValue)
        {
            return RLPStatus.LengthOverflow;
        }

        int total = (int)size;
        if ((uint)total > (uint)destination.Length)
        {
            return RLPStatus.DestinationTooSmall;
        }

        Span<byte> region = destination.Slice(0, total);
        if (payload.Overlaps(region))
        {
            return RLPStatus.OverlappingBuffers;
        }

        RLPHeaderWriter.WriteByteString(region, payload);
        written = total;
        return RLPStatus.Success;
    }

    /// <summary>
    /// Writes <paramref name="value"/> as an Ethereum unsigned integer: zero is <c>80</c>, 1..127 the literal byte,
    /// otherwise the shortest big-endian magnitude as a string (<c>ulong.MaxValue</c> is <c>88 ff*8</c>). Reads no
    /// buffer, so it cannot overlap anything.
    /// </summary>
    /// <param name="value">The integer.</param>
    /// <param name="destination">Caller storage.</param>
    /// <param name="written">The exact number of bytes written (1..9); 0 on failure.</param>
    public static RLPStatus TryWriteUInt64(ulong value, Span<byte> destination, out int written)
    {
        int total = RLPHeaderWriter.UInt64Size(value);
        if ((uint)total > (uint)destination.Length)
        {
            written = 0;
            return RLPStatus.DestinationTooSmall;
        }

        RLPHeaderWriter.WriteUInt64(destination, value);
        written = total;
        return RLPStatus.Success;
    }

    /// <summary>
    /// Writes an unsigned integer given as a big-endian magnitude of any length. An empty magnitude is zero
    /// (<c>80</c>). A non-empty magnitude must not start with <c>00</c>: it is rejected with
    /// <see cref="RLPStatus.NonCanonicalInteger"/> and never normalised. A one-byte magnitude below <c>0x80</c> is
    /// written as the literal byte; everything else as a byte string of the magnitude.
    /// </summary>
    /// <param name="minimalBigEndianMagnitude">The magnitude; must not overlap the output region.</param>
    /// <param name="destination">Caller storage.</param>
    /// <param name="written">The exact number of bytes written; 0 on failure.</param>
    public static RLPStatus TryWriteUnsignedMagnitude(ReadOnlySpan<byte> minimalBigEndianMagnitude, Span<byte> destination, out int written)
    {
        if (minimalBigEndianMagnitude.Length != 0 && minimalBigEndianMagnitude[0] == 0)
        {
            written = 0;
            return RLPStatus.NonCanonicalInteger;
        }

        return TryWriteByteString(minimalBigEndianMagnitude, destination, out written);
    }
}
