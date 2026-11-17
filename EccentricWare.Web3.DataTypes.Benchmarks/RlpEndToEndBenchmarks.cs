using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.Rlp;
using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// The realistic hot path of a transaction consumer: strict decode, locate the fields (through a range index or one
/// enumerator pass), read every field with the typed adapters, and Keccak-256 the encoding. <c>Hash_Only</c> shows how
/// much of each row is the hash. <c>[GlobalSetup]</c> compares every field read, and the digest, with the fixture values.
/// </summary>
[MemoryDiagnoser]
public class RlpEndToEndBenchmarks
{
    private const int KeccakDigestLength = 32;

    private struct LegacyFields
    {
        public ulong Nonce;
        public uint256 GasPrice;
        public ulong GasLimit;
        public Address To;
        public uint256 Value;
        public FunctionSelector Selector;
        public ulong V;
        public uint256 R;
        public uint256 S;
    }

    private struct Eip1559Fields
    {
        public ulong ChainId;
        public ulong Nonce;
        public uint256 MaxPriorityFee;
        public uint256 MaxFee;
        public ulong GasLimit;
        public Address To;
        public uint256 Value;
        public FunctionSelector Selector;
        public ulong YParity;
        public uint256 R;
        public uint256 S;
        public int AddressCount;
        public int KeyCount;
    }

    private RLPDecodeLimits _limits;
    private RlpTransactionFixture _tx = null!;
    private byte[] _legacy = null!;
    private byte[] _eip1559 = null!;
    private readonly byte[] _digest = new byte[KeccakDigestLength];

    [GlobalSetup]
    public void Setup()
    {
        _limits = RLPDecodeLimits.Default;
        _tx = RlpBenchmarkData.Transactions;
        _legacy = _tx.LegacyEncoded;
        _eip1559 = _tx.Eip1559Encoded;
        byte[] legacyDigest = new byte[KeccakDigestLength];
        byte[] eip1559Digest = new byte[KeccakDigestLength];
        Keccak256.ComputeHash(_legacy, legacyDigest);
        Keccak256.ComputeHash(_eip1559, eip1559Digest);

        LegacyFields indexed = default;
        RlpBenchmarkData.RequireSuccess(ReadLegacyIndexed(ref indexed), "legacy indexed read");
        VerifyLegacy(in indexed, legacyDigest, "indexed");
        LegacyFields enumerated = default;
        RlpBenchmarkData.RequireSuccess(ReadLegacyEnumerated(ref enumerated), "legacy enumerated read");
        VerifyLegacy(in enumerated, legacyDigest, "enumerated");

        Eip1559Fields fields = default;
        var addresses = new Address[RlpTransactionFixture.AccessListEntryCount];
        var keys = new Hash32[RlpTransactionFixture.AccessListEntryCount * RlpTransactionFixture.KeysPerEntry];
        RlpBenchmarkData.RequireSuccess(ReadEip1559Enumerated(ref fields, addresses, keys), "EIP-1559 read");
        if (fields.ChainId != _tx.ChainId || fields.Nonce != _tx.Nonce || fields.MaxPriorityFee != _tx.MaxPriorityFee
            || fields.MaxFee != _tx.MaxFee || fields.GasLimit != _tx.GasLimit || fields.To != _tx.To || fields.Value != _tx.Value
            || fields.Selector.Value != 0x095ea7b3 || fields.YParity != _tx.YParity || fields.R != _tx.R || fields.S != _tx.S
            || fields.AddressCount != addresses.Length || fields.KeyCount != keys.Length)
        {
            throw new InvalidOperationException("EIP-1559 typed fields differ from the fixture.");
        }

        for (int i = 0; i < addresses.Length; i++)
        {
            if (addresses[i] != _tx.AccessListAddresses[i])
            {
                throw new InvalidOperationException($"Access list address {i} differs.");
            }
        }

        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] != _tx.AccessListKeys[i])
            {
                throw new InvalidOperationException($"Access list key {i} differs.");
            }
        }

        RlpBenchmarkData.RequireEqual(eip1559Digest, _digest, "EIP-1559 digest");
    }

    private void VerifyLegacy(in LegacyFields f, byte[] expectedDigest, string what)
    {
        if (f.Nonce != _tx.Nonce || f.GasPrice != _tx.GasPrice || f.GasLimit != _tx.GasLimit || f.To != _tx.To || f.Value != _tx.Value
            || f.Selector.Value != 0x095ea7b3 || f.V != _tx.V || f.R != _tx.R || f.S != _tx.S)
        {
            throw new InvalidOperationException($"Legacy typed fields ({what}) differ from the fixture.");
        }

        RlpBenchmarkData.RequireEqual(expectedDigest, _digest, $"legacy digest ({what})");
    }

    private RLPStatus ReadLegacyIndexed(ref LegacyFields fields)
    {
        RLPStatus status = RLPData.TryDecodeComplete(_legacy, in _limits, out RLPData root, out _);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        Span<RLPRange> workspace = stackalloc RLPRange[RlpTransactionFixture.LegacyFieldCount];
        status = root.TryIndexDirectChildren(workspace, out RLPChildrenIndex index, out _);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        status |= index.TryGetAt(0, out RLPData field);
        status |= field.TryGetUInt64(out fields.Nonce);
        status |= index.TryGetAt(1, out field);
        status |= field.TryGetUInt256(out fields.GasPrice);
        status |= index.TryGetAt(2, out field);
        status |= field.TryGetUInt64(out fields.GasLimit);
        status |= index.TryGetAt(3, out field);
        status |= field.TryGetAddress(out fields.To);
        status |= index.TryGetAt(4, out field);
        status |= field.TryGetUInt256(out fields.Value);
        status |= index.TryGetAt(5, out field);
        status |= field.TryGetFunctionSelector(out fields.Selector);
        status |= index.TryGetAt(6, out field);
        status |= field.TryGetUInt64(out fields.V);
        status |= index.TryGetAt(7, out field);
        status |= field.TryGetUInt256(out fields.R);
        status |= index.TryGetAt(8, out field);
        status |= field.TryGetUInt256(out fields.S);
        Keccak256.ComputeHash(root.Encoded, _digest);
        return status;
    }

    private RLPStatus ReadLegacyEnumerated(ref LegacyFields fields)
    {
        RLPStatus status = RLPData.TryDecodeComplete(_legacy, in _limits, out RLPData root, out _);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        status = root.TryGetChildren(out RLPChildEnumerator children);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        status |= Next(ref children, out RLPData field);
        status |= field.TryGetUInt64(out fields.Nonce);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out fields.GasPrice);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt64(out fields.GasLimit);
        status |= Next(ref children, out field);
        status |= field.TryGetAddress(out fields.To);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out fields.Value);
        status |= Next(ref children, out field);
        status |= field.TryGetFunctionSelector(out fields.Selector);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt64(out fields.V);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out fields.R);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out fields.S);
        if (children.MoveNext())
        {
            status |= RLPStatus.InvalidArgument; // a ninth-plus field would be a schema error
        }

        Keccak256.ComputeHash(root.Encoded, _digest);
        return status;
    }

    private RLPStatus ReadEip1559Enumerated(ref Eip1559Fields fields, Span<Address> addresses, Span<Hash32> keys)
    {
        RLPStatus status = RLPData.TryDecodeComplete(_eip1559, in _limits, out RLPData root, out _);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        status = root.TryGetChildren(out RLPChildEnumerator children);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        status |= Next(ref children, out RLPData field);
        status |= field.TryGetUInt64(out fields.ChainId);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt64(out fields.Nonce);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out fields.MaxPriorityFee);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out fields.MaxFee);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt64(out fields.GasLimit);
        status |= Next(ref children, out field);
        status |= field.TryGetAddress(out fields.To);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out fields.Value);
        status |= Next(ref children, out field);
        status |= field.TryGetFunctionSelector(out fields.Selector);

        // Access list: [[address, [key, ...]], ...]
        status |= Next(ref children, out field);
        status |= field.TryGetChildren(out RLPChildEnumerator entries);
        fields.AddressCount = 0;
        fields.KeyCount = 0;
        while (entries.MoveNext())
        {
            status |= entries.Current.TryGetChildren(out RLPChildEnumerator entry);
            status |= Next(ref entry, out RLPData addressItem);
            status |= addressItem.TryGetAddress(out Address address);
            status |= Next(ref entry, out RLPData keyList);
            status |= keyList.TryGetChildren(out RLPChildEnumerator keyItems);
            if ((uint)fields.AddressCount >= (uint)addresses.Length)
            {
                return RLPStatus.IndexCapacityExceeded;
            }

            addresses[fields.AddressCount++] = address;
            while (keyItems.MoveNext())
            {
                status |= keyItems.Current.TryGetHash32(out Hash32 key);
                if ((uint)fields.KeyCount >= (uint)keys.Length)
                {
                    return RLPStatus.IndexCapacityExceeded;
                }

                keys[fields.KeyCount++] = key;
            }
        }

        status |= Next(ref children, out field);
        status |= field.TryGetUInt64(out fields.YParity);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out fields.R);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out fields.S);
        if (children.MoveNext())
        {
            status |= RLPStatus.InvalidArgument;
        }

        Keccak256.ComputeHash(root.Encoded, _digest);
        return status;
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

    [Benchmark]
    public int Legacy_Index_ReadAllTyped_Keccak()
    {
        LegacyFields fields = default;
        RLPStatus status = ReadLegacyIndexed(ref fields);
        return Fold(status, in fields);
    }

    [Benchmark]
    public int Legacy_Enumerate_ReadAllTyped_Keccak()
    {
        LegacyFields fields = default;
        RLPStatus status = ReadLegacyEnumerated(ref fields);
        return Fold(status, in fields);
    }

    [Benchmark]
    public int Eip1559_Enumerate_ReadAllTyped_AccessList_Keccak()
    {
        Eip1559Fields fields = default;
        Span<Address> addresses = stackalloc Address[RlpTransactionFixture.AccessListEntryCount];
        Span<Hash32> keys = stackalloc Hash32[RlpTransactionFixture.AccessListEntryCount * RlpTransactionFixture.KeysPerEntry];
        RLPStatus status = ReadEip1559Enumerated(ref fields, addresses, keys);
        return (int)status + (int)fields.Nonce + fields.KeyCount + _digest[0];
    }

    /// <summary>Reference row: the cost of the digest alone for the legacy encoding, to subtract from the rows above.</summary>
    [Benchmark]
    public int Hash_Only_Legacy()
    {
        Keccak256.ComputeHash(_legacy, _digest);
        return _digest[0];
    }

    /// <summary>Reference row: strict decode of the legacy transaction alone, without indexing, typed reads or hashing.</summary>
    [Benchmark]
    public RLPStatus Legacy_Decode_Only()
    {
        return RLPData.TryDecodeComplete(_legacy, in _limits, out _, out _);
    }

    private int Fold(RLPStatus status, in LegacyFields fields) =>
        (int)status + (int)(fields.Nonce ^ fields.GasLimit ^ fields.V) + _digest[0];
}
