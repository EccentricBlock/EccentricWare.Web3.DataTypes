# RLP wire format as implemented

This is the encoding the `EccentricWare.Web3.DataTypes.Rlp` reader accepts and the writer produces. It is **strict canonical RLP**: exactly one encoding exists for each value, and the reader rejects every other encoding. Source of truth: `docs/specs/RLPData_DotNet_Design_Specification_v2.0.md` section 2.

## Items

An RLP item is either a **byte string** or a **list** of items. A document is exactly one root item, with no bytes after it.

| First byte | Item | Layout | Canonical when |
| --- | --- | --- | --- |
| `00`..`7f` | single byte string | the byte is its own encoding | always |
| `80`..`b7` | byte string, length `n = b - 0x80` (0..55) | prefix, then `n` bytes | `n == 1` requires the payload byte to be `>= 0x80` |
| `b8`..`bf` | byte string, length in `L = b - 0xb7` bytes (1..8) | prefix, `L` big-endian length bytes, payload | length `>= 56`, no leading zero length byte |
| `c0`..`f7` | list, payload length `n = b - 0xc0` (0..55) | prefix, then `n` bytes of whole child items | children exactly fill the payload |
| `f8`..`ff` | list, length in `L = b - 0xf7` bytes (1..8) | prefix, `L` big-endian length bytes, payload | length `>= 56`, no leading zero length byte |

## Rejected as non-canonical

Each of these decodes under lenient RLP parsers but returns `NonCanonicalEncoding` here:

- `81 00` to `81 7f`: a one-byte string below `0x80` must be written as the literal byte.
- A long form whose length is below 56 (`b8 37 ...`, `f8 05 ...`).
- A long-form length with a leading zero byte (`b9 00 38 ...`).

## Lengths that cannot be represented

A length field of 1 to 8 bytes is read as an unsigned big-endian number. If the value, or the item it implies, exceeds `int.MaxValue`, the status is `LengthOverflow`. An 8-byte length of `ffffffffffffffff` is therefore `LengthOverflow`, not `TruncatedInput`. A length that is representable but larger than the remaining input is `TruncatedInput`; a child that overruns its list is `InvalidListBoundary`.

## Integers

RLP has no integer type. Ethereum fields use the **shortest big-endian magnitude as a byte string**:

| Value | Encoding |
| --- | --- |
| `0` | `80` (the empty string) |
| `1`..`127` | the literal byte |
| `128` | `81 80` |
| `0x0400` | `82 04 00` |

A payload with a leading zero byte is **not** an integer. A raw string `00` is valid RLP, but `TryGetUInt64` and `TryGetUInt256` return `NonCanonicalInteger` for it. The leading-zero rule is checked before the width rule, so a 9-byte payload starting with `00` is `NonCanonicalInteger`, not `IntegerOverflow`.

## Worked examples

| Item | Bytes |
| --- | --- |
| empty string | `80` |
| empty list | `c0` |
| `"dog"` | `83 64 6f 67` |
| `["cat", "dog"]` | `c8 83 63 61 74 83 64 6f 67` |
| nested lists `[ [], [[]], [ [], [[]] ] ]` | `c7 c0 c1 c0 c3 c0 c1 c0` |
| 55-byte string | `b7` + 55 bytes |
| 56-byte string | `b8 38` + 56 bytes |
| 256-byte string | `b9 01 00` + 256 bytes |

## Budgets are policy, not wire format

`RLPDecodeLimits` bounds document size, list depth and item count. Exceeding a budget returns `TooLarge`, `TooDeep` or `TooManyItems`; the bytes are still valid RLP. See `docs/rlp-security.md`.

Depth counts list nesting: a root list is depth 1, a root string is depth 0. Items count the root, every nested list and every string.
