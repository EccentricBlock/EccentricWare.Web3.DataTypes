using System.Buffers.Binary;
using EccentricWare.Web3.DataTypes.ABI;
using EccentricWare.Web3.DataTypes.ABI.EvmAdapters;
using EccentricWare.Web3.DataTypes.Rlp;
using EccentricWare.Web3.DataTypes.Utils;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.EvmAdapters.EvmAdapterFixtures;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Spec 10.5, end to end, on the LIBRARY's components: owned transaction bytes -> <see cref="RLPCalldataAdapter"/> (zero-copy calldata) -> target identity
/// resolution (<see cref="ContractFunctionResolver"/> with a host-supplied code identity) -> strict <see cref="ABICallData"/> validation -> selector and argument
/// rules -> nested <c>bytes[]</c> children only where an <see cref="ExecutionDescriptor"/> declares them (<see cref="NestedCallWalker"/>) -> child-specific schema
/// validation -> policy denial of a forbidden child -> approval bound to the digest of the exact bytes. Then one test per spec 10.5 claim that the thing named
/// cannot authorise a signature.
/// <para>
/// What is test-local, as spec 10.5 allows: the policy (an allow-list host) and the signing boundary (<see cref="Sign"/> and <see cref="SignerAccepts"/>, a
/// deterministic fake with a Keccak-256 digest). Tests marked <c>IllustrativeFakes</c> are the ones whose claim rests on that fake signer (digest binding);
/// every other claim is enforced by library code (the adapter, resolver, walker or decoder) and would fail if that code stopped enforcing it.
/// </para>
/// </summary>
[TestClass]
public sealed class ABISecurityContractTests
{
    private static readonly byte[] TokenBytes = Hex("a0b86991c6218b36c1d19d4a2e9eb0ce3606eb48");
    private static readonly Address Token = Address.FromEvmBytes(TokenBytes);
    private static readonly Hash32 RouterV1 = new(0, 0, 0, 0x01);
    private static readonly Hash32 RouterV2 = new(0, 0, 0, 0x02);
    private static readonly Hash32 TokenCode = new(0, 0, 0, 0x20);

    private static readonly ABIFunctionSchema Multicall = Function("multicall(bytes[])");
    private static readonly ABIFunctionSchema Transfer = Function("transfer(address,uint256)");
    private static readonly ABIFunctionSchema Approve = Function("approve(address,uint256)");
    private static readonly ABIFunctionSchema Execute = Function("execute(bytes)");
    private static readonly ABIFunctionSchema Burn = Function("burn(uint256)");
    private static readonly ABIFunctionSchema Collate = Function("collate_propagate_storage(bytes16)");

    // ------------------------------------------------------------------ trusted metadata (library types, test data)

    private static ContractFunctionResolver TrustedRegistry(Action<ContractFunctionResolverBuilder>? extra = null)
    {
        Assert.IsTrue(ExecutionDescriptor.TryCreateNested(Multicall, [NestedCallRule.DelegateCallToSelf([0, NestedCallRule.EachElement])], out ExecutionDescriptor? multicall, out _, out _));
        var builder = new ContractFunctionResolverBuilder()
            .Add(ChainId, Router, RouterV1, multicall)
            .Add(ChainId, Router, RouterV1, ExecutionDescriptor.NoNestedCalls(Transfer))
            .Add(ChainId, Router, RouterV1, ExecutionDescriptor.NoNestedCalls(Approve))
            .Add(ChainId, Router, RouterV1, ExecutionDescriptor.Undescribed(Execute)) // execute(bytes) runs its bytes somehow: nobody described how
            .Add(ChainId, Token, TokenCode, ExecutionDescriptor.NoNestedCalls(Approve));
        extra?.Invoke(builder);
        return builder.Build();
    }

    /// <summary>The policy and identity source: the code identities observed now, approve forbidden, transfers only to the allow-listed recipient.</summary>
    private sealed class PolicyHost(Hash32 routerIdentity) : INestedCallHost
    {
        public readonly List<string> Evaluated = [];

        public bool TryGetCode(ulong chainId, in Address account, out EvmAccountCode code)
        {
            code = account == Router ? EvmAccountCode.Contract(routerIdentity) : account == Token ? EvmAccountCode.Contract(TokenCode) : default;
            return account == Router || account == Token;
        }

        public bool Allows(in NestedCall nestedCall, scoped in ABICallData calldata)
        {
            Evaluated.Add(nestedCall.Function.Name);
            if (ReferenceEquals(nestedCall.Function, Approve))
            {
                return false;
            }

            return !ReferenceEquals(nestedCall.Function, Transfer)
                || (calldata.Arguments.TryGetAddress([0], out Address to, out _) && to == Recipient);
        }
    }

    // ------------------------------------------------------------------ the fake signing boundary (test-local, spec 10.5)

    private sealed record Decision(bool Approved, NestedCallStatus Status, EvmTransactionStatus Envelope, byte[]? Digest);

    private static byte[] Digest(ReadOnlySpan<byte> envelope, ulong chainId, ReadOnlySpan<byte> signer)
    {
        byte[] header = new byte[16];
        BinaryPrimitives.WriteInt32BigEndian(header, envelope.Length);
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(4), chainId);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(12), signer.Length);
        byte[] preimage = [.. header, .. envelope, .. signer];
        byte[] digest = new byte[32];
        Keccak256.ComputeHash(preimage, digest);
        return digest;
    }

    /// <summary>The whole pipeline over the signing payload: adapter, walker with the trusted registry and the policy host, then a digest of the exact bytes.</summary>
    private static Decision Sign(byte[] envelope, ContractFunctionResolver registry, PolicyHost host)
    {
        var rlpLimits = RLPDecodeLimits.Default;
        if (!RLPCalldataAdapter.TryRead(envelope, EvmTransactionForm.SigningPayload, in rlpLimits, out EvmTransactionView transaction, out EvmTransactionStatus envelopeStatus))
        {
            return new Decision(false, NestedCallStatus.NotAFunctionCall, envelopeStatus, null);
        }

        var walker = new NestedCallWalker(registry, NestedCallLimits.Default, ABIDecodeLimits.Default);
        bool allowed = walker.TryWalk(in transaction, Sender, ref host, out NestedCallWalkResult result);
        return new Decision(allowed, result.Status, EvmTransactionStatus.Success, allowed ? Digest(envelope, transaction.ChainId!.Value, SenderBytes) : null);
    }

    /// <summary>The signer refuses anything not bound to the digest of the exact bytes presented at signing time.</summary>
    private static bool SignerAccepts(Decision decision, ReadOnlySpan<byte> bytesAtSigning, ulong chainId) =>
        decision.Approved && decision.Digest is not null && decision.Digest.AsSpan().SequenceEqual(Digest(bytesAtSigning, chainId, SenderBytes));

    private static byte[] TransferTo(byte[] recipient, ulong amount) => [.. Hex("a9059cbb"), .. new byte[12], .. recipient, .. Word(amount)];

    private static byte[] ApproveMax(byte[] spender) => [.. Hex("095ea7b3"), .. new byte[12], .. spender, .. Filled(0xff)];

    private static byte[] MulticallOf(params byte[][] children) =>
        [.. Hex("ac9650d8"), .. Word(0x20), .. Word((ulong)children.Length), .. OffsetTable(children), .. children.SelectMany(BytesValue)];

    private static byte[] OffsetTable(byte[][] children)
    {
        var table = new List<byte>();
        ulong offset = (ulong)children.Length * 32;
        foreach (byte[] child in children)
        {
            table.AddRange(Word(offset));
            offset += (ulong)BytesValue(child).Length;
        }

        return [.. table];
    }

    /// <summary>An EIP-1559 signing payload (what a signer hashes), to the router, carrying <paramref name="calldata"/>.</summary>
    private static byte[] Payload(byte[] calldata, byte[]? to = null) => N1559(to ?? RouterBytes, calldata).GetRLPEncodedRaw();

    // ------------------------------------------------------------------ the end-to-end flow

    [TestMethod]
    public void EndToEnd_AnAllowedMulticall_IsApproved_AndEveryChildWasEvaluatedWithItsOwnSchema()
    {
        byte[] envelope = Payload(MulticallOf(TransferTo(RecipientBytes, 5), TransferTo(RecipientBytes, 6)));
        var host = new PolicyHost(RouterV1);
        Decision decision = Sign(envelope, TrustedRegistry(), host);
        Assert.IsTrue(decision.Approved, decision.Status.ToString());
        CollectionAssert.AreEqual(new[] { "multicall", "transfer", "transfer" }, host.Evaluated, "the outer call and each declared child, each resolved and validated");
        Assert.IsTrue(SignerAccepts(decision, envelope, ChainId));
    }

    [TestMethod]
    public void EndToEnd_AnAllowedOuterCall_CannotHideAForbiddenChild()
    {
        byte[] envelope = Payload(MulticallOf(TransferTo(RecipientBytes, 5), ApproveMax(RouterBytes)));
        var host = new PolicyHost(RouterV1);
        Decision decision = Sign(envelope, TrustedRegistry(), host);
        Assert.IsFalse(decision.Approved);
        Assert.AreEqual(NestedCallStatus.DeniedByHost, decision.Status);
        Assert.IsNull(decision.Digest, "a denial carries nothing to sign");
        CollectionAssert.AreEqual(new[] { "multicall", "transfer", "approve" }, host.Evaluated);
    }

    [TestMethod]
    public void EndToEnd_NestedChildren_AreEnumeratedRecursively_AndAnUnknownGrandchildDenies()
    {
        byte[] inner = MulticallOf(TransferTo(RecipientBytes, 1), [.. Hex("deadbeef"), .. new byte[32]]);
        Decision decision = Sign(Payload(MulticallOf(TransferTo(RecipientBytes, 2), inner)), TrustedRegistry(), new PolicyHost(RouterV1));
        Assert.IsFalse(decision.Approved);
        Assert.AreEqual(NestedCallStatus.UnknownFunction, decision.Status);
    }

    // ------------------------------------------------------------------ things that cannot authorise a signature (spec 10.5)

    [TestMethod]
    public void ASelectorOnlyMatch_CannotAuthorise()
    {
        // The selector of transfer resolves, but there are no arguments: strict validation refuses it.
        Assert.AreEqual(NestedCallStatus.MalformedCalldata, Sign(Payload(Hex("a9059cbb")), TrustedRegistry(), new PolicyHost(RouterV1)).Status);
        byte[] garbage = [.. Hex("a9059cbb"), .. Filled(0xff), .. Word(1)]; // address word with non-zero high bytes
        Assert.AreEqual(NestedCallStatus.MalformedCalldata, Sign(Payload(garbage), TrustedRegistry(), new PolicyHost(RouterV1)).Status);
        Assert.AreEqual(NestedCallStatus.MalformedCalldata, Sign(Payload([.. TransferTo(RecipientBytes, 5), 0x00]), TrustedRegistry(), new PolicyHost(RouterV1)).Status, "trailing byte");
    }

    [TestMethod]
    public void AnUntrustedFourByteDatabaseMatch_CannotAuthorise()
    {
        // transfer(address,uint256) is the most famous selector there is, but the token is registered with approve only, and other chains with nothing.
        Assert.AreEqual(NestedCallStatus.UnknownFunction, Sign(Payload(TransferTo(RecipientBytes, 5), TokenBytes), TrustedRegistry(), new PolicyHost(RouterV1)).Status);
        Assert.AreEqual(NestedCallStatus.UnknownContract, Sign(Payload(TransferTo(RecipientBytes, 5), TokenBytes), new ContractFunctionResolverBuilder().Build(), new PolicyHost(RouterV1)).Status);
    }

    [TestMethod]
    public void AnAmbiguousSelectorResolution_IsRejected_NotResolvedByPickingTheFirst()
    {
        // A REAL four-byte collision (Keccak-256, checked independently with Foundry `cast sig`): burn(uint256) and collate_propagate_storage(bytes16).
        Assert.AreEqual(Burn.Selector, Collate.Selector);
        byte[] call = [.. Hex("42966c68"), .. new byte[15], 0x05, .. new byte[16]]; // valid under both schemas
        var limits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABICallData.TryDecode(call, Burn, in limits, out _, out _));
        Assert.IsTrue(ABICallData.TryDecode(call, Collate, in limits, out _, out _));
        ContractFunctionResolver registry = TrustedRegistry(b => b.Add(ChainId, Router, RouterV1, ExecutionDescriptor.NoNestedCalls(Burn)).Add(ChainId, Router, RouterV1, ExecutionDescriptor.NoNestedCalls(Collate)));
        Assert.AreEqual(NestedCallStatus.AmbiguousFunction, Sign(Payload(call), registry, new PolicyHost(RouterV1)).Status);
    }

    [TestMethod]
    public void AnUnknownExecutionDescriptor_CannotAuthorise_EvenForStructurallyValidCalls()
    {
        // execute(bytes) is registered, but as Undescribed: nobody has said what its bytes do, so the walker refuses before the policy sees it.
        byte[] call = [.. Hex(Execute.Selector.ToString("x8")), .. Word(0x20), .. BytesValue(TransferTo(RecipientBytes, 5))];
        var limits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABICallData.TryDecode(call, Execute, in limits, out _, out _), "structurally valid");
        var host = new PolicyHost(RouterV1);
        Assert.AreEqual(NestedCallStatus.UndescribedExecution, Sign(Payload(call), TrustedRegistry(), host).Status);
        Assert.IsEmpty(host.Evaluated);
    }

    [TestMethod]
    public void AChangedProxyImplementation_CannotAuthoriseAgainstTheOldRegistryEntry()
    {
        byte[] envelope = Payload(MulticallOf(TransferTo(RecipientBytes, 5)));
        Assert.IsTrue(Sign(envelope, TrustedRegistry(), new PolicyHost(RouterV1)).Approved);
        Decision afterUpgrade = Sign(envelope, TrustedRegistry(), new PolicyHost(RouterV2));
        Assert.IsFalse(afterUpgrade.Approved);
        Assert.AreEqual(NestedCallStatus.UnknownCodeIdentity, afterUpgrade.Status);
    }

    [TestMethod]
    public void ADelegationChangeInsideTheTransaction_CannotAuthorise_WithoutTheProcessedAuthorizationList()
    {
        // An EIP-7702 transaction installs code before it runs: walked without its processed authorization list, the code observed now may not be the
        // code that runs, so the walk refuses. (Eip7702Tests covers the processed flow, including a delegation to unregistered code.)
        byte[] setCode = N7702(RouterBytes, MulticallOf(TransferTo(RecipientBytes, 5))).GetRLPEncodedRaw();
        Decision decision = Sign(setCode, TrustedRegistry(), new PolicyHost(RouterV1));
        Assert.IsFalse(decision.Approved);
        Assert.AreEqual(NestedCallStatus.AuthorizationsNotProcessed, decision.Status);
    }

    [TestMethod]
    public void ContextsThatAreNotFunctionCalls_NeverReachTheRegistryOrThePolicy()
    {
        var host = new PolicyHost(RouterV1);
        Assert.AreEqual(NestedCallStatus.NotAFunctionCall, Sign(N1559([], TransferTo(RecipientBytes, 5)).GetRLPEncodedRaw(), TrustedRegistry(), host).Status, "initcode");
        Assert.AreEqual(NestedCallStatus.NotAFunctionCall, Sign(Payload([]), TrustedRegistry(), host).Status, "receive()");
        Assert.AreEqual(NestedCallStatus.NotAFunctionCall, Sign(Payload(Hex("a9")), TrustedRegistry(), host).Status, "fallback()");
        Assert.AreEqual(EvmTransactionStatus.WrongFieldCount, Sign(N1559(RouterBytes, TransferTo(RecipientBytes, 5)).GetRLPEncoded(), TrustedRegistry(), host).Envelope,
            "a signed transaction presented as a signing payload");
        Assert.IsEmpty(host.Evaluated);
    }

    [TestMethod]
    [TestCategory("IllustrativeFakes")] // the binding is the fake signer's: it shows what the library's zero-copy, exact-range contract lets a signer do
    public void AMutatedInputBuffer_CannotUseAnEarlierApproval()
    {
        byte[] envelope = Payload(MulticallOf(TransferTo(RecipientBytes, 5)));
        Decision approval = Sign(envelope, TrustedRegistry(), new PolicyHost(RouterV1));
        Assert.IsTrue(SignerAccepts(approval, envelope, ChainId));

        byte[] mutated = (byte[])envelope.Clone();
        mutated[envelope.AsSpan().IndexOf(TransferTo(RecipientBytes, 5)) + 67] ^= 0x01; // the amount changes after validation (TOCTOU)
        Assert.IsFalse(SignerAccepts(approval, mutated, ChainId), "the digest binding detects the change");
        Decision second = Sign(mutated, TrustedRegistry(), new PolicyHost(RouterV1));
        CollectionAssert.AreNotEqual(approval.Digest, second.Digest, "a new decision is a new digest");

        byte[] outside = (byte[])envelope.Clone();
        outside[1] ^= 0x01; // a change outside the calldata (the chain id field)
        Assert.IsFalse(SignerAccepts(approval, outside, ChainId), "the digest covers the whole envelope, not just the calldata");
        Assert.IsFalse(SignerAccepts(approval, envelope, ChainId + 1), "and the chain");
    }

    // ------------------------------------------------------------------ codec-level guarantees the composition relies on

    [TestMethod]
    public void OpaqueBytesContainingKnownSelectors_AreNotTraversedByTheCodec()
    {
        // A bytes argument that happens to hold approve(spender, max): the codec returns the payload range and nothing more.
        byte[] approveMax = ApproveMax(RouterBytes);
        byte[] call = [.. Hex(Execute.Selector.ToString("x8")), .. Word(0x20), .. BytesValue(approveMax)];
        var limits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABICallData.TryDecode(call, Execute, in limits, out var decoded, out _));
        Assert.IsTrue(decoded.Arguments.TryGetBytePayload([0], out var payload, out ABIRange range, out _));
        CollectionAssert.AreEqual(approveMax, payload.ToArray());
        Assert.AreEqual(new ABIRange(64, 68), range);
        Assert.IsFalse(decoded.Arguments.TryLocate([0, 0], out _, out var status), "no synthetic child view exists for a bytes payload");
        Assert.AreEqual(ABIStatus.InvalidPath, status);
    }

    [TestMethod]
    public void AViewOverMutatedBytes_StillNeverReadsOutsideTheDocument()
    {
        // The codec does not detect mutation (documented); it only promises memory safety. Corrupt the offset table of a validated multicall.
        byte[] calldata = MulticallOf(TransferTo(RecipientBytes, 5), TransferTo(RecipientBytes, 6));
        var limits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABICallData.TryDecode(calldata, Multicall, in limits, out var call, out _));
        Word(0x7fff_ffe0).CopyTo(calldata, 4 + 64);      // first element offset now points far outside
        Word(ulong.MaxValue).CopyTo(calldata, 4 + 96);   // second element offset: 2^64 - 1
        Assert.IsFalse(call.Arguments.TryGetBytePayload([0, 0], out var payload, out var range, out var status));
        Assert.AreEqual(default, range);
        Assert.AreEqual(0, payload.Length);
        Assert.IsTrue(status is ABIStatus.InvalidOffset or ABIStatus.Truncated or ABIStatus.NonCanonicalLayout, status.ToString());
        Assert.IsFalse(call.Arguments.TryGetBytePayload([0, 1], out _, out _, out status));
    }
}
