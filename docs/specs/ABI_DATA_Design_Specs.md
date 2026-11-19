# ABIData & ABICallData — High-Assurance, Zero-Copy Solidity ABI Codec for .NET

**Version:** 2.1 — standalone final design and implementation contract  
**Date:** 16 November 2026  
**Platform:** .NET 10 LTS; C# 14; x64 and ARM64; Windows and Linux  
**Namespace:** `Evm.Abi`  
**Primary types:** `ABIData`, `ABICallData`  
**Delivery status:** Specification only; implementation, compilation, fuzz results and CPU benchmarks are required acceptance work, not claimed accomplishments.

---

## 1. Purpose and non-negotiable requirements

Implement a small, fast and secure codec for **standard Solidity ABI encoding**. `ABIData` represents a **validated, schema-bound borrowed view** of an ABI tuple's encoded arguments, return values, or other tuple-encoded data. `ABICallData` represents a validated Solidity function invocation: the **four-byte selector** followed by an `ABIData` argument tuple. Both types MUST use one shared validator, field locator, scalar codec and schema representation; neither is a general-purpose object serialiser.

The codec is intended for an EVM signing-policy pipeline, where attackers may control entire payloads and where incorrectly identifying a recipient, amount, function or nested call can result in an unsafe signing decision. Its security objective is **exact structural interpretation under a supplied trusted ABI schema**, not proof of contract behaviour.

### 1.1 Mandatory characteristics

- Complete, strict decoding of the supported standard ABI grammar, with **no per-field allocations**, no decoded object tree, no reflection and no JSON processing on the steady-state path.
- **Borrowed** `ReadOnlySpan<byte>` views: input is never copied by the codec; the owner remains responsible for preserving bytes throughout validation, evaluation and signing.
- Deterministic, bounded validation of all fields before exposing a fully validated view. Schema-driven field lookup then follows prevalidated offsets without rescanning unrelated siblings.
- Caller-supplied `Span<byte>` encoding with canonical output, explicit writer state, accurately calculated offsets, no hidden allocations, and no valid result from a failed or incomplete write.
- Correct static and dynamic tuples, multidimensional fixed and dynamic arrays, 256-bit numeric words, dynamic bytes, strings, fixed bytes, function pointers, and canonical padding.
- Precise coordinate rules for every range, especially selector-relative arguments and nested `bytes[]` payloads.
- Explicit resource ceilings, malformed-input statuses, threat controls, independent test vectors, adversarial tests, differential verification and reproducible performance gates.
- Clean boundaries to transaction/RLP decoding, contract-aware function resolution, executable nested-call interpretation, RPC, signing and policy decisions.

### 1.2 Meaning of MUST, SHOULD and MAY

**MUST** is required for v2.1 release acceptance; **SHOULD** is a recommended implementation choice whose omission requires documented justification; **MAY** is optional. Normative ABI encoding behaviour is defined by the [Solidity Contract ABI Specification](https://docs.soliditylang.org/en/latest/abi-spec.html). This document defines additional *codec* constraints and deliberately strict validation semantics.

### 1.3 Out of scope

Not supported by this release: non-standard `abi.encodePacked`, Solidity indexed-event special encoding, EIP-712, RLP, EVM bytecode execution, on-chain state simulation, free-form decoding without a schema, untrusted public four-byte lookups during approval, ABI inference by pattern matching, external RPC in the codec, native pointers, and custom router command bytecode. Standard ABI tuples and function input data are in scope; event unindexed data, error payloads and output data may reuse the codec with explicit higher-layer adapters.

An ABI `bytes` or `string` is **not automatically executable calldata**. Execution semantics belong to verified contract-specific metadata, not to this codec.

---

## 2. Supported grammar and precise wire format

### 2.1 Solidity ABI types

| Type | Encoding | Mandatory checks |
|---|---|---|
| `uint<M>` | 32-byte big-endian unsigned word, left zero-extended | `M ∈ {8,16,…,256}`; upper unused bits zero |
| `int<M>` | 32-byte two's-complement signed word, left sign-extended | Same widths; upper unused bits equal sign extension |
| `address` | 20 bytes in low-order portion of word | First 12 bytes zero |
| `bool` | 32-byte word containing precisely 0 or 1 | No other bit set |
| `bytes<M>` | First `M` bytes of a word, right zero-padded | `1 ≤ M ≤ 32`; remainder zero |
| `function` | 20-byte address + four-byte selector + eight zero bytes | Last eight bytes zero |
| `fixed<M>x<N>` | Signed 256-bit ABI word of integer scaled by `10^N` | `M` legal integer width; `1 ≤ N ≤ 80`; signed extension |
| `ufixed<M>x<N>` | Unsigned scaled integer | Same limits; upper unused bits zero |
| `bytes` | 32-byte length, bytes, then zero padding to multiple of 32 | Length fits remaining input; padding all zero |
| `string` | Same layout as dynamic `bytes`, UTF-8 contents conceptually | Structural decode preserves raw bytes; optional strict UTF-8 validation |
| `T[k]` | Tuple-like encoding of exactly `k` elements; no count prefix | `k > 0` in this release; checked static size |
| `T[]` | 32-byte count then tuple-like encoding of its elements | Count bounded; offset base **immediately after count** |
| `(T1,…,Tn)` | Concatenated head regions then dynamic tails | Correct static widths and dynamic offsets |

Aliases MUST be normalised for function signatures: `uint`→`uint256`, `int`→`int256`, `fixed`→`fixed128x18`, `ufixed`→`ufixed128x18`; canonical Solidity tuple syntax uses parenthesised component types (including array suffixes), not literal `tuple` in the hashed function signature. Parameter names, `calldata`, `memory`, `payable`, output types and mutability are excluded from the signature. `Keccak-256` (Ethereum), **not** NIST `SHA3-256`, generates the selector.

A root tuple of zero arguments has precisely **zero bytes** of encoded arguments. The Solidity grammar permits empty tuples; this codec supports them, including nested zero-size tuples, subject to strict traversal budgets. Such values may occupy **zero bytes**; no algorithm may assume every logical value advances the input cursor. Zero-length *fixed arrays* are legal in the abstract ABI grammar but not supported by Solidity's compiler; v2.1 explicitly rejects `T[0]` at schema compilation with `UnsupportedSchema`, rather than silently misencoding it. Dynamic arrays with zero-size tuple elements are supported with bounded element counts.

A Solidity `string` is UTF-8 text semantically, but a structurally valid byte payload may contain malformed UTF-8 on chain. Structural validation MUST NOT allocate strings, replace sequences, normalise text or claim UTF-8 semantic validity. Callers inspecting textual content MUST request a separate strict UTF-8 validation step and apply the policy to exactly the validated bytes/text.

### 2.2 Static/dynamic and head-width functions

For an ABI type `T`:

- `IsDynamic(T)` is true for `bytes`, `string`, `T[]`, `T[k]` where the element type is dynamic, and tuples with at least one dynamic component.
- `StaticSize(T)` is defined only when `IsDynamic(T)` is false: primitive static value 32 bytes; static fixed array `k × StaticSize(element)`; static tuple sum of its component static sizes. Empty tuple size is zero.
- `HeadWidth(T)` is **32** when dynamic, otherwise `StaticSize(T)` (which can exceed 32 or equal zero).
- `TupleHeadSize(T1,…,Tn)` equals the checked sum of `HeadWidth(Ti)`.

The schema compiler MUST reject arithmetic overflow and prohibit unsafe products from becoming encoded sizes. Treating every field, including a static tuple or fixed array, as one 32-byte head is a fatal correctness defect.

### 2.3 Canonical head/tail rule

A tuple's byte representation is the concatenation of its children’s heads, followed by the **encodings of dynamic children in declaration order**. A dynamic child's head is a 32-byte unsigned offset relative to the beginning of the tuple-like region **containing that offset**. Its tail begins at the exact next canonical tail position. A static child's entire encoding is inline in the containing head.

For a dynamic array `T[]`, the value starts with a 32-byte count, followed by a tuple-like region of `count` elements. Dynamic element offsets are relative to **the first byte after the count word**. For a fixed array of dynamic elements, offsets are relative to the **start of the array value**, not to an outer tuple. For a tuple containing a dynamic tuple, offset arithmetic resets to the nested tuple's own encoding start. For function calldata, the first four selector bytes are **never part of any ABI offset base**.

All dynamic pointers MUST be word-aligned relative to their tuple region, at or beyond that tuple's entire head, and equal exactly the next canonical tail position. Canonicality forbids gaps, aliasing, out-of-order tails, overlaps and ignored trailing data. A zero-length dynamic `bytes` value still encodes a 32-byte zero-length word; a zero-length dynamic array still encodes a 32-byte zero-count word.

**Compatibility policy:** Solidity's actual decoder accepts some non-strict ABI layouts (including offsets with gaps, overlap or trailing bytes). This library deliberately validates the *strict output format produced by Solidity encoders*, and therefore can reject a transaction that the deployed EVM contract would accept. Security-sensitive deployments MUST make that explicit compatibility choice. There is **no automatic permissive fallback** in the signing path.

### 2.4 Strict scalar rules

- For `uint<M>`, high `256−M` bits are zero; for `int<M>`, they are copies of bit `M−1`, including when `M=256` (no unused high bits).
- A narrowed accessor such as `TryGetUInt64` MUST reject a value that does not fit 64 bits; it MUST NOT truncate or reinterpret a signed value as unsigned.
- `bool` accepts only words 0 and 1. `address` uses exactly 20 low-order bytes. `bytes<M>` is left-aligned, not address-aligned.
- `function` consists of a 24-byte address+selector payload left-aligned in its ABI word; trailing eight bytes MUST be zero.
- Fixed-point values are encoded as *scaled integer* values; the codec MUST NOT round, use floating-point conversions, or silently narrow their range. Higher-level decimal conversion is outside this component.
- All 32-byte pointer, length and count words MUST be checked as unsigned 256-bit values **before** safe conversion to a native index. No unchecked `uint256 → int` cast is permitted.

---

## 3. Trust boundaries and memory ownership

### 3.1 Three separate guarantees

1. **Structure:** `ABIData` has successfully validated the entire supplied document against an immutable, specified schema. `ABICallData` additionally verified the supplied function selector.
2. **Identity:** An external trusted registry binds that schema to a chain, target, verified code/implementation identity and selector. A selector alone is only a *candidate* identifier; collisions are possible.
3. **Authorisation:** An external policy engine has checked transaction context, nested execution semantics, spending rules and the exact bytes/digest to be signed.

Neither `ABIData` nor `ABICallData` certifies guarantees 2 or 3. A public four-byte database is suitable for *offline suggestions*, never for automatic signing approval. ABI decode success with any plausible schema does not prove it is the actual called function.

### 3.2 Borrowed buffers and TOCTOU

Both primary types MUST be `readonly ref struct` views containing or referencing a `ReadOnlySpan<byte>`. They MUST NOT copy or allocate the input. `ReadOnlySpan<T>` prevents mutation **through the span**, not through other aliases. Neither `IsValid` nor `readonly` proves the original backing bytes remain unchanged after validation.

A policy service MUST create a stable per-request ownership boundary, reject oversized input before allocation, decode hexadecimal exactly once, validate/evaluate synchronously over stable bytes, and bind the approval to the **exact signing message, request identity, chain, signer and policy version**. If buffer ownership can be transferred or mutation cannot be ruled out, copy once at ingress into exclusively owned storage. The codec itself remains zero-copy from that point. Async boundaries MUST retain an owner/lease (`ReadOnlyMemory<byte>` or equivalent) and reconstruct stack-scoped views inside synchronous regions. No ref-struct view may outlive its backing owner, be stored in a heap object, or be used after a pooled buffer is returned.

Changing an input buffer after validation **invalidates the trust result**; repeating a field lookup on changed bytes is not a valid security check. The caller MUST prevent mutation or revalidate the exact bytes before authorisation/signing.

### 3.3 Range coordinate model

`ABIRange(Offset, Length)` is a compact **eight-byte coordinate**, with signed 32-bit components. The implementation MUST verify `Unsafe.SizeOf<ABIRange>() == 8` on all declared targets, use a sequential layout if any native or binary interoperability is introduced, and never serialize native struct memory as an ABI wire format. It is not a validation token and contains no pointer. All decoder-produced ranges are relative to the **root ABI document** supplied to `ABIData`, not relative to a nested field. The range of a static field includes its entire static encoding; the range of a dynamic field includes the complete tail encoding (e.g. `[length word][data][padding]`), excluding the offset word in the parent head. `ABIFieldLocation` exposes separately the **head range** and **value range**. A dynamic-bytes accessor exposes separately the **unpadded payload range**. Zero-byte nested empty tuples are represented by a valid `Length=0` range.

`ABICallData` stores `[selector 4 bytes][argument document]`; `ABICallData.Arguments` has **coordinate zero at calldata byte 4**. Mapping any arguments range into call-coordinate space adds four bytes; following ABI pointers internally never adds four. A nested call taken from a `bytes` payload starts a **new** call coordinate system at that child selector. Outer RLP offsets can be composed explicitly by adapters, without allowing a mapping function to certify arbitrary ranges as fields.

A `TryTranslateArgumentRange` convenience API MAY bounds-check an arbitrary range and translate its coordinates. It MUST be documented as **translation only**; it does not establish that the input range is a validated field. Policy code MUST take field locations only from schema-driven accessors on an already validated view.

### 3.4 Validation provenance

- Public constructors MUST NOT allow a caller to create an `IsValid=true` view, inject a field location, or install a trusted index from arbitrary offsets.
- A successful complete validation is the only way to create a valid root view; its child locations remain derived from the same schema and original source.
- Default-initialised views MUST have `IsValid=false`. Every `Try*` method MUST reset `out` values on failure and never return a partly validated view.
- Caller storage may hold copies of **ordinary** `ABIRange` values, but those values alone can never bypass schema validation or authorise a transaction.
- Underlying buffer mutation breaks provenance; the codec does not attempt expensive mutation detection on each field access.

---

## 4. Immutable compiled schema model

### 4.1 Compilation at build/startup only

`ABISchema` is an immutable, shareable reference descriptor whose root represents an ABI **tuple**. It is not a proof of contract identity, and validation under different plausible schemas may succeed for the same bytes. `ABIFunctionSchema` binds a canonical function name/signature, the exact selector and its input `ABISchema` (optionally also output metadata). A schema compiler ingests a typed schema definition or verified contract ABI **outside the request hot path**, recursively checks it and builds a compact flattened node table.

Each compiled node requires at least: kind, width/scale, fixed count or dynamic sentinel, child-node or element-node reference, `IsDynamic`, `StaticSize` when relevant, `HeadWidth`, and for tuples precomputed child head positions, total head size **and the next dynamic sibling (or a sentinel)** for every child position. For homogeneous dynamic arrays the next sibling is computed as `index + 1` and its offset-table slot; for static elements the known static stride is used. The flattened table MUST be private, immutable after publication and inaccessible for mutation through public getters. Mutable source definitions MUST be copied during compilation. Compiled function selectors MUST be calculated from canonical signatures using Keccak-256 and checked against known reference vectors; accepting an arbitrary user-supplied selector without matching its signature is prohibited.

The compiler MUST enforce finite schema depth, node count, per-tuple arity, max fixed-array count, static footprint bounds and valid ABI type grammar. Schema metadata includes `NextDynamicSibling` for each tuple position. The index is precomputed in a reverse pass over the tuple at compile time; its offset is then obtained from the tuple head-position table without searching intervening static members. Reject cyclic definition objects, duplicate/conflicting parameter definitions where relevant, negative/overflowed array bounds, unsupported fixed zero-length arrays, invalid widths/scales, and invalid tuple syntax. Error reports MUST identify the schema component path and reason without exposing transaction bytes.

Repeated typed subgraphs MAY reuse immutable descriptors to save memory. Schema immutability and reuse MUST NOT create accidental shared mutable state or allow indefinite recursive type graphs.

### 4.2 Selector resolution

A trusted caller supplies `ABIFunctionSchema` directly or resolves it from an external contract-aware registry before invoking `ABICallData.TryDecode`. A recommended registry key is `(chainId, targetAddress, verifiedCodeOrImplementationIdentity, selector)`, with explicit treatment of proxies, upgrades and delegated accounts. The ABI library MUST NOT perform live RPC or external selector lookup.

A four-byte collision yields **ambiguous resolution** unless independently verified contract metadata selects precisely one function schema. Neither matching calldata length nor successful decoding resolves a selector collision safely. Fallback, receive, constructor arguments and custom error selectors MUST be represented by separate adapters/contexts and must not be accidentally treated as ordinary function calldata.

### 4.3 Schema compilation surface

Use stable bootstrap-facing definitions such as `ABITupleDefinition` and `ABITypeDefinition` (immutable descriptors or defensive-copy builder inputs). Required entry points, with equivalent signatures permitted only when they preserve the following behaviour:

```csharp
public static class ABISchemaCompiler
{
    public static bool TryCompileArguments(
        ABITupleDefinition definition,
        in ABICompileLimits limits,
        out ABISchema schema,
        out ABISchemaError error);

    public static bool TryCompileFunction(
        string name,
        ABITupleDefinition arguments,
        in ABICompileLimits limits,
        out ABIFunctionSchema function,
        out ABISchemaError error);
}
```

`ABIFunctionSchema`'s selector MUST equal the actual Keccak-derived selector for its immutable canonical signature. The compiler MAY allocate; validated hot-path decoding MUST NOT. If function overloads share a selector, a trusted registry MUST detect and handle ambiguity rather than silently keeping the first registration.

---

## 5. Public types, statuses and behavioural contracts

The following represents the required public contract; compiler-required mechanical changes MUST preserve semantics and be documented. Examples are design contracts, **not claims of having compiled an implementation**.

```csharp
namespace Evm.Abi;

public readonly record struct ABIRange(int Offset, int Length);

public readonly record struct ABIFieldLocation(
    ABIRange HeadRange,
    ABIRange ValueRange,
    ABITypeKind Kind);

public enum ABITypeKind : byte
{
    UInt, Int, Address, Bool, FixedBytes, Function,
    Fixed, UFixed, Bytes, String, Tuple, FixedArray, DynamicArray
}

public enum ABIStatus : byte
{
    Success,
    InvalidArgument,
    InvalidSchema,
    UnsupportedSchema,
    InputTooLarge,
    InvalidSelector,
    InvalidPath,
    TypeMismatch,
    Truncated,
    InvalidOffset,
    NonCanonicalLayout,
    InvalidPadding,
    InvalidScalar,
    ValueOutOfRange,
    InvalidUtf8,
    SourceOverlapsDestination,
    ArithmeticOverflow,
    DepthLimitExceeded,
    ElementLimitExceeded,
    WorkLimitExceeded,
    InsufficientDestination,
    InsufficientScratch,
    IncompleteWrite,
    InvalidWriterState
}

public readonly record struct ABIDecodeLimits(
    int MaxEncodedBytes,
    int MaxValueDepth,
    int MaxArrayElements,
    int MaxVisitedValues,
    int MaxWorkUnits);

public readonly record struct ABICompileLimits(
    int MaxSchemaNodes,
    int MaxSchemaDepth,
    int MaxFixedArrayElements,
    int MaxTupleElements,
    int MaxStaticEncodedBytes);

public readonly record struct ABIEncodeLimits(
    int MaxEncodedBytes,
    int MaxValueDepth,
    int MaxArrayElements,
    int MaxWrittenValues,
    int MaxWorkUnits);

public readonly ref struct ABIData
{
    public bool IsValid { get; }
    public ReadOnlySpan<byte> EncodedBytes { get; }
    // Nullable on a default-initialised, invalid view.
    public ABISchema? Schema { get; }

    public static bool TryDecodeComplete(
        ReadOnlySpan<byte> encodedArguments,
        ABISchema schema,
        in ABIDecodeLimits limits,
        out ABIData data,
        out ABIStatus status);

    // Path entries select zero-based tuple components or array elements.
    public bool TryLocate(
        ReadOnlySpan<int> path,
        out ABIFieldLocation field,
        out ABIStatus status);

    public bool TryGetArrayLength(
        ReadOnlySpan<int> path,
        out int length,
        out ABIStatus status);

    public bool TryGetWord(
        ReadOnlySpan<int> path,
        out ReadOnlySpan<byte> word,
        out ABIStatus status);

    public bool TryGetUInt64(
        ReadOnlySpan<int> path,
        out ulong value,
        out ABIStatus status);

    public bool TryGetInt64(
        ReadOnlySpan<int> path,
        out long value,
        out ABIStatus status);

    public bool TryGetAddress(
        ReadOnlySpan<int> path,
        out ReadOnlySpan<byte> address20,
        out ABIStatus status);

    public bool TryGetBool(
        ReadOnlySpan<int> path,
        out bool value,
        out ABIStatus status);

    public bool TryGetBytePayload(
        ReadOnlySpan<int> path,
        out ReadOnlySpan<byte> payload,
        out ABIRange payloadRange,
        out ABIStatus status);

    // Required strongly typed primitive operations; all are borrowed/zero-alloc.
    public bool TryGetFixedBytes(
        ReadOnlySpan<int> path, out ReadOnlySpan<byte> value,
        out ABIStatus status);
    public bool TryGetFunction(
        ReadOnlySpan<int> path, out ReadOnlySpan<byte> address20,
        out uint selector, out ABIStatus status);
    public bool TryGetSignedScaledWord(
        ReadOnlySpan<int> path, out ReadOnlySpan<byte> word32,
        out byte scale, out ABIStatus status);
    public bool TryGetUnsignedScaledWord(
        ReadOnlySpan<int> path, out ReadOnlySpan<byte> word32,
        out byte scale, out ABIStatus status);
    public bool TryGetUInt256Word(
        ReadOnlySpan<int> path, out ReadOnlySpan<byte> word32,
        out ABIStatus status);
    public bool TryGetInt256Word(
        ReadOnlySpan<int> path, out ReadOnlySpan<byte> word32,
        out ABIStatus status);
}

public readonly ref struct ABICallData
{
    public bool IsValid { get; }
    public uint Selector { get; }
    public ReadOnlySpan<byte> EncodedCall { get; }
    public ABIData Arguments { get; }

    public static bool TryDecode(
        ReadOnlySpan<byte> callBytes,
        ABIFunctionSchema function,
        in ABIDecodeLimits limits,
        out ABICallData call,
        out ABIStatus status);

    // Bounds-checked coordinate translation ONLY, not field validation.
    public bool TryTranslateArgumentRange(
        ABIRange argumentRange,
        out ABIRange callRange);
}
```

### 5.1 Accessors and scope rules

- All default instances are invalid. Public `Try*` operations on invalid instances MUST return `InvalidArgument`, reset output and never throw for malformed payloads.
- `TryLocate([])` returns root tuple's whole `ValueRange=(0,EncodedBytes.Length)` and `HeadRange=(0,RootTupleHeadSize)`. Paths within arrays select elements, not the dynamic-array count word. A path into a primitive is `InvalidPath`; a negative/out-of-range index is `InvalidPath`.
- `TryGetWord` succeeds **only** for 32-byte elementary ABI types (including the encoded fixed-point word). A composite, `bytes` or `string` path returns `TypeMismatch`; the caller cannot use a raw word read to bypass declared type semantics.
- `TryGetUInt64` applies **only** to `UInt`, with exact narrowing checks; `TryGetInt64` applies **only** to `Int`, with exact signed range checks. Fixed-point types MUST be accessed using the separately named `TryGetSignedScaledWord` / `TryGetUnsignedScaledWord` operations which return the scale. A consumer MUST NOT silently treat the scaled integer as an ordinary unscaled `uint`/`int`. A **canonical** 256-bit value that does not fit a requested 64-bit accessor fails with `ValueOutOfRange`, not `InvalidScalar` (which is reserved for malformed encoded scalars).
- `TryGetAddress` returns precisely 20 low-order bytes; `TryGetBool` returns a scalar boolean. Accessors for fixed bytes, function address/selector and 256-bit comparisons MUST be supplied as type-safe, allocation-free extensions with the same status contract.
- `TryGetBytePayload` applies **only to dynamic `bytes` and `string`** (fixed `bytes<M>` uses its own accessor). It returns the unpadded data span and root-document-relative payload range, not a fabricated nested function-call view.
- `TryGetFixedBytes` returns only the declared `M` actual bytes and excludes right padding. `TryGetFunction` returns exactly the 20 address bytes and a big-endian `uint` selector; it does **not** imply an executable nested call. `TryGetUInt256Word` / `TryGetInt256Word` return canonical validated 32-byte words only for their respective integer ABI kinds, never a differently typed scalar. Fixed-point accessors return the exact canonical scaled integer word **plus declared decimal scale `N`**, without rounding, floating-point conversion or materialisation.
- For all typed methods, a matching ABI type kind and width is mandatory; a valid ABI document does not authorise reinterpretation of a value as another type. In particular, ordinary integer methods MUST reject `Fixed` / `UFixed` and scale-bearing values MUST never be returned without scale metadata. An optional `TryCompareUnsigned256` helper MAY compare validated 32-byte words lexicographically in big-endian order; signed comparisons MUST compare signs correctly before magnitudes. Errors from failed narrowing must not be conflated with invalid underlying encoding.
- Range containment is checked using subtraction, e.g. `offset >= 0 && offset <= length && size >= 0 && size <= length-offset`. No signed overflow may precede this check.
- All `Try*` outputs are defaulted on failure. No normal malicious input may escape via `IndexOutOfRangeException`, `OverflowException` or an invalid span slice.
- `Schema` is nullable so that `default(ABIData)` has an honest and compilable nullability contract. A successful decode guarantees `IsValid=true` and `Schema != null`; unsuccessful decode guarantees `IsValid=false`, `Schema=null`, `EncodedBytes.IsEmpty=true`. Invalid `ABICallData` likewise has an invalid `Arguments` value. No non-nullable reference member may silently promise validity in the default-initialised view.

### 5.2 Error ordering and diagnostics

The decoder MUST deterministically prioritise: (1) invalid schema, null reference or invalid limits; (2) request size exceeds configured maximum; (3) insufficient four-byte selector or selector mismatch for `ABICallData`; (4) the first structural error encountered during defined depth-first, declaration-order validation; (5) resource exhaustion at the first attempted work charge exceeding its budget. When two errors are possible within the same field, validate word containment before reading, then length/offset fit, then canonical pointer relation, then scalar contents/padding. A limit encountered *while traversing* is reported when reached, not made dependent on platform-specific traversal choices.

`ABIStatus` MUST be accompanied in diagnostic builds by an optional lightweight `ABIError(ABIStatus, int DocumentOffset, int SchemaNode)`; diagnostics MUST NOT embed sensitive calldata or logging side effects. All public paths MUST be fuzz-safe and deterministic. Invalid API use can fail with statuses, not uncontrolled exceptions.

### 5.3 Boundary-aware random access without a value tree

For an already compiled schema, static tuple child head positions are precomputed; dynamic array element head slots are directly calculable from `index × 32`; static array element positions derive from `index × StaticSize(element)`. However **finding the beginning of a dynamic composite is not sufficient to return its full `ValueRange.Length`**. Deriving that length by recursively walking the selected subtree on every lookup would violate the intended O(path-depth) contract.

The locator MUST carry an **exclusive enclosing end bound** alongside each current container's start/head base. It starts with the validated root document's exact `Length`. When selecting a dynamic child, its exclusive end is the start of the **next dynamic sibling's canonical tail** if one exists, otherwise the enclosing container's exclusive end. The next dynamic sibling is located from precomputed tuple metadata or the next homogeneous array offset-table entry; its offset is relative to the *same containing head base*. On descent, the selected child's start and end become the next enclosing interval. For a static child, end is `start + StaticSize(child)` (checked), irrespective of any dynamic siblings elsewhere. For dynamic arrays, after reading the count word, the element tuple head base is `arrayStart + 32` and the child bound is calculated within that element tuple. For a dynamic `bytes`/`string` leaf the validated length word gives the unpadded payload range; the exclusive end must also equal the padded canonical end.

The validator has already proved that the tails are ordered, tight, non-overlapping, and exhaust each enclosing region. Boundary-aware navigation may rely on those facts **only while the same input remains immutable**; it MUST re-check arithmetic and containment before slicing and MUST NOT treat an arbitrary caller-created `ABIRange` as evidence of a valid field. If the schema compiler does not precompute next-dynamic-sibling metadata, the implementation MUST either document O(tuple arity) lookup or add a bounded caller-owned cache; it MUST NOT claim O(depth) without a demonstrated algorithm.

With this metadata, locating a field and its complete value range is O(path depth) with O(1) work per level (subject to bounded word reads), after strict full-payload validation. A runtime value tree/index is not needed. Optional caller-owned cached locations MAY be introduced only when benchmarks establish that they improve repeated lookups, never as a new source of validation provenance. The successful initial validation remains O(total logical values plus bytes/words examined).

---
## 6. Strict validator — implementation-ready algorithm

### 6.1 Entry and invariants

`ABIData.TryDecodeComplete(encoded, schema, limits)` MUST:

1. Verify schema is compiled/valid and all limits are positive or explicitly defined for empty documents.
2. Check `encoded.Length ≤ MaxEncodedBytes` before inspecting any input bytes.
3. Create a bounded stack of value frames, using a constant compile-time capacity. v2.1 requires an internal unmanaged `ABIValidationFrame[32]` stack; `MaxValueDepth` MUST be in `[1,32]` or fail with `InvalidArgument`. Stack capacity and actual frame layout are verified at compile time; no attacker-controlled `stackalloc` size.
4. Initialise the root **tuple-like encoding region** to `(start=0, declared component schema, expected end unbounded until traversal)`. Validate the *entire* supplied document; no unused trailing suffix is allowed.
5. Return an `ABIData` view only after the root's determined canonical end equals `encoded.Length`. Failed validation always returns a default-invalid view.

`ABICallData.TryDecode(call, function, limits)` MUST apply `MaxEncodedBytes` to **all call bytes including the four-byte selector**, check length ≥ 4, compare the four-byte big-endian selector with `function.Selector`, then invoke the **same** validator over `call.Slice(4)`. It MUST NOT validate the argument bytes a second time when constructing `call.Arguments`. Empty calldata is not a function invocation. A four-byte selector alone is valid for a schema with zero arguments.

### 6.2 Unmanaged validation frame

The private frame MUST be a fixed-size value type containing only integer/enum fields, not managed references or `ReadOnlySpan` fields. At minimum it retains:

| Field | Meaning |
|---|---|
| `NodeId` | Compiled schema node for this tuple/array/value |
| `ValueStart` | Absolute offset inside the root arguments document |
| `HeadBase` | Absolute start of this tuple-like region; for `T[]`, after count |
| `HeadSize` | Checked total head width in this tuple-like region |
| `HeadCursor` | Next child head location, including inline static widths |
| `TailCursor` | Next expected canonical dynamic child tail position |
| `NextChild` | Next ordinal of tuple member or array element |
| `ChildCount` | Declared tuple arity, fixed count or validated dynamic-array count |
| `PendingChild` | Child/return state, including dynamic versus static parent slot |
| `State` | Entering, processing head, awaiting child, completing |
| `Depth` | Defined schema/value depth for budget enforcement |

For every tuple-like region: `HeadBase ≤ HeadBase + HeadSize ≤ TailCursor ≤ encoded.Length` once bounds have been established. All dynamic child offsets are tested relative to **this frame's** `HeadBase`, never relative to calldata byte zero, except for the root arguments tuple whose head base is zero. The parent's frame is retained while the child is validated and its canonical encoded end returned.

### 6.3 Detailed traversal

Execute one **iterative depth-first** traversal; no recursive managed method calls, `yield` iterators, heap frames, LINQ or per-child `List<T>`:

**On entering a tuple:** Compute `headBytes = sum(HeadWidth(children))` from precompiled metadata. Prove `[start,start+headBytes)` lies within the supplied document by subtraction-based checks. Set `headCursor=start`, `tailCursor=start+headBytes`.

**On entering a fixed array:** Use `count=fixedCount` and `elementHeadWidth`; check the product and array bounds, then treat it as a tuple-like region beginning at `ValueStart`. For arrays with dynamic elements, the head contains exactly `count` 32-byte pointers.

**On entering a dynamic array:** Read the 32-byte count at `ValueStart`, require it fit both local indices and `MaxArrayElements`, establish `HeadBase=ValueStart+32`, and calculate the tuple-like head size as `count × HeadWidth(element)` with checked operations. Its canonical total size is `32 + elementsEncodedSize`.

**On entering dynamic bytes/string:** Read 32-byte unsigned length; reject high bits or a value greater than the remaining bounded buffer. Compute `padded=(length+31) & ~31` **only after** proving `length ≤ int.MaxValue−31`; check the entire `[length word][payload][padding]` span; verify all padding bytes zero. Return end=`ValueStart+32+padded`. For `string`, do not automatically decode UTF-8.

**For a static primitive:** Check 32-byte containment, then its exact canonical width/sign/padding constraints. Return end=`ValueStart+32`.

**For each tuple/array child:**

- Increment logical-value/work counters in a defined order before reading or descending; fail if a counter would exceed its limit.
- For a static child, its `ValueStart` equals its inline `HeadCursor`; validate it in place and require the returned size equal its compiled `StaticSize` (including zero-size nested tuples).
- For a dynamic child, the 32 bytes at `HeadCursor` encode a relative offset. Read as bounded unsigned word; require 32-byte alignment and `offset == TailCursor−HeadBase`. The equality test also rejects gaps, aliases and pointers into head space. Set `childStart = HeadBase+offset`, descend, and on return set `TailCursor = childEnd`.
- Advance `HeadCursor` by `HeadWidth(childType)`, **not** unconditionally by 32. For a static tuple or static fixed array this may consume many words; for an empty static tuple it advances by zero. Always advance the *logical element ordinal* even when encoded bytes consume zero space.

**On completing the container:** Require `HeadCursor == HeadBase+HeadSize`, all children visited, and return `End=TailCursor`. A static container must additionally consume exactly its precompiled static size. For a dynamic array the returned end includes the initial count word. On returning to a parent, update the pending child's cursor and proceed with the next child. At the root, `End` MUST equal the entire input length.

The implementation MUST keep *frame-local state* for parent processing. It MUST NOT overwrite the parent's next-head position or expected tail cursor with child's coordinate system. A descendant's canonical end may be larger than its own head end; the containing dynamic slot is validated only when the child finishes.

### 6.4 Exact work budgets and zero-sized values

Initial recommended limits (configurable at call site, not universal ABI restrictions):

| Limit | Default starting point | Enforcement |
|---|---:|---|
| `MaxEncodedBytes` | 262,144 bytes | Entire `ABICallData` including selector; or `ABIData` span |
| `MaxValueDepth` | 16; hard codec maximum 32 | Entry to a nested value/container |
| `MaxArrayElements` | 4,096 per array | Checked before head-size multiplication |
| `MaxVisitedValues` | 16,384 total | One charge on entry to each value, including zero-size tuple elements |
| `MaxWorkUnits` | 100,000 | Value entry and each 32-byte word read/inspected; repeated reads charged |
| `MaxSchemaNodes` | 4,096 | At compilation, not during request |
| `MaxSchemaDepth` | 32 | At compilation; decoder runtime limit may be lower |
| `MaxNestedExecutableCalls` | 256 | Policy integration, **not ABI validator** |

`MaxWorkUnits` MUST count (a) one unit per logical ABI value entered and (b) one unit per 32-byte word physically examined for scalar, offset, length, count or padding validation, including partial last word as one unit; repeated examination is charged again. Work accounting must be deterministic for the same data/schema/limits and independent of x64 versus ARM64 vectorisation. A value may have no encoded bytes, so work **cannot** be measured only by `encoded.Length`. Check budgets before incrementing; counter arithmetic is checked/saturating rather than wrapping.

Depth convention: the top-level arguments tuple is depth **1**. Every nested tuple, fixed array, dynamic array or primitive increases depth by one when entered. A `bytes` payload is one leaf, not a recursively discovered child ABI. The concrete frame stack MUST be able to represent the configured depth plus any transitional state without memory overrun. A root with zero arguments is depth 1 and consumes no input words but still charges its root value entry.

### 6.5 Canonical empty cases and repeated accesses

- Zero-argument root tuple: input must be zero bytes.
- Empty dynamic `bytes`/`string`: exactly one 32-byte zero-length word.
- Empty dynamic array of any valid element type: exactly one 32-byte zero-count word.
- Nested empty tuple: zero encoded bytes, valid only at the schema-derived location. Index iteration advances its element index even though the byte cursor does not advance.
- Nonempty dynamic array of empty tuple elements: exactly a 32-byte count and zero element bytes (when the element tuple is static and empty), bounded by `MaxArrayElements` and `MaxVisitedValues`.
- Dynamic child with an empty payload still has a nonzero encoded extent (at least its offset/length or count components). Its pointer MUST be canonical, not a shortcut into zero-filled head bytes.
- A successful schema-driven `TryLocate` recomputes field locations from compiled head displacements and already-validated offsets. It MUST NOT perform a new whole-document scan. No index is required for O(path-depth) lookup.

### 6.6 Complexity and exclusions

The validator's target complexity is **O(total logical values + bytes/words examined)** with a fixed bounded frame stack, not O(number of paths × full document length). Precomputed schema lookup and array offset tables permit O(depth) random access to specific validated fields. A non-strict or partially scanned view MUST NOT use the same `IsValid` contract as a fully validated view. If an optimised parser is eventually introduced for telemetry, it MUST have an explicitly separate **untrusted** API and cannot be substituted in signing-policy evaluation.

---

## 7. Encoding — direct canonical writer

### 7.1 API surface

Two writers share the same core schema-driven logic. `ABIWriter` encodes a single argument tuple into a caller-owned `Span<byte>`. `ABICallEncoder` is a thin adapter that first writes the immutable function's four-byte selector and then delegates to the **same** tuple writer on `destination[4..]`.

```csharp
public struct ABIWriteFrame
{
    // Public opaque scratch element; fields are private implementation state.
}

public ref struct ABIWriter
{
    public static bool TryBegin(
        ABISchema schema,
        Span<byte> destination,
        Span<ABIWriteFrame> scratch,
        in ABIEncodeLimits limits,
        out ABIWriter writer,
        out ABIStatus status);

    public bool TryWriteUInt64(ulong value, out ABIStatus status);
    public bool TryWriteInt64(long value, out ABIStatus status);
    public bool TryWriteUInt256Word(ReadOnlySpan<byte> word32, out ABIStatus status);
    public bool TryWriteInt256Word(ReadOnlySpan<byte> word32, out ABIStatus status);
    public bool TryWriteAddress(ReadOnlySpan<byte> address20, out ABIStatus status);
    public bool TryWriteBool(bool value, out ABIStatus status);
    public bool TryWriteFixedBytes(ReadOnlySpan<byte> bytes, out ABIStatus status);
    public bool TryWriteFunction(ReadOnlySpan<byte> address20, uint selector, out ABIStatus status);
    public bool TryWriteBytes(ReadOnlySpan<byte> bytes, out ABIStatus status);
    public bool TryWriteStringUtf8(ReadOnlySpan<byte> utf8Bytes, out ABIStatus status);
    // Fixed-point inputs are *already scaled*, exactly 32-byte canonical words.
    public bool TryWriteSignedScaledWord(ReadOnlySpan<byte> word32, out ABIStatus status);
    public bool TryWriteUnsignedScaledWord(ReadOnlySpan<byte> word32, out ABIStatus status);
    public bool TryBeginTuple(out ABIStatus status);
    public bool TryBeginFixedArray(out ABIStatus status);
    public bool TryBeginDynamicArray(int elementCount, out ABIStatus status);
    public bool TryEndContainer(out ABIStatus status);
    public bool TryFinish(out int bytesWritten, out ABIStatus status);
}
```

The fixed-point methods above are mandatory: they validate the word against the schema-declared signed/unsigned width and scale, and write the scaled integer directly without performing decimal arithmetic. The API MUST accept all declared width bounds without `BigInteger` allocations. `ABIWriter` is a `ref struct` because it owns borrowed output/scratch spans; `ABIWriteFrame` and `ABIMeasureFrame` are **unmanaged** non-ref value types with private mutable fields and measurable fixed sizes, because they appear in public caller-supplied scratch spans. Callers MUST NOT modify/reuse either scratch span while a writer or measurer is active; a scratch lease is exclusive for the operation. This ownership restriction must be made explicit even though the fields are private. Scratch must be filled/initialised by `TryBegin`; callers cannot provide a forged in-progress frame state. `ABIEncodeLimits` is validated at creation and enforced on every operation (encoded bytes, depth, array count, total written logical values and work); no array count may be used to size stack scratch. The encoder limits are independent of decoder limits, although a signing pipeline SHOULD use matching maximum payload sizes.

### 7.2 Leaf encoding and aliasing

Every leaf operation MUST check the next schema slot's type, declared width, input-value fit, `source.Length`, destination capacity, integer arithmetic and source/destination overlap **before any write**. On a failed leaf operation, that particular operation MUST leave destination unchanged and report zero new bytes committed; the enclosing writer moves to `Faulted` and cannot later finish. This deliberately distinguishes atomic *individual* operations from the full multi-operation document: a later failure may leave a partially written document in caller storage, which the caller must discard.

For v2.1, source spans MUST NOT overlap **any portion of the writer's destination span**, even if the particular write would happen not to overwrite the source. An overlap returns `SourceOverlapsDestination` before mutation. Use `Span.Overlaps`/`ReadOnlySpan.Overlaps` where applicable to enforce this; support for in-place encoding is a separate, explicitly tested future feature. This rule covers dynamic `bytes`, `string`, `bytes<M>` and address/function sources. Callers encoding from their own previously validated input span need a distinct, non-overlapping destination.

Primitive implementation rules:

- Write 32-byte ABI words directly, with big-endian numeric bytes and correct zero/sign extension. No `BitConverter` endianness dependence.
- A caller-supplied 32-byte integer word MUST be checked against the **declared `M` width**; a `uint64` overload MUST verify it fits the declared `uint8`/`uint16`/… slot rather than silently truncating. Plain integer writer methods MUST reject `Fixed`/`UFixed` slots; only explicitly scaled fixed-point writer methods may fill those slots.
- Write 20-byte address at byte offsets 12..31 of a zero-cleared word. Write `bytes<M>` at offsets 0..M−1 and clear the remainder. Write 24-byte function at offsets 0..23 and clear eight bytes.
- For dynamic bytes: write a 32-byte unsigned length, copy the supplied bytes once, write zero padding to the next 32-byte boundary. Empty `bytes` still consumes a length word.
- `TryWriteStringUtf8` assumes bytes supplied as UTF-8 and MUST either perform strict byte validation or make that precondition explicit in its name/contract; v2.1 requires **strict UTF-8 validation before mutation** for this named method. A separate raw-bytes method does not claim text validity. Invalid UTF-8 must return `InvalidUtf8`, not a generic padding or schema error.
- For fixed-point data, accept already-scaled full-width signed/unsigned 256-bit values and enforce declared width/sign extension; never call floating-point conversion methods as part of encoding.

### 7.3 Container writer invariant

The writer consumes values **in schema declaration/array element order**, but ABI byte layout is **head-then-tail**. The input event stream MUST not be permitted to skip, duplicate or reorder slots; the writer state and current schema node determine the next legal operation. Therefore the writer MUST reserve the **entire parent head region** as soon as a container opens, then backfill each static head or dynamic offset word while appending child dynamic data at a separate canonical tail cursor. Advancing a single cursor linearly through the supplied value order is incorrect.

Each internal writer frame MUST retain:

| Member | Required meaning |
|---|---|
| Node ID and container kind | Which schema is being written |
| Head base and full head size | The tuple-like region relative to which pointers are written |
| Head cursor / next ordinal | Where this child's head belongs and which child is next |
| Tail cursor | First free byte after dynamically appended encoded children |
| Expected and written element counts | Exact arity, including dynamic-array supplied count |
| Parent slot and continuation state | How to resume the enclosing container after child completion |
| Value-start position | For dynamic-array count and child tuple-like base |

For a root tuple, the head base is destination offset zero. For a dynamic array the first 32 bytes contain its count; the **element head base is value start + 32**. For a fixed array the element head base is the array value start. A *static* nested tuple or fixed array occupies its parent's inline head; a *dynamic* child tuple/array starts at the current enclosing tail cursor. The implementation MUST reserve the nested child's head before writing its children, and its parent MUST not advance its own dynamic tail cursor until the nested child is complete.

**Opening a container:** Verify expected schema type, writable destination and scratch capacity; for dynamic arrays validate element count against configured encoder limits before multiplying by head width. Reserve/write the head region, including a 32-byte count for dynamic arrays, and establish `headCursor=headBase`, `tailCursor=headBase+headSize`. For a dynamic child, write the parent's relative pointer equal to the parent's current tail position and open the child exactly there.

**Writing a static child:** Write into its schema-defined position in the parent head, advance the parent's head cursor by its declared static width, and leave the parent's tail cursor unchanged. Static compound child frames can write within this already reserved region.

**Writing a dynamic child:** The parent holds a pending dynamic slot. Write the pointer to the next free canonical tail position, encode the child at that position, then update the parent's `tailCursor` to the child's returned end. Advance its head cursor by 32 bytes only after the child is completed. Dynamic siblings are appended in declaration order.

**Closing a container:** Require all `ExpectedCount` logical children written, `headCursor == headBase+headSize`, no outstanding child, and that all bytes up to the container's computed end have been written. Return the container's computed end to the parent, then continue. Empty static tuples can close without writing bytes; logical index still advances.

**Root finish:** Require all root elements present, no unclosed frames, no pending dynamic offsets and the writer state `Open`. Set `bytesWritten` to the final canonical end, excluding unused caller-provided buffer capacity. Zero-argument root returns `bytesWritten=0` even with a nonempty destination buffer.

### 7.4 Writer state machine and failure atomicity

```text
Uninitialised --TryBegin succeeds--> Open
Open --begin nested container--> Open (push frame)
Open --end complete child-----> Open (pop frame, advance parent)
Open --finish complete root---> Completed
Open --any failed mutation----> Faulted
Completed/Faulted --mutation----> InvalidWriterState (no mutation)
```

`TryBegin` MUST check all immutable configuration, root head size and scratch capacity before reporting success; failed creation yields a default-invalid writer and MUST NOT modify destination. An attempted leaf or container action that fails due to type mismatch, overflow, count limit, overlap, insufficient destination or insufficient scratch transitions an active writer to `Faulted`. `TryFinish` called too early MUST return `IncompleteWrite` and move the writer to `Faulted`; any later attempt to finish or mutate MUST fail. A completed document cannot be resumed or reused. A writer MUST never claim success after a partial-buffer write.

To avoid an unobservable partially written buffer at the system boundary, the application SHOULD use a two-pass `ABIMeasurer` plus `ABIWriter`, or otherwise retain destination ownership and expose only `destination[..bytesWritten]` on successful completion. Input values MUST remain unchanged between measurement and writing.

### 7.5 Measurement and call-encoder adapter

`ABIMeasurer` MUST use exactly the same schema event sequence and nested head/tail sizing rules as `ABIWriter`, except it increments checked logical lengths instead of modifying a byte buffer. It MUST calculate all count words, offset tables, length words, nested heads, dynamic tails and padding, reject overflow and enforce matching configured budgets. It MUST be a `ref struct` with a `TryBegin(ABISchema, Span<ABIMeasureFrame>, in ABIEncodeLimits, out ABIMeasurer, out ABIStatus)` factory, and mirror the ABIWriter typed-value / begin-container / end-container event methods, with `TryFinish(out int encodedLength, out ABIStatus)` replacing the byte-output finish. `ABIMeasureFrame` is an opaque public value-type scratch element with private writer-owned fields. For dynamic bytes/string, the measuring operation takes the source span for the same type/length and UTF-8 checks as the writer. A length-only measuring overload MAY exist for ordinary raw `bytes` after the caller has separately established that the source bytes remain stable; it MUST NOT claim to validate UTF-8 from a length alone. Both measurement and writing MUST validate budgets and fail on limits/overflow; no measurement operation may mutate output storage.

`ABICallEncoder` MUST take an `ABIFunctionSchema`; its selector is not an independently supplied arbitrary value. It MUST be a `ref struct` with a factory `TryBegin(ABIFunctionSchema, Span<byte> destination, Span<ABIWriteFrame> scratch, in ABIEncodeLimits, out ABICallEncoder, out ABIStatus)` that checks the four-byte selector prefix capacity, and exposes the same schema-ordered value and container operations by delegation to one contained `ABIWriter`. `TryFinish(out int callBytesWritten, out ABIStatus)` succeeds only if the argument writer successfully completes and reports `4 + argumentLength`. It adds four bytes to measured arguments size with checked arithmetic, writes the selector to bytes `[0..4)`, encodes the arguments into `[4..]`, and exposes the complete output only after the argument writer has completed successfully. The `MaxEncodedBytes` limit applies to the **full call including selector**: the adapter MUST first require `MaxEncodedBytes >= 4`, then pass an effective arguments budget of `MaxEncodedBytes - 4` to its contained argument writer/measurer. A document-level encoder failure invalidates the entire call buffer even if the selector is already present.

### 7.6 Deterministic encode API and scratch ownership

`ABIWriter` and `ABIMeasurer` MUST implement exactly the same **schema-ordered event grammar**: `TryBeginTuple`, `TryBeginFixedArray`, `TryBeginDynamicArray(count)`, the type-correct `TryWrite*` methods, `TryEndContainer`, then `TryFinish`. The public method signatures and semantic differences (such as `bytesWritten` versus measured size) MUST be documented together in API reference material and compile-time contract tests. A bare scalar cannot be written at the root of `ABIWriter`; the root is always a tuple of supplied arguments, even when that tuple has one member. The optional `ABICallEncoder` prepends the selector but does not change the event grammar.

A failure in any mutating event faults the writer, even if that event has not touched the buffer. On failure an `out` count is always zero and remains unusable as a committed length. Caller scratch, destination, and all borrowed input spans MUST remain exclusively leased for the entire attempt; concurrent mutation is unsupported. The writer cannot make a partially written destination magically atomic for an external observer. Only publish/return encoded bytes after `TryFinish` succeeds, or use an isolated output buffer plus an explicit commit step when third parties can observe the destination.

### 7.7 Required encoder invariants

1. Every dynamic pointer equals the next available tail offset relative to the correct containing tuple-like head base.
2. Every static slot occupies exactly its compiled width, including multiword tuples and arrays and zero-byte empty tuples.
3. No two distinct **nonempty** encoded child regions overlap and no gap exists between canonical dynamic tails. Empty values have no payload bytes but still occupy a logical child position.
4. Every scalar is canonical and every unused padding byte is zero, including when the destination buffer initially contains nonzero garbage.
5. `Decode(Encode(values))` recovers each typed value; `Encode(Decode(canonical bytes))` reproduces identical bytes for the supported schema.
6. `bytesWritten` excludes destination excess; no failed writer's output can be used as a valid result.
7. Alias detection, width/type errors and capacity errors occur before *that operation's* first write; no `IndexOutOfRangeException` or integer overflow from malicious input.

---

## 8. Field navigation and nested calldata boundaries

### 8.1 Path-based lookup

A path is a sequence of non-negative zero-based indices. At a tuple it selects a component; at a fixed/dynamic array it selects an element. Example: path `[1,3,0]` means root component 1 → array item 3 → tuple component 0. Empty path identifies the root tuple only. Array counts are metadata and are returned through `TryGetArrayLength`, not addressable as synthetic children.

A locator MUST use compiled tuple head offsets, static array strides and dynamic array offset tables. Every dynamic pointer it encounters has previously been checked by a successful complete decoder and is followed relative to the appropriate local head base. Location calculations still MUST perform defensive bounds and overflow checks (e.g. to detect accidental input ownership violations) but MUST NOT silently interpret unvalidated arbitrary ranges as ABI values.

### 8.2 Worked `multicall(bytes[])` example

Assume `multicall(bytes[])` contains two `bytes` entries. Each entry contains a 68-byte function invocation: four-byte selector and two 32-byte arguments. The standard ABI layout is:

| Region | Outer calldata offset | Field value / interpretation |
|---|---:|---|
| Outer selector | 0 | Four bytes |
| Root argument head | 4 | `0x20`, array value relative to arguments base 4 |
| Array count | 36 | `0x02` |
| Array element-table base | 68 | Immediately after count word |
| Element 0 offset word | 68 | `0x40` relative to table base 68 |
| Element 1 offset word | 100 | `0xc0` relative to table base 68 |
| Element 0 `bytes` length word | 132 | `0x44` = 68 bytes |
| Element 0 payload | 164 | 68 unpadded bytes; 96 allocated data bytes |
| Element 1 `bytes` length word | 260 | `0x44` = 68 bytes |
| Element 1 payload | 292 | 68 unpadded bytes; 96 allocated data bytes |
| End of calldata | 388 | Four selector bytes + 384 argument bytes |

The corresponding arguments-relative payload ranges are `(160,68)` and `(288,68)`; every outer calldata offset is four greater than its arguments-relative counterpart. These numbers MUST be independently reconstructed in tests from a canonical encoder, rather than hardcoded as the sole source of truth. For this fixture, `head=32`, `arrayCount=32`, `elementTable=64`, and each element encoding is `lengthWord(32)+paddedBytes(96)=128`, giving `arguments=32+32+64+128+128=384` bytes and `calldata=4+384=388` bytes. Each extracted child, *if a trusted execution descriptor says it is a call*, starts a new `ABICallData` document whose selector is at child-relative byte zero. The locator MUST NOT copy, re-encode, allocate child arrays or assume a `bytes[]` element is executable without external semantics.

### 8.3 Other nested layouts

The codec MUST support nested `T[]`/`T[k]`/tuples with arbitrary combinations of static and dynamic member types. A nested `(address,uint256,bytes)` has the encoded `bytes` tail offset relative to that **tuple's start**; its target and native value are encoded arguments, not capabilities established by the ABI library. A router command stream or contract-specific packed sequence is not automatically decoded by interpreting embedded four-byte patterns.

A separate `NestedCallWalker` in an optional adapter may consume validated `ABICallData`, verified execution descriptors and a caller-owned bounded frame stack. It MUST preserve child target, call/delegatecall mode, sender/value semantics, policy context and exact child byte location. Each discovered child call MUST be independently resolved to the correct trusted function schema, validated and evaluated. The walker MUST fail closed on unsupported or ambiguous executable children, exceedance of limits, or unknown execution descriptors.

Some EVM calls depend on code paths, storage, proxy implementation, delegatecalls or off-chain state and are not statically enumerable from transaction calldata. The codec's nested-call support **does not prove complete execution coverage**, nor replace simulation or trusted bytecode analysis.

---
## 9. Security threat model and control coverage

### 9.1 Assets and threat actors

**Assets:** transaction signing decisions, sender/recipient/amount/function interpretation, approved signing digest, contract and schema registry integrity, caller-owned buffers, signer service availability, application audit records and encoded outputs that could be signed or submitted on chain.

**Threat actors:** transaction submitters supplying arbitrary bytes; malicious contracts with deceptive selectors or custom fallback handlers; compromised registry/configuration inputs; concurrent code modifying caller-owned buffers; upstream components providing oversized or incorrectly bounded transaction data; and internal callers misusing range, writer or schema APIs.

### 9.2 Threat-to-control matrix

| Threat | Impact | Required control | Evidence to test |
|---|---|---|---|
| Forged dynamic offset | Wrong value/recipient interpretation | Canonical relative-base check; offset equals tail cursor | Head-target, gap, overlap, backward and out-of-range fixtures |
| 256-bit length/count overflow | Bounds bypass, crash, excessive work | Full-width check before `int` cast; subtraction-based containment | `2^256−1`, `2^31`, `int.MaxValue±1` fixtures |
| Static compound width mistake | Incorrect subsequent arguments | Precomputed full static widths and checked head cursor | Static nested tuple/array followed by other parameters |
| Hidden trailing bytes | Different consumer interpretations | Require computed root end equals input length | Valid root plus extra 1/32 bytes |
| Noncanonical signed/boolean data | Divergent scalar interpretation | Width/sign extension, exact 0/1 boolean | Invalid high bits and padding cases |
| Selector collision | Wrong schema chosen | Contract-scoped identity resolver; no heuristic choice | Two signatures sharing selector; explicit ambiguity rejection |
| Arbitrary bytes misclassified as calls | Fake or missed executable operations | Only metadata-declared child calldata is traversed | Opaque bytes containing known selectors remain opaque |
| Allowed outer multicall hides forbidden child | Policy bypass | Enumerate all declared executable descendants and fail closed | Parent allowed, child prohibited regression |
| Proxy or delegated-account changes | Authorisation against stale implementation | Verified code/implementation binding external to codec | Upgrade or changed-delegation integration scenarios |
| Mutable backing input | TOCTOU; approve different bytes | Exclusive immutable request ownership and digest binding | Mutate backing array and require ingress/signing guard to deny |
| Encoder source/destination alias | Corrupted encoded output | Reject overlap before leaf writes | Same buffer, partial overlap, disjoint buffer cases |
| Writer misuse | Partial or malformed output treated as complete | Faulted terminal state; expose bytes only after finish | Incomplete, double finish, wrong schema slot, OOM-style capacity failure |
| Excessive nested containers / empty values | CPU or stack exhaustion | Depth, array, visited-value and work limits | Many zero-width tuples, nested arrays, limit boundaries |
| ABI ambiguity with permissive Solidity decoder | Policy checks differ from chain behaviour | Strict signing default; record compatibility limitations | Noncanonical but Solidity-decodable sample rejected |
| Malformed schema or poisoned selector | Compromised interpretation | Immutable compiler output; canonical signature and Keccak checks | Invalid grammar, cycles, source mutation, hash vectors |
| Unbounded logging of raw payloads | Exposure, log amplification | Structured error codes, bounded diagnostics, no secret byte dumps | Redaction and error-rate integration tests |

### 9.3 Explicit non-guarantees

The codec does not verify that a contract exists, that a selector maps to a particular deployed function, that a call succeeds, that transaction value or token amounts are safe, or that all internal EVM calls are statically visible. Strict ABI decoding is not an EVM simulator, wallet signature verifier, contract security scanner or policy engine. A production signer MUST independently establish the exact signing preimage/digest, chain ID, signer, policy snapshot and valid signing-authorisation record.

### 9.4 Trust-preserving integration with RLPData

The EVM transaction adapter is responsible for decoding typed or legacy transaction envelopes and extracting a validated RLP byte-string payload for the transaction `data` field. It passes that **unchanged span** into `ABICallData` only after trustworthy destination/contract context is established. The RLP codec verifies its own structural format; it cannot validate nested ABI arguments. The ABI codec verifies ABI structure; it cannot validate the outer transaction signature, sender, nonce, chain ID or execution effects. Each layer MUST retain its own coordinate system, provenance and failure contract.

EIP-2718 transactions are not unconditionally bare RLP documents. Fallback/receive dispatch, contract creation initcode and EIP-712 typed structured messages MUST use distinct adapters; treating any arbitrary transaction `data` as a function call is prohibited.

---

## 10. Complete automated test specification

Every implementation change MUST include automated tests covering normal data flow, negative cases, boundary conditions, invalid/malicious data, invariant preservation and failures. Every discovered bug becomes a minimal regression vector. Tests MUST be runnable without external RPC or a live blockchain for core codec correctness.

### 10.1 Canonical byte vectors

For each vector, assert full encoded byte identity, strict decode success, exact `ABIFieldLocation` head/value/payload ranges, typed accessor results, measurement results, encoder round-trip identity and no surplus accepted bytes.

1. `transfer(address,uint256)` with selector `0xa9059cbb`: first argument address is right-aligned in a 32-byte word; second argument is a 32-byte unsigned value; total calldata is 68 bytes.
2. `approve(address,uint256)` with selector `0x095ea7b3`, including zero spender/amount and maximum 256-bit amount.
3. Known Keccak selector vectors independently checked against a trusted external implementation: `transfer(address,uint256)` and `approve(address,uint256)`; reject use of SHA3-256 in place of Keccak-256.
4. Zero-argument function: selector-only 4-byte call; zero-length root ABI argument document; reject trailing extra bytes.
5. Scalar variants: `uint8/16/64/128/256`, `int8/16/64/128/256`, bool, address, `bytes1/bytes24/bytes32`, `function`, `fixed128x18`, `ufixed128x18` and declared-width overflow boundaries.
6. Dynamic bytes and strings of length **0, 1, 31, 32, 33, 63, 64, 65**, including full-byte round trips and exactly correct zero padding.
7. Static `(uint256,uint256)` nested inside another tuple, and `(uint256[3],bool,bytes)` with head positions demonstrably exceeding a single word.
8. Dynamic `uint256[]`, `bytes[]`, `string[]`, fixed `uint256[3]`, fixed `bytes[2]`, `(address,uint256,bytes)[]`, fixed arrays of dynamic tuples, arrays of arrays and nested mixed static/dynamic tuples.
9. `multicall(bytes[])` from §8: total calldata **388 bytes**, child payload ranges **(160,68)** and **(288,68)** relative to arguments, and selector-relative outer ranges **(164,68)** and **(292,68)**.
10. Solidity documentation examples: `bar(bytes3[2])`, `baz(uint32,bool)`, `sam(bytes,bool,uint256[])`, `f(uint256,uint32[],bytes10,bytes)` and `g(uint256[][],string[])`.
11. Nested empty tuple `()`, tuple of empty tuples, dynamic array of empty tuple elements and fixed arrays of empty tuples; assert finite traversal and budget charges despite zero encoded child length. Reject **zero-length fixed arrays** with `UnsupportedSchema`.
12. Empty dynamic arrays and strings inside nested nonempty tuples, with tail pointers ending exactly at the canonical boundary.
13. Malformed UTF-8 raw `string` payload: structural validation succeeds, strict text validator rejects, and no silent replacement or normalisation occurs.
14. Values at integer-width minima/maxima, and fixed-point scaled integer extrema; ensure no floating-point dependency.

### 10.2 Adversarial malformed ABI fixtures

Tests MUST cover each distinct failure status and assert that failed decode produces `IsValid=false`, defaulted out values and no exception. Focus on these exact categories:

- Root tuple too short or too long, truncated word at every position, input containing a single unexpected byte, and trailing data after a canonical value.
- Pointer = 0 when nonempty head precedes a dynamic tail; pointer into head, pointer to middle of previous scalar, wrong 32-byte alignment, pointer beyond input, pointer equal to another child's tail, pointer with nonzero high-order bytes, pointer to an earlier sibling, gaps and tail reversal.
- Dynamic array element offsets interpreted relative to the wrong base; a nested tuple whose offsets are incorrectly interpreted relative to the outer selector or outer arguments root.
- Dynamic length/count `2^256−1`, `2^255`, `int.MaxValue+1`, `MaxArrayElements+1`, and declared limit±1. Test multiplication/rounding overflow before reading or allocating.
- Nonzero tail padding for `bytes`/`string`, nonzero right padding for `bytes<M>` and `function`, nonzero leading address bytes, invalid signed extension for positive/negative narrow ints, `bool=2`, and `uint8=256`.
- Extra dynamic pointer words, unvisited extra array element tails, overlapping dynamic tails, duplicate child tails, and input containing valid prefix plus garbage suffix.
- Empty arrays nested to depth limit; many zero-byte tuple elements inside large arrays; bounded high logical-value count with very small encoded payload.
- Invalid path entries: negative element, out-of-range child, path through primitive, wrong typed accessor, default-invalid view, caller-created arbitrary range and coordinate translation that exceeds original call bounds.
- Schema cycles, invalid `fixed` precision, invalid width not multiple of 8, invalid `bytes33`, malformed canonical signature, unsupported fixed length 0, excessive schema nodes/depth and static-size overflow.
- Unknown or ambiguous external selector resolution and mismatch of otherwise valid argument bytes to expected selector.

For each configured budget, test **limit−1**, **limit**, **limit+1**, invalid negative/zero limits, and a value that would overflow the chosen counter. Failures must return deterministic statuses on every supported architecture.

### 10.3 Encoder and writer state tests

For each supported schema shape, compare measured size with successfully written length, run strict decode on the result and assert byte-for-byte equivalence with an independent reference. Explicit negative tests:

- Insufficient root head capacity at `TryBegin` leaves destination unchanged and writer invalid.
- Insufficient tail capacity after earlier successful operations produces a faulted writer; finalisation fails and partial output is never exposed as valid.
- Insufficient scratch for nested container; excessive array element count; fewer or more elements than declared; wrong next ABI kind; premature `TryEndContainer`; missing `TryEndContainer`; duplicate end; `TryFinish` with pending children; duplicate finish; write after `Completed` and after `Faulted`.
- Source/output identical, partial overlap, and disjoint input/output; assert no mutation for rejected leaf operations.
- Destination prefilled with nonzero bytes: output uses zeros for every ABI-required padding byte and every word's unused high/low portion.
- Encoder and measurer inputs mutate between passes: caller-side precondition test rejects such misuse or documents resulting difference; do not claim writer can prevent arbitrary external mutation.
- Static tuples after dynamic fields, multiple dynamic siblings, fixed array of dynamic tuples, nested array count backfill and zero-width tuple elements.
- **Boundary-aware locator tests:** dynamic field followed by static fields; first/middle/last dynamic field; nested last dynamic child inheriting an ancestor end; dynamic tuple inside a dynamic array; arrays of dynamic arrays; zero-sized tuple elements; and `TryLocate` of a composite `ValueRange` where calculating the end from its own children would be expensive. Verify the next-dynamic-sibling end calculation and prove access complexity does not increase with unrelated sibling or descendant count on fixed-depth schemas.
- Signed/unsigned word input of incorrect declared width, short address/function inputs, oversized fixed bytes, invalid UTF-8 in `TryWriteStringUtf8`, and checked-length overflow.

### 10.4 Differential, property and fuzz testing

- Use **two independent implementations** where available, e.g. a Solidity contract/harness via Foundry or solc and a separate Nethereum or `eth-abi` implementation. Pin versions, inputs and expected outputs. Disagreements MUST be investigated; never mutate expected vectors merely to match the library.
- Generate bounded recursive type trees and values, encode→decode→encode and assert **byte identity** for canonical inputs, with explicit coverage of static and dynamic compound types.
- Generate malformed near-miss inputs by mutating pointers, counts, offsets, scalar high bits and padding. Confirm fail-closed behaviour. Solidity's more permissive decoder may accept some intentionally rejected samples; document that distinction.
- Fuzz the schema parser/compiler, function-selector compiler, decoder, field locator, writer state machine and RLP→ABI adapter. Run regression corpus against every commit. Tooling may be SharpFuzz or an equivalent runner; if unavailable, provide deterministic high-volume mutation tests and record coverage limitations.
- Assert absence of unhandled parser exceptions, indefinite loops, stack overflow, managed allocations proportional to malicious counts and out-of-bounds memory access. Run test execution with finite timeouts at the harness level.
- Test x64 and ARM64 and both Debug/Release where CI infrastructure permits. Skipped targets remain explicitly unverified; do not claim coverage by inference.

### 10.5 End-to-end security contract tests

At least one test flow MUST cover: stable owned transaction input → RLP-calldata extraction → target identity resolution → `ABICallData` strict validation → selector/argument rules → nested `bytes[]` child extraction when descriptor demands it → child-specific schema validation → policy denial of forbidden child → exact digest binding in signing request.

Additional tests MUST prove that a selector-only match, untrusted four-byte database match, unknown call execution descriptor, changed proxy implementation or mutated input buffer **cannot** authorise a signature. No live signer/key material is required; use a deterministic fake signing boundary with asserted approval provenance.

---

## 11. Performance requirements and measurement plan

### 11.1 Optimisation priorities

In order of priority: **correctness → bounded work → zero-copy → no per-request allocations → schema-precomputed random access → CPU optimisation only after profiling**. The library MUST prefer predictable performance over speculative micro-optimisations that weaken validation.

- Store input as borrowed spans; compile ABI schemas once at startup or build time.
- Use a small unmanaged frame structure and bounded stack scratch; no `new` per node or field.
- Read words using `BinaryPrimitives` and fixed-size byte comparisons; no `BigInteger`, string formatting, hash-table lookup per field or reflection in the decode path.
- Validate all fields on the first pass, then use tuple head offsets and array indexing for targeted access. Avoid a per-field cache unless benchmarks show repeated traversal is a measured bottleneck.
- In the writer, clear/write only the bytes in the completed encoded document, not the entire oversized destination capacity.
- Hex decoding and RLP parsing occur outside ABI benchmarks and MUST be measured separately for end-to-end signing evaluation.

### 11.2 Required workloads

| Workload | Primary metrics |
|---|---|
| Two-field static `transfer` call | Decode nanoseconds/op, bytes allocated/op, fixed word reads |
| Mixed `(address,uint256,bytes)` | Validator latency and selective access |
| Dynamic `bytes` at 0, 31, 32, 33, 65 bytes | Tail validation and padding overhead |
| `multicall(bytes[])`, 2 and 128 children | Array-offset validation and nested inspection overhead |
| Nested dynamic tuple/array depth 1, 8 and 16 | Stack usage and p50/p99 latency |
| 256-KiB bounded bytes payload | Memory bandwidth and no copying |
| Malformed offset rejected near beginning/end | Bounded rejection latency |
| Many zero-byte tuple elements | Logical-value budget enforcement, no infinite loops |
| Encoding single/multiple dynamic arguments | Bytes written, CPU, allocation and scratch requirements |
| RLP extraction + ABI decoding | Integration overhead without copy or duplicate validation |

### 11.3 Release gates

- **Zero managed bytes allocated per successful steady-state `ABIData` decode, `ABICallData` decode, `TryLocate`/primitive accessor and caller-backed `ABIWriter` operation**, after schema compilation, JIT warm-up and caller-owned input/scratch setup. Explicit optional materialisation APIs are excluded and must be measured separately.
- **No allocations proportional to attacker-supplied lengths/counts**, irrespective of success or failure. Schema compilation is an explicit bootstrap cost.
- CPU benchmarks MUST use BenchmarkDotNet in Release with recorded target framework, CPU model, operating system, architecture, process/runtime settings, workload fixture sizes, iterations and confidence intervals.
- Collect latency distribution for full policy integration separately from codec microbenchmarks. Report throughput, median, p95/p99, allocated bytes, Gen0/1/2, stack/scratch budget and peak process memory as applicable.
- No arbitrary universal sub-microsecond promise exists until real hardware measurements. Record an accepted baseline and require investigation for **more than 10% median regression** in a statistically stable fixture, while retaining correctness as an absolute gate.
- Any optimisation MUST pass the same canonicality, adversarial and differential suites before a performance improvement can justify merging it.

---

## 12. Packaging and dependency boundaries

Recommended repository structure:

```text
src/
  Evm.Abi/
    ABIData.cs
    ABICallData.cs
    ABIRange.cs
    ABIFieldLocation.cs
    ABIStatus.cs
    ABIDecodeLimits.cs
    ABIEncodeLimits.cs
    ABIWriter.cs
    ABIMeasurer.cs
    ABICallEncoder.cs
    Internal/
      ABIValidator.cs
      ABIValidationFrame.cs
      ABILocator.cs
      ABIWord.cs
      ABIWriteFrameState.cs
    Schema/
      ABISchema.cs
      ABIFunctionSchema.cs
      ABISchemaCompiler.cs
      ABITypeDefinition.cs
      ABISelector.cs
  Evm.Abi.EvmAdapters/       # optional separate assembly
    RlpCalldataAdapter.cs
    ContractFunctionResolver.cs
    ExecutionDescriptor.cs
    NestedCallWalker.cs

tests/
  Evm.Abi.Tests/
    GoldenVectors/
    Validation/
    Scalars/
    WriterState/
    Adversarial/
  Evm.Abi.IntegrationTests/
  Evm.Abi.FuzzTests/
benchmarks/
  Evm.Abi.Benchmarks/
docs/
  architecture.md
  api.md
  wire-format.md
  threat-model.md
  performance.md
  integration.md
```

The schema compiler MUST initially ship in the same core library/assembly as compiled schema descriptors to avoid unnecessary assembly cycles. JSON ABI import, registry persistence, RPC and contract metadata acquisition SHOULD be separate optional tooling or startup adapters. The core MUST run without a dependency on Nethereum; independent ABI libraries are welcome as **test references**, not required runtime dependencies.

Maintain the deliberately chosen names `ABIData` and `ABICallData` for consistency with the existing `RLPData` project, even though `AbiData` would be more idiomatic general .NET acronym casing. Types SHOULD be sealed/immutable or `readonly` as appropriate; the encoder is mutable but stack-scoped.

---

## 13. Implementation execution plan

### Schema and signature foundation

Implement type grammar, immutable schema definition/compilation, head widths, static sizes, tuple head displacements, array dimensions, Keccak-256 signature selectors and compile-time budgets. **Complete when** all legal supported forms compile, malformed/recursive/overflowed schemas fail deterministically, and selector vectors match an independent implementation.

### Strict decoder and borrowed data types

Implement word reader, scalar checks, the checked iterative validator, complete-document enforcement, resource limits, `ABIData` and `ABICallData` factories. **Complete when** official and adversarial vectors pass and no failed validation yields a valid view.

### Range locator and typed accessors

Implement root-relative `ABIFieldLocation`, schema-path traversal, direct static strides, dynamic offset lookups, exact scalar narrowing, fixed/dynamic bytes and address access. **Complete when** all range, type-mismatch, empty-container and coordinate-translation tests pass with zero managed allocations on warmed hot paths.

### Canonical writer and measurer

Implement typed primitive writes, scratch frame state machine, root/nested head reservations, ordered tails, dynamic-array counts, checked sizing, finish/fault rules and `ABICallEncoder`. **Complete when** full reference round trips, overlap/failure-state tests and exact byte measures pass across all supported type combinations.

### EVM integration and security

Provide reference adapters for RLP-extracted calldata, verified function schemas and explicit nested execution descriptors, without embedding policy or RPC in the codec. **Complete when** tests deny unknown/forbidden nested operations and stale/mutated signing context, while exact field/child ranges remain correct.

### Hardening and release evidence

Perform independent differential verification, fuzzing, cross-architecture tests, BenchmarkDotNet performance baselines and threat-model review. Document runtime limits, actual behaviour, compatibility rejection cases and external authorisation obligations. **Complete only when** every release gate below has evidence.

---

## 14. Acceptance checklist

- [ ] .NET 10/C# 14 library compiles on declared targets; warnings and analyzers pass, with failures addressed or explicitly explained.
- [ ] `ABIData` and `ABICallData` share one validated schema interpreter; no duplicate or contradictory decoding logic.
- [ ] Full supported standard ABI type coverage: static/dynamic tuples/arrays, fixed-point representations and canonical padding; explicit rejection of fixed zero-length arrays.
- [ ] Parser consistently rejects non-canonical data, malformed/truncated words, invalid offsets, overflowed counts and unexpected trailing bytes.
- [ ] Read-only borrowed views, ownership preconditions, path semantics, default-invalid state and document-relative coordinate rules tested.
- [ ] Full `ValueRange` for dynamic composite paths is correct without descendant rescans, using verified next-dynamic-sibling/end-bound metadata; lookups are benchmarked at fixed path depth with growing sibling counts.
- [ ] Strict validator enforces maximum bytes, depth, array elements, logical values and deterministic work units; zero-sized values cannot bypass limits.
- [ ] Selector and schema compilation validated against independent Keccak and official Solidity vectors; collisions and unknown schemas never silently resolve.
- [ ] Direct encoded output is canonical for every supported type; writer state transitions, measuring, overlap rejection and incomplete/faulted behaviour tested.
- [ ] No per-request hot-path managed allocations for mandated operations; no input copies after trust-boundary ingress; stack/scratch usage bounded and measured.
- [ ] Golden, boundary, negative, property-based, fuzz and differential test suites pass, with a preserved regression corpus.
- [ ] Nested calldata interpretations are enabled only by trusted execution descriptors, with independent child schema validation and fail-closed authorisation tests.
- [ ] RLP integration preserves byte identity and ownership; exact approved signing digest cannot change without a new validation/decision.
- [ ] Benchmarks and full-policy latency measurements recorded with reproducible configuration and comparison baseline.
- [ ] README, API reference, wire-format examples, security threat model, compatibility policy and implementation limitations are published accurately.

The present artefact is a design specification, **not executable code**; no .NET compilation, memory-allocation measurements, runtime fuzzing, signer integration or cross-architecture tests have yet been performed. No result is considered a completed library release if a required test, security check or benchmark has not been run. Omitted platform checks and known limitations must be stated plainly.

---

## 15. Implementation conformance requirements

The coding agent MUST supply a traceable conformance matrix linking each public API and wire-format rule to at least one normal, boundary, invalid-input and adversarial test. At minimum it must map:

| Contract | Mandatory evidence |
|---|---|
| Immutable schema and selector identity | Bootstrap validation; selector vectors; conflicting/cyclic descriptors rejected |
| Complete root validation | Golden vectors; forged offsets, overlaps, gaps, trailing bytes and truncated inputs rejected |
| Borrowed view provenance | Default-invalid accessors; input ownership simulation; no trusted arbitrary range constructor |
| Constant-depth navigation | Next-sibling boundary propagation; composite `ValueRange`; variable sibling-count benchmarks |
| Scalar correctness | Width extremes, two's-complement sign extension, narrowing overflow, function/bytes padding |
| Nested values | Multi-dimensional static/dynamic arrays, tuple nesting and zero-byte tuple traversal limits |
| Direct encoding | Exact head/tail offsets, measured sizing, scratch capacity, fault-state monotonicity |
| Embedded calldata | ABI `bytes` remains opaque until contract execution metadata identifies a child; each child gets a new trusted schema |
| Allocation/performance | Post-warm-up `GC.GetAllocatedBytesForCurrentThread` assertions and BenchmarkDotNet fixtures |
| Signing boundary | No authorisation with stale buffer, stale implementation identity, ambiguous selector or unknown executable child |

Unimplemented surface APIs MUST be marked incomplete rather than replaced by ad hoc public methods. For every supported type, include encode→decode→encode identity tests and compare to independent Solidity-derived fixtures. Use verified SHA3-vs-Keccak selector test vectors to prevent accidentally selecting NIST SHA3-256.

---

## 16. Usage examples (API sketches)

### 16.1 Decode a complete function call

```csharp
// The caller resolved an immutable function schema using trusted
// contract identity before this call and guarantees stable input bytes.
if (!ABICallData.TryDecode(
        calldata,
        trustedFunctionSchema,
        in decodeLimits,
        out ABICallData call,
        out ABIStatus status))
{
    return DenyMalformed(status);
}

// The selector is verified against the supplied schema.
ReadOnlySpan<int> recipientPath = stackalloc int[] { 0 };
ReadOnlySpan<int> amountPath    = stackalloc int[] { 1 };

if (!call.Arguments.TryGetAddress(
        recipientPath, out ReadOnlySpan<byte> recipient, out status))
    return DenyMalformed(status);

if (!call.Arguments.TryGetWord(
        amountPath, out ReadOnlySpan<byte> amountWord, out status))
    return DenyMalformed(status);

// The external policy checks chain, contract identity, recipient, amount,
// complete nested-call semantics and the exact signing payload.
```

### 16.2 Locate nested calldata without copying

```csharp
// Only when verified contract execution metadata says root parameter 0
// is an executable bytes[] collection.
ReadOnlySpan<int> firstChildPath = stackalloc int[] { 0, 0 };
if (!call.Arguments.TryGetBytePayload(
        firstChildPath,
        out ReadOnlySpan<byte> childCalldata,
        out ABIRange childPayloadRange,
        out ABIStatus childStatus))
{
    return DenyMalformed(childStatus);
}

// Resolve the child's actual target and trusted ABI schema *externally*.
// A bytes range is not proof of execution and never grants approval alone.
if (!ABICallData.TryDecode(
        childCalldata,
        trustedChildFunctionSchema,
        in decodeLimits,
        out ABICallData child,
        out childStatus))
{
    return DenyMalformed(childStatus);
}
```

These examples are behavioural illustrations, not evidence that the library has been compiled. The final implementation MUST confirm C# ref-safety and cannot preserve borrowed views across an `await` or beyond buffer ownership.

---

## 17. Normative references

1. Solidity, [Contract ABI Specification](https://docs.soliditylang.org/en/latest/abi-spec.html): canonical type grammar, function selectors, formal head/tail model, arrays, strict mode and supported/unsupported non-standard modes.
2. Solidity, [Frequently Reported Non-Bugs](https://docs.soliditylang.org/en/latest/frequently-reported-non-bugs.html): permissive decoding of noncanonical calldata, explaining the explicit strict-mode compatibility decision.
3. Microsoft, [ref struct](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/ref-struct): C# stack-only type and lifetime constraints.
4. Microsoft, [Write safe and efficient C# code](https://learn.microsoft.com/en-us/dotnet/csharp/write-safe-efficient-code): span-oriented, allocation-aware implementation patterns.
5. Ethereum, [EIP-2718](https://eips.ethereum.org/EIPS/eip-2718): typed transaction envelopes and the transaction/ABI boundary.

---

**Specification boundary:** This is the complete intended implementation contract for `ABIData` and `ABICallData`. Passing a decode method establishes canonical byte structure for a selected immutable schema and stable input; it does not establish deployed contract identity, execution effect or signing authorisation. The .NET implementation, automated testing, independent vector validation, fuzzing and hardware measurements remain release acceptance requirements.
