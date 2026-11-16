using System.Numerics;
using System.Text;
using static EccentricWare.Web3.DataTypes.Tests.Int256TestValues;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>UTF-8 parsing of <see cref="int256"/> (the JSON token path): tables shared with the char tests, quotes, whitespace, size limits.</summary>
[TestClass]
public sealed class Int256Utf8ParseTests
{
    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static bool Parse(byte[] utf8, out int256 value) => int256.TryParse(utf8, out value);

    private static bool ParseUnquoted(byte[] utf8, out int256 value) => int256.TryParseUnquoted(utf8, out value);

    private static void AssertValid(string text, BigInteger expected)
    {
        int256 want = FromBig(expected);
        Assert.IsTrue(Parse(Bytes(text), out int256 viaPublic), $"TryParse '{text}'");
        Assert.AreEqual(want, viaPublic);
        Assert.IsTrue(ParseUnquoted(Bytes(text), out int256 viaUnquoted), $"TryParseUnquoted '{text}'");
        Assert.AreEqual(want, viaUnquoted);
        Assert.IsTrue(Parse(Bytes("\"" + text.Trim('\t', '\r', '\n', ' ') + "\""), out int256 viaQuoted), $"quoted '{text}'");
        Assert.AreEqual(want, viaQuoted);
    }

    private static void AssertInvalid(string text)
    {
        Assert.IsFalse(Parse(Bytes(text), out int256 viaPublic), $"TryParse '{text}'");
        Assert.AreEqual(int256.Zero, viaPublic);
        Assert.IsFalse(ParseUnquoted(Bytes(text), out int256 viaUnquoted), $"TryParseUnquoted '{text}'");
        Assert.AreEqual(int256.Zero, viaUnquoted);
    }

    [TestMethod]
    [DynamicData(nameof(Int256ParseTests.ValidDecimal), typeof(Int256ParseTests))]
    public void Decimal_Valid_ParsesFromUtf8(string text, BigInteger expected) => AssertValid(text, expected);

    [TestMethod]
    [DynamicData(nameof(Int256ParseTests.ValidHex), typeof(Int256ParseTests))]
    public void Hex_Valid_ParsesFromUtf8(string text, BigInteger expected) => AssertValid(text, expected);

    [TestMethod]
    [DynamicData(nameof(Int256ParseTests.InvalidDecimal), typeof(Int256ParseTests))]
    public void Decimal_Invalid_IsRejectedFromUtf8(string text) => AssertInvalid(text);

    [TestMethod]
    [DynamicData(nameof(Int256ParseTests.InvalidHex), typeof(Int256ParseTests))]
    public void Hex_Invalid_IsRejectedFromUtf8(string text) => AssertInvalid(text);

    // ---- branches that only the UTF-8 parser has ----

    [TestMethod]
    public void Utf8_ZeroSpellings_ParseToZero()
    {
        foreach (string text in new[] { "0x", "0X", "-0", "+0", "-0x0", "0x0", "-0x" + new string('0', 64) })
        {
            Assert.IsTrue(Parse(Bytes(text), out int256 value), text);
            Assert.AreEqual(int256.Zero, value, text);
        }
    }

    [TestMethod]
    public void Utf8_NegativeHexAndDecimal_RejectMagnitudesAboveTwoPow255()
    {
        Assert.IsFalse(Parse(Bytes("-0x" + new string('f', 64)), out int256 hex));
        Assert.AreEqual(int256.Zero, hex);
        Assert.IsFalse(Parse(Bytes("-" + (Two255 + 1)), out int256 dec));
        Assert.AreEqual(int256.Zero, dec);
        Assert.IsFalse(Parse(Bytes(Two255.ToString()), out int256 pos));
        Assert.AreEqual(int256.Zero, pos);
    }

    [TestMethod]
    public void TryParse_Utf8_Unquoted_And_Quoted()
    {
        Assert.IsTrue(int256.TryParse(Bytes("123"), out var a));
        Assert.AreEqual(new int256(123), a);
        Assert.IsTrue(int256.TryParse(Bytes("\"123\""), out var b));
        Assert.AreEqual(new int256(123), b);
        Assert.IsTrue(int256.TryParse(Bytes("  \"-0x01\"  "), out var c));
        Assert.AreEqual(int256.MinusOne, c);
    }

    [TestMethod]
    public void TryParse_Utf8_OutOfRange_ReturnsFalse()
    {
        Assert.IsFalse(int256.TryParse(Bytes(Two255.ToString()), out int256 r));
        Assert.AreEqual(int256.Zero, r);
    }

    // ---- quotes ----

    [TestMethod]
    [DataRow("\"\"")]
    [DataRow("\"")]
    [DataRow("\"\"12\"\"")]
    [DataRow("\"12")]
    [DataRow("12\"")]
    [DataRow("'12'")]
    [DataRow("\"1\"2\"")]
    [DataRow("\"\"\"")]
    public void MalformedQuoting_IsRejected(string text)
    {
        Assert.IsFalse(Parse(Bytes(text), out int256 value));
        Assert.AreEqual(int256.Zero, value);
    }

    [TestMethod]
    public void TryParseUnquoted_RejectsQuotes_ThatTryParseStrips()
    {
        Assert.IsTrue(Parse(Bytes("\"12\""), out _));
        Assert.IsFalse(ParseUnquoted(Bytes("\"12\""), out int256 value));
        Assert.AreEqual(int256.Zero, value);
    }

    [TestMethod]
    public void QuotedHexAndZeroPrefix_Parse()
    {
        Assert.IsTrue(Parse(Bytes("\"0x\""), out int256 zero));
        Assert.AreEqual(int256.Zero, zero);
        Assert.IsTrue(Parse(Bytes("\"0x" + new string('f', 64) + "\""), out int256 minusOne));
        Assert.AreEqual(int256.MinusOne, minusOne);
    }

    // ---- whitespace: UTF-8 trims only space, tab, CR, LF (documented in ByteUtils) ----

    [TestMethod]
    public void Utf8_TrimsAsciiWhitespaceOnly()
    {
        foreach (string text in new[] { " 5", "5 ", "\t5\r\n", "\r\n 5 \t" })
            Assert.IsTrue(Parse(Bytes(text), out int256 v) && v == new int256(5), text);

        foreach (byte[] rejected in new[]
        {
            new byte[] { 0x0C, (byte)'5' },                   // form feed
            new byte[] { 0x0B, (byte)'5' },                   // vertical tab
            new byte[] { (byte)'5', 0x0C },
            new byte[] { 0xC2, 0xA0, (byte)'5' },             // NBSP
            new byte[] { (byte)'5', 0xC2, 0xA0 },
            new byte[] { 0xE3, 0x80, 0x80, (byte)'5' },       // ideographic space
        })
        {
            Assert.IsFalse(Parse(rejected, out int256 v), Convert.ToHexString(rejected));
            Assert.AreEqual(int256.Zero, v);
        }
    }

    [TestMethod]
    public void WhitespaceTrimming_DiffersBetweenCharAndUtf8Paths_ForNonAsciiSpace()
    {
        // Documents current behaviour: the UTF-16 overload trims Unicode whitespace, the UTF-8 one only ASCII whitespace.
        Assert.IsTrue(int256.TryParse("\f5 ".AsSpan(), null, out int256 viaChars));
        Assert.AreEqual(new int256(5), viaChars);
        Assert.IsFalse(Parse(Bytes("\f5 "), out _));

        // The ASCII four are trimmed identically by both paths.
        foreach (string ws in new[] { " ", "\t", "\r", "\n" })
        {
            Assert.IsTrue(int256.TryParse((ws + "9" + ws).AsSpan(), null, out int256 c));
            Assert.IsTrue(Parse(Bytes(ws + "9" + ws), out int256 u));
            Assert.AreEqual(c, u);
        }
    }

    // ---- size limits ----

    [TestMethod]
    public void VeryLongInputs_AreHandledWithoutExceptions()
    {
        Assert.IsFalse(Parse(Encoding.ASCII.GetBytes(new string('1', 1 << 20)), out int256 tooLong));
        Assert.AreEqual(int256.Zero, tooLong);
        Assert.IsFalse(int256.TryParse(new string('1', 1 << 20).AsSpan(), null, out _));

        string manyZeros = new string('0', 10_000) + "5";
        Assert.IsTrue(Parse(Bytes(manyZeros), out int256 five));
        Assert.AreEqual(new int256(5), five);
        Assert.IsTrue(int256.TryParse(manyZeros.AsSpan(), null, out five));
        Assert.AreEqual(new int256(5), five);
    }

    [TestMethod]
    public void EmptyAndWhitespaceOnly_AreRejected()
    {
        Assert.IsFalse(Parse([], out int256 empty));
        Assert.AreEqual(int256.Zero, empty);
        Assert.IsFalse(Parse(Bytes("   \t\r\n"), out _));
        Assert.IsFalse(ParseUnquoted([], out _));
    }
}
