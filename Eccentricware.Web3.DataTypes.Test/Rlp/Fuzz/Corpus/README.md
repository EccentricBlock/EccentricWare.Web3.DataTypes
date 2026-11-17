# RLP fuzz corpus

One input per file, hex text on a single line, extension `.hex` (no `0x` prefix, no separators, either case). Every file is replayed
through `RlpFuzzTarget.Run` by `RlpFuzzCorpusReplayTests` on every `dotnet test`, so each file is a permanent regression test.
The folder is copied to the test output by the `<None Update="Rlp\Fuzz\Corpus\**" ...>` item in the test project; the replay test fails
if the folder or `MANIFEST.txt` is missing, if any category holds fewer files than the manifest records, or if the total is lower (see
"Manifest" below), so a broken copy rule or a deleted file cannot pass silently.

## What `RlpFuzzTarget.Run(ReadOnlySpan<byte>)` checks

It never throws when the codec is correct. For the default limits, a tight limit set, a size limit one byte under the input and
`default(RLPDecodeLimits)` it requires: the same status as the independent oracle `RlpReference` (and the same offset where the oracle
marks the offset as normative); a fully reset result on failure; on success a walk of the whole tree with the enumerator and with the
index that agree with the oracle (document-relative ranges, contiguity, kinds, payload bytes, integer accessors, `TryCopyEncodedTo`,
index capacity behaviour, and the typed readers `TryGetUInt256`, `TryGetHash32`, `TryGetAddress`, `TryGetOptionalAddress` and
`TryGetFunctionSelector` against their width rules); for every consecutive triple of direct children of every list with at least three
children, `TryGetEvmLegacySignature` and `TryGetEvmYParitySignature` against the BigInteger-only oracle `EvmSignatureOracle` (status, parity,
chain id, 32-byte r and s), and on success a rebuild with `TryWriteEvmLegacyV` / `TryWriteEvmYParity` (parity and `Signature` overloads, the latter
with V in the 0/1 and 27/28 forms), `TryWriteUInt256` and the sizers, which must emit exactly the sized number of bytes, equal to the source items; and re-encoding with `RLPWriter` (value by value and with `TryWriteEncoded`) byte-identical to the input.
A violated invariant, or any exception from the codec, becomes an `InvalidOperationException` whose message ends with the input as
`0x<hex>` (cut after 8,192 bytes, with the total length stated). A fuzzer reports that exception as the crash.

## Naming

`<category>-<Status>-[lim-b<bytes>-d<depth>-i<items>-]<detail>.hex`. `<Status>` is the `RLPStatus` the oracle and the library give the
input (checked by `EveryCorpusFileName_StatesTheStatusTheOracleAndLibraryGiveIt`); without a `lim-` token the limits are
`RLPDecodeLimits.Default`, with one the file is replayed under exactly those limits as well (`RlpFuzzTarget.Run(input, limits)`), so depth, size and
item boundary cases run at their intended limits. `canonical-` and `known-ethereum-` files are always accepted; `integer-<Status>-` files are valid
generic RLP whose `TryGetUInt64` status is `<Status>`; `divergence-<pattern>-lim-...` files are re-compared with Nethereum under their limits and
must reproduce `<pattern>`. `signature-Success-<legacy|typed>-<ReaderStatus>-<detail>` files are documents that decode successfully (the first
token is the decode status, like every other category) and hold exactly three items; the next two tokens name the signature reader
(`TryGetEvmLegacySignature` or `TryGetEvmYParitySignature`) applied to those items and the `RLPStatus` it returns, which the name check compares with the
library and with `EvmSignatureOracle`. File names stay under 110 characters and differ by more than case (Windows and macOS).

| Prefix | Source |
| --- | --- |
| `canonical-` | `RlpVectors.Canonical`: spec 2.3, all 128 literals, all 128 one-byte strings, 55/56 and 255/256 transitions, 65535/65536 strings |
| `known-ethereum-` | `RlpVectors.KnownEthereum` (ethereum/tests `rlptest.json` and ethereum.org examples) |
| `spec-12-2-` | every row of the spec 12.2 negative table |
| `invalid-` / `invalid-nested-` | other hand-stated rejections, and the same rejections wrapped in one and two lists |
| `ambiguous-` | rows where the spec leaves the check order open (the oracle's reading) |
| `limit-` | inputs of the limit rows (depth, size and item budgets at limit-1, limit, limit+1) |
| `single-byte-`, `list-one-byte-child-` | every one-byte input, and `c1 xx` for every `xx` |
| `integer-` | integer-accessor rows (leading zero, overflow, raw `00`, wrong kind) |
| `hostile-` | patterns of `AdversarialInputTests`: depth bombs, item-budget bombs, the long-form length attack corpus, all-`ff` lengths, child crossing its parent at every depth, opaque payloads that look like headers, strict prefixes and trailing bytes of valid documents. Very large instances (100,000 levels, four million children, 900 KB strings) are not stored; those tests build them in memory |
| `evm-` | signed transactions and signing preimages from `EvmChainIdTransactionVectorTests`, with and without the EIP-2718 type byte, and the EIP-155 example |
| `signature-` | signature-reader edge cases: legacy `v` of empty, raw `00`, `001b`, 1, 26..29, 31, 34..38, 127, `81 80`, 197767/197768 (Plume), the 9-byte maximum-chain values, a 9-byte chain above `ulong`, a 10-byte `v`; typed y parity empty, `01`, `02`, `1b`, raw `00`; `r`/`s` with a leading zero, 33 bytes and a list |
| `divergence-<pattern>-` | one file per divergence pattern against Nethereum.RLP (see `NethereumDifferentialTests` and `NethereumDivergenceClassifier.DocumentedPatterns`) |

## Manifest

`MANIFEST.txt` records, per category prefix, how many files the folder had when it was generated, and the total. `RlpFuzzCorpusReplayTests`
requires every category to hold at least its recorded count and the folder at least the recorded total, so deleting files fails and adding files
passes. A file counts under the first prefix of the list in `RlpFuzzCorpusReplayTests.ManifestCategories` (longest first, so `invalid-nested-` is
not counted under `invalid-`). The check is a count, not a hash: replacing one file by another of the same category is not detected.

**When you add corpus files, regenerate the manifest** (raise the numbers; never lower one to hide a deletion). For example, from this folder:

```
python3 - <<'PY'
import os
cats = ["list-one-byte-child-", "known-ethereum-", "invalid-nested-", "single-byte-", "divergence-", "signature-", "canonical-",
        "ambiguous-", "spec-12-2-", "integer-", "hostile-", "invalid-", "limit-", "evm-"]
n = {c: 0 for c in cats}
for f in sorted(os.listdir('.')):
    if f.endswith('.hex'):
        n[next(c for c in cats if f.startswith(c))] += 1
lines = open('MANIFEST.txt').read().splitlines()
head = [l for l in lines if l.startswith('#')]
open('MANIFEST.txt', 'w').write('\n'.join(head + [f"{c} {n[c]}" for c in sorted(cats)] + [f"TOTAL {sum(n.values())}"]) + '\n')
PY
```

A new category needs a new entry in `ManifestCategories`, in the test's category lists and in this README.

## Turning a crash or divergence into a corpus file

1. Take the input hex from the exception message (or the fuzzer's crash file: `xxd -p crash-file | tr -d '\n'`).
2. Save it as `<category>-<what-it-is>.hex` in this folder (one line, trailing newline), then regenerate `MANIFEST.txt` (see "Manifest"). Use `divergence-<pattern>-...` for a new
   disagreement with another implementation, and add the pattern with its reason to `NethereumDivergenceClassifier.DocumentedPatterns`.
3. Run `dotnet test Eccentricware.Web3.DataTypes.Test -c Release --filter "FullyQualifiedName~RlpFuzzCorpusReplayTests"`. The new file must
   fail before the fix and pass after it. Fix the production defect (or the oracle, if the oracle is wrong) in the same change.
4. Do not delete corpus files to make a test pass.

## Hooking the target to a coverage-guided fuzzer (documentation only)

`RlpFuzzTarget.cs` itself has no MSTest dependency, but the signature oracle it calls reaches MSTest through two test helpers (below), so the harness project needs the MSTest assertion package or the test assembly reference. In a separate harness project that references the test assembly or links
`RlpFuzzTarget.cs`, `Support/RlpReference.cs`, `EvmSignatureOracle.cs` (and the test helpers it uses, `TypedAdapterTestSupport.cs` and
`RlpReaderTestSupport.cs`, which reference MSTest) and the library:

* SharpFuzz with AFL++: instrument `EccentricWare.Web3.DataTypes.dll` with `sharpfuzz`, then build an executable whose `Main` is
  `Fuzzer.Run(stream => { var bytes = ReadAll(stream); RlpFuzzTarget.Run(bytes); });`, and run `afl-fuzz -i Corpus-as-binary -o findings -- dotnet harness.dll`.
* SharpFuzz with libFuzzer: `Fuzzer.LibFuzzer.Run(span => RlpFuzzTarget.Run(span));` and run it under `libfuzzer-dotnet`.
* AFL and libFuzzer want raw bytes: convert the seeds with `for f in *.hex; do xxd -r -p "$f" "seeds/${f%.hex}"; done`.

No coverage-guided fuzzer is installed in the environment where this corpus was created (`afl-fuzz`, `libfuzzer-dotnet` and `sharpfuzz` are
not on the path), so **no coverage-guided run has happened**. The corpus holds seeds derived from the test vectors and hostile-input
patterns, not inputs found by a fuzzer. The only randomised coverage is the seeded generative test in `RlpFuzzCorpusReplayTests`
(200,000 random and mutated inputs through the same target) and the seeded mutation tests in `NethereumDifferentialTests`.
