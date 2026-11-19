# ABI fuzz corpus

This folder holds **only inputs that a fuzzer or a reviewer found** (a crash, an invariant violation, a divergence from the reference model), one input per
file: hex text on a single line, extension `.hex` (no `0x` prefix, no separators). It is empty until such an input exists. Every `.hex` file here is
replayed by `ABIFuzzCorpusReplayTests` on every `dotnet test`, so each one is a permanent regression test. The folder is copied to the test output by
the `<None Update="ABI\Fuzz\Corpus\**" ...>` item of the test project.

The ~1,900 **generated seeds** (fixtures, structured mutations, hostile shapes) are *not* stored: `ABIFuzzCorpus.Seeds()` generates them deterministically in
memory at test time and the replay tests run every one of them through the full target (the same checks as before; a stored copy compared with its own
generator proved nothing and cost 9.7 MB in the repository). The naming scheme below is that of the generated seeds and is the convention for found files.

## Three targets, one format each

The target is chosen by the file-name prefix (a coverage-guided harness calls the entry point it wants directly; see "Hooking a fuzzer").

| Prefix | Entry point | Input bytes |
| --- | --- | --- |
| `schema-` | `ABIFuzzTarget.RunSchemaText` | The text of a function signature or tuple, one byte per character (Latin-1). |
| `script-` | `ABIFuzzTarget.RunWriterScript` | `[schema index][event]*`, the event format of `ABIFuzzScript` (opcode low nibble plus a fixed-width payload; every byte string parses). |
| everything else | `ABIFuzzTarget.Run` | `[schema index][document]`: byte 0 selects the schema from the **append-only** registry `ABIFuzzSchemas.All` (`index % count`), the rest is the encoded arguments. |

Appending an entry is safe for the generated seeds, but it changes which schema an externally mutated input selects when its first byte is at least the old count (`index % count`). Reordering or removing an entry invalidates found files: every document and script seed records the schema index in its first byte
and the schema name in its name, and `EverySeedName_StatesTheSchemaAndStatusTheLibraryGivesIt` fails when they disagree.

## What the targets check

`Run` decodes the document under six limit sets (default, tight, minimal, size minus one, `default(ABIDecodeLimits)`, and a generous set), and for each:
status and diagnostic overloads agree; a failure resets the view; the status obeys the spec 5.2 precedence (invalid limits, size, structure);
`ABICallData.TryDecode` of the same bytes with the right selector, a flipped selector and no selector agrees with that precedence; acceptance under
a stricter set implies acceptance under the generous one; the decode calls allocate nothing (after one warm-up, three tries). On the generous set the
verdict must equal the independent reference model (`ABIReferenceModel.cs`: permissive decode plus canonical re-encode). An accepted document is then
read through every locator path and typed accessor (a failing accessor returns defaulted outputs and a legal status; invalid paths give
`InvalidPath`), compared with the reference values, and re-encoded from the accessor values through `ABIWriter`, `ABIMeasurer` and `ABICallEncoder`
(equal lengths, identical bytes, bytes beyond the document untouched, zero allocation, one byte over the budget and one byte short of destination refused,
the equal encode limits accept exactly what the equal decode limits accept).

`RunWriterScript` replays the events through writer, measurer and call encoder in lockstep under three encode limit sets (statuses equal at every step, a
failing step changes no destination byte, everything after a failure answers `InvalidWriterState`, `TryFinish` agrees, bytes past the document are
untouched, replay allocates nothing); a script that completes is then checked exactly like a document (strict decode under the equal budgets, reference verdict, re-encode).

`RunSchemaText` parses and compiles the text as a tuple and as a function signature; a success must be canonical (its canonical text compiles to itself with the
same selector) and its selector must equal the first four bytes of Nethereum's independent Keccak-256 of the canonical signature.

A violated invariant, or any exception from the codec, becomes an `InvalidOperationException` whose message ends with the input as `0x<hex>` (cut after
8,192 bytes with the total length stated); a fuzzer reports that exception as the crash.

## Naming

`<category>-<schema>-<Status>-<detail>.hex` for documents, `script-<category>-<schema>-<Status>-<detail>.hex` for scripts (`Status` is what the writer
returns at the roomy encode limits), `schema-<Status>-<NNN>-<detail>.hex` for text (`Status` is that of `TryCompileFunctionSignature`). For documents `Status`
is what `ABIData.TryDecodeComplete` returns under `ABIDecodeLimits.Default`; the name is checked against the library, and the accept/reject verdict is checked
against the reference model by the target, so a name can pin a failure kind but never bless a wrong verdict.

| Prefix | Source |
| --- | --- |
| `fixture-` | Every independently produced fixture (`cast`, Solidity docs, real ERC-20, Uniswap V3, multicall, Seaport shapes) |
| `reference-` | For every registry schema: the reference encoder's minimal value (one element per array) and, except for the deep schemas, two seeded random values |
| `truncated-`, `trailing-`, `padding-` | Prefixes (0, 1, 31, every word boundary, size minus one), appended bytes/words, a non-zero last byte, of 16 structurally different fixtures |
| `word-` | Each of the first 12 words of those fixtures replaced by 0, 0x20, 0x21, 2^31, 2^256-1, or a set high byte |
| `insert-`, `delete-` | A zero word inserted before, or a word removed at, each of the first 8 word positions |
| `hostile-` | Counts and lengths of 2^256-1, 2^255, 2^31, `int.MaxValue`, 4097, 4096; zero-width element arrays; aliased and reversed pointers; deep schemas at and over the frame tiers |
| `limit-` | Array counts at the tight element cap (16) and the default one (4,096), byte lengths at the tight (4,096) and default (262,144) caps, and the default visited-value budget (16,384) to the value (counts totalling 16,378 accepted, 16,379 refused). The tight value (64) and work (256) budgets are not boundary-seeded (the element cap fires first for these shapes); `ABIDecodeLimitsTests` pins them |
| `script-fixture-`, `script-mutated-`, `script-value-` | The event script of every fixture, edits of eight of them (drop, duplicate, swap, wrong count), and wrong values for the next slot |
| `schema-` | Real signatures, aliases, canonical tuples, malformed text, whitespace and names, 31/32/33/200 nested tuples, 1,024/1,025/5,000 members, 4,096/4,097-element and 8,192-word arrays |

## Adding a found input

A crash found by a fuzzer or a reviewer may be added by hand (and is replayed with the generated seeds):

1. Take the input hex from the exception message (or `xxd -p crash-file | tr -d '\n'` for a fuzzer's crash file).
2. Save it as `<category>-<schema>-<Status>-<detail>.hex` here (one line, trailing newline), with the first byte the registry index for document and script files.
   The name must not reuse the name of a generated seed (a test checks it) and the status in it is checked against the library.
3. Run `dotnet test Eccentricware.Web3.DataTypes.Test -c Release --filter "FullyQualifiedName~ABIFuzzCorpusReplayTests"`. The file must fail before the fix and pass after it. Fix the defect (or the oracle, if the oracle is wrong) in the same change.
4. Do not delete found files to make a test pass.

To change the generator on purpose, edit `ABIFuzzCorpus.cs`; there is nothing to regenerate or commit.

## Hooking a coverage-guided fuzzer (documentation only)

The targets live in the test assembly (they use the reference model and the MSTest-based helpers). In a separate harness project that references the test assembly (it has
`InternalsVisibleTo` for the library, not for a harness: link the files `ABI/Fuzz/*.cs`, `ABI/ABIReferenceModel.cs`, `ABI/ABIReferenceAssertions.cs`, `ABI/ABIWriteTestSupport.cs`,
`ABI/ABIDecodeTestSupport.cs`, `ABI/ABISchemaTestSupport.cs`, `ABI/ABIFixtureCatalog.cs`, `ABI/ABIFixtures.cs`, `ABI/ABIWriteFixtures.cs` and reference MSTest and `Nethereum.ABI`):

* SharpFuzz with AFL++: instrument `EccentricWare.Web3.DataTypes.dll`, then `Fuzzer.Run(stream => { var bytes = ReadAll(stream); ABIFuzzTarget.Run(bytes); });` (one executable per target), run with `afl-fuzz -i seeds -o findings -- dotnet harness.dll`.
* SharpFuzz with libFuzzer: `Fuzzer.LibFuzzer.Run(span => ABIFuzzTarget.Run(span));`.
* AFL and libFuzzer want raw bytes: export the generated seeds with `ABI_FUZZ_EXPORT_SEEDS=seeds dotnet test Eccentricware.Web3.DataTypes.Test -c Release --filter "FullyQualifiedName~TheGenerator_IsDeterministic"` (raw bytes, one file per seed), and add found files with `for f in *.hex; do xxd -r -p "$f" "seeds/${f%.hex}"; done`.

No coverage-guided fuzzer is installed where this corpus was created (`afl-fuzz`, `libfuzzer-dotnet` and `sharpfuzz` are not on the path), so **no coverage-guided run has happened**. The seed
set is generated (fixtures, structured mutations, hostile shapes), not found by a fuzzer, and this folder is empty. The only randomised coverage is the seeded generative tests in
`ABIFuzzCorpusReplayTests` (the RLP-to-ABI adapter and nested-call legs of spec 10.4 are likewise seeded mutation tests, `RLPCalldataAdapterTests.MutatedEnvelopes_*` and `NestedCallWalkerTests.MutatedNestedCalldata_*`, not coverage-guided targets) and the seeded mutation tests in `ABIDecodeDifferentialTests` and `ABIWriterDifferentialTests`.
