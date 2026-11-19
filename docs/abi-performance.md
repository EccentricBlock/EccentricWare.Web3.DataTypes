# ABI codec performance record

Measurements of the ABI codec on one machine, taken with a BenchmarkDotNet **short job**. They show that the benchmarks run and give orders of magnitude; they are **not** an accepted baseline, carry wide error bars, and support no regression gate. No claim below is stronger than the table it comes from. Spec 11.2 and 11.3 ask for more (full BenchmarkDotNet jobs, confidence intervals, p95/p99 for a full policy pipeline, peak memory); what is missing is listed at the end.

## Environment

| Item | Value |
| --- | --- |
| BenchmarkDotNet | 0.15.8, `--inProcess --job short` (ShortRun: 1 launch, 3 warm-up, 3 measured iterations, `InProcessEmitToolchain`) |
| OS / CPU | Linux Ubuntu 26.04.1, Intel Core Ultra 9 185H (12 logical cores, hybrid), x86-64-v3 |
| Runtime | .NET SDK 10.0.115, runtime 10.0.15, X64 RyuJIT; Release build, `TieredPGO` on, workstation non-concurrent GC |
| Library | `HEAD` 358db52 plus the uncommitted working tree; `sha256(sha256sum of every EccentricWare.Web3.DataTypes/ABI/**/*.cs, sorted)` begins `30d7b5135cb023e6`; unchecked arithmetic, the default build |
| Machine state | load average 1.0 when the run started; nothing was compiled or tested while it ran (about 20 minutes, 175 benchmarks) |

The tables in the sections below are **run 3**: the whole `*Abi*` suite (175 benchmarks, 18 classes) re-run on the final code of the correction round (typed surface, vector run kernels, bounds-checked padding window, `TryBegin` scratch cut), replacing the numbers of runs 1 and 2 (which predated the encoder prologue change and the vector kernels). The raw reports are kept outside the repository (`BenchmarkDotNet.Artifacts/` is git-ignored); the manifest above, the command under "Re-running" and the class names let anyone repeat the run. Short job, 3 iterations: several cells have an error interval wider than the mean difference being discussed, and run-to-run differences of 20 to 30 percent for identical code were seen in runs 1 and 2 (128-child multicall decode: 1.96 us, 2.58 us and 2.69 us in three classes of the same code), so read every figure as plus or minus that. Comparisons against the earlier runs below are directional only.

Inputs are built by two independent reference encoders (`ABIReferenceModel.cs`, `ABIIndependentEncoder.cs`, compiled into the benchmark project) and each `[GlobalSetup]` checks they decode and that the writer reproduces them byte for byte, including the Foundry `cast` bytes of the ERC-20 `transfer`. Decode limits are `ABIDecodeLimits.Default` throughout.

## Spec 11.2 workloads

### Strict decode (run 3)

| Workload | Mean | Allocated |
| --- | ---: | ---: |
| ERC-20 `transfer` arguments (2 static words) | 14.4 ns | 0 |
| ERC-20 `transfer` call (selector + arguments) | 15.4 ns | 0 |
| Uniswap V3 `exactInputSingle` arguments (static tuple of 8 words) | 52.2 ns | 0 |
| Uniswap V3 `exactInputSingle` call | 53.8 ns | 0 |
| `(address,uint256,bytes)` with 65 bytes | 24.5 ns | 0 |
| 256-KiB `bytes` payload (262,048 bytes) | 14.5 ns | 0 |
| `multicall(bytes[])`, 2 children | 38.2 ns | 0 |
| `multicall(bytes[])`, 128 children | 1,235 ns | 0 |

Dynamic `bytes` at five lengths: 0 bytes 14.4 ns, 31 bytes 15.9 ns, 32 bytes 14.3 ns, 33 bytes 16.0 ns, 65 bytes 16.0 ns. The decoder reads the length word and checks the padding; it never touches the payload bytes, so the 256-KiB row costs the same as an empty value (it validates, it does not copy). The step from 32 to 33 bytes (the first value with partial-word padding to check) was the padding scan itself, a byte-at-a-time loop that cost 32.5 ns at 33 bytes in the first run; it is now a masked `Vector128` check from 8 padding bytes up, bounds-checked (`docs/abi-design-notes.md` rows 59 and 66), and the step is about 1.7 ns.

Nested `uint256[]...[]`, one element per level (value depth is levels + 2, so 14 levels is exactly the default `MaxValueDepth` of 16): 1 level 21.2 ns, 8 levels 116 ns, 14 levels 203 ns, about 14 ns per level.

### Locate (run 3)

`AbiLocateSiblingBenchmarks`: `TryLocate` and `TryGetBytePayload` of a child of `multicall(bytes[])` with 2, 16, 128 and 1,024 children, repeated 16,384 times per decode so the decode adds about 1 ns per lookup.

| Children | Locate first child | Locate last child | `TryGetBytePayload` last child | Decode (reference) |
| ---: | ---: | ---: | ---: | ---: |
| 2 | 20.2 ns | 18.1 ns | 19.7 ns | 37.1 ns |
| 16 | 21.0 ns | 18.0 ns | 20.1 ns | 176 ns |
| 128 | 20.4 ns | 17.6 ns | 19.7 ns | 1,261 ns |
| 1,024 | 20.9 ns | 17.9 ns | 20.2 ns | 9,511 ns |

The lookup stays at 18 to 21 ns while decoding grows 257-fold, which is what the next-dynamic-sibling end bound predicts. These lookups are 25 to 35 percent slower than in run 2 (13 to 16 ns), in every row and in the same direction. That is inside the 20 to 30 percent spread seen between runs of identical code, but the consistency means a real regression of a few nanoseconds cannot be ruled out; it was **not investigated** (the locator source differs from run 2 only by `scoped` on parameters, which does not change generated code). `AbiLocateStaticSiblingBenchmarks` (`(bytes, uint256 x K, bytes)`, locate the first dynamic field): K = 1, 16, 256 give 11.7, 11.7 and 11.9 ns while the decode grows 29 to 879 ns. This is the benchmark half of the spec 14 item "lookups at fixed path depth with growing sibling counts"; the deterministic proof is the step-counter tests `ABILocatorTests.LocatingADynamicField_CostsTheSameNumberOfSteps_*` and `StaticSiblingsBetweenDynamicOnes_AreSkippedWithoutScanning`. All rows repeat one path, so they are steady-state, branch-predictable lookups, not cold-cache ones.

### Typed accessors (run 3; one decode per 1,024 reads, decode share about 0.02 ns)

| Accessor | Mean |
| --- | ---: |
| `TryGetAddress` (transfer recipient) | 7.3 ns |
| `TryGetUInt64` (transfer amount) | 8.2 ns |
| `TryGetUInt256Word` (transfer amount) | 7.2 ns |
| `TryLocate` (`exactInputSingle.amountIn`, path `[0,5]`) | 9.9 ns |
| `TryGetUInt64` (`exactInputSingle.fee`, path `[0,2]`) | 11.1 ns |

### Encoding (run 3)

| Workload | Mean | Allocated |
| --- | ---: | ---: |
| `ABICallEncoder`: ERC-20 `transfer` call | 20.3 ns | 0 |
| `ABIWriter`: `transfer` arguments | 19.5 ns | 0 |
| `ABICallEncoder`: Uniswap V3 `exactInputSingle` call | 66.7 ns | 0 |
| `ABIWriter`: `(address,uint256,bytes)` with 65 bytes | 33.8 ns | 0 |
| `ABIWriter` `bytes`: 0 / 31 / 32 / 33 / 65 bytes | 22.2 / 22.9 / 22.0 / 22.8 / 23.7 ns | 0 |
| `ABIWriter` `bytes`: 262,048 bytes | 4,520 ns | 0 |
| `multicall` call, 2 children: write / measure | 56.2 / 42.6 ns | 0 |
| `multicall` call, 128 children: write / measure | 1,730 / 1,069 ns | 0 |

Against run 1 the encoder rows moved in both directions: `transfer` call -13 percent, `exactInputSingle` -16, the 65-byte mixed write +8, the `bytes` rows +9 to +16, `multicall` write +11 to +12. The short job cannot resolve these (all are inside the 20 to 30 percent spread), so none of them is a finding. The evidence for the encoder-prologue change (`transfer` -20 percent, `exactInputSingle` -31) is the default-job A/B in the SIMD section below, not this table; the `bytes` and `multicall` rows were not part of that A/B and a few-percent cost from the safety guards (row 57) is plausible. The 262,048-byte write is one copy of the payload plus padding (about 58 GB/s; a 256-KiB copy is cache-resident, so this is not main-memory bandwidth). Scratch is `stackalloc`ed per call (8 frames of 52 bytes) and is part of these times. A measure-then-write pipeline costs the sum of the two rows.

### Hostile and boundary input (run 3)

| Input | Mean | Result |
| --- | ---: | --- |
| 128-child multicall, pointer error in child 0 | 25.4 ns | `InvalidOffset` |
| 128-child multicall, pointer error in child 127 | 1,212 ns | `InvalidOffset` |
| `bytes[]` count 2^256 - 1 | 18.5 ns | `ElementLimitExceeded` |
| `bytes` length 2^256 - 1 | 15.6 ns | `Truncated` |
| 128-child multicall plus one trailing byte | 1,228 ns | `NonCanonicalLayout` |
| `(()[])` with 4,096 zero-width elements (4,098 values from 64 bytes) | 29.6 us (StdDev 0.2 us) | accepted |
| five such arrays: 20,486 values from 320 bytes | 119 us (StdDev 1.2 us) | `WorkLimitExceeded` at the 16,384-value budget |

Rejection is bounded by where the error sits (25 ns near the start, the cost of a full scan at the end), not by claimed sizes. The zero-width bomb is refused after about 16,400 values (about 7 ns each), not after 20,486, and cost grows with the configured visited-value budget, not with the 320 input bytes: size `MaxVisitedValues` in proportion to `MaxEncodedBytes` (`docs/abi-api.md`). The bomb row again reported `1 B` allocated per operation (in both runs where it was measured). The same inputs allocate exactly zero bytes in `ABIHardeningAllocationTests.ZeroWidthValueBudgetExhaustion_*` (an exact assertion over 200 decodes after warm-up, five trials), so the library does not allocate per call; why BenchmarkDotNet shows 1 B (a one-off runtime or harness allocation amortised over the iterations is the likely, unmeasured, explanation) was not investigated. The exact assertion, not the rounded column, is the evidence for zero allocation.

### RLP extraction plus ABI decoding (run 3)

A signed legacy transaction whose `data` is `approve(spender, 2^256-1)`:

| Row | Mean |
| --- | ---: |
| RLP decode of the transaction only | 21.8 ns |
| `ABICallData.TryDecode` of the `approve` calldata only | 14.9 ns |
| RLP decode, enumerate to the `data` item, ABI call decode of that exact span, read spender and amount | 60.0 ns (error 27.6 ns) |

The sum of the two halves is 36.7 ns; the remaining 23 ns (with a wide interval) is the child enumeration (six `MoveNext` calls over the nine fields) and the two accessors. The `data` span is passed unchanged: no copy, no second validation.

### EVM adapters (run 4)

`AbiEvmAdapterBenchmarks`, short job like run 3, run on its own after the adapters were added (same machine and runtime; the load average was 4.7 falling to 3.0 during the 39-second run, because a mutation-testing job had just finished, so read these as plus or minus the usual 20 to 30 percent or worse). The walker uses a struct host with a fixed code identity; `DecodeOnly` is the strict decode of the same outer call.

| Row | 2 children | 128 children | Allocated |
| --- | ---: | ---: | ---: |
| `RLPCalldataAdapter.TryRead` of the signed legacy `approve` transaction, then `TryGetFunctionCalldata` | 88.5 ns | 91.3 ns | 0 |
| `ABICallData.TryDecode` of `multicall(bytes[])` (reference) | 31.2 ns | 1,023 ns | 0 |
| `NestedCallWalker.TryWalk` of the same call (outer call plus every `transfer` child resolved, decoded and shown to the host) | 189 ns | 8,988 ns | 0 |

The walk costs about 62 ns per child beyond the outer decode: locating the child payload from the root, one resolver lookup (a frozen dictionary keyed by chain, contract, code identity and selector, hashing an `Address` and a `Hash32`), one strict decode of the child, and two host calls. The adapter reads every field of the envelope and the signature, so it costs about four times the 21.8 ns plain RLP decode of the end-to-end rows. Neither was optimised: no profile was taken, and no gate in the SIMD guidance applies (no vector code was added).

### EVM adapters after the EIP-7702 work (run 5)

The same benchmark after rows 80 to 84 of `docs/abi-design-notes.md` (the adapter now reads the nonce; the walker's host reports an `EvmAccountCode` instead of a `Hash32`, and every code lookup scans the processed authorization list, empty here). Short job, run while other work kept the load average between 8.3 and 5.3, so the error bars are wider than run 4's and the comparison is indicative only. The strict decode baseline matched run 4 (32.4 against 31.2 ns, 1,040 against 1,023 ns).

| Row | 2 children | 128 children | Allocated |
| --- | ---: | ---: | ---: |
| `RLPCalldataAdapter.TryRead` + `TryGetFunctionCalldata` | 113.8 ns | 114.3 ns | 0 |
| `ABICallData.TryDecode` (reference) | 32.4 ns | 1,040 ns | 0 |
| `NestedCallWalker.TryWalk` | 243 ns | 11,871 ns | 0 |

Against run 4 that is about 85 ns per child instead of 62, and about 25 ns more per adapter read. Part of it may be the load; the likely real costs are the larger value the host returns per lookup and the nonce read. Not profiled and not optimised. EIP-7702 processing itself (one Keccak-256 and one host recovery per tuple) has no benchmark: its cost is dominated by the host's secp256k1 recovery.

## Comparison with Nethereum.ABI 7.1.0 (run 3)

Not like for like: Nethereum decodes permissively into freshly allocated objects (it accepts trailing bytes, `bool` = 2 and `uint8` = 256), this library validates strictly and returns borrowed spans; Nethereum's encoder allocates its result, the library writes into a caller buffer. Both sides produce the same bytes and read the same values (checked in `[GlobalSetup]`).

| Workload | Library | Allocated | Nethereum | Allocated |
| --- | ---: | ---: | ---: | ---: |
| decode `transfer` | 14.7 ns | 0 | 1,595 ns | 5,800 B |
| decode `multicall` 128 children | 1,231 ns | 0 | 17,719 ns | 109,801 B |
| encode `transfer` | 19.9 ns | 0 | 328 ns | 1,000 B |
| encode `multicall` 128 children | 1,721 ns | 0 | 27,900 ns | 128,816 B |

## SIMD and CPU evaluation (SIMD guidance 1.0)

The Environment table and the short-job tables above describe run 3. This section uses default jobs on a loaded VMware guest and says so; it does not replace the accepted-baseline caveats at the top and bottom of this file.

What the guidance asked for: profile first, then keep only what passes its gates (scalar reference kept and tested, at least 10 percent on a hot representative operation, at least 3 percent end to end, no repeatable regression above 3 percent elsewhere, zero new allocations, identical statuses, offsets and work accounting). The decision record is `docs/abi-design-notes.md` rows 59 to 63; the numbers below are BenchmarkDotNet 0.15.8 **default** jobs, out of process, Release, .NET 10.0.15 x64 on the machine of the Environment table (a VMware guest shared with other jobs; load average 1.1 to 2.5 for the quoted A/B pairs; original library and final code alternated, two rounds where marked). Drift of the same code between sessions was 3 to 10 percent, so a difference under about 3 percent is not resolved. AVX-512 is absent here (`Vector512` not accelerated); ARM64 was not available.

Where the time goes (original decoder, medians): an empty document 6.2 ns (entry, frame set-up, one pass of the loop); every tuple member 5.2 to 5.7 ns (`uint256 x 8`: 51.8 ns), of which the word's own rule is under 1 ns (a `uint8` array element costs 5.4 ns, a `uint256` one 4.7); every array element 4.4 to 5.4 ns (the loop bookkeeping, not the check); a `multicall(bytes[])` child 13 ns, about 6 of them the padding loop; a static tuple inside an array about 14.8 ns per `(address,uint256)` element (frame push and pop); a `bytes` payload nothing (never read). Encoder (original): `TryBegin` 8.7 ns, `TryFinish` about 3, an `address` write 6.9 to 7.4, a `uint256` word 9 to 10.5, the measurer about 8 per word.

Retained (original against final, default job):

| Operation | Original | Final | Change |
| --- | ---: | ---: | ---: |
| `uint256[N]` decode, N = 3 / 32 / 4,096 | 28.5 / 174 / 18,800 ns | 18.6 / 18.6 / 18.5 ns | -35 percent / -89 / -99.9 (no byte read) |
| `address[N]`, N = 3 / 32 / 4,096 | 26.3 / 156 / 16,700 ns | 22.1 / 27.5 / 1,316 ns | -16 / -82 / -92 |
| `uint8[N]`, N = 3 / 32 / 4,096 | 29.8 / 192 / 21,900 ns | 22.7 / 27.1 / 1,302 ns | -24 / -86 / -94 |
| `address[1]`, `address[2]`, `uint8[2]` | 18.4 / 22.4 / 24.8 ns | 18.4 / 23.1 / 25.2 ns | 0 / +3 / +2 percent (at the noise floor) |
| `bytes` of 33 bytes; `multicall` of 2 / 16 / 128 children | 19.7; 39.7 / 227 / 1,668 ns | 14.5; 29.9 / 146 / 970 ns | -26; -25 / -36 / -42 percent |
| ERC-20 `transfer` arguments / call (mean of two rounds) | 16.6 / 17.0 ns | 13.8 / 14.9 ns | -17 / -12 percent |
| Uniswap V3 `exactInputSingle` arguments / call (mean of two rounds) | 53.8 / 54.4 ns | 47.6 / 48.8 ns | -11 / -10 percent |
| `uint256 x 8` tuple (two rounds) | 51.3 ns | 33.9 ns | -34 percent |
| Guidance request mix (70/20/10) / array-heavy mix, per request (two rounds) | 57.8 / 200.9 ns | 39.1 / 34.3 ns | -32 / -83 percent |
| Encoder: transfer arguments / `exactInputSingle` call / `uint256[32]` write | 24.7 / 91.8 / 316 ns | 19.8 / 62.9 / 237 ns | -20 / -31 / -25 percent |

Not retained, with the numbers (kernel medians, ns): `TryCompareUnsigned256` limb / `UInt128` / `Vector256` variants 2.10 / 2.02 / 2.23 against 2.56 on random words but worse on equal or last-limb-different ones; word equality `SequenceEqual` 1.65 against `Vector256` 1.21 (the codec has no such helper); selector read `ReadUInt32BigEndian` 0.75 against a `Vector128` compare 0.93; isolated per-word masks (address 1.49 against 1.98, `uint8` 1.60 against 3.51) lose in the validator because the mask must be selected per member (a prototype: `transfer` +20 percent); selector batching was modelled, not built (at most 7 percent of a 13 ns child traversal). `Vector256` for the padding check, a scalar four-limb mask kernel, and a variant with the small-padding loop inlined into `ValidateBytes` (2 to 10 percent slower at 1 to 7 bytes) were measured and dropped. Details: docs/abi-design-notes.md rows 59 to 62.

### Checked against unchecked vector loads (correction round)

The retained SIMD code first used `MemoryMarshal.GetReference` plus `Vector128/256.LoadUnsafe` in both places (the run kernels and the padding window). SIMD guidance 4.5 and 4.6 say no unsafe loads unless benchmarks justify them and memory-safety tests exist, so bounds-checked forms were prototyped (on the contested fixtures only: `Vector256.Create(ReadOnlySpan<byte>)` on exact slices, `MemoryMarshal.Cast` to a vector span with indexed and with `foreach` loops, a sliding span) and measured against the unchecked form: BenchmarkDotNet 0.15.8 default job, out of process, Release, .NET 10.0.15 x64, the VMware guest of the Environment table, benchmark process pinned to two cores (`taskset -c 4,5`), nothing else running on them, load average 0.5 to 1.8 (`uptime` checked before), three alternated rounds with the order rotated, medians per round (ns); `AbiShapeBenchmarks.cs` fixtures. An earlier unpinned two-round pass at load 1.4 to 3.4 had run-to-run differences of 5 to 10 percent on the same code (for instance `uint256[32]`, which reads no word at all, differed by 12 percent between two builds of identical behaviour), which is why the pinned pass was made.

| Fixture | Unchecked | Bounds-checked (`Create` on slices) | Change | `Cast` plus quad slice | Change |
| --- | ---: | ---: | ---: | ---: | ---: |
| `address[32]` dynamic / fixed | 27.7 / 26.6 / 27.9; 26.8 / 23.5 / 26.9 | 28.2 / 28.0 / 29.5; 25.8 / 28.7 / 28.2 | +4.4 / +7.0 percent | 25.9 / 28.9 / 29.1; 27.4 / 26.8 / 27.2 | +2.1 / +5.3 percent |
| `uint8[4096]` dynamic / fixed | 1,318 / 1,307 / 1,310; 918 / 911 / 914 | 1,211 / 1,215 / 1,207; 981 / 994 / 990 | -7.7 / +8.1 percent | 1,331 / 1,330 / 1,333; 1,027 / 1,028 / 1,027 | +1.5 / +12.4 percent |
| `bytes` of 33 bytes (padding window only) | 14.5 / 14.3 / 14.3 | 14.5 / 14.6 / 14.6 | +1.4 percent | | |
| `multicall` 128 children (padding window only) | 968 / 963 / 981 | 973 / 980 / 968 | +0.3 percent | | |
| `uint256[32]` (no word is read) | 16.9 / 16.4 | 17.0 / 16.3 | +0 percent | | |
| `multicall` 2 / 16 children | 31.0 / 28.7; 145.5 / 144.8 | 30.8 / 29.2; 153.1 / 143.8 | +0.6 / +2.3 percent (two noisy rounds) | | |

Two other safe forms (a sliding span, a `Cast` loop with a single accumulator) were worse (up to +31 percent on the fixed `uint8[4096]`). Decision, with the guidance 10.5 rule that more than 3 percent on a gate fixture is a loss: **the padding window is bounds-checked** (within 1.5 percent on every fixture that uses it, so no unsafe load is justified there), **the two run kernels keep their unchecked loads** (every safe form measured loses 4 to 12 percent on `address[32]` and the fixed `uint8[4096]`, in all three rounds, with the same sign). The unchecked kernels are guarded by a safety argument in the source and by real guard-page over-read tests (`ABIWordRunGuardPageTests`, Linux; `docs/abi-design-notes.md`, row 66). The absolute cost of the safe form is 1 to 2 ns on a 27 ns decode and about 70 ns on a 920 ns one; on the request mix and on multicalls it is not measurable. This is a judgement the owner may reverse: switching the two kernels to the bounds-checked form costs the figures above (1 to 2 ns and 4 to 8 percent on those two fixtures, nothing measurable elsewhere) and removes the only unchecked reads in the codec.

**Reversed on 2026-11-19 (owner's decision, `docs/abi-design-notes.md` row 85): the run kernels now load bounds-checked slices.** Re-measured A/B on the gate fixtures, the two builds side by side: BenchmarkDotNet default job, in process, pinned to cores 4 and 5, three rounds with the order rotated, load average 1.5 to 3.1. Medians of the three round means, unchecked against checked: `address[32]` dynamic 28.2 against 29.1 ns (+3 percent), fixed 28.2 against 25.9 (-8); `uint8[32]` dynamic 28.0 against 29.0 (+4), fixed 28.2 against 27.8 (-1); `address[4096]` dynamic 1,353 against 1,407 (+4), fixed 1,335 against 1,409 (+6); `uint8[4096]` dynamic 1,317 against 1,449 (+10), fixed 1,342 against 1,430 (+7). Small arrays move by about a nanosecond either way; large static arrays pay 4 to 10 percent. Single rounds were bimodal on the 4,096-element rows (one round of each build measured 935 and 1,034 ns), so only the medians are compared.

Known costs: arrays of 1 or 2 narrow elements pay the entry test (+0 to +5 percent, one to two nanoseconds at most). The run path is tested against a verbatim copy of the old per-element validator (`Eccentricware.Web3.DataTypes.Test/ABI/ABILegacyValidator.cs`); the mix and array shapes above are benchmark classes of the optional benchmarks patch, not part of this change.

## Zero allocation

The evidence is `GC.GetAllocatedBytesForCurrentThread` deltas after warm-up in `ABIDecodeAllocationTests`, `ABIWriterAllocationTests` and `ABIHardeningAllocationTests` (decode with and without `ABIError`, call decode, every locator and accessor on success and failure, attacker-sized counts, zero-width budget exhaustion; every writer method on success; writer, measurer and call-encoder failure sequences for each leaf kind, with the statuses asserted for the writer failures; every fuzz input repeats the probe for all three encoders), and by the allocation probe of every fuzz input. The `Allocated` column above agrees (the one `1 B` is explained above). These depend on the JIT and tiering of .NET 10.0.15 on x64 Linux.

## What was not measured

- Full BenchmarkDotNet jobs: only the short job (3 iterations), so intervals are wide and several cells have an error larger than the mean; no confidence-interval-based comparison is possible.
- An accepted baseline and the spec 11.3 regression gate (more than 10 percent median on a stable fixture): none exists; the two-run spread above already exceeds it.
- p50/p95/p99 latency and throughput of a full signing-policy pipeline, Gen0/1/2 counts beyond the Nethereum rows, peak process memory, stack usage (validator frames are 8 or 32 frames of 32 bytes; writer scratch is 52 bytes per frame; neither was measured at process level).
- Concurrent throughput (the RLP suite has a stand-alone harness; no ABI equivalent was written).
- Other CPUs, ARM64, Windows, macOS, other runtime versions, `DOTNET_EnableAVX2=0`.
- Hex decoding and RLP parsing outside the one end-to-end row (spec 11.1 asks for them separately).
- Full-policy latency through the adapters with a realistic host (identity lookups against a state snapshot, a real policy): the adapter rows use a fixed-identity struct host.
- Spec 11.2 asks for nested tuple/array depth 1, 8 and 16. Only chains of dynamic arrays were measured, at 1, 8 and 14 levels, which are value depths 3, 10 and 16 (the default limit); nested tuples and mixed tuple/array chains were not measured, and depth 1 (a root tuple with no nesting) is the transfer row.
- Multiple dynamic arguments on the encode side beyond `multicall` and `(address,uint256,bytes)`, and selective field access on the mixed workload.
- The raw BenchmarkDotNet reports are not committed (`BenchmarkDotNet.Artifacts/` is git-ignored): the tables here were transcribed from the run directory and cannot be re-checked from the repository; re-run to reproduce. The run 3 reports were kept at `/var/tmp/bench-final/` on the author's machine only.

## Re-running

```
dotnet build EccentricWare.Web3.DataTypes.Benchmarks -c Release
dotnet EccentricWare.Web3.DataTypes.Benchmarks/bin/Release/net10.0/EccentricWare.Web3.DataTypes.Benchmarks.dll --inProcess --filter "*Abi*" --job short
```

Drop `--job short` for the default job (tighter intervals, much longer). Close other CPU-heavy work first, and do not compile while a job runs. See `EccentricWare.Web3.DataTypes.Benchmarks/README.md` for the baseline and comparison workflow.
