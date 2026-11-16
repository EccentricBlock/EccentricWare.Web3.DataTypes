using System.Globalization;
using System.Numerics;
using System.Text;
using static EccentricWare.Web3.DataTypes.Tests.Int256TestValues;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Text formatting of <see cref="int256"/>: ToString, TryFormat (UTF-16 and UTF-8), interpolation and System.Text.Unicode.Utf8.TryWrite.</summary>
[TestClass]
public sealed class Int256FormattingTests
{
    private static readonly string HexMinValue64 = "8" + new string('0', 63);
    private static readonly string HexMinusOne64 = new('f', 64);

    private static string Chars(int256 v, string format)
    {
        Span<char> buffer = stackalloc char[80];
        Assert.IsTrue(v.TryFormat(buffer, out int written, format));
        return new string(buffer[..written]);
    }

    private static string Utf8Text(int256 v, string format)
    {
        Span<byte> buffer = stackalloc byte[80];
        Assert.IsTrue(v.TryFormat(buffer, out int written, format));
        return Encoding.ASCII.GetString(buffer[..written]);
    }

    // ---- ToString ----

    [TestMethod]
    public void ToString_Default_IsDecimalInvariant()
    {
        Assert.AreEqual("0", int256.Zero.ToString());
        Assert.AreEqual("1", int256.One.ToString());
        Assert.AreEqual("-1", int256.MinusOne.ToString());
        Assert.AreEqual((Two255 - 1).ToString(CultureInfo.InvariantCulture), int256.MaxValue.ToString());
        Assert.AreEqual((-Two255).ToString(CultureInfo.InvariantCulture), int256.MinValue.ToString());
    }

    [TestMethod]
    public void ToString_D_Format_UsesSignedDecimal()
    {
        var v = FromBig(-1234567890123456789L);
        Assert.AreEqual("-1234567890123456789", v.ToString("D", CultureInfo.InvariantCulture));
        Assert.AreEqual("-1234567890123456789", v.ToString("d", CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void ToString_Decimal_MatchesBigInteger_ForBoundaryValues()
    {
        foreach (BigInteger v in BoundaryValues)
            Assert.AreEqual(v.ToString(CultureInfo.InvariantCulture), FromBig(v).ToString(), $"{v}");
    }

    [TestMethod]
    public void ToString_Hex_Positive_Minimal_NoPrefix()
    {
        Assert.AreEqual("0", int256.Zero.ToString("x", CultureInfo.InvariantCulture));
        Assert.AreEqual("1", int256.One.ToString("x", CultureInfo.InvariantCulture));
        Assert.AreEqual("10000000000000000", FromBig(Pow2(64)).ToString("x", CultureInfo.InvariantCulture));
        Assert.AreEqual("7" + new string('f', 63), int256.MaxValue.ToString("x", CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void ToString_Hex_Negative_IsFullWidthTwosComplement()
    {
        Assert.AreEqual(HexMinusOne64, int256.MinusOne.ToString("x", CultureInfo.InvariantCulture));
        Assert.AreEqual(HexMinusOne64.ToUpperInvariant(), int256.MinusOne.ToString("X", CultureInfo.InvariantCulture));
        Assert.AreEqual(HexMinValue64, int256.MinValue.ToString("x", CultureInfo.InvariantCulture));
        Assert.AreEqual(new string('f', 62) + "01", FromBig(-255).ToString("x", CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void ToString_Hex_WithPrefix()
    {
        Assert.AreEqual("0x0", int256.Zero.ToString("0x", CultureInfo.InvariantCulture));
        Assert.AreEqual("0x1", int256.One.ToString("0x", CultureInfo.InvariantCulture));
        Assert.AreEqual("0x" + HexMinusOne64, int256.MinusOne.ToString("0x", CultureInfo.InvariantCulture));
        Assert.AreEqual("0x" + HexMinusOne64.ToUpperInvariant(), int256.MinusOne.ToString("0X", CultureInfo.InvariantCulture));
        Assert.AreEqual("0x7" + new string('f', 63), int256.MaxValue.ToString("0x", CultureInfo.InvariantCulture));
        Assert.AreEqual("0x10000000000000000", FromBig(Pow2(64)).ToString("0x", CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void ToString_Hex_RoundTripsThroughParse_ForBoundaryValues()
    {
        foreach (BigInteger v in BoundaryValues)
        {
            int256 x = FromBig(v);
            Assert.AreEqual(x, int256.Parse(x.ToString("0x", CultureInfo.InvariantCulture)), $"{v}");
            Assert.AreEqual(x, int256.Parse(x.ToString()), $"{v}");
        }
    }

    [TestMethod]
    public void ToString_InvalidFormat_ThrowsFormatException()
        => Assert.ThrowsExactly<FormatException>(() => _ = int256.One.ToString("NOPE", CultureInfo.InvariantCulture));

    [TestMethod]
    public void Format_Output_DoesNotDependOnTheProvider_ForEveryFormat()
    {
        IFormatProvider[] providers =
        [
            CultureInfo.InvariantCulture, new CultureInfo("ar-SA"), new CultureInfo("fa-IR"),
            new NumberFormatInfo { NegativeSign = "neg" }, new NumberFormatInfo { NegativeSign = "−" },
        ];

        Span<char> chars = stackalloc char[80];
        foreach (string format in new[] { "D", "x", "X", "0x", "0X" })
        {
            string expected = FromBig(-12345).ToString(format, CultureInfo.InvariantCulture);
            foreach (IFormatProvider provider in providers)
            {
                Assert.AreEqual(expected, FromBig(-12345).ToString(format, provider), format);
                Assert.IsTrue(FromBig(-12345).TryFormat(chars, out int n, format, provider));
                Assert.AreEqual(expected, new string(chars[..n]), format);
            }
        }
    }

    // ---- TryFormat ----

    [TestMethod]
    public void TryFormat_BothDestinations_SucceedForCommonFormats()
    {
        Assert.AreEqual("-1", Chars(int256.MinusOne, "D"));
        Assert.AreEqual(new string('f', 64), Chars(int256.MinusOne, "x"));
        Assert.AreEqual("0x1", Chars(int256.One, "0x"));
        Assert.AreEqual("-1", Utf8Text(int256.MinusOne, "D"));
        Assert.AreEqual(new string('f', 64), Utf8Text(int256.MinusOne, "x"));
        Assert.AreEqual("0x1", Utf8Text(int256.One, "0x"));
        Assert.AreEqual("0", Chars(int256.Zero, ""));
        Assert.AreEqual("0", Utf8Text(int256.Zero, ""));
    }

    [TestMethod]
    public void TryFormat_ZeroInEveryFormat()
    {
        foreach ((string format, string expected) in new[] { ("D", "0"), ("x", "0"), ("X", "0"), ("0x", "0x0"), ("0X", "0x0") })
        {
            Assert.AreEqual(expected, Chars(int256.Zero, format), format);
            Assert.AreEqual(expected, Utf8Text(int256.Zero, format), format);
        }
    }

    [TestMethod]
    public void TryFormat_DestinationTooSmall_ReturnsFalseWithZeroWritten_ForBothDestinations()
    {
        Span<char> chars = stackalloc char[1];
        Assert.IsFalse(int256.MinusOne.TryFormat(chars, out int charsWritten, "0x"));
        Assert.AreEqual(0, charsWritten);

        Span<byte> bytes = stackalloc byte[2];
        Assert.IsFalse(int256.MinusOne.TryFormat(bytes, out int bytesWritten, "x"));
        Assert.AreEqual(0, bytesWritten);

        // An empty destination cannot even hold "0".
        Assert.IsFalse(int256.Zero.TryFormat(Span<char>.Empty, out charsWritten, "D"));
        Assert.AreEqual(0, charsWritten);
        Assert.IsFalse(int256.Zero.TryFormat(Span<byte>.Empty, out bytesWritten, "D"));
        Assert.AreEqual(0, bytesWritten);
    }

    [TestMethod]
    public void TryFormat_UnknownFormat_ThrowsEvenForAnEmptyDestination()
    {
        // Throwing (not returning false) matters: callers such as string interpolation retry with a bigger buffer on false.
        Assert.ThrowsExactly<FormatException>(() => int256.Zero.TryFormat(Span<char>.Empty, out _, "G"));
        Assert.ThrowsExactly<FormatException>(() => int256.Zero.TryFormat(Span<byte>.Empty, out _, "G"));
    }

    [TestMethod]
    public void TryFormat_OversizedDestination_WritesOnlyTheText_AndLeavesTheTail()
    {
        char[] chars = Enumerable.Repeat('#', 80).ToArray();
        Assert.IsTrue(int256.MinusOne.TryFormat(chars, out int charsWritten, "D"));
        Assert.AreEqual(2, charsWritten);
        Assert.AreEqual("-1", new string(chars[..2]));
        Assert.IsTrue(chars[2..].All(c => c == '#'));

        byte[] bytes = Enumerable.Repeat((byte)0xAA, 80).ToArray();
        Assert.IsTrue(int256.MinusOne.TryFormat(bytes, out int bytesWritten, "D"));
        Assert.AreEqual(2, bytesWritten);
        Assert.AreEqual("-1", Encoding.ASCII.GetString(bytes[..2]));
        Assert.IsTrue(bytes[2..].All(b => b == 0xAA));
    }

    [TestMethod]
    public void TryFormat_ExactBufferBoundaries_AtMaximumWidths()
    {
        foreach ((int256 value, string format, int length) in new[]
        {
            (int256.MinValue, "D", 78), (int256.MinValue, "0x", 66), (int256.MinValue, "x", 64),
            (int256.MaxValue, "0x", 66), (int256.MaxValue, "D", 77),
        })
        {
            Assert.IsTrue(value.TryFormat(new char[length], out int w, format), $"{format} exact");
            Assert.AreEqual(length, w);
            Assert.IsFalse(value.TryFormat(new char[length - 1], out w, format), $"{format} exact-1");
            Assert.AreEqual(0, w);
            Assert.IsTrue(value.TryFormat(new byte[length], out w, format), $"{format} utf8 exact");
            Assert.AreEqual(length, w);
            Assert.IsFalse(value.TryFormat(new byte[length - 1], out w, format), $"{format} utf8 exact-1");
            Assert.AreEqual(0, w);
        }
    }

    // ---- IFormattable / ISpanFormattable / IUtf8SpanFormattable use ----

    [TestMethod]
    public void Interpolation_And_StringFormat_Succeed_WithExpectedText()
    {
        int256 v = FromBig(-255);
        int256 big = int256.MinValue;

        Assert.AreEqual("-255", $"{v}");
        Assert.AreEqual("-255", $"{v:D}");
        Assert.AreEqual(new string('f', 62) + "01", $"{v:x}");
        Assert.AreEqual("0x" + new string('F', 62) + "01", $"{v:0X}");
        Assert.AreEqual(big.ToString(), $"{big}");
        Assert.AreEqual(new string(' ', 86) + "-255", $"{v,90}");
        Assert.AreEqual("-255" + new string(' ', 86), $"{v,-90}");
        Assert.AreEqual("x=0x1;", string.Format(CultureInfo.InvariantCulture, "x={0:0x};", int256.One));
        Assert.AreEqual("[1   ]", string.Format(CultureInfo.InvariantCulture, "[{0,-4:D}]", int256.One));
        Assert.AreEqual("-255", ((IFormattable)v).ToString("D", null));
        Assert.AreEqual("0xff", ((IFormattable)new int256(255)).ToString("0x", null));
    }

    [TestMethod]
    public void Utf8TryWrite_WithInterpolatedHandler_WritesAsciiOrReturnsFalse()
    {
        Span<byte> buffer = stackalloc byte[80];
        int256 min = int256.MinValue;

        Assert.IsTrue(System.Text.Unicode.Utf8.TryWrite(buffer, $"{min}", out int written));
        Assert.AreEqual(78, written);
        Assert.AreEqual((-Two255).ToString(CultureInfo.InvariantCulture), Encoding.ASCII.GetString(buffer[..written]));

        Assert.IsTrue(System.Text.Unicode.Utf8.TryWrite(buffer, $"{int256.One:0x}", out written));
        Assert.AreEqual("0x1", Encoding.ASCII.GetString(buffer[..written]));

        Assert.IsFalse(System.Text.Unicode.Utf8.TryWrite(buffer[..77], $"{min}", out written));
        Assert.AreEqual(0, written);
    }
}
