using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

// All classes in this file use RLPDecodeLimits.Default (1 MiB, depth 32, 8,192 items). A RLPData is a ref struct and
// cannot live in a field, so rows that exercise an accessor decode once per invocation and repeat the accessor
// OperationsPerInvoke times; the single decode is amortised into the per-operation figure. Each [Params] aspect has its
// own class so the parameter sets are not multiplied into a cartesian product.

/// <summary>Strict decode of the signed-style legacy transaction (9 fields) and an EIP-1559 payload with an access list, plus <c>TryGetUInt64</c>.</summary>
[MemoryDiagnoser]
public class RlpDecodeBenchmarks
{
    private const int Repeats = 64;

    private RLPDecodeLimits _limits;
    private byte[] _legacy = null!;
    private byte[] _eip1559 = null!;
    private byte[] _uint64Max = null!;
    private byte[] _uint64Small = null!;

    [GlobalSetup]
    public void Setup()
    {
        _limits = RLPDecodeLimits.Default;
        RlpTransactionFixture tx = RlpBenchmarkData.Transactions;
        _legacy = tx.LegacyEncoded;
        _eip1559 = tx.Eip1559Encoded;
        RlpBenchmarkData.RequireDecodes(_legacy, RLPKind.List, "legacy transaction");
        RlpBenchmarkData.RequireDecodes(_eip1559, RLPKind.List, "EIP-1559 payload");

        _uint64Max = [0x88, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff];
        _uint64Small = [0x82, 0x52, 0x08]; // 21000
        VerifyUInt64(_uint64Max, ulong.MaxValue);
        VerifyUInt64(_uint64Small, 21000);
    }

    private void VerifyUInt64(byte[] input, ulong expected)
    {
        RlpBenchmarkData.RequireDecodes(input, RLPKind.ByteString, "uint64 input");
        RLPData.TryDecodeComplete(input, in _limits, out RLPData root, out _);
        RlpBenchmarkData.RequireSuccess(root.TryGetUInt64(out ulong value), "uint64 read");
        if (value != expected)
        {
            throw new InvalidOperationException($"TryGetUInt64 read {value}, expected {expected}.");
        }
    }

    [Benchmark]
    public int Decode_LegacyTransaction()
    {
        RLPStatus status = RLPData.TryDecodeComplete(_legacy, in _limits, out RLPData root, out _);
        return status == RLPStatus.Success ? root.PayloadLength : -1;
    }

    [Benchmark]
    public int Decode_Eip1559WithAccessList()
    {
        RLPStatus status = RLPData.TryDecodeComplete(_eip1559, in _limits, out RLPData root, out _);
        return status == RLPStatus.Success ? root.PayloadLength : -1;
    }

    /// <summary>Reference row: the decode that the two <c>TryGetUInt64</c> rows below also pay once per 64 reads.</summary>
    [Benchmark]
    public int Decode_UInt64Max()
    {
        RLPStatus status = RLPData.TryDecodeComplete(_uint64Max, in _limits, out RLPData root, out _);
        return status == RLPStatus.Success ? root.PayloadLength : -1;
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public ulong TryGetUInt64_EightBytes() => ReadRepeated(_uint64Max);

    [Benchmark(OperationsPerInvoke = Repeats)]
    public ulong TryGetUInt64_TwoBytes() => ReadRepeated(_uint64Small);

    private ulong ReadRepeated(byte[] input)
    {
        if (RLPData.TryDecodeComplete(input, in _limits, out RLPData root, out _) != RLPStatus.Success)
        {
            return 0;
        }

        ulong sum = 0;
        for (int i = 0; i < Repeats; i++)
        {
            root.TryGetUInt64(out ulong value);
            sum += value;
        }

        return sum;
    }
}

/// <summary>Strict decode of one byte string: empty <c>80</c>, a literal byte, the 55/56-byte boundary of the short and long string forms, and 2- and 3-byte length fields (300 and 70,000 bytes).</summary>
[MemoryDiagnoser]
public class RlpDecodeStringBenchmarks
{
    private RLPDecodeLimits _limits;
    private byte[] _input = null!;

    [Params("empty", "literal", "len55", "len56", "len300", "len70000")]
    public string Case { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _limits = RLPDecodeLimits.Default;
        _input = Case switch
        {
            "empty" => [0x80],
            "literal" => [0x42],
            "len55" => [(byte)(0x80 + 55), .. BenchmarkData.RandomBytes(55)],
            "len56" => [0xb8, 56, .. BenchmarkData.RandomBytes(56)],
            "len300" => [0xb9, 0x01, 0x2c, .. BenchmarkData.RandomBytes(300)], // two length bytes
            "len70000" => [0xba, 0x01, 0x11, 0x70, .. BenchmarkData.RandomBytes(70_000)], // three length bytes
            _ => throw new InvalidOperationException($"Unknown Case {Case}."),
        };
        RlpBenchmarkData.RequireDecodes(_input, RLPKind.ByteString, $"string {Case}");
    }

    [Benchmark]
    public int Decode_String()
    {
        RLPStatus status = RLPData.TryDecodeComplete(_input, in _limits, out RLPData root, out _);
        return status == RLPStatus.Success ? root.PayloadLength : -1;
    }
}

/// <summary>Strict decode of nested single-child lists at depths 1, 4, 16 and 32 (32 is the default depth limit).</summary>
[MemoryDiagnoser]
public class RlpDecodeNestedBenchmarks
{
    private RLPDecodeLimits _limits;
    private byte[] _input = null!;

    [Params(1, 4, 16, 32)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _limits = RLPDecodeLimits.Default;
        _input = RlpBenchmarkData.NestedLists(Depth);
        RlpBenchmarkData.RequireDecodes(_input, RLPKind.List, $"nested depth {Depth}");
    }

    [Benchmark]
    public int Decode_NestedLists()
    {
        RLPStatus status = RLPData.TryDecodeComplete(_input, in _limits, out RLPData root, out _);
        return status == RLPStatus.Success ? root.PayloadLength : -1;
    }
}

/// <summary>Strict decode of a flat list of 4-byte strings (5 encoded bytes each); the per-item cost is the mean divided by <see cref="Count"/>.</summary>
[MemoryDiagnoser]
public class RlpDecodeWideBenchmarks
{
    private RLPDecodeLimits _limits;
    private byte[] _input = null!;

    [Params(1, 9, 64, 1024)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _limits = RLPDecodeLimits.Default;
        _input = RlpBenchmarkData.FlatList(Count, 4);
        RlpBenchmarkData.RequireDecodes(_input, RLPKind.List, $"wide list {Count}");
    }

    [Benchmark]
    public int Decode_WideFlatList()
    {
        RLPStatus status = RLPData.TryDecodeComplete(_input, in _limits, out RLPData root, out _);
        return status == RLPStatus.Success ? root.PayloadLength : -1;
    }
}

/// <summary><c>TryCopyEncodedTo</c> of an already decoded item (9-byte integer, the 180-byte legacy transaction, a 1,027-byte string), repeated 64 times per decode.</summary>
[MemoryDiagnoser]
public class RlpDecodeCopyBenchmarks
{
    private const int Repeats = 64;

    private RLPDecodeLimits _limits;
    private byte[] _source = null!;
    private byte[] _destination = null!;

    [Params(9, 180, 1027)]
    public int EncodedSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _limits = RLPDecodeLimits.Default;
        _source = EncodedSize switch
        {
            9 => [0x88, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff],
            180 => RlpBenchmarkData.Transactions.LegacyEncoded,
            _ => [0xb9, 0x04, 0x00, .. BenchmarkData.RandomBytes(1024)],
        };
        if (_source.Length != EncodedSize)
        {
            throw new InvalidOperationException($"Copy source is {_source.Length} bytes, expected {EncodedSize}.");
        }

        _destination = new byte[EncodedSize + 8];
        RlpBenchmarkData.RequireDecodes(_source, _source[0] >= 0xc0 ? RLPKind.List : RLPKind.ByteString, $"copy source {EncodedSize}");
        RLPData.TryDecodeComplete(_source, in _limits, out RLPData root, out _);
        RlpBenchmarkData.RequireSuccess(root.TryCopyEncodedTo(_destination, out int written), "copy");
        RlpBenchmarkData.RequireEqual(_source, _destination.AsSpan(0, written), "copied bytes");
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryCopyEncodedTo_Repeated()
    {
        if (RLPData.TryDecodeComplete(_source, in _limits, out RLPData root, out _) != RLPStatus.Success)
        {
            return -1;
        }

        int total = 0;
        for (int i = 0; i < Repeats; i++)
        {
            root.TryCopyEncodedTo(_destination, out int written);
            total += written;
        }

        return total;
    }
}
