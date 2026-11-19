# ABI wire format as implemented

What `ABIData`, `ABICallData` and the writers accept and produce. The normative encoding is the [Solidity Contract ABI Specification](https://docs.soliditylang.org/en/latest/abi-spec.html); this page states the deliberately strict subset this library enforces (spec 2.1 to 2.4, 6.3, 6.5) and where it is stricter than the Solidity decoder.

## Words, widths, offsets

Everything is made of 32-byte big-endian words. For a type `T`: `IsDynamic(T)` is true for `bytes`, `string`, `T[]`, `T[k]` with a dynamic element, and tuples with a dynamic member. A static `T` has a fixed `StaticSize` (a primitive 32 bytes, `T[k]` is `k * StaticSize`, a tuple the sum of its members, an empty tuple zero). The head width of a member is 32 when it is dynamic (an offset word) and its `StaticSize` otherwise, so a static tuple or array occupies many words, and an empty tuple none, in its parent's head. Treating every member as one word is a defect the schema compiler cannot make.

A tuple (or fixed array) is its heads, then the tails of its dynamic members in declaration order. A dynamic member's head word is the offset of its tail, relative to the start of the **containing** tuple-like region:

| Container | Offsets are relative to |
| --- | --- |
| root arguments tuple | byte 0 of the arguments (for a call, call byte 4: the selector is never part of an offset base) |
| nested tuple | the nested tuple's own start |
| fixed array `T[k]` | the array's start (not the outer tuple) |
| dynamic array `T[]` | the first byte after the count word |

`T[]` is a 32-byte count followed by its elements as a tuple-like region. `bytes` and `string` are a 32-byte length, the data, and zero padding to a multiple of 32; an empty one is exactly one zero length word, an empty dynamic array exactly one zero count word.

## Scalars (spec 2.4)

| Type | Accepted words |
| --- | --- |
| `uint<M>` | high `256-M` bits zero |
| `int<M>` | high bits equal bit `M-1` (sign extension), including `M = 256` |
| `address` | first 12 bytes zero |
| `bool` | exactly 0 or 1 |
| `bytes<M>` | the last `32-M` bytes zero |
| `function` | the last 8 bytes zero (20-byte address, 4-byte selector) |
| `fixed<M>x<N>` / `ufixed<M>x<N>` | same rules as `int<M>` / `uint<M>`; a scaled integer, never rounded |

A pointer, count or length word is read as an unsigned 256-bit value and checked against the input **before** it is narrowed to `int`.

## Canonical layout

A document is accepted only if it is byte for byte what a Solidity encoder emits for its value: every dynamic pointer is word aligned, not inside its own head, and equals exactly the next canonical tail position; tails are consecutive in declaration order; padding is zero; the root ends exactly at the input end. The rejected forms, each with a test:

| Form | Status | Test |
| --- | --- | --- |
| pointer inside the head, not 32-byte aligned, wider than 31 bits, or past the document | `InvalidOffset` | `ABIDecodeAdversarialTests.Pointer_*` |
| gap before a tail, overlapping or aliased tails, tails out of order, extra tails | `NonCanonicalLayout` | `TwoDynamicChildren_WithDuplicateAliasedOrReversedTails_AreNonCanonical`, `ExtraPointerWordsAndExtraTails_AreRejected` |
| bytes after the root's canonical end (one byte or a whole word) | `NonCanonicalLayout` | `TrailingData_AfterACanonicalDynamicValue_*`, `AppendedBytes_AfterAnyFixture_*` |
| non-zero padding after `bytes`, `string`, `bytes<M>`, `function` | `InvalidPadding` | `BytesAndStringPadding_AnyNonZeroByteIsInvalidPadding_*` |
| bad sign extension, `bool` 2, `uint8` 256, address high bytes | `InvalidScalar` | `AddressBool_AndIntegerWidthViolations_AreInvalidScalar` |
| offsets taken from the wrong base (root, selector, count word) | `InvalidOffset` / `NonCanonicalLayout` | `DynamicArrayElementOffsets_RelativeToTheCountWord*`, `CallPointersComputedFromTheSelector_AreRejected` |
| length or count not contained, or not a 31-bit number | `Truncated`; counts over the limit and counts that do not fit 31 bits are `ElementLimitExceeded` | `BytesLength_HugeOrLargerThanTheInput_*`, `DynamicArrayCount_*` |

### Where this is stricter than Solidity

The Solidity decoder accepts some non-strict layouts (spec 2.3 and the Solidity "Frequently Reported Non-Bugs" page: offsets with gaps, overlap, and trailing bytes). This library rejects every form in the table above: a transaction the deployed contract would execute can be rejected here. That is the specified compatibility policy (spec 2.3), made because a signing policy that reads a permissive decoding can approve something different from what the contract reads. There is no permissive fallback. `ABINethereumDifferentialTests.NethereumDecoderIsPermissive_TheLibraryRejectsEachOfTheseOnPurpose` documents six such inputs that Nethereum.ABI 7.1.0 decodes and this library refuses (trailing byte, `bool` 2, `uint8` 256, dirty address, a gapped `bytes` pointer, non-zero padding).

## Empty and zero-width values

- Zero arguments: zero bytes of arguments; a call is the bare four selector bytes; any extra byte is `NonCanonicalLayout`.
- A nested empty tuple `()` occupies zero bytes and is a valid, locatable zero-length range; its member index still advances. A dynamic array of empty tuples is the count word alone.
- `T[0]` is not supported (`UnsupportedSchema` at compile time), as Solidity's compiler does not support it either.
- A dynamic member always has a non-zero extent (its length or count word); its pointer must be canonical, never a shortcut into zero-filled head bytes.

Because a value can occupy no bytes, work is bounded by value and word budgets, not by the input length (`docs/abi-api.md`, limits).

## Depth

The root arguments tuple is depth 1; every nested tuple, array or primitive is one deeper. A `bytes` or `string` payload is one leaf. Depth is structural: an empty array never enters its element type, so in `(uint256[][])` an outer array holding one empty inner array needs `MaxValueDepth = 3`, and with one `uint256` in the inner array, 4. The hard cap is 32 (the validator's fixed frame stack).

## Worked examples (published bytes)

`baz(uint32,bool)` with `69, true` (Solidity documentation): selector `cdcd77c0`, then the words `…0045` and `…0001`; 68 bytes.

`sam(bytes,bool,uint256[])` with `"dave", true, [1,2,3]` (Solidity documentation), arguments relative to byte 4:

```
0x00  0000…0060   offset of the bytes tail (3 head words = 0x60)
0x20  0000…0001   true
0x40  0000…00a0   offset of the array tail (0x60 + 32 length + 32 data = 0xa0)
0x60  0000…0004   length of "dave"
0x80  6461766500…  "dave", zero padded to 32 bytes
0xa0  0000…0003   array count
0xc0  0000…0001   0000…0002   0000…0003
```

`multicall(bytes[])` with two 68-byte children (spec 8.2): the head is `0x20`; the array starts with the count `2`; the element offsets `0x40` and `0xc0` are relative to the byte after the count word; each element is a length word and 96 allocated bytes; 384 argument bytes, 388 call bytes. The payload ranges are `(160, 68)` and `(288, 68)` in argument coordinates and `(164, 68)` and `(292, 68)` in call coordinates (`ABICallDataTests.Multicall388_HasTheSpecRangesInArgumentAndCallCoordinates`, reproduced by two independent encoders and by `cast`).

Each such child starts a **new** call coordinate system at its own selector. The bytes are opaque to the codec until trusted contract metadata says they are a call (`docs/abi-integration.md`).
