using System.Numerics;
using System.Text;
using static EccentricWare.Web3.DataTypes.Tests.Int256ReferenceParser;
using static EccentricWare.Web3.DataTypes.Tests.Int256TestValues;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Differential tests of every int256 text parser against <see cref="Int256ReferenceParser"/> (an independent grammar
/// model built on BigInteger arithmetic), with fixed seeds, plus exhaustive single-byte substitution sweeps.
/// </summary>
[TestClass]
public sealed class Int256ParseDifferentialFuzzTests
{
    private const string Alphabet = "0123456789abcdefABCDEFxX+- \t\r\n_";

    private static string RandomText(Random rng)
    {
        string text;
        switch (rng.Next(5))
        {
            case 0: // decimal digits, optionally with leading zeros and a sign
                text = Sign(rng) + new string('0', rng.Next(3)) + RandomDigits(rng, "0123456789", 1 + rng.Next(80));
                break;
            case 1: // hex with prefix, any case
                text = Sign(rng) + (rng.Next(2) == 0 ? "0x" : "0X") + RandomDigits(rng, "0123456789abcdefABCDEF", rng.Next(67));
                break;
            case 2: // a value near the int256 limits, written in decimal, minimal hex or full-width hex
            {
                BigInteger v = (rng.Next(2) == 0 ? Two255 : -Two255) + rng.Next(-3, 4);
                text = rng.Next(3) switch
                {
                    0 => v.ToString(),
                    1 => (v < 0 ? "-" : "") + "0x" + Hex(BigInteger.Abs(v)),
                    _ => "0x" + Hex(v & (Two256 - 1), 64),
                };
                break;
            }
            case 3: // random string over the whole alphabet
                text = RandomDigits(rng, Alphabet, rng.Next(14));
                break;
            default: // random value anywhere in the range
            {
                BigInteger v = FromBigOrRandom(rng);
                text = rng.Next(2) == 0 ? v.ToString() : (v < 0 ? "-0x" + Hex(-v) : "0x" + Hex(v));
                break;
            }
        }

        if (rng.Next(3) == 0) // mutate: replace, insert or delete one character
        {
            int at = rng.Next(text.Length + 1);
            char c = Alphabet[rng.Next(Alphabet.Length)];
            text = rng.Next(3) switch
            {
                0 when text.Length > 0 => text[..Math.Min(at, text.Length - 1)] + c + text[Math.Min(at + 1, text.Length)..],
                1 => text[..at] + c + text[at..],
                _ when text.Length > 0 => text.Remove(Math.Min(at, text.Length - 1), 1),
                _ => text,
            };
        }

        string[] padding = ["", " ", "\t", "\r\n", "  \t"];
        return padding[rng.Next(5)] + text + padding[rng.Next(5)];
    }

    private static BigInteger FromBigOrRandom(Random rng)
    {
        byte[] bytes = new byte[32];
        rng.NextBytes(bytes);
        bytes[0] = (byte)(bytes[0] >> rng.Next(8)); // vary the magnitude
        BigInteger v = new(bytes, isUnsigned: true, isBigEndian: true);
        return rng.Next(2) == 0 ? v : -v;
    }

    private static string Sign(Random rng) => rng.Next(4) switch { 0 => "-", 1 => "+", _ => "" };

    private static string RandomDigits(Random rng, string alphabet, int count)
    {
        var sb = new StringBuilder(count);
        for (int i = 0; i < count; i++) sb.Append(alphabet[rng.Next(alphabet.Length)]);
        return sb.ToString();
    }

    private static void AssertAgrees(string description, Outcome expected, bool parsed, int256 actual)
    {
        if (expected.Kind == Kind.Unspecified) return;

        if (expected.Kind == Kind.Valid)
        {
            Assert.IsTrue(parsed, $"{description}: expected {expected.Value}, parser rejected");
            Assert.AreEqual(expected.Value, ToBig(actual), description);
        }
        else
        {
            Assert.IsFalse(parsed, $"{description}: expected rejection, parser returned {actual}");
            Assert.AreEqual(int256.Zero, actual, $"{description}: failure must leave Zero");
        }
    }

    [TestMethod]
    public void RandomText_AllParsers_AgreeWithTheReferenceGrammar()
    {
        var rng = new Random(2560_001);
        int valid = 0, invalid = 0;
        for (int i = 0; i < 20_000; i++)
        {
            string text = RandomText(rng);
            Outcome expected = Parse(text, IsAsciiWhitespace);
            string shown = $"'{text.Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n")}'";

            bool ok = int256.TryParse(text.AsSpan(), null, out int256 viaChars);
            AssertAgrees("TryParse(char) " + shown, expected, ok, viaChars);

            ok = int256.TryParse(text, null, out int256 viaString);
            AssertAgrees("TryParse(string) " + shown, expected, ok, viaString);

            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            ok = int256.TryParse(utf8, out int256 viaUtf8);
            AssertAgrees("TryParse(utf8) " + shown, expected, ok, viaUtf8);

            ok = int256.TryParseUnquoted(utf8, out int256 viaUnquoted);
            AssertAgrees("TryParseUnquoted " + shown, expected, ok, viaUnquoted);

            ok = int256.TryParse(Encoding.UTF8.GetBytes("\"" + text + "\""), out int256 viaQuoted);
            AssertAgrees("TryParse(quoted utf8) " + shown, expected, ok, viaQuoted);

            Outcome decimalOnly = ParseDecimalOnly(text, IsAsciiWhitespace);
            ok = int256.TryParseDecimal(text, out int256 viaDecimal);
            AssertAgrees("TryParseDecimal " + shown, decimalOnly, ok, viaDecimal);

            if (expected.Kind == Kind.Valid) valid++; else if (expected.Kind == Kind.Invalid) invalid++;
        }

        // The generator must exercise both outcomes in volume, or the comparison proves little.
        Assert.IsGreaterThan(4_000, valid);
        Assert.IsGreaterThan(4_000, invalid);
    }

    [TestMethod]
    public void RandomValidValues_RoundTripThroughEveryTextForm()
    {
        var rng = new Random(2560_002);
        for (int i = 0; i < 5_000; i++)
        {
            BigInteger v = FromBigOrRandom(rng);
            if (!Fits(v)) v = Wrap(v);
            int256 want = FromBig(v);

            string[] forms =
            [
                v.ToString(),
                "0x" + Hex(v & (Two256 - 1), 64),                 // raw 64-digit two's complement
                (v < 0 ? "-" : "") + "0x" + Hex(BigInteger.Abs(v)), // signed magnitude
                (v < 0 ? "-" : "") + "0X" + Hex(BigInteger.Abs(v)).ToUpperInvariant(),
            ];

            foreach (string form in forms)
            {
                Assert.AreEqual(want, int256.Parse(form), form);
                Assert.IsTrue(int256.TryParse(Encoding.ASCII.GetBytes(form), out int256 viaUtf8), form);
                Assert.AreEqual(want, viaUtf8, form);
                Assert.IsTrue(int256.TryParse(Encoding.ASCII.GetBytes("\"" + form + "\""), out int256 viaQuoted), form);
                Assert.AreEqual(want, viaQuoted, form);
            }

            Assert.IsTrue(int256.TryParseDecimal(forms[0], out int256 viaDecimal));
            Assert.AreEqual(want, viaDecimal);
        }
    }

    // ---- single-byte substitution sweeps ----

    private static bool IsHexDigitChar(int b) => b is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    [TestMethod]
    public void Utf8_EveryNonDigitByte_AtEveryPosition_IsRejectedExactlyWhenTheReferenceRejects()
    {
        foreach (int length in new[] { 1, 7, 8, 9, 16, 19, 20, 27, 38, 76, 77 })
        {
            for (int pos = 0; pos < length; pos++)
            {
                for (int b = 0; b < 256; b++)
                {
                    if (b is >= '0' and <= '9') continue;

                    byte[] text = Enumerable.Repeat((byte)'1', length).ToArray();
                    text[pos] = (byte)b;
                    Outcome expected = Parse(Encoding.Latin1.GetString(text), IsAsciiWhitespace);

                    bool ok = int256.TryParse(text, out int256 value);
                    AssertAgrees($"len={length} pos={pos} byte=0x{b:X2}", expected, ok, value);
                }
            }
        }
    }

    [TestMethod]
    public void Utf8_EveryNonHexByte_InsideAHexLiteral_IsRejectedExactlyWhenTheReferenceRejects()
    {
        // Includes the neighbours of the digit ranges ('/', ':', '@', 'G', '`', 'g') that range-check tricks get wrong.
        foreach (int digits in new[] { 1, 8, 16, 17, 32, 63, 64 })
        {
            int total = 2 + digits;
            for (int pos = 0; pos < total; pos++)
            {
                for (int b = 0; b < 256; b++)
                {
                    if (pos >= 2 && IsHexDigitChar(b)) continue;

                    byte[] text = Encoding.ASCII.GetBytes("0x" + new string('a', digits));
                    text[pos] = (byte)b;
                    Outcome expected = Parse(Encoding.Latin1.GetString(text), IsAsciiWhitespace);

                    bool ok = int256.TryParse(text, out int256 value);
                    AssertAgrees($"digits={digits} pos={pos} byte=0x{b:X2}", expected, ok, value);
                }
            }
        }
    }

    [TestMethod]
    public void Chars_RepresentativeNonDigits_AtEveryPosition_AreRejectedExactlyWhenTheReferenceRejects()
    {
        char[] probes =
        [
            '\0', '/', ':', '@', 'G', 'g', '`', '\u00B2', '\u0660', '\u0663', '\uFF10', '\uFF15', '\u2212', '\uFFFF', ' ', '\u00A0',
            '\f', '\v', '+', '-', '_', ',', '.', 'x', 'X',
        ];

        foreach (bool hex in new[] { false, true })
        {
            foreach (int length in new[] { 1, 8, 19, 20, 38, 64, 77 })
            {
                string baseText = hex ? "0x" + new string('a', length) : new string('1', length);
                if (!hex && length > 77) continue;

                for (int pos = 0; pos < baseText.Length; pos++)
                {
                    foreach (char probe in probes)
                    {
                        char[] text = baseText.ToCharArray();
                        text[pos] = probe;
                        string s = new(text);
                        Outcome expected = Parse(s, char.IsWhiteSpace);

                        bool ok = int256.TryParse(s.AsSpan(), null, out int256 value);
                        AssertAgrees($"hex={hex} len={length} pos={pos} char=U+{(int)probe:X4}", expected, ok, value);
                    }
                }
            }
        }
    }
}
