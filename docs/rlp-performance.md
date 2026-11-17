# RLP codec performance record

Measured with BenchmarkDotNet on one machine; every number below comes from the runs listed in "Method". Nothing here is a
hardware-independent target, and numbers from another machine, runtime or power plan are not comparable (spec 13.2: no
universal nanosecond targets without an observed baseline).

## Environment

| Item | Value |
| --- | --- |
| CPU (as reported by BenchmarkDotNet) | Intel Core Ultra 9 185H, 3.07 GHz nominal, 12 logical / 12 physical cores (BenchmarkDotNet's header also says "4 CPU": the guest sees 4 sockets of 3 cores) |
| Virtualisation | **VMware guest** (`hypervisor` flag in `/proc/cpuinfo`, DMI `VMware, Inc.` / `VMware Virtual Platform`). Every figure in this document, including the concurrency run, was taken inside the VM; the host's scheduling, core placement and frequency are unknown |
| Clock regime, power plan, core placement | Not controllable and not readable from the guest: `/sys/devices/system/cpu/cpu0/cpufreq` does not exist (no governor, scaling limits or turbo state), and `/proc/cpuinfo` reports a constant 3072.002 MHz on every CPU (the nominal figure, not a measurement). The processor model is a hybrid design (P and E cores) in hardware, but the guest exposes no hybrid topology (`cpu_core`/`cpu_atom` `cpus` files empty, `cpu_capacity` 1024 on all 12 CPUs), so P-core against E-core placement cannot be told from inside. Processes were not pinned (affinity mask 0xfff, all 12 CPUs); BenchmarkDotNet could not raise the process priority (permission denied) |
| OS | Ubuntu 26.04.1 LTS, Linux 7.0 |
| .NET SDK / runtime | SDK 10.0.115, runtime 10.0.15 (X64 RyuJIT x86-64-v3, AVX2) |
| Library / benchmarks | `net10.0`, Release, `TreatWarningsAsErrors`; benchmarks use BenchmarkDotNet 0.15.8 |
| GC | Workstation, non-concurrent (benchmark project settings) |
| Tiered PGO | On (the .NET 10 default; the benchmark project also sets `TieredPGO=true`). JIT listings of the measured methods show "optimized using Dynamic PGO". Tiered compilation is at its default (on); the concurrency harness read `System.Runtime.TieredPGO=true` from the runtime configuration and found no `DOTNET_TieredPGO`, `DOTNET_TieredCompilation` or `DOTNET_TC_*` variable set (the only `DOTNET_*` variable was `DOTNET_BUNDLE_EXTRACT_BASE_DIR`) |
| Job | BenchmarkDotNet `DefaultJob`: out-of-process toolchain, one launch, `[MemoryDiagnoser]` on every class. Actual parameters, from the per-iteration data of the saved JSON files: a pilot stage of 12 to 26 iterations (13 to 24 in the second measurement) that picks the invocation count, 6 to 12 warm-up iterations (median 7), then 15 measured iterations per row (up to 100 on some noisy rows), each measured iteration 0.46 to 1.1 s of work (65,536 to 1,073,741,824 invocations). Source: `rlp-after/results/*-report-full-compressed.json` and `rlp-after-signature-fix/results/*-report-full-compressed.json` |
| Memory architecture | One NUMA node as seen by the guest (`/sys/devices/system/node/node0`), 31,765,028 kB RAM (`MemTotal`), 31,020 MiB visible to the GC. L1d 576 KiB, L2 24 MiB and L3 96 MiB are the hypervisor's reported totals (`lscpu`), not verified topology. The host's NUMA layout is unknown |

## Method

1. Suite: 130 benchmark cases (158 since the second measurement below) in `EccentricWare.Web3.DataTypes.Benchmarks/Rlp*.cs` (spec 13.1 rows a to h; see the README). Inputs are
   deterministic (`BenchmarkData.Seed`). Every `[GlobalSetup]` decodes or encodes its input and compares it with an independent
   reference (a small allocating recursive encoder in `RlpBenchmarkData.cs` that shares no code with the library, plus the EIP-155
   example transaction), so a wrong benchmark throws instead of timing a no-op.
2. Baseline: unchanged production code, full default job over `*Rlp*` (125 cases, 47 min), saved to
   `BenchmarkDotNet.Artifacts/rlp-baseline/`. Afterwards five rows were added and one was fixed (a constant-folded `TryGetListLength`
   row; the 300-byte and 70,000-byte string rows; the large-workspace index row). Those classes (`RlpDecodeStringBenchmarks`,
   `RlpEncoderListBenchmarks`, `RlpIndexBenchmarks`) were re-run on the still-unchanged production code into
   `rlp-baseline-supplement/`, and their baseline figures below come from that run.
3. Each candidate change was built, run against the relevant classes (`--job short` to look, the full default job to decide) and
   kept only when the gain was clear in a hot path, the Rlp tests still passed and the zero-allocation rows stayed at 0 B.
   Otherwise the file was restored from a saved copy and verified with `diff -r`.
4. After: full default job over `*Rlp*` (130 cases, 47 min) on the final code, saved to `BenchmarkDotNet.Artifacts/rlp-after/`.
   The Mean column is BenchmarkDotNet's mean. `±` is a confidence half-width derived from the per-iteration statistics; an independent
   review found it is about 0.6 times BenchmarkDotNet's printed `Error` column (the 99.9% margin, for example 0.0701 ns against the 0.04 shown
   for `Decode_String[empty]`), so it is narrower than a 99.9% interval. Read the `Error` column in the saved CSV files when a delta is small.

Rows that use a `ref struct` accessor (`TryGetUInt64`, `TryGetUInt256`, `TryGetAddress`, `TryCopyEncodedTo`, the typed writers,
`TryIndexDirectChildren`) cannot keep a `RLPData` in a field, so each invocation decodes (or opens a writer) once and repeats the
accessor 64 or 1,024 times: the figure is per repetition with the decode amortised. Enumeration rows are per child (64 children per
invocation) **and include the full-document decode of that 64-child list**: `Decode_WideFlatList[64]` is 122.46 ns, about 1.9 ns per child,
so more than half of `Enumerate_Children[short4]` (3.43 ns) is validation, and a bare `MoveNext` is about 1.5 to 1.6 ns
(`Scan_EnumeratorToOrdinal[last]`, 19.2 ns for 12 children). `TryGetListLength` is per size computation.

Limits of these figures: every case runs in its own process with one monomorphic input under dynamic PGO, so the numbers are best-case,
branch-profile-specialised timings; `Decode_String[len55]` (6.64 ns) is slower than `[len56]` (3.39 ns) although `len56` takes the longer
header path, and the cause was not isolated. They do not transfer to mixed workloads. Each case was launched once on an unpinned hybrid CPU,
so deltas under about 10% are not evidence of a change. The writer rows (`Writer_*`, `Ours_Encode_*`) pass a precomputed constant to
`TryBeginList`: the cost of summing field sizes, which every real caller pays, is outside the timed region, while the Nethereum encoder does
its equivalent sizing inside the timed call. The `*_WithSizing` rows of the second measurement time that sizing (see below).

### How much do untouched rows move?

Rows whose code path was not edited are the noise control. Between the baseline and the after run they moved by: `Hash_Only_Legacy`
+1%, `Indexed_TryGetAt` -3% to +2%, `Scan_EnumeratorToOrdinal` -1% to +2%, `Index_Build` -1% to -3%, the `Nethereum_*` rows -8% to 0%
with one outlier (`Nethereum_Encode_Legacy` 188.7 ns to 122.6 ns, -35%, no code change), `TryCopyEncodedTo[1027]` +12%, and a few
sub-3 ns rows by up to +17% (`TryWriteUnsignedMagnitude[0]`). Treat any single row's delta under about 10% as noise, and rows
under 1 ns (`TryWriteUInt64` for 0, 1, 127; `TryGetByteStringLength`; `Literal_TryWriteByteString`) as at the measurement floor.

## Baseline against after, headline rows

Allocated is bytes per operation, baseline -> after.

| Benchmark | Baseline (mean ± half-width, see Method) | After | Delta | Allocated B/op |
| --- | ---: | ---: | ---: | ---: |
| `RlpDecodeStringBenchmarks.Decode_String[empty]` | 13.76 ns ± 0.10 | 6.63 ns ± 0.04 | -52% | 0 -> 0 |
| `RlpDecodeStringBenchmarks.Decode_String[literal]` | 13.60 ns ± 0.08 | 6.60 ns ± 0.05 | -51% | 0 -> 0 |
| `RlpDecodeStringBenchmarks.Decode_String[len55]` | 14.35 ns ± 0.13 | 6.64 ns ± 0.02 | -54% | 0 -> 0 |
| `RlpDecodeStringBenchmarks.Decode_String[len56]` | 11.45 ns ± 0.05 | 3.39 ns ± 0.04 | -70% | 0 -> 0 |
| `RlpDecodeStringBenchmarks.Decode_String[len300]` | 19.03 ns ± 0.22 | 3.45 ns ± 0.02 | -82% | 0 -> 0 |
| `RlpDecodeStringBenchmarks.Decode_String[len70000]` | 13.87 ns ± 0.07 | 3.59 ns ± 0.02 | -74% | 0 -> 0 |
| `RlpDecodeBenchmarks.Decode_LegacyTransaction` | 23.80 ns ± 0.09 | 22.23 ns ± 0.19 | -7% | 0 -> 0 |
| `RlpDecodeBenchmarks.Decode_Eip1559WithAccessList` | 50.93 ns ± 0.18 | 46.36 ns ± 0.37 | -9% | 0 -> 0 |
| `RlpDecodeNestedBenchmarks.Decode_NestedLists[1]` | 14.87 ns ± 0.20 | 9.47 ns ± 0.08 | -36% | 0 -> 0 |
| `RlpDecodeNestedBenchmarks.Decode_NestedLists[4]` | 18.38 ns ± 0.20 | 15.23 ns ± 0.10 | -17% | 0 -> 0 |
| `RlpDecodeNestedBenchmarks.Decode_NestedLists[16]` | 58.91 ns ± 0.75 | 56.50 ns ± 0.30 | -4% | 0 -> 0 |
| `RlpDecodeNestedBenchmarks.Decode_NestedLists[32]` | 96.69 ns ± 0.76 | 95.92 ns ± 0.68 | -1% | 0 -> 0 |
| `RlpDecodeWideBenchmarks.Decode_WideFlatList[1]` | 14.22 ns ± 0.20 | 10.43 ns ± 0.12 | -27% | 0 -> 0 |
| `RlpDecodeWideBenchmarks.Decode_WideFlatList[9]` | 32.75 ns ± 0.19 | 30.04 ns ± 0.40 | -8% | 0 -> 0 |
| `RlpDecodeWideBenchmarks.Decode_WideFlatList[64]` | 120.71 ns ± 1.23 | 122.46 ns ± 0.71 | +1% | 0 -> 0 |
| `RlpDecodeWideBenchmarks.Decode_WideFlatList[1024]` | 1.66 us ± 0.02 | 1.73 us ± 0.02 | +4% | 0 -> 0 |
| `RlpDecodeBenchmarks.TryGetUInt64_TwoBytes` | 1.94 ns ± 0.01 | 1.45 ns ± 0.01 | -25% | 0 -> 0 |
| `RlpDecodeBenchmarks.TryGetUInt64_EightBytes` | 1.49 ns ± 0.01 | 1.12 ns ± 0.00 | -25% | 0 -> 0 |
| `RlpDecodeCopyBenchmarks.TryCopyEncodedTo_Repeated[180]` | 3.92 ns ± 0.05 | 3.70 ns ± 0.01 | -6% | 0 -> 0 |
| `RlpEnumerationBenchmarks.Enumerate_Children[literal]` | 2.26 ns ± 0.02 | 2.17 ns ± 0.01 | -4% | 0 -> 0 |
| `RlpEnumerationBenchmarks.Enumerate_Children[short4]` | 3.60 ns ± 0.01 | 3.43 ns ± 0.02 | -5% | 0 -> 0 |
| `RlpEnumerationBenchmarks.Enumerate_Children[hash32]` | 3.67 ns ± 0.03 | 3.58 ns ± 0.02 | -3% | 0 -> 0 |
| `RlpEnumerationBenchmarks.Enumerate_Children[long300]` | 9.58 ns ± 0.08 | 6.21 ns ± 0.02 | -35% | 0 -> 0 |
| `RlpEnumerationBenchmarks.Enumerate_Children[mixed]` | 4.44 ns ± 0.05 | 3.82 ns ± 0.04 | -14% | 0 -> 0 |
| `RlpEnumerationWalkBenchmarks.Walk_AllItems[legacy9]` | 103.64 ns ± 1.39 | 95.38 ns ± 1.08 | -8% | 0 -> 0 |
| `RlpEnumerationWalkBenchmarks.Walk_AllItems[eip1559]` | 118.77 ns ± 0.86 | 117.80 ns ± 0.73 | -1% | 0 -> 0 |
| `RlpIndexBenchmarks.Index_Build[9]` | 26.64 ns ± 0.16 | 26.37 ns ± 0.14 | -1% | 0 -> 0 |
| `RlpIndexBenchmarks.Index_Build[12]` | 32.89 ns ± 0.17 | 32.23 ns ± 0.39 | -2% | 0 -> 0 |
| `RlpIndexBenchmarks.Index_Build[16]` | 43.29 ns ± 0.41 | 41.89 ns ± 0.36 | -3% | 0 -> 0 |
| `RlpIndexBenchmarks.Index_CountPassOnly[12]` | 16.05 ns ± 0.08 | 14.67 ns ± 0.04 | -9% | 0 -> 0 |
| `RlpIndexLookupBenchmarks.Indexed_TryGetAt[last]` | 4.14 ns ± 0.02 | 4.22 ns ± 0.04 | +2% | 0 -> 0 |
| `RlpIndexLookupBenchmarks.Scan_EnumeratorToOrdinal[last]` | 18.78 ns ± 0.09 | 19.21 ns ± 0.23 | +2% | 0 -> 0 |
| `RlpEncoderStringBenchmarks.TryWriteByteString[55]` | 2.92 ns ± 0.03 | 2.86 ns ± 0.05 | -2% | 0 -> 0 |
| `RlpEncoderStringBenchmarks.TryWriteByteString[56]` | 3.96 ns ± 0.04 | 3.68 ns ± 0.03 | -7% | 0 -> 0 |
| `RlpEncoderStringBenchmarks.TryWriteByteString[256]` | 5.42 ns ± 0.04 | 5.52 ns ± 0.08 | +2% | 0 -> 0 |
| `RlpEncoderUInt64Benchmarks.TryWriteUInt64[128]` | 1.01 ns ± 0.03 | 0.930 ns ± 0.020 | -8% | 0 -> 0 |
| `RlpEncoderUInt64Benchmarks.TryWriteUInt64[18446744073709551615]` | 2.87 ns ± 0.04 | 2.93 ns ± 0.04 | +2% | 0 -> 0 |
| `RlpEncoderListBenchmarks.Writer_LegacyTransaction` | 56.70 ns ± 0.35 | 49.34 ns ± 0.52 | -13% | 0 -> 0 |
| `RlpEncoderListBenchmarks.Writer_Eip1559WithAccessList` | 163.30 ns ± 0.80 | 119.16 ns ± 1.27 | -27% | 0 -> 0 |
| `RlpEncoderListBenchmarks.Writer_NestedLists32` | 164.05 ns ± 0.65 | 167.58 ns ± 1.55 | +2% | 0 -> 0 |
| `RlpHostileInputBenchmarks.Decode_Hostile[LengthOverflow4Bytes]` | 12.49 ns ± 0.14 | 4.15 ns ± 0.02 | -67% | 0 -> 0 |
| `RlpHostileInputBenchmarks.Decode_Hostile[NonCa(...)eByte [22]]` | 10.85 ns ± 0.06 | 3.84 ns ± 0.03 | -65% | 0 -> 0 |
| `RlpHostileInputBenchmarks.Decode_Hostile[TooLargeInput]` | 7.90 ns ± 0.06 | 2.47 ns ± 0.04 | -69% | 0 -> 0 |
| `RlpHostileInputBenchmarks.Decode_Hostile[DepthAtLimit]` | 95.44 ns ± 0.69 | 96.74 ns ± 0.92 | +1% | 0 -> 0 |
| `RlpHostileInputBenchmarks.Decode_Hostile[DepthBomb]` | 77.29 ns ± 1.12 | 70.94 ns ± 0.64 | -8% | 0 -> 0 |
| `RlpHostileInputBenchmarks.Decode_Hostile[ItemsAtLimit]` | 8.70 us ± 0.04 | 9.46 us ± 0.08 | +9% | 0 -> 0 |
| `RlpHostileInputBenchmarks.Decode_Hostile[NonCa(...)f1024 [22]]` | 1.71 us ± 0.02 | 1.62 us ± 0.01 | -6% | 0 -> 0 |
| `RlpTypedAdapterReadBenchmarks.TryGetUInt256_Small` | 4.69 ns ± 0.03 | 2.07 ns ± 0.02 | -56% | 0 -> 0 |
| `RlpTypedAdapterReadBenchmarks.TryGetUInt256_Bits64` | 4.28 ns ± 0.04 | 1.63 ns ± 0.02 | -62% | 0 -> 0 |
| `RlpTypedAdapterReadBenchmarks.TryGetUInt256_FullWidth` | 2.03 ns ± 0.01 | 1.96 ns ± 0.01 | -4% | 0 -> 0 |
| `RlpTypedAdapterReadBenchmarks.TryGetAddress` | 1.58 ns ± 0.02 | 1.43 ns ± 0.01 | -10% | 0 -> 0 |
| `RlpTypedAdapterReadBenchmarks.TryGetHash32` | 1.84 ns ± 0.02 | 1.63 ns ± 0.01 | -11% | 0 -> 0 |
| `RlpTypedAdapterReadBenchmarks.TryGetFunctionSelector` | 0.762 ns ± 0.005 | 0.703 ns ± 0.003 | -8% | 0 -> 0 |
| `RlpTypedAdapterWriteBenchmarks.TryWriteUInt256_Small` | 14.62 ns ± 0.09 | 4.88 ns ± 0.03 | -67% | 0 -> 0 |
| `RlpTypedAdapterWriteBenchmarks.TryWriteUInt256_Bits64` | 14.02 ns ± 0.05 | 4.39 ns ± 0.04 | -69% | 0 -> 0 |
| `RlpTypedAdapterWriteBenchmarks.TryWriteUInt256_FullWidth` | 13.87 ns ± 0.09 | 6.68 ns ± 0.04 | -52% | 0 -> 0 |
| `RlpTypedAdapterWriteBenchmarks.TryWriteAddress` | 6.46 ns ± 0.03 | 4.25 ns ± 0.03 | -34% | 0 -> 0 |
| `RlpTypedAdapterWriteBenchmarks.TryWriteHash32` | 7.11 ns ± 0.05 | 3.64 ns ± 0.03 | -49% | 0 -> 0 |
| `RlpEndToEndBenchmarks.Legacy_Index_ReadAllTyped_Keccak` | 630.89 ns ± 1.94 | 625.12 ns ± 4.61 | -1% | 0 -> 0 |
| `RlpEndToEndBenchmarks.Legacy_Enumerate_ReadAllTyped_Keccak` | 612.95 ns ± 5.53 | 583.67 ns ± 6.41 | -5% | 0 -> 0 |
| `RlpEndToEndBenchmarks.Eip1559_Enumerate_ReadAllTyped_AccessList_Keccak` | 1.05 us ± 0.01 | 1.00 us ± 0.00 | -5% | 0 -> 0 |
| `RlpEndToEndBenchmarks.Hash_Only_Legacy` | 516.81 ns ± 4.50 | 522.23 ns ± 5.04 | +1% | 0 -> 0 |

## Second measurement: EVM signature APIs, sizing helpers, limits snapshot

Run after the typed adapters gained `TryGetEvmYParitySignature`, `TryGetEvmLegacySignature` (chain-id aware), `TryWriteEvmYParity`, `TryWriteEvmLegacyV`
and the public sizing helpers, and after `RLPData.TryDecodeComplete` started copying `RLPDecodeLimits` once per call (a later change replaced the copy with single reads in `RLPValidator.TryValidate`; see the follow-up in the regression section). Same machine, SDK, runtime, default
job and out-of-process toolchain as the `rlp-after` run, 2026-11-17.

* **Run 1**, `BenchmarkDotNet.Artifacts/rlp-after-signature-fix/`: 86 cases (34 minutes): the new rows, the changed classes (`RlpEncoderListBenchmarks`,
  `RlpTypedAdapterReadBenchmarks`, `RlpTypedAdapterWriteBenchmarks`, `RlpComparisonBenchmarks`) and the regression classes (`RlpDecodeStringBenchmarks`,
  `RlpDecodeBenchmarks`, `RlpDecodeNestedBenchmarks`, `RlpHostileInputBenchmarks`).
* **Run 2**, `.../rerun-1/`: the encoder, string, decode, nested and hostile classes again (34 cases), after the benchmark fixture was changed (see below).
* **Run 3**, `.../ab-current-3/`: strings, `Decode_*` rows and nested lists a third time on the unchanged library.
* **Run 4**, `.../rerun-2-typed-write/`: `RlpTypedAdapterWriteBenchmarks` again.
* **A/B**, `.../ab-nosnapshot-1/` and `.../ab-nosnapshot-2/`: the same classes as run 3 on a private copy of the repository (in the scratchpad, not the working
  tree) in which the one-line limits copy in `RLPData.TryDecodeComplete` was removed. No production file in the repository was edited.

Tables below give BenchmarkDotNet's mean and its `Error` column (the 99.9% half-width) for the new rows, because the `±` convention of the earlier tables is narrower.
All library rows in these runs reported 0 B/op; the eight `Nethereum_*` rows allocate as before.

### New API rows (run 1; no earlier figure exists)

Each row decodes (or opens a writer) once and repeats the call 64 times, so the figure is per call with that set-up amortised; for the signature readers that
includes the decode of a three-field list and three child lookups, about 0.4 ns per call. `[GlobalSetup]` of each class compares the result with the fixture
(signature `r`, `s`, `V` = parity, returned chain id) or the writer output with the independent reference encoder. Wire `v` of the cases: 27 (no chain id, parity 0),
37 (chain 1, parity 0), 310 (chain 137, parity 1), 197,768 (chain 98866, parity 1) and 2^65 + 34 (chain id `ulong.MaxValue`, parity 1, 9 bytes).

| Benchmark | Mean | Error (99.9%) | B/op |
| --- | ---: | ---: | ---: |
| `RlpEvmLegacySignatureReadBenchmarks.TryGetEvmLegacySignature[NoChainId_v27]` | 12.11 ns | 0.17 | 0 |
| `...TryGetEvmLegacySignature[Chain1_v37]` | 12.51 ns | 0.25 | 0 |
| `...TryGetEvmLegacySignature[Chain137_v310]` | 13.07 ns | 0.25 | 0 |
| `...TryGetEvmLegacySignature[Chain98866_v197768]` | 13.32 ns | 0.17 | 0 |
| `...TryGetEvmLegacySignature[ChainIdMax_9ByteV]` | 15.61 ns | 0.21 | 0 |
| `RlpTypedAdapterReadBenchmarks.TryGetEvmYParitySignature` | 9.67 ns | 0.08 | 0 |
| `RlpEvmLegacyVWriteBenchmarks.TryWriteEvmLegacyV[NoChainId_v27]` | 5.39 ns | 0.08 | 0 |
| `...TryWriteEvmLegacyV[Chain1_v37]` | 14.73 ns | 0.21 | 0 |
| `...TryWriteEvmLegacyV[Chain137_v310]` | 16.78 ns | 0.21 | 0 |
| `...TryWriteEvmLegacyV[Chain98866_v197768]` | 17.01 ns | 0.34 | 0 |
| `...TryWriteEvmLegacyV[ChainIdMax_9ByteV]` | 16.96 ns | 0.21 | 0 |
| `RlpTypedAdapterWriteBenchmarks.TryWriteEvmYParity` (parity 1) | 1.67 ns | 0.02 | 0 |
| `RlpSizingBenchmarks.GetUInt64EncodedLength` zero / literal / 2 bytes / 8 bytes | 0.45 / 0.45 / 0.81 / 0.79 ns | 0.006 to 0.016 | 0 |
| `RlpSizingBenchmarks.GetUInt256EncodedLength` 2 bytes / 8 bytes / 32 bytes | 1.22 / 1.20 / 1.01 ns | 0.017 to 0.024 | 0 |
| `RlpSizingBenchmarks.TryGetUnsignedMagnitudeLength` 2 bytes / 32 bytes | 0.66 / 0.63 ns | 0.011 to 0.013 | 0 |
| `RlpSizingBenchmarks.TryGetEvmLegacyVEncodedLength` no chain id / chain 1 / chain 98866 / `ulong.MaxValue` | 0.95 / 1.20 / 1.64 / 1.52 ns | 0.007 to 0.031 | 0 |

Reading them (measured facts, then labelled hypotheses):

* The sizing helpers cost 0.45 to 1.6 ns per call. Each sizing row loops over 64 distinct values, so the figure includes an array load and loop overhead of roughly a cycle
  or two; rows under about 1 ns are at the measurement floor.
* The signature readers cost 9.7 to 15.6 ns, several times the 2 ns of one `TryGetUInt256`. Each builds `Signature` through two stack buffers and `Signature.FromEvmComponents`
  after two integer reads. The legacy reader adds a `UInt128` accumulation over the `v` bytes (12.1 ns for 1 byte, 15.6 ns for 9 bytes). Where the remaining time goes was not
  isolated.
* `TryWriteEvmLegacyV` is 5.4 ns with no chain id and 14.7 to 17.0 ns with any chain id, although chain 1 (`v` = 37) and no chain id (`v` = 27) both write a single literal byte.
  Hypothesis, not tested: the no-chain row benefits from dynamic PGO pruning the never-taken `UInt128` shift/add branch (the process runs one case), while the chain rows pay for the
  128-bit arithmetic, a 16-byte stack buffer and `WriteUInt128BigEndian` on top of the public magnitude writer. For comparison a plain `TryWriteUInt64` of a small value is about 1 ns and
  `TryWriteEvmYParity` 1.7 ns. A caller on the hot path would pay about 15 ns per legacy signature for the `v` alone; a `ulong` fast path for chain ids that keep `v` within 64 bits is the
  obvious candidate if that ever matters (not implemented, not measured; this work does not change production code).

### What list-length sizing adds to encoding (review finding M1)

The `Writer_*` and `Ours_Encode_*` rows pass a constant to `TryBeginList`. The `_WithSizing` rows run the same writes after first computing every list length from the field
values with `RLPEncoder.GetUInt64EncodedLength`, `RLPTypedExtensions.GetUInt256EncodedLength`, `AddressEncodedLength`, `Hash32EncodedLength`, `TryGetByteStringLength` and
`TryGetListLength` (the EIP-1559 row also sizes each access-list entry and its key list). `[GlobalSetup]` checks that both variants reproduce the fixture bytes. Figures are
run 2, in which the constant rows are the original, unchanged write code (45 ns rows are back at their `rlp-after` value):

| Row (run 2) | Constant length | With sizing | Sizing adds |
| --- | ---: | ---: | ---: |
| `RlpEncoderListBenchmarks` legacy transaction (9 fields) | 44.86 ns ± 0.67 | 49.95 ns ± 0.77 | +5.1 ns (+11%) |
| `RlpComparisonBenchmarks` legacy transaction | 45.22 ns ± 0.64 | 50.76 ns ± 0.93 | +5.5 ns (+12%) |
| `RlpEncoderListBenchmarks` EIP-1559 payload with access list (12 fields) | 110.01 ns ± 2.11 | 136.50 ns ± 1.47 | +26.5 ns (+24%) |

(± is BenchmarkDotNet's `Error` here.) Sizing is therefore 11% to 24% of a framed encode, which the constant-length rows hide; the legacy row makes 8 helper calls and the EIP-1559 row 15 (plus the per-entry loop), which is the same order as the 0.5 to 1.6 ns per call
measured above. The two variants have separate copies of the write sequence, so a few percent of the difference can be code layout; no repeated-run
spread was measured for the difference itself. The first run of these rows (run 1, `rlp-after-signature-fix/`) shared one write method between the constant and sized variants and measured the
constant rows at 80.98 ns (`Writer_LegacyTransaction`, `Error` 11.5, unstable) and 55.61 ns (`Ours_Encode_Legacy`, +22%); that was an artefact of the shared method (the rows
returned to 44.86 and 45.22 ns when the original code was restored), so those two run-1 figures are not used. No Nethereum "with sizing" row was added: `Nethereum_Encode_Legacy`
already sizes inside its timed call (it takes field bytes that were trimmed to minimal magnitudes in setup, so its typed-value conversion is untimed). In run 2 it measured 120.68 ns
(776 B), against 122.64 ns in the `rlp-after` run and 124.29 ns in run 1.

### Regression check for the limits snapshot (first a copy in `RLPData.TryDecodeComplete`, later single reads in `RLPValidator.TryValidate`)

Rows of `RlpDecodeStringBenchmarks`, `RlpDecodeBenchmarks`, `RlpDecodeNestedBenchmarks` and `RlpHostileInputBenchmarks`, mean in ns, `rlp-after` against up to three runs on the
current library, and against two runs with the copy removed (A/B on the private copy). "Mean of runs" averages the runs on the current library; `-` means the row was not
re-run in that run (the hostile rows ran twice, the two `TryGetUInt64` rows once).

| Row | rlp-after | run 1 | run 2 | run 3 | mean of runs | Delta (mean of runs vs rlp-after) | no snapshot (A1, A2) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `RlpDecode.Decode_Eip1559WithAccessList` | 46.36 | 48.91 | 47.33 | 45.73 | 47.33 | +2.1% | 45.58 / 45.81 |
| `RlpDecode.Decode_LegacyTransaction` | 22.23 | 22.54 | 23.02 | 22.14 | 22.57 | +1.5% | 21.36 / 21.70 |
| `RlpDecode.Decode_UInt64Max` | 6.65 | 7.59 | 7.21 | 7.00 | 7.27 | +9.2% | 6.86 / 6.43 |
| `RlpDecode.TryGetUInt64_EightBytes` | 1.12 | 1.15 | - | - | 1.15 | +2.8% | - |
| `RlpDecode.TryGetUInt64_TwoBytes` | 1.45 | 1.51 | - | - | 1.51 | +4.4% | - |
| `RlpDecodeNested.Decode_NestedLists [1]` | 9.47 | 10.05 | 10.00 | 9.58 | 9.88 | +4.4% | 9.43 / 9.18 |
| `RlpDecodeNested.Decode_NestedLists [16]` | 56.50 | 57.53 | 53.24 | 53.15 | 54.64 | -3.3% | 51.58 / 51.79 |
| `RlpDecodeNested.Decode_NestedLists [32]` | 95.92 | 95.80 | 95.53 | 98.44 | 96.59 | +0.7% | 92.06 / 96.79 |
| `RlpDecodeNested.Decode_NestedLists [4]` | 15.23 | 16.88 | 15.37 | 15.25 | 15.83 | +4.0% | 15.62 / 15.07 |
| `RlpDecodeString.Decode_String [empty]` | 6.63 | 7.10 | 7.02 | 7.01 | 7.04 | +6.3% | 6.80 / 6.38 |
| `RlpDecodeString.Decode_String [len300]` | 3.45 | 3.71 | 3.70 | 3.65 | 3.69 | +7.0% | 3.52 / 3.46 |
| `RlpDecodeString.Decode_String [len55]` | 6.64 | 7.46 | 7.18 | 6.96 | 7.20 | +8.3% | 6.73 / 6.34 |
| `RlpDecodeString.Decode_String [len56]` | 3.39 | 3.49 | 3.34 | 3.35 | 3.39 | +0.2% | 3.29 / 3.43 |
| `RlpDecodeString.Decode_String [len70000]` | 3.59 | 3.94 | 3.72 | 3.72 | 3.79 | +5.5% | 3.55 / 3.68 |
| `RlpDecodeString.Decode_String [literal]` | 6.60 | 7.41 | 7.27 | 6.77 | 7.15 | +8.3% | 6.35 / 6.35 |
| `RlpHostileInput.Decode_Hostile [DepthAtLimit]` | 96.74 | 102.21 | 94.97 | - | 98.59 | +1.9% | - |
| `RlpHostileInput.Decode_Hostile [DepthBomb]` | 70.94 | 74.67 | 69.54 | - | 72.11 | +1.6% | - |
| `RlpHostileInput.Decode_Hostile [ItemsAtLimit]` | 9,463 | 9613.50 | 9199.54 | - | 9,407 | -0.6% | - |
| `RlpHostileInput.Decode_Hostile [ItemsOverLimit]` | 8,712 | 8954.92 | 8471.83 | - | 8,713 | +0.0% | - |
| `RlpHostileInput.Decode_Hostile [Lengt(...)f_ff8 [21]]` | 4.55 | 5.10 | 4.58 | - | 4.84 | +6.3% | - |
| `RlpHostileInput.Decode_Hostile [LengthOverflow4Bytes]` | 4.15 | 4.49 | 4.24 | - | 4.37 | +5.2% | - |
| `RlpHostileInput.Decode_Hostile [NonCa(...)eByte [22]]` | 3.84 | 3.77 | 3.42 | - | 3.60 | -6.2% | - |
| `RlpHostileInput.Decode_Hostile [NonCa(...)ength [22]]` | 3.80 | 4.11 | 3.92 | - | 4.01 | +5.6% | - |
| `RlpHostileInput.Decode_Hostile [NonCa(...)f1024 [22]]` | 1,617 | 1695.48 | 1619.12 | - | 1,657 | +2.5% | - |
| `RlpHostileInput.Decode_Hostile [TooLargeInput]` | 2.47 | 2.47 | 2.75 | - | 2.61 | +5.5% | - |
| `RlpHostileInput.Decode_Hostile [TruncatedChild]` | 11.02 | 12.13 | 10.47 | - | 11.30 | +2.6% | - |
| `RlpHostileInput.Decode_Hostile [TruncatedLongList]` | 4.45 | 4.76 | 4.29 | - | 4.52 | +1.6% | - |

Result:

* No row moved more than 10% when the runs on the current library are averaged. In a single run 7 rows were at or above the line: `Decode_UInt64Max` +14.0%, string `literal` and `len55`
  +12.3%, nested depth 4 +10.8%, hostile `bf ff*8` +12.0%, hostile `TruncatedChild` +10.1%, and `TryWriteUInt256_FullWidth` +11.6% (a writer row, not touched by the snapshot; see below).
  Re-run, the first six came back to between -5.0% and +10.1% (only `literal` reached +10.1% once) and average, over the runs, between +2.6% and +9.2%.
* The cheapest scalar documents show a small, consistent increase: empty string, literal, 55-byte string and `88 ff*8` read 6.6 to 6.7 ns in `rlp-after`, 7.0 to 7.3 ns on the current library
  (every one of the 2 to 3 runs is above `rlp-after`), and 6.3 to 6.9 ns with the copy removed. The difference between the mean of the current runs and the mean of the two no-snapshot runs
  is +0.45 ns (empty, +6.9%), +0.80 ns (literal, +12.6%), +0.66 ns (len55, +10.1%) and +0.62 ns (`88 ff*8`, +9.3%), close to the cost of storing and reloading a 12-byte struct. The 3.4 to 3.8 ns
  long-form rows differ by +0.03 to +0.20 ns, the legacy transaction by +1.04 ns (+4.8%), the EIP-1559 payload by +1.63 ns (+3.6%) and the four nested-list rows by +0.49 to +2.96 ns (+2.3% to +6.2%).
  The hostile rows were not part of the A/B.
* Assessment: the snapshot costs roughly half a nanosecond per decode, visible only on documents that decode in under 10 ns, and it is not a regression by the 10% rule. Individual deltas are near the
  noise (the same code varied by 9.4% on `literal` and 8.3% on `Decode_UInt64Max` between runs 1 and 3), so the evidence is the direction: the mean of the current runs is above the no-snapshot
  run on 13 of 13 rows against A/B run 1, on 11 of 13 against A/B run 2, and above both on 11 of 13. If that half nanosecond ever matters, the smallest fix is to move the snapshot
  into `RLPValidator.TryValidate`, which already copies `MaxListDepth` into a local (`maxDepth`) before it picks the frame bucket: read `MaxItems` there too and pass the two integers
  by value to `ValidateShallow`/`ValidateMedium`/`ValidateDeep`/`ValidateChildren` instead of `in limits`; then `TryDecodeComplete` needs no copy and the bucket and the depth check still
  come from the same read.
* Follow-up, made and measured afterwards (`BenchmarkDotNet.Artifacts/rlp-after-single-read-limits/`, same default job, same classes, one run): the production code now does exactly that.
  `RLPData.TryDecodeComplete` passes `in limits` with no copy, and `RLPValidator.TryValidate` reads `MaxEncodedBytes`, `MaxListDepth` and `MaxItems` once into locals and passes the two
  integers by value to the inner methods. It did **not** recover the cost. Against the copy version the mean over 13 rows is +1.3% (within noise); against the mean of the two
  no-snapshot runs it is still +4.8%, with the cheapest scalar rows at +7.4% (`empty`), +11.2% (`literal`), +15.4% (`len55`) and +13.1% (`Decode_UInt64Max`), the legacy transaction at +2.8%
  and the EIP-1559 payload at +1.7%. Single-run deltas of that size are inside the observed run-to-run spread (up to 9.4%), so the honest reading is: a cost of about half a nanosecond on
  the cheapest scalar decodes remains, it is not the struct copy, and it was not isolated further (reading three fields instead of one and the extra validity logic are candidates;
  untested). Decision recorded: the single-read form is kept because it preserves the guarantee that a racing writer of a shared limits variable cannot make a decode throw, at about
  half a nanosecond on documents that decode in under 10 ns. If that guarantee is not wanted, reverting `TryValidate` to read the limits fields where they are used (the code as of `rlp-after`)
  restores the earlier timings; the guarantee is the owner's call.

### Noise in this measurement

Control rows (code untouched, same run): the eight `Nethereum_*` rows were +1.3% to +11.2% against the `rlp-after` run in run 1 (mean +6.3%, two rows above 10%), so the machine state
drifted upward by about 6% between the two sessions, which is the order of the small systematic differences above. Run-to-run spread (largest over smallest) of the same code across the three current-library runs of the 13 re-run rows:
1.3% to 10.7% on the rows under 20 ns (9.4% on `literal`, 8.3% on `Decode_UInt64Max`), 3.0% to 8.2% on the 22 to 96 ns rows. Rows near the 10% line were run again (the table above); `TryWriteUInt256_FullWidth`
was +11.6% in run 1 and +7.9% in run 4 against `rlp-after` (7.46 and 7.21 against 6.68 ns) while the other four writer rows stayed within -6% to +1%; its code was not touched by the
new work as far as I can tell, so this is either code layout or a real change that this comparison cannot attribute (the library had other changes between `rlp-after` and now, which
are not versioned here). The run is on a VMware guest with unpinned threads, so these bounds describe this session only.

## Asymptotic evidence (spec 13.2)

From the `rlp-after` results (`rlp-after/results/*RlpDecodeWideBenchmarks-report.csv` and `...RlpDecodeNestedBenchmarks-report.csv`), mean ns per full strict decode:

| Flat list, items | Decode | ns per item (mean / n) | Marginal ns per item since the previous size |
| ---: | ---: | ---: | ---: |
| 1 | 10.43 | 10.43 | - |
| 9 | 30.04 | 3.34 | 2.45 |
| 64 | 122.46 | 1.91 | 1.68 |
| 1,024 | 1,728.63 | 1.69 | 1.67 |

| Nested lists, levels | Decode | ns per level (mean / depth) | Marginal ns per level since the previous depth |
| ---: | ---: | ---: | ---: |
| 1 | 9.47 | 9.47 | - |
| 4 | 15.23 | 3.81 | 1.92 |
| 16 | 56.50 | 3.53 | 3.44 |
| 32 | 95.92 | 3.00 | 2.46 |

What this shows, and no more: for the flat list the marginal cost is 1.67 to 1.68 ns per item from 9 to 1,024 items (a 16-fold step each), so growth is linear in the number of items over
that range (a straight line through the 64- and 1,024-item rows predicts 30.4 ns for 9 items; 30.04 was measured). The 1-item row sits below the line (10.43 ns against a predicted 17 ns), so
a fixed cost plus a per-item term does not describe the smallest size, and the reason was not isolated. For nesting the marginal cost per level is not constant (1.9, 3.4 and 2.5 ns
over the three segments), so "linear" is an approximation here: doubling the depth from 16 to 32 multiplied the time by 1.70 and quadrupling from 4 to 16 by 3.71, both below the 2x and 4x that
proportional growth would give, and the totals are far from quadratic (32 levels cost 10 times one level, not 1,000). The cause of the steeper 4 to 16 segment was not isolated. All four depths use
the same validator frame bucket (`MaxListDepth` is 32), so frame size does not explain the differences. The index and encoder asymptotics were not measured separately (the
9 / 12 / 16-field index rows are 26.4 / 32.2 / 41.9 ns, 2.9 / 2.7 / 2.6 ns per field including the fixed part). Memory O(max depth) is by construction (fixed-size frame locals) and was not
measured separately; every row allocates 0 B.

## Concurrent application throughput (spec 13.1 last row)

Measured with the stand-alone harness `RlpConcurrentThroughputRunner.cs` (`--rlp-concurrency`, default settings), one run, 2026-11-17 10:04. Source:
`BenchmarkDotNet.Artifacts/rlp-concurrency/20261117-100406/rlp-concurrency.md`, `.csv` (one row per run) and `-ops.csv` (per operation). It runs
1, 2, 4, 8 and 12 (logical cores) threads, 3 s warm-up and 10 s measured per thread count, plus a 1-thread burn-in, and an untimed reference run per thread count
(1 s warm-up, 5 s measured) that shows what the per-operation timing costs. The harness verifies every operation result on every call against a value computed from the
fixtures and the independent reference encoder (typed fields, signatures, chain id, status and offset for hostile input, exact bytes for writers); a wrong result aborts the run.
I demonstrated that once with a temporary edit (a result that turns wrong after 3,000,000 calls): the run stopped with `RUN ABORTED: operation BuildLegacy on thread 0: expected 183, got ...`
and exit code 2, a result wrong at start-up is rejected before any thread starts, and the edit was reverted and checked with `diff`.

### Environment block printed by the harness

| Item | Value |
| --- | --- |
| CPU | Intel(R) Core(TM) Ultra 9 185H |
| Logical cores | 12 |
| Virtualisation | running under a hypervisor (cpuinfo hypervisor flag); DMI VMware, Inc. / VMware Virtual Platform; guest /proc/cpuinfo reports cpu MHz 3072.002 |
| Core types | no hybrid topology visible to this OS (cpu_core/cpu_atom cpus files absent or empty; cpu_capacity 1024); the CPU model may still be hybrid in hardware |
| Clock regime | cpufreq is not exposed to this OS (no /sys/devices/system/cpu/cpu0/cpufreq), so the governor, scaling limits and turbo state cannot be read; the frequency actually delivered is not known |
| Memory architecture | 1 NUMA node(s) under /sys/devices/system/node; MemTotal 31765028 kB; GC-visible memory 31,020 MiB |
| OS | Ubuntu 26.04.1 LTS (X64) |
| SDK | 10.0.115 (dotnet --version in the working directory) |
| Runtime | .NET 10.0.15 (X64), Release (optimised) build |
| GC | Workstation, concurrent: false, latency mode Batch |
| Tiered compilation and PGO | environment DOTNET_BUNDLE_EXTRACT_BASE_DIR=/home/user/.cache/dotnet_bundle_extract; runtimeconfig System.Runtime.TieredPGO=true, System.Runtime.TieredCompilation=default, System.Runtime.TieredCompilation.QuickJitForLoops=default, System.GC.Server=default, System.GC.Concurrent=false |
| Thread pinning | not pinned (no affinity or priority set; process affinity mask 0xfff (12 CPUs allowed)) |
| Timer | Stopwatch frequency 1,000,000,000 Hz, high resolution True; pair overhead min/p50/p99 12/14/26 ns |

### Operation mix

Each operation is drawn from a 1,000-entry table by a per-thread seeded xorshift64* generator, so the share is exact in expectation. All inputs are stable, owned byte arrays shared read-only by every thread; the writers use a per-thread output buffer and a `stackalloc` frame workspace. Default limits (1 MiB, depth 32, 8,192 items).

| Operation | Share | What it does |
| --- | ---: | --- |
| DecodeLegacy | 10% | strict decode of the signed legacy transaction (chain 1, 9 fields), structure only |
| DecodeEip1559 | 10% | strict decode of the EIP-1559 payload with a 2-entry access list, structure only |
| ReadLegacyTyped | 25% | decode, enumerate all 9 fields as typed values, chain-id-aware `TryGetEvmLegacySignature` (chain 98866, v = 197768) |
| ReadEip1559Typed | 15% | decode, enumerate all 12 fields and the access list as typed values, `TryGetEvmYParitySignature` |
| IndexLateField | 10% | decode the EIP-1559 payload, `TryIndexDirectChildren` into a 16-slot `stackalloc` workspace, read field 11 (`s`) as `uint256` |
| BuildLegacy | 10% | build the chain-98866 legacy transaction: size every field with the public helpers, then framed `RLPWriter` with `TryWriteEvmLegacyV`; output compared with the expected bytes |
| BuildEip1559 | 10% | build the EIP-1559 payload: size every field and list with the public helpers, then framed `RLPWriter`; output compared with the expected bytes |
| HostileOversizedLength | 4% | hostile: `bf ff*8` (length above `int.MaxValue`), expects `LengthOverflow` |
| HostileNonCanonical | 3% | hostile: `81 05` (non-canonical single byte), expects `NonCanonicalEncoding` |
| HostileDepthBomb | 2.5% | hostile: 33 nested lists (one above the default depth 32), expects `TooDeep` |
| HostileItemsOverLimit | 0.5% | hostile: a list of 8,192 literals (8,193 items, one above the default 8,192), expects `TooManyItems` |

Fixture sizes: legacy transaction 180 bytes, chain-98866 legacy transaction 183 bytes, EIP-1559 payload 371 bytes. Every operation is verified on every call against a value computed from fixture data and the independent reference encoder at start-up (typed fields, signature, chain id, status and offset for the hostile inputs, exact output bytes for the writers).

### Throughput and GC

The timed runs time every operation (two timestamp reads each); the untimed reference runs do not, so their ops/s shows what the instrumentation costs. GC columns are collections and managed bytes allocated by the whole process during the measured period only.

| Threads | Mode | Ops | ops/s total | ops/s per thread | gen0 | gen1 | gen2 | Allocated bytes |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | timed | 57,933,410 | 5,791,490 | 5,791,490 | 0 | 0 | 0 | 0 |
| 1 | untimed | 35,373,998 | 7,068,226 | 7,068,226 | 0 | 0 | 0 | 0 |
| 2 | timed | 111,388,581 | 11,127,733 | 5,563,866 | 0 | 0 | 0 | 0 |
| 2 | untimed | 67,138,701 | 13,397,182 | 6,698,591 | 0 | 0 | 0 | 0 |
| 4 | timed | 215,100,616 | 21,498,094 | 5,374,524 | 0 | 0 | 0 | 0 |
| 4 | untimed | 129,153,667 | 25,756,583 | 6,439,146 | 0 | 0 | 0 | 0 |
| 8 | timed | 303,158,712 | 30,268,096 | 3,783,512 | 0 | 0 | 0 | 0 |
| 8 | untimed | 184,974,470 | 36,913,019 | 4,614,127 | 0 | 0 | 0 | 0 |
| 12 | timed | 357,536,817 | 35,748,897 | 2,979,075 | 0 | 0 | 0 | 0 |
| 12 | untimed | 218,086,254 | 43,460,256 | 3,621,688 | 0 | 0 | 0 | 0 |

The harness's `ops/s` are for this machine and this mix only. With one thread the instrumentation costs about 18% of throughput (7.07 M ops/s untimed against 5.79 M timed): 141.5 ns per operation untimed, 172.7 ns timed, so 31 ns per operation,
about twice the 14 ns median cost of a back-to-back `Stopwatch.GetTimestamp` pair (the rest, histogram updates and bookkeeping, was not separated). Scaling from 1 to 12 threads is 6.2x timed (5.8 M to 35.7 M ops/s) and 6.1x untimed, and per-thread
throughput falls from 5.8 M to 3.0 M ops/s (timed) between 1 and 12 threads. 12 logical CPUs of a hypervisor guest may not be 12 independent physical cores (the guest cannot tell, see Environment),
so the cause of the sub-linear scaling was not isolated. No collection of any generation ran and the process allocated 0 bytes during every measured period (the library and the harness
allocate nothing in the loop; `GC.GetTotalAllocatedBytes(precise: true)`).

### Latency

Raw is the measured interval including one timer read pair; net subtracts the median timer overhead (14 ns), clamped at 0. Percentiles are bucket upper bounds of a log-linear histogram (resolution about 1.6% above 128 ns); max is exact. Values in ns.

| Threads | p50 raw | p95 raw | p99 raw | p99.9 raw | max raw | p50 net | p95 net | p99 net | p99.9 net | max net |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 99 | 205 | 359 | 6,847 | 16,117,832 | 85 | 191 | 345 | 6,833 | 16,117,818 |
| 2 | 101 | 215 | 451 | 7,359 | 15,430,636 | 87 | 201 | 437 | 7,345 | 15,430,622 |
| 4 | 107 | 223 | 415 | 7,359 | 5,085,722 | 93 | 209 | 401 | 7,345 | 5,085,708 |
| 8 | 135 | 423 | 535 | 13,311 | 3,335,288 | 121 | 409 | 521 | 13,297 | 3,335,274 |
| 12 | 173 | 491 | 663 | 23,807 | 13,243,715 | 159 | 477 | 649 | 23,793 | 13,243,701 |

Raw values include the timer; the net columns subtract the median timer overhead measured at start-up (12 ns minimum, 14 ns median, 26 ns p99 for a back-to-back `GetTimestamp` pair; the figures printed in the
run's environment block). p50 is 99 ns raw with one thread and 173 ns with 12 threads. p99 is 359 to 663 ns. The p99.9 values of 6.8 to 23.8 us are not noise: the hostile
items-over-limit input (an 8,193-item list rejected after scanning, about 7 us alone) is 0.5% of operations by design, so it alone occupies the slowest 0.5% and sets p99.9 (its own p50 is 6.8 us at one thread, see below); p99 sits below it.
The maxima (3.3 to 16.1 ms) are single operations about 10,000 times the median; the likely cause is a thread being descheduled or the vCPU being paused by the hypervisor, but the harness does not distinguish causes and none was established.

### Latency by operation

Raw ns, from the same run.

Threads 1:

| Operation | Share of ops | Mean | p50 | p99 | p99.9 | Max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| DecodeLegacy | 10.00% | 47 | 46 | 94 | 175 | 280,618 |
| DecodeEip1559 | 10.00% | 75 | 71 | 145 | 371 | 344,495 |
| ReadLegacyTyped | 25.00% | 101 | 98 | 201 | 371 | 562,295 |
| ReadEip1559Typed | 15.01% | 208 | 199 | 411 | 831 | 16,117,832 |
| IndexLateField | 9.99% | 119 | 114 | 233 | 615 | 198,066 |
| BuildLegacy | 9.99% | 103 | 100 | 219 | 363 | 252,190 |
| BuildEip1559 | 10.00% | 205 | 197 | 443 | 727 | 566,380 |
| HostileOversizedLength | 4.00% | 39 | 38 | 78 | 123 | 207,108 |
| HostileNonCanonical | 3.00% | 33 | 32 | 71 | 110 | 31,634 |
| HostileDepthBomb | 2.50% | 98 | 93 | 189 | 487 | 546,951 |
| HostileItemsOverLimit | 0.50% | 6,947 | 6,783 | 13,183 | 26,367 | 1,760,601 |

Threads 2:

| Operation | Share of ops | Mean | p50 | p99 | p99.9 | Max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| DecodeLegacy | 10.00% | 50 | 47 | 114 | 187 | 526,645 |
| DecodeEip1559 | 10.00% | 78 | 73 | 183 | 391 | 251,026 |
| ReadLegacyTyped | 25.00% | 105 | 99 | 245 | 395 | 3,816,325 |
| ReadEip1559Typed | 15.00% | 215 | 203 | 491 | 847 | 1,984,382 |
| IndexLateField | 10.00% | 124 | 117 | 291 | 631 | 1,022,002 |
| BuildLegacy | 9.99% | 108 | 101 | 251 | 387 | 294,266 |
| BuildEip1559 | 10.00% | 217 | 199 | 495 | 775 | 15,430,636 |
| HostileOversizedLength | 4.00% | 41 | 39 | 89 | 133 | 229,609 |
| HostileNonCanonical | 3.00% | 35 | 32 | 78 | 126 | 131,768 |
| HostileDepthBomb | 2.50% | 102 | 96 | 237 | 507 | 292,715 |
| HostileItemsOverLimit | 0.50% | 7,194 | 6,911 | 16,639 | 33,791 | 1,304,073 |

Threads 4:

| Operation | Share of ops | Mean | p50 | p99 | p99.9 | Max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| DecodeLegacy | 10.00% | 51 | 49 | 119 | 191 | 200,528 |
| DecodeEip1559 | 10.00% | 81 | 76 | 191 | 403 | 5,059,934 |
| ReadLegacyTyped | 25.00% | 109 | 105 | 245 | 403 | 1,485,299 |
| ReadEip1559Typed | 15.00% | 223 | 213 | 491 | 895 | 5,085,722 |
| IndexLateField | 10.00% | 129 | 123 | 299 | 671 | 683,218 |
| BuildLegacy | 10.00% | 112 | 107 | 267 | 391 | 382,159 |
| BuildEip1559 | 10.00% | 221 | 211 | 503 | 775 | 3,389,352 |
| HostileOversizedLength | 4.00% | 43 | 41 | 82 | 117 | 2,664,003 |
| HostileNonCanonical | 3.00% | 36 | 34 | 74 | 111 | 53,583 |
| HostileDepthBomb | 2.50% | 106 | 100 | 245 | 535 | 818,660 |
| HostileItemsOverLimit | 0.50% | 7,455 | 7,359 | 13,567 | 33,279 | 1,415,645 |

Threads 8:

| Operation | Share of ops | Mean | p50 | p99 | p99.9 | Max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| DecodeLegacy | 10.00% | 71 | 61 | 141 | 259 | 2,393,570 |
| DecodeEip1559 | 10.00% | 112 | 96 | 227 | 499 | 1,497,536 |
| ReadLegacyTyped | 25.00% | 149 | 131 | 299 | 519 | 647,078 |
| ReadEip1559Typed | 15.00% | 305 | 267 | 623 | 1,119 | 1,551,093 |
| IndexLateField | 10.00% | 177 | 153 | 363 | 815 | 3,335,288 |
| BuildLegacy | 10.00% | 156 | 133 | 339 | 631 | 849,858 |
| BuildEip1559 | 10.00% | 309 | 263 | 663 | 1,631 | 353,167 |
| HostileOversizedLength | 4.00% | 58 | 52 | 110 | 233 | 190,608 |
| HostileNonCanonical | 3.00% | 52 | 43 | 117 | 219 | 220,370 |
| HostileDepthBomb | 2.50% | 145 | 124 | 303 | 655 | 236,167 |
| HostileItemsOverLimit | 0.50% | 11,466 | 8,959 | 25,599 | 50,687 | 1,216,845 |

Threads 12:

| Operation | Share of ops | Mean | p50 | p99 | p99.9 | Max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| DecodeLegacy | 10.00% | 90 | 69 | 161 | 399 | 3,139,116 |
| DecodeEip1559 | 10.00% | 140 | 111 | 247 | 559 | 3,033,354 |
| ReadLegacyTyped | 25.00% | 186 | 149 | 339 | 735 | 5,772,153 |
| ReadEip1559Typed | 15.00% | 381 | 303 | 743 | 1,567 | 6,108,292 |
| IndexLateField | 10.00% | 219 | 177 | 447 | 887 | 13,243,715 |
| BuildLegacy | 10.00% | 197 | 151 | 419 | 983 | 7,474,684 |
| BuildEip1559 | 10.00% | 389 | 299 | 783 | 1,919 | 4,150,119 |
| HostileOversizedLength | 4.00% | 72 | 60 | 135 | 331 | 2,388,039 |
| HostileNonCanonical | 3.00% | 69 | 48 | 139 | 343 | 3,235,861 |
| HostileDepthBomb | 2.50% | 180 | 145 | 335 | 727 | 3,347,374 |
| HostileItemsOverLimit | 0.50% | 15,250 | 10,111 | 34,815 | 62,463 | 4,116,799 |

### Caveats of the concurrency numbers

* Closed loop: each thread issues its next operation after the previous one finished, so a stall delays later operations instead of queueing them; the percentiles under-report what an
  open-loop arrival process would see (coordinated omission is not modelled).
* Timer cost is comparable to the cheapest operations, so read the net columns for the codec and the raw columns for what the harness saw; the net figures assume the timer cost is constant, which a cache
  miss or a descheduled thread breaks in the tail.
* Threads were not pinned, the host is a VMware guest, the processor model is hybrid in hardware but not visible as such from the guest, and the clock regime is unknown, so which kind of core
  ran a thread, and at what frequency, is not known.
* One run per thread count: run-to-run variation of the throughput and the tail percentiles was not measured. The mix (a single seed per thread, fixed percentages) is realistic in shape, not
  taken from production traffic; the hostile share (10%) is a stress choice. The `rlp-after` microbenchmark figures cannot be added to this table: the mix includes list-length sizing, typed
  reads and a verification of every result, none of which the single-operation rows include.

## Comparison library: Nethereum.RLP 7.1.0

`Nethereum.RLP` 7.1.0 restored and exposes both an allocating tree decoder (`RLP.Decode(byte[], maxElements)`, `RLP.EncodeElement`,
`RLP.EncodeList`) and a cursor reader (`RlpReader`: `ReadListHeader`, `ReadUInt64`, `ReadBigInteger`, `ReadBytes`, `EnsureEnd`). It is a
`PackageReference` of the benchmark project and, since the differential tests were added, of the test project (`NethereumDifferentialTests`, spec 12.4); never of the library. Rows are in `RlpComparisonBenchmarks`; `[GlobalSetup]`
checks that both libraries decode the legacy transaction to the same field bytes, that `RlpReader` returns the same values, and that both
encoders produce the fixture bytes. Figures are from the after run, the same run as the library rows above.

| Workload (legacy 9-field tx unless noted) | This library | Nethereum.RLP 7.1.0 |
| --- | ---: | ---: |
| Strict decode, structure only | 22.27 ns, 0 B | 424.35 ns, 1,512 B (`RLP.Decode`, element tree) |
| Strict decode, EIP-1559 payload with access list | 46.76 ns, 0 B | 652.99 ns, 2,712 B |
| Strict decode, 32 nested lists | 92.82 ns, 0 B | 1.20 us, 4,312 B |
| Decode and touch every field's bytes | 37.22 ns, 0 B (enumerator) | 406.13 ns, 1,304 B (tree) |
| Decode and read all 9 fields as typed values | 55.66 ns, 0 B (`uint256`, `Address`, enumerator) | 167.48 ns, 736 B (`RlpReader`, `BigInteger`, `byte[]`) |
| Encode, constant list length | 45.55 ns, 0 B (framed `RLPWriter`, typed adapters) | 122.64 ns, 776 B (`EncodeElement` per field, `EncodeList`) |
| Encode, list length computed from the field values (`Ours_Encode_Legacy_WithSizing`, second measurement, run 2) | 50.76 ns, 0 B | 120.68 ns, 776 B (same row, same run; the Nethereum encoder sizes inside the timed call) |
| Reject `81 05` | 3.78 ns, 0 B (status) | 6.52 us, 832 B (exception) |
| Reject `bf ff*8` | 4.43 ns, 0 B (status) | 6.63 us, 912 B (exception) |

These are not a like-for-like strictness comparison, so no row is a claimed win:

* Both decoders were probed with the inputs in the table further down. Nethereum 7.1.0's `RLP.Decode` rejects every non-canonical or malformed form
  tried (single byte below 0x80 with a prefix, long form below 56, leading zero in the length, lengths above `int.MaxValue`, trailing
  bytes, truncation, child overrunning its list, empty input), so the decode rows do comparable validation work. It reports every
  failure as the same `Exception` type with the input hex in the message; this library reports a status and an offset.
* Different limits: Nethereum's nesting limit is a fixed 64 (`RLP.MaxDecodeDepth`) while this library defaults to 32 (configurable 1 to 64);
  its API has no maximum-document-size parameter; its element budget was set to 8,192 for these rows and rejected a list with
  8,193 items, matching this library's item limit in that probe.
* Different work: Nethereum builds an allocating tree (or allocates `byte[]`/`BigInteger` per field), this library validates in place and
  returns views. The typed-read rows convert to different types (`uint256`/`Address` against `BigInteger`/`byte[]`). The library's
  typed readers also enforce integer minimality and width (leading zero, more than 8 or 32 bytes), which `RlpReader.ReadUInt64` also
  rejected in the probe below but whose coverage of 256-bit values and addresses was not compared.
* Reject rows compare a status return against an exception. The exception cost is part of that API, not of its parsing.
* The Nethereum encoder takes pre-trimmed field bytes (done in setup) and allocates one array per field plus the list; the library writer
  takes typed values, announces list lengths up front and writes into caller storage.
* The encode ratio (45.55 ns against 122.64 ns) excludes the library's list-length sizing, because the benchmark passes a constant to
  `TryBeginList`; a caller that sums the field sizes first pays more than the figure shows. The second measurement added the row with sizing: 50.76 ns against
  120.68 ns in the same run (2.4 times, where the constant-length rows of that run give 45.22 against 120.68 ns, 2.7 times). The Nethereum encoder takes field bytes trimmed to minimal
  magnitudes in setup, so the typed-value conversion it would need from a `uint256` is untimed in its row.
* The `Nethereum_Encode_Legacy` row moved 35% between two runs without any code change (see the noise note above); read its ratio loosely.

Probe results. The table is now the test `NethereumDifferentialTests.ProbeTable_RecordedOutcomesHoldForBothLibraries`
(`Eccentricware.Web3.DataTypes.Test/Rlp/NethereumDifferentialTests.cs`), which asserts each row's library status and offset and whether Nethereum accepts; it fails if the
table or either library changes. The claims about Nethereum's exception type and message are asserted by `ProbeTable_NethereumRejectionMessageNamesTheInputHex`, the 8,192-budget note below by
`ProbeTable_DefaultNethereumOverloadHasNoItemLimit_ExplicitBudgetDoes`, and the integer table by `IntegerProbe_LibraryAndNethereumReaderBehaveAsRecorded`. The same test class also compares
decoded structure and encoded bytes on seeded trees and mutations. Its header documents a divergence register: across every vector, 6,000+ seeded structural mutations and every 1- and 2-byte input
(and, in a one-off run not repeated in the suite, all 16,777,216 three-byte inputs with matched limits) the two decoders disagree on accept or reject in exactly three patterns, all caused by limit policy
and none by the wire rules, and in each the library rejects and Nethereum accepts:

* library limits out of range (`InvalidArgument`; Nethereum has no limits object);
* nesting above the configured depth but at most 64 (`TooDeep`; Nethereum's cap is a fixed 64);
* input above `MaxEncodedBytes` (`TooLarge`; Nethereum has no size limit).

No input was found that the library accepts and Nethereum rejects, and with matched limits (depth 64, the same item budget, size not binding) no accept/reject divergence was found on any mutation.

| Input | Library (default limits) | Nethereum.RLP 7.1.0 `RLP.Decode(data, 8192)` |
| --- | --- | --- |
| empty input | EmptyInput @ 0 | rejected (Exception) |
| 80 empty string | accepted | accepted |
| c0 empty list | accepted | accepted |
| 00 literal | accepted | accepted |
| 7f literal | accepted | accepted |
| 81 05 (single byte below 0x80 with prefix) | NonCanonicalEncoding @ 0 | rejected (Exception) |
| 81 80 (valid) | accepted | accepted |
| b8 05 + 5 bytes (long form below 56) | NonCanonicalEncoding @ 1 | rejected (Exception) |
| b9 00 38 + 56 bytes (leading zero length byte) | NonCanonicalEncoding @ 1 | rejected (Exception) |
| b8 38 + 56 bytes (valid long string) | accepted | accepted |
| bb ff ff ff ff (length 2^32-1) | LengthOverflow @ 1 | rejected (Exception) |
| bf ff*8 | LengthOverflow @ 1 | rejected (Exception) |
| c8 83 63 61 74 83 64 6f 67 + 00 (trailing byte) | TrailingData @ 9 | rejected (Exception) |
| 83 61 62 (truncated string) | TruncatedInput @ 3 | rejected (Exception) |
| c8 83 63 61 74 (truncated list) | TruncatedInput @ 5 | rejected (Exception) |
| c3 83 61 62 (child overruns its list) | InvalidListBoundary @ 1 | rejected (Exception) |
| c2 81 05 (non-canonical child) | NonCanonicalEncoding @ 1 | rejected (Exception) |
| c4 c3 83 61 62 (nested child overruns) | InvalidListBoundary @ 2 | rejected (Exception) |
| c8 83 63 61 74 83 64 6f 67 (cat, dog) | accepted | accepted |
| nested 32 lists | accepted | accepted |
| nested 33 lists | TooDeep @ 32 | accepted |
| nested 64 lists | TooDeep @ 40 | accepted |
| nested 65 lists | TooDeep @ 41 | rejected (Exception) |
| list of 8191 literals (8192 items) | accepted | accepted |
| list of 8192 literals (8193 items) | TooManyItems @ 8194 | rejected (Exception) |

The 33rd list prefix follows 32 enclosing headers, of which 8 (64 lists) or 9 (65 lists) are two-byte `f8` headers because their payload is at least 56 bytes, so the offsets are 32 + 8 = 40 and 32 + 9 = 41. (An earlier version of this table said 43 and 44; `NethereumDifferentialTests` asserts 40 and 41.)

The default overload `RLP.Decode(data)` (no explicit budget) accepted the 8,193-item list; only an explicit `maxElements` enforces an item limit.

Integer and cursor-reader probe (`RlpReader` is Nethereum's cursor API):

| Integer input | This library `TryGetUInt64` | Nethereum `RlpReader.ReadUInt64` |
| --- | --- | --- |
| `80` (zero) | 0 | 0 |
| `00` (raw zero byte) | `NonCanonicalInteger` | throws `InvalidOperationException` |
| `82 00 01` (leading zero) | `NonCanonicalInteger` | throws `InvalidOperationException` |
| `88 ff*8` | 18446744073709551615 | 18446744073709551615 |
| `89 01 00*8` (9 bytes) | `IntegerOverflow` | throws `InvalidOperationException` |
| `81 80` | 128 | 128 |

`RlpReader` also threw on `81 05`, on a child overrunning its list, on a long form below 56, and on a trailing byte after `c0` with `EnsureEnd`.

## Kept optimisations

All kept changes are in `EccentricWare.Web3.DataTypes/Rlp/`; no public signature, status, check order or error offset changed. Baseline
figures are from the baseline runs, "after" from the final run.

1. **Validator frames as `InlineArray` locals, not `stackalloc`; `RLPValidator.TryValidate` is `NoInlining`** (`RLPValidator.cs`).
   *Why:* the JIT listing of the original `TryValidate` shows a stack-cookie prolog (security cookie, a zeroed frame and copies of the
   span argument) that exists because the method contains `stackalloc`, and it ran on every call, including scalar documents that never
   touch the frame buffer. Frames now live in three fixed-size struct locals (8, 32 and 64 slots, chosen from `MaxListDepth` exactly as
   before) inside three small `NoInlining` methods that call the unchanged loop (`ValidateChildren`).
   *Effect:* decode of a scalar document 13.76 ns to 6.63 ns (empty string), 11.45 ns to 3.39 ns (56-byte string), 13.67 ns to 6.65 ns
   (`88 ff*8`); `[ "ab" ]` 14.87 ns to 9.47 ns; signed legacy transaction 23.80 ns to 22.23 ns (-7%), EIP-1559 payload 50.93 ns to 46.36 ns
   (-9%, includes item 2); the three immediate-rejection hostile rows (`LengthOverflow4Bytes`, `NonCanonical...[22]`, `TooLargeInput`) 7.9 to 12.5 ns before, 2.5 to 4.2 ns
   after (-65% to -69%). Other rejection rows gained far less: `TruncatedChild` 18.14 to 11.02 ns (-39%), `NonCanonicalLastOf1024` 1.71 to 1.62 us
   (-6%), `ItemsOverLimit` 8.62 to 8.71 us (+1%), `DepthBomb` -8%, because they spend their time in the validation loop, which this change did not touch.
   Depth-32 nesting, the
   1,024-item list and the 8,191-literal list did not improve (-1%, +4%, +9%; the loop was not changed and the +9% row is within the
   code-layout noise described above, but it is not proven to be noise).
   *Why `NoInlining`:* with the `stackalloc` gone the JIT inlined `TryValidate` into callers, which enlarged their frames: in a run
   without the attribute the enumeration rows got 20% to 34% slower (`Enumerate_Children[literal]` 2.26 to 2.73 ns, `[short4]` 3.60 to
   4.41 ns, `Walk_AllItems[eip1559]` 118.8 to 159.3 ns). With the attribute they are back to or below baseline.
2. **Dedicated 2-, 3- and 4-byte reads of the long-form length** (`RLPHeaderReader.TryReadLongForm`). *Why:* the general
   `LimbReader.TakeBigEndianTail` path branches on the width bits. *Effect* (run with this change alone): `Decode_String[len300]` 19.03 ns to
   10.95 ns (-42%), `Enumerate_Children[long300]` 9.58 ns to 6.85 ns per child (-28%), `Decode_String[len70000]` 13.87 ns to 12.82 ns
   (-8%); final run `[len300]` 3.45 ns and `[long300]` 6.21 ns together with item 1. The 5 to 8 byte path is unchanged.
3. **`TryGetUInt256` reads a payload of up to 8 bytes as one limb** (`RLPTypedExtensions.cs`). *Why:* the `uint256(ReadOnlySpan<byte>)`
   constructor sends every width except 32 through a non-inlined padded path. *Effect:* 2-byte value 4.69 ns to 2.56 ns (-45%), 8-byte value
   4.28 ns to 2.11 ns (-51%); the 32-byte path is unchanged (2.03 ns to 2.12 ns, within noise).
4. **`RLPData.ReadMagnitude` for `TryGetUInt64` and the one-limb `TryGetUInt256` path** (`RLPData.cs`): one 8-byte load for 8 bytes, a
   plain loop over the span otherwise. *Why:* `TakeBigEndianTail` slices and tests width bits. *Effect* (with items 1 to 3 in place):
   `TryGetUInt64` 2 bytes 1.94 ns to 1.45 ns, 8 bytes 1.49 ns to 1.12 ns (-25%); `TryGetUInt256` small 2.56 ns to 2.07 ns. A first version
   without the 8-byte load made the 8-byte read 82% slower (1.66 ns to 3.02 ns), so the load is part of the helper.
5. **`TryWriteUInt256` writes from the limbs** (`RLPWriter.TryWriteUInt256Limbs`, internal, called by the extension). *Why:* the old path built
   a 32-byte stack temporary (a `stackalloc` frame), wrote all 32 bytes, then went through `TryWriteUnsignedMagnitude`. The new member runs the
   same checks in the same order (state, root, size and parent limit), then emits the minimal magnitude with 8-byte big-endian stores (the
   top limb is stored left-aligned and overwritten by the next store when a lower limb follows). *Effect:* 2-byte value 14.62 ns to 4.88 ns
   (-67%), 8-byte 14.02 ns to 4.39 ns (-69%), full width 13.87 ns to 6.68 ns (-52%). The first version (byte loop for the top limb) gave
   4.97 / 7.47 / 11.03 ns; the 8-byte top-limb store gave the rest.
6. **`TryWriteAddress` and `TryWriteHash32` write into a reserved region** (`RLPWriter.TryReserveShortByteString`, internal). *Why:* same
   stack-temporary cost. *Effect:* address 6.46 ns to 4.25 ns (-34%), hash 7.11 ns to 3.64 ns (-49%). Framed sessions: EIP-1559 payload
   163.30 ns to 119.16 ns (-27%), legacy transaction 56.70 ns to 49.34 ns (-13%), which also include items 5 and 6.

Tests added for the changed paths: `LongFormLengthWidthTests` (every length-field width 1 to 8: boundary and distinct-byte values,
truncation of payload and of the length field at the root and inside a list, leading zero, below 56, above `int.MaxValue`),
`ValidatorFrameBucketTests` (every `MaxListDepth` 1 to 64 accepts the exact depth and rejects one more, with empty and non-empty
innermost lists), `TypedWriterDirectEmitTests` (every uint256 byte width plus zero, a literal, address and hash: oracle equality, no store
outside the item, `InvalidListBoundary`, `DestinationTooSmall` and `ExtraRoot` leave destination and writer unchanged). An independent
review mutated the changed code (about 40 single-line mutations) against the full suite and the new tests; the gaps it found were
closed and the new tests kill the reviewer's depth-bucket, 3-byte-read and limb-order mutants.

## Rejected experiments (reverted, verified with `diff -r` against a saved copy)

| Experiment | Measurement | Why reverted |
| --- | --- | --- |
| `AggressiveInlining` on the `RLPHeader` constructor (JIT listings showed it as a real call inside the inlined `TryRead`) | On the original validator (`--job short`): empty string +9%, legacy tx +4%, 1,024-item list +5%, 56-byte string -10%. On the tuned code (full job): rows within -11% to +16%, mixed signs, no row consistently better | No clear gain |
| Validator split with a `stackalloc` wrapper method | Scalar decode -54%, but legacy transaction 23.80 ns to 27.26 ns (+14.5%), 1-item list +6.6% | Costs the list path, which is the main workload; superseded by item 1 |
| Validator loop merged into the `stackalloc` method | Legacy transaction 23.8 ns to 42.3 ns (+78%, short job), EIP-1559 +56% | The loop is slow in a frame with a dynamic stack allocation |
| `RLPHeaderWriter.WriteHeader` fast paths for 1- and 2-byte lengths (`WriteUInt16BigEndian`) | `TryWriteByteString[256]` 5.42 ns to 8.34 ns (+54%), `Writer_RootByteString[256]` 6.96 ns to 11.22 ns (+61%); the 255-byte case -7% | Slower on the 2-byte case it targeted |
| `CheckFits` as one compare against `min(frame end, destination length)`, dropping the "defensive" second compare | Not benchmarked: `AdversarialInputTests.TamperedFrameWorkspace_NeverLetsTheWriterWriteOutsideTheDestination` failed with an `ArgumentOutOfRangeException` | The second compare is load-bearing when the caller tampers with the frame workspace (a wrapped subtraction); weakening it breaks the never-throw guarantee |
| Single-pass `TryIndexDirectChildren` when `workspace.Length >= PayloadLength` (each child takes at least one payload byte, so the workspace cannot overflow; same statuses) | With a workspace of one slot per payload byte: 9 / 12 / 16 fields 25.5 / 31.9 / 39.5 ns to 14.7 / 19.5 / 22.7 ns (-39% to -43%). With a 16-slot workspace: within 4% of the two-pass build | Only helps when the caller supplies a slot per payload byte (about 1.4 KB for the 180-byte legacy transaction), which no realistic caller does; it adds a second code path. Kept the spec's two-pass default |

Hypotheses from the brief that were examined but produced no change: enumerator `MoveNext` (the JIT listing shows the loop with `TryRead` fully inlined, and
literal / 4-byte / 32-byte children measure 2.2 / 3.4 / 3.6 ns each after tuning, but those figures include the amortised decode of the list,
about 1.9 ns per child, so the enumerator's own cost is roughly 1.5 ns; nothing cheap to remove), `TryGetAt` (defensive range checks are required
by its documented contract), and the `RLPData` size / `in` passing (not tested). Bounds-check elimination in `TryRead` was not attempted: the
expected gain was below the 5% bar and every approach needs `Unsafe`/`MemoryMarshal`, which the brief allows only for a gain of 10% or more on a headline benchmark.

## Writer rows after the `RLPWriter.CheckFits` fix

The checked-arithmetic run found that `CheckFits` let a negative room (a frame end below the cursor) wrap through its unsigned compare; the fix computes the room in `long` and rejects a negative room (`docs/rlp-security.md`). `CheckFits` runs on every writer call, so the writer rows were re-run (`BenchmarkDotNet.Artifacts/rlp-after-checkfits-fix/`, default job, out of process, one run, classes `RlpEncoderStringBenchmarks`, `RlpEncoderUInt64Benchmarks`, `RlpEncoderListBenchmarks`, `RlpTypedAdapterWriteBenchmarks`, `RlpEvmLegacyVWriteBenchmarks`; 52 rows).

* Result: no regression. Across the 44 rows that also exist in `rlp-after` the mean of the per-row change is -4.1%; against the `rlp-after-signature-fix` runs (17 rows) it is -2.3%. Individual rows moved between -29% (`TryWriteUInt64 0`, 0.24 to 0.17 ns) and +9.1% (`TryWriteEvmYParity`, 1.67 to 1.82 ns, 0.15 ns), all inside the 10% run-to-run spread seen elsewhere in this document. `Writer_NestedLists32` was +4.1% against `rlp-after` and +5.5% against the signature-fix runs, the only list row that moved up.
* Reading: the extra compare is not measurable on these rows. The comparison is between runs on different days of the same machine, so a small cost (a few percent) cannot be excluded.
* The earlier `rlp-after-signature-fix` run-1 figure for `Writer_LegacyTransaction` (62.92 ns) is the fixture-sharing artefact described above; this run's 47.62 ns is in line with `rlp-after` (49.34 ns).

## Index: when does it pay?

Per operation, final run, 12 fields: `Index_Build` 32.23 ns, of which the counting pass (`Index_CountPassOnly`) is 14.67 ns, so the populating second
pass is about 17.6 ns (55%). `RLPChildrenIndex.TryGetAt` costs 4.0 to 4.7 ns per read; an enumerator scan to the first, middle and last field costs
2.2, 12.3 and 19.2 ns. One read of the last field is cheaper with the enumerator (19.2 ns against 32.2 + 4.2 ns); the index wins from about three reads of
the last field, and never for the first field. In the end-to-end row that reads all nine fields in order, the enumerator version (583.67 ns) beat the
indexed version (625.12 ns) by about 41 ns: use the index for random access or repeated visits, not for one in-order pass.

## End to end

`Legacy_Enumerate_ReadAllTyped_Keccak` is 583.67 ns, of which Keccak-256 of the 180-byte encoding alone (`Hash_Only_Legacy`) is 522.23 ns and the
strict decode 22.56 ns. The codec is therefore about 10% of this path; the typed reads of all nine fields cost the remaining roughly 40 ns.

## Zero-allocation evidence

* Every library benchmark in the after run reports 0 B allocated per operation (`[MemoryDiagnoser]`, warm JIT): all 122 library rows across decode,
  enumeration, index, encoder, writer, typed adapters, hostile inputs and end-to-end. Only the 8 `Nethereum_*` rows allocate (736 to 4,312 B/op for
  accepted input, 832 to 912 B/op when rejecting by exception). A script over `rlp-after/results/*-report-full-compressed.json` confirmed it.
* The second measurement added 28 library rows (158 cases in total, 150 library rows, 8 Nethereum rows). A script over the saved JSON of every run in
  `rlp-after-signature-fix/` (including the re-runs and the A/B runs) and `rlp-after/` found `BytesAllocatedPerOperation` = 0 for all 150 library rows in every run they appear in.
  The concurrency harness reported 0 collections of every generation and 0 allocated bytes in all ten measured periods (1 to 12 threads, timed and untimed).
* The existing allocation tests (`ReaderAllocationTests`, `WriterAllocationTests`, `TypedAdapterAllocationTests`, `TypedAdapterWriterAllocationTests`)
  passed with all changes: full suite 9,545 passing, 0 failing (9,477 before this work plus 68 new tests). The full suite now has 10,039 tests, all passing, 0 failing
  (run at the end of the second measurement; the benchmark work changed no test).

## What was not measured

* ARM64, Windows, macOS, other .NET runtimes, Tiered PGO off, NativeAOT, a bare-metal host, and any other CPU, power plan or clock regime. Everything here ran in one VMware guest.
* Spec 13.1 row "Concurrent application throughput" (ops/s, p50 / p95 / p99, GC under a mixed workload): measured once, on this machine only, by the stand-alone harness (see the
  concurrency section). Not measured: run-to-run variation of that run, pinned threads, an open-loop (arrival-rate) load that would show coordinated omission, Server GC, Tiered PGO off,
  a mix taken from real traffic, and the other benchmark rows under concurrency. The p99 and p99.9 figures are valid for this machine and this synthetic mix only.
* Documents above the default 1 MiB limit, and long-form lengths with 4 length bytes in the timed rows (a 4-byte length needs a payload of 16 MiB or more;
  it is covered by correctness tests only).
* P-core against E-core placement of the benchmark processes and the CPU frequency actually delivered: neither is readable from inside the VMware guest (no cpufreq, no hybrid topology),
  and the processes were not pinned. Memory architecture is recorded only as the guest sees it (one NUMA node); the host's layout is unknown.
* Run-to-run variation of the new rows: each new row ran once (run 1); only the encoder, regression and typed-writer rows were repeated.
* The cause of the 14.7 to 17.0 ns cost of `TryWriteEvmLegacyV` with a chain id, the 9.7 to 15.6 ns of the signature readers, and the `TryWriteUInt256_FullWidth` drift of +8% to +12% were
  not isolated; the explanations offered are hypotheses.
* The hostile-input rows measure one input per case; the cost of other hostile shapes (for example many small nested lists near the item limit) was not
  explored.
* Mean only for the BenchmarkDotNet rows: no per-iteration distributions, percentile or histogram analysis was done on them. Percentiles exist only for the concurrency harness's mixed workload.

## Re-running

See `EccentricWare.Web3.DataTypes.Benchmarks/README.md` ("Recording a baseline and comparing"). Results are written outside source control
(`BenchmarkDotNet.Artifacts/` is git-ignored): `rlp-baseline/`, `rlp-baseline-supplement/`, `rlp-after/`, `rlp-after-signature-fix/` (with `rerun-1/`, `rerun-2-typed-write/`,
`ab-current-3/`, `ab-nosnapshot-1/` and `ab-nosnapshot-2/` inside) and `rlp-concurrency/20261117-100406/` hold the runs quoted here. The concurrency harness is started with
`dotnet EccentricWare.Web3.DataTypes.Benchmarks/bin/Release/net10.0/EccentricWare.Web3.DataTypes.Benchmarks.dll --rlp-concurrency` (options in the benchmarks README); about 100 s with the defaults.
