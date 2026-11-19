using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>Counts the locator's steps (descents and word reads). The public path uses <see cref="ABINoLocateCounter"/>, which the JIT erases.</summary>
internal interface IABILocateCounter
{
    void Step();
}

/// <summary>A counter that does nothing; the generic locator specialised on it has no counting overhead.</summary>
internal struct ABINoLocateCounter : IABILocateCounter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Step()
    {
    }
}

/// <summary>Where a located field lives, in root-document coordinates (internal precursor of <see cref="ABIFieldLocation"/>).</summary>
internal readonly struct ABILocated
{
    internal ABILocated(int nodeId, int headOffset, int headLength, int valueOffset, int valueLength)
    {
        NodeId = nodeId;
        HeadOffset = headOffset;
        HeadLength = headLength;
        ValueOffset = valueOffset;
        ValueLength = valueLength;
    }

    internal readonly int NodeId;
    internal readonly int HeadOffset;
    internal readonly int HeadLength;
    internal readonly int ValueOffset;
    internal readonly int ValueLength;
}

/// <summary>
/// Schema-driven random access into an already validated document (spec 5.3 and 8.1): O(path depth), one constant-size step per
/// level, no scan of siblings or descendants. Each step uses the compiled head displacement (tuples) or index times head width
/// (arrays) to find the child's head slot, reads that one offset word, and takes the child's exclusive end from the NEXT dynamic
/// sibling's offset word (tuple: <see cref="ABITupleChild.NextDynamicSibling"/>; array: the next slot) or, for the last dynamic
/// child, from the enclosing container's end. A static child's end is its start plus its static size.
/// </summary>
/// <remarks>
/// The validator already proved offsets canonical, tails ordered and tight, and every container exhausted. Navigation relies on
/// that only while the bytes stay unchanged, but it still re-checks every offset and containment with long
/// arithmetic before it slices, so a mutated document yields a status, never an exception or a read outside the document. The
/// statuses of those defensive failures are <see cref="ABIStatus.Truncated"/> (a word is not contained),
/// <see cref="ABIStatus.InvalidOffset"/> (a pointer or bound is inconsistent) and <see cref="ABIStatus.NonCanonicalLayout"/>.
/// </remarks>
internal static class ABILocator
{
    /// <summary>Locates the field selected by <paramref name="path"/>; the empty path selects the root tuple.</summary>
    internal static ABIStatus TryLocate<TCounter>(
        ABISchema schema,
        ReadOnlySpan<byte> document,
        scoped ReadOnlySpan<int> path,
        ref TCounter counter,
        out ABILocated located)
        where TCounter : struct, IABILocateCounter
    {
        located = default;
        int nodeId = ABISchema.RootNodeId;
        int start = 0;
        int end = document.Length;
        int headOffset = 0;
        int headLength = schema.Root.HeadSize;
        if (headLength > document.Length)
        {
            return ABIStatus.Truncated;
        }

        for (int step = 0; step < path.Length; step++)
        {
            counter.Step();
            int index = path[step];
            if (index < 0)
            {
                return ABIStatus.InvalidPath;
            }

            ref readonly ABISchemaNode node = ref schema.GetNode(nodeId);
            int childId;
            long headBase;
            long headEnd;
            long slot;
            long nextSlot;
            switch (node.Kind)
            {
                case ABITypeKind.Tuple:
                {
                    if (index >= node.Count)
                    {
                        return ABIStatus.InvalidPath;
                    }

                    ReadOnlySpan<ABITupleChild> children = schema.GetTupleChildren(in node);
                    ref readonly ABITupleChild member = ref children[index];
                    childId = member.NodeId;
                    headBase = start;
                    headEnd = headBase + node.HeadSize;
                    slot = headBase + member.HeadOffset;
                    nextSlot = member.NextDynamicSibling == ABISchema.NoSibling
                        ? -1
                        : headBase + children[member.NextDynamicSibling].HeadOffset;
                    break;
                }

                case ABITypeKind.FixedArray:
                {
                    if (index >= node.Count)
                    {
                        return ABIStatus.InvalidPath;
                    }

                    childId = node.Element;
                    headBase = start;
                    headEnd = headBase + node.HeadSize;
                    slot = headBase + (long)index * node.ElementHeadWidth;
                    nextSlot = index + 1 < node.Count && schema.GetNode(childId).IsDynamic ? slot + node.ElementHeadWidth : -1;
                    break;
                }

                case ABITypeKind.DynamicArray:
                {
                    counter.Step();
                    if (!ABIWord.TryLoad(document, start, out ABIWord countWord))
                    {
                        return ABIStatus.Truncated;
                    }

                    if (!countWord.TryGetInt32(out int count))
                    {
                        return ABIStatus.InvalidOffset;
                    }

                    if (index >= count)
                    {
                        return ABIStatus.InvalidPath;
                    }

                    childId = node.Element;
                    headBase = (long)start + ABIWord.ABIWordSize;
                    headEnd = headBase + (long)count * node.ElementHeadWidth;
                    slot = headBase + (long)index * node.ElementHeadWidth;
                    nextSlot = index + 1 < count && schema.GetNode(childId).IsDynamic ? slot + node.ElementHeadWidth : -1;
                    break;
                }

                default:
                    // A primitive, bytes or string has no children.
                    return ABIStatus.InvalidPath;
            }

            if (headEnd > end)
            {
                return ABIStatus.Truncated;
            }

            ABIStatus resolved = ResolveChild(document, in schema.GetNode(childId), headBase, headEnd, slot, nextSlot, end, ref counter, out int childStart, out int childEnd, out int childHeadLength);
            if (resolved != ABIStatus.Success)
            {
                return resolved;
            }

            nodeId = childId;
            headOffset = (int)slot; // slot is inside [headBase, headEnd) which is inside the document: fits an int
            headLength = childHeadLength;
            start = childStart;
            end = childEnd;
        }

        located = new ABILocated(nodeId, headOffset, headLength, start, end - start);
        return ABIStatus.Success;
    }

    /// <summary>
    /// Finds one child's value interval <c>[childStart, childEnd)</c> from its head slot. <paramref name="nextSlot"/> is the head
    /// slot of the next dynamic sibling, or -1; <paramref name="containerEnd"/> is the container's exclusive end.
    /// </summary>
    private static ABIStatus ResolveChild<TCounter>(
        ReadOnlySpan<byte> document,
        in ABISchemaNode child,
        long headBase,
        long headEnd,
        long slot,
        long nextSlot,
        int containerEnd,
        ref TCounter counter,
        out int childStart,
        out int childEnd,
        out int headLength)
        where TCounter : struct, IABILocateCounter
    {
        childStart = 0;
        childEnd = 0;
        headLength = 0;

        if (!child.IsDynamic)
        {
            long size = child.StaticSize;
            if (slot < headBase || slot > headEnd - size)
            {
                return ABIStatus.InvalidOffset;
            }

            childStart = (int)slot;
            childEnd = (int)(slot + size);
            headLength = (int)size;
            return ABIStatus.Success;
        }

        if (slot < headBase || slot > headEnd - ABIWord.ABIWordSize)
        {
            return ABIStatus.InvalidOffset;
        }

        ABIStatus status = ReadTailStart(document, headBase, headEnd, containerEnd, slot, ref counter, out long tailStart);
        if (status != ABIStatus.Success)
        {
            return status;
        }

        long tailEnd = containerEnd;
        if (nextSlot >= 0)
        {
            if (nextSlot <= slot || nextSlot > headEnd - ABIWord.ABIWordSize)
            {
                return ABIStatus.InvalidOffset;
            }

            status = ReadTailStart(document, headBase, headEnd, containerEnd, nextSlot, ref counter, out tailEnd);
            if (status != ABIStatus.Success)
            {
                return status;
            }
        }

        // Every dynamic value is at least one word (count, length or tuple head), and its end cannot precede its start.
        if (tailEnd - tailStart < ABIWord.ABIWordSize)
        {
            return ABIStatus.InvalidOffset;
        }

        childStart = (int)tailStart;
        childEnd = (int)tailEnd;
        headLength = ABIWord.ABIWordSize;
        return ABIStatus.Success;
    }

    /// <summary>Reads the offset word at <paramref name="slot"/> and returns the absolute tail start, which must lie after the head and inside the container.</summary>
    private static ABIStatus ReadTailStart<TCounter>(
        ReadOnlySpan<byte> document,
        long headBase,
        long headEnd,
        int containerEnd,
        long slot,
        ref TCounter counter,
        out long tailStart)
        where TCounter : struct, IABILocateCounter
    {
        tailStart = 0;
        counter.Step();
        if (!ABIWord.TryLoad(document, (int)slot, out ABIWord word))
        {
            return ABIStatus.Truncated;
        }

        if (!word.TryGetInt32(out int offset))
        {
            return ABIStatus.InvalidOffset;
        }

        long target = headBase + offset;
        if (target < headEnd || target > containerEnd)
        {
            return ABIStatus.InvalidOffset;
        }

        tailStart = target;
        return ABIStatus.Success;
    }

    /// <summary>
    /// For a located dynamic <c>bytes</c>/<c>string</c>: reads the length word and returns the unpadded payload range. The value
    /// must be exactly <c>[length word][payload][padding to a word boundary]</c>, otherwise the document changed after validation.
    /// </summary>
    internal static ABIStatus TryGetPayload(ReadOnlySpan<byte> document, in ABILocated located, out int payloadOffset, out int payloadLength)
    {
        payloadOffset = 0;
        payloadLength = 0;
        if (!ABIWord.TryLoad(document, located.ValueOffset, out ABIWord lengthWord) || located.ValueLength < ABIWord.ABIWordSize)
        {
            return ABIStatus.Truncated;
        }

        long available = (long)located.ValueLength - ABIWord.ABIWordSize;
        if (!lengthWord.TryGetInt32(out int length) || length > available)
        {
            return ABIStatus.Truncated;
        }

        long padded = ((long)length + 31) & ~31L;
        if (padded != available)
        {
            return ABIStatus.NonCanonicalLayout;
        }

        payloadOffset = located.ValueOffset + ABIWord.ABIWordSize;
        payloadLength = length;
        return ABIStatus.Success;
    }

    /// <summary>For a located dynamic array: reads the count word.</summary>
    internal static ABIStatus TryGetCount(ReadOnlySpan<byte> document, in ABILocated located, out int count)
    {
        count = 0;
        if (!ABIWord.TryLoad(document, located.ValueOffset, out ABIWord countWord) || located.ValueLength < ABIWord.ABIWordSize)
        {
            return ABIStatus.Truncated;
        }

        return countWord.TryGetInt32(out count) ? ABIStatus.Success : ABIStatus.InvalidOffset;
    }
}
