# Integrating RLP with the EVM types

How to use the RLP codec with `uint256`, `Address`, `Hash32`, `FunctionSelector` and `Signature`. The codec is a layer under your transaction or block model; it does not define one.

## Where RLP ends

- An **EIP-2718 typed transaction** is a type byte (`01`, `02`, `03`, `04`, ...) followed by an RLP payload. The type byte is not RLP. Read it yourself, then pass the remaining bytes to `RLPData.TryDecodeComplete`. This is the form used when a transaction is sent on its own (for example `eth_sendRawTransaction`).
- **Inside a block body, a transactions trie leaf or a devp2p `Transactions` message, a typed transaction is wrapped as an RLP byte string** whose payload is `type || rlp(payload)`. Decode that string first (`TryDecodeComplete` on the whole item gives a `ByteString`), take its `Payload`, read `Payload[0]` as the type, and decode `Payload[1..]`. Passing the string's payload including the type byte to `TryDecodeComplete` does not produce a transaction list. Legacy transactions are plain lists in the same positions. Typed receipts are wrapped the same way.
- An **EIP-4844 blob transaction** has two forms: the canonical `0x03 || rlp([chain_id, nonce, ..., blob_versioned_hashes, y_parity, r, s])`, and the network (gossip) form `0x03 || rlp([tx_payload_body, blobs, commitments, proofs])`, which carries whole 131,072-byte blobs and exceeds the default size limit from about 8 blobs. Later revisions (EIP-7594) change that wrapper (a wrapper version and cell proofs); check the current EIP before parsing it. The tests in this repository cover only the form above.
- An **EIP-7702** transaction (type `04`) carries an authorization list of tuples `[chain_id, address, nonce, y_parity, r, s]`. The tuple's `chain_id` is a `uint256` that may be 0 (valid on any chain); read it with `TryGetUInt256`, not `TryGetUInt64`.
- A **legacy transaction** is a bare RLP list of nine items.
- **Header and receipt fields are not all integers.** A block header's `nonce` is a fixed 8-byte string (every post-merge header carries `88 00 00 00 00 00 00 00 00`), so `TryGetUInt64` returns `NonCanonicalInteger` for it; read it as a byte string. The logs bloom is a 256-byte string (`b9 01 00` + 256 bytes), not a `Hash32`. Pre-Byzantium receipts carry a 32-byte state root where later receipts carry a status.
- The RLP layer checks structure only. Field count, field order, field meaning and signature recovery belong to your code.

## Reading a legacy transaction

```csharp
RLPStatus status = RLPData.TryDecodeComplete(bytes, RLPDecodeLimits.Default, out RLPData root, out RLPError error);
if (status != RLPStatus.Success) return Reject(status);

Span<RLPRange> fields = stackalloc RLPRange[9];
status = root.TryIndexDirectChildren(fields, out RLPChildrenIndex index, out int count);
if (status != RLPStatus.Success || count != 9) return Reject();   // IndexCapacityExceeded means more than 9 fields

index.TryGetAt(0, out RLPData nonce);
index.TryGetAt(1, out RLPData gasPrice);
index.TryGetAt(2, out RLPData gasLimit);
index.TryGetAt(3, out RLPData to);
index.TryGetAt(4, out RLPData value);
index.TryGetAt(5, out RLPData input);
index.TryGetAt(6, out RLPData v);
index.TryGetAt(7, out RLPData r);
index.TryGetAt(8, out RLPData s);

nonce.TryGetUInt64(out ulong nonceValue);
value.TryGetUInt256(out uint256 amount);
to.TryGetOptionalAddress(out Address recipient, out bool isCreation);
input.TryGetFunctionSelector(out FunctionSelector selector);   // InvalidFixedLength for calldata under 4 bytes
RLPTypedExtensions.TryGetEvmLegacySignature(v, r, s, out Signature signature, out ulong? chainId);
// signature.V is the y parity (0 or 1); chainId is null for a pre-EIP-155 signature (v of 27 or 28)
```

Check every status in real code; the sketch omits them for length. Reading the nine fields in order with `TryGetChildren` skips the index build, which cost about 26 ns for nine fields in the benchmarks (see `docs/rlp-performance.md`); use the index when you need a late field out of order.

Notes that affect correctness:

- **`to` is optional.** An empty string means contract creation. `TryGetAddress` rejects it; use `TryGetOptionalAddress`.
- **Calldata is opaque.** `TryGetFunctionSelector` reads the first four bytes of a longer payload and leaves the rest alone. Empty and 1 to 3 byte calldata are legal; branch on `PayloadLength` before treating `InvalidFixedLength` as a rejection.
- **`v` carries the chain id.** In a legacy transaction `v = 35 + 2 * chainId + parity` (or 27/28 before EIP-155), so it is usually several bytes long (Plume, chain id 98866: `v` is `83 03 04 87` or `83 03 04 88`). `TryGetEvmLegacySignature` splits it into `Signature.V` (the y parity, 0 or 1) and a `ulong?` chain id, for any chain id up to `ulong.MaxValue`. Check the chain id against the one your node serves; the codec does not. A `v` that is not 27, 28 or at least 35 is `InvalidSignatureV`.
- **Typed transactions** (EIP-2930, EIP-1559, EIP-4844, EIP-7702) carry the y parity in its own field, `0` or `1`. Read it with `TryGetEvmYParitySignature`; their chain id is a separate `uint256` field read with `TryGetUInt256`.
- **`r` and `s` are integers, not hashes.** They have no leading zero bytes on the wire. Both readers pad them to 32 bytes.

## Signing and hashing: bind the exact bytes

The signing hash and the transaction hash must be computed over the bytes as received, or over the bytes you wrote, not over a value you decoded and re-encoded.

- To hash a received transaction, hash the original buffer (or `root.Encoded`).
- To sign, build the unsigned item with `RLPWriter`, then hash `buffer[..written]`.
- To embed an item you received inside a new list unchanged, use `writer.TryWriteEncoded(in item)`; it copies the exact bytes.

## Writing a signed legacy transaction

The framed writer needs each list's exact children length before the first child is written. Compute it with the sizing helpers (they never allocate or throw, and each agrees with what the matching writer emits), then write.

```csharp
// A signed EIP-155 legacy transaction on Plume (chain id 98866), contract call with data.
ulong nonce = 9, gasLimit = 21_000, chainId = 98_866;
Address to = Address.FromEvmBytes(new byte[20]);   // the recipient (use writer.TryWriteByteString(default) for contract creation)
uint256 r = default, s = default;                       // the signature integers from your signer
uint256 gasPrice = new uint256(20_000_000_000UL), value = uint256.One;
byte yParity = 1;                                  // 0 or 1, from your signer
ReadOnlySpan<byte> data = stackalloc byte[] { 0xa9, 0x05, 0x9c, 0xbb };

RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(yParity, chainId, out int vLength);   // 4 bytes: 83 03 04 88
RLPEncoder.TryGetByteStringLength(data, out int dataLength);

int children =
    RLPEncoder.GetUInt64EncodedLength(nonce)
    + RLPTypedExtensions.GetUInt256EncodedLength(gasPrice)
    + RLPEncoder.GetUInt64EncodedLength(gasLimit)
    + RLPTypedExtensions.AddressEncodedLength
    + RLPTypedExtensions.GetUInt256EncodedLength(value)
    + dataLength
    + vLength
    + RLPTypedExtensions.GetUInt256EncodedLength(r)
    + RLPTypedExtensions.GetUInt256EncodedLength(s);
RLPEncoder.TryGetListLength(children, out int total);

Span<byte> buffer = total <= 512 ? stackalloc byte[total] : new byte[total];
Span<int> frames = stackalloc int[1];
var writer = new RLPWriter(buffer, frames);

writer.TryBeginList(children);
writer.TryWriteUInt64(nonce);
writer.TryWriteUInt256(gasPrice);
writer.TryWriteUInt64(gasLimit);
writer.TryWriteAddress(to);                        // empty `to` (contract creation): writer.TryWriteByteString(default)
writer.TryWriteUInt256(value);
writer.TryWriteByteString(data);
writer.TryWriteEvmLegacyV(yParity, chainId);       // 35 + 2 * 98866 + 1 = 197768
writer.TryWriteUInt256(r);
writer.TryWriteUInt256(s);
writer.TryEndList();
writer.TryComplete(out int written);               // written == total
```

Check every status in real code. For an unsigned EIP-155 signing preimage write `chainId`, `0`, `0` in place of `v`, `r`, `s` (the integers 1, 0, 0 for chain id 1), which is what `EvmTransactionIntegrationTests` does against the published EIP-155 vector.

## Writing a signed typed transaction (EIP-1559)

The type byte is not part of the RLP item: write it yourself, then give the writer the rest of the buffer. Hash and sign over `type || payload` exactly as written.

```csharp
ulong chainId = 98_866, nonce = 3, gasLimit = 21_000;
Address to = Address.FromEvmBytes(new byte[20]);
ReadOnlySpan<byte> data = default;                  // empty calldata
uint256 r = default, s = default;                        // the signature integers from your signer
byte yParity = 1;                                   // 0 or 1, from your signer
uint256 maxPriorityFee = new uint256(1_000_000_000UL), maxFee = new uint256(30_000_000_000UL), value = uint256.One;

int children =
    RLPEncoder.GetUInt64EncodedLength(chainId)
    + RLPEncoder.GetUInt64EncodedLength(nonce)
    + RLPTypedExtensions.GetUInt256EncodedLength(maxPriorityFee)
    + RLPTypedExtensions.GetUInt256EncodedLength(maxFee)
    + RLPEncoder.GetUInt64EncodedLength(gasLimit)
    + RLPTypedExtensions.AddressEncodedLength
    + RLPTypedExtensions.GetUInt256EncodedLength(value)
    + 1                                             // empty data: 80
    + 1                                             // empty access list: c0
    + RLPEncoder.GetUInt64EncodedLength(yParity)
    + RLPTypedExtensions.GetUInt256EncodedLength(r)
    + RLPTypedExtensions.GetUInt256EncodedLength(s);
RLPEncoder.TryGetListLength(children, out int listLength);

Span<byte> buffer = new byte[1 + listLength];       // type byte, then the RLP list
buffer[0] = 0x02;                                   // EIP-2718 type, not part of the RLP item
Span<int> frames = stackalloc int[2];               // the transaction list plus the access list
var writer = new RLPWriter(buffer.Slice(1), frames);

writer.TryBeginList(children);
writer.TryWriteUInt64(chainId);
writer.TryWriteUInt64(nonce);
writer.TryWriteUInt256(maxPriorityFee);
writer.TryWriteUInt256(maxFee);
writer.TryWriteUInt64(gasLimit);
writer.TryWriteAddress(to);
writer.TryWriteUInt256(value);
writer.TryWriteByteString(data);                    // empty here
writer.TryBeginList(0);                             // empty access list
writer.TryEndList();
writer.TryWriteEvmYParity(yParity);
writer.TryWriteUInt256(r);
writer.TryWriteUInt256(s);
writer.TryEndList();
writer.TryComplete(out int written);                // written == listLength
```

Check every status in real code. For a typed transaction the signing preimage is `type || rlp([chain_id, nonce, ..., access_list])` without `y_parity`, `r` and `s`; build it the same way. To read it back, decode `buffer[1..]` (the first byte is the type), read the chain id with `TryGetUInt64` (or `TryGetUInt256` for EIP-7702 tuples), and read `y_parity`, `r`, `s` with `TryGetEvmYParitySignature`.

Encoded sizes of the common fields (the helpers above compute these; the table is for reading hex by eye):

| Field | Bytes |
| --- | --- |
| integer 0 | 1 (`80`) |
| integer 1..127 | 1 |
| integer with `n` byte magnitude (`n >= 1`, value `>= 128`) | `1 + n` |
| `Address` | 21 |
| `Hash32` | 33 |
| empty `to` | 1 (`80`) |
| legacy `v`, no chain id | 1 (27 or 28) |
| legacy `v`, chain id `c` | `1 + n` where `n` is the byte count of `35 + 2c + parity` (chain 1: 1 byte; chain 137: `82 01 35` or `82 01 36`, 3 bytes; chain 98866: 4 bytes; `ulong.MaxValue`: 10 bytes) |
| list with children of `n <= 55` bytes | `1 + n`; otherwise `1 + (bytes in n) + n` |

`TryWriteHash32` keeps leading zero bytes. It is right for a 32-byte string (a hash, a storage key) and wrong for `r`, `s` or any quantity: those are integers, so use `TryWriteUInt256`.

## Resource choices

**`RLPDecodeLimits.Default` (1 MiB, depth 32, 8,192 items) rejects some valid data.** It suits a single transaction from an untrusted peer; it is not a protocol maximum. Reproduced in review: an EIP-1559 payload with one access-list entry and 8,800 storage keys (290,433 bytes) returns `TooManyItems`, because every key is an item; a network-form blob transaction with 8 blobs returns `TooLarge` (6 and 7 blobs decode). Full block bodies and receipt lists hold far more than 8,192 items and can exceed 1 MiB, so decoding them needs limits sized to the block, not the default.

Derive each limit from the structure you expect, not from a round number:

- **`MaxEncodedBytes`**: your node's transaction (or message) size cap. The default 1 MiB is a ceiling for a single transaction, not a typical value. An EIP-4844 blob transaction in its network form carries whole blobs of 131,072 bytes each, so a payload with about 8 blobs or more exceeds 1 MiB and needs a larger limit on that path only.
- **`MaxItems`**: every item takes at least one byte, so `MaxItems` never needs to exceed `MaxEncodedBytes`. Budget from the schema: an EIP-2930 or EIP-1559 access list costs one item per address entry (the tuple, the address and the key list) plus one item per storage key, so a list with `N` storage keys needs about `N + 3 * entries` items and `33 * N` bytes. A budget of "a few hundred" would reject legitimate transactions with large access lists.
- **`MaxListDepth`**: the deepest nesting your schema defines, plus no slack for the codec. A legacy transaction is depth 1; an EIP-1559 access list reaches depth 4 (transaction, access list, entry, storage keys). Never exceed `RLPDecodeLimits.HardMaxListDepth` (64).

These are derivations, not measured recommendations. Block bodies, receipts and trie nodes have their own bounds; set limits from your protocol's rules.
