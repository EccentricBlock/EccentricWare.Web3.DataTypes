using EccentricWare.Web3.DataTypes.Utils;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Randomised and boundary tests for the chunked decimal parser, the hex fast paths and the ASCII formatting core,
/// with <see cref="BigInteger"/> and the BCL as oracles.
/// </summary>
[TestClass]
public sealed class UInt256ParsingAndFormattingFuzzTests
{
    private static readonly BigInteger Two256 = BigInteger.One << 256;
    private static readonly BigInteger Two255 = BigInteger.One << 255;

    private static ulong Limb(Random rng) => rng.Next(4) switch
    {
        0 => 0UL,
        1 => ulong.MaxValue,
        2 => (ulong)rng.NextInt64() ^ ((ulong)rng.NextInt64() << 1),
        _ => 1UL << rng.Next(64),
    };

    private static uint256 RandomUInt256(Random rng)
    {
        int limbs = rng.Next(1, 5);
        return new uint256(Limb(rng), limbs > 1 ? Limb(rng) : 0, limbs > 2 ? Limb(rng) : 0, limbs > 3 ? Limb(rng) : 0);
    }

    private static string ExpectedHex(BigInteger value)
    {
        string hex = value.ToString("x", CultureInfo.InvariantCulture).TrimStart('0');
        return hex.Length == 0 ? "0" : hex;
    }

    // ---- decimal parsing ----

    [TestMethod]
    public void DecimalParse_RandomDigitStrings_MatchesBigInteger_Chars_And_Utf8()
    {
        var rng = new Random(2468);
        for (int i = 0; i < 60_000; i++)
        {
            int length = rng.Next(1, 90);
            var sb = new StringBuilder();
            for (int k = 0; k < length; k++)
                sb.Append((char)('0' + (rng.Next(5) == 0 ? 9 : rng.Next(10))));
            if (rng.Next(3) == 0)
                sb.Insert(0, new string('0', rng.Next(1, 30)));

            string text = sb.ToString();
            BigInteger expected = BigInteger.Parse(text, CultureInfo.InvariantCulture);
            bool fits = expected < Two256;

            Assert.AreEqual(fits, ByteUtils.TryParseUInt256DecimalChars(text, out ulong c0, out ulong c1, out ulong c2, out ulong c3), text);
            Assert.AreEqual(fits, ByteUtils.TryParseUInt256DecimalUtf8(Encoding.ASCII.GetBytes(text), out ulong b0, out ulong b1, out ulong b2, out ulong b3), text);
            if (fits)
            {
                Assert.AreEqual(expected, new uint256(c0, c1, c2, c3).ToBigInteger(), text);
                Assert.AreEqual(expected, new uint256(b0, b1, b2, b3).ToBigInteger(), text);
            }
        }
    }

    [TestMethod]
    public void DecimalParse_ChunkAndLengthBoundaries()
    {
        foreach (int digits in new[] { 1, 7, 8, 9, 15, 16, 17, 18, 19, 20, 21, 27, 28, 37, 38, 39, 40, 56, 57, 58, 59, 76, 77, 78, 79, 100 })
        {
            string nines = new string('9', digits);
            string oneThenZeros = "1" + new string('0', digits - 1);
            foreach (string text in new[] { nines, oneThenZeros })
            {
                BigInteger expected = BigInteger.Parse(text, CultureInfo.InvariantCulture);
                bool fits = expected < Two256;
                Assert.AreEqual(fits, uint256.TryParse(text.AsSpan(), out uint256 viaChars), text);
                Assert.AreEqual(fits, uint256.TryParse(Encoding.ASCII.GetBytes(text), out uint256 viaUtf8), text);
                if (fits)
                {
                    Assert.AreEqual(expected, viaChars.ToBigInteger(), text);
                    Assert.AreEqual(viaChars, viaUtf8, text);
                }
            }
        }

        // The exact edge: 2^256 - 1 parses, 2^256 does not (78 digits each).
        string max = (Two256 - 1).ToString(CultureInfo.InvariantCulture);
        string over = Two256.ToString(CultureInfo.InvariantCulture);
        Assert.IsTrue(uint256.TryParse(max.AsSpan(), out uint256 parsedMax));
        Assert.AreEqual(uint256.MaxValue, parsedMax);
        Assert.IsFalse(uint256.TryParse(over.AsSpan(), out _));
        Assert.IsFalse(uint256.TryParse(Encoding.ASCII.GetBytes(over), out _));
    }

    [TestMethod]
    public void DecimalParse_LeadingZeros_AreAccepted_AndDoNotCountTowardLength()
    {
        string text = new string('0', 200) + "42";
        Assert.IsTrue(ByteUtils.TryParseUInt256DecimalChars(text, out ulong a, out _, out _, out _));
        Assert.AreEqual(42UL, a);
        Assert.IsTrue(ByteUtils.TryParseUInt256DecimalUtf8(Encoding.ASCII.GetBytes(text), out ulong b, out _, out _, out _));
        Assert.AreEqual(42UL, b);

        Assert.IsTrue(ByteUtils.TryParseUInt256DecimalChars(new string('0', 100), out ulong z, out ulong z1, out ulong z2, out ulong z3));
        Assert.AreEqual(0UL, z | z1 | z2 | z3);
        Assert.IsFalse(ByteUtils.TryParseUInt256DecimalChars("", out _, out _, out _, out _));
        Assert.IsFalse(ByteUtils.TryParseUInt256DecimalUtf8(ReadOnlySpan<byte>.Empty, out _, out _, out _, out _));
    }

    [TestMethod]
    public void DecimalParse_InvalidCharacterAtEveryPosition_IsRejected()
    {
        // Characters adjacent to the digit range, digits' high-nibble look-alikes, whitespace, signs, and non-ASCII.
        char[] badChars = ['/', ':', ';', '?', '@', 'a', 'A', 'x', ' ', '+', '-', '.', ',', '_', '\0', '٠', '０', 'İ', '±'];
        foreach (int length in new[] { 1, 5, 8, 9, 16, 19, 20, 27, 38, 39, 57, 78 })
        {
            string valid = new string('7', length);
            for (int position = 0; position < length; position++)
            {
                foreach (char bad in badChars)
                {
                    char[] chars = valid.ToCharArray();
                    chars[position] = bad;
                    string text = new string(chars);
                    Assert.IsFalse(ByteUtils.TryParseUInt256DecimalChars(text, out _, out _, out _, out _), $"chars len={length} pos={position} bad=U+{(int)bad:X4}");

                    if (bad < 0x80)
                    {
                        byte[] bytes = Encoding.ASCII.GetBytes(text);
                        Assert.IsFalse(ByteUtils.TryParseUInt256DecimalUtf8(bytes, out _, out _, out _, out _), $"utf8 len={length} pos={position} bad=U+{(int)bad:X4}");
                    }
                }

                // Raw non-ASCII bytes, including 0xB0..0xB9 (digit bytes with the top bit set) and 0xFA..0xFF (carry bait for SWAR).
                foreach (byte badByte in new byte[] { 0x80, 0xB0, 0xB7, 0xB9, 0xC0, 0xFA, 0xFF, 0x03, 0x37 ^ 0x40 })
                {
                    byte[] bytes = Encoding.ASCII.GetBytes(valid);
                    bytes[position] = badByte;
                    Assert.IsFalse(ByteUtils.TryParseUInt256DecimalUtf8(bytes, out _, out _, out _, out _), $"utf8 len={length} pos={position} byte=0x{badByte:X2}");
                }
            }
        }
    }

    [TestMethod]
    public void CombineEightDigitValues_MatchesIntParse()
    {
        var rng = new Random(13);
        Span<byte> digits = stackalloc byte[8];
        for (int i = 0; i < 200_000; i++)
        {
            ulong packed = 0;
            uint expected = 0;
            for (int k = 0; k < 8; k++)
            {
                uint d = (uint)(i < 20 ? (k == 7 ? 9 : 0) : rng.Next(10)); // include 00000009-style values
                digits[k] = (byte)d;
                packed |= (ulong)d << (8 * k);
                expected = expected * 10 + d;
            }

            Assert.AreEqual(expected, ByteUtils.CombineEightDigitValues(packed));
        }

        Assert.AreEqual(99_999_999u, ByteUtils.CombineEightDigitValues(0x0909090909090909UL));
        Assert.AreEqual(0u, ByteUtils.CombineEightDigitValues(0UL));
        Assert.AreEqual(12_345_678u, ByteUtils.CombineEightDigitValues(0x0807060504030201UL));
    }

    // ---- hex parsing ----

    [TestMethod]
    public void HexParse_RandomStrings_AllLengths_MatchesBigInteger()
    {
        var rng = new Random(97);
        const string alphabet = "0123456789abcdefABCDEF";
        for (int i = 0; i < 30_000; i++)
        {
            int length = rng.Next(1, 65);
            var sb = new StringBuilder("0x");
            for (int k = 0; k < length; k++)
                sb.Append(alphabet[rng.Next(alphabet.Length)]);
            string text = sb.ToString();
            BigInteger expected = BigInteger.Parse("0" + text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            Assert.IsTrue(uint256.TryParse(text.AsSpan(), out uint256 viaChars), text);
            Assert.IsTrue(uint256.TryParse(Encoding.ASCII.GetBytes(text), out uint256 viaUtf8), text);
            Assert.AreEqual(expected, viaChars.ToBigInteger(), text);
            Assert.AreEqual(viaChars, viaUtf8, text);
        }
    }

    [TestMethod]
    public void HexParse_InvalidCharacterAtEveryPosition_IsRejected()
    {
        // U+FF10 (fullwidth '0') truncates to 0x10 and U+0130 to 0x30 ('0'): they must not alias onto valid digits.
        // Whitespace and control characters matter for the 64-digit path (Convert.FromHexString): inside the digits they must be rejected.
        char[] badChars = ['g', 'G', 'x', '/', ':', '@', '`', '\0', '\uFF10', '\u0130', '\uFF41', '\u0161', '\t', '\n', '\r', '\v', '\u00A0', '\u0085', '\u2003', ' '];
        foreach (int length in new[] { 1, 8, 15, 16, 17, 31, 32, 33, 48, 63, 64 })
        {
            string valid = new string('a', length);
            for (int position = 0; position < length; position++)
            {
                foreach (char bad in badChars)
                {
                    char[] chars = valid.ToCharArray();
                    chars[position] = bad;
                    string text = "0x" + new string(chars);
                    // Trailing whitespace is stripped by design, so a whitespace character in the last digit position
                    // must parse to the value of the remaining digits (and never be decoded as a digit).
                    BigInteger withoutLast = length == 1
                        ? BigInteger.Zero
                        : BigInteger.Parse("0" + new string('a', length - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

                    bool trailingWhitespaceTrimmed = position == length - 1 && char.IsWhiteSpace(bad);
                    if (trailingWhitespaceTrimmed)
                    {
                        Assert.IsTrue(uint256.TryParse(text.AsSpan(), out uint256 trimmed), $"chars len={length} trailing U+{(int)bad:X4}");
                        Assert.AreEqual(withoutLast, trimmed.ToBigInteger(), $"chars len={length} trailing U+{(int)bad:X4}");
                    }
                    else
                    {
                        Assert.IsFalse(uint256.TryParse(text.AsSpan(), out _), $"chars len={length} pos={position} bad=U+{(int)bad:X4}");
                    }

                    if (bad < 0x80)
                    {
                        bool utf8TrailingTrimmed = position == length - 1 && (bad is ' ' or '\t' or '\n' or '\r');
                        byte[] utf8Text = Encoding.ASCII.GetBytes(text);
                        if (utf8TrailingTrimmed)
                        {
                            Assert.IsTrue(uint256.TryParse(utf8Text, out uint256 trimmedUtf8), $"utf8 len={length} trailing U+{(int)bad:X4}");
                            Assert.AreEqual(withoutLast, trimmedUtf8.ToBigInteger(), $"utf8 len={length} trailing U+{(int)bad:X4}");
                        }
                        else
                        {
                            Assert.IsFalse(uint256.TryParse(utf8Text, out _), $"utf8 len={length} pos={position} bad=U+{(int)bad:X4}");
                        }
                    }
                }
            }
        }
    }

    // ---- formatting ----

    [TestMethod]
    public void CountDigits_MatchesUInt64ToString_AtEveryPowerOfTenBoundary()
    {
        var values = new List<ulong> { 0, 1, 9, 10, 99, 100, ulong.MaxValue, ulong.MaxValue - 1, 1UL << 63, (1UL << 63) - 1 };
        ulong power = 1;
        for (int i = 0; i < 20; i++)
        {
            values.Add(power - 1);
            values.Add(power);
            values.Add(power + 1);
            if (i < 19) power *= 10;
        }

        for (int bit = 0; bit < 64; bit++)
        {
            values.Add(1UL << bit);
            values.Add((1UL << bit) - 1);
        }

        var rng = new Random(3);
        for (int i = 0; i < 100_000; i++)
            values.Add(((ulong)rng.NextInt64() ^ ((ulong)rng.NextInt64() << 1)) >> rng.Next(64)); // log-uniform digit counts

        foreach (ulong v in values)
            Assert.AreEqual(v.ToString(CultureInfo.InvariantCulture).Length, UInt256Formatting.CountDigits(v), v.ToString());
    }

    [TestMethod]
    public void UInt256_Format_Random_MatchesBigInteger_InAllThreeOutputs()
    {
        var rng = new Random(555);
        Span<char> chars = stackalloc char[80];
        Span<byte> utf8 = stackalloc byte[80];
        for (int i = 0; i < 80_000; i++)
        {
            uint256 value = RandomUInt256(rng);
            BigInteger big = value.ToBigInteger();
            string decimalText = big.ToString(CultureInfo.InvariantCulture);
            string hex = ExpectedHex(big);

            Assert.AreEqual(decimalText, value.ToDecimalString(), decimalText);
            Assert.AreEqual(decimalText, value.ToString("D", CultureInfo.InvariantCulture));
            Assert.AreEqual("0x" + hex, value.ToString());
            Assert.AreEqual(hex, value.ToString("x", CultureInfo.InvariantCulture));
            Assert.AreEqual(hex.ToUpperInvariant(), value.ToString("X", CultureInfo.InvariantCulture));
            Assert.AreEqual(big.ToString("x64", CultureInfo.InvariantCulture).Substring(big.ToString("x64", CultureInfo.InvariantCulture).Length - 64), value.ToFullHexString());

            Assert.IsTrue(value.TryFormat(chars, out int written, "D"));
            Assert.AreEqual(decimalText, new string(chars.Slice(0, written)));

            Assert.IsTrue(value.TryFormat(utf8, out int bytesWritten, "0x"));
            Assert.AreEqual("0x" + hex, Encoding.ASCII.GetString(utf8.Slice(0, bytesWritten)));
        }
    }

    [TestMethod]
    public void Int256_Format_Random_MatchesBigInteger()
    {
        var rng = new Random(556);
        Span<char> chars = stackalloc char[80];
        for (int i = 0; i < 80_000; i++)
        {
            uint256 raw = RandomUInt256(rng);
            var value = new int256(raw.U0, raw.U1, raw.U2, raw.U3);
            BigInteger big = value.ToBigInteger();
            string decimalText = big.ToString(CultureInfo.InvariantCulture);

            Assert.AreEqual(decimalText, value.ToString(), decimalText);
            Assert.AreEqual(decimalText, value.ToString("D", CultureInfo.InvariantCulture));

            // Hex: positive = minimal magnitude, negative = full-width two's complement.
            BigInteger twos = big.Sign < 0 ? big + Two256 : big;
            string expectedHex = big.Sign < 0 ? twos.ToString("x", CultureInfo.InvariantCulture).TrimStart('0') : ExpectedHex(big);
            Assert.AreEqual("0x" + expectedHex, value.ToString("0x", CultureInfo.InvariantCulture));
            if (big.Sign < 0)
                Assert.AreEqual(64, expectedHex.Length);

            Assert.IsTrue(value.TryFormat(chars, out int written, "D"));
            Assert.AreEqual(decimalText, new string(chars.Slice(0, written)));
        }
    }

    [TestMethod]
    public void Format_PowersOfTenAndTheirNeighbours_AreExact()
    {
        for (int exponent = 0; exponent <= 77; exponent++)
        {
            BigInteger power = BigInteger.Pow(10, exponent);
            foreach (BigInteger value in new[] { power - 1, power, power + 1 })
            {
                if (value.Sign < 0 || value >= Two256) continue;
                uint256 u = (uint256)value;
                Assert.AreEqual(value.ToString(CultureInfo.InvariantCulture), u.ToDecimalString(), $"10^{exponent}");

                if (value < Two255)
                {
                    int256 i = (int256)value;
                    Assert.AreEqual(value.ToString(CultureInfo.InvariantCulture), i.ToString());
                    Assert.AreEqual((-value).ToString(CultureInfo.InvariantCulture), (-i).ToString());
                }
            }
        }
    }
}
