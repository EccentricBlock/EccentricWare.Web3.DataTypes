using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// Typed readers (<see cref="RLPTypedExtensions"/>) on an already decoded single byte string. Each row decodes once and
/// repeats the reader <see cref="Repeats"/> times, so the figure is per read with the decode amortised.
/// </summary>
[MemoryDiagnoser]
public class RlpTypedAdapterReadBenchmarks
{
    private const int Repeats = 64;

    private RLPDecodeLimits _limits;
    private byte[] _small = null!;
    private byte[] _bits64 = null!;
    private byte[] _full = null!;
    private byte[] _address = null!;
    private byte[] _hash = null!;
    private byte[] _calldata = null!;
    private uint256 _smallValue;
    private uint256 _bits64Value;
    private uint256 _fullValue;
    private Address _addressValue;
    private Hash32 _hashValue;
    private FunctionSelector _selectorValue;
    private byte[] _yParityFields = null!;
    private Signature _yParitySignature;

    [GlobalSetup]
    public void Setup()
    {
        _limits = RLPDecodeLimits.Default;
        var random = new Random(BenchmarkData.Seed);
        byte[] smallMagnitude = RlpBenchmarkData.Magnitude(21000);
        byte[] bits64Magnitude = RlpBenchmarkData.Magnitude(ulong.MaxValue - 12345);
        byte[] fullMagnitude = RlpBenchmarkData.RandomNonZeroLeading(random, 32);
        byte[] addressBytes = RlpBenchmarkData.RandomBytes(random, 20);
        byte[] hashBytes = RlpBenchmarkData.RandomBytes(random, 32);
        _calldata = BenchmarkData.ApproveMaxCalldata(BenchmarkData.Seed);

        _small = Encode(smallMagnitude);
        _bits64 = Encode(bits64Magnitude);
        _full = Encode(fullMagnitude);
        _address = Encode(addressBytes);
        _hash = Encode(hashBytes);
        _calldata = Encode(_calldata);

        _smallValue = new uint256(smallMagnitude);
        _bits64Value = new uint256(bits64Magnitude);
        _fullValue = new uint256(fullMagnitude);
        _addressValue = Address.FromEvmBytes(addressBytes);
        _hashValue = new Hash32(hashBytes);
        _selectorValue = new FunctionSelector(0x09, 0x5e, 0xa7, 0xb3);

        Require(_small, (in RLPData d) => d.TryGetUInt256(out uint256 v) == RLPStatus.Success && v == _smallValue, "small uint256");
        Require(_bits64, (in RLPData d) => d.TryGetUInt256(out uint256 v) == RLPStatus.Success && v == _bits64Value, "64-bit uint256");
        Require(_full, (in RLPData d) => d.TryGetUInt256(out uint256 v) == RLPStatus.Success && v == _fullValue, "full uint256");
        Require(_address, (in RLPData d) => d.TryGetAddress(out Address v) == RLPStatus.Success && v == _addressValue, "address");
        Require(_hash, (in RLPData d) => d.TryGetHash32(out Hash32 v) == RLPStatus.Success && v == _hashValue, "hash32");
        Require(_calldata, (in RLPData d) => d.TryGetFunctionSelector(out FunctionSelector v) == RLPStatus.Success && v == _selectorValue, "selector");

        // [yParity, r, s] of an EIP-1559 transaction: parity 1, then the fixture's 32-byte r and s.
        RlpTransactionFixture tx = RlpBenchmarkData.Transactions;
        _yParityFields = RlpReferenceNode.List(
            RlpReferenceNode.String([1]), RlpReferenceNode.String(tx.LegacyFieldBytes[7]), RlpReferenceNode.String(tx.LegacyFieldBytes[8])).Encode();
        _yParitySignature = RlpBenchmarkData.ExpectedSignature(tx, 1);
        RlpBenchmarkData.RequireSuccess(RLPData.TryDecodeComplete(_yParityFields, in _limits, out RLPData fields, out _), "y parity fields");
        RlpBenchmarkData.RequireSuccess(fields.TryGetChildren(out RLPChildEnumerator children), "y parity children");
        children.MoveNext();
        RLPData yParity = children.Current;
        children.MoveNext();
        RLPData r = children.Current;
        children.MoveNext();
        RLPData s = children.Current;
        RlpBenchmarkData.RequireSuccess(RLPTypedExtensions.TryGetEvmYParitySignature(in yParity, in r, in s, out Signature signature), "y parity signature");
        if (signature != _yParitySignature || signature.V != 1)
        {
            throw new InvalidOperationException("TryGetEvmYParitySignature returned the wrong signature.");
        }
    }

    private delegate bool Check(in RLPData data);

    private void Require(byte[] encoded, Check check, string what)
    {
        RlpBenchmarkData.RequireSuccess(RLPData.TryDecodeComplete(encoded, in _limits, out RLPData root, out _), what);
        if (!check(in root))
        {
            throw new InvalidOperationException($"Typed read of {what} returned the wrong value.");
        }
    }

    private static byte[] Encode(byte[] payload) => RlpReferenceNode.String(payload).Encode();

    [Benchmark(OperationsPerInvoke = Repeats)]
    public ulong TryGetUInt256_Small() => ReadUInt256(_small);

    [Benchmark(OperationsPerInvoke = Repeats)]
    public ulong TryGetUInt256_Bits64() => ReadUInt256(_bits64);

    [Benchmark(OperationsPerInvoke = Repeats)]
    public ulong TryGetUInt256_FullWidth() => ReadUInt256(_full);

    private ulong ReadUInt256(byte[] input)
    {
        if (RLPData.TryDecodeComplete(input, in _limits, out RLPData root, out _) != RLPStatus.Success)
        {
            return 0;
        }

        ulong sum = 0;
        for (int i = 0; i < Repeats; i++)
        {
            root.TryGetUInt256(out uint256 value);
            sum += value.IsZero ? 0UL : 1UL;
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryGetAddress()
    {
        if (RLPData.TryDecodeComplete(_address, in _limits, out RLPData root, out _) != RLPStatus.Success)
        {
            return -1;
        }

        int sum = 0;
        for (int i = 0; i < Repeats; i++)
        {
            root.TryGetAddress(out Address value);
            sum += value.IsZero ? 0 : 1;
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryGetHash32()
    {
        if (RLPData.TryDecodeComplete(_hash, in _limits, out RLPData root, out _) != RLPStatus.Success)
        {
            return -1;
        }

        int sum = 0;
        for (int i = 0; i < Repeats; i++)
        {
            root.TryGetHash32(out Hash32 value);
            sum += value.IsZero ? 0 : 1;
        }

        return sum;
    }

    /// <summary>Builds the signature from three already located fields (y parity, r, s); the decode and the three field lookups are amortised into the figure.</summary>
    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryGetEvmYParitySignature()
    {
        if (RLPData.TryDecodeComplete(_yParityFields, in _limits, out RLPData fields, out _) != RLPStatus.Success
            || fields.TryGetChildren(out RLPChildEnumerator children) != RLPStatus.Success)
        {
            return -1;
        }

        children.MoveNext();
        RLPData yParity = children.Current;
        children.MoveNext();
        RLPData r = children.Current;
        children.MoveNext();
        RLPData s = children.Current;
        int sum = 0;
        for (int i = 0; i < Repeats; i++)
        {
            RLPTypedExtensions.TryGetEvmYParitySignature(in yParity, in r, in s, out Signature signature);
            sum += signature.V;
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryGetFunctionSelector()
    {
        if (RLPData.TryDecodeComplete(_calldata, in _limits, out RLPData root, out _) != RLPStatus.Success)
        {
            return -1;
        }

        int sum = 0;
        for (int i = 0; i < Repeats; i++)
        {
            root.TryGetFunctionSelector(out FunctionSelector value);
            sum += (int)value.Value;
        }

        return sum;
    }
}

/// <summary>
/// Typed writers on a <see cref="RLPWriter"/>. Each row opens one list, writes the value <see cref="Repeats"/> times, and
/// closes it, so the figure is per write with the writer set-up amortised. The output is compared with the independent
/// reference encoder.
/// </summary>
[MemoryDiagnoser]
public class RlpTypedAdapterWriteBenchmarks
{
    private const int Repeats = 64;

    private byte[] _destination = null!;
    private uint256 _small;
    private uint256 _bits64;
    private uint256 _full;
    private Address _address;
    private Hash32 _hash;
    private int _smallItemLength;
    private int _bits64ItemLength;
    private int _fullItemLength;
    private int _addressItemLength;
    private int _hashItemLength;
    private const int YParityItemLength = 1; // parity 1 is the literal byte 01

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(BenchmarkData.Seed);
        byte[] smallMagnitude = RlpBenchmarkData.Magnitude(21000);
        byte[] bits64Magnitude = RlpBenchmarkData.Magnitude(ulong.MaxValue - 12345);
        byte[] fullMagnitude = RlpBenchmarkData.RandomNonZeroLeading(random, 32);
        byte[] addressBytes = RlpBenchmarkData.RandomBytes(random, 20);
        byte[] hashBytes = RlpBenchmarkData.RandomBytes(random, 32);

        _small = new uint256(smallMagnitude);
        _bits64 = new uint256(bits64Magnitude);
        _full = new uint256(fullMagnitude);
        _address = Address.FromEvmBytes(addressBytes);
        _hash = new Hash32(hashBytes);

        _smallItemLength = RlpReferenceNode.String(smallMagnitude).EncodedLength;
        _bits64ItemLength = RlpReferenceNode.String(bits64Magnitude).EncodedLength;
        _fullItemLength = RlpReferenceNode.String(fullMagnitude).EncodedLength;
        _addressItemLength = RlpReferenceNode.String(addressBytes).EncodedLength;
        _hashItemLength = RlpReferenceNode.String(hashBytes).EncodedLength;
        _destination = new byte[(Repeats * 33) + 16];

        Verify(smallMagnitude, WriteSmall(), "small uint256");
        Verify(bits64Magnitude, WriteBits64(), "64-bit uint256");
        Verify(fullMagnitude, WriteFull(), "full uint256");
        Verify(addressBytes, WriteAddress(), "address");
        Verify(hashBytes, WriteHash(), "hash32");
        Verify([1], WriteYParity(), "y parity 1");
    }

    private void Verify(byte[] item, int written, string what)
    {
        var children = new RlpReferenceNode[Repeats];
        Array.Fill(children, RlpReferenceNode.String(item));
        byte[] expected = RlpReferenceNode.List(children).Encode();
        if (written != expected.Length)
        {
            throw new InvalidOperationException($"{what}: wrote {written} bytes, reference {expected.Length}.");
        }

        RlpBenchmarkData.RequireEqual(expected, _destination.AsSpan(0, written), $"{what} bytes");
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryWriteUInt256_Small() => WriteSmall();

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryWriteUInt256_Bits64() => WriteBits64();

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryWriteUInt256_FullWidth() => WriteFull();

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryWriteAddress() => WriteAddress();

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryWriteHash32() => WriteHash();

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryWriteEvmYParity() => WriteYParity();

    private int WriteSmall()
    {
        Span<int> frames = stackalloc int[1];
        var writer = new RLPWriter(_destination, frames);
        RLPStatus status = writer.TryBeginList(Repeats * _smallItemLength);
        for (int i = 0; i < Repeats; i++)
        {
            status |= writer.TryWriteUInt256(_small);
        }

        return Finish(ref writer, status);
    }

    private int WriteBits64()
    {
        Span<int> frames = stackalloc int[1];
        var writer = new RLPWriter(_destination, frames);
        RLPStatus status = writer.TryBeginList(Repeats * _bits64ItemLength);
        for (int i = 0; i < Repeats; i++)
        {
            status |= writer.TryWriteUInt256(_bits64);
        }

        return Finish(ref writer, status);
    }

    private int WriteFull()
    {
        Span<int> frames = stackalloc int[1];
        var writer = new RLPWriter(_destination, frames);
        RLPStatus status = writer.TryBeginList(Repeats * _fullItemLength);
        for (int i = 0; i < Repeats; i++)
        {
            status |= writer.TryWriteUInt256(_full);
        }

        return Finish(ref writer, status);
    }

    private int WriteAddress()
    {
        Span<int> frames = stackalloc int[1];
        var writer = new RLPWriter(_destination, frames);
        RLPStatus status = writer.TryBeginList(Repeats * _addressItemLength);
        for (int i = 0; i < Repeats; i++)
        {
            status |= writer.TryWriteAddress(_address);
        }

        return Finish(ref writer, status);
    }

    private int WriteHash()
    {
        Span<int> frames = stackalloc int[1];
        var writer = new RLPWriter(_destination, frames);
        RLPStatus status = writer.TryBeginList(Repeats * _hashItemLength);
        for (int i = 0; i < Repeats; i++)
        {
            status |= writer.TryWriteHash32(_hash);
        }

        return Finish(ref writer, status);
    }

    private int WriteYParity()
    {
        Span<int> frames = stackalloc int[1];
        var writer = new RLPWriter(_destination, frames);
        RLPStatus status = writer.TryBeginList(Repeats * YParityItemLength);
        for (int i = 0; i < Repeats; i++)
        {
            status |= writer.TryWriteEvmYParity(1);
        }

        return Finish(ref writer, status);
    }

    private static int Finish(ref RLPWriter writer, RLPStatus status)
    {
        status |= writer.TryEndList();
        status |= writer.TryComplete(out int written);
        return status == RLPStatus.Success ? written : -1;
    }
}
