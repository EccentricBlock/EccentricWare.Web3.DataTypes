namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// One open tuple-like region of the encoder (spec 7.3): plain integers, no references, so a caller-supplied span of them can
/// live on the stack. <see cref="ABIWriteFrame"/> and <see cref="ABIMeasureFrame"/> each wrap exactly one of these, so the writer
/// and the measurer run the same state machine. Offsets are absolute positions in the document being built (coordinate zero is
/// the first byte of the arguments, never of a call selector). Thirteen <see cref="int"/>s (52 bytes); frame 0 additionally carries the
/// writer-wide state (see the last five fields).
/// </summary>
internal struct ABIEncodeFrame
{
    /// <summary>Schema node of the tuple, fixed array or dynamic array.</summary>
    internal int Node;

    /// <summary>
    /// Start of the whole value (for a dynamic array, its count word). Written when a container opens and never read by the encoder: kept because spec 7.3 lists
    /// "value-start position" among the state every writer frame MUST retain (docs/abi-design-notes.md, row 74); remove it only with a spec change.
    /// </summary>
    internal int ValueStart;

    /// <summary>Base for dynamic child offsets: the tuple or fixed-array start, or the byte after a dynamic array's count word.</summary>
    internal int HeadBase;

    /// <summary>Exclusive end of this region's head (<see cref="HeadBase"/> plus its head size); the whole head is reserved when the container opens.</summary>
    internal int HeadEnd;

    /// <summary>Location of the next child's head slot; advances by the child's head width.</summary>
    internal int HeadCursor;

    /// <summary>First free byte after the dynamic children written so far (where the next dynamic child will be placed).</summary>
    internal int TailCursor;

    /// <summary>Ordinal of the next member or element; advances even for zero-width children.</summary>
    internal int NextChild;

    /// <summary>Tuple arity, fixed-array length, or the element count announced for a dynamic array.</summary>
    internal int ChildCount;

    // The five fields below are writer-wide state and are meaningful in frame 0 only (the root frame). They live in the caller's scratch,
    // not in the ref struct, so that a struct copy of a writer shares them with the original instead of diverging (a copy cannot bypass
    // a budget or hide a fault); unused in frames 1 and above.

    /// <summary>Frame 0: index of the innermost open container's frame (0 when only the root is open).</summary>
    internal int Top;

    /// <summary>Frame 0: values entered so far, the root included.</summary>
    internal int Values;

    /// <summary>Frame 0: work units charged so far.</summary>
    internal int Work;

    /// <summary>Frame 0: the state machine position (see <c>ABIEncoder.EncoderState</c>).</summary>
    internal int State;

    /// <summary>Frame 0: generation of the writer that owns these frames. <c>TryBegin</c> increments it, so a stale writer value whose scratch was reused for a new writer reads as invalid instead of acting on the new writer's state.</summary>
    internal int Epoch;
}
