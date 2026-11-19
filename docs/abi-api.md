# ABI API

Namespace `EccentricWare.Web3.DataTypes.ABI`. A strict, allocation-free codec for standard Solidity ABI encoding: compile a trusted schema once, validate a whole document against it, read fields in place, and encode canonical documents into caller-owned buffers. Wire rules are in `docs/abi-wire-format.md`; threat model, signing-path contract and checked-arithmetic notes in `docs/abi-security.md`; the RLP boundary in `docs/abi-integration.md`; measurements in `docs/abi-performance.md`; the traceability matrix against the design specification in `docs/abi-conformance.md`. The specification is `docs/specs/ABI_DATA_Design_Specs.md` (v2.1); decisions where it is silent or self-contradictory are the numbered rows of `docs/abi-design-notes.md`.

## Rules that apply to every member

1. **No exceptions on bad input.** Every `Try*` method returns `bool` and never throws for any bytes, limits, paths or arguments (null schema and null function, a default view, and a scratch span of any claimed length included). The decode, locate, accessor, write, measure and call-encode members report an `ABIStatus`; the schema compile and parse members (`ABISchemaCompiler.TryCompile*`, `ABITupleDefinition.TryParse`, `ABITypeDefinition.TryParse`) report an `ABISchemaError`, whose `Status` is an `ABIStatus`. Deny on every status other than `Success`. A status is a diagnostic, not an authorisation decision.
2. **No heap allocation** on the decode, locate, accessor, write, measure and call-encode paths, on success and on failure. The caller supplies all buffers. Schema compilation and text parsing allocate (bootstrap only).
3. **Validate once, then trust (while the bytes stay put).** `ABIData.TryDecodeComplete` checks the whole document; accessors follow pre-validated offsets and do not rescan.
4. **Failure leaves outputs defined.** On failure every `out` value is `default`, a failed decode yields a default (invalid) view, and a failed write operation changes no destination byte (the writer itself is faulted for good: see "State machine" below).
5. **Safe defaults.** `default(ABIData)`, `default(ABICallData)`, `default(ABIWriter)`, `default(ABIMeasurer)` and `default(ABICallEncoder)` are invalid; every operation answers `InvalidArgument` (readers) or `InvalidWriterState` (writers). There is no public constructor that can create a valid view from arbitrary bytes or ranges.
6. **Strict, not permissive.** Only the canonical form Solidity encoders emit is accepted (`docs/abi-wire-format.md`). There is no lenient mode and no fallback.

## Ownership and lifetime

- **Input buffer.** A `ReadOnlySpan<byte>` does not freeze its backing array. Keep the bytes unchanged and unshared with concurrent writers for the lifetime of every `ABIData`/`ABICallData` derived from them. After a change the accessors still never throw or read outside the document (they re-check every pointer and bound and answer a status), but their results carry no guarantee, and validation is void: revalidate, or bind the approval to a digest of the exact bytes (`docs/abi-security.md`).
- **Ranges are root-relative.** Every `ABIRange` is an offset from the first byte of the arguments document at any depth; for `ABICallData.Arguments` that is call byte 4. An `ABIRange` is a number pair, not a capability.
- **Writer buffers.** Destination and scratch frames are leased to the writer exclusively and must not overlap each other; a source span passed to a write must not overlap any byte of the destination (`SourceOverlapsDestination`); in-place encoding is not supported.
- **ref structs.** All views and writers are `ref struct`: not storable in fields of classes, not capturable, not usable across `await`.

## Resource limits

```csharp
public readonly record struct ABIDecodeLimits(int MaxEncodedBytes, int MaxValueDepth, int MaxArrayElements, int MaxVisitedValues, int MaxWorkUnits);
public readonly record struct ABIEncodeLimits(int MaxEncodedBytes, int MaxValueDepth, int MaxArrayElements, int MaxWrittenValues, int MaxWorkUnits);
public readonly record struct ABICompileLimits(int MaxSchemaNodes, int MaxSchemaDepth, int MaxFixedArrayElements, int MaxTupleElements, int MaxStaticEncodedBytes);
```

| Limit | `Default` | Valid range | Exceeded |
| --- | ---: | --- | --- |
| decode/encode `MaxEncodedBytes` | 262,144 | `> 0` | `InputTooLarge` (a call counts its four selector bytes) |
| decode/encode `MaxValueDepth` | 16 | `1..32` (`HardMaxValueDepth`) | `DepthLimitExceeded` |
| decode/encode `MaxArrayElements` | 4,096 | `> 0` | `ElementLimitExceeded`; applies to dynamic-array counts only, not to fixed-array lengths (those come from the trusted schema) |
| decode `MaxVisitedValues` / encode `MaxWrittenValues` | 16,384 | `> 0` | `WorkLimitExceeded` (both: the visited-value budget of the decoder and the written-value budget of the writer, measurer and call encoder) |
| decode/encode `MaxWorkUnits` | 100,000 | `> 0` | `WorkLimitExceeded` |
| compile `MaxSchemaNodes` | 4,096 | `> 0` | `ElementLimitExceeded` |
| compile `MaxSchemaDepth` | 32 | `1..32` (`HardMaxSchemaDepth`) | `DepthLimitExceeded` |
| compile `MaxFixedArrayElements` | 4,096 | `> 0` | `ElementLimitExceeded` |
| compile `MaxTupleElements` | 1,024 | `> 0` | `ElementLimitExceeded` |
| compile `MaxStaticEncodedBytes` | 262,144 | `> 0` | `ElementLimitExceeded` |

`default(...)` of any limits struct is all zeros and invalid (`InvalidArgument`). Limits are read once into locals at the start of an operation, so another thread changing a shared limits variable cannot split validation from use. Limits are application policy, not part of the ABI format; start from `Default` and size them to your traffic. The decode defaults accept realistic large calls (4,000 `(address,uint256)` pairs; a `bytes` argument of 262,080 payload bytes, which as an argument document is exactly the 262,144-byte limit and as a call is 4 bytes over it, `InputTooLarge`; a 100-child multicall; Seaport `fulfillOrder`, which needs depth 6; all tested); larger batches need a larger `MaxEncodedBytes` and `MaxVisitedValues`. Zero-width values (empty tuples) consume no bytes, so `MaxVisitedValues` and `MaxWorkUnits` are their only bound; keep them proportionate to `MaxEncodedBytes`.

Work is deterministic and architecture independent: one unit per value entered, one per 32-byte word examined (scalar, offset, count, length, and one for a partially used last padded word). The encoder charges exactly like the decoder, so a document written under `ABIEncodeLimits` L decodes under the equal `ABIDecodeLimits`.

## Schemas

A schema describes the encoded arguments tuple of one function (or any tuple). Compile it once at startup; it is immutable and thread-safe.

```csharp
var compileLimits = ABICompileLimits.Default;

// From canonical function signature text (bootstrap convenience; aliases uint/int/fixed/ufixed are expanded).
if (!ABISchemaCompiler.TryCompileFunctionSignature("transfer(address,uint256)", in compileLimits,
        out ABIFunctionSchema? transfer, out ABISchemaError error))
{
    throw new InvalidOperationException(error.ToString());   // Status, Reason, Path ("$/1/[]/0"), TextPosition; never any transaction bytes
}
uint selector = transfer.Selector;                             // 0xa9059cbb, Keccak-256 of the canonical signature, never caller-supplied
FunctionSelector typedSelector = transfer.FunctionSelector;    // the same four bytes as this library's FunctionSelector value type
string signature = transfer.CanonicalSignature;                // "transfer(address,uint256)"
ABISchema arguments = transfer.InputSchema;                    // arguments.CanonicalType == "(address,uint256)"
```

| Member | Meaning |
| --- | --- |
| `ABISchemaCompiler.TryCompileArguments(ABITupleDefinition, in ABICompileLimits, out ABISchema?, out ABISchemaError)` | Compile an arguments tuple (spec 4.3). |
| `ABISchemaCompiler.TryCompileFunction(string name, ABITupleDefinition, in ABICompileLimits, out ABIFunctionSchema?, out ABISchemaError)` | Compile a function; the selector is computed from the canonical signature (spec 4.3). |
| `ABISchemaCompiler.TryCompileFunctionSignature(string, in ABICompileLimits, out ABIFunctionSchema?, out ABISchemaError)` | Convenience: parse and compile `name(type,...)` (added; the name differs from the spec overloads on purpose). |
| `ABITupleDefinition`, `ABITypeDefinition` | Typed definitions. `ABITupleDefinition` is a mutable builder (`Add`) that the compiler copies; `ABITypeDefinition` is immutable (factories `UInt(bits)`, `Int(bits)`, `Address`, `Bool`, `FixedBytes(n)`, `Function`, `Fixed(bits, scale)`, `UFixed(bits, scale)`, `Bytes`, `String`, `Tuple(...)`, `FixedArray(element, k)`, `DynamicArray(element)`). Both have `TryParse` for canonical text. |
| `ABISchema` | `CanonicalType`, `ArgumentCount`, `IsDynamic`. No public constructor and no public mutable table. |
| `ABIFunctionSchema` | `Name`, `CanonicalSignature`, `Selector` (`uint`), `FunctionSelector` (the same four bytes as a `FunctionSelector`), `InputSchema`. Sealed, no public constructor: a selector cannot disagree with its signature. The selector is derived by `FunctionSelector.FromSignature` (one Keccak implementation in the library). |
| `ABISchemaError` / `ABISchemaErrorReason` | Status category, precise reason (for example `Cycle`, `InvalidWidth`, `ZeroLengthFixedArray`, `StaticSizeLimit`, `MalformedSyntax`), component path, text position. |

Rules the compiler enforces (tests in `ABISchemaRejectionTests`, `ABITypeParserTests`, `ABISchemaImmutabilityTests`, `ABISelectorTests`):

- Integer widths are multiples of 8 from 8 to 256; `bytes<M>` has 1 to 32 bytes; fixed-point has a legal width and `N` from 1 to 80; `T[0]` is `UnsupportedSchema`; a negative or overflowing array length is `InvalidSchema` or `ArithmeticOverflow`.
- Cyclic definitions, schemas over the depth, node, arity, fixed-array or static-size limits, and size arithmetic overflow are rejected before anything is built. Mutating a source definition after compilation changes nothing.
- The text parser accepts only canonical text: no whitespace, no parameter names, no `tuple` keyword, no leading zeros. Whether text is a legal type width or length is decided by the compiler, not the parser.
- Function names are ASCII Solidity identifiers. Two functions may share a selector; resolving that collision is the registry's job (spec 4.2), never the codec's.

## Decoding

```csharp
var limits = ABIDecodeLimits.Default;
if (!ABICallData.TryDecode(calldata, transfer, in limits, out ABICallData call, out ABIStatus status))
{
    return Deny(status);                                       // selector mismatch, truncated, trailing data, bad padding, over a limit ...
}

if (!call.Arguments.TryGetAddress([0], out Address recipient, out status)) return Deny(status);     // typed: an EVM Address (the span overload gives the 20 bytes in place)
if (!call.Arguments.TryGetUInt256([1], out uint256 amount, out status)) return Deny(status);        // the whole 256-bit amount; see the warning below
```

**Read token amounts as `uint256`, not as `ulong`.** An ERC-20 amount is a `uint256`: `approve(spender, type(uint256).max)` is a routine call and 1e20 wei (100 tokens at 18 decimals) is above 2^64. `TryGetUInt64` is correct for fields that are really small (a `uint8`, a counter, a `uint32` deadline) and answers `ValueOutOfRange` for anything above 2^64-1; using it for an amount would deny every large transfer or approval. `TryGetUInt256` (typed, see below) or `TryGetUInt256Word` (the 32-byte word) read the whole value.

```csharp
// Span form, no value type involved: the exact canonical 32-byte word.
if (!call.Arguments.TryGetUInt256Word([1], out ReadOnlySpan<byte> amountWord, out status)) return Deny(status);
```

`ABICallData.TryDecode` checks, in this order (spec 5.2): invalid limits or null function (`InvalidArgument`), total size including the selector (`InputTooLarge`), at least four bytes and an equal selector (`InvalidSelector`), then the strict document validator over the bytes after the selector. The arguments are validated once. A bare selector is a valid call of a zero-argument function and `Truncated` for any function with arguments.

```csharp
public readonly ref struct ABIData
{
    public bool IsValid { get; }
    public ReadOnlySpan<byte> EncodedBytes { get; }
    public ABISchema? Schema { get; }            // null on a default view

    public static bool TryDecodeComplete(ReadOnlySpan<byte> encodedArguments, ABISchema schema, scoped in ABIDecodeLimits limits,
        out ABIData data, out ABIStatus status);
    public static bool TryDecodeComplete(..., out ABIData data, out ABIStatus status, out ABIError error);   // adds (Status, DocumentOffset, SchemaNode)

    public bool TryLocate(ReadOnlySpan<int> path, out ABIFieldLocation field, out ABIStatus status);
    public bool TryGetArrayLength(ReadOnlySpan<int> path, out int length, out ABIStatus status);
    public bool TryGetWord(...); TryGetUInt64(...); TryGetInt64(...); TryGetAddress(...); TryGetBool(...);   // TryGetWord: not fixed/ufixed, see the note below
    public bool TryGetBytePayload(ReadOnlySpan<int> path, out ReadOnlySpan<byte> payload, out ABIRange payloadRange, out ABIStatus status);
    public bool TryGetStringUtf8Validated(ReadOnlySpan<int> path, out ReadOnlySpan<byte> utf8, out ABIRange payloadRange, out ABIStatus status);
    public bool TryGetFixedBytes(...); TryGetFunction(path, out address20, out uint selector, out status);
    public bool TryGetSignedScaledWord(path, out word32, out byte scale, out status); TryGetUnsignedScaledWord(...);
    public bool TryGetUInt256Word(...); TryGetInt256Word(...);
    public static bool TryCompareUnsigned256(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, out int comparison, out ABIStatus status);
    public static bool TryCompareSigned256(...);
}

public readonly ref struct ABICallData
{
    public bool IsValid { get; }
    public uint Selector { get; }
    public FunctionSelector FunctionSelector { get; }   // the same four bytes as the library value type
    public ReadOnlySpan<byte> EncodedCall { get; }
    public ABIData Arguments { get; }            // coordinate zero is call byte 4
    public static bool TryDecode(ReadOnlySpan<byte> callBytes, ABIFunctionSchema function, scoped in ABIDecodeLimits limits,
        out ABICallData call, out ABIStatus status);          // and an overload with out ABIError
    public bool TryTranslateArgumentRange(ABIRange argumentRange, out ABIRange callRange);   // translation only
}
```

`limits` is `scoped in` (the specification shows `in`): a compiler requirement so the returned ref struct is not tied to the lifetime of the caller's limits variable. Behaviour is the specified one. `TryCompareSigned256` and `TryGetStringUtf8Validated` are additions (the specification allows the unsigned comparison and asks for a separate strict text step).

### Paths and the locator

A path is a sequence of zero-based indices: at a tuple it selects a member, at a fixed or dynamic array an element. The empty path is the root tuple. Array counts are not children; read them with `TryGetArrayLength`. A negative or out-of-range index, or a step through a primitive, `bytes` or `string`, is `InvalidPath`; a path error always wins over a type mismatch.

`TryLocate` returns `ABIFieldLocation(HeadRange, ValueRange, Kind)` in root coordinates in O(path depth): no sibling or descendant is scanned. The empty path gives head `(0, RootHeadSize)` and value `(0, EncodedBytes.Length)`. For a static field both ranges are its inline encoding (a zero-width field has a zero-length range at its head position). For a dynamic field the head range is its 32-byte offset word and the value range is its whole tail `[length or count word][data][padding]`, which ends at the start of the next dynamic sibling's tail (precomputed per tuple position, or the next array slot) or at the enclosing container's end. A `bytes` or `string` payload range excludes the length word and the padding; no synthetic child view is ever created for it.

### Typed accessors

| Accessor | Serves | Fails with |
| --- | --- | --- |
| `TryGetWord` | the six scale-free 32-byte elementary kinds: `uint<M>`, `int<M>`, `address`, `bool`, `bytes<M>`, `function` (not `fixed`/`ufixed`, `bytes`, `string`, composites) | `TypeMismatch` |
| `TryGetUInt64` / `TryGetInt64` | `uint<M>` / `int<M>` of any width; never fixed-point | `TypeMismatch`; `ValueOutOfRange` for a canonical value that does not fit (never truncated, never signed as unsigned) |
| `TryGetUInt256Word` / `TryGetInt256Word` | the canonical word of `uint<M>` / `int<M>` | `TypeMismatch` |
| `TryGetAddress` | `address`: exactly the 20 low-order bytes | `TypeMismatch` |
| `TryGetBool` | `bool` | `TypeMismatch` |
| `TryGetFixedBytes` | `bytes<M>`: the M data bytes without padding | `TypeMismatch` |
| `TryGetFunction` | `function`: the 20 address bytes and a big-endian selector; implies no executable call | `TypeMismatch` |
| `TryGetSignedScaledWord` / `TryGetUnsignedScaledWord` | `fixed<M>x<N>` / `ufixed<M>x<N>`: the canonical scaled word plus `N`; no rounding, no floating point | `TypeMismatch` |
| `TryGetBytePayload` | `bytes` and `string`: raw unpadded payload and its root-relative range | `TypeMismatch` |
| `TryGetStringUtf8Validated` | `string` only: the same payload after strict UTF-8 validation | `TypeMismatch` (for `bytes`), `InvalidUtf8` |
| `TryGetArrayLength` | fixed and dynamic arrays | `TypeMismatch` (tuple arity is schema knowledge) |

**Behavioural note: `TryGetWord` and fixed-point.** The specification contradicts itself here: section 5.1 says `TryGetWord` serves "32-byte elementary ABI types (including the encoded fixed-point word)", and the same section says scale-bearing values "MUST never be returned without scale metadata". The second rule is a security rule (an unscaled word read as an integer misreads an amount by a factor of 10^N) and wins: `TryGetWord` answers `TypeMismatch` (and an empty span) for `fixed` and `ufixed` fields, which are read with `TryGetSignedScaledWord` / `TryGetUnsignedScaledWord`, always together with their scale (`docs/abi-design-notes.md`, row 64).

Strictness of the stored words (high bits, sign extension, padding) is enforced at decode time, so accessors never reinterpret a value as another type. `TryCompareUnsigned256` compares 32-byte words big-endian lexicographically; `TryCompareSigned256` compares signs first. Both return `InvalidArgument` and `0` for a span that is not 32 bytes.

## Writing

`ABIWriter`, `ABIMeasurer` and `ABICallEncoder` share one engine and one event grammar. The root is always the arguments tuple (opened by `TryBegin`, closed by `TryFinish`); a bare scalar cannot be the root.

```csharp
Span<byte> buffer = stackalloc byte[68];
Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[2];     // one per open container including the root; TryBegin checks it (see the scratch rule below)
var encodeLimits = ABIEncodeLimits.Default;
if (!ABICallEncoder.TryBegin(transfer, buffer, frames, in encodeLimits, out ABICallEncoder encoder, out ABIStatus status)) return Fail(status);

bool ok = encoder.TryWriteAddress(recipient, out status) && encoder.TryWriteUInt256(amount, out status);   // Address and uint256: typed adapters, see below
if (!ok || !encoder.TryFinish(out int written, out status)) return Fail(status);   // after any failure the buffer holds no valid call: discard it
ReadOnlySpan<byte> call = buffer[..written];                                       // selector + arguments, 68 bytes
```

| Event | Methods |
| --- | --- |
| Scalars | `TryWriteUInt64`, `TryWriteInt64` (small fields only: not for token amounts), `TryWriteUInt256Word(word32)`, `TryWriteInt256Word(word32)`, `TryWriteAddress(address20)`, `TryWriteBool`, `TryWriteFixedBytes(exactly M bytes)`, `TryWriteFunction(address20, selector)`, `TryWriteSignedScaledWord(word32)`, `TryWriteUnsignedScaledWord(word32)` |
| Dynamic data | `TryWriteBytes` (a `bytes` slot with any content, or a `string` slot with strictly valid UTF-8), `TryWriteStringUtf8` (a `string` slot only); every `string` write is UTF-8 checked before any byte is written (`InvalidUtf8`) |
| Containers | `TryBeginTuple`, `TryBeginFixedArray`, `TryBeginDynamicArray(count)`, `TryEndContainer` |
| Finish | `TryFinish(out bytesWritten)` (`ABIMeasurer`: `out encodedLength`; `ABICallEncoder`: `out callBytesWritten`, which is `4 + argument length`) |

Rules (tests in `ABIWriter*Tests`, `ABICallEncoderTests`):

- Values arrive in schema declaration order; the writer reserves each container's head when it opens and backfills offsets, so the byte layout is head-then-tail whatever the call order. Every byte of `[0, bytesWritten)` is written (padding included), so garbage already in the buffer never survives; bytes beyond `bytesWritten` are never touched; a zero-argument root writes zero bytes.
- Values are checked against the declared width (`ValueOutOfRange`, never truncated), the slot kind (`TypeMismatch`) and the source length (`InvalidArgument`: `bytes<M>` needs exactly M bytes, an address or function address exactly 20, a word exactly 32) before any byte is written.
- State machine: the first failing operation (any reason, including an early `TryFinish`) faults the writer for good; every later call answers `InvalidWriterState`, and a completed writer is dead too. More values than a container's arity or announced count is `ElementLimitExceeded`; fewer at `TryEndContainer` or `TryFinish` is `IncompleteWrite`. A failed operation writes no destination byte, but it does fault the writer and may already have charged the value and work budgets; earlier successful operations stay in the buffer, so after any failure the buffer holds no valid document: publish `destination[..bytesWritten]` only after `TryFinish` succeeds.
- Scratch rule: `TryBegin` returns `InsufficientScratch` when `scratch.Length < min(MaxValueDepth, deepest container path of the schema)`. The deepest container path counts the root tuple as 1 and each nested tuple, fixed array or dynamic array as one more (primitives do not count): `(uint256)` needs 1 frame, `(uint256[][])` 3, `(((uint256)))` 3, `g(uint256[][][],string[][])` 4. It is a worst case over the schema (an empty array never opens its element container), so a scratch that passes can never run short mid-write; `ABIEncodeLimits.HardMaxValueDepth` (32) frames always pass. Each frame is 52 bytes.
- Scratch frames are caller-writable memory and are treated as untrusted: corrupted or forged frames (random bytes, a byte copy of another writer's frames) get `InvalidWriterState`, never an exception or a write outside the destination, and `bytesWritten` is always a real position. Garbage that merely looks like a plausible frame can make the document wrong but not unsafe; do not touch the scratch while a writer is in use.
- All mutable state lives in the scratch frames, so a struct copy of a writer shares it with the original (it cannot exceed a budget or hide a fault), and a writer whose scratch was reused by a later `TryBegin` is dead. Still pass the writer by `ref` and do not copy it.
- `ABIMeasurer.TryBegin(schema, Span<ABIMeasureFrame>, in limits, ...)` runs the same state machine without a destination; use it, then `ABIWriter`, when the size is not known up front. The values must not change between the passes (the writer re-validates every value, so a change can only make the write fail or produce a different length).
- `ABICallEncoder` takes an `ABIFunctionSchema`; its `MaxEncodedBytes` covers the whole call, so the arguments get `MaxEncodedBytes - 4` (`InputTooLarge` below 4); the selector is written only after every `TryBegin` check passed, and a later document failure voids the whole buffer (`TryFinish` reports 0).
- Not offered: a length-only `bytes` measuring overload (UTF-8 validity and length both need the source). Typed convenience members over `uint256`, `int256`, `Address`, `Hash32` and `FunctionSelector` are the extension methods of `ABITypedExtensions` (next section).
- Every `ReadOnlySpan<byte>` parameter of the `TryWrite*` members is `scoped`, so a `stackalloc` or local span can be passed (without it the compiler rejects the call: the writer is a `ref struct` that could capture the argument).
- `TryBegin` never throws, whatever the scratch span claims: only the first `min(scratch.Length, MaxValueDepth)` frames can ever be used, so a longer scratch is cut to that before any byte view of it is made, and the scratch/destination overlap check covers exactly those frames (`docs/abi-design-notes.md`, row 65).

## Typed adapters

`ABITypedExtensions` (static class, extension methods; the counterpart of `RLPTypedExtensions`) connects the codec to the library's value types. Each member delegates to the existing span accessor or writer and only converts the validated word, so every status, precedence rule, budget and failure-atomicity rule is that of the span member; none allocates or throws.

| On `ABIData` | Serves | Notes |
| --- | --- | --- |
| `TryGetUInt256(path, out uint256, out status)` | `uint<M>`, any width | `TypeMismatch` for `ufixed` and everything else |
| `TryGetInt256(path, out int256, out status)` | `int<M>`, any width | sign-extended |
| `TryGetAddress(path, out Address, out status)` | `address` | an EVM `Address`; `default(Address)` is a VALID zero address, so test the returned bool |
| `TryGetBytes32(path, out Hash32, out status)` | `bytes32` | any other `bytes<M>` is `TypeMismatch` |
| `TryGetBytes4(path, out FunctionSelector, out status)` | `bytes4` | other widths `TypeMismatch`; a `bytes4` is not necessarily a function selector (an ERC-165 interface id is also a `bytes4`) |
| `TryGetFunction(path, out Address, out FunctionSelector, out status)` | `function` | a pointer value, not an executable call |

| On `ABIWriter`, `ABICallEncoder`, `ABIMeasurer` | Delegates to |
| --- | --- |
| `TryWriteUInt256(uint256, out status)` | `TryWriteUInt256Word`: a value that does not fit a narrow slot is `ValueOutOfRange` |
| `TryWriteInt256(int256, out status)` | `TryWriteInt256Word` |
| `TryWriteAddress(Address, out status)` | `TryWriteAddress(span)`: a non-EVM (Solana) address is `InvalidArgument`, exactly like a wrong-length span, after the slot, kind and budget checks |
| `TryWriteBytes32(Hash32, out status)` / `TryWriteBytes4(FunctionSelector, out status)` | `TryWriteFixedBytes`: the slot must be a `bytes32` / `bytes4` (another width is `InvalidArgument`, as for a span of the wrong length) |
| `TryWriteFunction(Address, FunctionSelector, out status)` | `TryWriteFunction(span, uint)` |

They are extension methods on purpose: an instance overload `TryGetAddress(path, out Address, ...)` would change what `out var` means at existing call sites, and new public instance members would change the pinned event grammar of the writer types. Instance members win overload resolution, so `data.TryGetAddress(path, out var a, out var s)` still binds the span member (a `ReadOnlySpan<byte>`); name the out type (`out Address a`) to select the typed one. Typed reads convert the already validated word and do not re-check the canonical high bytes (the decoder did, for the unchanged document).

**Call-site conversions run before the codec.** The implicit `int` and `long` to `uint256` conversions throw `OverflowException` for a negative number, and `uint256` `+`, `-` and `*` throw on overflow. `w.TryWriteUInt256(-1, out s)` or `w.TryWriteUInt256(a + b, out s)` therefore throws in the caller's own expression before the writer is entered: no status reports it and the writer is not faulted. Build values with the non-throwing constructors where operands are untrusted (`ABITypedExtensionsTests.ImplicitNegativeConversions...` pins this).

## EVM adapters

`RLPCalldataAdapter`, `ContractFunctionResolver` (built by `ContractFunctionResolverBuilder`), `ExecutionDescriptor` with `NestedCallRule`, `EvmAuthorizationProcessor` with `EvmAuthorization`, `EvmAuthorizationOutcome` and `IEvmAuthorizationHost` (EIP-7702), `EvmAccountCode`, and `NestedCallWalker` with `INestedCallHost`, `NestedCall`, `NestedCallLimits` and `NestedCallWalkResult` live in namespace `EccentricWare.Web3.DataTypes.ABI.EvmAdapters`. Their contracts, statuses and an end-to-end example are in `docs/abi-integration.md`. Request-path members (`TryRead`, `TryProcess`, `TryResolve`, `TryWalk`, `TryMapCalldataRange`) never throw on malformed input and allocate nothing; bootstrap members (`ContractFunctionResolverBuilder.Add`, the `NestedCallWalker` constructor, `ExecutionDescriptor.NoNestedCalls` and `Undescribed`) throw `ArgumentException` or `ArgumentNullException` on programming errors, and `ExecutionDescriptor.TryCreateNested` reports rule errors as `ExecutionDescriptorError` with the rule's position. Exceptions thrown by a host propagate out of `TryWalk`.

## Status reference

| Status | Where it comes from |
| --- | --- |
| `Success` | the operation completed |
| `InvalidArgument` | null schema or function, invalid limits, default view, wrong source length, negative array count, overlapping scratch |
| `InvalidSchema`, `UnsupportedSchema` | malformed definition or text; `T[0]` |
| `InputTooLarge` | document or call over `MaxEncodedBytes` |
| `InvalidSelector` | call shorter than four bytes or selector differs from the function's |
| `InvalidPath` | negative or out-of-range index, step through a primitive |
| `TypeMismatch` | accessor or writer method does not serve the field or slot kind |
| `Truncated` | a word, length, count or padding byte is not contained in the input |
| `InvalidOffset` | pointer not 32-byte aligned, wider than 31 bits, beyond the document, or inside its own head |
| `NonCanonicalLayout` | plausible pointer that is not the next canonical tail (gap, overlap, alias, reversal); trailing data |
| `InvalidPadding` | a padding byte that must be zero is not |
| `InvalidScalar` | bad sign extension, `bool` other than 0 or 1, address or function high bytes, value above the declared width |
| `ValueOutOfRange` | a canonical value or a writer input does not fit the requested width |
| `InvalidUtf8` | strict text validation failed |
| `SourceOverlapsDestination` | writer source overlaps the destination |
| `ArithmeticOverflow` | schema size or array-length arithmetic overflowed (never returned by the encoder) |
| `DepthLimitExceeded`, `ElementLimitExceeded`, `WorkLimitExceeded` | a limit above; a dynamic-array count that does not fit 31 bits is `ElementLimitExceeded` |
| `InsufficientDestination`, `InsufficientScratch` | caller buffers too small |
| `IncompleteWrite`, `InvalidWriterState` | finished or closed too early; any operation on a default, faulted or completed writer |

Decode statuses obey a fixed precedence: invalid limits or null, size, selector (calls), then the first structural error in depth-first declaration order, with a limit reported where it is reached. `ABIError(Status, DocumentOffset, SchemaNode)` (the diagnostic overloads) gives the offset of the failing word, region or padding byte (`-1` for argument, size and selector errors; call offsets are argument-relative) and never carries payload bytes.

## Deviations and choices where the specification is open or differs

Namespace `EccentricWare.Web3.DataTypes.ABI` instead of `Evm.Abi`, folder `ABI/`. `limits` parameters are `scoped in`. Compile outputs are nullable `[NotNullWhen(true)]`. `TryCompileFunctionSignature`, the `ABIError` overloads, `TryCompareSigned256`, `TryGetStringUtf8Validated`, the `FunctionSelector` properties of `ABIFunctionSchema` and `ABICallData`, and the `ABITypedExtensions` adapters are additions. `TryGetWord` refuses `fixed`/`ufixed` (the specification contradicts itself; the scale rule wins, row 64). The `TryWrite*` source-span parameters are `scoped` (row 70). `MaxArrayElements` bounds dynamic-array counts only. Statuses for conditions the specification leaves open (pointer kinds, count and length overflow, writer misuse) are documented per row in `docs/abi-design-notes.md` (rows 5, 18, 19, 41, 64, 65). The spec 12 EVM adapters are library types in the core assembly (`docs/abi-integration.md`). Every `string` slot write is UTF-8 validated, `TryWriteBytes` included, so spec 7.7 invariant 5 holds only for documents whose strings are valid UTF-8 (row 80).
