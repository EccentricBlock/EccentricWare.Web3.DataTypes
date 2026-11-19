using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// Compiles type definitions or canonical signature text into immutable <see cref="ABISchema"/> and
/// <see cref="ABIFunctionSchema"/> objects. This is bootstrap-time work that allocates; run it once at startup, never per
/// request. No <c>Try*</c> method throws for any input; every failure reports an <see cref="ABISchemaError"/> naming the
/// failing component and reason (never transaction bytes). The compiler copies everything it needs from mutable definitions
/// and rejects cycles, malformed widths, scales and lengths, <c>T[0]</c>, arithmetic overflow, and every limit in
/// <see cref="ABICompileLimits"/>. Do not change a definition while it is being compiled.
/// </summary>
public static class ABISchemaCompiler
{
    /// <summary>Compiles the argument tuple of a function, or any tuple-encoded document, into an <see cref="ABISchema"/>.</summary>
    /// <remarks>
    /// Signature note: the specification declares <c>out ABISchema schema</c>; it is annotated <c>[NotNullWhen(true)]</c>
    /// and nullable so that the nullable-reference-type contract is honest on failure (mechanical change, same behaviour).
    /// </remarks>
    /// <param name="definition">The root tuple definition.</param>
    /// <param name="limits">Compile limits; <c>default</c> is invalid. Read once.</param>
    /// <param name="schema">The compiled schema, or null on failure.</param>
    /// <param name="error">The failure detail, <see cref="ABISchemaError.None"/> on success.</param>
    public static bool TryCompileArguments(
        ABITupleDefinition definition,
        in ABICompileLimits limits,
        [NotNullWhen(true)] out ABISchema? schema,
        out ABISchemaError error)
    {
        schema = null;
        if (definition is null)
        {
            error = ABISchemaError.Argument(ABISchemaErrorReason.NullArgument);
            return false;
        }

        // Read the caller's storage once so the validated values are the values used.
        ABICompileLimits snapshot = limits;
        if (!snapshot.IsValid)
        {
            error = ABISchemaError.Argument(ABISchemaErrorReason.InvalidLimits);
            return false;
        }

        var builder = new Builder(snapshot);
        if (!builder.CompileRoot(definition))
        {
            error = builder.Error;
            return false;
        }

        schema = builder.Build();
        error = ABISchemaError.None;
        return true;
    }

    /// <summary>
    /// Compiles a function: validates <paramref name="name"/>, compiles <paramref name="arguments"/>, builds the canonical
    /// signature and derives the selector from it with Keccak-256. The selector cannot be supplied by the caller.
    /// </summary>
    /// <remarks>Nullable annotation on <paramref name="function"/>: see <see cref="TryCompileArguments"/>.</remarks>
    /// <param name="name">Solidity identifier (<c>[A-Za-z_$][A-Za-z0-9_$]*</c>).</param>
    /// <param name="arguments">The argument tuple definition.</param>
    /// <param name="limits">Compile limits; <c>default</c> is invalid. Read once.</param>
    /// <param name="function">The compiled function, or null on failure.</param>
    /// <param name="error">The failure detail, <see cref="ABISchemaError.None"/> on success.</param>
    public static bool TryCompileFunction(
        string name,
        ABITupleDefinition arguments,
        in ABICompileLimits limits,
        [NotNullWhen(true)] out ABIFunctionSchema? function,
        out ABISchemaError error)
    {
        function = null;
        if (name is null)
        {
            error = ABISchemaError.Argument(ABISchemaErrorReason.NullArgument);
            return false;
        }

        if (!ABISelector.IsValidFunctionName(name))
        {
            error = ABISchemaError.At(ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidFunctionName, string.Empty);
            return false;
        }

        if (!TryCompileArguments(arguments, in limits, out var schema, out error))
        {
            return false;
        }

        string signature = name + schema.CanonicalType;
        function = new ABIFunctionSchema(name, signature, FunctionSelector.FromSignature(signature), schema);
        return true;
    }

    /// <summary>
    /// Bootstrap convenience: parses and compiles a whole function signature such as <c>transfer(address,uint256)</c> or
    /// <c>transfer(address,uint)</c>. Aliases (<c>uint</c>, <c>int</c>, <c>fixed</c>, <c>ufixed</c>) are normalised, so the
    /// canonical signature and selector are those of <c>transfer(address,uint256)</c>. Allocates; not for request paths.
    /// Whitespace, parameter names and the word <c>tuple</c> are rejected.
    /// </summary>
    /// <param name="signature">The signature text.</param>
    /// <param name="limits">Compile limits; <c>default</c> is invalid. Read once.</param>
    /// <param name="function">The compiled function, or null on failure.</param>
    /// <param name="error">The failure detail, <see cref="ABISchemaError.None"/> on success.</param>
    public static bool TryCompileFunctionSignature(
        string signature,
        in ABICompileLimits limits,
        [NotNullWhen(true)] out ABIFunctionSchema? function,
        out ABISchemaError error)
    {
        function = null;
        if (!ABITypeParser.TryParseFunctionSignature(signature, out var name, out var arguments, out error))
        {
            return false;
        }

        return TryCompileFunction(name, arguments, in limits, out function, out error);
    }

    /// <summary>Single-use compilation state. Recursion is bounded by <see cref="ABICompileLimits.HardMaxSchemaDepth"/>.</summary>
    private sealed class Builder(ABICompileLimits limits)
    {
        /// <summary>Path step recorded when descending into an array's element type.</summary>
        private const int ElementStep = -1;

        private readonly List<ABISchemaNode> _nodes = [];
        private readonly List<ABITupleChild> _children = [];
        private readonly HashSet<ABITupleDefinition> _activeTuples = [];

        // _path[d] is the step taken to reach the node at depth d (d >= 2). Sized for the deepest depth a failing node can have.
        private readonly int[] _path = new int[ABICompileLimits.HardMaxSchemaDepth + 2];
        private int _maxDepth;

        internal ABISchemaError Error { get; private set; }

        internal bool CompileRoot(ABITupleDefinition root) => CompileTuple(root, 1, out _);

        internal ABISchema Build()
        {
            var text = new StringBuilder();
            AppendCanonical(ABISchema.RootNodeId, text);
            return new ABISchema(_nodes.ToArray(), _children.ToArray(), _maxDepth, text.ToString());
        }

        private bool Fail(ABIStatus status, ABISchemaErrorReason reason, int depth)
        {
            var path = new StringBuilder("$");
            for (int d = 2; d <= depth; d++)
            {
                path.Append('/');
                if (_path[d] == ElementStep)
                {
                    path.Append("[]");
                }
                else
                {
                    path.Append(_path[d].ToString(CultureInfo.InvariantCulture));
                }
            }

            Error = ABISchemaError.At(status, reason, path.ToString());
            return false;
        }

        /// <summary>Charges one node against the depth and node budgets.</summary>
        private bool Enter(int depth)
        {
            if (depth > limits.MaxSchemaDepth)
            {
                return Fail(ABIStatus.DepthLimitExceeded, ABISchemaErrorReason.SchemaTooDeep, depth);
            }

            if (_nodes.Count >= limits.MaxSchemaNodes)
            {
                return Fail(ABIStatus.ElementLimitExceeded, ABISchemaErrorReason.TooManySchemaNodes, depth);
            }

            if (depth > _maxDepth)
            {
                _maxDepth = depth;
            }

            return true;
        }

        /// <summary>Checks a head-region size computed in <see cref="long"/> against 32-bit range and the static-size limit.</summary>
        private bool CheckSize(long size, int depth)
        {
            if (size > int.MaxValue)
            {
                return Fail(ABIStatus.ArithmeticOverflow, ABISchemaErrorReason.SizeOverflow, depth);
            }

            if (size > limits.MaxStaticEncodedBytes)
            {
                return Fail(ABIStatus.ElementLimitExceeded, ABISchemaErrorReason.StaticSizeLimit, depth);
            }

            return true;
        }

        private bool CompileType(ABITypeDefinition definition, int depth, out int nodeId)
        {
            nodeId = ABISchema.NoNode;
            switch (definition.Kind)
            {
                case ABITypeKind.Tuple:
                    return CompileTuple(definition.TupleDefinition!, depth, out nodeId);
                case ABITypeKind.FixedArray:
                    return CompileArray(definition, depth, out nodeId);
                case ABITypeKind.DynamicArray:
                    return CompileArray(definition, depth, out nodeId);
            }

            if (!Enter(depth))
            {
                return false;
            }

            switch (definition.Kind)
            {
                case ABITypeKind.UInt:
                case ABITypeKind.Int:
                    if (!IsLegalIntegerWidth(definition.Width))
                    {
                        return Fail(ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidWidth, depth);
                    }

                    break;
                case ABITypeKind.Fixed:
                case ABITypeKind.UFixed:
                    if (!IsLegalIntegerWidth(definition.Width))
                    {
                        return Fail(ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidWidth, depth);
                    }

                    if (definition.Scale < 1 || definition.Scale > 80)
                    {
                        return Fail(ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidFixedPrecision, depth);
                    }

                    break;
                case ABITypeKind.FixedBytes:
                    if (definition.Width < 1 || definition.Width > 32)
                    {
                        return Fail(ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidFixedBytesLength, depth);
                    }

                    break;
                case ABITypeKind.Address:
                case ABITypeKind.Bool:
                case ABITypeKind.Function:
                case ABITypeKind.Bytes:
                case ABITypeKind.String:
                    break;
                default:
                    return Fail(ABIStatus.InvalidSchema, ABISchemaErrorReason.UnknownType, depth);
            }

            bool dynamic = definition.Kind is ABITypeKind.Bytes or ABITypeKind.String;
            nodeId = _nodes.Count;
            _nodes.Add(new ABISchemaNode(
                definition.Kind,
                dynamic,
                width: definition.Width,
                scale: definition.Scale,
                count: 0,
                element: ABISchema.NoNode,
                childStart: 0,
                staticSize: dynamic ? ABISchema.NotStatic : 32,
                headWidth: 32,
                headSize: 0,
                elementHeadWidth: 0));
            return true;
        }

        private static bool IsLegalIntegerWidth(int bits) => bits >= 8 && bits <= 256 && (bits & 7) == 0;

        private bool CompileTuple(ABITupleDefinition tuple, int depth, out int nodeId)
        {
            nodeId = ABISchema.NoNode;

            // Cycle first: it is the more fundamental defect than any budget the cycle would eventually exhaust.
            // No removal on failure paths: a failed compile discards the whole builder.
            if (!_activeTuples.Add(tuple))
            {
                return Fail(ABIStatus.InvalidSchema, ABISchemaErrorReason.Cycle, depth);
            }

            if (!Enter(depth))
            {
                return false;
            }

            int count = tuple.Count;
            if (count > limits.MaxTupleElements)
            {
                return Fail(ABIStatus.ElementLimitExceeded, ABISchemaErrorReason.TupleTooWide, depth);
            }

            int id = _nodes.Count;
            _nodes.Add(default); // reserved so children get larger ids (pre-order); filled in below
            var members = new ABITupleChild[count];
            long headSize = 0;
            bool dynamic = false;
            for (int i = 0; i < count; i++)
            {
                _path[depth + 1] = i;
                if (!CompileType(tuple[i], depth + 1, out int memberId))
                {
                    return false;
                }

                ABISchemaNode member = _nodes[memberId];
                members[i] = new ABITupleChild(memberId, (int)headSize, ABISchema.NoSibling);
                headSize += member.HeadWidth;
                if (!CheckSize(headSize, depth))
                {
                    return false;
                }

                dynamic |= member.IsDynamic;
            }

            // Reverse pass: for each position, the first dynamic member after it.
            int nextDynamic = ABISchema.NoSibling;
            for (int i = count - 1; i >= 0; i--)
            {
                members[i] = new ABITupleChild(members[i].NodeId, members[i].HeadOffset, nextDynamic);
                if (_nodes[members[i].NodeId].IsDynamic)
                {
                    nextDynamic = i;
                }
            }

            int head = (int)headSize;
            int childStart = _children.Count;
            _children.AddRange(members);
            _nodes[id] = new ABISchemaNode(
                ABITypeKind.Tuple,
                dynamic,
                width: 0,
                scale: 0,
                count: count,
                element: ABISchema.NoNode,
                childStart: childStart,
                staticSize: dynamic ? ABISchema.NotStatic : head,
                headWidth: dynamic ? 32 : head,
                headSize: head,
                elementHeadWidth: 0);
            _activeTuples.Remove(tuple);
            nodeId = id;
            return true;
        }

        private bool CompileArray(ABITypeDefinition definition, int depth, out int nodeId)
        {
            nodeId = ABISchema.NoNode;
            if (!Enter(depth))
            {
                return false;
            }

            bool fixedLength = definition.Kind == ABITypeKind.FixedArray;
            int length = definition.Length;
            if (fixedLength)
            {
                if (length < 0)
                {
                    return Fail(ABIStatus.InvalidSchema, ABISchemaErrorReason.NegativeArrayLength, depth);
                }

                if (length == 0)
                {
                    return Fail(ABIStatus.UnsupportedSchema, ABISchemaErrorReason.ZeroLengthFixedArray, depth);
                }

                if (length > limits.MaxFixedArrayElements)
                {
                    return Fail(ABIStatus.ElementLimitExceeded, ABISchemaErrorReason.FixedArrayTooLong, depth);
                }
            }

            int id = _nodes.Count;
            _nodes.Add(default);
            _path[depth + 1] = ElementStep;
            if (!CompileType(definition.Element!, depth + 1, out int elementId))
            {
                return false;
            }

            ABISchemaNode element = _nodes[elementId];
            if (fixedLength)
            {
                long headSize = (long)length * element.HeadWidth;
                if (!CheckSize(headSize, depth))
                {
                    return false;
                }

                int head = (int)headSize;
                bool dynamic = element.IsDynamic;
                _nodes[id] = new ABISchemaNode(
                    ABITypeKind.FixedArray,
                    dynamic,
                    width: 0,
                    scale: 0,
                    count: length,
                    element: elementId,
                    childStart: 0,
                    staticSize: dynamic ? ABISchema.NotStatic : head,
                    headWidth: dynamic ? 32 : head,
                    headSize: head,
                    elementHeadWidth: element.HeadWidth);
            }
            else
            {
                _nodes[id] = new ABISchemaNode(
                    ABITypeKind.DynamicArray,
                    isDynamic: true,
                    width: 0,
                    scale: 0,
                    count: ABISchema.DynamicCount,
                    element: elementId,
                    childStart: 0,
                    staticSize: ABISchema.NotStatic,
                    headWidth: 32,
                    headSize: 0,
                    elementHeadWidth: element.HeadWidth);
            }

            nodeId = id;
            return true;
        }

        /// <summary>Canonical Solidity text of a compiled node (aliases expanded, tuples as parenthesised lists).</summary>
        private void AppendCanonical(int nodeId, StringBuilder text)
        {
            ABISchemaNode node = _nodes[nodeId];
            switch (node.Kind)
            {
                case ABITypeKind.UInt: text.Append(CultureInfo.InvariantCulture, $"uint{node.Width}"); break;
                case ABITypeKind.Int: text.Append(CultureInfo.InvariantCulture, $"int{node.Width}"); break;
                case ABITypeKind.Address: text.Append("address"); break;
                case ABITypeKind.Bool: text.Append("bool"); break;
                case ABITypeKind.FixedBytes: text.Append(CultureInfo.InvariantCulture, $"bytes{node.Width}"); break;
                case ABITypeKind.Function: text.Append("function"); break;
                case ABITypeKind.Fixed: text.Append(CultureInfo.InvariantCulture, $"fixed{node.Width}x{node.Scale}"); break;
                case ABITypeKind.UFixed: text.Append(CultureInfo.InvariantCulture, $"ufixed{node.Width}x{node.Scale}"); break;
                case ABITypeKind.Bytes: text.Append("bytes"); break;
                case ABITypeKind.String: text.Append("string"); break;
                case ABITypeKind.Tuple:
                    text.Append('(');
                    for (int i = 0; i < node.Count; i++)
                    {
                        if (i > 0)
                        {
                            text.Append(',');
                        }

                        AppendCanonical(_children[node.ChildStart + i].NodeId, text);
                    }

                    text.Append(')');
                    break;
                case ABITypeKind.FixedArray:
                    AppendCanonical(node.Element, text);
                    text.Append(CultureInfo.InvariantCulture, $"[{node.Count}]");
                    break;
                case ABITypeKind.DynamicArray:
                    AppendCanonical(node.Element, text);
                    text.Append("[]");
                    break;
            }
        }
    }
}
