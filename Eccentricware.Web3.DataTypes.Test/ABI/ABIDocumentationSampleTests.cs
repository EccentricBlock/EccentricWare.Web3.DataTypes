using EccentricWare.Web3.DataTypes.ABI;
using EccentricWare.Web3.DataTypes.ABI.EvmAdapters;
using EccentricWare.Web3.DataTypes.Rlp;
using EccentricWare.Web3.DataTypes.Tests.ABI.EvmAdapters;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// The code snippets of README.md and docs/abi-api.md, compiled and run, so a documentation example cannot drift from the API. The expected
/// bytes are the independent <c>cast</c> fixture (<see cref="ABIFixtures.TransferVitalik1Eth"/>), not derived from the code under test.
/// </summary>
[TestClass]
public sealed class ABIDocumentationSampleTests
{
    private static readonly byte[] Calldata = Hex(ABIFixtures.TransferVitalik1Eth);
    private static readonly byte[] RecipientBytes20 = Hex("d8dA6BF26964aF9D7eEd9e03E53415D37aA96045");

    [TestMethod]
    public void TheReadme_HasOneAbiSection_AndTeachesNoSixtyFourBitAmount()
    {
        // The README is the package readme. A duplicated section, RLP text pasted into the ABI list, or the old TryWriteUInt64 amount example
        // is the exact drift the snippet tests above cannot see, because they re-type the code.
        string? directory = AppContext.BaseDirectory;
        string? readmePath = null;
        while (directory is not null)
        {
            string candidate = System.IO.Path.Combine(directory, "README.md");
            if (File.Exists(candidate) && File.Exists(System.IO.Path.Combine(directory, "EccentricWare.Web3.DataTypes.slnx")))
            {
                readmePath = candidate;
                break;
            }

            directory = System.IO.Path.GetDirectoryName(directory);
        }

        if (readmePath is null)
        {
            Assert.Inconclusive("README.md not found above the test output directory (package-only checkout).");
            return;
        }

        string readme = File.ReadAllText(readmePath);
        string[] lines = readme.Split('\n');
        Assert.AreEqual(1, lines.Count(l => l.TrimEnd() == "## ABI"), "exactly one ## ABI heading");
        Assert.IsFalse(readme.Contains("TryWriteUInt64(1_000_000"), "the 64-bit amount example must not return");

        int start = Array.FindIndex(lines, l => l.TrimEnd() == "## ABI");
        int end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## "));
        string section = string.Join('\n', lines[start..end]);
        Assert.IsTrue(section.Contains("TryGetUInt256("), "the typed amount read");
        Assert.IsFalse(section.Contains("RLPDecodeLimits") || section.Contains("RLPWriter") || section.Contains("TryIndexDirectChildren"), "no RLP text inside the ABI section");
        Assert.IsTrue(section.Contains("uint256 amountToSend") && section.Contains("calldata"), "the example declares its inputs");
    }

    /// <summary>The host of the docs/abi-integration.md example: a pinned code snapshot and a policy.</summary>
    private sealed class MyHost(Dictionary<Address, EvmAccountCode> stateSnapshot, Func<NestedCall, bool> policy) : INestedCallHost
    {
        public bool TryGetCode(ulong chainId, in Address account, out EvmAccountCode code) => stateSnapshot.TryGetValue(account, out code);

        public bool Allows(in NestedCall nestedCall, scoped in ABICallData calldata) => policy(nestedCall);
    }

    [TestMethod]
    public void IntegrationDocEip7702Snippet_ProcessesJudgesAndWalksACastBuiltTransaction()
    {
        // The statements of the docs/abi-integration.md EIP-7702 example, over the cast-built sponsored transaction of Eip7702Tests (the relayer sends,
        // authority A delegates to D in the same transaction and is called). Deny and Approve return the decision instead of a response.
        NestedCallWalker walker = Eip7702Tests.Walker();
        Address signerAddress = Eip7702Tests.Relayer;
        byte[] payload = Hex(Eip7702Tests.SponsoredTransaction);
        var rlpLimits = RLPDecodeLimits.Default;

        string Decide(Func<Address, Address, bool> TrustedDelegate)
        {
            var host = new Eip7702Tests.ChainState();
            string Deny(object reason, ABIRange range = default) => "deny " + reason;
            if (!RLPCalldataAdapter.TryRead(payload, EvmTransactionForm.SignedTransaction, in rlpLimits, out EvmTransactionView tx, out EvmTransactionStatus txStatus))
                return Deny(txStatus);

            var entries = new EvmAuthorization[tx.AuthorizationCount];  // or stackalloc for a short list
            if (!EvmAuthorizationProcessor.TryProcess(in tx, signerAddress, ref host, entries, out int written, out EvmAuthorizationStatus authStatus))
                return Deny(authStatus);                                  // RecoveryUnavailable, AccountStateUnavailable ...
            foreach (EvmAuthorization a in entries)
                if (a.IsRecovered && !TrustedDelegate(a.DelegateAddress, a.Authority))
                    return Deny("delegation");                            // judge every tuple that could apply, not only Applied ones

            if (!walker.TryWalk(in tx, signerAddress, entries, ref host, out NestedCallWalkResult result))
                return Deny(result.Status, result.FailedCalldataRange);
            Assert.AreEqual(written, entries.Length);
            return "approve " + result.CallCount;
        }

        Assert.AreEqual("approve 2", Decide((delegateAddress, _) => delegateAddress == Eip7702Tests.Delegate));
        Assert.AreEqual("deny delegation", Decide((delegateAddress, _) => delegateAddress == Eip7702Tests.Evil), "a policy that does not trust D refuses the delegation");
    }

    [TestMethod]
    public void IntegrationDocSnippet_WalksARealUniswapMulticall_AndDeniesWhatThePolicyRefuses()
    {
        // The statements of the docs/abi-integration.md example, over the Foundry `cast` fixture of multicall(bytes[]) with two exactInputSingle children.
        ABIFunctionSchema multicallSchema = Function("multicall(bytes[])");
        ABIFunctionSchema exactInputSingleSchema = Function("exactInputSingle((address,address,uint24,address,uint256,uint256,uint256,uint160))");
        ABIFunctionSchema multiSendSchema = Function("multiSend(bytes)");
        Address router = Address.FromEvmBytes(Hex("e592427a0aece92de3edee1f18e0157c05861564"));
        Address safe = Address.FromEvmBytes(Hex("5afe5afe5afe5afe5afe5afe5afe5afe5afe5afe"));
        var routerCodeIdentity = new Hash32(0x11, 0x22, 0x33, 0x44);
        var safeCodeIdentity = new Hash32(0x55, 0x66, 0x77, 0x88);
        const ulong servedChainId = EvmAdapterFixtures.ChainId;
        Address signerAddress = EvmAdapterFixtures.Sender;
        var stateSnapshot = new Dictionary<Address, EvmAccountCode> { [router] = EvmAccountCode.Contract(routerCodeIdentity), [safe] = EvmAccountCode.Contract(safeCodeIdentity) };

        Assert.IsTrue(ExecutionDescriptor.TryCreateNested(multicallSchema, [NestedCallRule.DelegateCallToSelf([0, NestedCallRule.EachElement])],
            out ExecutionDescriptor? multicall, out _, out _));
        ContractFunctionResolver registry = new ContractFunctionResolverBuilder()
            .Add(8453, router, routerCodeIdentity, multicall!)
            .Add(8453, router, routerCodeIdentity, ExecutionDescriptor.NoNestedCalls(exactInputSingleSchema))
            .Add(8453, safe, safeCodeIdentity, ExecutionDescriptor.Undescribed(multiSendSchema)) // known, but never walked
            .Build();
        var walker = new NestedCallWalker(registry, NestedCallLimits.Default, ABIDecodeLimits.Default);

        byte[] calldata = Hex(ABIFixtures.MulticallTwoExactInputSingle);
        byte[] payload = EvmAdapterFixtures.N1559(Hex("e592427a0aece92de3edee1f18e0157c05861564"), calldata).GetRLPEncodedRaw();
        var rlpLimits = RLPDecodeLimits.Default;
        Assert.IsTrue(RLPCalldataAdapter.TryRead(payload, EvmTransactionForm.SigningPayload, in rlpLimits, out EvmTransactionView tx, out EvmTransactionStatus txStatus), txStatus.ToString());
        Assert.AreEqual(servedChainId, tx.ChainId);
        var host = new MyHost(stateSnapshot, _ => true);
        Assert.IsTrue(walker.TryWalk(in tx, signerAddress, ref host, out NestedCallWalkResult result), result.Status.ToString());
        Assert.AreEqual(3, result.CallCount, "the multicall and its two swaps");

        // A policy that refuses swaps denies the whole transaction at the first child, with that child's range.
        var strict = new MyHost(stateSnapshot, call => !ReferenceEquals(call.Function, exactInputSingleSchema));
        Assert.IsFalse(walker.TryWalk(in tx, signerAddress, ref strict, out NestedCallWalkResult denied));
        Assert.AreEqual(NestedCallStatus.DeniedByHost, denied.Status);
        Assert.AreEqual(1, denied.CallCount);
        Assert.AreEqual(0x414bf389u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(calldata.AsSpan(denied.FailedCalldataRange.Offset)), "the range starts at the child's selector");

        // An upgraded router (another identity in the snapshot) is unknown code.
        var upgraded = new MyHost(new Dictionary<Address, EvmAccountCode> { [router] = EvmAccountCode.Contract(Hash32.Zero) }, _ => true);
        Assert.IsFalse(walker.TryWalk(in tx, signerAddress, ref upgraded, out NestedCallWalkResult stale));
        Assert.AreEqual(NestedCallStatus.UnknownCodeIdentity, stale.Status);
    }

    [TestMethod]
    public void ReadmeSnippet_DecodesAndEncodesTheTransfer_WithTheTypedUint256Api()
    {
        var compileLimits = ABICompileLimits.Default;
        Assert.IsTrue(ABISchemaCompiler.TryCompileFunctionSignature("transfer(address,uint256)", in compileLimits, out ABIFunctionSchema? transfer, out _));
        byte[] calldata = Calldata;

        var limits = ABIDecodeLimits.Default;
        bool decoded = false;
        if (ABICallData.TryDecode(calldata, transfer!, in limits, out ABICallData call, out ABIStatus status)
            && call.Arguments.TryGetAddress([0], out Address recipient, out status)
            && call.Arguments.TryGetUInt256([1], out uint256 amount, out status))
        {
            decoded = true;
            Assert.AreEqual(Address.FromEvmBytes(RecipientBytes20), recipient);
            Assert.AreEqual(new uint256(1_000_000_000_000_000_000UL), amount, "cast: 1 ETH in wei");
        }

        Assert.IsTrue(decoded, status.ToString());

        // The README encodes a uint256 amount: here 1e20 wei, which does NOT fit 64 bits (the amounts TryWriteUInt64 / TryGetUInt64 cannot carry).
        // Expected bytes: cast calldata 'transfer(address,uint256)' 0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045 100000000000000000000
        Address recipientAddress = Address.FromEvmBytes(RecipientBytes20);
        uint256 amountToSend = uint256.Parse("100000000000000000000");
        Span<byte> buffer = stackalloc byte[68];
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[2];
        var encodeLimits = ABIEncodeLimits.Default;
        int written = -1;
        if (ABICallEncoder.TryBegin(transfer!, buffer, frames, in encodeLimits, out ABICallEncoder encoder, out status)
            && encoder.TryWriteAddress(recipientAddress, out status) && encoder.TryWriteUInt256(amountToSend, out status)
            && encoder.TryFinish(out written, out status))
        {
            Assert.AreEqual(68, written);
        }

        Assert.AreEqual(68, written, status.ToString());
        CollectionAssert.AreEqual(Hex("a9059cbb000000000000000000000000d8da6bf26964af9d7eed9e03e53415d37aa960450000000000000000000000000000000000000000000000056bc75e2d63100000"), buffer[..written].ToArray());

        // The warning in the README, as a test: the 64-bit accessor refuses exactly the amounts a typed uint256 read carries.
        Assert.IsTrue(ABICallData.TryDecode(buffer[..written], transfer!, in limits, out ABICallData big, out status));
        Assert.IsFalse(big.Arguments.TryGetUInt64([1], out _, out status));
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
        Assert.IsTrue(big.Arguments.TryGetUInt256([1], out uint256 readBack, out status));
        Assert.AreEqual(amountToSend, readBack);
    }

    [TestMethod]
    public void ApiDocSnippet_ApproveMax_IsReadAndWrittenAsAUint256()
    {
        // docs/abi-api.md: "Read token amounts as uint256". approve(spender, type(uint256).max), bytes from cast (ABIFixtures.ApproveRouterMax).
        ABIFunctionSchema approve = ABIDecodeTestSupport.Function("approve(address,uint256)");
        byte[] calldata = Hex(ABIFixtures.ApproveRouterMax);
        var limits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABICallData.TryDecode(calldata, approve, in limits, out ABICallData call, out ABIStatus status), status.ToString());
        Assert.IsTrue(call.Arguments.TryGetAddress([0], out Address spender, out status));
        Assert.IsTrue(call.Arguments.TryGetUInt256([1], out uint256 amount, out status));
        Assert.AreEqual(Address.Parse("0x68b3465833fb72A70ecDF485E0e4C7bD8665Fc45"), spender);
        Assert.AreEqual(uint256.MaxValue, amount);
        Assert.IsFalse(call.Arguments.TryGetUInt64([1], out _, out status), "the 64-bit accessor refuses the standard unlimited approval");
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
        // The span form of the same read.
        Assert.IsTrue(call.Arguments.TryGetUInt256Word([1], out ReadOnlySpan<byte> amountWord, out status));
        CollectionAssert.AreEqual(Enumerable.Repeat((byte)0xff, 32).ToArray(), amountWord.ToArray());

        Span<byte> buffer = stackalloc byte[68];
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[2];
        var encodeLimits = ABIEncodeLimits.Default;
        Assert.IsTrue(ABICallEncoder.TryBegin(approve, buffer, frames, in encodeLimits, out ABICallEncoder encoder, out status));
        int written = -1;
        Assert.IsTrue(encoder.TryWriteAddress(spender, out status) && encoder.TryWriteUInt256(uint256.MaxValue, out status) && encoder.TryFinish(out written, out status), status.ToString());
        Assert.AreEqual(68, written);
        CollectionAssert.AreEqual(calldata, buffer.ToArray());
    }

    [TestMethod]
    public void ApiDocSnippets_SchemaLocateAndMeasureThenWrite()
    {
        var compileLimits = ABICompileLimits.Default;
        Assert.IsTrue(ABISchemaCompiler.TryCompileFunctionSignature("transfer(address,uint256)", in compileLimits, out ABIFunctionSchema? transfer, out ABISchemaError error), error.ToString());
        Assert.AreEqual(0xa9059cbbu, transfer.Selector);
        Assert.AreEqual("transfer(address,uint256)", transfer.CanonicalSignature);
        Assert.AreEqual("(address,uint256)", transfer.InputSchema.CanonicalType);

        var limits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABICallData.TryDecode(Calldata, transfer, in limits, out ABICallData call, out ABIStatus status), status.ToString());
        Assert.IsTrue(call.Arguments.TryLocate([1], out ABIFieldLocation amount, out status), status.ToString());
        Assert.AreEqual(new ABIRange(32, 32), amount.ValueRange, "argument coordinates: selector excluded");
        Assert.IsTrue(call.TryTranslateArgumentRange(amount.ValueRange, out ABIRange inCall));
        Assert.AreEqual(new ABIRange(36, 32), inCall, "call coordinates: four bytes later");
        Assert.IsTrue(call.Arguments.TryGetUInt256([1], out uint256 amount256, out status), status.ToString());
        Assert.AreEqual(new uint256(1_000_000_000_000_000_000UL), amount256);
        // TryGetUInt64 serves small fields only; this particular amount (1 ETH in wei) happens to fit, an approve(max) does not (see the next test).
        Assert.IsTrue(call.Arguments.TryGetUInt64([1], out ulong amount64, out status), status.ToString());
        Assert.AreEqual(1_000_000_000_000_000_000UL, amount64);

        // Measure, then write: the size is not known up front for a document with dynamic data.
        var arguments = CompileTupleForSample("(address,uint256,bytes)");
        byte[] payload = [1, 2, 3];
        Span<ABIMeasureFrame> measureFrames = stackalloc ABIMeasureFrame[2];
        var encodeLimits = ABIEncodeLimits.Default;
        Assert.IsTrue(ABIMeasurer.TryBegin(arguments, measureFrames, in encodeLimits, out ABIMeasurer measurer, out status), status.ToString());
        int size = -1;
        Assert.IsTrue(measurer.TryWriteAddress(RecipientBytes20, out status) && measurer.TryWriteUInt64(7, out status) && measurer.TryWriteBytes(payload, out status) && measurer.TryFinish(out size, out status), status.ToString());
        Assert.AreEqual(3 * 32 + 32 + 32, size, "three head words, then the length word and one padded data word");

        byte[] destination = new byte[size];
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[2];
        Assert.IsTrue(ABIWriter.TryBegin(arguments, destination, frames, in encodeLimits, out ABIWriter writer, out status), status.ToString());
        int written = -1;
        Assert.IsTrue(writer.TryWriteAddress(RecipientBytes20, out status) && writer.TryWriteUInt64(7, out status) && writer.TryWriteBytes(payload, out status) && writer.TryFinish(out written, out status), status.ToString());
        Assert.AreEqual(size, written);

        var decodeLimits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABIData.TryDecodeComplete(destination, arguments, in decodeLimits, out ABIData data, out status), status.ToString());
        Assert.IsTrue(data.TryGetBytePayload([2], out ReadOnlySpan<byte> read, out ABIRange payloadRange, out status), status.ToString());
        CollectionAssert.AreEqual(payload, read.ToArray());
        Assert.AreEqual(new ABIRange(3 * 32 + 32, 3), payloadRange, "the payload without its length word and padding");
    }

    private static ABISchema CompileTupleForSample(string tuple) => ABISchemaTestSupport.CompileTuple(tuple);
}
