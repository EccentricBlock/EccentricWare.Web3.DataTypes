using System.Numerics;
using System.Runtime.InteropServices;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIWriteTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Value rules of the typed write methods (spec 7.2, 10.3): per-width range checks for every integer and fixed-point width, exact
/// source lengths, strict UTF-8, source/destination overlap and size arithmetic. A rejected value must leave the destination untouched
/// and report the documented status; expected words come from BigInteger arithmetic in the test-local reference model.
/// </summary>
[TestClass]
public sealed class ABIWriterValueTests
{
    private static readonly int[] Widths = [.. Enumerable.Range(1, 32).Select(i => i * 8)];

    private static BigInteger Pow2(int bits) => BigInteger.One << bits;

    /// <summary>Runs a one-event document against a fresh garbage-filled 32-byte destination; the destination must be untouched on failure.</summary>
    private static (RunResult Result, byte[] Destination) RunOne(string slot, Ev ev)
    {
        byte[] destination = new byte[256]; // roomy enough for a dynamic value, so capacity never preempts the value rules
        Array.Fill(destination, Garbage);
        RunResult result = RunWriter([ev], CompileTuple($"({slot})"), destination, GenerousEncode);
        RunResult measured = RunMeasurer([ev], CompileTuple($"({slot})"), GenerousEncode);
        Assert.AreEqual(result.Ok, measured.Ok, $"{slot}: writer and measurer disagree");
        Assert.AreEqual(result.Status, measured.Status, $"{slot}: writer and measurer statuses differ");
        if (!result.Ok)
        {
            Assert.IsTrue(destination.All(b => b == Garbage), $"{slot}: a rejected value changed the destination");
        }

        return (result, destination);
    }

    private static void AssertAccepted(string slot, Ev ev, BigInteger expectedValue)
    {
        var (result, destination) = RunOne(slot, ev);
        Assert.IsTrue(result.Ok, $"{slot} {expectedValue}: {result.Status}");
        Assert.AreEqual(32, result.Length);
        CollectionAssert.AreEqual(ABIReference.ToWord(expectedValue), destination[..32], $"{slot} {expectedValue}");
    }

    private static void AssertRejected(string slot, Ev ev, ABIStatus expected, string why)
    {
        var (result, _) = RunOne(slot, ev);
        Assert.IsFalse(result.Ok, $"{slot} {why}: accepted");
        Assert.AreEqual(expected, result.Status, $"{slot} {why}");
    }

    // ------------------------------------------------------------------ uint

    [TestMethod]
    public void UInt64_EveryWidth_AcceptsUpToTheMaximumAndRejectsOneMore()
    {
        foreach (int w in Widths)
        {
            string slot = $"uint{w}";
            BigInteger max = Pow2(w) - 1;
            if (w < 64)
            {
                AssertAccepted(slot, Ev.U64(0), 0);
                AssertAccepted(slot, Ev.U64((ulong)max), max);
                AssertAccepted(slot, Ev.U64((ulong)max / 2 + 1), max / 2 + 1);
                AssertRejected(slot, Ev.U64((ulong)max + 1), ABIStatus.ValueOutOfRange, "max+1");
                AssertRejected(slot, Ev.U64(ulong.MaxValue), ABIStatus.ValueOutOfRange, "ulong.Max");
            }
            else
            {
                AssertAccepted(slot, Ev.U64(ulong.MaxValue), ulong.MaxValue);
                AssertAccepted(slot, Ev.U64(0), 0);
                AssertAccepted(slot, Ev.U64(1UL << 63), BigInteger.One << 63);
            }
        }
    }

    [TestMethod]
    public void UInt256Word_EveryWidth_AcceptsTheMaximum_AndRejectsEveryHighBit()
    {
        foreach (int w in Widths)
        {
            string slot = $"uint{w}";
            BigInteger max = Pow2(w) - 1;
            AssertAccepted(slot, Ev.Uint256(ABIReference.ToWord(max)), max);
            AssertAccepted(slot, Ev.Uint256(ABIReference.ToWord(0)), 0);
            AssertAccepted(slot, Ev.Uint256(ABIReference.ToWord(max / 3)), max / 3);
            for (int bit = w; bit < 256; bit++)
            {
                AssertRejected(slot, Ev.Uint256(ABIReference.ToWord(Pow2(bit))), ABIStatus.ValueOutOfRange, $"bit {bit} set");
                AssertRejected(slot, Ev.Uint256(ABIReference.ToWord(max | Pow2(bit))), ABIStatus.ValueOutOfRange, $"max with bit {bit} set");
            }
        }
    }

    // ------------------------------------------------------------------ int

    [TestMethod]
    public void Int64_EveryWidth_AcceptsTheRange_AndRejectsOneBeyondEitherEnd()
    {
        foreach (int w in Widths)
        {
            string slot = $"int{w}";
            if (w < 64)
            {
                BigInteger max = Pow2(w - 1) - 1;
                BigInteger min = -Pow2(w - 1);
                AssertAccepted(slot, Ev.I64((long)max), max);
                AssertAccepted(slot, Ev.I64((long)min), min);
                AssertAccepted(slot, Ev.I64(-1), -1);
                AssertAccepted(slot, Ev.I64(0), 0);
                AssertAccepted(slot, Ev.I64(1), 1);
                AssertRejected(slot, Ev.I64((long)max + 1), ABIStatus.ValueOutOfRange, "max+1");
                AssertRejected(slot, Ev.I64((long)min - 1), ABIStatus.ValueOutOfRange, "min-1");
                AssertRejected(slot, Ev.I64(long.MaxValue), ABIStatus.ValueOutOfRange, "long.Max");
                AssertRejected(slot, Ev.I64(long.MinValue), ABIStatus.ValueOutOfRange, "long.Min");
            }
            else
            {
                AssertAccepted(slot, Ev.I64(long.MaxValue), long.MaxValue);
                AssertAccepted(slot, Ev.I64(long.MinValue), long.MinValue);
                AssertAccepted(slot, Ev.I64(-1), -1);
            }
        }
    }

    [TestMethod]
    public void Int256Word_EveryWidth_AcceptsTheRange_AndRejectsBadSignExtension()
    {
        foreach (int w in Widths)
        {
            string slot = $"int{w}";
            BigInteger max = Pow2(w - 1) - 1;
            BigInteger min = -Pow2(w - 1);
            foreach (BigInteger value in new[] { max, min, BigInteger.Zero, BigInteger.One, BigInteger.MinusOne, max / 3, min / 3 })
            {
                AssertAccepted(slot, Ev.Int256(ABIReference.ToWord(value)), value);
            }

            if (w < 256)
            {
                AssertRejected(slot, Ev.Int256(ABIReference.ToWord(max + 1)), ABIStatus.ValueOutOfRange, "max+1");
                AssertRejected(slot, Ev.Int256(ABIReference.ToWord(min - 1)), ABIStatus.ValueOutOfRange, "min-1");
            }

            for (int bit = w; bit < 256; bit++)
            {
                // A positive value with one extension bit set, and a negative one with one extension bit cleared.
                byte[] positive = ABIReference.ToWord(BigInteger.One);
                positive[31 - bit / 8] |= (byte)(1 << (bit % 8));
                AssertRejected(slot, Ev.Int256(positive), ABIStatus.ValueOutOfRange, $"+1 with extension bit {bit}");

                byte[] negative = ABIReference.ToWord(BigInteger.MinusOne);
                negative[31 - bit / 8] &= (byte)~(1 << (bit % 8));
                AssertRejected(slot, Ev.Int256(negative), ABIStatus.ValueOutOfRange, $"-1 with extension bit {bit} cleared");
            }
        }
    }

    // ------------------------------------------------------------------ fixed point

    [TestMethod]
    public void FixedPoint_ScaledWords_FollowTheDeclaredWidth_WhateverTheScale()
    {
        foreach (int w in new[] { 8, 16, 24, 64, 120, 128, 248, 256 })
        {
            foreach (int scale in new[] { 1, 18, 80 })
            {
                string signed = $"fixed{w}x{scale}";
                string unsigned = $"ufixed{w}x{scale}";
                BigInteger smax = Pow2(w - 1) - 1;
                BigInteger smin = -Pow2(w - 1);
                BigInteger umax = Pow2(w) - 1;
                AssertAccepted(signed, new Ev(EvKind.SignedScaled, ABIReference.ToWord(smax)), smax);
                AssertAccepted(signed, new Ev(EvKind.SignedScaled, ABIReference.ToWord(smin)), smin);
                AssertAccepted(signed, new Ev(EvKind.SignedScaled, ABIReference.ToWord(-1)), -1);
                AssertAccepted(unsigned, new Ev(EvKind.UnsignedScaled, ABIReference.ToWord(umax)), umax);
                AssertAccepted(unsigned, new Ev(EvKind.UnsignedScaled, ABIReference.ToWord(0)), 0);
                if (w < 256)
                {
                    AssertRejected(signed, new Ev(EvKind.SignedScaled, ABIReference.ToWord(smax + 1)), ABIStatus.ValueOutOfRange, "fixed max+1");
                    AssertRejected(signed, new Ev(EvKind.SignedScaled, ABIReference.ToWord(smin - 1)), ABIStatus.ValueOutOfRange, "fixed min-1");
                    AssertRejected(unsigned, new Ev(EvKind.UnsignedScaled, ABIReference.ToWord(umax + 1)), ABIStatus.ValueOutOfRange, "ufixed max+1");
                }

                // A negative two's-complement word is never a valid unsigned value of a narrower width.
                if (w < 256)
                {
                    AssertRejected(unsigned, new Ev(EvKind.UnsignedScaled, ABIReference.ToWord(-1)), ABIStatus.ValueOutOfRange, "ufixed from -1");
                }
            }
        }
    }

    [TestMethod]
    public void FixedPointSlots_RejectThePlainIntegerMethods_AndIntegerSlotsRejectTheScaledOnes()
    {
        byte[] word = new byte[32];
        AssertRejected("fixed128x18", Ev.Int256(word), ABIStatus.TypeMismatch, "TryWriteInt256Word on fixed");
        AssertRejected("fixed128x18", Ev.I64(1), ABIStatus.TypeMismatch, "TryWriteInt64 on fixed");
        AssertRejected("ufixed128x18", Ev.Uint256(word), ABIStatus.TypeMismatch, "TryWriteUInt256Word on ufixed");
        AssertRejected("ufixed128x18", Ev.U64(1), ABIStatus.TypeMismatch, "TryWriteUInt64 on ufixed");
        AssertRejected("int128", new Ev(EvKind.SignedScaled, word), ABIStatus.TypeMismatch, "scaled on int");
        AssertRejected("uint128", new Ev(EvKind.UnsignedScaled, word), ABIStatus.TypeMismatch, "scaled on uint");
        AssertRejected("fixed128x18", new Ev(EvKind.UnsignedScaled, word), ABIStatus.TypeMismatch, "unsigned scaled on fixed");
        AssertRejected("ufixed128x18", new Ev(EvKind.SignedScaled, word), ABIStatus.TypeMismatch, "signed scaled on ufixed");
    }

    // ------------------------------------------------------------------ source lengths

    [TestMethod]
    public void WordSources_OfAnyLengthOtherThan32_AreInvalidArgument()
    {
        foreach (int length in new[] { 0, 1, 20, 31, 33, 64 })
        {
            byte[] source = new byte[length];
            AssertRejected("uint256", Ev.Uint256(source), ABIStatus.InvalidArgument, $"uint word of {length} bytes");
            AssertRejected("int256", Ev.Int256(source), ABIStatus.InvalidArgument, $"int word of {length} bytes");
            AssertRejected("fixed128x18", new Ev(EvKind.SignedScaled, source), ABIStatus.InvalidArgument, $"fixed word of {length} bytes");
            AssertRejected("ufixed128x18", new Ev(EvKind.UnsignedScaled, source), ABIStatus.InvalidArgument, $"ufixed word of {length} bytes");
        }
    }

    [TestMethod]
    public void AddressAndFunctionAddress_MustBeExactly20Bytes()
    {
        foreach (int length in new[] { 0, 1, 19, 21, 24, 32 })
        {
            AssertRejected("address", Ev.Addr(new byte[length]), ABIStatus.InvalidArgument, $"address of {length} bytes");
            AssertRejected("function", Ev.Func(new byte[length], 1), ABIStatus.InvalidArgument, $"function address of {length} bytes");
        }

        AssertAccepted("address", Ev.Addr(Enumerable.Repeat((byte)0xff, 20).ToArray()), Pow2(160) - 1);
        AssertAccepted("address", Ev.Addr(new byte[20]), 0);
    }

    [TestMethod]
    public void FixedBytes_MustBeExactlyM_ForEveryM()
    {
        for (int m = 1; m <= 32; m++)
        {
            string slot = $"bytes{m}";
            foreach (int length in new[] { 0, m - 1, m + 1, 33, 64 }.Where(l => l != m && l >= 0).Distinct())
            {
                AssertRejected(slot, Ev.FixedB(new byte[length]), ABIStatus.InvalidArgument, $"{length} bytes");
            }

            AssertAccepted(slot, Ev.FixedB(Enumerable.Repeat((byte)0xff, m).ToArray()), (Pow2(8 * m) - 1) << (8 * (32 - m)));
        }
    }

    [TestMethod]
    public void FunctionSelectorExtremes_AreWrittenBigEndian_WithEightZeroBytes()
    {
        foreach (uint selector in new[] { 0u, 1u, 0x80000000u, 0xaabbccddu, uint.MaxValue })
        {
            byte[] address = Enumerable.Range(1, 20).Select(i => (byte)i).ToArray();
            var (result, destination) = RunOne("function", Ev.Func(address, selector));
            Assert.IsTrue(result.Ok);
            byte[] expected = new byte[32];
            address.CopyTo(expected, 0);
            expected[20] = (byte)(selector >> 24);
            expected[21] = (byte)(selector >> 16);
            expected[22] = (byte)(selector >> 8);
            expected[23] = (byte)selector;
            CollectionAssert.AreEqual(expected, destination[..32], $"selector {selector:x8}");
        }
    }

    [TestMethod]
    public void BoolValues_AreExactlyZeroAndOne()
    {
        AssertAccepted("bool", Ev.Bool(false), 0);
        AssertAccepted("bool", Ev.Bool(true), 1);
    }

    // ------------------------------------------------------------------ strict UTF-8

    public static IEnumerable<object[]> ValidUtf8() =>
    [
        [""], ["a"], ["hello world"], ["héllo"], ["€"], ["😀"], ["\0"], ["a\0b"], ["￿"], ["\U0010FFFF"], ["日本語のテキスト"], ["\u0080"], ["߿"], ["ࠀ"], ["퟿"], [""],
    ];

    [TestMethod]
    [DynamicData(nameof(ValidUtf8))]
    public void ValidUtf8Strings_AreAccepted(string text)
    {
        byte[] utf8 = Utf8(text);
        byte[] written = WriteOk([Ev.Text(utf8)], CompileTuple("(string)"));
        CollectionAssert.AreEqual(Cat(Word(0x20), BytesValue(utf8)), written);
    }

    public static IEnumerable<object[]> InvalidUtf8() =>
    [
        ["80"], ["bf"], ["c3"], ["e282"], ["f09f98"], ["c080"], ["c1bf"], ["e08080"], ["e0809f"], ["f0808080"], ["f08fbfbf"], ["eda080"], ["edbfbf"], ["f4908080"], ["f5808080"],
        ["f8888080 80"], ["fe"], ["ff"], ["c328"], ["e28228"], ["f0288c28"], ["616263c3"], ["fffe41"], ["41ff42"],
    ];

    [TestMethod]
    [DynamicData(nameof(InvalidUtf8))]
    public void InvalidUtf8_IsRejected_BeforeAnyByteIsWritten_ByWriterAndMeasurer(string hex)
    {
        byte[] bad = Convert.FromHexString(hex.Replace(" ", ""));
        AssertRejected("string", Ev.Text(bad), ABIStatus.InvalidUtf8, hex);

        // The very same bytes are ordinary data for a `bytes` slot.
        byte[] written = WriteOk([Ev.Blob(bad)], CompileTuple("(bytes)"));
        CollectionAssert.AreEqual(Cat(Word(0x20), BytesValue(bad)), written);

        // The bytes entry point validates a string slot the same way: no write path puts non-UTF-8 bytes into a string (regression: it used to copy them).
        AssertRejected("string", Ev.Blob(bad), ABIStatus.InvalidUtf8, hex + " through TryWriteBytes");
    }

    [TestMethod]
    public void RawBytes_IntoAStringSlot_ProduceTheSameDocumentAsTheValidatingMethod_ForValidText()
    {
        byte[] text = Utf8("héllo €😀");
        CollectionAssert.AreEqual(WriteOk([Ev.Text(text)], CompileTuple("(string)")), WriteOk([Ev.Blob(text)], CompileTuple("(string)")));
    }

    [TestMethod]
    public void Utf8Method_DoesNotServeABytesSlot()
    {
        AssertRejected("bytes", Ev.Text(Ascii("abc")), ABIStatus.TypeMismatch, "TryWriteStringUtf8 on a bytes slot");
    }

    [TestMethod]
    public void InvalidUtf8_AtTheEndOrMiddleOfALongString_IsStillCaught_AndNothingIsWritten()
    {
        byte[] valid = Enumerable.Repeat((byte)'x', 4000).ToArray();
        foreach (int position in new[] { 0, 1, 2000, 3999 })
        {
            byte[] text = valid.ToArray();
            text[position] = 0xff;
            foreach (Ev write in new[] { Ev.Text(text), Ev.Blob(text) })
            {
                byte[] destination = new byte[64 + 4064];
                Array.Fill(destination, Garbage);
                RunResult result = RunWriter([write], CompileTuple("(string)"), destination, GenerousEncode);
                Assert.AreEqual(ABIStatus.InvalidUtf8, result.Status, $"position {position}, {write}");
                Assert.IsTrue(destination.All(b => b == Garbage), $"position {position}, {write}: destination modified");
            }
        }
    }

    [TestMethod]
    public void CheckOrder_CapacityAndOverlapComeBeforeUtf8_SoValidationIsBoundedByTheCallersBuffer()
    {
        byte[] bad = [0xff, 0xff, 0xff];

        // Too small a destination: the capacity error wins, and no scan of the (possibly huge) source is needed.
        byte[] destination = new byte[64];
        RunResult small = RunWriter([Ev.Text(bad)], CompileTuple("(string)"), destination, GenerousEncode);
        Assert.AreEqual(ABIStatus.InsufficientDestination, small.Status);

        // Overlap also comes first.
        byte[] backing = new byte[256];
        ABIEncodeLimits limits = GenerousEncode;
        Assert.IsTrue(ABIWriter.TryBegin(CompileTuple("(string)"), backing, new ABIWriteFrame[4], in limits, out ABIWriter w, out _));
        backing[100] = 0xff;
        Assert.IsFalse(w.TryWriteStringUtf8(backing.AsSpan(100, 1), out ABIStatus status));
        Assert.AreEqual(ABIStatus.SourceOverlapsDestination, status);
    }

    // ------------------------------------------------------------------ overlap

    /// <summary>Opens a writer over backing[0..destinationLength) of a larger array, so a source can overlap, abut, or lie outside it.</summary>
    private static ABIWriter OpenOver(byte[] backing, int destinationLength, string tuple)
    {
        ABIEncodeLimits limits = GenerousEncode;
        Assert.IsTrue(ABIWriter.TryBegin(CompileTuple(tuple), backing.AsSpan(0, destinationLength), new ABIWriteFrame[8], in limits, out ABIWriter w, out ABIStatus status), status.ToString());
        return w;
    }

    private delegate bool WriteSource(ref ABIWriter writer, ReadOnlySpan<byte> source, out ABIStatus status);

    private static readonly (string Slot, int Length, WriteSource Write)[] SourceOps =
    [
        ("address", 20, (ref ABIWriter w, ReadOnlySpan<byte> s, out ABIStatus st) => w.TryWriteAddress(s, out st)),
        ("uint256", 32, (ref ABIWriter w, ReadOnlySpan<byte> s, out ABIStatus st) => w.TryWriteUInt256Word(s, out st)),
        ("int256", 32, (ref ABIWriter w, ReadOnlySpan<byte> s, out ABIStatus st) => w.TryWriteInt256Word(s, out st)),
        ("fixed256x18", 32, (ref ABIWriter w, ReadOnlySpan<byte> s, out ABIStatus st) => w.TryWriteSignedScaledWord(s, out st)),
        ("ufixed256x18", 32, (ref ABIWriter w, ReadOnlySpan<byte> s, out ABIStatus st) => w.TryWriteUnsignedScaledWord(s, out st)),
        ("bytes7", 7, (ref ABIWriter w, ReadOnlySpan<byte> s, out ABIStatus st) => w.TryWriteFixedBytes(s, out st)),
        ("function", 20, (ref ABIWriter w, ReadOnlySpan<byte> s, out ABIStatus st) => w.TryWriteFunction(s, 5, out st)),
        ("bytes", 10, (ref ABIWriter w, ReadOnlySpan<byte> s, out ABIStatus st) => w.TryWriteBytes(s, out st)),
        ("string", 10, (ref ABIWriter w, ReadOnlySpan<byte> s, out ABIStatus st) => w.TryWriteStringUtf8(s, out st)),
    ];

    [TestMethod]
    public void SourceOverlappingTheDestination_IsRejected_ForEveryByteSource_WhereverItLies()
    {
        foreach (var (slot, length, write) in SourceOps)
        {
            // Destination = backing[0..300). Source windows: identical start, inside, straddling the end, entirely in the unwritten tail.
            int[] starts = [0, 1, 7, 150, 300 - length, 300 - length + 1, 299];
            foreach (int start in starts)
            {
                byte[] backing = new byte[600];
                Array.Fill(backing, (byte)'a'); // printable ASCII: valid UTF-8 and a valid word where a word is not range-checked
                ABIWriter w = OpenOver(backing, 300, $"({slot})");
                byte[] before = backing.ToArray();
                bool ok = write(ref w, backing.AsSpan(start, length), out ABIStatus status);
                Assert.IsFalse(ok, $"{slot} source at {start}");
                Assert.AreEqual(ABIStatus.SourceOverlapsDestination, status, $"{slot} source at {start}");
                CollectionAssert.AreEqual(before, backing, $"{slot} source at {start}: destination modified");
            }
        }
    }

    [TestMethod]
    public void SourceOutsideTheDestination_EvenAdjacentInTheSameArray_IsAccepted()
    {
        foreach (var (slot, length, write) in SourceOps)
        {
            byte[] backing = new byte[600];
            for (int i = 0; i < backing.Length; i++)
            {
                backing[i] = (byte)'a';
            }

            // Words for integer slots must be in range: zero them.
            if (slot is "uint256" or "int256" or "fixed256x18" or "ufixed256x18")
            {
                Array.Clear(backing, 300, 32);
            }

            ABIWriter w = OpenOver(backing, 300, $"({slot})"); // destination backing[0..300), source backing[300..]
            Assert.IsTrue(write(ref w, backing.AsSpan(300, length), out ABIStatus status), $"{slot}: {status}");
            Assert.IsTrue(w.TryFinish(out int written, out status), $"{slot}: {status}");
            Assert.IsTrue(written >= 32, slot);
        }
    }

    [TestMethod]
    public void SourceOverlappingTheCallBuffer_IncludingTheSelectorArea_IsRejectedByTheCallEncoder()
    {
        ABIFunctionSchema function = Function("transfer(address,uint256)");
        byte[] backing = new byte[400];
        ABIEncodeLimits limits = GenerousEncode;
        Assert.IsTrue(ABICallEncoder.TryBegin(function, backing.AsSpan(0, 100), new ABIWriteFrame[4], in limits, out ABICallEncoder c, out ABIStatus status));
        byte[] before = backing.ToArray();

        // Bytes 0..3 hold the selector, which is outside the argument writer's own span but inside the call buffer.
        Assert.IsFalse(c.TryWriteAddress(backing.AsSpan(0, 20), out status));
        Assert.AreEqual(ABIStatus.SourceOverlapsDestination, status);
        CollectionAssert.AreEqual(before, backing);

        Assert.IsTrue(ABICallEncoder.TryBegin(function, backing.AsSpan(0, 100), new ABIWriteFrame[4], in limits, out c, out status));
        Assert.IsFalse(c.TryWriteAddress(backing.AsSpan(2, 20), out status), "a source that touches only the selector bytes still overlaps");
        Assert.AreEqual(ABIStatus.SourceOverlapsDestination, status);
    }

    [TestMethod]
    public void SourceInsideOnlyTheSelectorBytes_IsRejectedByTheCallEncoder()
    {
        // A 3-byte bytes source lies entirely in call bytes 0..2: outside the argument writer's own span (bytes 4..), inside the call buffer.
        ABIFunctionSchema function = Function("f(bytes)");
        byte[] backing = new byte[300];
        ABIEncodeLimits limits = GenerousEncode;
        Assert.IsTrue(ABICallEncoder.TryBegin(function, backing.AsSpan(0, 200), new ABIWriteFrame[4], in limits, out ABICallEncoder c, out ABIStatus status));
        byte[] before = backing.ToArray();
        Assert.IsFalse(c.TryWriteBytes(backing.AsSpan(1, 3), out status));
        Assert.AreEqual(ABIStatus.SourceOverlapsDestination, status);
        CollectionAssert.AreEqual(before, backing);

        // The same bytes just past the call buffer are fine.
        Assert.IsTrue(ABICallEncoder.TryBegin(function, backing.AsSpan(0, 200), new ABIWriteFrame[4], in limits, out c, out status));
        Assert.IsTrue(c.TryWriteBytes(backing.AsSpan(250, 3), out status), status.ToString());
        Assert.IsTrue(c.TryFinish(out int written, out _));
        Assert.AreEqual(4 + 32 + 32 + 32, written);
    }

    [TestMethod]
    public void EmptySourcesNeverOverlap_AndAZeroLengthBytesValueIsAllowedAnywhere()
    {
        byte[] backing = new byte[200];
        ABIWriter w = OpenOver(backing, 100, "(bytes)");
        Assert.IsTrue(w.TryWriteBytes(backing.AsSpan(50, 0), out ABIStatus status), status.ToString());
        Assert.IsTrue(w.TryFinish(out int written, out _));
        Assert.AreEqual(64, written);
    }

    // ------------------------------------------------------------------ size arithmetic

    [TestMethod]
    public void HugeLengths_AreRejectedByArithmeticBeforeAnyByteIsRead_NoOverflowNoException()
    {
        // A span that claims int.MaxValue bytes over a single real byte: any read of the data would be a bug (and could fault).
        byte[] one = [0x41];
        ReadOnlySpan<byte> lie = MemoryMarshal.CreateReadOnlySpan(ref one[0], int.MaxValue);
        const int largestLength = int.MaxValue - 31 - 64; // 2^31 - 96 = 2147483552, a multiple of 32: total document = 32 + 32 + length = 2^31 - 32
        ReadOnlySpan<byte> almost = MemoryMarshal.CreateReadOnlySpan(ref one[0], largestLength);

        foreach (bool asString in new[] { false, true })
        {
            ABISchema schema = CompileTuple(asString ? "(string)" : "(bytes)");

            ABIEncodeLimits defaults = ABIEncodeLimits.Default;
            Assert.IsTrue(ABIWriter.TryBegin(schema, new byte[1024], new ABIWriteFrame[4], in defaults, out ABIWriter w, out _));
            Assert.IsFalse(asString ? w.TryWriteStringUtf8(lie, out ABIStatus s) : w.TryWriteBytes(lie, out s));
            Assert.AreEqual(ABIStatus.InputTooLarge, s, "the configured byte limit is checked first");

            ABIEncodeLimits unlimited = GenerousEncode with { MaxEncodedBytes = int.MaxValue };
            Assert.IsTrue(ABIWriter.TryBegin(schema, new byte[1024], new ABIWriteFrame[4], in unlimited, out w, out _));
            Assert.IsFalse(asString ? w.TryWriteStringUtf8(lie, out s) : w.TryWriteBytes(lie, out s));
            Assert.AreEqual(ABIStatus.InputTooLarge, s, "32 + 32 + (int.MaxValue padded) does not fit an int, so it exceeds even an int.MaxValue limit");

            Assert.IsTrue(ABIWriter.TryBegin(schema, new byte[1024], new ABIWriteFrame[4], in unlimited, out w, out _));
            Assert.IsFalse(asString ? w.TryWriteStringUtf8(almost, out s) : w.TryWriteBytes(almost, out s));
            Assert.AreEqual(ABIStatus.InsufficientDestination, s, "fits the limit, not the 1 KiB destination");
        }

        // The measurer has no destination: the largest document under an int.MaxValue limit is 2^31 - 32 bytes, and it never reads the data.
        ABIEncodeLimits limits = GenerousEncode with { MaxEncodedBytes = int.MaxValue };
        ABISchema bytesSchema = CompileTuple("(bytes)");
        ReadOnlySpan<byte> largest = almost;
        Assert.IsTrue(ABIMeasurer.TryBegin(bytesSchema, new ABIMeasureFrame[4], in limits, out ABIMeasurer m, out _));
        Assert.IsTrue(m.TryWriteBytes(largest, out ABIStatus ms), ms.ToString());
        Assert.IsTrue(m.TryFinish(out int length, out ms));
        Assert.AreEqual(int.MaxValue - 31, length);

        ReadOnlySpan<byte> oneMore = MemoryMarshal.CreateReadOnlySpan(ref one[0], largestLength + 1);
        Assert.IsTrue(ABIMeasurer.TryBegin(bytesSchema, new ABIMeasureFrame[4], in limits, out m, out _));
        Assert.IsFalse(m.TryWriteBytes(oneMore, out ms));
        Assert.AreEqual(ABIStatus.InputTooLarge, ms, "one more byte pads to a document of 2^31 bytes");
    }

    [TestMethod]
    public void DynamicArrayCountTimesHeadWidth_NeverOverflows_AtTheLimitBoundaries()
    {
        // 4096 elements of a 20-word static tuple: 4096 * 640 = 2.6 MB head, larger than the default 256 KiB limit: a limit failure, not an overflow.
        ABISchema wide = CompileTuple("((uint256,uint256,uint256,uint256,uint256,uint256,uint256,uint256,uint256,uint256,uint256,uint256,uint256,uint256,uint256,uint256,uint256,uint256,uint256,uint256)[])");
        ABIEncodeLimits limits = ABIEncodeLimits.Default;
        Assert.IsTrue(ABIWriter.TryBegin(wide, new byte[1 << 22], new ABIWriteFrame[8], in limits, out ABIWriter w, out _));
        Assert.IsFalse(w.TryBeginDynamicArray(4096, out ABIStatus status));
        Assert.AreEqual(ABIStatus.InputTooLarge, status);

        // The same count with a huge byte limit and a huge max element count still cannot overflow: it is a destination failure.
        ABIEncodeLimits huge = new(int.MaxValue, 16, int.MaxValue, int.MaxValue, int.MaxValue);
        Assert.IsTrue(ABIWriter.TryBegin(wide, new byte[1 << 12], new ABIWriteFrame[8], in huge, out w, out _));
        Assert.IsFalse(w.TryBeginDynamicArray(int.MaxValue, out status));
        Assert.AreEqual(ABIStatus.InputTooLarge, status, "int.MaxValue * 640 does not fit an int, so it exceeds the limit");

        Assert.IsTrue(ABIWriter.TryBegin(wide, new byte[1 << 12], new ABIWriteFrame[8], in huge, out w, out _));
        Assert.IsFalse(w.TryBeginDynamicArray(10_000, out status));
        Assert.AreEqual(ABIStatus.InsufficientDestination, status);

        Assert.IsTrue(ABIWriter.TryBegin(wide, new byte[1 << 12], new ABIWriteFrame[8], in huge, out w, out _));
        Assert.IsFalse(w.TryBeginDynamicArray(-1, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
    }
}
