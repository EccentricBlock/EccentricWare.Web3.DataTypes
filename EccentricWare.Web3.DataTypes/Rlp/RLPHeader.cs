using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>Outcome of reading one item header; the hot path reports only which rule failed, never an offset.</summary>
internal enum RLPHeaderStatus : byte
{
    /// <summary>A complete, canonical header whose payload lies inside the container.</summary>
    Valid = 0,

    /// <summary>Length bytes or payload extend past the container end (truncated at the root, a boundary violation when nested).</summary>
    Incomplete,

    /// <summary>A long-form length with a leading zero byte or a value below 56. Offset: the first length byte.</summary>
    NonCanonicalLength,

    /// <summary><c>81 xx</c> with <c>xx</c> below <c>0x80</c>. Offset: the <c>81</c> prefix.</summary>
    NonCanonicalSingleByte,

    /// <summary>A long-form length above <see cref="int.MaxValue"/>. Offset: the first length byte.</summary>
    LengthOverflow,
}

/// <summary>
/// A parsed item header. For a literal byte <c>00..7f</c> the payload is the prefix byte itself
/// (<c>PayloadStart == itemStart</c>, <c>PayloadLength == 1</c>). All offsets are document-relative.
/// </summary>
internal readonly struct RLPHeader(RLPKind kind, int payloadStart, int payloadLength)
{
    public RLPKind Kind { get; } = kind;

    public int PayloadStart { get; } = payloadStart;

    public int PayloadLength { get; } = payloadLength;

    /// <summary>Exclusive end of the whole item. Cannot overflow: a valid header fits inside its container.</summary>
    public int ItemEnd => PayloadStart + PayloadLength;
}

/// <summary>
/// The single source of truth for RLP prefix rules (spec 5.1). The validator, child enumerator and index builder all
/// read headers through <see cref="TryRead"/>. Check order: bounds, classify, long-form length bytes present inside the
/// container, first length byte non-zero, accumulate, below 56 non-canonical / above int.MaxValue overflow, single
/// byte below 0x80 written as <c>81 xx</c>, header plus payload fit. Every comparison against input-derived values is
/// a subtraction against the remaining container bytes, never an addition that could wrap.
/// </summary>
internal static class RLPHeaderReader
{
    private const int ShortStringMaxPrefix = 0xb7;
    private const int ShortListMinPrefix = 0xc0;
    private const int ShortListMaxPrefix = 0xf7;
    private const int LongStringPrefixBase = 0xb7;
    private const int LongListPrefixBase = 0xf7;
    private const int MinimumLongFormLength = 56;

    /// <summary>
    /// Reads the header of the item starting at <paramref name="itemStart"/> inside
    /// <c>[itemStart, containerEnd)</c> of <paramref name="document"/>. Requires
    /// <c>0 &lt;= itemStart &lt; containerEnd &lt;= document.Length</c>; a violation reports
    /// <see cref="RLPHeaderStatus.Incomplete"/> rather than reading. <paramref name="header"/> is default unless the
    /// result is <see cref="RLPHeaderStatus.Valid"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RLPHeaderStatus TryRead(ReadOnlySpan<byte> document, int itemStart, int containerEnd, out RLPHeader header)
    {
        header = default;

        // Unsigned bounds idiom: a negative offset must wrap to a huge uint and fail the compare, so it is Incomplete rather than
        // an OverflowException in a checked build (unchecked: the reinterpretation is the point).
        if (unchecked((uint)itemStart >= (uint)containerEnd || (uint)containerEnd > (uint)document.Length))
        {
            return RLPHeaderStatus.Incomplete;
        }

        int first = document[itemStart];
        if (first < 0x80)
        {
            header = new RLPHeader(RLPKind.ByteString, itemStart, 1);
            return RLPHeaderStatus.Valid;
        }

        if (first <= ShortStringMaxPrefix)
        {
            int length = first - 0x80;
            int payloadStart = itemStart + 1;
            if ((uint)length > (uint)(containerEnd - payloadStart))
            {
                return RLPHeaderStatus.Incomplete;
            }

            // A one-byte payload below 0x80 must be written as the literal byte. The payload is present here (length is 1).
            if (length == 1 && document[payloadStart] < 0x80)
            {
                return RLPHeaderStatus.NonCanonicalSingleByte;
            }

            header = new RLPHeader(RLPKind.ByteString, payloadStart, length);
            return RLPHeaderStatus.Valid;
        }

        // first is 0xb8..0xff here, so first - 0xc0 is negative for 0xb8..0xbf: the unsigned reinterpretation turns that into
        // "above the range" (unchecked: deliberate wrap).
        if (unchecked((uint)(first - ShortListMinPrefix)) <= ShortListMaxPrefix - ShortListMinPrefix)
        {
            int length = first - ShortListMinPrefix;
            int payloadStart = itemStart + 1;
            if ((uint)length > (uint)(containerEnd - payloadStart))
            {
                return RLPHeaderStatus.Incomplete;
            }

            header = new RLPHeader(RLPKind.List, payloadStart, length);
            return RLPHeaderStatus.Valid;
        }

        return TryReadLongForm(document, itemStart, containerEnd, first, out header);
    }

    /// <summary>Long strings (<c>b8..bf</c>) and long lists (<c>f8..ff</c>): one to eight big-endian length bytes.</summary>
    private static RLPHeaderStatus TryReadLongForm(ReadOnlySpan<byte> document, int itemStart, int containerEnd, int first, out RLPHeader header)
    {
        header = default;
        RLPKind kind;
        int lengthOfLength;
        if (first < ShortListMinPrefix)
        {
            kind = RLPKind.ByteString;
            lengthOfLength = first - LongStringPrefixBase;
        }
        else
        {
            kind = RLPKind.List;
            lengthOfLength = first - LongListPrefixBase;
        }

        // lengthOfLength is 1..8; the length bytes must all lie inside the container.
        int lengthStart = itemStart + 1;
        if ((uint)lengthOfLength > (uint)(containerEnd - lengthStart))
        {
            return RLPHeaderStatus.Incomplete;
        }

        ReadOnlySpan<byte> lengthBytes = document.Slice(lengthStart, lengthOfLength);
        if (lengthBytes[0] == 0)
        {
            return RLPHeaderStatus.NonCanonicalLength;
        }

        ulong length;
        if (lengthOfLength == 1)
        {
            length = lengthBytes[0];
        }
        else if (lengthOfLength == 2)
        {
            length = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);
        }
        else if (lengthOfLength == 3)
        {
            length = ((uint)BinaryPrimitives.ReadUInt16BigEndian(lengthBytes) << 8) | lengthBytes[2];
        }
        else if (lengthOfLength == 4)
        {
            length = BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
        }
        else
        {
            int end = lengthOfLength;
            length = LimbReader.TakeBigEndianTail(lengthBytes, ref end, 0);
        }

        if (length < MinimumLongFormLength)
        {
            return RLPHeaderStatus.NonCanonicalLength;
        }

        if (length > int.MaxValue)
        {
            return RLPHeaderStatus.LengthOverflow;
        }

        int payloadStart = lengthStart + lengthOfLength;
        int payloadLength = (int)length;
        if ((uint)payloadLength > (uint)(containerEnd - payloadStart))
        {
            return RLPHeaderStatus.Incomplete;
        }

        header = new RLPHeader(kind, payloadStart, payloadLength);
        return RLPHeaderStatus.Valid;
    }

    /// <summary>
    /// Maps a failed header read to the public error (cold path). <paramref name="isRoot"/> selects between
    /// <see cref="RLPStatus.TruncatedInput"/> at the end of the input and <see cref="RLPStatus.InvalidListBoundary"/>
    /// at the child's prefix. Requires <paramref name="status"/> to be a failure.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static RLPError ToError(RLPHeaderStatus status, int itemStart, int containerEnd, bool isRoot) => status switch
    {
        RLPHeaderStatus.Incomplete => isRoot
            ? new RLPError(RLPStatus.TruncatedInput, containerEnd)
            : new RLPError(RLPStatus.InvalidListBoundary, itemStart),
        RLPHeaderStatus.NonCanonicalLength => new RLPError(RLPStatus.NonCanonicalEncoding, itemStart + 1),
        RLPHeaderStatus.NonCanonicalSingleByte => new RLPError(RLPStatus.NonCanonicalEncoding, itemStart),
        RLPHeaderStatus.LengthOverflow => new RLPError(RLPStatus.LengthOverflow, itemStart + 1),
        _ => new RLPError(RLPStatus.InvalidArgument, -1),
    };
}
