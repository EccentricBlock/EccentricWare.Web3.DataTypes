# EccentricWare.Web3.DataTypes

A compact, allocation-minimal set of immutable value types for representing blockchain primitives in .NET 10.  

Designed for **very low CPU and memory overhead**, with each type fully independent and optimised for JSON-RPC responses, calldata, and event data.

---

## Overview

This library provides **native C# value types** for common EVM and Solana primitives in a unified structure, focusing on:

- Zero/low allocation parsing and formatting
- Predictable memory layouts
- Span-based APIs (`ReadOnlySpan<char>`, `ReadOnlySpan<byte>`)
- Explicit opt-in zero-copy paths
- `JsonConverter` implentations for all types
- EF Core `ValueConverter` implentations for all types

### Included types
- `HexBytes`
- `Hash32`
- `Address`
- `FunctionSelector`
- `Signature`
- `HexBigInteger`
- `uint256`, `int256`

All types are immutable, thread-safe, and optimised for high-throughput pipelines.

---

## Install

```powershell
dotnet add package EccentricWare.Web3.DataTypes
````

---

## Usage

### Parsing and formatting (zero-allocation friendly)

```csharp
var hb = HexBytes.Parse("0xdeadbeef");

ReadOnlySpan<byte> utf8 = "0xdeadbeef"u8;
HexBytes.TryParse(utf8, out var parsed);

Span<char> buf = stackalloc char[hb.HexLength + 2];
hb.TryFormat(buf, out _);
```

### Signatures (EVM and Solana)

```csharp
// Hex (130 digits = EVM r||s||v, 128 digits = Solana) or Solana Base58; a 0x prefix means hex only.
var evm = Signature.Parse("0x" + new string('a', 130));
var sol = Signature.Parse("5VERv8NMvzbJMEkV8xnrLkEaWRtSz9CosKDYjCJjBRnbJLgp8uirBgmQpjKhoR4tjF3ZpRzrFmBV6UjKdiSZkQUW");

string base58 = sol.ToBase58String();                 // Solana only; throws for an EVM signature
Span<byte> utf8 = stackalloc byte[Signature.SolanaMaxBase58EncodedLength];
sol.TryFormatBase58(utf8, out int written);           // no allocation; false for EVM or a short buffer
```

### Zero-copy (explicit and unsafe)

```csharp
byte[] bytes = { 0xDE, 0xAD, 0xBE, 0xEF };

// Defensive copy
var safe = new HexBytes(bytes);

// Zero-copy (caller guarantees immutability)
var unsafeZeroCopy = HexBytes.FromArrayUnsafe(bytes);
```

### EVM helpers

```csharp
var selector = hb.GetFunctionSelector();
var calldata = hb.GetCalldataParams();
var padded = hb.PadToWord(); // 32-byte EVM word
```

---

## EF Core ValueConverters

Optimised converters for efficient storage and indexing, with fixed-size binary layouts where possible.

Available converters:

| Type | Converter | Stored as | Size hint | Orders like the value (`ORDER BY`, `<`, `>`) |
| --- | --- | --- | --- | --- |
| `Hash32` | `Hash32ValueConverter` | 32 bytes, big-endian | 32 | yes |
| `Address` | `AddressValueConverter` | type byte + 20 (EVM) or 32 (Solana) bytes | 33 | yes |
| `Signature` | `SignatureValueConverter` | 66 bytes (type, payload, v) | 66 | yes |
| `uint256` | `UInt256BytesValueConverter` | 32 bytes, big-endian | 32 | yes |
| `int256` | `Int256BytesValueConverter` | 32 bytes, big-endian two's complement with the sign bit inverted | 32 | yes |
| `FunctionSelector` | `FunctionSelectorBinaryValueConverter` | 4 bytes, big-endian | 4 | yes |
| `FunctionSelector` | `FunctionSelectorValueConverter` | `int`, selector minus 2^31 (no allocation) | none | yes |
| `InstructionDiscriminator` | `InstructionDiscriminatorBinaryValueConverter` | 8 bytes, big-endian | 8 | yes |
| `InstructionDiscriminator` | `InstructionDiscriminatorValueConverter` | `long`, discriminator minus 2^63 (no allocation) | none | yes |
| `HexBigInteger` | `HexBigIntegerToBytesConverter` | variable-length signed big-endian | none | **no** |
| `HexBytes` | `HexBytesValueConverter` | variable-length binary, no copy either way | none | not guaranteed |
| `HexBytes` | `HexBytesStringValueConverter` | `0x` hex text | none | not guaranteed |

**Ordering.** EF Core translates `<`, `>` and `OrderBy` on a converted property into SQL comparisons on the stored column, so a column must sort in the same order as the value or such queries silently return the wrong rows. Every fixed-width converter is built to do that: the bytes are big-endian, and the signed `int256` and the `int`/`long` selector and discriminator columns store the value with its sign bit inverted (offset binary), so a signed column sorts as an unsigned value. The `int`/`long` and `int256` columns therefore do not hold the number you would read off the value (a selector of `0x00000000` is stored as `int.MinValue`); read them through the converter, or use the binary converters, whose bytes are the value itself. `HexBigInteger` is the exception: its stored bytes are variable length and signed, so use it for equality and `IN` lookups only, and use `uint256` or `int256` for a column that is sorted or range-filtered. The `HexBytes` converters are not tested for ordering.

**Size hints.** Converters with a fixed width declare it, so providers create a bounded column that can be a key or index (an unbounded `varbinary(max)` cannot be indexed on SQL Server). Every converter takes an optional `ConverterMappingHints`; a hint you set wins, a hint you leave unset keeps the default (a precision-only hint keeps the size).

**Malformed rows.** A stored value that is the wrong length or otherwise not something the converter wrote throws `FormatException`; `null` handed straight to a converter's delegate throws `ArgumentNullException` (EF Core itself never passes `null`).

**Zero.** `HexBigInteger` zero is stored as an empty array, which providers that treat an empty binary value as NULL (Oracle) turn into NULL.

Every converter has a public parameterless constructor, so `HasConversion<TConverter>()` works for all of them (a unit test pins this for every converter in the assembly). Tests also run the converters against a real SQLite database: stored layout, equality, range filters and `ORDER BY`.

Example:

```csharp
eb.Property(t => t.FunctionSelector)
  .HasConversion<FunctionSelectorBinaryValueConverter>();   // 4-byte size hint is built in

eb.Property(t => t.Amount)
  .HasConversion<UInt256BytesValueConverter>();             // 32-byte size hint is built in

eb.Property(t => t.Delta)
  .HasConversion<Int256BytesValueConverter>();              // signed, and sorts as signed
```

---

## JSON Converters

All core types include `System.Text.Json` converters with UTF-8, span-based implementations.

Features:

* Accepts `0x` hex and (where applicable) decimal
* Avoids intermediate string allocations
* Supports throwing (`Parse`) and non-throwing (`TryParse`) paths
* Every converter reads and writes dictionary keys (`Dictionary<Address, T>`, `Dictionary<HexBytes, T>`, ...) in the same string forms as values
* Fast paths (contiguous, unescaped tokens) and writes allocate nothing for the fixed-width types, `uint256` and `int256`; a unit test guards this
* Escaped or segment-split string tokens are copied into a buffer for parsing. Each converter caps the raw token at the longest text its type can need, fully `\uXXXX`-escaped, plus 256 bytes of whitespace allowance (whitespace and leading zeros both count toward it, so a decimal `uint256` with hundreds of leading zeros is rejected when escaped or split, but accepted when contiguous and unescaped) (for example 316 bytes for `FunctionSelector`, 652 for `Hash32`); larger tokens throw `JsonException`. `HexBytes` is variable length: an unescaped token, contiguous or split across segments, is validated and decoded straight into the result array (an invalid token allocates nothing) and is not limited; an escaped token must be unescaped into a buffer first, so it is capped by a configurable limit (`new HexBytesJsonConverter(maxEscapedTokenBytes)`, default 32 MiB, at most 256 MiB)
* ASCII whitespace around the value inside the string is ignored; quote characters inside the string are content, not framing, and are rejected
* A JSON `null` throws `JsonException` for every non-nullable target, instead of silently becoming a zero value. Use `Address?`, `Hash32?` and so on where a value can be absent; `System.Text.Json` then maps `null` to `null`
* Malformed input always throws `JsonException`, never `FormatException`
* `Hash32` reads `0x` hex, bare 64-digit hex, Base58 and **padded** Base64 (44 characters ending in `=`). Base58 and unpadded Base64 cannot be told apart (about 1.3% of 43-character unpadded Base64 strings are also valid Base58, and Base58 wins), so unpadded Base64 is not accepted: it fails loudly in nearly every case, but those 1.3% would still be read as Base58. Do not send unpadded Base64 to a `Hash32` property; use padded Base64, or `Hash32.TryParseBase64` for a source known to emit unpadded text
* `HexBytes` writes long values (above 255 bytes) as a stream of fixed-size string segments, so no buffer grows with the value. A `HexBytes` dictionary key is limited to 4,096 bytes when read and when written (`HexBytesJsonConverter.MaxDictionaryKeyBytes`), because `HexBytes.GetHashCode` samples only the head and tail of larger values and attacker-chosen keys could otherwise all collide; a longer key throws `JsonException`

`Signature` is written as 0x-prefixed hex by default and read from hex or Solana Base58. Solana JSON-RPC expects Base58, so opt in per options instance:

```csharp
options.Converters.Add(new SignatureJsonConverter(writeSolanaAsBase58: true)); // EVM signatures stay hex
```

Register globally:

```csharp
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.Converters.Add(new AddressJsonConverter());
options.Converters.Add(new UInt256JsonConverter());
```

---

## Performance & Safety Notes

* Constructors copy by default to preserve immutability
* `FromArrayUnsafe` performs **no copy** — use only when lifetime and immutability are guaranteed
* `TryParse` APIs minimise allocations and work directly on UTF-8 spans
* `uint256` / `int256` `GetHashCode()` is non-cryptographic and **randomised per process** (keys that come from untrusted input cannot be precomputed to collide). Never persist or compare hash codes across processes
* Every type that writes hex (`uint256`, `int256`, `Hash32`, `HexBytes`, `HexBigInteger`, `FunctionSelector`, `InstructionDiscriminator`, `Address`) uses a lowercase `0x` prefix; format `"0X"` uppercases the digits only
* `Address.Parse` / `TryParse` take an unprefixed 40-digit hex string as EVM, except text that starts with at least seven `1` digits and has no `0`, which can be a canonical Base58 Solana key and is parsed as one. Write the `0x` prefix when an EVM address may begin that way. Mixed-case hex is accepted without checking its EIP-55 checksum
* `uint256` / `int256` shifts: a count of 256 or more shifts every bit out (zero; `-1` for an arithmetic right shift of a negative `int256`), and a negative count throws `ArgumentOutOfRangeException`
* `uint256` / `int256` multiply, divide and modulo are allocation-free (no `BigInteger` fallback)

---

## Benchmarks

The `EccentricWare.Web3.DataTypes.Benchmarks` project is a [BenchmarkDotNet](https://benchmarkdotnet.org/) suite covering every type: construction, parsing, formatting, equality, ordering, hashing, JSON and EF converters, collections (dictionary, set, sort, SIMD scans). Inputs use fixed seeds and each suite checks in `[GlobalSetup]` that its inputs round-trip, so a wrong benchmark fails instead of timing a no-op.

```bash
# One suite (use the default job for decisions; add --job short for a quick look)
dotnet run -c Release --project EccentricWare.Web3.DataTypes.Benchmarks -- --filter "*SignatureBenchmarks*"

# Everything, quick
dotnet run -c Release --project EccentricWare.Web3.DataTypes.Benchmarks -- --filter "*" --job short
```

If an IDE with a C# project system is open on the repo, BenchmarkDotNet's generated build folder can disappear mid-run. Build once and run the assembly with `--inProcess` instead (use the same toolchain for baseline and comparison). Recording a baseline and comparing runs is described in [the benchmarks README](EccentricWare.Web3.DataTypes.Benchmarks/README.md).

### Reference results

Intel Core Ultra 9 185H, .NET 10.0.15 (RyuJIT x86-64-v3), BenchmarkDotNet 0.15.8, `--inProcess --job short`. Short-job figures for nanosecond rows carry roughly 10% noise; compare only runs from the same machine. "Before" was measured on the uncommitted working tree as it stood ahead of the Signature review, so it cannot be reproduced from a commit; re-record your own baseline as described in the benchmarks README.

| Operation | Before | After |
| --- | ---: | ---: |
| `Signature.TryParse`, Solana Base58 (UTF-8) | 2.81 µs | 96 ns |
| `Signature.TryParse`, Solana Base58 (chars) | 2.97 µs | 125 ns |
| `Signature` JSON read, Solana Base58 | 2.74 µs | 122 ns |
| `Signature.GetHashCode` | 14.1 ns | 1.9 ns |
| `Signature` `Dictionary` hit (per 1,024 keys) | 19.5 µs | 9.1 µs |
| `Signature.TryFormat` to UTF-8 hex | 41 ns | 28 ns |
| `ByteUtils.TryDecodeBase58To32` | 747 ns | 36 ns |

Other figures worth knowing (current code, no allocation unless noted):

| Operation | Time |
| --- | ---: |
| `Hash32.TryParseBase58` (32-byte Base58) | 40 ns |
| `Address.TryParse`, Solana Base58 (UTF-8) | 41 ns |
| `Signature.TryParse`, EVM hex (UTF-8) | 29 ns |
| `Signature.TryFormatBase58` (Solana, UTF-8) | 176 ns |
| `Address.TryFormat`, Solana Base58 (UTF-8) | 58 ns |
| `Signature` equality / ordering | 1.4 ns |
| `Signature` JSON write, hex / Base58 | 55 ns / 201 ns |

Base58 encoding is slower than decoding because it divides the value repeatedly. The shared encoder (used by `Signature` and `Address`) fuses three division passes per sweep, writes digits backwards and takes two digits per table lookup, which made it about 1.6x faster than a straightforward pass-per-five-digits loop (`Signature` 284 to 176 ns, `Address` 93 to 58 ns). `Hash32` and `Address` use the same 40-45 ns decode path as before, which already folded ten digits per 64-bit multiply.

---

## Design Principles

* Minimal allocations and predictable CPU cost
* Fixed-size types for hot paths and indexing
* Explicit APIs where safety vs performance is a caller decision

---

## Compatibility

* **Runtime:** .NET 10
* **Language:** C# 14.0

---

## Contributing & Support

Contributions welcome. 

Issues and requests:
[https://github.com/EccentricBlock/Eccentricware.Web3.DataTypes/issues](https://github.com/EccentricBlock/Eccentricware.Web3.DataTypes/issues)
