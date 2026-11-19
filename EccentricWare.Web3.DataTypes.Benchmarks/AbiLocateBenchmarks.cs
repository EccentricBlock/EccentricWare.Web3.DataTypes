using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.ABI;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

// An ABIData is a ref struct and cannot live in a field, so rows that exercise the locator or an accessor decode once per invocation and
// repeat the lookup OperationsPerInvoke times; the single decode is amortised into the per-operation figure. The *_DecodeOnly reference row of
// each class is the decode those rows also pay once per Repeats lookups: subtract (decode / Repeats) when comparing a lookup with a decode.
// The same path is repeated, so these are steady-state, branch-predictable lookups, not a cold-cache measurement.

/// <summary>
/// Spec 14: "lookups are benchmarked at fixed path depth with growing sibling counts". <c>TryLocate</c> of the last child of
/// <c>multicall(bytes[])</c> with 2, 16, 128 and 1,024 children must not grow with the sibling count (next-sibling end bound, no scan).
/// </summary>
[MemoryDiagnoser]
public class AbiLocateSiblingBenchmarks
{
    // 16,384 repeats so that the single decode (about 15 us for 1,024 children) adds about 1 ns per lookup instead of about 15 ns.
    private const int Repeats = 16_384;
    private static readonly int[] FirstChildPath = [0, 0];

    private ABIDecodeLimits _limits;
    private AbiWorkload _workload = null!;
    private int[] _lastChildPath = null!;

    [Params(2, 16, 128, 1024)]
    public int Children { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // 1,024 children of 128 bytes each is about 128 KiB and 1,026 values: inside the default byte (262,144) and value (16,384) limits.
        _limits = AbiBenchmarkData.DecodeLimits;
        _workload = AbiBenchmarkData.Multicall(Children);
        _lastChildPath = [0, Children - 1];

        bool ok = ABIData.TryDecodeComplete(_workload.Arguments, _workload.Schema, in _limits, out ABIData data, out _);
        AbiBenchmarkData.RequireTrue(ok, "multicall decodes");
        AbiBenchmarkData.RequireTrue(data.TryLocate(_lastChildPath, out ABIFieldLocation last, out _) && last.ValueRange.Offset + last.ValueRange.Length == _workload.Arguments.Length,
            "the last child ends at the end of the document");
        AbiBenchmarkData.RequireTrue(data.TryGetBytePayload(_lastChildPath, out ReadOnlySpan<byte> payload, out _, out _) && payload.Length == 68, "child payload is the 68-byte call");
    }

    [Benchmark]
    public int DecodeOnly()
    {
        bool ok = ABIData.TryDecodeComplete(_workload.Arguments, _workload.Schema, in _limits, out ABIData data, out _);
        return ok ? data.EncodedBytes.Length : -1;
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int Locate_FirstChild() => Locate(FirstChildPath);

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int Locate_LastChild() => Locate(_lastChildPath);

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int GetBytePayload_LastChild()
    {
        if (!ABIData.TryDecodeComplete(_workload.Arguments, _workload.Schema, in _limits, out ABIData data, out _))
        {
            return -1;
        }

        int sum = 0;
        for (int i = 0; i < Repeats; i++)
        {
            data.TryGetBytePayload(_lastChildPath, out ReadOnlySpan<byte> payload, out _, out _);
            sum += payload.Length;
        }

        return sum;
    }

    private int Locate(int[] path)
    {
        if (!ABIData.TryDecodeComplete(_workload.Arguments, _workload.Schema, in _limits, out ABIData data, out _))
        {
            return -1;
        }

        int sum = 0;
        for (int i = 0; i < Repeats; i++)
        {
            data.TryLocate(path, out ABIFieldLocation field, out _);
            sum += field.ValueRange.Length;
        }

        return sum;
    }
}

/// <summary>
/// <c>(bytes, uint256 x K, bytes)</c>: locating the FIRST dynamic field needs the start of the next dynamic sibling, which sits K static members
/// further on. With the precomputed next-dynamic-sibling index the cost must not grow with K (1, 16, 256).
/// </summary>
[MemoryDiagnoser]
public class AbiLocateStaticSiblingBenchmarks
{
    private const int Repeats = 16_384;
    private static readonly int[] FirstPath = [0];

    private ABIDecodeLimits _limits;
    private AbiWorkload _workload = null!;

    [Params(1, 16, 256)]
    public int StaticMembers { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _limits = AbiBenchmarkData.DecodeLimits;
        _workload = AbiBenchmarkData.StaticMembersBetween(StaticMembers);
    }

    [Benchmark]
    public int DecodeOnly()
    {
        bool ok = ABIData.TryDecodeComplete(_workload.Arguments, _workload.Schema, in _limits, out ABIData data, out _);
        return ok ? data.EncodedBytes.Length : -1;
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int Locate_FirstDynamicField()
    {
        if (!ABIData.TryDecodeComplete(_workload.Arguments, _workload.Schema, in _limits, out ABIData data, out _))
        {
            return -1;
        }

        int sum = 0;
        for (int i = 0; i < Repeats; i++)
        {
            data.TryLocate(FirstPath, out ABIFieldLocation field, out _);
            sum += field.ValueRange.Length;
        }

        return sum;
    }
}

/// <summary>Typed accessors on the ERC-20 <c>transfer</c> and Uniswap V3 <c>exactInputSingle</c> arguments (static fields: a word read plus the canonical-value check).</summary>
[MemoryDiagnoser]
public class AbiAccessorBenchmarks
{
    private const int Repeats = 1024;
    private static readonly int[] TransferTo = [0];
    private static readonly int[] TransferAmount = [1];
    private static readonly int[] ExactTokenIn = [0, 0];
    private static readonly int[] ExactFee = [0, 2];
    private static readonly int[] ExactAmountIn = [0, 5];

    private ABIDecodeLimits _limits;
    private AbiWorkload _transfer = null!;
    private AbiWorkload _exact = null!;

    [GlobalSetup]
    public void Setup()
    {
        _limits = AbiBenchmarkData.DecodeLimits;
        _transfer = AbiBenchmarkData.Transfer;
        _exact = AbiBenchmarkData.ExactInputSingle;
        ABIData.TryDecodeComplete(_exact.Arguments, _exact.Schema, in _limits, out ABIData data, out _);
        AbiBenchmarkData.RequireTrue(data.TryGetUInt64(ExactFee, out ulong fee, out _) && fee == 500, "fee reads 500");
        AbiBenchmarkData.RequireTrue(data.TryGetAddress(ExactTokenIn, out ReadOnlySpan<byte> tokenIn, out _) && tokenIn.SequenceEqual(AbiBenchmarkData.WethAddress), "tokenIn is WETH");
        ABIData.TryDecodeComplete(_transfer.Arguments, _transfer.Schema, in _limits, out ABIData transfer, out _);
        AbiBenchmarkData.RequireTrue(transfer.TryGetUInt64(TransferAmount, out ulong amount, out _) && amount == 1_000_000_000_000_000_000UL, "amount reads 1e18");
    }

    [Benchmark]
    public int DecodeOnly_Transfer()
    {
        bool ok = ABIData.TryDecodeComplete(_transfer.Arguments, _transfer.Schema, in _limits, out ABIData data, out _);
        return ok ? data.EncodedBytes.Length : -1;
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryGetAddress_TransferRecipient()
    {
        if (!ABIData.TryDecodeComplete(_transfer.Arguments, _transfer.Schema, in _limits, out ABIData data, out _))
        {
            return -1;
        }

        int sum = 0;
        for (int i = 0; i < Repeats; i++)
        {
            data.TryGetAddress(TransferTo, out ReadOnlySpan<byte> address, out _);
            sum += address.Length;
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public ulong TryGetUInt64_TransferAmount()
    {
        if (!ABIData.TryDecodeComplete(_transfer.Arguments, _transfer.Schema, in _limits, out ABIData data, out _))
        {
            return 0;
        }

        ulong sum = 0;
        for (int i = 0; i < Repeats; i++)
        {
            data.TryGetUInt64(TransferAmount, out ulong value, out _);
            sum += value;
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryGetUInt256Word_TransferAmount()
    {
        if (!ABIData.TryDecodeComplete(_transfer.Arguments, _transfer.Schema, in _limits, out ABIData data, out _))
        {
            return -1;
        }

        int sum = 0;
        for (int i = 0; i < Repeats; i++)
        {
            data.TryGetUInt256Word(TransferAmount, out ReadOnlySpan<byte> word, out _);
            sum += word[31];
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryLocate_ExactInputSingleAmountIn()
    {
        if (!ABIData.TryDecodeComplete(_exact.Arguments, _exact.Schema, in _limits, out ABIData data, out _))
        {
            return -1;
        }

        int sum = 0;
        for (int i = 0; i < Repeats; i++)
        {
            data.TryLocate(ExactAmountIn, out ABIFieldLocation field, out _);
            sum += field.ValueRange.Offset;
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public ulong TryGetUInt64_ExactInputSingleFee()
    {
        if (!ABIData.TryDecodeComplete(_exact.Arguments, _exact.Schema, in _limits, out ABIData data, out _))
        {
            return 0;
        }

        ulong sum = 0;
        for (int i = 0; i < Repeats; i++)
        {
            data.TryGetUInt64(ExactFee, out ulong value, out _);
            sum += value;
        }

        return sum;
    }
}
