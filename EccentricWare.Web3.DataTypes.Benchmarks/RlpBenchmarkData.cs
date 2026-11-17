using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// An independent, allocating, recursive RLP tree used only to build benchmark inputs and to verify them in
/// <c>[GlobalSetup]</c>. It shares no code with the library: prefix rules are re-implemented here, so a library defect
/// cannot silently produce its own benchmark input.
/// </summary>
internal sealed class RlpReferenceNode
{
    private RlpReferenceNode(byte[]? bytes, RlpReferenceNode[]? children)
    {
        Bytes = bytes;
        Children = children;
    }

    public byte[]? Bytes { get; }

    public RlpReferenceNode[]? Children { get; }

    public bool IsList => Children is not null;

    public static RlpReferenceNode String(byte[] bytes) => new(bytes, null);

    public static RlpReferenceNode List(params RlpReferenceNode[] children) => new(null, children);

    /// <summary>Total encoded size of the children of a list, or the byte count of a string.</summary>
    public int PayloadLength
    {
        get
        {
            if (Children is null)
            {
                return Bytes!.Length;
            }

            int sum = 0;
            foreach (RlpReferenceNode child in Children)
            {
                sum += child.EncodedLength;
            }

            return sum;
        }
    }

    public int EncodedLength
    {
        get
        {
            if (Bytes is { Length: 1 } single && single[0] < 0x80)
            {
                return 1;
            }

            int payload = PayloadLength;
            return HeaderLength(payload) + payload;
        }
    }

    /// <summary>Naive allocating encoder: every level builds and concatenates fresh arrays.</summary>
    public byte[] Encode()
    {
        if (Children is null)
        {
            byte[] bytes = Bytes!;
            if (bytes.Length == 1 && bytes[0] < 0x80)
            {
                return [bytes[0]];
            }

            return [.. Header(bytes.Length, 0x80), .. bytes];
        }

        var payload = new List<byte>();
        foreach (RlpReferenceNode child in Children)
        {
            payload.AddRange(child.Encode());
        }

        return [.. Header(payload.Count, 0xc0), .. payload];
    }

    private static int HeaderLength(int payloadLength) => payloadLength <= 55 ? 1 : 1 + MinimalLengthBytes(payloadLength).Length;

    private static byte[] Header(int payloadLength, int shortBase)
    {
        if (payloadLength <= 55)
        {
            return [(byte)(shortBase + payloadLength)];
        }

        byte[] length = MinimalLengthBytes(payloadLength);
        return [(byte)(shortBase + 55 + length.Length), .. length];
    }

    private static byte[] MinimalLengthBytes(int value)
    {
        var bytes = new List<byte>();
        for (int remaining = value; remaining != 0; remaining >>= 8)
        {
            bytes.Insert(0, (byte)remaining);
        }

        return [.. bytes];
    }
}

/// <summary>
/// Deterministic transaction-shaped fixtures built from <see cref="BenchmarkData.Seed"/>: a signed legacy transaction
/// (9 fields) and an EIP-1559 payload (12 fields, access list of 2 entries with 2 storage keys each). The encodings come
/// from the writer under test and are cross-checked byte-for-byte against <see cref="RlpReferenceNode.Encode"/>.
/// </summary>
internal sealed class RlpTransactionFixture
{
    public const int LegacyFieldCount = 9;
    public const int Eip1559FieldCount = 12;
    public const int AccessListEntryCount = 2;
    public const int KeysPerEntry = 2;

    public ulong ChainId = 1;
    public ulong Nonce = 137;
    public uint256 GasPrice;
    public uint256 MaxPriorityFee;
    public uint256 MaxFee;
    public ulong GasLimit = 65_000;
    public Address To;
    public uint256 Value;
    public byte[] Data;
    public ulong V = 37;
    public ulong YParity = 1;
    public uint256 R;
    public uint256 S;
    public Address[] AccessListAddresses;
    public Hash32[] AccessListKeys;

    /// <summary>The nine legacy fields as raw RLP string contents (integers as minimal big-endian magnitudes), in wire order.</summary>
    public byte[][] LegacyFieldBytes;
    public byte[] LegacyEncoded;
    public byte[] Eip1559Encoded;
    public int LegacyPayloadLength;
    public int Eip1559PayloadLength;
    public int AccessListPayloadLength;
    public int AccessEntryPayloadLength;
    public int AccessKeysPayloadLength;

    public RlpTransactionFixture(int seed)
    {
        var random = new Random(seed);
        byte[] gasPrice = RlpBenchmarkData.Magnitude(20_000_000_000UL);
        byte[] maxPriority = RlpBenchmarkData.Magnitude(1_500_000_000UL);
        byte[] maxFee = RlpBenchmarkData.Magnitude(30_000_000_000UL);
        byte[] value = RlpBenchmarkData.Magnitude(1_500_000_000_000_000_000UL);
        byte[] to = RlpBenchmarkData.RandomBytes(random, 20);
        Data = BenchmarkData.ApproveMaxCalldata(seed);
        byte[] r = RlpBenchmarkData.RandomNonZeroLeading(random, 32);
        byte[] s = RlpBenchmarkData.RandomNonZeroLeading(random, 32);

        GasPrice = new uint256(gasPrice);
        MaxPriorityFee = new uint256(maxPriority);
        MaxFee = new uint256(maxFee);
        Value = new uint256(value);
        To = Address.FromEvmBytes(to);
        R = new uint256(r);
        S = new uint256(s);

        var accessAddressBytes = new byte[AccessListEntryCount][];
        var accessKeyBytes = new byte[AccessListEntryCount * KeysPerEntry][];
        AccessListAddresses = new Address[AccessListEntryCount];
        AccessListKeys = new Hash32[AccessListEntryCount * KeysPerEntry];
        for (int i = 0; i < AccessListEntryCount; i++)
        {
            accessAddressBytes[i] = RlpBenchmarkData.RandomBytes(random, 20);
            AccessListAddresses[i] = Address.FromEvmBytes(accessAddressBytes[i]);
        }

        for (int i = 0; i < accessKeyBytes.Length; i++)
        {
            accessKeyBytes[i] = RlpBenchmarkData.RandomBytes(random, 32);
            AccessListKeys[i] = new Hash32(accessKeyBytes[i]);
        }

        RlpReferenceNode legacy = RlpReferenceNode.List(
            Str(RlpBenchmarkData.Magnitude(Nonce)), Str(gasPrice), Str(RlpBenchmarkData.Magnitude(GasLimit)), Str(to),
            Str(value), Str(Data), Str(RlpBenchmarkData.Magnitude(V)), Str(r), Str(s));

        var accessEntries = new RlpReferenceNode[AccessListEntryCount];
        for (int i = 0; i < AccessListEntryCount; i++)
        {
            var keys = new RlpReferenceNode[KeysPerEntry];
            for (int k = 0; k < KeysPerEntry; k++)
            {
                keys[k] = Str(accessKeyBytes[(i * KeysPerEntry) + k]);
            }

            accessEntries[i] = RlpReferenceNode.List(Str(accessAddressBytes[i]), RlpReferenceNode.List(keys));
        }

        RlpReferenceNode accessList = RlpReferenceNode.List(accessEntries);
        RlpReferenceNode eip1559 = RlpReferenceNode.List(
            Str(RlpBenchmarkData.Magnitude(ChainId)), Str(RlpBenchmarkData.Magnitude(Nonce)), Str(maxPriority), Str(maxFee),
            Str(RlpBenchmarkData.Magnitude(GasLimit)), Str(to), Str(value), Str(Data), accessList,
            Str(RlpBenchmarkData.Magnitude(YParity)), Str(r), Str(s));

        LegacyFieldBytes =
        [
            RlpBenchmarkData.Magnitude(Nonce), gasPrice, RlpBenchmarkData.Magnitude(GasLimit), to, value, Data,
            RlpBenchmarkData.Magnitude(V), r, s,
        ];
        LegacyPayloadLength = legacy.PayloadLength;
        Eip1559PayloadLength = eip1559.PayloadLength;
        AccessListPayloadLength = accessList.PayloadLength;
        AccessEntryPayloadLength = accessEntries[0].PayloadLength;
        AccessKeysPayloadLength = accessEntries[0].Children![1].PayloadLength;

        LegacyEncoded = RlpBenchmarkData.BuildWithWriter(legacy);
        Eip1559Encoded = RlpBenchmarkData.BuildWithWriter(eip1559);
        RlpBenchmarkData.RequireEqual(legacy.Encode(), LegacyEncoded, "legacy transaction: writer vs reference encoder");
        RlpBenchmarkData.RequireEqual(eip1559.Encode(), Eip1559Encoded, "EIP-1559 payload: writer vs reference encoder");

        static RlpReferenceNode Str(byte[] bytes) => RlpReferenceNode.String(bytes);
    }

    // The constant-length rows keep the original write code untouched so they stay comparable with earlier runs; the
    // *WithSizing methods repeat the same write sequence after computing the lengths, so the timed difference between the
    // two is the sizing work (plus any code-layout noise, see docs/rlp-performance.md).
    /// <summary>Writes the legacy transaction with a framed <see cref="RLPWriter"/> session and the typed adapters; returns the length, or -1 on any failure.</summary>
    public int WriteLegacy(Span<byte> destination)
    {
        Span<int> frames = stackalloc int[2];
        var writer = new RLPWriter(destination, frames);
        RLPStatus status = writer.TryBeginList(LegacyPayloadLength);
        status |= writer.TryWriteUInt64(Nonce);
        status |= writer.TryWriteUInt256(GasPrice);
        status |= writer.TryWriteUInt64(GasLimit);
        status |= writer.TryWriteAddress(To);
        status |= writer.TryWriteUInt256(Value);
        status |= writer.TryWriteByteString(Data);
        status |= writer.TryWriteUInt64(V);
        status |= writer.TryWriteUInt256(R);
        status |= writer.TryWriteUInt256(S);
        status |= writer.TryEndList();
        status |= writer.TryComplete(out int written);
        return status == RLPStatus.Success ? written : -1;
    }

    /// <summary>
    /// As <see cref="WriteLegacy"/>, but first sums the exact children length from the field values with the public
    /// sizing helpers, as a caller that does not know the length up front must.
    /// </summary>
    public int WriteLegacyWithSizing(Span<byte> destination)
    {
        if (RLPEncoder.TryGetByteStringLength(Data, out int dataLength) != RLPStatus.Success)
        {
            return -1;
        }

        int payloadLength = RLPEncoder.GetUInt64EncodedLength(Nonce)
            + RLPTypedExtensions.GetUInt256EncodedLength(GasPrice)
            + RLPEncoder.GetUInt64EncodedLength(GasLimit)
            + RLPTypedExtensions.AddressEncodedLength
            + RLPTypedExtensions.GetUInt256EncodedLength(Value)
            + dataLength
            + RLPEncoder.GetUInt64EncodedLength(V)
            + RLPTypedExtensions.GetUInt256EncodedLength(R)
            + RLPTypedExtensions.GetUInt256EncodedLength(S);
        Span<int> frames = stackalloc int[2];
        var writer = new RLPWriter(destination, frames);
        RLPStatus status = writer.TryBeginList(payloadLength);
        status |= writer.TryWriteUInt64(Nonce);
        status |= writer.TryWriteUInt256(GasPrice);
        status |= writer.TryWriteUInt64(GasLimit);
        status |= writer.TryWriteAddress(To);
        status |= writer.TryWriteUInt256(Value);
        status |= writer.TryWriteByteString(Data);
        status |= writer.TryWriteUInt64(V);
        status |= writer.TryWriteUInt256(R);
        status |= writer.TryWriteUInt256(S);
        status |= writer.TryEndList();
        status |= writer.TryComplete(out int written);
        return status == RLPStatus.Success ? written : -1;
    }

    /// <summary>Writes the EIP-1559 payload (12 fields, access list of nested lists); returns the length, or -1 on any failure.</summary>
    public int WriteEip1559(Span<byte> destination)
    {
        Span<int> frames = stackalloc int[8];
        var writer = new RLPWriter(destination, frames);
        RLPStatus status = writer.TryBeginList(Eip1559PayloadLength);
        status |= writer.TryWriteUInt64(ChainId);
        status |= writer.TryWriteUInt64(Nonce);
        status |= writer.TryWriteUInt256(MaxPriorityFee);
        status |= writer.TryWriteUInt256(MaxFee);
        status |= writer.TryWriteUInt64(GasLimit);
        status |= writer.TryWriteAddress(To);
        status |= writer.TryWriteUInt256(Value);
        status |= writer.TryWriteByteString(Data);
        status |= writer.TryBeginList(AccessListPayloadLength);
        for (int entry = 0; entry < AccessListEntryCount; entry++)
        {
            status |= writer.TryBeginList(AccessEntryPayloadLength);
            status |= writer.TryWriteAddress(AccessListAddresses[entry]);
            status |= writer.TryBeginList(AccessKeysPayloadLength);
            for (int key = 0; key < KeysPerEntry; key++)
            {
                status |= writer.TryWriteHash32(AccessListKeys[(entry * KeysPerEntry) + key]);
            }

            status |= writer.TryEndList();
            status |= writer.TryEndList();
        }

        status |= writer.TryEndList();
        status |= writer.TryWriteUInt64(YParity);
        status |= writer.TryWriteUInt256(R);
        status |= writer.TryWriteUInt256(S);
        status |= writer.TryEndList();
        status |= writer.TryComplete(out int written);
        return status == RLPStatus.Success ? written : -1;
    }
    /// <summary>
    /// As <see cref="WriteEip1559"/>, but first computes every list length from the field values with the public sizing
    /// helpers: each access list entry's key list and entry, the access list itself, then the whole payload.
    /// </summary>
    public int WriteEip1559WithSizing(Span<byte> destination)
    {
        Span<int> entryPayloads = stackalloc int[AccessListEntryCount];
        Span<int> keysPayloads = stackalloc int[AccessListEntryCount];
        int accessListPayload = 0;
        for (int entry = 0; entry < AccessListEntryCount; entry++)
        {
            int keysPayload = KeysPerEntry * RLPTypedExtensions.Hash32EncodedLength;
            if (RLPEncoder.TryGetListLength(keysPayload, out int keysList) != RLPStatus.Success)
            {
                return -1;
            }

            int entryPayload = RLPTypedExtensions.AddressEncodedLength + keysList;
            if (RLPEncoder.TryGetListLength(entryPayload, out int entryList) != RLPStatus.Success)
            {
                return -1;
            }

            keysPayloads[entry] = keysPayload;
            entryPayloads[entry] = entryPayload;
            accessListPayload += entryList;
        }

        if (RLPEncoder.TryGetListLength(accessListPayload, out int accessList) != RLPStatus.Success
            || RLPEncoder.TryGetByteStringLength(Data, out int dataLength) != RLPStatus.Success)
        {
            return -1;
        }

        int payloadLength = RLPEncoder.GetUInt64EncodedLength(ChainId)
            + RLPEncoder.GetUInt64EncodedLength(Nonce)
            + RLPTypedExtensions.GetUInt256EncodedLength(MaxPriorityFee)
            + RLPTypedExtensions.GetUInt256EncodedLength(MaxFee)
            + RLPEncoder.GetUInt64EncodedLength(GasLimit)
            + RLPTypedExtensions.AddressEncodedLength
            + RLPTypedExtensions.GetUInt256EncodedLength(Value)
            + dataLength
            + accessList
            + RLPEncoder.GetUInt64EncodedLength(YParity)
            + RLPTypedExtensions.GetUInt256EncodedLength(R)
            + RLPTypedExtensions.GetUInt256EncodedLength(S);

        Span<int> frames = stackalloc int[8];
        var writer = new RLPWriter(destination, frames);
        RLPStatus status = writer.TryBeginList(payloadLength);
        status |= writer.TryWriteUInt64(ChainId);
        status |= writer.TryWriteUInt64(Nonce);
        status |= writer.TryWriteUInt256(MaxPriorityFee);
        status |= writer.TryWriteUInt256(MaxFee);
        status |= writer.TryWriteUInt64(GasLimit);
        status |= writer.TryWriteAddress(To);
        status |= writer.TryWriteUInt256(Value);
        status |= writer.TryWriteByteString(Data);
        status |= writer.TryBeginList(accessListPayload);
        for (int entry = 0; entry < AccessListEntryCount; entry++)
        {
            status |= writer.TryBeginList(entryPayloads[entry]);
            status |= writer.TryWriteAddress(AccessListAddresses[entry]);
            status |= writer.TryBeginList(keysPayloads[entry]);
            for (int key = 0; key < KeysPerEntry; key++)
            {
                status |= writer.TryWriteHash32(AccessListKeys[(entry * KeysPerEntry) + key]);
            }

            status |= writer.TryEndList();
            status |= writer.TryEndList();
        }

        status |= writer.TryEndList();
        status |= writer.TryWriteUInt64(YParity);
        status |= writer.TryWriteUInt256(R);
        status |= writer.TryWriteUInt256(S);
        status |= writer.TryEndList();
        status |= writer.TryComplete(out int written);
        return status == RLPStatus.Success ? written : -1;
    }
}

/// <summary>
/// A signed legacy transaction whose <c>v</c> is built by the EVM signature adapters: pre-EIP-155 (<c>27 + parity</c>) or
/// EIP-155 (<c>35 + 2 * chainId + parity</c>). The expected bytes come from the independent reference encoder, with
/// <c>v</c> computed here in 128-bit arithmetic; the writer path under test is cross-checked against them.
/// </summary>
internal sealed class RlpEvmLegacyFixture
{
    public RlpEvmLegacyFixture(RlpTransactionFixture tx, ulong? chainId, byte yParity)
    {
        ChainId = chainId;
        YParity = yParity;
        Transaction = tx;
        UInt128 wireV = RlpEvmCase.WireV(chainId, yParity);
        WireV = wireV;
        byte[][] fields = [.. tx.LegacyFieldBytes];
        fields[6] = MinimalMagnitude(wireV);
        var nodes = new RlpReferenceNode[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            nodes[i] = RlpReferenceNode.String(fields[i]);
        }

        Encoded = RlpReferenceNode.List(nodes).Encode();
        ExpectedSignature = RlpBenchmarkData.ExpectedSignature(tx, yParity);

        var destination = new byte[Encoded.Length + 16];
        int written = Write(destination);
        if (written != Encoded.Length)
        {
            throw new InvalidOperationException($"EVM legacy fixture (chain {chainId}): wrote {written} bytes, reference {Encoded.Length}.");
        }

        RlpBenchmarkData.RequireEqual(Encoded, destination.AsSpan(0, written), $"EVM legacy fixture (chain {chainId}) bytes");
    }

    public RlpTransactionFixture Transaction { get; }

    public ulong? ChainId { get; }

    public byte YParity { get; }

    /// <summary>The wire <c>v</c> (27/28 or 35 + 2 * chainId + parity); up to 2^65 + 35 for a 64-bit chain id.</summary>
    public UInt128 WireV { get; }

    public byte[] Encoded { get; }

    /// <summary>The signature the readers must return: the fixture's <c>r</c> and <c>s</c> with <c>V</c> = y parity.</summary>
    public Signature ExpectedSignature { get; }

    /// <summary>Minimal big-endian bytes of <paramref name="value"/>; empty for zero.</summary>
    public static byte[] MinimalMagnitude(UInt128 value)
    {
        var bytes = new List<byte>();
        for (UInt128 remaining = value; remaining != UInt128.Zero; remaining >>= 8)
        {
            bytes.Insert(0, (byte)remaining);
        }

        return [.. bytes];
    }

    /// <summary>Sizes every field with the public helpers (<c>v</c> through <c>TryGetEvmLegacyVEncodedLength</c>), then writes the transaction with <c>TryWriteEvmLegacyV</c>; returns the length, or -1.</summary>
    public int Write(Span<byte> destination)
    {
        RlpTransactionFixture tx = Transaction;
        if (RLPEncoder.TryGetByteStringLength(tx.Data, out int dataLength) != RLPStatus.Success
            || RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(YParity, ChainId, out int vLength) != RLPStatus.Success)
        {
            return -1;
        }

        int payloadLength = RLPEncoder.GetUInt64EncodedLength(tx.Nonce)
            + RLPTypedExtensions.GetUInt256EncodedLength(tx.GasPrice)
            + RLPEncoder.GetUInt64EncodedLength(tx.GasLimit)
            + RLPTypedExtensions.AddressEncodedLength
            + RLPTypedExtensions.GetUInt256EncodedLength(tx.Value)
            + dataLength
            + vLength
            + RLPTypedExtensions.GetUInt256EncodedLength(tx.R)
            + RLPTypedExtensions.GetUInt256EncodedLength(tx.S);

        Span<int> frames = stackalloc int[2];
        var writer = new RLPWriter(destination, frames);
        RLPStatus status = writer.TryBeginList(payloadLength);
        status |= writer.TryWriteUInt64(tx.Nonce);
        status |= writer.TryWriteUInt256(tx.GasPrice);
        status |= writer.TryWriteUInt64(tx.GasLimit);
        status |= writer.TryWriteAddress(tx.To);
        status |= writer.TryWriteUInt256(tx.Value);
        status |= writer.TryWriteByteString(tx.Data);
        status |= writer.TryWriteEvmLegacyV(YParity, ChainId);
        status |= writer.TryWriteUInt256(tx.R);
        status |= writer.TryWriteUInt256(tx.S);
        status |= writer.TryEndList();
        status |= writer.TryComplete(out int written);
        return status == RLPStatus.Success ? written : -1;
    }
}

internal static class RlpBenchmarkData
{
    /// <summary>Built once per process; deterministic.</summary>
    public static readonly RlpTransactionFixture Transactions = new(BenchmarkData.Seed);

    /// <summary>The legacy transaction signed for chain 98866 (v = 197768, y parity 1): the fixture shared by the harness and the signature rows.</summary>
    public static readonly RlpEvmLegacyFixture LegacyChain98866 = new(Transactions, 98866UL, 1);

    public static RLPDecodeLimits DefaultLimits => RLPDecodeLimits.Default;

    public static byte[] RandomBytes(Random random, int length)
    {
        var bytes = new byte[length];
        random.NextBytes(bytes);
        return bytes;
    }

    /// <summary>Random big-endian magnitude whose first byte is non-zero, so it is a canonical RLP integer.</summary>
    public static byte[] RandomNonZeroLeading(Random random, int length)
    {
        byte[] bytes = RandomBytes(random, length);
        bytes[0] |= 0x10;
        return bytes;
    }

    /// <summary>Minimal big-endian bytes of <paramref name="value"/>; empty for zero.</summary>
    public static byte[] Magnitude(ulong value)
    {
        var bytes = new List<byte>();
        for (ulong remaining = value; remaining != 0; remaining >>= 8)
        {
            bytes.Insert(0, (byte)remaining);
        }

        return [.. bytes];
    }

    /// <summary>A byte string whose first byte is at least 0x80 when it has one byte (so a 1-byte string takes a header).</summary>
    public static byte[] StringPayload(int length, int seed)
    {
        byte[] bytes = BenchmarkData.RandomBytes(length, seed);
        if (length == 1)
        {
            bytes[0] |= 0x80;
        }

        return bytes;
    }

    /// <summary>Builds the encoding of a reference tree with <see cref="RLPWriter"/>; throws if the writer rejects any step.</summary>
    public static byte[] BuildWithWriter(RlpReferenceNode root)
    {
        var destination = new byte[root.EncodedLength];
        Span<int> frames = stackalloc int[RLPDecodeLimits.HardMaxListDepth];
        var writer = new RLPWriter(destination, frames);
        RequireSuccess(WriteNode(ref writer, root), "writer node");
        RequireSuccess(writer.TryComplete(out int written), "writer complete");
        if (written != destination.Length)
        {
            throw new InvalidOperationException($"Writer produced {written} bytes, reference length is {destination.Length}.");
        }

        return destination;
    }

    private static RLPStatus WriteNode(ref RLPWriter writer, RlpReferenceNode node)
    {
        if (!node.IsList)
        {
            return writer.TryWriteByteString(node.Bytes!);
        }

        RLPStatus status = writer.TryBeginList(node.PayloadLength);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        foreach (RlpReferenceNode child in node.Children!)
        {
            status = WriteNode(ref writer, child);
            if (status != RLPStatus.Success)
            {
                return status;
            }
        }

        return writer.TryEndList();
    }

    /// <summary>Nested lists: <paramref name="depth"/> lists around one short string (depth 1 is <c>[ "a" ]</c>).</summary>
    public static byte[] NestedLists(int depth)
    {
        RlpReferenceNode node = RlpReferenceNode.List(RlpReferenceNode.String([(byte)'a', (byte)'b']));
        for (int level = 1; level < depth; level++)
        {
            node = RlpReferenceNode.List(node);
        }

        byte[] encoded = BuildWithWriter(node);
        RequireEqual(node.Encode(), encoded, $"nested depth {depth}: writer vs reference encoder");
        return encoded;
    }

    /// <summary>A flat list of <paramref name="count"/> strings of <paramref name="stringLength"/> bytes each (length 1 gives literal bytes below 0x80).</summary>
    public static byte[] FlatList(int count, int stringLength, int seed = BenchmarkData.Seed)
    {
        var random = new Random(seed);
        var children = new RlpReferenceNode[count];
        for (int i = 0; i < count; i++)
        {
            byte[] bytes = RandomBytes(random, stringLength);
            if (stringLength == 1)
            {
                bytes[0] &= 0x7f;
            }

            children[i] = RlpReferenceNode.String(bytes);
        }

        RlpReferenceNode list = RlpReferenceNode.List(children);
        byte[] encoded = BuildWithWriter(list);
        RequireEqual(list.Encode(), encoded, $"flat list {count}x{stringLength}: writer vs reference encoder");
        return encoded;
    }

    /// <summary>A flat list of <paramref name="count"/> children cycling through the given string lengths (0 is the empty string).</summary>
    public static byte[] MixedFlatList(int count, ReadOnlySpan<int> stringLengths, int seed = BenchmarkData.Seed)
    {
        var random = new Random(seed);
        var children = new RlpReferenceNode[count];
        for (int i = 0; i < count; i++)
        {
            byte[] bytes = RandomBytes(random, stringLengths[i % stringLengths.Length]);
            if (bytes.Length == 1)
            {
                bytes[0] |= 0x80;
            }

            children[i] = RlpReferenceNode.String(bytes);
        }

        RlpReferenceNode list = RlpReferenceNode.List(children);
        byte[] encoded = BuildWithWriter(list);
        RequireEqual(list.Encode(), encoded, $"mixed flat list {count}: writer vs reference encoder");
        return encoded;
    }

    private static readonly int[] TransactionFieldLengths = [2, 5, 3, 20, 9, 68, 1, 32, 32, 4, 20, 32];

    /// <summary>A flat list of <paramref name="fieldCount"/> transaction-like fields (integers, a 20-byte address, 68 bytes of calldata, 32-byte words).</summary>
    public static byte[] TransactionShapedList(int fieldCount) => MixedFlatList(fieldCount, TransactionFieldLengths);

    /// <summary>A flat list of <paramref name="count"/> short lists, each holding one 2-byte string.</summary>
    public static byte[] ListOfLists(int count)
    {
        var children = new RlpReferenceNode[count];
        for (int i = 0; i < count; i++)
        {
            children[i] = RlpReferenceNode.List(RlpReferenceNode.String([(byte)i, (byte)(i | 0x80)]));
        }

        RlpReferenceNode list = RlpReferenceNode.List(children);
        byte[] encoded = BuildWithWriter(list);
        RequireEqual(list.Encode(), encoded, $"list of lists {count}: writer vs reference encoder");
        return encoded;
    }

    /// <summary>The signature the EVM readers must return for the fixture's <c>r</c> and <c>s</c> and a y parity.</summary>
    public static Signature ExpectedSignature(RlpTransactionFixture tx, byte yParity)
    {
        byte[] r32 = new byte[32];
        byte[] s32 = new byte[32];
        tx.R.WriteBigEndian(r32);
        tx.S.WriteBigEndian(s32);
        return Signature.FromEvmComponents(r32, s32, yParity);
    }

    public static void RequireSuccess(RLPStatus status, string what)
    {
        if (status != RLPStatus.Success)
        {
            throw new InvalidOperationException($"{what} failed: {status}.");
        }
    }

    public static void RequireStatus(RLPStatus expected, RLPStatus actual, string what)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"{what}: expected {expected}, got {actual}.");
        }
    }

    public static void RequireEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, string what)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"{what}: bytes differ.");
        }
    }

    /// <summary>Strict decode with default limits; throws unless the document is valid and spans the whole input.</summary>
    public static void RequireDecodes(byte[] input, RLPKind expectedKind, string what)
    {
        RLPDecodeLimits limits = RLPDecodeLimits.Default;
        RequireSuccess(RLPData.TryDecodeComplete(input, in limits, out RLPData root, out _), what);
        if (root.Kind != expectedKind || root.EncodedLength != input.Length)
        {
            throw new InvalidOperationException($"{what}: decoded kind {root.Kind}, length {root.EncodedLength} of {input.Length}.");
        }
    }
}

/// <summary>
/// Named legacy-signature cases shared by the read and write rows: the chain id (or none) and the y parity whose wire
/// <c>v</c> is 27 (no chain id), 37 (chain 1), 310 (chain 137), 197,768 (chain 98866) and 2^65 + 34 (the largest 64-bit chain id with parity 1, 9 bytes).
/// </summary>
internal static class RlpEvmCase
{
    public const string NoChainId = "NoChainId_v27";
    public const string Chain1 = "Chain1_v37";
    public const string Chain137 = "Chain137_v310";
    public const string Chain98866 = "Chain98866_v197768";
    public const string ChainIdMax = "ChainIdMax_9ByteV";

    public static void Resolve(string name, out ulong? chainId, out byte yParity)
    {
        switch (name)
        {
            case NoChainId:
                chainId = null;
                yParity = 0;
                break;
            case Chain1:
                chainId = 1;
                yParity = 0;
                break;
            case Chain137:
                chainId = 137;
                yParity = 1;
                break;
            case Chain98866:
                chainId = 98866;
                yParity = 1;
                break;
            case ChainIdMax:
                chainId = ulong.MaxValue;
                yParity = 1;
                break;
            default:
                throw new InvalidOperationException($"Unknown EVM case {name}.");
        }
    }

    /// <summary>The wire <c>v</c> for the case, computed in 128 bits independently of the library.</summary>
    public static UInt128 WireV(ulong? chainId, byte yParity) =>
        chainId is ulong chain ? 35 + ((UInt128)chain << 1) + yParity : (UInt128)(27 + yParity);
}
