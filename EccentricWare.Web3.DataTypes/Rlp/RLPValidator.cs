using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>
/// Iterative whole-document validation (spec 5.2). No recursion: open lists are tracked in an explicit stack of
/// enclosing list ends whose size comes from the configured depth limit (at most <see cref="RLPDecodeLimits.HardMaxListDepth"/>),
/// never from the input.
/// </summary>
internal static class RLPValidator
{
    /// <summary>
    /// Validates <paramref name="input"/> as exactly one canonical RLP item. On success <paramref name="root"/> is the
    /// root header and <paramref name="error"/> is default; on any failure <paramref name="root"/> is default.
    /// </summary>
    // NoInlining: keeps the validator prefix out of caller frames. Once this method had no stackalloc the JIT inlined it
    // into callers, and enumeration loops in those callers measured about 20% slower (docs/rlp-performance.md).
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static RLPStatus TryValidate(ReadOnlySpan<byte> input, in RLPDecodeLimits limits, out RLPHeader root, out RLPError error)
    {
        root = default;

        // Each field is read exactly once into a local, and everything below (validity, size check, frame bucket, budgets)
        // uses those locals. A caller mutating a shared limits variable concurrently therefore cannot pass IsValid and then
        // size the frame buffer from a different MaxListDepth. Locals, not a struct copy: an address-taken copy measured
        // about 0.5 ns per decode (docs/rlp-performance.md).
        int maxBytes = limits.MaxEncodedBytes;
        int maxDepth = limits.MaxListDepth;
        int maxItems = limits.MaxItems;

        // Spec 5.3: limits before input, then size before content, then emptiness.
        if (!new RLPDecodeLimits(maxBytes, maxDepth, maxItems).IsValid)
        {
            return Fail(RLPStatus.InvalidArgument, -1, out error);
        }

        if (input.Length > maxBytes)
        {
            return Fail(RLPStatus.TooLarge, maxBytes, out error);
        }

        if (input.Length == 0)
        {
            return Fail(RLPStatus.EmptyInput, 0, out error);
        }

        RLPHeaderStatus headerStatus = RLPHeaderReader.TryRead(input, 0, input.Length, out RLPHeader rootHeader);
        if (headerStatus != RLPHeaderStatus.Valid)
        {
            error = RLPHeaderReader.ToError(headerStatus, 0, input.Length, isRoot: true);
            return error.Status;
        }

        // A well-formed root that ends early is TrailingData, reported before any descent.
        int rootEnd = rootHeader.ItemEnd;
        if (rootEnd != input.Length)
        {
            return Fail(RLPStatus.TrailingData, rootEnd, out error);
        }

        if (rootHeader.Kind == RLPKind.ByteString || rootHeader.PayloadLength == 0)
        {
            root = rootHeader;
            error = default;
            return RLPStatus.Success;
        }

        // Frame size from the configured limit only (spec 9): small constants keep shallow configurations cheap.
        if (maxDepth <= EnclosingEndsShallow)
        {
            return ValidateShallow(input, maxDepth, maxItems, rootHeader, out root, out error);
        }

        return maxDepth <= EnclosingEndsMedium
            ? ValidateMedium(input, maxDepth, maxItems, rootHeader, out root, out error)
            : ValidateDeep(input, maxDepth, maxItems, rootHeader, out root, out error);
    }

    private const int EnclosingEndsShallow = 8;
    private const int EnclosingEndsMedium = 32;

    // Fixed-size frame buffers as plain struct locals, not stackalloc: a method containing stackalloc gets a stack-cookie
    // prolog (visible in the JIT disassembly), which about doubled the cost of decoding a scalar document (13 ns to
    // 6 ns without it; docs/rlp-performance.md). One method per depth bucket keeps each frame as small as the limit needs.
    [InlineArray(EnclosingEndsShallow)]
    private struct ShallowFrames
    {
        private int _element0;
    }

    [InlineArray(EnclosingEndsMedium)]
    private struct MediumFrames
    {
        private int _element0;
    }

    [InlineArray(RLPDecodeLimits.HardMaxListDepth)]
    private struct DeepFrames
    {
        private int _element0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RLPStatus ValidateShallow(ReadOnlySpan<byte> input, int maxDepth, int maxItems, RLPHeader rootHeader, out RLPHeader root, out RLPError error)
    {
        ShallowFrames frames = default;
        RLPStatus status = ValidateChildren(input, maxDepth, maxItems, rootHeader, frames, out error);
        root = status == RLPStatus.Success ? rootHeader : default;
        return status;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RLPStatus ValidateMedium(ReadOnlySpan<byte> input, int maxDepth, int maxItems, RLPHeader rootHeader, out RLPHeader root, out RLPError error)
    {
        MediumFrames frames = default;
        RLPStatus status = ValidateChildren(input, maxDepth, maxItems, rootHeader, frames, out error);
        root = status == RLPStatus.Success ? rootHeader : default;
        return status;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RLPStatus ValidateDeep(ReadOnlySpan<byte> input, int maxDepth, int maxItems, RLPHeader rootHeader, out RLPHeader root, out RLPError error)
    {
        DeepFrames frames = default;
        RLPStatus status = ValidateChildren(input, maxDepth, maxItems, rootHeader, frames, out error);
        root = status == RLPStatus.Success ? rootHeader : default;
        return status;
    }

    /// <summary>
    /// Walks every descendant of the non-empty root list. The root counts as item 1 at list depth 1. The first error in
    /// document order wins; the item budget is checked after an item's header parses and before its depth.
    /// </summary>
    private static RLPStatus ValidateChildren(
        ReadOnlySpan<byte> input,
        int maxDepth,
        int maxItems,
        RLPHeader rootHeader,
        Span<int> enclosingListEnds,
        out RLPError error)
    {
        int items = 1;
        int depth = 1;
        int listEnd = rootHeader.ItemEnd;
        int cursor = rootHeader.PayloadStart;

        while (true)
        {
            if (cursor == listEnd)
            {
                // The current list is complete exactly at its end; resume the enclosing list where this one stopped.
                if (depth == 1)
                {
                    error = default;
                    return RLPStatus.Success;
                }

                depth--;
                listEnd = enclosingListEnds[depth - 1];
                continue;
            }

            // cursor < listEnd holds here: every header fits inside its container, so the cursor never passes listEnd.
            RLPHeaderStatus headerStatus = RLPHeaderReader.TryRead(input, cursor, listEnd, out RLPHeader child);
            if (headerStatus != RLPHeaderStatus.Valid)
            {
                error = RLPHeaderReader.ToError(headerStatus, cursor, listEnd, isRoot: false);
                return error.Status;
            }

            if (items == maxItems)
            {
                return Fail(RLPStatus.TooManyItems, cursor, out error);
            }

            items++;
            if (child.Kind == RLPKind.ByteString)
            {
                cursor = child.ItemEnd;
                continue;
            }

            if (depth == maxDepth)
            {
                return Fail(RLPStatus.TooDeep, cursor, out error);
            }

            if (child.PayloadLength == 0)
            {
                cursor = child.ItemEnd;
                continue;
            }

            // depth < maxDepth <= enclosingListEnds.Length, so the frame index is in range.
            enclosingListEnds[depth - 1] = listEnd;
            depth++;
            listEnd = child.ItemEnd;
            cursor = child.PayloadStart;
        }
    }

    private static RLPStatus Fail(RLPStatus status, int offset, out RLPError error)
    {
        error = new RLPError(status, offset);
        return status;
    }
}
