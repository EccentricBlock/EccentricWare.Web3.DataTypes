using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>
/// A read-only, stack-scoped view of one fully validated RLP item inside an existing encoded buffer. It owns no bytes.
/// The only way to obtain a valid instance is <see cref="TryDecodeComplete"/> (strict validation of the whole
/// document, every nested list included) or traversal of an already validated instance
/// (<see cref="TryGetChildren"/>, <see cref="TryIndexDirectChildren"/>). There is no public constructor taking bytes,
/// ranges or a kind, and <c>default(RLPData)</c> is invalid (<see cref="Kind"/> is <see cref="RLPKind.None"/>).
/// </summary>
/// <remarks>
/// <para>
/// Buffer ownership: a validated view is trusted only while the bytes it was validated against stay unchanged. A
/// <see cref="ReadOnlySpan{T}"/> does not freeze its backing array, so the caller must keep the encoded input stable,
/// and not share it with concurrent writers, for the lifetime of every view derived from it. No fail-safe behaviour is
/// claimed after mutation, beyond the guarantee that the traversal members never read outside the document.
/// </para>
/// <para>
/// Ranges are document-relative: <see cref="EncodedRange"/> and <see cref="PayloadRange"/> of every child, at any
/// nesting depth, are offsets from the first byte of the originally decoded document.
/// </para>
/// </remarks>
public readonly ref struct RLPData
{
    private readonly ReadOnlySpan<byte> _document;
    private readonly int _itemStart;
    private readonly int _payloadStart;
    private readonly int _itemEnd;
    private readonly RLPKind _kind;

    /// <summary>Internal factory for the validator, enumerator and index; callers must pass validated coordinates.</summary>
    internal RLPData(ReadOnlySpan<byte> document, RLPKind kind, int itemStart, int payloadStart, int itemEnd)
    {
        _document = document;
        _kind = kind;
        _itemStart = itemStart;
        _payloadStart = payloadStart;
        _itemEnd = itemEnd;
    }

    /// <summary>True for a view produced by validation or validated traversal; false for <c>default</c>.</summary>
    public bool IsValid => _kind != RLPKind.None;

    /// <summary>The item kind, or <see cref="RLPKind.None"/> for <c>default</c>.</summary>
    public RLPKind Kind => _kind;

    /// <summary>The whole item including its prefix and length bytes; empty when <see cref="IsValid"/> is false.</summary>
    public ReadOnlySpan<byte> Encoded => _document.Slice(_itemStart, _itemEnd - _itemStart);

    /// <summary>The bytes after the prefix; for a literal byte <c>00..7f</c> this is the byte itself (equal to <see cref="Encoded"/>).</summary>
    public ReadOnlySpan<byte> Payload => _document.Slice(_payloadStart, _itemEnd - _payloadStart);

    /// <summary>Document-relative range of the whole encoded item; <c>(0, 0)</c> for <c>default</c>.</summary>
    public RLPRange EncodedRange => new(_itemStart, _itemEnd - _itemStart);

    /// <summary>Document-relative range of the payload; equals <see cref="EncodedRange"/> for a literal byte.</summary>
    public RLPRange PayloadRange => new(_payloadStart, _itemEnd - _payloadStart);

    /// <summary>Length of the whole encoded item in bytes (at least 1 when valid).</summary>
    public int EncodedLength => _itemEnd - _itemStart;

    /// <summary>Length of the payload in bytes.</summary>
    public int PayloadLength => _itemEnd - _payloadStart;

    /// <summary>
    /// Strictly validates <paramref name="input"/> as exactly one canonical RLP item: a single complete root, every
    /// nested child inside its list, canonical prefixes and lengths, no trailing bytes, and the budgets in
    /// <paramref name="limits"/>. Never throws for any input or limits; every failure is a status.
    /// </summary>
    /// <param name="input">The encoded document. Must stay stable and unshared while the result is in use.</param>
    /// <param name="limits">Resource budgets; start from <see cref="RLPDecodeLimits.Default"/>.</param>
    /// <param name="document">The root view on success; <c>default</c> on any failure.</param>
    /// <param name="error">Default (success, offset 0) on success; otherwise the failure and its offset in <paramref name="input"/>.</param>
    /// <returns>The failure status, or <see cref="RLPStatus.Success"/>.</returns>
    public static RLPStatus TryDecodeComplete(ReadOnlySpan<byte> input, scoped in RLPDecodeLimits limits, out RLPData document, out RLPError error)
    {
        RLPStatus status = RLPValidator.TryValidate(input, in limits, out RLPHeader root, out error);
        document = status == RLPStatus.Success
            ? new RLPData(input, root.Kind, 0, root.PayloadStart, root.ItemEnd)
            : default;
        return status;
    }

    /// <summary>
    /// Returns an enumerator over the direct children of a list. <see cref="RLPStatus.WrongKind"/> for a byte string,
    /// <see cref="RLPStatus.InvalidArgument"/> for <c>default</c>; <paramref name="children"/> is default on failure.
    /// </summary>
    public RLPStatus TryGetChildren(out RLPChildEnumerator children)
    {
        RLPStatus status = CheckIsList();
        children = status == RLPStatus.Success
            ? new RLPChildEnumerator(_document, _payloadStart, _itemEnd)
            : default;
        return status;
    }

    /// <summary>
    /// Records the document-relative encoded range of every direct child of a list into caller storage, so repeated
    /// positional access is O(1). Counts the children first without touching <paramref name="workspace"/>; when the
    /// workspace cannot hold them all it returns <see cref="RLPStatus.IndexCapacityExceeded"/> with the exact
    /// <paramref name="requiredCount"/>, an invalid index and an unmodified workspace. An empty list succeeds even
    /// with a zero-length workspace and yields a valid index of count zero.
    /// </summary>
    /// <remarks>
    /// The caller must not modify, recycle or share <paramref name="workspace"/> while the index is in use. The index
    /// is a performance cache inside the validated-data trust boundary, not an independent authorisation record.
    /// <see cref="RLPChildrenIndex.TryGetAt"/> still never reads out of bounds if the workspace or document is altered.
    /// </remarks>
    /// <param name="workspace">Caller-owned storage for the ranges.</param>
    /// <param name="index">The index over the populated prefix of <paramref name="workspace"/>; invalid on failure.</param>
    /// <param name="requiredCount">The number of direct children on success and on <see cref="RLPStatus.IndexCapacityExceeded"/>; 0 otherwise.</param>
    public RLPStatus TryIndexDirectChildren(Span<RLPRange> workspace, out RLPChildrenIndex index, out int requiredCount)
    {
        index = default;
        requiredCount = 0;
        RLPStatus status = CheckIsList();
        if (status != RLPStatus.Success)
        {
            return status;
        }

        // Pass 1: count only. The workspace is not touched until capacity is proven.
        int count = 0;
        int cursor = _payloadStart;
        while (cursor < _itemEnd)
        {
            if (RLPHeaderReader.TryRead(_document, cursor, _itemEnd, out RLPHeader child) != RLPHeaderStatus.Valid)
            {
                // Only reachable if the document changed after validation.
                return RLPStatus.InvalidListBoundary;
            }

            count++;
            cursor = child.ItemEnd;
        }

        if (workspace.Length < count)
        {
            requiredCount = count;
            return RLPStatus.IndexCapacityExceeded;
        }

        // Pass 2: populate exactly `count` entries. On a stable document this cannot fail; if the bytes change between
        // the passes the workspace contents are unspecified, the call returns InvalidListBoundary, and
        // RLPChildrenIndex.TryGetAt re-checks every entry it hands out.
        cursor = _payloadStart;
        for (int i = 0; i < count; i++)
        {
            if (RLPHeaderReader.TryRead(_document, cursor, _itemEnd, out RLPHeader child) != RLPHeaderStatus.Valid)
            {
                return RLPStatus.InvalidListBoundary;
            }

            workspace[i] = new RLPRange(cursor, child.ItemEnd - cursor);
            cursor = child.ItemEnd;
        }

        index = new RLPChildrenIndex(_document, workspace.Slice(0, count), _payloadStart, _itemEnd);
        requiredCount = count;
        return RLPStatus.Success;
    }

    /// <summary>
    /// Interprets a byte string as an Ethereum unsigned integer (shortest big-endian magnitude; the empty string is
    /// zero). <see cref="RLPStatus.WrongKind"/> for a list, <see cref="RLPStatus.NonCanonicalInteger"/> for a payload
    /// with a leading <c>00</c> byte (including the raw literal <c>00</c>, which is valid generic RLP but not an
    /// integer), <see cref="RLPStatus.IntegerOverflow"/> for more than 8 payload bytes. The leading-zero rule is
    /// applied before the width rule. <c>ulong.MaxValue</c> is valid. <paramref name="value"/> is 0 on failure.
    /// </summary>
    public RLPStatus TryGetUInt64(out ulong value)
    {
        value = 0;
        if (_kind != RLPKind.ByteString)
        {
            return _kind == RLPKind.None ? RLPStatus.InvalidArgument : RLPStatus.WrongKind;
        }

        ReadOnlySpan<byte> payload = Payload;
        if (payload.IsEmpty)
        {
            return RLPStatus.Success;
        }

        if (payload[0] == 0)
        {
            return RLPStatus.NonCanonicalInteger;
        }

        if (payload.Length > sizeof(ulong))
        {
            return RLPStatus.IntegerOverflow;
        }

        value = ReadMagnitude(payload);
        return RLPStatus.Success;
    }

    /// <summary>
    /// Big-endian value of 1..8 bytes (callers guarantee the width; wider input would keep only its low 64 bits). A full 8 bytes is one load; shorter widths use a plain loop over the span, which
    /// has no bounds checks and no per-width branches.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong ReadMagnitude(ReadOnlySpan<byte> bigEndian)
    {
        if (bigEndian.Length == sizeof(ulong))
        {
            return BinaryPrimitives.ReadUInt64BigEndian(bigEndian);
        }

        ulong value = 0;
        foreach (byte b in bigEndian)
        {
            value = (value << 8) | b;
        }

        return value;
    }

    /// <summary>
    /// Copies the item's exact encoded bytes (prefix included) to the start of <paramref name="destination"/> without
    /// normalising anything. Checks, before writing: valid item (<see cref="RLPStatus.InvalidArgument"/>), capacity
    /// (<see cref="RLPStatus.DestinationTooSmall"/>), then overlap between the source bytes and the destination region
    /// that would be written (<see cref="RLPStatus.OverlappingBuffers"/>; writing exactly onto the source is an
    /// error too). On any failure nothing is written and <paramref name="written"/> is 0.
    /// </summary>
    public RLPStatus TryCopyEncodedTo(Span<byte> destination, out int written)
    {
        written = 0;
        if (_kind == RLPKind.None)
        {
            return RLPStatus.InvalidArgument;
        }

        ReadOnlySpan<byte> source = Encoded;
        if (destination.Length < source.Length)
        {
            return RLPStatus.DestinationTooSmall;
        }

        Span<byte> region = destination.Slice(0, source.Length);
        if (source.Overlaps(region))
        {
            return RLPStatus.OverlappingBuffers;
        }

        source.CopyTo(region);
        written = source.Length;
        return RLPStatus.Success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private RLPStatus CheckIsList() => _kind switch
    {
        RLPKind.List => RLPStatus.Success,
        RLPKind.ByteString => RLPStatus.WrongKind,
        _ => RLPStatus.InvalidArgument,
    };
}
