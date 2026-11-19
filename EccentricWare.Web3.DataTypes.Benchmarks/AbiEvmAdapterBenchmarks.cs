using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.ABI;
using EccentricWare.Web3.DataTypes.ABI.EvmAdapters;
using EccentricWare.Web3.DataTypes.Rlp;
using EccentricWare.Web3.DataTypes.Tests.ABI;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// The EVM adapters (spec 11.2, "multicall(bytes[]) 2 and 128 children: array-offset validation and nested inspection overhead"): reading a signed legacy
/// envelope, then walking a <c>multicall(bytes[])</c> whose children are delegatecalls to the router, each resolved and validated. <c>DecodeOnly</c> is
/// the plain strict decode of the same outer call, so the walker's extra cost (one resolution and one strict decode per child, plus the host calls) is the
/// difference. The host is a struct with a fixed identity, so the walk allocates nothing.
/// </summary>
[MemoryDiagnoser]
public class AbiEvmAdapterBenchmarks
{
    private static readonly Address Router = Address.FromEvmBytes(Convert.FromHexString("68b3465833fb72a70ecdf485e0e4c7bd8665fc45"));
    private static readonly Address Sender = Address.FromEvmBytes(Convert.FromHexString("1111111111111111111111111111111111111111"));
    private static readonly Hash32 RouterCode = new(0, 0, 0, 0x10);

    private struct FixedHost : INestedCallHost
    {
        public int Calls;

        public readonly bool TryGetCode(ulong chainId, in Address account, out EvmAccountCode code)
        {
            code = account == Router ? EvmAccountCode.Contract(RouterCode) : default;
            return account == Router;
        }

        public bool Allows(in NestedCall nestedCall, scoped in ABICallData calldata)
        {
            Calls++;
            return true;
        }
    }

    [Params(2, 128)]
    public int Children { get; set; }

    private RLPDecodeLimits _rlpLimits;
    private ABIDecodeLimits _abiLimits;
    private byte[] _legacyTransaction = null!;
    private byte[] _multicall = null!;
    private ABIFunctionSchema _multicallSchema = null!;
    private NestedCallWalker _walker = null!;

    [GlobalSetup]
    public void Setup()
    {
        _rlpLimits = RLPDecodeLimits.Default;
        _abiLimits = AbiBenchmarkData.DecodeLimits;
        _legacyTransaction = RlpBenchmarkData.Transactions.LegacyEncoded;

        var compileLimits = ABICompileLimits.Default;
        ABIFunctionSchema Compile(string signature) =>
            ABISchemaCompiler.TryCompileFunctionSignature(signature, in compileLimits, out ABIFunctionSchema? function, out ABISchemaError error)
                ? function
                : throw new InvalidOperationException(error.ToString());
        _multicallSchema = Compile("multicall(bytes[])");
        ABIFunctionSchema transfer = Compile("transfer(address,uint256)");
        AbiBenchmarkData.RequireTrue(
            ExecutionDescriptor.TryCreateNested(_multicallSchema, [NestedCallRule.DelegateCallToSelf([0, NestedCallRule.EachElement])], out ExecutionDescriptor? multicall, out _, out _),
            "multicall descriptor");
        ContractFunctionResolver registry = new ContractFunctionResolverBuilder()
            .Add(1, Router, RouterCode, multicall!)
            .Add(1, Router, RouterCode, ExecutionDescriptor.NoNestedCalls(transfer))
            .Build();
        _walker = new NestedCallWalker(registry, NestedCallLimits.Default, _abiLimits);

        // multicall(bytes[]) of Children transfer calls, encoded by the test-only independent encoder.
        byte[] child = [0xa9, 0x05, 0x9c, 0xbb, .. new byte[12], .. Convert.FromHexString("d8da6bf26964af9d7eed9e03e53415d37aa96045"), .. new byte[31], 0x05];
        var children = new RValue[Children];
        for (int i = 0; i < Children; i++)
        {
            children[i] = new RBlob(child);
        }

        _multicall = [0xac, 0x96, 0x50, 0xd8, .. ABIIndependentEncoder.EncodeArguments(ABIReference.ParseTuple("(bytes[])"), new RSeq([new RSeq(children)]))];
        AbiBenchmarkData.RequireTrue(DecodeOnly() == _multicall.Length, "the multicall decodes");
        AbiBenchmarkData.RequireTrue(Walk() == Children + 1, "the walk visits the multicall and every child");
        AbiBenchmarkData.RequireTrue(AdapterRead() > 0, "the legacy envelope reads as a function call");
    }

    /// <summary>Reads the signed legacy <c>approve</c> transaction and takes its calldata (independent of <see cref="Children"/>).</summary>
    [Benchmark]
    public int AdapterRead()
    {
        return RLPCalldataAdapter.TryRead(_legacyTransaction, EvmTransactionForm.SignedTransaction, in _rlpLimits, out EvmTransactionView tx, out _)
            && tx.TryGetFunctionCalldata(out ReadOnlySpan<byte> calldata)
            ? calldata.Length
            : -1;
    }

    [Benchmark(Baseline = true)]
    public int DecodeOnly()
    {
        return ABICallData.TryDecode(_multicall, _multicallSchema, in _abiLimits, out ABICallData call, out _) ? call.EncodedCall.Length : -1;
    }

    [Benchmark]
    public int Walk()
    {
        var host = new FixedHost();
        return _walker.TryWalk(1, Sender, Router, uint256.Zero, _multicall, ref host, out NestedCallWalkResult result) ? result.CallCount : -1;
    }
}
