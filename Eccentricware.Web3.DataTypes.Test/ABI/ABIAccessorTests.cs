using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// The accessor contract of spec 5.1: default views, path errors, kind/width mismatches, exact narrowing, and "every Try* resets its outs on
/// failure". A table of accessors is run against one field of every kind, and for each (accessor, field) the outcome is stated by hand.
/// </summary>
[TestClass]
public sealed class ABIAccessorTests
{
    // One field of every kind. Paths: 0 uint8, 1 int8, 2 address, 3 bool, 4 bytes4, 5 function, 6 fixed128x18, 7 ufixed128x18, 8 bytes, 9 string,
    // 10 (uint8) tuple, 11 uint8[2], 12 uint8[] (3 elements), 13 uint256 (wide value), 14 int256 (wide negative).
    private const string Tuple = "(uint8,int8,address,bool,bytes4,function,fixed128x18,ufixed128x18,bytes,string,(uint8),uint8[2],uint8[],uint256,int256)";

    private static readonly ABITypeKind[] Kinds =
    [
        ABITypeKind.UInt, ABITypeKind.Int, ABITypeKind.Address, ABITypeKind.Bool, ABITypeKind.FixedBytes, ABITypeKind.Function, ABITypeKind.Fixed,
        ABITypeKind.UFixed, ABITypeKind.Bytes, ABITypeKind.String, ABITypeKind.Tuple, ABITypeKind.FixedArray, ABITypeKind.DynamicArray, ABITypeKind.UInt, ABITypeKind.Int,
    ];

    private static byte[] Document()
    {
        var type = ABIReference.ParseTuple(Tuple);
        RWord W(byte[] w) => new(w);
        var value = new RSeq(
        [
            W(Word(200)), W(ABIReference.ToWord(-5)), W(Cat(new byte[12], Filled(0xab)[..20])), W(Word(1)), W(Cat(Hex("deadbeef"), new byte[28])),
            W(Cat(Filled(0x22)[..20], Hex("01020304"), new byte[8])), W(ABIReference.ToWord(-1_500_000_000_000_000_000)), W(Word(2_500_000_000_000_000_000)),
            new RBlob(Ascii("raw")), new RBlob(Ascii("text")), new RSeq([W(Word(9))]), new RSeq([W(Word(1)), W(Word(2))]), new RSeq([W(Word(3)), W(Word(4)), W(Word(5))]),
            W(Cat([0x01], new byte[31])), W(ABIReference.ToWord(System.Numerics.BigInteger.MinusOne << 200)),
        ]);
        return ABIReference.EncodeRoot(type, value);
    }

    private delegate ABIStatus Accessor(in ABIData data, int[] path);

    private sealed record Entry(string Name, Accessor Call, ABITypeKind[] Accepts);

    private static readonly Entry[] Accessors =
    [
        new("TryGetWord", (in ABIData d, int[] p) => { bool ok = d.TryGetWord(p, out var w, out var s); AssertFailedOutsDefault(ok, w.IsEmpty); return s; },
            [ABITypeKind.UInt, ABITypeKind.Int, ABITypeKind.Address, ABITypeKind.Bool, ABITypeKind.FixedBytes, ABITypeKind.Function]), // not Fixed/UFixed: a scaled word never leaves without its scale (spec 5.1, design note 64)
        new("TryGetUInt64", (in ABIData d, int[] p) => { bool ok = d.TryGetUInt64(p, out var v, out var s); AssertFailedOutsDefault(ok, v == 0); return s; }, [ABITypeKind.UInt]),
        new("TryGetInt64", (in ABIData d, int[] p) => { bool ok = d.TryGetInt64(p, out var v, out var s); AssertFailedOutsDefault(ok, v == 0); return s; }, [ABITypeKind.Int]),
        new("TryGetAddress", (in ABIData d, int[] p) => { bool ok = d.TryGetAddress(p, out var v, out var s); AssertFailedOutsDefault(ok, v.IsEmpty); return s; }, [ABITypeKind.Address]),
        new("TryGetBool", (in ABIData d, int[] p) => { bool ok = d.TryGetBool(p, out var v, out var s); AssertFailedOutsDefault(ok, !v); return s; }, [ABITypeKind.Bool]),
        new("TryGetBytePayload", (in ABIData d, int[] p) => { bool ok = d.TryGetBytePayload(p, out var v, out var r, out var s); AssertFailedOutsDefault(ok, v.IsEmpty && r == default); return s; },
            [ABITypeKind.Bytes, ABITypeKind.String]),
        new("TryGetStringUtf8Validated", (in ABIData d, int[] p) => { bool ok = d.TryGetStringUtf8Validated(p, out var v, out var r, out var s); AssertFailedOutsDefault(ok, v.IsEmpty && r == default); return s; },
            [ABITypeKind.String]),
        new("TryGetFixedBytes", (in ABIData d, int[] p) => { bool ok = d.TryGetFixedBytes(p, out var v, out var s); AssertFailedOutsDefault(ok, v.IsEmpty); return s; }, [ABITypeKind.FixedBytes]),
        new("TryGetFunction", (in ABIData d, int[] p) => { bool ok = d.TryGetFunction(p, out var a, out var sel, out var s); AssertFailedOutsDefault(ok, a.IsEmpty && sel == 0); return s; }, [ABITypeKind.Function]),
        new("TryGetSignedScaledWord", (in ABIData d, int[] p) => { bool ok = d.TryGetSignedScaledWord(p, out var w, out var sc, out var s); AssertFailedOutsDefault(ok, w.IsEmpty && sc == 0); return s; }, [ABITypeKind.Fixed]),
        new("TryGetUnsignedScaledWord", (in ABIData d, int[] p) => { bool ok = d.TryGetUnsignedScaledWord(p, out var w, out var sc, out var s); AssertFailedOutsDefault(ok, w.IsEmpty && sc == 0); return s; }, [ABITypeKind.UFixed]),
        new("TryGetUInt256Word", (in ABIData d, int[] p) => { bool ok = d.TryGetUInt256Word(p, out var w, out var s); AssertFailedOutsDefault(ok, w.IsEmpty); return s; }, [ABITypeKind.UInt]),
        new("TryGetInt256Word", (in ABIData d, int[] p) => { bool ok = d.TryGetInt256Word(p, out var w, out var s); AssertFailedOutsDefault(ok, w.IsEmpty); return s; }, [ABITypeKind.Int]),
        new("TryGetArrayLength", (in ABIData d, int[] p) => { bool ok = d.TryGetArrayLength(p, out var n, out var s); AssertFailedOutsDefault(ok, n == 0); return s; }, [ABITypeKind.FixedArray, ABITypeKind.DynamicArray]),
        new("TryLocate", (in ABIData d, int[] p) => { bool ok = d.TryLocate(p, out var f, out var s); AssertFailedOutsDefault(ok, f == default); return s; }, Enum.GetValues<ABITypeKind>()),
    ];

    /// <summary>On a failed call every out must be its default; <paramref name="outsAreDefault"/> is evaluated by the caller after the call.</summary>
    private static void AssertFailedOutsDefault(bool succeeded, bool outsAreDefault)
    {
        if (!succeeded)
        {
            Assert.IsTrue(outsAreDefault, "a failed Try* must leave every out default");
        }
    }

    [TestMethod]
    public void EveryAccessor_OnEveryFieldKind_SucceedsOnItsKinds_AndIsTypeMismatchOtherwise()
    {
        ABIData data = DecodeOk(Tuple, Document());
        for (int field = 0; field < Kinds.Length; field++)
        {
            foreach (Entry accessor in Accessors)
            {
                ABIStatus status = accessor.Call(in data, [field]);
                bool applies = accessor.Accepts.Contains(Kinds[field]);
                // field 13 (a wide uint256) and 14 (a wide negative int256) legitimately do not fit 64 bits.
                ABIStatus expected = applies ? ABIStatus.Success : ABIStatus.TypeMismatch;
                if (applies && field == 13 && accessor.Name == "TryGetUInt64") expected = ABIStatus.ValueOutOfRange;
                if (applies && field == 14 && accessor.Name == "TryGetInt64") expected = ABIStatus.ValueOutOfRange;
                Assert.AreEqual(expected, status, $"{accessor.Name} on field {field} ({Kinds[field]})");
            }
        }
    }

    [TestMethod]
    public void EveryAccessor_OnADefaultView_IsInvalidArgument_WithDefaultOuts()
    {
        ABIData data = default;
        Assert.IsFalse(data.IsValid);
        Assert.IsNull(data.Schema);
        Assert.IsTrue(data.EncodedBytes.IsEmpty);
        foreach (Entry accessor in Accessors)
        {
            Assert.AreEqual(ABIStatus.InvalidArgument, accessor.Call(in data, []), accessor.Name + " [] ");
            Assert.AreEqual(ABIStatus.InvalidArgument, accessor.Call(in data, [0]), accessor.Name + " [0]");
            Assert.AreEqual(ABIStatus.InvalidArgument, accessor.Call(in data, [-1]), accessor.Name + " [-1]");
        }
    }

    [TestMethod]
    public void AFailedDecode_LeavesADefaultViewWhoseAccessorsFail()
    {
        ABIDecodeLimits limits = ABIDecodeLimits.Default;
        Assert.IsFalse(ABIData.TryDecodeComplete([1], CompileTuple("(uint256)"), in limits, out ABIData data, out _));
        foreach (Entry accessor in Accessors)
        {
            Assert.AreEqual(ABIStatus.InvalidArgument, accessor.Call(in data, [0]), accessor.Name);
        }
    }

    // ------------------------------------------------------------------ paths

    [TestMethod]
    public void Paths_NegativeOutOfRangeAndThroughPrimitives_AreInvalidPath_ForEveryAccessor()
    {
        ABIData data = DecodeOk(Tuple, Document());
        int[][] badPaths =
        [
            [-1], [15], [16], [int.MaxValue], [int.MinValue],              // root has 15 components
            [0, 0], [3, 0], [8, 0], [9, 0],                                // through a primitive, bytes and string
            [10, 1], [10, -1], [10, 0, 0],                                 // tuple with one component, then a primitive
            [11, 2], [11, -1], [11, 0, 0],                                 // uint8[2]
            [12, 3], [12, int.MaxValue], [12, 0, 0],                       // uint8[] with 3 elements
            [0, -1], [-1, 0], [10, 0, -1],
        ];
        foreach (int[] path in badPaths)
        {
            foreach (Entry accessor in Accessors)
            {
                Assert.AreEqual(ABIStatus.InvalidPath, accessor.Call(in data, path), $"{accessor.Name} [{string.Join(",", path)}]");
            }
        }
    }

    [TestMethod]
    public void PathErrors_TakePrecedenceOverTypeMismatch()
    {
        ABIData data = DecodeOk(Tuple, Document());
        // TryGetBool on a path through a primitive is a path error, not a mismatch; on a valid path of the wrong kind it is a mismatch.
        Assert.IsFalse(data.TryGetBool([0, 0], out _, out var status));
        Assert.AreEqual(ABIStatus.InvalidPath, status);
        Assert.IsFalse(data.TryGetBool([0], out _, out status));
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
    }

    [TestMethod]
    public void ValidNestedPaths_SelectComponentsAndElements()
    {
        ABIData data = DecodeOk(Tuple, Document());
        Assert.IsTrue(data.TryGetUInt64([10, 0], out ulong inner, out _));
        Assert.AreEqual(9UL, inner);
        Assert.IsTrue(data.TryGetUInt64([11, 1], out ulong second, out _));
        Assert.AreEqual(2UL, second);
        Assert.IsTrue(data.TryGetUInt64([12, 2], out ulong third, out _));
        Assert.AreEqual(5UL, third);
        Assert.IsTrue(data.TryGetArrayLength([11], out int fixedLength, out _));
        Assert.AreEqual(2, fixedLength);
        Assert.IsTrue(data.TryGetArrayLength([12], out int dynamicLength, out _));
        Assert.AreEqual(3, dynamicLength);
        // Counts are metadata: there is no path that addresses the count word itself (element 0 is the first ELEMENT).
        Assert.IsTrue(data.TryLocate([12, 0], out var first, out _));
        Assert.AreEqual(32, first.ValueRange.Length);
    }

    // ------------------------------------------------------------------ exact values and narrowing

    [TestMethod]
    public void ScalarAccessors_ReturnTheExactValues()
    {
        ABIData data = DecodeOk(Tuple, Document());
        Assert.IsTrue(data.TryGetUInt64([0], out ulong u8, out _));
        Assert.AreEqual(200UL, u8);
        Assert.IsTrue(data.TryGetInt64([1], out long i8, out _));
        Assert.AreEqual(-5L, i8);
        Assert.IsTrue(data.TryGetAddress([2], out var address, out _));
        CollectionAssert.AreEqual(Filled(0xab)[..20], address.ToArray());
        Assert.IsTrue(data.TryGetBool([3], out bool flag, out _));
        Assert.IsTrue(flag);
        Assert.IsTrue(data.TryGetFixedBytes([4], out var four, out _));
        CollectionAssert.AreEqual(Hex("deadbeef"), four.ToArray());
        Assert.IsTrue(data.TryGetFunction([5], out var target, out uint selector, out _));
        CollectionAssert.AreEqual(Filled(0x22)[..20], target.ToArray());
        Assert.AreEqual(0x01020304U, selector);
        Assert.IsTrue(data.TryGetSignedScaledWord([6], out var negative, out byte scale, out _));
        CollectionAssert.AreEqual(ABIReference.ToWord(-1_500_000_000_000_000_000), negative.ToArray());
        Assert.AreEqual((byte)18, scale);
        Assert.IsTrue(data.TryGetUnsignedScaledWord([7], out var positive, out _, out _));
        CollectionAssert.AreEqual(Word(2_500_000_000_000_000_000), positive.ToArray());
        Assert.IsTrue(data.TryGetBytePayload([8], out var raw, out ABIRange rawRange, out _));
        CollectionAssert.AreEqual(Ascii("raw"), raw.ToArray());
        Assert.AreEqual(3, rawRange.Length);
        Assert.IsTrue(data.TryGetBytePayload([9], out var viaBytes, out _, out _), "a string may also be read as raw bytes");
        CollectionAssert.AreEqual(Ascii("text"), viaBytes.ToArray());
        Assert.IsTrue(data.TryGetStringUtf8Validated([9], out var text, out _, out _));
        CollectionAssert.AreEqual(Ascii("text"), text.ToArray());
        Assert.IsFalse(data.TryGetStringUtf8Validated([8], out _, out _, out var status), "bytes is not a string");
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
    }

    [TestMethod]
    public void UInt64Narrowing_IsExactAtTheBoundary()
    {
        ABISchema schema = CompileTuple("(uint256,uint256,uint256,uint64,uint8)");
        byte[] doc = Cat(Word(ulong.MaxValue), WordHex("10000000000000000"), WordHex("ffffffffffffffffffffffffffffffff"), Word(ulong.MaxValue), Word(255));
        ABIData data = DecodeOk(doc, schema);
        Assert.IsTrue(data.TryGetUInt64([0], out ulong max, out _));
        Assert.AreEqual(ulong.MaxValue, max);
        Assert.IsFalse(data.TryGetUInt64([1], out ulong over, out var status));   // 2^64
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
        Assert.AreEqual(0UL, over);
        Assert.IsFalse(data.TryGetUInt64([2], out _, out status));                // 2^128 - 1
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
        Assert.IsTrue(data.TryGetUInt64([3], out ulong u64, out _));
        Assert.AreEqual(ulong.MaxValue, u64);
        Assert.IsTrue(data.TryGetUInt64([4], out ulong u8, out _));
        Assert.AreEqual(255UL, u8);
    }

    [TestMethod]
    public void Int64Narrowing_IsExactAtBothBoundaries_AndNeverReinterpretsSignedAsUnsigned()
    {
        ABISchema schema = CompileTuple("(int256,int256,int256,int256,int256,int256,int64)");
        byte[] doc = Cat(
            ABIReference.ToWord(long.MaxValue),
            ABIReference.ToWord(long.MinValue),
            ABIReference.ToWord((System.Numerics.BigInteger)long.MaxValue + 1),
            ABIReference.ToWord((System.Numerics.BigInteger)long.MinValue - 1),
            ABIReference.ToWord(-1),
            ABIReference.ToWord(System.Numerics.BigInteger.One << 200),
            ABIReference.ToWord(long.MinValue));
        ABIData data = DecodeOk(doc, schema);
        long value;
        ABIStatus status;
        Assert.IsTrue(data.TryGetInt64([0], out value, out _));
        Assert.AreEqual(long.MaxValue, value);
        Assert.IsTrue(data.TryGetInt64([1], out value, out _));
        Assert.AreEqual(long.MinValue, value);
        Assert.IsFalse(data.TryGetInt64([2], out value, out status));
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
        Assert.AreEqual(0L, value);
        Assert.IsFalse(data.TryGetInt64([3], out _, out status));
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
        Assert.IsTrue(data.TryGetInt64([4], out value, out _));
        Assert.AreEqual(-1L, value);
        Assert.IsFalse(data.TryGetInt64([5], out _, out status));
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
        Assert.IsTrue(data.TryGetInt64([6], out value, out _));
        Assert.AreEqual(long.MinValue, value);
        // A uint accessor never reads an int field (no signed -> unsigned reinterpretation), and vice versa.
        Assert.IsFalse(data.TryGetUInt64([4], out _, out status));
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
    }

    [TestMethod]
    public void FixedPoint_IsNeverReadAsAnOrdinaryInteger()
    {
        ABIData data = DecodeOk(Tuple, Document());
        Assert.IsFalse(data.TryGetInt64([6], out _, out var status));
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
        Assert.IsFalse(data.TryGetInt256Word([6], out _, out status));
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
        Assert.IsFalse(data.TryGetUInt64([7], out _, out status));
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
        Assert.IsFalse(data.TryGetUInt256Word([7], out _, out status));
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
        // Spec 5.1: "scale-bearing values MUST never be returned without scale metadata". The raw word accessor therefore refuses them too.
        Assert.IsFalse(data.TryGetWord([6], out ReadOnlySpan<byte> signedWord, out status));
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
        Assert.IsTrue(signedWord.IsEmpty, "a refused word read leaves the out span empty");
        Assert.IsFalse(data.TryGetWord([7], out ReadOnlySpan<byte> unsignedWord, out status));
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
        Assert.IsTrue(unsignedWord.IsEmpty);
        // The scaled accessors are the way in, and they return the scale with the word.
        Assert.IsTrue(data.TryGetSignedScaledWord([6], out ReadOnlySpan<byte> scaledSigned, out byte signedScale, out status));
        Assert.AreEqual(32, scaledSigned.Length);
        Assert.AreNotEqual(0, signedScale);
        Assert.IsTrue(data.TryGetUnsignedScaledWord([7], out ReadOnlySpan<byte> scaledUnsigned, out byte unsignedScale, out status));
        Assert.AreEqual(32, scaledUnsigned.Length);
        Assert.AreNotEqual(0, unsignedScale);
        // Signed and unsigned scaled accessors do not cross over either.
        Assert.IsFalse(data.TryGetUnsignedScaledWord([6], out _, out _, out status));
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
        Assert.IsFalse(data.TryGetSignedScaledWord([7], out _, out _, out status));
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
    }

    // ------------------------------------------------------------------ locate of the root and of nothing else

    [TestMethod]
    public void LocateEmptyPath_ReturnsTheRootTupleWithItsHeadAndWholeValue()
    {
        ABIData data = DecodeOk(Tuple, Document());
        Assert.IsTrue(data.TryLocate([], out ABIFieldLocation root, out var status));
        Assert.AreEqual(ABIStatus.Success, status);
        Assert.AreEqual(ABITypeKind.Tuple, root.Kind);
        Assert.AreEqual(new ABIRange(0, data.EncodedBytes.Length), root.ValueRange);
        // Head widths: eight scalars (256) + bytes and string pointers (64) + (uint8) (32) + uint8[2] (64) + uint8[] pointer (32) + uint256 and int256 (64) = 512.
        Assert.AreEqual(new ABIRange(0, 512), root.HeadRange);
    }

    // ------------------------------------------------------------------ comparison helpers

    [TestMethod]
    public void CompareUnsigned256_IsLexicographicBigEndian()
    {
        byte[] small = Word(5);
        byte[] big = Cat([0x01], new byte[31]);
        Assert.IsTrue(ABIData.TryCompareUnsigned256(small, big, out int c, out var status));
        Assert.AreEqual(-1, c);
        Assert.IsTrue(ABIData.TryCompareUnsigned256(big, small, out c, out _));
        Assert.AreEqual(1, c);
        Assert.IsTrue(ABIData.TryCompareUnsigned256(big, big, out c, out _));
        Assert.AreEqual(0, c);
        Assert.IsTrue(ABIData.TryCompareUnsigned256(Filled(0xff), Word(0), out c, out _));
        Assert.AreEqual(1, c, "unsigned: all-ones is the maximum, not -1");
        Assert.IsFalse(ABIData.TryCompareUnsigned256(new byte[31], big, out c, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.AreEqual(0, c);
        Assert.IsFalse(ABIData.TryCompareUnsigned256(big, new byte[33], out c, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.IsFalse(ABIData.TryCompareUnsigned256([], [], out _, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
    }

    [TestMethod]
    public void CompareSigned256_ComparesSignsBeforeMagnitudes()
    {
        byte[] minusOne = Filled(0xff);
        byte[] one = Word(1);
        byte[] min = Cat([0x80], new byte[31]);
        byte[] max = Cat([0x7f], Filled(0xff)[..31]);
        foreach ((byte[] left, byte[] right, int expected) in new[]
        {
            (minusOne, one, -1), (one, minusOne, 1), (min, max, -1), (max, min, 1), (min, minusOne, -1), (minusOne, min, 1),
            (minusOne, minusOne, 0), (one, one, 0), (Word(0), Word(0), 0), (Word(0), minusOne, 1), (Word(2), one, 1),
            (ABIReference.ToWord(-3), ABIReference.ToWord(-2), -1),
        })
        {
            Assert.IsTrue(ABIData.TryCompareSigned256(left, right, out int c, out _));
            Assert.AreEqual(expected, c, $"{Convert.ToHexString(left)} vs {Convert.ToHexString(right)}");
        }

        Assert.IsFalse(ABIData.TryCompareSigned256(new byte[31], one, out int none, out var status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.AreEqual(0, none);
    }

    [TestMethod]
    public void Compare_AgreesWithBigIntegerForRandomWords()
    {
        var rng = new DeterministicRng(99);
        for (int i = 0; i < 5_000; i++)
        {
            byte[] a = rng.Bytes(32);
            byte[] b = rng.Next(4) == 0 ? (byte[])a.Clone() : rng.Bytes(32);
            if (rng.Next(3) == 0)
            {
                Array.Clear(a, 0, rng.Next(33));
            }

            Assert.IsTrue(ABIData.TryCompareUnsigned256(a, b, out int unsigned, out _));
            Assert.AreEqual(Math.Sign(ABIReference.Unsigned(a).CompareTo(ABIReference.Unsigned(b))), unsigned);
            Assert.IsTrue(ABIData.TryCompareSigned256(a, b, out int signed, out _));
            int expected = ABIReference.Signed(a).CompareTo(ABIReference.Signed(b));
            Assert.AreEqual(Math.Sign(expected), signed);
        }
    }

    // ------------------------------------------------------------------ views are not forgeable

    [TestMethod]
    public void NoPublicConstructorOrFactoryCanMintAValidView()
    {
        foreach (Type type in new[] { typeof(ABIData), typeof(ABICallData) })
        {
            Assert.AreEqual(0, type.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Length, type.Name + " has no public constructor");
            // Only the TryDecode* entry points are public static producers of the view.
            var producers = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(m => m.GetParameters().Any(p => p.ParameterType.IsByRef && p.ParameterType.GetElementType() == type))
                .Select(m => m.Name)
                .Distinct();
            CollectionAssert.AreEquivalent(type == typeof(ABIData) ? new[] { "TryDecodeComplete" } : new[] { "TryDecode" }, producers.ToArray());
        }
    }
}
