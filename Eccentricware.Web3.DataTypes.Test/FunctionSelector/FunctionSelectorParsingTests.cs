using System.Globalization;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Hex parsing: TryParse/Parse (chars, UTF-8, string, IParsable overloads) and TryParseFromCalldataHexUtf8.</summary>
[TestClass]
public sealed class FunctionSelectorParsingTests
{
    private const string Valid = "a9059cbb";
    private const uint ValidValue = 0xa9059cbbu;

    // Independent oracle: the BCL's own hex digit rules, restricted to ASCII.
    private static bool IsAsciiHexDigit(int c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static void AssertRejectedEverywhere(string text, string? label = null)
    {
        label ??= text;
        Assert.IsFalse(FunctionSelector.TryParse(text.AsSpan(), out var r1), "chars: " + label);
        Assert.AreEqual(FunctionSelector.Zero, r1, "chars result: " + label);
        Assert.IsFalse(FunctionSelector.TryParse(Encoding.UTF8.GetBytes(text), out var r2), "utf8: " + label);
        Assert.AreEqual(FunctionSelector.Zero, r2, "utf8 result: " + label);
        Assert.IsFalse(FunctionSelector.TryParse(text, out var r3), "string: " + label);
        Assert.AreEqual(FunctionSelector.Zero, r3, "string result: " + label);
        Assert.IsFalse(FunctionSelector.TryParse(text, CultureInfo.InvariantCulture, out var r4), "string+provider: " + label);
        Assert.AreEqual(FunctionSelector.Zero, r4, "string+provider result: " + label);
        Assert.IsFalse(FunctionSelector.TryParse(text.AsSpan(), CultureInfo.InvariantCulture, out var r5), "span+provider: " + label);
        Assert.AreEqual(FunctionSelector.Zero, r5, "span+provider result: " + label);

        Assert.ThrowsExactly<FormatException>(() => FunctionSelector.Parse(text.AsSpan()), "Parse chars: " + label);
        Assert.ThrowsExactly<FormatException>(() => FunctionSelector.Parse(Encoding.UTF8.GetBytes(text)), "Parse utf8: " + label);
        Assert.ThrowsExactly<FormatException>(() => FunctionSelector.Parse(text), "Parse string: " + label);
        Assert.ThrowsExactly<FormatException>(() => FunctionSelector.Parse(text, CultureInfo.InvariantCulture), "Parse string+provider: " + label);
        Assert.ThrowsExactly<FormatException>(() => FunctionSelector.Parse(text.AsSpan(), CultureInfo.InvariantCulture), "Parse span+provider: " + label);
    }

    // ----------------- exhaustive character / byte oracles -----------------

    [TestMethod]
    [DataRow("", 0)]
    [DataRow("", 7)]
    [DataRow("0x", 0)]
    [DataRow("0X", 7)]
    public void TryParse_EveryUtf16CodeUnit_InAnyDigitPosition_AcceptedExactlyWhenAsciiHex(string prefix, int position)
    {
        char[] digits = "1234abcd".ToCharArray();
        for (int c = 0; c <= char.MaxValue; c++)
        {
            digits[position] = (char)c;
            string text = prefix + new string(digits);
            bool ok = FunctionSelector.TryParse(text.AsSpan(), out var parsed);

            Assert.AreEqual(IsAsciiHexDigit(c), ok, $"U+{c:X4} at {position}");
            if (ok)
                Assert.AreEqual(Convert.ToUInt32(new string(digits), 16), parsed.Value, $"U+{c:X4} at {position}");
            else
                Assert.AreEqual(FunctionSelector.Zero, parsed, $"U+{c:X4} at {position}");
        }
    }

    [TestMethod]
    [DataRow("", 0)]
    [DataRow("", 7)]
    [DataRow("0x", 3)]
    public void TryParse_EveryByteValue_InAnyDigitPosition_AcceptedExactlyWhenAsciiHex(string prefix, int position)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(prefix + "1234abcd");
        int index = prefix.Length + position;
        for (int b = 0; b <= byte.MaxValue; b++)
        {
            bytes[index] = (byte)b;
            bool ok = FunctionSelector.TryParse(bytes, out var parsed);

            Assert.AreEqual(IsAsciiHexDigit(b), ok, $"byte {b:X2} at {position}");
            if (ok)
                Assert.AreEqual(Convert.ToUInt32(Encoding.ASCII.GetString(bytes, prefix.Length, 8), 16), parsed.Value, $"byte {b:X2} at {position}");
            else
                Assert.AreEqual(FunctionSelector.Zero, parsed, $"byte {b:X2} at {position}");
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(7)]
    public void TryParseFromCalldataHexUtf8_EveryByteValue_InFirstEightDigits_AcceptedExactlyWhenAsciiHex(int position)
    {
        byte[] bytes = Encoding.ASCII.GetBytes("0x1234abcd" + "00ff");
        for (int b = 0; b <= byte.MaxValue; b++)
        {
            bytes[2 + position] = (byte)b;
            bool ok = FunctionSelector.TryParseFromCalldataHexUtf8(bytes, out var parsed);

            Assert.AreEqual(IsAsciiHexDigit(b), ok, $"byte {b:X2} at {position}");
            Assert.AreEqual(ok ? Convert.ToUInt32(Encoding.ASCII.GetString(bytes, 2, 8), 16) : 0u, parsed.Value, $"byte {b:X2} at {position}");
        }
    }

    // ----------------- accepted shapes, against BCL-formatted values -----------------

    [TestMethod]
    public void Parse_AcceptsEveryCaseAndPrefixAndWhitespaceShape_ForEveryOverload()
    {
        var rng = new Random(2024);
        var values = new List<uint> { 0, 1, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF, 0x0000000A, 0xA0000000, 0x0F0F0F0F, 0xF0F0F0F0 };
        for (int i = 0; i < 200; i++)
            values.Add((uint)rng.NextInt64(0, uint.MaxValue + 1L));

        foreach (uint value in values)
        {
            string lower = value.ToString("x8", CultureInfo.InvariantCulture);
            string upper = value.ToString("X8", CultureInfo.InvariantCulture);
            string mixed = string.Concat(lower.Select((c, i) => i % 2 == 0 ? char.ToUpperInvariant(c) : c));

            foreach (string digits in new[] { lower, upper, mixed })
            foreach (string prefix in new[] { "", "0x", "0X" })
            foreach (string pad in new[] { "", " ", "\t", "\r\n", " \t\r\n " })
            {
                string text = pad + prefix + digits + pad;
                var expected = new FunctionSelector(value);

                Assert.AreEqual(expected, FunctionSelector.Parse(text.AsSpan()), text);
                Assert.AreEqual(expected, FunctionSelector.Parse(Encoding.ASCII.GetBytes(text)), text);
                Assert.AreEqual(expected, FunctionSelector.Parse(text), text);
                Assert.AreEqual(expected, FunctionSelector.Parse(text, null), text);
                Assert.AreEqual(expected, FunctionSelector.Parse(text.AsSpan(), CultureInfo.InvariantCulture), text);

                Assert.IsTrue(FunctionSelector.TryParse(text.AsSpan(), out var a) && a == expected, text);
                Assert.IsTrue(FunctionSelector.TryParse(Encoding.ASCII.GetBytes(text), out var b) && b == expected, text);
                Assert.IsTrue(FunctionSelector.TryParse(text, out var c) && c == expected, text);
                Assert.IsTrue(FunctionSelector.TryParse(text, null, out var d) && d == expected, text);
                Assert.IsTrue(FunctionSelector.TryParse(text.AsSpan(), null, out var e) && e == expected, text);
                Assert.IsTrue(FunctionSelector.TryParseFromCalldataHexUtf8(Encoding.ASCII.GetBytes(text), out var f) && f == expected, text);
            }
        }
    }

    // ----------------- rejected shapes -----------------

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("\t")]
    [DataRow("\r\n")]
    [DataRow(" \t\r\n ")]
    [DataRow("\v")]                // vertical tab and form feed are NOT in the trimmed set (space, \t, \r, \n)
    [DataRow("\f")]
    [DataRow(" \v ")]
    [DataRow("\0")]
    [DataRow("0x")]
    [DataRow("0X")]
    [DataRow(" 0x ")]
    public void Parse_EmptyWhitespaceOnlyOrPrefixOnly_IsRejectedEverywhere(string text) => AssertRejectedEverywhere(text);

    [TestMethod]
    [DataRow("\va9059cbb", "leading VT")]
    [DataRow("a9059cbb\v", "trailing VT")]
    [DataRow("\fa9059cbb", "leading FF")]
    [DataRow("a9059cbb\f", "trailing FF")]
    [DataRow("a9059cbb\0", "trailing NUL")]
    [DataRow("\0a9059cbb", "leading NUL")]
    [DataRow("\u00A0a9059cbb", "NBSP")]
    [DataRow("\u0085a9059cbb", "NEL")]
    [DataRow("\u2003a9059cbb", "EM SPACE")]
    [DataRow("a9059cbb\u2028", "LINE SEPARATOR")]
    [DataRow("a905 9cbb", "internal space")]
    [DataRow("a905\t9cbb", "internal tab")]
    [DataRow("0x a9059cbb", "space after prefix")]
    [DataRow("0 xa9059cbb", "space inside prefix")]
    [DataRow("+a9059cbb", "plus sign")]
    [DataRow("-a9059cbb", "minus sign")]
    [DataRow("+0xa9059cbb", "plus before prefix")]
    [DataRow("-0xa9059cbb", "minus before prefix")]
    [DataRow("0x+a9059cbb", "plus after prefix")]
    [DataRow("0x-a9059cb", "minus after prefix, 8 chars")]
    [DataRow("a9059c-b", "minus inside digits")]
    [DataRow("a9059cbb0x", "prefix at the end")]
    [DataRow("0xa9059cbb0x", "prefix at start and end")]
    [DataRow("0x0xa9059cbb", "doubled prefix")]
    [DataRow("0x0xa9059c", "doubled prefix, 8 chars")]
    [DataRow("00xa9059cbb", "prefix after a leading zero")]
    [DataRow("x0a9059cbb", "x before zero")]
    [DataRow("xa9059cbb", "bare x prefix")]
    [DataRow("0xxa9059cb", "0xx prefix")]
    [DataRow("a9059cb", "7 digits")]
    [DataRow("a9059cbba", "9 digits")]
    [DataRow("0xa9059cb", "7 digits after prefix")]
    [DataRow("0xa9059cbba", "9 digits after prefix")]
    [DataRow("0xa9059cbb00000000000000000000000000000000000000000000000000000000", "full calldata word")]
    [DataRow("\uFF10\uFF11\uFF12\uFF13\uFF14\uFF15\uFF16\uFF17", "full-width digits")]
    [DataRow("\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667", "Arabic-Indic digits")]
    [DataRow("0b1010101", "binary-looking")]
    [DataRow("0o1234567", "octal-looking")]
    [DataRow("1e3456789", "exponent-looking")]
    [DataRow("null", "json null")]
    [DataRow("\"a9059cbb\"", "quoted")]
    public void Parse_MalformedShape_IsRejectedEverywhere(string text, string label) => AssertRejectedEverywhere(text, label);

    [TestMethod]
    public void TryParse_DefaultSpans_Fail()
    {
        Assert.IsFalse(FunctionSelector.TryParse(default(ReadOnlySpan<char>), out var a));
        Assert.IsFalse(FunctionSelector.TryParse(default(ReadOnlySpan<byte>), out var b));
        Assert.AreEqual(FunctionSelector.Zero, a);
        Assert.AreEqual(FunctionSelector.Zero, b);
        Assert.ThrowsExactly<FormatException>(() => FunctionSelector.Parse(default(ReadOnlySpan<char>)));
        Assert.ThrowsExactly<FormatException>(() => FunctionSelector.Parse(default(ReadOnlySpan<byte>)));
    }

    [TestMethod]
    public void TryParse_FailureAfterASuccessfulParse_ResetsTheOutParameterToZero()
    {
        FunctionSelector result = FunctionSelector.Erc20.Transfer;

        Assert.IsFalse(FunctionSelector.TryParse("zzzzzzzz".AsSpan(), out result));
        Assert.AreEqual(FunctionSelector.Zero, result);

        result = FunctionSelector.Erc20.Transfer;
        Assert.IsFalse(FunctionSelector.TryParse("a9059cb"u8, out result));
        Assert.AreEqual(FunctionSelector.Zero, result);
    }

    [TestMethod]
    public void Parse_FormatException_NamesTheProblem()
    {
        var ex = Assert.ThrowsExactly<FormatException>(() => FunctionSelector.Parse("nope"));
        StringAssert.Contains(ex.Message, "function selector");
    }

    // ----------------- null strings -----------------

    [TestMethod]
    public void NullStrings_ParseThrowsArgumentNullException_AndTryParseReturnsFalseWithZero()
    {
        Assert.AreEqual("s", Assert.ThrowsExactly<ArgumentNullException>(() => FunctionSelector.Parse((string)null!)).ParamName);
        Assert.AreEqual("s", Assert.ThrowsExactly<ArgumentNullException>(() => FunctionSelector.Parse((string)null!, null)).ParamName);

        Assert.IsFalse(FunctionSelector.TryParse((string?)null, out var a));
        Assert.IsFalse(FunctionSelector.TryParse((string?)null, null, out var b));
        Assert.AreEqual(FunctionSelector.Zero, a);
        Assert.AreEqual(FunctionSelector.Zero, b);
    }

    [TestMethod]
    public void TryParse_String_ValidAndInvalid_UseTheSameRulesAsTheSpanOverloads()
    {
        Assert.IsTrue(FunctionSelector.TryParse(" 0X" + Valid.ToUpperInvariant() + "\n", out var parsed));
        Assert.AreEqual(ValidValue, parsed.Value);

        Assert.IsFalse(FunctionSelector.TryParse(string.Empty, out parsed));
        Assert.AreEqual(FunctionSelector.Zero, parsed);
    }

    // ----------------- TryParseFromCalldataHexUtf8 -----------------

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\t\r\n")]
    [DataRow("\v")]
    [DataRow("0x")]
    [DataRow("0X  ")]
    [DataRow("0xa9059cb")]
    [DataRow("a9059cb")]
    [DataRow("0xa9059cbg00")]
    [DataRow("0x a9059cbb")]
    [DataRow("\va9059cbb")]
    [DataRow("0x0xa9059cbb")]
    [DataRow("zzzzzzzz")]
    public void TryParseFromCalldataHexUtf8_Rejects_AndReturnsZero(string text)
    {
        Assert.IsFalse(FunctionSelector.TryParseFromCalldataHexUtf8(Encoding.UTF8.GetBytes(text), out var selector), text);
        Assert.AreEqual(FunctionSelector.Zero, selector, text);
    }

    [TestMethod]
    [DataRow("a9059cbb")]
    [DataRow("0xa9059cbb")]
    [DataRow("0Xa9059cbb")]
    [DataRow("  0xa9059cbb")]
    [DataRow("0xa9059cbb  ")]
    [DataRow("0xa9059cbb0000000000000000000000000000000000000000000000000000000000000001")]
    [DataRow("0xa9059cbbzz")]            // only the first 8 digits are read; the rest is not validated
    [DataRow("a9059cbb\0\0")]
    public void TryParseFromCalldataHexUtf8_ReadsOnlyTheFirstEightDigits(string text)
    {
        Assert.IsTrue(FunctionSelector.TryParseFromCalldataHexUtf8(Encoding.UTF8.GetBytes(text), out var selector), text);
        Assert.AreEqual(ValidValue, selector.Value, text);
    }
}
