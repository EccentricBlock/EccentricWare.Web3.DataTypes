using EccentricWare.Web3.DataTypes.Utils;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Variable-width and 256-bit hex parsers (chars and UTF-8): lengths and values against BigInteger/Convert oracles,
/// per-position rejection, and independent exhaustive nibble sweeps.
/// </summary>
[TestClass]
public sealed class ByteUtils_HexParsingContractTests
{
    private static readonly char[] BadAsciiChars = ['g', 'G', 'x', 'z', '/', ':', '@', '`', ' ', '\0', '\t', '-', '+', '\u007F'];

    private static string RandomHex(Random random, int length)
    {
        const string digits = "0123456789abcdefABCDEF";
        return string.Create(length, random, static (span, r) =>
        {
            for (int i = 0; i < span.Length; i++) span[i] = digits[r.Next(digits.Length)];
        });
    }

    private static ulong[] Limbs(BigInteger value) =>
        [(ulong)(value & ulong.MaxValue), (ulong)((value >> 64) & ulong.MaxValue), (ulong)((value >> 128) & ulong.MaxValue), (ulong)((value >> 192) & ulong.MaxValue)];

    private static BigInteger ParseOracle(string hex) => BigInteger.Parse("0" + hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static bool IsHexOracle(int c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static int NibbleOracle(int c) => !IsHexOracle(c) ? -1 : Convert.ToInt32(((char)c).ToString(), 16);

    // ---- exhaustive nibble sweeps (independent predicate, not the code under test) -------------------------------

    [TestMethod]
    public void ParseHexNibble_AllUtf16Characters_MatchAnExplicitHexPredicate()
    {
        for (int c = 0; c <= char.MaxValue; c++)
            Assert.AreEqual(NibbleOracle(c), ByteUtils.ParseHexNibble((char)c), $"U+{c:X4}");
    }

    [TestMethod]
    public void ParseHexNibbleUtf8_AllByteValues_MatchAnExplicitHexPredicate()
    {
        for (int b = 0; b < 256; b++)
            Assert.AreEqual(NibbleOracle(b), ByteUtils.ParseHexNibbleUtf8((byte)b), $"0x{b:X2}");
    }

    [TestMethod]
    public void IsAllHexChars_SingleCharacterSweep_MatchesPredicate()
    {
        for (int c = 0; c <= char.MaxValue; c++)
            Assert.AreEqual(IsHexOracle(c), ByteUtils.IsAllHexChars([(char)c]), $"U+{c:X4}");
    }

    [TestMethod]
    public void IsAllHexUtf8_SingleByteSweep_MatchesPredicate()
    {
        for (int b = 0; b < 256; b++)
            Assert.AreEqual(IsHexOracle(b), ByteUtils.IsAllHexUtf8([(byte)b]), $"0x{b:X2}");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("0123456789abcdefABCDEF")]
    public void IsAllHex_ValidAndEmpty_AreTrue(string text)
    {
        Assert.IsTrue(ByteUtils.IsAllHexChars(text));
        Assert.IsTrue(ByteUtils.IsAllHexUtf8(Encoding.ASCII.GetBytes(text)));
    }

    [TestMethod]
    public void IsAllHex_OneBadCharacterAtFirstMiddleOrLast_IsFalse()
    {
        foreach (int position in new[] { 0, 5, 11 })
        {
            char[] chars = "0123456789ab".ToCharArray();
            chars[position] = 'g';
            Assert.IsFalse(ByteUtils.IsAllHexChars(chars), $"chars @{position}");
            Assert.IsFalse(ByteUtils.IsAllHexUtf8(Encoding.ASCII.GetBytes(chars)), $"utf8 @{position}");
        }
    }

    // ---- 1..8 digit (uint) and 1..16 digit (ulong) variable parsers ---------------------------------------------------

    [TestMethod]
    public void TryParseHexUInt32Utf8_AllLengths1To8_MatchConvertToUInt32()
    {
        var random = new Random(1);
        for (int length = 1; length <= 8; length++)
            for (int trial = 0; trial < 20; trial++)
            {
                string hex = RandomHex(random, length);
                Assert.IsTrue(ByteUtils.TryParseHexUInt32Utf8(Encoding.ASCII.GetBytes(hex), out uint value), hex);
                Assert.AreEqual(Convert.ToUInt32(hex, 16), value, hex);
            }
    }

    [TestMethod]
    public void TryParseHexUInt64Variable_AllLengths1To16_MatchConvertToUInt64_ForCharsAndUtf8()
    {
        var random = new Random(2);
        for (int length = 1; length <= 16; length++)
            for (int trial = 0; trial < 20; trial++)
            {
                string hex = RandomHex(random, length);
                ulong expected = Convert.ToUInt64(hex, 16);
                Assert.IsTrue(ByteUtils.TryParseHexUInt64CharsVariable(hex, out ulong fromChars), hex);
                Assert.IsTrue(ByteUtils.TryParseHexUInt64Utf8Variable(Encoding.ASCII.GetBytes(hex), out ulong fromUtf8), hex);
                Assert.AreEqual(expected, fromChars, hex);
                Assert.AreEqual(expected, fromUtf8, hex);
            }
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("123456789")]
    [DataRow("0x1")]
    [DataRow("+1")]
    [DataRow("-1")]
    [DataRow(" 1")]
    [DataRow("1 ")]
    [DataRow("12x")]
    [DataRow("x12")]
    public void TryParseHexUInt32Utf8_OutOfRangeLengthOrInvalidText_ReturnsFalse(string text)
    {
        Assert.IsFalse(ByteUtils.TryParseHexUInt32Utf8(Encoding.ASCII.GetBytes(text), out _));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("12345678901234567")]
    [DataRow("0x1")]
    [DataRow("+1")]
    [DataRow(" 1")]
    [DataRow("1 ")]
    [DataRow("12x")]
    [DataRow("\uFF11")]
    [DataRow("\u0130")]
    public void TryParseHexUInt64Variable_OutOfRangeLengthOrInvalidText_ReturnsFalse(string text)
    {
        Assert.IsFalse(ByteUtils.TryParseHexUInt64CharsVariable(text, out _));
        Assert.IsFalse(ByteUtils.TryParseHexUInt64Utf8Variable(Encoding.UTF8.GetBytes(text), out _));
    }

    [TestMethod]
    public void VariableParsers_InvalidCharacterAtEveryPosition_ReturnsFalse()
    {
        foreach (char bad in BadAsciiChars)
        {
            for (int length = 1; length <= 8; length++)
                for (int position = 0; position < length; position++)
                {
                    char[] chars = new string('a', length).ToCharArray();
                    chars[position] = bad;
                    string text = new(chars);
                    byte[] utf8 = Encoding.ASCII.GetBytes(text);

                    Assert.IsFalse(ByteUtils.TryParseHexUInt32Utf8(utf8, out _), $"u32 '{text}'");
                    Assert.IsFalse(ByteUtils.TryParseHexUInt64Utf8Variable(utf8, out _), $"u64 utf8 '{text}'");
                    Assert.IsFalse(ByteUtils.TryParseHexUInt64CharsVariable(text, out _), $"u64 chars '{text}'");
                }
        }
    }

    [TestMethod]
    public void Utf8VariableParsers_HighBytesAreRejected()
    {
        for (int b = 0x80; b < 256; b++)
        {
            byte[] text = [(byte)'1', (byte)b, (byte)'2'];
            Assert.IsFalse(ByteUtils.TryParseHexUInt32Utf8(text, out _), $"0x{b:X2}");
            Assert.IsFalse(ByteUtils.TryParseHexUInt64Utf8Variable(text, out _), $"0x{b:X2}");
        }
    }

    // ---- fixed 8 / 16 UTF-8 parsers: rejection sweep (the chars versions are swept in ByteUtils_FixedWidthHexTests) ----

    [TestMethod]
    public void Utf8FixedParsers_InvalidOrHighByteAtEveryPosition_ReturnFalseWithZeroValue()
    {
        byte[] badBytes = [.. BadAsciiChars.Select(c => (byte)c), 0x80, 0xA0, 0xB0, 0xFF];

        foreach (byte bad in badBytes)
        {
            for (int position = 0; position < 8; position++)
            {
                byte[] text = Enumerable.Repeat((byte)'f', 8).ToArray();
                text[position] = bad;
                Assert.IsFalse(ByteUtils.TryParseHexUInt32Utf8Fixed8(text, out uint v32), $"0x{bad:X2}@{position}");
                Assert.AreEqual(0u, v32, $"0x{bad:X2}@{position}");
            }

            for (int position = 0; position < 16; position++)
            {
                byte[] text = Enumerable.Repeat((byte)'f', 16).ToArray();
                text[position] = bad;
                Assert.IsFalse(ByteUtils.TryParseHexUInt64Utf8Fixed16(text, out ulong v64), $"0x{bad:X2}@{position}");
                Assert.AreEqual(0UL, v64, $"0x{bad:X2}@{position}");
            }
        }
    }

    // ---- ParseHexUInt64CharsVariable (returns ulong.MaxValue as the failure sentinel) ---------------------------------

    [TestMethod]
    [DataRow("0", 0UL)]
    [DataRow("1f", 31UL)]
    [DataRow("FfFf", 0xFFFFUL)]
    [DataRow("0123456789abcdef", 0x0123456789ABCDEFUL)]
    [DataRow("00000000000000ff", 255UL)]
    public void ParseHexUInt64CharsVariable_OrdinaryValues_AreParsed(string hex, ulong expected)
    {
        Assert.AreEqual(expected, ByteUtils.ParseHexUInt64CharsVariable(hex));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("g")]
    [DataRow("0x1")]
    [DataRow("12345678901234567")]
    public void ParseHexUInt64CharsVariable_InvalidInput_ReturnsMaxValueSentinel(string hex)
    {
        Assert.AreEqual(ulong.MaxValue, ByteUtils.ParseHexUInt64CharsVariable(hex));
    }

    // ---- 256-bit hex parsers -----------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(15)]
    [DataRow(16)]
    [DataRow(17)]
    [DataRow(31)]
    [DataRow(32)]
    [DataRow(33)]
    [DataRow(47)]
    [DataRow(48)]
    [DataRow(49)]
    [DataRow(63)]
    [DataRow(64)]
    public void TryParseUInt256Hex_EveryLengthClass_MatchesBigIntegerOracle_ForCharsAndUtf8(int length)
    {
        var random = new Random(length * 7919);
        for (int trial = 0; trial < 25; trial++)
        {
            string hex = RandomHex(random, length);
            ulong[] expected = Limbs(ParseOracle(hex));

            Assert.IsTrue(ByteUtils.TryParseUInt256HexChars(hex, out ulong c0, out ulong c1, out ulong c2, out ulong c3), hex);
            Assert.IsTrue(ByteUtils.TryParseUInt256HexUtf8(Encoding.ASCII.GetBytes(hex), out ulong u0, out ulong u1, out ulong u2, out ulong u3), hex);
            CollectionAssert.AreEqual(expected, new[] { c0, c1, c2, c3 }, $"chars {hex}");
            CollectionAssert.AreEqual(expected, new[] { u0, u1, u2, u3 }, $"utf8 {hex}");
        }
    }

    [TestMethod]
    public void TryParseUInt256Hex_Empty_IsZero_ForCharsAndUtf8()
    {
        Assert.IsTrue(ByteUtils.TryParseUInt256HexChars("", out ulong a, out ulong b, out ulong c, out ulong d));
        CollectionAssert.AreEqual(new ulong[4], new[] { a, b, c, d });
        Assert.IsTrue(ByteUtils.TryParseUInt256HexUtf8([], out a, out b, out c, out d));
        CollectionAssert.AreEqual(new ulong[4], new[] { a, b, c, d });
    }

    [TestMethod]
    public void TryParseUInt256Hex_MaxValueAndLeadingZeros()
    {
        string max = new('F', 64);
        Assert.IsTrue(ByteUtils.TryParseUInt256HexChars(max, out ulong a, out ulong b, out ulong c, out ulong d));
        CollectionAssert.AreEqual(new[] { ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue }, new[] { a, b, c, d });

        string leadingZeros = new string('0', 63) + "1";
        Assert.IsTrue(ByteUtils.TryParseUInt256HexUtf8(Encoding.ASCII.GetBytes(leadingZeros), out a, out b, out c, out d));
        CollectionAssert.AreEqual(new ulong[] { 1, 0, 0, 0 }, new[] { a, b, c, d });
    }

    [TestMethod]
    [DataRow(65)]
    [DataRow(66)]
    [DataRow(1000)]
    public void TryParseUInt256Hex_MoreThan64Digits_IsRejected(int length)
    {
        string hex = new('1', length);

        Assert.IsFalse(ByteUtils.TryParseUInt256HexChars(hex, out _, out _, out _, out _));
        Assert.IsFalse(ByteUtils.TryParseUInt256HexUtf8(Encoding.ASCII.GetBytes(hex), out _, out _, out _, out _));
    }

    [TestMethod]
    [DataRow("0x1")]
    [DataRow(" 1")]
    [DataRow("1 ")]
    [DataRow("+1")]
    [DataRow("-1")]
    [DataRow("\uFF10")]
    public void TryParseUInt256Hex_PrefixSignWhitespaceOrFullwidth_IsRejected(string text)
    {
        Assert.IsFalse(ByteUtils.TryParseUInt256HexChars(text, out _, out _, out _, out _));
        Assert.IsFalse(ByteUtils.TryParseUInt256HexUtf8(Encoding.UTF8.GetBytes(text), out _, out _, out _, out _));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(16)]
    [DataRow(17)]
    [DataRow(33)]
    [DataRow(48)]
    [DataRow(49)]
    [DataRow(63)]
    [DataRow(64)]
    public void TryParseUInt256Hex_InvalidCharacterAtEveryPosition_IsRejected(int length)
    {
        foreach (char bad in new[] { 'g', '/', ':', '@', '`', ' ' })
            for (int position = 0; position < length; position++)
            {
                char[] chars = new string('1', length).ToCharArray();
                chars[position] = bad;
                string text = new(chars);

                Assert.IsFalse(ByteUtils.TryParseUInt256HexChars(text, out _, out _, out _, out _), $"chars '{bad}'@{position}/{length}");
                Assert.IsFalse(ByteUtils.TryParseUInt256HexUtf8(Encoding.ASCII.GetBytes(text), out _, out _, out _, out _), $"utf8 '{bad}'@{position}/{length}");
            }
    }

    [TestMethod]
    public void TryParseUInt256HexUtf8_HighBytesAreRejected_AtLengths16And64()
    {
        foreach (int length in new[] { 16, 64 })
            for (int b = 0x80; b < 256; b++)
            {
                byte[] text = Enumerable.Repeat((byte)'1', length).ToArray();
                text[length / 2] = (byte)b;
                Assert.IsFalse(ByteUtils.TryParseUInt256HexUtf8(text, out _, out _, out _, out _), $"0x{b:X2}/{length}");
            }
    }
}
