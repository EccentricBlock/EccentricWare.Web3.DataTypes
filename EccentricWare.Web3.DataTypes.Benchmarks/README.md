# EccentricWare.Web3.DataTypes.Benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org/) suite for the Web3 primitives. It exists to produce a repeatable
baseline before and after a change, so regressions and improvements are measured rather than assumed.

## Suites

| Class | Covers |
| --- | --- |
| `Hash32Benchmarks` | Construction, byte conversion, hex/Base58/Base64 parsing (including the public non-canonical `ByteUtils.TryDecodeBase58To32`), formatting, equality, ordering, hashing, JSON converter |
| `Hash32CollectionBenchmarks` | `IndexOf` / `CountEquals` span scans (8, 1,024, 65,536 items), `Dictionary`/`HashSet` lookups, sorting |
| `HexBytesBenchmarks` | Construction, parsing, formatting, equality, ordering, hashing, slicing, concat, JSON converter at 4 / 68 / 1,024 / 24,576 bytes |
| `HexBytesDictionaryBenchmarks` | Dictionary lookups with `approve(spender, MAX)` calldata (hash quality under shared head/tail) |
| `FunctionSelectorBenchmarks` | Construction, `FromSignature` (short, tuple, very long), parsing, formatting, JSON converter |
| `FunctionSelectorCollectionBenchmarks` | `ContainsSimd` scans, `BinarySearchSorted`, dictionary lookups at 8 / 64 / 1,024 / 65,536 selectors |
| `AddressBenchmarks` | EVM (20-byte) and Solana (32-byte) addresses: construction, byte output, parsing (chars and UTF-8; EVM prefixed, unprefixed and quoted; Solana Base58), formatting (lowercase hex, Base58, EIP-55 checksum), equality/ordering/hashing, JSON converter, EF Core value converter |
| `AddressCollectionBenchmarks` | `AddressBulkMatcher.ContainsLinear` against a scalar loop and `Array.IndexOf`, `AddressSet` build/lookup/match against `HashSet<Address>` and `Dictionary<Address,int>`, hashing and sorting at 8 / 64 / 1,024 / 65,536 addresses (40 bytes each) |
| `SignatureBenchmarks` | EVM (65-byte) and Solana (64-byte) signatures: construction from bytes/components, byte and r/s output, hex parsing (chars and UTF-8), Solana Base58 parsing and encoding, formatting, equality/ordering/hashing, JSON converter (hex and Base58 reads and writes), EF Core value converter |
| `SignatureCollectionBenchmarks` | `Dictionary`/`HashSet` lookups, hashing and sorting of a mixed EVM/Solana population (8, 1,024, 65,536 items); the struct is 72 bytes, so copy cost shows here |
| `ValueConverterBenchmarks` | EF Core value converters without a suite of their own: `Hash32`, `FunctionSelector` (int and binary), `InstructionDiscriminator` (long and binary), `int256`, `HexBytes` binary (zero-copy) and text at 68 / 4,096 bytes; write and read of each |
| `InstructionDiscriminatorBenchmarks` | Construction, parsing, formatting, JSON converter |
| `Keccak256Benchmarks` | `ComputeHash` at 0 / 25 / 90 / 136 / 1,024 bytes (selector derivation and EIP-55 checksums) |
| `UInt256Benchmarks` | Construction, byte conversion, equality/ordering/hashing, add/sub/mul/div/mod by operand shape (one limb, 128-bit, full width; 64/128/256-bit divisors), shifts, hex/decimal parsing (chars and UTF-8, small and 78-digit), formatting, `BigInteger` conversion, JSON converter, EF Core value converter, `BigInteger_*` reference rows |
| `UInt256CollectionBenchmarks` | `UInt256BulkSimd` scans (8, 1,024, 65,536 items) against a plain scalar loop, `Dictionary`/`HashSet` lookups with random and sequential keys (default vs `UInt256Comparer`), hashing, sorting |
| `Int256Benchmarks` | The same areas for `int256`: sign-aware compare, checked arithmetic, multiply/divide/modulo (native sign-magnitude, with `BigInteger_*` reference rows), negative hex (two's complement) and signed decimal parsing/formatting, JSON, EF Core value converter |
| `Int256CollectionBenchmarks` | `Dictionary` lookups (random and signed-sequential keys, default vs `Int256Comparer`) and sorting with mixed signs |
| `HexBigIntegerBenchmarks` | Construction, equality/ordering/hashing, hex parsing (chars and UTF-8; tiny block-number, 64-bit, 256-bit, negative, quoted and 2,048-bit values), `ToString`, UTF-8 and char formatting (hex and decimal), JSON converter (hex strings, number tokens, property names), EF Core value converter, `BigInteger_*` reference rows |

Inputs are generated from fixed seeds (`BenchmarkData`), and each suite verifies in `[GlobalSetup]` that its parse
inputs round-trip, so a wrong benchmark fails loudly instead of reporting a fast no-op.

## Running

Always use `-c Release`; BenchmarkDotNet refuses to run unoptimised builds. Close other CPU-heavy work first.

If an IDE with a C# project system is open on the repo, it can delete BenchmarkDotNet's generated per-job build folder under
`bin/` mid-run (`DirectoryNotFoundException ... -ShortRun-1`). Build once, then run the built assembly with `--inProcess`:
`dotnet EccentricWare.Web3.DataTypes.Benchmarks/bin/Release/net10.0/EccentricWare.Web3.DataTypes.Benchmarks.dll --inProcess --filter "*" --job short`.
Use the same toolchain for the baseline and the comparison run.

```bash
# Everything, quick (about 15-20 minutes): good enough to spot large changes
dotnet run -c Release --project EccentricWare.Web3.DataTypes.Benchmarks -- --filter "*" --job short

# One suite, full default job (more iterations, tighter error bars): use this for decisions
dotnet run -c Release --project EccentricWare.Web3.DataTypes.Benchmarks -- --filter "*Hash32Benchmarks*"

# One method
dotnet run -c Release --project EccentricWare.Web3.DataTypes.Benchmarks -- --filter "*Hash32Benchmarks.TryParse_HexUtf8"

# Only the 256-bit integer suites
dotnet run -c Release --project EccentricWare.Web3.DataTypes.Benchmarks -- --filter "*UInt256*" "*Int256*" --job short

# List what is available
dotnet run -c Release --project EccentricWare.Web3.DataTypes.Benchmarks -- --list flat
```

## Recording a baseline and comparing

```bash
# 1. On the unchanged code: write JSON + GitHub-markdown results to a folder outside the repo
dotnet run -c Release --project EccentricWare.Web3.DataTypes.Benchmarks -- \
  --filter "*" --job short --exporters json github --artifacts ../bench-baseline

# 2. Apply the change, then repeat into a second folder
dotnet run -c Release --project EccentricWare.Web3.DataTypes.Benchmarks -- \
  --filter "*" --job short --exporters json github --artifacts ../bench-after
```

Compare the two `*-report-github.md` files, or feed the `*-report-full.json` files to
[`ResultsComparer`](https://github.com/dotnet/performance/tree/main/src/tools/ResultsComparer)
(`ResultsComparer --base ../bench-baseline/results --diff ../bench-after/results --threshold 5%`).

For a Signature-only baseline, use the filter `"*SignatureBenchmarks*" "*SignatureCollectionBenchmarks*"` (about 20 minutes on the `short` job with `--inProcess`).

For a `HexBigInteger`-only baseline, use the filter `"*HexBigIntegerBenchmarks*"` (about 6 minutes on the `short` job with `--inProcess`) and a folder such as
`BenchmarkDotNet.Artifacts/baseline-hexbiginteger`. The `[GlobalSetup]` of this suite checks every result against `BigInteger`, so it only runs on a
library that is already correct: a baseline needs the correct code, and cannot be taken from the earlier char parser and formatter (they returned
wrong values for hex above 2^255 and for 4,096-digit output).

For a 256-bit-only baseline, use the filter `"*UInt256*" "*Int256*"` and a folder such as
`BenchmarkDotNet.Artifacts/baseline-256bit` (that folder is git-ignored, so it survives branch switches).

Compare only runs from the same machine, power plan, and .NET runtime. Results are not portable and are
intentionally not committed.

## Trustworthy numbers

Microsecond-and-below benchmarks are easily ruined by other work on the machine. Before a run that you intend to
compare, check `uptime`: if the 1-minute load average is above roughly half the core count, wait. After the run, look at
the `Error` column: if it is more than about 25% of `Mean` for most rows, the run was disturbed and should be repeated.
Run the baseline and the changed code back to back, in the same quiet window.

## Reading the output

* `Mean` is the figure to compare; check `Error`/`StdDev` before trusting a small difference. Differences under
  about 5% on the `short` job are noise.
* `Allocated` is per operation. Anything marked "no allocation" in the XML docs must show `-` here.
* `Json_*` benchmarks call the converters directly; the `JsonSerializer` paths are covered by unit tests.
* `BigInteger_*` rows in the 256-bit suites are references, not library code: they show what the BCL `BigInteger` costs for
  the same operation, so the library's own figures can be judged against it.
* `Dictionary_*` / `HashSet_*` benchmarks in the collection suites loop over all `Count` keys, so divide by `Count`
  for a per-lookup figure.

A row that moves by 30% or more when you did not touch the code under it is usually code generation, not logic. Two causes
seen on `Address`: sub-nanosecond rows (`Equals_*`, `CompareTo_*`) shift by a cycle or two with code alignment, and a hot method
that inlines too much can exhaust the JIT's inline budget so that `AggressiveInlining` callees become real calls (prefixed
UTF-8 parse went from 11 ns to 19 ns that way, and only with tiered PGO on). To tell them apart, re-run the single row, then
run it again with `DOTNET_TieredPGO=0`; if the two differ widely, inspect the Tier1 code with
`DOTNET_JitStdOutFile=<file> DOTNET_JitDisasm="<MethodName>"` and look for `call` instructions where an inlined body was expected.

For an Address-only baseline, use the filter `"*AddressBenchmarks*" "*AddressCollectionBenchmarks*"` (about 10 minutes on the
`short` job) and a folder such as `BenchmarkDotNet.Artifacts/baseline-address`.

Do not run two benchmark sessions at once, and do not rebuild the library while one runs: BenchmarkDotNet builds the benchmark
project it was started from, so a concurrent build or a second run on the same machine corrupts the numbers. If a row looks
impossible (for example a string-returning benchmark at 2 ns with no allocation), repeat that row alone before believing it.
