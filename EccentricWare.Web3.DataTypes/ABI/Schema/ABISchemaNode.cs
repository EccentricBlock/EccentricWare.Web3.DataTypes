namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// One compiled schema node. Plain unmanaged data, copied by value or read through <c>ref readonly</c>. Field meaning and the
/// invariants that hold for every node of a compiled <see cref="ABISchema"/> are documented in the header of ABISchema.cs.
/// </summary>
internal readonly struct ABISchemaNode
{
    internal ABISchemaNode(
        ABITypeKind kind,
        bool isDynamic,
        int width,
        int scale,
        int count,
        int element,
        int childStart,
        int staticSize,
        int headWidth,
        int headSize,
        int elementHeadWidth)
    {
        Kind = kind;
        IsDynamic = isDynamic;
        AcceptsEveryWord = ABIWordRun.AcceptsEveryWord(kind, width);
        Width = width;
        Scale = scale;
        Count = count;
        Element = element;
        ChildStart = childStart;
        StaticSize = staticSize;
        HeadWidth = headWidth;
        HeadSize = headSize;
        ElementHeadWidth = elementHeadWidth;
    }

    /// <summary>The node's ABI kind.</summary>
    internal readonly ABITypeKind Kind;

    /// <summary>True for bytes, string, T[], a T[k] of dynamic elements, and a tuple with a dynamic member.</summary>
    internal readonly bool IsDynamic;

    /// <summary>
    /// True for an elementary node whose strict rule accepts every 32-byte word (uint256, int256, bytes32 and the 256-bit fixed-point kinds), so
    /// the validator and the writer need not read the word. Precomputed here, once per node, because evaluating it per word cost more than it saved
    /// for the kinds that do have a rule (docs/abi-performance.md).
    /// </summary>
    internal readonly bool AcceptsEveryWord;

    /// <summary>Bit width M of UInt/Int/Fixed/UFixed, byte length M of FixedBytes, otherwise 0.</summary>
    internal readonly int Width;

    /// <summary>Decimal precision N of Fixed/UFixed, otherwise 0.</summary>
    internal readonly int Scale;

    /// <summary>Tuple: arity. FixedArray: k (at least 1). DynamicArray: <see cref="ABISchema.DynamicCount"/>. Otherwise 0.</summary>
    internal readonly int Count;

    /// <summary>Array kinds: node id of the element type. Otherwise <see cref="ABISchema.NoNode"/>.</summary>
    internal readonly int Element;

    /// <summary>Tuple: index of the first of its <see cref="Count"/> entries in the schema's child table. Otherwise 0 (unused).</summary>
    internal readonly int ChildStart;

    /// <summary>Encoded size in bytes of a static node (zero for an empty tuple); <see cref="ABISchema.NotStatic"/> for a dynamic node.</summary>
    internal readonly int StaticSize;

    /// <summary>Bytes this node occupies in its parent's head: 32 when dynamic, otherwise <see cref="StaticSize"/>.</summary>
    internal readonly int HeadWidth;

    /// <summary>Tuple and FixedArray: size of the node's own head region (sum of member head widths, or k times the element head width). Otherwise 0.</summary>
    internal readonly int HeadSize;

    /// <summary>Array kinds: head width of the element type (the stride of the element head table). Otherwise 0.</summary>
    internal readonly int ElementHeadWidth;
}

/// <summary>
/// One member position of a compiled tuple. Plain unmanaged data. For a tuple <c>T</c> and position <c>p</c>, the entry gives
/// the member's node, its head displacement and the position of the next dynamic member.
/// </summary>
internal readonly struct ABITupleChild
{
    internal ABITupleChild(int nodeId, int headOffset, int nextDynamicSibling)
    {
        NodeId = nodeId;
        HeadOffset = headOffset;
        NextDynamicSibling = nextDynamicSibling;
    }

    /// <summary>Node id of the member's type.</summary>
    internal readonly int NodeId;

    /// <summary>Byte displacement of the member's head slot from the start of the tuple's head region.</summary>
    internal readonly int HeadOffset;

    /// <summary>
    /// Position (not node id) of the first dynamic member after this one in the same tuple, or
    /// <see cref="ABISchema.NoSibling"/> when there is none. Defined for every position, static or dynamic.
    /// </summary>
    internal readonly int NextDynamicSibling;
}
