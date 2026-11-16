using System.Numerics;
using System.Runtime.InteropServices;
using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Contracts hardened after the coverage review: size arithmetic that no longer overflows, argument validation on public
/// helpers, and "out" values that are zero (not half-parsed) when a parse fails.
/// </summary>
[TestClass]
public sealed class ByteUtils_ArgumentAndFailureContractTests
{
    // ----------------- GetHexDecodedByteCount -----------------

    [TestMethod]
    public void GetHexDecodedByteCount_DoesNotOverflow_ForTheLargestDigitCounts()
    {
        // (int.MaxValue + 1) used to wrap to a large negative count.
        Assert.AreEqual(1_073_741_824, ByteUtils.GetHexDecodedByteCount(int.MaxValue, allowOddLength: true));
        Assert.AreEqual(1_073_741_823, ByteUtils.GetHexDecodedByteCount(int.MaxValue - 1, allowOddLength: true));
        Assert.AreEqual(1_073_741_823, ByteUtils.GetHexDecodedByteCount(int.MaxValue - 1, allowOddLength: false));
        Assert.AreEqual(-1, ByteUtils.GetHexDecodedByteCount(int.MaxValue, allowOddLength: false)); // odd, strict
    }

    [TestMethod]
    [DataRow(0, true, 0)]
    [DataRow(1, true, 1)]
    [DataRow(2, true, 1)]
    [DataRow(3, true, 2)]
    [DataRow(3, false, -1)]
    [DataRow(4, false, 2)]
    [DataRow(-1, true, -1)]
    [DataRow(int.MinValue, false, -1)]
    public void GetHexDecodedByteCount_SmallAndInvalidCounts(int digits, bool allowOdd, int expected)
        => Assert.AreEqual(expected, ByteUtils.GetHexDecodedByteCount(digits, allowOdd));

    // ----------------- GetMaxHexUtf8LengthFromMagnitudeByteCount -----------------

    [TestMethod]
    public void GetMaxHexUtf8Length_IsExactAtTheLargestSupportedByteCount()
    {
        const int Largest = (int.MaxValue - 3) / 2; // 1,073,741,822 bytes: 2 digits per byte + sign + prefix == int.MaxValue
        Assert.AreEqual(int.MaxValue, ByteUtils.GetMaxHexUtf8LengthFromMagnitudeByteCount(Largest, includeSign: true, include0xPrefix: true));
        Assert.AreEqual(int.MaxValue - 3, ByteUtils.GetMaxHexUtf8LengthFromMagnitudeByteCount(Largest, includeSign: false, include0xPrefix: false));
    }

    [TestMethod]
    public void GetMaxHexUtf8Length_RejectsCountsThatWouldOverflowOrAreNegative()
    {
        // 0x40000000 << 1 used to wrap to int.MinValue and return 4.
        foreach (int tooLarge in new[] { ((int.MaxValue - 3) / 2) + 1, 0x40000000, int.MaxValue })
        {
            ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => ByteUtils.GetMaxHexUtf8LengthFromMagnitudeByteCount(tooLarge, true, true));
            Assert.AreEqual("magnitudeByteCount", error.ParamName);
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ByteUtils.GetMaxHexUtf8LengthFromMagnitudeByteCount(-1, false, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ByteUtils.GetMaxHexUtf8LengthFromMagnitudeByteCount(int.MinValue, false, false));
    }

    [TestMethod]
    [DataRow(0, false, false, 1)] // zero still needs one digit
    [DataRow(0, true, true, 4)]
    [DataRow(1, false, false, 2)]
    [DataRow(32, true, true, 67)]
    [DataRow(32, false, true, 66)]
    public void GetMaxHexUtf8Length_SmallCounts(int bytes, bool sign, bool prefix, int expected)
        => Assert.AreEqual(expected, ByteUtils.GetMaxHexUtf8LengthFromMagnitudeByteCount(bytes, sign, prefix));

    // ----------------- ReadVector256 -----------------

    [TestMethod]
    public void ReadVector256_ReadsTheFirst32Bytes_AndRejectsShorterSpans()
    {
        byte[] data = Enumerable.Range(0, 40).Select(i => (byte)(i + 1)).ToArray();

        var fromExact = ByteUtils.ReadVector256(data.AsSpan(0, 32));
        var fromLonger = ByteUtils.ReadVector256(data);
        for (int i = 0; i < 32; i++)
        {
            Assert.AreEqual(data[i], fromExact[i], $"exact {i}");
            Assert.AreEqual(data[i], fromLonger[i], $"longer {i}");
        }

        // A shorter span used to be read past its end (an unchecked unaligned load).
        foreach (int length in new[] { 0, 1, 16, 31 })
        {
            ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => ByteUtils.ReadVector256(new byte[length]));
            Assert.AreEqual("bytes32", error.ParamName);
        }
    }

    // ----------------- TryParseBigIntegerHexUnsignedUtf8: negative bound -----------------

    [TestMethod]
    public void TryParseBigIntegerHexUnsignedUtf8_NegativeBound_Throws_ZeroBoundAcceptsOnlyEmpty()
    {
        foreach (int negative in new[] { -1, int.MinValue })
        {
            ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => ByteUtils.TryParseBigIntegerHexUnsignedUtf8("ff"u8, negative, out _));
            Assert.AreEqual("maxHexDigits", error.ParamName);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ByteUtils.TryParseBigIntegerHexUnsignedUtf8(default, negative, out _));
        }

        Assert.IsFalse(ByteUtils.TryParseBigIntegerHexUnsignedUtf8("f"u8, 0, out _));
        Assert.IsTrue(ByteUtils.TryParseBigIntegerHexUnsignedUtf8(default, 0, out BigInteger zero));
        Assert.AreEqual(BigInteger.Zero, zero);
        Assert.IsTrue(ByteUtils.TryParseBigIntegerHexUnsignedUtf8("ff"u8, 2, out BigInteger value));
        Assert.AreEqual(new BigInteger(255), value);
    }

    // ----------------- sizes of 2^30 bytes or more -----------------

    // A span whose length claims 2^30 bytes or more, built over a one-element array. Every call below returns false at its
    // size check, before any byte past the first is read, so no gigabyte of memory is needed.
    private static ReadOnlySpan<byte> ClaimedLength(byte[] backing, int length)
        => MemoryMarshal.CreateReadOnlySpan(ref MemoryMarshal.GetArrayDataReference(backing), length);

    [TestMethod]
    public void TryWriteHexMinimalUtf8_AMagnitudeOf2Pow30BytesOrMore_ReturnsFalse_InsteadOfWrappingTheSize()
    {
        // (trimmed.Length << 1) wrapped negative, so the size check passed and the method threw IndexOutOfRangeException.
        byte[] backing = [0x01]; // non-zero first byte: the leading-zero scan stops at once
        foreach (int length in new[] { (1 << 30) - 1, 1 << 30, (1 << 30) + 5, int.MaxValue })
        {
            byte[] destination = new byte[16];
            Assert.IsFalse(ByteUtils.TryWriteHexMinimalUtf8(ClaimedLength(backing, length), true, false, destination, out int written), $"length {length}");
            Assert.AreEqual(0, written, $"length {length}");
            Assert.IsTrue(destination.All(b => b == 0), $"destination untouched, length {length}");
        }
    }

    [TestMethod]
    public void TryEncodeHex_ASourceOf2Pow30BytesOrMore_ReturnsFalse_BeforeTouchingTheDestination()
    {
        byte[] backing = [0xAB];
        foreach (int length in new[] { 1 << 30, (1 << 30) + 5, int.MaxValue })
        {
            char[] chars = new char[16];
            byte[] bytes = new byte[16];
            Assert.IsFalse(ByteUtils.TryEncodeHexChars(ClaimedLength(backing, length), chars, uppercase: false), $"chars {length}");
            Assert.IsFalse(ByteUtils.TryEncodeHexUtf8(ClaimedLength(backing, length), bytes, uppercase: true), $"utf8 {length}");
            Assert.IsTrue(chars.All(c => c == '\0') && bytes.All(b => b == 0), $"destination untouched, length {length}");
        }
    }

    [TestMethod]
    public void TryEncodeHex_AndTryWriteHexMinimal_StillWorkForOrdinarySizes()
    {
        char[] chars = new char[4];
        Assert.IsTrue(ByteUtils.TryEncodeHexChars([0xAB, 0xCD], chars, uppercase: false));
        Assert.AreEqual("abcd", new string(chars));

        byte[] utf8 = new byte[4];
        Assert.IsTrue(ByteUtils.TryEncodeHexUtf8([0xAB, 0xCD], utf8, uppercase: true));
        Assert.AreEqual("ABCD", System.Text.Encoding.ASCII.GetString(utf8));

        byte[] minimal = new byte[8];
        Assert.IsTrue(ByteUtils.TryWriteHexMinimalUtf8([0x00, 0x0f, 0xAB], true, false, minimal, out int written));
        Assert.AreEqual("0xfab", System.Text.Encoding.ASCII.GetString(minimal, 0, written));
        Assert.IsFalse(ByteUtils.TryWriteHexMinimalUtf8([0x00, 0x0f, 0xAB], true, false, new byte[4], out written));
        Assert.AreEqual(0, written);
    }

    // ----------------- failed parses leave zero, never a partial value -----------------

    [TestMethod]
    public void VariableWidthHexParsers_ZeroTheOutput_WhenADigitIsInvalid()
    {
        // "12x" used to leave 0x12 in the output although the parse failed.
        foreach (string text in new[] { "12x", "x12", "1x2", "fffffffx" })
        {
            Assert.IsFalse(ByteUtils.TryParseHexUInt32Utf8(System.Text.Encoding.ASCII.GetBytes(text), out uint u32), text);
            Assert.AreEqual(0u, u32, text);
            Assert.IsFalse(ByteUtils.TryParseHexUInt64Utf8Variable(System.Text.Encoding.ASCII.GetBytes(text), out ulong u64Utf8), text);
            Assert.AreEqual(0UL, u64Utf8, text);
            Assert.IsFalse(ByteUtils.TryParseHexUInt64CharsVariable(text.AsSpan(), out ulong u64Chars), text);
            Assert.AreEqual(0UL, u64Chars, text);
        }

        // Length failures were already zero; keep it so.
        Assert.IsFalse(ByteUtils.TryParseHexUInt32Utf8("123456789"u8, out uint tooLong32));
        Assert.AreEqual(0u, tooLong32);
        Assert.IsFalse(ByteUtils.TryParseHexUInt64Utf8Variable(default, out ulong empty));
        Assert.AreEqual(0UL, empty);
    }

    [TestMethod]
    public void UInt256HexParsers_ZeroAllFourLimbs_WhateverPositionTheInvalidDigitIsAt()
    {
        // A valid prefix of the digits is parsed into the low limbs before the bad digit is reached; none of it may leak out.
        foreach (int length in new[] { 5, 16, 17, 33, 48, 49, 63, 64 })
        {
            for (int position = 0; position < length; position++)
            {
                char[] chars = Enumerable.Repeat('f', length).ToArray();
                chars[position] = 'g';
                string text = new(chars);

                Assert.IsFalse(ByteUtils.TryParseUInt256HexChars(text, out ulong c0, out ulong c1, out ulong c2, out ulong c3), $"chars {text}");
                Assert.AreEqual(0UL, c0 | c1 | c2 | c3, $"chars {text}");

                Assert.IsFalse(ByteUtils.TryParseUInt256HexUtf8(System.Text.Encoding.ASCII.GetBytes(text), out ulong b0, out ulong b1, out ulong b2, out ulong b3), $"utf8 {text}");
                Assert.AreEqual(0UL, b0 | b1 | b2 | b3, $"utf8 {text}");
            }
        }

        // More than 64 digits is rejected with all-zero outputs too.
        Assert.IsFalse(ByteUtils.TryParseUInt256HexChars(new string('f', 65), out ulong t0, out ulong t1, out ulong t2, out ulong t3));
        Assert.AreEqual(0UL, t0 | t1 | t2 | t3);
    }

    [TestMethod]
    public void UInt256HexParsers_StillParseValidInput_AtEveryLength()
    {
        for (int length = 1; length <= 64; length++)
        {
            string text = new('f', length);
            BigInteger expected = (BigInteger.One << (4 * length)) - 1;

            Assert.IsTrue(ByteUtils.TryParseUInt256HexChars(text, out ulong u0, out ulong u1, out ulong u2, out ulong u3), text);
            Assert.AreEqual(expected, new BigInteger(u0) + (new BigInteger(u1) << 64) + (new BigInteger(u2) << 128) + (new BigInteger(u3) << 192), text);
        }
    }

    // ----------------- IsAllHexChars -----------------

    [TestMethod]
    public void IsAllHexChars_AgreesWithTheNibbleParser_ForEveryUtf16Value()
    {
        // The span search replaced a scalar loop; check all 65,536 code units, each alone and at the end of a valid run.
        for (int unit = 0; unit <= char.MaxValue; unit++)
        {
            char c = (char)unit;
            bool expected = unit < 128 && Uri.IsHexDigit(c); // Uri.IsHexDigit: ASCII 0-9 a-f A-F only
            Assert.AreEqual(expected, ByteUtils.IsAllHexChars([c]), $"U+{unit:X4}");
            Assert.AreEqual(expected, ByteUtils.IsAllHexChars(new[] { 'a', 'b', 'c', 'd', 'e', 'f', 'A', 'B', c }), $"run U+{unit:X4}");
        }

        Assert.IsTrue(ByteUtils.IsAllHexChars(default));
        Assert.IsTrue(ByteUtils.IsAllHexChars("0123456789abcdefABCDEF"));
    }
}
