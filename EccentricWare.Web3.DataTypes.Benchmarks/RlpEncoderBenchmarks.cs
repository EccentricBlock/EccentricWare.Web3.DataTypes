using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// Byte-string sizing and atomic writes for payloads of 0, 1, 55, 56, 255 and 256 bytes (a 1-byte payload is at least
/// <c>0x80</c>, so it takes a header; <c>Literal_</c> rows cover the single-byte fast path). Every output is compared with
/// the independent reference encoder in <c>[GlobalSetup]</c>.
/// </summary>
[MemoryDiagnoser]
public class RlpEncoderStringBenchmarks
{
    private byte[] _payload = null!;
    private byte[] _destination = null!;
    private byte[] _expected = null!;
    private readonly byte[] _literal = [0x2a];

    [Params(0, 1, 55, 56, 255, 256)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _payload = RlpBenchmarkData.StringPayload(Size, BenchmarkData.Seed);
        if (_payload.Length > 1)
        {
            _payload[0] |= 0x10; // non-zero lead: also a canonical magnitude
        }

        _destination = new byte[Size + 16];
        _expected = RlpReferenceNode.String(_payload).Encode();

        RlpBenchmarkData.RequireSuccess(RLPEncoder.TryGetByteStringLength(_payload, out int length), "size");
        if (length != _expected.Length)
        {
            throw new InvalidOperationException($"Size {length} differs from reference {_expected.Length}.");
        }

        RlpBenchmarkData.RequireSuccess(RLPEncoder.TryWriteByteString(_payload, _destination, out int written), "write");
        RlpBenchmarkData.RequireEqual(_expected, _destination.AsSpan(0, written), "TryWriteByteString bytes");
        Array.Clear(_destination);
        RlpBenchmarkData.RequireSuccess(RLPEncoder.TryWriteUnsignedMagnitude(_payload, _destination, out written), "magnitude");
        RlpBenchmarkData.RequireEqual(_expected, _destination.AsSpan(0, written), "TryWriteUnsignedMagnitude bytes");
        Array.Clear(_destination);
        RlpBenchmarkData.RequireSuccess(RlpSession(), "session");
        RlpBenchmarkData.RequireEqual(_expected, _destination.AsSpan(0, _expected.Length), "root writer session bytes");
        RlpBenchmarkData.RequireSuccess(RLPEncoder.TryWriteByteString(_literal, _destination, out written), "literal");
        if (written != 1 || _destination[0] != 0x2a)
        {
            throw new InvalidOperationException("Literal byte was not written as itself.");
        }
    }

    private RLPStatus RlpSession()
    {
        Span<int> frames = stackalloc int[1];
        var writer = new RLPWriter(_destination, frames);
        RLPStatus status = writer.TryWriteByteString(_payload);
        return status | writer.TryComplete(out _);
    }

    [Benchmark]
    public int TryGetByteStringLength()
    {
        RLPEncoder.TryGetByteStringLength(_payload, out int length);
        return length;
    }

    [Benchmark]
    public int TryWriteByteString()
    {
        RLPEncoder.TryWriteByteString(_payload, _destination, out int written);
        return written;
    }

    [Benchmark]
    public int TryWriteUnsignedMagnitude()
    {
        RLPEncoder.TryWriteUnsignedMagnitude(_payload, _destination, out int written);
        return written;
    }

    /// <summary>A root-level writer session for one string: construct, write, complete.</summary>
    [Benchmark]
    public RLPStatus Writer_RootByteString() => RlpSession();

    [Benchmark]
    public int Literal_TryWriteByteString()
    {
        RLPEncoder.TryWriteByteString(_literal, _destination, out int written);
        return written;
    }
}

/// <summary>Unsigned integer writes: zero (<c>80</c>), 1, 127 (literals), 128 (first header form) and <c>ulong.MaxValue</c> (<c>88 ff*8</c>).</summary>
[MemoryDiagnoser]
public class RlpEncoderUInt64Benchmarks
{
    private byte[] _destination = null!;

    [Params(0UL, 1UL, 127UL, 128UL, ulong.MaxValue)]
    public ulong Value { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _destination = new byte[16];
        byte[] expected = RlpReferenceNode.String(RlpBenchmarkData.Magnitude(Value)).Encode();
        RlpBenchmarkData.RequireSuccess(RLPEncoder.TryWriteUInt64(Value, _destination, out int written), "write");
        RlpBenchmarkData.RequireEqual(expected, _destination.AsSpan(0, written), "TryWriteUInt64 bytes");
    }

    [Benchmark]
    public int TryWriteUInt64()
    {
        RLPEncoder.TryWriteUInt64(Value, _destination, out int written);
        return written;
    }
}

/// <summary>
/// List sizing and complete framed <see cref="RLPWriter"/> sessions that build the signed legacy transaction, the
/// EIP-1559 payload with its nested access list, and a 32-deep nested list. The <c>Writer_*</c> rows pass list payload
/// lengths that are constants of the fixture, so the cost of computing them is outside the timed region. The
/// <c>*_WithSizing</c> rows time the same write after first computing every list length from the field values with the
/// public sizing helpers, which is what a caller that does not already know the lengths pays (review finding M1).
/// </summary>
[MemoryDiagnoser]
public class RlpEncoderListBenchmarks
{
    private const int NestedDepth = 32;

    private RlpTransactionFixture _tx = null!;
    private byte[] _destination = null!;
    private byte[] _nestedExpected = null!;
    private readonly int[] _nestedPayloads = new int[NestedDepth];
    private readonly int[] _listPayloadSizes = [0, 55, 56, 65_536];
    private byte[] _eip155Expected = null!;

    [GlobalSetup]
    public void Setup()
    {
        _tx = RlpBenchmarkData.Transactions;
        _destination = new byte[2048];
        _nestedExpected = RlpBenchmarkData.NestedLists(NestedDepth);

        // The innermost list holds one 3-byte string; each enclosing list adds one header byte (every payload is below 56).
        for (int level = NestedDepth - 1, payload = 3; level >= 0; level--, payload++)
        {
            _nestedPayloads[level] = payload;
        }

        if (_tx.WriteLegacy(_destination) != _tx.LegacyEncoded.Length)
        {
            throw new InvalidOperationException("Legacy session length differs.");
        }

        RlpBenchmarkData.RequireEqual(_tx.LegacyEncoded, _destination.AsSpan(0, _tx.LegacyEncoded.Length), "legacy session bytes");
        if (_tx.WriteEip1559(_destination) != _tx.Eip1559Encoded.Length)
        {
            throw new InvalidOperationException("EIP-1559 session length differs.");
        }

        RlpBenchmarkData.RequireEqual(_tx.Eip1559Encoded, _destination.AsSpan(0, _tx.Eip1559Encoded.Length), "EIP-1559 session bytes");

        Array.Clear(_destination);
        if (_tx.WriteLegacyWithSizing(_destination) != _tx.LegacyEncoded.Length)
        {
            throw new InvalidOperationException("Legacy session with sizing: length differs.");
        }

        RlpBenchmarkData.RequireEqual(_tx.LegacyEncoded, _destination.AsSpan(0, _tx.LegacyEncoded.Length), "legacy session with sizing bytes");
        Array.Clear(_destination);
        if (_tx.WriteEip1559WithSizing(_destination) != _tx.Eip1559Encoded.Length)
        {
            throw new InvalidOperationException("EIP-1559 session with sizing: length differs.");
        }

        RlpBenchmarkData.RequireEqual(_tx.Eip1559Encoded, _destination.AsSpan(0, _tx.Eip1559Encoded.Length), "EIP-1559 session with sizing bytes");
        if (WriteNested() != _nestedExpected.Length)
        {
            throw new InvalidOperationException("Nested session length differs.");
        }

        RlpBenchmarkData.RequireEqual(_nestedExpected, _destination.AsSpan(0, _nestedExpected.Length), "nested session bytes");

        // Cross-check the writer against the published EIP-155 example (signed transaction from the EIP text).
        _eip155Expected = Convert.FromHexString(
            "f86c098504a817c800825208943535353535353535353535353535353535353535880de0b6b3a764000080" +
            "25a028ef61340bd939bc2195fe537567866003e1a15d3c71ff63e1590620aa636276" +
            "a067cbe9d8997f761aecb703304b3800ccf555c9f3dc64214b297fb1966a3b6d83");
        var eip155 = new RlpTransactionFixture(BenchmarkData.Seed)
        {
            Nonce = 9,
            GasPrice = new uint256(20_000_000_000UL),
            GasLimit = 21000,
            To = Address.FromEvmBytes(Enumerable.Repeat((byte)0x35, 20).ToArray()),
            Value = new uint256(1_000_000_000_000_000_000UL),
            Data = [],
            V = 37,
            R = new uint256(Convert.FromHexString("28ef61340bd939bc2195fe537567866003e1a15d3c71ff63e1590620aa636276")),
            S = new uint256(Convert.FromHexString("67cbe9d8997f761aecb703304b3800ccf555c9f3dc64214b297fb1966a3b6d83")),
            LegacyPayloadLength = _eip155Expected.Length - 2,
        };
        int written = eip155.WriteLegacy(_destination);
        if (written != _eip155Expected.Length)
        {
            throw new InvalidOperationException($"EIP-155 vector length {written}, expected {_eip155Expected.Length}.");
        }

        RlpBenchmarkData.RequireEqual(_eip155Expected, _destination.AsSpan(0, written), "EIP-155 vector bytes");
    }

    private int WriteNested()
    {
        Span<int> frames = stackalloc int[NestedDepth];
        var writer = new RLPWriter(_destination, frames);
        RLPStatus status = RLPStatus.Success;

        for (int level = 0; level < NestedDepth; level++)
        {
            status |= writer.TryBeginList(_nestedPayloads[level]);
        }

        status |= writer.TryWriteByteString("ab"u8);
        for (int level = 0; level < NestedDepth; level++)
        {
            status |= writer.TryEndList();
        }

        status |= writer.TryComplete(out int written);
        return status == RLPStatus.Success ? written : -1;
    }

    /// <summary>Sizing of lists of 0, 55, 56 and 65,536 payload bytes (read from a field so the JIT cannot fold them); time is per size computation.</summary>
    [Benchmark(OperationsPerInvoke = 4)]
    public int TryGetListLength()
    {
        int[] sizes = _listPayloadSizes;
        RLPEncoder.TryGetListLength(sizes[0], out int a);
        RLPEncoder.TryGetListLength(sizes[1], out int b);
        RLPEncoder.TryGetListLength(sizes[2], out int c);
        RLPEncoder.TryGetListLength(sizes[3], out int d);
        return a + b + c + d;
    }

    [Benchmark]
    public int Writer_LegacyTransaction() => _tx.WriteLegacy(_destination);

    [Benchmark]
    public int Writer_Eip1559WithAccessList() => _tx.WriteEip1559(_destination);

    /// <summary>The <c>Writer_LegacyTransaction</c> write preceded by summing the nine field sizes from the field values.</summary>
    [Benchmark]
    public int Writer_LegacyTransaction_WithSizing() => _tx.WriteLegacyWithSizing(_destination);

    /// <summary>The <c>Writer_Eip1559WithAccessList</c> write preceded by sizing every field, the two key lists, the two entries and the access list.</summary>
    [Benchmark]
    public int Writer_Eip1559WithAccessList_WithSizing() => _tx.WriteEip1559WithSizing(_destination);

    [Benchmark]
    public int Writer_NestedLists32() => WriteNested();
}
