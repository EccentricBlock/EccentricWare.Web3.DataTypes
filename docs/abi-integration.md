# Integrating ABI with RLP and a signing policy

Where each layer ends, and the EVM adapters that compose RLP and ABI for a signing pipeline (spec 12 and 13, namespace `EccentricWare.Web3.DataTypes.ABI.EvmAdapters`). Specification: sections 3, 8, 9.4 and 10.5 of `docs/specs/ABI_DATA_Design_Specs.md`.

## Where ABI ends

The ABI codec answers one question: "are these bytes exactly one canonical encoding of this trusted schema, within my budgets?" It does not know which contract the bytes are for, which function a selector means there, what a `bytes` argument does, or whether to sign.

| Layer | Owns | Does not own |
| --- | --- | --- |
| RLP (`EccentricWare.Web3.DataTypes.Rlp`) | envelope structure, canonical integers, the `data` item as a byte string | what the bytes mean |
| `RLPCalldataAdapter` | envelope type and form, which item is `to`, `value` and `data`, refusing contexts that are not function calls | policy, schema choice |
| `ContractFunctionResolver` (your data in a library type) | `(chain, contract, verified code identity, selector)` to exactly one `ExecutionDescriptor`; collisions and unknowns fail closed | the bytes, fetching code identities |
| ABI (`ABICallData`) | strict structure of the call under the supplied schema | identity, execution semantics, authorisation |
| `NestedCallWalker` | following only the nested calls a trusted descriptor declares, with call, delegatecall, sender and value semantics; failing closed | deciding what is allowed |
| Your host (`INestedCallHost`) | the code identities of a pinned state snapshot, and the policy decision for every call | decoding |
| Your signer | the signing preimage and the digest binding | decoding |

Each layer keeps its own coordinates and failure contract. RLP ranges are relative to the RLP document, ABI ranges to the arguments (call byte 4), and a nested call taken from a `bytes` payload starts a new coordinate system at its own selector. The only composition is an explicit translation (`ABICallData.TryTranslateArgumentRange`, then the adapter adds where the calldata sits); it bounds-checks and certifies nothing. Take field locations only from schema-driven accessors on a validated view.

## The adapters

Five components, in the core assembly under their own namespace (spec 12 marks a separate assembly as optional; the RLP and ABI layers they compose already live here, and nothing in `ABI/` outside `EvmAdapters/` refers to them). They contain no RPC, no registry storage and no policy.

```csharp
using EccentricWare.Web3.DataTypes.ABI;
using EccentricWare.Web3.DataTypes.ABI.EvmAdapters;

// Startup: trusted metadata (verified source, audited bytecode) becomes an immutable registry. (Compiled and run by
// ABIDocumentationSampleTests.IntegrationDocSnippet_WalksARealUniswapMulticall_AndDeniesWhatThePolicyRefuses.)
ExecutionDescriptor.TryCreateNested(multicallSchema, [NestedCallRule.DelegateCallToSelf([0, NestedCallRule.EachElement])],
    out ExecutionDescriptor? multicall, out _, out _);
ContractFunctionResolver registry = new ContractFunctionResolverBuilder()
    .Add(8453, router, routerCodeIdentity, multicall!)
    .Add(8453, router, routerCodeIdentity, ExecutionDescriptor.NoNestedCalls(exactInputSingleSchema))
    .Add(8453, safe, safeCodeIdentity, ExecutionDescriptor.Undescribed(multiSendSchema)) // known, but never walked
    .Build();
var walker = new NestedCallWalker(registry, NestedCallLimits.Default, ABIDecodeLimits.Default);

// Per request: the owned signing payload (what the signer will hash).
var rlpLimits = RLPDecodeLimits.Default;
if (!RLPCalldataAdapter.TryRead(payload, EvmTransactionForm.SigningPayload, in rlpLimits, out EvmTransactionView tx, out EvmTransactionStatus txStatus))
    return Deny(txStatus);                                       // malformed, unsupported type, wrong form ...
if (tx.ChainId != servedChainId) return Deny("chain");           // the transaction's claim; checking it is yours
var host = new MyHost(stateSnapshot, policy);                    // INestedCallHost: pinned code (EvmAccountCode) + per-call decisions
if (!walker.TryWalk(in tx, signerAddress, ref host, out NestedCallWalkResult result))
    return Deny(result.Status, result.FailedCalldataRange);      // unknown, ambiguous, malformed, undescribed, denied, over limit ...
return Approve(digest: Keccak256(payload));                      // bind the approval to these exact bytes
```

**`RLPCalldataAdapter.TryRead`** validates a legacy, EIP-2930, EIP-1559, EIP-4844 or EIP-7702 envelope in the form the caller declares (`SignedTransaction`, or `SigningPayload`: the unsigned fields a signer hashes) and returns an `EvmTransactionView`:

- **Zero copy.** `Data` is the envelope's own memory at `DataRange` (envelope coordinates, the type byte counted); the tests check this with `Unsafe.AreSame` and against an independent search of the bytes.
- **Only function calls reach the ABI codec** (spec 9.4). `Kind` is `FunctionCall` (four or more data bytes to a destination), `ContractCreation` (empty `to`: initcode), `ValueTransfer` (no data: `receive()` or an account without code) or `Fallback` (one to three bytes). `TryGetFunctionCalldata` hands out calldata only for `FunctionCall`. EIP-712 messages and constructor arguments need their own handling.
- **Layouts**, checked against an independent encoder (Nethereum.Model 7.1.0, signed `GetRLPEncoded` and unsigned `GetRLPEncodedRaw`): legacy 9 fields (`to` 3, `value` 4, `data` 5; unsigned: 6, or 9 with `chainId, 0, 0`), EIP-2930 11 (4, 5, 6), EIP-1559 12 (5, 6, 7), EIP-4844 14 (5, 6, 7), EIP-7702 13 (5, 6, 7); typed signing payloads drop the last three fields. An empty `to` in an EIP-4844 or EIP-7702 transaction is `InvalidTo`. The EIP-4844 network wrapper is `WrongFieldCount`. A form mismatch is refused, never reinterpreted: the two 9-field legacy forms are told apart because a signed transaction's `r` and `s` are never zero and a payload's placeholders always are.
- **Fields read**: the chain id (`ulong`, from field 0, from `v` (EIP-155) or from field 6 of the payload; `null` without replay protection), the nonce (`ulong`; 2^64-1 and above is `InvalidNonce`, EIP-2681), `to`, `value` (`uint256`), `data`, the signature fields' validity (`yParity` or `v`, and `r` in 1..n-1 and `s` in 1..n/2 for secp256k1 order n: EIP-2 made a high `s` invalid), the EIP-4844 blob versioned hashes (non-empty, 32 bytes each, version byte `0x01`: `InvalidBlobHashes`), and every EIP-7702 authorization tuple (`[chain_id, address, nonce, y_parity, r, s]` within the EIP's bounds; an empty list is `InvalidAuthorizationList`). Every other field is checked only for its RLP kind. The chain id is the transaction's claim: comparing it with the chain served is yours.
- **Coordinates.** `TryMapCalldataRange` turns a calldata range (from `ABICallData.TryTranslateArgumentRange`, or `NestedCall.CalldataRange`) into an envelope range; it refuses ranges outside the data and certifies nothing.

**`ContractFunctionResolver`** (built with `ContractFunctionResolverBuilder`) binds `(chain id, contract, code identity, selector)` to exactly one `ExecutionDescriptor`. Failures are distinct: `UnknownContract`, `UnknownCodeIdentity` (upgraded proxy, changed delegation), `UnknownFunction` (a public four-byte database match changes nothing), `Ambiguous` (two registrations share the selector: a real collision such as `burn(uint256)` and `collate_propagate_storage(bytes16)`, both `0x42966c68`, or a duplicate). The code identity is a `Hash32` your host computes from verified state, in one convention for the whole registry: typically the Keccak-256 of the runtime code; for a proxy, a digest that also covers the implementation. An EIP-7702 delegated account is not registered itself: the walker resolves a call to it by its delegate's address and the delegate's code identity, so a delegate's functions are registered once, under the delegate's address, for every account delegated to it. The library never fetches code.

**`ExecutionDescriptor`** is the trusted metadata for one function: `NoNestedCalls` (its `bytes` arguments are data), `Undescribed` (it executes something no rule can describe, such as a packed command stream; the walker refuses it), or `TryCreateNested` with `NestedCallRule`s, each checked against the function's schema at bootstrap:

| Rule | Child | Deployed examples (verify against what you register) |
| --- | --- | --- |
| `DelegateCallToSelf(calldataPath)` | the code at the caller's own address, in the caller's context, with its sender and value | OpenZeppelin and Uniswap `multicall(bytes[])`: `[0, EachElement]` |
| `Call(targetPath, calldataPath)` | a `CALL` to an argument address, sender = the calling contract, no value | Multicall3 `aggregate3((address,bool,bytes)[])`: `[0, Each, 0]`, `[0, Each, 2]` |
| `CallWithValue(targetPath, calldataPath, valuePath)` | the same, with a value argument | Multicall3 `aggregate3Value`; ERC-4337 `executeBatch(address[],uint256[],bytes[])` (parallel arrays must line up) |
| `CallOrDelegateCall(targetPath, calldataPath, valuePath, operationPath)` | 0 = `CALL`, 1 = `DELEGATECALL`, anything else fails closed | Safe `execTransaction(...)`: `[0]`, `[2]`, `[1]`, `[3]` |

A descriptor is a claim by whoever builds the registry; the library cannot verify it. A wrong descriptor makes the walker miss or invent calls.

**`NestedCallWalker.TryWalk`** resolves the transaction's call, validates it strictly, asks the host, and then follows only the declared nested calls, each resolved by the code that runs (for a `DELEGATECALL`, the target's code in the caller's context), validated against its own schema and evaluated, depth first, parent before children. Each `NestedCall` records the target address (the address the call names), the code address (the target, or its EIP-7702 delegate; `IsDelegated`), the context address (`address(this)`), sender, value (inherited, not transferred, under `DELEGATECALL`: `TransfersValue` is false), call type, parent, depth, code identity, descriptor and the exact calldata range in the transaction's calldata. Every failure stops the walk: `NotAFunctionCall` (including a nested call shorter than a selector), `MissingChainId`, `AuthorizationsNotProcessed` (an EIP-7702 transaction walked without its own processed authorization list, see below), `CodeIdentityUnavailable`, `NoCode` (the target, or its delegate, has no code), `UnsettledDelegation` (the account's code at inclusion depends on its nonce: a pending tuple, or a sponsored re-delegation), `ChainedDelegation` (a delegate that is itself delegated; the EVM follows one hop and would fail), `UnknownContract`, `UnknownCodeIdentity`, `UnknownFunction`, `AmbiguousFunction`, `MalformedCalldata` (with the ABI status), `UndescribedExecution`, `DescriptorMismatch`, `UnsupportedCallMode`, `DepthLimitExceeded`, `CallLimitExceeded` (`NestedCallLimits.Default`: 256 calls, the spec's `MaxNestedExecutableCalls`, and depth 8; hard maximum depth 16, which also bounds the walker's recursion) and `DeniedByHost`. `FailedCalldataRange` says where. With a struct or `ref struct` host a walk allocates nothing.

The host reports each account's code BEFORE the transaction with `TryGetCode`: `EvmAccountCode.Empty`, `EvmAccountCode.Contract(identity)`, or `EvmAccountCode.DelegatedTo(delegate)` for an EIP-7702 delegation indicator (never a contract whose identity is the indicator's hash). Returning false, or `default` (Unknown), fails the walk closed.

**What it does not prove** (spec 8.3): that no other call happens. Calls made by code paths, storage reads, proxies or off-chain data are invisible to calldata analysis, and a descriptor covers only what its author declared. The walker is not a simulator and not a policy.

### EIP-7702 transactions

An EIP-7702 (type 4) transaction carries an authorization list: each tuple, signed by an authority, sets the authority's code to a delegation indicator (or clears it, for the zero address) BEFORE the call runs, and the delegation persists after the transaction. Two steps, both required:

```csharp
// 1. Apply the list as the EIP does, against your pinned state. The host recovers each tuple's signer (secp256k1 over the
//    library's signing hash) and reports an authority's nonce and code; the library does the rest. (Compiled and run by
//    ABIDocumentationSampleTests.IntegrationDocEip7702Snippet_ProcessesJudgesAndWalksACastBuiltTransaction.)
var entries = new EvmAuthorization[tx.AuthorizationCount];  // or stackalloc for a short list
if (!EvmAuthorizationProcessor.TryProcess(in tx, signerAddress, ref host, entries, out int written, out EvmAuthorizationStatus authStatus))
    return Deny(authStatus);                                  // RecoveryUnavailable, AccountStateUnavailable ...
foreach (EvmAuthorization a in entries)
    if (a.IsRecovered && !TrustedDelegate(a.DelegateAddress, a.Authority))
        return Deny("delegation");                            // judge every tuple that could apply, not only Applied ones

// 2. Walk with the processed list: calls to an authority of an Applied entry run its delegate's code.
if (!walker.TryWalk(in tx, signerAddress, entries, ref host, out NestedCallWalkResult result))
    return Deny(result.Status, result.FailedCalldataRange);
```

- **The processor** checks each tuple in the EIP's order and records its `Outcome`: `WrongChain` (neither 0 nor the transaction's chain), `NonceOutOfRange`, `InvalidSignature` (`y_parity` above 1, `r` outside 1..n-1, `s` outside 1..n/2), `NotRecoverable`, `AuthorityHasCode`, `NonceMismatch`, `Superseded` (valid, but a later tuple of the same authority won) or `Applied`. The sender's nonce is the transaction's nonce plus one (the EIP increments it first), so a self-sponsored tuple needs that value; the processor uses it instead of asking the host. `IEvmAuthorizationHost.RecoverAuthority` answers `Recovered`, `NotRecoverable` (the tuple is skipped, as the EIP says) or `Unavailable` (fails closed); one host type can implement both host interfaces, since `TryGetCode` has the same contract in both.
- **State changes before inclusion.** Outcomes are exact for your pinned state only. Only the sender's tuples are fixed by the transaction itself; an authority other than the sender can reach the nonce of a skipped tuple before inclusion (`IsPending`: the tuple would then apply) or spend the nonce an `Applied` tuple needs (the tuple then fails and the pinned code stays). The walker refuses both where they change the code that runs (`UnsettledDelegation`): an account with a pending tuple, and an applied tuple of another authority whose pinned code is neither empty (a failed first delegation runs nothing) nor the same delegation (a failed first delegation runs nothing, so nothing the policy approved is exceeded, though effects it counted on, such as reimbursing a sponsor, may not happen). So a relayer can sponsor a first delegation, but a sponsored re-delegation to another delegate is refused; the authority can send that one itself. Pin nonces at the `pending` block tag, as wallets do when they fill a tuple's nonce: pinning at `latest` makes every authority with a transaction in flight look pending. Beyond the list, a delegated account's code, like a proxy's implementation, is in the hands of whoever controls it until inclusion. `IsRecovered` marks every tuple that names an authority: judge all of them, because each is a delegation the transaction can install.
- **The walker** refuses an EIP-7702 transaction without its processed list, with a list of the wrong length, with an unprocessed entry, with entries whose tuples differ from the envelope's, or with entries processed for another sender, transaction nonce or chain (`AuthorizationsNotProcessed`). Delegations that existed before the transaction come from the host (`EvmAccountCode.DelegatedTo`) and are followed for every transaction type. A value transfer or fallback call to a delegated account also runs the delegate's code; the walker refuses those kinds (`NotAFunctionCall`) as before, so your policy must handle them.
- **Not done here**: signature recovery (the host's; the library has no secp256k1 code), and deciding which delegates to trust.

### What the tests establish

`RLPCalldataAdapterTests`, `ContractFunctionResolverTests`, `ExecutionDescriptorTests`, `NestedCallWalkerTests` and `Eip7702Tests` (in `Eccentricware.Web3.DataTypes.Test/ABI/EvmAdapters/`); nested calldata there is built with the test-only independent encoder, not the library's writer, and every reported range is checked against the memory the validated view covers. `Eip7702Tests` uses tuples and two complete transactions signed by Foundry `cast` (alloy) with Anvil's public development keys, recovered by Nethereum.Signer over the library's signing hash. `ABISecurityContractTests` runs the spec 10.5 flow on these library types; only the policy and the signing boundary (a Keccak-256 digest over the exact envelope, chain and signer) are test-local, as spec 10.5 allows, and only the digest-binding test is marked `IllustrativeFakes`.

## Resource choices

Pick the RLP limits for the transaction envelope and the ABI limits for the calldata separately. `ABIDecodeLimits.Default` (256 KiB, depth 16, 4,096 elements, 16,384 values, 100,000 work units) suits one call from an untrusted submitter; a transaction's `data` is also bounded by the envelope's `MaxEncodedBytes`. Reject oversized input before allocating or hex-decoding it, decode hex exactly once, and keep the buffer owned and unchanged from validation to signing (copy once at ingress if you cannot rule out mutation).

## Existing datatypes: what the codec reuses and why

The original request asked to investigate and outline how the codec fits the library's existing value types. The codec core (`ABIData`, `ABICallData`, the writers) works on spans and `ABIRange` and knows no value type, on purpose: its contract is exact structure, and a value type adds a conversion and a failure mode. The one place the types meet is `ABITypedExtensions` (`docs/abi-api.md`), plus `ABIFunctionSchema.FunctionSelector` / `ABICallData.FunctionSelector`, and the selector derivation, which is `FunctionSelector.FromSignature` (one Keccak implementation in the library).

| Type | What the ABI layer uses | What it deliberately does not | Hazards at the boundary |
| --- | --- | --- | --- |
| `uint256` | `TryGetUInt256` / `TryWriteUInt256`: the value of any `uint<M>` field (zero-extended), written with a width check against M. The amount type for ERC-20 fields | `uint256` arithmetic inside the codec (none: words are compared and range-checked as bytes) | `+`, `-`, `*` and the implicit `int`/`long` conversions throw `OverflowException` in the caller's expression before the codec is entered. An ERC-20 amount must be read as `uint256`, never with `TryGetUInt64` (approve(max), 1e20 wei) |
| `int256` | `TryGetInt256` / `TryWriteInt256`: any `int<M>`, sign-extended | signed arithmetic | the same call-site conversions; `fixed<M>x<N>` has NO typed form (it needs its scale, read it with `TryGetSignedScaledWord`) |
| `Address` | `TryGetAddress(.., out Address)` / `TryWriteAddress(Address)` and the `function` target: an EVM address from the 20 low-order bytes | `Address` parsing or checksum casing (the decoder compares bytes; casing is presentation); Solana addresses | `default(Address)` is a VALID zero EVM address, so a caller must test the returned `bool`, never the value. A Solana-type `Address` cannot be an ABI address: the writer reports `InvalidArgument` (like a wrong-length span). The 12 high bytes were proven zero at decode time and are not re-checked by the typed read |
| `Hash32` | `bytes32` as a `Hash32` (`TryGetBytes32` / `TryWriteBytes32`, same bytes, same order) | hashing (Keccak is used by the selector derivation only) | only `bytes32` maps: another `bytes<M>` is `TypeMismatch`. A `bytes32` is not necessarily a hash (a storage slot, a role id, an arbitrary tag) |
| `FunctionSelector` | `bytes4` (`TryGetBytes4` / `TryWriteBytes4`), the selector of a `function` value, `ABIFunctionSchema.FunctionSelector`, `ABICallData.FunctionSelector`, and the Keccak derivation (`FromSignature`) | `FunctionSelector.FromCalldata` / `TryFromCalldata` and `HexBytes.TryGetFunctionSelector`: the ABI layer takes the selector from the validated call, and a selector read from raw calldata is a candidate, not an identity | a `bytes4` is not necessarily a function selector (an ERC-165 interface id, the XOR of selectors, is also a `bytes4`). A selector never identifies a function: collisions are real (`burn(uint256)` and `collate_propagate_storage(bytes16)` are both `0x42966c68`) |
| `HexBytes` | nothing in the codec. It is the natural INGRESS container for calldata text | `HexBytes.GetCalldataParamsSpan()` and `TryGetFunctionSelector`: not an ABI entry point | `GetCalldataParamsSpan()` returns an EMPTY span for 4 bytes or fewer, which is also the valid argument document of a zero-argument function, so passing it to `TryDecodeComplete` would accept a 1-byte or 3-byte "call" for a function without arguments: for calls always give `ABICallData.TryDecode` the WHOLE calldata. `HexBytes.TryParse` accepts odd-length hex (left-padded with a `0`), which is wrong for calldata: ingress must require an even number of digits (after an optional `0x`) and a maximum length before decoding |
| `Signature` | nothing | everything: a signature is not part of ABI data. `Signature` belongs to the transaction envelope (the RLP layer) | no ABI relevance; `Signature.FromEvmBytes` and `FromEvmComponents` throw `ArgumentException` for a wrong length, so they must not be called on untrusted lengths without a prior check |

`HexBigInteger` (the JSON-RPC quantity format: minimal digits, no leading zeros) is a different encoding from an ABI word (32 bytes, fixed width, zero- or sign-extended); converting between them is the caller's job and an easy place to mis-size an amount. `InstructionDiscriminator` is the Solana analogue of a selector and has no ABI use.

A typical request path, with each conversion at the boundary where its failure mode is handled:

1. Ingress: hex text to bytes ONCE, with a length cap and an even digit count (`HexBytes.TryParse` alone does not enforce either), into an owned buffer.
2. RLP envelope: `RLPCalldataAdapter.TryRead`, which returns the `data` item as a span of the same buffer.
3. For an EIP-7702 transaction, `EvmAuthorizationProcessor.TryProcess` and a policy decision on its entries. Then `NestedCallWalker.TryWalk` (or, without nesting, `ABICallData.TryDecode(wholeCalldata, trustedFunction, ...)` with the function your registry resolved): strict structure under the trusted schema.
4. Typed reads (`TryGetAddress(out Address)`, `TryGetUInt256`) for the policy comparison, with the returned `bool` tested.
5. Digest binding over the exact envelope bytes.

