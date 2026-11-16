using System.Globalization;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class Hash32HexParsingTests
{
    private static readonly string LowerHex = Hash32TestData.Hex(Hash32TestData.Bytes0To31());
    private static readonly string UpperHex = Hash32TestData.Hex(Hash32TestData.Bytes0To31(), uppercase: true);

    /// <summary>Valid textual forms; whitespace is limited to space, tab, CR and LF so the UTF-8 path accepts them too.</summary>
    public static IEnumerable<object[]> ValidForms()
    {
        yield return ["bare lowercase", LowerHex];
        yield return ["bare uppercase", UpperHex];
        yield return ["0x prefix", "0x" + LowerHex];
        yield return ["0X prefix with uppercase digits", "0X" + UpperHex];
        yield return ["mixed case digits", "0x" + UpperHex[..20] + LowerHex[20..]];
        yield return ["surrounding whitespace", " \t\r\n0x" + LowerHex + "\r\n\t "];
    }

    /// <summary>Invalid inputs, all pure ASCII so every entry point (chars and UTF-8) can be driven.</summary>
    public static IEnumerable<object[]> InvalidForms()
    {
        yield return ["empty", ""];
        yield return ["whitespace only", "   \t\r\n"];
        yield return ["prefix only 0x", "0x"];
        yield return ["prefix only 0X", "0X"];
        yield return ["63 digits", new string('a', 63)];
        yield return ["65 digits", new string('a', 65)];
        yield return ["63 digits with prefix", "0x" + new string('a', 63)];
        yield return ["non-hex last character", new string('a', 63) + "g"];
        yield return ["non-hex first character", "g" + new string('a', 63)];
        yield return ["double prefix", "0x0x" + new string('a', 62)];
        yield return ["space after prefix", "0x " + LowerHex];
        yield return ["interior space", LowerHex[..10] + " " + LowerHex[10..]];
        yield return ["trailing NUL", LowerHex + "\0"];
        yield return ["embedded NUL", LowerHex[..32] + "\0" + LowerHex[33..]];
        yield return ["base58 text", "SysvarC1ock11111111111111111111111111111111"];
        yield return ["sign prefix", "-" + new string('a', 63)];
    }

    [TestMethod]
    [DynamicData(nameof(ValidForms))]
    public void ValidForms_ParseToTheSameValue_ThroughEveryEntryPoint(string description, string text)
    {
        Hash32 expected = Hash32TestData.Value0To31();
        byte[] utf8 = Hash32TestData.Utf8(text);

        Assert.AreEqual(expected, Hash32.Parse(text.AsSpan()), description);
        Assert.AreEqual(expected, Hash32.Parse(text), description);
        Assert.AreEqual(expected, Hash32.Parse(text.AsSpan(), null), description);
        Assert.AreEqual(expected, Hash32.Parse(text, null), description);
        Assert.AreEqual(expected, Hash32.Parse(utf8), description);

        Assert.IsTrue(Hash32.TryParse(text.AsSpan(), out Hash32 viaSpan), description);
        Assert.IsTrue(Hash32.TryParse(text, out Hash32 viaString), description);
        Assert.IsTrue(Hash32.TryParse(text.AsSpan(), null, out Hash32 viaSpanProvider), description);
        Assert.IsTrue(Hash32.TryParse(text, null, out Hash32 viaStringProvider), description);
        Assert.IsTrue(Hash32.TryParseHexUtf8(utf8, out Hash32 viaUtf8), description);
        Assert.AreEqual(expected, viaSpan, description);
        Assert.AreEqual(expected, viaString, description);
        Assert.AreEqual(expected, viaSpanProvider, description);
        Assert.AreEqual(expected, viaStringProvider, description);
        Assert.AreEqual(expected, viaUtf8, description);
    }

    [TestMethod]
    [DynamicData(nameof(InvalidForms))]
    public void InvalidForms_TryParse_ReturnsFalse_AndZero_ThroughEveryEntryPoint(string description, string text)
    {
        byte[] utf8 = Hash32TestData.Utf8(text);

        Assert.IsFalse(Hash32.TryParse(text.AsSpan(), out Hash32 viaSpan), description);
        Assert.IsFalse(Hash32.TryParse(text, out Hash32 viaString), description);
        Assert.IsFalse(Hash32.TryParse(text.AsSpan(), null, out Hash32 viaSpanProvider), description);
        Assert.IsFalse(Hash32.TryParse(text, null, out Hash32 viaStringProvider), description);
        Assert.IsFalse(Hash32.TryParseHexUtf8(utf8, out Hash32 viaUtf8), description);

        foreach (Hash32 result in new[] { viaSpan, viaString, viaSpanProvider, viaStringProvider, viaUtf8 })
            Assert.IsTrue(result.IsZero, description);
    }

    [TestMethod]
    [DynamicData(nameof(InvalidForms))]
    public void InvalidForms_Parse_ThrowsFormatException_ThroughEveryEntryPoint(string description, string text)
    {
        byte[] utf8 = Hash32TestData.Utf8(text);

        Assert.ThrowsExactly<FormatException>(() => Hash32.Parse(text.AsSpan()), description);
        Assert.ThrowsExactly<FormatException>(() => Hash32.Parse(text), description);
        Assert.ThrowsExactly<FormatException>(() => Hash32.Parse(text.AsSpan(), null), description);
        Assert.ThrowsExactly<FormatException>(() => Hash32.Parse(text, null), description);
        Assert.ThrowsExactly<FormatException>(() => Hash32.Parse(utf8), description);
    }

    [TestMethod]
    public void TryParse_String_NullOrEmpty_ReturnsFalse_AndZero()
    {
        Assert.IsFalse(Hash32.TryParse((string?)null, out Hash32 r1));
        Assert.IsTrue(r1.IsZero);
        Assert.IsFalse(Hash32.TryParse(string.Empty, out Hash32 r2));
        Assert.IsTrue(r2.IsZero);
        Assert.IsFalse(Hash32.TryParse((string?)null, null, out Hash32 r3));
        Assert.IsTrue(r3.IsZero);
        Assert.IsFalse(Hash32.TryParse(string.Empty, null, out Hash32 r4));
        Assert.IsTrue(r4.IsZero);
    }

    [TestMethod]
    public void Parse_NullString_ThrowsArgumentNullException_WithParamName()
    {
        var plain = Assert.ThrowsExactly<ArgumentNullException>(() => Hash32.Parse((string)null!));
        Assert.AreEqual("hex", plain.ParamName);

        var withProvider = Assert.ThrowsExactly<ArgumentNullException>(() => Hash32.Parse((string?)null, CultureInfo.InvariantCulture));
        Assert.AreEqual("s", withProvider.ParamName);
    }

    [TestMethod]
    public void Parse_WrongLength_AndBadDigit_ThrowDistinctMessages()
    {
        var lengthError = Assert.ThrowsExactly<FormatException>(() => Hash32.Parse("0x1234".AsSpan()));
        var digitError = Assert.ThrowsExactly<FormatException>(() => Hash32.Parse((new string('a', 63) + "g").AsSpan()));

        Assert.AreNotEqual(lengthError.Message, digitError.Message);
    }

    [TestMethod]
    [DataRow("tr-TR")]
    [DataRow("ar-SA")]
    [DataRow("")]
    public void Parse_WithFormatProvider_IsInvariant(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        string text = "0x" + UpperHex;
        Hash32 expected = Hash32TestData.Value0To31();

        Assert.AreEqual(expected, Hash32.Parse(text.AsSpan(), culture));
        Assert.AreEqual(expected, Hash32.Parse(text, culture));
        Assert.IsTrue(Hash32.TryParse(text.AsSpan(), culture, out Hash32 parsed));
        Assert.AreEqual(expected, parsed);
        Assert.IsTrue(Hash32.TryParse(text, culture, out parsed));
        Assert.AreEqual(expected, parsed);
    }

    [TestMethod]
    public void Parse_MixedCaseHex_NormalisesToLowercaseOnFormatting()
    {
        string mixed = "0x" + UpperHex[..20] + LowerHex[20..];

        Hash32 parsed = Hash32.Parse(mixed);

        Assert.AreEqual("0x" + LowerHex, parsed.ToString());
        Assert.IsTrue(Hash32.TryParse(parsed.ToString("x", CultureInfo.InvariantCulture).AsSpan(), out Hash32 reparsed));
        Assert.AreEqual(parsed, reparsed);
    }

    [TestMethod]
    public void TryParse_InvalidCharacterAtAnyPosition_IsRejected_ForCharsAndUtf8()
    {
        // Guards the vectorised decoder: a bad character in any lane must fail the whole parse.
        char[] badChars = ['g', 'G', '/', ':', '@', '`', ' ', '\0', '٠', 'é'];

        for (int position = 0; position < LowerHex.Length; position++)
        {
            foreach (char bad in badChars)
            {
                string text = LowerHex[..position] + bad + LowerHex[(position + 1)..];
                Assert.IsFalse(Hash32.TryParse(text.AsSpan(), out Hash32 parsed), $"chars pos={position} bad={(int)bad}");
                Assert.IsTrue(parsed.IsZero);
                Assert.IsFalse(Hash32.TryParseHexUtf8(Hash32TestData.Utf8(text), out _), $"utf8 pos={position} bad={(int)bad}");
            }
        }
    }

    [TestMethod]
    [DataRow(0x00)]
    [DataRow(0x80)]
    [DataRow(0xC3)]
    [DataRow(0xFF)]
    public void TryParseHexUtf8_HighOrNulByteAtAnyPosition_IsRejected(int badByte)
    {
        byte[] valid = Hash32TestData.Utf8(LowerHex);

        for (int position = 0; position < valid.Length; position++)
        {
            byte[] corrupted = (byte[])valid.Clone();
            corrupted[position] = (byte)badByte;

            Assert.IsFalse(Hash32.TryParseHexUtf8(corrupted, out Hash32 parsed), $"pos={position} byte={badByte:X}");
            Assert.IsTrue(parsed.IsZero);
        }
    }

    [TestMethod]
    public void TryParse_CharSpan_DoesNotAllowInteriorWhitespace()
    {
        string bad = "0x" + LowerHex[..10] + " " + LowerHex[10..];

        Assert.IsFalse(Hash32.TryParse(bad.AsSpan(), out Hash32 parsed));
        Assert.IsTrue(parsed.IsZero);
    }

    [TestMethod]
    public void TryParseHexUtf8_NonAsciiWhitespace_IsNotTrimmed()
    {
        // NBSP (U+00A0) is not ASCII whitespace; the UTF-8 trim leaves it in place.
        byte[] utf8 = Hash32TestData.Utf8(" 0x" + LowerHex + " ");

        Assert.IsFalse(Hash32.TryParseHexUtf8(utf8, out _));
    }

    [TestMethod]
    [DataRow("\v")]
    [DataRow("\f")]
    [DataRow("\u0085")]
    [DataRow(" ")]
    public void WhitespaceTrimming_CharPathTrimsUnicodeWhitespace_Utf8PathDoesNot_DocumentsCurrentBehaviour(string whitespace)
    {
        // The char overloads trim every char.IsWhiteSpace character; the UTF-8 overload trims only space, tab, CR and LF.
        string text = whitespace + LowerHex + whitespace;
        Hash32 expected = Hash32TestData.Value0To31();

        Assert.IsTrue(Hash32.TryParse(text.AsSpan(), out Hash32 viaChars));
        Assert.AreEqual(expected, viaChars);
        Assert.IsFalse(Hash32.TryParseHexUtf8(Hash32TestData.Utf8(text), out _));
    }

    [TestMethod]
    public void TryParseHexUtf8_Base58Text_IsRejected()
    {
        byte[] utf8 = Hash32TestData.Utf8("SysvarC1ock11111111111111111111111111111111");

        Assert.IsFalse(Hash32.TryParseHexUtf8(utf8, out Hash32 parsed));
        Assert.IsTrue(parsed.IsZero);
    }

    [TestMethod]
    public void RoundTrip_AllFiveFormats_ParseBackToTheSameValue()
    {
        foreach (Hash32 value in Hash32TestData.RandomHashes(new Random(808), 1_000))
        {
            foreach (string format in new[] { "", "0x", "0X", "x", "X" })
            {
                string text = value.ToString(format, CultureInfo.InvariantCulture);
                Assert.AreEqual(value, Hash32.Parse(text), format);
                Assert.IsTrue(Hash32.TryParseHexUtf8(Hash32TestData.Utf8(text), out Hash32 viaUtf8), format);
                Assert.AreEqual(value, viaUtf8, format);
            }
        }
    }

    // ---- generic dispatch through the parsing interfaces ----

    private static T ParseGeneric<T>(string text) where T : IParsable<T> => T.Parse(text, null);

    private static bool TryParseGeneric<T>(string? text, out T result) where T : IParsable<T> => T.TryParse(text, null, out result!);

    private static T ParseGenericSpan<T>(ReadOnlySpan<char> text) where T : ISpanParsable<T> => T.Parse(text, null);

    private static bool TryParseGenericSpan<T>(ReadOnlySpan<char> text, out T result) where T : ISpanParsable<T> => T.TryParse(text, null, out result!);

    [TestMethod]
    public void IParsable_Dispatch_ParsesAndRejects()
    {
        Hash32 expected = Hash32TestData.Value0To31();

        Assert.AreEqual(expected, ParseGeneric<Hash32>("0x" + LowerHex));
        Assert.IsTrue(TryParseGeneric("0x" + LowerHex, out Hash32 parsed));
        Assert.AreEqual(expected, parsed);

        Assert.IsFalse(TryParseGeneric<Hash32>("0x12", out Hash32 rejected));
        Assert.IsTrue(rejected.IsZero);
        Assert.IsFalse(TryParseGeneric<Hash32>(null, out _));
        Assert.ThrowsExactly<FormatException>(() => ParseGeneric<Hash32>("0x12"));
        Assert.ThrowsExactly<ArgumentNullException>(() => ParseGeneric<Hash32>(null!));
    }

    [TestMethod]
    public void ISpanParsable_Dispatch_ParsesAndRejects()
    {
        Hash32 expected = Hash32TestData.Value0To31();

        Assert.AreEqual(expected, ParseGenericSpan<Hash32>(("0x" + LowerHex).AsSpan()));
        Assert.IsTrue(TryParseGenericSpan(LowerHex.AsSpan(), out Hash32 parsed));
        Assert.AreEqual(expected, parsed);

        Assert.IsFalse(TryParseGenericSpan<Hash32>("zz".AsSpan(), out Hash32 rejected));
        Assert.IsTrue(rejected.IsZero);
        Assert.ThrowsExactly<FormatException>(() => ParseGenericSpan<Hash32>("zz".AsSpan()));
    }
}
