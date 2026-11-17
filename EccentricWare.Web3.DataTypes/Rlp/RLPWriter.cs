using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>
/// A checked, framed writer that builds exactly one canonical RLP item, possibly a nested list, in caller storage
/// (spec 7.3). Lists are announced with their exact payload length (<see cref="TryBeginList"/>) so the header is
/// written once and nothing is moved afterwards; <see cref="TryEndList"/> verifies that exactly that many child bytes
/// were written. Every <c>Try*</c> member is allocation-free, never throws, and is atomic: on any failure no output
/// byte and no writer field changes, so the caller may repair the cause and retry the same call.
/// </summary>
/// <remarks>
/// <para>
/// Usage: construct explicitly with <see cref="RLPWriter(Span{byte}, Span{int})"/>, call the members on that one
/// variable, and finish with <see cref="TryComplete"/>. <c>default(RLPWriter)</c> is invalid and answers every
/// operation with <see cref="RLPStatus.WriterStateInvalid"/>. After a successful <see cref="TryComplete"/> every
/// further call also answers <see cref="RLPStatus.WriterStateInvalid"/>.
/// </para>
/// <para>
/// This is a mutable <c>ref struct</c>. Pass it by <c>ref</c> and never copy it once use has begun: a copy holds its
/// own cursor and frame count, so the copy and the original diverge while sharing the same buffers, and bytes written
/// through one are not accounted for by the other. For the same reason never call members through an <c>in</c>
/// parameter or a <c>readonly</c> field: the compiler would operate on a defensive copy and the mutation would be lost.
/// </para>
/// <para>
/// Buffer ownership: the destination and the frame workspace must stay stable, unshared and unmodified by the caller
/// for the writer's lifetime. The workspace content (absolute exclusive end offsets of the open lists) is internal and
/// not meaningful to callers; its length is the maximum list nesting depth, and one more open list is rejected with
/// <see cref="RLPStatus.TooDeep"/>. Source buffers passed to a write must not overlap the bytes about to be written
/// (<see cref="RLPStatus.OverlappingBuffers"/>); in-place encoding is not supported.
/// </para>
/// <para>
/// Check order of every item-adding member, all before any mutation: state (<see cref="RLPStatus.WriterStateInvalid"/>),
/// root discipline (<see cref="RLPStatus.ExtraRoot"/>), argument and minimality errors
/// (<see cref="RLPStatus.InvalidArgument"/>, <see cref="RLPStatus.NonCanonicalInteger"/>), total encoded size
/// (<see cref="RLPStatus.LengthOverflow"/>), the parent limit (<see cref="RLPStatus.InvalidListBoundary"/> inside an
/// open list, <see cref="RLPStatus.DestinationTooSmall"/> at the root), the free frame slot for lists
/// (<see cref="RLPStatus.TooDeep"/>), then overlap. The parent limit is the end of the innermost open list's announced
/// payload, or the end of the destination when no list is open.
/// </para>
/// <para>
/// The writer guarantees canonical structure only for items it produced or verbatim copied from a validated
/// <see cref="RLPData"/>; it does not check any higher-level schema.
/// </para>
/// </remarks>
public ref struct RLPWriter
{
    // Value 0 must be Invalid so default(RLPWriter) rejects everything.
    private enum WriterState : byte
    {
        Invalid = 0,
        Ready,
        RootInProgress,
        RootDone,
        Completed,
    }

    private const int MaxShortStringPayload = 55;

    private readonly Span<byte> _destination;

    // Slot i is the ABSOLUTE exclusive end offset of the payload of the i-th open list.
    private readonly Span<int> _frameEnds;
    private int _cursor;
    private int _depth;
    private WriterState _state;

    /// <summary>
    /// Creates a ready writer. Both buffers may be empty (the first write then fails with
    /// <see cref="RLPStatus.DestinationTooSmall"/> or <see cref="RLPStatus.TooDeep"/>); nothing is validated here.
    /// </summary>
    /// <param name="destination">Caller storage for the encoded item.</param>
    /// <param name="listEndWorkspace">Frame storage; its length is the maximum list nesting depth, for example <c>stackalloc int[32]</c>.</param>
    public RLPWriter(Span<byte> destination, Span<int> listEndWorkspace)
    {
        _destination = destination;
        _frameEnds = listEndWorkspace;
        _cursor = 0;
        _depth = 0;
        _state = WriterState.Ready;
    }

    /// <summary>
    /// Announces a list whose already encoded children will total <paramref name="encodedChildrenLength"/> bytes,
    /// writes its canonical header and opens a frame. The whole list (header plus announced payload) is reserved
    /// against the parent limit now, so children can never cross a parent. The list counts as one item of its parent
    /// immediately; it must be closed with <see cref="TryEndList"/> after exactly that many child bytes. A length of 0
    /// writes <c>c0</c> and still needs <see cref="TryEndList"/>.
    /// </summary>
    /// <param name="encodedChildrenLength">Exact total size of the encoded children; must not be negative.</param>
    public RLPStatus TryBeginList(int encodedChildrenLength)
    {
        RLPStatus status = CheckAcceptsItem();
        if (status != RLPStatus.Success)
        {
            return status;
        }

        if (encodedChildrenLength < 0)
        {
            return RLPStatus.InvalidArgument;
        }

        long size = RLPHeaderWriter.ListSize(encodedChildrenLength);
        if (size > int.MaxValue)
        {
            return RLPStatus.LengthOverflow;
        }

        int total = (int)size;
        status = CheckFits(total);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        if ((uint)_depth >= (uint)_frameEnds.Length)
        {
            return RLPStatus.TooDeep;
        }

        int headerLength = RLPHeaderWriter.WriteListHeader(_destination.Slice(_cursor), encodedChildrenLength);
        _frameEnds[_depth] = _cursor + total;
        _cursor += headerLength;
        if (_depth == 0)
        {
            _state = WriterState.RootInProgress;
        }

        _depth++;
        return RLPStatus.Success;
    }

    /// <summary>
    /// Writes <paramref name="payload"/> as one canonical byte string (the empty payload as <c>80</c>, a single byte
    /// below <c>0x80</c> as itself).
    /// </summary>
    /// <param name="payload">The content; must not overlap the bytes about to be written.</param>
    public RLPStatus TryWriteByteString(scoped ReadOnlySpan<byte> payload)
    {
        RLPStatus status = CheckAcceptsItem();
        if (status != RLPStatus.Success)
        {
            return status;
        }

        long size = RLPHeaderWriter.ByteStringSize(payload);
        if (size > int.MaxValue)
        {
            return RLPStatus.LengthOverflow;
        }

        return WriteByteStringChecked(payload, (int)size);
    }

    /// <summary>Writes an unsigned integer as <see cref="RLPEncoder.TryWriteUInt64"/> does: zero is <c>80</c>, 1..127 the literal byte, otherwise the shortest big-endian magnitude.</summary>
    /// <param name="value">The integer.</param>
    public RLPStatus TryWriteUInt64(ulong value)
    {
        RLPStatus status = CheckAcceptsItem();
        if (status != RLPStatus.Success)
        {
            return status;
        }

        int total = RLPHeaderWriter.UInt64Size(value);
        status = CheckFits(total);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        RLPHeaderWriter.WriteUInt64(_destination.Slice(_cursor, total), value);
        CommitItem(total);
        return RLPStatus.Success;
    }

    /// <summary>
    /// Writes an unsigned integer given as a big-endian magnitude of any length, as
    /// <see cref="RLPEncoder.TryWriteUnsignedMagnitude"/> does: empty is zero, a leading <c>00</c> is rejected with
    /// <see cref="RLPStatus.NonCanonicalInteger"/> and never normalised.
    /// </summary>
    /// <param name="minimalBigEndianMagnitude">The magnitude; must not overlap the bytes about to be written.</param>
    public RLPStatus TryWriteUnsignedMagnitude(scoped ReadOnlySpan<byte> minimalBigEndianMagnitude)
    {
        RLPStatus status = CheckAcceptsItem();
        if (status != RLPStatus.Success)
        {
            return status;
        }

        if (minimalBigEndianMagnitude.Length != 0 && minimalBigEndianMagnitude[0] == 0)
        {
            return RLPStatus.NonCanonicalInteger;
        }

        long size = RLPHeaderWriter.ByteStringSize(minimalBigEndianMagnitude);
        if (size > int.MaxValue)
        {
            return RLPStatus.LengthOverflow;
        }

        return WriteByteStringChecked(minimalBigEndianMagnitude, (int)size);
    }

    /// <summary>
    /// Writes a <see cref="uint256"/> as <see cref="TryWriteUnsignedMagnitude"/> would write its minimal big-endian
    /// magnitude, emitting the bytes straight from the limbs (no 32-byte temporary). Same checks in the same order:
    /// state, root, then size and capacity. There is no source buffer, so no overlap is possible.
    /// </summary>
    internal RLPStatus TryWriteUInt256Limbs(in uint256 value)
    {
        RLPStatus status = CheckAcceptsItem();
        if (status != RLPStatus.Success)
        {
            return status;
        }

        int byteCount = value.GetByteCount();
        bool isSingleByte = byteCount == 0 || (byteCount == 1 && value.U0 < 0x80);
        int total = isSingleByte ? 1 : 1 + byteCount;
        status = CheckFits(total);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        Span<byte> region = _destination.Slice(_cursor, total);
        if (isSingleByte)
        {
            region[0] = byteCount == 0 ? (byte)0x80 : (byte)value.U0;
        }
        else
        {
            region[0] = (byte)(0x80 + byteCount);
            WriteMagnitude(region.Slice(1), in value, byteCount);
        }

        CommitItem(total);
        return RLPStatus.Success;
    }

    /// <summary>Writes the lowest <paramref name="byteCount"/> (1..32) bytes of <paramref name="value"/> big-endian into <paramref name="destination"/>, which is exactly that long.</summary>
    private static void WriteMagnitude(Span<byte> destination, in uint256 value, int byteCount)
    {
        int topLimb = (byteCount - 1) >> 3;
        int topBytes = byteCount - (topLimb << 3); // 1..8 significant bytes in the top limb
        ulong limb = LimbAt(in value, topLimb);
        int position = topBytes;
        if (topBytes == sizeof(ulong))
        {
            BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(0, sizeof(ulong)), limb);
        }
        else if (topLimb > 0)
        {
            // A lower limb follows, so eight bytes always fit: write the top limb left-aligned and let the next
            // store overwrite the zero padding.
            BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(0, sizeof(ulong)), limb << ((sizeof(ulong) - topBytes) << 3));
        }
        else
        {
            for (int shift = (topBytes - 1) << 3, i = 0; shift >= 0; shift -= 8, i++)
            {
                destination[i] = unchecked((byte)(limb >> shift)); // one byte of the limb: truncation is the point
            }
        }

        for (int index = topLimb - 1; index >= 0; index--)
        {
            BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(position, sizeof(ulong)), LimbAt(in value, index));
            position += sizeof(ulong);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong LimbAt(in uint256 value, int index) => index switch
    {
        0 => value.U0,
        1 => value.U1,
        2 => value.U2,
        _ => value.U3,
    };

    /// <summary>
    /// Writes the header of a byte string of constant length <paramref name="payloadLength"/> (2..55: never the
    /// single-byte literal, always a one-byte header), commits the item and returns the payload region for the caller to
    /// fill immediately. Same checks in the same order as <see cref="TryWriteByteString"/> (state, root, then size and
    /// capacity); nothing is written and the writer is unchanged on failure. There is no source buffer, so no overlap is
    /// possible. The caller must fill the whole region before any further call.
    /// </summary>
    internal RLPStatus TryReserveShortByteString(int payloadLength, out Span<byte> payload)
    {
        Debug.Assert(payloadLength is >= 2 and <= MaxShortStringPayload, "a one-byte header needs 2..55 payload bytes");
        payload = default;
        RLPStatus status = CheckAcceptsItem();
        if (status != RLPStatus.Success)
        {
            return status;
        }

        int total = 1 + payloadLength;
        status = CheckFits(total);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        Span<byte> region = _destination.Slice(_cursor, total);
        region[0] = (byte)(0x80 + payloadLength);
        payload = region.Slice(1);
        CommitItem(total);
        return RLPStatus.Success;
    }

    /// <summary>
    /// Copies the exact encoded bytes of a validated item (string or list, prefix included) as one new item. Nothing is
    /// normalised or reinterpreted. <see cref="RLPStatus.InvalidArgument"/> for <c>default(RLPData)</c>. The source
    /// bytes must stay stable and must not overlap the bytes about to be written.
    /// </summary>
    /// <param name="validatedItem">An item obtained from <see cref="RLPData.TryDecodeComplete"/> or traversal of one.</param>
    public RLPStatus TryWriteEncoded(in RLPData validatedItem)
    {
        RLPStatus status = CheckAcceptsItem();
        if (status != RLPStatus.Success)
        {
            return status;
        }

        if (!validatedItem.IsValid)
        {
            return RLPStatus.InvalidArgument;
        }

        ReadOnlySpan<byte> source = validatedItem.Encoded;
        int total = source.Length;
        status = CheckFits(total);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        Span<byte> region = _destination.Slice(_cursor, total);
        if (source.Overlaps(region))
        {
            return RLPStatus.OverlappingBuffers;
        }

        source.CopyTo(region);
        CommitItem(total);
        return RLPStatus.Success;
    }

    /// <summary>
    /// Closes the innermost open list. Succeeds only when exactly the announced number of child bytes has been
    /// written; writes nothing. <see cref="RLPStatus.UnbalancedList"/> when no list is open or children are missing
    /// (the frame stays open, so the caller can add the missing children and retry).
    /// </summary>
    public RLPStatus TryEndList()
    {
        if (_state == WriterState.Invalid || _state == WriterState.Completed)
        {
            return RLPStatus.WriterStateInvalid;
        }

        int depth = _depth;
        if (depth == 0 || _cursor != _frameEnds[depth - 1])
        {
            return RLPStatus.UnbalancedList;
        }

        _depth = depth - 1;
        if (depth == 1)
        {
            _state = WriterState.RootDone;
        }

        return RLPStatus.Success;
    }

    /// <summary>
    /// Finishes the writer once exactly one root item is fully written and every list is closed. On success
    /// <paramref name="written"/> is the length of the encoding at the start of the destination and the writer rejects
    /// all further calls. On failure the writer is unchanged (it can be retried after fixing the cause) and
    /// <paramref name="written"/> is 0: <see cref="RLPStatus.UnbalancedList"/> with an open list,
    /// <see cref="RLPStatus.EmptyInput"/> when nothing was written.
    /// </summary>
    /// <param name="written">Total encoded length; 0 on failure.</param>
    public RLPStatus TryComplete(out int written)
    {
        written = 0;
        if (_state == WriterState.Invalid || _state == WriterState.Completed)
        {
            return RLPStatus.WriterStateInvalid;
        }

        if (_depth != 0)
        {
            return RLPStatus.UnbalancedList;
        }

        if (_state != WriterState.RootDone)
        {
            return RLPStatus.EmptyInput;
        }

        written = _cursor;
        _state = WriterState.Completed;
        return RLPStatus.Success;
    }

    /// <summary>State and root discipline: Ready and RootInProgress accept an item, RootDone is an extra root.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly RLPStatus CheckAcceptsItem()
    {
        WriterState state = _state;
        if (state == WriterState.Ready || state == WriterState.RootInProgress)
        {
            return RLPStatus.Success;
        }

        return RejectItem(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RLPStatus RejectItem(WriterState state) =>
        state == WriterState.RootDone ? RLPStatus.ExtraRoot : RLPStatus.WriterStateInvalid;

    /// <summary>
    /// Parent limit: <paramref name="total"/> bytes must fit before the innermost open list's end, or the end of the
    /// destination when no list is open. A negative room (a frame end below the cursor, which only a tampered workspace or a stale
    /// copy of the writer can produce) is rejected explicitly: reading it as an unsigned value would make it a huge room.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly RLPStatus CheckFits(int total)
    {
        int depth = _depth;
        int limit = depth > 0 ? _frameEnds[depth - 1] : _destination.Length;
        // long: a tampered frame end (even int.MinValue) cannot overflow the subtraction. total is never negative.
        long room = (long)limit - _cursor;
        if (room < 0 || (uint)total > room)
        {
            return RejectOverrun(depth);
        }

        // Defensive: every frame end was checked against the destination when its list began.
        if ((uint)total > (uint)(_destination.Length - _cursor))
        {
            return RLPStatus.DestinationTooSmall;
        }

        return RLPStatus.Success;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RLPStatus RejectOverrun(int depth) =>
        depth > 0 ? RLPStatus.InvalidListBoundary : RLPStatus.DestinationTooSmall;

    /// <summary>Shared tail of the byte-string and magnitude writes once state, arguments and size are settled.</summary>
    private RLPStatus WriteByteStringChecked(scoped ReadOnlySpan<byte> payload, int total)
    {
        RLPStatus status = CheckFits(total);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        Span<byte> region = _destination.Slice(_cursor, total);
        if (payload.Overlaps(region))
        {
            return RLPStatus.OverlappingBuffers;
        }

        RLPHeaderWriter.WriteByteString(region, payload);
        CommitItem(total);
        return RLPStatus.Success;
    }

    /// <summary>Advances past a complete item; a root-level item completes the root.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CommitItem(int total)
    {
        _cursor += total;
        if (_depth == 0)
        {
            _state = WriterState.RootDone;
        }
    }
}
