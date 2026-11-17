using System.Numerics;
using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.TypedAdapterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Every status path of the typed reader adapters (<see cref="RLPTypedExtensions"/>): valid values, the empty string,
/// lists, <c>default</c>, wrong widths, leading zeros, overflow, and default <c>out</c> values on failure.
/// </summary>
[TestClass]
public sealed class TypedAdapterReaderTests
{
    private static readonly int[] FixedWidthProbeLengths = [0, 1, 2, 19, 20, 21, 31, 32, 33, 34, 55, 56, 64, 300];

    // ------------------------------------------------------------------ status values

    [TestMethod]
    public void RLPStatus_NumericValuesAreStable_AndInvalidSignatureVIsLast()
    {
        // New members are appended: no existing value may shift.
        var expected = new (RLPStatus Status, byte Value)[]
        {
            (RLPStatus.Success, 0), (RLPStatus.InvalidArgument, 1), (RLPStatus.EmptyInput, 2), (RLPStatus.TruncatedInput, 3),
            (RLPStatus.NonCanonicalEncoding, 4), (RLPStatus.TrailingData, 5), (RLPStatus.InvalidListBoundary, 6),
            (RLPStatus.LengthOverflow, 7), (RLPStatus.TooLarge, 8), (RLPStatus.TooDeep, 9), (RLPStatus.TooManyItems, 10),
            (RLPStatus.WrongKind, 11), (RLPStatus.NonCanonicalInteger, 12), (RLPStatus.IntegerOverflow, 13),
            (RLPStatus.DestinationTooSmall, 14), (RLPStatus.IndexCapacityExceeded, 15), (RLPStatus.OverlappingBuffers, 16),
            (RLPStatus.WriterStateInvalid, 17), (RLPStatus.UnbalancedList, 18), (RLPStatus.ExtraRoot, 19),
            (RLPStatus.InvalidFixedLength, 20), (RLPStatus.InvalidSignatureV, 21),
        };

        foreach (var (status, value) in expected)
        {
            Assert.AreEqual(value, (byte)status, status.ToString());
        }

        Assert.HasCount(expected.Length, Enum.GetValues<RLPStatus>());
        Assert.AreEqual(RLPStatus.InvalidSignatureV, Enum.GetValues<RLPStatus>().Max());
    }

    // ------------------------------------------------------------------ uint256

    [TestMethod]
    public void UInt256_BoundaryValues_DecodeToTheExactValue()
    {
        foreach (var expected in BoundaryValues())
        {
            var item = StringItem(Minimal(expected));
            Assert.AreEqual(RLPStatus.Success, item.TryGetUInt256(out var value), expected.ToString());
            CollectionAssert.AreEqual(Padded32(expected), value.ToBigEndianBytes(), expected.ToString());
        }
    }

    [TestMethod]
    public void UInt256_NamedBoundaries_DecodeFromHandWrittenWire()
    {
        var rows = new (string Hex, string Expected)[]
        {
            ("80", "0"),
            ("01", "1"),
            ("7f", "127"),
            ("81 80", "128"),
            ("81 ff", "255"),
            ("82 0100", "256"),
            ("88 ffffffffffffffff", "18446744073709551615"),
            ("89 010000000000000000", "18446744073709551616"),
            ("a0 8000000000000000000000000000000000000000000000000000000000000000", "57896044618658097711785492504343953926634992332820282019728792003956564819968"),
            ("a0 ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "115792089237316195423570985008687907853269984665640564039457584007913129639935"),
        };

        foreach (var (hex, expected) in rows)
        {
            Assert.AreEqual(RLPStatus.Success, HexItem(hex).TryGetUInt256(out var value), hex);
            CollectionAssert.AreEqual(Padded32(BigInteger.Parse(expected)), value.ToBigEndianBytes(), hex);
        }
    }

    [TestMethod]
    public void UInt256_EveryLimbBoundary_ReadsIntoTheRightLimbs()
    {
        // 2^(64k) - 1 fills limbs 0..k-1, 2^(64k) sets only limb k.
        for (var limb = 1; limb <= 3; limb++)
        {
            var power = BigInteger.One << (64 * limb);
            Assert.AreEqual(RLPStatus.Success, StringItem(Minimal(power - 1)).TryGetUInt256(out var below));
            Assert.AreEqual(RLPStatus.Success, StringItem(Minimal(power)).TryGetUInt256(out var at));
            Assert.AreEqual(power - 1, new BigInteger(below.ToBigEndianBytes(), isUnsigned: true, isBigEndian: true));
            Assert.AreEqual(power, new BigInteger(at.ToBigEndianBytes(), isUnsigned: true, isBigEndian: true));
            Assert.AreEqual(0UL, at.U0);
            Assert.AreEqual(1UL, limb switch { 1 => at.U1, 2 => at.U2, _ => at.U3 });
        }
    }

    [TestMethod]
    public void UInt256_EmptyString_IsZero()
    {
        Assert.AreEqual(RLPStatus.Success, HexItem("80").TryGetUInt256(out var value));
        Assert.IsTrue(value.IsZero);
    }

    [TestMethod]
    public void UInt256_List_IsWrongKind_AndOutIsDefault()
    {
        for (var variant = 0; variant < 3; variant++)
        {
            var item = variant switch
            {
                0 => HexItem("c0"),
                1 => ListItem([1], [2]),
                _ => HexItem("c1 80"),
            };
            var value = uint256.MaxValue;
            Assert.AreEqual(RLPStatus.WrongKind, item.TryGetUInt256(out value), $"variant {variant}");
            Assert.AreEqual(default, value, $"variant {variant}");
        }
    }

    [TestMethod]
    public void UInt256_DefaultItem_IsInvalidArgument()
    {
        var value = uint256.MaxValue;
        Assert.AreEqual(RLPStatus.InvalidArgument, default(RLPData).TryGetUInt256(out value));
        Assert.AreEqual(default, value);
    }

    [TestMethod]
    public void UInt256_LeadingZero_IsNonCanonicalInteger_IncludingRawZero()
    {
        var rows = new[]
        {
            "00",                                     // raw literal 00: valid RLP, not an integer
            "82 0001",                                // 00 01
            "82 0000",
            "88 0000000000000001",
            "a0 0011111111111111111111111111111111111111111111111111111111111111", // 32 bytes with leading zero
        };

        foreach (var hex in rows)
        {
            var value = uint256.MaxValue;
            Assert.AreEqual(RLPStatus.NonCanonicalInteger, HexItem(hex).TryGetUInt256(out value), hex);
            Assert.AreEqual(default, value, hex);
        }
    }

    [TestMethod]
    public void UInt256_LeadingZeroIsCheckedBeforeTheWidth()
    {
        // 33 bytes: leading zero => NonCanonicalInteger, leading nonzero => IntegerOverflow.
        var withZero = Pattern(33, first: 0x00);
        var withoutZero = Pattern(33, first: 0x01);
        Assert.AreEqual(RLPStatus.NonCanonicalInteger, StringItem(withZero).TryGetUInt256(out _));
        Assert.AreEqual(RLPStatus.IntegerOverflow, StringItem(withoutZero).TryGetUInt256(out _));
    }

    [TestMethod]
    public void UInt256_WidthAroundTheLimit_AcceptsUpTo32Bytes()
    {
        for (var length = 1; length <= 80; length++)
        {
            var payload = Pattern(length, first: 0x80);
            var status = StringItem(payload).TryGetUInt256(out var value);
            if (length <= 32)
            {
                Assert.AreEqual(RLPStatus.Success, status, $"length {length}");
                var expected = new byte[32];
                payload.CopyTo(expected, 32 - length);
                CollectionAssert.AreEqual(expected, value.ToBigEndianBytes(), $"length {length}");
            }
            else
            {
                Assert.AreEqual(RLPStatus.IntegerOverflow, status, $"length {length}");
                Assert.AreEqual(default, value, $"length {length}");
            }
        }
    }

    [TestMethod]
    public void UInt256_AgreesWithUInt64_OnEveryStatusForUpToNineBytes()
    {
        for (var length = 0; length <= 9; length++)
        {
            foreach (var first in new byte[] { 0x00, 0x01, 0x7f, 0x80, 0xff })
            {
                var item = StringItem(Pattern(length, first));
                var wide = item.TryGetUInt256(out var value256);
                var narrow = item.TryGetUInt64(out var value64);
                if (length == 9 && first != 0)
                {
                    Assert.AreEqual(RLPStatus.IntegerOverflow, narrow);
                    Assert.AreEqual(RLPStatus.Success, wide);
                    continue;
                }

                Assert.AreEqual(narrow, wide, $"length {length} first {first:x2}");
                Assert.AreEqual(value64, value256.U0, $"length {length} first {first:x2}");
            }
        }
    }

    // ------------------------------------------------------------------ Hash32

    [TestMethod]
    public void Hash32_Exactly32Bytes_Succeeds_AndRoundTripsTheBytes()
    {
        foreach (var first in new byte[] { 0x00, 0x01, 0x7f, 0x80, 0xff })
        {
            var payload = Pattern(32, first);
            Assert.AreEqual(RLPStatus.Success, StringItem(payload).TryGetHash32(out var value), $"{first:x2}");
            var written = new byte[32];
            value.WriteBigEndian(written);
            CollectionAssert.AreEqual(payload, written, $"{first:x2}");
        }
    }

    [TestMethod]
    public void Hash32_EveryOtherLength_IsInvalidFixedLength_AndOutIsDefault()
    {
        foreach (var length in FixedWidthProbeLengths.Where(l => l != 32))
        {
            foreach (var first in new byte[] { 0x00, 0x05, 0x91 })
            {
                var value = new Hash32(Pattern(32));
                Assert.AreEqual(RLPStatus.InvalidFixedLength, StringItem(Pattern(length, first)).TryGetHash32(out value), $"length {length} first {first:x2}");
                Assert.AreEqual(default, value, $"length {length}");
            }
        }
    }

    [TestMethod]
    public void Hash32_ListAndDefault_AreWrongKindAndInvalidArgument()
    {
        var value = new Hash32(Pattern(32));
        Assert.AreEqual(RLPStatus.WrongKind, HexItem("c0").TryGetHash32(out value));
        Assert.AreEqual(default, value);

        value = new Hash32(Pattern(32));
        Assert.AreEqual(RLPStatus.WrongKind, ListItem(Pattern(32)).TryGetHash32(out value));
        Assert.AreEqual(default, value);

        value = new Hash32(Pattern(32));
        Assert.AreEqual(RLPStatus.InvalidArgument, default(RLPData).TryGetHash32(out value));
        Assert.AreEqual(default, value);
    }

    // ------------------------------------------------------------------ Address

    [TestMethod]
    public void Address_Exactly20Bytes_Succeeds_AsAnEvmAddress()
    {
        foreach (var first in new byte[] { 0x00, 0x01, 0x7f, 0x80, 0xff })
        {
            var payload = Pattern(20, first);
            Assert.AreEqual(RLPStatus.Success, StringItem(payload).TryGetAddress(out var value), $"{first:x2}");
            Assert.AreEqual(AddressType.Evm, value.Type);
            CollectionAssert.AreEqual(payload, value.ToBytes(), $"{first:x2}");
        }
    }

    [TestMethod]
    public void Address_EveryOtherLength_IsInvalidFixedLength_AndOutIsDefault()
    {
        foreach (var length in FixedWidthProbeLengths.Where(l => l != 20))
        {
            foreach (var first in new byte[] { 0x00, 0x05, 0x91 })
            {
                var value = Address.FromEvmBytes(Pattern(20));
                Assert.AreEqual(RLPStatus.InvalidFixedLength, StringItem(Pattern(length, first)).TryGetAddress(out value), $"length {length} first {first:x2}");
                Assert.AreEqual(default, value, $"length {length}");
            }
        }
    }

    [TestMethod]
    public void Address_ListAndDefault_AreWrongKindAndInvalidArgument()
    {
        var value = Address.FromEvmBytes(Pattern(20));
        Assert.AreEqual(RLPStatus.WrongKind, HexItem("c0").TryGetAddress(out value));
        Assert.AreEqual(default, value);

        value = Address.FromEvmBytes(Pattern(20));
        Assert.AreEqual(RLPStatus.WrongKind, ListItem(Pattern(20)).TryGetAddress(out value));
        Assert.AreEqual(default, value);

        value = Address.FromEvmBytes(Pattern(20));
        Assert.AreEqual(RLPStatus.InvalidArgument, default(RLPData).TryGetAddress(out value));
        Assert.AreEqual(default, value);
    }

    [TestMethod]
    public void OptionalAddress_EmptyString_IsContractCreation()
    {
        var value = Address.FromEvmBytes(Pattern(20));
        Assert.AreEqual(RLPStatus.Success, HexItem("80").TryGetOptionalAddress(out value, out var isEmpty));
        Assert.IsTrue(isEmpty);
        Assert.AreEqual(default, value);
    }

    [TestMethod]
    public void OptionalAddress_Exactly20Bytes_IsNotEmpty()
    {
        var payload = Pattern(20, 0xab);
        Assert.AreEqual(RLPStatus.Success, StringItem(payload).TryGetOptionalAddress(out var value, out var isEmpty));
        Assert.IsFalse(isEmpty);
        CollectionAssert.AreEqual(payload, value.ToBytes());
    }

    [TestMethod]
    public void OptionalAddress_TwentyZeroBytes_IsTheZeroAddress_NotContractCreation()
    {
        var item = StringItem(new byte[20]);
        Assert.AreEqual(RLPStatus.Success, item.TryGetOptionalAddress(out var zero, out var isEmpty));
        Assert.IsFalse(isEmpty);
        Assert.AreEqual(AddressType.Evm, zero.Type);
        Assert.IsTrue(zero.IsZero);
        Assert.AreEqual(RLPStatus.Success, item.TryGetAddress(out var plain));
        Assert.AreEqual(zero, plain);
    }

    [TestMethod]
    public void OptionalAddress_EveryOtherNonEmptyLength_IsInvalidFixedLength()
    {
        foreach (var length in FixedWidthProbeLengths.Where(l => l != 20 && l != 0))
        {
            foreach (var first in new byte[] { 0x00, 0x05, 0x91 })
            {
                var value = Address.FromEvmBytes(Pattern(20));
                Assert.AreEqual(RLPStatus.InvalidFixedLength, StringItem(Pattern(length, first)).TryGetOptionalAddress(out value, out var isEmpty), $"length {length}");
                Assert.AreEqual(default, value, $"length {length}");
                Assert.IsFalse(isEmpty, $"length {length}");
            }
        }
    }

    [TestMethod]
    public void OptionalAddress_ListAndDefault_FailWithDefaultOutputs()
    {
        var value = Address.FromEvmBytes(Pattern(20));
        Assert.AreEqual(RLPStatus.WrongKind, HexItem("c0").TryGetOptionalAddress(out value, out var isEmpty));
        Assert.AreEqual(default, value);
        Assert.IsFalse(isEmpty);

        value = Address.FromEvmBytes(Pattern(20));
        Assert.AreEqual(RLPStatus.InvalidArgument, default(RLPData).TryGetOptionalAddress(out value, out isEmpty));
        Assert.AreEqual(default, value);
        Assert.IsFalse(isEmpty);
    }

    // ------------------------------------------------------------------ FunctionSelector

    [TestMethod]
    public void FunctionSelector_PayloadOfAtLeast4Bytes_ReadsTheFirstFour()
    {
        foreach (var length in new[] { 4, 5, 36, 55, 56, 68, 300 })
        {
            foreach (var first in new byte[] { 0x00, 0x7f, 0x80, 0xa9 })
            {
                var payload = Pattern(length, first);
                Assert.AreEqual(RLPStatus.Success, StringItem(payload).TryGetFunctionSelector(out var selector), $"length {length}");
                var written = new byte[4];
                selector.WriteBytes(written);
                CollectionAssert.AreEqual(payload.AsSpan(0, 4).ToArray(), written, $"length {length} first {first:x2}");
            }
        }
    }

    [TestMethod]
    public void FunctionSelector_KnownTransferCalldata_IsA9059cbb()
    {
        // transfer(address,uint256) selector followed by opaque argument bytes.
        var payload = H("a9059cbb 000000000000000000000000000000000000000000000000000000000000dead 0000000000000000000000000000000000000000000000000000000000000001");
        Assert.AreEqual(RLPStatus.Success, StringItem(payload).TryGetFunctionSelector(out var selector));
        Assert.AreEqual(0xa9059cbbU, selector.Value);
    }

    [TestMethod]
    public void FunctionSelector_ShorterThan4Bytes_IsInvalidFixedLength_AndOutIsDefault()
    {
        foreach (var length in new[] { 0, 1, 2, 3 })
        {
            foreach (var first in new byte[] { 0x00, 0x05, 0x91 })
            {
                var selector = new FunctionSelector(0xdeadbeef);
                Assert.AreEqual(RLPStatus.InvalidFixedLength, StringItem(Pattern(length, first)).TryGetFunctionSelector(out selector), $"length {length}");
                Assert.AreEqual(default, selector, $"length {length}");
            }
        }
    }

    [TestMethod]
    public void FunctionSelector_ListAndDefault_AreWrongKindAndInvalidArgument()
    {
        var selector = new FunctionSelector(0xdeadbeef);
        Assert.AreEqual(RLPStatus.WrongKind, HexItem("c0").TryGetFunctionSelector(out selector));
        Assert.AreEqual(default, selector);

        selector = new FunctionSelector(0xdeadbeef);
        Assert.AreEqual(RLPStatus.WrongKind, ListItem(Pattern(8)).TryGetFunctionSelector(out selector));
        Assert.AreEqual(default, selector);

        selector = new FunctionSelector(0xdeadbeef);
        Assert.AreEqual(RLPStatus.InvalidArgument, default(RLPData).TryGetFunctionSelector(out selector));
        Assert.AreEqual(default, selector);
    }

    [TestMethod]
    public void FunctionSelector_PayloadThatLooksLikeRlp_StaysOpaque()
    {
        // The payload bytes are a valid list encoding; the selector is just their first four bytes.
        var inner = H("c8 83 636174 83 646f67");
        var padded = RlpReference.Concatenate(inner, Pattern(4, 0x01));
        Assert.AreEqual(RLPStatus.Success, StringItem(padded).TryGetFunctionSelector(out var selector));
        Assert.AreEqual(0xc8836361U, selector.Value);
    }

    // The signature readers (TryGetEvmLegacySignature, TryGetEvmYParitySignature) are covered in EvmSignatureReaderTests.

    // ------------------------------------------------------------------ views

    [TestMethod]
    public void Readers_WorkOnNestedChildren_WithDocumentRelativeViews()
    {
        // [ uint256, hash32, address ] decoded from a root, read from the child enumerator.
        var hash = Pattern(32, 0xee);
        var address = Pattern(20, 0xa1);
        var document = RlpReference.Encode(RlpNode.ListOf(RlpNode.Bytes(Minimal(0x123456)), RlpNode.Bytes(hash), RlpNode.Bytes(address)));
        var root = DecodeOk(document);
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));

        Assert.IsTrue(children.MoveNext());
        Assert.AreEqual(RLPStatus.Success, children.Current.TryGetUInt256(out var number));
        Assert.AreEqual(0x123456UL, number.U0);
        Assert.IsTrue(children.MoveNext());
        Assert.AreEqual(RLPStatus.Success, children.Current.TryGetHash32(out var hashValue));
        Assert.AreEqual(new Hash32(hash), hashValue);
        Assert.IsTrue(children.MoveNext());
        Assert.AreEqual(RLPStatus.Success, children.Current.TryGetAddress(out var addressValue));
        Assert.AreEqual(Address.FromEvmBytes(address), addressValue);
        Assert.IsFalse(children.MoveNext());
    }
}
