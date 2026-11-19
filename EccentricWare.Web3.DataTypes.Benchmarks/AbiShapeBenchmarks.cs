using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.ABI;
using EccentricWare.Web3.DataTypes.Tests.ABI;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

// SIMD/CPU evaluation (docs/specs/EVM_Codecs_SIMD_Optimisation_Guidance_v1.0.md, section 10.2): decode and write shapes that the
// existing Abi* suites do not cover (static arrays of every narrow element type, dynamic and fixed, arrays of static tuples, string-heavy
// structs). Inputs come from the independent reference encoders, like every other Abi* class.

internal static class AbiShapeData
{
    private static readonly Random Rng = new(20260518);

    private static byte[] RandomAddress()
    {
        var a = new byte[20];
        Rng.NextBytes(a);
        return a;
    }

    /// <summary>A canonical word for an element type, with realistic content (random address, small amount, hash-like bytes32).</summary>
    public static RWord Element(string type, int index)
    {
        switch (type)
        {
            case "uint256":
                return new RWord(ABIReference.ToWord((ulong)(index + 1) * 1_000_003UL));
            case "uint8":
                return new RWord(ABIReference.ToWord((ulong)(index & 0xff)));
            case "bool":
                return new RWord(ABIReference.ToWord((ulong)(index & 1)));
            case "address":
                return new RWord(ABIReference.ToWord(ABIReference.Unsigned(RandomAddress())));
            case "int64":
                return new RWord(ABIReference.ToWord(-(System.Numerics.BigInteger)(index + 1) * 12345));
            case "bytes32":
            {
                var w = new byte[32];
                Rng.NextBytes(w);
                return new RWord(w);
            }
            case "bytes20":
            {
                var w = new byte[32];
                Rng.NextBytes(w.AsSpan(0, 20));
                return new RWord(w);
            }
            default:
                throw new ArgumentException(type);
        }
    }

    /// <summary>"(T[N])" (fixed) or "(T[])" (dynamic) holding n elements.</summary>
    public static AbiWorkload Array(string type, int n, bool isFixed)
    {
        RValue[] items = Enumerable.Range(0, n).Select(i => (RValue)Element(type, i)).ToArray();
        string tuple = isFixed ? $"({type}[{n}])" : $"({type}[])";
        return new AbiWorkload($"{type}[{(isFixed ? n.ToString() : "")}]x{n}", tuple, new RSeq([new RSeq(items)]));
    }

    /// <summary>"((address,uint256)[])": an array of static tuples.</summary>
    public static AbiWorkload TupleArray(int n)
    {
        RValue[] items = Enumerable.Range(0, n).Select(i => (RValue)new RSeq([Element("address", i), Element("uint256", i)])).ToArray();
        return new AbiWorkload($"(address,uint256)[]x{n}", "((address,uint256)[])", new RSeq([new RSeq(items)]));
    }

    /// <summary>"(uint256 x n)": a static tuple of n members (the per-member cost of the validator loop, no array).</summary>
    public static AbiWorkload Members(int n) =>
        new($"uint256-members-{n}", "(" + string.Join(",", Enumerable.Repeat("uint256", n)) + ")", new RSeq(Enumerable.Range(0, n).Select(i => (RValue)Element("uint256", i)).ToArray()));

    /// <summary>"(string,bytes,string,bytes)" with the given payload length on each field (a string-heavy struct).</summary>
    public static AbiWorkload StringHeavy(int len)
    {
        byte[] Text(int seed) => System.Text.Encoding.ASCII.GetBytes(new string((char)('a' + seed), len));
        byte[] Blob(int seed) => BenchmarkData.RandomBytes(len, 7 + seed);
        return new AbiWorkload($"string-heavy-{len}", "(string,bytes,string,bytes)",
            new RSeq([new RBlob(Text(0)), new RBlob(Blob(1)), new RBlob(Text(2)), new RBlob(Blob(3))]));
    }

    /// <summary>"(uint256,address[],bytes32[],bytes)": a realistic contract call carrying a path, a Merkle proof and a payload.</summary>
    public static AbiWorkload ProofCall(int proof, int path, int payload)
    {
        return new AbiWorkload($"proof{proof}-path{path}-bytes{payload}", "(uint256,address[],bytes32[],bytes)",
            new RSeq([Element("uint256", 5),
                new RSeq(Enumerable.Range(0, path).Select(i => (RValue)Element("address", i)).ToArray()),
                new RSeq(Enumerable.Range(0, proof).Select(i => (RValue)Element("bytes32", i)).ToArray()),
                new RBlob(BenchmarkData.RandomBytes(payload, 11))]));
    }
}

/// <summary>Whole-document strict decode of a static array of one element type: the per-element validation cost.</summary>
[MemoryDiagnoser]
public class AbiShapeArrayDecodeBenchmarks
{
    private ABIDecodeLimits _limits;
    private AbiWorkload _workload = null!;

    // Defaults: the full matrix. ABI_ELEMS / ABI_NS / ABI_FIXED narrow it for an A/B run of two builds.
    public static IEnumerable<string> Elements => (Environment.GetEnvironmentVariable("ABI_ELEMS") ?? "uint256,address,uint8,bool,bytes32").Split(',');

    public static IEnumerable<int> Ns => (Environment.GetEnvironmentVariable("ABI_NS") ?? "1,4,32,256,4096").Split(',').Select(int.Parse);

    public static IEnumerable<bool> Fixeds => (Environment.GetEnvironmentVariable("ABI_FIXED") ?? "false,true").Split(',').Select(bool.Parse);

    [ParamsSource(nameof(Elements))]
    public string Element { get; set; } = "uint256";

    [ParamsSource(nameof(Ns))]
    public int N { get; set; }

    [ParamsSource(nameof(Fixeds))]
    public bool Fixed { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _limits = AbiBenchmarkData.DecodeLimits;
        _workload = AbiShapeData.Array(Element, N, Fixed);
    }

    [Benchmark]
    public int Decode()
    {
        bool ok = ABIData.TryDecodeComplete(_workload.Arguments, _workload.Schema, in _limits, out ABIData data, out _);
        return ok ? data.EncodedBytes.Length : -1;
    }
}

/// <summary>Other shapes of the profile: static tuple arrays, string/bytes heavy structs, the multicall sizes and the large bytes payloads.</summary>
[MemoryDiagnoser]
public class AbiShapeMiscDecodeBenchmarks
{
    private ABIDecodeLimits _limits;
    private AbiWorkload _workload = null!;

    private static readonly string[] AllShapes = ["tuple-array-1", "tuple-array-16", "tuple-array-256", "tuple-array-1024", "string-heavy-0", "string-heavy-31", "string-heavy-100", "string-heavy-1000", "multicall-2", "multicall-16", "multicall-128", "multicall-1024", "bytes-31", "bytes-32", "bytes-33", "bytes-65", "bytes-1024", "bytes-65536", "proof-call-small", "proof-call-merkle", "empty", "uint256-members-1", "uint256-members-2", "uint256-members-8", "mixed-65"];

    public static IEnumerable<string> Shapes => Environment.GetEnvironmentVariable("ABI_SHAPES")?.Split(',') ?? AllShapes;

    [ParamsSource(nameof(Shapes))]
    public string Shape { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _limits = AbiBenchmarkData.DecodeLimits;
        _workload = Shape switch
        {
            "tuple-array-1" => AbiShapeData.TupleArray(1),
            "tuple-array-16" => AbiShapeData.TupleArray(16),
            "tuple-array-256" => AbiShapeData.TupleArray(256),
            "tuple-array-1024" => AbiShapeData.TupleArray(1024),
            "string-heavy-0" => AbiShapeData.StringHeavy(0),
            "string-heavy-31" => AbiShapeData.StringHeavy(31),
            "string-heavy-100" => AbiShapeData.StringHeavy(100),
            "string-heavy-1000" => AbiShapeData.StringHeavy(1000),
            "multicall-2" => AbiBenchmarkData.Multicall(2),
            "multicall-16" => AbiBenchmarkData.Multicall(16),
            "multicall-128" => AbiBenchmarkData.Multicall(128),
            "multicall-1024" => AbiBenchmarkData.Multicall(1024),
            "bytes-31" => AbiBenchmarkData.Bytes(31),
            "bytes-32" => AbiBenchmarkData.Bytes(32),
            "bytes-33" => AbiBenchmarkData.Bytes(33),
            "bytes-65" => AbiBenchmarkData.Bytes(65),
            "bytes-1024" => AbiBenchmarkData.Bytes(1024),
            "bytes-65536" => AbiBenchmarkData.Bytes(65536),
            "proof-call-small" => AbiShapeData.ProofCall(0, 3, 68),
            "proof-call-merkle" => AbiShapeData.ProofCall(20, 0, 0),
            "empty" => new AbiWorkload("empty", "()", new RSeq([])),
            "uint256-members-1" => AbiShapeData.Members(1),
            "uint256-members-2" => AbiShapeData.Members(2),
            "uint256-members-8" => AbiShapeData.Members(8),
            "mixed-65" => AbiBenchmarkData.Mixed(65),
            _ => throw new InvalidOperationException(Shape),
        };
    }

    [Benchmark]
    public int Decode()
    {
        bool ok = ABIData.TryDecodeComplete(_workload.Arguments, _workload.Schema, in _limits, out ABIData data, out _);
        return ok ? data.EncodedBytes.Length : -1;
    }
}

/// <summary>
/// Whole-operation decode of a request mix (guidance 10.3: the illustrative 70 percent transfers, 20 percent small contract calls, 9 percent
/// 2 to 16 child multicalls, 1 percent a larger multicall). A fixed shuffled sequence of calls is decoded with <c>ABICallData.TryDecode</c>
/// (selector check plus strict validation) and one accessor read per call; the result is ns per request. The "ArrayHeavy" mix replaces
/// the small calls and a part of the transfers with array-carrying calls, to bound the effect of a static-array optimisation.
/// </summary>
[MemoryDiagnoser]
public class AbiMixBenchmarks
{
    private ABIDecodeLimits _limits;
    private (byte[] Call, ABIFunctionSchema Function)[] _guidance = null!;
    private (byte[] Call, ABIFunctionSchema Function)[] _arrays = null!;

    [GlobalSetup]
    public void Setup()
    {
        _limits = AbiBenchmarkData.DecodeLimits;
        var transfer = AbiBenchmarkData.Transfer;
        var exact = AbiBenchmarkData.ExactInputSingle;
        var swap = new AbiWorkload("swap", "(uint256,uint256,address[],address,uint256)", new RSeq([
            AbiShapeData.Element("uint256", 1), AbiShapeData.Element("uint256", 2),
            new RSeq([AbiShapeData.Element("address", 1), AbiShapeData.Element("address", 2), AbiShapeData.Element("address", 3)]),
            AbiShapeData.Element("address", 4), AbiShapeData.Element("uint256", 3)]), "swapExactTokensForTokens");
        var mc2 = AbiBenchmarkData.Multicall(2);
        var mc16 = AbiBenchmarkData.Multicall(16);
        var mc128 = AbiBenchmarkData.Multicall(128);
        var claim = new AbiWorkload("claim", "(uint256,bytes32[])", new RSeq([AbiShapeData.Element("uint256", 1),
            new RSeq(Enumerable.Range(0, 24).Select(i => (RValue)AbiShapeData.Element("bytes32", i)).ToArray())]), "claim");
        var batch64 = new AbiWorkload("batch", "(address[],uint256[])", new RSeq([
            new RSeq(Enumerable.Range(0, 64).Select(i => (RValue)AbiShapeData.Element("address", i)).ToArray()),
            new RSeq(Enumerable.Range(0, 64).Select(i => (RValue)AbiShapeData.Element("uint256", i)).ToArray())]), "airdrop");
        var flags = new AbiWorkload("flags", "(bool[],uint8[])", new RSeq([
            new RSeq(Enumerable.Range(0, 32).Select(i => (RValue)AbiShapeData.Element("bool", i)).ToArray()),
            new RSeq(Enumerable.Range(0, 32).Select(i => (RValue)AbiShapeData.Element("uint8", i)).ToArray())]), "setFlags");

        _guidance = Shuffle(Pick(70, transfer), Pick(10, exact), Pick(10, swap), Pick(4, mc2), Pick(4, mc16), Pick(1, mc16), Pick(1, mc128));
        _arrays = Shuffle(Pick(30, transfer), Pick(10, exact), Pick(10, swap), Pick(20, claim), Pick(15, batch64), Pick(15, flags));
        foreach ((byte[] call, ABIFunctionSchema function) in _guidance.Concat(_arrays))
        {
            AbiBenchmarkData.RequireTrue(ABICallData.TryDecode(call, function, in _limits, out _, out ABIStatus s), $"mix call rejected: {s}");
        }
    }

    private static (byte[], ABIFunctionSchema)[] Pick(int count, AbiWorkload w) => Enumerable.Repeat((w.Call, w.Function), count).ToArray();

    private static (byte[], ABIFunctionSchema)[] Shuffle(params (byte[], ABIFunctionSchema)[][] groups)
    {
        var all = groups.SelectMany(g => g).ToArray();
        var rng = new Random(77);
        for (int i = all.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (all[i], all[j]) = (all[j], all[i]);
        }

        return all;
    }

    private int DecodeAll((byte[] Call, ABIFunctionSchema Function)[] mix)
    {
        int sum = 0;
        foreach ((byte[] call, ABIFunctionSchema function) in mix)
        {
            if (ABICallData.TryDecode(call, function, in _limits, out ABICallData decoded, out _))
            {
                sum += decoded.Arguments.EncodedBytes.Length;
            }
        }

        return sum;
    }

    /// <summary>100 requests: 70 transfers, 20 small calls (exactInputSingle, swap with a 3-address path), 10 multicalls (8 with 2 or 16 children, 1 with 16, 1 with 128). Divide by 100.</summary>
    [Benchmark(OperationsPerInvoke = 100)]
    public int GuidanceMix() => DecodeAll(_guidance);

    /// <summary>100 requests: 30 transfers, 20 small calls, 20 Merkle claims (bytes32[24]), 15 airdrops (address[64], uint256[64]) and 15 flag setters (bool[32], uint8[32]).</summary>
    [Benchmark(OperationsPerInvoke = 100)]
    public int ArrayHeavyMix() => DecodeAll(_arrays);
}

/// <summary>A decode of <c>(bytes)</c> whose padding is exactly <see cref="Padding"/> bytes (1..31): the whole-operation view of the padding scan.</summary>
[MemoryDiagnoser]
public class AbiPaddingSweepBenchmarks
{
    private ABIDecodeLimits _limits;
    private AbiWorkload _workload = null!;

    [Params(1, 2, 3, 4, 6, 8, 12, 16, 24, 31)]
    public int Padding { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _limits = AbiBenchmarkData.DecodeLimits;
        _workload = AbiBenchmarkData.Bytes(64 - Padding);
    }

    [Benchmark]
    public int Decode()
    {
        bool ok = ABIData.TryDecodeComplete(_workload.Arguments, _workload.Schema, in _limits, out ABIData data, out _);
        return ok ? data.EncodedBytes.Length : -1;
    }
}

/// <summary>
/// UTF-8 validation of a <c>string</c> field (guidance 3: "Hex text conversion / Large ABI dynamic bytes payload ... vectorise only a proven
/// payload-specific comparison"): <c>TryGetStringUtf8Validated</c> (one BCL <c>Utf8.IsValid</c> pass over the payload) against
/// <c>TryGetBytePayload</c> (locate only), so the difference is the validation pass alone.
/// </summary>
[MemoryDiagnoser]
public class AbiUtf8Benchmarks
{
    private byte[] _document = null!;
    private ABISchema _schema = null!;
    private ABIDecodeLimits _limits;

    [Params(31, 1000, 65536)]
    public int Length { get; set; }

    [Params("ascii", "multibyte")]
    public string Text { get; set; } = "ascii";

    [GlobalSetup]
    public void Setup()
    {
        _limits = AbiBenchmarkData.DecodeLimits;
        string text = Text == "ascii" ? new string('a', Length) : string.Concat(Enumerable.Repeat("é€\U0001F600", Length / 9 + 1));
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text).AsSpan(0, Text == "ascii" ? Length : Length / 9 * 9).ToArray();
        var workload = new AbiWorkload($"string-{Text}-{Length}", "(string)", new RSeq([new RBlob(utf8)]));
        _document = workload.Arguments;
        _schema = workload.Schema;
    }

    [Benchmark(Baseline = true)]
    public int LocateOnly()
    {
        ABIData.TryDecodeComplete(_document, _schema, in _limits, out ABIData data, out _);
        return data.TryGetBytePayload([0], out ReadOnlySpan<byte> payload, out _, out _) ? payload.Length : -1;
    }

    [Benchmark]
    public int LocateAndValidateUtf8()
    {
        ABIData.TryDecodeComplete(_document, _schema, in _limits, out ABIData data, out _);
        return data.TryGetStringUtf8Validated([0], out ReadOnlySpan<byte> payload, out _, out _) ? payload.Length : -1;
    }
}
