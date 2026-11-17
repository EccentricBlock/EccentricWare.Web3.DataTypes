using System.Numerics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using EccentricWare.Web3.DataTypes.Rlp;
using Nethereum.RLP;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// Comparison against <c>Nethereum.RLP</c> 7.1.0 (referenced by the benchmark and test projects, never by the library). These rows are NOT a like-for-like
/// strictness comparison: see <c>docs/rlp-performance.md</c> for the validation-semantics differences (the reference's
/// depth limit is fixed at 64, its item budget is configured to this library's 8,192, it reports failures by exception,
/// and it does not offer the library's status codes, error offsets, resource-limit statuses or atomic writes).
/// Do not read a speed ratio as a win unless the work is equivalent: the Nethereum decoder builds an allocating element tree,
/// the library's decoder validates in place. <c>[GlobalSetup]</c> checks that both decode the same input to the same
/// field bytes and that both encoders produce the fixture bytes.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class RlpComparisonBenchmarks
{
    private const int ItemBudget = 8192;
    private const int NestedDepth = 32;

    private RLPDecodeLimits _limits;
    private RlpTransactionFixture _tx = null!;
    private byte[] _legacy = null!;
    private byte[] _eip1559 = null!;
    private byte[] _nested = null!;
    private byte[] _destination = null!;
    private readonly byte[] _nonCanonical = [0x81, 0x05];
    private readonly byte[] _overflow = [0xbf, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff];

    [GlobalSetup]
    public void Setup()
    {
        _limits = RLPDecodeLimits.Default;
        _tx = RlpBenchmarkData.Transactions;
        _legacy = _tx.LegacyEncoded;
        _eip1559 = _tx.Eip1559Encoded;
        _nested = RlpBenchmarkData.NestedLists(NestedDepth);
        _destination = new byte[2048];

        // Decode agreement: the Nethereum tree holds exactly the fixture field bytes.
        var tree = (RLPCollection)RLP.Decode(_legacy, ItemBudget);
        if (tree.Count != RlpTransactionFixture.LegacyFieldCount)
        {
            throw new InvalidOperationException($"Nethereum decoded {tree.Count} fields.");
        }

        for (int i = 0; i < tree.Count; i++)
        {
            RlpBenchmarkData.RequireEqual(_tx.LegacyFieldBytes[i], tree[i].RLPData ?? [], $"Nethereum field {i}");
        }

        RlpBenchmarkData.RequireDecodes(_nested, RLPKind.List, "nested 32");
        _ = RLP.Decode(_nested, ItemBudget);
        _ = RLP.Decode(_eip1559, ItemBudget);

        // The typed-read rows return the same values from both libraries.
        VerifyNethereumReader();
        RlpBenchmarkData.RequireSuccess(RLPData.TryDecodeComplete(_legacy, in _limits, out RLPData root, out _), "legacy decode");
        if (Ours_Legacy_Enumerate_ReadAllTyped() == -1 || NethereumReader_Legacy_ReadAllTyped() == -1)
        {
            throw new InvalidOperationException("Typed legacy read failed.");
        }

        // Encode agreement.
        if (Ours_Encode_Legacy() != _legacy.Length)
        {
            throw new InvalidOperationException("Library legacy encode length differs.");
        }

        RlpBenchmarkData.RequireEqual(_legacy, _destination.AsSpan(0, _legacy.Length), "library legacy encode");
        Array.Clear(_destination);
        if (Ours_Encode_Legacy_WithSizing() != _legacy.Length)
        {
            throw new InvalidOperationException("Library legacy encode with sizing: length differs.");
        }

        RlpBenchmarkData.RequireEqual(_legacy, _destination.AsSpan(0, _legacy.Length), "library legacy encode with sizing");
        RlpBenchmarkData.RequireEqual(_legacy, EncodeWithNethereum(), "Nethereum legacy encode");

        // Both reject the hostile inputs (the reference by exception).
        if (RLPData.TryDecodeComplete(_nonCanonical, in _limits, out _, out _) == RLPStatus.Success
            || RLPData.TryDecodeComplete(_overflow, in _limits, out _, out _) == RLPStatus.Success)
        {
            throw new InvalidOperationException("The library accepted a hostile input.");
        }

        if (!NethereumRejects(_nonCanonical) || !NethereumRejects(_overflow))
        {
            throw new InvalidOperationException("Nethereum accepted a hostile input.");
        }
    }

    private void VerifyNethereumReader()
    {
        var reader = new RlpReader(_legacy);
        RlpListScope scope = reader.ReadListHeader();
        ulong nonce = reader.ReadUInt64();
        BigInteger gasPrice = reader.ReadBigInteger();
        ulong gasLimit = reader.ReadUInt64();
        byte[] to = reader.ReadBytes();
        BigInteger value = reader.ReadBigInteger();
        byte[] data = reader.ReadBytes();
        ulong v = reader.ReadUInt64();
        BigInteger r = reader.ReadBigInteger();
        BigInteger s = reader.ReadBigInteger();
        reader.EnsureEnd(scope);
        byte[][] fields = _tx.LegacyFieldBytes;
        if (nonce != _tx.Nonce || gasLimit != _tx.GasLimit || v != _tx.V
            || gasPrice != Big(fields[1]) || value != Big(fields[4]) || r != Big(fields[7]) || s != Big(fields[8])
            || !to.AsSpan().SequenceEqual(fields[3]) || !data.AsSpan().SequenceEqual(fields[5]))
        {
            throw new InvalidOperationException("Nethereum reader values differ from the fixture.");
        }
    }

    private static BigInteger Big(byte[] magnitude) => new(magnitude, isUnsigned: true, isBigEndian: true);

    private static bool NethereumRejects(byte[] input)
    {
        try
        {
            _ = RLP.Decode(input, ItemBudget);
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private byte[] EncodeWithNethereum()
    {
        byte[][] fields = _tx.LegacyFieldBytes;
        var encoded = new byte[fields.Length][];
        for (int i = 0; i < fields.Length; i++)
        {
            encoded[i] = RLP.EncodeElement(fields[i]);
        }

        return RLP.EncodeList(encoded);
    }

    // ---- strict decode (structure only) -------------------------------------------------------------------------

    [Benchmark, BenchmarkCategory("Decode")]
    public int Ours_Decode_Legacy()
    {
        RLPStatus status = RLPData.TryDecodeComplete(_legacy, in _limits, out RLPData root, out _);
        return status == RLPStatus.Success ? root.PayloadLength : -1;
    }

    [Benchmark, BenchmarkCategory("Decode")]
    public int Nethereum_Decode_Legacy() => RLP.Decode(_legacy, ItemBudget).RLPData?.Length ?? 0;

    [Benchmark, BenchmarkCategory("Decode")]
    public int Ours_Decode_Eip1559()
    {
        RLPStatus status = RLPData.TryDecodeComplete(_eip1559, in _limits, out RLPData root, out _);
        return status == RLPStatus.Success ? root.PayloadLength : -1;
    }

    [Benchmark, BenchmarkCategory("Decode")]
    public int Nethereum_Decode_Eip1559() => ((RLPCollection)RLP.Decode(_eip1559, ItemBudget)).Count;

    [Benchmark, BenchmarkCategory("Decode")]
    public int Ours_Decode_Nested32()
    {
        RLPStatus status = RLPData.TryDecodeComplete(_nested, in _limits, out RLPData root, out _);
        return status == RLPStatus.Success ? root.PayloadLength : -1;
    }

    [Benchmark, BenchmarkCategory("Decode")]
    public int Nethereum_Decode_Nested32() => ((RLPCollection)RLP.Decode(_nested, ItemBudget)).Count;

    // ---- decode and read all nine fields ------------------------------------------------------------------------

    /// <summary>Decode, then touch every field's bytes (length and first byte): no typed conversion on either side.</summary>
    [Benchmark, BenchmarkCategory("ReadFields")]
    public int Ours_Legacy_Enumerate_TouchPayloads()
    {
        if (RLPData.TryDecodeComplete(_legacy, in _limits, out RLPData root, out _) != RLPStatus.Success
            || root.TryGetChildren(out RLPChildEnumerator children) != RLPStatus.Success)
        {
            return -1;
        }

        int total = 0;
        while (children.MoveNext())
        {
            ReadOnlySpan<byte> payload = children.Current.Payload;
            total += payload.Length + (payload.IsEmpty ? 0 : payload[0]);
        }

        return total;
    }

    [Benchmark, BenchmarkCategory("ReadFields")]
    public int Nethereum_Legacy_Tree_TouchPayloads()
    {
        var tree = (RLPCollection)RLP.Decode(_legacy, ItemBudget);
        int total = 0;
        for (int i = 0; i < tree.Count; i++)
        {
            byte[]? bytes = tree[i].RLPData;
            total += (bytes?.Length ?? 0) + (bytes is { Length: > 0 } ? bytes[0] : 0);
        }

        return total;
    }

    /// <summary>Decode and read the nine fields as typed values: library <c>uint256</c>/<c>Address</c> (no allocation) against <c>RlpReader</c> with <c>BigInteger</c> and <c>byte[]</c> results.</summary>
    [Benchmark, BenchmarkCategory("ReadFields")]
    public int Ours_Legacy_Enumerate_ReadAllTyped()
    {
        if (RLPData.TryDecodeComplete(_legacy, in _limits, out RLPData root, out _) != RLPStatus.Success
            || root.TryGetChildren(out RLPChildEnumerator children) != RLPStatus.Success)
        {
            return -1;
        }

        RLPStatus status = Next(ref children, out RLPData field);
        status |= field.TryGetUInt64(out ulong nonce);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out uint256 gasPrice);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt64(out ulong gasLimit);
        status |= Next(ref children, out field);
        status |= field.TryGetAddress(out Address to);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out uint256 value);
        status |= Next(ref children, out field);
        ReadOnlySpan<byte> data = field.Payload;
        status |= Next(ref children, out field);
        status |= field.TryGetUInt64(out ulong v);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out uint256 r);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out uint256 s);
        if (children.MoveNext() || status != RLPStatus.Success)
        {
            return -1;
        }

        return (int)(nonce ^ gasLimit ^ v) + data.Length + (gasPrice.IsZero ? 0 : 1) + (value.IsZero ? 0 : 1) + (to.IsZero ? 0 : 1) + (r.IsZero ? 0 : 1) + (s.IsZero ? 0 : 1);
    }

    [Benchmark, BenchmarkCategory("ReadFields")]
    public int NethereumReader_Legacy_ReadAllTyped()
    {
        var reader = new RlpReader(_legacy);
        RlpListScope scope = reader.ReadListHeader();
        ulong nonce = reader.ReadUInt64();
        BigInteger gasPrice = reader.ReadBigInteger();
        ulong gasLimit = reader.ReadUInt64();
        byte[] to = reader.ReadBytes();
        BigInteger value = reader.ReadBigInteger();
        byte[] data = reader.ReadBytes();
        ulong v = reader.ReadUInt64();
        BigInteger r = reader.ReadBigInteger();
        BigInteger s = reader.ReadBigInteger();
        reader.EnsureEnd(scope);
        return (int)(nonce ^ gasLimit ^ v) + data.Length + (gasPrice.IsZero ? 0 : 1) + (value.IsZero ? 0 : 1) + (to.Length == 20 ? 1 : 0) + (r.IsZero ? 0 : 1) + (s.IsZero ? 0 : 1);
    }

    private static RLPStatus Next(ref RLPChildEnumerator children, out RLPData child)
    {
        if (children.MoveNext())
        {
            child = children.Current;
            return RLPStatus.Success;
        }

        child = default;
        return RLPStatus.TruncatedInput;
    }

    // ---- encode the legacy transaction ----------------------------------------------------------------------------

    [Benchmark, BenchmarkCategory("Encode")]
    public int Ours_Encode_Legacy() => _tx.WriteLegacy(_destination);

    /// <summary>
    /// <c>Ours_Encode_Legacy</c> preceded by computing the list length from the field values with the public sizing helpers.
    /// Compare this row, not the constant-length row, with <c>Nethereum_Encode_Legacy</c>, which sizes inside the timed call
    /// (but takes field bytes that were trimmed to minimal magnitudes in setup, so its typed-value conversion is untimed).
    /// </summary>
    [Benchmark, BenchmarkCategory("Encode")]
    public int Ours_Encode_Legacy_WithSizing() => _tx.WriteLegacyWithSizing(_destination);

    /// <summary>The reference API takes pre-trimmed field bytes and returns fresh arrays: one per field plus the list.</summary>
    [Benchmark, BenchmarkCategory("Encode")]
    public int Nethereum_Encode_Legacy() => EncodeWithNethereum().Length;

    // ---- rejection (the reference rejects by throwing; exception cost is part of its API, not like-for-like) -------

    [Benchmark, BenchmarkCategory("Reject")]
    public int Ours_Reject_NonCanonical()
    {
        RLPStatus status = RLPData.TryDecodeComplete(_nonCanonical, in _limits, out _, out RLPError error);
        return (int)status + error.Offset;
    }

    [Benchmark, BenchmarkCategory("Reject")]
    public bool Nethereum_Reject_NonCanonical() => NethereumRejects(_nonCanonical);

    [Benchmark, BenchmarkCategory("Reject")]
    public int Ours_Reject_Overflow_bf_ff8()
    {
        RLPStatus status = RLPData.TryDecodeComplete(_overflow, in _limits, out _, out RLPError error);
        return (int)status + error.Offset;
    }

    [Benchmark, BenchmarkCategory("Reject")]
    public bool Nethereum_Reject_Overflow_bf_ff8() => NethereumRejects(_overflow);
}
