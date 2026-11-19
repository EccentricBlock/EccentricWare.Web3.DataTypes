namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// One open tuple-like region of the iterative validator (spec 6.2): plain integers, no references, so a fixed-capacity array of
/// them lives on the stack. Offsets are absolute positions in the root arguments document. Mapping to the spec table:
/// <c>HeadSize</c> is <see cref="HeadEnd"/> minus <see cref="HeadBase"/>, <c>Depth</c> is the frame's index plus one, and
/// <c>PendingChild</c> and <c>State</c> are implicit in <see cref="NextChild"/> and the loop (a child is pending exactly while a
/// frame above this one exists).
/// </summary>
internal struct ABIValidationFrame
{
    /// <summary>Schema node of the tuple, fixed array or dynamic array.</summary>
    internal int Node;

    /// <summary>Start of the whole value (for a dynamic array, its count word).</summary>
    internal int ValueStart;

    /// <summary>Base for dynamic child offsets: the tuple or fixed-array start, or the byte after a dynamic array's count word.</summary>
    internal int HeadBase;

    /// <summary>Exclusive end of this region's head (<see cref="HeadBase"/> plus its head size).</summary>
    internal int HeadEnd;

    /// <summary>Location of the next child's head slot; advances by the child's head width.</summary>
    internal int HeadCursor;

    /// <summary>The canonical position of the next dynamic child's tail; after the last child it is the value's exclusive end.</summary>
    internal int TailCursor;

    /// <summary>Ordinal of the next member or element; advances even for zero-width children.</summary>
    internal int NextChild;

    /// <summary>Tuple arity, fixed-array length, or the validated dynamic-array count.</summary>
    internal int ChildCount;
}
