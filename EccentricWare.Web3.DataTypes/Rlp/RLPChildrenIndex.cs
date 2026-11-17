namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>
/// A caller-storage-backed cache of the direct-child ranges of one validated list, built by
/// <see cref="RLPData.TryIndexDirectChildren"/>. It cannot be constructed from arbitrary ranges, and
/// <c>default(RLPChildrenIndex)</c> is invalid.
/// </summary>
/// <remarks>
/// Stable-workspace contract: the caller must not modify, recycle or concurrently share the workspace passed to
/// <see cref="RLPData.TryIndexDirectChildren"/> while the index is in use, nor change the document bytes. The index is a
/// local performance cache inside the validated-data trust boundary, not an authorisation record, not for
/// persistence and not for reuse across requests. As defence in depth <see cref="TryGetAt"/> never reads out of
/// bounds and never throws even if the workspace was corrupted: it re-checks that the stored range lies inside the
/// list payload, continues where the previous entry ended and stops where the next entry begins, and that a
/// re-read header ends exactly there. A single corrupted entry is therefore rejected with
/// <see cref="RLPStatus.InvalidListBoundary"/>; coordinated edits of several adjacent entries are not detectable
/// without re-validating the list, so the stability contract above remains the actual guarantee.
/// </remarks>
public readonly ref struct RLPChildrenIndex
{
    private readonly ReadOnlySpan<byte> _document;
    private readonly ReadOnlySpan<RLPRange> _ranges;
    private readonly int _payloadStart;

    // Exclusive end of the owning list's payload. A real list prefix takes at least one byte, so a valid index has a
    // positive end and default(RLPChildrenIndex) (zero) is invalid.
    private readonly int _payloadEnd;

    internal RLPChildrenIndex(ReadOnlySpan<byte> document, ReadOnlySpan<RLPRange> ranges, int payloadStart, int payloadEnd)
    {
        _document = document;
        _ranges = ranges;
        _payloadStart = payloadStart;
        _payloadEnd = payloadEnd;
    }

    /// <summary>True for an index built by <see cref="RLPData.TryIndexDirectChildren"/>; false for <c>default</c>.</summary>
    public bool IsValid => _payloadEnd != 0;

    /// <summary>The number of direct children; 0 for an empty list and for an invalid index.</summary>
    public int Count => _ranges.Length;

    /// <summary>
    /// Returns the child at <paramref name="ordinal"/> as a view that keeps the original document coordinate base.
    /// <see cref="RLPStatus.InvalidArgument"/> for a negative or out-of-range ordinal or an invalid index;
    /// <see cref="RLPStatus.InvalidListBoundary"/> if the stored range no longer matches a child of the list.
    /// <paramref name="child"/> is <c>default</c> on failure. O(1): one header re-read.
    /// </summary>
    public RLPStatus TryGetAt(int ordinal, out RLPData child)
    {
        child = default;
        if (_payloadEnd == 0 || unchecked((uint)ordinal) >= (uint)_ranges.Length)
        {
            return RLPStatus.InvalidArgument;
        }

        RLPRange range = _ranges[ordinal];

        // The entry must start where the previous one ended (the payload start for the first) and end where the
        // next one starts (the payload end for the last). Long arithmetic: forged values cannot wrap.
        long expectedStart = ordinal == 0
            ? _payloadStart
            : (long)_ranges[ordinal - 1].Offset + _ranges[ordinal - 1].Length;
        long expectedEnd = ordinal == _ranges.Length - 1
            ? _payloadEnd
            : _ranges[ordinal + 1].Offset;
        if (range.Offset != expectedStart || (long)range.Offset + range.Length != expectedEnd || range.Length < 1)
        {
            return RLPStatus.InvalidListBoundary;
        }

        // The entry is now a non-empty slice of [_payloadStart, _payloadEnd) given a sane payload range, but do not
        // rely on that: the header read re-checks the bounds against the list end and the document length.
        if (RLPHeaderReader.TryRead(_document, range.Offset, _payloadEnd, out RLPHeader header) != RLPHeaderStatus.Valid
            || header.ItemEnd != range.Offset + range.Length)
        {
            return RLPStatus.InvalidListBoundary;
        }

        child = new RLPData(_document, header.Kind, range.Offset, header.PayloadStart, header.ItemEnd);
        return RLPStatus.Success;
    }
}
