using System.Globalization;
using System.Numerics;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Parsing of uint256 from text: hex and decimal, chars and UTF-8, quoting, whitespace, limits and failures.</summary>
[TestClass]
public sealed class UInt256ParsingTests
{
    private static readonly string MaxDecimal = (UInt256TestValues.Two256 - 1).ToString(CultureInfo.InvariantCulture);
    private static readonly string TwoPow256Decimal = UInt256TestValues.Two256.ToString(CultureInfo.InvariantCulture);
    private static readonly string MaxHex = new('f', 64);

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static uint256 U(ulong value) => new(value);

    // Valid inputs shared by the char and UTF-8 parsers (value as decimal text).
    public static IEnumerable<(string Text, string Expected)> ValidTokens()
    {
        yield return ("0x", "0");
        yield return ("0X", "0");
        yield return ("0x0", "0");
        yield return ("0", "0");
        yield return ("42", "42");
        yield return ("0x2a", "42");
        yield return ("0x2A", "42");
        yield return ("0X2a", "42");
        yield return ("  0x2a  ", "42");
        yield return ("\t42\r\n", "42");
        yield return ("0x " , "0");                         // trailing space is trimmed first, leaving a bare prefix
        yield return ("00042", "42");
        yield return ("0x00042", "66");
        yield return ("0x" + MaxHex, MaxDecimal);
        yield return ("0X" + MaxHex.ToUpperInvariant(), MaxDecimal);
        yield return (MaxDecimal, MaxDecimal);
        yield return ("0x" + new string('0', 64), "0");
        yield return (new string('0', 200) + "42", "42");  // decimal accepts any number of leading zeros
        yield return (new string('0', 10_000), "0");
    }

    // Inputs that must be rejected by both parsers, with the result left at zero.
    public static IEnumerable<string> InvalidTokens()
    {
        yield return "";
        yield return "   ";
        yield return "0xGG";
        yield return "0xZZ";
        yield return "not-a-number";
        yield return "-1";
        yield return "-0";
        yield return "+1";
        yield return "0x0x1";
        yield return "00x1";
        yield return "x1";
        yield return "0xx1";
        yield return "0 x1";
        yield return "0x 1";
        yield return "1 2";
        yield return "1_0";
        yield return "0b1";
        yield return "1e3";
        yield return "1.0";
        yield return "1,000";
        yield return "ff";                                   // hex digits need the 0x prefix
        yield return "١٢٣";                                  // Arabic-Indic digits
        yield return "１２３";                                // fullwidth digits
        yield return "\0";
        yield return "0x\0";
        yield return TwoPow256Decimal;
        yield return TwoPow256Decimal + "0";
        yield return new string('9', 79);
        yield return "0x" + new string('f', 65);
        yield return "0x" + new string('0', 65);             // hex longer than 64 digits is rejected even when the extra digits are zeros
        yield return "0x1" + new string('0', 64);
        yield return "0x" + new string('f', 10_000);
        yield return new string('1', 10_000);
    }

    // ---- char parsing ----

    [TestMethod]
    public void TryParse_Chars_ValidTokens()
    {
        foreach (var (text, expected) in ValidTokens())
        {
            Assert.IsTrue(uint256.TryParse(text.AsSpan(), out uint256 result), Shorten(text));
            Assert.AreEqual(BigInteger.Parse(expected, CultureInfo.InvariantCulture), UInt256TestValues.ToBig(result), Shorten(text));
            Assert.IsTrue(uint256.TryParse(text.AsSpan(), CultureInfo.InvariantCulture, out uint256 withProvider), Shorten(text));
            Assert.AreEqual(result, withProvider);
            Assert.IsTrue(uint256.TryParse(text, out uint256 viaString), Shorten(text));
            Assert.AreEqual(result, viaString);
            Assert.IsTrue(uint256.TryParse(text, CultureInfo.InvariantCulture, out uint256 viaStringWithProvider), Shorten(text));
            Assert.AreEqual(result, viaStringWithProvider);
        }
    }

    [TestMethod]
    public void TryParse_Chars_InvalidTokens_ReturnFalseAndZero()
    {
        foreach (string text in InvalidTokens())
        {
            Assert.IsFalse(uint256.TryParse(text.AsSpan(), out uint256 result), Shorten(text));
            Assert.AreEqual(uint256.Zero, result, Shorten(text));
            Assert.IsFalse(uint256.TryParse(text.AsSpan(), CultureInfo.InvariantCulture, out result), Shorten(text));
            Assert.AreEqual(uint256.Zero, result, Shorten(text));
            Assert.IsFalse(uint256.TryParse(text, out result), Shorten(text));
            Assert.AreEqual(uint256.Zero, result, Shorten(text));
        }
    }

    [TestMethod]
    public void TryParse_NullString_ReturnsFalseAndZero()
    {
        Assert.IsFalse(uint256.TryParse((string?)null, out uint256 result));
        Assert.AreEqual(uint256.Zero, result);
        Assert.IsFalse(uint256.TryParse((string?)null, CultureInfo.InvariantCulture, out result));
        Assert.AreEqual(uint256.Zero, result);
    }

    [TestMethod]
    public void TryParse_Chars_TrimsUnicodeWhitespace_ButUtf8OnlyAsciiWhitespace()
    {
        // Documents current behaviour: the char parser trims char.IsWhiteSpace, the UTF-8 parser only space, tab, CR and LF.
        Assert.IsTrue(uint256.TryParse("  42  ".AsSpan(), out uint256 viaChars));
        Assert.AreEqual(U(42), viaChars);
        Assert.IsFalse(uint256.TryParse(Utf8("  42"), out _));
    }

    // ---- UTF-8 parsing ----

    [TestMethod]
    public void TryParse_Utf8_ValidTokens_QuotedAndUnquoted()
    {
        foreach (var (text, expected) in ValidTokens())
        {
            BigInteger expectedValue = BigInteger.Parse(expected, CultureInfo.InvariantCulture);

            Assert.IsTrue(uint256.TryParse(Utf8(text), out uint256 unquoted), Shorten(text));
            Assert.AreEqual(expectedValue, UInt256TestValues.ToBig(unquoted), Shorten(text));

            Assert.IsTrue(uint256.TryParse(Utf8("\"" + text + "\""), out uint256 quoted), "quoted " + Shorten(text));
            Assert.AreEqual(unquoted, quoted);
        }
    }

    [TestMethod]
    public void TryParse_Utf8_InvalidTokens_ReturnFalseAndZero()
    {
        foreach (string text in InvalidTokens())
        {
            Assert.IsFalse(uint256.TryParse(Utf8(text), out uint256 result), Shorten(text));
            Assert.AreEqual(uint256.Zero, result, Shorten(text));
            Assert.IsFalse(uint256.TryParse(Utf8("\"" + text + "\""), out result), "quoted " + Shorten(text));
            Assert.AreEqual(uint256.Zero, result, "quoted " + Shorten(text));
        }
    }

    [TestMethod]
    [DataRow("\"42\"", true, 42UL)]
    [DataRow("  \"  0x2A  \"  ", true, 42UL)]
    [DataRow("\"\"", false, 0UL)]
    [DataRow("\"\"42\"\"", false, 0UL)]        // only one pair of quotes is removed; the inner pair stays and is invalid
    [DataRow("\"42", false, 0UL)]
    [DataRow("42\"", false, 0UL)]
    [DataRow("\"", false, 0UL)]
    [DataRow("'42'", false, 0UL)]
    public void TryParse_Utf8_QuoteHandling(string token, bool expectedSuccess, ulong expectedValue)
    {
        Assert.AreEqual(expectedSuccess, uint256.TryParse(Utf8(token), out uint256 result));
        Assert.AreEqual(U(expectedValue), result);
    }

    [TestMethod]
    [DataRow("42", true, 42UL)]
    [DataRow(" 42 ", true, 42UL)]
    [DataRow("0x", true, 0UL)]
    [DataRow("0x2a", true, 42UL)]
    [DataRow("\"42\"", false, 0UL)]            // the JSON converter hands over unquoted token text, so a quote must stay invalid
    [DataRow("", false, 0UL)]
    [DataRow("   ", false, 0UL)]
    [DataRow("-1", false, 0UL)]
    public void TryParseUnquoted_DoesNotAcceptQuotes(string token, bool expectedSuccess, ulong expectedValue)
    {
        Assert.AreEqual(expectedSuccess, uint256.TryParseUnquoted(Utf8(token), out uint256 result));
        Assert.AreEqual(U(expectedValue), result);
    }

    // ---- Parse (throwing forms) ----

    [TestMethod]
    public void Parse_AllOverloads_ReturnTheValue()
    {
        Assert.AreEqual(U(42), uint256.Parse("0x2a".AsSpan()));
        Assert.AreEqual(U(42), uint256.Parse("42".AsSpan()));              // the char overload accepts decimal as well as hex
        Assert.AreEqual(U(42), uint256.Parse("  0x2a  ".AsSpan()));
        Assert.AreEqual(U(42), uint256.Parse("0x2a"));
        Assert.AreEqual(U(42), uint256.Parse("42", CultureInfo.InvariantCulture));
        Assert.AreEqual(U(42), uint256.Parse("0x2a".AsSpan(), CultureInfo.InvariantCulture));
        Assert.AreEqual(U(42), uint256.Parse(Utf8("0x2a")));
        Assert.AreEqual(U(42), uint256.Parse(Utf8("\"0x2a\"")));
        Assert.AreEqual(U(123), uint256.Parse(Utf8("123")));
        Assert.AreEqual(uint256.MaxValue, uint256.Parse(Utf8(MaxDecimal)));
        Assert.AreEqual(uint256.MaxValue, uint256.Parse(MaxDecimal.AsSpan()));
    }

    [TestMethod]
    public void Parse_InvalidInput_ThrowsFormatException_InEveryOverload()
    {
        foreach (string text in new[] { "", "0xGG", "-1", "not-a-number", TwoPow256Decimal, "0x" + new string('f', 65) })
        {
            Assert.ThrowsExactly<FormatException>(() => _ = uint256.Parse(text.AsSpan()), Shorten(text));
            Assert.ThrowsExactly<FormatException>(() => _ = uint256.Parse(text), Shorten(text));
            Assert.ThrowsExactly<FormatException>(() => _ = uint256.Parse(text, CultureInfo.InvariantCulture), Shorten(text));
            Assert.ThrowsExactly<FormatException>(() => _ = uint256.Parse(text.AsSpan(), CultureInfo.InvariantCulture), Shorten(text));
            Assert.ThrowsExactly<FormatException>(() => _ = uint256.Parse(Utf8(text)), Shorten(text));
        }

        Assert.ThrowsExactly<FormatException>(() => _ = uint256.Parse(Utf8("\"")));
    }

    // ---- ParseDecimal ----

    [TestMethod]
    public void ParseDecimal_ValidInputs()
    {
        Assert.AreEqual(U(12345), uint256.ParseDecimal("  12345  ".AsSpan()));
        Assert.AreEqual(U(42), uint256.ParseDecimal("  00042  ".AsSpan()));
        Assert.AreEqual(uint256.MaxValue, uint256.ParseDecimal(MaxDecimal.AsSpan()));
        Assert.AreEqual(U(0), uint256.ParseDecimal("0".AsSpan()));

        Assert.IsTrue(uint256.TryParseDecimal("  12345  ".AsSpan(), out uint256 parsed));
        Assert.AreEqual(U(12345), parsed);
        Assert.IsTrue(uint256.TryParseDecimal(MaxDecimal.AsSpan(), out parsed));
        Assert.AreEqual(uint256.MaxValue, parsed);
    }

    [TestMethod]
    public void ParseDecimal_InvalidInputs_ThrowOrReturnFalseWithZero()
    {
        // A hex prefix is not accepted here, unlike in TryParse.
        string[] invalid = ["", "   ", "abc", "-1", "+1", "1.0", "1e3", "0x10", "0x", "12 34", TwoPow256Decimal, new string('9', 79)];
        foreach (string text in invalid)
        {
            Assert.ThrowsExactly<FormatException>(() => _ = uint256.ParseDecimal(text.AsSpan()), Shorten(text));
            Assert.IsFalse(uint256.TryParseDecimal(text.AsSpan(), out uint256 result), Shorten(text));
            Assert.AreEqual(uint256.Zero, result, Shorten(text));
        }
    }

    // ---- generic contract ----

    private static T ParseGeneric<T>(string text) where T : ISpanParsable<T> => T.Parse(text, null);

    private static bool TryParseGeneric<T>(string text, out T? result) where T : ISpanParsable<T> => T.TryParse(text, null, out result);

    [TestMethod]
    public void ISpanParsable_ThroughGenericConstraint()
    {
        Assert.AreEqual(U(42), ParseGeneric<uint256>("0x2a"));
        Assert.AreEqual(U(42), ParseGeneric<uint256>("42"));
        Assert.IsTrue(TryParseGeneric("0x2a", out uint256 parsed));
        Assert.AreEqual(U(42), parsed);
        Assert.IsFalse(TryParseGeneric("nope", out uint256 failed));
        Assert.AreEqual(uint256.Zero, failed);
    }

    private static string Shorten(string text) => text.Length <= 40 ? text : $"{text[..20]}...({text.Length} chars)";
}
