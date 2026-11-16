using System.Globalization;
using static EccentricWare.Web3.DataTypes.Tests.AddressTestHelpers;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Behaviour of every <see cref="Address"/> parse entry point on well-formed and malformed text (chars, UTF-8, quoted, ISpanParsable).</summary>
[TestClass]
public sealed class AddressParsingTests
{
    private const string Hex40 = "52908400098527886e0f7030069857d2e4169ee7";
    private static readonly string s_evmText = "0x" + Hex40;

    // ----------------- chars: EVM -----------------

    [TestMethod]
    public void TryParse_Chars_Evm_With_0x_Or_0X_Prefix()
    {
        const string upperPrefix = "0X52908400098527886E0F7030069857D2E4169EE7";

        Assert.IsTrue(Address.TryParse(s_evmText, out Address a));
        Assert.AreEqual(AddressType.Evm, a.Type);
        Assert.AreEqual(s_evmText, a.ToString());

        Assert.IsTrue(Address.TryParse(upperPrefix, out Address b));
        Assert.AreEqual(AddressType.Evm, b.Type);
        Assert.AreEqual(s_evmText, b.ToString()); // canonical output is lower + 0x
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void TryParse_Chars_Evm_WithoutPrefix_40Hex_Fallback()
    {
        // Contains '0' so Base58 cannot apply; the 40-hex fallback succeeds (EVM).
        string hex40 = "000102030405060708090a0b0c0d0e0f10111213";

        Assert.IsTrue(Address.TryParse(hex40, out Address a));
        Assert.AreEqual(AddressType.Evm, a.Type);
        Assert.AreEqual("0x" + hex40, a.ToString());
    }

    // With a '0' the input cannot be Base58 and goes straight to the hex path; without one it may try Base58 first, which
    // rejects it (non-canonical) before the hex fallback accepts it.
    [TestMethod]
    [DataRow("52908400098527886e0f7030069857d2e4169ee7")]
    [DataRow("52918499998527886e8f7a3a6a9857d2e4169ee7")]
    [DataRow("52908400098527886E0F7030069857D2E4169EE7")]
    [DataRow("52908400098527886E0f7030069857D2e4169eE7")]
    [DataRow("0000000000000000000000000000000000000000")]
    public void Parse_Unprefixed40Hex_AnyCaseWithOrWithoutZeroDigit_IsEvm(string text)
    {
        byte[] expected = Convert.FromHexString(text);

        Assert.IsTrue(Address.TryParse(text, out Address chars));
        Assert.IsTrue(Address.TryParse(Utf8(text), out Address utf8));

        Assert.AreEqual(AddressType.Evm, chars.Type);
        CollectionAssert.AreEqual(expected, chars.ToBytes());
        Assert.AreEqual(chars, utf8);
    }

    // The ambiguity guard `Contains('0')`: seven leading '1's would be tried as Base58, but a '0' is not a Base58 digit.
    [TestMethod]
    public void Parse_SevenLeadingOnes_WithAZeroLater_IsEvm_NotSolana()
    {
        foreach (string text in new[] { "1111111" + "0" + "89abcdef89abcdef89abcdef89abcdef", "1111111" + "89abcdef89abcdef89abcdef89abcdef" + "0" })
        {
            Assert.AreEqual(40, text.Length);

            Assert.IsTrue(Address.TryParse(text, out Address chars), text);
            Assert.IsTrue(Address.TryParse(Utf8(text), out Address utf8), text);
            Assert.AreEqual(AddressType.Evm, chars.Type, text);
            Assert.AreEqual("0x" + text, chars.ToString(), text);
            Assert.AreEqual(chars, utf8, text);
        }
    }

    [TestMethod]
    public void TryParse_Chars_Evm_Prefix_LengthMismatch_Fails()
    {
        Assert.IsFalse(Address.TryParse("0x" + new string('0', 39), out _)); // 41 total
        Assert.IsFalse(Address.TryParse("0x" + new string('0', 41), out _)); // 43 total
        Assert.IsFalse(Address.TryParse("0x", out _));                       // 2 total
    }

    [TestMethod]
    public void TryParse_Chars_Evm_Prefix_InvalidHex_Fails()
    {
        Assert.IsFalse(Address.TryParse("0x" + new string('0', 39) + "g", out Address a));
        Assert.AreEqual(default, a);
    }

    // The prefix is exactly '0' then 'x'/'X'; anything else in those two slots is not a prefix.
    [TestMethod]
    [DataRow("0y")]
    [DataRow("00")]
    [DataRow("x0")]
    [DataRow("0 ")]
    public void TryParse_NotAPrefix_Fails_ForCharsAndUtf8(string twoChars)
    {
        string text = twoChars + new string('1', 40);

        Assert.IsFalse(Address.TryParse(text, out Address chars), text);
        Assert.IsFalse(Address.TryParse(Utf8(text), out Address utf8), text);
        Assert.AreEqual(default, chars);
        Assert.AreEqual(default, utf8);
    }

    [TestMethod]
    public void Parse_Unprefixed40Chars_WithNonHex_Fails()
    {
        Assert.IsFalse(Address.TryParse("0" + new string('g', 39), out _));
        Assert.IsFalse(Address.TryParse(Utf8("0" + new string('g', 39)), out _));
        Assert.IsFalse(Address.TryParse(new string('0', 39) + "٠", out _)); // Arabic-Indic zero is not ASCII
    }

    [TestMethod]
    public void TryParse_Chars_DoesNotTrim_Whitespace_Fails()
    {
        string evm = "0x" + new string('1', 40);

        Assert.IsFalse(Address.TryParse(" " + evm, out _));
        Assert.IsFalse(Address.TryParse(evm + " ", out _));
        Assert.IsFalse(Address.TryParse("\n" + evm, out _));
        Assert.IsFalse(Address.TryParse(evm + "\r", out _));
    }

    // ----------------- chars: Solana and ambiguity -----------------

    [TestMethod]
    public void TryParse_Chars_Solana_Base58_Zero()
    {
        Assert.IsTrue(Address.TryParse(new string('1', 32), out Address a));
        Assert.AreEqual(AddressType.Solana, a.Type);
        Assert.AreEqual(Address.ZeroSolana, a);
    }

    [TestMethod]
    public void TryParse_Chars_Solana_LengthOutsideRange_Fails()
    {
        Assert.IsFalse(Address.TryParse(new string('1', 31), out _));
        Assert.IsFalse(Address.TryParse(new string('1', 45), out _));
    }

    // 40 characters, canonical Base58 with non-hex digits: the hex route would reject it, so only the Base58 attempt accepts.
    // Vector from an independent Python Base58 reference (9 leading zero bytes).
    [TestMethod]
    public void TryParse_FortyCharBase58WithNonHexDigits_ParsesAsSolana_ForEveryRoute()
    {
        const string text = "1111111112N8uVh5wJmg3Fwjpm5WQKLVUS4kug7V";
        byte[] expected = Convert.FromHexString("000000000000000000012392d9cec411421f7fc37479a762ca36197d08e5d664");
        Assert.IsFalse(text.All(IsAsciiHexDigit));

        Assert.IsTrue(Address.TryParse(text, out Address fromChars));
        Assert.IsTrue(Address.TryParse(Utf8(text), out Address fromUtf8));

        Assert.AreEqual(AddressType.Solana, fromChars.Type);
        CollectionAssert.AreEqual(expected, fromChars.ToBytes());
        Assert.AreEqual(fromChars, fromUtf8);
        Assert.AreEqual(text, fromChars.ToString());
    }

    [TestMethod]
    public void TryParse_Unprefixed40Hex_WithEightLeadingOnes_IsClassifiedAsSolana_UnlessPrefixed()
    {
        // Documented ambiguity, pinned so a change is deliberate. Eight leading '1' characters followed by 32 more Base58
        // digits is a canonical encoding of a 32-byte value (eight zero bytes), and the unprefixed form has no '0' to rule
        // Base58 out, so it parses as Solana. The "0x" prefix is the only unambiguous way to write such an EVM address.
        const string hex = "1111111189abcdef89abcdef89abcdef89abcdef";

        Assert.IsTrue(Address.TryParse(hex, out Address bare));
        Assert.AreEqual(AddressType.Solana, bare.Type);

        Assert.IsTrue(Address.TryParse(Utf8(hex), out Address bareUtf8));
        Assert.AreEqual(AddressType.Solana, bareUtf8.Type);

        Assert.IsTrue(Address.TryParse("0x" + hex, out Address prefixed));
        Assert.AreEqual(AddressType.Evm, prefixed.Type);
        Assert.AreEqual("0x" + hex, prefixed.ToString());

        // Seven leading '1's is not canonical for this payload, so it stays EVM.
        Assert.AreEqual(AddressType.Evm, Address.Parse("11111119abcdef89abcdef89abcdef89abcdef89").Type);
    }

    [TestMethod]
    public void Parse_Unprefixed40HexWithoutZero_FamilyMatchesABigIntegerBase58Oracle_ForEveryLeadingOnesCount()
    {
        // The parser skips Base58 for 40-character text with fewer than seven leading '1's. This proves that shortcut never
        // changes the answer: the family must be Solana exactly when the text is a canonical 32-byte Base58 value.
        const string digits = "123456789abcdef"; // hex without '0'
        var random = new Random(4242);
        var solanaSeen = new HashSet<int>();
        var evmSeen = new HashSet<int>();

        for (int leadingOnes = 0; leadingOnes <= 24; leadingOnes++)
        {
            for (int sample = 0; sample < 400; sample++)
            {
                char[] text = new char[Address.EvmHexLength];
                for (int i = 0; i < text.Length; i++)
                    text[i] = i < leadingOnes ? '1' : digits[random.Next(digits.Length)];

                if (leadingOnes < text.Length && text[leadingOnes] == '1')
                    text[leadingOnes] = 'a'; // exactly `leadingOnes` leading '1's

                string s = new(text);
                AddressType expected = TryReferenceDecode32(s, out _) ? AddressType.Solana : AddressType.Evm;

                Assert.IsTrue(Address.TryParse(s, out Address fromChars), s);
                Assert.IsTrue(Address.TryParse(Utf8(s), out Address fromUtf8), s);
                Assert.AreEqual(expected, fromChars.Type, s);
                Assert.AreEqual(fromChars, fromUtf8, s);

                (expected == AddressType.Solana ? solanaSeen : evmSeen).Add(leadingOnes);
            }
        }

        // The sweep must reach both outcomes around the threshold, or it proves nothing. Canonical 40-character strings
        // exist for about 7 to 12 leading '1's (all of them are canonical at 10), never below 7 or above 12.
        Assert.IsFalse(solanaSeen.Any(k => k < 7 || k > 12), "canonical 40-character Base58 needs 7 to 12 leading '1's");
        Assert.IsTrue(solanaSeen.Contains(8) && evmSeen.Contains(8), "k = 8 reaches both outcomes");
        Assert.IsTrue(solanaSeen.Contains(10) && !evmSeen.Contains(10), "k = 10 is always canonical");
        Assert.IsTrue(evmSeen.Contains(0) && !solanaSeen.Contains(0));
    }

    // ----------------- null, empty, throwing -----------------

    [TestMethod]
    public void TryParse_Chars_Empty_And_Null_Fail()
    {
        Assert.IsFalse(Address.TryParse(ReadOnlySpan<char>.Empty, out Address a));
        Assert.AreEqual(default, a);

        Assert.IsFalse(Address.TryParse((string?)null, out Address b));
        Assert.AreEqual(default, b);
    }

    [TestMethod]
    public void Parse_Chars_Invalid_Throws_FormatException()
    {
        foreach (string text in new[] { "not-an-address", "0x" + new string('z', 40), "" })
        {
            FormatException ex = Assert.ThrowsExactly<FormatException>(() => Address.Parse(text));
            Assert.AreEqual("Invalid blockchain address format", ex.Message);
        }
    }

    [TestMethod]
    public void Parse_ReadOnlySpanOfChars_ReturnsTheAddress_Or_ThrowsFormatException()
    {
        Assert.AreEqual(Address.Parse(s_evmText), Address.Parse(s_evmText.AsSpan()));
        Assert.AreEqual(Address.ZeroSolana, Address.Parse(new string('1', 32).AsSpan()));

        Assert.ThrowsExactly<FormatException>(() => Address.Parse(ReadOnlySpan<char>.Empty));
        Assert.ThrowsExactly<FormatException>(() => Address.Parse("0x12".AsSpan()));
    }

    [TestMethod]
    public void Parse_NullString_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Address.Parse((string)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Address.Parse((string)null!, CultureInfo.InvariantCulture));
    }

    // ----------------- UTF-8 -----------------

    [TestMethod]
    public void TryParse_Utf8_Unquoted_And_Quoted_Evm()
    {
        Assert.IsTrue(Address.TryParse(Utf8(s_evmText), out Address a));
        Assert.AreEqual(AddressType.Evm, a.Type);
        Assert.AreEqual(s_evmText, a.ToString());

        Assert.IsTrue(Address.TryParse(Quoted(s_evmText), out Address b));
        Assert.AreEqual(AddressType.Evm, b.Type);
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void TryParse_Utf8_Unquoted_And_Quoted_Solana()
    {
        Address sol = Address.FromSolanaBytes(Sequence(32, i => (byte)i));
        string s = ReferenceEncode(sol.ToBytes());

        Assert.IsTrue(Address.TryParse(Utf8(s), out Address a));
        Assert.AreEqual(AddressType.Solana, a.Type);
        Assert.AreEqual(sol, a);

        Assert.IsTrue(Address.TryParse(Quoted(s), out Address b));
        Assert.AreEqual(AddressType.Solana, b.Type);
        Assert.AreEqual(sol, b);
    }

    [TestMethod]
    public void TryParse_Utf8_Evm_UppercasePrefix_MixedCaseHex_Succeeds()
    {
        Assert.IsTrue(Address.TryParse(Utf8("0X52908400098527886E0f7030069857D2e4169eE7"), out Address a));
        Assert.AreEqual(AddressType.Evm, a.Type);
        Assert.AreEqual(s_evmText, a.ToString());
    }

    [TestMethod]
    public void TryParse_Utf8_Empty_Fails()
    {
        Assert.IsFalse(Address.TryParse(ReadOnlySpan<byte>.Empty, out Address a));
        Assert.AreEqual(default, a);
    }

    [TestMethod]
    public void TryParse_Utf8_MalformedQuotes_Fails()
    {
        Assert.IsFalse(Address.TryParse(Utf8("\"0x" + new string('0', 40)), out _));     // missing trailing quote
        Assert.IsFalse(Address.TryParse(Utf8("0x" + new string('0', 40) + "\""), out _)); // missing leading quote
    }

    // The unquoting step strips exactly one matching pair, nothing else.
    [TestMethod]
    public void TryParse_Utf8_QuoteEdgeCases_AllFail_AndLeaveDefault()
    {
        string evm = s_evmText;
        string[] texts =
        [
            "\"\"",                 // empty after unquoting
            "\"",                   // a lone quote
            "\"\"\"",               // unquotes to a single quote character
            "\"\"" + evm + "\"\"",  // doubled quotes: only one pair is stripped
            "'" + evm + "'",        // single quotes are not JSON quotes
            "\" " + evm + " \"",    // no trimming inside the quotes
            " \"" + evm + "\"",     // nor outside
            "\"" + evm + "\" ",
        ];

        foreach (string text in texts)
        {
            Assert.IsFalse(Address.TryParse(Utf8(text), out Address a), text);
            Assert.AreEqual(default, a, text);
        }
    }

    // The chars overload has no unquoting step, unlike the UTF-8 one.
    [TestMethod]
    public void TryParse_Chars_DoesNotUnquote_ButUtf8Does()
    {
        string quoted = "\"" + s_evmText + "\"";

        Assert.IsFalse(Address.TryParse(quoted, out Address chars));
        Assert.AreEqual(default, chars);
        Assert.IsTrue(Address.TryParse(Utf8(quoted), out _));
    }

    [TestMethod]
    public void Parse_Utf8_ReturnsTheAddress_ForEveryAcceptedShape()
    {
        Address evm = Address.Parse(s_evmText);
        Address solana = Address.Parse(SampleSolana);

        Assert.AreEqual(evm, Address.Parse(Utf8(s_evmText)));
        Assert.AreEqual(evm, Address.Parse(Quoted(s_evmText)));
        Assert.AreEqual(evm, Address.Parse(Utf8(Hex40)));
        Assert.AreEqual(solana, Address.Parse(Utf8(SampleSolana)));
        Assert.AreEqual(solana, Address.Parse(Quoted(SampleSolana)));
    }

    [TestMethod]
    public void Parse_Utf8_InvalidInput_ThrowsFormatException()
    {
        byte[] invalidUtf8 = Utf8(s_evmText);
        invalidUtf8[10] = 0xFF;

        byte[][] inputs =
        [
            [],
            Utf8("0x"),
            Utf8("\"\""),
            Utf8("0x" + new string('z', 40)),
            Utf8(new string('z', 44)), // above 2^256
            invalidUtf8,
        ];

        foreach (byte[] input in inputs)
        {
            FormatException ex = Assert.ThrowsExactly<FormatException>(() => Address.Parse(input));
            Assert.AreEqual("Invalid blockchain address format", ex.Message);
        }
    }

    [TestMethod]
    public void TryParse_Utf8_DoesNotAccept_WhitespaceAround_JsonQuotes()
    {
        Assert.IsFalse(Address.TryParse(Utf8(" \"" + s_evmText + "\""), out _));
        Assert.IsFalse(Address.TryParse(Utf8("\"" + s_evmText + "\" "), out _));
    }

    // ----------------- TryParseUnquotedUtf8 (internal) -----------------

    [TestMethod]
    public void TryParseUnquotedUtf8_AcceptsTheSameTextAsTheOverloadWithoutQuotes()
    {
        foreach (string text in new[] { s_evmText, Hex40, "0X" + Hex40.ToUpperInvariant(), SampleSolana, new string('1', 32) })
        {
            Assert.IsTrue(Address.TryParseUnquotedUtf8(Utf8(text), out Address unquoted), text);
            Assert.IsTrue(Address.TryParse(Utf8(text), out Address viaPublic), text);
            Assert.AreEqual(viaPublic, unquoted, text);
        }
    }

    // It must not strip quotes: a value that merely contains quote characters is not an address.
    [TestMethod]
    public void TryParseUnquotedUtf8_RejectsQuotedText_AndLeavesDefault()
    {
        foreach (string text in new[] { s_evmText, Hex40, SampleSolana })
        {
            byte[] quoted = Quoted(text);
            Assert.IsFalse(Address.TryParseUnquotedUtf8(quoted, out Address a), text);
            Assert.AreEqual(default, a, text);
            Assert.IsTrue(Address.TryParse(quoted, out _), text); // the public overload strips the pair first
        }
    }

    [TestMethod]
    public void TryParseUnquotedUtf8_RejectsEmptyAndTinyInput_AndLeavesDefault()
    {
        foreach (byte[] input in new[] { Array.Empty<byte>(), Utf8("\""), Utf8("0"), Utf8("0x"), new byte[] { 0xFF } })
        {
            Assert.IsFalse(Address.TryParseUnquotedUtf8(input, out Address a), Convert.ToHexString(input));
            Assert.AreEqual(default, a);
        }
    }

    // ----------------- ISpanParsable / IParsable surface -----------------

    [TestMethod]
    public void ISpanParsable_Overloads_Work_And_Ignore_Provider()
    {
        // Solana text containing 'i': a parser that upper-cased or compared case-insensitively under tr-TR would see 'İ'.
        var turkish = CultureInfo.GetCultureInfo("tr-TR");
        Assert.IsTrue(SampleSolana.Contains('i'));

        foreach (string text in new[] { s_evmText, SampleSolana })
        {
            Address expected = Address.Parse(text);

            Assert.AreEqual(expected, Address.Parse(text.AsSpan(), turkish));
            Assert.AreEqual(expected, Address.Parse(text, turkish));
            Assert.AreEqual(expected, Address.Parse(text, new ThrowingFormatProvider()));

            Assert.IsTrue(Address.TryParse(text.AsSpan(), turkish, out Address c));
            Assert.AreEqual(expected, c);
            Assert.IsTrue(Address.TryParse(text, turkish, out Address d));
            Assert.AreEqual(expected, d);
            Assert.IsTrue(Address.TryParse(text, new ThrowingFormatProvider(), out Address e));
            Assert.AreEqual(expected, e);
        }
    }

    [TestMethod]
    public void ISpanParsable_InvalidText_Fails_WithDefaultOutput()
    {
        var provider = CultureInfo.InvariantCulture;

        Assert.ThrowsExactly<FormatException>(() => Address.Parse("zz", provider));
        Assert.ThrowsExactly<FormatException>(() => Address.Parse("zz".AsSpan(), provider));
        Assert.ThrowsExactly<FormatException>(() => Address.Parse(ReadOnlySpan<char>.Empty, provider));

        Assert.IsFalse(Address.TryParse("zz", provider, out Address a));
        Assert.AreEqual(default, a);
        Assert.IsFalse(Address.TryParse("zz".AsSpan(), provider, out Address b));
        Assert.AreEqual(default, b);
        Assert.IsFalse(Address.TryParse((string?)null, provider, out Address c));
        Assert.AreEqual(default, c);
    }

    [TestMethod]
    public void Parse_NullStringWithProvider_NamesTheParameter()
    {
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(() => Address.Parse((string)null!, CultureInfo.InvariantCulture));

        Assert.AreEqual("s", ex.ParamName);
    }

    [TestMethod]
    public void GenericParsing_ThroughIParsableAndISpanParsable_MatchesTheConcreteCalls()
    {
        Address expected = Address.Parse(s_evmText);

        Assert.AreEqual(expected, ParseGeneric<Address>(s_evmText));
        Assert.AreEqual(expected, ParseGenericSpan<Address>(s_evmText));
        Assert.IsTrue(TryParseGeneric(s_evmText, out Address viaInterface));
        Assert.AreEqual(expected, viaInterface);
        Assert.IsFalse(TryParseGeneric("zz", out Address failed));
        Assert.AreEqual(default, failed);
        Assert.ThrowsExactly<FormatException>(() => ParseGeneric<Address>("zz"));

        static T ParseGeneric<T>(string s) where T : IParsable<T> => T.Parse(s, null);
        static T ParseGenericSpan<T>(string s) where T : ISpanParsable<T> => T.Parse(s.AsSpan(), null);
        static bool TryParseGeneric<T>(string s, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value) where T : IParsable<T> => T.TryParse(s, null, out value);
    }

    private sealed class ThrowingFormatProvider : IFormatProvider
    {
        public object GetFormat(Type? formatType) => throw new InvalidOperationException("The provider must not be consulted.");
    }
}
