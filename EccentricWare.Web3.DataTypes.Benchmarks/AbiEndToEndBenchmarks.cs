using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.ABI;
using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// Spec 11.2 "RLP extraction + ABI decoding": a signed legacy transaction whose <c>data</c> is <c>approve(spender, 2^256-1)</c>. The rows decode the
/// RLP document, take the <c>data</c> payload span without a copy (the RLP layer keeps its own coordinates and validation), validate it once as an
/// <see cref="ABICallData"/> and read the spender and amount. <c>RlpDecode_Only</c> and <c>AbiCallDecode_Only</c> are the two halves measured separately so the
/// integration overhead (the glue between them) is visible; it is the difference, not a separate cost. The ABI side runs after the caller has
/// established which function schema applies (spec 3.1); this benchmark does not model that identity resolution.
/// </summary>
[MemoryDiagnoser]
public class AbiEndToEndBenchmarks
{
    private const int DataFieldIndex = 5;
    private static readonly int[] SpenderPath = [0];
    private static readonly int[] AmountPath = [1];

    private RLPDecodeLimits _rlpLimits;
    private ABIDecodeLimits _abiLimits;
    private ABIFunctionSchema _approve = null!;
    private byte[] _transaction = null!;
    private byte[] _calldata = null!;

    [GlobalSetup]
    public void Setup()
    {
        _rlpLimits = RLPDecodeLimits.Default;
        _abiLimits = AbiBenchmarkData.DecodeLimits;
        RlpTransactionFixture tx = RlpBenchmarkData.Transactions;
        _transaction = tx.LegacyEncoded;
        _calldata = tx.Data;
        var limits = ABICompileLimits.Default;
        if (!ABISchemaCompiler.TryCompileFunctionSignature("approve(address,uint256)", in limits, out ABIFunctionSchema? approve, out ABISchemaError error))
        {
            throw new InvalidOperationException(error.ToString());
        }

        _approve = approve;
        AbiBenchmarkData.RequireTrue(_approve.Selector == 0x095ea7b3, "approve selector");
        AbiBenchmarkData.RequireTrue(ExtractAndDecode() == 32, "the amount word is read (2^256-1 is not a uint64: expected 32 word bytes)");
    }

    [Benchmark]
    public int RlpDecode_Only()
    {
        RLPStatus status = RLPData.TryDecodeComplete(_transaction, in _rlpLimits, out RLPData root, out _);
        return status == RLPStatus.Success ? root.PayloadLength : -1;
    }

    [Benchmark]
    public int AbiCallDecode_Only()
    {
        bool ok = ABICallData.TryDecode(_calldata, _approve, in _abiLimits, out ABICallData call, out _);
        return ok ? call.Arguments.EncodedBytes.Length : -1;
    }

    /// <summary>RLP decode, enumerate to the <c>data</c> field, ABI call decode of that exact span, read the spender address and the amount word.</summary>
    [Benchmark]
    public int RlpExtract_AbiDecode_ReadSpenderAndAmount() => ExtractAndDecode();

    private int ExtractAndDecode()
    {
        if (RLPData.TryDecodeComplete(_transaction, in _rlpLimits, out RLPData root, out _) != RLPStatus.Success
            || root.TryGetChildren(out RLPChildEnumerator fields) != RLPStatus.Success)
        {
            return -1;
        }

        for (int i = 0; i <= DataFieldIndex; i++)
        {
            if (!fields.MoveNext())
            {
                return -1;
            }
        }

        ReadOnlySpan<byte> data = fields.Current.Payload;
        if (!ABICallData.TryDecode(data, _approve, in _abiLimits, out ABICallData call, out _)
            || !call.Arguments.TryGetAddress(SpenderPath, out ReadOnlySpan<byte> spender, out _)
            || !call.Arguments.TryGetUInt256Word(AmountPath, out ReadOnlySpan<byte> amount, out _))
        {
            return -1;
        }

        return spender.Length == 20 ? amount.Length : -1;
    }
}
