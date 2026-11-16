using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Unicode;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Public formatting of uint256: ToString, TryFormat (chars and UTF-8), full-width hex and decimal helpers.</summary>
[TestClass]
public sealed class UInt256FormattingTests
{
    private static uint256 U(BigInteger value) => UInt256TestValues.FromBig(value);

    private static string MinimalHex(BigInteger value)
    {
        string hex = value.ToString("x", CultureInfo.InvariantCulture).TrimStart('0');
        return hex.Length == 0 ? "0" : hex;
    }

    private static IEnumerable<BigInteger> Values()
    {
        foreach (BigInteger boundary in UInt256TestValues.Boundaries)
            yield return boundary;
        yield return 0xABCDEF;
        yield return BigInteger.Pow(10, 18);
        yield return BigInteger.Pow(10, 19);
        yield return BigInteger.Pow(10, 77);
        yield return 0xDEADBEEFUL;
    }

    private static string Expected(BigInteger value, string format) => format switch
    {
        "0x" => "0x" + MinimalHex(value),
        "0X" => "0x" + MinimalHex(value).ToUpperInvariant(),
        "x" => MinimalHex(value),
        "X" => MinimalHex(value).ToUpperInvariant(),
        "d" or "D" => value.ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static readonly string[] Formats = ["0x", "0X", "x", "X", "d", "D"];

    // ---- ToString ----

    [TestMethod]
    public void ToString_EveryFormat_MatchesTheOracle()
    {
        foreach (BigInteger value in Values())
        {
            uint256 v = U(value);
            foreach (string format in Formats)
                Assert.AreEqual(Expected(value, format), v.ToString(format, CultureInfo.InvariantCulture), $"{value} format {format}");
        }
    }

    [TestMethod]
    public void ToString_Default_IsPrefixedLowercaseHex()
    {
        Assert.AreEqual("0x0", uint256.Zero.ToString());
        Assert.AreEqual("0x1", uint256.One.ToString());
        Assert.AreEqual("0xabcdef", new uint256(0xABCDEFUL).ToString());
        Assert.AreEqual("0x" + new string('f', 64), uint256.MaxValue.ToString());
        Assert.AreEqual("0xabcdef", new uint256(0xABCDEFUL).ToString(null, null));
        Assert.AreEqual("0xabcdef", new uint256(0xABCDEFUL).ToString("", CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void ToString_UppercaseFormats_OnlyUppercaseTheDigits()
    {
        uint256 v = new(0xABCDEFUL);

        Assert.AreEqual("ABCDEF", v.ToString("X", CultureInfo.InvariantCulture));
        Assert.AreEqual("0xABCDEF", v.ToString("0X", CultureInfo.InvariantCulture));   // the prefix is always lowercase
        Assert.AreEqual("abcdef", v.ToString("x", CultureInfo.InvariantCulture));
        Assert.AreEqual("0xabcdef", v.ToString("0x", CultureInfo.InvariantCulture));
    }

    [TestMethod]
    [DataRow("nope")]
    [DataRow("NOPE")]
    [DataRow("xx")]
    [DataRow("dd")]
    [DataRow("d1")]
    [DataRow("x16")]
    [DataRow("G")]
    [DataRow("N")]
    [DataRow("0")]
    [DataRow("0y")]
    [DataRow(" x")]
    public void UnknownFormats_ThrowFormatException_InEveryFormattingEntryPoint(string format)
    {
        uint256 v = uint256.One;

        Assert.ThrowsExactly<FormatException>(() => _ = v.ToString(format, CultureInfo.InvariantCulture));
        Assert.ThrowsExactly<FormatException>(() => v.TryFormat(new char[80], out _, format.AsSpan(), CultureInfo.InvariantCulture));
        Assert.ThrowsExactly<FormatException>(() => v.TryFormat(new byte[80], out _, format.AsSpan(), CultureInfo.InvariantCulture));
    }

    // ---- TryFormat ----

    [TestMethod]
    public void TryFormat_Chars_ExactBufferBoundaries()
    {
        foreach (BigInteger value in Values())
        {
            foreach (string format in Formats)
            {
                string expected = Expected(value, format);
                uint256 v = U(value);

                char[] exact = new char[expected.Length];
                Assert.IsTrue(v.TryFormat(exact, out int written, format.AsSpan(), CultureInfo.InvariantCulture), $"{value} {format}");
                Assert.AreEqual(expected, new string(exact, 0, written));

                char[] tooSmall = new char[expected.Length - 1];
                Assert.IsFalse(v.TryFormat(tooSmall, out written, format.AsSpan(), CultureInfo.InvariantCulture), $"{value} {format} (one short)");
                Assert.AreEqual(0, written);

                char[] larger = Enumerable.Repeat('#', expected.Length + 3).ToArray();
                Assert.IsTrue(v.TryFormat(larger, out written, format.AsSpan(), CultureInfo.InvariantCulture));
                Assert.AreEqual(expected, new string(larger, 0, written));
                Assert.AreEqual("###", new string(larger, expected.Length, 3), "characters past the output stay untouched");
            }
        }
    }

    [TestMethod]
    public void TryFormat_Utf8_ExactBufferBoundaries()
    {
        foreach (BigInteger value in Values())
        {
            foreach (string format in Formats)
            {
                string expected = Expected(value, format);
                uint256 v = U(value);

                byte[] exact = new byte[expected.Length];
                Assert.IsTrue(v.TryFormat(exact, out int written, format.AsSpan(), CultureInfo.InvariantCulture), $"{value} {format}");
                Assert.AreEqual(expected, Encoding.ASCII.GetString(exact, 0, written));

                Assert.IsFalse(v.TryFormat(new byte[expected.Length - 1], out written, format.AsSpan(), CultureInfo.InvariantCulture), $"{value} {format} (one short)");
                Assert.AreEqual(0, written);

                byte[] larger = Enumerable.Repeat((byte)'#', expected.Length + 3).ToArray();
                Assert.IsTrue(v.TryFormat(larger, out written, format.AsSpan(), CultureInfo.InvariantCulture));
                Assert.AreEqual("###", Encoding.ASCII.GetString(larger, expected.Length, 3));
            }
        }
    }

    [TestMethod]
    public void TryFormat_EmptyDestination_ReturnsFalse()
    {
        Assert.IsFalse(uint256.Zero.TryFormat(Span<char>.Empty, out int chars, "x".AsSpan()));
        Assert.AreEqual(0, chars);
        Assert.IsFalse(uint256.Zero.TryFormat(Span<byte>.Empty, out int bytes, "x".AsSpan()));
        Assert.AreEqual(0, bytes);
    }

    [TestMethod]
    public void TryFormat_DefaultFormat_IsPrefixedHex()
    {
        char[] chars = new char[80];
        byte[] utf8 = new byte[80];

        Assert.IsTrue(new uint256(255UL).TryFormat(chars, out int charsWritten));
        Assert.AreEqual("0xff", new string(chars, 0, charsWritten));
        Assert.IsTrue(new uint256(255UL).TryFormat(utf8, out int bytesWritten));
        Assert.AreEqual("0xff", Encoding.ASCII.GetString(utf8, 0, bytesWritten));
    }

    // ---- helpers ----

    [TestMethod]
    public void ToFullHexString_IsFixedWidthLowercaseWithoutPrefix()
    {
        foreach (BigInteger value in Values())
            Assert.AreEqual(MinimalHex(value).PadLeft(64, '0'), U(value).ToFullHexString(), value.ToString());
    }

    [TestMethod]
    public void ToDecimalString_MatchesBigInteger()
    {
        foreach (BigInteger value in Values())
            Assert.AreEqual(value.ToString(CultureInfo.InvariantCulture), U(value).ToDecimalString());

        Assert.AreEqual("1000000000000000000", new uint256(1_000_000_000_000_000_000UL).ToDecimalString());
    }

    // ---- framework integration ----

    [TestMethod]
    public void StringFormatAndInterpolation_UseTheFormatSpecifier()
    {
        uint256 v = new(0xABCDEFUL);

        Assert.AreEqual("      abcdef|", string.Format(CultureInfo.InvariantCulture, "{0,12:x}|", v));
        Assert.AreEqual("abcdef      |", string.Format(CultureInfo.InvariantCulture, "{0,-12:x}|", v));
        Assert.AreEqual("11259375", $"{v:D}");
        Assert.AreEqual("0xabcdef", $"{v}");
        Assert.AreEqual("ABCDEF", $"{v:X}");
        Assert.ThrowsExactly<FormatException>(() => _ = $"{v:nope}");
    }

    [TestMethod]
    public void Utf8Formatting_ThroughTheInterpolationHandler()
    {
        byte[] destination = new byte[40];

        Assert.IsTrue(Utf8.TryWrite(destination, CultureInfo.InvariantCulture, $"v={new uint256(255UL):x};", out int written));

        Assert.AreEqual("v=ff;", Encoding.UTF8.GetString(destination, 0, written));
    }

    [TestMethod]
    public void Output_DoesNotDependOnTheFormatProvider()
    {
        var provider = new NumberFormatInfo { NegativeSign = "N", PositiveSign = "P", NumberDecimalSeparator = "x", NumberGroupSeparator = "y" };
        uint256 v = U(BigInteger.Pow(10, 30) + 12345);
        char[] chars = new char[80];

        Assert.AreEqual(BigInteger.Pow(10, 30) + 12345, BigInteger.Parse(v.ToString("d", provider), CultureInfo.InvariantCulture));
        Assert.AreEqual(Expected(BigInteger.Pow(10, 30) + 12345, "0x"), v.ToString("0x", provider));
        Assert.IsTrue(v.TryFormat(chars, out int written, "D".AsSpan(), provider));
        Assert.AreEqual(v.ToDecimalString(), new string(chars, 0, written));
        Assert.AreEqual(v, uint256.Parse("1000000000000000000000000012345", provider));
    }

    [TestMethod]
    public void FormatThenParse_RoundTrips_InHexAndDecimal()
    {
        var rng = new Random(31337);
        for (int i = 0; i < 300; i++)
        {
            uint256 v = UInt256TestValues.SparseValue(rng);

            Assert.AreEqual(v, uint256.Parse(v.ToString()));
            Assert.AreEqual(v, uint256.Parse(v.ToString("d", null)));
            Assert.AreEqual(v, uint256.Parse("0x" + v.ToFullHexString().TrimStart('0').PadLeft(1, '0')));
            Assert.AreEqual(v, uint256.Parse(Encoding.ASCII.GetBytes(v.ToDecimalString())));
        }
    }
}
