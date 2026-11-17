# RLP API

Namespace `EccentricWare.Web3.DataTypes.Rlp`. A strict, allocation-free RLP reader and writer built on `ReadOnlySpan<byte>` and `ref struct` views. Wire rules are in `docs/rlp-wire-format.md`; security boundaries in `docs/rlp-security.md`.

## Rules that apply to every member

1. **No exceptions on bad input.** Every `Try*` method returns an `RLPStatus` and never throws for any bytes, limits or arguments. Deny on every status other than `Success`. A non-success status is a diagnostic, not an authorisation decision. The only exceptions are two `InvalidOperationException` cases that signal a caller bug, not bad input: reading `RLPChildEnumerator.Current` outside a valid position, and `MoveNext` on a document whose bytes were modified after validation (the enumerator has no status channel).
2. **No heap allocation** on the decode, traversal, index, encode and typed-adapter paths. The caller supplies all buffers.
3. **Validate once, then trust.** `RLPData.TryDecodeComplete` checks the whole document, every nested list included. Views derived from a valid `RLPData` are not re-validated.
4. **Failure leaves outputs defined.** Reader `out` values are `default` on failure. Writer and encoder calls are atomic: on failure no destination byte and no writer field changes.
5. **Safe defaults.** `default(RLPData)`, `default(RLPChildrenIndex)` and `default(RLPWriter)` are invalid and reject every operation. `default(RLPChildEnumerator)` exposes no data: `MoveNext()` returns `false` (it behaves as an empty list) and `Current` throws.

## Ownership and lifetime

- **Input buffer.** A `ReadOnlySpan<byte>` does not freeze its backing array. Keep the encoded bytes unchanged and unshared with concurrent writers for the lifetime of every `RLPData`, enumerator and index derived from them. No behaviour is promised after the bytes change, other than that traversal members never read outside the document.
- **Ranges are document-relative.** `EncodedRange` and `PayloadRange` of a child at any depth are offsets from the first byte of the originally decoded buffer. An `RLPRange` is a number pair, not a capability: no API accepts a caller-built range as a trusted boundary.
- **Index workspace.** The `Span<RLPRange>` passed to `TryIndexDirectChildren` belongs to the caller. Do not modify, recycle or share it while the `RLPChildrenIndex` is in use, and do not keep an index across requests.
- **Writer buffers.** The destination and the frame workspace must stay stable and unmodified for the writer's lifetime. A source passed to a write must not overlap the bytes about to be written; in-place encoding is not supported.
- **ref structs.** All views are `ref struct`; they cannot be stored in fields of classes, captured by lambdas or used across `await`.

## Resource limits

```csharp
public readonly record struct RLPDecodeLimits(int MaxEncodedBytes, int MaxListDepth, int MaxItems)
{
    public const int HardMaxListDepth = 64;
    public static RLPDecodeLimits Default { get; }   // 1 MiB, depth 32, 8,192 items
    public bool IsValid { get; }
}
```

| Field | Meaning | Valid range | Exceeded |
| --- | --- | --- | --- |
| `MaxEncodedBytes` | document length | `> 0` | `TooLarge` |
| `MaxListDepth` | list nesting; root list is 1, root string 0 | `1..64` | `TooDeep` |
| `MaxItems` | root + every nested list + every string | `> 0` | `TooManyItems` |

`default(RLPDecodeLimits)` is all zeros and is rejected with `InvalidArgument`. Start from `Default` and tighten for untrusted peers. Limits are application policy; choose them for your protocol, not for the RLP format.

## Reading

```csharp
public readonly ref struct RLPData
{
    public bool IsValid { get; }
    public RLPKind Kind { get; }                       // None, ByteString, List
    public ReadOnlySpan<byte> Encoded { get; }         // prefix included
    public ReadOnlySpan<byte> Payload { get; }         // for a literal byte 00..7f, the byte itself
    public RLPRange EncodedRange { get; }
    public RLPRange PayloadRange { get; }
    public int EncodedLength { get; }
    public int PayloadLength { get; }

    public static RLPStatus TryDecodeComplete(ReadOnlySpan<byte> input, scoped in RLPDecodeLimits limits,
        out RLPData document, out RLPError error);
    public RLPStatus TryGetChildren(out RLPChildEnumerator children);
    public RLPStatus TryIndexDirectChildren(Span<RLPRange> workspace, out RLPChildrenIndex index, out int requiredCount);
    public RLPStatus TryGetUInt64(out ulong value);
    public RLPStatus TryCopyEncodedTo(Span<byte> destination, out int written);
}
```

`RLPError` is `(RLPStatus Status, int Offset)`: the failure and the byte offset in `input` where it was found. It is default on success.

### Decode

```csharp
RLPStatus status = RLPData.TryDecodeComplete(bytes, RLPDecodeLimits.Default, out RLPData root, out RLPError error);
if (status != RLPStatus.Success)
{
    return Reject(status, error.Offset);
}
```

`TryDecodeComplete` requires exactly one root item and no trailing bytes. Trailing data is reported as `TrailingData` before the contents of the root are examined.

### Enumerate direct children

```csharp
if (root.TryGetChildren(out RLPChildEnumerator children) != RLPStatus.Success)
{
    return Reject();   // WrongKind for a string, InvalidArgument for default
}

while (children.MoveNext())
{
    RLPData item = children.Current;
    // item.Kind, item.Payload, item.TryGetUInt64(...), nested item.TryGetChildren(...)
}
```

`Current` throws `InvalidOperationException` outside a valid position (before the first `MoveNext`, after the end, on `default`). That is a programming error, not an input error. `MoveNext` also throws `InvalidOperationException` if the document bytes were modified after validation so that a child no longer fits; the enumerator has no status channel, so this is the only signal. It never reads outside the document.

### Index for positional access

```csharp
Span<RLPRange> ranges = stackalloc RLPRange[16];
RLPStatus status = root.TryIndexDirectChildren(ranges, out RLPChildrenIndex index, out int required);
// IndexCapacityExceeded: `required` is the exact child count; the workspace is untouched. Retry with required slots.
if (status == RLPStatus.Success && index.TryGetAt(3, out RLPData field) == RLPStatus.Success) { /* O(1) */ }
```

An empty list succeeds with a zero-length workspace and yields a valid index of count 0. `TryGetAt` returns `InvalidArgument` for a negative or out-of-range ordinal and `InvalidListBoundary` if a stored entry no longer matches a child of the list.

Choose an index only when you read a late field several times. A single in-order pass is cheaper with the enumerator (see `docs/rlp-performance.md`).

### Integers and copying

`TryGetUInt64` accepts an empty string (0) and any canonical magnitude up to 8 bytes. Failures: `WrongKind` (list), `InvalidArgument` (`default`), `NonCanonicalInteger` (leading `00`, including the raw item `00`), `IntegerOverflow` (more than 8 bytes). `ulong.MaxValue` is valid. `value` is 0 on failure.

`TryCopyEncodedTo` copies the exact encoded bytes, prefix included, without normalising. Check order: valid item (`InvalidArgument`), capacity (`DestinationTooSmall`), overlap (`OverlappingBuffers`). Writing exactly onto the source counts as overlap.

## Typed adapters (`RLPTypedExtensions`)

These are thin extensions over the core and apply no schema (field counts, field positions and EIP-2718 type bytes are the caller's concern).

| Member | Reads / writes | Notes |
| --- | --- | --- |
| `TryGetUInt256` | integer up to 32 bytes | `NonCanonicalInteger` on a leading zero; `IntegerOverflow` above 32 bytes |
| `TryGetHash32` | exactly 32 bytes | `InvalidFixedLength` otherwise |
| `TryGetAddress` | exactly 20 bytes (EVM) | `InvalidFixedLength` otherwise, including empty |
| `TryGetOptionalAddress(out value, out isEmpty)` | empty (contract creation) or 20 bytes | for the transaction `to` field |
| `TryGetFunctionSelector` | first 4 bytes of a string of at least 4 bytes | the rest stays opaque; shorter is `InvalidFixedLength` |
| `TryGetEvmLegacySignature(v, r, s, out Signature, out ulong? chainId)` | legacy `v`, `r`, `s` | `chainId` is `null` for `v` of 27 or 28, else `(v - 35) / 2` (any chain id up to `ulong.MaxValue`); `Signature.V` is the y parity, 0 or 1 |
| `TryGetEvmYParitySignature(yParity, r, s, out Signature)` | typed-transaction y parity (empty or `01`), `r`, `s` | `Signature.V` is the y parity, 0 or 1 |
| `TryWriteUInt256(this ref RLPWriter, uint256)` | canonical integer | zero is `80` |
| `TryWriteHash32` | 32-byte string (`a0` + 32 bytes) | keeps leading zeros; never use it for `r`/`s` |
| `TryWriteAddress` | 20-byte string (`94` + 20 bytes) | a Solana address is `InvalidArgument` |
| `TryWriteEvmLegacyV(yParity, ulong? chainId)` | legacy `v` | `27 + parity` without a chain id, else `35 + 2 * chainId + parity` (computed in 128 bits, so no chain id overflows); parity above 1 is `InvalidSignatureV` |
| `TryWriteEvmYParity(yParity)` | typed y parity | parity above 1 is `InvalidSignatureV` |
| `TryWriteEvmLegacyV(in Signature, ulong? chainId)`, `TryWriteEvmYParity(in Signature)` | the same items, from a `Signature` | `Signature.V` of 0 or 1 (a parity) or 27 or 28 (parity `V - 27`); any other `V` is `InvalidSignatureV`; a Solana signature is `InvalidArgument` |

Wrong kind returns `WrongKind` and `default` returns `InvalidArgument` for every reader.

### Signature fields: `v`, chain id and `Signature.V`

Chain ids are large on almost every real network (Polygon 137, Base 8453, Arbitrum One 42161, Plume 98866), and an EIP-155 `v` is `35 + 2 * chainId + parity`, so `v` rarely fits one byte (Plume: `v` is 197767 or 197768, `83 03 04 87` or `83 03 04 88`). The adapters therefore split the wire value instead of storing it:

- `Signature.V` is the **y parity (0 or 1)** from both readers. Add 27 for the common 65-byte `r||s||v` form.
- The legacy reader returns the chain id as `ulong?`: `null` for a pre-EIP-155 signature (`v` of 27 or 28), `0` for `v` of 35 or 36, otherwise the chain id.
- Legacy `v` must be 27, 28 or at least 35 (go-ethereum's `isProtectedV` and `deriveChainId` rules). Anything else, including the empty string, is `InvalidSignatureV`. A chain id above `ulong.MaxValue`, or a `v` longer than 9 bytes, is `IntegerOverflow`. A leading zero byte is `NonCanonicalInteger`, checked before the width.
- A typed y parity must be the empty string (0) or `01` (1); another canonical integer of at most 8 bytes is `InvalidSignatureV`.
- Fields are checked in the order `v`, `r`, `s`; the first failure is returned and the outputs are default (`chainId` is `null`).
- The reader does not check that the chain id is the one your node expects, that `r` and `s` are in the curve order, or that the signature recovers a sender. Those are the caller's checks.

There is deliberately no whole-signature writer: it would write three items and could not be atomic. Write the three fields in order:

```csharp
// legacy: parity and chain id (null for a pre-EIP-155 signature)
writer.TryWriteEvmLegacyV(signature.V, chainId);   // signature.V must be the parity, 0 or 1
writer.TryWriteUInt256(signature.R.ToUInt256());
writer.TryWriteUInt256(signature.S.ToUInt256());

// typed transaction
writer.TryWriteEvmYParity(signature.V);
writer.TryWriteUInt256(signature.R.ToUInt256());
writer.TryWriteUInt256(signature.S.ToUInt256());
```

The `byte` writer overloads are strict: a parity above 1 is `InvalidSignatureV` and nothing is written. The `Signature` overloads, `TryWriteEvmYParity(in Signature)`, `TryWriteEvmLegacyV(in Signature, ulong? chainId)` and `TryGetEvmLegacyVEncodedLength(in Signature, ulong?, out int)`, accept a `Signature.V` of 0 or 1 (a parity) and of 27 or 28 (the common 65-byte `r||s||v` form, parity `V - 27`), so a signature from a wallet or from `Signature.FromEvmBytes` can be written directly. Any other `V` is `InvalidSignatureV`, and a Solana signature is `InvalidArgument`.

**Pitfalls of `V` being a parity.** An adapter-built `Signature` has `V` of 0 or 1, so:

- `Equals` and `==` include `V`. A signature the legacy reader builds from `v` of 27 has `V` of 0, so it is not equal to the same `r` and `s` built from a wallet signature whose `V` is 27. Compare `R`, `S` and the parity explicitly when signatures from both sources are mixed.
- `ToString()` and `ToBytes()` of an adapter-built signature end in `00` or `01`, not `1b` or `1c`. Some verifiers (for example Solidity code that checks `v` against 27 and 28) accept only the 27/28 form.
- To get the 27/28 form, rebuild the value; `Signature` is immutable:

```csharp
Span<byte> r = stackalloc byte[32], s = stackalloc byte[32];
signature.R.WriteBigEndian(r);
signature.S.WriteBigEndian(s);
Signature wallet = Signature.FromEvmComponents(r, s, (byte)(signature.V + 27));
```

- `TryWriteEvmLegacyV(parity, chainId: 0)` means chain id 0 (`v` of 35 or 36), not "no chain id". Pass `null` for a pre-EIP-155 signature.

### Sizing helpers for announcing list lengths

The framed writer needs the exact byte length of a list's children before the first child is written. These helpers compute it without writing, never allocate and never throw, and each agrees exactly with what the matching writer emits:

| Helper | Returns |
| --- | --- |
| `RLPEncoder.TryGetByteStringLength(payload, out total)` | size of a byte string |
| `RLPEncoder.TryGetListLength(childrenLength, out total)` | size of a list |
| `RLPEncoder.GetUInt64EncodedLength(value)` | 1 to 9 |
| `RLPEncoder.TryGetUnsignedMagnitudeLength(magnitude, out total)` | size of a big-endian magnitude; `NonCanonicalInteger` for a leading zero |
| `RLPTypedExtensions.GetUInt256EncodedLength(value)` | 1 to 33 |
| `RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(parity, chainId, out total)` | size of the legacy `v` item; `InvalidSignatureV` for parity above 1 |
| `RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(in Signature, chainId, out total)` | the same size from a `Signature` (`V` of 0, 1, 27 or 28); `InvalidSignatureV` for another `V`, `InvalidArgument` for a Solana signature |
| `RLPTypedExtensions.AddressEncodedLength`, `Hash32EncodedLength` | constants 21 and 33 |

## Writing

### One-shot (`RLPEncoder`)

```csharp
public static class RLPEncoder
{
    public static RLPStatus TryGetByteStringLength(ReadOnlySpan<byte> payload, out int totalLength);
    public static RLPStatus TryGetListLength(int encodedChildrenLength, out int totalLength);
    public static RLPStatus TryWriteByteString(ReadOnlySpan<byte> payload, Span<byte> destination, out int written);
    public static RLPStatus TryWriteUInt64(ulong value, Span<byte> destination, out int written);
    public static RLPStatus TryWriteUnsignedMagnitude(ReadOnlySpan<byte> minimalBigEndianMagnitude,
        Span<byte> destination, out int written);
}
```

Check order, all before any write: argument and minimality, exact size (`LengthOverflow`), capacity (`DestinationTooSmall`), overlap (`OverlappingBuffers`). `written` is 0 on failure. `TryWriteUnsignedMagnitude` rejects a leading zero byte with `NonCanonicalInteger`; the empty magnitude encodes `80`.

### Framed (`RLPWriter`)

The writer builds one root item. A list is announced with the exact byte length of its already-encoded children, so each header is written once and nothing is moved afterwards.

```csharp
Span<byte> buffer = stackalloc byte[128];
Span<int> frames = stackalloc int[4];          // maximum list depth
var writer = new RLPWriter(buffer, frames);

// [ 1, address ]: children are 1 byte + 21 bytes = 22
if (writer.TryBeginList(22) != RLPStatus.Success) { /* handle */ }
writer.TryWriteUInt64(1);
writer.TryWriteAddress(address);
writer.TryEndList();
RLPStatus status = writer.TryComplete(out int written);   // buffer[..written] is the encoding
```

Rules:

- Construct with `new RLPWriter(destination, listEndWorkspace)`. `listEndWorkspace.Length` is the maximum nesting depth; one more list returns `TooDeep`.
- Pass the writer by `ref` and never copy it once in use. Do not call members through an `in` parameter or a `readonly` field: the compiler would mutate a copy.
- Members: `TryBeginList(int)`, `TryWriteByteString`, `TryWriteUInt64`, `TryWriteUnsignedMagnitude`, `TryWriteEncoded(in RLPData)`, `TryEndList()`, `TryComplete(out int)`.
- `TryEndList` returns `UnbalancedList` unless exactly the announced child bytes were written. The frame stays open, so you can write the missing children and retry.
- A list is checked against its parent's announced limit when it is opened, so children can never cross a parent. At the root the limit is the end of the destination.
- Item-adding check order: state (`WriterStateInvalid`), root discipline (`ExtraRoot`), argument and minimality (`InvalidArgument`, `NonCanonicalInteger`), total size (`LengthOverflow`), parent limit (`InvalidListBoundary` inside a list, `DestinationTooSmall` at the root), free frame slot (`TooDeep`), overlap (`OverlappingBuffers`).
- `TryComplete` returns `UnbalancedList` with an open list and `EmptyInput` when nothing was written; after success every call returns `WriterStateInvalid`.
- `TryWriteEncoded` copies a validated item verbatim, which is how you re-emit a signed payload exactly as received.

## Deviations and choices where the design specification is open or differs

`docs/specs/RLPData_DotNet_Design_Specification_v2.0.md` was written without knowledge of this repository. The implementation follows it, with these differences. The only edits made to the spec file are the `InvalidFixedLength` and `InvalidSignatureV` statuses in the section 4 enum listing and the section 8 table; the other items below are recorded here only.

- **Namespace and layout.** The spec's `Evm.Rlp` namespace and separate projects are not used: the code is namespace `EccentricWare.Web3.DataTypes.Rlp` inside the existing library, tests are in the existing test project (the spec's separate differential and fuzz projects are folders there), and the documents are `docs/rlp-*.md`. There is no `global.json` (the SDK pin is the owner's decision).
- **Lifetime annotations.** `RLPData.TryDecodeComplete` takes `scoped in RLPDecodeLimits limits` (the spec shows `in`), and `RLPWriter.TryWriteByteString` and `TryWriteUnsignedMagnitude` take `scoped ReadOnlySpan<byte>`. The C# compiler requires `scoped` (CS8350/CS8352) when a caller passes a `stackalloc` span or a local into a `ref struct` API. This changes no behaviour.
- **Capacity is checked before overlap** in `RLPEncoder.TryWriteByteString`, `RLPEncoder.TryWriteUnsignedMagnitude` and the writer (spec 7.2 lists overlap first). The output region is undefined while the destination is too small, so overlap cannot be judged first. Example: a payload that is a slice of a 60-byte destination needs 62 bytes, and returns `DestinationTooSmall`, not `OverlappingBuffers`. Both are rejected with no mutation. `TryCopyEncodedTo` and the writer's other members use the same order.
- **Item budget is checked before the depth budget** for the same item. `c2 c1 c0` with `MaxListDepth` 2 and `MaxItems` 2 returns `TooManyItems`; the spec text is ambiguous.
- **`TryComplete` with nothing written** returns `EmptyInput`; the spec leaves that status open.
- **Writer frame workspace exhaustion** returns `TooDeep`, as decided for this repository.
- **Typed adapters and `RLPDecodeLimits.Default`** (1 MiB, depth 32, 8,192 items) are additions, not part of the spec.

## Status reference

| Status | Typical cause |
| --- | --- |
| `Success` | complete |
| `InvalidArgument` | invalid limits, negative size, bad ordinal, `default` view |
| `EmptyInput` | no root item |
| `TruncatedInput` | prefix, length bytes or payload cut short |
| `NonCanonicalEncoding` | redundant string or length form |
| `TrailingData` | bytes after the root |
| `InvalidListBoundary` | child overruns its list |
| `LengthOverflow` | length does not fit `int` |
| `TooLarge`, `TooDeep`, `TooManyItems` | a decode limit; `TooDeep` is also the writer frame workspace being full |
| `WrongKind` | list operation on a string or the reverse |
| `NonCanonicalInteger`, `IntegerOverflow` | integer rules |
| `DestinationTooSmall`, `OverlappingBuffers` | output buffer problems |
| `IndexCapacityExceeded` | index workspace too small; `requiredCount` is exact |
| `WriterStateInvalid`, `UnbalancedList`, `ExtraRoot` | writer state machine |
| `InvalidFixedLength` | typed adapter width mismatch |
| `InvalidSignatureV` | signature recovery value out of domain: typed y parity not 0 or 1, legacy `v` not 27, 28 or at least 35, or (writers taking a `Signature`) `Signature.V` not 0, 1, 27 or 28 |
