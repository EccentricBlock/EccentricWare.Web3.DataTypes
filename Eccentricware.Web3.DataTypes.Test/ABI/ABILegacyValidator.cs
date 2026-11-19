using System.Runtime.CompilerServices;
using EccentricWare.Web3.DataTypes.ABI;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

// TEST-ONLY ORACLE. A copy of the strict validator as it was before the run validation of static array elements (ABIWordRun) and the vector
// padding check were added: one loop iteration per element, scalar padding loop. Only the class name, the namespace and these comment lines
// differ from the original ABIValidator.cs. ABIWordRunTests compares the production validator against it on status, document offset and
// schema node for random schemas, documents and budgets. Do not "improve" it: its value is that it is the old code.

/// <summary>
/// The one strict validator behind <see cref="ABIData"/> and <see cref="ABICallData"/> (spec 6). Iterative: open tuple-like
/// regions live in a fixed-capacity stack of <see cref="ABIValidationFrame"/> whose size comes from the validated depth limit
/// (never from the input), so there is no recursion and no heap use on success or failure.
/// </summary>
/// <remarks>
/// <para>Order of checks for one value (spec 5.2): depth, visited-value budget, then one work unit for the value entry; for a
/// dynamic child its offset word (containment, work unit, 256-bit fit, bounds, alignment, head overlap, canonical position);
/// then the value's own words (containment, work unit, then contents). Work units: one per value entered and one per 32-byte
/// word examined (scalar, offset, length, count, and the partially used last word when padding is examined); payload data
/// words are not examined and cost nothing.</para>
/// <para>Failure offsets name the start of the word, region or padding byte that failed (-1 when none applies).</para>
/// </remarks>
internal static class ABILegacyValidator
{
    private const int FramesShallow = 8;
    private const int FramesDeep = ABIDecodeLimits.HardMaxValueDepth;

    [InlineArray(FramesShallow)]
    private struct ShallowFrames
    {
        private ABIValidationFrame _element0;
    }

    [InlineArray(FramesDeep)]
    private struct DeepFrames
    {
        private ABIValidationFrame _element0;
    }

    /// <summary>
    /// Validates <paramref name="input"/> as exactly one canonical encoding of <paramref name="schema"/>'s root tuple. The limit
    /// fields arrive as separate values because the public entry points read the caller's limits storage once.
    /// </summary>
    internal static ABIStatus TryValidate(
        ReadOnlySpan<byte> input,
        ABISchema? schema,
        int maxBytes,
        int maxDepth,
        int maxArrayElements,
        int maxVisited,
        int maxWork,
        out ABIError error)
    {
        // Spec 5.2 (1): schema and limits; then (2): size before any byte is read.
        if (schema is null || !new ABIDecodeLimits(maxBytes, maxDepth, maxArrayElements, maxVisited, maxWork).IsValid)
        {
            return Fail(ABIStatus.InvalidArgument, -1, -1, out error);
        }

        if (input.Length > maxBytes)
        {
            return Fail(ABIStatus.InputTooLarge, -1, -1, out error);
        }

        // Nesting can never exceed the schema's own depth, so the frame bucket follows the smaller of the two.
        return Math.Min(maxDepth, schema.MaxDepth) <= FramesShallow
            ? RunShallow(input, schema, maxDepth, maxArrayElements, maxVisited, maxWork, out error)
            : RunDeep(input, schema, maxDepth, maxArrayElements, maxVisited, maxWork, out error);
    }

    // NoInlining on the two tier methods keeps each frame buffer in its own method, so the shallow path never zeroes the deep buffer.
    // It is a layout guarantee, not a speed claim: letting the JIT decide (also for TryValidate) measured the same within noise
    // (docs: docs/abi-design-notes.md, informal Stopwatch run; BenchmarkDotNet numbers belong to the benchmarks).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ABIStatus RunShallow(ReadOnlySpan<byte> input, ABISchema schema, int maxDepth, int maxArrayElements, int maxVisited, int maxWork, out ABIError error)
    {
        ShallowFrames frames = default;
        return Run(input, schema, maxDepth, maxArrayElements, maxVisited, maxWork, frames, out error);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ABIStatus RunDeep(ReadOnlySpan<byte> input, ABISchema schema, int maxDepth, int maxArrayElements, int maxVisited, int maxWork, out ABIError error)
    {
        DeepFrames frames = default;
        return Run(input, schema, maxDepth, maxArrayElements, maxVisited, maxWork, frames, out error);
    }

    private static ABIStatus Run(
        ReadOnlySpan<byte> input,
        ABISchema schema,
        int maxDepth,
        int maxArrayElements,
        int maxVisited,
        int maxWork,
        Span<ABIValidationFrame> frames,
        out ABIError error)
    {
        // The root entry is value 1 at depth 1 and one work unit; every budget is at least 1 (checked by the caller).
        int visited = 1;
        int work = 1;

        ref readonly ABISchemaNode root = ref schema.Root;
        if (root.HeadSize > input.Length)
        {
            return Fail(ABIStatus.Truncated, 0, ABISchema.RootNodeId, out error);
        }

        frames[0] = new ABIValidationFrame
        {
            Node = ABISchema.RootNodeId,
            ValueStart = 0,
            HeadBase = 0,
            HeadEnd = root.HeadSize,
            HeadCursor = 0,
            TailCursor = root.HeadSize,
            NextChild = 0,
            ChildCount = root.Count,
        };

        int top = 0;
        while (true)
        {
            ref ABIValidationFrame frame = ref frames[top];

            if (frame.NextChild == frame.ChildCount)
            {
                // Container complete: its head must be exactly consumed (guaranteed by the schema invariants; re-checked).
                if (frame.HeadCursor != frame.HeadEnd)
                {
                    return Fail(ABIStatus.NonCanonicalLayout, frame.HeadCursor, frame.Node, out error);
                }

                int end = frame.TailCursor;
                if (top == 0)
                {
                    if (end != input.Length)
                    {
                        // Spec 6.1 step 5: the root must end exactly at the input end (trailing data is rejected).
                        return Fail(ABIStatus.NonCanonicalLayout, end, ABISchema.RootNodeId, out error);
                    }

                    error = default;
                    return ABIStatus.Success;
                }

                int finishedNode = frame.Node;
                int finishedStart = frame.ValueStart;
                top--;
                ref ABIValidationFrame parent = ref frames[top];
                ref readonly ABISchemaNode finished = ref schema.GetNode(finishedNode);
                if (finished.IsDynamic)
                {
                    parent.TailCursor = end;
                    parent.HeadCursor += ABIWord.ABIWordSize;
                }
                else
                {
                    // A static child must consume exactly its compiled static size (including zero for an empty tuple).
                    if (end - finishedStart != finished.StaticSize)
                    {
                        return Fail(ABIStatus.NonCanonicalLayout, finishedStart, finishedNode, out error);
                    }

                    parent.HeadCursor += finished.HeadWidth;
                }

                parent.NextChild++;
                continue;
            }

            ref readonly ABISchemaNode container = ref schema.GetNode(frame.Node);
            int childId = container.Kind == ABITypeKind.Tuple
                ? schema.GetTupleChildren(in container)[frame.NextChild].NodeId
                : container.Element;
            ref readonly ABISchemaNode child = ref schema.GetNode(childId);

            // Value entry: the root tuple is depth 1, so this child is at depth (top + 1) + 1.
            if (top + 2 > maxDepth)
            {
                return Fail(ABIStatus.DepthLimitExceeded, frame.HeadCursor, childId, out error);
            }

            if (visited >= maxVisited || work >= maxWork)
            {
                return Fail(ABIStatus.WorkLimitExceeded, frame.HeadCursor, childId, out error);
            }

            visited++;
            work++;

            if (child.IsDynamic)
            {
                int slot = frame.HeadCursor;
                if (!ABIWord.TryLoad(input, slot, out ABIWord pointer))
                {
                    return Fail(ABIStatus.Truncated, slot, childId, out error);
                }

                if (work >= maxWork)
                {
                    return Fail(ABIStatus.WorkLimitExceeded, slot, childId, out error);
                }

                work++;

                // Pointer checks in the order of spec 5.2: fit, then the canonical relation. Everything that is not a
                // plausible in-document pointer is InvalidOffset; a plausible one that is not the next tail is a layout error.
                if (!pointer.TryGetInt32(out int offset)
                    || offset > input.Length - frame.HeadBase
                    || (offset & (ABIWord.ABIWordSize - 1)) != 0
                    || offset < frame.HeadEnd - frame.HeadBase)
                {
                    return Fail(ABIStatus.InvalidOffset, slot, childId, out error);
                }

                if (offset != frame.TailCursor - frame.HeadBase)
                {
                    return Fail(ABIStatus.NonCanonicalLayout, slot, childId, out error);
                }

                int start = frame.TailCursor;
                if (child.Kind == ABITypeKind.Bytes || child.Kind == ABITypeKind.String)
                {
                    ABIStatus leaf = ValidateBytes(input, start, ref work, maxWork, out int leafEnd, out int leafFailAt);
                    if (leaf != ABIStatus.Success)
                    {
                        return Fail(leaf, leafFailAt, childId, out error);
                    }

                    frame.TailCursor = leafEnd;
                    frame.HeadCursor += ABIWord.ABIWordSize;
                    frame.NextChild++;
                    continue;
                }

                if (top + 1 >= frames.Length)
                {
                    return Fail(ABIStatus.DepthLimitExceeded, start, childId, out error); // unreachable: capacity follows the depth
                }

                ABIStatus entered = EnterContainer(input, childId, in child, start, maxArrayElements, ref work, maxWork, ref frames[top + 1], out int enterFailAt);
                if (entered != ABIStatus.Success)
                {
                    return Fail(entered, enterFailAt, childId, out error);
                }

                top++;
                continue;
            }

            if (child.Kind == ABITypeKind.Tuple || child.Kind == ABITypeKind.FixedArray)
            {
                if (top + 1 >= frames.Length)
                {
                    return Fail(ABIStatus.DepthLimitExceeded, frame.HeadCursor, childId, out error); // unreachable: capacity follows the depth
                }

                ABIStatus entered = EnterContainer(input, childId, in child, frame.HeadCursor, maxArrayElements, ref work, maxWork, ref frames[top + 1], out int enterFailAt);
                if (entered != ABIStatus.Success)
                {
                    return Fail(entered, enterFailAt, childId, out error);
                }

                top++;
                continue;
            }

            // Static primitive, validated in place in its parent's head.
            int position = frame.HeadCursor;
            if (!ABIWord.TryLoad(input, position, out ABIWord word))
            {
                return Fail(ABIStatus.Truncated, position, childId, out error);
            }

            if (work >= maxWork)
            {
                return Fail(ABIStatus.WorkLimitExceeded, position, childId, out error);
            }

            work++;
            ABIStatus scalar = word.CheckScalar(child.Kind, child.Width);
            if (scalar != ABIStatus.Success)
            {
                return Fail(scalar, position, childId, out error);
            }

            frame.HeadCursor += ABIWord.ABIWordSize;
            frame.NextChild++;
        }
    }

    /// <summary>
    /// Validates a dynamic <c>bytes</c>/<c>string</c> value at <paramref name="start"/>: length word, length against the bytes that
    /// follow (256-bit compare first, so nothing is narrowed or added before it is proven to fit), padded end, zero padding.
    /// </summary>
    private static ABIStatus ValidateBytes(ReadOnlySpan<byte> input, int start, ref int work, int maxWork, out int end, out int failAt)
    {
        end = 0;
        failAt = start;
        if (!ABIWord.TryLoad(input, start, out ABIWord lengthWord))
        {
            return ABIStatus.Truncated;
        }

        if (work >= maxWork)
        {
            return ABIStatus.WorkLimitExceeded;
        }

        work++;
        int dataStart = start + ABIWord.ABIWordSize;
        int available = input.Length - dataStart; // >= 0: the length word was contained
        if ((lengthWord.W0 | lengthWord.W1 | lengthWord.W2) != 0 || lengthWord.W3 > (ulong)available)
        {
            return ABIStatus.Truncated;
        }

        // length <= available <= int.MaxValue - 32, so the padding arithmetic below cannot overflow (spec 6.3).
        int length = unchecked((int)lengthWord.W3);
        int padding = (ABIWord.ABIWordSize - (length & (ABIWord.ABIWordSize - 1))) & (ABIWord.ABIWordSize - 1);
        if (padding > available - length)
        {
            return ABIStatus.Truncated;
        }

        if (padding != 0)
        {
            if (work >= maxWork)
            {
                return ABIStatus.WorkLimitExceeded;
            }

            work++;
            int paddingStart = dataStart + length;
            int bad = ABIWord.FirstNonZero(input.Slice(paddingStart, padding));
            if (bad >= 0)
            {
                failAt = paddingStart + bad;
                return ABIStatus.InvalidPadding;
            }
        }

        end = dataStart + length + padding;
        return ABIStatus.Success;
    }

    /// <summary>
    /// Opens a tuple, fixed array or dynamic array whose value starts at <paramref name="start"/>: reads and bounds a dynamic count
    /// (against the element limit before any multiplication), and proves the whole head region lies inside the document.
    /// </summary>
    private static ABIStatus EnterContainer(
        ReadOnlySpan<byte> input,
        int nodeId,
        in ABISchemaNode node,
        int start,
        int maxArrayElements,
        ref int work,
        int maxWork,
        ref ABIValidationFrame frame,
        out int failAt)
    {
        failAt = start;
        int headBase;
        int count;
        long headBytes;
        if (node.Kind == ABITypeKind.DynamicArray)
        {
            if (!ABIWord.TryLoad(input, start, out ABIWord countWord))
            {
                return ABIStatus.Truncated;
            }

            if (work >= maxWork)
            {
                return ABIStatus.WorkLimitExceeded;
            }

            work++;
            if ((countWord.W0 | countWord.W1 | countWord.W2) != 0 || countWord.W3 > (ulong)maxArrayElements)
            {
                return ABIStatus.ElementLimitExceeded;
            }

            count = unchecked((int)countWord.W3);
            headBase = start + ABIWord.ABIWordSize;
            headBytes = (long)count * node.ElementHeadWidth; // both below 2^31: cannot overflow a long
        }
        else
        {
            count = node.Count;
            headBase = start;
            headBytes = node.HeadSize;
        }

        if (headBytes > input.Length - headBase)
        {
            return ABIStatus.Truncated;
        }

        int headEnd = headBase + (int)headBytes;
        frame = new ABIValidationFrame
        {
            Node = nodeId,
            ValueStart = start,
            HeadBase = headBase,
            HeadEnd = headEnd,
            HeadCursor = headBase,
            TailCursor = headEnd,
            NextChild = 0,
            ChildCount = count,
        };
        return ABIStatus.Success;
    }

    private static ABIStatus Fail(ABIStatus status, int offset, int node, out ABIError error)
    {
        error = new ABIError(status, offset, node);
        return status;
    }
}
