using System.Numerics;
using EccentricWare.Web3.DataTypes.Rlp;
using EccentricWare.Web3.DataTypes.Utils;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.TypedAdapterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Transaction-level contract tests (spec 11, 12.5): RLP-shaped EVM transactions are built with the writer and the typed
/// adapters, decoded, indexed and read back with the typed accessors, and hostile variants are rejected with the right
/// status. The codec itself is schema-less: the field-count and field-type checks below live in this test's schema
/// reader, the shape a real transaction adapter must have (spec 11.1).
/// </summary>
[TestClass]
public sealed class EvmTransactionIntegrationTests
{
    private const int LegacySignedFieldCount = 9;
    private const int Eip1559FieldCount = 12;
    private const byte DynamicFeeTransactionType = 0x02;

    // The EIP-155 example transaction (nonce 9, 20 gwei, 21000 gas, 10^18 wei to 0x3535...35, chain id 1).
    private const string Eip155SigningPreimageHex = "ec 09 85 04a817c800 82 5208 94 3535353535353535353535353535353535353535 88 0de0b6b3a7640000 80 01 80 80";
    private const string Eip155SigningHashHex = "daf5a779ae972f972197303d7b574746c7ef83eadac0f2791ad23db92e4c8e53";
    private const string Eip155SignedTransactionHex =
        "f86c 09 85 04a817c800 82 5208 94 3535353535353535353535353535353535353535 88 0de0b6b3a7640000 80 25 " +
        "a0 28ef61340bd939bc2195fe537567866003e1a15d3c71ff63e1590620aa636276 " +
        "a0 67cbe9d8997f761aecb703304b3800ccf555c9f3dc64214b297fb1966a3b6d83";

    // ------------------------------------------------------------------ test-local schema reader

    private enum ReadOutcome
    {
        Ok,
        RlpRejected,
        NotAList,
        FieldCountMismatch,
        FieldRejected,
    }

    private sealed class ParsedTransaction
    {
        public ReadOutcome Outcome;
        public RLPStatus Status;
        public int FieldIndex = -1;
        public int ChildCount;

        public ulong ChainId;
        public ulong Nonce;
        public uint256 GasPrice;
        public uint256 MaxPriorityFee;
        public uint256 MaxFee;
        public ulong GasLimit;
        public bool IsContractCreation;
        public Address To;
        public uint256 Value;
        public byte[] Data = [];
        public bool HasSelector;
        public FunctionSelector Selector;
        public List<(Address Address, List<Hash32> Keys)> AccessList = [];
        public Signature Signature;
        public ulong? LegacyChainId;
        public Hash32 PayloadHash;
    }

    private static ParsedTransaction Reject(ReadOutcome outcome, RLPStatus status, int fieldIndex, int childCount = 0) =>
        new() { Outcome = outcome, Status = status, FieldIndex = fieldIndex, ChildCount = childCount };

    /// <summary>Decodes an RLP-backed transaction and requires a list root; the caller indexes it and enforces the field count.</summary>
    private static ParsedTransaction? DecodeRoot(byte[] rlp, out RLPData root)
    {
        var limits = RLPDecodeLimits.Default;
        var status = RLPData.TryDecodeComplete(rlp, in limits, out root, out _);
        if (status != RLPStatus.Success)
        {
            return Reject(ReadOutcome.RlpRejected, status, -1);
        }

        return root.Kind == RLPKind.List ? null : Reject(ReadOutcome.NotAList, RLPStatus.WrongKind, -1);
    }

    private static bool HasExpectedFieldCount(RLPStatus status, int count, int required, int expected, out ParsedTransaction? failure)
    {
        failure = status == RLPStatus.IndexCapacityExceeded || (status == RLPStatus.Success && count != expected)
            ? Reject(ReadOutcome.FieldCountMismatch, status, -1, status == RLPStatus.Success ? count : required)
            : null;
        return failure is null && status == RLPStatus.Success;
    }

    private static ParsedTransaction ReadEip1559(byte[] rlp)
    {
        var rootFailure = DecodeRoot(rlp, out var root);
        if (rootFailure is not null)
        {
            return rootFailure;
        }

        Span<RLPRange> workspace = stackalloc RLPRange[16];
        var indexStatus = root.TryIndexDirectChildren(workspace, out var index, out var required);
        if (!HasExpectedFieldCount(indexStatus, index.Count, required, Eip1559FieldCount, out var failure))
        {
            return failure ?? Reject(ReadOutcome.RlpRejected, indexStatus, -1);
        }

        var tx = new ParsedTransaction { Outcome = ReadOutcome.Ok, ChildCount = index.Count };
        RLPStatus status;

        status = index.TryGetAt(0, out var field0);
        if (status == RLPStatus.Success) { status = field0.TryGetUInt64(out tx.ChainId); }
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 0); }

        status = index.TryGetAt(1, out var field1);
        if (status == RLPStatus.Success) { status = field1.TryGetUInt64(out tx.Nonce); }
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 1); }

        status = index.TryGetAt(2, out var field2);
        if (status == RLPStatus.Success) { status = field2.TryGetUInt256(out tx.MaxPriorityFee); }
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 2); }

        status = index.TryGetAt(3, out var field3);
        if (status == RLPStatus.Success) { status = field3.TryGetUInt256(out tx.MaxFee); }
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 3); }

        status = index.TryGetAt(4, out var field4);
        if (status == RLPStatus.Success) { status = field4.TryGetUInt64(out tx.GasLimit); }
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 4); }

        status = ReadDestination(index, 5, tx);
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 5); }

        status = index.TryGetAt(6, out var field6);
        if (status == RLPStatus.Success) { status = field6.TryGetUInt256(out tx.Value); }
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 6); }

        status = ReadData(index, 7, tx);
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 7); }

        status = ReadAccessList(index, 8, tx);
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 8); }

        status = ReadSignature(index, 9, tx, legacy: false);
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 9); }

        tx.PayloadHash = HashOf(root.Encoded);
        return tx;
    }

    private static ParsedTransaction ReadLegacySigned(byte[] rlp)
    {
        var rootFailure = DecodeRoot(rlp, out var root);
        if (rootFailure is not null)
        {
            return rootFailure;
        }

        Span<RLPRange> workspace = stackalloc RLPRange[16];
        var indexStatus = root.TryIndexDirectChildren(workspace, out var index, out var required);
        if (!HasExpectedFieldCount(indexStatus, index.Count, required, LegacySignedFieldCount, out var failure))
        {
            return failure ?? Reject(ReadOutcome.RlpRejected, indexStatus, -1);
        }

        var tx = new ParsedTransaction { Outcome = ReadOutcome.Ok, ChildCount = index.Count };
        RLPStatus status;

        status = index.TryGetAt(0, out var field0);
        if (status == RLPStatus.Success) { status = field0.TryGetUInt64(out tx.Nonce); }
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 0); }

        status = index.TryGetAt(1, out var field1);
        if (status == RLPStatus.Success) { status = field1.TryGetUInt256(out tx.GasPrice); }
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 1); }

        status = index.TryGetAt(2, out var field2);
        if (status == RLPStatus.Success) { status = field2.TryGetUInt64(out tx.GasLimit); }
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 2); }

        status = ReadDestination(index, 3, tx);
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 3); }

        status = index.TryGetAt(4, out var field4);
        if (status == RLPStatus.Success) { status = field4.TryGetUInt256(out tx.Value); }
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 4); }

        status = ReadData(index, 5, tx);
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 5); }

        status = ReadSignature(index, 6, tx, legacy: true);
        if (status != RLPStatus.Success) { return Reject(ReadOutcome.FieldRejected, status, 6); }

        tx.PayloadHash = HashOf(root.Encoded);
        return tx;
    }

    private static RLPStatus ReadDestination(RLPChildrenIndex index, int ordinal, ParsedTransaction tx)
    {
        var status = index.TryGetAt(ordinal, out var field);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        status = field.TryGetOptionalAddress(out tx.To, out var isEmpty);
        tx.IsContractCreation = isEmpty;
        return status;
    }

    private static RLPStatus ReadData(RLPChildrenIndex index, int ordinal, ParsedTransaction tx)
    {
        var status = index.TryGetAt(ordinal, out var field);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        if (field.Kind != RLPKind.ByteString)
        {
            return RLPStatus.WrongKind;
        }

        tx.Data = field.Payload.ToArray();
        tx.HasSelector = field.TryGetFunctionSelector(out tx.Selector) == RLPStatus.Success;
        return RLPStatus.Success;
    }

    /// <summary>accessList = [ [address, [hash32, ...]], ... ] walked with the child enumerator.</summary>
    private static RLPStatus ReadAccessList(RLPChildrenIndex index, int ordinal, ParsedTransaction tx)
    {
        var status = index.TryGetAt(ordinal, out var list);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        status = list.TryGetChildren(out var entries);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        while (entries.MoveNext())
        {
            var entry = entries.Current;
            status = entry.TryGetChildren(out var parts);
            if (status != RLPStatus.Success)
            {
                return status;
            }

            if (!parts.MoveNext())
            {
                return RLPStatus.InvalidArgument;
            }

            status = parts.Current.TryGetAddress(out var address);
            if (status != RLPStatus.Success)
            {
                return status;
            }

            if (!parts.MoveNext())
            {
                return RLPStatus.InvalidArgument;
            }

            status = parts.Current.TryGetChildren(out var keys);
            if (status != RLPStatus.Success)
            {
                return status;
            }

            var keyList = new List<Hash32>();
            while (keys.MoveNext())
            {
                status = keys.Current.TryGetHash32(out var key);
                if (status != RLPStatus.Success)
                {
                    return status;
                }

                keyList.Add(key);
            }

            if (parts.MoveNext())
            {
                return RLPStatus.InvalidArgument;
            }

            tx.AccessList.Add((address, keyList));
        }

        return RLPStatus.Success;
    }

    /// <summary>Reads (v-or-yParity, r, s) at <paramref name="firstOrdinal"/>..+2: legacy <c>v</c> with its chain id, or a typed y parity.</summary>
    private static RLPStatus ReadSignature(RLPChildrenIndex index, int firstOrdinal, ParsedTransaction tx, bool legacy)
    {
        var status = index.TryGetAt(firstOrdinal, out var v);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        status = index.TryGetAt(firstOrdinal + 1, out var r);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        status = index.TryGetAt(firstOrdinal + 2, out var s);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        return legacy
            ? RLPTypedExtensions.TryGetEvmLegacySignature(v, r, s, out tx.Signature, out tx.LegacyChainId)
            : RLPTypedExtensions.TryGetEvmYParitySignature(v, r, s, out tx.Signature);
    }

    private static Hash32 HashOf(ReadOnlySpan<byte> bytes)
    {
        Span<byte> digest = stackalloc byte[32];
        Keccak256.ComputeHash(bytes, digest);
        return new Hash32(digest);
    }

    // ------------------------------------------------------------------ transaction builders

    private sealed record TransactionInput(
        ulong ChainId,
        ulong Nonce,
        BigInteger MaxPriorityFee,
        BigInteger MaxFee,
        ulong GasLimit,
        byte[]? To,
        BigInteger Value,
        byte[] Data,
        (byte[] Address, byte[][] Keys)[] AccessList,
        ulong YParity,
        BigInteger R,
        BigInteger S);

    private static TransactionInput SampleEip1559() => new(
        ChainId: 1,
        Nonce: 0x1234,
        MaxPriorityFee: 1_500_000_000,
        MaxFee: 100_000_000_000,
        GasLimit: 120_000,
        To: Pattern(20, 0xa1),
        Value: BigInteger.Pow(10, 18),
        Data: RlpReference.Concatenate(H("a9059cbb"), new byte[12], Pattern(20, 0xb2), new byte[31], [0x01]),
        AccessList:
        [
            (Pattern(20, 0xc3), [Pattern(32, 0x01), Pattern(32, 0x02)]),
            (Pattern(20, 0xd4), []),
            (Pattern(20, 0xe5), [Pattern(32, 0xff)]),
        ],
        YParity: 1,
        R: (BigInteger.One << 255) + 5,
        S: (BigInteger.One << 240) + 7);

    /// <summary>The oracle tree of the EIP-1559 payload (type byte not part of the RLP document).</summary>
    private static RlpList Eip1559Tree(TransactionInput tx) => (RlpList)RlpNode.ListOf(
        RlpNode.Bytes(Minimal(tx.ChainId)),
        RlpNode.Bytes(Minimal(tx.Nonce)),
        RlpNode.Bytes(Minimal(tx.MaxPriorityFee)),
        RlpNode.Bytes(Minimal(tx.MaxFee)),
        RlpNode.Bytes(Minimal(tx.GasLimit)),
        RlpNode.Bytes(tx.To ?? []),
        RlpNode.Bytes(Minimal(tx.Value)),
        RlpNode.Bytes(tx.Data),
        RlpNode.ListOf(tx.AccessList.Select(entry =>
            RlpNode.ListOf(RlpNode.Bytes(entry.Address), RlpNode.ListOf(entry.Keys.Select(k => RlpNode.Bytes(k)))))),
        RlpNode.Bytes(Minimal(tx.YParity)),
        RlpNode.Bytes(Minimal(tx.R)),
        RlpNode.Bytes(Minimal(tx.S)));

    private static byte[] WriteEip1559(TransactionInput tx)
    {
        var tree = Eip1559Tree(tx);
        var expected = RlpReference.Encode(tree);
        var destination = Filled(expected.Length + 8);
        var writer = new RLPWriter(destination, new int[8]);

        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(RlpReference.PayloadLength(tree)));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(tx.ChainId));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(tx.Nonce));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(ToUInt256(tx.MaxPriorityFee)));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(ToUInt256(tx.MaxFee)));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(tx.GasLimit));
        Assert.AreEqual(RLPStatus.Success, tx.To is null ? writer.TryWriteByteString(default) : writer.TryWriteAddress(Address.FromEvmBytes(tx.To)));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(ToUInt256(tx.Value)));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(tx.Data));

        var accessListNode = (RlpList)tree.Items[8];
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(RlpReference.PayloadLength(accessListNode)));
        for (var i = 0; i < tx.AccessList.Length; i++)
        {
            var entryNode = (RlpList)accessListNode.Items[i];
            Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(RlpReference.PayloadLength(entryNode)));
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteAddress(Address.FromEvmBytes(tx.AccessList[i].Address)));
            Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(RlpReference.PayloadLength((RlpList)entryNode.Items[1])));
            foreach (var key in tx.AccessList[i].Keys)
            {
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteHash32(new Hash32(key)));
            }

            Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
            Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        }

        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());

        // The signature is three integers, written one item each; the y parity of a typed transaction is 0 or 1.
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteEvmYParity((byte)tx.YParity));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(ToUInt256(tx.R)));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(ToUInt256(tx.S)));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));

        Assert.AreEqual(expected.Length, written);
        CollectionAssert.AreEqual(expected, destination.AsSpan(0, written).ToArray(), "writer + adapters equal the oracle");
        Assert.IsTrue(IsFilled(destination.AsSpan(written)));
        return expected;
    }

    private static uint256 ToUInt256(BigInteger value) => new(Padded32(value));

    /// <summary>Builds a 9-field legacy signed transaction through the writer and adapters (legacy <c>v</c> from the y parity and the optional chain id).</summary>
    private static byte[] WriteLegacySigned(ulong nonce, BigInteger gasPrice, ulong gasLimit, byte[]? to, BigInteger value, byte[] data, byte yParity, ulong? chainId, BigInteger r, BigInteger s)
    {
        var tree = (RlpList)RlpNode.ListOf(
            RlpNode.Bytes(Minimal(nonce)),
            RlpNode.Bytes(Minimal(gasPrice)),
            RlpNode.Bytes(Minimal(gasLimit)),
            RlpNode.Bytes(to ?? []),
            RlpNode.Bytes(Minimal(value)),
            RlpNode.Bytes(data),
            RlpNode.Bytes(Minimal(EvmSignatureOracle.LegacyV(yParity, chainId))),
            RlpNode.Bytes(Minimal(r)),
            RlpNode.Bytes(Minimal(s)));
        var expected = RlpReference.Encode(tree);
        var destination = Filled(expected.Length);
        var writer = new RLPWriter(destination, new int[2]);

        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(RlpReference.PayloadLength(tree)));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(nonce));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(ToUInt256(gasPrice)));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(gasLimit));
        Assert.AreEqual(RLPStatus.Success, to is null ? writer.TryWriteByteString(default) : writer.TryWriteAddress(Address.FromEvmBytes(to)));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(ToUInt256(value)));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(data));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteEvmLegacyV(yParity, chainId));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(ToUInt256(r)));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(ToUInt256(s)));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(expected.Length, written);
        CollectionAssert.AreEqual(expected, destination, "writer + adapters equal the oracle");
        return destination;
    }

    // ------------------------------------------------------------------ (1) EIP-1559 and legacy round trips

    [TestMethod]
    public void Eip1559_BuildWithAdapters_DecodeIndexAndReadEveryFieldBack()
    {
        var input = SampleEip1559();
        var rlp = WriteEip1559(input);

        var parsed = ReadEip1559(rlp);

        Assert.AreEqual(ReadOutcome.Ok, parsed.Outcome, $"{parsed.Status} at field {parsed.FieldIndex}");
        Assert.AreEqual(Eip1559FieldCount, parsed.ChildCount);
        Assert.AreEqual(input.ChainId, parsed.ChainId);
        Assert.AreEqual(input.Nonce, parsed.Nonce);
        Assert.AreEqual(ToUInt256(input.MaxPriorityFee), parsed.MaxPriorityFee);
        Assert.AreEqual(ToUInt256(input.MaxFee), parsed.MaxFee);
        Assert.AreEqual(input.GasLimit, parsed.GasLimit);
        Assert.IsFalse(parsed.IsContractCreation);
        Assert.AreEqual(Address.FromEvmBytes(input.To!), parsed.To);
        Assert.AreEqual(ToUInt256(input.Value), parsed.Value);
        CollectionAssert.AreEqual(input.Data, parsed.Data);
        Assert.IsTrue(parsed.HasSelector);
        Assert.AreEqual(0xa9059cbbU, parsed.Selector.Value);

        Assert.HasCount(input.AccessList.Length, parsed.AccessList);
        for (var i = 0; i < input.AccessList.Length; i++)
        {
            Assert.AreEqual(Address.FromEvmBytes(input.AccessList[i].Address), parsed.AccessList[i].Address, $"entry {i}");
            Assert.HasCount(input.AccessList[i].Keys.Length, parsed.AccessList[i].Keys);
            for (var k = 0; k < input.AccessList[i].Keys.Length; k++)
            {
                Assert.AreEqual(new Hash32(input.AccessList[i].Keys[k]), parsed.AccessList[i].Keys[k], $"entry {i} key {k}");
            }
        }

        Assert.AreEqual(SignatureType.Evm, parsed.Signature.Type);
        Assert.AreEqual((byte)input.YParity, parsed.Signature.V);
        Assert.AreEqual(new Hash32(Padded32(input.R)), parsed.Signature.R);
        Assert.AreEqual(new Hash32(Padded32(input.S)), parsed.Signature.S);
    }

    [TestMethod]
    public void Eip1559_RealChainIds_BothYParities_RoundTripThroughTheTypedFields()
    {
        foreach (var (name, chain) in EvmSignatureOracle.RealChainIds)
        {
            foreach (var parity in new ulong[] { 0, 1 })
            {
                var input = SampleEip1559() with { ChainId = chain, YParity = parity };
                var parsed = ReadEip1559(WriteEip1559(input));

                Assert.AreEqual(ReadOutcome.Ok, parsed.Outcome, $"{name} parity {parity}: {parsed.Status} at field {parsed.FieldIndex}");
                Assert.AreEqual(chain, parsed.ChainId, name);
                Assert.AreEqual((byte)parity, parsed.Signature.V, name);
            }
        }
    }

    [TestMethod]
    public void Hostile_Eip1559YParityOutOfRange_IsInvalidSignatureV_AtTheSignatureField()
    {
        // 27 and 28 are legacy v values, not y parities; 256 is a canonical integer outside the domain.
        foreach (var parity in new ulong[] { 2, 27, 28, 255, 256 })
        {
            var rlp = RlpReference.Encode(Eip1559Tree(SampleEip1559() with { YParity = parity }));
            var parsed = ReadEip1559(rlp);
            Assert.AreEqual(ReadOutcome.FieldRejected, parsed.Outcome, $"parity {parity}");
            Assert.AreEqual(RLPStatus.InvalidSignatureV, parsed.Status, $"parity {parity}");
            Assert.AreEqual(9, parsed.FieldIndex, $"parity {parity}");
        }
    }

    [TestMethod]
    public void Eip1559_ContractCreation_HasAnEmptyToAndNoSelectorForEmptyData()
    {
        var input = SampleEip1559() with { To = null, Data = [], AccessList = [] };
        var rlp = WriteEip1559(input);

        var parsed = ReadEip1559(rlp);

        Assert.AreEqual(ReadOutcome.Ok, parsed.Outcome, $"{parsed.Status} at field {parsed.FieldIndex}");
        Assert.IsTrue(parsed.IsContractCreation);
        Assert.AreEqual(default, parsed.To);
        Assert.IsFalse(parsed.HasSelector);
        Assert.IsEmpty(parsed.Data);
        Assert.IsEmpty(parsed.AccessList);
    }

    [TestMethod]
    public void Eip1559_TransactionHash_IsKeccakOfTheTypeByteAndTheRlpPayload()
    {
        var rlp = WriteEip1559(SampleEip1559());
        var parsed = ReadEip1559(rlp);
        Assert.AreEqual(ReadOutcome.Ok, parsed.Outcome);

        // Hash of the RLP document alone (root.Encoded).
        Span<byte> expectedPayloadHash = stackalloc byte[32];
        Keccak256.ComputeHash(rlp, expectedPayloadHash);
        Assert.AreEqual(new Hash32(expectedPayloadHash), parsed.PayloadHash);

        // The EIP-2718 transaction hash covers the type byte too; the codec never sees it.
        var envelope = new byte[rlp.Length + 1];
        envelope[0] = DynamicFeeTransactionType;
        rlp.CopyTo(envelope, 1);
        Span<byte> transactionHash = stackalloc byte[32];
        Keccak256.ComputeHash(envelope, transactionHash);
        Assert.AreNotEqual(parsed.PayloadHash, new Hash32(transactionHash), "the type byte changes the hash");

        // Decoding the envelope minus its first byte gives the same document, with offsets shifted by one in the envelope.
        var limits = RLPDecodeLimits.Default;
        Assert.AreEqual(RLPStatus.Success, RLPData.TryDecodeComplete(envelope.AsSpan(1), in limits, out var root, out _));
        Assert.AreEqual(0, root.EncodedRange.Offset);
        Assert.AreEqual(rlp.Length, root.EncodedLength);
        Assert.IsTrue(envelope.AsSpan(1 + root.EncodedRange.Offset, root.EncodedLength).SequenceEqual(root.Encoded));
    }

    [TestMethod]
    public void EnvelopeWithTheTypeByteIncluded_IsNotAnRlpDocument()
    {
        // 0x02 followed by a list is a literal 02 plus trailing bytes: rejected, never silently decoded.
        var rlp = WriteEip1559(SampleEip1559());
        var envelope = new byte[rlp.Length + 1];
        envelope[0] = DynamicFeeTransactionType;
        rlp.CopyTo(envelope, 1);

        var parsed = ReadEip1559(envelope);

        Assert.AreEqual(ReadOutcome.RlpRejected, parsed.Outcome);
        Assert.AreEqual(RLPStatus.TrailingData, parsed.Status);
    }

    [TestMethod]
    public void LegacySigned_NineFields_RoundTripThroughAdapters()
    {
        var to = Pattern(20, 0x35);
        var data = H("a9059cbb 0000000000000000000000000000000000000000000000000000000000000001");
        var r = (BigInteger.One << 250) + 3;
        var s = (BigInteger.One << 200) + 9;
        var rlp = WriteLegacySigned(9, 20_000_000_000, 21_000, to, BigInteger.Pow(10, 18), data, 0, 1, r, s);

        var parsed = ReadLegacySigned(rlp);

        Assert.AreEqual(ReadOutcome.Ok, parsed.Outcome, $"{parsed.Status} at field {parsed.FieldIndex}");
        Assert.AreEqual(LegacySignedFieldCount, parsed.ChildCount);
        Assert.AreEqual(9UL, parsed.Nonce);
        Assert.AreEqual(new uint256(20_000_000_000UL), parsed.GasPrice);
        Assert.AreEqual(21_000UL, parsed.GasLimit);
        Assert.AreEqual(Address.FromEvmBytes(to), parsed.To);
        Assert.AreEqual(new uint256(1_000_000_000_000_000_000UL), parsed.Value);
        CollectionAssert.AreEqual(data, parsed.Data);
        Assert.AreEqual(0xa9059cbbU, parsed.Selector.Value);
        Assert.AreEqual((byte)0, parsed.Signature.V, "v = 37 is parity 0");
        Assert.AreEqual(1UL, parsed.LegacyChainId, "v = 37 is chain id 1");
        Assert.AreEqual(new Hash32(Padded32(r)), parsed.Signature.R);
        Assert.AreEqual(new Hash32(Padded32(s)), parsed.Signature.S);
    }

    [TestMethod]
    public void LegacySigned_RealChainIds_WriteAndReadBackWithTheChainIdAndParity()
    {
        // Chain ids above 110 (Polygon, Base, Arbitrum, Plume, ...) produce a legacy v above 255; the adapter once rejected them.
        var to = Pattern(20, 0x35);
        var r = (BigInteger.One << 250) + 3;
        var s = (BigInteger.One << 200) + 9;
        foreach (var (name, chain) in EvmSignatureOracle.RealChainIds)
        {
            foreach (byte parity in new byte[] { 0, 1 })
            {
                var rlp = WriteLegacySigned(1, 1_000_000_000, 21_000, to, 0, [], parity, chain, r, s);
                var parsed = ReadLegacySigned(rlp);

                Assert.AreEqual(ReadOutcome.Ok, parsed.Outcome, $"{name} parity {parity}: {parsed.Status} at field {parsed.FieldIndex}");
                Assert.AreEqual(chain, parsed.LegacyChainId, $"{name} parity {parity}");
                Assert.AreEqual(parity, parsed.Signature.V, $"{name} parity {parity}");
                Assert.AreEqual(new Hash32(Padded32(r)), parsed.Signature.R, name);
                Assert.AreEqual(new Hash32(Padded32(s)), parsed.Signature.S, name);
            }
        }
    }

    [TestMethod]
    public void LegacySigned_PreEip155_ReadsBackWithoutAChainId()
    {
        foreach (byte parity in new byte[] { 0, 1 })
        {
            var rlp = WriteLegacySigned(0, 1, 21_000, Pattern(20, 1), 0, [], parity, null, 1, 1);
            var parsed = ReadLegacySigned(rlp);
            Assert.AreEqual(ReadOutcome.Ok, parsed.Outcome, $"{parsed.Status} at field {parsed.FieldIndex}");
            Assert.IsNull(parsed.LegacyChainId);
            Assert.AreEqual(parity, parsed.Signature.V);
        }
    }

    [TestMethod]
    public void LegacySigned_VAbove255_IsStillReadableAsAnIntegerByTheCoreAccessor()
    {
        // Chain id 1000: v = 35 + 2000 + parity = 2035 or 2036. The core accessor reads the raw value; the adapter derives chain and parity.
        var rlp = WriteLegacySigned(0, 1, 21_000, Pattern(20, 1), 0, [], 0, 1000, 1, 1);

        var root = DecodeOk(rlp);
        Span<RLPRange> workspace = stackalloc RLPRange[16];
        Assert.AreEqual(RLPStatus.Success, root.TryIndexDirectChildren(workspace, out var index, out _));
        Assert.AreEqual(RLPStatus.Success, index.TryGetAt(6, out var v));
        Assert.AreEqual(RLPStatus.Success, v.TryGetUInt64(out var rawV));
        Assert.AreEqual(2035UL, rawV);

        var parsed = ReadLegacySigned(rlp);
        Assert.AreEqual(ReadOutcome.Ok, parsed.Outcome);
        Assert.AreEqual(1000UL, parsed.LegacyChainId);
        Assert.AreEqual((byte)0, parsed.Signature.V);
    }

    // ------------------------------------------------------------------ (2) the EIP-155 example

    [TestMethod]
    public void Eip155_SigningPreimage_BuiltWithAdapters_HashesToTheKnownVector()
    {
        var expectedPreimage = H(Eip155SigningPreimageHex);
        var destination = Filled(expectedPreimage.Length);
        var writer = new RLPWriter(destination, new int[2]);

        // 9 items: nonce, gasPrice, gasLimit, to, value, data, chainId, 0, 0
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(expectedPreimage.Length - 1));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(9));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(new uint256(20_000_000_000UL)));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(21_000));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteAddress(Address.FromEvmBytes(Repeat(0x35, 20))));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(new uint256(1_000_000_000_000_000_000UL)));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(default));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(1));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(0));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(0));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));

        Assert.AreEqual(expectedPreimage.Length, written);
        CollectionAssert.AreEqual(expectedPreimage, destination, "the adapters produce the published preimage bytes");

        // Self-verifying known vector: if Keccak or the preimage were wrong this would fail.
        var root = DecodeOk(destination);
        Span<byte> digest = stackalloc byte[32];
        Keccak256.ComputeHash(root.Encoded, digest);
        Assert.AreEqual(Eip155SigningHashHex, Convert.ToHexStringLower(digest));
    }

    [TestMethod]
    public void Eip155_SignedTransaction_ReadsBackThroughTheLegacySchema()
    {
        var published = H(Eip155SignedTransactionHex);

        var parsed = ReadLegacySigned(published);

        Assert.AreEqual(ReadOutcome.Ok, parsed.Outcome, $"{parsed.Status} at field {parsed.FieldIndex}");
        Assert.AreEqual(9UL, parsed.Nonce);
        Assert.AreEqual(new uint256(20_000_000_000UL), parsed.GasPrice);
        Assert.AreEqual(21_000UL, parsed.GasLimit);
        Assert.AreEqual(Address.FromEvmBytes(RlpReference.FromHex("3535353535353535353535353535353535353535")), parsed.To);
        Assert.AreEqual(new uint256(1_000_000_000_000_000_000UL), parsed.Value);
        Assert.IsEmpty(parsed.Data);
        Assert.AreEqual((byte)0, parsed.Signature.V, "v = 0x25 is parity 0");
        Assert.AreEqual(1UL, parsed.LegacyChainId, "v = 0x25 is chain id 1");
        Assert.AreEqual(new Hash32(RlpReference.FromHex("28ef61340bd939bc2195fe537567866003e1a15d3c71ff63e1590620aa636276")), parsed.Signature.R);
        Assert.AreEqual(new Hash32(RlpReference.FromHex("67cbe9d8997f761aecb703304b3800ccf555c9f3dc64214b297fb1966a3b6d83")), parsed.Signature.S);

        // And the writer rebuilds the same bytes from the parsed fields.
        var rebuilt = WriteLegacySigned(9, 20_000_000_000, 21_000, RlpReference.FromHex("3535353535353535353535353535353535353535"), BigInteger.Pow(10, 18), [], 0, 1,
            new BigInteger(RlpReference.FromHex("28ef61340bd939bc2195fe537567866003e1a15d3c71ff63e1590620aa636276"), isUnsigned: true, isBigEndian: true),
            new BigInteger(RlpReference.FromHex("67cbe9d8997f761aecb703304b3800ccf555c9f3dc64214b297fb1966a3b6d83"), isUnsigned: true, isBigEndian: true));
        CollectionAssert.AreEqual(published, rebuilt);
    }

    [TestMethod]
    public void Keccak256_EmptyInput_IsTheKnownConstant()
    {
        // Independent sanity anchor for the hash used above.
        Span<byte> digest = stackalloc byte[32];
        Keccak256.ComputeHash([], digest);
        Assert.AreEqual("c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470", Convert.ToHexStringLower(digest));
    }

    // ------------------------------------------------------------------ (3) hostile transactions

    private static RlpNode[] ValidLegacyFields() =>
    [
        RlpNode.Bytes(Minimal(9)),
        RlpNode.Bytes(Minimal(20_000_000_000)),
        RlpNode.Bytes(Minimal(21_000)),
        RlpNode.Bytes(Pattern(20, 0x35)),
        RlpNode.Bytes(Minimal(BigInteger.Pow(10, 18))),
        RlpNode.Bytes([]),
        RlpNode.Bytes(Minimal(37)),
        RlpNode.Bytes(Minimal((BigInteger.One << 250) + 3)),
        RlpNode.Bytes(Minimal((BigInteger.One << 200) + 9)),
    ];

    private static byte[] Encode(params RlpNode[] fields) => RlpReference.Encode(RlpNode.ListOf(fields));

    [TestMethod]
    public void Hostile_ValidBaseline_ParsesOk()
    {
        var parsed = ReadLegacySigned(Encode(ValidLegacyFields()));
        Assert.AreEqual(ReadOutcome.Ok, parsed.Outcome, $"{parsed.Status} at field {parsed.FieldIndex}");
    }

    [TestMethod]
    public void Hostile_ExtraField_IsStructurallyValid_ButTheSchemaRejectsIt()
    {
        var fields = ValidLegacyFields().Append(RlpNode.Bytes([0x01])).ToArray();
        var rlp = Encode(fields);

        // The codec is schema-less: the 10-item list is valid RLP.
        Assert.AreEqual(RLPStatus.Success, RLPData.TryDecodeComplete(rlp, RLPDecodeLimits.Default, out var root, out _));
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));
        var count = 0;
        while (children.MoveNext())
        {
            count++;
        }

        Assert.AreEqual(10, count);

        var parsed = ReadLegacySigned(rlp);
        Assert.AreEqual(ReadOutcome.FieldCountMismatch, parsed.Outcome);
        Assert.AreEqual(10, parsed.ChildCount);
    }

    [TestMethod]
    public void Hostile_MissingField_IsStructurallyValid_ButTheSchemaRejectsIt()
    {
        var fields = ValidLegacyFields().Take(8).ToArray();
        var rlp = Encode(fields);

        Assert.AreEqual(RLPStatus.Success, RLPData.TryDecodeComplete(rlp, RLPDecodeLimits.Default, out _, out _));
        var parsed = ReadLegacySigned(rlp);
        Assert.AreEqual(ReadOutcome.FieldCountMismatch, parsed.Outcome);
        Assert.AreEqual(8, parsed.ChildCount);
    }

    [TestMethod]
    public void Hostile_MoreChildrenThanTheIndexWorkspace_ReportsTheRequiredCount()
    {
        var fields = Enumerable.Repeat(RlpNode.Bytes([0x01]), 40).ToArray();
        var parsed = ReadLegacySigned(Encode(fields));
        Assert.AreEqual(ReadOutcome.FieldCountMismatch, parsed.Outcome);
        Assert.AreEqual(RLPStatus.IndexCapacityExceeded, parsed.Status);
        Assert.AreEqual(40, parsed.ChildCount);
    }

    [TestMethod]
    public void Hostile_TheDocumentIsAScalar_IsNotAList()
    {
        var parsed = ReadLegacySigned(RlpReference.Encode(RlpNode.Bytes(Pattern(40, 1))));
        Assert.AreEqual(ReadOutcome.NotAList, parsed.Outcome);
        Assert.AreEqual(RLPStatus.WrongKind, parsed.Status);
    }

    [TestMethod]
    public void Hostile_DestinationOf19Or21Bytes_IsInvalidFixedLength()
    {
        foreach (var length in new[] { 1, 19, 21, 32 })
        {
            var fields = ValidLegacyFields();
            fields[3] = RlpNode.Bytes(Pattern(length, 0x35));
            var parsed = ReadLegacySigned(Encode(fields));
            Assert.AreEqual(ReadOutcome.FieldRejected, parsed.Outcome, $"length {length}");
            Assert.AreEqual(RLPStatus.InvalidFixedLength, parsed.Status, $"length {length}");
            Assert.AreEqual(3, parsed.FieldIndex, $"length {length}");
        }
    }

    [TestMethod]
    public void Hostile_NonceWithLeadingZero_IsNonCanonicalInteger()
    {
        foreach (var nonce in new[] { new byte[] { 0x00 }, new byte[] { 0x00, 0x09 }, new byte[] { 0x00, 0x00, 0x00, 0x09 } })
        {
            var fields = ValidLegacyFields();
            fields[0] = RlpNode.Bytes(nonce);
            var parsed = ReadLegacySigned(Encode(fields));
            Assert.AreEqual(ReadOutcome.FieldRejected, parsed.Outcome, Convert.ToHexString(nonce));
            Assert.AreEqual(RLPStatus.NonCanonicalInteger, parsed.Status, Convert.ToHexString(nonce));
            Assert.AreEqual(0, parsed.FieldIndex, Convert.ToHexString(nonce));
        }
    }

    [TestMethod]
    public void Hostile_NonceWiderThan64Bits_IsIntegerOverflow()
    {
        var fields = ValidLegacyFields();
        fields[0] = RlpNode.Bytes(Pattern(9, 1));
        var parsed = ReadLegacySigned(Encode(fields));
        Assert.AreEqual(RLPStatus.IntegerOverflow, parsed.Status);
        Assert.AreEqual(0, parsed.FieldIndex);
    }

    [TestMethod]
    public void Hostile_ValueOf33Bytes_IsIntegerOverflow_AndOf32BytesIsAccepted()
    {
        var fields = ValidLegacyFields();
        fields[4] = RlpNode.Bytes(Pattern(33, 0x01));
        var parsed = ReadLegacySigned(Encode(fields));
        Assert.AreEqual(ReadOutcome.FieldRejected, parsed.Outcome);
        Assert.AreEqual(RLPStatus.IntegerOverflow, parsed.Status);
        Assert.AreEqual(4, parsed.FieldIndex);

        fields[4] = RlpNode.Bytes(Pattern(32, 0x01));
        Assert.AreEqual(ReadOutcome.Ok, ReadLegacySigned(Encode(fields)).Outcome);
    }

    [TestMethod]
    public void Hostile_ValueOrSignatureAsAList_IsWrongKind()
    {
        for (var field = 0; field < LegacySignedFieldCount; field++)
        {
            if (field == 5)
            {
                continue; // data: covered by the opaque-string tests; a list is WrongKind in ReadData too
            }

            var fields = ValidLegacyFields();
            fields[field] = RlpNode.ListOf(RlpNode.Bytes([0x01]));
            var parsed = ReadLegacySigned(Encode(fields));
            Assert.AreEqual(ReadOutcome.FieldRejected, parsed.Outcome, $"field {field}");
            Assert.AreEqual(RLPStatus.WrongKind, parsed.Status, $"field {field}");
            Assert.AreEqual(Math.Min(field, 6), parsed.FieldIndex, $"field {field} (v, r and s are reported as the signature group at 6)");
        }

        var dataAsList = ValidLegacyFields();
        dataAsList[5] = RlpNode.ListOf();
        var parsedData = ReadLegacySigned(Encode(dataAsList));
        Assert.AreEqual(RLPStatus.WrongKind, parsedData.Status);
        Assert.AreEqual(5, parsedData.FieldIndex);
    }

    [TestMethod]
    public void Hostile_SignatureFieldsOutOfRange_AreRejectedWithTheSignatureStatuses()
    {
        var fields = ValidLegacyFields();
        fields[6] = RlpNode.Bytes([0x01, 0x00]); // v = 256 is chain id 110 with parity 1: valid
        Assert.AreEqual(ReadOutcome.Ok, ReadLegacySigned(Encode(fields)).Outcome);

        fields = ValidLegacyFields();
        fields[6] = RlpNode.Bytes([0x1d]); // v = 29: neither 27, 28 nor at least 35
        Assert.AreEqual(RLPStatus.InvalidSignatureV, ReadLegacySigned(Encode(fields)).Status);

        fields = ValidLegacyFields();
        fields[6] = RlpNode.Bytes(Pattern(10, 1)); // v wider than any 64-bit chain id needs
        Assert.AreEqual(RLPStatus.IntegerOverflow, ReadLegacySigned(Encode(fields)).Status);

        fields = ValidLegacyFields();
        fields[6] = RlpNode.Bytes(Minimal((BigInteger.One << 65) + 35)); // chain id ulong.MaxValue + 1
        Assert.AreEqual(RLPStatus.IntegerOverflow, ReadLegacySigned(Encode(fields)).Status);

        fields = ValidLegacyFields();
        fields[7] = RlpNode.Bytes(Pattern(33, 1)); // r too wide
        Assert.AreEqual(RLPStatus.IntegerOverflow, ReadLegacySigned(Encode(fields)).Status);

        fields = ValidLegacyFields();
        fields[8] = RlpNode.Bytes(Pattern(32, 0)); // s with a leading zero
        Assert.AreEqual(RLPStatus.NonCanonicalInteger, ReadLegacySigned(Encode(fields)).Status);
    }

    [TestMethod]
    public void Hostile_CalldataThatLooksLikeRlp_StaysAnOpaqueString()
    {
        // The data bytes are a complete valid RLP list (and a nested one): they must never be recursed into.
        var lookalike = RlpReference.Encode(RlpNode.ListOf(RlpNode.Bytes([1, 2, 3]), RlpNode.ListOf(RlpNode.Bytes(Pattern(30, 1)))));
        var fields = ValidLegacyFields();
        fields[5] = RlpNode.Bytes(lookalike);
        var rlp = Encode(fields);

        Assert.AreEqual(RLPStatus.Success, RLPData.TryDecodeComplete(rlp, RLPDecodeLimits.Default, out var root, out _));
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));
        var count = 0;
        RLPKind dataKind = RLPKind.None;
        while (children.MoveNext())
        {
            if (count == 5)
            {
                var data = children.Current;
                dataKind = data.Kind;
                CollectionAssert.AreEqual(lookalike, data.Payload.ToArray());
                Assert.AreEqual(RLPStatus.WrongKind, data.TryGetChildren(out _), "the data string has no children");
                Assert.AreEqual(RLPStatus.WrongKind, data.TryIndexDirectChildren(new RLPRange[8], out _, out _));
                Assert.AreEqual(RLPStatus.Success, data.TryGetFunctionSelector(out var selector));
                Assert.AreEqual(BitConverter.ToUInt32(lookalike.Take(4).Reverse().ToArray()), selector.Value);
            }

            count++;
        }

        Assert.AreEqual(LegacySignedFieldCount, count, "the lookalike's inner items are not flattened into the transaction");
        Assert.AreEqual(RLPKind.ByteString, dataKind);

        var parsed = ReadLegacySigned(rlp);
        Assert.AreEqual(ReadOutcome.Ok, parsed.Outcome);
        CollectionAssert.AreEqual(lookalike, parsed.Data);
    }

    [TestMethod]
    public void Hostile_TrailingBytesAfterTheTransaction_AreRejectedByTheCodec()
    {
        var rlp = RlpReference.Concatenate(Encode(ValidLegacyFields()), [0x00]);
        var parsed = ReadLegacySigned(rlp);
        Assert.AreEqual(ReadOutcome.RlpRejected, parsed.Outcome);
        Assert.AreEqual(RLPStatus.TrailingData, parsed.Status);
    }

    [TestMethod]
    public void Hostile_AccessListEntries_WithWrongWidths_AreRejected()
    {
        var valid = SampleEip1559();

        var shortAddress = valid with { AccessList = [(Pattern(19, 1), [Pattern(32, 1)])] };
        var parsed = ReadEip1559(RlpReference.Encode(Eip1559Tree(shortAddress)));
        Assert.AreEqual(ReadOutcome.FieldRejected, parsed.Outcome);
        Assert.AreEqual(RLPStatus.InvalidFixedLength, parsed.Status);
        Assert.AreEqual(8, parsed.FieldIndex);

        var shortKey = valid with { AccessList = [(Pattern(20, 1), [Pattern(31, 1)])] };
        parsed = ReadEip1559(RlpReference.Encode(Eip1559Tree(shortKey)));
        Assert.AreEqual(RLPStatus.InvalidFixedLength, parsed.Status);
        Assert.AreEqual(8, parsed.FieldIndex);
    }

    [TestMethod]
    public void Hostile_Eip1559WithTheLegacyFieldCount_IsRejectedBySchema()
    {
        var rlp = WriteEip1559(SampleEip1559());
        Assert.AreEqual(ReadOutcome.FieldCountMismatch, ReadLegacySigned(rlp).Outcome);

        var legacy = Encode(ValidLegacyFields());
        Assert.AreEqual(ReadOutcome.FieldCountMismatch, ReadEip1559(legacy).Outcome);
    }
}
