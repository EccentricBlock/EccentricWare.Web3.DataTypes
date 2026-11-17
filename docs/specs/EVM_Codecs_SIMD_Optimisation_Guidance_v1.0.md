# SIMD and CPU/Memory Optimisation Guidance for EVM Binary Codecs

**Document:** Engineering guidance and implementation hand-off  
**Version:** 1.0  
**Date:** 17 November 2026  
**Target:** .NET 10 LTS, C# 14, x64 and ARM64, Windows and Linux  
**Status:** Prescriptive guidance and verification plan; no SIMD implementation or benchmark results are claimed

## 1. Purpose, companion documents and authority

This document guides a coding agent adding **measured CPU and memory optimisations** to an EVM binary-codec stack that processes untrusted transactions for signing policy decisions. It is intentionally a **supplement**, not a rewrite, of:

1. **`RLPData_DotNet_Design_Specification_v2.0.md`** — namespace `Evm.Rlp`, principal type `RLPData`, canonical RLP structure, validation, range indexing, encoding and security contracts.
2. **`ABIData_ABICallData_DotNet_Design_Specification_v2.1.md`** — namespace `Evm.Abi`, principal types `ABIData` and `ABICallData`, compiled ABI schemas, strict validation, range location, encoding and nested calldata boundaries.

Both specifications must be supplied to the implementing agent and read **in full** before modifying code. Their canonical wire-format rules, trust boundaries, public contracts, resource limits, error semantics, and tests are **normative**. This guidance specifies optimisation candidates, acceptance gates and integration constraints. Where an implementation suggestion here conflicts with either codec specification, **preserve the codec's normative security/correctness contract**, record the conflict, and do not silently change the public API or validation semantics.

**Success:** The codec remains correct, fully validated and allocation-free on its required hot paths; any SIMD implementation is internal, demonstrably faster on a representative target workload, portable through a tested fallback, and causes no unacceptable latency, memory, correctness or security regression. **Not adding SIMD is an acceptable and potentially optimal outcome.**

### 1.1 Explicit scope

- Inspect SIMD/CPU opportunities in hexadecimal ingress, strict RLP validation/indexing/encoding, standard ABI validation/access/encoding, function-selector handling and nested-call inspection.
- Prefer built-in .NET operations and a scalar reference implementation before manual vectorisation.
- Benchmark **whole signing-policy preparation**, not only a byte-comparison microbenchmark.
- Maintain identical observable results, status codes, offsets, canonicality checks, output bytes and policy decisions across all supported execution paths.
- Support low-latency mixed workloads, including numerous short transactions and selected large multicalls. No speculative large buffers or extra input copies in the common path.

### 1.2 Out of scope

This document does not implement an EVM execution simulator, discover trusted contract ABI definitions, authorise nested calls from selector-only matches, implement cryptography, change transaction-signing rules, provide an RPC client, or redefine either existing codec's wire grammar. It does not advocate vectorising cryptographic secret handling. No undocumented `unsafe` loads, native code or hardware-specific public types are required.

### 1.3 Terminology and evidence labels

**MUST** = release requirement. **SHOULD** = default engineering choice unless justified by evidence. **MAY** = optional experiment. **Baseline** = an unoptimised-but-correct implementation tested against the codec specifications. **Hot path** = repeated transaction decode/lookup/encode calls after one-time schema compilation and runtime warm-up. **Candidate** = unproven optimisation until compilation, correctness and benchmark gates pass. A microbenchmark result is **not** an end-to-end performance result.

## 2. Guiding architecture

```text
Incoming hex string / binary request
    ├── enforce ingress byte/character limits
    ├── decode hex once, only if needed (application boundary)
    └── establish stable, exclusive input ownership for signing evaluation
                  │
                  ▼
     RLPData strict structural validation
     (scalar variable-length prefixes; no copied RLP tree)
                  │
                  ▼
     validated transaction field range / calldata slice
                  │
                  ▼
     ABIData / ABICallData strict schema-bound validation
     (scalar head/tail navigation; no decoded object tree)
                  │
                  ▼
     trusted nested execution semantics + selected field ranges
                  │
                  ▼
     INTERNAL byte operations / policy comparisons
       ├── .NET built-in span operations / scalar baseline
       ├── optional vectorised contiguous-batch operations
       └── portable fallback
                  │
                  ▼
     policy result, bound to exact signing bytes/digest
```

Structural parsing is **not** a pattern-search problem. RLP is self-delimiting but has variable-length item prefixes; standard ABI is schema-driven with context-relative offsets; executable nested-call semantics come from trusted contract metadata, not arbitrary bytes matching a selector. **The scanner must never infer child calls by `IndexOf` over an opaque `bytes` payload.**

Avoid introducing a broad public `BinaryOperations` framework. Shared internal helpers may live in a small `Evm.Binary` project if both packages already share infrastructure or measurements justify the dependency. Otherwise keep equivalent small helpers internal to their respective packages. Do not create extra assemblies, copy buffers or virtual dispatch merely to claim a shared SIMD layer.

## 3. Ranked opportunities — what to optimise and what not to optimise

The table ranks expected *engineering value*, not measured speedups.

| Operation | Typical size / data shape | First implementation | SIMD decision |
|---|---|---|---|
| 4-byte function selector | Exactly 4 contiguous bytes | `BinaryPrimitives.ReadUInt32BigEndian` | **Do not SIMD** an isolated selector |
| ABI 32-byte word equality | Exactly 32 contiguous bytes | `ReadOnlySpan<byte>.SequenceEqual` | Benchmark explicit 256-bit vector against built-in; no presumption of benefit |
| 20-byte address equality | Exactly 20 contiguous bytes | `SequenceEqual` | Usually avoid hand-written SIMD or overreading to 32 bytes |
| ABI uint256 ordering | 32 bytes, unsigned big-endian | Four big-endian 64-bit limb comparisons or equivalent | Avoid SIMD equality as substitute for numeric ordering |
| ABI sign-extended/widened integer validation | One 32-byte word | Width-aware scalar/high-byte check | SIMD not expected to help common cases |
| ABI static/dynamic padding | At most 31 padding bytes per encoded dynamic byte/string tail | Bounded zero comparison (`IndexOfAnyExcept`, `IndexOf`, or loop as actually supported by target runtime) | Large-buffer SIMD scanner **not warranted** |
| RLP item-header scan | 1–9-byte prefix; dependent sibling lengths | Scalar canonical header parser | **Do not replace** with arbitrary vectorised byte search |
| RLP child index construction | Small number of varying-width children | Existing validated two-pass index | Benchmark fewer scans/targeted lookup before SIMD |
| ABI head/tail offsets | 32-byte words representing uint256 offsets | Scalar checked word read + bounds arithmetic | **Do not SIMD** control-flow navigation |
| Repeated selector filtering | Many selectors, potentially non-contiguous | Direct scalar hash/switch/set lookup while walking validated children | MAY batch if already contiguous or collected once and reused |
| Large *contiguous* homogeneous fixed-width arrays | Hundreds/thousands of adjacent values | Scalar/`Span<T>` baseline | **Best credible SIMD candidate**, if schema guarantees layout and limits |
| Hex text conversion | Potentially large contiguous characters | `Convert.FromHexString` into caller-owned buffer | Benchmark built-in before any custom routine; remains outside RLP/ABI library |
| Large ABI dynamic bytes payload | Opaque binary payload | Slice/copy only if policy requires it | Vectorise only a proven **payload-specific** comparison/search, not structural validation |
| RLP/ABI encoding | Small headers, fixed words and necessary output bytes | Pre-size, write directly, zero-fill bounded padding | MAY accelerate a demonstrably large contiguous copy/fill; built-ins first |

The prior idea of vectorising **ABI padding checks** as a bulk workload is misleading: standard ABI bytes/string padding adds between **zero and 31 bytes** to a value. Large byte payloads are not padding, and the generic validator is not authorised to scan their content to infer semantics. RLP has **no ABI-style padding**.

### 3.1 Performance upper bound (Amdahl's law)

Let the end-to-end request time be

`Trequest = Thex + Trlp + Tabi + Tsemantic + Tpolicy + TsignerBoundary`.

If fraction `f` of the baseline time is spent in an operation improved by factor `s`, the maximum end-to-end speedup is

`S = 1 / ((1 - f) + f / s)`.

Example: a 2× word-comparison speedup that affects only 5% of request CPU time yields at most approximately **2.56%** end-to-end speedup (`1 / (0.95 + 0.05/2)`). This is an illustrative calculation, **not a measurement**. If comparison accounts for 0.5% of total time, substantial engineering effort is unlikely to pay off. Profile before implementing.

## 4. .NET 10 SIMD implementation policy

1. **Prefer existing APIs**: `ReadOnlySpan<T>.SequenceEqual`, `IndexOf`, `Contains`, `CopyTo`, `Span<T>.Clear`, `BinaryPrimitives`, `Convert.FromHexString`. Their JIT/runtime implementations may already be vectorised and often beat custom intrinsics.
2. **Scalar reference path is mandatory**: implement and test it independently; its behaviour defines semantic equivalence (not untrusted SIMD output).
3. **Portable vector APIs first**: `System.Runtime.Intrinsics.Vector128<T>` is a reasonable first SIMD experiment for contiguous arrays; wider `Vector256<T>`/`Vector512<T>` only after measurements. These are fixed-width APIs, not proof that every operation is hardware-accelerated.
4. **Hardware gating**: check the exact supported width/operation with `IsHardwareAccelerated` and supported element types; where platform-specific intrinsics are used, gate the exact instruction set (e.g. `Avx2.IsSupported` or `AdvSimd.IsSupported`). JIT constants typically eliminate inactive branches. Do not cache feature flags on every transaction.
5. **No speculative memory reads**: a 20-byte address is 20 bytes, not 32 bytes that happen to be readable in a particular buffer. Validate length **before** any vector load; never read beyond a slice, allocation, page or enclosing validated field.
6. **No unsafe optimisation as the initial implementation**: do not introduce pointers, `Unsafe.ReadUnaligned`, casts with unchecked alignment, or native buffers unless benchmarks justify them and dedicated memory-safety tests are in place.
7. **No change to public API**: optional internal scalar/SIMD methods may coexist; no `RLPDataSimd`, `ABIDataVectorised`, public CPU-feature knobs or platform-specific policy outputs.
8. **No per-transaction dispatch object or allocation**: JIT-folded feature checks or internally selected static fast paths are preferable to request-scoped strategy objects, delegates and interfaces.
9. **Profile CPU, memory and whole-operation latency**: a wider vector does not guarantee speed on short or branchy inputs; AVX-512 may create power/frequency trade-offs on some processors, so measure sustained load.
10. **Preserve deterministic results**: exact same success/failure, error status, reported offsets/ranges, decoded values and encoding bytes regardless of SIMD availability.

Do not make the public codec accept an arbitrary SIMD preference merely to simplify tests. Instead, expose **internal test-only entry points** through `InternalsVisibleTo` or equivalent and run distinct test processes with documented JIT settings.

## 5. Optimisation contracts by component

### 5.1 `RLPData` (`Evm.Rlp`)

**Preserve** canonical strict root validation, variable-length header semantics, iterative bounded nesting, document-relative `RLPRange`, validated child-index provenance, two-pass all-or-nothing index construction, byte ownership and writer finalisation contracts from the RLP v2.0 specification.

- The canonical header parser remains a **single scalar source of truth**. Do not substitute a byte-classification SIMD scan that treats payload bytes as item headers.
- Valid RLP strings contain arbitrary bytes. Decoding must skip payload content based on validated header length, not scan it for delimiters or zero bytes.
- Direct-child enumeration may process only validated child headers. Its complexity remains O(children) unless a pre-existing valid index is used.
- An `RLPChildrenIndex` is a metadata cache for one validated list, not an independent proof of structure and not safe to reuse after input or workspace mutation.
- Fast integer decoding (up to 64 bits) uses checked scalar big-endian operations. Larger integer-word comparisons are higher-layer concerns and require the correct signed/unsigned semantics.
- Encoding of short payloads, length prefixes and integer magnitudes is scalar. For long payloads, benchmark `ReadOnlySpan<byte>.CopyTo` before any custom copy loops; do not use non-temporal stores without a demonstrated large-output advantage and correct ordering.
- Fixed 55/56 boundary and prefix cases are security requirements; SIMD must not change canonicality or rejection offsets.

**Admissible experiments**: vectorised equality of already identified long RLP byte-string payloads when a real policy uses repeated content equality, or a large-volume internal copy operation shown to be a bottleneck. These are not part of generic RLP structure parsing.

### 5.2 `ABIData` (`Evm.Abi`)

**Preserve** immutable precompiled schemas, strict canonical head/tail ordering, word alignment, checked uint256 offset conversion, typed accessors, bounded logical-value traversal (including zero-width tuples), root-relative `ABIRange` values and whole-document validation from ABI v2.1.

- 32-byte words are tiny: begin with built-in span equality and measured scalar arithmetic. A single `Vector256<byte>` load/compare is a **candidate**, not a guaranteed optimisation.
- ABI unsigned integer ordering follows **big-endian numeric order**. Lexicographic byte comparison has that order for equal-width canonical unsigned words, but SIMD `EqualsAll` is only an equality test. Signed integers must be handled separately (compare sign before magnitude or use a proven signed-word comparator).
- For `uint<M>` and `int<M>`, respect width and sign extension; for `address`, compare the correct 20 low-order bytes (after validation of 12 leading zero bytes); for `bytes<M>`, compare only the left-aligned M bytes while validating its right-side padding.
- ABI offset/count/length words must be checked as full 256-bit unsigned values before narrowing to `int`; no SIMD lane truncation or reinterpretation can replace this check.
- The `bytes`/`string` tail padding length is `(32 - (length mod 32)) mod 32`, bounded **0..31**. Do not scan full values for padding. For fixed `bytes<M>`, **in-word right padding** is `32-M`, also **0..31** (it is not a separate dynamic tail).
- Schema-driven static arrays may permit direct range access, but heterogeneous/static-tuple layouts and dynamic-array heads do not become a uniform 32-byte stream merely because they are ABI encoded.
- A zero-width tuple/array element can contribute **logical validation work without byte progress**. Vector loops must not assume every element moves a cursor; retain independent element/step/depth budgets.
- For large homogeneous static arrays of 32-byte values, SIMD **may** accelerate a policy-specific operation over contiguous value words, but a schema-aware loop is still needed for semantic validation, and data must remain stable.

### 5.3 `ABICallData` (`Evm.Abi`)

- Extract the selector using `BinaryPrimitives.ReadUInt32BigEndian(call[..4])` after a length check. Do not vectorise four bytes.
- A selector is a candidate function identity only. Trusted `(chain, target, verified code/implementation identity, selector)` resolution is outside the codec and **must precede policy authorisation**.
- Do not reinterpret arbitrary `bytes` data as a nested call. A trusted contract-specific execution descriptor determines which fields contain executable subcalls and how the child target/value/call context is established.
- To traverse a `bytes[]` multicall: strictly validate its structural encoding, locate each child by schema/range, then check its selector and semantically relevant fields. No byte-pattern sweep, hidden call-tree materialisation or unknown-selector automatic approval.
- If a batch of child selectors is required, the **direct per-child scalar lookup** is the default. Batch SIMD is permitted only where the selector collection is already available, or a benchmark shows that collection/reuse costs are outweighed.
- Vectorised filtering **must not** skip required child validation or misclassify a forbidden call because of collisions, stale code identity, delegatecall semantics or dynamically generated internal calls.

### 5.4 Hex ingress and encoding output

- Decode hexadecimal **once at ingress** and **outside** the RLP/ABI codec. Enforce a maximum character count and even-length requirement after handling an optional `0x` prefix, then convert into caller-owned stable storage.
- For the .NET 10 span overload, `Convert.FromHexString(ReadOnlySpan<char>, Span<byte>, out int charsConsumed, out int bytesWritten)` returns `OperationStatus`. **Only** a complete `Done` result with expected consumption and length can be accepted; some failure cases may leave partially written destination contents. Do not expose those bytes as a valid transaction.
- Treat benchmarking a custom hex SIMD converter as an independent work item. It must equal the built-in conversion on uppercase/lowercase digits, odd input, invalid symbols, truncation, boundary sizes and failure handling; never bypass ingress limits.
- Output writers may use built-in span clear/copy; preserve atomicity and finalisation contracts. SIMD must not produce partially authorised output or overwrite bytes outside the caller-declared destination range.

## 6. Small-value operations — reference implementation contracts

The following examples are **reference baselines and API sketches**, not a claim that code has been compiled in this deliverable. Implementers must compile against the pinned .NET 10 SDK and run the specified tests.

```csharp
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

internal static class BinaryValueOperations
{
    // Fast-path baseline for already-validated 32-byte ABI words.
    internal static bool WordEquals(
        ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        => left.Length == 32 && right.Length == 32 && left.SequenceEqual(right);

    // Do not read 32 bytes of a 20-byte address.
    internal static bool AddressEquals(
        ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        => left.Length == 20 && right.Length == 20 && left.SequenceEqual(right);

    // Function selectors are four-byte unsigned big-endian values.
    internal static bool TryReadSelector(
        ReadOnlySpan<byte> callData, out uint selector)
    {
        selector = 0;
        if (callData.Length < 4) return false;
        selector = BinaryPrimitives.ReadUInt32BigEndian(callData);
        return true;
    }

    // Use for *secret-dependent* comparisons, never assume ordinary
    // SequenceEqual or SIMD equality has fixed-time behaviour.
    internal static bool SecretEquals(
        ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        => CryptographicOperations.FixedTimeEquals(left, right);

    // Big-endian unsigned numeric comparison; no BigInteger allocation.
    // Returns -1, 0 or +1. Caller provides exactly 32-byte values.
    internal static int CompareUnsigned256(
        ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length != 32 || right.Length != 32)
            throw new ArgumentException("Expected two 32-byte words.");

        for (int offset = 0; offset < 32; offset += 8)
        {
            ulong a = BinaryPrimitives.ReadUInt64BigEndian(left.Slice(offset, 8));
            ulong b = BinaryPrimitives.ReadUInt64BigEndian(right.Slice(offset, 8));
            if (a < b) return -1;
            if (a > b) return +1;
        }
        return 0;
    }
}
```

For the public codecs' `Try*` methods, adapt exceptions to the **existing status-code contract** instead of throwing on untrusted wrong-length inputs. The comparator above is an internal helper with a strict precondition, not a public decoder. Asymmetric return paths are fine for public transaction values but **not** for comparing secret material.

### 6.1 Optional 32-byte SIMD benchmark candidate

If `WordEquals` is measurably hot, compare the built-in baseline to the following internal **candidate** after compiling it for the target framework:

```csharp
using System;
using System.Runtime.Intrinsics;

internal static bool WordEqualsVector256(
    ReadOnlySpan<byte> left,
    ReadOnlySpan<byte> right)
{
    if (left.Length != 32 || right.Length != 32)
        return false;

    if (!Vector256.IsHardwareAccelerated)
        return left.SequenceEqual(right);

    return Vector256.EqualsAll(
        Vector256.Create<byte>(left),
        Vector256.Create<byte>(right));
}
```

The .NET 10 `Vector256.Create<T>(ReadOnlySpan<T>)` overload exists, but has a minimum-length requirement. The lengths **must** be checked before the load. `IsHardwareAccelerated` does not guarantee this operation beats `SequenceEqual`; treat the method as an experiment. Avoid using `Vector256` for 20-byte addresses unless a safe, measured, non-overreading algorithm is established. Do not assume `Vector512` doubles performance.

### 6.2 Padding-zero check baseline

```csharp
internal static bool IsAllZero(ReadOnlySpan<byte> bytes)
{
    foreach (byte value in bytes)
        if (value != 0) return false;
    return true;
}
```

Use this for the **bounded padding slice**, not the entire dynamic value. Compare with an available span primitive on .NET 10 if profiling shows padding checks are significant. A malicious caller may choose arbitrary padding contents; all paths must reject the same non-zero bytes.

## 7. Bulk selector matching: a conditional research track

### 7.1 Data layout determines viability

SIMD works well on **contiguous homogeneous** numeric values. Real nested call selectors are typically the first four bytes of separate, variable-length child ranges and are not one contiguous selector array. A vectorised search requires a gather step (or already-available caller-provided selector index). Model total cost:

`Tbatch = TvalidatedChildTraversal + Tgather + TvectorCompare + TresultMapping`.

`Tdirect = TvalidatedChildTraversal + Σ TscalarSetLookup`.

For a single pass, `Tgather` and result bookkeeping commonly outweigh the short scalar lookup. This is a **hypothesis to measure**, not an asserted universal result.

### 7.2 Allowed use cases

A batch filter is reasonable to investigate if **all** these apply:

- The nested-call walker has already established every candidate child boundary and relevant execution context using trusted schemas.
- A contiguous `ReadOnlySpan<uint>` of selectors already exists or is collected into bounded caller-owned scratch with a justified reuse count.
- The lookup workload is sufficiently large and homogeneous; there are no extra per-selector heap allocations.
- The program needs to test the **same predicate** over many selectors. Selector membership in a large changing allowlist may still favour compiled scalar hash/set lookup rather than a vector comparison.
- Every matching result maps back to the correct original child and contract-aware context, with no false authorisation from selector collisions.
- A negative batch result does not bypass structural/semantic validation of other nested operations.

### 7.3 Decision rule

Start with direct lookups for `1, 2, 4, 8, 16, 32, 64, 128, 256, 1,024` nested calls. Measure distributions of (a) one permitted selector, (b) small fixed allowlist, (c) large compiled allowlist, (d) many duplicates, (e) mostly misses and (f) mixed selectors. Only then test an internal contiguous-batch representation. If batch collection is not demonstrably cheaper for realistic requests, **do not retain it**.

## 8. Input ownership, memory and security constraints

### 8.1 Borrowed views are not immutable storage

`ReadOnlySpan<byte>` protects mutation *through that view* only. The underlying array or pooled buffer may be modified through another alias. A SIMD method has no mechanism to repair a mutable validated buffer. The signing application MUST own or exclusively lease the input for the whole validate→inspect→authorise→sign binding, and MUST prevent pool return until the result is no longer used. If crossing an asynchronous boundary, retain an owner/lease, and rebuild stack-scoped views in a new synchronous region.

### 8.2 Reject malformed bytes before fast access

- **Never** treat bounded range alone as validation provenance. Only the strict RLP/ABI validators create trusted field views and indexes.
- SIMD fast paths may run only on **already validated** and **correctly typed** value slices, unless they are themselves complete, separately tested validation functions.
- Preserve exact full-width arithmetic for RLP lengths and ABI 256-bit offsets/counts; no wraparound, narrowing or signed overflow.
- Ensure every loop makes progress or consumes a bounded logical-value budget; do not assume non-empty encodings for nested ABI structures.
- All vector loads/stores must remain within the field and document bounds, including tails shorter than the vector width.
- A rejection fast path MAY stop early for a definitively forbidden transaction; an **Allow** result MUST never be produced from incomplete validation or incomplete nested-operation inspection.
- Contract-specific execution metadata and policy context remain outside SIMD routines. A false-positive selector search cannot be promoted into a trusted function identity.

### 8.3 Side-channel classification

| Data or comparison | Permitted default | Special rule |
|---|---|---|
| Public function selectors, public contract addresses, transaction amounts | Normal `SequenceEqual`, scalar branching, measured vector operations | Timing may reveal public predicates; review application-specific confidentiality |
| Secret keys, secret tokens, secret authentication values, hidden cryptographic material | `CryptographicOperations.FixedTimeEquals` for equal-length comparisons | Reject ordinary early-exit or speculative SIMD implementations |
| Policy confidentiality-sensitive attributes | Security review required | A public transaction may still carry confidential metadata; assess length/timing/error disclosure |

`CryptographicOperations.FixedTimeEquals` is length-dependent, not a general guarantee for arbitrary protocol-side comparisons. The codec should not contain secret-key processing.

## 9. CPU and platform support matrix

| Execution environment | Required strategy | Optional experiment |
|---|---|---|
| x64 without AVX2 / software fallback | Built-in span/scalar reference | Vector128 where actually accelerated |
| x64 with AVX2 | Same reference result | Vector256 for large contiguous workloads |
| x64 with AVX-512 | Same reference result | Vector512, only with measured power/throughput/tail-latency benefit |
| ARM64 with AdvSIMD | Same reference result | Vector128 / AdvSIMD for appropriate contiguous operations |
| JIT with intrinsics explicitly disabled | Scalar/built-in fallback | Required equivalence test environment |
| ReadyToRun or Native AOT, if adopted later | Compile and test target-specific execution | Do not assume JIT feature switches affect already AOT-compiled code |

Use cross-platform fixed-width vector APIs before architecture-specific intrinsics. JIT code generation, tiered compilation, PGO, CPU frequency, core scheduling, memory bandwidth and container CPU quota can all influence results. Wider vectors can be less efficient on short work or on a particular processor. Do not make claims about AVX-512 downclocking or energy use without measurements on deployment hardware.

Hardware feature overrides such as `DOTNET_EnableHWIntrinsic=0` and `DOTNET_EnableAVX2=0` are useful **test-process diagnostics** on supported runtime versions. Confirm the actual knobs and effects against the pinned .NET **10** runtime; Microsoft notes that the set of knobs differs across versions and that AOT/ReadyToRun may not follow JIT overrides. These overrides belong in test scripts, **not** production defaults.

## 10. Measurement programme

### 10.1 Baselines and experiment controls

Prepare three separately measurable baselines before optimisation:

1. **Correct scalar/reference implementation**: strict RLP/ABI validation and range access in accordance with companion specs.
2. **Built-in .NET baseline**: scalar structural decoder plus `SequenceEqual`, `CopyTo`, `Clear`, `Convert.FromHexString` and `BinaryPrimitives` as applicable.
3. **Full application path**: hex ingress (when present), transaction envelope dispatch, RLP validation, ABI schema resolution/validation, nested-call semantics, policy checks, authorisation binding; exclude unrelated network latency from pure-code measurements but include it in separate service-level tests.

Use the same immutable fixture bytes, strictness settings, compiled schemas, workloads and validation depth for all variants. Never compare a strict full validator to a decoder that only locates one field and call that a valid speedup. Keep setup and schema compilation outside per-request benchmarks, but measure them separately if cold-start matters.

### 10.2 Required microbenchmarks

| Suite | Fixture sizes / variants | Measurements |
|---|---|---|
| Selector read | 0, 3, 4, 68 bytes; valid/invalid | ns/op; allocations; status |
| ABI word equality | 32 bytes: equal; first/middle/last byte mismatch | ns/op; allocations |
| Address equality | 20 bytes; equal/first/middle/last mismatch | ns/op; allocations; no overread |
| 256-bit comparison | 32 bytes: equal, differing high/low limb, values around sign boundary | ns/op; correct order |
| ABI zero padding | 0, 1, 15, 16, 31 bytes; zero/nonzero | ns/op; allocations |
| Hex ingress | 0, 4, 128, 2,048, 524,288 hex chars; malformed positions | ns/op; bytes/s; allocations incl. caller buffer context |
| RLP strict decode | single byte, 55/56 thresholds, realistic tx, 16/256 children | ns/op; allocation; exact status |
| RLP indexing | 9, 12, 16, 128 children; repeated lookups 1/4/16 | ns/op; index scratch; allocation |
| ABI strict decode | ERC-20 transfer, `(address,uint256,bytes)`, 0/31/32/33/65 payloads | ns/op; allocation |
| Nested ABI | multicall 2/16/128/1,024 children; depth 1/8/16 | ns/op; scratch; bounded steps |
| Contiguous fixed-width value scan | 1/8/32/128/1,024/4,096 entries, match first/middle/last/miss | ns/op; bytes touched; allocations |
| Selector filtering | 1/2/4/8/16/32/64/128/256/1,024 children, duplicate/skew distributions | traversal + collection + predicate + mapping total |
| Encoding | static transfer, nested `bytes[]`, long RLP payloads | ns/op; exact bytes; destination overwrite |
| Adversarial rejection | truncated, overflow, invalid word offsets, malformed padding, pathological zero-width tuples | bounded failure latency, zero unexpected allocation |

The **1,024-child** fixtures must respect or deliberately test configured resource limits; exceeding the limits is an expected rejection, not a successful large-workload benchmark. Distinguish the number of child calls from the total bytes of calldata. Do not manufacture invalid byte layouts just to increase counts.

### 10.3 Macrobenchmarks and tail latency

Run a realistic mix specified by the deployment team (illustrative starting mix: 70% simple token transfers, 20% small contract calls, 9% 2–16-child multicalls, 1% larger permitted multicalls). Record actual production distributions before treating that mix as representative. Compare:

- p50/p95/p99/p99.9 end-to-end **in-process** latency and request CPU cost under sustained concurrency;
- throughput at fixed CPU budget / fixed worker count, not throughput after silently allocating extra cores;
- peak RSS, managed allocation B/op, heap/GC collections and peak scratch/stack memory;
- frequency/thermal/energy indicators where the deployment environment can capture them;
- malformed/adversarial input mix to check worst-case bounded-work behaviour;
- cold-start, JIT/TieredPGO and deployment-mode behaviour separately from steady-state.

BenchmarkDotNet provides robust microbenchmark measurement, but its average is not an end-to-end p99 guarantee. Use a representative load harness for tail latency, with fixed concurrency, arrival rate and CPU affinity/quota policies recorded. An aggressively saturating system can distort p99 and mask small gains.

### 10.4 Reproducibility manifest

Every performance result MUST record: repository commit, SDK (`dotnet --info`), target framework, runtime version, architecture, CPU model/features, OS/kernel, power policy, whether virtualised, memory, core topology/affinity, TieredCompilation/TieredPGO/ReadyToRun settings, debug/release status, benchmark framework version, input fixture hashes/sizes, limits, warm-up configuration, iterations/samples and confidence interval or variability. Record whether JIT instruction-set switches were applied **before process start**.

For machine-code-sensitive changes, capture JIT/disassembly where feasible, to confirm vector instructions actually appear. Validate interpretation of `IsHardwareAccelerated`; branch selection alone is not proof of faster generated code.

### 10.5 Default acceptance thresholds

These thresholds are **engineering change gates**, not promised performance numbers:

1. **Correctness gate (absolute):** zero semantic divergences across scalar, built-in and vectorised paths on all supported valid/invalid fixtures, fuzz corpora and contract tests.
2. **Allocation gate (absolute):** no additional managed allocations on existing zero-allocation hot paths; no attacker-controlled size-proportional allocations. Caller-provided scratch must be measured separately and bounded.
3. **Safety gate (absolute):** no out-of-range load/store, overflow, stale-buffer reuse, invalid shortcut to Allow, unsupported-hardware exception or data race.
4. **Microbenchmark gain (default for accepting additional complexity):** reproducible **≥10% lower median latency** on at least one genuinely hot, representative candidate operation, with confidence interval excluding an insignificant result. Do not optimise a niche case alone.
5. **End-to-end relevance:** either demonstrate **≥3% improvement** in a representative in-process workload at equivalent resource budgets, or document a stronger operational reason (e.g. much lower CPU per transaction at scale). If neither occurs, default to retaining the simpler implementation.
6. **Non-target regression:** no repeatable **>3%** slowdown in important common fixtures and no material p99, memory, throughput or power regression in sustained workloads. If noise exceeds the threshold, remeasure rather than force a pass/fail verdict.
7. **Portability:** tested scalar fallback on unsupported platforms and controlled instruction-set-disabled processes; target ARM64/x64 builds must compile and pass.

Thresholds are *initial decision rules* for the engineering team, not universal scientific constants. Report raw evidence and a concise keep/reject justification; do not redefine a failed experiment as successful by selecting a favourable fixture.

## 11. Automated testing and adversarial plan

### 11.1 Equivalence oracle

The scalar, built-in and optional SIMD implementations MUST receive identical source bytes, validated ranges, schemas, policy contexts and limits. Assert equality of:

- accept/reject outcome, error/status enum, error position and `out` resets;
- field ranges and output spans (including zero-length fields) and typed values;
- exact encoded output and writer failure/finalisation semantics;
- every nested child and its execution context, plus final policy decision;
- unchanged source, caller workspace and destination outside the authorised write interval.

The scalar algorithm must itself be checked against **independent reference implementations and published vectors**; agreement among two implementations sharing the same bug is not sufficient.

### 11.2 Boundary cases — mandatory

- Byte lengths `0,1,3,4,15,16,17,19,20,21,31,32,33,55,56,63,64,65` and boundaries around every vector width; non-multiples of 16/32/64.
- Slice begins at unaligned offsets, ends exactly on allocation/page boundaries; short tails; fragmented logical structures; zero-length spans.
- Match/no-match and first/middle/last-byte differences; duplicate selectors; selector collisions selected by different trusted contract identities.
- ABI numbers at 0, 1, maximum `uint64`, maximum `uint256`, sign boundary; values too wide to narrow; both correct and malformed sign extension.
- ABI padding 0..31 bytes; early/late non-zero padding; malformed dynamic offsets, overlapping tails, wrong alignment, excessive array count, zero-width tuple work exhaustion.
- RLP prefix classes and every 55/56 canonical transition; long-form zero-leading length, invalid nested boundaries, extra top-level bytes, too-deep and too-many-items rejection.
- Multicall with valid nested `bytes[]`, nested tuples, indirect target semantics and opaque bytes that coincidentally contain selectors.
- Hex uppercase/lowercase, optional handled prefix, odd number of hex chars, invalid digit at first/middle/last, destination too small, partial-conversion output rejected.
- Mutating an aliased buffer after validation must be treated as a violation of caller ownership assumptions and caught by integration controls; do not claim SIMD can detect it.

### 11.3 Property, mutation and fuzz testing

- Property tests: for any valid input and supported schema, scalar and SIMD paths have identical decoded values, canonical encodings, byte ranges and statuses.
- Mutation tests: flip every byte class in canonical fixtures; truncate at strategic offsets; modify 256-bit offsets/counts, RLP length prefixes and ABI padding.
- Fuzz RLP and ABI independently using multiple encoders/decoders where possible; preserve a regression corpus and minimise crashes/divergences.
- Run long-lived fuzz campaigns against native/managed memory-boundary conditions. In .NET, use constrained/guard-page tests or equivalent harnesses to detect overreads; avoid assuming that a successful span slice makes an unsafe load safe.
- Test concurrency with distinct immutable buffers and reused pooled storage under strict lease ownership; avoid hidden static scratch and nondeterministic buffers.
- Run each test suite under normal hardware, reduced SIMD availability and fully disabled intrinsics (where the actual pinned runtime supports the test flags). Run on actual x64 and ARM64 hardware for release coverage; emulation alone is not sufficient evidence of intrinsic performance.

### 11.4 Integration tests with signing policy

An approval MUST be bound to the exact transaction input/digest, chain, signer and policy version. Include regression fixtures where selector-only matching would authorise an opaque `bytes` region, nested forbidden calls are hidden inside an otherwise permitted multicall, or the target code/implementation changes after policy resolution. SIMD optimisations must never alter those decisions.

## 12. Threat model and required controls

| Threat / failure | Security consequence | Control and test |
|---|---|---|
| Vector load past a 20-byte address or end of field | Read past authorised bytes, possible crash/data exposure | Length check **before** load; exact 20-byte range; guard-page/short-tail tests |
| RLP payload scanned for child headers | False field boundaries or bypass | Preserve scalar canonical RLP header traversal; differential adversarial corpus |
| Arbitrary ABI bytes searched for selector | False nested-call identity; policy bypass | Resolve nested calls only from trusted execution schemas and validated ranges |
| SIMD integer reinterpretation | Incorrect amount/limit, overflow or signedness confusion | Explicit big-endian signed/unsigned comparators, width checks, extreme-value tests |
| ABI offset narrowing to 32-bit lane | Wrong child/field, out-of-bounds | Full uint256 bounds check before native index conversion |
| Vector tail/mask error | False matches or out-of-bounds read | Handle 0..vectorWidth-1 tails; guard and mutation tests |
| Validation shortcut in fast path | Allow for malformed calldata | Same full strict validation before Allow, identical error/status contract |
| Mutable borrowed buffer / stale index | Authorisation of changed bytes | Exclusive ownership/lease and signing-digest binding; integration tests |
| Hidden per-request scratch allocation | DoS, increased GC or memory spikes | Caller-owned bounded scratch, allocation checks and workload budgets |
| Unsupported intrinsic or runtime mode | Crash or divergent results | Capability gating + tested scalar fallback |
| Early-exit secret comparison | Timing side channel | Fixed-time primitives for secret data; explicit data classification |
| Benchmarks favour malformed or non-equivalent work | Unsafe optimisation decision | Matched fixture/strictness, reproducible baselines and code review |
| AVX-512 energy/frequency or contention effect | Increased latency/operating cost | Sustained multithread load test on actual deployment hardware |

**Non-guarantees:** Vectorisation does not prove an ABI schema matches deployed code, that EVM execution is restricted to encoded calls, that an RPC result is current, or that a transaction is safe to sign. The codec and policy trust boundaries remain unchanged.

## 13. Implementation sequence and work items

The coding agent MUST inspect existing exports, direct callers, common binary utilities, tests and transaction data flow before modifying code. **Do not refactor unrelated code.** Use existing names and layout if the repository differs from the illustrative structure below.

### A. Record and verify the baseline

- Read both companion specifications completely; compare implemented behaviour and tests against their release contracts.
- Inventory existing `SequenceEqual`, scalar word readers, RLP prefix parser, ABI validator, selector extraction, hex ingress and writer calls.
- Add correctness/edge/adversarial test fixtures for the existing paths **before** performance changes.
- Add BenchmarkDotNet microbenchmarks and an in-process policy integration benchmark; record the baseline, fixtures and environment.

**Done when:** Baseline semantic tests pass, allocations and latency are measured, and the strongest actual CPU hotspot is identified.

### B. Use existing .NET fast paths

- Replace only proven unnecessary object allocations, redundant scans or slow handwritten loops with correct span/BinaryPrimitives APIs.
- Maintain status-code contracts; ensure no functional change to `RLPData`, `ABIData`, `ABICallData` or writer public surfaces.
- Add regression tests for each changed routine, including invalid and adversarial input.

**Done when:** Built-in baseline passes all security and correctness tests; effects are measured and documented.

### C. Implement one narrowly scoped SIMD experiment

- Choose **one** profiled operation with a realistic contiguous-data workload.
- Keep scalar reference and vector version private/internal, with a clear capability gate and no request-scoped allocation.
- Cover short input, exact-width input, tail lengths, unaligned data, pathological values, unsupported hardware, error precedence and fallback.
- Examine generated machine code if practical, then compare CPU, allocations, tail latency and load-level behaviour.

**Done when:** Candidate either passes all gates and is retained, or is rejected and removed with recorded evidence.

### D. Apply measured lessons without increasing surface area

- If a second independent hot operation qualifies, repeat the same procedure.
- Reject selector batching if traversal/gather/mapping cost outweighs gains.
- Avoid introducing a framework of unused scalar/SIMD strategies. Promote common code to a small internal library **only if reused by both codecs and beneficial in maintenance or measured performance**.

**Done when:** Every retained optimisation is independently justified, tested and isolated from public API contracts.

### E. Final release evidence and documentation

- Execute complete RLP v2.0 and ABI v2.1 acceptance suites, plus this document's cross-path tests.
- Record x64/ARM64 evidence, reduced-hardware path coverage, fuzz campaigns, benchmark variability and sustained-load results.
- Document operational assumptions, hard limits, input ownership, fallback and diagnostic flags.
- Record any unverified platform, environment, benchmark or test as **not verified**; never claim it passed.

**Done when:** All required correctness, security, portability and performance gates have recorded evidence, with remaining exceptions explicitly approved.

## 14. Suggested repository placement

The actual repository structure controls exact filenames. Do not rename existing types or break package boundaries for this guide. A possible minimal arrangement is:

```text
src/
  Evm.Rlp/
    ... existing RLPData and encoder/validator files ...
  Evm.Abi/
    ... existing ABIData and ABICallData files ...
  Evm.Binary/                 # OPTIONAL, only if shared existing dependency is justified
    Internal/
      BinaryValueOperations.cs
      VectorizedBulkOperations.cs    # ONLY after benchmark-backed acceptance

tests/
  Evm.Rlp.Tests/
  Evm.Abi.Tests/
  Evm.Binary.Tests/           # OPTIONAL if a shared internal package exists
  Evm.Codecs.IntegrationTests/

benchmarks/
  Evm.Codecs.Benchmarks/
    BinaryWordBenchmarks.cs
    RlpBenchmarks.cs
    AbiBenchmarks.cs
    NestedCallBenchmarks.cs
    IngressBenchmarks.cs
    SigningPolicyPreparationBenchmarks.cs
    README.md                 # results, fixtures, command lines and environment

docs/
  SIMD_Optimisation_Evidence.md    # measured keep/reject decisions, not guessed wins
```

A small internal helper should usually remain in whichever package owns the operation; avoid creating an `Evm.Binary` project merely because SIMD exists.

## 15. Code review checklist

A change is not ready for merge unless reviewers can answer **yes** to all applicable questions:

- [ ] Did the agent read both normative codec specifications and relevant code/callers/tests?
- [ ] Is the operation demonstrably hot, and is SIMD plausible given the real data layout?
- [ ] Are scalar/built-in and SIMD paths semantically identical under all resource limits?
- [ ] Does the code validate lengths and types before a vector load/store?
- [ ] Are offsets, encoded ranges and nested-call semantics unaffected?
- [ ] Does every allow/authorisation still depend on complete required validation?
- [ ] Is borrowed buffer ownership preserved for the entire authorisation/signing boundary?
- [ ] Are only public non-secret data processed by ordinary early-exit comparisons?
- [ ] Is the scalar fallback tested without relying on an AVX2/ARM64 runtime?
- [ ] Do all affected normal, edge, invalid, fuzz, differential and integration tests pass?
- [ ] Are there no additional managed allocations on the zero-allocation hot paths?
- [ ] Is measured throughput/CPU gain significant **end-to-end**, not merely in isolation?
- [ ] Are p95/p99 latency, peak memory and adverse/slow hardware regressions acceptable?
- [ ] Were command lines, runtime, hardware, fixtures and measurements recorded?
- [ ] Is a failed or unmeasured experiment labelled accurately and rejected by default?

## 16. Independent acceptance criteria

The optimisation effort is complete when:

1. The exact `RLPData v2.0` and `ABIData`/`ABICallData v2.1` public semantics and security requirements remain satisfied; all their required tests pass.
2. Scalar paths and built-in operations provide the portable correctness baseline and remain available on the supported targets.
3. Any SIMD fast path is internal and selected only under a correct hardware/length/data-layout guard; all supported paths produce identical results and errors.
4. Zero-copy validation, immutable ownership assumptions, caller-owned scratch limits, full-width arithmetic and fail-closed authorisation are preserved.
5. BenchmarkDotNet and full policy-path measurements are reproducible, including allocation and tail latency evidence; retained vector operations satisfy the stated evidence gates or have explicitly approved justified exceptions.
6. ARM64 and x64 are compiled and tested on appropriate runtimes, and fallback tests are run; any missing hardware evidence is disclosed.
7. A coding agent can implement or decline each optimisation without changing the normative codec designs, and the final documentation includes tested decisions and known limitations.

**Preferred final outcome:** The smallest, simplest, fully tested implementation that wins on representative production workloads. A result with **zero custom SIMD routines** is conformant if profiling finds no worthwhile SIMD opportunity.

## 17. Primary references

Consult the following official sources and pin target-framework specifics when implementation begins:

1. **Microsoft — Use SIMD and hardware intrinsics in .NET:** https://learn.microsoft.com/en-us/dotnet/standard/simd  
   Available vector APIs, capability checks, JIT behaviour, loop tails, best practices, platform differences and test-mode feature overrides.
2. **Microsoft — `Vector256.Create` (.NET 10):** https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.vector256.create?view=net-10.0  
   Includes `Create<T>(ReadOnlySpan<T>)` and length/element-type constraints.
3. **Microsoft — `MemoryExtensions.SequenceEqual`:** https://learn.microsoft.com/en-us/dotnet/api/system.memoryextensions.sequenceequal?view=net-10.0  
   Baseline API for span equality.
4. **Microsoft — `Convert.FromHexString` (.NET 10):** https://learn.microsoft.com/en-us/dotnet/api/system.convert.fromhexstring?view=net-10.0  
   Includes the destination-span overload returning `OperationStatus` and partial-write warning.
5. **Microsoft — `CryptographicOperations.FixedTimeEquals` (.NET 10):** https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals?view=net-10.0  
   Fixed-time equality for secret-dependent comparisons.
6. **Microsoft — `GC.GetAllocatedBytesForCurrentThread` (.NET 10):** https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-10.0  
   Per-thread managed allocation measurement.
7. **Solidity — Contract ABI Specification:** https://docs.soliditylang.org/en/latest/abi-spec.html  
   Schema-dependence, selector semantics, strict ABI head/tail layout, encoding and padding.
8. **Ethereum — Recursive-Length Prefix (RLP):** https://ethereum.org/developers/docs/data-structures-and-encoding/rlp/  
   Variable-length RLP item prefix and canonical encoding rules.
9. **BenchmarkDotNet documentation:** https://benchmarkdotnet.org/  
   Performance benchmark harness; pair with independent sustained-load latency measurements.

**Reference check date:** 17 November 2026. None of these source references constitutes evidence that a proposed implementation has compiled, passed tests or achieved a performance target.
