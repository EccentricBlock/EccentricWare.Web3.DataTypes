using System.Globalization;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Hex parsing: TryParse/Parse for chars, UTF-8, string and the IParsable overloads (16 hex digits, optional 0x prefix).</summary>
[TestClass]
public sealed class InstructionDiscriminatorParsingTests
{
    private const string Valid = "afaf6d1f0d989bed";
    private const ulong ValidValue = 0xafaf6d1f0d989bedUL;

    private static bool IsAsciiHexDigit(int c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static void AssertRejectedEverywhere(string text, string? label = null)
    {
        label ??= text;
        Assert.IsFalse(InstructionDiscriminator.TryParse(text.AsSpan(), out var r1), "chars: " + label);
        Assert.AreEqual(InstructionDiscriminator.Zero, r1, "chars result: " + label);
        Assert.IsFalse(InstructionDiscriminator.TryParse(Encoding.UTF8.GetBytes(text), out var r2), "utf8: " + label);
        Assert.AreEqual(InstructionDiscriminator.Zero, r2, "utf8 result: " + label);
        Assert.IsFalse(InstructionDiscriminator.TryParse(text, out var r3), "string: " + label);
        Assert.AreEqual(InstructionDiscriminator.Zero, r3, "string result: " + label);
        Assert.IsFalse(InstructionDiscriminator.TryParse(text, CultureInfo.InvariantCulture, out var r4), "string+provider: " + label);
        Assert.AreEqual(InstructionDiscriminator.Zero, r4, "string+provider result: " + label);
        Assert.IsFalse(InstructionDiscriminator.TryParse(text.AsSpan(), CultureInfo.InvariantCulture, out var r5), "span+provider: " + label);
        Assert.AreEqual(InstructionDiscriminator.Zero, r5, "span+provider result: " + label);

        Assert.ThrowsExactly<FormatException>(() => InstructionDiscriminator.Parse(text.AsSpan()), "Parse chars: " + label);
        Assert.ThrowsExactly<FormatException>(() => InstructionDiscriminator.Parse(Encoding.UTF8.GetBytes(text)), "Parse utf8: " + label);
        Assert.ThrowsExactly<FormatException>(() => InstructionDiscriminator.Parse(text), "Parse string: " + label);
        Assert.ThrowsExactly<FormatException>(() => InstructionDiscriminator.Parse(text, CultureInfo.InvariantCulture), "Parse string+provider: " + label);
        Assert.ThrowsExactly<FormatException>(() => InstructionDiscriminator.Parse(text.AsSpan(), CultureInfo.InvariantCulture), "Parse span+provider: " + label);
    }

    // ----------------- exhaustive character / byte oracles -----------------

    [TestMethod]
    [DataRow("", 0)]
    [DataRow("", 7)]
    [DataRow("", 8)]
    [DataRow("", 15)]
    [DataRow("0x", 0)]
    [DataRow("0X", 15)]
    public void TryParse_EveryUtf16CodeUnit_InAnyDigitPosition_AcceptedExactlyWhenAsciiHex(string prefix, int position)
    {
        char[] digits = "1234abcd5678ef90".ToCharArray();
        for (int c = 0; c <= char.MaxValue; c++)
        {
            digits[position] = (char)c;
            string text = prefix + new string(digits);
            bool ok = InstructionDiscriminator.TryParse(text.AsSpan(), out var parsed);

            Assert.AreEqual(IsAsciiHexDigit(c), ok, $"U+{c:X4} at {position}");
            if (ok)
                Assert.AreEqual(Convert.ToUInt64(new string(digits), 16), parsed.Value, $"U+{c:X4} at {position}");
            else
                Assert.AreEqual(InstructionDiscriminator.Zero, parsed, $"U+{c:X4} at {position}");
        }
    }

    [TestMethod]
    [DataRow("", 0)]
    [DataRow("", 7)]
    [DataRow("", 8)]
    [DataRow("", 15)]
    [DataRow("0x", 9)]
    public void TryParse_EveryByteValue_InAnyDigitPosition_AcceptedExactlyWhenAsciiHex(string prefix, int position)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(prefix + "1234abcd5678ef90");
        int index = prefix.Length + position;
        for (int b = 0; b <= byte.MaxValue; b++)
        {
            bytes[index] = (byte)b;
            bool ok = InstructionDiscriminator.TryParse(bytes, out var parsed);

            Assert.AreEqual(IsAsciiHexDigit(b), ok, $"byte {b:X2} at {position}");
            if (ok)
                Assert.AreEqual(Convert.ToUInt64(Encoding.ASCII.GetString(bytes, prefix.Length, 16), 16), parsed.Value, $"byte {b:X2} at {position}");
            else
                Assert.AreEqual(InstructionDiscriminator.Zero, parsed, $"byte {b:X2} at {position}");
        }
    }

    // ----------------- accepted shapes, against BCL-formatted values -----------------

    [TestMethod]
    public void Parse_AcceptsEveryCaseAndPrefixAndWhitespaceShape_ForEveryOverload()
    {
        var rng = new Random(2026);
        var values = new List<ulong>
        {
            0, 1, 0xFFFFFFFF, 0x100000000, 0x7FFFFFFFFFFFFFFF, 0x8000000000000000, ulong.MaxValue, 0xA, 0xA000000000000000, 0x0F0F0F0F0F0F0F0F, 0xF0F0F0F0F0F0F0F0,
        };
        for (int i = 0; i < 120; i++)
            values.Add((ulong)rng.NextInt64() ^ ((ulong)rng.NextInt64() << 1));

        foreach (ulong value in values)
        {
            string lower = value.ToString("x16", CultureInfo.InvariantCulture);
            string upper = value.ToString("X16", CultureInfo.InvariantCulture);
            string mixed = string.Concat(lower.Select((c, i) => i % 2 == 0 ? char.ToUpperInvariant(c) : c));
            var expected = new InstructionDiscriminator(value);

            foreach (string digits in new[] { lower, upper, mixed })
            foreach (string prefix in new[] { "", "0x", "0X" })
            foreach (string pad in new[] { "", " ", "\t", "\r\n", " \t\r\n " })
            {
                string text = pad + prefix + digits + pad;

                Assert.AreEqual(expected, InstructionDiscriminator.Parse(text.AsSpan()), text);
                Assert.AreEqual(expected, InstructionDiscriminator.Parse(Encoding.ASCII.GetBytes(text)), text);
                Assert.AreEqual(expected, InstructionDiscriminator.Parse(text), text);
                Assert.AreEqual(expected, InstructionDiscriminator.Parse(text, null), text);
                Assert.AreEqual(expected, InstructionDiscriminator.Parse(text.AsSpan(), CultureInfo.InvariantCulture), text);

                Assert.IsTrue(InstructionDiscriminator.TryParse(text.AsSpan(), out var a) && a == expected, text);
                Assert.IsTrue(InstructionDiscriminator.TryParse(Encoding.ASCII.GetBytes(text), out var b) && b == expected, text);
                Assert.IsTrue(InstructionDiscriminator.TryParse(text, out var c) && c == expected, text);
                Assert.IsTrue(InstructionDiscriminator.TryParse(text, null, out var d) && d == expected, text);
                Assert.IsTrue(InstructionDiscriminator.TryParse(text.AsSpan(), null, out var e) && e == expected, text);
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
    [DataRow("\v" + Valid, "leading VT")]
    [DataRow(Valid + "\v", "trailing VT")]
    [DataRow("\f" + Valid, "leading FF")]
    [DataRow(Valid + "\f", "trailing FF")]
    [DataRow(Valid + "\0", "trailing NUL")]
    [DataRow("\0" + Valid, "leading NUL")]
    [DataRow("\u00A0" + Valid, "NBSP")]
    [DataRow("\u0085" + Valid, "NEL")]
    [DataRow("\u2003" + Valid, "EM SPACE")]
    [DataRow(Valid + "\u2028", "LINE SEPARATOR")]
    [DataRow("afaf6d1f 0d989bed", "internal space")]
    [DataRow("afaf6d1f\t0d989bed", "internal tab")]
    [DataRow("0x " + Valid, "space after prefix")]
    [DataRow("0 x" + Valid, "space inside prefix")]
    [DataRow("+" + Valid, "plus sign")]
    [DataRow("-" + Valid, "minus sign")]
    [DataRow("+0x" + Valid, "plus before prefix")]
    [DataRow("-0x" + Valid, "minus before prefix")]
    [DataRow("0x+" + Valid, "plus after prefix")]
    [DataRow("0x-afaf6d1f0d989be", "minus after prefix, 16 chars")]
    [DataRow("afaf6d1f0d989b-d", "minus inside digits")]
    [DataRow(Valid + "0x", "prefix at the end")]
    [DataRow("0x" + Valid + "0x", "prefix at start and end")]
    [DataRow("0x0x" + Valid, "doubled prefix")]
    [DataRow("0x0xafaf6d1f0d989b", "doubled prefix, 16 chars")]
    [DataRow("00x" + Valid, "prefix after a leading zero")]
    [DataRow("x0" + Valid, "x before zero")]
    [DataRow("x" + Valid, "bare x prefix")]
    [DataRow("0xxafaf6d1f0d989be", "0xx prefix")]
    [DataRow("afaf6d1f0d989be", "15 digits")]
    [DataRow(Valid + "d", "17 digits")]
    [DataRow("0xafaf6d1f0d989be", "15 digits after prefix")]
    [DataRow("0x" + Valid + "d", "17 digits after prefix")]
    [DataRow("afaf6d1f", "only 8 digits (a function selector, not a discriminator)")]
    [DataRow("0xafaf6d1f0d989bed0000000000000000000000000000000000000000000000000", "far too long")]
    [DataRow("\uFF10\uFF11\uFF12\uFF13\uFF14\uFF15\uFF16\uFF17\uFF10\uFF11\uFF12\uFF13\uFF14\uFF15\uFF16\uFF17", "full-width digits")]
    [DataRow("\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667", "Arabic-Indic digits")]
    [DataRow("null", "json null")]
    [DataRow("\"" + Valid + "\"", "quoted")]
    public void Parse_MalformedShape_IsRejectedEverywhere(string text, string label) => AssertRejectedEverywhere(text, label);

    [TestMethod]
    public void TryParse_DefaultSpans_Fail()
    {
        Assert.IsFalse(InstructionDiscriminator.TryParse(default(ReadOnlySpan<char>), out var a));
        Assert.IsFalse(InstructionDiscriminator.TryParse(default(ReadOnlySpan<byte>), out var b));
        Assert.AreEqual(InstructionDiscriminator.Zero, a);
        Assert.AreEqual(InstructionDiscriminator.Zero, b);
        Assert.ThrowsExactly<FormatException>(() => InstructionDiscriminator.Parse(default(ReadOnlySpan<char>)));
        Assert.ThrowsExactly<FormatException>(() => InstructionDiscriminator.Parse(default(ReadOnlySpan<byte>)));
    }

    [TestMethod]
    public void TryParse_FailureAfterASuccessfulParse_ResetsTheOutParameterToZero()
    {
        InstructionDiscriminator result = new(ValidValue);

        Assert.IsFalse(InstructionDiscriminator.TryParse("zzzzzzzzzzzzzzzz".AsSpan(), out result));
        Assert.AreEqual(InstructionDiscriminator.Zero, result);

        result = new InstructionDiscriminator(ValidValue);
        Assert.IsFalse(InstructionDiscriminator.TryParse("afaf6d1f0d989be"u8, out result));
        Assert.AreEqual(InstructionDiscriminator.Zero, result);
    }

    [TestMethod]
    public void Parse_FormatException_NamesTheProblem()
    {
        var ex = Assert.ThrowsExactly<FormatException>(() => InstructionDiscriminator.Parse("nope"));
        StringAssert.Contains(ex.Message, "instruction discriminator");
    }

    // ----------------- null strings -----------------

    [TestMethod]
    public void NullStrings_ParseThrowsArgumentNullException_AndTryParseReturnsFalseWithZero()
    {
        Assert.AreEqual("s", Assert.ThrowsExactly<ArgumentNullException>(() => InstructionDiscriminator.Parse((string)null!)).ParamName);
        Assert.AreEqual("s", Assert.ThrowsExactly<ArgumentNullException>(() => InstructionDiscriminator.Parse((string)null!, null)).ParamName);

        Assert.IsFalse(InstructionDiscriminator.TryParse((string?)null, out var a));
        Assert.IsFalse(InstructionDiscriminator.TryParse((string?)null, null, out var b));
        Assert.AreEqual(InstructionDiscriminator.Zero, a);
        Assert.AreEqual(InstructionDiscriminator.Zero, b);
    }

    [TestMethod]
    public void TryParse_String_ValidAndInvalid_UseTheSameRulesAsTheSpanOverloads()
    {
        Assert.IsTrue(InstructionDiscriminator.TryParse(" 0X" + Valid.ToUpperInvariant() + "\n", out var parsed));
        Assert.AreEqual(ValidValue, parsed.Value);

        Assert.IsFalse(InstructionDiscriminator.TryParse(string.Empty, out parsed));
        Assert.AreEqual(InstructionDiscriminator.Zero, parsed);
    }
}
