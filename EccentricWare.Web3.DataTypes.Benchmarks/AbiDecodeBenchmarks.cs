using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.ABI;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

// All classes in this file use ABIDecodeLimits.Default (262,144 bytes, depth 16, 4,096 array elements, 16,384 values, 100,000 work units)
// unless noted. An ABIData is a ref struct and cannot live in a field, so every row decodes inside the benchmark method. Each [Params]
// aspect has its own class so the parameter sets are not multiplied into a cartesian product. Inputs come from the independent
// reference encoders (AbiBenchmarkData); [GlobalSetup] of every class checks they decode, so a broken input fails loudly.

/// <summary>Strict decode of the fixed workloads of spec 11.2: a two-field static <c>transfer</c>, Uniswap V3 <c>exactInputSingle</c> (a static 8-member tuple), a mixed <c>(address,uint256,bytes)</c> and a 256-KiB payload.</summary>
[MemoryDiagnoser]
public class AbiDecodeBenchmarks
{
    private ABIDecodeLimits _limits;
    private AbiWorkload _transfer = null!;
    private AbiWorkload _exact = null!;
    private AbiWorkload _mixed = null!;
    private AbiWorkload _large = null!;

    [GlobalSetup]
    public void Setup()
    {
        _limits = AbiBenchmarkData.DecodeLimits;
        _transfer = AbiBenchmarkData.Transfer;
        _exact = AbiBenchmarkData.ExactInputSingle;
        _mixed = AbiBenchmarkData.Mixed(65);
        _large = AbiBenchmarkData.Bytes(262_048); // the largest whole-word payload whose CALL (4 + 32 + 32 + data) still fits the 262,144-byte default
    }

    [Benchmark]
    public int Decode_Erc20TransferArguments() => Decode(_transfer);

    [Benchmark]
    public int Decode_UniswapV3ExactInputSingleArguments() => Decode(_exact);

    [Benchmark]
    public int Decode_MixedAddressUintBytes65() => Decode(_mixed);

    /// <summary>Memory-bandwidth workload: the decoder validates the length word and the padding only and never copies, so this must not scale with the payload.</summary>
    [Benchmark]
    public int Decode_Bytes256KiB() => Decode(_large);

    /// <summary>The call form: selector check plus the same validator over the arguments.</summary>
    [Benchmark]
    public int DecodeCall_Erc20Transfer()
    {
        bool ok = ABICallData.TryDecode(_transfer.Call, _transfer.Function, in _limits, out ABICallData call, out _);
        return ok ? call.Arguments.EncodedBytes.Length : -1;
    }

    [Benchmark]
    public int DecodeCall_UniswapV3ExactInputSingle()
    {
        bool ok = ABICallData.TryDecode(_exact.Call, _exact.Function, in _limits, out ABICallData call, out _);
        return ok ? call.Arguments.EncodedBytes.Length : -1;
    }

    private int Decode(AbiWorkload workload)
    {
        bool ok = ABIData.TryDecodeComplete(workload.Arguments, workload.Schema, in _limits, out ABIData data, out _);
        return ok ? data.EncodedBytes.Length : -1;
    }
}

/// <summary>Dynamic <c>bytes</c> at 0, 31, 32, 33 and 65 bytes: tail validation and padding overhead.</summary>
[MemoryDiagnoser]
public class AbiDecodeBytesBenchmarks
{
    private ABIDecodeLimits _limits;
    private AbiWorkload _workload = null!;

    [Params(0, 31, 32, 33, 65)]
    public int Length { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _limits = AbiBenchmarkData.DecodeLimits;
        _workload = AbiBenchmarkData.Bytes(Length);
    }

    [Benchmark]
    public int Decode()
    {
        bool ok = ABIData.TryDecodeComplete(_workload.Arguments, _workload.Schema, in _limits, out ABIData data, out _);
        return ok ? data.EncodedBytes.Length : -1;
    }
}

/// <summary><c>multicall(bytes[])</c> with 2 and 128 children (68 bytes each): array-offset validation, one pointer and one padded tail per child.</summary>
[MemoryDiagnoser]
public class AbiDecodeMulticallBenchmarks
{
    private ABIDecodeLimits _limits;
    private AbiWorkload _workload = null!;

    [Params(2, 128)]
    public int Children { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _limits = AbiBenchmarkData.DecodeLimits;
        _workload = AbiBenchmarkData.Multicall(Children);
    }

    [Benchmark]
    public int DecodeCall()
    {
        bool ok = ABICallData.TryDecode(_workload.Call, _workload.Function, in _limits, out ABICallData call, out _);
        return ok ? call.Arguments.EncodedBytes.Length : -1;
    }
}

/// <summary>
/// <c>uint256[]...[]</c> nested 1, 8 and 14 levels with one element per level. Value depth is levels + 2 (root tuple, the arrays, the leaf), so 14
/// levels is exactly the default <c>MaxValueDepth</c> of 16 and 8 levels (depth 10) takes the deep frame tier (the 8-frame tier covers schemas up to depth 8).
/// </summary>
[MemoryDiagnoser]
public class AbiDecodeNestedBenchmarks
{
    private ABIDecodeLimits _limits;
    private AbiWorkload _workload = null!;

    [Params(1, 8, 14)]
    public int Levels { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _limits = AbiBenchmarkData.DecodeLimits;
        _workload = AbiBenchmarkData.NestedArrays(Levels);
    }

    [Benchmark]
    public int Decode()
    {
        bool ok = ABIData.TryDecodeComplete(_workload.Arguments, _workload.Schema, in _limits, out ABIData data, out _);
        return ok ? data.EncodedBytes.Length : -1;
    }
}
