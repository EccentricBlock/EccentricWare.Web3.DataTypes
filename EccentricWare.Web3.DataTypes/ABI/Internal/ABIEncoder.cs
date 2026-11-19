using System.Buffers.Binary;
using System.Text.Unicode;

namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// The one schema-ordered encoding state machine behind <see cref="ABIWriter"/>, <see cref="ABICallEncoder"/> and
/// <see cref="ABIMeasurer"/> (spec 7.3 to 7.6). With <c>writes</c> false it performs every check and every cursor move of a real
/// write but touches no byte, so a measured length and a written length cannot disagree.
/// </summary>
/// <remarks>
/// <para>Layout rule: opening a container reserves its whole head region; a static child fills its slot in the parent's head, a
/// dynamic child gets a 32-byte relative pointer in the head and is placed at the parent's tail cursor, which moves past it only
/// when the child closes. Pointers are relative to the container's own head base (for a dynamic array, the byte after its count
/// word). Every byte in <c>[0, end)</c> is written exactly once, padding included, so destination garbage never survives.</para>
/// <para>Check order of one operation, all before any byte is written or any frame field changes (a failed operation also
/// leaves the destination untouched): writer state, arity (the container must still expect a child), expected kind, then depth,
/// value budget and work budget. A scalar continues with the source length, the value range and the source overlap (its head slot
/// was reserved when its container opened, so there is no capacity check). A <c>bytes</c>/<c>string</c> continues with the byte
/// capacity (limit before destination), the overlap and the UTF-8 scan. A container continues with the scratch, the array count
/// limit (dynamic arrays) and the byte capacity of its head. Budgets are charged exactly like the decoder (one value per value entered, one work unit per
/// value and per 32-byte word: scalar, pointer, count, length, and the partially used last word of padded data), so a document
/// encoded under limits L decodes under the same L. Any failure faults the encoder.</para>
/// </remarks>
internal ref struct ABIEncoder
{
    private const int Word = ABIWord.ABIWordSize;

    // The state values live in frame 0 as ints. Open is 1 so a zeroed frame (and default(ABIEncoder), whose frame span is empty) is never Open.
    private enum EncoderState : int
    {
        Invalid = 0,
        Open,
        Completed,
        Faulted,
    }

    // Configuration: assigned once by TryBegin, never changed afterwards. Every piece of MUTABLE state (the state machine position, the
    // open-container count and the budget counters) lives in frame 0 of the caller's scratch span, next to the cursors, so a struct copy
    // of a writer shares all of it with the original and cannot bypass a budget or hide a fault.
    private ABISchema? _schema;
    private Span<byte> _destination;
    private Span<byte> _guard;
    private Span<ABIEncodeFrame> _frames;
    private bool _writes;
    private int _maxBytes;
    private int _maxDepth;
    private int _maxArray;
    private int _maxValues;
    private int _maxWork;
    private int _epoch;

    /// <summary>Index of the innermost open container's frame (0 = root). Stored in frame 0.</summary>
    private ref int Top => ref _frames[0].Top;

    /// <summary>Values entered so far (the root counts). Stored in frame 0.</summary>
    private ref int Values => ref _frames[0].Values;

    /// <summary>Work units charged so far. Stored in frame 0.</summary>
    private ref int Work => ref _frames[0].Work;

    /// <summary>The state machine position. A default encoder has no frames and reads as Invalid. Stored in frame 0.</summary>
    private EncoderState State
    {
        // A writer is only live while the scratch still belongs to it: a later TryBegin on the same frames changes the epoch.
        get => _frames.IsEmpty || _frames[0].Epoch != _epoch ? EncoderState.Invalid : (EncoderState)_frames[0].State;
        set => _frames[0].State = (int)value;
    }

    /// <summary>
    /// Validates the immutable configuration and reserves the root head. Nothing is written. <paramref name="guard"/> is the span
    /// no source may overlap (the whole caller buffer; for a call, including the selector bytes). A <paramref name="maxBytes"/>
    /// of zero is legal (a selector-only call encodes zero argument bytes); the callers validate the public limits.
    /// </summary>
    internal static ABIStatus TryBegin(
        ABISchema schema,
        Span<byte> destination,
        Span<byte> guard,
        bool writes,
        Span<ABIEncodeFrame> frames,
        int maxBytes,
        int maxDepth,
        int maxArray,
        int maxValues,
        int maxWork,
        out ABIEncoder encoder)
    {
        encoder = default;

        // Spec 7.4: scratch capacity is checked before success. A container at depth d occupies frame d-1, the depth limit stops anything
        // deeper than MaxValueDepth, and the schema's deepest container path bounds the rest: that many frames always suffice (at least 1).
        if (frames.Length < Math.Min(maxDepth, schema.MaxContainerDepth))
        {
            return ABIStatus.InsufficientScratch;
        }

        ref readonly ABISchemaNode root = ref schema.Root;
        if (root.HeadSize > maxBytes)
        {
            return ABIStatus.InputTooLarge;
        }

        if (writes && root.HeadSize > destination.Length)
        {
            return ABIStatus.InsufficientDestination;
        }

        // Initialise every frame that can ever be used (at most one per depth level) so no stale caller content is ever visible, then
        // the root frame. The root value is entered: one value, one work unit (as in the decoder). Every budget is at least 1.
        int epoch = unchecked(frames[0].Epoch + 1); // wraps by design: only inequality with the previous owner's value matters
        frames.Slice(0, Math.Min(frames.Length, maxDepth)).Clear();
        frames[0] = new ABIEncodeFrame
        {
            Node = ABISchema.RootNodeId,
            ValueStart = 0,
            HeadBase = 0,
            HeadEnd = root.HeadSize,
            HeadCursor = 0,
            TailCursor = root.HeadSize,
            NextChild = 0,
            ChildCount = root.Count,
            Top = 0,
            Values = 1,
            Work = 1,
            State = (int)EncoderState.Open,
            Epoch = epoch,
        };

        encoder = new ABIEncoder
        {
            _schema = schema,
            _destination = destination,
            _guard = guard,
            _frames = frames,
            _writes = writes,
            _maxBytes = maxBytes,
            _maxDepth = maxDepth,
            _maxArray = maxArray,
            _maxValues = maxValues,
            _maxWork = maxWork,
            _epoch = epoch,
        };
        return ABIStatus.Success;
    }

    // ------------------------------------------------------------------ typed leaves

    internal bool WriteUInt64(ulong value, out ABIStatus status) => Done(UInt64Core(value), out status);

    internal bool WriteInt64(long value, out ABIStatus status) => Done(Int64Core(value), out status);

    internal bool WriteUInt256Word(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => Done(WordCore(ABITypeKind.UInt, word32), out status);

    internal bool WriteInt256Word(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => Done(WordCore(ABITypeKind.Int, word32), out status);

    internal bool WriteUnsignedScaledWord(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => Done(WordCore(ABITypeKind.UFixed, word32), out status);

    internal bool WriteSignedScaledWord(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => Done(WordCore(ABITypeKind.Fixed, word32), out status);

    internal bool WriteAddress(scoped ReadOnlySpan<byte> address20, out ABIStatus status) => Done(AddressCore(address20), out status);

    internal bool WriteBool(bool value, out ABIStatus status) => Done(BoolCore(value), out status);

    internal bool WriteFixedBytes(scoped ReadOnlySpan<byte> bytes, out ABIStatus status) => Done(FixedBytesCore(bytes), out status);

    internal bool WriteFunction(scoped ReadOnlySpan<byte> address20, uint selector, out ABIStatus status) => Done(FunctionCore(address20, selector), out status);

    internal bool WriteBytes(scoped ReadOnlySpan<byte> bytes, out ABIStatus status) => Done(BlobCore(utf8: false, bytes), out status);

    internal bool WriteStringUtf8(scoped ReadOnlySpan<byte> utf8Bytes, out ABIStatus status) => Done(BlobCore(utf8: true, utf8Bytes), out status);

    // ------------------------------------------------------------------ containers

    internal bool BeginTuple(out ABIStatus status) => Done(BeginCore(ABITypeKind.Tuple, 0), out status);

    internal bool BeginFixedArray(out ABIStatus status) => Done(BeginCore(ABITypeKind.FixedArray, 0), out status);

    internal bool BeginDynamicArray(int elementCount, out ABIStatus status) => Done(BeginCore(ABITypeKind.DynamicArray, elementCount), out status);

    internal bool EndContainer(out ABIStatus status) => Done(EndCore(), out status);

    internal bool Finish(out int length, out ABIStatus status)
    {
        length = 0;
        ABIStatus result = FinishCore(out int end);
        if (result == ABIStatus.Success)
        {
            length = end;
        }

        return Done(result, out status);
    }

    // ------------------------------------------------------------------ operations

    /// <summary>Records the result; any failure of an open encoder faults it for good. A rejected call on a default, completed or faulted encoder changes nothing.</summary>
    private bool Done(ABIStatus result, out ABIStatus status)
    {
        status = result;
        if (result == ABIStatus.Success)
        {
            return true;
        }

        if (State == EncoderState.Open)
        {
            State = EncoderState.Faulted;
        }

        return false;
    }

    /// <summary>State and arity checks shared by every operation that adds a value to the innermost container; yields the schema node of the next slot.</summary>
    private ABIStatus FindSlot(out int childId)
    {
        childId = 0;
        if (State != EncoderState.Open)
        {
            return ABIStatus.InvalidWriterState;
        }

        // The frames are caller-writable memory, so nothing read from them is trusted: every value used as an index or an offset below is
        // range-checked first (a forged frame gets InvalidWriterState, never an exception or a write outside the destination), and the
        // arithmetic that adds to forged fields is unchecked so that it can only produce a value that the next check refuses.
        int top = Top;
        if (unchecked((uint)top >= (uint)_frames.Length))
        {
            return ABIStatus.InvalidWriterState;
        }

        ref ABIEncodeFrame frame = ref _frames[top];
        if (frame.NextChild >= frame.ChildCount)
        {
            // One value more than the tuple arity, fixed length or announced dynamic count.
            return ABIStatus.ElementLimitExceeded;
        }

        ABISchema schema = _schema!;
        if (unchecked((uint)frame.Node >= (uint)schema.NodeCount))
        {
            return ABIStatus.InvalidWriterState;
        }

        ref readonly ABISchemaNode container = ref schema.GetNode(frame.Node);
        if (container.Kind == ABITypeKind.Tuple)
        {
            ReadOnlySpan<ABITupleChild> members = schema.GetTupleChildren(in container);
            if (unchecked((uint)frame.NextChild >= (uint)members.Length))
            {
                return ABIStatus.InvalidWriterState;
            }

            childId = members[frame.NextChild].NodeId;
        }
        else
        {
            childId = container.Element; // -1 for a node that is not an array: refused below
        }

        if (unchecked((uint)childId >= (uint)schema.NodeCount))
        {
            return ABIStatus.InvalidWriterState;
        }

        if (_writes)
        {
            // The child's head slot must lie inside the destination, so slicing at the cursor cannot fail.
            ref readonly ABISchemaNode child = ref schema.GetNode(childId);
            int width = child.IsDynamic ? Word : child.HeadWidth;
            if (unchecked((uint)frame.HeadCursor > (uint)(_destination.Length - width)))
            {
                return ABIStatus.InvalidWriterState;
            }
        }

        return ABIStatus.Success;
    }

    /// <summary>State, arity and kind checks shared by every operation that adds a value of exactly one kind to the innermost container.</summary>
    private ABIStatus Slot(ABITypeKind expected, out int childId)
    {
        ABIStatus status = FindSlot(out childId);
        if (status != ABIStatus.Success)
        {
            return status;
        }

        return _schema!.GetNode(childId).Kind == expected ? ABIStatus.Success : ABIStatus.TypeMismatch;
    }

    /// <summary>Depth, value budget and work budget for one value entered under the innermost container (the root tuple is depth 1).</summary>
    private ABIStatus ChargeValue()
    {
        if (Top + 2 > _maxDepth)
        {
            return ABIStatus.DepthLimitExceeded;
        }

        if (Values >= _maxValues || Work >= _maxWork)
        {
            return ABIStatus.WorkLimitExceeded;
        }

        Values++;
        Work++;
        return ABIStatus.Success;
    }

    /// <summary>One work unit for a 32-byte word written or examined.</summary>
    private ABIStatus ChargeWord()
    {
        if (Work >= _maxWork)
        {
            return ABIStatus.WorkLimitExceeded;
        }

        Work++;
        return ABIStatus.Success;
    }

    /// <summary>
    /// Proves <paramref name="size"/> bytes fit at <paramref name="position"/> under the configured maximum, then under the
    /// destination (the limit is reported first). Subtraction only; <paramref name="position"/> is range-checked first because it may come from forged scratch.
    /// </summary>
    private ABIStatus Reserve(int position, long size)
    {
        if (unchecked((uint)position > (uint)_maxBytes))
        {
            return ABIStatus.InvalidWriterState; // a position that is no position of this document: forged scratch
        }

        if (size > _maxBytes - position)
        {
            return ABIStatus.InputTooLarge;
        }

        if (_writes && size > _destination.Length - position)
        {
            return ABIStatus.InsufficientDestination;
        }

        return ABIStatus.Success;
    }

    /// <summary>
    /// Common prologue of a static primitive: state, arity, kind, depth, value and word budgets. <paramref name="slot"/> is the head position the
    /// word goes to, <paramref name="width"/> the schema width of the slot, <paramref name="top"/> the (range-checked) innermost frame index.
    /// One pass over locals: the frames are caller-writable memory, so every store through them would otherwise force the configuration fields
    /// to be reloaded and every index re-checked. The checks and their order are those of <see cref="FindSlot"/>, the kind check, then
    /// <see cref="ChargeValue"/> and <see cref="ChargeWord"/> merged (the value and word units are charged together, so a failed word unit
    /// leaves the counters unchanged; the writer is faulted either way and no public member reads them): keep the two prologues in step.
    /// </summary>
    private ABIStatus PrepareScalar(ABITypeKind kind, out int slot, out int width, out int top)
    {
        slot = 0;
        width = 0;
        top = 0;
        Span<ABIEncodeFrame> frames = _frames;
        if (frames.IsEmpty)
        {
            return ABIStatus.InvalidWriterState;
        }

        ref ABIEncodeFrame root = ref frames[0];
        if (root.Epoch != _epoch || root.State != (int)EncoderState.Open)
        {
            return ABIStatus.InvalidWriterState;
        }

        top = root.Top;
        if (unchecked((uint)top >= (uint)frames.Length))
        {
            return ABIStatus.InvalidWriterState;
        }

        ref ABIEncodeFrame frame = ref frames[top];
        if (frame.NextChild >= frame.ChildCount)
        {
            return ABIStatus.ElementLimitExceeded;
        }

        ABISchema schema = _schema!;
        if (unchecked((uint)frame.Node >= (uint)schema.NodeCount))
        {
            return ABIStatus.InvalidWriterState;
        }

        ref readonly ABISchemaNode container = ref schema.GetNode(frame.Node);
        int childId;
        if (container.Kind == ABITypeKind.Tuple)
        {
            if (unchecked((uint)frame.NextChild >= (uint)container.Count))
            {
                return ABIStatus.InvalidWriterState;
            }

            childId = schema.GetTupleChildren(in container)[frame.NextChild].NodeId;
        }
        else
        {
            childId = container.Element;
        }

        if (unchecked((uint)childId >= (uint)schema.NodeCount))
        {
            return ABIStatus.InvalidWriterState;
        }

        ref readonly ABISchemaNode child = ref schema.GetNode(childId);
        int slotAt = frame.HeadCursor;
        if (_writes && unchecked((uint)slotAt > (uint)(_destination.Length - child.HeadWidth)))
        {
            return ABIStatus.InvalidWriterState;
        }

        if (child.Kind != kind)
        {
            return ABIStatus.TypeMismatch;
        }

        // Depth, then the value budget, then the work budget for the value entry and its word (the two charges of the decoder: two units).
        if (top + 2 > _maxDepth)
        {
            return ABIStatus.DepthLimitExceeded;
        }

        int values = root.Values;
        int work = root.Work;
        if (values >= _maxValues || work >= _maxWork - 1)
        {
            return ABIStatus.WorkLimitExceeded;
        }

        root.Values = values + 1;
        root.Work = work + 2;
        slot = slotAt;
        width = child.Width;
        return ABIStatus.Success;
    }

    /// <summary>The scalar is complete: the parent's head cursor moves one word on.</summary>
    private void CommitScalar(int top)
    {
        ref ABIEncodeFrame frame = ref _frames[top];
        frame.HeadCursor = unchecked(frame.HeadCursor + Word);
        frame.NextChild++; // below ChildCount (PrepareScalar checked it), so it cannot overflow
    }

    private ABIStatus UInt64Core(ulong value)
    {
        ABIStatus status = PrepareScalar(ABITypeKind.UInt, out int slot, out int width, out int top);
        if (status != ABIStatus.Success)
        {
            return status;
        }

        if (width < 64 && (value >> width) != 0)
        {
            return ABIStatus.ValueOutOfRange;
        }

        if (_writes)
        {
            Span<byte> word = _destination.Slice(slot, Word);
            word.Slice(0, 24).Clear();
            BinaryPrimitives.WriteUInt64BigEndian(word.Slice(24), value);
        }

        CommitScalar(top);
        return ABIStatus.Success;
    }

    private ABIStatus Int64Core(long value)
    {
        ABIStatus status = PrepareScalar(ABITypeKind.Int, out int slot, out int width, out int top);
        if (status != ABIStatus.Success)
        {
            return status;
        }

        if (width < 64)
        {
            long limit = 1L << (width - 1);
            if (value < -limit || value >= limit)
            {
                return ABIStatus.ValueOutOfRange;
            }
        }

        if (_writes)
        {
            Span<byte> word = _destination.Slice(slot, Word);
            word.Slice(0, 24).Fill(value < 0 ? (byte)0xff : (byte)0x00);
            BinaryPrimitives.WriteInt64BigEndian(word.Slice(24), value);
        }

        CommitScalar(top);
        return ABIStatus.Success;
    }

    /// <summary>A caller-supplied full word for an integer or fixed-point slot: it must be the canonical encoding of a value of the declared width.</summary>
    private ABIStatus WordCore(ABITypeKind kind, scoped ReadOnlySpan<byte> word32)
    {
        ABIStatus status = PrepareScalar(kind, out int slot, out int width, out int top);
        if (status != ABIStatus.Success)
        {
            return status;
        }

        if (word32.Length != Word)
        {
            return ABIStatus.InvalidArgument;
        }

        // A 256-bit integer has no extension bits: every 32-byte word is canonical, so its limbs need not be read.
        if (!ABIWordRun.AcceptsEveryWord(kind, width))
        {
            ABIWord.TryLoad(word32, 0, out ABIWord word);
            bool signed = kind == ABITypeKind.Int || kind == ABITypeKind.Fixed;
            if (!(signed ? word.IsSignExtended(width) : word.HighBitsAreZero(Word * 8 - width)))
            {
                return ABIStatus.ValueOutOfRange;
            }
        }

        if (word32.Overlaps(_guard))
        {
            return ABIStatus.SourceOverlapsDestination;
        }

        if (_writes)
        {
            word32.CopyTo(_destination.Slice(slot, Word));
        }

        CommitScalar(top);
        return ABIStatus.Success;
    }

    private ABIStatus AddressCore(scoped ReadOnlySpan<byte> address20)
    {
        ABIStatus status = PrepareScalar(ABITypeKind.Address, out int slot, out _, out int top);
        if (status != ABIStatus.Success)
        {
            return status;
        }

        if (address20.Length != 20)
        {
            return ABIStatus.InvalidArgument;
        }

        if (address20.Overlaps(_guard))
        {
            return ABIStatus.SourceOverlapsDestination;
        }

        if (_writes)
        {
            Span<byte> word = _destination.Slice(slot, Word);
            word.Slice(0, 12).Clear();
            address20.CopyTo(word.Slice(12));
        }

        CommitScalar(top);
        return ABIStatus.Success;
    }

    private ABIStatus BoolCore(bool value)
    {
        ABIStatus status = PrepareScalar(ABITypeKind.Bool, out int slot, out _, out int top);
        if (status != ABIStatus.Success)
        {
            return status;
        }

        if (_writes)
        {
            Span<byte> word = _destination.Slice(slot, Word);
            word.Slice(0, 31).Clear();
            word[31] = value ? (byte)1 : (byte)0;
        }

        CommitScalar(top);
        return ABIStatus.Success;
    }

    private ABIStatus FixedBytesCore(scoped ReadOnlySpan<byte> bytes)
    {
        ABIStatus status = PrepareScalar(ABITypeKind.FixedBytes, out int slot, out int width, out int top);
        if (status != ABIStatus.Success)
        {
            return status;
        }

        // Exactly M bytes: a shorter or longer source is a caller bug, never silently padded or cut.
        if (bytes.Length != width)
        {
            return ABIStatus.InvalidArgument;
        }

        if (bytes.Overlaps(_guard))
        {
            return ABIStatus.SourceOverlapsDestination;
        }

        if (_writes)
        {
            Span<byte> word = _destination.Slice(slot, Word);
            bytes.CopyTo(word);
            word.Slice(width).Clear();
        }

        CommitScalar(top);
        return ABIStatus.Success;
    }

    private ABIStatus FunctionCore(scoped ReadOnlySpan<byte> address20, uint selector)
    {
        ABIStatus status = PrepareScalar(ABITypeKind.Function, out int slot, out _, out int top);
        if (status != ABIStatus.Success)
        {
            return status;
        }

        if (address20.Length != 20)
        {
            return ABIStatus.InvalidArgument;
        }

        if (address20.Overlaps(_guard))
        {
            return ABIStatus.SourceOverlapsDestination;
        }

        if (_writes)
        {
            Span<byte> word = _destination.Slice(slot, Word);
            address20.CopyTo(word);
            BinaryPrimitives.WriteUInt32BigEndian(word.Slice(20), selector);
            word.Slice(24).Clear();
        }

        CommitScalar(top);
        return ABIStatus.Success;
    }

    /// <summary>
    /// Dynamic <c>bytes</c> or <c>string</c>: pointer in the parent's head, then [length word][data][zero padding] at the parent's tail
    /// cursor. The bytes entry point (<paramref name="utf8"/> false) fills a <c>bytes</c> slot with any bytes and a <c>string</c> slot with
    /// strictly valid UTF-8 only; the UTF-8 entry point fills a <c>string</c> slot only. Every <c>string</c> write is validated, whatever
    /// the entry point, so the encoder never emits a <c>string</c> that is not UTF-8.
    /// </summary>
    private ABIStatus BlobCore(bool utf8, scoped ReadOnlySpan<byte> data)
    {
        ABIStatus status = FindSlot(out int childId);
        if (status != ABIStatus.Success)
        {
            return status;
        }

        ABITypeKind kind = _schema!.GetNode(childId).Kind;
        if (utf8 ? kind != ABITypeKind.String : (kind != ABITypeKind.Bytes && kind != ABITypeKind.String))
        {
            return ABIStatus.TypeMismatch;
        }

        status = ChargeValue();
        if (status == ABIStatus.Success)
        {
            status = ChargeWord(); // the pointer word in the parent's head
        }

        if (status == ABIStatus.Success)
        {
            status = ChargeWord(); // the length word
        }

        int padding = (Word - (data.Length & (Word - 1))) & (Word - 1);
        if (status == ABIStatus.Success && padding != 0)
        {
            status = ChargeWord(); // the partially used last word (data words themselves are free)
        }

        if (status != ABIStatus.Success)
        {
            return status;
        }

        ref ABIEncodeFrame frame = ref _frames[Top];
        int at = frame.TailCursor;

        // long: data.Length up to int.MaxValue plus 63 does not fit an int, and nothing is narrowed before the bound is proven.
        status = Reserve(at, Word + (long)data.Length + padding);
        if (status != ABIStatus.Success)
        {
            return status;
        }

        if (data.Overlaps(_guard))
        {
            return ABIStatus.SourceOverlapsDestination;
        }

        // Strict validation before the first write; capacity was proven first, so this scan is bounded by the caller's own buffer.
        if (kind == ABITypeKind.String && !Utf8.IsValid(data))
        {
            return ABIStatus.InvalidUtf8;
        }

        if (_writes)
        {
            WriteWordNumber(frame.HeadCursor, unchecked((uint)(at - frame.HeadBase)));
            WriteWordNumber(at, unchecked((uint)data.Length));
            Span<byte> payload = _destination.Slice(at + Word, data.Length + padding);
            data.CopyTo(payload);
            payload.Slice(data.Length).Clear();
        }

        frame.TailCursor = at + Word + data.Length + padding;
        frame.HeadCursor = unchecked(frame.HeadCursor + Word);
        frame.NextChild++;
        return ABIStatus.Success;
    }

    /// <summary>Opens a tuple, fixed array or dynamic array: reserves its whole head, writes the parent's pointer and a dynamic array's count, pushes a frame.</summary>
    private ABIStatus BeginCore(ABITypeKind kind, int elementCount)
    {
        ABIStatus status = Slot(kind, out int childId);
        if (status != ABIStatus.Success)
        {
            return status;
        }

        if (elementCount < 0)
        {
            return ABIStatus.InvalidArgument;
        }

        status = ChargeValue();
        if (status != ABIStatus.Success)
        {
            return status;
        }

        if (Top + 1 >= _frames.Length)
        {
            return ABIStatus.InsufficientScratch;
        }

        ref ABIEncodeFrame parent = ref _frames[Top];
        ref readonly ABISchemaNode node = ref _schema!.GetNode(childId);
        int start;
        int headBase;
        int headBytes;
        int count;
        if (node.IsDynamic)
        {
            status = ChargeWord(); // the pointer word in the parent's head
            if (status != ABIStatus.Success)
            {
                return status;
            }

            start = parent.TailCursor;
            if (kind == ABITypeKind.DynamicArray)
            {
                status = ChargeWord(); // the count word
                if (status != ABIStatus.Success)
                {
                    return status;
                }

                // The count is bounded before it is multiplied by the element head width.
                if (elementCount > _maxArray)
                {
                    return ABIStatus.ElementLimitExceeded;
                }

                long arrayHead = (long)elementCount * node.ElementHeadWidth;
                status = Reserve(start, Word + arrayHead);
                if (status != ABIStatus.Success)
                {
                    return status;
                }

                count = elementCount;
                headBase = start + Word;
                headBytes = (int)arrayHead;
            }
            else
            {
                status = Reserve(start, node.HeadSize);
                if (status != ABIStatus.Success)
                {
                    return status;
                }

                count = node.Count;
                headBase = start;
                headBytes = node.HeadSize;
            }
        }
        else
        {
            // A static tuple or fixed array lives inside the parent's already reserved head.
            count = node.Count;
            start = parent.HeadCursor;
            headBase = start;
            headBytes = node.HeadSize;
        }

        if (_writes && node.IsDynamic)
        {
            WriteWordNumber(parent.HeadCursor, unchecked((uint)(start - parent.HeadBase)));
            if (kind == ABITypeKind.DynamicArray)
            {
                WriteWordNumber(start, unchecked((uint)elementCount));
            }
        }

        int headEnd = unchecked(headBase + headBytes); // a forged parent cursor can make this wrap; the next operation's range checks refuse the result
        _frames[Top + 1] = new ABIEncodeFrame
        {
            Node = childId,
            ValueStart = start,
            HeadBase = headBase,
            HeadEnd = headEnd,
            HeadCursor = headBase,
            TailCursor = headEnd,
            NextChild = 0,
            ChildCount = count,
        };
        Top++;
        return ABIStatus.Success;
    }

    private ABIStatus EndCore()
    {
        if (State != EncoderState.Open || Top == 0)
        {
            // The root tuple is closed by Finish, not by EndContainer.
            return ABIStatus.InvalidWriterState;
        }

        // Both frames come from caller-writable scratch: range-check what is used as an index before using it.
        int top = Top;
        if (unchecked((uint)top >= (uint)_frames.Length))
        {
            return ABIStatus.InvalidWriterState;
        }

        ref ABIEncodeFrame frame = ref _frames[top];
        if (frame.NextChild != frame.ChildCount)
        {
            return ABIStatus.IncompleteWrite;
        }

        if (frame.HeadCursor != frame.HeadEnd || unchecked((uint)frame.Node >= (uint)_schema!.NodeCount))
        {
            return ABIStatus.InvalidWriterState; // unreachable for an honest frame: the schema invariants make the head exactly consumed
        }

        ref ABIEncodeFrame parent = ref _frames[top - 1];
        ref readonly ABISchemaNode node = ref _schema.GetNode(frame.Node);
        if (parent.NextChild >= parent.ChildCount)
        {
            return ABIStatus.InvalidWriterState; // the parent must be waiting for exactly this child
        }

        if (node.IsDynamic)
        {
            parent.TailCursor = frame.TailCursor;
            parent.HeadCursor = unchecked(parent.HeadCursor + Word);
        }
        else
        {
            parent.HeadCursor = unchecked(parent.HeadCursor + node.HeadWidth);
        }

        parent.NextChild++; // below ChildCount, so it cannot overflow
        Top = top - 1;
        return ABIStatus.Success;
    }

    private ABIStatus FinishCore(out int end)
    {
        end = 0;
        if (State != EncoderState.Open)
        {
            return ABIStatus.InvalidWriterState;
        }

        ref ABIEncodeFrame root = ref _frames[0];
        if (Top != 0)
        {
            return ABIStatus.IncompleteWrite;
        }

        int limit = _writes ? Math.Min(_maxBytes, _destination.Length) : _maxBytes;
        if (unchecked((uint)root.TailCursor > (uint)limit))
        {
            return ABIStatus.InvalidWriterState; // the length returned below must be a real position, not caller-written scratch content
        }

        if (root.NextChild != root.ChildCount)
        {
            return ABIStatus.IncompleteWrite;
        }

        end = root.TailCursor;
        State = EncoderState.Completed;
        return ABIStatus.Success;
    }

    /// <summary>Writes a 32-byte big-endian word holding a number that fits 32 bits (an offset, a count or a length).</summary>
    private void WriteWordNumber(int position, uint value)
    {
        Span<byte> word = _destination.Slice(position, Word);
        word.Slice(0, 28).Clear();
        BinaryPrimitives.WriteUInt32BigEndian(word.Slice(28), value);
    }
}
