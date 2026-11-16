using System.Globalization;
using System.Numerics;
using System.Text;
using static EccentricWare.Web3.DataTypes.Tests.HexBigIntegerTestData;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="HexBigInteger"/> parsing from chars (<c>Parse</c>/<c>TryParse</c>), the char parse thresholds, and invalid-digit handling.</summary>
[TestClass]
public sealed class HexBigIntegerParsingTests
{
    /// <summary>Independent oracle: the leading "0" keeps BigInteger.Parse from reading a high-bit digit string as negative.</summary>
    private static BigInteger ReferenceValue(string hexDigits) => BigInteger.Parse("0" + hexDigits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private static int[] ProbePositions(int length)
    {
        var positions = new SortedSet<int> { 0, 1, length / 2, length - 2, length - 1 };
        for (int p = 0; p < length; p += 16)
            positions.Add(p);
        return [.. positions];
    }

    // ----------------- valid -----------------

    [TestMethod]
    public void Parse_MatchesBigInteger_ForSignBoundaryValues()
    {
        // BigInteger.Parse(.., HexNumber) reads "ff" as -1; the library must read it as 255.
        foreach (BigInteger value in InterestingSigned())
        {
            foreach (bool upper in new[] { false, true })
            {
                string text = ReferenceHex(value, upper);
                Assert.IsTrue(HexBigInteger.TryParse(text.AsSpan(), CultureInfo.InvariantCulture, out HexBigInteger parsed), text);
                Assert.AreEqual(value, parsed.Value, text);
            }
        }
    }

    [TestMethod]
    public void Parse_UnprefixedAndExplicitSign()
    {
        Assert.AreEqual(255, HexBigInteger.Parse("ff", null).Value);
        Assert.AreEqual(255, HexBigInteger.Parse("0XFF", null).Value);
        Assert.AreEqual(255, HexBigInteger.Parse("+0xff", null).Value);
        Assert.AreEqual(-255, HexBigInteger.Parse("-0xff", null).Value);
        Assert.AreEqual(-255, HexBigInteger.Parse("-ff", null).Value);
        Assert.AreEqual(0xFFFFFFFFFFFFFFFFUL, (ulong)HexBigInteger.Parse("0xffffffffffffffff", null).Value);
    }

    [TestMethod]
    public void Parse_TrimsWhitespace_AndAcceptsBarePrefixAsZero()
    {
        Assert.AreEqual(26, HexBigInteger.Parse("  0x1a\r\n", null).Value);
        Assert.AreEqual(-26, HexBigInteger.Parse(" - 0x1a ", null).Value);
        Assert.AreEqual(BigInteger.Zero, HexBigInteger.Parse("0x", null).Value);
        Assert.AreEqual(BigInteger.Zero, HexBigInteger.Parse("0x0", null).Value);
        Assert.AreEqual(BigInteger.Zero, HexBigInteger.Parse("-0x0", null).Value);
        Assert.IsFalse(HexBigInteger.Parse("-0x0", null).IsNegative);
    }

    [TestMethod]
    public void TryParse_StringOverload_Succeeds_AndSpanOverloadAgrees()
    {
        Assert.IsTrue(HexBigInteger.TryParse("0x1a", null, out HexBigInteger fromString));
        Assert.IsTrue(HexBigInteger.TryParse("0x1a".AsSpan(), null, out HexBigInteger fromSpan));
        Assert.AreEqual(26, fromString.Value);
        Assert.AreEqual(fromString, fromSpan);
        Assert.IsTrue(HexBigInteger.TryParse("0x", null, out HexBigInteger zero));
        Assert.AreEqual(HexBigInteger.Zero, zero);
    }

    [TestMethod]
    public void Parse_VeryLongValue_RoundTripsWithoutTruncation()
    {
        BigInteger value = BigInteger.Pow(2, 40000) - 3; // 10,000 hex digits: past the stackalloc thresholds
        string text = ReferenceHex(value);
        Assert.AreEqual(value, HexBigInteger.Parse(text, null).Value);
        Assert.AreEqual(-value, HexBigInteger.Parse("-" + text, null).Value);
    }

    [TestMethod]
    public void Parse_LeadingZeros_AroundTheSingleLimbBoundary()
    {
        // 16 hex digits is the single-limb fast path; 17 takes the byte-decode path. Both must agree, with and without sign.
        foreach (int digits in new[] { 15, 16, 17, 18, 40 })
        {
            string zeros = new('0', digits - 1);
            string text = "0x" + zeros + "f";
            Assert.AreEqual(15, HexBigInteger.Parse(text, null).Value, text);
            Assert.AreEqual(-15, HexBigInteger.Parse("-" + text, null).Value, text);
            Assert.AreEqual(BigInteger.Zero, HexBigInteger.Parse("0x" + new string('0', digits), null).Value);
            Assert.IsFalse(HexBigInteger.TryParse("0x" + zeros + "g", null, out _));
        }
    }

    // ----------------- invalid -----------------

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("-")]
    [DataRow("+")]
    [DataRow(" - ")]
    [DataRow("0xg")]
    [DataRow("0x1 2")]
    [DataRow("--0x1")]
    [DataRow("-+0x1")]
    [DataRow("0x-1")]
    [DataRow("0x+1")]
    [DataRow("0x0x1")]
    [DataRow("x1")]
    [DataRow("1.5")]
    [DataRow("١")] // Arabic-Indic digit one: char.IsDigit but not hex
    [DataRow("0x１")] // fullwidth digit one
    [DataRow(" 1")]  // NBSP is not ASCII whitespace
    public void TryParse_RejectsInvalid_WithADefaultResult(string text)
    {
        Assert.IsFalse(HexBigInteger.TryParse(text.AsSpan(), null, out HexBigInteger result), text);
        Assert.AreEqual(default, result);
        Assert.IsFalse(HexBigInteger.TryParse(text, null, out result), text);
        Assert.AreEqual(default, result);
        Assert.ThrowsExactly<FormatException>(() => HexBigInteger.Parse(text, null));
        Assert.ThrowsExactly<FormatException>(() => HexBigInteger.Parse(text.AsSpan(), null));
    }

    [TestMethod]
    public void Parse_NullString_ThrowsArgumentNull_NotFormat()
    {
        ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(() => HexBigInteger.Parse((string)null!, null));
        Assert.AreEqual("s", error.ParamName);
        Assert.IsFalse(HexBigInteger.TryParse((string?)null, null, out HexBigInteger result));
        Assert.AreEqual(default, result);
        Assert.ThrowsExactly<FormatException>(() => HexBigInteger.Parse("zz", null));
    }

    // ----------------- thresholds and invalid digits beyond one limb -----------------

    [TestMethod]
    public void Parse_AroundTheStackAndPoolThreshold_MatchesTheReference()
    {
        // The char parser decodes onto a 128-byte stack buffer up to 256 digits and rents beyond that.
        for (int digits = 252; digits <= 260; digits++)
        {
            foreach (string text in new[] { "1" + new string('f', digits - 1), "8" + new string('0', digits - 1), "a" + new string('5', digits - 1) })
            {
                BigInteger expected = ReferenceValue(text);
                foreach (bool upper in new[] { false, true })
                {
                    string digitsText = upper ? text.ToUpperInvariant() : text;
                    Assert.AreEqual(expected, HexBigInteger.Parse("0x" + digitsText, null).Value, $"digits={digits} '{text[..2]}'");
                    Assert.AreEqual(expected, HexBigInteger.Parse(digitsText, null).Value, $"digits={digits} bare");
                    Assert.AreEqual(-expected, HexBigInteger.Parse("-0x" + digitsText, null).Value, $"digits={digits} negative");
                    Assert.AreEqual(expected, HexBigInteger.ParseUtf8(Encoding.ASCII.GetBytes("0x" + digitsText)).Value, $"digits={digits} utf8");
                    Assert.AreEqual(-expected, HexBigInteger.ParseUtf8(Encoding.ASCII.GetBytes("-" + digitsText)).Value, $"digits={digits} utf8 negative");
                }
            }
        }
    }

    [TestMethod]
    [DataRow(17)]
    [DataRow(18)]
    [DataRow(31)]
    [DataRow(32)]
    [DataRow(33)]
    [DataRow(64)]
    [DataRow(255)]
    [DataRow(256)]
    [DataRow(257)]
    [DataRow(258)]
    public void TryParse_Chars_RejectsAnInvalidDigit_BeyondTheSingleLimbPath(int digitCount)
    {
        string valid = new('a', digitCount);
        foreach (string prefix in new[] { "", "0x", "-0x" })
        {
            foreach (int position in ProbePositions(digitCount))
            {
                foreach (char bad in new[] { 'g', 'G', '/', ':', '@', '`', '١', '１', '\0' })
                {
                    string text = prefix + valid[..position] + bad + valid[(position + 1)..];
                    Assert.IsFalse(HexBigInteger.TryParse(text.AsSpan(), null, out HexBigInteger result), $"n={digitCount} pos={position} bad=U+{(int)bad:X4} prefix='{prefix}'");
                    Assert.AreEqual(default, result);
                }
            }
        }
    }

    [TestMethod]
    public void TryParse_Chars_AcceptsALargeValueOnlyUnbounded()
    {
        // Documented difference from TryParseUtf8: the char parser has no digit bound.
        string text = "0x" + new string('f', 5000);
        Assert.IsTrue(HexBigInteger.TryParse(text.AsSpan(), null, out HexBigInteger parsed));
        Assert.AreEqual(BigInteger.Pow(2, 20000) - 1, parsed.Value);
        Assert.IsFalse(HexBigInteger.TryParseUtf8(Encoding.ASCII.GetBytes(text), out _));
    }
}
