using System.Diagnostics.CodeAnalysis;

namespace EccentricWare.Web3.DataTypes.ABI.EvmAdapters;

/// <summary>How a function treats its arguments, as far as nested calls go.</summary>
public enum ExecutionKind : byte
{
    /// <summary>Verified metadata says the function executes no call encoded in its arguments: <c>bytes</c> arguments stay opaque data.</summary>
    NoNestedCalls,

    /// <summary>The function executes the calls its <see cref="NestedCallRule"/>s describe, and no others.</summary>
    NestedCalls,

    /// <summary>
    /// The function executes calls in a way no <see cref="NestedCallRule"/> can describe (a packed command stream such as Safe <c>multiSend(bytes)</c> or a
    /// router command list). Registering it lets the schema be known; <see cref="NestedCallWalker"/> refuses it (<see cref="NestedCallStatus.UndescribedExecution"/>).
    /// </summary>
    Undescribed,
}

/// <summary>The call type a nested call runs with.</summary>
public enum NestedCallMode : byte
{
    /// <summary><c>CALL</c>: the target's code runs in the target's context; <c>msg.sender</c> is the calling contract; value moves to the target.</summary>
    Call,

    /// <summary><c>DELEGATECALL</c>: the target's code runs in the CALLER's context (its storage and balance); <c>msg.sender</c> and <c>msg.value</c> are inherited; no value moves.</summary>
    DelegateCall,
}

/// <summary>Why <see cref="ExecutionDescriptor.TryCreateNested"/> refused its rules.</summary>
public enum ExecutionDescriptorError : byte
{
    /// <summary>No error.</summary>
    None,

    /// <summary>A null function or rule, or an empty rule list.</summary>
    InvalidArgument,

    /// <summary>The calldata path does not lead to a dynamic <c>bytes</c> value, uses <see cref="NestedCallRule.EachElement"/> at a step that is not an array or more than once.</summary>
    InvalidCalldataPath,

    /// <summary>The target path does not lead to an <c>address</c> value, or misuses <see cref="NestedCallRule.EachElement"/>.</summary>
    InvalidTargetPath,

    /// <summary>The value path does not lead to a <c>uint&lt;M&gt;</c> value, or misuses <see cref="NestedCallRule.EachElement"/>.</summary>
    InvalidValuePath,

    /// <summary>The operation path does not lead to a <c>uint&lt;M&gt;</c> value, or misuses <see cref="NestedCallRule.EachElement"/>.</summary>
    InvalidOperationPath,

    /// <summary>A target, value or operation path iterates (<see cref="NestedCallRule.EachElement"/>) while the calldata path does not, so it is unclear which element belongs to the one child.</summary>
    InconsistentEachElement,
}

/// <summary>
/// One family of nested calls a function makes: where the child calldata is, where its target comes from, and with which call type and value it runs.
/// Paths are argument paths as <see cref="ABIData.TryLocate"/> takes them; <see cref="EachElement"/> at an array step means "every element, in order".
/// Rules are immutable. They are checked against the function's schema by <see cref="ExecutionDescriptor.TryCreateNested"/>, not here.
/// </summary>
/// <remarks>
/// Patterns from deployed contracts (verify against the contract you register; these are examples, not metadata):
/// <list type="table">
/// <listheader><term>Function</term><description>Rule</description></listheader>
/// <item><term>OpenZeppelin / Uniswap V3 <c>multicall(bytes[])</c></term><description><c>DelegateCallToSelf([0, EachElement])</c></description></item>
/// <item><term>Uniswap <c>multicall(uint256,bytes[])</c> (deadline first)</term><description><c>DelegateCallToSelf([1, EachElement])</c></description></item>
/// <item><term>Multicall3 <c>aggregate3((address,bool,bytes)[])</c></term><description><c>Call([0, EachElement, 0], [0, EachElement, 2])</c></description></item>
/// <item><term>Multicall3 <c>aggregate3Value((address,bool,uint256,bytes)[])</c></term><description><c>CallWithValue([0, EachElement, 0], [0, EachElement, 3], [0, EachElement, 2])</c></description></item>
/// <item><term>Safe <c>execTransaction(address,uint256,bytes,uint8,...)</c></term><description><c>CallOrDelegateCall([0], [2], [1], [3])</c></description></item>
/// <item><term>ERC-4337 account <c>executeBatch(address[],uint256[],bytes[])</c></term><description><c>CallWithValue([0, EachElement], [2, EachElement], [1, EachElement])</c> (the three arrays must have equal length, or the walk fails closed)</description></item>
/// </list>
/// </remarks>
public sealed class NestedCallRule
{
    /// <summary>Marks an array step of a path as "each element": the rule then yields one child per element of that array.</summary>
    public const int EachElement = -1;

    private readonly int[] _calldataPath;
    private readonly int[]? _targetPath;
    private readonly int[]? _valuePath;
    private readonly int[]? _operationPath;

    private NestedCallRule(int[] calldataPath, int[]? targetPath, int[]? valuePath, int[]? operationPath, NestedCallMode mode)
    {
        _calldataPath = calldataPath;
        _targetPath = targetPath;
        _valuePath = valuePath;
        _operationPath = operationPath;
        Mode = mode;
    }

    /// <summary>
    /// A <c>DELEGATECALL</c> to the calling contract's own address (<c>address(this)</c>) with the calldata at <paramref name="calldataPath"/>: the
    /// OpenZeppelin and Uniswap <c>multicall</c> pattern. The child runs the code at the parent's context address, with the parent's sender and value.
    /// </summary>
    public static NestedCallRule DelegateCallToSelf(ReadOnlySpan<int> calldataPath) =>
        new(calldataPath.ToArray(), null, null, null, NestedCallMode.DelegateCall);

    /// <summary>A <c>CALL</c> to the address at <paramref name="targetPath"/> with the calldata at <paramref name="calldataPath"/> and no value.</summary>
    public static NestedCallRule Call(ReadOnlySpan<int> targetPath, ReadOnlySpan<int> calldataPath) =>
        new(calldataPath.ToArray(), targetPath.ToArray(), null, null, NestedCallMode.Call);

    /// <summary>A <c>CALL</c> to the address at <paramref name="targetPath"/> with the calldata at <paramref name="calldataPath"/> and the value at <paramref name="valuePath"/>.</summary>
    public static NestedCallRule CallWithValue(ReadOnlySpan<int> targetPath, ReadOnlySpan<int> calldataPath, ReadOnlySpan<int> valuePath) =>
        new(calldataPath.ToArray(), targetPath.ToArray(), valuePath.ToArray(), null, NestedCallMode.Call);

    /// <summary>
    /// A call whose type is an argument, Safe style: the value at <paramref name="operationPath"/> is 0 for <c>CALL</c> and 1 for <c>DELEGATECALL</c>; any
    /// other value fails the walk closed. A <c>DELEGATECALL</c> with a non-zero value argument also fails closed (no value can move).
    /// </summary>
    public static NestedCallRule CallOrDelegateCall(ReadOnlySpan<int> targetPath, ReadOnlySpan<int> calldataPath, ReadOnlySpan<int> valuePath, ReadOnlySpan<int> operationPath) =>
        new(calldataPath.ToArray(), targetPath.ToArray(), valuePath.ToArray(), operationPath.ToArray(), NestedCallMode.Call);

    /// <summary>The path of the child calldata (a <c>bytes</c> value).</summary>
    public ReadOnlySpan<int> CalldataPath => _calldataPath;

    /// <summary>True when the target is the calling contract's own address (<see cref="DelegateCallToSelf"/>); <see cref="TargetPath"/> is then empty.</summary>
    public bool TargetsSelf => _targetPath is null;

    /// <summary>The path of the target address; empty when <see cref="TargetsSelf"/>.</summary>
    public ReadOnlySpan<int> TargetPath => _targetPath;

    /// <summary>True when the value comes from an argument; otherwise a <c>CALL</c> sends no value.</summary>
    public bool HasValuePath => _valuePath is not null;

    /// <summary>The path of the value argument; empty when <see cref="HasValuePath"/> is false.</summary>
    public ReadOnlySpan<int> ValuePath => _valuePath;

    /// <summary>True when the call type comes from an argument (<see cref="CallOrDelegateCall"/>); otherwise it is <see cref="Mode"/>.</summary>
    public bool HasOperationPath => _operationPath is not null;

    /// <summary>The path of the operation argument; empty when <see cref="HasOperationPath"/> is false.</summary>
    public ReadOnlySpan<int> OperationPath => _operationPath;

    /// <summary>The fixed call type, used when <see cref="HasOperationPath"/> is false.</summary>
    public NestedCallMode Mode { get; }
}

/// <summary>
/// Trusted execution metadata for one function (spec 8.3 and 12): its compiled schema and what it does with its arguments. Nested calldata is interpreted
/// ONLY where a descriptor says so; any <c>bytes</c> argument no rule names stays opaque, whatever it contains (spec 9.2, "arbitrary bytes misclassified as
/// calls"). Instances are immutable and shareable across threads.
/// </summary>
/// <remarks>
/// A descriptor is a claim by whoever builds the registry, not something this library can verify: it must come from the same trusted source as the
/// schema and the code identity (verified source, audited bytecode analysis). A wrong descriptor makes the walker miss or invent calls.
/// </remarks>
public sealed class ExecutionDescriptor
{
    private readonly NestedCallRule[] _rules;

    private ExecutionDescriptor(ABIFunctionSchema function, ExecutionKind kind, NestedCallRule[] rules)
    {
        Function = function;
        Kind = kind;
        _rules = rules;
    }

    /// <summary>The function this descriptor describes.</summary>
    public ABIFunctionSchema Function { get; }

    /// <summary>What the function does with its arguments.</summary>
    public ExecutionKind Kind { get; }

    /// <summary>The nested-call rules, in the order the walker applies them; empty unless <see cref="Kind"/> is <see cref="ExecutionKind.NestedCalls"/>.</summary>
    public ReadOnlySpan<NestedCallRule> Rules => _rules;

    /// <summary>A function that executes no call encoded in its arguments.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is null.</exception>
    public static ExecutionDescriptor NoNestedCalls(ABIFunctionSchema function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new ExecutionDescriptor(function, ExecutionKind.NoNestedCalls, []);
    }

    /// <summary>A function whose nested execution cannot be described by rules; the walker fails closed on it.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is null.</exception>
    public static ExecutionDescriptor Undescribed(ABIFunctionSchema function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new ExecutionDescriptor(function, ExecutionKind.Undescribed, []);
    }

    /// <summary>
    /// A function that executes the calls <paramref name="rules"/> describe. Every path is checked against <paramref name="function"/>'s schema: the calldata
    /// path must reach a dynamic <c>bytes</c>, the target path an <c>address</c>, the value and operation paths a <c>uint&lt;M&gt;</c>; indexes must be in
    /// range for tuples and fixed arrays (an index into a dynamic array is checked per call); <see cref="NestedCallRule.EachElement"/> may appear at most once per
    /// path, only at an array step, and in a target, value or operation path only when the calldata path also has it.
    /// </summary>
    /// <param name="function">The compiled function.</param>
    /// <param name="rules">One or more rules; the array is copied.</param>
    /// <param name="descriptor">The descriptor, or null on failure.</param>
    /// <param name="error">Why the rules were refused; <see cref="ExecutionDescriptorError.None"/> on success.</param>
    /// <param name="ruleIndex">The position of the refused rule, or -1.</param>
    public static bool TryCreateNested(
        ABIFunctionSchema function,
        ReadOnlySpan<NestedCallRule> rules,
        [NotNullWhen(true)] out ExecutionDescriptor? descriptor,
        out ExecutionDescriptorError error,
        out int ruleIndex)
    {
        descriptor = null;
        ruleIndex = -1;
        if (function is null || rules.IsEmpty)
        {
            error = ExecutionDescriptorError.InvalidArgument;
            return false;
        }

        ABISchema schema = function.InputSchema;
        for (int i = 0; i < rules.Length; i++)
        {
            ruleIndex = i;
            NestedCallRule? rule = rules[i];
            if (rule is null)
            {
                error = ExecutionDescriptorError.InvalidArgument;
                return false;
            }

            if (!TryCheckPath(schema, rule.CalldataPath, ABITypeKind.Bytes, out bool calldataEach))
            {
                error = ExecutionDescriptorError.InvalidCalldataPath;
                return false;
            }

            error = CheckOptionalPath(schema, rule.TargetsSelf, rule.TargetPath, ABITypeKind.Address, calldataEach, ExecutionDescriptorError.InvalidTargetPath);
            if (error == ExecutionDescriptorError.None)
            {
                error = CheckOptionalPath(schema, !rule.HasValuePath, rule.ValuePath, ABITypeKind.UInt, calldataEach, ExecutionDescriptorError.InvalidValuePath);
            }

            if (error == ExecutionDescriptorError.None)
            {
                error = CheckOptionalPath(schema, !rule.HasOperationPath, rule.OperationPath, ABITypeKind.UInt, calldataEach, ExecutionDescriptorError.InvalidOperationPath);
            }

            if (error != ExecutionDescriptorError.None)
            {
                return false;
            }
        }

        ruleIndex = -1;
        error = ExecutionDescriptorError.None;
        descriptor = new ExecutionDescriptor(function, ExecutionKind.NestedCalls, rules.ToArray());
        return true;
    }

    private static ExecutionDescriptorError CheckOptionalPath(
        ABISchema schema, bool absent, ReadOnlySpan<int> path, ABITypeKind expected, bool calldataEach, ExecutionDescriptorError invalid)
    {
        if (absent)
        {
            return ExecutionDescriptorError.None;
        }

        if (!TryCheckPath(schema, path, expected, out bool each))
        {
            return invalid;
        }

        return each && !calldataEach ? ExecutionDescriptorError.InconsistentEachElement : ExecutionDescriptorError.None;
    }

    /// <summary>Walks <paramref name="path"/> through the schema and checks the leaf kind. <paramref name="hasEach"/> reports a (single) EachElement step.</summary>
    private static bool TryCheckPath(ABISchema schema, ReadOnlySpan<int> path, ABITypeKind expected, out bool hasEach)
    {
        hasEach = false;
        if (path.IsEmpty)
        {
            return false; // the root tuple is never a bytes, address or uint value
        }

        int nodeId = ABISchema.RootNodeId;
        foreach (int step in path)
        {
            ref readonly ABISchemaNode node = ref schema.GetNode(nodeId);
            switch (node.Kind)
            {
                case ABITypeKind.Tuple:
                    if (step < 0 || step >= node.Count)
                    {
                        return false;
                    }

                    nodeId = schema.GetTupleChildren(in node)[step].NodeId;
                    break;

                case ABITypeKind.FixedArray:
                case ABITypeKind.DynamicArray:
                    if (step == NestedCallRule.EachElement)
                    {
                        if (hasEach)
                        {
                            return false;
                        }

                        hasEach = true;
                    }
                    else if (step < 0 || (node.Kind == ABITypeKind.FixedArray && step >= node.Count))
                    {
                        return false;
                    }

                    nodeId = node.Element;
                    break;

                default:
                    return false; // a step through a primitive, bytes or string
            }
        }

        return schema.GetNode(nodeId).Kind == expected;
    }
}
