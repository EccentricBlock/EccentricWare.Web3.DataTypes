using System.Globalization;
using EccentricWare.Web3.DataTypes.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Direct tests of <see cref="HexCodec"/>, the fixed-width (8 and 16 digit) hex codec.</summary>
[TestClass]
public class HexCodecTests
{
    private const string LowerDigits = "0123456789abcdef";

    private static string EncodeBytes8(uint value, bool uppercase)
    {
        byte[] destination = new byte[8];
        HexCodec.Encode8(destination, value, uppercase);
        return System.Text.Encoding.ASCII.GetString(destination);
    }

    private static string EncodeChars8(uint value, bool uppercase)
    {
        char[] destination = new char[8];
        HexCodec.Encode8(destination, value, uppercase);
        return new string(destination);
    }

    private static string EncodeBytes16(ulong value, bool uppercase)
    {
        byte[] destination = new byte[16];
        HexCodec.Encode16(destination, value, uppercase);
        return System.Text.Encoding.ASCII.GetString(destination);
    }

    private static string EncodeChars16(ulong value, bool uppercase)
    {
        char[] destination = new char[16];
        HexCodec.Encode16(destination, value, uppercase);
        return new string(destination);
    }

    private static byte[] Ascii(string text) => System.Text.Encoding.Latin1.GetBytes(text);

    // ------------------------------------------------------------------ encode

    [TestMethod]
    public void Encode8_EveryNibbleAtEveryPosition_WritesThatDigitThere()
    {
        foreach (bool uppercase in new[] { false, true })
        {
            for (int position = 0; position < 8; position++)
            {
                for (int nibble = 0; nibble < 16; nibble++)
                {
                    uint value = (uint)nibble << (28 - 4 * position);
                    char digit = uppercase ? char.ToUpperInvariant(LowerDigits[nibble]) : LowerDigits[nibble];
                    string expected = new string('0', position) + digit + new string('0', 7 - position);

                    Assert.AreEqual(expected, EncodeBytes8(value, uppercase), $"bytes pos={position} nibble={nibble} upper={uppercase}");
                    Assert.AreEqual(expected, EncodeChars8(value, uppercase), $"chars pos={position} nibble={nibble} upper={uppercase}");
                }
            }
        }
    }

    [TestMethod]
    [DataRow(0x00000000u)]
    [DataRow(0xFFFFFFFFu)]
    [DataRow(0x01234567u)]
    [DataRow(0x89ABCDEFu)]
    [DataRow(0xDEADBEEFu)]
    [DataRow(0x0000000Au)]
    [DataRow(0xA0000000u)]
    [DataRow(0x09090909u)]
    [DataRow(0x0A0A0A0Au)]
    [DataRow(0x7FFFFFFFu)]
    [DataRow(0x80000000u)]
    public void Encode8_MatchesBclFormatting(uint value)
    {
        Assert.AreEqual(value.ToString("x8", CultureInfo.InvariantCulture), EncodeBytes8(value, uppercase: false));
        Assert.AreEqual(value.ToString("x8", CultureInfo.InvariantCulture), EncodeChars8(value, uppercase: false));
        Assert.AreEqual(value.ToString("X8", CultureInfo.InvariantCulture), EncodeBytes8(value, uppercase: true));
        Assert.AreEqual(value.ToString("X8", CultureInfo.InvariantCulture), EncodeChars8(value, uppercase: true));
    }

    [TestMethod]
    public void Encode16_EveryNibbleAtEveryPosition_WritesThatDigitThere()
    {
        foreach (bool uppercase in new[] { false, true })
        {
            for (int position = 0; position < 16; position++)
            {
                for (int nibble = 0; nibble < 16; nibble++)
                {
                    ulong value = (ulong)nibble << (60 - 4 * position);
                    char digit = uppercase ? char.ToUpperInvariant(LowerDigits[nibble]) : LowerDigits[nibble];
                    string expected = new string('0', position) + digit + new string('0', 15 - position);

                    Assert.AreEqual(expected, EncodeBytes16(value, uppercase), $"bytes pos={position} nibble={nibble} upper={uppercase}");
                    Assert.AreEqual(expected, EncodeChars16(value, uppercase), $"chars pos={position} nibble={nibble} upper={uppercase}");
                }
            }
        }
    }

    [TestMethod]
    [DataRow(0x0000000000000000UL)]
    [DataRow(0x0123456789ABCDEFUL)]
    [DataRow(0xFEDCBA9876543210UL)]
    [DataRow(0x00000000FFFFFFFFUL)]
    [DataRow(0xFFFFFFFF00000000UL)]
    [DataRow(ulong.MaxValue)]
    [DataRow(0x8000000000000000UL)]
    [DataRow(0x0000000100000001UL)]
    public void Encode16_MatchesBclFormatting(ulong value)
    {
        Assert.AreEqual(value.ToString("x16", CultureInfo.InvariantCulture), EncodeBytes16(value, uppercase: false));
        Assert.AreEqual(value.ToString("x16", CultureInfo.InvariantCulture), EncodeChars16(value, uppercase: false));
        Assert.AreEqual(value.ToString("X16", CultureInfo.InvariantCulture), EncodeBytes16(value, uppercase: true));
        Assert.AreEqual(value.ToString("X16", CultureInfo.InvariantCulture), EncodeChars16(value, uppercase: true));
    }

    [TestMethod]
    public void Encode_LongerDestination_WritesOnlyTheLeadingDigits()
    {
        byte[] bytes8 = new byte[11];
        Array.Fill(bytes8, (byte)0xEE);
        HexCodec.Encode8(bytes8, 0x12345678u, false);
        Assert.AreEqual("12345678", System.Text.Encoding.ASCII.GetString(bytes8, 0, 8));
        CollectionAssert.AreEqual(new byte[] { 0xEE, 0xEE, 0xEE }, bytes8[8..]);

        char[] chars8 = new char[11];
        Array.Fill(chars8, '#');
        HexCodec.Encode8(chars8, 0x12345678u, false);
        Assert.AreEqual("12345678###", new string(chars8));

        byte[] bytes16 = new byte[19];
        Array.Fill(bytes16, (byte)0xEE);
        HexCodec.Encode16(bytes16, 0x123456789ABCDEF0UL, true);
        Assert.AreEqual("123456789ABCDEF0", System.Text.Encoding.ASCII.GetString(bytes16, 0, 16));
        CollectionAssert.AreEqual(new byte[] { 0xEE, 0xEE, 0xEE }, bytes16[16..]);

        char[] chars16 = new char[19];
        Array.Fill(chars16, '#');
        HexCodec.Encode16(chars16, 0x123456789ABCDEF0UL, true);
        Assert.AreEqual("123456789ABCDEF0###", new string(chars16));
    }

    [TestMethod]
    public void Encode_DestinationShorterThanRequired_ThrowsInsteadOfWritingOutOfBounds()
    {
        // The documented contract is "at least 8/16"; shorter destinations must fail loudly (bounds-checked spans).
        // The concrete exception type differs between the byte and char paths, so only ArgumentException is pinned.
        AssertArgumentException(() => HexCodec.Encode8(new byte[7], 1u, false));
        AssertArgumentException(() => HexCodec.Encode8(new char[7], 1u, false));
        AssertArgumentException(() => HexCodec.Encode16(new byte[15], 1UL, false));
        AssertArgumentException(() => HexCodec.Encode16(new char[15], 1UL, false));
    }

    private static void AssertArgumentException(Action action)
    {
        Exception? caught = null;
        try { action(); } catch (Exception ex) { caught = ex; }

        Assert.IsInstanceOfType<ArgumentException>(caught);
    }

    // ------------------------------------------------------------------ decode

    [TestMethod]
    public void TryDecode8Bytes_EveryByteAtEveryPosition_AcceptsExactlyTheHexDigits()
    {
        for (int position = 0; position < 8; position++)
        {
            for (int b = 0; b <= 0xFF; b++)
            {
                byte[] text = Ascii("00000000");
                text[position] = (byte)b;

                bool ok = HexCodec.TryDecode8(text, out uint value);

                Assert.AreEqual(char.IsAsciiHexDigit((char)b), ok, $"byte 0x{b:X2} at {position}");
                Assert.AreEqual(ok ? uint.Parse(System.Text.Encoding.Latin1.GetString(text), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0u, value);
            }
        }
    }

    [TestMethod]
    public void TryDecode8Chars_EveryCharAtEveryPosition_AcceptsExactlyTheAsciiHexDigits()
    {
        // Covers 0x100..0xFFFF too: a char whose low byte is a hex digit must not alias onto it.
        for (int position = 0; position < 8; position++)
        {
            for (int c = 0; c <= char.MaxValue; c++)
            {
                char[] text = "00000000".ToCharArray();
                text[position] = (char)c;

                bool ok = HexCodec.TryDecode8(text, out uint value);

                Assert.AreEqual(char.IsAsciiHexDigit((char)c), ok, $"char U+{c:X4} at {position}");
                Assert.AreEqual(ok ? uint.Parse(new string(text), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0u, value);
            }
        }
    }

    [TestMethod]
    [DataRow("00000000", 0x00000000u)]
    [DataRow("ffffffff", 0xFFFFFFFFu)]
    [DataRow("FFFFFFFF", 0xFFFFFFFFu)]
    [DataRow("aBcDeF01", 0xABCDEF01u)]
    [DataRow("89abcdef", 0x89ABCDEFu)]
    [DataRow("01234567", 0x01234567u)]
    public void TryDecode8_ValidInputInAnyCase_DecodesBigEndianNibbles(string text, uint expected)
    {
        Assert.IsTrue(HexCodec.TryDecode8(Ascii(text), out uint fromBytes));
        Assert.IsTrue(HexCodec.TryDecode8(text.AsSpan(), out uint fromChars));
        Assert.AreEqual(expected, fromBytes);
        Assert.AreEqual(expected, fromChars);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(7)]
    [DataRow(9)]
    [DataRow(16)]
    public void TryDecode8_WrongLength_FailsWithZeroValue(int length)
    {
        Assert.IsFalse(HexCodec.TryDecode8(Ascii(new string('a', length)), out uint fromBytes));
        Assert.IsFalse(HexCodec.TryDecode8(new string('a', length).AsSpan(), out uint fromChars));
        Assert.AreEqual(0u, fromBytes);
        Assert.AreEqual(0u, fromChars);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(8)]
    [DataRow(15)]
    [DataRow(17)]
    [DataRow(32)]
    public void TryDecode16_WrongLength_FailsWithZeroValue(int length)
    {
        Assert.IsFalse(HexCodec.TryDecode16(Ascii(new string('a', length)), out ulong fromBytes));
        Assert.IsFalse(HexCodec.TryDecode16(new string('a', length).AsSpan(), out ulong fromChars));
        Assert.AreEqual(0UL, fromBytes);
        Assert.AreEqual(0UL, fromChars);
    }

    [TestMethod]
    [DataRow("AbCdEf0123456789", 0xABCDEF0123456789UL)]
    [DataRow("0000000000000000", 0UL)]
    [DataRow("ffffffffffffffff", ulong.MaxValue)]
    [DataRow("FFFFFFFFFFFFFFFF", ulong.MaxValue)]
    [DataRow("00000000ffffffff", 0x00000000FFFFFFFFUL)]
    [DataRow("ffffffff00000000", 0xFFFFFFFF00000000UL)]
    public void TryDecode16_ValidInput_HighHalfFirst(string text, ulong expected)
    {
        Assert.IsTrue(HexCodec.TryDecode16(Ascii(text), out ulong fromBytes));
        Assert.IsTrue(HexCodec.TryDecode16(text.AsSpan(), out ulong fromChars));
        Assert.AreEqual(expected, fromBytes);
        Assert.AreEqual(expected, fromChars);
    }

    [TestMethod]
    [DataRow("0123g567" + "89abcdef")] // high half only
    [DataRow("01234567" + "89abcdeg")] // low half only
    [DataRow("0123g567" + "89abcdeg")] // both halves
    [DataRow("01234567" + "89abcde ")]
    public void TryDecode16_InvalidDigit_FailsWithoutPartialValue(string text)
    {
        Assert.IsFalse(HexCodec.TryDecode16(Ascii(text), out ulong fromBytes));
        Assert.IsFalse(HexCodec.TryDecode16(text.AsSpan(), out ulong fromChars));
        Assert.AreEqual(0UL, fromBytes);
        Assert.AreEqual(0UL, fromChars);
    }

    [TestMethod]
    public void TryDecode16_NonAsciiCharacterInEitherHalf_Fails()
    {
        foreach (int position in new[] { 0, 7, 8, 15 })
        {
            foreach (char wide in new[] { 'İ', '０', '١' })
            {
                char[] text = "0123456789abcdef".ToCharArray();
                text[position] = wide;
                Assert.IsFalse(HexCodec.TryDecode16(text, out ulong value), $"U+{(int)wide:X4} at {position}");
                Assert.AreEqual(0UL, value);
            }
        }
    }

    // ------------------------------------------------------------------ round trips

    [TestMethod]
    public void EncodeThenDecode_SeededRandomValues_RoundTripsInEveryRepresentation()
    {
        var random = new Random(8016);
        for (int iteration = 0; iteration < 25_000; iteration++)
        {
            uint value32 = (uint)random.NextInt64(0, 1L << 32);
            ulong value64 = (ulong)random.NextInt64() ^ ((ulong)random.NextInt64() << 1);

            foreach (bool uppercase in new[] { false, true })
            {
                string text8 = EncodeChars8(value32, uppercase);
                Assert.AreEqual(text8, EncodeBytes8(value32, uppercase));
                Assert.IsTrue(text8.All(c => uppercase ? char.IsAsciiHexDigitUpper(c) || char.IsAsciiDigit(c) : char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c)));
                Assert.IsTrue(HexCodec.TryDecode8(Ascii(text8), out uint back8) && back8 == value32);
                Assert.IsTrue(HexCodec.TryDecode8(text8.AsSpan(), out back8) && back8 == value32);

                string text16 = EncodeChars16(value64, uppercase);
                Assert.AreEqual(text16, EncodeBytes16(value64, uppercase));
                Assert.IsTrue(HexCodec.TryDecode16(Ascii(text16), out ulong back16) && back16 == value64);
                Assert.IsTrue(HexCodec.TryDecode16(text16.AsSpan(), out back16) && back16 == value64);
            }
        }
    }
}
