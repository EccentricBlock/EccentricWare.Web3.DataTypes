using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.ABI;

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
internal static class ABIValidator
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

                if (child.Kind >= ABITypeKind.FixedArray && frames[top + 1].ChildCount >= ABIWordRun.MinimumRun && top + 3 <= maxDepth)
                {
                    int consumed = ValidateElementRun(input, schema, in child, ref frames[top + 1], ref work, maxWork, visited, maxVisited);
                    if (consumed < 0)
                    {
                        return Fail((ABIStatus)~consumed, frames[top + 1].HeadCursor, child.Element, out error);
                    }

                    visited += consumed;
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

                if (child.Kind >= ABITypeKind.FixedArray && frames[top + 1].ChildCount >= ABIWordRun.MinimumRun && top + 3 <= maxDepth)
                {
                    int consumed = ValidateElementRun(input, schema, in child, ref frames[top + 1], ref work, maxWork, visited, maxVisited);
                    if (consumed < 0)
                    {
                        return Fail((ABIStatus)~consumed, frames[top + 1].HeadCursor, child.Element, out error);
                    }

                    visited += consumed;
                }

                top++;
                continue;
            }

            // Static primitive, validated in place in its parent's head. Containment first (the word is read only when it has a rule to
            // check: uint256, int256 and bytes32 accept every word, so their limbs are never loaded), then the work unit, then the word's rules.
            int position = frame.HeadCursor;
            if (position < 0 || position > input.Length || input.Length - position < ABIWord.ABIWordSize)
            {
                return Fail(ABIStatus.Truncated, position, childId, out error);
            }

            if (work >= maxWork)
            {
                return Fail(ABIStatus.WorkLimitExceeded, position, childId, out error);
            }

            work++;
            if (!child.AcceptsEveryWord)
            {
                ABIWord.TryLoad(input, position, out ABIWord word); // cannot fail: containment was checked above
                ABIStatus scalar = word.CheckScalar(child.Kind, child.Width);
                if (scalar != ABIStatus.Success)
                {
                    return Fail(scalar, position, childId, out error);
                }
            }

            frame.HeadCursor += ABIWord.ABIWordSize;
            frame.NextChild++;
        }
    }

    /// <summary>
    /// Called right after an array with at least <see cref="ABIWordRun.MinimumRun"/> elements was entered (its frame is <paramref name="frame"/>)
    /// at a depth that allows its elements (the caller tests both before the call, so a short array does not pay for it): when its elements
    /// are static elementary values, validates all of them as one run, with the same decisions as the per-element loop in
    /// <see cref="Run"/> (depth, then budgets, then the word's own rules, element by element): the number of elements that fit the visited and
    /// work budgets is computed arithmetically, those words are checked (<see cref="ABIWordRun.FindFirstRejected"/>: a fast accept, the scalar
    /// reference to locate a failure), and the counters and cursors are advanced for all of them.
    /// Returns the number of values to add to the caller's visited count (0 when the run does not apply: a non-elementary element), with the work
    /// units charged to <paramref name="work"/>; or, when
    /// one of the words that fit is rejected, the bitwise complement of the status the per-element loop would report, with <c>frame.HeadCursor</c>
    /// set to the offset of that word (the frame is dead after a failure). Elements beyond the budgets are left to the loop, which reports the
    /// budget failure at the first of them. A status is returned this way, not through <c>out</c> parameters, because extra address-exposed
    /// locals in <see cref="Run"/> measurably slowed its hot loop (docs/abi-performance.md).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ValidateElementRun(
        ReadOnlySpan<byte> input,
        ABISchema schema,
        in ABISchemaNode array,
        ref ABIValidationFrame frame,
        ref int work,
        int maxWork,
        int visited,
        int maxVisited)
    {
        int remaining = frame.ChildCount - frame.NextChild;

        ref readonly ABISchemaNode element = ref schema.GetNode(array.Element);
        if (element.Kind > ABITypeKind.UFixed)
        {
            return 0; // composite elements (every elementary kind comes first in the enum, and none of them is dynamic)
        }

        int start = frame.HeadCursor;
        // Element i completes iff visited + i < maxVisited and work + 2 i + 1 < maxWork (the entry unit, then the word unit).
        // visited <= maxVisited and work <= maxWork always hold (a counter only grows after its limit was checked), so fit >= 0.
        int fit = Math.Min(Math.Min(remaining, maxVisited - visited), (maxWork - work) / 2);

        // Contained after EnterContainer; if not (a bug), the per-element loop reports the truncation.
        if (unchecked((uint)start) > (uint)input.Length || fit > (input.Length - start) / ABIWord.ABIWordSize)
        {
            return 0;
        }

        int bad = ABIWordRun.FindFirstRejected(input.Slice(start, fit * ABIWord.ABIWordSize), element.Kind, element.Width, out ABIStatus rejected);
        if (bad >= 0)
        {
            frame.HeadCursor = start + bad * ABIWord.ABIWordSize;
            return ~(int)rejected;
        }

        // When fewer than `remaining` elements fit the budgets, the loop reports the budget failure at the first one that does not (its slot is
        // now HeadCursor, and the checks it makes are exactly those that failed the budget in the per-element order).
        work += 2 * fit;
        frame.HeadCursor = start + fit * ABIWord.ABIWordSize;
        frame.NextChild += fit;
        return fit;
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
            int bad = ABIWordRun.FirstNonZeroPadding(input, paddingStart, padding);
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
