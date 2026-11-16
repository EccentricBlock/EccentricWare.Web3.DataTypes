using System.Globalization;
using System.Numerics;
using static EccentricWare.Web3.DataTypes.Tests.Int256TestValues;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Text parsing of <see cref="int256"/> from UTF-16: decimal, hex, whitespace, failure results and API parity.</summary>
[TestClass]
public sealed class Int256ParseTests
{
    private static readonly string MaxDec = (Two255 - 1).ToString(CultureInfo.InvariantCulture);
    private static readonly string MinDec = (-Two255).ToString(CultureInfo.InvariantCulture);
    private static string Zeros(int n) => new('0', n);
    private static string Fs(int n) => new('f', n);
    private static string Digits(int n, char c = '7') => new(c, n);

    private static object[] Row(string text, BigInteger expected) => [text, expected];

    public static IEnumerable<object[]> ValidDecimal()
    {
        yield return Row("0", 0);
        yield return Row("+0", 0);
        yield return Row("-0", 0);
        yield return Row(Zeros(100), 0);
        yield return Row(Zeros(100) + "5", 5);
        yield return Row("-" + Zeros(100) + "7", -7);
        yield return Row("10", 10);
        yield return Row("-42", -42);
        yield return Row("+42", 42);
        yield return Row("  +42  ", 42);
        yield return Row("\t\n -42 \r", -42);
        yield return Row(MaxDec, Two255 - 1);
        yield return Row("+" + MaxDec, Two255 - 1);
        yield return Row(MinDec, -Two255);
        yield return Row("-" + (Two255 - 1), -(Two255 - 1));

        // The digit parser works in 19-digit chunks: probe either side of every chunk boundary.
        foreach (int n in new[] { 18, 19, 20, 37, 38, 39, 56, 57, 58, 75, 76, 77 })
        {
            foreach (BigInteger v in new[] { BigInteger.Pow(10, n - 1), BigInteger.Pow(10, n) - 1 })
            {
                if (!Fits(v)) continue;
                yield return Row(v.ToString(CultureInfo.InvariantCulture), v);
                yield return Row("-" + v.ToString(CultureInfo.InvariantCulture), -v);
            }
        }
    }

    public static IEnumerable<object[]> InvalidDecimal()
    {
        foreach (string s in new[]
        {
            "", "   ", "-", "+", "-   ", "+-5", "-+5", "--5", "++5", "5-", "5+", "1 2", "1,000", "1_000", "1e3", "1.0", ".5", "abc",
            "\uFF15", "\u0663", "\u00B2", "\u22125", "5\0", "\05", "5\u0301", "0b1", "1-1",
        })
            yield return [s];

        yield return [Two255.ToString()];                                      // 2^255
        yield return ["-" + (Two255 + 1)];                                     // -(2^255 + 1)
        yield return [(Two256 - 1).ToString()];
        yield return ["-" + (Two256 - 1)];
        yield return [Two256.ToString()];
        yield return [BigInteger.Pow(10, 77).ToString()];                      // 78 digits
        yield return ["1" + Zeros(100)];
        yield return [Digits(1000, '1')];
        yield return ["-" + Digits(1000, '1')];
    }

    public static IEnumerable<object[]> ValidHex()
    {
        yield return Row("0x", 0);
        yield return Row("0x0", 0);
        yield return Row("0x" + Zeros(64), 0);
        yield return Row("0X" + Zeros(64), 0);
        yield return Row("0xFF", 255);
        yield return Row("0XFF", 255);
        yield return Row("0xAbCdEf", 0xABCDEF);
        yield return Row("+0x1", 1);
        yield return Row("-0x1", -1);
        yield return Row("-0x01", -1);
        yield return Row("-0x0", 0);
        yield return Row("-0x" + Zeros(64), 0);
        yield return Row("0x" + Fs(64), -1);                                   // 64 digits: raw two's complement
        yield return Row("0x" + Fs(63), P252 - 1);                             // 63 digits: positive magnitude
        yield return Row("0x8" + Zeros(63), -Two255);
        yield return Row("0x7" + Fs(63), Two255 - 1);
        yield return Row("-0x8" + Zeros(63), -Two255);
        yield return Row("-0x7" + Fs(63), -(Two255 - 1));
        yield return Row(" 0x1 ", 1);
        yield return Row("\t\n0x1\r", 1);
        yield return Row("0x" + Zeros(63) + "1", 1);
        yield return Row("0x" + Zeros(60) + "ffff", 0xFFFF);
        yield return Row("0x" + "0123456789abcdef", 0x0123456789ABCDEFUL);
        yield return Row("0x" + "0123456789abcdef0123456789abcdef", BigInteger.Parse("00123456789abcdef0123456789abcdef", NumberStyles.HexNumber));
    }

    private static readonly BigInteger P252 = Pow2(252);

    public static IEnumerable<object[]> InvalidHex()
    {
        foreach (string s in new[]
        {
            "0x-1", "-0x-1", "0x 1", "0x1 1", "0xg", "0x1g", "0xZZ", "0x" + "z" + Zeros(63), "0x0x1", "0x+1", "-0x8" + Zeros(62) + "1",
            "0x" + Zeros(65), "0x" + Fs(65), "0X" + Fs(100), "-0x" + Fs(64), "-0x" + Zeros(65), "0x\0", "0x\uFF11", "0x\u0663", "0x\u00E9",
        })
            yield return [s];
    }

    private static void AssertParsesInAllCharOverloads(string text, BigInteger expected)
    {
        int256 want = FromBig(expected);
        Assert.AreEqual(want, int256.Parse(text), $"Parse(string) '{text}'");
        Assert.AreEqual(want, int256.Parse(text, null), $"Parse(string, provider) '{text}'");
        Assert.AreEqual(want, int256.Parse(text.AsSpan()), $"Parse(span) '{text}'");
        Assert.AreEqual(want, int256.Parse(text.AsSpan(), CultureInfo.InvariantCulture), $"Parse(span, provider) '{text}'");

        Assert.IsTrue(int256.TryParse(text, null, out int256 viaString), $"TryParse(string) '{text}'");
        Assert.AreEqual(want, viaString);
        Assert.IsTrue(int256.TryParse(text.AsSpan(), null, out int256 viaSpan), $"TryParse(span) '{text}'");
        Assert.AreEqual(want, viaSpan);
    }

    private static void AssertRejectedInAllCharOverloads(string text)
    {
        Assert.ThrowsExactly<FormatException>(() => int256.Parse(text));
        Assert.ThrowsExactly<FormatException>(() => int256.Parse(text, CultureInfo.InvariantCulture));
        Assert.ThrowsExactly<FormatException>(() => int256.Parse(text.AsSpan()));
        Assert.ThrowsExactly<FormatException>(() => int256.Parse(text.AsSpan(), CultureInfo.InvariantCulture));

        Assert.IsFalse(int256.TryParse(text, null, out int256 viaString), $"TryParse(string) '{text}'");
        Assert.AreEqual(int256.Zero, viaString);
        Assert.IsFalse(int256.TryParse(text.AsSpan(), null, out int256 viaSpan), $"TryParse(span) '{text}'");
        Assert.AreEqual(int256.Zero, viaSpan);
    }

    [TestMethod]
    [DynamicData(nameof(ValidDecimal))]
    public void Decimal_Valid_ParsesThroughEveryCharOverload(string text, BigInteger expected)
    {
        AssertParsesInAllCharOverloads(text, expected);

        Assert.IsTrue(int256.TryParseDecimal(text, out int256 viaString));
        Assert.AreEqual(FromBig(expected), viaString);
        Assert.IsTrue(int256.TryParseDecimal(text.AsSpan(), out int256 viaSpan));
        Assert.AreEqual(FromBig(expected), viaSpan);
    }

    [TestMethod]
    [DynamicData(nameof(InvalidDecimal))]
    public void Decimal_Invalid_IsRejectedByEveryCharOverload_LeavingZero(string text)
    {
        AssertRejectedInAllCharOverloads(text);

        Assert.IsFalse(int256.TryParseDecimal(text, out int256 viaString));
        Assert.AreEqual(int256.Zero, viaString);
        Assert.IsFalse(int256.TryParseDecimal(text.AsSpan(), out int256 viaSpan));
        Assert.AreEqual(int256.Zero, viaSpan);
    }

    [TestMethod]
    [DynamicData(nameof(ValidHex))]
    public void Hex_Valid_ParsesThroughEveryCharOverload(string text, BigInteger expected)
        => AssertParsesInAllCharOverloads(text, expected);

    [TestMethod]
    [DynamicData(nameof(InvalidHex))]
    public void Hex_Invalid_IsRejectedByEveryCharOverload_LeavingZero(string text)
        => AssertRejectedInAllCharOverloads(text);

    [TestMethod]
    public void TryParseDecimal_NullString_ReturnsFalseWithZero()
    {
        Assert.IsFalse(int256.TryParseDecimal(null, out int256 result));
        Assert.AreEqual(int256.Zero, result);
    }

    [TestMethod]
    public void TryParseDecimal_RejectsHexPrefix_ThatTryParseAccepts()
    {
        Assert.IsFalse(int256.TryParseDecimal("0x10", out _));
        Assert.IsTrue(int256.TryParse("0x10".AsSpan(), null, out int256 hex));
        Assert.AreEqual(new int256(16), hex);
    }

    [TestMethod]
    public void TryParseString_Null_ReturnsFalseWithZero()
    {
        Assert.IsFalse(int256.TryParse((string?)null, CultureInfo.InvariantCulture, out int256 r));
        Assert.AreEqual(int256.Zero, r);
    }

    [TestMethod]
    public void Parse_ErrorMessage_IsMeaningful()
    {
        var ex = Assert.ThrowsExactly<FormatException>(() => int256.Parse("not a number"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));
    }

    [TestMethod]
    public void TwoPowers_AtTheDecimalAndHexEdges_AgreeAcrossBases()
    {
        // The same boundary values written in decimal and hex must give the same int256.
        Assert.AreEqual(int256.MaxValue, int256.Parse("0x7" + Fs(63)));
        Assert.AreEqual(int256.Parse(MaxDec), int256.Parse("0x7" + Fs(63)));
        Assert.AreEqual(int256.Parse(MinDec), int256.Parse("0x8" + Zeros(63)));
        Assert.AreEqual(int256.Parse(MinDec), int256.Parse("-0x8" + Zeros(63)));
        Assert.AreEqual(int256.MinusOne, int256.Parse("-1"));
        Assert.AreEqual(int256.Parse("-1"), int256.Parse("0x" + Fs(64)));
    }

    // ---- whitespace: the char path trims Unicode whitespace ----

    [TestMethod]
    [DataRow("\f5")]
    [DataRow("\v5")]
    [DataRow("\u00A05\u00A0")]
    [DataRow("\u20035\u2003")]
    [DataRow("\u30005")]
    [DataRow("5\u2028")]
    public void CharPath_TrimsUnicodeWhitespaceAroundTheValue(string text)
    {
        Assert.IsTrue(int256.TryParse(text.AsSpan(), null, out int256 r));
        Assert.AreEqual(new int256(5), r);
        Assert.IsTrue(int256.TryParseDecimal(text, out int256 d));
        Assert.AreEqual(new int256(5), d);
    }

    [TestMethod]
    [DataRow("1\u00A02")]
    [DataRow("1\t2")]
    [DataRow("0x1\u00A02")]
    public void Whitespace_InsideTheDigits_IsRejected(string text) => AssertRejectedInAllCharOverloads(text);

    // ---- provider independence ----

    [TestMethod]
    public void Parse_IgnoresTheFormatProvider()
    {
        var exotic = new NumberFormatInfo { NegativeSign = "neg", PositiveSign = "pos" };

        Assert.AreEqual(new int256(-5), int256.Parse("-5", exotic));
        Assert.AreEqual(new int256(-5), int256.Parse("-5".AsSpan(), exotic));
        Assert.IsFalse(int256.TryParse("neg5", exotic, out _));
        Assert.IsFalse(int256.TryParse("pos5", exotic, out _));
        Assert.IsFalse(int256.TryParse("\u22125", new NumberFormatInfo { NegativeSign = "\u2212" }, out _));
        Assert.AreEqual(new int256(255), int256.Parse("0xff", new CultureInfo("ar-SA")));
        Assert.AreEqual(new int256(-1234), int256.Parse("-1234", new CultureInfo("fa-IR")));
    }

    // ---- generic interfaces ----

    private static T ParseGeneric<T>(string s) where T : IParsable<T> => T.Parse(s, null);

    private static bool TryParseGeneric<T>(string? s, out T? value) where T : IParsable<T> => T.TryParse(s, null, out value);

    private static T ParseSpanGeneric<T>(ReadOnlySpan<char> s) where T : ISpanParsable<T> => T.Parse(s, null);

    private static bool TryParseSpanGeneric<T>(ReadOnlySpan<char> s, out T? value) where T : ISpanParsable<T> => T.TryParse(s, null, out value);

    [TestMethod]
    public void ParsableInterfaces_ForwardToTheSameBehaviour()
    {
        Assert.AreEqual(new int256(-7), ParseGeneric<int256>("-7"));
        Assert.AreEqual(new int256(-7), ParseSpanGeneric<int256>("-7"));
        Assert.ThrowsExactly<FormatException>(() => ParseGeneric<int256>("x"));
        Assert.ThrowsExactly<FormatException>(() => ParseSpanGeneric<int256>("x"));

        Assert.IsTrue(TryParseGeneric("0x" + Fs(64), out int256 viaString));
        Assert.AreEqual(int256.MinusOne, viaString);
        Assert.IsFalse(TryParseGeneric<int256>(null, out int256 nullResult));
        Assert.AreEqual(int256.Zero, nullResult);
        Assert.IsTrue(TryParseSpanGeneric("12", out int256 viaSpan));
        Assert.AreEqual(new int256(12), viaSpan);
        Assert.IsFalse(TryParseSpanGeneric("", out int256 emptyResult));
        Assert.AreEqual(int256.Zero, emptyResult);
    }
}
