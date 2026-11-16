using System.Globalization;
using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Regression tests for defects found while reviewing <see cref="int256"/> and <see cref="uint256"/>:
/// the <c>(long)</c> range check, unknown format handling in <c>TryFormat</c>, and culture-dependent signs.
/// </summary>
[TestClass]
public sealed class Int256UInt256FormattingAndConversionRegressionTests
{
    // ---- int256 -> long range check ----

    [TestMethod]
    public void Int256_ExplicitToLong_Accepts_LongMinAndMax()
    {
        Assert.AreEqual(long.MaxValue, (long)new int256(long.MaxValue));
        Assert.AreEqual(long.MinValue, (long)new int256(long.MinValue));
    }

    [TestMethod]
    public void Int256_ExplicitToLong_PositiveAboveLongMax_Throws()
    {
        // 2^63: upper limbs are zero, but bit 63 of the low limb makes it > long.MaxValue.
        int256 value = new(0x8000_0000_0000_0000UL, 0, 0, 0);
        Assert.ThrowsExactly<OverflowException>(() => _ = (long)value);

        int256 longMaxPlusOne = (int256)((BigInteger)long.MaxValue + 1);
        Assert.ThrowsExactly<OverflowException>(() => _ = (long)longMaxPlusOne);
    }

    [TestMethod]
    public void Int256_ExplicitToLong_NegativeBelowLongMin_Throws()
    {
        // -(2^63) - 1: upper limbs are all ones, but bit 63 of the low limb is clear, so it is < long.MinValue.
        int256 value = new(0x7FFF_FFFF_FFFF_FFFFUL, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue);
        Assert.ThrowsExactly<OverflowException>(() => _ = (long)value);

        int256 longMinMinusOne = (int256)((BigInteger)long.MinValue - 1);
        Assert.ThrowsExactly<OverflowException>(() => _ = (long)longMinMinusOne);
    }

    // ---- TryFormat: unknown formats ----

    [TestMethod]
    [DataRow("G")]
    [DataRow("N")]
    [DataRow("x16")]
    [DataRow("0y")]
    [DataRow("xx")]
    [DataRow("0")]
    [DataRow("00")]
    [DataRow("0xx")]
    [DataRow("D2")]
    [DataRow("d ")]
    [DataRow("xX")]
    public void UInt256_TryFormat_UnknownFormat_ThrowsFormatException(string format)
    {
        uint256 value = new(42UL);
        Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new char[80].AsSpan(), out _, format));
        Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new byte[80].AsSpan(), out _, format));
        Assert.ThrowsExactly<FormatException>(() => value.ToString(format, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    [DataRow("G")]
    [DataRow("N")]
    [DataRow("x16")]
    [DataRow("0y")]
    [DataRow("xx")]
    [DataRow("0")]
    [DataRow("00")]
    [DataRow("0xx")]
    [DataRow("D2")]
    [DataRow("d ")]
    [DataRow("xX")]
    public void Int256_TryFormat_UnknownFormat_ThrowsFormatException(string format)
    {
        int256 value = new(42L);
        Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new char[80].AsSpan(), out _, format));
        Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new byte[80].AsSpan(), out _, format));
        Assert.ThrowsExactly<FormatException>(() => value.ToString(format, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void UInt256_Interpolation_WithUnknownFormat_ThrowsInsteadOfGrowingForever()
    {
        // DefaultInterpolatedStringHandler retries ISpanFormattable.TryFormat with a bigger buffer while it returns false.
        uint256 value = new(42UL);
        Assert.ThrowsExactly<FormatException>(() => _ = $"{value:G}");
    }

    [TestMethod]
    public void Int256_Interpolation_WithUnknownFormat_ThrowsInsteadOfGrowingForever()
    {
        int256 value = new(42L);
        Assert.ThrowsExactly<FormatException>(() => _ = $"{value:G}");
    }

    [TestMethod]
    public void TryFormat_KnownFormats_StillSucceed_AndTooSmallStillReturnsFalse()
    {
        uint256 u = new(255UL);
        int256 i = new(255L);
        Span<char> big = stackalloc char[80];

        foreach (string format in new[] { "x", "X", "0x", "0X", "d", "D" })
        {
            Assert.IsTrue(u.TryFormat(big, out int uw, format), $"uint256 '{format}'");
            Assert.IsTrue(uw > 0);
            Assert.IsTrue(i.TryFormat(big, out int iw, format), $"int256 '{format}'");
            Assert.IsTrue(iw > 0);
        }

        Span<char> tiny = stackalloc char[1];
        Assert.IsFalse(u.TryFormat(tiny, out int w1, "0x"));
        Assert.AreEqual(0, w1);
        Assert.IsFalse(i.TryFormat(tiny, out int w2, "0x"));
        Assert.AreEqual(0, w2);
    }

    // ---- int256 decimal formatting must not depend on the caller's culture ----

    [TestMethod]
    public void Int256_DecimalFormat_UsesAsciiMinusSign_RegardlessOfProvider()
    {
        // Some cultures use U+2212 or a multi-character negative sign; the UTF-8 path would truncate it to garbage,
        // and the output could never be parsed back.
        var exoticSign = new NumberFormatInfo { NegativeSign = "neg" };
        int256 value = new(-5L);

        Span<char> chars = stackalloc char[80];
        Assert.IsTrue(value.TryFormat(chars, out int written, "D", exoticSign));
        Assert.AreEqual("-5", new string(chars.Slice(0, written)));

        Span<byte> utf8 = stackalloc byte[80];
        Assert.IsTrue(value.TryFormat(utf8, out int bytesWritten, "D", exoticSign));
        Assert.AreEqual("-5", System.Text.Encoding.ASCII.GetString(utf8.Slice(0, bytesWritten)));

        Assert.AreEqual("-5", value.ToString("D", exoticSign));
    }

    [TestMethod]
    public void Int256_DecimalFormat_RoundTripsThroughParse_ForExtremes()
    {
        var exoticSign = new NumberFormatInfo { NegativeSign = "−" };
        foreach (int256 value in new[] { int256.MinValue, int256.MaxValue, int256.MinusOne, int256.Zero })
        {
            string text = value.ToString("D", exoticSign);
            Assert.AreEqual(value, int256.Parse(text), text);
        }
    }

    // ---- empty / null format selects the default ----

    [TestMethod]
    public void UInt256_ToString_NullAndEmptyFormat_UseDefaultHex_MatchingTryFormat()
    {
        uint256 value = new(255UL);
        Assert.AreEqual("0xff", value.ToString(null, CultureInfo.InvariantCulture));
        Assert.AreEqual("0xff", value.ToString("", CultureInfo.InvariantCulture));
        Assert.AreEqual("0xff", string.Format(CultureInfo.InvariantCulture, "{0:}", value));

        Span<char> buffer = stackalloc char[80];
        Assert.IsTrue(value.TryFormat(buffer, out int written, ""));
        Assert.AreEqual("0xff", new string(buffer.Slice(0, written)));
    }

    [TestMethod]
    public void Int256_ToString_NullAndEmptyFormat_UseDefaultDecimal_MatchingTryFormat()
    {
        int256 value = new(-255L);
        Assert.AreEqual("-255", value.ToString(null, CultureInfo.InvariantCulture));
        Assert.AreEqual("-255", value.ToString("", CultureInfo.InvariantCulture));

        Span<char> buffer = stackalloc char[80];
        Assert.IsTrue(value.TryFormat(buffer, out int written, ""));
        Assert.AreEqual("-255", new string(buffer.Slice(0, written)));
    }

    // ---- exact-size buffers: the maximum width must fit, one char less must return false (never throw or overrun) ----

    private static void AssertExactFit(Func<int, bool> tryFormatWithLength, int expectedLength, string context)
    {
        Assert.IsTrue(tryFormatWithLength(expectedLength), context + " exact");
        Assert.IsTrue(tryFormatWithLength(expectedLength + 1), context + " exact+1");
        Assert.IsFalse(tryFormatWithLength(expectedLength - 1), context + " exact-1");
        Assert.IsFalse(tryFormatWithLength(0), context + " empty");
    }

    [TestMethod]
    public void UInt256_TryFormat_ExactBufferBoundaries()
    {
        uint256 max = uint256.MaxValue;
        AssertExactFit(n => max.TryFormat(new char[n], out _, "D"), 78, "uint256 max decimal chars");
        AssertExactFit(n => max.TryFormat(new byte[n], out _, "D"), 78, "uint256 max decimal utf8");
        AssertExactFit(n => max.TryFormat(new char[n], out _, "0x"), 66, "uint256 max 0x chars");
        AssertExactFit(n => max.TryFormat(new byte[n], out _, "0x"), 66, "uint256 max 0x utf8");
        AssertExactFit(n => max.TryFormat(new char[n], out _, "x"), 64, "uint256 max x chars");
    }

    [TestMethod]
    public void Int256_TryFormat_ExactBufferBoundaries()
    {
        int256 min = int256.MinValue;
        AssertExactFit(n => min.TryFormat(new char[n], out _, "D"), 78, "int256 min decimal chars");
        AssertExactFit(n => min.TryFormat(new byte[n], out _, "D"), 78, "int256 min decimal utf8");
        AssertExactFit(n => min.TryFormat(new char[n], out _, "0x"), 66, "int256 min 0x chars");
        AssertExactFit(n => min.TryFormat(new byte[n], out _, "0x"), 66, "int256 min 0x utf8");
        AssertExactFit(n => min.TryFormat(new char[n], out _, "x"), 64, "int256 min x chars");
        AssertExactFit(n => int256.MaxValue.TryFormat(new char[n], out _, "0x"), 66, "int256 max 0x chars");
    }

    [TestMethod]
    public void TryFormat_ReportsCharsWrittenZero_WhenDestinationTooSmall()
    {
        Assert.IsFalse(uint256.MaxValue.TryFormat(new char[65], out int a, "0x"));
        Assert.AreEqual(0, a);
        Assert.IsFalse(int256.MinValue.TryFormat(new char[77], out int b, "D"));
        Assert.AreEqual(0, b);
    }

    // ---- prefix casing: always lowercase "0x"; "0X" only uppercases the digits ----

    [TestMethod]
    public void HexFormats_PrefixIsAlwaysLowercase_UpperFormatUppercasesDigitsOnly()
    {
        uint256 u = new(0xABCDEFUL);
        Assert.AreEqual("0xabcdef", u.ToString("0x", CultureInfo.InvariantCulture));
        Assert.AreEqual("0xABCDEF", u.ToString("0X", CultureInfo.InvariantCulture));
        Assert.AreEqual("ABCDEF", u.ToString("X", CultureInfo.InvariantCulture));
        Assert.AreEqual("0xabcdef", u.ToString());

        int256 positive = new(0xABCDEFL);
        Assert.AreEqual("0xabcdef", positive.ToString("0x", CultureInfo.InvariantCulture));
        Assert.AreEqual("0xABCDEF", positive.ToString("0X", CultureInfo.InvariantCulture));

        int256 negative = new(-0xABCDEFL);
        // -0xABCDEF = 2^256 - 0xABCDEF: 58 'F' digits then 543211, in 64-digit two's complement.
        Assert.AreEqual("0x" + new string('F', 58) + "543211", negative.ToString("0X", CultureInfo.InvariantCulture));
        Assert.AreEqual("0x" + new string('f', 58) + "543211", negative.ToString("0x", CultureInfo.InvariantCulture));

        Span<char> chars = stackalloc char[80];
        Assert.IsTrue(u.TryFormat(chars, out int written, "0X"));
        Assert.AreEqual("0xABCDEF", new string(chars.Slice(0, written)));
        Span<byte> utf8 = stackalloc byte[80];
        Assert.IsTrue(u.TryFormat(utf8, out int bytes, "0X"));
        Assert.AreEqual("0xABCDEF", System.Text.Encoding.ASCII.GetString(utf8.Slice(0, bytes)));
    }
}
