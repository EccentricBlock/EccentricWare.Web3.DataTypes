using System.Numerics;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>The strict scalar and pointer-word rules of spec 2.4, checked against BigInteger arithmetic and hand-written boundary words.</summary>
[TestClass]
public sealed class ABIWordTests
{
    private static ABIWord Load(byte[] word)
    {
        Assert.IsTrue(ABIWord.TryLoad(word, 0, out var loaded));
        return loaded;
    }

    private static byte[] WordWithByte(int index, byte value)
    {
        var word = new byte[32];
        word[index] = value;
        return word;
    }

    private static int[] AllWidths => [.. Enumerable.Range(1, 32).Select(b => b * 8)];

    // ------------------------------------------------------------------ loading

    [TestMethod]
    public void TryLoad_ReadsFourBigEndianLimbs()
    {
        byte[] bytes = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var word = Load(bytes);
        Assert.AreEqual(0x0001020304050607UL, word.W0);
        Assert.AreEqual(0x08090a0b0c0d0e0fUL, word.W1);
        Assert.AreEqual(0x1011121314151617UL, word.W2);
        Assert.AreEqual(0x18191a1b1c1d1e1fUL, word.W3);
    }

    [TestMethod]
    public void TryLoad_RejectsPartialNegativeAndOutOfRangeOffsets_WithoutThrowing()
    {
        byte[] bytes = new byte[40];
        Assert.IsTrue(ABIWord.TryLoad(bytes, 0, out _));
        Assert.IsTrue(ABIWord.TryLoad(bytes, 8, out _));
        Assert.IsFalse(ABIWord.TryLoad(bytes, 9, out var word));
        Assert.AreEqual(default, word);
        Assert.IsFalse(ABIWord.TryLoad(bytes, 40, out _));
        Assert.IsFalse(ABIWord.TryLoad(bytes, 41, out _));
        Assert.IsFalse(ABIWord.TryLoad(bytes, -1, out _));
        Assert.IsFalse(ABIWord.TryLoad(bytes, int.MinValue, out _));
        Assert.IsFalse(ABIWord.TryLoad(bytes, int.MaxValue, out _));
        Assert.IsFalse(ABIWord.TryLoad(ReadOnlySpan<byte>.Empty, 0, out _));
    }

    // ------------------------------------------------------------------ pointer, length and count words (256-bit check BEFORE narrowing)

    [TestMethod]
    public void TryGetInt32_AcceptsZeroAndIntMaxValue()
    {
        Assert.IsTrue(Load(Word(0)).TryGetInt32(out int zero));
        Assert.AreEqual(0, zero);
        Assert.IsTrue(Load(Word(int.MaxValue)).TryGetInt32(out int max));
        Assert.AreEqual(int.MaxValue, max);
    }

    [TestMethod]
    public void TryGetInt32_RejectsEveryWordAboveIntMaxValue()
    {
        byte[][] tooBig =
        [
            Word((ulong)int.MaxValue + 1),             // 2^31
            Word(uint.MaxValue),                        // 2^32 - 1 (a truncating cast would yield -1)
            Word((ulong)uint.MaxValue + 1),             // 2^32 (a truncating cast would yield 0)
            Word(ulong.MaxValue),                       // 2^64 - 1
            WordHex("10000000000000000"),               // 2^64 (low limb zero, limb 2 set)
            WordHex("100000000000000000000000000000000"), // 2^128
            WordHex("1" + new string('0', 48)),         // 2^192
            Cat([0x80], new byte[31]),                  // 2^255
            Filled(0xff),                               // 2^256 - 1
            WordWithByte(0, 0x01),                      // 2^248
            WordWithByte(8, 0x01),                      // top byte of limb 1 set
        ];

        foreach (byte[] word in tooBig)
        {
            Assert.IsFalse(Load(word).TryGetInt32(out int value), Convert.ToHexString(word));
            Assert.AreEqual(0, value);
        }
    }

    [TestMethod]
    public void TryGetInt32_AgreesWithBigIntegerForRandomWords()
    {
        var rng = new DeterministicRng(0xA11CE);
        for (int i = 0; i < 5_000; i++)
        {
            byte[] word = rng.Bytes(32);
            // Bias the high bytes towards zero so that accepted values occur.
            int zeroBytes = rng.Next(33);
            Array.Clear(word, 0, zeroBytes);
            bool expected = ABIReference.Unsigned(word) <= int.MaxValue;
            Assert.AreEqual(expected, Load(word).TryGetInt32(out int value), Convert.ToHexString(word));
            if (expected)
            {
                Assert.AreEqual((int)ABIReference.Unsigned(word), value);
            }
        }
    }

    // ------------------------------------------------------------------ uint / ufixed

    [TestMethod]
    public void UInt_AcceptsExactlyTheWidthRange_ForEveryWidth()
    {
        foreach (int width in AllWidths)
        {
            BigInteger max = (BigInteger.One << width) - 1;
            Assert.AreEqual(ABIStatus.Success, Load(ABIReference.ToWord(max)).CheckScalar(ABITypeKind.UInt, width), $"uint{width} max");
            Assert.AreEqual(ABIStatus.Success, Load(ABIReference.ToWord(0)).CheckScalar(ABITypeKind.UInt, width), $"uint{width} 0");
            if (width < 256)
            {
                Assert.AreEqual(ABIStatus.InvalidScalar, Load(ABIReference.ToWord(max + 1)).CheckScalar(ABITypeKind.UInt, width), $"uint{width} max+1");
                Assert.AreEqual(ABIStatus.InvalidScalar, Load(ABIReference.ToWord(max + 1)).CheckScalar(ABITypeKind.UFixed, width), $"ufixed{width} max+1");
            }
        }
    }

    [TestMethod]
    public void UInt_RejectsEveryIndividualHighBit_ForEveryNarrowWidth()
    {
        foreach (int width in AllWidths.Where(w => w < 256))
        {
            for (int bit = width; bit < 256; bit++)
            {
                byte[] word = ABIReference.ToWord(BigInteger.One << bit);
                Assert.AreEqual(ABIStatus.InvalidScalar, Load(word).CheckScalar(ABITypeKind.UInt, width), $"uint{width} bit {bit}");
                Assert.AreEqual(ABIStatus.InvalidScalar, Load(word).CheckScalar(ABITypeKind.UFixed, width), $"ufixed{width} bit {bit}");
            }
        }
    }

    [TestMethod]
    public void UInt8_Of256_IsInvalidScalar()
    {
        Assert.AreEqual(ABIStatus.InvalidScalar, Load(Word(256)).CheckScalar(ABITypeKind.UInt, 8));
        Assert.AreEqual(ABIStatus.Success, Load(Word(255)).CheckScalar(ABITypeKind.UInt, 8));
    }

    // ------------------------------------------------------------------ int / fixed

    [TestMethod]
    public void Int_AcceptsExactlyTheSignedRange_ForEveryWidth()
    {
        foreach (int width in AllWidths)
        {
            BigInteger half = BigInteger.One << (width - 1);
            foreach (BigInteger value in new[] { BigInteger.Zero, BigInteger.One, BigInteger.MinusOne, half - 1, -half })
            {
                Assert.AreEqual(ABIStatus.Success, Load(ABIReference.ToWord(value)).CheckScalar(ABITypeKind.Int, width), $"int{width} {value}");
                Assert.AreEqual(ABIStatus.Success, Load(ABIReference.ToWord(value)).CheckScalar(ABITypeKind.Fixed, width), $"fixed{width} {value}");
            }

            if (width < 256)
            {
                foreach (BigInteger value in new[] { half, -half - 1, half << 1, -(half << 1) })
                {
                    Assert.AreEqual(ABIStatus.InvalidScalar, Load(ABIReference.ToWord(value)).CheckScalar(ABITypeKind.Int, width), $"int{width} {value}");
                    Assert.AreEqual(ABIStatus.InvalidScalar, Load(ABIReference.ToWord(value)).CheckScalar(ABITypeKind.Fixed, width), $"fixed{width} {value}");
                }
            }
        }
    }

    [TestMethod]
    public void Int256_EveryWordIsValid()
    {
        var rng = new DeterministicRng(256);
        for (int i = 0; i < 2_000; i++)
        {
            Assert.AreEqual(ABIStatus.Success, Load(rng.Bytes(32)).CheckScalar(ABITypeKind.Int, 256));
        }

        Assert.AreEqual(ABIStatus.Success, Load(new byte[32]).CheckScalar(ABITypeKind.Int, 256));
        Assert.AreEqual(ABIStatus.Success, Load(Filled(0xff)).CheckScalar(ABITypeKind.Int, 256));
        Assert.AreEqual(ABIStatus.Success, Load(Cat([0x80], new byte[31])).CheckScalar(ABITypeKind.Int, 256));
    }

    [TestMethod]
    public void Int_FlippingAnyHighBitOfAValidWord_IsInvalidScalar()
    {
        foreach (int width in AllWidths.Where(w => w < 256))
        {
            foreach (BigInteger baseValue in new[] { BigInteger.One, BigInteger.MinusOne })
            {
                byte[] valid = ABIReference.ToWord(baseValue);
                Assert.AreEqual(ABIStatus.Success, Load(valid).CheckScalar(ABITypeKind.Int, width));
                for (int bit = width - 1; bit < 256; bit++)
                {
                    // Bits width-1 .. 255 must all equal the sign; flipping any one of them (including the sign bit itself) breaks it.
                    byte[] flipped = (byte[])valid.Clone();
                    flipped[31 - bit / 8] ^= (byte)(1 << (bit % 8));
                    Assert.AreEqual(ABIStatus.InvalidScalar, Load(flipped).CheckScalar(ABITypeKind.Int, width), $"int{width} value {baseValue} flip bit {bit}");
                }
            }
        }
    }

    [TestMethod]
    public void Int8_PositiveWithHighBitsAndNegativeWithPartialExtension_AreInvalid()
    {
        Assert.AreEqual(ABIStatus.InvalidScalar, Load(Word(0x80)).CheckScalar(ABITypeKind.Int, 8)); // +128 does not fit int8
        Assert.AreEqual(ABIStatus.InvalidScalar, Load(Word(0x100)).CheckScalar(ABITypeKind.Int, 8));
        Assert.AreEqual(ABIStatus.Success, Load(Word(0x7f)).CheckScalar(ABITypeKind.Int, 8));
        Assert.AreEqual(ABIStatus.Success, Load(Cat(Enumerable.Repeat((byte)0xff, 31).ToArray(), [0x80])).CheckScalar(ABITypeKind.Int, 8)); // -128
        Assert.AreEqual(ABIStatus.InvalidScalar, Load(Cat([0x7f], Enumerable.Repeat((byte)0xff, 30).ToArray(), [0x80])).CheckScalar(ABITypeKind.Int, 8)); // one zero bit in the extension
        Assert.AreEqual(ABIStatus.InvalidScalar, Load(Cat(Enumerable.Repeat((byte)0xff, 30).ToArray(), [0x00, 0x80])).CheckScalar(ABITypeKind.Int, 8));
    }

    [TestMethod]
    public void IntAndUInt_AgreeWithBigIntegerForRandomWords()
    {
        var rng = new DeterministicRng(0xC0FFEE);
        for (int i = 0; i < 20_000; i++)
        {
            byte[] word = rng.Bytes(32);
            int mode = rng.Next(3);
            if (mode == 0)
            {
                Array.Clear(word, 0, rng.Next(33)); // many near-valid unsigned words
            }
            else if (mode == 1)
            {
                Array.Fill(word, (byte)0xff, 0, rng.Next(33)); // many near-valid negative words
            }

            int width = AllWidths[rng.Next(AllWidths.Length)];
            bool unsignedOk = ABIReference.Unsigned(word) < BigInteger.One << width;
            BigInteger signed = ABIReference.Signed(word);
            BigInteger half = BigInteger.One << (width - 1);
            bool signedOk = signed >= -half && signed < half;
            string label = $"{Convert.ToHexString(word)} width {width}";
            Assert.AreEqual(unsignedOk ? ABIStatus.Success : ABIStatus.InvalidScalar, Load(word).CheckScalar(ABITypeKind.UInt, width), "uint " + label);
            Assert.AreEqual(signedOk ? ABIStatus.Success : ABIStatus.InvalidScalar, Load(word).CheckScalar(ABITypeKind.Int, width), "int " + label);
        }
    }

    // ------------------------------------------------------------------ address, bool

    [TestMethod]
    public void Address_RequiresTwelveLeadingZeroBytes()
    {
        Assert.AreEqual(ABIStatus.Success, Load(Cat(new byte[12], Filled(0xff)[..20])).CheckScalar(ABITypeKind.Address, 0));
        Assert.AreEqual(ABIStatus.Success, Load(new byte[32]).CheckScalar(ABITypeKind.Address, 0));
        for (int index = 0; index < 12; index++)
        {
            Assert.AreEqual(ABIStatus.InvalidScalar, Load(WordWithByte(index, 0x01)).CheckScalar(ABITypeKind.Address, 0), $"byte {index}");
            Assert.AreEqual(ABIStatus.InvalidScalar, Load(WordWithByte(index, 0x80)).CheckScalar(ABITypeKind.Address, 0), $"byte {index} high bit");
        }

        for (int index = 12; index < 32; index++)
        {
            Assert.AreEqual(ABIStatus.Success, Load(WordWithByte(index, 0xff)).CheckScalar(ABITypeKind.Address, 0), $"byte {index}");
        }
    }

    [TestMethod]
    public void Bool_AcceptsOnlyZeroAndOne()
    {
        Assert.AreEqual(ABIStatus.Success, Load(Word(0)).CheckScalar(ABITypeKind.Bool, 0));
        Assert.AreEqual(ABIStatus.Success, Load(Word(1)).CheckScalar(ABITypeKind.Bool, 0));
        Assert.AreEqual(ABIStatus.InvalidScalar, Load(Word(2)).CheckScalar(ABITypeKind.Bool, 0));
        Assert.AreEqual(ABIStatus.InvalidScalar, Load(Word(0x100)).CheckScalar(ABITypeKind.Bool, 0));
        Assert.AreEqual(ABIStatus.InvalidScalar, Load(Word(ulong.MaxValue)).CheckScalar(ABITypeKind.Bool, 0));
        for (int index = 0; index < 31; index++)
        {
            Assert.AreEqual(ABIStatus.InvalidScalar, Load(WordWithByte(index, 1)).CheckScalar(ABITypeKind.Bool, 0), $"byte {index}");
        }
    }

    // ------------------------------------------------------------------ bytes<M>, function

    [TestMethod]
    public void FixedBytes_RequiresZeroRightPadding_ForEveryLength()
    {
        for (int m = 1; m <= 32; m++)
        {
            // Payload bytes may be anything; every padding byte must be zero.
            Assert.AreEqual(ABIStatus.Success, Load(Cat(Filled(0xff)[..m], new byte[32 - m])).CheckScalar(ABITypeKind.FixedBytes, m), $"bytes{m} valid");
            for (int index = m; index < 32; index++)
            {
                Assert.AreEqual(ABIStatus.InvalidPadding, Load(WordWithPayload(m, index)).CheckScalar(ABITypeKind.FixedBytes, m), $"bytes{m} padding byte {index}");
            }
        }
    }

    private static byte[] WordWithPayload(int payloadLength, int dirtyIndex)
    {
        byte[] word = Cat(Filled(0x11)[..payloadLength], new byte[32 - payloadLength]);
        word[dirtyIndex] = 0x01;
        return word;
    }

    [TestMethod]
    public void Function_RequiresTheLastEightBytesZero()
    {
        Assert.AreEqual(ABIStatus.Success, Load(Cat(Filled(0xee)[..24], new byte[8])).CheckScalar(ABITypeKind.Function, 0));
        for (int index = 24; index < 32; index++)
        {
            Assert.AreEqual(ABIStatus.InvalidPadding, Load(WordWithByte(index, 0x01)).CheckScalar(ABITypeKind.Function, 0), $"byte {index}");
        }
    }

    [TestMethod]
    public void NonScalarKinds_AreNeverAcceptedAsScalars()
    {
        foreach (ABITypeKind kind in new[] { ABITypeKind.Bytes, ABITypeKind.String, ABITypeKind.Tuple, ABITypeKind.FixedArray, ABITypeKind.DynamicArray })
        {
            Assert.AreEqual(ABIStatus.InvalidScalar, Load(new byte[32]).CheckScalar(kind, 0), kind.ToString());
        }
    }

    // ------------------------------------------------------------------ bit helpers

    [TestMethod]
    public void HighAndLowBitsAreZero_MatchBigIntegerForEveryCount()
    {
        var rng = new DeterministicRng(7);
        for (int i = 0; i < 400; i++)
        {
            byte[] bytes = rng.Bytes(32);
            Array.Clear(bytes, 0, rng.Next(33));
            BigInteger value = ABIReference.Unsigned(bytes);
            var word = Load(bytes);
            for (int bits = 0; bits <= 256; bits++)
            {
                bool highZero = (value >> (256 - bits)).IsZero;
                bool lowZero = (value & ((BigInteger.One << bits) - 1)).IsZero;
                Assert.AreEqual(highZero, word.HighBitsAreZero(bits), $"high {bits} of {Convert.ToHexString(bytes)}");
                Assert.AreEqual(lowZero, word.LowBitsAreZero(bits), $"low {bits} of {Convert.ToHexString(bytes)}");
            }
        }
    }

    [TestMethod]
    public void FirstNonZero_FindsTheFirstDirtyByteOrMinusOne()
    {
        Assert.AreEqual(-1, ABIWord.FirstNonZero(ReadOnlySpan<byte>.Empty));
        Assert.AreEqual(-1, ABIWord.FirstNonZero(new byte[31]));
        for (int i = 0; i < 31; i++)
        {
            byte[] padding = new byte[31];
            padding[i] = 0x80;
            padding[^1] |= 0x01;
            Assert.AreEqual(i, ABIWord.FirstNonZero(padding));
        }
    }
}
