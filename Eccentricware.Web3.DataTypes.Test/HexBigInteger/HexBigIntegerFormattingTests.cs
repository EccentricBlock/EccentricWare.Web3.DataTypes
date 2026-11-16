using System.Globalization;
using System.Numerics;
using System.Text;
using static EccentricWare.Web3.DataTypes.Tests.HexBigIntegerTestData;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="HexBigInteger"/> formatting: <c>ToString</c>, <c>TryFormat</c> into chars and UTF-8, formats, failure paths, culture independence.</summary>
[TestClass]
public sealed class HexBigIntegerFormattingTests
{
    private static readonly string[] Formats = ["", "0x", "0X", "x", "X", "d", "D"];

    /// <summary>Independent expectation: ReferenceHex for the hex formats, BigInteger.ToString for decimal.</summary>
    private static string Expected(BigInteger value, string format) => format switch
    {
        "" or "0x" => ReferenceHex(value),
        "0X" => ReferenceHex(value, upper: true),
        "x" => ReferenceHex(value, prefix: false),
        "X" => ReferenceHex(value, upper: true, prefix: false),
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    // ----------------- ToString -----------------

    [TestMethod]
    public void ToString_IsMinimalHex_WithNoLeadingZeroNibble()
    {
        // BigInteger.ToString("x") emits a leading '0' whenever the top nibble is >= 8 ("0ff"); the library must not.
        Assert.AreEqual("0xff", new HexBigInteger(255).ToString());
        Assert.AreEqual("-0xff", new HexBigInteger(-255).ToString());
        Assert.AreEqual("0x8", new HexBigInteger(8).ToString());
        Assert.AreEqual("0x0", HexBigInteger.Zero.ToString());
        Assert.AreEqual("0x1", HexBigInteger.One.ToString());
        Assert.AreEqual("-0x1", HexBigInteger.MinusOne.ToString());
    }

    [TestMethod]
    public void ToString_MatchesTheReference_ForEveryFormat_OverTheInterestingValues()
    {
        foreach (BigInteger value in InterestingSigned())
        {
            var hex = new HexBigInteger(value);
            foreach (string format in Formats)
                Assert.AreEqual(Expected(value, format), hex.ToString(format, CultureInfo.InvariantCulture), $"{value} '{format}'");

            Assert.AreEqual(ReferenceHex(value), hex.ToString(), value.ToString());
            Assert.AreEqual(ReferenceHex(value), hex.ToString(null, null), value.ToString());
        }
    }

    [TestMethod]
    public void ToString_Formats_AllSupportedSpecifiers()
    {
        var value = new HexBigInteger(0xABCDEF);
        var negative = new HexBigInteger(-0xABCDEF);

        Assert.AreEqual("0xabcdef", value.ToString("0x", CultureInfo.InvariantCulture));
        Assert.AreEqual("0xABCDEF", value.ToString("0X", CultureInfo.InvariantCulture));
        Assert.AreEqual("abcdef", value.ToString("x", CultureInfo.InvariantCulture));
        Assert.AreEqual("ABCDEF", value.ToString("X", CultureInfo.InvariantCulture));
        Assert.AreEqual("0xabcdef", value.ToString(string.Empty, CultureInfo.InvariantCulture));
        Assert.AreEqual("0xabcdef", value.ToString(null, CultureInfo.InvariantCulture));
        Assert.AreEqual("11259375", value.ToString("d", CultureInfo.InvariantCulture));
        Assert.AreEqual("11259375", value.ToString("D", CultureInfo.InvariantCulture));
        Assert.AreEqual("-0xabcdef", negative.ToString("0x", CultureInfo.InvariantCulture));
        Assert.AreEqual("-0xABCDEF", negative.ToString("0X", CultureInfo.InvariantCulture));
        Assert.AreEqual("-ABCDEF", negative.ToString("X", CultureInfo.InvariantCulture));
        Assert.AreEqual("-11259375", negative.ToString("d", CultureInfo.InvariantCulture));
        Assert.AreEqual("0", HexBigInteger.Zero.ToString("x", CultureInfo.InvariantCulture));
        Assert.AreEqual("0", HexBigInteger.Zero.ToString("d", CultureInfo.InvariantCulture));
        Assert.AreEqual("0", HexBigInteger.Zero.ToString("D", CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void Format_NegativeBoundaryValues_AroundSixtyThreeAndSixtyFourBits()
    {
        BigInteger[] edges =
        [
            long.MinValue, (BigInteger)long.MinValue + 1, (BigInteger)long.MinValue - 1,
            -(BigInteger)ulong.MaxValue, -(BigInteger)ulong.MaxValue - 1, -(BigInteger)ulong.MaxValue + 1,
            long.MaxValue, (BigInteger)long.MaxValue + 1, ulong.MaxValue, (BigInteger)ulong.MaxValue + 1,
        ];

        foreach (BigInteger edge in edges)
        {
            var value = new HexBigInteger(edge);
            foreach (string format in new[] { "", "0X", "x", "X" })
                Assert.AreEqual(Expected(edge, format), value.ToString(format, null), $"{edge} '{format}'");
        }
    }

    [TestMethod]
    [DataRow("g")]
    [DataRow("G")]
    [DataRow("N")]
    [DataRow("R")]
    [DataRow("xx")]
    [DataRow("0y")]
    [DataRow("0x0")]
    [DataRow("dd")]
    [DataRow(" x")]
    [DataRow("x ")]
    [DataRow("0 x")]
    public void UnknownFormat_ThrowsFormatException_FromToStringAndBothTryFormatOverloads(string format)
    {
        // TryFormat used to return false for an unknown format; it now throws like ToString and like HexBytes, so a bad format
        // is never mistaken for "destination too small".
        var value = new HexBigInteger(1);

        FormatException error = Assert.ThrowsExactly<FormatException>(() => value.ToString(format, CultureInfo.InvariantCulture));
        StringAssert.Contains(error.Message, format);

        FormatException fromBytes = Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new byte[64], out _, format, null));
        FormatException fromChars = Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new char[64], out _, format, null));
        StringAssert.Contains(fromBytes.Message, format);
        StringAssert.Contains(fromChars.Message, format);

        // Even a destination that is far too small reports the format, not "false".
        Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new byte[0], out _, format, null));
        Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new char[0], out _, format, null));
    }

    // ----------------- TryFormat -----------------

    [TestMethod]
    public void TryFormat_Utf8_ExactFit_OneShort_AndOversized_ForEveryFormat()
    {
        foreach (BigInteger raw in InterestingSigned())
        {
            var value = new HexBigInteger(raw);
            foreach (string format in Formats)
            {
                string expected = Expected(raw, format);
                string label = $"{raw} '{format}'";

                var exact = new byte[expected.Length];
                Assert.IsTrue(value.TryFormat(exact, out int written, format, null), label);
                Assert.AreEqual(expected, Encoding.ASCII.GetString(exact, 0, written), label);

                // Failed call: false, nothing written, destination untouched.
                var shortBuffer = new byte[expected.Length - 1];
                Array.Fill(shortBuffer, (byte)0xAB);
                Assert.IsFalse(value.TryFormat(shortBuffer, out written, format, null), label);
                Assert.AreEqual(0, written, label);
                Assert.IsTrue(shortBuffer.All(b => b == 0xAB), $"{label}: destination was modified");

                // Oversized: exact output, sentinel tail untouched.
                var roomy = new byte[expected.Length + 4];
                Array.Fill(roomy, (byte)'#');
                Assert.IsTrue(value.TryFormat(roomy, out written, format, null), label);
                Assert.AreEqual(expected.Length, written, label);
                Assert.AreEqual(expected + "####", Encoding.ASCII.GetString(roomy), label);
            }
        }
    }

    [TestMethod]
    public void TryFormat_Chars_ExactFit_OneShort_AndOversized_ForEveryFormat()
    {
        foreach (BigInteger raw in InterestingSigned())
        {
            var value = new HexBigInteger(raw);
            foreach (string format in Formats)
            {
                string expected = Expected(raw, format);
                string label = $"{raw} '{format}'";

                var exact = new char[expected.Length];
                Assert.IsTrue(value.TryFormat(exact, out int written, format, null), label);
                Assert.AreEqual(expected, new string(exact, 0, written), label);

                var shortBuffer = new char[expected.Length - 1];
                Array.Fill(shortBuffer, '@');
                Assert.IsFalse(value.TryFormat(shortBuffer, out written, format, null), label);
                Assert.AreEqual(0, written, label);
                Assert.IsTrue(shortBuffer.All(c => c == '@'), $"{label}: destination was modified");

                // An oversized destination must not change the answer (a fixed internal scratch buffer used to).
                var roomy = new char[expected.Length + 5000];
                Array.Fill(roomy, '#');
                Assert.IsTrue(value.TryFormat(roomy, out written, format, null), label);
                Assert.AreEqual(expected.Length, written, label);
                Assert.AreEqual(expected, new string(roomy, 0, written), label);
                Assert.IsTrue(roomy.AsSpan(written).IndexOfAnyExcept('#') < 0, $"{label}: tail was modified");
            }
        }
    }

    [TestMethod]
    public void TryFormat_FailedCall_LeavesTheDestinationUntouched_ForEveryFormatAndEverySize()
    {
        // A negative value wider than 64 bits used to write its '-' before discovering the digits did not fit.
        BigInteger[] values = [255, -255, long.MinValue, BigInteger.Pow(2, 200), -BigInteger.Pow(2, 200), -BigInteger.Pow(2, 64), -BigInteger.Pow(2, 63) - 1, 0];
        foreach (BigInteger raw in values)
        {
            var value = new HexBigInteger(raw);
            foreach (string format in Formats)
            {
                int needed = Expected(raw, format).Length;
                for (int size = 0; size < needed; size++)
                {
                    var buffer = new byte[size];
                    Array.Fill(buffer, (byte)0xAB);
                    Assert.IsFalse(value.TryFormat(buffer, out int written, format, null), $"{raw} '{format}' size={size}");
                    Assert.AreEqual(0, written);
                    Assert.IsTrue(buffer.All(b => b == 0xAB), $"{raw} '{format}' size={size}: destination was modified");

                    var chars = new char[size];
                    Array.Fill(chars, '@');
                    Assert.IsFalse(value.TryFormat(chars, out written, format, null), $"{raw} '{format}' chars size={size}");
                    Assert.AreEqual(0, written);
                    Assert.IsTrue(chars.All(c => c == '@'));
                }
            }
        }
    }

    [TestMethod]
    public void TryFormat_ValueLargerThanAnyInternalScratchBuffer()
    {
        // 4096 hex digits is the UTF-8 parser's own limit, so a value the library can read it must also be able to write.
        BigInteger value = BigInteger.Pow(2, 16384) - 1;
        var hex = new HexBigInteger(value);

        var chars = new char[5000];
        Assert.IsTrue(hex.TryFormat(chars, out int written, "0x", CultureInfo.InvariantCulture));
        Assert.AreEqual(ReferenceHex(value), new string(chars, 0, written));
        Assert.AreEqual(value, HexBigInteger.Parse(chars.AsSpan(0, written), null).Value);

        var utf8 = new byte[5000];
        Assert.IsTrue(hex.TryFormat(utf8, out written, "0x", CultureInfo.InvariantCulture));
        Assert.AreEqual(ReferenceHex(value), Encoding.ASCII.GetString(utf8, 0, written));

        Assert.IsTrue(hex.TryFormat(chars, out written, "d", CultureInfo.InvariantCulture));
        Assert.AreEqual(value.ToString(CultureInfo.InvariantCulture), new string(chars, 0, written));
    }

    [TestMethod]
    public void TryFormat_EmptyDestination_ReturnsFalse_ForEveryFormat()
    {
        foreach (string format in new[] { "", "x", "d" })
        {
            Assert.IsFalse(new HexBigInteger(1).TryFormat(Span<byte>.Empty, out int bytes, format, null));
            Assert.AreEqual(0, bytes);
            Assert.IsFalse(new HexBigInteger(1).TryFormat(Span<char>.Empty, out int chars, format, null));
            Assert.AreEqual(0, chars);
            Assert.IsFalse(HexBigInteger.Zero.TryFormat(Span<byte>.Empty, out bytes, format, null));
            Assert.IsFalse(HexBigInteger.Zero.TryFormat(Span<char>.Empty, out chars, format, null));
        }
    }

    [TestMethod]
    public void TryFormat_LargeUtf8Destination_DoesNotChangeOutput()
    {
        var huge = new byte[1 << 20];
        Assert.IsTrue(new HexBigInteger(-255).TryFormat(huge, out int written, "d", CultureInfo.InvariantCulture));
        Assert.AreEqual("-255", Encoding.ASCII.GetString(huge, 0, written));
        Assert.IsTrue(new HexBigInteger(-255).TryFormat(huge, out written, "0x", CultureInfo.InvariantCulture));
        Assert.AreEqual("-0xff", Encoding.ASCII.GetString(huge, 0, written));
    }

    [TestMethod]
    public void RoundTrip_FormatThenParse_AllFormatsAndBothEncodings()
    {
        var random = new Random(99);
        for (int i = 0; i < 300; i++)
        {
            BigInteger raw = RandomMagnitude(random, random.Next(1, 100));
            if (random.Next(2) == 0)
                raw = -raw;
            var value = new HexBigInteger(raw);

            Assert.AreEqual(value, HexBigInteger.Parse(value.ToString(), null));
            Assert.AreEqual(value, HexBigInteger.Parse(value.ToString("X", null), null));
            Assert.AreEqual(value, HexBigInteger.ParseUtf8(Encoding.ASCII.GetBytes(value.ToString())));
        }
    }

    // ----------------- culture independence -----------------

    private static void AssertAsciiUnder(CultureInfo culture)
    {
        var negative = new HexBigInteger(-255);
        Assert.AreEqual("-255", negative.ToString("d", culture));
        Assert.AreEqual("-0xff", negative.ToString("0x", culture));

        var utf8 = new byte[16];
        Assert.IsTrue(negative.TryFormat(utf8, out int bytes, "d", culture));
        Assert.AreEqual("-255", Encoding.ASCII.GetString(utf8, 0, bytes));

        var chars = new char[16];
        Assert.IsTrue(negative.TryFormat(chars, out int count, "d", culture));
        Assert.AreEqual("-255", new string(chars, 0, count));
        Assert.AreEqual("v=-0xff", string.Create(culture, $"v={negative}"));
    }

    [TestMethod]
    public void Formatting_IsAscii_UnderACustomCultureWithANonAsciiMinusSign()
    {
        // Needs no ICU data: a clone of the invariant culture with U+2212 as the minus sign.
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "−";
        Assert.AreEqual("−255", new BigInteger(-255).ToString(culture), "the custom culture really differs");

        AssertAsciiUnder(culture);
    }

    [TestMethod]
    public void Formatting_IsAscii_UnderSwedish()
    {
        // sv-SE uses U+2212 for the minus sign; output must stay ASCII "-" whatever provider is passed.
        CultureInfo culture;
        try { culture = new CultureInfo("sv-SE"); }
        catch (CultureNotFoundException) { Assert.Inconclusive("sv-SE is not available on this host (invariant globalization or no ICU)."); return; }

        if (culture.NumberFormat.NegativeSign == "-")
            Assert.Inconclusive("This host's sv-SE data uses an ASCII minus sign, so it cannot exercise the behaviour.");

        AssertAsciiUnder(culture);
    }
}
