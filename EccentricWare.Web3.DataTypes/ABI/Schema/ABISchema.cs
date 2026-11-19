// =====================================================================================================================
// ABISchema: compiled, immutable ABI schema (spec section 4 and 5.3).
//
// PUBLIC SURFACE: CanonicalType, ArgumentCount, IsDynamic, ToString. Everything else is internal and is the contract that the
// decoder, locator and writer program against. Instances are created only by ABISchemaCompiler; there is no public
// constructor, and no member exposes the private tables mutably.
//
// NODE TABLE (internal)
//   * Node ids are indexes into one private array, assigned in PRE-ORDER: the root tuple is node 0
//     (ABISchema.RootNodeId) and every node's children have strictly larger ids than the node itself. Walking ids downwards
//     is therefore a valid bottom-up order, and the table cannot contain a cycle.
//   * Read nodes with GetNode(id) (ref readonly, no copy, no allocation). Ids come from the table itself (Root,
//     ABISchemaNode.Element, ABITupleChild.NodeId), so they are always in range; an id from anywhere else throws
//     IndexOutOfRangeException (the CLR bounds check), it never reads foreign memory.
//   * Today every type occurrence has its own node (no subtree sharing), but consumers must not rely on a node having a
//     unique parent: the compiler may share identical subtrees later without changing this contract.
//
// ABISchemaNode fields, by Kind:
//   Kind              : ABITypeKind.
//   IsDynamic         : bytes, string, T[]: true. T[k]: element.IsDynamic. Tuple: any member dynamic. Others: false.
//   Width             : UInt/Int/Fixed/UFixed: bit width M (multiple of 8, 8..256). FixedBytes: byte length M (1..32). Else 0.
//   Scale             : Fixed/UFixed: decimals N (1..80). Else 0.
//   Count             : Tuple: arity (>= 0). FixedArray: k (>= 1). DynamicArray: DynamicCount (-1). Else 0.
//   Element           : FixedArray/DynamicArray: element node id. Else NoNode (-1).
//   ChildStart        : Tuple: index of the first of Count entries in the child table (see GetTupleChildren). Else 0.
//   StaticSize        : IsDynamic false: encoded byte size (a primitive is 32, empty tuple 0, T[k] k * StaticSize(element),
//                       tuple sum of member StaticSize). IsDynamic true: NotStatic (-1). Never use it as a width when dynamic.
//   HeadWidth         : bytes occupied in the PARENT's head: 32 when IsDynamic, else StaticSize (may be 0 or more than 32).
//   HeadSize          : Tuple/FixedArray: size of the node's own head region = sum of member HeadWidth / k * element
//                       HeadWidth. Equals StaticSize when the node is static. DynamicArray: 0 (the head size is count *
//                       ElementHeadWidth and count is known only at run time). Primitives: 0.
//   ElementHeadWidth  : array kinds: HeadWidth of the element type, the stride of the element head table. Else 0.
//
// TUPLE CHILD TABLE (internal): one array of ABITupleChild shared by all tuples. GetTupleChildren(tupleNodeId) returns exactly
// Count entries for that tuple, in declaration order (empty span for an empty tuple or a non-tuple). For position p:
//   NodeId             : node id of the member type.
//   HeadOffset         : displacement of the member's head slot from the tuple's head base (HeadOffset[0] is 0 and
//                        HeadOffset[p+1] = HeadOffset[p] + HeadWidth(member p); the last offset + width = HeadSize).
//   NextDynamicSibling : position of the first dynamic member after p, or NoSibling (-1). Computed in a reverse pass at
//                        compile time, so a locator finds the next dynamic tail without scanning static members: the offset
//                        WORD of that sibling is at headBase + children[NextDynamicSibling].HeadOffset.
//
// ARRAY RULES (there is no per-element table; the locator derives everything from the node):
//   * FixedArray: k = Count elements, element head slots at i * ElementHeadWidth from the ARRAY START (offsets of dynamic
//     elements are relative to the array start). Static elements are inline at i * StaticSize(element).
//   * DynamicArray: the value starts with a 32-byte count; element head slots start at valueStart + 32 and dynamic element
//     offsets are relative to that point (the byte after the count word). The head size is count * ElementHeadWidth: compute it in
//     long, and bound count by MaxArrayElements BEFORE multiplying. ElementHeadWidth can be 0 (for example ()[]), so a count can
//     NOT be bounded by the remaining input bytes; only the element/work limits bound it.
//   * "Next dynamic sibling" exists in the table for TUPLE positions only. For an array with dynamic elements it is simply element
//     i + 1 (offset word at head slot i + 1); for static elements there is none.
//
// PITFALLS
//   * Width is in BITS for UInt/Int/Fixed/UFixed and in BYTES for FixedBytes.
//   * ABITupleChild.NextDynamicSibling is a tuple POSITION, not a node id; ABITupleChild.NodeId is a node id.
//   * ChildStart is NOT monotonic in node id (child blocks are appended when a tuple finishes, nodes are numbered pre-order).
//   * StaticSize of a dynamic node is -1: never add it to an offset.
//
// INVARIANTS (all established by the compiler, all arithmetic checked there):
//   * every HeadOffset, HeadSize, StaticSize, HeadWidth and ElementHeadWidth is in [0, int.MaxValue] (StaticSize of a dynamic
//     node is -1) and every tuple/fixed-array head region is <= the compile limit MaxStaticEncodedBytes;
//   * FixedArray HeadSize == Count * ElementHeadWidth and Count >= 1; a tuple's HeadSize == last HeadOffset + last HeadWidth;
//   * depth of every node (root = 1, +1 per nesting step, primitives included) <= the compile limit MaxSchemaDepth <= 32,
//     and MaxDepth is the deepest node actually present;
//   * the table is never modified after construction; ABISchema holds no mutable state.
// =====================================================================================================================

namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// An immutable, shareable compiled ABI schema whose root is a tuple (the argument list of a function, or any tuple-encoded
/// document). It proves nothing about contract identity: bytes may validate under several plausible schemas. Create one with
/// <see cref="ABISchemaCompiler"/> at startup; it is then safe to use from any number of threads.
/// </summary>
public sealed class ABISchema
{
    /// <summary>Marks a dynamic array's element count in <see cref="ABISchemaNode.Count"/>: known only at run time.</summary>
    internal const int DynamicCount = -1;

    /// <summary><see cref="ABISchemaNode.StaticSize"/> of a dynamic node.</summary>
    internal const int NotStatic = -1;

    /// <summary><see cref="ABISchemaNode.Element"/> of a node without an element type.</summary>
    internal const int NoNode = -1;

    /// <summary><see cref="ABITupleChild.NextDynamicSibling"/> when no dynamic member follows.</summary>
    internal const int NoSibling = -1;

    /// <summary>Node id of the root tuple.</summary>
    internal const int RootNodeId = 0;

    private readonly ABISchemaNode[] _nodes;
    private readonly ABITupleChild[] _children;

    internal ABISchema(ABISchemaNode[] nodes, ABITupleChild[] children, int maxDepth, string canonicalType)
    {
        _nodes = nodes;
        _children = children;
        MaxDepth = maxDepth;
        CanonicalType = canonicalType;
        MaxContainerDepth = ComputeMaxContainerDepth(nodes, children);
    }

    /// <summary>
    /// Depth of the deepest tuple, fixed array or dynamic array (root = 1): the number of encoder frames needed when every container of
    /// the deepest path is open at once. Computed once at construction (ids are pre-order, so a parent's depth is known before its children's).
    /// </summary>
    private static int ComputeMaxContainerDepth(ABISchemaNode[] nodes, ABITupleChild[] children)
    {
        var depth = new int[nodes.Length];
        depth[RootNodeId] = 1;
        int deepest = 1;
        for (int id = 0; id < nodes.Length; id++)
        {
            ref readonly ABISchemaNode node = ref nodes[id];
            int childDepth = depth[id] + 1;
            if (node.Kind == ABITypeKind.Tuple)
            {
                for (int i = 0; i < node.Count; i++)
                {
                    SetDepth(children[node.ChildStart + i].NodeId, childDepth);
                }
            }
            else if (node.Kind == ABITypeKind.FixedArray || node.Kind == ABITypeKind.DynamicArray)
            {
                SetDepth(node.Element, childDepth);
            }
        }

        return deepest;

        void SetDepth(int childId, int value)
        {
            depth[childId] = value;
            ABITypeKind kind = nodes[childId].Kind;
            if (value > deepest && (kind == ABITypeKind.Tuple || kind == ABITypeKind.FixedArray || kind == ABITypeKind.DynamicArray))
            {
                deepest = value;
            }
        }
    }

    /// <summary>The root tuple in canonical Solidity syntax, for example <c>(address,uint256)</c>; aliases are expanded and the word <c>tuple</c> never appears.</summary>
    public string CanonicalType { get; }

    /// <summary>Number of members of the root tuple.</summary>
    public int ArgumentCount => _nodes[RootNodeId].Count;

    /// <summary>True when the root tuple is dynamic (an encoding then contains tails).</summary>
    public bool IsDynamic => _nodes[RootNodeId].IsDynamic;

    /// <summary>Returns <see cref="CanonicalType"/>.</summary>
    public override string ToString() => CanonicalType;

    /// <summary>Total number of nodes.</summary>
    internal int NodeCount => _nodes.Length;

    /// <summary>Depth of the deepest node (root tuple = 1, primitives included).</summary>
    internal int MaxDepth { get; }

    /// <summary>Depth of the deepest container node (root tuple = 1); at most <see cref="MaxDepth"/>. The encoder needs this many scratch frames (or <c>MaxValueDepth</c>, if smaller).</summary>
    internal int MaxContainerDepth { get; }

    /// <summary>The root tuple node.</summary>
    internal ref readonly ABISchemaNode Root => ref _nodes[RootNodeId];

    /// <summary>The node with the given id (see the header for the id rules).</summary>
    internal ref readonly ABISchemaNode GetNode(int nodeId) => ref _nodes[nodeId];

    /// <summary>The members of a tuple node in declaration order; empty for an empty tuple and for every non-tuple node.</summary>
    internal ReadOnlySpan<ABITupleChild> GetTupleChildren(in ABISchemaNode tuple) =>
        tuple.Kind == ABITypeKind.Tuple ? new ReadOnlySpan<ABITupleChild>(_children, tuple.ChildStart, tuple.Count) : default;

    /// <summary>The members of the tuple with node id <paramref name="tupleNodeId"/>.</summary>
    internal ReadOnlySpan<ABITupleChild> GetTupleChildren(int tupleNodeId) => GetTupleChildren(in _nodes[tupleNodeId]);
}
