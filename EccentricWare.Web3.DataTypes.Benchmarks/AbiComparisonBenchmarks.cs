using System.Numerics;
using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.ABI;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.ABI.Model;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// The same workloads against <c>Nethereum.ABI</c> 7.1.0 (a <c>PackageReference</c> of the benchmark and test projects, never of the library).
/// NOT a like-for-like comparison: Nethereum decodes permissively (trailing bytes, <c>bool</c> = 2 and <c>uint8</c> = 256 are accepted, see
/// <c>ABINethereumDifferentialTests</c>) into freshly allocated objects (<c>BigInteger</c>, strings, lists), the library validates strictly and reads values in place
/// as borrowed spans; Nethereum's encoder allocates its result, the library writes into a caller buffer. The rows show what each approach costs for the same
/// bytes, not that either does the same work. <c>[GlobalSetup]</c> checks that both sides produce the same bytes and read the same values.
/// </summary>
[MemoryDiagnoser]
public class AbiComparisonBenchmarks
{
    private static readonly ParametersEncoder NethereumEncoder = new();
    private static readonly ParameterDecoder NethereumDecoder = new();

    private ABIDecodeLimits _limits;
    private AbiWorkload _transfer = null!;
    private AbiWorkload _multicall = null!;
    private byte[] _destination = null!;
    private Parameter[] _transferParameters = null!;
    private object[] _transferValues = null!;
    private Parameter[] _multicallParameters = null!;
    private object[] _multicallValues = null!;
    private byte[][] _children = null!;

    [GlobalSetup]
    public void Setup()
    {
        _limits = AbiBenchmarkData.DecodeLimits;
        _transfer = AbiBenchmarkData.Transfer;
        _multicall = AbiBenchmarkData.Multicall(128);
        _destination = new byte[_multicall.Call.Length + 64];

        _transferParameters = [new Parameter("address", "to"), new Parameter("uint256", "amount")];
        _transferValues = ["0x" + Convert.ToHexStringLower(AbiBenchmarkData.VitalikAddress), new BigInteger(1_000_000_000_000_000_000UL)];
        _multicallParameters = [new Parameter("bytes[]", "data")];
        _children = ((EccentricWare.Web3.DataTypes.Tests.ABI.RSeq)_multicall.Value.Items[0]).Items.Select(i => ((EccentricWare.Web3.DataTypes.Tests.ABI.RBlob)i).Data).ToArray();
        _multicallValues = [_children.ToList()];

        AbiBenchmarkData.RequireEqual(_transfer.Arguments, Nethereum_Encode_Transfer(), "Nethereum transfer encoding");
        AbiBenchmarkData.RequireEqual(_multicall.Arguments, Nethereum_Encode_Multicall128(), "Nethereum multicall encoding");
        AbiBenchmarkData.RequireTrue(Library_Decode_Transfer() == 64 && Nethereum_Decode_Transfer() == 2, "transfer decodes on both sides");
        AbiBenchmarkData.RequireTrue(Library_Decode_Multicall128() == _multicall.Arguments.Length && Nethereum_Decode_Multicall128() == 128, "multicall decodes on both sides");
    }

    // ------------------------------------------------------------------ decode

    [Benchmark]
    public int Library_Decode_Transfer()
    {
        bool ok = ABIData.TryDecodeComplete(_transfer.Arguments, _transfer.Schema, in _limits, out ABIData data, out _);
        return ok ? data.EncodedBytes.Length : -1;
    }

    [Benchmark]
    public int Nethereum_Decode_Transfer() => NethereumDecoder.DecodeDefaultData(_transfer.Arguments, _transferParameters).Count;

    [Benchmark]
    public int Library_Decode_Multicall128()
    {
        bool ok = ABIData.TryDecodeComplete(_multicall.Arguments, _multicall.Schema, in _limits, out ABIData data, out _);
        return ok ? data.EncodedBytes.Length : -1;
    }

    [Benchmark]
    public int Nethereum_Decode_Multicall128() =>
        ((System.Collections.IList)NethereumDecoder.DecodeDefaultData(_multicall.Arguments, _multicallParameters)[0].Result).Count;

    // ------------------------------------------------------------------ encode

    [Benchmark]
    public int Library_Encode_Transfer() => AbiWriteRoutines.Transfer(_destination, AbiBenchmarkData.VitalikAddress, AbiBenchmarkData.OneEthWord);

    [Benchmark]
    public byte[] Nethereum_Encode_Transfer() => NethereumEncoder.EncodeParameters(_transferParameters, _transferValues);

    [Benchmark]
    public int Library_Encode_Multicall128() => AbiWriteRoutines.MulticallCall(_destination, _multicall, _children) - 4;

    [Benchmark]
    public byte[] Nethereum_Encode_Multicall128() => NethereumEncoder.EncodeParameters(_multicallParameters, _multicallValues);
}
