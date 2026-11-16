using System.Runtime.InteropServices;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="HexBytes"/>: slicing, copying out, concatenation, word padding and the EVM calldata helpers.</summary>
[TestClass]
public sealed class HexBytesSliceConcatAndEvmTests
{
    private static byte[] BackingArray(HexBytes value)
    {
        Assert.IsTrue(MemoryMarshal.TryGetArray(value.Memory, out ArraySegment<byte> segment));
        return segment.Array!;
    }

    private static byte[] Pattern(int length)
    {
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
            bytes[i] = (byte)(i * 7 + 1);
        return bytes;
    }

    // ----------------- SliceSpan / TryCopyTo -----------------

    [TestMethod]
    public void SliceSpan_ReturnsAViewOfTheRequestedRange()
    {
        var value = new HexBytes(new byte[] { 1, 2, 3, 4 });

        Assert.IsTrue(value.SliceSpan(1, 2).SequenceEqual(new byte[] { 2, 3 }));
        Assert.IsTrue(value.SliceSpan(3).SequenceEqual(new byte[] { 4 }));
        Assert.IsTrue(value.SliceSpan(0).SequenceEqual(value.Span));
        Assert.IsTrue(value.SliceSpan(4).IsEmpty);
        Assert.IsTrue(value.SliceSpan(4, 0).IsEmpty);
        Assert.IsTrue(HexBytes.Empty.SliceSpan(0).IsEmpty);
        Assert.IsTrue(HexBytes.Empty.SliceSpan(0, 0).IsEmpty);
    }

    [TestMethod]
    public void SliceSpan_OutOfRange_ThrowsArgumentOutOfRange()
    {
        var value = new HexBytes(new byte[] { 1, 2, 3 });

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => value.SliceSpan(4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => value.SliceSpan(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => value.SliceSpan(4, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => value.SliceSpan(-1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => value.SliceSpan(1, 3));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => value.SliceSpan(0, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HexBytes.Empty.SliceSpan(1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HexBytes.Empty.SliceSpan(0, 1));
    }

    [TestMethod]
    public void TryCopyTo_ExactAndOversizedDestination_CopiesOnlyTheValue()
    {
        var value = new HexBytes(new byte[] { 1, 2, 3, 4 });

        byte[] exact = new byte[4];
        Assert.IsTrue(value.TryCopyTo(exact, out int written));
        Assert.AreEqual(4, written);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, exact);

        byte[] roomy = new byte[8];
        Array.Fill(roomy, (byte)0xEE);
        Assert.IsTrue(value.TryCopyTo(roomy, out written));
        Assert.AreEqual(4, written);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 0xEE, 0xEE, 0xEE, 0xEE }, roomy);
    }

    [TestMethod]
    public void TryCopyTo_TooSmallDestination_FailsAndLeavesItUntouched()
    {
        var value = new HexBytes(new byte[] { 1, 2, 3, 4 });

        byte[] tooSmall = [0xEE, 0xEE, 0xEE];
        Assert.IsFalse(value.TryCopyTo(tooSmall, out int written));
        Assert.AreEqual(0, written);
        CollectionAssert.AreEqual(new byte[] { 0xEE, 0xEE, 0xEE }, tooSmall);

        Assert.IsFalse(value.TryCopyTo(Span<byte>.Empty, out written));
        Assert.AreEqual(0, written);
    }

    [TestMethod]
    public void TryCopyTo_Empty_SucceedsWithZeroBytes_AndDoesNotTouchTheDestination()
    {
        byte[] destination = [0xEE, 0xEE];

        Assert.IsTrue(HexBytes.Empty.TryCopyTo(destination, out int written));
        Assert.AreEqual(0, written);
        CollectionAssert.AreEqual(new byte[] { 0xEE, 0xEE }, destination);

        Assert.IsTrue(HexBytes.Empty.TryCopyTo(Span<byte>.Empty, out written));
        Assert.AreEqual(0, written);
    }

    // ----------------- Slice -----------------

    [TestMethod]
    public void Slice_ReturnsTheRequestedRange()
    {
        var value = new HexBytes(new byte[] { 1, 2, 3, 4, 5 });

        CollectionAssert.AreEqual(new byte[] { 3, 4, 5 }, value.Slice(2).ToArray());
        CollectionAssert.AreEqual(new byte[] { 2, 3 }, value.Slice(1, 2).ToArray());
        CollectionAssert.AreEqual(value.ToArray(), value.Slice(0).ToArray());
        Assert.IsTrue(value.Slice(5).IsEmpty);
        Assert.IsTrue(value.Slice(5, 0).IsEmpty);
        Assert.IsTrue(value.Slice(2, 0).IsEmpty);
    }

    [TestMethod]
    public void Slice_ReturnsIndependentCopy_ThatLaterSourceMutationCannotChange()
    {
        // FromArrayUnsafe wraps the array, so mutating it afterwards changes the source value; a copying Slice must not follow.
        byte[] array = [1, 2, 3, 4, 5];
        HexBytes source = HexBytes.FromArrayUnsafe(array);

        HexBytes tail = source.Slice(1);
        HexBytes middle = source.Slice(1, 3);
        array[1] = 0xEE;
        array[2] = 0xEE;

        CollectionAssert.AreEqual(new byte[] { 2, 3, 4, 5 }, tail.ToArray());
        CollectionAssert.AreEqual(new byte[] { 2, 3, 4 }, middle.ToArray());
        Assert.AreNotSame(array, BackingArray(tail));
        Assert.AreNotSame(array, BackingArray(middle));
    }

    [TestMethod]
    public void Slice_OutOfRange_Throws_ForEmptyAndNonEmpty()
    {
        var value = new HexBytes(new byte[] { 1, 2, 3 });

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => value.Slice(4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => value.Slice(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => value.Slice(1, 3));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => value.Slice(0, -1));

        // Empty must obey the same contract as non-empty values and as Span.Slice.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HexBytes.Empty.Slice(1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HexBytes.Empty.Slice(0, 1));
        Assert.IsTrue(HexBytes.Empty.Slice(0).IsEmpty);
        Assert.IsTrue(HexBytes.Empty.Slice(0, 0).IsEmpty);
    }

    // ----------------- Concat -----------------

    [TestMethod]
    public void Concat_JoinsInOrder_AndHandlesEmptySides()
    {
        var a = new HexBytes(new byte[] { 1, 2 });
        var b = new HexBytes(new byte[] { 3 });

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, HexBytes.Concat(a, b).ToArray());
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (a + b).ToArray());
        CollectionAssert.AreEqual(new byte[] { 3, 1, 2 }, (b + a).ToArray());
        Assert.AreEqual(a, a + HexBytes.Empty);
        Assert.AreEqual(b, HexBytes.Empty + b);
        Assert.IsTrue((HexBytes.Empty + HexBytes.Empty).IsEmpty);
        Assert.IsTrue((default(HexBytes) + default(HexBytes)).IsEmpty);
    }

    [TestMethod]
    public void Concat_NonEmptySides_AllocatesAFreshArray()
    {
        var a = new HexBytes(new byte[] { 1, 2 });
        var b = new HexBytes(new byte[] { 3 });

        byte[] result = BackingArray(HexBytes.Concat(a, b));

        Assert.AreNotSame(BackingArray(a), result);
        Assert.AreNotSame(BackingArray(b), result);
    }

    [TestMethod]
    public void Concat_WithAnEmptySide_ReusesTheOtherSidesArray()
    {
        // Documented trade-off: values are immutable, so sharing the array is safe and avoids a copy.
        var a = new HexBytes(new byte[] { 1, 2 });

        Assert.AreSame(BackingArray(a), BackingArray(a + HexBytes.Empty));
        Assert.AreSame(BackingArray(a), BackingArray(HexBytes.Empty + a));
    }

    [TestMethod]
    public void Concat_LengthIsTheSum_AndIsAssociative()
    {
        var a = new HexBytes(Pattern(3));
        var b = new HexBytes(Pattern(40));
        var c = new HexBytes(Pattern(100_000)); // 100 KiB

        Assert.AreEqual(a.Length + b.Length + c.Length, (a + b + c).Length);
        Assert.AreEqual((a + b) + c, a + (b + c));
        Assert.IsTrue((a + b + c).Span.Slice(a.Length + b.Length).SequenceEqual(c.Span));
    }

    // ----------------- PadToWord -----------------

    [TestMethod]
    public void PadToWord_RightPadsWithZeros_ToMultipleOf32_ForEveryLengthUpTo100()
    {
        for (int length = 0; length <= 100; length++)
        {
            byte[] bytes = new byte[length];
            bytes.AsSpan().Fill(0xAA);
            var original = new HexBytes(bytes);

            HexBytes padded = original.PadToWord();

            Assert.AreEqual((length + 31) / 32 * 32, padded.Length, $"length={length}");
            Assert.IsTrue(padded.Span.Slice(0, length).SequenceEqual(bytes), $"length={length}");
            foreach (byte b in padded.Span.Slice(length))
                Assert.AreEqual((byte)0, b, $"length={length}");

            Assert.AreEqual(padded, padded.PadToWord(), $"idempotent length={length}");
            Assert.AreEqual(length, original.Length, $"original unchanged length={length}");
        }
    }

    [TestMethod]
    public void PadToWord_AlignedInput_ReturnsTheSameBytes()
    {
        var aligned = new HexBytes(Pattern(64));

        Assert.AreEqual(aligned, aligned.PadToWord());
        Assert.AreSame(BackingArray(aligned), BackingArray(aligned.PadToWord()));
    }

    [TestMethod]
    public void PadToWord_LargeInput_PadsToTheNextWord()
    {
        byte[] bytes = Pattern(1000);

        HexBytes padded = new HexBytes(bytes).PadToWord();

        Assert.AreEqual(1024, padded.Length);
        Assert.IsTrue(padded.Span.Slice(0, 1000).SequenceEqual(bytes));
        Assert.IsLessThan(0, padded.Span.Slice(1000).IndexOfAnyExcept((byte)0));
    }

    [TestMethod]
    public void PadToWord_Empty_StaysEmpty()
    {
        Assert.IsTrue(HexBytes.Empty.PadToWord().IsEmpty);
    }

    // ----------------- EVM helpers -----------------

    [TestMethod]
    public void TryGetFunctionSelector_ReadsTheFirstFourBytes()
    {
        // transfer(address,uint256) selector a9059cbb: literal, not derived by the code under test.
        var value = new HexBytes(new byte[] { 0xa9, 0x05, 0x9c, 0xbb, 0x11, 0x22 });

        Assert.IsTrue(value.TryGetFunctionSelector(out FunctionSelector selector));
        Assert.AreEqual("0xa9059cbb", selector.ToString());
        Assert.IsTrue(value.GetCalldataParamsSpan().SequenceEqual(new byte[] { 0x11, 0x22 }));
    }

    [TestMethod]
    public void TryGetFunctionSelector_ExactlyFourBytes_HasNoParams()
    {
        var value = new HexBytes(new byte[] { 0xa9, 0x05, 0x9c, 0xbb });

        Assert.IsTrue(value.TryGetFunctionSelector(out FunctionSelector selector));
        Assert.AreEqual("0xa9059cbb", selector.ToString());
        Assert.IsTrue(value.GetCalldataParamsSpan().IsEmpty);
    }

    [TestMethod]
    public void GetCalldataParamsSpan_FiveBytes_IsOneByte()
    {
        var value = new HexBytes(new byte[] { 1, 2, 3, 4, 0x55 });

        Assert.IsTrue(value.GetCalldataParamsSpan().SequenceEqual(new byte[] { 0x55 }));
    }

    [TestMethod]
    public void TryGetFunctionSelector_TooShort_FailsWithDefaultSelectorAndNoParams()
    {
        foreach (HexBytes tooShort in new[] { HexBytes.Empty, new HexBytes(new byte[] { 1 }), new HexBytes(new byte[] { 1, 2, 3 }) })
        {
            Assert.IsFalse(tooShort.TryGetFunctionSelector(out FunctionSelector selector));
            Assert.AreEqual(default, selector);
            Assert.IsTrue(tooShort.GetCalldataParamsSpan().IsEmpty);
        }
    }
}
