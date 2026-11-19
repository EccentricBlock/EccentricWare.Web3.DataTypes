using EccentricWare.Web3.DataTypes.ABI;
using EccentricWare.Web3.DataTypes.Tests.ABI;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

// ABIReferenceModel.cs and ABIIndependentEncoder.cs are compiled into this project from the test project (see the csproj): two independent,
// allocating, recursive reference encoders that share no code with the library. They build every benchmark input, and [GlobalSetup] checks
// the inputs against them, so a library defect cannot silently produce its own benchmark input.

/// <summary>One benchmark workload: schema, function, the canonical arguments and the call (selector first).</summary>
internal sealed class AbiWorkload
{
    public AbiWorkload(string name, string tuple, RSeq value, string functionName = "fn")
    {
        Name = name;
        Type = ABIReference.ParseTuple(tuple);
        Value = value;
        Arguments = ABIIndependentEncoder.EncodeArguments(Type, value);
        byte[] second = ABIReference.EncodeRoot(Type, value);
        AbiBenchmarkData.RequireEqual(Arguments, second, $"{name}: the two reference encoders disagree");

        var limits = ABICompileLimits.Default;
        if (!ABITupleDefinition.TryParse(tuple, out ABITupleDefinition? definition, out ABISchemaError parseError)
            || !ABISchemaCompiler.TryCompileFunction(functionName, definition, in limits, out ABIFunctionSchema? function, out parseError))
        {
            throw new InvalidOperationException($"{name}: cannot compile {tuple}: {parseError}");
        }

        Function = function;
        Schema = function.InputSchema;
        Call = [(byte)(function.Selector >> 24), (byte)(function.Selector >> 16), (byte)(function.Selector >> 8), (byte)function.Selector, .. Arguments];

        var decodeLimits = AbiBenchmarkData.DecodeLimits;
        AbiBenchmarkData.RequireTrue(ABIData.TryDecodeComplete(Arguments, Schema, in decodeLimits, out _, out ABIStatus status), $"{name}: arguments rejected: {status}");
        AbiBenchmarkData.RequireTrue(ABICallData.TryDecode(Call, Function, in decodeLimits, out _, out status), $"{name}: call rejected: {status}");
    }

    public string Name { get; }

    public RTupleType Type { get; }

    public RSeq Value { get; }

    public ABISchema Schema { get; }

    public ABIFunctionSchema Function { get; }

    public byte[] Arguments { get; }

    public byte[] Call { get; }

    public override string ToString() => Name;
}

internal static class AbiBenchmarkData
{
    /// <summary>The decode defaults: 262,144 bytes, depth 16, 4,096 array elements, 16,384 values, 100,000 work units.</summary>
    public static readonly ABIDecodeLimits DecodeLimits = ABIDecodeLimits.Default;

    public static readonly ABIEncodeLimits EncodeLimits = ABIEncodeLimits.Default;

    // Real addresses and the 1 ETH amount of the cast fixtures used by the tests.
    private static readonly byte[] Vitalik = Convert.FromHexString("d8dA6BF26964aF9D7eEd9e03E53415D37aA96045");
    private static readonly byte[] Weth = Convert.FromHexString("C02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2");
    private static readonly byte[] Usdc = Convert.FromHexString("A0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48");

    // cast calldata 'transfer(address,uint256)' 0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045 1000000000000000000
    private const string TransferCastHex = "a9059cbb000000000000000000000000d8da6bf26964af9d7eed9e03e53415d37aa960450000000000000000000000000000000000000000000000000de0b6b3a7640000";

    public static AbiWorkload Transfer { get; } = Build();

    private static AbiWorkload Build()
    {
        var transfer = new AbiWorkload("transfer", "(address,uint256)", Args(Addr(Vitalik), Uint(1_000_000_000_000_000_000UL)), "transfer");
        RequireEqual(Convert.FromHexString(TransferCastHex), transfer.Call, "transfer: reference encoders vs the cast fixture");
        return transfer;
    }

    public static AbiWorkload ExactInputSingle { get; } = new(
        "exactInputSingle", "((address,address,uint24,address,uint256,uint256,uint256,uint160))",
        Args(Seq(Addr(Weth), Addr(Usdc), Uint(500), Addr(Vitalik), Uint(1_700_000_000), Uint(1_000_000_000_000_000_000UL), Uint(1_800_000_000), Uint(0))),
        "exactInputSingle");

    public static AbiWorkload Mixed(int bytesLength) => new(
        $"mixed-{bytesLength}", "(address,uint256,bytes)", Args(Addr(Vitalik), Uint(42), Blob(Payload(bytesLength))));

    public static AbiWorkload Bytes(int bytesLength) => new($"bytes-{bytesLength}", "(bytes)", Args(Blob(Payload(bytesLength))));

    /// <summary><c>multicall(bytes[])</c> of <paramref name="children"/> ERC-20 <c>transfer</c> calls (68 bytes each), as the spec 8.2 example.</summary>
    public static AbiWorkload Multicall(int children)
    {
        RValue[] calls = Enumerable.Range(0, children).Select(i => (RValue)Blob(TransferChild(i))).ToArray();
        return new AbiWorkload($"multicall-{children}", "(bytes[])", Args(new RSeq(calls)), "multicall");
    }

    /// <summary>A chain of <paramref name="levels"/> dynamic arrays with one element each and a <c>uint256</c> at the bottom: depth levels + 2.</summary>
    public static AbiWorkload NestedArrays(int levels)
    {
        string tuple = "(uint256" + string.Concat(Enumerable.Repeat("[]", levels)) + ")";
        RValue value = Uint(7);
        for (int i = 0; i < levels; i++)
        {
            value = new RSeq([value]);
        }

        return new AbiWorkload($"nested-{levels}", tuple, Args(value));
    }

    /// <summary><c>(bytes, uint256 x K, bytes)</c>: the first dynamic field is followed by K static members before the next dynamic one.</summary>
    public static AbiWorkload StaticMembersBetween(int staticMembers)
    {
        string tuple = "(bytes," + string.Join(",", Enumerable.Repeat("uint256", staticMembers)) + ",bytes)";
        var items = new List<RValue> { Blob(Payload(40)) };
        items.AddRange(Enumerable.Range(0, staticMembers).Select(i => (RValue)Uint((ulong)i)));
        items.Add(Blob(Payload(40)));
        return new AbiWorkload($"static-{staticMembers}", tuple, new RSeq([.. items]));
    }

    /// <summary><c>(()[])</c> with <paramref name="count"/> zero-width elements.</summary>
    public static AbiWorkload EmptyTuples(int count) =>
        new($"empty-tuples-{count}", "(()[])", Args(new RSeq(Enumerable.Range(0, count).Select(_ => (RValue)new RSeq([])).ToArray())));

    public static byte[] Payload(int length) => BenchmarkData.RandomBytes(length);

    /// <summary>The 68-byte calldata of <c>transfer(address,uint256)</c> to a different address per index.</summary>
    public static byte[] TransferChild(int index)
    {
        var to = (byte[])Vitalik.Clone();
        to[19] = (byte)index;
        to[18] = (byte)(index >> 8);
        uint selector = Transfer.Function.Selector;
        return [(byte)(selector >> 24), (byte)(selector >> 16), (byte)(selector >> 8), (byte)selector,
            .. ABIIndependentEncoder.EncodeArguments(Transfer.Type, Args(Addr(to), Uint((ulong)(index + 1) * 1_000_000UL)))];
    }

    public static byte[] VitalikAddress => Vitalik;

    public static byte[] WethAddress => Weth;

    public static byte[] UsdcAddress => Usdc;

    /// <summary>1 ETH (10^18) as a 32-byte word.</summary>
    public static byte[] OneEthWord { get; } = ABIReference.ToWord(1_000_000_000_000_000_000UL);

    /// <summary>Compiles a canonical tuple (for workloads that the decoder is meant to reject).</summary>
    public static ABISchema CompileTuple(string tuple)
    {
        var limits = ABICompileLimits.Default;
        if (!ABITupleDefinition.TryParse(tuple, out ABITupleDefinition? definition, out ABISchemaError error)
            || !ABISchemaCompiler.TryCompileArguments(definition, in limits, out ABISchema? schema, out error))
        {
            throw new InvalidOperationException($"cannot compile {tuple}: {error}");
        }

        return schema;
    }

    // ------------------------------------------------------------------ value builders

    private static RSeq Args(params RValue[] items) => new(items);

    private static RSeq Seq(params RValue[] items) => new(items);

    private static RWord Addr(byte[] address20) => new(ABIReference.ToWord(ABIReference.Unsigned(address20)));

    private static RWord Uint(ulong value) => new(ABIReference.ToWord(value));

    private static RBlob Blob(byte[] data) => new(data);

    // ------------------------------------------------------------------ checks

    public static void RequireTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void RequireEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, string what)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"{what}: bytes differ (expected {expected.Length}, actual {actual.Length}).");
        }
    }
}
