# RLP security notes

Scope: the `EccentricWare.Web3.DataTypes.Rlp` codec. It answers one question: "is this byte string one canonical RLP item within my budgets?" It does not authenticate, authorise, or understand what the items mean.

## Threat model

Inputs come from untrusted peers (RPC, mempool, P2P, files). The attacker controls every byte and can choose lengths, nesting and item counts. The attacker cannot change the buffer after validation unless the caller lets them (see "Caller obligations").

| Threat | Control | Result |
| --- | --- | --- |
| Out-of-bounds read from a forged length | every input-derived length is compared by subtraction against the remaining span before it is added to an offset (the `int` additions that remain (a header's `ItemEnd`, `itemStart + 1`, `lengthStart + lengthOfLength`) are bounded because the container-bound check and the length checks run first; index entries use `long` arithmetic) | `TruncatedInput`, `InvalidListBoundary`, `LengthOverflow` |
| Integer overflow in 4- and 8-byte length fields | lengths are read as `ulong` and compared against `int.MaxValue` before use | `LengthOverflow` |
| Two encodings of one value (signature or hash malleability) | strict canonical rules, see `docs/rlp-wire-format.md` | `NonCanonicalEncoding`, `NonCanonicalInteger` |
| Stack exhaustion by deep nesting | validation is iterative with an explicit stack sized from `MaxListDepth` (at most 64), never from input | `TooDeep` |
| CPU or memory exhaustion by size or item count | `MaxEncodedBytes`, `MaxItems`; validation is linear in document size | `TooLarge`, `TooManyItems` |
| Trailing or multiple roots | `TryDecodeComplete` demands exactly one root | `TrailingData` |
| Forged child boundaries | there is no public constructor taking bytes, ranges or a kind; a child view exists only by traversing a validated document | n/a |
| Forged index ranges | `RLPChildrenIndex` has no public constructor; `TryGetAt` re-checks every entry against the list and re-reads the header | `InvalidListBoundary` |
| Writing past the destination | every write checks capacity and the parent list's announced limit before the first byte | `DestinationTooSmall`, `InvalidListBoundary` |
| Partial writes after a failure | writes validate first and mutate last | destination and writer unchanged |
| Writer reuse, extra roots, premature end | five-state machine; `default(RLPWriter)` is invalid | `WriterStateInvalid`, `ExtraRoot`, `UnbalancedList` |

## Caller obligations (the parts the codec cannot enforce)

1. **Keep the input stable.** A span does not freeze its array. If another thread or later code mutates the bytes while an `RLPData`, enumerator or index derived from them is alive, the view is no longer trustworthy. The traversal members still never read outside the document, but they may return different items, report `InvalidListBoundary`, or (enumerator only) throw `InvalidOperationException`.
2. **Do not alias a writer's destination with its source.** Overlap is detected and rejected, and in-place encoding is unsupported.
3. **Own the index workspace.** Do not share or recycle it while the index is in use. A single corrupted entry is detected; several coordinated edits to adjacent entries are not. The index is a performance cache, not an authorisation record.
4. **Choose limits per trust level.** `RLPDecodeLimits.Default` (1 MiB, depth 32, 8,192 items) is a starting point for one transaction from an untrusted peer, and it rejects some valid mainnet data (an access list with more than about 8,190 storage keys, a network-form blob transaction with 8 or more blobs, most block bodies and receipt lists); size the limits to the structure you decode (`docs/rlp-integration.md`). Tighten them for anonymous peers, and do not raise `MaxListDepth` above what your protocol needs; the hard cap is 64.
5. **Deny on every non-success status.** Statuses explain a failure; they are not a set of acceptable alternatives. Do not turn a non-canonical or trailing-data failure into a retry with a lenient parser.
6. **Hash the exact received bytes.** Where a signature or identifier binds to bytes (for example a transaction hash), compute it over the original buffer, or over `RLPData.Encoded`. Never re-encode a decoded value and hash that: strict canonicality makes the two equal for accepted input, but the habit hides bugs.
7. **Apply the schema yourself.** The codec does not check field counts, field meaning, EIP-2718 type bytes, chain ids, signature validity or address checksums.

## Behaviour worth knowing

- **Validation cost is paid once.** `TryDecodeComplete` is O(n) in the document. `TryGetChildren` and `TryGetUInt64` do not re-scan. `TryIndexDirectChildren` makes two linear passes over one list's direct children and does not descend.
- **Error precedence is fixed.** For `bf ff ff ff ff ff ff ff ff` the status is `LengthOverflow`; for `b8 37` it is `NonCanonicalEncoding`; for `c1 82` it is `InvalidListBoundary`. Trailing data is reported before the root's contents are examined, so a document with trailing bytes and a malformed child reports `TrailingData`.
- **`TooDeep` can be masked.** When a list is both too deep and too large for its parent, the parent-limit check (`InvalidListBoundary`, or `DestinationTooSmall` at the root) fires first in the writer. Either way nothing is written.
- **Signature fields.** `TryGetEvmLegacySignature` accepts any EIP-155 chain id up to `ulong.MaxValue` and returns it; a larger chain id or a `v` longer than 9 bytes is `IntegerOverflow`, and a `v` that is not 27, 28 or at least 35 is `InvalidSignatureV`. The codec does not compare the chain id with the one you serve, does not range-check `r` and `s` against the curve order, and does not recover a sender: a transaction replayed from another chain decodes successfully, so check the chain id yourself.
- **No constant-time claim.** Decode time depends on the input. Do not use the codec to compare secrets.
- **Thread safety.** The types hold no static mutable state. Reading one validated buffer from several threads is safe if nothing writes it. A writer or an index workspace is single-owner.

## Verification

Security-critical behaviour is covered by tests in `Eccentricware.Web3.DataTypes.Test/Rlp/`:

- prefix families, 55/56 and 255/256 and 65535/65536 length transitions (`PrefixCanonicalityTests`, `BoundaryAndLimitsTests`, `LongFormLengthWidthTests`);
- hostile input, forged workspaces, tampered documents (`AdversarialInputTests`, `RangeIndexProvenanceTests`);
- writer atomicity, state machine, aliasing (`EncoderAtomicityTests`, `WriterStateMachineTests`, `OwnershipAndAliasingTests`);
- an independent, spec-only reference decoder and encoder (`Support/RlpReference.cs`) compared with the production code on vectors and on random and mutated inputs (`ReaderDifferentialTests`, `WriterDifferentialTests`, `TypedAdapter*DifferentialTests`);
- zero-allocation checks (`ReaderAllocationTests`, `WriterAllocationTests`, `TypedAdapter*AllocationTests`). They compare exact `GC.GetAllocatedBytesForCurrentThread` deltas after a warm-up, so they depend on the JIT, tiering and runtime version; they passed here (Linux x64, also with `DOTNET_EnableAVX2=0`, `DOTNET_TieredCompilation=0` and `DOTNET_ReadyToRun=0`) but have not run on ARM64, Windows or macOS, where they are the tests most likely to behave differently;
- an EVM transaction round trip (`EvmTransactionIntegrationTests`) and real signed transactions for chain ids 137, 8453, 42161, 43114 and 98866, generated with Nethereum and embedded as constants (`EvmChainIdTransactionVectorTests`), with an independent `BigInteger` model of the EIP-155 arithmetic (`EvmSignatureOracle`);
- an independent implementation as a second oracle: `Nethereum.RLP` 7.1.0 is a test-only dependency (`NethereumDifferentialTests`). On vectors and on seeded mutations it agrees with this library on accept or reject and on the decoded tree, except for three documented limit-policy patterns (this library enforces a configurable depth, a byte limit and a limits object that Nethereum does not have). No divergence on a wire rule was found;
- a persistent fuzz corpus of about 2,000 inputs (`Rlp/Fuzz/Corpus/`), replayed through `RlpFuzzTarget.Run` on every test run together with 200,000 generated inputs (`RlpFuzzCorpusReplayTests`).

- a checked-arithmetic run: `dotnet build Eccentricware.Web3.DataTypes.Test -c Release --no-incremental -p:RlpCheckedArithmetic=true`, then `dotnet test ... --no-build -p:RlpCheckedArithmetic=true --filter "FullyQualifiedName~Rlp"` compiles the library with C# overflow checking and the RLP suite passes (5,346 tests on Linux x64). The property only matters when the library is compiled, so a `--no-build` run alone proves nothing; `CheckedArithmeticBuildGuardTests` fails when `RLP_EXPECT_CHECKED_BUILD=1` is set and the build is not checked. The deliberate wraps (seven `unchecked(...)` expressions in five files; one of them holds three casts) (low-byte truncation and the `(uint)x < (uint)y` range idiom) are marked `unchecked(...)`; the remaining overflow-checked operations in the RLP namespace were audited by reading, not proven exhaustively. The rest of the library is not checked-arithmetic clean: with the switch on, 1,080 tests of other types fail (`ByteUtils`, `Address`, `int256`, `Signature`, `uint256` and others), which is outside this codec.

The checked run found one real defect, fixed: `RLPWriter.CheckFits` compared `(uint)total > (uint)(limit - _cursor)`, so a negative room (a frame end below the cursor) wrapped to a huge unsigned value and passed the parent-limit check. It was memory-safe, because a second compare against the destination length still held, and reachable only through a tampered frame workspace or a stale copy of the writer, not through input; but it contradicted the documented "never writes past the innermost list" rule, and in a checked build it threw `OverflowException`. It now returns `InvalidListBoundary` (regression tests `Writer_RejectsANegativeRoom_*` and `Writer_RejectsATamperedNegativeRoom_*`). Both earlier independent reviews missed it.

`.github/workflows/ci.yml` builds and tests on Linux x64, Linux ARM64, Windows and macOS for every push and pull request, uploads test results, coverage and an environment and corpus-version record, and runs the checked-arithmetic job; none of it has run on GitHub yet, so those platforms are unverified until it does.

Not covered: a coverage-guided fuzzer run (no fuzzer was available, so the corpus holds seeds derived from the tests and no fuzzer-found cases), runs on ARM64, Windows and macOS (CI is written but unverified), and a differential against go-ethereum (spec 12.4 prefers it; Nethereum stands in). `RLPDecodeLimits` fields are each read once at the start of a decode, so a caller mutating a shared limits variable concurrently cannot change a running decode; this costs about 0.5 to 1 ns on the cheapest decodes (up to +15% on a 6 ns document, +4.8% on average over the measured decode rows, +3% on a legacy transaction; see `docs/rlp-performance.md`). It is a deliberate trade: remove it if you do not want the guarantee. See `docs/rlp-performance.md` for what was and was not measured.
