# RLPData — Zero-Copy Recursive-Length Prefix Codec for .NET

**Specification:** 2.0 — final standalone implementation specification  
**Date:** 16 November 2026  
**Target platform:** .NET 10 LTS, C# 14, x64 and ARM64, Windows and Linux  
**Proposed package/namespace:** `Evm.Rlp`  
**Principal public type:** `RLPData`  
**Status:** Design and acceptance contract; implementation, compilation and benchmark results are not asserted

---

## 1. Purpose and design contract

Build a compact, defensive RLP reader and encoder for Ethereum-related applications, particularly latency-sensitive transaction-policy and signing services. The library MUST read and write **canonical Ethereum RLP** without reflection, object graphs, per-field allocations or unbounded recursion. Parsing MUST distinguish well-formed generic RLP from higher-layer application semantics, including Ethereum's integer rules and EVM transaction schemas.

`RLPData` is a **read-only, stack-scoped view of one fully validated item** in an existing, stable encoded buffer. It is not an object-tree builder and does not own bytes. Its public construction path validates the complete RLP root, including every nested list. Its children reuse the same backing buffer and document-relative coordinate system. Canonical new values are written using separate, allocation-free encoding primitives and a checked nested-list writer; a validated `RLPData` can also be copied verbatim to a destination.

### 1.1 Mandatory outcomes

1. Canonical strings, empty strings, lists and arbitrary nested valid lists round-trip exactly, subject only to explicit resource limits.
2. Every public value treated as validated is produced by the strict validator or by traversal of an already validated value. Arbitrary `(offset, length)` input is never promoted into a trusted child by bounds checking alone.
3. All accesses use span slicing with checked or subtraction-based bounds verification. The common decode/enumerate/index/encode paths allocate **zero managed bytes** after warm-up, excluding storage provided by callers.
4. Validation and encoding are deterministic; malformed untrusted bytes return status codes rather than propagating input-triggered exceptions.
5. No hidden copies, object trees, dynamic ABI resolution, transaction parsing, cryptography or network access occur inside the codec.
6. Encoding into caller storage is canonical, pre-sized and bounded; unsuccessful atomic writes preserve output bytes and writer state.
7. Failure, ownership, concurrency, resource limits, performance measurement, testing and hand-off criteria are specified sufficiently for an independent coding agent.

### 1.2 Explicit non-goals

This component does **not** implement Solidity ABI encoding/decoding, EIP-712, EVM execution, an EVM transaction model, EIP-2718 dispatch, transaction signing or EVM policy decisions. Hexadecimal text conversion is performed at the application's input boundary. It does not promise that an attacker-controlled byte array remains immutable, nor that RLP-valid bytes describe a valid transaction.

No public permissive decoder, arbitrary-object serializer, generic byte-stream parser, unsafe code, pooling scheme or `IBufferWriter<byte>` integration is required for the first deliverable. Optional adapters can be introduced behind separate interfaces after evidence of demand.

### 1.3 Normative vocabulary

**MUST** is required for conformance; **SHOULD** is expected unless a documented, tested reason exists to depart; **MAY** is optional. A *document* is exactly one top-level RLP item, without trailing bytes. An *item* is either a byte string or a list. A *payload* is the byte content after the item's RLP prefix; a list payload is the concatenation of complete encoded child items. A *validated view* refers to bytes that have passed strict whole-document structural validation **and have remained unchanged since validation**.

---

## 2. Normative RLP wire rules

RLP has two kinds: **byte string** and **list**. The single-byte form is a string, not a third kind. Encoded items are never empty; empty string is `80`, empty list is `c0`.

| First byte | Meaning | Prefix size | Payload size |
|---|---|---:|---|
| `00..7f` | Literal single-byte string | 0 | 1 (the initial byte itself) |
| `80..b7` | Short string | 1 | `prefix - 0x80` |
| `b8..bf` | Long string | `1 + (prefix - 0xb7)` | Big-endian length, 1–8 bytes |
| `c0..f7` | Short list | 1 | `prefix - 0xc0` |
| `f8..ff` | Long list | `1 + (prefix - 0xf7)` | Big-endian length, 1–8 bytes |

The long form is allowed only when payload length is at least **56 bytes**. Its length must use the shortest possible non-zero big-endian encoding, with no leading `00`. A payload length of `2^64` or more is not RLP-encodable; this library also rejects values that cannot fit an actual .NET span and configured budgets.

### 2.1 Canonical structural validation

The decoder MUST reject all of these, whether at the root or nested within a list:

- Short-string encoding of a single byte below `0x80` (e.g. `81 7f` rather than `7f`).
- Long-form strings or lists of payload length 55 or less (`b8 37 ...`, `f8 37 ...`).
- Long-form lengths with leading zero, omitted length bytes, advertised payload beyond available input or length above `int.MaxValue`.
- A child that exceeds the enclosing list's payload boundary, even if it fits in the overall document.
- An enclosing list whose body cannot be decomposed into complete, canonical child items.
- An incomplete root, multiple roots, trailing bytes, empty input and exceeded configured resource budgets.

The generic decoder MUST accept a literal `00` as the RLP byte string containing zero. **Only typed unsigned-integer accessors** reject that value when asked to interpret it as an integer zero.

### 2.2 Unsigned-integer semantics

Ethereum's unsigned integer representation is a shortest big-endian magnitude RLP-encoded as a string. `0` is the **empty string** `80`; `1` is `01`; `127` is `7f`; `128` is `81 80`; `256` is `82 01 00`.

- An empty byte-string payload represents integer zero.
- A nonempty payload beginning with `00` is **not** a canonical unsigned integer.
- Lists cannot be interpreted as integers.
- `TryGetUInt64` MUST reject byte-string payload lengths greater than 8 and reject overflows; `ulong.MaxValue` is valid.
- `TryWriteUnsignedMagnitude` MUST accept an empty magnitude for zero, and reject any nonempty magnitude whose first byte is zero. It MUST NOT silently normalise callers' nonminimal magnitudes.
- Signed integers and floating-point values are outside the standard codec API.

These rules DO NOT apply to raw RLP byte-string decoding or byte-for-byte copying.

### 2.3 Canonical reference vectors

All hex in this document is byte-oriented and case-insensitive. Spaces separate bytes only for readability.

| Semantic value | Exact encoded hex |
|---|---|
| Empty byte string | `80` |
| Empty list | `c0` |
| Raw string `[00]` | `00` |
| Raw string `[7f]` | `7f` |
| Raw string `[80]` | `81 80` |
| String `dog` (ASCII bytes) | `83 64 6f 67` |
| List `["cat", "dog"]` | `c8 83 63 61 74 83 64 6f 67` |
| List `[[], [[]]]` | `c3 c0 c1 c0` |
| Unsigned 0 | `80` |
| Unsigned 128 | `81 80` |
| Unsigned 256 | `82 01 00` |
| String of 55 `00` bytes | `b7` followed by 55 `00` bytes |
| String of 56 `00` bytes | `b8 38` followed by 56 `00` bytes |
| List payload of 55 bytes | `f7` followed by exactly 55 bytes of **valid encoded children** |
| List payload of 56 bytes | `f8 38` followed by exactly 56 bytes of **valid encoded children** |

The tests MUST construct the 55/56-byte list cases from valid children (e.g. 55 or 56 literal `01` items), not arbitrary unencoded filler.

---

## 3. Component architecture and invariant boundaries

```text
Stable caller-owned encoded buffer
          |
          v
RLPData.TryDecodeComplete  <---- RLPDecodeLimits
          |
          +--> internal TryReadHeader (single source of prefix rules)
          |          |
          |          +--> iterative strict validator (every nested item)
          |
          v
Valid root RLPData (borrowed; no heap tree)
          |
          +--> RLPChildEnumerator (sequential zero-copy views)
          +--> RLPChildrenIndex  (optional caller-backed direct-child cache)
          +--> TryGetUInt64     (semantic scalar accessor)
          +--> TryCopyEncodedTo (verbatim canonical bytes)

Canonical construction:
RLPEncoder (sizing/leaf writes) --> RLPWriter (framed lists) --> caller Span<byte>
```

The **internal header reader** alone understands RLP prefix families. The full validator, direct-child enumerator and index builder MUST share it, rather than maintain divergent decoding logic.

### 3.1 Validity states

There are three distinct states:

1. **Untrusted bytes:** no structural assumptions are permitted.
2. **Validated RLP view:** all structural canonicality and boundaries were checked on the original buffer; trusted only while the bytes are stable.
3. **Semantically validated higher-layer data:** a separate consumer has also checked integer widths, expected field count, addresses, chain requirements, signing rules and other context.

No API may silently convert state 1 into state 2 by inspecting only a header or by checking a range's bounds. State 2 does not imply state 3.

### 3.2 Range coordinate system

`RLPRange.Offset` is the **zero-based byte offset from the first byte of the originally decoded RLP document**. `RLPRange.Length` is the complete **encoded length** of an item, including its prefix and length bytes. It never means ABI-relative offset, child ordinal, pointer, string payload length or external transaction envelope offset.

A `RLPData` child retains the full original RLP document span internally, together with its start offset and header metadata. Therefore `child.EncodedRange` remains document-relative at any nesting level. `child.PayloadRange` is also document-relative but describes the bytes after the RLP prefix. For literal single-byte items, `EncodedRange` and `PayloadRange` coincide.

For a typed EVM transaction, the document begins **after** the envelope type byte; the outer transaction adapter separately records that envelope offset. Ranges cannot be reused against another document or an altered original buffer.

### 3.3 Buffer ownership and concurrency contract

A `ReadOnlySpan<byte>` prevents mutation through that span; it does **not** freeze the underlying array, pooled owner, memory-mapped region or other aliases. The caller MUST keep both the encoded input and caller-provided index/writer scratch storage stable for the lifetime of views using them. In a signing service:

1. Receive and size-limit input.
2. Convert hex, if needed, **once** into an exclusively owned byte buffer, or accept a buffer whose immutable ownership can be proven.
3. Validate, inspect and evaluate synchronously without exposing mutable aliases to concurrent writers.
4. Bind the authorisation record to the exact signing preimage or authorised digest, account, chain and policy version.
5. Ensure the signer checks this binding against the exact bytes it signs. Reuse of a pooled buffer before the signing decision completes is prohibited.

Crossing an `await` or job boundary SHOULD carry an owned `ReadOnlyMemory<byte>` plus explicit lifetime/lease and rebuild fresh stack-scoped views in the next synchronous region. C# 13+ permits `ref struct` locals in selected async/iterator scopes, but never permits them to escape their ref-safe lifetime or be live across a suspension point. Buffer ownership, not language syntax alone, is the security boundary.

---

## 4. Public API contract

The declarations below describe the **required source-level shape and behaviour**; implementation must compile the final API in the .NET 10 / C# 14 solution. Small mechanical signature adjustments MAY be made for C# compiler constraints only if tests and public documentation preserve every stated invariant.

```csharp
namespace Evm.Rlp;

public enum RLPKind : byte { None = 0, ByteString = 1, List = 2 }

public readonly record struct RLPRange(int Offset, int Length);

public readonly record struct RLPDecodeLimits(
    int MaxEncodedBytes,
    int MaxListDepth,
    int MaxItems);

public enum RLPStatus : byte
{
    Success = 0,
    InvalidArgument,
    EmptyInput,
    TruncatedInput,
    NonCanonicalEncoding,
    TrailingData,
    InvalidListBoundary,
    LengthOverflow,
    TooLarge,
    TooDeep,
    TooManyItems,
    WrongKind,
    NonCanonicalInteger,
    IntegerOverflow,
    DestinationTooSmall,
    IndexCapacityExceeded,
    OverlappingBuffers,
    WriterStateInvalid,
    UnbalancedList,
    ExtraRoot,
    InvalidFixedLength, // appended; used by the typed adapters (Hash32, Address, FunctionSelector), not by the core
    InvalidSignatureV // appended last; used by the typed signature adapters (y parity, legacy v), not by the core
}

public readonly record struct RLPError(RLPStatus Status, int Offset);

public readonly ref struct RLPData
{
    public bool IsValid { get; }
    public RLPKind Kind { get; }
    public ReadOnlySpan<byte> Encoded { get; }
    public ReadOnlySpan<byte> Payload { get; }
    public RLPRange EncodedRange { get; }
    public RLPRange PayloadRange { get; }
    public int EncodedLength { get; }
    public int PayloadLength { get; }

    public static RLPStatus TryDecodeComplete(
        ReadOnlySpan<byte> input,
        in RLPDecodeLimits limits,
        out RLPData document,
        out RLPError error);

    public RLPStatus TryGetChildren(out RLPChildEnumerator children);
    public RLPStatus TryIndexDirectChildren(
        Span<RLPRange> workspace,
        out RLPChildrenIndex index,
        out int requiredCount);
    public RLPStatus TryGetUInt64(out ulong value);
    public RLPStatus TryCopyEncodedTo(
        Span<byte> destination, out int written);
}

public ref struct RLPChildEnumerator
{
    public bool MoveNext();
    public RLPData Current { get; }
}

public readonly ref struct RLPChildrenIndex
{
    public bool IsValid { get; }
    public int Count { get; }
    public RLPStatus TryGetAt(int ordinal, out RLPData child);
}

public static class RLPEncoder
{
    public static RLPStatus TryGetByteStringLength(
        ReadOnlySpan<byte> payload, out int totalLength);
    public static RLPStatus TryGetListLength(
        int encodedChildrenLength, out int totalLength);
    public static RLPStatus TryWriteByteString(
        ReadOnlySpan<byte> payload, Span<byte> destination, out int written);
    public static RLPStatus TryWriteUInt64(
        ulong value, Span<byte> destination, out int written);
    public static RLPStatus TryWriteUnsignedMagnitude(
        ReadOnlySpan<byte> minimalBigEndianMagnitude,
        Span<byte> destination, out int written);
}

public ref struct RLPWriter
{
    public RLPWriter(Span<byte> destination, Span<int> listEndWorkspace);
    public RLPStatus TryBeginList(int encodedChildrenLength);
    public RLPStatus TryWriteByteString(ReadOnlySpan<byte> payload);
    public RLPStatus TryWriteUInt64(ulong value);
    public RLPStatus TryWriteUnsignedMagnitude(
        ReadOnlySpan<byte> minimalBigEndianMagnitude);
    public RLPStatus TryWriteEncoded(in RLPData validatedItem);
    public RLPStatus TryEndList();
    public RLPStatus TryComplete(out int written);
}
```

### 4.1 Construction and default values

- `RLPData` and `RLPChildrenIndex` MUST have **no public constructors** that accept raw bytes, ranges, kind or purported validation markers. Private/internal construction occurs only after validation/traversal or successful indexing, respectively.
- `default(RLPData)` and `default(RLPChildrenIndex)` are invalid; `Kind.None` means no value. Their accessors MUST not expose an apparently authorised item. `IsValid` MUST be explicit.
- `RLPData` may be copied as a small value struct; it does not copy bytes. Avoid promises about its precise runtime size; confirm with measurements on target runtimes.
- `RLPRange` contains two signed 32-bit offsets, giving 8 bytes of field data. Its value is a location descriptor, **not a certificate** that a child boundary was validated.
- `RLPChildEnumerator.Current` is defined only after `MoveNext()` returns `true` and before the next unsuccessful `MoveNext`. Misuse is a programmer error and MUST be documented; prefer a deterministic `InvalidOperationException` over a default-valid result.

### 4.2 Index trust and mutation

`TryIndexDirectChildren` accepts a **validated list view**, not arbitrary `root + listRange` coordinates. It performs a complete direct-child count pass and then, only if the workspace can hold all results, a population pass. On success it returns a `RLPChildrenIndex` tied internally to that same document and list and holding a `ReadOnlySpan<RLPRange>` view over the caller's workspace.

Because a `ReadOnlySpan` does not make its backing workspace immutable, **the caller MUST NOT modify, recycle or concurrently share that workspace while the index is in use**. The index is a local performance cache inside the validated-data trust boundary, not suitable for independent authorisation, untrusted serialisation or cross-request reuse. If an attacker can alter a cached index or the document bytes, the validation contract is void; the signer must independently bind the final authorised bytes.

`RLPChildrenIndex.TryGetAt` MUST reject negative/out-of-range ordinals and return a child retaining the original document coordinate base. A successful lookup is O(1) for locating the indexed range; creating a child view may re-read its local header at constant cost. It MUST NOT accept arbitrary caller-provided ranges as alternative field identities.

If workspace is too small, indexing MUST return `IndexCapacityExceeded`, set `requiredCount` to the exact number of direct children, return an invalid index and leave workspace **unchanged**. If the list is empty, zero capacity succeeds and returns a valid index of count zero. Any input-kind failure also leaves workspace unchanged and reports `requiredCount = 0`.

### 4.3 Failure output and exceptions

For all `Try*` operations, failure MUST initialise `out` values to invalid/default/zero, except that `requiredCount` intentionally reports a non-zero needed capacity on `IndexCapacityExceeded`. `RLPError.Offset` is relative to the original supplied input, including at `input.Length` when missing bytes would start there. Use `Offset = -1` for failures caused only by invalid configuration or API arguments, with no offending input byte.

No malformed input bytes may cause a managed exception on documented `Try*` paths. Exceptions MAY be raised for programming contract violations such as reading an enumerator `Current` in the wrong state, disposal/lifetime misuse in optional adapters or implementation bugs; these must not be represented as normal input errors.

`TryCopyEncodedTo` returns `RLPStatus` and does not mix a boolean success convention with the rest of the API. An empty but valid source item still has an encoded length of at least one byte.

---

## 5. Strict decoder

### 5.1 Header parser: one source of truth

Implement an internal non-allocating `TryReadHeader` over the **original document span**, taking an item start and an exclusive container end. Return a compact internal header (`kind`, `itemStart`, `headerLength`, `payloadStart`, `payloadLength`, `itemEnd`). For a literal byte `00..7f`, `headerLength = 0`, `payloadStart = itemStart`, `payloadLength = 1`, `itemEnd = itemStart + 1`.

**Mandatory order of checks:**

1. Validate `0 <= itemStart < containerEnd <= input.Length` before reading the first byte.
2. Classify the prefix into exactly one of the five families.
3. For long forms, verify the 1–8 length bytes are fully present **inside the enclosing container**, and that the first length byte is non-zero.
4. Accumulate the length into `ulong` using at most eight bounded shifts. Reject any length below 56 as noncanonical, and any length greater than `int.MaxValue` as `LengthOverflow`.
5. For short-string length 1, verify that its payload byte is at least `0x80`; otherwise reject as noncanonical.
6. Verify the header and payload fit using **subtraction checks** before adding to a cursor or calling `Slice`.
7. Return exact inclusive/exclusive boundaries. Do not inspect opaque string contents for integer meaning.

The bounds guard for any prospective slice of length `n` at `offset` is conceptually `offset >= 0 && n >= 0 && offset <= input.Length && n <= input.Length - offset`; evaluate this before addition. For a nested item, use `containerEnd - offset` instead of the whole document length.

All prefixes `0x00..0xff` belong to an RLP encoding family; malformedness results from noncanonical representation, incomplete data or invalid nesting, **not** from an unknown RLP prefix.

### 5.2 Strict root validation

Use an iterative, explicit list-end stack. No recursive method call may be proportional to attacker-selected nesting depth. Fixed stack storage of `MaxListDepth` entries MUST be bounded by an implementation hard cap (see Section 9) before allocation; a fixed `stackalloc int[64]` inside the validator is acceptable if the runtime frame size is measured and the configured limit is at most 64.

**Algorithm:**

1. Reject invalid resource limits and input exceeding `MaxEncodedBytes`; reject empty input.
2. Parse the root header against `input.Length`. If its encoded end is before `input.Length`, return `TrailingData`; if it overruns, return `TruncatedInput`.
3. Initialise total item count to one. A root byte string is complete at this point. A root list has **list depth 1**, including the empty list.
4. For each open list, maintain its exclusive payload end and the current cursor. At a cursor equal to the top end, pop the frame. At a cursor below the top end, parse exactly one child header, constrained to that end; increment the item count and advance to the child's encoded end. If that child is a list, push its end and set the scanning cursor to its payload start.
5. Reject list depth `MaxListDepth + 1` and item count `MaxItems + 1` immediately. Before pushing, verify stack capacity. Reject overshooting the parent boundary with `InvalidListBoundary`.
6. Finish only when all list frames have been popped and the root end has been reached exactly. Construct `RLPData` after validation succeeds, not before.

**Depth convention:** root list depth is 1; a list within it is 2; a root scalar has list depth 0. `MaxItems` includes the root, all nested lists and every string. Empty lists count as one item. Depth and count validation MUST be identical across root validation and optional future streaming parsers.

**Complexity:** O(items + bytes in length headers) inspection, O(max list depth) stack space, O(1) heap allocation. Long opaque string payloads are skipped after bounds validation; their bytes are not copied or semantically decoded.

### 5.3 Error precedence

For deterministic diagnostics, implement these precedence rules:

1. Invalid limits/arguments are reported before reading input.
2. Size limit is checked before examining input content (with empty input reported as `EmptyInput` when within size budget).
3. At each item, incomplete prefix/length/payload is `TruncatedInput` at the root; a child that crosses its enclosing list end is `InvalidListBoundary`.
4. A fully present but nonminimal encoding is `NonCanonicalEncoding`; excessive representable width is `LengthOverflow`.
5. An otherwise properly delimited root followed by bytes is `TrailingData` before descending into that root's children. No view is returned on either error.
6. During strict descent, the first error encountered in document traversal order wins; depth and item budgets are checked when the next item/list is discovered.

A precise input offset SHOULD identify the item prefix, offending length byte or first missing byte as appropriate. Malformed status is never silently downgraded to a successful partial parse.

### 5.4 Direct-child enumeration

On a fully validated list, `RLPChildEnumerator` needs only the document span, current byte cursor, list payload end and current child metadata. `MoveNext` reads a single child header and advances by the complete encoded child size. It does not recursively scan that child's descendants, because they were already validated at root entry.

Enumerating *k* children takes O(k) header work, with no new heap memory. Traversing child lists explicitly is a caller decision and MUST retain the shared document-offset origin. The enumerator returns `false` only at the exact list end; it must not interpret malformed state as an ordinary end-of-list condition.

A validated source buffer is required to remain stable. If mutation occurs after validation, this invariant no longer holds, and **no fail-safe behaviour is claimed** for accesses based on the stale view.

---

## 6. Range indexing

A field index is an optional optimisation for repeated positional access, not a replacement for validation. The decoder does not allocate index arrays internally.

### 6.1 Index shape

```csharp
public readonly record struct RLPRange(int Offset, int Length);
```

Examples for `c8 83 63 61 74 83 64 6f 67` (root-relative):

| Item | `Offset` | `Length` | Encoded bytes |
|---|---:|---:|---|
| Root list | 0 | 9 | `c8 ...` |
| Child 0 (`cat`) | 1 | 4 | `83 63 61 74` |
| Child 1 (`dog`) | 5 | 4 | `83 64 6f 67` |

The corresponding **payload ranges** are `(2, 3)` and `(6, 3)`. A short string containing one literal `7f` has identical encoded/payload ranges `(start, 1)`.

### 6.2 Two-pass, all-or-nothing builder

`RLPData.TryIndexDirectChildren` performs:

1. Require a valid list. Scan only its direct child headers to count them; do not modify the index workspace.
2. If `destination.Length < childCount`, report `IndexCapacityExceeded`, exact `requiredCount`, invalid index, and preserve destination bytes.
3. Fill exactly `childCount` `RLPRange` entries in a second pass, retaining the same root coordinate system.
4. Return a private-constructed `RLPChildrenIndex` referencing exactly the populated workspace prefix.

The second pass costs extra header reads, but avoids partial indexes and extra allocations. For fixed transaction-field layouts (typically small), this is an acceptable default until profiling establishes otherwise. An optional internal fast path may combine counting and writing only when it can prove destination completeness and uphold the same failure contract.

### 6.3 Cache lifecycle and validation boundaries

- A range index can be used repeatedly only with the **same unchanged encoded document**, validated parent list, stable index storage and correct original coordinate base.
- Ranges must not be exported as standalone trusted field descriptions or reconstructed from user-supplied offsets without fresh validation.
- A range that is in bounds but does not begin at a real child boundary is invalid as a child identity. Cached ranges are trusted only because the **index builder**, not the caller, discovered those boundaries.
- Persisting or asynchronously transferring indexes is outside the initial API. Rebuild them from an owned buffer after crossing that boundary, or add a separate verified serialised-index protocol with an explicit integrity binding.
- Field-count requirements belong to the transaction schema adapter, which MUST reject missing **and extra** fields; generic RLP lists may contain any number of children within resource limits.

### 6.4 Cost model

Strict initial validation: O(total nested items). Direct-child index construction: O(direct children) twice. Indexed ordinal lookup: O(1) for the cached range. Sequential enumeration: O(children visited) and often faster for one-time traversal. Indexing should be used only when fields will be revisited or random access is required.

---

## 7. Canonical encoder and sizing

The writer MUST generate the same bytes as the normative RLP rules, not simply copy a presumed encoding. Separate three operations: **size a value**, **write one complete atomic value**, and **compose a framed list**.

### 7.1 Sizing

Given a byte-string payload of length `n`:

- If `n == 1` and payload[0] `< 0x80`: total encoded length is `1`.
- If `n <= 55`: total encoded length is `1 + n`.
- Otherwise: total encoded length is `1 + MinimalBigEndianByteCount(n) + n`.

For a list of already encoded children totalling `n` bytes:

- If `n <= 55`: list encoded length is `1 + n`.
- Otherwise: list encoded length is `1 + MinimalBigEndianByteCount(n) + n`.

All size operations MUST use checked arithmetic, return `LengthOverflow` for totals not representable by `int`, and reject negative `encodedChildrenLength`. `TryGetByteStringLength` takes the actual payload span to handle the single-byte fast path correctly. Empty input to this **sizing method** means a valid empty byte-string payload, unlike empty input to the document decoder.

### 7.2 Scalar writes

`RLPEncoder.TryWriteByteString`, `TryWriteUInt64` and `TryWriteUnsignedMagnitude` MUST:

1. Validate arguments and unsigned integer minimality before modifying destination.
2. Determine exact output length.
3. Check source/destination overlap against the output region. The first version **does not support overlapping encoded writes**, including in-place prefix insertion. Return `OverlappingBuffers` with no mutation.
4. Check destination capacity before writing any output; on failure return `DestinationTooSmall`, `written = 0`, destination unchanged.
5. Write the prefix, minimal big-endian length and payload directly to the destination. Report exact bytes written.

An implementation MAY use `MemoryExtensions.Overlaps` for span-backed alias detection. Avoid `Array.Copy`, intermediate `byte[]`, `BigInteger`, `string`, `IEnumerable<T>`, reflection and `BinaryWriter` in the hot path. For a `ulong`, use a fixed stack-local 8-byte magnitude (or shifts with no intermediate buffer); zero encodes as `80`.

### 7.3 Nested writer state machine

`RLPWriter` maintains: destination span, current cursor, root count, an open-list frame count, and caller-provided stack workspace holding each open list's exclusive expected payload end.

| Operation | Preconditions | State change on success | Failure guarantee |
|---|---|---|---|
| `TryBeginList(n)` | `n >= 0`; complete encoded list fits current parent and destination; stack frame available; no extra root | Emit canonical header, register one new item at parent/root level, push expected end `cursor + n` | No bytes or state changed |
| `TryWriteByteString` | Entire encoded item fits parent/root and output; no forbidden overlap | Emit item, register one new item at current level | No bytes or state changed |
| `TryWriteUInt64` / `TryWriteUnsignedMagnitude` | Canonical input and complete fit | Emit item, register one new item | No bytes or state changed |
| `TryWriteEncoded` | `validatedItem.IsValid`; stable source; complete fit; non-overlap | Copy full canonical encoded item and register one new item | No bytes or state changed |
| `TryEndList` | At least one open list; cursor equals top frame's expected end | Pop one frame, no bytes written | No bytes or state changed |
| `TryComplete` | Exactly one top-level item; no open lists | Report written count; no extra writes | `written = 0`; no state change |

**Root discipline:** Any attempt to emit a second top-level item MUST return `ExtraRoot` before modifying output. A successful `BeginList` counts as one item at its parent/root level even while its children are being populated; final completion remains blocked until that list is closed.

**Parent discipline:** Child operations are limited by the most deeply nested open list's expected end, **and** the complete list reservation was checked against its parent when that list began. The writer cannot cross a parent limit. A child list of announced payload length `n` is incomplete until exactly `n` encoded child bytes have been written and its frame has been closed.

**Recoverability:** A failed atomic write does not advance cursor, consume a frame or change counts. A premature `TryEndList` returns `UnbalancedList` without popping the frame; the caller may add valid children and retry. `TryComplete` can be retried after closing outstanding lists. No failed call may silently produce a successfully complete root. Once successfully completed, the recommended implementation MUST reject further writes or starts until a fresh writer is constructed (`WriterStateInvalid`).

**Stack allocation:** The caller passes a list-end frame buffer with a fixed, trusted maximum depth, e.g. `stackalloc int[32]`. The writer MUST reject depth exceeding that workspace and MUST NOT resize it or fall back to heap allocation. Its initial state and default behaviour must be tested; construct writers explicitly and do not treat `default(RLPWriter)` as a valid destination.

### 7.4 Encoding examples

To encode `["cat", "dog"]`:

1. Calculate `TryGetByteStringLength(cat) = 4` and `TryGetByteStringLength(dog) = 4`.
2. Calculate `TryGetListLength(8) = 9` and obtain destination of at least 9 bytes.
3. `TryBeginList(8)` emits `c8` and opens a frame ending after 8 child-encoding bytes.
4. Write both strings; `TryEndList` closes only when exactly 8 bytes have been emitted.
5. `TryComplete` returns 9; destination is `c8 83 63 61 74 83 64 6f 67`.

To encode an empty list: `TryBeginList(0)` emits `c0`, `TryEndList` succeeds immediately and `TryComplete` returns 1. To encode an empty string: write an empty byte-string payload, producing `80`.

### 7.5 Encoded-item copying

`RLPData.TryCopyEncodedTo` and `RLPWriter.TryWriteEncoded` perform verbatim copy only from a validated view backed by stable bytes. They MUST NOT normalise embedded integer bytes or reinterpret the source as an EVM transaction. Check destination size and overlap before copying.

The writer's guarantee that newly constructed values are canonical relies on every emitted child being produced by a canonical primitive or copied from a validated view; it is not a guarantee that the list satisfies a higher-level transaction schema.

### 7.6 Optional output adapters

An `IBufferWriter<byte>` adapter MAY be added as a separate convenience layer. It must respect `GetSpan(sizeHint)`/`Advance` lifetime rules and may propagate exceptions from the supplied writer, so its failure model is different from the core span-based `Try*` API. Do not mix adapter guarantees into the first release's allocation-free/exception-free claims. A segmented writer MUST NOT assume a previous `GetSpan` remains usable after `Advance`.

---

## 8. Error contracts

| Status | Meaning | Caller action |
|---|---|---|
| `Success` | Complete operation | Use outputs as documented |
| `InvalidArgument` | Invalid limits, negative size, invalid ordinal or caller contract | Fix caller configuration/code |
| `EmptyInput` | No top-level encoded item | Reject message |
| `TruncatedInput` | Root prefix/length/payload not fully available | Reject or request complete input at external transport boundary |
| `NonCanonicalEncoding` | Redundant string/length encoding | Reject and request canonical input |
| `TrailingData` | Bytes remain after one complete root item | Reject; never silently ignore a second item |
| `InvalidListBoundary` | Child exceeds containing list or body is not a complete item sequence | Reject malformed input |
| `LengthOverflow` | Advertised or calculated length cannot fit API indexing | Reject oversized input |
| `TooLarge` | Configured encoded-byte limit exceeded | Reject or apply documented higher trust-boundary budget |
| `TooDeep` | List nesting exceeds budget | Reject resource-exhaustion attempt |
| `TooManyItems` | Total item count exceeds budget | Reject resource-exhaustion attempt |
| `WrongKind` | List operation on byte string or scalar operation on list | Correct caller's expected schema |
| `NonCanonicalInteger` | Integer magnitude has leading zero | Reject semantic field |
| `IntegerOverflow` | Scalar magnitude does not fit requested width | Reject semantic field |
| `DestinationTooSmall` | Insufficient output capacity | Resize at caller boundary or fail |
| `IndexCapacityExceeded` | Workspace too short for **all** direct-child ranges | Use returned `requiredCount`, provide larger workspace |
| `OverlappingBuffers` | Source and output alias in unsupported way | Supply distinct buffers |
| `WriterStateInvalid` | Writer completed or otherwise cannot accept operation | Construct new writer / fix caller state |
| `UnbalancedList` | No matching open list or announced child bytes incomplete | Complete/repair writer sequence |
| `ExtraRoot` | Attempted second top-level item | Use a parent list or create new writer |
| `InvalidFixedLength` | Byte string length differs from the fixed width a typed accessor requires (typed adapters only) | Reject semantic field |
| `InvalidSignatureV` | Signature recovery value outside its domain: typed y parity not 0 or 1, or legacy `v` not 27, 28 or at least 35 (typed adapters only) | Reject semantic field |

Failure statuses and offsets are **diagnostics, not authorisation decisions**. Security-sensitive callers MUST deny or halt on every non-success result. Logging MUST avoid full transaction payloads or uncontrolled error messages; record bounded status codes, offsets, input sizes and trusted context as required by the deployment.

---

## 9. Resource budgets

Limits are **application policy**, not arbitrary limits in the RLP wire format. Suggested conservative defaults for the generic codec:

| Budget | Default | Validation |
|---|---:|---|
| Maximum encoded document length | 1 MiB | Check before parsing |
| Maximum nested list depth | 32 | Root list depth = 1 |
| Maximum total items | 8,192 | Includes root and nested items |
| Absolute hard list-depth cap | 64 | Reject invalid configured depth above hard cap before stack allocation |
| Index capacity | Caller-supplied | Count before writing; no hidden allocation |
| Maximum `UInt64` payload width | 8 bytes | Higher-level integer accessor limit |

Implementers MUST test every limit at one below, equal to and one above the boundary. The depth limit should be configurable downward to match the signer workload. The codec MAY provide named presets but MUST NOT silently elevate a limit when a transaction exceeds it. Invalid configurations (`MaxEncodedBytes <= 0`, `MaxListDepth < 1`, `MaxItems < 1`, or list depth > hard cap) return `InvalidArgument` before reading any input. A scalar-only document is permitted with a positive `MaxListDepth` even though it consumes depth 0.

No `stackalloc` size is computed directly from untrusted bytes. Do not reserve scratch proportional to advertised RLP lengths, number of children, or input content. Large payloads require no codec-side allocations.

---

## 10. Threat model and required controls

### 10.1 Assets and adversary

Assets are exact transaction bytes, trustworthy field boundaries, signer authorisation integrity, deterministic decisions, service CPU/memory availability and audit records. Assume an adversary fully controls input bytes and can choose long length prefixes, nested containers, malformed encodings, numeric values and input timing. Assume application code may accidentally reuse pooled buffers or mishandle cached ranges. Do not assume malicious input can modify a correctly owned immutable buffer; instead treat unwanted aliasing/reuse as a separate integration threat.

| Threat / abuse case | Impact | Required control / regression evidence |
|---|---|---|
| Forged `(offset,length)` targets bytes inside another item | Incorrect field interpretation and signing decision | No public trusted view from arbitrary range; index only a validated list; adversarial forged-offset test |
| Input buffer mutation after validation | TOCTOU: decoded field differs from bytes signed | Exclusive stable buffer lease; signer digest/preimage binding; integration test mutating deliberately shared buffer |
| Workspace/index modified after construction | Fast indexed lookup reads wrong boundary | Stable caller-owned workspace contract; private index constructor; mutation test demonstrating invalidation |
| Long-form 8-byte length near `ulong.MaxValue` | Overflow or out-of-bounds | Bounded `ulong` decode and `int`/remaining-byte checks; `bf/ff` attack corpus |
| Child item extends past parent but fits root | Container escape and hidden sibling interpretation | Parent-end bounds for every child; nested boundary tests |
| `81 00`, nonminimal length prefixes | Multiple serialisations of same logical data | Reject noncanonical structure; exact fixture tests |
| Raw string `00` treated as scalar zero | Semantic mismatch | Separate integer accessor with no leading zero; distinct raw vs numeric tests |
| Millions of tiny valid children | CPU exhaustion | MaxItems and byte limits; item-budget benchmark |
| Deep lists | Stack exhaustion | Iterative traversal and hard depth cap; deep nesting fuzz tests |
| Partially constructed output returned as successful | Signer hashes incomplete/inconsistent transaction | Framed writer, exact end enforcement, root count, completion gate |
| Failed write changes destination or writer cursor | Corruption, unreproducible encoding | Full preflight, alias checks and atomic failure tests |
| Unexpected allocation / expensive diagnostics | Memory pressure or denial-of-service | No per-item objects, bounded logging, allocation tests |
| Incomplete EIP-2718 dispatch | Type prefix interpreted as RLP field | Separate envelope adapter, type-specific schema verification |
| Malformed ABI interpreted as nested RLP | Policy bypass | Keep ABI/call semantics outside this component; no recursive treatment of arbitrary string payloads |
| Unsupported integer widths/field counts | Invalid EVM transaction accepted | Higher-layer positional transaction schema MUST validate exact fields and types |

### 10.2 Security guarantees and limits

The codec guarantees canonical **structural** RLP only while its buffers are stable. It does not verify that a transaction is signed, belongs to a chain, has valid `to`/`value`/signature fields, targets an allowed contract, or that internal contract execution is safe. In particular, RLP treats an EVM `data` field as opaque bytes. ABI decoding of nested calls requires a separate schema-driven decoder and trusted contract semantics; RLP itself cannot identify arbitrary multicalls.

For transaction signing, the application MUST enforce the binding between reviewed fields and the exact preimage/digest actually signed. A structural decoder alone cannot enforce that invariant.

---

## 11. EVM integration boundaries

### 11.1 Legacy transactions

Legacy EVM transaction encodings use a top-level RLP list. A separate transaction adapter MUST verify exact field counts for the signed or signing-preimage form, integer canonicality, address widths and chain-specific rules. The generic RLP codec MUST NOT infer positional meaning from the number or contents of list items.

### 11.2 Typed transactions

EIP-2718 defines `TransactionType || TransactionPayload`, where the payload is **type-defined opaque bytes**, not necessarily RLP. For known RLP-backed types such as EIP-1559 type `0x02`, the adapter removes/records the leading type byte, passes the remaining RLP item to `TryDecodeComplete`, then maps document-relative offsets back to the full transaction with `offset + 1` when needed.

The adapter MUST preserve the typed-envelope byte wherever the corresponding transaction specification requires it in the signing preimage. Unknown types MUST be rejected by a signing-policy consumer rather than assumed to use RLP. EIP-2930, EIP-1559 and later transaction schemas belong to this adapter, not the codec.

### 11.3 Calldata and nested calls

An RLP transaction's `data` item is one opaque byte string. The RLP codec exposes its **payload range**, whose bytes may then be given to an ABI module. The ABI module separately resolves a selector against a trusted contract-aware schema, validates static/dynamic offsets and follows contract-specific nested-call semantics.

Never treat arbitrary RLP strings or arbitrary ABI `bytes` fields as nested calls without an explicit schema. Do not conflate RLP prefix offsets with ABI-relative offsets. An ABI call tree does not expose internal EVM calls generated dynamically from storage or execution state.

### 11.4 Integration example (pseudocode)

```csharp
// The caller has already acquired a stable byte buffer and selected
// an RLP-backed transaction format. `rlpPayload` excludes any type byte.
var limits = new RLPDecodeLimits(
    MaxEncodedBytes: 1024 * 1024,
    MaxListDepth: 32,
    MaxItems: 8192);

var status = RLPData.TryDecodeComplete(
    rlpPayload, in limits, out var root, out var error);
if (status != RLPStatus.Success)
    return DenyMalformedInput(error);

if (root.Kind != RLPKind.List)
    return DenySchemaMismatch();

Span<RLPRange> ranges = stackalloc RLPRange[16];
status = root.TryIndexDirectChildren(
    ranges, out var index, out var requiredCount);
if (status != RLPStatus.Success)
    return DenySchemaMismatch();

if (index.Count != expectedFieldCount)
    return DenySchemaMismatch();

// Map fields using the known transaction schema; evaluate remaining
// semantic rules, then bind any approval to the exact signer input.
```

The example illustrates call order, not a drop-in compiled signing service: `DenyMalformedInput`, `DenySchemaMismatch` and `expectedFieldCount` are application-specific placeholders. Static root and index views must not outlive the stable input and index workspace.

---

## 12. Automated verification programme

All code changes MUST have automated unit tests and, where relevant, property-based, integration, differential and fuzz tests. Tests MUST exercise public methods as well as internal invariants. A specification alone does not satisfy the validation gate.

### 12.1 Exhaustive and table-driven codec vectors

- All 128 literal single bytes `00..7f`, and one-byte string contents `80..ff` encoded with `81` prefix.
- Empty string `80` and empty list `c0`; ensure distinct kinds.
- Strings at lengths `0,1,2,54,55,56,57,254,255,256,65535,65536` with exact canonical prefixes and length-of-length transitions.
- List payload lengths `0,1,54,55,56,57,255,256` made from genuine encoded children.
- Empty nested lists, lists of mixed strings/lists, deeply nested lists, and lists containing long byte strings.
- All integer boundary magnitudes: 0, 1, 127, 128, 255, 256, `ulong.MaxValue`; 1..8-byte unsigned widths.
- Raw byte strings containing leading zeroes must decode structurally; typed numeric accessors must reject nonminimal magnitudes.
- For each canonical decoded item, `TryCopyEncodedTo` must return exactly the original bytes.

### 12.2 Negative and adversarial fixtures

Test at least these invalid inputs, including the same patterns nested inside enclosing lists:

| Hex/pattern | Expected category |
|---|---|
| empty input | `EmptyInput` |
| `81 7f` | `NonCanonicalEncoding` |
| `b8 37` + 55 bytes | `NonCanonicalEncoding` |
| `f8 37` + 55 valid child bytes | `NonCanonicalEncoding` |
| `b9 00 38` + 56 bytes | `NonCanonicalEncoding` |
| `f9 00 38` + 56 valid child bytes | `NonCanonicalEncoding` |
| `b8`, `b8 38` (no payload) | `TruncatedInput` |
| `c1 82` | `InvalidListBoundary` |
| `80 00`, `c0 00` | `TrailingData` |
| `bf ff ff ff ff ff ff ff ff` | `LengthOverflow` |
| `ff ff ff ff ff ff ff ff ff` | `LengthOverflow` |
| Valid raw `00` used with `TryGetUInt64` | `NonCanonicalInteger` |
| `82 00 01` used with `TryGetUInt64` | `NonCanonicalInteger` |
| `89` + 9 nonzero magnitude bytes used with `TryGetUInt64` | `IntegerOverflow` |

In addition, exercise malformed prefixes at the last available byte, overlapping source/destination, offset overflow (`int.MaxValue` and negative ranges in any untrusted integration helper), unused index capacity, forged internal child boundaries, exactly-full and one-short indexes, and all limits at `limit-1`, `limit` and `limit+1`.

### 12.3 Writer state-machine tests

Verify:

1. Empty root, one root and attempted second root.
2. Exact list payload length, premature `EndList`, insufficient child bytes and attempted overrun.
3. Nested lists reaching frame capacity exactly and one beyond.
4. Failed atomic write leaves both destination bytes and writer cursor/state unchanged; valid retry succeeds.
5. `TryComplete` fails with any open frame, succeeds only with exactly one closed root, and rejects subsequent writes.
6. Invalid unsigned magnitudes produce no output mutation.
7. Source/destination overlap in scalar writes and encoded-item copying returns `OverlappingBuffers` before writing.
8. Encoding results match independently generated reference bytes, including nested lists and 55/56 transitions.
9. `default(RLPData)`, invalid indexing and enumerator misuse never appear as valid data.

### 12.4 Property-based and fuzz testing

- Generate bounded canonical RLP trees, encode, decode and prove structural equivalence and byte-identical canonical re-encoding.
- Mutate prefix, length bytes, child boundaries, nesting, source ownership and resource limits; all malformed input must yield status rather than runtime exceptions.
- Keep a persistent fuzz corpus with every failure and divergence and regression-test it in CI.
- Compare decode acceptance and exact encoded bytes with **an independent Ethereum RLP implementation**, preferably go-ethereum. Compare integer handling only against matching typed integer expectations; do not confuse generic byte-string behaviour with scalar decoding in the reference library.
- Validate under Release with checked/sanitised or instrumented builds as available. Test x64 and ARM64 where CI infrastructure supports both.

### 12.5 Transaction-level contract tests

- Legacy nine-field signed transaction and corresponding signing-preimage variants: validate structural RLP and independently enforce the correct schema.
- EIP-1559 `0x02` envelope: remove the type byte for RLP decoding, preserve it for signing-preimage logic, and verify offset translation.
- Unknown typed transaction payload: refuse rather than automatically decoding as RLP.
- ABI calldata as an opaque `data` field: locate the payload without reinterpreting it inside the RLP package.
- Multiple unexpected transaction fields: structural decoder succeeds where valid, but schema adapter rejects extra/missing fields.
- Mutation of input between validation and signing must be caught by the signing-boundary binding, not incorrectly attributed to generic RLP validation.

### 12.6 Acceptance evidence

CI artefacts MUST include unit/integration/differential results, fuzz corpus version, coverage summary, failed-case regressions and benchmark environment. A skipped or unavailable test is **unverified**, not passed. Security review MUST explicitly inspect prefix maths, container boundaries, aliasing, default values, index provenance and the signing adapter trust boundary.

---

## 13. Benchmark specification and optimisation policy

### 13.1 Workloads

Benchmark using BenchmarkDotNet, Release compilation, pinned SDK/runtime, documented tiered PGO, CPU model/clock regime, core count, memory architecture, warm-up and sampling parameters. Compare at least one appropriate existing .NET RLP codec where equivalently strict behaviour can be configured; report differences in validation semantics rather than treating non-equivalent faster parsing as a win.

| Workload | Measurement | Important variants |
|---|---|---|
| Single short-string strict decode | ns/op, B/op | one byte, empty, 55, 56 bytes |
| Signed-style RLP transaction list | ns/op, B/op | typical 9 fields and nested lists |
| Nested list strict decode | ns/op, B/op | depths 1, 4, 16, 32 |
| Direct-child enumeration | ns/child, B/op | one pass, varied payloads |
| Range index creation | ns/op, B/op | 9, 12, 16 fields |
| Indexed field reads | ns/lookup | repeated accesses versus direct scan |
| Byte string / unsigned encoding | ns/op, B/op | 0, 1, 55, 56, 255, 256 bytes |
| Framed list encoding | ns/op, B/op | nested transaction-like payload |
| Invalid and hostile input | ns/op, B/op | oversized length, depth, many children |
| Concurrent application throughput | ops/s, p50/p95/p99, GC | mixed workload with stable owned input |

### 13.2 Allocation and asymptotic gates

- **Required target:** zero managed allocations in warm span-backed strict decode, validated child enumeration, direct-child index building into caller storage, `UInt64` access and span-backed scalar/framed encode. Measure this explicitly with warm JIT/static state; buffers supplied by the caller do not count as hidden library allocations.
- Strict validation time: O(number of items and length-header bytes), memory: O(max list depth).
- Direct-child index build: O(number of direct children), two scans by default; indexed range lookup: O(1).
- Canonical encoding: O(number of output bytes); no repeated concatenation of intermediate arrays.
- No universal hard-coded throughput or nanosecond pass target is defined without an observed baseline. Establish measured baseline on representative test hardware, record it, and add statistically meaningful performance-regression gates in a controlled environment.
- Measure p99 under sustained load separately from microbenchmarks; BenchmarkDotNet means alone do not establish end-to-end tail latency.

### 13.3 Optimisation order

1. Verify canonical correctness and comprehensive failure handling.
2. Verify zero managed allocations on the hot path.
3. Profile unnecessary decoding, repeated prefix scans, branch costs and transient buffer ownership.
4. Use indexes only where repeated positional access justifies an extra pass.
5. Specialise fixed-schema transaction access in **transaction adapters** if measurement justifies it; do not contaminate generic RLP semantics.
6. Consider unsafe code, vectorisation or segmented input only after profiling and dedicated adversarial tests prove a meaningful net benefit.

---

## 14. Implementation layout and build configuration

```text
src/
  Evm.Rlp/
    RLPData.cs
    RLPKind.cs
    RLPRange.cs
    RLPDecodeLimits.cs
    RLPStatus.cs
    RLPError.cs
    RLPHeader.cs              # internal, single canonical prefix parser
    RLPValidator.cs           # internal iterative whole-document validation
    RLPChildEnumerator.cs
    RLPChildrenIndex.cs
    RLPEncoder.cs
    RLPWriter.cs
    Evm.Rlp.csproj

tests/
  Evm.Rlp.Tests/
    PrefixCanonicalityTests.cs
    BoundaryAndLimitsTests.cs
    NestedTraversalTests.cs
    RangeIndexProvenanceTests.cs
    IntegerSemanticsTests.cs
    EncoderAtomicityTests.cs
    WriterStateMachineTests.cs
    OwnershipAndAliasingTests.cs
    AdversarialInputTests.cs
  Evm.Rlp.DifferentialTests/
    Fixtures/
    IndependentReferenceTests.cs
  Evm.Rlp.Fuzz/
    Corpus/
    RlpFuzzTarget.cs

benchmarks/
  Evm.Rlp.Benchmarks/
    DecoderBenchmarks.cs
    EnumerationBenchmarks.cs
    IndexBenchmarks.cs
    EncoderBenchmarks.cs
    HostileInputBenchmarks.cs

docs/
  api.md
  wire-format.md
  security.md
  integration.md
  performance.md

README.md
LICENSE
```

Use a library project with `<TargetFramework>net10.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<LangVersion>14.0</LangVersion>`, deterministic builds, analyzers and warnings treated as errors in CI. Do not enable unsafe code by default. Pin the SDK in `global.json` once the implementation environment is selected, commit dependency versions, and keep production dependencies limited to necessary .NET platform libraries.

Licence choice is a repository-owner decision; do not invent or assume the project licence. The README should give a short purpose, installation, safe synchronous usage and links to API/security/benchmark documents. The API documentation MUST spell out stable-buffer ownership, index workspace lifetimes, resource limits and failure semantics.

---

## 15. Implementation sequence and completion gates

**Codec contract and prefix parser.** Fix public status codes, root/child coordinate conventions, limits and ownership semantics. Implement the strict header parser. Test all prefix families, long length boundaries, integer-vs-string distinction and pathological inputs. **Gate:** no malformed prefix returns a trusted view and no input-triggered overflow/out-of-bounds exception occurs.

**Validated views and traversal.** Implement iterative full-root validation, `RLPData` and child enumeration. Ensure all descendants are checked before success. **Gate:** nested malformed structures are rejected and traversal has zero managed allocations.

**Direct-child index and numeric access.** Implement index counting, all-or-nothing workspace population and `RLPChildrenIndex` private construction; add unsigned `UInt64` accessor. **Gate:** forged boundaries cannot enter the trusted API, indexes never silently truncate and semantic integers reject leading zeroes.

**Canonical encoding.** Implement sizing, scalar encoders and the framed `RLPWriter`. Prove atomic failures, root/list state transitions and exact output. **Gate:** reference vectors, state-machine tests and round-trip tests pass.

**Integration and hardening.** Add EVM envelope adapter tests, buffer ownership/security review, differential reference comparison, fuzz corpus and CI on supported platforms. **Gate:** all security-critical acceptance tests pass, with no unexplained codec/reference divergences.

**Performance and release evidence.** Collect allocation and latency benchmarks, profile real transaction-like fixtures, document measured baselines, and publish API/security/operating guidance. **Gate:** hot-path zero-allocation contract verified, regression thresholds justified by measurements, no outstanding critical/high security defects, and repository documentation reflects tested behaviour.

### 15.1 Release acceptance checklist

- [ ] All canonical RLP prefix families and 55/56-byte transitions decode and encode exactly.
- [ ] The public decoder checks a complete, single root and all nested children before producing a valid `RLPData`.
- [ ] All input-controlled arithmetic and slices are bounded; hostile input never causes out-of-range or overflow exceptions.
- [ ] An RLP-valid raw `00` remains distinct from a canonical unsigned integer zero.
- [ ] Every nested child shares the original document coordinate system; encoded and payload ranges are unambiguous.
- [ ] Caller-supplied range workspace is left untouched on insufficient capacity and required count is reported exactly.
- [ ] No public method treats arbitrary caller ranges as trusted RLP boundaries.
- [ ] The framed writer rejects extra roots, premature endings, length overruns, overlap and use after completion.
- [ ] All unsuccessful atomic writes preserve destination and internal writer state.
- [ ] Input ownership, index stability and signing-time exact-byte binding are tested at integration boundaries.
- [ ] Unit, property-based, differential, fuzz, integration and performance checks have recorded results.
- [ ] Span-backed hot paths show zero managed allocations under the documented measurement conditions.
- [ ] Compiler/analyzer/test/CI results are green on the supported targets, or explicit release blockers remain.
- [ ] Documentation and examples accurately describe the implemented APIs and their security boundaries.

---

## 16. Authoritative references

1. [Ethereum — Recursive-length prefix (RLP) serialization](https://ethereum.org/developers/docs/data-structures-and-encoding/rlp/)
2. [Ethereum Yellow Paper — RLP specification in Appendix B](https://github.com/ethereum/yellowpaper/blob/master/Paper.tex)
3. [EIP-2718 — Typed Transaction Envelope](https://eips.ethereum.org/EIPS/eip-2718)
4. [EIP-1559 — Fee market change and transaction envelope](https://eips.ethereum.org/EIPS/eip-1559)
5. [go-ethereum — RLP decoder and canonicality handling](https://github.com/ethereum/go-ethereum/blob/master/rlp/decode.go)
6. [Microsoft — `ref struct` types](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/ref-struct)
7. [Microsoft — `MemoryExtensions.Overlaps`](https://learn.microsoft.com/en-us/dotnet/api/system.memoryextensions.overlaps?view=net-10.0)
8. [Microsoft — `IBufferWriter<T>.GetSpan`](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.ibufferwriter-1.getspan?view=net-10.0)

---

**Implementation mandate:** Implement the minimum correct, strict, zero-copy RLP codec first. RLP structural validity, cache/index trust, application semantics and signing authorisation are separate guarantees and MUST remain separate in code, tests and documentation.
