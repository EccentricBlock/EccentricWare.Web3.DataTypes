using EccentricWare.Web3.DataTypes.Utils;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Whitespace trimming, "0x" prefix, sign, JSON-quote and AsSpanSafe edge cases (chars and UTF-8 overloads).</summary>
[TestClass]
public sealed class ByteUtils_TextEdgeCaseTests
{
    private static string Str(ReadOnlySpan<byte> s) => Encoding.UTF8.GetString(s);

    private static byte[] Latin1(string s) => Encoding.UTF8.GetBytes(s);

    // ---- TrimAsciiWhitespace: only space, \t, \r, \n -------------------------------------------------------------------------

    [TestMethod]
    [DataRow("\v\fx\f\v", "\v\fx\f\v")]
    [DataRow("\u00A0x\u00A0", "\u00A0x\u00A0")]
    [DataRow("\u2003x\u2003", "\u2003x\u2003")]
    [DataRow("\u0085x\u0085", "\u0085x\u0085")]
    [DataRow(" \v x \f ", "\v x \f")]
    [DataRow("   ", "")]
    [DataRow(" \t\r\n", "")]
    [DataRow("", "")]
    [DataRow("x", "x")]
    [DataRow(" a b ", "a b")]
    [DataRow("\t\ta\r\nb\n\n", "a\r\nb")]
    public void TrimAsciiWhitespace_OnlyTrimsSpaceTabCrLf_AndKeepsInteriorUntouched(string input, string expected)
    {
        Assert.AreEqual(expected, ByteUtils.TrimAsciiWhitespace(input).ToString());
        Assert.AreEqual(expected, Str(ByteUtils.TrimAsciiWhitespaceUtf8(Latin1(input))));
    }

    [TestMethod]
    public void TrimAsciiWhitespaceUtf8_RawHighAndControlBytes_AreNotWhitespace()
    {
        byte[] input = [0x0B, 0x0C, 0x85, 0xA0, (byte)'x', 0xA0, 0x85, 0x0C, 0x0B];

        CollectionAssert.AreEqual(input, ByteUtils.TrimAsciiWhitespaceUtf8(input).ToArray());
        CollectionAssert.AreEqual(input, ByteUtils.TrimAsciiWhitespaceUtf8([.. "  "u8, .. input, .. "\r\n"u8]).ToArray());
    }

    [TestMethod]
    public void TrimAsciiWhitespace_VeryLongLeadingWhitespace_IsTrimmed()
    {
        string input = new string(' ', 1_000_000) + "x";

        Assert.AreEqual("x", ByteUtils.TrimAsciiWhitespace(input).ToString());
        Assert.AreEqual("x", Str(ByteUtils.TrimAsciiWhitespaceUtf8(Latin1(input))));
    }

    [TestMethod]
    public void TrimAsciiWhitespace_ReturnsASliceOfTheInput_NotACopy()
    {
        string input = "  abc  ";

        ReadOnlySpan<char> trimmed = ByteUtils.TrimAsciiWhitespace(input);

        Assert.IsTrue(System.Runtime.CompilerServices.Unsafe.AreSame(in input.AsSpan(2, 1)[0], in trimmed[0]));
    }

    // ---- TrimWhitespace (Unicode) ------------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow("  \u0085\u00A0x\u2003\v\f ", "x")]
    [DataRow("   ", "")]
    [DataRow("", "")]
    [DataRow("a b", "a b")]
    [DataRow("\u200Bx\u200B", "\u200Bx\u200B")]   // zero-width space is not white space
    [DataRow("\u2007 a \u2028", "a")]
    public void TrimWhitespace_TrimsUnicodeWhiteSpace_ButNotZeroWidthSpace(string input, string expected)
    {
        Assert.AreEqual(expected, ByteUtils.TrimWhitespace(input).ToString());
    }

    [TestMethod]
    public void AsSpanSafe_NullAndEmptyStrings_GiveEmptySpans()
    {
        Assert.IsTrue(((string?)null).AsSpanSafe().IsEmpty);
        Assert.IsTrue("".AsSpanSafe().IsEmpty);
        Assert.AreEqual("abc", "abc".AsSpanSafe().ToString());
    }

    // ---- TryTrimHexPrefix ----------------------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow("0x", true, "")]
    [DataRow("0X", true, "")]
    [DataRow("0x1f", true, "1f")]
    [DataRow("0xx", true, "x")]
    [DataRow("0x0x1", true, "0x1")]
    [DataRow("", false, "")]
    [DataRow("0", false, "")]
    [DataRow("x", false, "")]
    [DataRow("x0", false, "")]
    [DataRow("00x", false, "")]
    [DataRow(" 0x1", false, "")]
    [DataRow("0 x", false, "")]
    [DataRow("1x1", false, "")]
    public void TryTrimHexPrefix_RemovesOnlyALeadingZeroX(string input, bool expectedTrimmed, string expectedRemainder)
    {
        Assert.AreEqual(expectedTrimmed, ByteUtils.TryTrimHexPrefix(input, out var chars));
        Assert.AreEqual(expectedRemainder, chars.ToString());
        Assert.AreEqual(expectedTrimmed, ByteUtils.TryTrimHexPrefixUtf8(Latin1(input), out var utf8));
        Assert.AreEqual(expectedRemainder, Str(utf8));
    }

    [TestMethod]
    public void TryTrimHexPrefixUtf8_SecondByteSweep_OnlyLowerAndUpperXMatch()
    {
        for (int b = 0; b < 256; b++)
        {
            bool expected = b is 'x' or 'X';
            Assert.AreEqual(expected, ByteUtils.TryTrimHexPrefixUtf8([(byte)'0', (byte)b, (byte)'1'], out var rest), $"0x{b:X2}");
            if (expected) Assert.AreEqual(1, rest.Length);
        }
    }

    [TestMethod]
    public void TryTrimHexPrefixChars_SecondCharacterSweep_OnlyLowerAndUpperXMatch()
    {
        for (int c = 0; c <= char.MaxValue; c++)
            Assert.AreEqual(c is 'x' or 'X', ByteUtils.TryTrimHexPrefix(new[] { '0', (char)c }, out _), $"U+{c:X4}");
    }

    // ---- TryTrimLeadingSign ----------------------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow("", false, false, "")]
    [DataRow("-", true, true, "")]
    [DataRow("+", true, false, "")]
    [DataRow("-1", true, true, "1")]
    [DataRow("+1", true, false, "1")]
    [DataRow("+-1", true, false, "-1")]
    [DataRow("--1", true, true, "-1")]
    [DataRow("1", true, false, "1")]
    [DataRow(" -1", true, false, " -1")]
    [DataRow("1-", true, false, "1-")]
    public void TryTrimLeadingSign_StripsAtMostOneLeadingSign(string input, bool expectedOk, bool expectedNegative, string expectedRemainder)
    {
        Assert.AreEqual(expectedOk, ByteUtils.TryTrimLeadingSign(input, out bool negative, out var chars));
        Assert.AreEqual(expectedNegative, negative);
        Assert.AreEqual(expectedRemainder, chars.ToString());

        Assert.AreEqual(expectedOk, ByteUtils.TryTrimLeadingSignUtf8(Latin1(input), out negative, out var utf8));
        Assert.AreEqual(expectedNegative, negative);
        Assert.AreEqual(expectedRemainder, Str(utf8));
    }

    // ---- JSON quote removal ----------------------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow("\"abc\"", "abc", true)]
    [DataRow("\"\"", "", false)]
    [DataRow("\"", "\"", true)]          // a lone quote is not a quote pair: returned unchanged
    [DataRow("\"abc", "\"abc", true)]
    [DataRow("abc\"", "abc\"", true)]
    [DataRow("abc", "abc", true)]
    [DataRow("\"\"a\"\"", "\"a\"", true)] // exactly one pair is removed
    [DataRow(" \"abc\" ", " \"abc\" ", true)]
    [DataRow("", "", false)]
    public void UnquoteJson_RemovesExactlyOneSurroundingQuotePair(string input, string expected, bool expectedNonEmpty)
    {
        byte[] utf8 = Latin1(input);

        Assert.AreEqual(expected, Str(ByteUtils.UnquoteJsonStringUtf8(utf8)));
        Assert.AreEqual(expectedNonEmpty, ByteUtils.TryUnquoteJsonUtf8(utf8, out var unquoted));
        Assert.AreEqual(expected, Str(unquoted));
    }
}
