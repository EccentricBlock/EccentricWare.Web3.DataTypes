using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using EccentricWare.Web3.DataTypes.ABI;
using EccentricWare.Web3.DataTypes.ABI.EvmAdapters;
using EccentricWare.Web3.DataTypes.Rlp;
using EccentricWare.Web3.DataTypes.Tests.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.EvmAdapters.EvmAdapterFixtures;

namespace EccentricWare.Web3.DataTypes.Tests.ABI.EvmAdapters;

/// <summary>
/// <see cref="NestedCallWalker"/> (spec 8.3, 10.5): nested calls are followed only where a trusted descriptor declares them, each is resolved, strictly
/// validated and evaluated, call/delegatecall/sender/value semantics and exact ranges are preserved, and every unknown, ambiguous, malformed, undescribed,
/// forbidden or over-limit call fails the walk closed. Calldata is built with the test-only <see cref="ABIIndependentEncoder"/>, not the library's writer,
/// and every reported range is checked against the memory the validated view actually covers.
/// </summary>
[TestClass]
public sealed class NestedCallWalkerTests
{
    private const int Each = NestedCallRule.EachElement;

    private static readonly byte[] TokenBytes = Hex("a0b86991c6218b36c1d19d4a2e9eb0ce3606eb48");
    private static readonly byte[] Multicall3Bytes = Hex("ca11bde05977b3631167028862be2a173976ca11");
    private static readonly byte[] SafeBytes = Hex("5afe5afe5afe5afe5afe5afe5afe5afe5afe5afe");
    private static readonly byte[] AccountBytes = Hex("acc0acc0acc0acc0acc0acc0acc0acc0acc0acc0");
    private static readonly byte[] LibraryBytes = Hex("11b0011b0011b0011b0011b0011b0011b0011b00");
    private static readonly Address Token = Address.FromEvmBytes(TokenBytes);
    private static readonly Address Multicall3 = Address.FromEvmBytes(Multicall3Bytes);
    private static readonly Address Safe = Address.FromEvmBytes(SafeBytes);
    private static readonly Address Account = Address.FromEvmBytes(AccountBytes);
    private static readonly Address Library = Address.FromEvmBytes(LibraryBytes);
    private static readonly Hash32 RouterCode = new(0, 0, 0, 0x10);
    private static readonly Hash32 TokenCode = new(0, 0, 0, 0x20);
    private static readonly Hash32 Multicall3Code = new(0, 0, 0, 0x30);
    private static readonly Hash32 SafeCode = new(0, 0, 0, 0x40);
    private static readonly Hash32 AccountCode = new(0, 0, 0, 0x50);
    private static readonly Hash32 LibraryCode = new(0, 0, 0, 0x60);
    private static readonly Hash32 UpgradedCode = new(0, 0, 0, 0x99);

    private static readonly ABIFunctionSchema Transfer = Function("transfer(address,uint256)");
    private static readonly ABIFunctionSchema Approve = Function("approve(address,uint256)");
    private static readonly ABIFunctionSchema Multicall = Function("multicall(bytes[])");
    private static readonly ABIFunctionSchema Execute = Function("execute(bytes)");
    private static readonly ABIFunctionSchema RunFirst = Function("runFirst(bytes[])");
    private static readonly ABIFunctionSchema MultiSend = Function("multiSend(bytes)");
    private static readonly ABIFunctionSchema Aggregate3 = Function("aggregate3((address,bool,bytes)[])");
    private static readonly ABIFunctionSchema Aggregate3Value = Function("aggregate3Value((address,bool,uint256,bytes)[])");
    private static readonly ABIFunctionSchema ExecTransaction = Function("execTransaction(address,uint256,bytes,uint8,uint256,uint256,uint256,address,address,bytes)");
    private static readonly ABIFunctionSchema ExecuteBatch = Function("executeBatch(address[],uint256[],bytes[])");
    private static readonly ABIFunctionSchema Burn = Function("burn(uint256)");
    private static readonly ABIFunctionSchema Collate = Function("collate_propagate_storage(bytes16)");
    private static readonly ABIFunctionSchema Pair = Function("pair(bytes,bytes)");
    private static readonly ABIFunctionSchema Both = Function("both(bytes[2])");
    private static readonly ABIFunctionSchema OperationBatch = Function("opBatch(address[],uint256[],bytes[],uint8[])");

    private static ExecutionDescriptor Nested(ABIFunctionSchema function, params NestedCallRule[] rules)
    {
        Assert.IsTrue(ExecutionDescriptor.TryCreateNested(function, rules, out ExecutionDescriptor? descriptor, out ExecutionDescriptorError error, out _), error.ToString());
        return descriptor;
    }

    /// <summary>The trusted registry of these tests. Router functions run under a delegatecall-to-self multicall, so the router registers transfer and approve too.</summary>
    private static ContractFunctionResolver Registry()
    {
        var builder = new ContractFunctionResolverBuilder();
        foreach (ABIFunctionSchema leaf in new[] { Transfer, Approve })
        {
            builder.Add(ChainId, Router, RouterCode, ExecutionDescriptor.NoNestedCalls(leaf));
            builder.Add(ChainId, Token, TokenCode, ExecutionDescriptor.NoNestedCalls(leaf));
        }

        return builder
            .Add(ChainId, Router, RouterCode, Nested(Multicall, NestedCallRule.DelegateCallToSelf([0, Each])))
            .Add(ChainId, Router, RouterCode, ExecutionDescriptor.NoNestedCalls(Execute)) // its bytes are data, by declaration
            .Add(ChainId, Router, RouterCode, Nested(RunFirst, NestedCallRule.DelegateCallToSelf([0, 0])))
            .Add(ChainId, Safe, SafeCode, ExecutionDescriptor.Undescribed(MultiSend))
            .Add(ChainId, Safe, SafeCode, Nested(ExecTransaction, NestedCallRule.CallOrDelegateCall([0], [2], [1], [3])))
            .Add(ChainId, Multicall3, Multicall3Code, Nested(Aggregate3, NestedCallRule.Call([0, Each, 0], [0, Each, 2])))
            .Add(ChainId, Multicall3, Multicall3Code, Nested(Aggregate3Value, NestedCallRule.CallWithValue([0, Each, 0], [0, Each, 3], [0, Each, 2])))
            .Add(ChainId, Account, AccountCode, Nested(ExecuteBatch, NestedCallRule.CallWithValue([0, Each], [2, Each], [1, Each])))
            .Add(ChainId, Router, RouterCode, Nested(Pair, NestedCallRule.DelegateCallToSelf([0]), NestedCallRule.DelegateCallToSelf([1])))
            .Add(ChainId, Router, RouterCode, Nested(Both, NestedCallRule.DelegateCallToSelf([0, Each])))
            .Add(ChainId, Account, AccountCode, Nested(OperationBatch, NestedCallRule.CallOrDelegateCall([0, Each], [2, Each], [1, Each], [3, Each])))
            .Add(ChainId, Library, LibraryCode, Nested(Multicall, NestedCallRule.DelegateCallToSelf([0, Each]))) // a multicall library the Safe delegatecalls
            .Add(ChainId, Token, TokenCode, ExecutionDescriptor.NoNestedCalls(Burn))
            .Add(ChainId, Token, TokenCode, ExecutionDescriptor.NoNestedCalls(Collate))
            .Build();
    }

    private static NestedCallWalker Walker(NestedCallLimits? limits = null) => new(Registry(), limits ?? NestedCallLimits.Default, ABIDecodeLimits.Default);

    // ------------------------------------------------------------------ calldata (independent encoder)

    private static RWord W(ulong value) => new(Word(value));

    private static RWord A(byte[] address20) => new([.. new byte[12], .. address20]);

    private static RBlob B(byte[] data) => new(data);

    private static RSeq S(params RValue[] items) => new(items);

    private static byte[] CallOf(ABIFunctionSchema function, params RValue[] arguments)
    {
        byte[] selector = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(selector, function.Selector);
        return [.. selector, .. ABIIndependentEncoder.EncodeArguments(ABIReference.ParseTuple(function.InputSchema.CanonicalType), new RSeq(arguments))];
    }

    private static byte[] TransferTo(byte[] to, ulong amount) => CallOf(Transfer, A(to), W(amount));

    private static byte[] ApproveMax(byte[] spender) => CallOf(Approve, A(spender), new RWord(Filled(0xff)));

    private static byte[] MulticallOf(params byte[][] children) => CallOf(Multicall, S([.. children.Select(B)]));

    private static byte[] SafeExec(byte[] to, ulong value, byte[] data, ulong operation) =>
        CallOf(ExecTransaction, A(to), W(value), B(data), W(operation), W(0), W(0), W(0), A(new byte[20]), A(new byte[20]), B(Filled(0x5a)));

    // ------------------------------------------------------------------ the host

    /// <summary>
    /// A recording host: code from dictionaries (contract identities, EIP-7702 delegations, empty accounts; anything else cannot be established), a policy
    /// predicate, and a check on every call that the validated view is exactly the reported range of the transaction calldata (same memory, not a copy) and
    /// was validated against the resolved function.
    /// </summary>
    private sealed class RecordingHost(byte[] buffer, int calldataOffset) : INestedCallHost
    {
        public readonly Dictionary<Address, Hash32> Identities = new()
        {
            [Router] = RouterCode, [Token] = TokenCode, [Multicall3] = Multicall3Code, [Safe] = SafeCode, [Account] = AccountCode, [Library] = LibraryCode,
        };

        public readonly Dictionary<Address, Address> Delegations = [];
        public readonly HashSet<Address> EmptyAccounts = [];
        public Func<NestedCall, bool> Policy = _ => true;
        public readonly List<NestedCall> Seen = [];
        public ulong ExpectedChain = ChainId;
        public int IdentityQueries;

        public bool TryGetCode(ulong chainId, in Address account, out EvmAccountCode code)
        {
            Assert.AreEqual(ExpectedChain, chainId, "the walk resolves on the transaction's chain");
            IdentityQueries++;
            if (Delegations.TryGetValue(account, out Address delegateAddress))
            {
                code = EvmAccountCode.DelegatedTo(delegateAddress);
                return true;
            }

            if (EmptyAccounts.Contains(account))
            {
                code = EvmAccountCode.Empty;
                return true;
            }

            bool known = Identities.TryGetValue(account, out Hash32 identity);
            code = known ? EvmAccountCode.Contract(identity) : default;
            return known;
        }

        public bool Allows(in NestedCall nestedCall, scoped in ABICallData calldata)
        {
            Assert.IsTrue(calldata.IsValid);
            ABIRange range = nestedCall.CalldataRange;
            Assert.AreEqual(range.Length, calldata.EncodedCall.Length, "the view covers exactly the reported range");
            Assert.IsTrue(Unsafe.AreSame(in MemoryMarshal.GetReference(calldata.EncodedCall), in buffer[calldataOffset + range.Offset]), "the view is the transaction's own bytes at the range");
            Assert.AreEqual(nestedCall.Function.Selector, calldata.Selector, "validated against the resolved function");
            Assert.AreEqual(Seen.Count, nestedCall.Index, "indexes are pre-order positions");
            Seen.Add(nestedCall);
            return Policy(nestedCall);
        }
    }

    private static NestedCallWalkResult Walk(
        byte[] calldata, out RecordingHost host, Func<NestedCall, bool>? policy = null, NestedCallLimits? limits = null, ulong value = 0, Address? to = null,
        Action<RecordingHost>? configure = null, ulong chain = ChainId)
    {
        host = new RecordingHost(calldata, 0) { ExpectedChain = chain };
        if (policy is not null)
        {
            host.Policy = policy;
        }

        configure?.Invoke(host);
        bool ok = Walker(limits).TryWalk(chain, Sender, to ?? Router, new uint256(value), calldata, ref host, out NestedCallWalkResult result);
        Assert.AreEqual(ok, result.Status == NestedCallStatus.Success, "the bool and the status agree");
        if (ok)
        {
            Assert.AreEqual(host.Seen.Count, result.CallCount);
            Assert.AreEqual(default, result.FailedCalldataRange);
            Assert.AreEqual(ABIStatus.Success, result.DecodeStatus);
        }
        else
        {
            Assert.IsTrue(result.FailedCalldataRange.Offset >= 0 && result.FailedCalldataRange.Offset + result.FailedCalldataRange.Length <= calldata.Length, "a failure range lies in the calldata");
        }

        return result;
    }

    private static ABIRange RangeOf(byte[] calldata, byte[] child)
    {
        int at = calldata.AsSpan().IndexOf(child);
        Assert.IsTrue(at >= 0 && calldata.AsSpan(at + 1).IndexOf(child) < 0, "the child bytes occur exactly once (an independent locator)");
        return new ABIRange(at, child.Length);
    }

    // ------------------------------------------------------------------ normal flow

    [TestMethod]
    public void ASimpleCall_IsResolvedValidatedAndReported_WithTheTransactionContext()
    {
        byte[] calldata = TransferTo(RecipientBytes, 5);
        NestedCallWalkResult result = Walk(calldata, out RecordingHost host, value: 9, to: Token);
        Assert.AreEqual(NestedCallStatus.Success, result.Status);
        Assert.AreEqual(1, result.CallCount);
        NestedCall call = host.Seen.Single();
        Assert.AreEqual((0, -1, 0), (call.Index, call.ParentIndex, call.Depth));
        Assert.AreEqual(NestedCallMode.Call, call.Mode);
        Assert.AreEqual(ChainId, call.ChainId);
        Assert.AreEqual(Token, call.CodeAddress);
        Assert.AreEqual(Token, call.ContextAddress);
        Assert.AreEqual(Sender, call.Sender);
        Assert.AreEqual(new uint256(9), call.Value);
        Assert.IsTrue(call.TransfersValue);
        Assert.AreEqual(TokenCode, call.CodeIdentity);
        Assert.AreSame(Transfer, call.Function);
        Assert.AreEqual(new ABIRange(0, 68), call.CalldataRange);
        Assert.AreEqual(1, host.IdentityQueries);
    }

    [TestMethod]
    public void AMulticall_DelegateCallsItsOwnCode_ChildrenInheritSenderAndValue_AndKeepTheSpecRanges()
    {
        // Spec 8.2: multicall(bytes[]) with two 68-byte children is 388 bytes; the children sit at (164,68) and (292,68) in calldata coordinates.
        byte[] first = TransferTo(RecipientBytes, 5);
        byte[] second = TransferTo(TokenBytes, 6);
        byte[] calldata = MulticallOf(first, second);
        Assert.AreEqual(388, calldata.Length);

        NestedCallWalkResult result = Walk(calldata, out RecordingHost host, value: 5);
        Assert.AreEqual(NestedCallStatus.Success, result.Status);
        Assert.AreEqual(3, result.CallCount);
        Assert.AreSame(Multicall, host.Seen[0].Function);
        Assert.AreEqual(new ABIRange(164, 68), host.Seen[1].CalldataRange);
        Assert.AreEqual(new ABIRange(292, 68), host.Seen[2].CalldataRange);
        Assert.AreEqual(RangeOf(calldata, first), host.Seen[1].CalldataRange);
        Assert.AreEqual(RangeOf(calldata, second), host.Seen[2].CalldataRange);
        foreach (NestedCall child in host.Seen.Skip(1))
        {
            Assert.AreEqual((0, 1), (child.ParentIndex, child.Depth));
            Assert.AreEqual(NestedCallMode.DelegateCall, child.Mode);
            Assert.AreEqual(Router, child.CodeAddress, "the router's own code runs");
            Assert.AreEqual(Router, child.ContextAddress, "in the router's context");
            Assert.AreEqual(Sender, child.Sender, "msg.sender is inherited");
            Assert.AreEqual(new uint256(5), child.Value, "msg.value is inherited");
            Assert.IsFalse(child.TransfersValue, "but nothing moves");
            Assert.AreSame(Transfer, child.Function, "each child resolved to its own schema");
        }
    }

    [TestMethod]
    public void NestedMulticalls_AreWalkedPreOrder_WithRangesInTransactionCoordinates()
    {
        byte[] a = TransferTo(RecipientBytes, 1);
        byte[] b = TransferTo(RecipientBytes, 2);
        byte[] c = TransferTo(RecipientBytes, 3);
        byte[] inner = MulticallOf(b, c);
        byte[] calldata = MulticallOf(a, inner);

        NestedCallWalkResult result = Walk(calldata, out RecordingHost host);
        Assert.AreEqual(NestedCallStatus.Success, result.Status);
        // Pre-order: outer, a, inner, b, c.
        (int Parent, int Depth, byte[] Bytes)[] expected = [(-1, 0, calldata), (0, 1, a), (0, 1, inner), (2, 2, b), (2, 2, c)];
        Assert.AreEqual(expected.Length, host.Seen.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i].Parent, host.Seen[i].ParentIndex, $"call {i}");
            Assert.AreEqual(expected[i].Depth, host.Seen[i].Depth, $"call {i}");
            Assert.AreEqual(i == 0 ? new ABIRange(0, calldata.Length) : RangeOf(calldata, expected[i].Bytes), host.Seen[i].CalldataRange, $"call {i}: root coordinates");
        }
    }

    [TestMethod]
    public void Multicall3_CallsEachTarget_AsTheMulticallContract()
    {
        byte[] toToken = TransferTo(RecipientBytes, 7);
        byte[] toRouter = ApproveMax(RecipientBytes);
        byte[] calldata = CallOf(Aggregate3, S(S(A(TokenBytes), W(0), B(toToken)), S(A(RouterBytes), W(1), B(toRouter))));
        NestedCallWalkResult result = Walk(calldata, out RecordingHost host, value: 0, to: Multicall3);
        Assert.AreEqual(NestedCallStatus.Success, result.Status);
        Assert.AreEqual(3, host.Seen.Count);
        (Address Target, byte[] Bytes, ABIFunctionSchema Function)[] expected = [(Token, toToken, Transfer), (Router, toRouter, Approve)];
        for (int i = 0; i < 2; i++)
        {
            NestedCall child = host.Seen[i + 1];
            Assert.AreEqual(NestedCallMode.Call, child.Mode);
            Assert.AreEqual(expected[i].Target, child.CodeAddress);
            Assert.AreEqual(expected[i].Target, child.ContextAddress);
            Assert.AreEqual(Multicall3, child.Sender, "msg.sender is Multicall3, not the transaction sender (an approval it makes is Multicall3's)");
            Assert.AreEqual(uint256.Zero, child.Value);
            Assert.AreSame(expected[i].Function, child.Function);
            Assert.AreEqual(RangeOf(calldata, expected[i].Bytes), child.CalldataRange);
        }

        // A target the registry does not know denies, with the child's range: first because the host has no identity for it, then because an identity
        // alone does not register a contract.
        byte[] unknownTarget = CallOf(Aggregate3, S(S(A(RecipientBytes), W(0), B(toToken))));
        NestedCallWalkResult noIdentity = Walk(unknownTarget, out _, to: Multicall3);
        Assert.AreEqual(NestedCallStatus.CodeIdentityUnavailable, noIdentity.Status);
        Assert.AreEqual(RangeOf(unknownTarget, toToken), noIdentity.FailedCalldataRange);
        NestedCallWalkResult notRegistered = Walk(unknownTarget, out _, to: Multicall3, configure: h => h.Identities[Recipient] = TokenCode);
        Assert.AreEqual(NestedCallStatus.UnknownContract, notRegistered.Status);
        Assert.AreEqual(RangeOf(unknownTarget, toToken), notRegistered.FailedCalldataRange);
        Assert.AreEqual(1, notRegistered.CallCount, "the aggregate3 call itself was allowed");
    }

    [TestMethod]
    public void Multicall3Value_CarriesEachCallsValue()
    {
        byte[] child = TransferTo(RecipientBytes, 1);
        byte[] calldata = CallOf(Aggregate3Value, S(S(A(TokenBytes), W(0), W(1_000), B(child)), S(A(TokenBytes), W(0), W(0), B(child))));
        NestedCallWalkResult result = Walk(calldata, out RecordingHost host, value: 1_000, to: Multicall3);
        Assert.AreEqual(NestedCallStatus.Success, result.Status);
        Assert.AreEqual(new uint256(1_000), host.Seen[1].Value);
        Assert.IsTrue(host.Seen[1].TransfersValue);
        Assert.AreEqual(uint256.Zero, host.Seen[2].Value);
    }

    [TestMethod]
    public void SafeExecTransaction_FollowsItsOperationArgument()
    {
        byte[] inner = TransferTo(RecipientBytes, 4);

        // operation 0: CALL from the Safe.
        NestedCallWalkResult call = Walk(SafeExec(TokenBytes, 0, inner, 0), out RecordingHost host, value: 0, to: Safe);
        Assert.AreEqual(NestedCallStatus.Success, call.Status);
        NestedCall child = host.Seen[1];
        Assert.AreEqual((NestedCallMode.Call, Token, Token, Safe), (child.Mode, child.CodeAddress, child.ContextAddress, child.Sender));

        // operation 1: DELEGATECALL: the target's code runs in the Safe's context, as the transaction sender, with the transaction's value.
        NestedCallWalkResult delegated = Walk(SafeExec(TokenBytes, 0, inner, 1), out host, value: 3, to: Safe);
        Assert.AreEqual(NestedCallStatus.Success, delegated.Status);
        child = host.Seen[1];
        Assert.AreEqual((NestedCallMode.DelegateCall, Token, Safe, Sender), (child.Mode, child.CodeAddress, child.ContextAddress, child.Sender));
        Assert.AreEqual(new uint256(3), child.Value);
        Assert.IsFalse(child.TransfersValue);
        Assert.AreEqual(TokenCode, child.CodeIdentity, "resolved by the code that runs, not by the context");

        // Any other operation, or a delegatecall carrying value, fails closed with the parent's range.
        foreach (var (operation, value) in new (ulong, ulong)[] { (2, 0), (255, 0), (1, 1) })
        {
            byte[] calldata = SafeExec(TokenBytes, value, inner, operation);
            NestedCallWalkResult refused = Walk(calldata, out host, to: Safe);
            Assert.AreEqual(NestedCallStatus.UnsupportedCallMode, refused.Status, $"operation {operation}, value {value}");
            Assert.AreEqual(new ABIRange(0, calldata.Length), refused.FailedCalldataRange);
            Assert.AreEqual(1, refused.CallCount, "the Safe call itself was allowed first");
        }

        // An operation word above uint8 is not valid ABI for the schema at all.
        byte[] wide = SafeExec(TokenBytes, 0, inner, 0);
        wide[4 + 3 * 32 + 30] = 0x01; // operation = 256
        Assert.AreEqual(NestedCallStatus.MalformedCalldata, Walk(wide, out _, to: Safe).Status);
    }

    [TestMethod]
    public void CallAndDelegateCallContexts_ComposeCorrectly_WhenCodeAndContextDiffer()
    {
        // Every parent at the root has code == context, which cannot tell them apart. Here they differ, three levels deep:
        // EOA -> Safe.execTransaction(op 1) -> DELEGATECALL Library.multicall (Library code, Safe context, EOA sender)
        //     -> DELEGATECALL to address(this) = the SAFE (Safe code, Safe context) execTransaction(op 0) -> CALL Token.transfer (sender: the Safe).
        byte[] leaf = TransferTo(RecipientBytes, 9);
        byte[] innerSafe = SafeExec(TokenBytes, 0, leaf, 0);
        byte[] library = MulticallOf(innerSafe);
        byte[] calldata = SafeExec(LibraryBytes, 0, library, 1);
        NestedCallWalkResult result = Walk(calldata, out RecordingHost host, value: 4, to: Safe);
        Assert.AreEqual(NestedCallStatus.Success, result.Status);
        Assert.AreEqual(4, host.Seen.Count);
        (NestedCallMode Mode, Address Code, Address Context, Address Sender, ulong Value, int Depth)[] expected =
        [
            (NestedCallMode.Call, Safe, Safe, Sender, 4, 0),
            (NestedCallMode.DelegateCall, Library, Safe, Sender, 4, 1),
            (NestedCallMode.DelegateCall, Safe, Safe, Sender, 4, 2), // "self" is address(this), the Safe, not the library's code address
            (NestedCallMode.Call, Token, Token, Safe, 0, 3),        // the CALL is made by the context (the Safe), not by the code (the library)
        ];
        for (int i = 0; i < expected.Length; i++)
        {
            NestedCall call = host.Seen[i];
            Assert.AreEqual(expected[i], (call.Mode, call.CodeAddress, call.ContextAddress, call.Sender, (ulong)call.Value, call.Depth), $"call {i}");
        }

        Assert.AreEqual(LibraryCode, host.Seen[1].CodeIdentity);
        Assert.AreEqual(SafeCode, host.Seen[2].CodeIdentity);
        Assert.AreEqual(RangeOf(calldata, leaf), host.Seen[3].CalldataRange, "depth 3, root coordinates");

        // Safe op 1 -> Multicall3 code in the Safe's context -> its CALLs are made by the Safe.
        byte[] viaMulticall3 = SafeExec(Multicall3Bytes, 0, CallOf(Aggregate3, S(S(A(TokenBytes), W(0), B(leaf)))), 1);
        Assert.AreEqual(NestedCallStatus.Success, Walk(viaMulticall3, out host, to: Safe).Status);
        Assert.AreEqual((Multicall3, Safe, Sender), (host.Seen[1].CodeAddress, host.Seen[1].ContextAddress, host.Seen[1].Sender));
        Assert.AreEqual((Token, Token, Safe), (host.Seen[2].CodeAddress, host.Seen[2].ContextAddress, host.Seen[2].Sender));

        // Multicall3 CALLs the router, whose multicall delegatecalls itself: the router's context, Multicall3 as sender.
        byte[] viaRouter = CallOf(Aggregate3, S(S(A(RouterBytes), W(0), B(MulticallOf(leaf)))));
        Assert.AreEqual(NestedCallStatus.Success, Walk(viaRouter, out host, to: Multicall3).Status);
        NestedCall delegated = host.Seen[2];
        Assert.AreEqual((NestedCallMode.DelegateCall, Router, Router, Multicall3), (delegated.Mode, delegated.CodeAddress, delegated.ContextAddress, delegated.Sender));
    }

    [TestMethod]
    public void EveryRule_IsWalked_InRuleOrder_AndFixedArraysIterate()
    {
        byte[] first = TransferTo(RecipientBytes, 1);
        byte[] second = ApproveMax(RecipientBytes);
        byte[] calldata = CallOf(Pair, B(first), B(second));
        NestedCallWalkResult result = Walk(calldata, out RecordingHost host);
        Assert.AreEqual(NestedCallStatus.Success, result.Status);
        Assert.AreEqual(3, host.Seen.Count, "both rules yield their child");
        Assert.AreEqual((RangeOf(calldata, first), RangeOf(calldata, second)), (host.Seen[1].CalldataRange, host.Seen[2].CalldataRange));
        Assert.AreEqual(NestedCallStatus.DeniedByHost, Walk(calldata, out _, policy: c => !ReferenceEquals(c.Function, Approve)).Status, "the second rule's child is evaluated too");

        byte[] fixedArray = CallOf(Both, S(B(first), B(second)));
        Assert.AreEqual(NestedCallStatus.Success, Walk(fixedArray, out host).Status);
        Assert.AreEqual(3, host.Seen.Count, "EachElement over bytes[2]");
        Assert.AreSame(Approve, host.Seen[2].Function);
    }

    [TestMethod]
    public void ParallelArrays_MustLineUp_ElementForElement()
    {
        byte[] one = TransferTo(RecipientBytes, 1);
        byte[] two = ApproveMax(RecipientBytes);
        byte[] batch = CallOf(ExecuteBatch, S(A(TokenBytes), A(RouterBytes)), S(W(10), W(20)), S(B(one), B(two)));
        NestedCallWalkResult result = Walk(batch, out RecordingHost host, to: Account);
        Assert.AreEqual(NestedCallStatus.Success, result.Status);
        Assert.AreEqual((Token, new uint256(10), Account), (host.Seen[1].CodeAddress, host.Seen[1].Value, host.Seen[1].Sender));
        Assert.AreEqual((Router, new uint256(20), Account), (host.Seen[2].CodeAddress, host.Seen[2].Value, host.Seen[2].Sender));

        byte[] shortValues = CallOf(ExecuteBatch, S(A(TokenBytes), A(RouterBytes)), S(W(10)), S(B(one), B(two)));
        NestedCallWalkResult mismatch = Walk(shortValues, out _, to: Account);
        Assert.AreEqual(NestedCallStatus.DescriptorMismatch, mismatch.Status, "values[] shorter than func[]");
        Assert.AreEqual(new ABIRange(0, shortValues.Length), mismatch.FailedCalldataRange);
        Assert.AreEqual(1, mismatch.CallCount, "the lengths are checked before any child is evaluated");

        // A LONGER target or value array would otherwise be silently ignored past the last calldata element.
        byte[] extraTarget = CallOf(ExecuteBatch, S(A(TokenBytes), A(RouterBytes), A(TokenBytes)), S(W(10), W(20)), S(B(one), B(two)));
        Assert.AreEqual(NestedCallStatus.DescriptorMismatch, Walk(extraTarget, out RecordingHost extraHost, to: Account).Status, "dest[] longer than func[]");
        Assert.AreEqual(1, extraHost.Seen.Count);
        byte[] extraValue = CallOf(ExecuteBatch, S(A(TokenBytes), A(RouterBytes)), S(W(10), W(20), W(30)), S(B(one), B(two)));
        Assert.AreEqual(NestedCallStatus.DescriptorMismatch, Walk(extraValue, out _, to: Account).Status, "value[] longer than func[]");
        byte[] operations = CallOf(OperationBatch, S(A(TokenBytes)), S(W(0)), S(B(one)), S(W(0)));
        Assert.AreEqual(NestedCallStatus.Success, Walk(operations, out _, to: Account).Status, "an iterated operation array of the right length");
        byte[] extraOperation = CallOf(OperationBatch, S(A(TokenBytes)), S(W(0)), S(B(one)), S(W(0), W(1)));
        Assert.AreEqual(NestedCallStatus.DescriptorMismatch, Walk(extraOperation, out _, to: Account).Status, "operation[] longer than calls[]");
        Assert.AreEqual(NestedCallStatus.DescriptorMismatch, Walk(CallOf(ExecuteBatch, S(A(TokenBytes)), S(W(10), W(20)), S(B(one), B(two))), out _, to: Account).Status, "targets shorter");
        Assert.AreEqual(NestedCallStatus.Success, Walk(CallOf(ExecuteBatch, S(), S(), S()), out host, to: Account).Status, "an empty batch is one call");
        Assert.AreEqual(1, host.Seen.Count);
    }

    // ------------------------------------------------------------------ fail closed

    [TestMethod]
    public void AForbiddenChild_DeniesTheWalk_EvenWhenEverythingAroundItIsAllowed()
    {
        byte[] approve = ApproveMax(RecipientBytes);
        byte[] calldata = MulticallOf(TransferTo(RecipientBytes, 5), approve, TransferTo(RecipientBytes, 6));
        NestedCallWalkResult result = Walk(calldata, out RecordingHost host, policy: c => !ReferenceEquals(c.Function, Approve));
        Assert.AreEqual(NestedCallStatus.DeniedByHost, result.Status);
        Assert.AreEqual(2, result.CallCount, "the outer call and the first child were allowed");
        Assert.AreEqual(RangeOf(calldata, approve), result.FailedCalldataRange);
        Assert.AreEqual(3, host.Seen.Count, "the walk stopped at the refusal: the third child was never evaluated");

        NestedCallWalkResult rootDenied = Walk(calldata, out host, policy: _ => false);
        Assert.AreEqual(NestedCallStatus.DeniedByHost, rootDenied.Status);
        Assert.AreEqual(0, rootDenied.CallCount);
        Assert.AreEqual(new ABIRange(0, calldata.Length), rootDenied.FailedCalldataRange);
    }

    [TestMethod]
    public void OpaqueBytes_StayOpaque_WhateverTheyContain()
    {
        // execute(bytes) is registered as executing nothing: the approve inside its argument is data, and is never resolved or shown to the policy.
        byte[] calldata = CallOf(Execute, B(ApproveMax(RecipientBytes)));
        NestedCallWalkResult result = Walk(calldata, out RecordingHost host, policy: c => !ReferenceEquals(c.Function, Approve));
        Assert.AreEqual(NestedCallStatus.Success, result.Status);
        Assert.AreEqual(1, host.Seen.Count);
        Assert.AreSame(Execute, host.Seen[0].Function);
    }

    [TestMethod]
    public void UndescribedExecution_FailsClosed_BeforeThePolicyIsAsked()
    {
        byte[] calldata = CallOf(MultiSend, B([0x00, .. TokenBytes, .. new byte[32], .. Word(68), .. TransferTo(RecipientBytes, 1)]));
        NestedCallWalkResult result = Walk(calldata, out RecordingHost host, to: Safe);
        Assert.AreEqual(NestedCallStatus.UndescribedExecution, result.Status);
        Assert.AreEqual(0, result.CallCount);
        Assert.IsEmpty(host.Seen);
    }

    [TestMethod]
    public void UnknownContractsFunctionsAndCodeIdentities_FailClosed()
    {
        byte[] unknownSelector = [.. Hex("deadbeef"), .. new byte[32]];
        byte[] calldata = MulticallOf(TransferTo(RecipientBytes, 1), unknownSelector);
        NestedCallWalkResult result = Walk(calldata, out _);
        Assert.AreEqual(NestedCallStatus.UnknownFunction, result.Status);
        Assert.AreEqual(RangeOf(calldata, unknownSelector), result.FailedCalldataRange);
        Assert.AreEqual(2, result.CallCount);

        Assert.AreEqual(NestedCallStatus.UnknownContract, Walk(TransferTo(RecipientBytes, 1), out _, to: Recipient, configure: h => h.Identities[Recipient] = TokenCode).Status,
            "an account the registry does not know");

        // The proxy was upgraded (or the delegation changed): the host reports the new identity, and the registry entry for the old code does not apply.
        NestedCallWalkResult stale = Walk(calldata, out RecordingHost upgraded, configure: h => h.Identities[Router] = UpgradedCode);
        Assert.AreEqual(NestedCallStatus.UnknownCodeIdentity, stale.Status);
        Assert.AreEqual(0, stale.CallCount);
        Assert.IsEmpty(upgraded.Seen);

        // Upgraded code at a CHILD's target: the outer call is fine, the child is refused.
        byte[] viaSafe = SafeExec(TokenBytes, 0, TransferTo(RecipientBytes, 1), 0);
        NestedCallWalkResult staleChild = Walk(viaSafe, out _, to: Safe, configure: h => h.Identities[Token] = UpgradedCode);
        Assert.AreEqual(NestedCallStatus.UnknownCodeIdentity, staleChild.Status);
        Assert.AreEqual(1, staleChild.CallCount);

        // The host cannot establish the identity at all.
        Assert.AreEqual(NestedCallStatus.CodeIdentityUnavailable, Walk(calldata, out _, configure: h => h.Identities.Clear()).Status);

        // Another chain: the registry binds the chain too.
        Assert.AreEqual(NestedCallStatus.UnknownContract, Walk(TransferTo(RecipientBytes, 1), out _, to: Token, chain: 1).Status);
    }

    [TestMethod]
    public void ARealSelectorCollision_IsAmbiguous_AndNeverResolvedByPicking()
    {
        byte[] calldata = [.. Hex("42966c68"), .. new byte[15], 0x05, .. new byte[16]]; // valid as burn(uint256) AND as collate_propagate_storage(bytes16)
        NestedCallWalkResult result = Walk(calldata, out RecordingHost host, to: Token);
        Assert.AreEqual(NestedCallStatus.AmbiguousFunction, result.Status);
        Assert.IsEmpty(host.Seen);
    }

    [TestMethod]
    public void MalformedAndShortCalls_FailClosed_WithTheirOwnStatusAndRange()
    {
        NestedCallWalkResult trailing = Walk([.. TransferTo(RecipientBytes, 1), 0x00], out _, to: Token);
        Assert.AreEqual((NestedCallStatus.MalformedCalldata, ABIStatus.NonCanonicalLayout), (trailing.Status, trailing.DecodeStatus));

        byte[] dirty = TransferTo(RecipientBytes, 1);
        dirty[4] = 0x01; // a non-zero high byte in the address word
        byte[] calldata = MulticallOf(TransferTo(RecipientBytes, 2), dirty);
        NestedCallWalkResult child = Walk(calldata, out _);
        Assert.AreEqual((NestedCallStatus.MalformedCalldata, ABIStatus.InvalidScalar), (child.Status, child.DecodeStatus));
        Assert.AreEqual(RangeOf(calldata, dirty), child.FailedCalldataRange);

        byte[] selectorOnly = Hex("a9059cbb");
        NestedCallWalkResult truncated = Walk(MulticallOf(selectorOnly), out _);
        Assert.AreEqual((NestedCallStatus.MalformedCalldata, ABIStatus.Truncated), (truncated.Status, truncated.DecodeStatus), "a selector-only match is not a call");

        foreach (int length in new[] { 0, 1, 3 })
        {
            byte[] shortChild = Hex("a9059cbb")[..length];
            byte[] withShort = MulticallOf(TransferTo(RecipientBytes, 2), shortChild);
            NestedCallWalkResult tooShort = Walk(withShort, out _);
            Assert.AreEqual(NestedCallStatus.NotAFunctionCall, tooShort.Status, $"{length} bytes: a fallback or receive call is not described");
            Assert.AreEqual(length, tooShort.FailedCalldataRange.Length);
        }

        Assert.AreEqual(NestedCallStatus.NotAFunctionCall, Walk(Hex("a9059c"), out _, to: Token).Status, "a root shorter than a selector");
        Assert.AreEqual(NestedCallStatus.DescriptorMismatch, Walk(CallOf(RunFirst, S()), out _).Status, "a fixed index past the end of an empty array");
        NestedCallWalkResult pathFailure = Walk(CallOf(RunFirst, S()), out _);
        Assert.AreEqual(ABIStatus.InvalidPath, pathFailure.DecodeStatus);
        Assert.AreEqual(NestedCallStatus.Success, Walk(CallOf(RunFirst, S(B(TransferTo(RecipientBytes, 1)), B(ApproveMax(RecipientBytes)))), out RecordingHost firstOnly).Status);
        Assert.AreEqual(2, firstOnly.Seen.Count, "only element 0 is declared executable");
    }

    // ------------------------------------------------------------------ limits

    [TestMethod]
    public void DepthAndCallLimits_AreEnforcedAtTheirBoundaries()
    {
        byte[] inner = MulticallOf(TransferTo(RecipientBytes, 1));
        byte[] calldata = MulticallOf(inner); // depth 0 -> 1 -> 2, three calls

        Assert.AreEqual(NestedCallStatus.Success, Walk(calldata, out _, limits: new NestedCallLimits(3, 2)).Status, "exactly at both limits");
        NestedCallWalkResult deep = Walk(calldata, out _, limits: new NestedCallLimits(3, 1));
        Assert.AreEqual(NestedCallStatus.DepthLimitExceeded, deep.Status);
        Assert.AreEqual(RangeOf(calldata, inner), deep.FailedCalldataRange, "the parent whose child would be too deep");
        Assert.AreEqual(2, deep.CallCount);
        NestedCallWalkResult rootOnly = Walk(calldata, out _, limits: new NestedCallLimits(3, 0));
        Assert.AreEqual(NestedCallStatus.DepthLimitExceeded, rootOnly.Status);
        Assert.AreEqual(1, rootOnly.CallCount);
        NestedCallWalkResult many = Walk(calldata, out _, limits: new NestedCallLimits(2, 2));
        Assert.AreEqual(NestedCallStatus.CallLimitExceeded, many.Status);
        Assert.AreEqual(2, many.CallCount);
        Assert.AreEqual(RangeOf(calldata, TransferTo(RecipientBytes, 1)), many.FailedCalldataRange, "the call that would exceed the limit");
        Assert.AreEqual(NestedCallStatus.Success, Walk(TransferTo(RecipientBytes, 1), out _, limits: new NestedCallLimits(1, 0), to: Token).Status, "one call, depth 0");
    }

    [TestMethod]
    public void HostileNestingAndFanOut_StopAtTheLimits_WithoutExhaustingTheStack()
    {
        byte[] nested = TransferTo(RecipientBytes, 1);
        for (int level = 0; level < 40; level++)
        {
            nested = MulticallOf(nested);
        }

        var hard = new NestedCallLimits(int.MaxValue, NestedCallLimits.HardMaxDepth);
        NestedCallWalkResult deep = Walk(nested, out _, limits: hard);
        Assert.AreEqual(NestedCallStatus.DepthLimitExceeded, deep.Status);
        Assert.AreEqual(NestedCallLimits.HardMaxDepth + 1, deep.CallCount, "depth 0 to 16 were walked");

        byte[] child = TransferTo(RecipientBytes, 1);
        byte[] wide = MulticallOf([.. Enumerable.Repeat(child, 300)]);
        NestedCallWalkResult fanOut = Walk(wide, out _);
        Assert.AreEqual(NestedCallStatus.CallLimitExceeded, fanOut.Status);
        Assert.AreEqual(NestedCallLimits.Default.MaxCalls, fanOut.CallCount);
    }

    [TestMethod]
    public void TheWalker_RefusesInvalidConfiguration_AndInvalidAddresses()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new NestedCallWalker(null!, NestedCallLimits.Default, ABIDecodeLimits.Default));
        foreach (NestedCallLimits bad in new[] { new NestedCallLimits(0, 8), new NestedCallLimits(-1, 8), new NestedCallLimits(1, -1), new NestedCallLimits(1, 17), default })
        {
            Assert.IsFalse(bad.IsValid, bad.ToString());
            Assert.ThrowsExactly<ArgumentException>(() => new NestedCallWalker(Registry(), bad, ABIDecodeLimits.Default), bad.ToString());
        }

        Assert.IsTrue(new NestedCallLimits(1, 0).IsValid);
        Assert.IsTrue(new NestedCallLimits(int.MaxValue, 16).IsValid);
        Assert.AreEqual(new NestedCallLimits(256, 8), NestedCallLimits.Default);
        Assert.ThrowsExactly<ArgumentException>(() => new NestedCallWalker(Registry(), NestedCallLimits.Default, default));

        byte[] calldata = TransferTo(RecipientBytes, 1);
        var host = new RecordingHost(calldata, 0);
        Assert.IsFalse(Walker().TryWalk(ChainId, Address.ZeroSolana, Token, uint256.Zero, calldata, ref host, out NestedCallWalkResult result));
        Assert.AreEqual(NestedCallStatus.InvalidArgument, result.Status);
        Assert.IsFalse(Walker().TryWalk(ChainId, Sender, Address.ZeroSolana, uint256.Zero, calldata, ref host, out result));
        Assert.AreEqual(NestedCallStatus.InvalidArgument, result.Status);
        Assert.IsEmpty(host.Seen);
    }

    // ------------------------------------------------------------------ the transaction overload (adapter -> walker)

    private static NestedCallWalkResult WalkEnvelope(byte[] envelope, EvmTransactionForm form, out RecordingHost host)
    {
        var limits = RLPDecodeLimits.Default;
        Assert.IsTrue(RLPCalldataAdapter.TryRead(envelope, form, in limits, out EvmTransactionView view, out EvmTransactionStatus status), status.ToString());
        host = new RecordingHost(envelope, view.DataRange.Offset);
        bool ok = Walker().TryWalk(in view, Sender, ref host, out NestedCallWalkResult result);
        Assert.AreEqual(ok, result.Status == NestedCallStatus.Success);
        if (ok)
        {
            // Every call's range maps back into the envelope at the bytes an independent search finds.
            foreach (NestedCall call in host.Seen)
            {
                Assert.IsTrue(view.TryMapCalldataRange(call.CalldataRange, out ABIRange inEnvelope));
                Assert.AreEqual(view.DataRange.Offset + call.CalldataRange.Offset, inEnvelope.Offset);
            }
        }

        return result;
    }

    [TestMethod]
    public void EveryEnvelopeType_IsWalkedFromItsOwnContext()
    {
        byte[] calldata = MulticallOf(TransferTo(RecipientBytes, 5), TransferTo(RecipientBytes, 6));
        foreach (var (name, type, signed, unsigned) in Envelopes(RouterBytes, calldata, value: 11))
        {
            foreach (var (form, envelope) in new[] { (EvmTransactionForm.SignedTransaction, signed), (EvmTransactionForm.SigningPayload, unsigned) })
            {
                NestedCallWalkResult result = WalkEnvelope(envelope, form, out RecordingHost host);
                if (type == EvmTransactionType.Eip7702)
                {
                    Assert.AreEqual(NestedCallStatus.AuthorizationsNotProcessed, result.Status, $"{name} {form}: the authorization list may change the code that runs");
                    Assert.IsEmpty(host.Seen);
                    continue;
                }

                Assert.AreEqual(NestedCallStatus.Success, result.Status, $"{name} {form}");
                Assert.AreEqual(3, host.Seen.Count);
                Assert.AreEqual(Router, host.Seen[0].CodeAddress, "to comes from the envelope");
                Assert.AreEqual(new uint256(11), host.Seen[0].Value, "value comes from the envelope");
                Assert.AreEqual(new ABIRange(164, 68), host.Seen[1].CalldataRange, "calldata coordinates, independent of the envelope");
            }
        }
    }

    [TestMethod]
    public void TransactionsThatAreNotFunctionCalls_OrHaveNoChainId_AreRefusedBeforeResolution()
    {
        byte[] call = TransferTo(RecipientBytes, 5);
        (byte[] Envelope, NestedCallStatus Expected)[] cases =
        [
            (Typed1559(RouterBytes, []), NestedCallStatus.NotAFunctionCall),
            (Typed1559([], call), NestedCallStatus.NotAFunctionCall),
            (Typed1559(RouterBytes, Hex("a9059c")), NestedCallStatus.NotAFunctionCall),
            (Encode(null, LegacyFields(TokenBytes, call, v: Number(27))), NestedCallStatus.MissingChainId),
            (Typed7702(TokenBytes, call), NestedCallStatus.AuthorizationsNotProcessed),
        ];
        foreach (var (envelope, expected) in cases)
        {
            NestedCallWalkResult result = WalkEnvelope(envelope, EvmTransactionForm.SignedTransaction, out RecordingHost host);
            Assert.AreEqual(expected, result.Status);
            Assert.AreEqual(0, host.IdentityQueries, "nothing was resolved");
        }

        EvmTransactionView none = default;
        var anyHost = new RecordingHost(call, 0);
        Assert.IsFalse(Walker().TryWalk(in none, Sender, ref anyHost, out NestedCallWalkResult defaultResult));
        Assert.AreEqual(NestedCallStatus.NotAFunctionCall, defaultResult.Status);

        // The explicit-root overload applies no authorization list: the caller vouches that the host's code is the code in effect.
        var limits = RLPDecodeLimits.Default;
        byte[] setCode = Typed7702(TokenBytes, call);
        Assert.IsTrue(RLPCalldataAdapter.TryRead(setCode, EvmTransactionForm.SignedTransaction, in limits, out EvmTransactionView view, out _));
        Assert.IsTrue(view.TryGetFunctionCalldata(out ReadOnlySpan<byte> calldata));
        var host7702 = new RecordingHost(setCode, view.DataRange.Offset);
        Assert.IsTrue(Walker().TryWalk(view.ChainId!.Value, Sender, view.To, view.Value, calldata, ref host7702, out NestedCallWalkResult explicitRoot));
        Assert.AreEqual(1, explicitRoot.CallCount);
    }

    // ------------------------------------------------------------------ allocation, ref struct hosts, fuzzing

    /// <summary>An allocation-free host: identities in a fixed array, every call allowed and counted.</summary>
    private struct CountingHost(Address[] addresses, Hash32[] identities) : INestedCallHost
    {
        public int Calls;

        public readonly bool TryGetCode(ulong chainId, in Address account, out EvmAccountCode code)
        {
            for (int i = 0; i < addresses.Length; i++)
            {
                if (addresses[i] == account)
                {
                    code = EvmAccountCode.Contract(identities[i]);
                    return true;
                }
            }

            code = default;
            return false;
        }

        public bool Allows(in NestedCall nestedCall, scoped in ABICallData calldata)
        {
            Calls++;
            return true;
        }
    }

    [TestMethod]
    public void AWalk_AllocatesNothing_WithAStructHost()
    {
        NestedCallWalker walker = Walker();
        byte[] calldata = MulticallOf(TransferTo(RecipientBytes, 5), MulticallOf(TransferTo(RecipientBytes, 6), ApproveMax(RecipientBytes)));
        byte[] refused = MulticallOf(TransferTo(RecipientBytes, 5), [.. Hex("deadbeef"), .. new byte[32]]);
        var host = new CountingHost([Router, Token], [RouterCode, TokenCode]);
        Address sender = Sender;
        Address router = Router;
        void Run()
        {
            Assert.IsTrue(walker.TryWalk(ChainId, sender, router, uint256.Zero, calldata, ref host, out _));
            Assert.IsFalse(walker.TryWalk(ChainId, sender, router, uint256.Zero, refused, ref host, out _));
        }

        for (int i = 0; i < 50; i++)
        {
            Run();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            Run();
        }

        Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.AreEqual(250 * 7, host.Calls, "five calls allowed per success, two per refusal");
    }

    /// <summary>A ref struct host may hold spans, for example the transaction bytes it signs.</summary>
    private ref struct SpanHost(ReadOnlySpan<byte> calldata) : INestedCallHost
    {
        private readonly ReadOnlySpan<byte> _calldata = calldata;
        public int Matched;

        public readonly bool TryGetCode(ulong chainId, in Address account, out EvmAccountCode code)
        {
            code = account == Router ? EvmAccountCode.Contract(RouterCode) : default;
            return account == Router;
        }

        public bool Allows(in NestedCall nestedCall, scoped in ABICallData calldata)
        {
            ABIRange range = nestedCall.CalldataRange;
            Matched += Unsafe.AreSame(in MemoryMarshal.GetReference(calldata.EncodedCall), in _calldata[range.Offset]) ? 1 : 0;
            return true;
        }
    }

    [TestMethod]
    public void ARefStructHost_IsSupported()
    {
        byte[] calldata = MulticallOf(TransferTo(RecipientBytes, 5), TransferTo(RecipientBytes, 6));
        var host = new SpanHost(calldata);
        Assert.IsTrue(Walker().TryWalk(ChainId, Sender, Router, uint256.Zero, calldata, ref host, out NestedCallWalkResult result));
        Assert.AreEqual(3, result.CallCount);
        Assert.AreEqual(3, host.Matched);
    }

    [TestMethod]
    public void MutatedNestedCalldata_NeverThrows_AndEverySuccessIsConsistent()
    {
        // Spec 10.4: deterministic mutation of a nested call tree. The recording host checks every call it is shown (exact memory, resolved selector).
        byte[] original = MulticallOf(TransferTo(RecipientBytes, 5), MulticallOf(TransferTo(RecipientBytes, 6), ApproveMax(RecipientBytes)), CallOf(Execute, B(ApproveMax(RecipientBytes))));
        var rng = new DeterministicRng(8453);
        int successes = 0;
        var statuses = new HashSet<NestedCallStatus>();
        for (int round = 0; round < 4_000; round++)
        {
            byte[] mutant = ABIDecodeDifferentialTests.Mutate(original, rng);
            NestedCallWalkResult result = Walk(mutant, out RecordingHost host);
            statuses.Add(result.Status);
            Assert.AreEqual(result.CallCount, host.Seen.Count, $"round {round}: the host allows everything, so every call it saw counts");
            if (result.Status == NestedCallStatus.Success)
            {
                successes++;
                Assert.IsTrue(host.Seen.All(c => c.Depth <= 2 && (c.Depth == 0 || c.ParentIndex < c.Index)), $"round {round}: parents precede children");
            }
        }

        // Strict validation turns almost every byte mutation into malformed calldata; a mutated selector gives an unknown function. Both must occur.
        CollectionAssert.IsSubsetOf(new[] { NestedCallStatus.MalformedCalldata, NestedCallStatus.UnknownFunction, NestedCallStatus.Success }, statuses.ToArray(), string.Join(", ", statuses));
        Assert.IsGreaterThan(10, successes, "and some mutants stay valid");
    }
}
