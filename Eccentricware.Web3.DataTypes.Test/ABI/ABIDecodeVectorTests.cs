using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Spec 10.1 vectors with hand-derived expectations: exact head and value ranges (root-relative), typed accessor results and
/// accepted lengths. The bytes come from ABIFixtures (cast / Solidity docs); the ranges below are computed by hand from the
/// head/tail rules and are NOT read back from the code under test.
/// </summary>
[TestClass]
public sealed class ABIDecodeVectorTests
{
    private const string Vitalik = "d8da6bf26964af9d7eed9e03e53415d37aa96045";
    private const string Router02 = "68b3465833fb72a70ecdf485e0e4c7bd8665fc45";

    private static byte[] Args(string callHex) => Hex(callHex)[4..];

    private static ulong U64(in ABIData data, params int[] path)
    {
        Assert.IsTrue(data.TryGetUInt64(path, out ulong value, out var status), $"[{string.Join(",", path)}] {status}");
        return value;
    }

    // ------------------------------------------------------------------ 1, 2: ERC-20

    [TestMethod]
    public void Transfer_IsSixtyEightBytes_WithRightAlignedAddressAndWholeWordAmount()
    {
        byte[] call = Hex(ABIFixtures.TransferVitalik1Eth);
        Assert.AreEqual(68, call.Length);
        CollectionAssert.AreEqual(new byte[] { 0xa9, 0x05, 0x9c, 0xbb }, call[..4]);
        CollectionAssert.AreEqual(new byte[12], call[4..16], "address is right aligned: 12 zero bytes first");

        var data = DecodeOk("(address,uint256)", call[4..]);
        AssertLocated(in data, [], ABITypeKind.Tuple, head: (0, 64), value: (0, 64));
        AssertLocated(in data, [0], ABITypeKind.Address, (0, 32), (0, 32));
        AssertLocated(in data, [1], ABITypeKind.UInt, (32, 32), (32, 32));
        Assert.IsTrue(data.TryGetAddress([0], out var address, out _));
        CollectionAssert.AreEqual(Hex(Vitalik), address.ToArray());
        Assert.AreEqual(1_000_000_000_000_000_000UL, U64(in data, 1));
        Assert.IsTrue(data.TryGetUInt256Word([1], out var word, out _));
        CollectionAssert.AreEqual(WordHex("de0b6b3a7640000"), word.ToArray());
    }

    [TestMethod]
    public void Approve_MaxAmount_IsCanonicalButDoesNotFitUInt64()
    {
        var data = DecodeOk("(address,uint256)", Args(ABIFixtures.ApproveRouterMax));
        Assert.IsTrue(data.TryGetAddress([0], out var spender, out _));
        CollectionAssert.AreEqual(Hex(Router02), spender.ToArray());
        Assert.IsTrue(data.TryGetUInt256Word([1], out var amount, out _));
        CollectionAssert.AreEqual(Filled(0xff), amount.ToArray());
        Assert.IsFalse(data.TryGetUInt64([1], out ulong narrowed, out var status));
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status, "canonical but too wide is ValueOutOfRange, not InvalidScalar");
        Assert.AreEqual(0UL, narrowed);
    }

    [TestMethod]
    public void Approve_ZeroSpenderZeroAmount_IsValid()
    {
        var data = DecodeOk("(address,uint256)", Args(ABIFixtures.ApproveZeroZero));
        Assert.IsTrue(data.TryGetAddress([0], out var spender, out _));
        CollectionAssert.AreEqual(new byte[20], spender.ToArray());
        Assert.AreEqual(0UL, U64(in data, 1));
    }

    [TestMethod]
    public void BalanceOf_IsOneStaticWord()
    {
        var data = DecodeOk("(address)", Args(ABIFixtures.BalanceOfVitalik));
        AssertLocated(in data, [], ABITypeKind.Tuple, (0, 32), (0, 32));
        Assert.IsTrue(data.TryGetAddress([0], out var owner, out _));
        CollectionAssert.AreEqual(Hex(Vitalik), owner.ToArray());
    }

    // ------------------------------------------------------------------ 4: zero-argument

    [TestMethod]
    public void ZeroArgumentRoot_IsZeroBytes_AndTrailingBytesAreRejected()
    {
        var data = DecodeOk("()", []);
        Assert.IsTrue(data.IsValid);
        Assert.AreEqual(0, data.EncodedBytes.Length);
        AssertLocated(in data, [], ABITypeKind.Tuple, (0, 0), (0, 0));
        Assert.IsFalse(data.TryLocate(Path(0), out _, out var status));
        Assert.AreEqual(ABIStatus.InvalidPath, status);

        DecodeFails("()", [0x00], ABIStatus.NonCanonicalLayout);
        DecodeFails("()", new byte[32], ABIStatus.NonCanonicalLayout);
    }

    // ------------------------------------------------------------------ 5, 14: scalars

    [TestMethod]
    public void UnsignedMaxima_OfEveryWidth_AreAcceptedAndNarrowOnlyWhenTheyFit()
    {
        var data = DecodeOk("(uint8,uint16,uint64,uint128,uint256)", Hex(ABIFixtures.ScalarsMixedMaxima));
        Assert.AreEqual(255UL, U64(in data, 0));
        Assert.AreEqual(65_535UL, U64(in data, 1));
        Assert.AreEqual(ulong.MaxValue, U64(in data, 2));
        Assert.IsFalse(data.TryGetUInt64([3], out _, out var status));
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
        Assert.IsTrue(data.TryGetUInt256Word([3], out var w128, out _));
        CollectionAssert.AreEqual(Cat(new byte[16], Filled(0xff)[..16]), w128.ToArray());
        Assert.IsTrue(data.TryGetUInt256Word([4], out var w256, out _));
        CollectionAssert.AreEqual(Filled(0xff), w256.ToArray());
    }

    [TestMethod]
    public void SignedMinima_OfEveryWidth_AreSignExtended()
    {
        var data = DecodeOk("(int8,int16,int64,int128,int256)", Hex(ABIFixtures.ScalarsMixedMinima));
        Assert.IsTrue(data.TryGetInt64([0], out long int8, out _));
        Assert.AreEqual(-128L, int8);
        Assert.IsTrue(data.TryGetInt64([1], out long int16, out _));
        Assert.AreEqual(-32_768L, int16);
        Assert.IsTrue(data.TryGetInt64([2], out long int64, out _));
        Assert.AreEqual(long.MinValue, int64);
        Assert.IsFalse(data.TryGetInt64([3], out _, out var status));
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status, "int128 minimum does not fit 64 bits");
        Assert.IsTrue(data.TryGetInt256Word([3], out var w128, out _));
        CollectionAssert.AreEqual(Hex("ffffffffffffffffffffffffffffffff80000000000000000000000000000000"), w128.ToArray());
        Assert.IsTrue(data.TryGetInt256Word([4], out var w256, out _));
        CollectionAssert.AreEqual(Hex("8000000000000000000000000000000000000000000000000000000000000000"), w256.ToArray());
    }

    [TestMethod]
    public void SignedMaxima_OfEveryWidth_AreAccepted()
    {
        var data = DecodeOk("(int8,int16,int64,int128,int256)", Hex(ABIFixtures.ScalarsMixedPositiveMaxima));
        Assert.IsTrue(data.TryGetInt64([0], out long a, out _));
        Assert.AreEqual(127L, a);
        Assert.IsTrue(data.TryGetInt64([1], out long b, out _));
        Assert.AreEqual(32_767L, b);
        Assert.IsTrue(data.TryGetInt64([2], out long c, out _));
        Assert.AreEqual(long.MaxValue, c);
        Assert.IsFalse(data.TryGetInt64([4], out _, out var status));
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
    }

    [TestMethod]
    public void BoolAddressAndFixedBytes_ReturnExactlyTheirBytes()
    {
        var data = DecodeOk("(bool,bool,address,bytes1,bytes24,bytes32)", Hex(ABIFixtures.ScalarsBoolAddressBytes));
        Assert.IsTrue(data.TryGetBool([0], out bool yes, out _));
        Assert.IsTrue(yes);
        Assert.IsTrue(data.TryGetBool([1], out bool no, out _));
        Assert.IsFalse(no);
        Assert.IsTrue(data.TryGetAddress([2], out var address, out _));
        CollectionAssert.AreEqual(Hex(Vitalik), address.ToArray());
        Assert.IsTrue(data.TryGetFixedBytes([3], out var b1, out _));
        CollectionAssert.AreEqual(new byte[] { 0xab }, b1.ToArray(), "bytes1 excludes its 31 padding bytes");
        Assert.IsTrue(data.TryGetFixedBytes([4], out var b24, out _));
        CollectionAssert.AreEqual(Enumerable.Range(1, 24).Select(i => (byte)i).ToArray(), b24.ToArray());
        Assert.IsTrue(data.TryGetFixedBytes([5], out var b32, out _));
        CollectionAssert.AreEqual(Enumerable.Range(0, 32).Select(i => (byte)(0xa0 + i)).ToArray(), b32.ToArray());
        Assert.IsTrue(data.TryGetWord([3], out var word, out _));
        Assert.AreEqual(32, word.Length, "the raw word keeps its padding");
    }

    [TestMethod]
    public void FunctionPointer_SplitsIntoAddressAndBigEndianSelector()
    {
        var data = DecodeOk("(function)", Hex(ABIFixtures.ScalarFunctionPointer));
        Assert.IsTrue(data.TryGetFunction([0], out var address, out uint selector, out _));
        CollectionAssert.AreEqual(Enumerable.Range(1, 20).Select(i => (byte)i).ToArray(), address.ToArray());
        Assert.AreEqual(0xaabbccddU, selector);
    }

    // Hand derivation (python int arithmetic, two's complement over 256 bits):
    //   -1.5e18 = 0xffffffffffffffffffffffffffffffffffffffffffffffffeb2eedf284ea0000   (1.5e18 = 0x14d1120d7b160000)
    //   2.5e18  = 0x22b1c8c1227a0000
    //   fixed128 min = -2^127 = 0xffffffffffffffffffffffffffffffff80000000000000000000000000000000
    //   fixed128 max =  2^127 - 1 = 0x7fffffffffffffffffffffffffffffff
    [TestMethod]
    public void FixedPoint_Fixed128x18_And_UFixed128x18_ReturnScaledWordsWithScale()
    {
        byte[] negative = Hex("ffffffffffffffffffffffffffffffffffffffffffffffffeb2eedf284ea0000");
        byte[] positive = WordHex("22b1c8c1227a0000");
        byte[] min = Hex("ffffffffffffffffffffffffffffffff80000000000000000000000000000000");
        byte[] max = WordHex("7fffffffffffffffffffffffffffffff");
        byte[] uMax = WordHex("ffffffffffffffffffffffffffffffff");
        var data = DecodeOk("(fixed128x18,ufixed128x18,fixed128x18,fixed128x18,ufixed128x18)", Cat(negative, positive, min, max, uMax));

        Assert.IsTrue(data.TryGetSignedScaledWord([0], out var w0, out byte s0, out _));
        CollectionAssert.AreEqual(negative, w0.ToArray());
        Assert.AreEqual((byte)18, s0);
        Assert.IsTrue(data.TryGetUnsignedScaledWord([1], out var w1, out byte s1, out _));
        CollectionAssert.AreEqual(positive, w1.ToArray());
        Assert.AreEqual((byte)18, s1);
        Assert.IsTrue(data.TryGetSignedScaledWord([2], out var w2, out _, out _));
        CollectionAssert.AreEqual(min, w2.ToArray());
        Assert.IsTrue(data.TryGetSignedScaledWord([3], out var w3, out _, out _));
        CollectionAssert.AreEqual(max, w3.ToArray());
        Assert.IsTrue(data.TryGetUnsignedScaledWord([4], out var w4, out _, out _));
        CollectionAssert.AreEqual(uMax, w4.ToArray());
    }

    [TestMethod]
    public void FixedPoint_OutsideTheDeclaredWidth_IsInvalidScalar()
    {
        // fixed128 max + 1 and ufixed128 max + 1, and a positive value with a stray high bit.
        DecodeFails("(fixed128x18)", WordHex("80000000000000000000000000000000"), ABIStatus.InvalidScalar);
        DecodeFails("(ufixed128x18)", WordHex("100000000000000000000000000000000"), ABIStatus.InvalidScalar);
        DecodeFails("(fixed128x18)", Hex("fffffffffffffffffffffffffffffffe" + "ffffffffffffffffffffffffffffffff"), ABIStatus.InvalidScalar);
    }

    // ------------------------------------------------------------------ 6: dynamic bytes and strings

    [TestMethod]
    public void DynamicBytesAndStrings_OfEveryBoundaryLength_HaveExactRangesAndPadding()
    {
        int[] lengths = [0, 1, 31, 32, 33, 63, 64, 65];
        string[] bytesFixtures = [ABIFixtures.Bytes0, ABIFixtures.Bytes1, ABIFixtures.Bytes31, ABIFixtures.Bytes32, ABIFixtures.Bytes33, ABIFixtures.Bytes63, ABIFixtures.Bytes64, ABIFixtures.Bytes65];
        string[] stringFixtures = [ABIFixtures.String0, ABIFixtures.String1, ABIFixtures.String31, ABIFixtures.String32, ABIFixtures.String33, ABIFixtures.String63, ABIFixtures.String64, ABIFixtures.String65];
        string[] payloads = [ABIFixtures.Bytes0Payload, ABIFixtures.Bytes1Payload, ABIFixtures.Bytes31Payload, ABIFixtures.Bytes32Payload, ABIFixtures.Bytes33Payload, ABIFixtures.Bytes63Payload, ABIFixtures.Bytes64Payload, ABIFixtures.Bytes65Payload];

        for (int i = 0; i < lengths.Length; i++)
        {
            int n = lengths[i];
            int padded = (n + 31) / 32 * 32;
            int expectedTotal = 32 /* pointer */ + 32 /* length */ + padded;

            byte[] bytesDoc = Hex(bytesFixtures[i]);
            Assert.AreEqual(expectedTotal, bytesDoc.Length, $"bytes{n} total length");
            var asBytes = DecodeOk("(bytes)", bytesDoc);
            AssertLocated(in asBytes, [], ABITypeKind.Tuple, (0, 32), (0, expectedTotal));
            AssertLocated(in asBytes, [0], ABITypeKind.Bytes, head: (0, 32), value: (32, 32 + padded));
            Assert.IsTrue(asBytes.TryGetBytePayload([0], out var payload, out ABIRange range, out _));
            CollectionAssert.AreEqual(Hex(payloads[i]), payload.ToArray(), $"bytes{n} payload");
            Assert.AreEqual(new ABIRange(64, n), range, $"bytes{n} payload range");
            CollectionAssert.AreEqual(new byte[padded - n], bytesDoc[(64 + n)..], $"bytes{n} padding is zero");

            byte[] stringDoc = Hex(stringFixtures[i]);
            Assert.AreEqual(expectedTotal, stringDoc.Length, $"string{n} total length");
            var asString = DecodeOk("(string)", stringDoc);
            AssertLocated(in asString, [0], ABITypeKind.String, (0, 32), (32, 32 + padded));
            Assert.IsTrue(asString.TryGetStringUtf8Validated([0], out var text, out ABIRange textRange, out _));
            Assert.AreEqual(new ABIRange(64, n), textRange);
            Assert.AreEqual(string.Concat(Enumerable.Range(0, n).Select(k => (char)(97 + k % 26))), System.Text.Encoding.ASCII.GetString(text), $"string{n} text");
        }
    }

    [TestMethod]
    public void BytesMultipleOf32_HaveNoPaddingBytesAndZeroPaddingWorkUnit()
    {
        // length 32 and 64: [ptr][len][data] with data exactly filling its words.
        DecodeOk("(bytes)", Hex(ABIFixtures.Bytes32));
        DecodeOk("(bytes)", Hex(ABIFixtures.Bytes64));
        DecodeFails("(bytes)", [.. Hex(ABIFixtures.Bytes32), .. new byte[32]], ABIStatus.NonCanonicalLayout);
    }

    // ------------------------------------------------------------------ 7: static composites and multi-word heads

    [TestMethod]
    public void StaticTupleInsideTuple_AdvancesTheHeadByItsFullWidth()
    {
        byte[] doc = Hex(ABIFixtures.StaticTupleInTuple);
        Assert.AreEqual(96, doc.Length);
        var data = DecodeOk("((uint256,uint256),uint256)", doc);
        AssertLocated(in data, [], ABITypeKind.Tuple, (0, 96), (0, 96));
        AssertLocated(in data, [0], ABITypeKind.Tuple, (0, 64), (0, 64));
        AssertLocated(in data, [0, 0], ABITypeKind.UInt, (0, 32), (0, 32));
        AssertLocated(in data, [0, 1], ABITypeKind.UInt, (32, 32), (32, 32));
        AssertLocated(in data, [1], ABITypeKind.UInt, (64, 32), (64, 32));
        Assert.AreEqual(1UL, U64(in data, 0, 0));
        Assert.AreEqual(2UL, U64(in data, 0, 1));
        Assert.AreEqual(3UL, U64(in data, 1));
    }

    [TestMethod]
    public void StaticArrayThenBoolThenBytes_HeadExceedsOneWord_AndThePointerIsRelativeToTheWholeHead()
    {
        byte[] doc = Hex(ABIFixtures.MultiWordHeadThenBytes);
        // head = 3*32 (uint256[3]) + 32 (bool) + 32 (bytes pointer) = 160; tail = length word + one padded word = 64.
        Assert.AreEqual(224, doc.Length);
        CollectionAssert.AreEqual(Word(160), doc[128..160], "the bytes pointer is 0xa0 = the full head size");
        var data = DecodeOk("(uint256[3],bool,bytes)", doc);
        AssertLocated(in data, [], ABITypeKind.Tuple, (0, 160), (0, 224));
        AssertLocated(in data, [0], ABITypeKind.FixedArray, (0, 96), (0, 96));
        AssertLocated(in data, [0, 2], ABITypeKind.UInt, (64, 32), (64, 32));
        AssertLocated(in data, [1], ABITypeKind.Bool, (96, 32), (96, 32));
        AssertLocated(in data, [2], ABITypeKind.Bytes, (128, 32), (160, 64));
        Assert.AreEqual(3UL, U64(in data, 0, 2));
        Assert.IsTrue(data.TryGetBytePayload([2], out var payload, out ABIRange range, out _));
        CollectionAssert.AreEqual(new byte[] { 0x12, 0x34 }, payload.ToArray());
        Assert.AreEqual(new ABIRange(192, 2), range);
    }

    // ------------------------------------------------------------------ 8: arrays

    [TestMethod]
    public void DynamicUintArray_ElementHeadBaseIsAfterTheCountWord()
    {
        byte[] doc = Hex(ABIFixtures.DynamicUintArray);
        Assert.AreEqual(160, doc.Length); // pointer + count + 3 elements
        var data = DecodeOk("(uint256[])", doc);
        AssertLocated(in data, [0], ABITypeKind.DynamicArray, head: (0, 32), value: (32, 128));
        AssertLocated(in data, [0, 0], ABITypeKind.UInt, (64, 32), (64, 32));
        AssertLocated(in data, [0, 2], ABITypeKind.UInt, (128, 32), (128, 32));
        Assert.IsTrue(data.TryGetArrayLength([0], out int length, out _));
        Assert.AreEqual(3, length);
        Assert.AreEqual(2UL, U64(in data, 0, 1));
    }

    [TestMethod]
    public void EmptyDynamicArray_IsOneZeroCountWord_AndHasNoAddressableElement()
    {
        byte[] doc = Hex(ABIFixtures.EmptyUintArray);
        Assert.AreEqual(64, doc.Length);
        var data = DecodeOk("(uint256[])", doc);
        AssertLocated(in data, [0], ABITypeKind.DynamicArray, (0, 32), (32, 32));
        Assert.IsTrue(data.TryGetArrayLength([0], out int length, out _));
        Assert.AreEqual(0, length);
        Assert.IsFalse(data.TryLocate(Path(0, 0), out _, out var status));
        Assert.AreEqual(ABIStatus.InvalidPath, status);
    }

    [TestMethod]
    public void BytesArray_TwoElements_HaveRangesFromTheirOwnOffsetTable()
    {
        // [ptr 0x20][count 2][off0 0x40][off1 0x80] then element 0 (len 1: 32+32 = 64 bytes) and element 1 (len 33: 32+64 = 96 bytes).
        byte[] doc = Hex(ABIFixtures.DynamicBytesArray);
        Assert.AreEqual(288, doc.Length);
        CollectionAssert.AreEqual(Word(0x40), doc[64..96]);
        CollectionAssert.AreEqual(Word(0x80), doc[96..128]);
        var data = DecodeOk("(bytes[])", doc);
        AssertLocated(in data, [0], ABITypeKind.DynamicArray, (0, 32), (32, 256));
        AssertLocated(in data, [0, 0], ABITypeKind.Bytes, head: (64, 32), value: (128, 64));
        AssertLocated(in data, [0, 1], ABITypeKind.Bytes, head: (96, 32), value: (192, 96));
        Assert.IsTrue(data.TryGetBytePayload([0, 0], out var first, out ABIRange firstRange, out _));
        CollectionAssert.AreEqual(new byte[] { 0x01 }, first.ToArray());
        Assert.AreEqual(new ABIRange(160, 1), firstRange);
        Assert.IsTrue(data.TryGetBytePayload([0, 1], out var second, out ABIRange secondRange, out _));
        CollectionAssert.AreEqual(Enumerable.Repeat((byte)0xab, 33).ToArray(), second.ToArray());
        Assert.AreEqual(new ABIRange(224, 33), secondRange);
    }

    [TestMethod]
    public void StringArray_WithAnEmptyString_EndsExactlyAtTheCanonicalBoundary()
    {
        // ["a","bc",""]: count 3, table 96 bytes (0x60,0xa0,0xe0), tails of 64,64,32 bytes starting at 32+32+96 = 160.
        byte[] doc = Hex(ABIFixtures.DynamicStringArray);
        Assert.AreEqual(320, doc.Length);
        var data = DecodeOk("(string[])", doc);
        AssertLocated(in data, [0], ABITypeKind.DynamicArray, (0, 32), (32, 288));
        AssertLocated(in data, [0, 0], ABITypeKind.String, (64, 32), (160, 64));
        AssertLocated(in data, [0, 1], ABITypeKind.String, (96, 32), (224, 64));
        AssertLocated(in data, [0, 2], ABITypeKind.String, (128, 32), (288, 32));
        Assert.IsTrue(data.TryGetStringUtf8Validated([0, 2], out var empty, out ABIRange emptyRange, out _));
        Assert.AreEqual(0, empty.Length);
        Assert.AreEqual(new ABIRange(320, 0), emptyRange, "an empty payload is a valid zero-length range at the document end");
    }

    [TestMethod]
    public void FixedUintArray_IsInlineWithNoCount()
    {
        byte[] doc = Hex(ABIFixtures.FixedUintArray3);
        Assert.AreEqual(96, doc.Length);
        var data = DecodeOk("(uint256[3])", doc);
        AssertLocated(in data, [0], ABITypeKind.FixedArray, (0, 96), (0, 96));
        AssertLocated(in data, [0, 1], ABITypeKind.UInt, (32, 32), (32, 32));
        Assert.IsTrue(data.TryGetArrayLength([0], out int length, out _));
        Assert.AreEqual(3, length);
        Assert.IsFalse(data.TryLocate(Path(0, 3), out _, out var status));
        Assert.AreEqual(ABIStatus.InvalidPath, status);
    }

    [TestMethod]
    public void FixedArrayOfDynamicBytes_OffsetsAreRelativeToTheArrayStartNotTheRoot()
    {
        // [ptr 0x20] then the array at 32: slots 0x40 and 0x80 (relative to 32), element 0 at 96 (64 bytes), element 1 at 160 (32 bytes).
        byte[] doc = Hex(ABIFixtures.FixedBytesArray2);
        Assert.AreEqual(192, doc.Length);
        CollectionAssert.AreEqual(Word(0x40), doc[32..64]);
        CollectionAssert.AreEqual(Word(0x80), doc[64..96]);
        var data = DecodeOk("(bytes[2])", doc);
        AssertLocated(in data, [0], ABITypeKind.FixedArray, (0, 32), (32, 160));
        AssertLocated(in data, [0, 0], ABITypeKind.Bytes, (32, 32), (96, 64));
        AssertLocated(in data, [0, 1], ABITypeKind.Bytes, (64, 32), (160, 32));
        Assert.IsTrue(data.TryGetBytePayload([0, 0], out var first, out _, out _));
        CollectionAssert.AreEqual(new byte[] { 0x12, 0x34 }, first.ToArray());
        Assert.IsTrue(data.TryGetBytePayload([0, 1], out var second, out ABIRange secondRange, out _));
        Assert.AreEqual(0, second.Length);
        Assert.AreEqual(new ABIRange(192, 0), secondRange);
    }

    [TestMethod]
    public void TupleArray_AddressUintBytes_EachTupleResetsItsOwnOffsetBase()
    {
        // array at 32; table (2 slots) at 64: off0 0x40, off1 0xe0 (tuple 0 is 96 head + 64 tail = 160 bytes); tuples at 128 and 288.
        byte[] doc = Hex(ABIFixtures.TupleArrayAddressUintBytes);
        Assert.AreEqual(416, doc.Length);
        CollectionAssert.AreEqual(Word(0x40), doc[64..96]);
        CollectionAssert.AreEqual(Word(0xe0), doc[96..128]);
        // Inside tuple 0 the bytes pointer is 0x60, relative to the tuple start (128), not to the array or the root.
        CollectionAssert.AreEqual(Word(0x60), doc[192..224]);
        var data = DecodeOk("((address,uint256,bytes)[])", doc);
        AssertLocated(in data, [0, 0], ABITypeKind.Tuple, head: (64, 32), value: (128, 160));
        AssertLocated(in data, [0, 0, 0], ABITypeKind.Address, (128, 32), (128, 32));
        AssertLocated(in data, [0, 0, 2], ABITypeKind.Bytes, (192, 32), (224, 64));
        AssertLocated(in data, [0, 1], ABITypeKind.Tuple, (96, 32), (288, 128));
        AssertLocated(in data, [0, 1, 2], ABITypeKind.Bytes, (352, 32), (384, 32));
        Assert.IsTrue(data.TryGetAddress([0, 1, 0], out var second, out _));
        CollectionAssert.AreEqual(Hex(Router02), second.ToArray());
        Assert.IsTrue(data.TryGetBytePayload([0, 0, 2], out var payload, out ABIRange range, out _));
        CollectionAssert.AreEqual(new byte[] { 0x11, 0x22 }, payload.ToArray());
        Assert.AreEqual(new ABIRange(256, 2), range);
    }

    [TestMethod]
    public void FixedArrayOfDynamicTuples_IsDynamicAndItsElementsAreDynamicTuples()
    {
        // array at 32 (slots 0x40, 0xc0 relative to 32); tuple 0 at 96 (64 head + 64 tail), tuple 1 at 224.
        byte[] doc = Hex(ABIFixtures.FixedArrayOfDynamicTuples);
        Assert.AreEqual(352, doc.Length);
        var data = DecodeOk("((uint256,bytes)[2])", doc);
        AssertLocated(in data, [0], ABITypeKind.FixedArray, (0, 32), (32, 320));
        AssertLocated(in data, [0, 0], ABITypeKind.Tuple, (32, 32), (96, 128));
        AssertLocated(in data, [0, 1], ABITypeKind.Tuple, (64, 32), (224, 128));
        AssertLocated(in data, [0, 1, 1], ABITypeKind.Bytes, (256, 32), (288, 64)); // tuple 1 starts at 224: its bytes slot is at 224 + 32
        Assert.AreEqual(2UL, U64(in data, 0, 1, 0));
    }

    [TestMethod]
    public void ArrayOfArrays_WithAnEmptyInnerArray_LocatesEachInnerArray()
    {
        // [[1,2],[3],[]]: outer at 32, table at 64 (3 slots: 0x60,0xc0,0x100); inner arrays at 160 (96 B), 256 (64 B), 320 (32 B).
        byte[] doc = Hex(ABIFixtures.ArrayOfArrays);
        Assert.AreEqual(352, doc.Length);
        var data = DecodeOk("(uint256[][])", doc);
        AssertLocated(in data, [0], ABITypeKind.DynamicArray, (0, 32), (32, 320));
        AssertLocated(in data, [0, 0], ABITypeKind.DynamicArray, (64, 32), (160, 96));
        AssertLocated(in data, [0, 1], ABITypeKind.DynamicArray, (96, 32), (256, 64));
        AssertLocated(in data, [0, 2], ABITypeKind.DynamicArray, (128, 32), (320, 32));
        AssertLocated(in data, [0, 0, 1], ABITypeKind.UInt, (224, 32), (224, 32));
        AssertLocated(in data, [0, 1, 0], ABITypeKind.UInt, (288, 32), (288, 32));
        foreach ((int index, int expected) in new[] { (0, 2), (1, 1), (2, 0) })
        {
            Assert.IsTrue(data.TryGetArrayLength([0, index], out int length, out _));
            Assert.AreEqual(expected, length);
        }
    }

    [TestMethod]
    public void NestedStaticArrays_AreInline_AndDynamicArrayOfStaticArraysStridesByTheStaticSize()
    {
        var fixedFixed = DecodeOk("(uint256[2][2])", Hex(ABIFixtures.FixedArrayOfFixedArrays));
        AssertLocated(in fixedFixed, [0], ABITypeKind.FixedArray, (0, 128), (0, 128));
        AssertLocated(in fixedFixed, [0, 1], ABITypeKind.FixedArray, (64, 64), (64, 64));
        AssertLocated(in fixedFixed, [0, 1, 0], ABITypeKind.UInt, (64, 32), (64, 32));
        Assert.AreEqual(4UL, U64(in fixedFixed, 0, 1, 1));

        byte[] doc = Hex(ABIFixtures.DynamicArrayOfFixedArrays);
        Assert.AreEqual(256, doc.Length);
        var dynamicFixed = DecodeOk("(uint256[2][])", doc);
        AssertLocated(in dynamicFixed, [0], ABITypeKind.DynamicArray, (0, 32), (32, 224));
        AssertLocated(in dynamicFixed, [0, 0], ABITypeKind.FixedArray, (64, 64), (64, 64));
        AssertLocated(in dynamicFixed, [0, 2], ABITypeKind.FixedArray, (192, 64), (192, 64));
        AssertLocated(in dynamicFixed, [0, 2, 1], ABITypeKind.UInt, (224, 32), (224, 32));
        Assert.AreEqual(6UL, U64(in dynamicFixed, 0, 2, 1));
    }

    [TestMethod]
    public void FixedArrayOfDynamicArrays_UsesTheArrayStartAsBase()
    {
        // [[1,2],[3]] as uint256[][2]: array at 32, slots 0x40 and 0xa0 (relative to 32), elements at 96 (96 B) and 192 (64 B).
        byte[] doc = Hex(ABIFixtures.FixedArrayOfDynamicArrays);
        Assert.AreEqual(256, doc.Length);
        var data = DecodeOk("(uint256[][2])", doc);
        AssertLocated(in data, [0], ABITypeKind.FixedArray, (0, 32), (32, 224));
        AssertLocated(in data, [0, 0], ABITypeKind.DynamicArray, (32, 32), (96, 96));
        AssertLocated(in data, [0, 1], ABITypeKind.DynamicArray, (64, 32), (192, 64));
        AssertLocated(in data, [0, 1, 0], ABITypeKind.UInt, (224, 32), (224, 32));
        Assert.AreEqual(3UL, U64(in data, 0, 1, 0));
    }

    [TestMethod]
    public void NestedMixedTuples_EveryLevelUsesItsOwnBase()
    {
        // root head 128: uint256 | pointer 0x80 | uint256[2] inline. Tuple at 128 has head 96 (string ptr 0x60, array ptr 0xa0, address),
        // string "hi" at 224 (64 B), array at 288 = count + 2 slots + 2 * (64 head + 64 tail) = 352 B -> 640.
        byte[] doc = Hex(ABIFixtures.NestedMixedTuples);
        Assert.AreEqual(640, doc.Length);
        var data = DecodeOk("(uint256,(string,(uint8,bytes)[],address),uint256[2])", doc);
        AssertLocated(in data, [], ABITypeKind.Tuple, (0, 128), (0, 640));
        AssertLocated(in data, [1], ABITypeKind.Tuple, head: (32, 32), value: (128, 512));
        AssertLocated(in data, [2], ABITypeKind.FixedArray, (64, 64), (64, 64));
        AssertLocated(in data, [1, 0], ABITypeKind.String, (128, 32), (224, 64));
        AssertLocated(in data, [1, 1], ABITypeKind.DynamicArray, (160, 32), (288, 352));
        AssertLocated(in data, [1, 2], ABITypeKind.Address, (192, 32), (192, 32));
        Assert.AreEqual(7UL, U64(in data, 0));
        Assert.AreEqual(9UL, U64(in data, 2, 1));
        Assert.AreEqual(2UL, U64(in data, 1, 1, 1, 0));
        Assert.IsTrue(data.TryGetBytePayload([1, 1, 1, 1], out var payload, out _, out _));
        CollectionAssert.AreEqual(new byte[] { 0xbb, 0xcc }, payload.ToArray());
    }

    [TestMethod]
    public void EmptyDynamicValuesInsideANestedTuple_EndExactlyAtTheCanonicalBoundary()
    {
        // root head 64 (ptr 0x40, bool); tuple at 64: head 128 (5, ptr 0x80, ptr 0xa0, ptr 0xc0), then three 32-byte tails: 224 B -> 288.
        byte[] doc = Hex(ABIFixtures.EmptyDynamicsInsideNestedTuple);
        Assert.AreEqual(288, doc.Length);
        var data = DecodeOk("((uint256,string,uint256[],bytes),bool)", doc);
        AssertLocated(in data, [0], ABITypeKind.Tuple, (0, 32), (64, 224));
        AssertLocated(in data, [0, 1], ABITypeKind.String, (96, 32), (192, 32));
        AssertLocated(in data, [0, 2], ABITypeKind.DynamicArray, (128, 32), (224, 32));
        AssertLocated(in data, [0, 3], ABITypeKind.Bytes, (160, 32), (256, 32));
        AssertLocated(in data, [1], ABITypeKind.Bool, (32, 32), (32, 32));
        Assert.IsTrue(data.TryGetArrayLength([0, 2], out int length, out _));
        Assert.AreEqual(0, length);
        Assert.IsTrue(data.TryGetBytePayload([0, 3], out var payload, out ABIRange range, out _));
        Assert.AreEqual(0, payload.Length);
        Assert.AreEqual(new ABIRange(288, 0), range);
    }

    [TestMethod]
    public void StaticFieldsAfterDynamicOnes_DoNotMoveTheNextDynamicTail()
    {
        // (bytes,uint256,string,uint256): head 128 = ptr 0x80 | 9 | ptr 0xc0 | 10; tails at 128 (64 B) and 192 (64 B).
        byte[] doc = Hex(ABIFixtures.DynamicTupleLastThenStatic);
        Assert.AreEqual(256, doc.Length);
        var data = DecodeOk("(bytes,uint256,string,uint256)", doc);
        AssertLocated(in data, [], ABITypeKind.Tuple, (0, 128), (0, 256));
        AssertLocated(in data, [0], ABITypeKind.Bytes, (0, 32), (128, 64));
        AssertLocated(in data, [1], ABITypeKind.UInt, (32, 32), (32, 32));
        AssertLocated(in data, [2], ABITypeKind.String, (64, 32), (192, 64));
        AssertLocated(in data, [3], ABITypeKind.UInt, (96, 32), (96, 32));
        Assert.AreEqual(10UL, U64(in data, 3));
    }

    // ------------------------------------------------------------------ 10: Solidity documentation examples

    [TestMethod]
    public void SolidityDocs_Baz_Bar_Sam_F_G_HaveThePublishedSelectorsAndLayouts()
    {
        // baz(uint32,bool) with (69, true): 0xcdcd77c0, 0x45, 0x01.
        byte[] baz = Hex(ABIFixtures.DocsBaz69True);
        Assert.AreEqual(68, baz.Length);
        CollectionAssert.AreEqual(Hex("cdcd77c0"), baz[..4]);
        var bazData = DecodeOk("(uint32,bool)", baz[4..]);
        Assert.AreEqual(69UL, U64(in bazData, 0));
        Assert.IsTrue(bazData.TryGetBool([1], out bool flag, out _));
        Assert.IsTrue(flag);

        // bar(bytes3[2]) with ["abc","def"]: 0xfce353f6, then "abc" and "def" left-aligned in two words.
        byte[] bar = Hex(ABIFixtures.DocsBarAbcDef);
        CollectionAssert.AreEqual(Hex("fce353f6"), bar[..4]);
        CollectionAssert.AreEqual(Cat(Ascii("abc"), new byte[29], Ascii("def"), new byte[29]), bar[4..]);
        var barData = DecodeOk("(bytes3[2])", bar[4..]);
        Assert.IsTrue(barData.TryGetFixedBytes([0, 0], out var abc, out _));
        CollectionAssert.AreEqual(Ascii("abc"), abc.ToArray());
        Assert.IsTrue(barData.TryGetFixedBytes([0, 1], out var def, out _));
        CollectionAssert.AreEqual(Ascii("def"), def.ToArray());

        // sam(bytes,bool,uint256[]) with ("dave", true, [1,2,3]): offsets 0x60 and 0xa0, then "dave" and the array.
        byte[] sam = Hex(ABIFixtures.DocsSamDaveTrue123);
        CollectionAssert.AreEqual(Hex("a5643bf2"), sam[..4]);
        CollectionAssert.AreEqual(Word(0x60), sam[4..36]);
        CollectionAssert.AreEqual(Word(0xa0), sam[68..100]);
        var samData = DecodeOk("(bytes,bool,uint256[])", sam[4..]);
        AssertLocated(in samData, [0], ABITypeKind.Bytes, (0, 32), (96, 64));
        AssertLocated(in samData, [2], ABITypeKind.DynamicArray, (64, 32), (160, 128));
        Assert.IsTrue(samData.TryGetBytePayload([0], out var dave, out _, out _));
        CollectionAssert.AreEqual(Ascii("dave"), dave.ToArray());
        Assert.AreEqual(3UL, U64(in samData, 2, 2));

        // f(uint256,uint32[],bytes10,bytes): 0x8be65246; offsets 0x80 and 0xe0.
        byte[] f = Hex(ABIFixtures.DocsFExample);
        CollectionAssert.AreEqual(Hex("8be65246"), f[..4]);
        CollectionAssert.AreEqual(Word(0x80), f[36..68]);
        CollectionAssert.AreEqual(Word(0xe0), f[100..132]);
        var fData = DecodeOk("(uint256,uint32[],bytes10,bytes)", f[4..]);
        Assert.AreEqual(0x123UL, U64(in fData, 0));
        Assert.AreEqual(0x789UL, U64(in fData, 1, 1));
        Assert.IsTrue(fData.TryGetFixedBytes([2], out var tenBytes, out _));
        CollectionAssert.AreEqual(Ascii("1234567890"), tenBytes.ToArray());
        Assert.IsTrue(fData.TryGetBytePayload([3], out var hello, out _, out _));
        CollectionAssert.AreEqual(Ascii("Hello, world!"), hello.ToArray());

        // g(uint256[][],string[]) with ([[1,2],[3]], ["one","two","three"]): 0x2289b18c; offsets 0x40 and 0x140.
        byte[] g = Hex(ABIFixtures.DocsGNestedArrays);
        CollectionAssert.AreEqual(Hex("2289b18c"), g[..4]);
        CollectionAssert.AreEqual(Word(0x40), g[4..36]);
        CollectionAssert.AreEqual(Word(0x140), g[36..68]);
        var gData = DecodeOk("(uint256[][],string[])", g[4..]);
        AssertLocated(in gData, [0], ABITypeKind.DynamicArray, (0, 32), (64, 256));
        AssertLocated(in gData, [1], ABITypeKind.DynamicArray, (32, 32), (320, 320)); // count + 3 slots + 3 * 64
        Assert.AreEqual(3UL, U64(in gData, 0, 1, 0));
        Assert.IsTrue(gData.TryGetStringUtf8Validated([1, 2], out var three, out _, out _));
        CollectionAssert.AreEqual(Ascii("three"), three.ToArray());
    }

    // ------------------------------------------------------------------ 11: empty tuples (hand-derived; no tool encodes these)

    [TestMethod]
    public void NestedEmptyTuple_OccupiesZeroBytes_AndIsALocatableZeroLengthValue()
    {
        // ((),uint256) encodes as the uint256 alone: the empty tuple has zero width.
        var data = DecodeOk("((),uint256)", Word(7));
        AssertLocated(in data, [], ABITypeKind.Tuple, (0, 32), (0, 32));
        AssertLocated(in data, [0], ABITypeKind.Tuple, (0, 0), (0, 0));
        AssertLocated(in data, [1], ABITypeKind.UInt, (0, 32), (0, 32));

        // (uint256,()) : the empty tuple sits at the very end.
        var atEnd = DecodeOk("(uint256,())", Word(7));
        AssertLocated(in atEnd, [1], ABITypeKind.Tuple, (32, 0), (32, 0));

        // (()): root of a single empty tuple is zero bytes.
        var single = DecodeOk("(())", []);
        AssertLocated(in single, [0], ABITypeKind.Tuple, (0, 0), (0, 0));
    }

    [TestMethod]
    public void TupleOfEmptyTuples_And_FixedArrayOfEmptyTuples_AreZeroBytes()
    {
        var tuples = DecodeOk("((),(),(()))", []);
        AssertLocated(in tuples, [2, 0], ABITypeKind.Tuple, (0, 0), (0, 0));

        var fixedArray = DecodeOk("(()[3])", []);
        AssertLocated(in fixedArray, [0], ABITypeKind.FixedArray, (0, 0), (0, 0));
        AssertLocated(in fixedArray, [0, 2], ABITypeKind.Tuple, (0, 0), (0, 0));
        Assert.IsFalse(fixedArray.TryLocate(Path(0, 3), out _, out var status));
        Assert.AreEqual(ABIStatus.InvalidPath, status);
    }

    [TestMethod]
    public void DynamicArrayOfEmptyTuples_IsJustTheCountWord()
    {
        // (()[]): pointer 0x20, count 3, no element bytes. Document = 64 bytes.
        byte[] doc = Cat(Word(0x20), Word(3));
        var data = DecodeOk("(()[])", doc);
        AssertLocated(in data, [0], ABITypeKind.DynamicArray, head: (0, 32), value: (32, 32));
        AssertLocated(in data, [0, 2], ABITypeKind.Tuple, head: (64, 0), value: (64, 0));
        Assert.IsTrue(data.TryGetArrayLength([0], out int length, out _));
        Assert.AreEqual(3, length);

        // Zero elements is the same shape with count 0.
        DecodeOk("(()[])", Cat(Word(0x20), Word(0)));
    }

    // ------------------------------------------------------------------ 13: UTF-8

    [TestMethod]
    public void MalformedUtf8String_IsStructurallyValid_ButTheStrictTextStepRejectsIt()
    {
        var data = DecodeOk("(string)", Hex(ABIFixtures.MalformedUtf8Payload));
        Assert.IsTrue(data.TryGetBytePayload([0], out var raw, out ABIRange range, out var status));
        CollectionAssert.AreEqual(new byte[] { 0xff, 0xfe, 0x41 }, raw.ToArray(), "raw bytes are returned unchanged: no replacement characters");
        Assert.AreEqual(new ABIRange(64, 3), range);

        Assert.IsFalse(data.TryGetStringUtf8Validated([0], out var text, out ABIRange textRange, out status));
        Assert.AreEqual(ABIStatus.InvalidUtf8, status);
        Assert.AreEqual(0, text.Length);
        Assert.AreEqual(default, textRange);
    }

    [TestMethod]
    public void Utf8Strings_AreAcceptedOnlyWhenWellFormed()
    {
        var multiByte = DecodeOk("(string)", Hex(ABIFixtures.Utf8MultiByteString));
        Assert.IsTrue(multiByte.TryGetStringUtf8Validated([0], out var text, out _, out _));
        Assert.AreEqual("héllo €\U0001F600", System.Text.Encoding.UTF8.GetString(text));

        // Overlong NUL, lone surrogate (ED A0 80), truncated sequence, out-of-range lead byte F5: all rejected after a structurally valid decode.
        foreach (string bad in new[] { "c080", "eda080", "e282", "f5808080", "80", "f4908080" })
        {
            var doc = DecodeOk("(string)", Cat(Word(0x20), BytesValue(Hex(bad))));
            Assert.IsFalse(doc.TryGetStringUtf8Validated([0], out _, out _, out var status), bad);
            Assert.AreEqual(ABIStatus.InvalidUtf8, status, bad);
        }

        // The empty string and a NUL byte are valid UTF-8.
        foreach (string good in new[] { "", "00", "7f", "c2a0", "f48fbfbf" })
        {
            var doc = DecodeOk("(string)", Cat(Word(0x20), BytesValue(Hex(good))));
            Assert.IsTrue(doc.TryGetStringUtf8Validated([0], out var utf8, out _, out var status), good);
            Assert.AreEqual(good, Convert.ToHexString(utf8).ToLowerInvariant());
        }
    }
}
