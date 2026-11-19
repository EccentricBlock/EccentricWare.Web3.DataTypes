using EccentricWare.Web3.DataTypes.ABI;
using EccentricWare.Web3.DataTypes.ABI.EvmAdapters;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI.EvmAdapters;

/// <summary>
/// <see cref="ExecutionDescriptor"/> and <see cref="NestedCallRule"/>: the deployed patterns the documentation names are accepted, and every rule
/// that does not fit the function's schema is refused at bootstrap with its own error and the position of the rule.
/// </summary>
[TestClass]
public sealed class ExecutionDescriptorTests
{
    private const int Each = NestedCallRule.EachElement;

    private static ExecutionDescriptorError Create(string signature, out int ruleIndex, params NestedCallRule[] rules)
    {
        bool ok = ExecutionDescriptor.TryCreateNested(Function(signature), rules, out ExecutionDescriptor? descriptor, out ExecutionDescriptorError error, out ruleIndex);
        Assert.AreEqual(ok, error == ExecutionDescriptorError.None);
        Assert.AreEqual(ok, descriptor is not null);
        Assert.AreEqual(ok, ruleIndex == -1, "a rule position only on failure");
        if (ok)
        {
            Assert.AreEqual(ExecutionKind.NestedCalls, descriptor!.Kind);
            Assert.AreEqual(rules.Length, descriptor.Rules.Length);
        }

        return error;
    }

    private static ExecutionDescriptorError Create(string signature, params NestedCallRule[] rules) => Create(signature, out _, rules);

    [TestMethod]
    public void TheDocumentedDeployedPatterns_AreAccepted()
    {
        Assert.AreEqual(ExecutionDescriptorError.None, Create("multicall(bytes[])", NestedCallRule.DelegateCallToSelf([0, Each])));
        Assert.AreEqual(ExecutionDescriptorError.None, Create("multicall(uint256,bytes[])", NestedCallRule.DelegateCallToSelf([1, Each])));
        Assert.AreEqual(ExecutionDescriptorError.None, Create("aggregate3((address,bool,bytes)[])", NestedCallRule.Call([0, Each, 0], [0, Each, 2])));
        Assert.AreEqual(ExecutionDescriptorError.None, Create("aggregate3Value((address,bool,uint256,bytes)[])", NestedCallRule.CallWithValue([0, Each, 0], [0, Each, 3], [0, Each, 2])));
        Assert.AreEqual(ExecutionDescriptorError.None, Create("execTransaction(address,uint256,bytes,uint8,uint256,uint256,uint256,address,address,bytes)",
            NestedCallRule.CallOrDelegateCall([0], [2], [1], [3])));
        Assert.AreEqual(ExecutionDescriptorError.None, Create("executeBatch(address[],uint256[],bytes[])", NestedCallRule.CallWithValue([0, Each], [2, Each], [1, Each])));
        Assert.AreEqual(ExecutionDescriptorError.None, Create("multicall(address,bytes[])", NestedCallRule.Call([0], [1, Each])), "one fixed target for every element");
        Assert.AreEqual(ExecutionDescriptorError.None, Create("pair(bytes,bytes)", NestedCallRule.DelegateCallToSelf([0]), NestedCallRule.DelegateCallToSelf([1])), "several rules");
        Assert.AreEqual(ExecutionDescriptorError.None, Create("first(bytes[2])", NestedCallRule.DelegateCallToSelf([0, 1])), "a fixed index into a fixed array");
        Assert.AreEqual(ExecutionDescriptorError.None, Create("first(bytes[])", NestedCallRule.DelegateCallToSelf([0, 7])), "a fixed index into a dynamic array is checked per call");
        Assert.AreEqual(ExecutionDescriptorError.None, Create("grid(bytes[][2])", NestedCallRule.DelegateCallToSelf([0, 1, Each])), "iteration inside a fixed array element");
    }

    [TestMethod]
    public void EveryPathRule_IsCheckedAgainstTheSchema()
    {
        // Calldata path: must reach dynamic bytes.
        Assert.AreEqual(ExecutionDescriptorError.InvalidCalldataPath, Create("f(string[])", NestedCallRule.DelegateCallToSelf([0, Each])), "string is not bytes");
        Assert.AreEqual(ExecutionDescriptorError.InvalidCalldataPath, Create("f(bytes32[])", NestedCallRule.DelegateCallToSelf([0, Each])), "bytes32 is not dynamic bytes");
        Assert.AreEqual(ExecutionDescriptorError.InvalidCalldataPath, Create("f(bytes[])", NestedCallRule.DelegateCallToSelf([0])), "the array, not an element");
        Assert.AreEqual(ExecutionDescriptorError.InvalidCalldataPath, Create("f(bytes)", NestedCallRule.DelegateCallToSelf([])), "the root tuple");
        Assert.AreEqual(ExecutionDescriptorError.InvalidCalldataPath, Create("f(bytes)", NestedCallRule.DelegateCallToSelf([1])), "past the last argument");
        Assert.AreEqual(ExecutionDescriptorError.InvalidCalldataPath, Create("f(bytes)", NestedCallRule.DelegateCallToSelf([0, 0])), "a step through bytes");
        Assert.AreEqual(ExecutionDescriptorError.InvalidCalldataPath, Create("f(bytes)", NestedCallRule.DelegateCallToSelf([Each])), "EachElement at a tuple step");
        Assert.AreEqual(ExecutionDescriptorError.InvalidCalldataPath, Create("f(bytes[2])", NestedCallRule.DelegateCallToSelf([0, 2])), "past a fixed array");
        Assert.AreEqual(ExecutionDescriptorError.InvalidCalldataPath, Create("f(bytes[])", NestedCallRule.DelegateCallToSelf([0, -2])), "a negative index that is not EachElement");
        Assert.AreEqual(ExecutionDescriptorError.InvalidCalldataPath, Create("f(bytes[][])", NestedCallRule.DelegateCallToSelf([0, Each, Each])), "EachElement twice");

        // Target, value and operation paths.
        Assert.AreEqual(ExecutionDescriptorError.InvalidTargetPath, Create("f(uint256,bytes)", NestedCallRule.Call([0], [1])), "a uint is not an address");
        Assert.AreEqual(ExecutionDescriptorError.InvalidTargetPath, Create("f(address[],bytes)", NestedCallRule.Call([0], [1])), "an address array is not an address");
        Assert.AreEqual(ExecutionDescriptorError.InvalidTargetPath, Create("f(address,bytes)", NestedCallRule.Call([], [1])), "an empty target path");
        Assert.AreEqual(ExecutionDescriptorError.InvalidValuePath, Create("f(address,bytes,int256)", NestedCallRule.CallWithValue([0], [1], [2])), "a signed value");
        Assert.AreEqual(ExecutionDescriptorError.InvalidValuePath, Create("f(address,bytes,uint256)", NestedCallRule.CallWithValue([0], [1], [3])), "past the end");
        Assert.AreEqual(ExecutionDescriptorError.None, Create("f(address,bytes,uint128)", NestedCallRule.CallWithValue([0], [1], [2])), "any uint width");
        Assert.AreEqual(ExecutionDescriptorError.InvalidOperationPath, Create("f(address,uint256,bytes,address)", NestedCallRule.CallOrDelegateCall([0], [2], [1], [3])), "an address operation");
        Assert.AreEqual(ExecutionDescriptorError.InvalidOperationPath, Create("f(address,uint256,bytes,bool)", NestedCallRule.CallOrDelegateCall([0], [2], [1], [3])), "a bool operation");

        // EachElement outside the calldata path, while the calldata path has none: which element belongs to the one child?
        Assert.AreEqual(ExecutionDescriptorError.InconsistentEachElement, Create("f(address[],bytes)", NestedCallRule.Call([0, Each], [1])));
        Assert.AreEqual(ExecutionDescriptorError.InconsistentEachElement, Create("f(address,bytes,uint256[])", NestedCallRule.CallWithValue([0], [1], [2, Each])));
        Assert.AreEqual(ExecutionDescriptorError.InvalidTargetPath, Create("f(address[][],bytes[])", NestedCallRule.Call([0, Each, Each], [1, Each])), "EachElement twice in a target");
    }

    [TestMethod]
    public void TheRefusedRule_IsNamed_AndArgumentsAreChecked()
    {
        Assert.AreEqual(ExecutionDescriptorError.InvalidCalldataPath, Create("f(bytes,uint256)", out int ruleIndex, NestedCallRule.DelegateCallToSelf([0]), NestedCallRule.DelegateCallToSelf([1])));
        Assert.AreEqual(1, ruleIndex);

        Assert.IsFalse(ExecutionDescriptor.TryCreateNested(Function("f(bytes)"), [], out _, out ExecutionDescriptorError error, out _));
        Assert.AreEqual(ExecutionDescriptorError.InvalidArgument, error, "no rules");
        Assert.IsFalse(ExecutionDescriptor.TryCreateNested(null!, [NestedCallRule.DelegateCallToSelf([0])], out _, out error, out _));
        Assert.AreEqual(ExecutionDescriptorError.InvalidArgument, error, "no function");
        Assert.IsFalse(ExecutionDescriptor.TryCreateNested(Function("f(bytes)"), [NestedCallRule.DelegateCallToSelf([0]), null!], out _, out error, out ruleIndex));
        Assert.AreEqual(ExecutionDescriptorError.InvalidArgument, error, "a null rule");
        Assert.AreEqual(1, ruleIndex);

        Assert.ThrowsExactly<ArgumentNullException>(() => ExecutionDescriptor.NoNestedCalls(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ExecutionDescriptor.Undescribed(null!));
        ABIFunctionSchema transfer = Function("transfer(address,uint256)");
        Assert.AreEqual(ExecutionKind.NoNestedCalls, ExecutionDescriptor.NoNestedCalls(transfer).Kind);
        Assert.AreEqual(0, ExecutionDescriptor.NoNestedCalls(transfer).Rules.Length);
        Assert.AreEqual(ExecutionKind.Undescribed, ExecutionDescriptor.Undescribed(transfer).Kind);
        Assert.AreSame(transfer, ExecutionDescriptor.Undescribed(transfer).Function);
    }

    [TestMethod]
    public void RulesAndDescriptors_AreImmutableCopies()
    {
        int[] calldata = [0, Each];
        NestedCallRule rule = NestedCallRule.DelegateCallToSelf(calldata);
        calldata[0] = 5;
        CollectionAssert.AreEqual(new[] { 0, Each }, rule.CalldataPath.ToArray(), "the path was copied");
        Assert.IsTrue(rule.TargetsSelf);
        Assert.IsFalse(rule.HasValuePath);
        Assert.IsFalse(rule.HasOperationPath);
        Assert.AreEqual(NestedCallMode.DelegateCall, rule.Mode);
        Assert.IsTrue(rule.TargetPath.IsEmpty);

        NestedCallRule safe = NestedCallRule.CallOrDelegateCall([0], [2], [1], [3]);
        Assert.IsFalse(safe.TargetsSelf);
        Assert.IsTrue(safe.HasValuePath);
        Assert.IsTrue(safe.HasOperationPath);
        CollectionAssert.AreEqual(new[] { 3 }, safe.OperationPath.ToArray());

        NestedCallRule[] rules = [rule];
        Assert.IsTrue(ExecutionDescriptor.TryCreateNested(Function("multicall(bytes[])"), rules, out ExecutionDescriptor? descriptor, out _, out _));
        rules[0] = safe;
        Assert.AreSame(rule, descriptor!.Rules[0], "the rule list was copied");
    }
}
