using EccentricWare.Web3.DataTypes.Utils;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// JSON-RPC style hex quantity helpers: TryNormaliseHexTokenUtf8, TryParseBigIntegerHexUnsignedUtf8 (stack and ArrayPool paths),
/// TryWriteHexMinimalUtf8 and GetMaxHexUtf8LengthFromMagnitudeByteCount, checked against BigInteger.
/// </summary>
[TestClass]
public sealed class ByteUtils_HexTokenAndMinimalHexTests
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static BigInteger ParseOracle(string hex) => BigInteger.Parse("0" + hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string RandomHex(Random random, int length)
    {
        const string digits = "0123456789abcdefABCDEF";
        return string.Create(length, random, static (span, r) =>
        {
            for (int i = 0; i < span.Length; i++) span[i] = digits[r.Next(digits.Length)];
        });
    }

    // ---- TryNormaliseHexTokenUtf8 ------------------------------------------------------------------------------------

    private static (bool Ok, bool IsNull, bool IsNegative, string Digits) Normalise(string token)
    {
        bool ok = ByteUtils.TryNormaliseHexTokenUtf8(Utf8(token), out bool isNull, out bool isNegative, out var digits);
        return (ok, isNull, isNegative, Encoding.ASCII.GetString(digits));
    }

    [TestMethod]
    [DataRow("0x1f", false, false, "1f")]
    [DataRow("1f", false, false, "1f")]
    [DataRow("0X0A", false, false, "0A")]
    [DataRow("\"0x1f\"", false, false, "1f")]
    [DataRow("\"1F\"", false, false, "1F")]
    [DataRow("-0x1f", false, true, "1f")]
    [DataRow("\"-0x1f\"", false, true, "1f")]
    [DataRow("+0x1f", false, false, "1f")]
    [DataRow("+1f", false, false, "1f")]
    [DataRow("0x", false, false, "")]
    [DataRow("\"0x\"", false, false, "")]
    [DataRow("-", false, true, "")]
    [DataRow("+", false, false, "")]
    [DataRow("+0x", false, false, "")]
    [DataRow("-0x", false, true, "")]
    [DataRow("null", true, false, "")]
    [DataRow("  null\t", true, false, "")]
    [DataRow(" \t\r\n0x1f \t\r\n", false, false, "1f")]
    [DataRow("\" 0x1f \"", false, false, "1f")]
    [DataRow("- 0x1", false, true, "1")]   // documents current behaviour: whitespace after the sign is trimmed
    public void TryNormaliseHexTokenUtf8_AcceptedForms(string token, bool isNull, bool isNegative, string digits)
    {
        var result = Normalise(token);

        Assert.IsTrue(result.Ok, token);
        Assert.AreEqual(isNull, result.IsNull, token);
        Assert.AreEqual(isNegative, result.IsNegative, token);
        Assert.AreEqual(digits, result.Digits, token);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\"\"")]       // documents current behaviour: a quoted empty string is rejected, unlike "0x" or "-" (see report)
    [DataRow("\"  \"")]
    [DataRow("NULL")]
    [DataRow("Null")]
    [DataRow("nul")]
    [DataRow("\"null\"")]
    [DataRow("0x-1")]
    [DataRow("--1")]
    [DataRow("+-1")]
    [DataRow("0x0x1")]
    [DataRow("xyz")]
    [DataRow("-xyz")]
    [DataRow("0xg")]
    [DataRow("1 f")]
    [DataRow("\"1f")]
    [DataRow("1f\"")]
    [DataRow("\u00E9")]
    [DataRow("\uFF11")]
    public void TryNormaliseHexTokenUtf8_RejectedForms(string token)
    {
        Assert.IsFalse(Normalise(token).Ok, token);
    }

    [TestMethod]
    public void TryNormaliseHexTokenUtf8_RawHighBytesAndNul_AreRejected()
    {
        for (int b = 0x80; b < 256; b++)
            Assert.IsFalse(ByteUtils.TryNormaliseHexTokenUtf8([(byte)'1', (byte)b], out _, out _, out _), $"0x{b:X2}");
        Assert.IsFalse(ByteUtils.TryNormaliseHexTokenUtf8([(byte)'1', 0], out _, out _, out _));
    }

    [TestMethod]
    public void TryNormaliseHexTokenUtf8_VeryLongValidToken_ReturnsAllDigitsWithoutCopy()
    {
        byte[] token = Utf8("0x" + new string('a', 100_000));

        Assert.IsTrue(ByteUtils.TryNormaliseHexTokenUtf8(token, out bool isNull, out bool isNegative, out var digits));
        Assert.IsFalse(isNull);
        Assert.IsFalse(isNegative);
        Assert.AreEqual(100_000, digits.Length);
        Assert.IsTrue(digits.SequenceEqual(token.AsSpan(2)), "digits must equal the token tail");
    }

    // ---- TryParseBigIntegerHexUnsignedUtf8 ---------------------------------------------------------------------------

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(63)]
    [DataRow(255)]
    [DataRow(256)]   // 128 bytes: largest stackalloc case
    [DataRow(257)]   // 129 bytes: first ArrayPool case (odd length)
    [DataRow(258)]
    [DataRow(512)]
    [DataRow(4096)]
    public void TryParseBigIntegerHexUnsignedUtf8_MatchesBigIntegerOracle_OnStackAndPooledPaths(int digitCount)
    {
        var random = new Random(digitCount);
        for (int trial = 0; trial < 5; trial++)
        {
            string hex = RandomHex(random, digitCount);

            Assert.IsTrue(ByteUtils.TryParseBigIntegerHexUnsignedUtf8(Utf8(hex), digitCount, out BigInteger value), hex);
            Assert.AreEqual(ParseOracle(hex), value);
        }
    }

    [TestMethod]
    [DataRow(258)]
    [DataRow(1000)]
    public void TryParseBigIntegerHexUnsignedUtf8_PooledPath_AllFsAndLeadingZeros(int digitCount)
    {
        string allF = new('f', digitCount);
        Assert.IsTrue(ByteUtils.TryParseBigIntegerHexUnsignedUtf8(Utf8(allF), digitCount, out BigInteger max));
        Assert.AreEqual(BigInteger.Pow(2, 4 * digitCount) - 1, max);

        string padded = new string('0', digitCount - 1) + "1";
        Assert.IsTrue(ByteUtils.TryParseBigIntegerHexUnsignedUtf8(Utf8(padded), digitCount, out BigInteger one));
        Assert.AreEqual(BigInteger.One, one);
    }

    [TestMethod]
    [DataRow(300, 150)]  // invalid digit in the middle
    [DataRow(300, 0)]    // first digit of an even-length value
    [DataRow(301, 0)]    // first digit of an odd-length value (written before the bulk decode)
    [DataRow(300, 299)]  // last digit
    public void TryParseBigIntegerHexUnsignedUtf8_PooledPath_InvalidDigit_ReturnsFalseWithZeroValue(int digitCount, int badPosition)
    {
        char[] chars = new string('a', digitCount).ToCharArray();
        chars[badPosition] = 'g';

        Assert.IsFalse(ByteUtils.TryParseBigIntegerHexUnsignedUtf8(Utf8(new string(chars)), digitCount, out BigInteger value));
        Assert.AreEqual(BigInteger.Zero, value);

        // The pool is shared: a following valid call must still work (the failed call must not corrupt or leak the rented array).
        Assert.IsTrue(ByteUtils.TryParseBigIntegerHexUnsignedUtf8(Utf8(new string('a', digitCount)), digitCount, out BigInteger ok));
        Assert.AreEqual(ParseOracle(new string('a', digitCount)), ok);
    }

    [TestMethod]
    public void TryParseBigIntegerHexUnsignedUtf8_EmptyIsZero_EvenWithZeroOrNegativeLimit()
    {
        Assert.IsTrue(ByteUtils.TryParseBigIntegerHexUnsignedUtf8([], 0, out BigInteger value));
        Assert.AreEqual(BigInteger.Zero, value);
        Assert.IsTrue(ByteUtils.TryParseBigIntegerHexUnsignedUtf8([], 5, out value));
        Assert.AreEqual(BigInteger.Zero, value);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(7)]
    [DataRow(64)]
    [DataRow(257)]
    public void TryParseBigIntegerHexUnsignedUtf8_MaxHexDigits_BoundaryIsInclusive(int digitCount)
    {
        byte[] hex = Utf8(new string('f', digitCount));

        Assert.IsTrue(ByteUtils.TryParseBigIntegerHexUnsignedUtf8(hex, digitCount, out BigInteger accepted));
        Assert.AreEqual(BigInteger.Pow(2, 4 * digitCount) - 1, accepted);

        Assert.IsFalse(ByteUtils.TryParseBigIntegerHexUnsignedUtf8(hex, digitCount - 1, out BigInteger rejected));
        Assert.AreEqual(BigInteger.Zero, rejected);
    }

    [TestMethod]
    public void TryParseBigIntegerHexUnsignedUtf8_ZeroLimit_RejectsAnyDigit()
    {
        Assert.IsFalse(ByteUtils.TryParseBigIntegerHexUnsignedUtf8("0"u8, 0, out BigInteger value));
        Assert.AreEqual(BigInteger.Zero, value);
    }

    [TestMethod]
    [DataRow("0x1")]
    [DataRow("-1")]
    [DataRow(" 1")]
    [DataRow("1 ")]
    [DataRow("zz")]
    public void TryParseBigIntegerHexUnsignedUtf8_PrefixSignWhitespaceOrNonHex_IsRejected(string text)
    {
        Assert.IsFalse(ByteUtils.TryParseBigIntegerHexUnsignedUtf8(Utf8(text), 64, out BigInteger value));
        Assert.AreEqual(BigInteger.Zero, value);
    }

    // ---- TryWriteHexMinimalUtf8 -------------------------------------------------------------------------------------------

    private static string WriteMinimal(byte[] magnitude, bool prefix, bool uppercase, int destinationSize = 4096)
    {
        byte[] destination = new byte[destinationSize];
        Assert.IsTrue(ByteUtils.TryWriteHexMinimalUtf8(magnitude, prefix, uppercase, destination, out int written));
        return Encoding.ASCII.GetString(destination, 0, written);
    }

    [TestMethod]
    [DataRow("", false, false, "0")]
    [DataRow("", true, false, "0x0")]
    [DataRow("00", false, false, "0")]
    [DataRow("00000000", true, true, "0x0")]
    [DataRow("0F", false, false, "f")]
    [DataRow("0000000F", false, false, "f")]
    [DataRow("0000000F", true, true, "0xF")]
    [DataRow("10", false, false, "10")]
    [DataRow("0123", true, false, "0x123")]
    [DataRow("00ABCD", false, true, "ABCD")]
    [DataRow("00ABCD", true, false, "0xabcd")]
    [DataRow("FFFF", false, false, "ffff")]
    public void TryWriteHexMinimalUtf8_FormatsMinimalDigits(string magnitudeHex, bool prefix, bool uppercase, string expected)
    {
        Assert.AreEqual(expected, WriteMinimal(Convert.FromHexString(magnitudeHex), prefix, uppercase));
    }

    [TestMethod]
    public void TryWriteHexMinimalUtf8_RandomMagnitudes_MatchBigIntegerOracle_AndRoundTripThroughTheParser()
    {
        var random = new Random(4242);
        for (int i = 0; i < 300; i++)
        {
            byte[] magnitude = new byte[random.Next(0, 40)];
            random.NextBytes(magnitude);
            if (magnitude.Length > 0) Array.Clear(magnitude, 0, random.Next(0, magnitude.Length + 1));

            BigInteger expectedValue = new(magnitude, isUnsigned: true, isBigEndian: true);
            // BigInteger "x" output may carry one extra leading 0 for the sign bit: strip it (zero becomes "0").
            string expected = expectedValue.ToString("x", CultureInfo.InvariantCulture).TrimStart('0');
            if (expected.Length == 0) expected = "0";

            string written = WriteMinimal(magnitude, prefix: false, uppercase: false);

            Assert.AreEqual(expected, written);
            Assert.IsTrue(ByteUtils.TryParseBigIntegerHexUnsignedUtf8(Utf8(written), 256, out BigInteger parsed));
            Assert.AreEqual(expectedValue, parsed);
        }
    }

    [TestMethod]
    public void TryWriteHexMinimalUtf8_ThirtyThreeByteValueWithLeadingZeros_IsTrimmed()
    {
        byte[] magnitude = new byte[33];
        magnitude[30] = 0x01;
        magnitude[32] = 0xFF;

        Assert.AreEqual("100ff", WriteMinimal(magnitude, false, false)); // bytes 01 00 FF
    }

    [TestMethod]
    [DataRow("0123", true, 5)]
    [DataRow("0123", false, 3)]
    [DataRow("ABCD", true, 6)]
    [DataRow("", false, 1)]
    [DataRow("", true, 3)]
    public void TryWriteHexMinimalUtf8_ExactSizeSucceeds_OneShortFailsWithoutWriting(string magnitudeHex, bool prefix, int total)
    {
        byte[] magnitude = Convert.FromHexString(magnitudeHex);

        byte[] exact = new byte[total];
        Assert.IsTrue(ByteUtils.TryWriteHexMinimalUtf8(magnitude, prefix, false, exact, out int written));
        Assert.AreEqual(total, written);

        byte[] oneShort = Enumerable.Repeat((byte)'#', total - 1).ToArray();
        Assert.IsFalse(ByteUtils.TryWriteHexMinimalUtf8(magnitude, prefix, false, oneShort, out written));
        Assert.AreEqual(0, written);
        Assert.IsTrue(oneShort.All(b => b == '#'), "nothing may be written when the destination is too small");
    }

    [TestMethod]
    public void TryWriteHexMinimalUtf8_LargerDestination_LeavesTheTailUntouched()
    {
        byte[] destination = Enumerable.Repeat((byte)'#', 8).ToArray();

        Assert.IsTrue(ByteUtils.TryWriteHexMinimalUtf8([0x01, 0x23], true, false, destination, out int written));

        Assert.AreEqual(5, written);
        Assert.AreEqual("0x123###", Encoding.ASCII.GetString(destination));
    }

    // ---- GetMaxHexUtf8LengthFromMagnitudeByteCount ---------------------------------------------------------------------

    [TestMethod]
    public void GetMaxHexUtf8Length_IsNeverBelowWhatTheWriterProduces_AndIsTightForAllFfMagnitudes()
    {
        var random = new Random(77);
        for (int length = 0; length <= 64; length++)
            foreach (bool prefix in new[] { false, true })
            {
                int max = ByteUtils.GetMaxHexUtf8LengthFromMagnitudeByteCount(length, includeSign: false, include0xPrefix: prefix);
                int maxSigned = ByteUtils.GetMaxHexUtf8LengthFromMagnitudeByteCount(length, includeSign: true, include0xPrefix: prefix);
                Assert.AreEqual(max + 1, maxSigned, $"sign adds exactly one char (length {length})");

                byte[] allFf = Enumerable.Repeat((byte)0xFF, length).ToArray();
                Assert.AreEqual(max, WriteMinimal(allFf, prefix, false).Length, $"worst case is tight (length {length}, prefix {prefix})");

                byte[] randomMagnitude = new byte[length];
                random.NextBytes(randomMagnitude);
                Assert.IsLessThanOrEqualTo(max, WriteMinimal(randomMagnitude, prefix, true).Length, $"random length {length}");
            }
    }
}
