using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

// Rows for the EVM signature adapters and the sizing helpers. Conventions as in RlpTypedAdapterBenchmarks.cs: a RLPData is
// a ref struct, so each row decodes (or opens a writer) once and repeats the call Repeats times, and the figure is per call
// with that set-up amortised. The legacy cases (see RlpEvmCase) have their own class because each [Params] aspect would
// otherwise multiply with the other rows of a shared class.

/// <summary>
/// <c>RLPTypedExtensions.TryGetEvmLegacySignature</c> on three already located fields (<c>v</c>, <c>r</c>, <c>s</c>) for a
/// pre-EIP-155 <c>v</c> and for chain ids 1, 137, 98866 and <see cref="ulong.MaxValue"/> (a 9-byte <c>v</c>, the widest accepted).
/// <c>[GlobalSetup]</c> checks the signature (fixture <c>r</c> and <c>s</c>, <c>V</c> = y parity) and the returned chain id.
/// </summary>
[MemoryDiagnoser]
public class RlpEvmLegacySignatureReadBenchmarks
{
    private const int Repeats = 64;

    private RLPDecodeLimits _limits;
    private byte[] _fields = null!;

    [Params(RlpEvmCase.NoChainId, RlpEvmCase.Chain1, RlpEvmCase.Chain137, RlpEvmCase.Chain98866, RlpEvmCase.ChainIdMax)]
    public string Case { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _limits = RLPDecodeLimits.Default;
        RlpEvmCase.Resolve(Case, out ulong? chainId, out byte yParity);
        RlpTransactionFixture tx = RlpBenchmarkData.Transactions;
        byte[] v = RlpEvmLegacyFixture.MinimalMagnitude(RlpEvmCase.WireV(chainId, yParity));
        if (Case == RlpEvmCase.ChainIdMax && v.Length != 9)
        {
            throw new InvalidOperationException($"The widest legacy v should be 9 bytes, got {v.Length}.");
        }

        _fields = RlpReferenceNode.List(
            RlpReferenceNode.String(v), RlpReferenceNode.String(tx.LegacyFieldBytes[7]), RlpReferenceNode.String(tx.LegacyFieldBytes[8])).Encode();

        RlpBenchmarkData.RequireSuccess(RLPData.TryDecodeComplete(_fields, in _limits, out RLPData list, out _), "legacy signature fields");
        RlpBenchmarkData.RequireSuccess(list.TryGetChildren(out RLPChildEnumerator children), "legacy signature children");
        children.MoveNext();
        RLPData vField = children.Current;
        children.MoveNext();
        RLPData rField = children.Current;
        children.MoveNext();
        RLPData sField = children.Current;
        RlpBenchmarkData.RequireSuccess(
            RLPTypedExtensions.TryGetEvmLegacySignature(in vField, in rField, in sField, out Signature signature, out ulong? readChainId), "legacy signature");
        if (signature != RlpBenchmarkData.ExpectedSignature(tx, yParity) || signature.V != yParity || readChainId != chainId)
        {
            throw new InvalidOperationException($"TryGetEvmLegacySignature case {Case}: wrong signature or chain id ({readChainId}).");
        }
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryGetEvmLegacySignature()
    {
        if (RLPData.TryDecodeComplete(_fields, in _limits, out RLPData list, out _) != RLPStatus.Success
            || list.TryGetChildren(out RLPChildEnumerator children) != RLPStatus.Success)
        {
            return -1;
        }

        children.MoveNext();
        RLPData vField = children.Current;
        children.MoveNext();
        RLPData rField = children.Current;
        children.MoveNext();
        RLPData sField = children.Current;
        int sum = 0;
        for (int i = 0; i < Repeats; i++)
        {
            RLPTypedExtensions.TryGetEvmLegacySignature(in vField, in rField, in sField, out Signature signature, out ulong? chainId);
            sum += signature.V + (int)((chainId ?? 0UL) & 1);
        }

        return sum;
    }
}

/// <summary>
/// <c>RLPTypedExtensions.TryWriteEvmLegacyV</c> inside a framed writer for the same cases as the read rows. Each row opens one
/// list, writes <c>v</c> 64 times and closes it; the bytes are compared with the independent reference encoder in <c>[GlobalSetup]</c>.
/// </summary>
[MemoryDiagnoser]
public class RlpEvmLegacyVWriteBenchmarks
{
    private const int Repeats = 64;

    private byte[] _destination = null!;
    private ulong? _chainId;
    private byte _yParity;
    private int _itemLength;

    [Params(RlpEvmCase.NoChainId, RlpEvmCase.Chain1, RlpEvmCase.Chain137, RlpEvmCase.Chain98866, RlpEvmCase.ChainIdMax)]
    public string Case { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        RlpEvmCase.Resolve(Case, out _chainId, out _yParity);
        byte[] wireV = RlpEvmLegacyFixture.MinimalMagnitude(RlpEvmCase.WireV(_chainId, _yParity));
        _itemLength = RlpReferenceNode.String(wireV).EncodedLength;
        _destination = new byte[(Repeats * 10) + 16];

        int written = WriteLegacyV();
        var children = new RlpReferenceNode[Repeats];
        Array.Fill(children, RlpReferenceNode.String(wireV));
        byte[] expected = RlpReferenceNode.List(children).Encode();
        if (written != expected.Length)
        {
            throw new InvalidOperationException($"TryWriteEvmLegacyV case {Case}: wrote {written} bytes, reference {expected.Length}.");
        }

        RlpBenchmarkData.RequireEqual(expected, _destination.AsSpan(0, written), $"TryWriteEvmLegacyV {Case} bytes");
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int TryWriteEvmLegacyV() => WriteLegacyV();

    private int WriteLegacyV()
    {
        Span<int> frames = stackalloc int[1];
        var writer = new RLPWriter(_destination, frames);
        RLPStatus status = writer.TryBeginList(Repeats * _itemLength);
        for (int i = 0; i < Repeats; i++)
        {
            status |= writer.TryWriteEvmLegacyV(_yParity, _chainId);
        }

        status |= writer.TryEndList();
        status |= writer.TryComplete(out int written);
        return status == RLPStatus.Success ? written : -1;
    }
}

/// <summary>
/// The public sizing helpers (<c>RLPEncoder.GetUInt64EncodedLength</c>, <c>TryGetUnsignedMagnitudeLength</c>,
/// <c>RLPTypedExtensions.GetUInt256EncodedLength</c>, <c>TryGetEvmLegacyVEncodedLength</c>) over 64 distinct values of one width
/// class, so the JIT cannot hoist a loop-invariant call; the figure is per call and includes the array load and loop
/// overhead (about a cycle or two), so rows near 1 ns are at the measurement floor. <c>[GlobalSetup]</c> checks every size
/// against the independent reference encoder. <c>TryGetListLength</c> and <c>TryGetByteStringLength</c> have their own rows in
/// <see cref="RlpEncoderListBenchmarks"/> and <see cref="RlpEncoderStringBenchmarks"/>.
/// </summary>
[MemoryDiagnoser]
public class RlpSizingBenchmarks
{
    private const int Count = 64;
    private const int TwoByteWidth = 2;
    private const int FullWidth = 32;

    private ulong[] _zero = null!;
    private ulong[] _literal = null!;
    private ulong[] _twoBytes = null!;
    private ulong[] _eightBytes = null!;
    private uint256[] _uint256TwoBytes = null!;
    private uint256[] _uint256EightBytes = null!;
    private uint256[] _uint256Full = null!;
    private byte[] _magnitudesTwoBytes = null!;
    private byte[] _magnitudesFull = null!;
    private ulong?[] _noChainId = null!;
    private ulong?[] _chain1 = null!;
    private ulong?[] _chain98866 = null!;
    private ulong?[] _chainIdMax = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(BenchmarkData.Seed);
        _zero = new ulong[Count];
        _literal = Fill(random, 1, 128);
        _twoBytes = Fill(random, 0x100, 0x10000);
        _eightBytes = Fill(random, 0x0100_0000_0000_0000UL, ulong.MaxValue);
        _uint256TwoBytes = ToUInt256(_twoBytes);
        _uint256EightBytes = ToUInt256(_eightBytes);
        _uint256Full = new uint256[Count];
        _magnitudesTwoBytes = new byte[Count * TwoByteWidth];
        _magnitudesFull = new byte[Count * FullWidth];
        for (int i = 0; i < Count; i++)
        {
            _uint256Full[i] = new uint256(RlpBenchmarkData.RandomNonZeroLeading(random, FullWidth));
            RlpBenchmarkData.RandomNonZeroLeading(random, TwoByteWidth).CopyTo(_magnitudesTwoBytes, i * TwoByteWidth);
            RlpBenchmarkData.RandomNonZeroLeading(random, FullWidth).CopyTo(_magnitudesFull, i * FullWidth);
        }

        _noChainId = new ulong?[Count];
        _chain1 = Enumerable.Repeat((ulong?)1, Count).ToArray();
        _chain98866 = Enumerable.Repeat((ulong?)98866, Count).ToArray();
        _chainIdMax = Enumerable.Repeat((ulong?)ulong.MaxValue, Count).ToArray();

        foreach (ulong[] values in new[] { _zero, _literal, _twoBytes, _eightBytes })
        {
            for (int i = 0; i < Count; i++)
            {
                RequireSize(RlpReferenceNode.String(RlpBenchmarkData.Magnitude(values[i])).EncodedLength, RLPEncoder.GetUInt64EncodedLength(values[i]), "GetUInt64EncodedLength");
            }
        }

        Span<byte> big = stackalloc byte[FullWidth];
        foreach (uint256[] values in new[] { _uint256TwoBytes, _uint256EightBytes, _uint256Full })
        {
            for (int i = 0; i < Count; i++)
            {
                values[i].WriteBigEndian(big);
                byte[] magnitude = big.TrimStart((byte)0).ToArray();
                RequireSize(RlpReferenceNode.String(magnitude).EncodedLength, RLPTypedExtensions.GetUInt256EncodedLength(values[i]), "GetUInt256EncodedLength");
            }
        }

        VerifyMagnitudes(_magnitudesTwoBytes, TwoByteWidth);
        VerifyMagnitudes(_magnitudesFull, FullWidth);
        VerifyLegacyV(_noChainId);
        VerifyLegacyV(_chain1);
        VerifyLegacyV(_chain98866);
        VerifyLegacyV(_chainIdMax);
    }

    private static ulong[] Fill(Random random, ulong minInclusive, ulong maxExclusive)
    {
        var values = new ulong[Count];
        for (int i = 0; i < Count; i++)
        {
            values[i] = minInclusive + (BenchmarkData.NextUInt64(random) % (maxExclusive - minInclusive));
        }

        return values;
    }

    private static uint256[] ToUInt256(ulong[] values)
    {
        var result = new uint256[Count];
        for (int i = 0; i < Count; i++)
        {
            result[i] = new uint256(values[i]);
        }

        return result;
    }

    private static void RequireSize(int expected, int actual, string what)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"{what}: {actual}, reference {expected}.");
        }
    }

    private static void VerifyMagnitudes(byte[] magnitudes, int width)
    {
        for (int i = 0; i < Count; i++)
        {
            ReadOnlySpan<byte> magnitude = magnitudes.AsSpan(i * width, width);
            RlpBenchmarkData.RequireSuccess(RLPEncoder.TryGetUnsignedMagnitudeLength(magnitude, out int length), "TryGetUnsignedMagnitudeLength");
            RequireSize(RlpReferenceNode.String(magnitude.ToArray()).EncodedLength, length, "TryGetUnsignedMagnitudeLength");
        }
    }

    private static void VerifyLegacyV(ulong?[] chainIds)
    {
        for (int i = 0; i < Count; i++)
        {
            byte parity = (byte)(i & 1);
            RlpBenchmarkData.RequireSuccess(RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(parity, chainIds[i], out int length), "TryGetEvmLegacyVEncodedLength");
            byte[] wireV = RlpEvmLegacyFixture.MinimalMagnitude(RlpEvmCase.WireV(chainIds[i], parity));
            RequireSize(RlpReferenceNode.String(wireV).EncodedLength, length, "TryGetEvmLegacyVEncodedLength");
        }
    }

    [Benchmark(OperationsPerInvoke = Count)]
    public int GetUInt64EncodedLength_Zero() => SumUInt64(_zero);

    [Benchmark(OperationsPerInvoke = Count)]
    public int GetUInt64EncodedLength_Literal() => SumUInt64(_literal);

    [Benchmark(OperationsPerInvoke = Count)]
    public int GetUInt64EncodedLength_TwoBytes() => SumUInt64(_twoBytes);

    [Benchmark(OperationsPerInvoke = Count)]
    public int GetUInt64EncodedLength_EightBytes() => SumUInt64(_eightBytes);

    private static int SumUInt64(ulong[] values)
    {
        int sum = 0;
        for (int i = 0; i < values.Length; i++)
        {
            sum += RLPEncoder.GetUInt64EncodedLength(values[i]);
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = Count)]
    public int GetUInt256EncodedLength_TwoBytes() => SumUInt256(_uint256TwoBytes);

    [Benchmark(OperationsPerInvoke = Count)]
    public int GetUInt256EncodedLength_EightBytes() => SumUInt256(_uint256EightBytes);

    [Benchmark(OperationsPerInvoke = Count)]
    public int GetUInt256EncodedLength_FullWidth() => SumUInt256(_uint256Full);

    private static int SumUInt256(uint256[] values)
    {
        int sum = 0;
        for (int i = 0; i < values.Length; i++)
        {
            sum += RLPTypedExtensions.GetUInt256EncodedLength(values[i]);
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = Count)]
    public int TryGetUnsignedMagnitudeLength_TwoBytes() => SumMagnitudes(_magnitudesTwoBytes, TwoByteWidth);

    [Benchmark(OperationsPerInvoke = Count)]
    public int TryGetUnsignedMagnitudeLength_FullWidth() => SumMagnitudes(_magnitudesFull, FullWidth);

    private static int SumMagnitudes(byte[] magnitudes, int width)
    {
        int sum = 0;
        for (int i = 0; i < Count; i++)
        {
            RLPEncoder.TryGetUnsignedMagnitudeLength(magnitudes.AsSpan(i * width, width), out int length);
            sum += length;
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = Count)]
    public int TryGetEvmLegacyVEncodedLength_NoChainId() => SumLegacyV(_noChainId);

    [Benchmark(OperationsPerInvoke = Count)]
    public int TryGetEvmLegacyVEncodedLength_Chain1() => SumLegacyV(_chain1);

    [Benchmark(OperationsPerInvoke = Count)]
    public int TryGetEvmLegacyVEncodedLength_Chain98866() => SumLegacyV(_chain98866);

    [Benchmark(OperationsPerInvoke = Count)]
    public int TryGetEvmLegacyVEncodedLength_ChainIdMax() => SumLegacyV(_chainIdMax);

    private static int SumLegacyV(ulong?[] chainIds)
    {
        int sum = 0;
        for (int i = 0; i < chainIds.Length; i++)
        {
            RLPTypedExtensions.TryGetEvmLegacyVEncodedLength((byte)(i & 1), chainIds[i], out int length);
            sum += length;
        }

        return sum;
    }
}
