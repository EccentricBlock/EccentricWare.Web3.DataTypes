using System.Globalization;
using System.Numerics;
using System.Text;
using static EccentricWare.Web3.DataTypes.Tests.HexBigIntegerTestData;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// <see cref="HexBigInteger"/> UTF-8 parsing: <c>TryParseUtf8</c>/<c>ParseUtf8</c> grammar, the digit bound,
/// the internal <c>TryParseUnquoted</c>, and parity with the char parser.
/// </summary>
[TestClass]
public sealed class HexBigIntegerUtf8ParsingTests
{
    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    // ----------------- grammar -----------------

    [TestMethod]
    public void TryParseUtf8_MatchesBigInteger_QuotedAndUnquoted()
    {
        foreach (BigInteger value in InterestingSigned())
        {
            string text = ReferenceHex(value);
            Assert.IsTrue(HexBigInteger.TryParseUtf8(Ascii(text), out HexBigInteger bare), text);
            Assert.IsTrue(HexBigInteger.TryParseUtf8(Ascii('"' + text + '"'), out HexBigInteger quoted), text);
            Assert.AreEqual(value, bare.Value, text);
            Assert.AreEqual(value, quoted.Value, text);
        }
    }

    [TestMethod]
    public void TryParseUtf8_NullLiteral_IsZero_AndWhitespaceTolerated()
    {
        Assert.IsTrue(HexBigInteger.TryParseUtf8("null"u8, out HexBigInteger result));
        Assert.AreEqual(HexBigInteger.Zero, result);
        Assert.IsTrue(HexBigInteger.TryParseUtf8(" null\n"u8, out result));
        Assert.AreEqual(HexBigInteger.Zero, result);
        Assert.IsTrue(HexBigInteger.TryParseUtf8("null"u8, out result, maxHexDigits: 0));
        Assert.AreEqual(HexBigInteger.Zero, result);
        Assert.IsTrue(HexBigInteger.TryParseUtf8(" \"0x1a\" "u8, out result));
        Assert.AreEqual(26, result.Value);
        Assert.IsTrue(HexBigInteger.TryParseUtf8("-0x1a"u8, out result));
        Assert.AreEqual(-26, result.Value);
        Assert.IsTrue(HexBigInteger.TryParseUtf8("0x"u8, out result));
        Assert.AreEqual(HexBigInteger.Zero, result);
    }

    [TestMethod]
    [DataRow("+0x1", 1)]
    [DataRow("\"+0x1\"", 1)]
    [DataRow("- 0x1", -1)]
    [DataRow("\"  0x1a  \"", 26)]        // whitespace inside the quotes is trimmed (unlike HexBytes)
    [DataRow("0XFF", 255)]
    [DataRow("\t0x1a\r\n", 26)]
    [DataRow("-0x", 0)]
    [DataRow("+0x", 0)]
    [DataRow("\"-0x\"", 0)]
    public void TryParseUtf8_AcceptsTheDocumentedGrammar(string text, int expected)
    {
        Assert.IsTrue(HexBigInteger.TryParseUtf8(Ascii(text), out HexBigInteger result), text);
        Assert.AreEqual(expected, result.Value, text);
        Assert.AreEqual(expected, HexBigInteger.ParseUtf8(Ascii(text)).Value, text);
    }

    [TestMethod]
    public void TryParseUtf8_SignedBareZero_IsNotNegative()
    {
        Assert.IsTrue(HexBigInteger.TryParseUtf8("-0x"u8, out HexBigInteger result));
        Assert.IsFalse(result.IsNegative);
        Assert.IsTrue(HexBigInteger.TryParseUtf8("-0x0"u8, out result));
        Assert.IsFalse(result.IsNegative);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("\"\"")]
    [DataRow("   ")]
    [DataRow("\"  \"")]
    [DataRow("-")]
    [DataRow("\"-\"")]
    [DataRow("+")]
    [DataRow("\"nul\"")]
    [DataRow("\"null\"")]
    [DataRow("NULL")]
    [DataRow("Null")]
    [DataRow("nullx")]
    [DataRow("nul")]
    [DataRow(" nul")]
    [DataRow("0xg")]
    [DataRow("\"0x1")]
    [DataRow("0x1\"")]
    [DataRow("0x-1")]
    [DataRow("0x+1")]
    [DataRow("0x 1")]
    [DataRow("--0x1")]
    [DataRow("0x1 2")]
    [DataRow("é")]
    [DataRow(" 0x1")]      // NBSP bytes (C2 A0) before the token
    [DataRow("0x1 ")]
    public void TryParseUtf8_RejectsInvalid_WithADefaultResult_AndParseUtf8Throws(string text)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text);

        Assert.IsFalse(HexBigInteger.TryParseUtf8(utf8, out HexBigInteger result), text);
        Assert.AreEqual(default, result);
        FormatException error = Assert.ThrowsExactly<FormatException>(() => HexBigInteger.ParseUtf8(utf8));
        Assert.AreEqual("Invalid hex quantity token.", error.Message);
    }

    [TestMethod]
    [DataRow(17)]
    [DataRow(18)]
    [DataRow(32)]
    [DataRow(33)]
    [DataRow(64)]
    [DataRow(255)]
    [DataRow(256)]
    [DataRow(257)]
    public void TryParseUtf8_RejectsAnInvalidByte_BeyondTheSingleLimbPath(int digitCount)
    {
        byte[] valid = Ascii(new string('a', digitCount));
        var positions = new SortedSet<int> { 0, 1, digitCount / 2, digitCount - 2, digitCount - 1 };
        for (int p = 0; p < digitCount; p += 16)
            positions.Add(p);

        foreach (string prefix in new[] { "", "0x", "-0x" })
        {
            byte[] prefixBytes = Ascii(prefix);
            foreach (int position in positions)
            {
                foreach (byte bad in new byte[] { 0x00, 0x80, 0xC3, 0xFF, (byte)'g', (byte)'G', (byte)'/', (byte)':', (byte)'@', (byte)'`' })
                {
                    byte[] text = [.. prefixBytes, .. valid[..position], bad, .. valid[(position + 1)..]];
                    Assert.IsFalse(HexBigInteger.TryParseUtf8(text, out HexBigInteger result), $"n={digitCount} pos={position} bad=0x{bad:X2} prefix='{prefix}'");
                    Assert.AreEqual(default, result);
                }
            }
        }
    }

    // ----------------- digit bound -----------------

    [TestMethod]
    public void TryParseUtf8_EnforcesTheDefaultBound_AtTheBoundary()
    {
        string atLimit = "0x" + new string('f', 4096);
        string overLimit = "0x" + new string('f', 4097);

        Assert.IsTrue(HexBigInteger.TryParseUtf8(Ascii(atLimit), out HexBigInteger parsed));
        Assert.AreEqual(BigInteger.Pow(2, 4 * 4096) - 1, parsed.Value);
        Assert.IsFalse(HexBigInteger.TryParseUtf8(Ascii(overLimit), out _));
        Assert.IsTrue(HexBigInteger.TryParseUtf8(Ascii("0x" + new string('f', HexBigInteger.DefaultMaxHexDigits)), out _));
        Assert.IsFalse(HexBigInteger.TryParseUtf8(Ascii("0x" + new string('f', HexBigInteger.DefaultMaxHexDigits + 1)), out _));
    }

    [TestMethod]
    public void TryParseUtf8_CustomBound_AppliesToTheSingleLimbAndTheWidePath()
    {
        Assert.IsTrue(HexBigInteger.TryParseUtf8("0xfff"u8, out _, maxHexDigits: 3));
        Assert.IsFalse(HexBigInteger.TryParseUtf8("0xffff"u8, out _, maxHexDigits: 3));
        Assert.IsFalse(HexBigInteger.TryParseUtf8("0x1"u8, out _, maxHexDigits: 0));
        Assert.IsTrue(HexBigInteger.TryParseUtf8("0x"u8, out _, maxHexDigits: 0));
        Assert.IsTrue(HexBigInteger.TryParseUtf8("0xf"u8, out _, maxHexDigits: 1));
        Assert.IsFalse(HexBigInteger.TryParseUtf8("0xff"u8, out _, maxHexDigits: 1));

        // Wide path (more than 16 digits).
        string seventeenF = "0x" + new string('f', 17);
        Assert.IsFalse(HexBigInteger.TryParseUtf8(Ascii(seventeenF), out _, maxHexDigits: 16));
        Assert.IsTrue(HexBigInteger.TryParseUtf8(Ascii(seventeenF), out HexBigInteger value, maxHexDigits: 17));
        Assert.AreEqual(BigInteger.Pow(2, 68) - 1, value.Value);
        Assert.IsTrue(HexBigInteger.TryParseUtf8(Ascii(seventeenF), out _, maxHexDigits: int.MaxValue));
    }

    [TestMethod]
    public void TryParseUtf8_DigitBound_CountsLeadingZeros_ButNotSignOrPrefix()
    {
        string seventeenWithZeros = "0x" + new string('0', 16) + "1";
        Assert.IsFalse(HexBigInteger.TryParseUtf8(Ascii(seventeenWithZeros), out _, maxHexDigits: 16));
        Assert.IsTrue(HexBigInteger.TryParseUtf8(Ascii(seventeenWithZeros), out HexBigInteger one, maxHexDigits: 17));
        Assert.AreEqual(1, one.Value);

        // Fast path too.
        Assert.IsFalse(HexBigInteger.TryParseUtf8("0x0000000000000001"u8, out _, maxHexDigits: 15));
        Assert.IsTrue(HexBigInteger.TryParseUtf8("0x0000000000000001"u8, out _, maxHexDigits: 16));

        // Neither the sign nor the prefix is counted.
        Assert.IsTrue(HexBigInteger.TryParseUtf8(Ascii("-0x" + new string('f', 16)), out _, maxHexDigits: 16));
        Assert.IsTrue(HexBigInteger.TryParseUtf8(Ascii("\"-0x" + new string('f', 20) + "\""), out _, maxHexDigits: 20));
    }

    [TestMethod]
    public void TryParseUtf8_NegativeMaxHexDigits_ThrowsForShortAndLongInputsAlike()
    {
        // A negative bound used to be compared as unsigned, which silently meant "no limit": a caller's arithmetic slip
        // disabled the DoS bound. It is now rejected as an invalid argument, on the short and the long path.
        foreach (int negative in new[] { -1, int.MinValue })
        {
            ArgumentOutOfRangeException shortError = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => HexBigInteger.TryParseUtf8("0x1"u8, out _, maxHexDigits: negative));
            ArgumentOutOfRangeException longError = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => HexBigInteger.TryParseUtf8("0xffffffffffffffffff"u8, out _, maxHexDigits: negative));
            Assert.AreEqual("maxHexDigits", shortError.ParamName);
            Assert.AreEqual("maxHexDigits", longError.ParamName);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HexBigInteger.ParseUtf8("0x1"u8, negative));
        }

        // Zero is a legal bound: nothing but an empty digit sequence fits.
        Assert.IsFalse(HexBigInteger.TryParseUtf8("0x1"u8, out _, maxHexDigits: 0));
        Assert.IsTrue(HexBigInteger.TryParseUtf8("0x"u8, out HexBigInteger zero, maxHexDigits: 0));
        Assert.AreEqual(0, zero.Value);
    }

    // ----------------- internal TryParseUnquoted -----------------

    [TestMethod]
    [DataRow("0x1a", true, 26)]
    [DataRow("-0x1a", true, -26)]
    [DataRow("0x", true, 0)]
    [DataRow("  0x1  ", true, 1)]
    [DataRow("1a", true, 26)]
    [DataRow("", false, 0)]
    [DataRow("-", false, 0)]
    [DataRow("\"0x1\"", false, 0)]        // a quote is content, not framing
    [DataRow("null", false, 0)]
    [DataRow("0xzz", false, 0)]
    public void TryParseUnquoted_Table(string text, bool expectedOk, int expected)
    {
        Assert.AreEqual(expectedOk, HexBigInteger.TryParseUnquoted(Ascii(text), out HexBigInteger result), text);
        Assert.AreEqual(expected, result.Value, text);
    }

    [TestMethod]
    public void TryParseUnquoted_AppliesTheDefaultDigitBound()
    {
        Assert.IsTrue(HexBigInteger.TryParseUnquoted(Ascii("0x" + new string('f', HexBigInteger.DefaultMaxHexDigits)), out _));
        Assert.IsFalse(HexBigInteger.TryParseUnquoted(Ascii("0x" + new string('f', HexBigInteger.DefaultMaxHexDigits + 1)), out HexBigInteger result));
        Assert.AreEqual(default, result);
    }

    // ----------------- parity with the char parser -----------------

    [TestMethod]
    public void CharAndUtf8Parsers_Agree_OnRandomValidValues()
    {
        var random = new Random(1234);
        for (int i = 0; i < 500; i++)
        {
            BigInteger value = RandomMagnitude(random, random.Next(1, 80));
            if (random.Next(2) == 0)
                value = -value;
            string text = ReferenceHex(value, upper: random.Next(2) == 0);

            Assert.AreEqual(value, HexBigInteger.Parse(text, null).Value, text);
            Assert.AreEqual(value, HexBigInteger.ParseUtf8(Ascii(text)).Value, text);
        }
    }

    [TestMethod]
    public void CharAndUtf8Parsers_Agree_OnRandomUnquotedText_ValidAndInvalid()
    {
        var random = new Random(4321);
        const string alphabet = "0123456789abcdefABCDEFxX+- \t";

        for (int round = 0; round < 4000; round++)
        {
            var text = new StringBuilder();
            int length = random.Next(0, 41);
            for (int i = 0; i < length; i++)
                text.Append(alphabet[random.Next(random.Next(3) == 0 ? alphabet.Length : 22)]);

            string s = text.ToString();
            bool charsOk = HexBigInteger.TryParse(s.AsSpan(), null, out HexBigInteger fromChars);
            bool utf8Ok = HexBigInteger.TryParseUtf8(Ascii(s), out HexBigInteger fromUtf8);

            Assert.AreEqual(charsOk, utf8Ok, $"'{s}'");
            Assert.AreEqual(fromChars, fromUtf8, $"'{s}'");
            if (!charsOk)
                Assert.AreEqual(default, fromChars, $"'{s}'");
        }
    }

    [TestMethod]
    public void DocumentedDifferences_BetweenTheCharAndUtf8Parsers()
    {
        // Quotes and the "null" token exist only in the UTF-8 (JSON-RPC) parser.
        Assert.IsFalse(HexBigInteger.TryParse("\"0x1\"".AsSpan(), null, out _));
        Assert.IsTrue(HexBigInteger.TryParseUtf8("\"0x1\""u8, out _));
        Assert.IsFalse(HexBigInteger.TryParse("null".AsSpan(), null, out _));
        Assert.IsTrue(HexBigInteger.TryParseUtf8("null"u8, out HexBigInteger zero));
        Assert.AreEqual(HexBigInteger.Zero, zero);
    }
}
