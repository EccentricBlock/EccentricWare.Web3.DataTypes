using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Spec 10.2 malformed-document categories. Every case builds its bytes by hand from the head/tail rules (Word, BytesValue, Cat) and asserts the
/// exact status, <c>IsValid == false</c>, the defaulted view, and (through DecodeFails) that no exception escapes. Status mapping used throughout
/// (see docs/abi-design-notes.md): InvalidOffset = a pointer that is not a plausible in-document tail pointer (unrepresentable, outside the document,
/// misaligned, or into its own head); NonCanonicalLayout = a plausible pointer that is not the next canonical tail (gaps, overlap, aliasing,
/// reversal) and trailing data; Truncated = a word, length, count-sized head or payload that is not contained; ElementLimitExceeded = a
/// dynamic-array count above MaxArrayElements (including any count that is not a 31-bit number).
/// </summary>
[TestClass]
public sealed class ABIDecodeAdversarialTests
{
    // ------------------------------------------------------------------ root too short / too long / single byte / trailing data

    [TestMethod]
    public void StaticRoot_EveryPrefixIsTruncated_AndEveryExtensionIsNonCanonical()
    {
        byte[] valid = Cat(Word(1), Word(2));
        for (int length = 0; length < valid.Length; length++)
        {
            DecodeFails("(uint256,uint256)", valid[..length], ABIStatus.Truncated);
        }

        DecodeOk("(uint256,uint256)", valid);
        foreach (int extra in new[] { 1, 31, 32, 33, 64 })
        {
            DecodeFails("(uint256,uint256)", [.. valid, .. new byte[extra]], ABIStatus.NonCanonicalLayout);
        }
    }

    [TestMethod]
    public void SingleUnexpectedByte_IsTruncatedForANonEmptyRoot_AndTrailingDataForAnEmptyOne()
    {
        DecodeFails("(uint256)", [0x01], ABIStatus.Truncated);
        DecodeFails("(uint256)", [0x00], ABIStatus.Truncated);
        DecodeFails("(bytes)", [0x20], ABIStatus.Truncated);
        DecodeFails("()", [0x01], ABIStatus.NonCanonicalLayout);
    }

    [TestMethod]
    public void TrailingData_AfterACanonicalDynamicValue_IsRejectedWithTheCanonicalEndAsTheOffset()
    {
        byte[] valid = Cat(Word(0x20), BytesValue(Ascii("hello")));
        DecodeOk("(bytes)", valid);
        foreach (byte[] suffix in new[] { new byte[] { 0 }, new byte[] { 0xff }, new byte[32], Filled(0xab), Cat(Word(0x20), Word(0)) })
        {
            ABIError error = DecodeFails("(bytes)", [.. valid, .. suffix], ABIStatus.NonCanonicalLayout);
            Assert.AreEqual(valid.Length, error.DocumentOffset);
        }
    }

    // ------------------------------------------------------------------ pointers

    private static byte[] UintThenBytes(byte[] pointerWord) => Cat(Word(5), pointerWord, BytesValue(Ascii("x"))); // canonical pointer is 0x40; 128 bytes

    [TestMethod]
    public void Pointer_IntoTheHead_IsInvalidOffset()
    {
        // Zero while a non-empty head precedes the tail; pointing at the pointer word itself; into the middle of the previous scalar.
        foreach (ulong pointer in new ulong[] { 0, 0x10, 0x20, 0x3f })
        {
            ABIError error = DecodeFails("(uint256,bytes)", UintThenBytes(Word(pointer)), ABIStatus.InvalidOffset);
            Assert.AreEqual(32, error.DocumentOffset, $"pointer {pointer:x}: the failing word is the pointer slot");
        }
    }

    [TestMethod]
    public void Pointer_WithWrongAlignment_IsInvalidOffset()
    {
        foreach (ulong pointer in new ulong[] { 1, 0x1f, 0x41, 0x5f, 0x7f })
        {
            DecodeFails("(uint256,bytes)", UintThenBytes(Word(pointer)), ABIStatus.InvalidOffset);
        }
    }

    [TestMethod]
    public void Pointer_PastTheNextTail_IsAGapAndNonCanonical_WhileOneBeyondTheInputIsInvalidOffset()
    {
        // Document is 128 bytes: base 0, canonical pointer 0x40.
        foreach (ulong pointer in new ulong[] { 0x60, 0x80 })
        {
            DecodeFails("(uint256,bytes)", UintThenBytes(Word(pointer)), ABIStatus.NonCanonicalLayout); // 0x80 = exactly the input end
        }

        foreach (ulong pointer in new ulong[] { 0xa0, 0x1000, 0x7fffffe0 })
        {
            DecodeFails("(uint256,bytes)", UintThenBytes(Word(pointer)), ABIStatus.InvalidOffset);
        }
    }

    [TestMethod]
    public void Pointer_WithNonZeroHighOrderBytes_IsInvalidOffset_EvenWhenTheLowBitsAreCanonical()
    {
        foreach (string high in new[]
        {
            "80" + new string('0', 62),            // 2^255 + 0
            "1" + new string('0', 16),              // 2^64
            "1" + new string('0', 8),               // 2^32
            "1" + new string('0', 48),              // 2^192
            "1" + new string('0', 32),              // 2^128
        })
        {
            // high + 0x40 as a 256-bit number: add by building the hex then padding.
            byte[] pointer = Hex(high.PadLeft(64, '0'));
            pointer[31] |= 0x40;
            DecodeFails("(uint256,bytes)", UintThenBytes(pointer), ABIStatus.InvalidOffset);
        }

        DecodeFails("(uint256,bytes)", UintThenBytes(Filled(0xff)), ABIStatus.InvalidOffset);                                // 2^256 - 1
        DecodeFails("(uint256,bytes)", UintThenBytes(Word(0x8000_0000)), ABIStatus.InvalidOffset);                           // int.MaxValue + 1
        DecodeFails("(uint256,bytes)", UintThenBytes(Word(0x7fff_ffff)), ABIStatus.InvalidOffset);                           // int.MaxValue
        DecodeFails("(uint256,bytes)", UintThenBytes(Word(0x1_0000_0040)), ABIStatus.InvalidOffset);                         // 2^32 + 0x40: a truncating cast would read 0x40
    }

    [TestMethod]
    public void TwoDynamicChildren_WithDuplicateAliasedOrReversedTails_AreNonCanonical()
    {
        // (bytes,bytes): head 64; tail A = BytesValue("a") at 64 (64 B), tail B at 128.
        byte[] tailA = BytesValue(Ascii("a"));
        byte[] tailB = BytesValue(Ascii("b"));
        byte[] Doc(ulong p0, ulong p1) => Cat(Word(p0), Word(p1), tailA, tailB);

        DecodeOk("(bytes,bytes)", Doc(0x40, 0x80));
        DecodeFails("(bytes,bytes)", Doc(0x40, 0x40), ABIStatus.NonCanonicalLayout);   // duplicate: both children share one tail
        DecodeFails("(bytes,bytes)", Doc(0x80, 0x40), ABIStatus.NonCanonicalLayout);   // tail reversal
        DecodeFails("(bytes,bytes)", Doc(0x40, 0x60), ABIStatus.NonCanonicalLayout);   // second pointer into the middle of the first tail
        DecodeFails("(bytes,bytes)", Doc(0x40, 0xa0), ABIStatus.NonCanonicalLayout);   // gap of one word between tails
        DecodeFails("(bytes,bytes)", Doc(0x40, 0x00), ABIStatus.InvalidOffset);        // second pointer back into the head
        DecodeFails("(bytes,bytes)", Doc(0x40, 0x20), ABIStatus.InvalidOffset);        // second pointer at the first pointer word
        ABIError first = DecodeFails("(bytes,bytes)", Doc(0x80, 0x40), ABIStatus.NonCanonicalLayout);
        Assert.AreEqual(0, first.DocumentOffset, "the first wrong pointer is reported, in declaration order");
        ABIError second = DecodeFails("(bytes,bytes)", Doc(0x40, 0x40), ABIStatus.NonCanonicalLayout);
        Assert.AreEqual(32, second.DocumentOffset);
    }

    [TestMethod]
    public void ExtraPointerWordsAndExtraTails_AreRejected()
    {
        // A second head word where the tail should start: (bytes) with pointer 0x40 and an extra word before the real tail.
        DecodeFails("(bytes)", Cat(Word(0x40), Word(0x20), Word(0)), ABIStatus.NonCanonicalLayout);
        // An unvisited extra element tail after a one-element bytes[].
        byte[] oneElement = Cat(Word(0x20), Word(1), Word(0x20), BytesValue(Ascii("a")));
        DecodeOk("(bytes[])", oneElement);
        DecodeFails("(bytes[])", [.. oneElement, .. BytesValue(Ascii("b"))], ABIStatus.NonCanonicalLayout);
        // Count 2 but only one element's worth of table and tail.
        DecodeFails("(bytes[])", Cat(Word(0x20), Word(2), Word(0x40), BytesValue(Ascii("a"))), ABIStatus.Truncated);
    }

    [TestMethod]
    public void DynamicArray_OverlappingAndDuplicateElementTails_AreRejected()
    {
        // bytes[] with 2 elements: table (0x40, 0x80) then two 64-byte tails.
        byte[] Doc(ulong o0, ulong o1) => Cat(Word(0x20), Word(2), Word(o0), Word(o1), BytesValue(Ascii("a")), BytesValue(Ascii("b")));
        DecodeOk("(bytes[])", Doc(0x40, 0x80));
        DecodeFails("(bytes[])", Doc(0x40, 0x40), ABIStatus.NonCanonicalLayout);
        DecodeFails("(bytes[])", Doc(0x40, 0x60), ABIStatus.NonCanonicalLayout);
        DecodeFails("(bytes[])", Doc(0x80, 0x40), ABIStatus.NonCanonicalLayout);
        DecodeFails("(bytes[])", Doc(0x00, 0x80), ABIStatus.InvalidOffset);
        DecodeFails("(bytes[])", Doc(0x20, 0x80), ABIStatus.InvalidOffset);
    }

    // ------------------------------------------------------------------ wrong base

    [TestMethod]
    public void DynamicArrayElementOffsets_RelativeToTheCountWordInsteadOfAfterIt_AreRejected()
    {
        // One element: correct offset 0x20 (table is one slot). Relative to the array start incl. the count word it would be 0x40.
        byte[] Doc(ulong offset) => Cat(Word(0x20), Word(1), Word(offset), BytesValue(Ascii("a")));
        DecodeOk("(bytes[])", Doc(0x20));
        DecodeFails("(bytes[])", Doc(0x40), ABIStatus.NonCanonicalLayout);
        DecodeFails("(bytes[])", Doc(0x20 + 0x20 + 0x20), ABIStatus.NonCanonicalLayout); // relative to the root: array at 0x20 + count + table
    }

    [TestMethod]
    public void FixedArrayOfDynamicElements_OffsetsRelativeToTheRootInsteadOfTheArrayStart_AreRejected()
    {
        // bytes[2] at 32: slots 0x40,0x80 relative to the array start 32. Relative to the root they would be 0x60,0xa0.
        byte[] Doc(ulong o0, ulong o1) => Cat(Word(0x20), Word(o0), Word(o1), BytesValue(Ascii("a")), BytesValue(Ascii("b")));
        DecodeOk("(bytes[2])", Doc(0x40, 0x80));
        DecodeFails("(bytes[2])", Doc(0x60, 0xa0), ABIStatus.NonCanonicalLayout);
    }

    [TestMethod]
    public void NestedTupleOffsets_RelativeToTheOuterRootOrTheSelector_AreRejected()
    {
        // ((uint256,bytes)): root pointer 0x20; the nested tuple starts at 0x20 and its bytes pointer must be 0x40 relative to that start.
        byte[] Doc(ulong nestedPointer) => Cat(Word(0x20), Word(7), Word(nestedPointer), BytesValue(Ascii("a")));
        DecodeOk("((uint256,bytes))", Doc(0x40));
        DecodeFails("((uint256,bytes))", Doc(0x60), ABIStatus.NonCanonicalLayout);  // relative to the root: 0x20 + 0x40
        DecodeFails("((uint256,bytes))", Doc(0x44), ABIStatus.InvalidOffset);       // relative to a selector base: 4 + 0x40, misaligned
        DecodeFails("((uint256,bytes))", Doc(0x24), ABIStatus.InvalidOffset);
    }

    [TestMethod]
    public void CallPointersComputedFromTheSelector_AreRejected()
    {
        var function = Function("f(bytes)");
        byte[] selector = Hex(function.Selector.ToString("x8"));
        var limits = ABIDecodeLimits.Default;
        foreach (ulong pointer in new ulong[] { 0x24, 0x40 })
        {
            byte[] call = Cat(selector, Word(pointer), BytesValue(Ascii("a")));
            Assert.IsFalse(ABICallData.TryDecode(call, function, in limits, out var decoded, out var status), $"pointer {pointer:x}");
            Assert.IsTrue(status is ABIStatus.InvalidOffset or ABIStatus.NonCanonicalLayout);
            Assert.IsFalse(decoded.IsValid);
        }

        byte[] good = Cat(selector, Word(0x20), BytesValue(Ascii("a")));
        Assert.IsTrue(ABICallData.TryDecode(good, function, in limits, out _, out _));
    }

    // ------------------------------------------------------------------ counts and lengths

    private static readonly byte[][] HugeWords =
    [
        Filled(0xff),                                  // 2^256 - 1
        Cat([0x80], new byte[31]),                     // 2^255
        WordHex("10000000000000000"),                  // 2^64
        WordHex("100000000"),                          // 2^32
        Word(0x8000_0000),                             // int.MaxValue + 1
        Word(0x7fff_ffff),                             // int.MaxValue
        Word(0x7fff_ffff - 31),                        // int.MaxValue - 31 (the padded-length overflow edge)
        Word(0x7fff_ffff - 30),
        Word(0x7fff_ffe0),
    ];

    [TestMethod]
    public void DynamicArrayCount_AtAndAboveTheLimit_IsElementLimitExceeded_NotAnOverflowOrAnAllocation()
    {
        foreach (byte[] count in HugeWords)
        {
            DecodeFails("(uint256[])", Cat(Word(0x20), count), ABIStatus.ElementLimitExceeded);
        }

        // Counts at or just above the default limit of 4096 (no data follows).
        DecodeFails("(uint256[])", Cat(Word(0x20), Word(4097)), ABIStatus.ElementLimitExceeded);
        DecodeFails("(uint256[])", Cat(Word(0x20), Word(4096)), ABIStatus.Truncated); // within the limit, but 128 KiB of head is missing
    }

    [TestMethod]
    public void DynamicArrayCount_WithAnUnboundedLimit_IsBoundedByTheInputSize_AndTheMultiplicationCannotOverflow()
    {
        var unbounded = Limits(array: int.MaxValue, work: int.MaxValue, visited: int.MaxValue, bytes: int.MaxValue);
        foreach (byte[] count in HugeWords)
        {
            // Wider than a 31-bit number is still ElementLimitExceeded; int.MaxValue itself passes the limit and then fails on size.
            ABIStatus expected = new System.Numerics.BigInteger(count, isUnsigned: true, isBigEndian: true) > int.MaxValue ? ABIStatus.ElementLimitExceeded : ABIStatus.Truncated;
            DecodeFails("(uint256[])", Cat(Word(0x20), count), expected, unbounded);
            // Element head width 32 * 1000: count * width reaches 6.9e13, far above int.MaxValue, and must not wrap.
            DecodeFails("(uint256[1000][])", Cat(Word(0x20), count), expected, unbounded);
        }
    }

    [TestMethod]
    public void DynamicArrayCount_LimitMinusOneLimitAndLimitPlusOne()
    {
        byte[] Doc(int count) => Cat(Word(0x20), Word((ulong)count), [.. Enumerable.Range(0, count).SelectMany(i => Word((ulong)i + 1))]);
        var limit3 = Limits(array: 3);
        DecodeOk("(uint256[])", Doc(2), limit3);
        DecodeOk("(uint256[])", Doc(3), limit3);
        DecodeFails("(uint256[])", Doc(4), ABIStatus.ElementLimitExceeded, limit3);
        DecodeOk("(uint256[])", Doc(0), limit3);
        var limit1 = Limits(array: 1);
        DecodeOk("(uint256[])", Doc(1), limit1);
        DecodeFails("(uint256[])", Doc(2), ABIStatus.ElementLimitExceeded, limit1);
    }

    [TestMethod]
    public void BytesLength_HugeOrLargerThanTheInput_IsTruncated_WithoutAnyPaddingArithmeticOnIt()
    {
        foreach (byte[] length in HugeWords)
        {
            DecodeFails("(bytes)", Cat(Word(0x20), length), ABIStatus.Truncated);
            DecodeFails("(string)", Cat(Word(0x20), length), ABIStatus.Truncated);
            DecodeFails("(bytes)", Cat(Word(0x20), length, new byte[96]), ABIStatus.Truncated);
        }

        // One byte more than what follows the length word; exactly what follows but with the padding missing.
        DecodeFails("(bytes)", Cat(Word(0x20), Word(65), new byte[64]), ABIStatus.Truncated);
        DecodeFails("(bytes)", Cat(Word(0x20), Word(33), new byte[33]), ABIStatus.Truncated);
        DecodeFails("(bytes)", Cat(Word(0x20), Word(1), [0x01]), ABIStatus.Truncated);
        DecodeOk("(bytes)", Cat(Word(0x20), Word(33), new byte[64]));
        DecodeOk("(bytes)", Cat(Word(0x20), Word(32), new byte[32]));
        DecodeOk("(bytes)", Cat(Word(0x20), Word(0)));
        // Length 0 followed by extra bytes is trailing data, not padding.
        DecodeFails("(bytes)", Cat(Word(0x20), Word(0), new byte[32]), ABIStatus.NonCanonicalLayout);
        DecodeFails("(bytes)", Cat(Word(0x20), Word(0), [0x00]), ABIStatus.NonCanonicalLayout);
    }

    [TestMethod]
    public void BytesLength_OnAHugeButAllowedInput_StillNeverOverflows()
    {
        // With MaxEncodedBytes at int.MaxValue the length arithmetic is exercised at its extreme without allocating 2 GiB:
        // every length word at or above 2^31 - 32 cannot fit a small buffer and must be rejected, not wrapped.
        var unbounded = Limits(bytes: int.MaxValue);
        foreach (byte[] length in HugeWords)
        {
            DecodeFails("(bytes)", Cat(Word(0x20), length, new byte[64]), ABIStatus.Truncated, unbounded);
        }
    }

    // ------------------------------------------------------------------ padding and scalars

    [TestMethod]
    public void BytesAndStringPadding_AnyNonZeroByteIsInvalidPadding_WithItsOffset()
    {
        foreach (string type in new[] { "(bytes)", "(string)" })
        {
            foreach (int payloadLength in new[] { 1, 31, 33, 63 })
            {
                int padding = (32 - (payloadLength & 31)) & 31;
                for (int index = 0; index < padding; index++)
                {
                    byte[] doc = Cat(Word(0x20), BytesValue(new byte[payloadLength]));
                    int position = 64 + payloadLength + index;
                    doc[position] = 0x01;
                    ABIError error = DecodeFails(type, doc, ABIStatus.InvalidPadding);
                    Assert.AreEqual(position, error.DocumentOffset, $"{type} length {payloadLength} padding byte {index}");
                }
            }
        }
    }

    [TestMethod]
    public void NonZeroPayloadBytes_AreNotPadding_AndAreAccepted()
    {
        DecodeOk("(bytes)", Cat(Word(0x20), BytesValue(Filled(0xff)[..31])));
    }

    [TestMethod]
    public void FixedBytesRightPadding_AndFunctionTrailingBytes_AreInvalidPadding()
    {
        for (int m = 1; m < 32; m++)
        {
            for (int index = m; index < 32; index++)
            {
                byte[] word = new byte[32];
                word[index] = 0x80;
                ABIError error = DecodeFails($"(bytes{m})", word, ABIStatus.InvalidPadding);
                Assert.AreEqual(0, error.DocumentOffset);
            }
        }

        DecodeOk("(bytes32)", Filled(0xff));
        for (int index = 24; index < 32; index++)
        {
            byte[] word = Cat(Filled(0x11)[..24], new byte[8]);
            word[index] = 0x01;
            DecodeFails("(function)", word, ABIStatus.InvalidPadding);
        }
    }

    [TestMethod]
    public void AddressBool_AndIntegerWidthViolations_AreInvalidScalar()
    {
        for (int index = 0; index < 12; index++)
        {
            byte[] word = new byte[32];
            word[index] = 0x01;
            DecodeFails("(address)", word, ABIStatus.InvalidScalar);
        }

        DecodeFails("(bool)", Word(2), ABIStatus.InvalidScalar);
        DecodeFails("(bool)", Word(0x100), ABIStatus.InvalidScalar);
        DecodeFails("(bool)", Filled(0xff), ABIStatus.InvalidScalar);
        DecodeFails("(uint8)", Word(256), ABIStatus.InvalidScalar);
        DecodeFails("(uint160)", WordHex("10000000000000000000000000000000000000000"), ABIStatus.InvalidScalar);
        DecodeFails("(uint248)", WordHex("100" + new string('0', 60)), ABIStatus.InvalidScalar);
        DecodeFails("(int8)", Word(0x80), ABIStatus.InvalidScalar);                                                // +128
        DecodeFails("(int8)", Word(0xff), ABIStatus.InvalidScalar);                                                // +255, not -1
        DecodeFails("(int8)", Cat(Enumerable.Repeat((byte)0xff, 31).ToArray(), [0x7f]), ABIStatus.InvalidScalar);  // -129 pattern
        DecodeFails("(int16)", Cat(Enumerable.Repeat((byte)0xff, 30).ToArray(), [0x7f, 0xff]), ABIStatus.InvalidScalar); // 0xff..7fff = -32769
        DecodeFails("(int64)", Cat([0x80], new byte[23], Word(0)[24..]), ABIStatus.InvalidScalar);                 // sign bit set only in the extension
        DecodeOk("(int8)", Cat(Enumerable.Repeat((byte)0xff, 31).ToArray(), [0x80]));                              // -128 is fine
        DecodeOk("(int8)", Cat(Enumerable.Repeat((byte)0xff, 32).ToArray()));                                       // -1
    }

    [TestMethod]
    public void ScalarErrors_InsideNestedStructures_ReportThePrimitivesOffsetAndNode()
    {
        // ((uint256,uint8)): the uint8 sits at offset 32 of the static nested tuple; node ids: 0 root, 1 tuple, 2 uint256, 3 uint8.
        ABIError error = DecodeFails("((uint256,uint8))", Cat(Word(1), Word(256)), ABIStatus.InvalidScalar);
        Assert.AreEqual(32, error.DocumentOffset);
        Assert.AreEqual(3, error.SchemaNode);
    }

    // ------------------------------------------------------------------ depth, zero-width values, many logical values

    private static string NestedArrayTuple(int dimensions) => "(uint256" + string.Concat(Enumerable.Repeat("[]", dimensions)) + ")";

    /// <summary>A uint256[]...[] value, <paramref name="dimensions"/> deep, with one element per level and an empty innermost array (or one uint256).</summary>
    private static byte[] NestedArrayDocument(int dimensions, bool innermostHoldsAValue)
    {
        byte[] value = innermostHoldsAValue ? Cat(Word(1), Word(42)) : Word(0);
        for (int level = 1; level < dimensions; level++)
        {
            value = Cat(Word(1), Word(0x20), value);
        }

        return Cat(Word(0x20), value);
    }

    [TestMethod]
    public void EmptyArraysNestedToTheDepthLimit_AreAcceptedAtTheLimit_AndRejectedBelowIt()
    {
        // Dimensions k: arrays sit at depths 2..k+1; the empty innermost array is depth k+1 and no leaf is ever entered.
        foreach (int dimensions in new[] { 1, 2, 5, 6, 7, 10, 30 })
        {
            string tuple = NestedArrayTuple(dimensions);
            byte[] doc = NestedArrayDocument(dimensions, innermostHoldsAValue: false);
            DecodeOk(tuple, doc, Limits(depth: dimensions + 1));
            DecodeOk(tuple, doc, Limits(depth: ABIDecodeLimits.HardMaxValueDepth));
            if (dimensions >= 1)
            {
                DecodeFails(tuple, doc, ABIStatus.DepthLimitExceeded, Limits(depth: dimensions));
            }
        }
    }

    [TestMethod]
    public void ANonEmptyInnermostArray_NeedsOneMoreLevelForItsLeaf()
    {
        foreach (int dimensions in new[] { 1, 3, 6, 7, 30 })
        {
            string tuple = NestedArrayTuple(dimensions);
            byte[] doc = NestedArrayDocument(dimensions, innermostHoldsAValue: true);
            DecodeOk(tuple, doc, Limits(depth: dimensions + 2));
            DecodeFails(tuple, doc, ABIStatus.DepthLimitExceeded, Limits(depth: dimensions + 1));
        }
    }

    [TestMethod]
    public void DepthLimitOne_AllowsOnlyAZeroArgumentRoot()
    {
        DecodeOk("()", [], Limits(depth: 1));
        DecodeFails("(uint256)", Word(1), ABIStatus.DepthLimitExceeded, Limits(depth: 1));
        DecodeOk("(uint256)", Word(1), Limits(depth: 2));
    }

    [TestMethod]
    public void ManyZeroByteElements_InsideALargeArray_AreBoundedByTheLogicalValueBudgets()
    {
        // (()[]) with N elements is exactly 64 bytes, whatever N is. visited = 2 + N; work = 4 + N (root, array, pointer word, count word, elements).
        const int N = 4096;
        byte[] doc = Cat(Word(0x20), Word(N));
        Assert.AreEqual(64, doc.Length);
        DecodeOk("(()[])", doc);                                              // default limits: 4096 elements, 16384 values, 100000 work
        DecodeOk("(()[])", doc, Limits(visited: 2 + N, work: 4 + N));
        DecodeFails("(()[])", doc, ABIStatus.WorkLimitExceeded, Limits(visited: 1 + N));
        DecodeFails("(()[])", doc, ABIStatus.WorkLimitExceeded, Limits(work: 3 + N));
        DecodeFails("(()[])", Cat(Word(0x20), Word(N + 1)), ABIStatus.ElementLimitExceeded);
        DecodeFails("(()[])", Cat(Word(0x20), Word(N + 1)), ABIStatus.WorkLimitExceeded, Limits(array: N + 1, visited: 2 + N));
    }

    [TestMethod]
    public void NestedZeroWidthTuples_AreEachCharged()
    {
        // (((),())[]) with 10 elements: 64 bytes; per element 1 (the tuple) + 2 (its empty members) visited values.
        byte[] doc = Cat(Word(0x20), Word(10));
        DecodeOk("(((),())[])", doc, Limits(visited: 2 + 30, work: 4 + 30));
        DecodeFails("(((),())[])", doc, ABIStatus.WorkLimitExceeded, Limits(visited: 2 + 29));
        DecodeFails("(((),())[])", doc, ABIStatus.WorkLimitExceeded, Limits(work: 4 + 29));
    }

    [TestMethod]
    public void HighLogicalValueCount_WithASmallPayload_IsBounded()
    {
        // 4096 uint256 elements cost 4096 * 2 work units and 4096 visited values on 128 KiB; an attacker's cheap shapes are the zero-width ones above.
        byte[] doc = Cat(Word(0x20), Word(4096), new byte[4096 * 32]);
        DecodeOk("(uint256[])", doc);
        DecodeFails("(uint256[])", doc, ABIStatus.WorkLimitExceeded, Limits(visited: 4096 + 1));    // root + array + 4096 elements = 4098 needed
        DecodeOk("(uint256[])", doc, Limits(visited: 4098, work: 4 + 2 * 4096));
        DecodeFails("(uint256[])", doc, ABIStatus.WorkLimitExceeded, Limits(work: 3 + 2 * 4096));
    }

    // ------------------------------------------------------------------ empty values inside nested tuples are canonical, not shortcuts

    [TestMethod]
    public void EmptyDynamicValues_MustStillHaveACanonicalPointer_NotAShortcutIntoZeroFilledHeadBytes()
    {
        // (bytes): empty bytes with pointer 0 (into the head) is invalid even though the head word is zero and "looks empty".
        DecodeFails("(bytes)", Cat(Word(0), Word(0)), ABIStatus.InvalidOffset);
        DecodeFails("(uint256[])", Cat(Word(0), Word(0)), ABIStatus.InvalidOffset);
        // An empty dynamic value still occupies its 32-byte length or count word.
        DecodeFails("(bytes)", Word(0x20), ABIStatus.Truncated);
        DecodeFails("(uint256[])", Word(0x20), ABIStatus.Truncated);
        DecodeOk("(bytes)", Cat(Word(0x20), Word(0)));
        DecodeOk("(uint256[])", Cat(Word(0x20), Word(0)));
    }

    // ------------------------------------------------------------------ no exception for any malformed shape: a small exhaustive sweep

    [TestMethod]
    public void EveryWordValueInEveryPositionOfSeveralShapes_NeverThrows_AndAgreesWithTheReferenceModel()
    {
        string[] shapes = ["(uint256,bytes)", "(bytes[])", "((uint256,bytes)[2])", "(uint8,string,uint256[])"];
        ulong[] probes = [0, 1, 0x1f, 0x20, 0x21, 0x40, 0x60, 0x80, 0xa0, 0xc0, 0x100, 0x7fffffff, 0x80000000, ulong.MaxValue];
        foreach (string shape in shapes)
        {
            ABISchema schema = CompileTuple(shape);
            byte[] canonical = shape switch
            {
                "(uint256,bytes)" => Cat(Word(5), Word(0x40), BytesValue(Ascii("abc"))),
                "(bytes[])" => Cat(Word(0x20), Word(2), Word(0x40), Word(0x80), BytesValue(Ascii("a")), BytesValue(Ascii("bb"))),
                "((uint256,bytes)[2])" => Cat(Word(0x20), Word(0x40), Word(0xc0), Word(1), Word(0x40), BytesValue(Ascii("a")), Word(2), Word(0x40), BytesValue(Ascii("b"))),
                _ => Cat(Word(7), Word(0x60), Word(0xa0), BytesValue(Ascii("xy")), Word(2), Word(1), Word(2)),
            };
            DecodeOk(canonical, schema);
            for (int word = 0; word < canonical.Length / 32; word++)
            {
                foreach (ulong probe in probes.Append(ulong.MaxValue - 31))
                {
                    byte[] mutated = (byte[])canonical.Clone();
                    Word(probe).CopyTo(mutated, word * 32);
                    var limits = ABIDecodeLimits.Default;
                    bool accepted = ABIData.TryDecodeComplete(mutated, schema, in limits, out var data, out var status);
                    Assert.AreEqual(accepted, data.IsValid);
                    Assert.AreEqual(accepted ? ABIStatus.Success : status, status);

                    // Oracle: strict acceptance <=> the permissive reference decode succeeds and re-encodes to the very same bytes.
                    RTupleType type = ABIReference.ParseTuple(shape);
                    bool expected = ABIReference.TryDecodePermissive(type, mutated, out RSeq? value)
                        && ABIReference.EncodeRoot(type, value!).AsSpan().SequenceEqual(mutated);
                    Assert.AreEqual(expected, accepted, $"{shape} word {word} := {probe:x}");
                }
            }
        }
    }
}
