using System.Globalization;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Parsing of <see cref="Signature"/>: hex, Solana Base58, the provider and interface overloads, invalid input.</summary>
[TestClass]
public sealed class SignatureTextTests
{
    private static string EvmHex => SignatureTestData.Ascending65Hex;
    private static string SolanaHex => SignatureTestData.Ascending64Hex;

    private static T ParseViaInterface<T>(string text, IFormatProvider? provider) where T : IParsable<T> => T.Parse(text, provider);

    private static T ParseSpanViaInterface<T>(ReadOnlySpan<char> text, IFormatProvider? provider) where T : ISpanParsable<T> => T.Parse(text, provider);

    private static bool TryParseViaInterface<T>(string? text, IFormatProvider? provider, out T result) where T : IParsable<T>
        => T.TryParse(text, provider, out result!);

    private static bool TrySpanViaInterface<T>(ReadOnlySpan<char> text, IFormatProvider? provider, out T result) where T : ISpanParsable<T>
        => T.TryParse(text, provider, out result!);

    // ----------------- hex parsing -----------------

    [TestMethod]
    public void TryParse_EvmHex_AllSpellings_Chars()
    {
        foreach (string text in new[] { "0x" + EvmHex, "0X" + EvmHex, EvmHex, "0x" + EvmHex.ToUpperInvariant(), "  0x" + EvmHex + "\r\n\t " })
        {
            Assert.IsTrue(Signature.TryParse(text.AsSpan(), out Signature value), text);
            Assert.AreEqual(SignatureTestData.SampleEvm, value, text);
        }
    }

    [TestMethod]
    public void TryParse_EvmHex_AllSpellings_Utf8()
    {
        foreach (string text in new[] { "0x" + EvmHex, "0X" + EvmHex, EvmHex, "0x" + EvmHex.ToUpperInvariant(), "  0x" + EvmHex + "\r\n\t ", "\"0x" + EvmHex + "\"", " \"" + EvmHex + "\" " })
        {
            Assert.IsTrue(Signature.TryParse(SignatureTestData.Utf8(text), out Signature value), text);
            Assert.AreEqual(SignatureTestData.SampleEvm, value, text);
        }
    }

    [TestMethod]
    public void TryParse_SolanaHex_AllSpellings_BothEncodings()
    {
        foreach (string text in new[] { "0x" + SolanaHex, "0X" + SolanaHex, SolanaHex, "0x" + SolanaHex.ToUpperInvariant() })
        {
            Assert.IsTrue(Signature.TryParse(text.AsSpan(), out Signature chars), text);
            Assert.IsTrue(Signature.TryParse(SignatureTestData.Utf8(text), out Signature utf8), text);
            Assert.AreEqual(SignatureTestData.SampleSolana, chars, text);
            Assert.AreEqual(SignatureTestData.SampleSolana, utf8, text);
        }
    }

    [TestMethod]
    public void TryParse_HexLengthDecidesTheType()
    {
        // 64 bytes is always Solana, even though an EIP-2098 compact ECDSA signature has the same length.
        Assert.AreEqual(SignatureType.Solana, Signature.Parse(SolanaHex.AsSpan()).Type);
        Assert.AreEqual(SignatureType.Evm, Signature.Parse(EvmHex.AsSpan()).Type);
    }

    // ----------------- Parse(ReadOnlySpan<byte>) -----------------

    [TestMethod]
    public void Parse_Utf8_ReturnsTheSignature_ForEverySpelling()
    {
        foreach (string text in new[] { "0x" + EvmHex, EvmHex, "\"0x" + EvmHex + "\"", "  0x" + EvmHex + "\r\n" })
            Assert.AreEqual(SignatureTestData.SampleEvm, Signature.Parse(SignatureTestData.Utf8(text)), text);

        foreach (string text in new[] { "0x" + SolanaHex, SolanaHex, "\"" + SolanaHex + "\"" })
        {
            Signature parsed = Signature.Parse(SignatureTestData.Utf8(text));
            Assert.AreEqual(SignatureTestData.SampleSolana, parsed, text);
            Assert.AreEqual(SignatureType.Solana, parsed.Type);
        }

        Signature docs = Signature.Parse(SignatureTestData.Utf8(SignatureTestData.SolanaDocsBase58));
        Assert.AreEqual(Signature.Parse(SignatureTestData.SolanaDocsBase58.AsSpan()), docs);
        CollectionAssert.AreEqual(Convert.FromHexString(SignatureTestData.SolanaDocsHex), docs.ToBytes());
    }

    [TestMethod]
    public void Parse_Utf8_Invalid_ThrowsFormatException_WithAMessageThatNamesEveryAcceptedForm()
    {
        foreach (string text in new[] { "", "nope", "\"\"", "0x" + EvmHex + "0", "0x" + SolanaHex[..^1] })
        {
            var error = Assert.ThrowsExactly<FormatException>(() => Signature.Parse(SignatureTestData.Utf8(text)), text);
            StringAssert.Contains(error.Message, "130");
            StringAssert.Contains(error.Message, "128");
            StringAssert.Contains(error.Message, "Base58");
        }
    }

    // ----------------- Base58 -----------------

    [TestMethod]
    public void TryParse_SolanaDocsExample_DecodesToTheKnownBytes()
    {
        Assert.AreEqual(88, SignatureTestData.SolanaDocsBase58.Length);
        Assert.IsTrue(Signature.TryParse(SignatureTestData.SolanaDocsBase58.AsSpan(), out Signature chars));
        Assert.IsTrue(Signature.TryParse(SignatureTestData.Utf8(SignatureTestData.SolanaDocsBase58), out Signature utf8));
        Assert.AreEqual(chars, utf8);
        Assert.AreEqual(SignatureType.Solana, chars.Type);
        CollectionAssert.AreEqual(Convert.FromHexString(SignatureTestData.SolanaDocsHex), chars.ToBytes());
    }

    [TestMethod]
    public void TryParse_Base58_MatchesTheIndependentVectors_InBothEncodings()
    {
        // vectors from an independent python Base58 implementation (including the RFC 8032 test 1 signature)
        foreach ((string name, string hex, string base58) in SignatureTestData.Base58Vectors)
        {
            byte[] expected = Convert.FromHexString(hex);

            Assert.IsTrue(Signature.TryParse(base58.AsSpan(), out Signature chars), name);
            Assert.IsTrue(Signature.TryParse(SignatureTestData.Utf8(base58), out Signature utf8), name);
            Assert.IsTrue(Signature.TryParse(SignatureTestData.Utf8("\"" + base58 + "\""), out Signature quoted), name);
            foreach (Signature parsed in new[] { chars, utf8, quoted })
            {
                Assert.AreEqual(SignatureType.Solana, parsed.Type, name);
                Assert.AreEqual(0, parsed.V, name);
                CollectionAssert.AreEqual(expected, parsed.ToBytes(), name);
            }
        }
    }

    [TestMethod]
    public void TryParse_Base58_MatchesIndependentEncoder_ForRandomPayloads_IncludingLeadingZeroBytes()
    {
        foreach (Signature signature in SignatureTestData.RandomSignatures(400).Where(s => s.Type == SignatureType.Solana))
        {
            string base58 = SignatureTestData.Base58(signature.ToBytes());

            Assert.IsTrue(Signature.TryParse(base58.AsSpan(), out Signature chars), base58);
            Assert.IsTrue(Signature.TryParse(SignatureTestData.Utf8(base58), out Signature utf8), base58);
            Assert.AreEqual(signature, chars, base58);
            Assert.AreEqual(signature, utf8, base58);
        }
    }

    [TestMethod]
    public void TryParse_SixtyFourOnes_IsTheAllZeroSolanaSignature()
    {
        Assert.IsTrue(Signature.TryParse(new string('1', 64).AsSpan(), out Signature value));
        Assert.AreEqual(Signature.ZeroSolana, value);
    }

    [TestMethod]
    public void TryParse_Base58_RejectsNonCanonicalAndOverflowingEncodings()
    {
        string canonical = SignatureTestData.SolanaDocsBase58;

        foreach (string text in new[]
        {
            "1" + canonical,                 // extra leading '1' without a matching zero byte
            new string('1', 65),             // more leading ones than bytes
            new string('z', 88),             // exceeds 2^512
            new string('z', 96),             // at the length cap, exceeds 2^512
            canonical[..40],                 // small value: far fewer than 64 significant bytes, no matching leading '1's
            "1",                             // one zero byte only
            "5",                             // tiny value
        })
        {
            Assert.IsFalse(Signature.TryParse(text.AsSpan(), out Signature chars), text);
            Assert.IsFalse(Signature.TryParse(SignatureTestData.Utf8(text), out Signature utf8), text);
            Assert.AreEqual(default, chars);
            Assert.AreEqual(default, utf8);
        }
    }

    [TestMethod]
    [DataRow('0')]
    [DataRow('O')]
    [DataRow('I')]
    [DataRow('l')]
    [DataRow('+')]
    [DataRow(' ')]
    [DataRow('é')]
    [DataRow('١')] // Arabic-Indic digit one
    public void TryParse_Base58_RejectsCharactersOutsideTheAlphabet(char bad)
    {
        string text = SignatureTestData.SolanaDocsBase58;
        string corrupted = string.Concat(text.AsSpan(0, 40), bad.ToString(), text.AsSpan(41));

        Assert.IsFalse(Signature.TryParse(corrupted.AsSpan(), out _));
        if (bad < 128)
            Assert.IsFalse(Signature.TryParse(SignatureTestData.Utf8(corrupted), out _));
    }

    [TestMethod]
    public void TryParse_Base58_ThatIsTooLong_FailsWithoutThrowing()
    {
        foreach (int length in new[] { 97, 128 + 1, 1_000, 100_000 })
        {
            string text = new('2', length);
            Assert.IsFalse(Signature.TryParse(text.AsSpan(), out _), $"{length}");
            Assert.IsFalse(Signature.TryParse(SignatureTestData.Utf8(text), out _), $"{length}");
        }
    }

    // ----------------- regression: 0x prefix means hex only -----------------

    [TestMethod]
    public void TryParse_ZeroXPrefixedBase58_IsRejected()
    {
        // "0x" announces hex. Base58 text after the prefix used to be accepted as a Solana signature, so one signature
        // had several spellings and "0x" + any hex run without a zero digit could be mistaken for Base58.
        string text = "0x" + SignatureTestData.SolanaDocsBase58;

        Assert.IsFalse(Signature.TryParse(text.AsSpan(), out _));
        Assert.IsFalse(Signature.TryParse(SignatureTestData.Utf8(text), out _));
        Assert.IsFalse(Signature.TryParse(SignatureTestData.Utf8("\"" + text + "\""), out _));
        Assert.ThrowsExactly<FormatException>(() => Signature.Parse(text.AsSpan()));
    }

    // ----------------- invalid input -----------------

    [TestMethod]
    public void TryParse_Invalid_ReturnsFalseAndDefault_ForEveryEntryPoint()
    {
        string badDigitInV = EvmHex[..128] + "zz";
        string[] inputs =
        [
            "", " ", "0x", "0X", "\"\"", "\"0x\"",
            "0x" + EvmHex[..^1],                       // 129 digits
            "0x" + EvmHex + "0",                       // 131 digits
            "0x" + SolanaHex[..^1],                    // 127 digits
            "0x" + new string('a', 64),                // a hash, not a signature
            "0x" + new string('a', 40),                // an address
            "0x0x" + EvmHex,
            "0x" + badDigitInV,
            "0x" + EvmHex.Remove(10, 1).Insert(10, "g"),
            "0x" + EvmHex.Remove(0, 1).Insert(0, "-"),
            "0x " + EvmHex,                            // space after prefix
            "0x" + EvmHex[..64] + " " + EvmHex[65..],  // embedded space
            "0x" + EvmHex[..64] + "\0" + EvmHex[65..],
            "0x" + EvmHex.Remove(129, 1).Insert(129, "０"), // fullwidth zero in v
            "0x" + EvmHex.Remove(3, 1).Insert(3, "１"),     // fullwidth one in payload
            "0x" + EvmHex.Remove(3, 1).Insert(3, "١"),     // Arabic-Indic digit in payload
        ];

        foreach (string text in inputs)
        {
            Assert.IsFalse(Signature.TryParse(text.AsSpan(), out Signature chars), $"chars: '{text}'");
            Assert.AreEqual(default, chars);

            // The UTF-8 entry point only receives ASCII here: non-ASCII characters have no single-byte form.
            if (text.All(c => c < 128))
            {
                Assert.IsFalse(Signature.TryParse(SignatureTestData.Utf8(text), out Signature utf8), $"utf8: '{text}'");
                Assert.AreEqual(default, utf8);
            }

            Assert.IsFalse(Signature.TryParse(text, null, out Signature viaProvider), $"provider: '{text}'");
            Assert.AreEqual(default, viaProvider);
            Assert.ThrowsExactly<FormatException>(() => Signature.Parse(text.AsSpan()), $"Parse: '{text}'");
        }
    }

    [TestMethod]
    public void TryParse_Utf8_RejectsBytesAbove0x7F_InsideAnOtherwiseValidToken()
    {
        byte[] utf8 = SignatureTestData.Utf8("0x" + EvmHex);
        for (int index = 2; index < utf8.Length; index += 13)
        {
            byte[] corrupted = (byte[])utf8.Clone();
            corrupted[index] = 0xC3;
            Assert.IsFalse(Signature.TryParse(corrupted, out _), $"index {index}");
        }
    }

    [TestMethod]
    public void TryParse_Utf8_QuoteHandling()
    {
        Assert.IsTrue(Signature.TryParse(SignatureTestData.Utf8("\"" + EvmHex + "\""), out _));
        Assert.IsFalse(Signature.TryParse(SignatureTestData.Utf8("\"" + EvmHex), out _)); // unbalanced
        Assert.IsFalse(Signature.TryParse(SignatureTestData.Utf8(EvmHex + "\""), out _));
        Assert.IsFalse(Signature.TryParse(SignatureTestData.Utf8("\" " + EvmHex + "\""), out _)); // inside the quotes nothing is trimmed
    }

    [TestMethod]
    public void Parse_Invalid_ThrowsFormatException_WithAMessageThatNamesEveryAcceptedForm()
    {
        var error = Assert.ThrowsExactly<FormatException>(() => Signature.Parse("nope".AsSpan()));
        StringAssert.Contains(error.Message, "130");
        StringAssert.Contains(error.Message, "128");
        StringAssert.Contains(error.Message, "Base58");
    }

    // ----------------- IParsable / ISpanParsable -----------------

    [TestMethod]
    public void ParseWithProvider_DoesNotRecurse_AndParses()
    {
        // Parse(ReadOnlySpan<char>, IFormatProvider) once called itself, so every provider overload overflowed the stack.
        Signature expected = SignatureTestData.SampleEvm;

        Assert.AreEqual(expected, Signature.Parse("0x" + EvmHex, null));
        Assert.AreEqual(expected, Signature.Parse("0x" + EvmHex, CultureInfo.InvariantCulture));
        Assert.AreEqual(expected, Signature.Parse(("0x" + EvmHex).AsSpan(), null));
        Assert.AreEqual(expected, Signature.Parse(("0x" + EvmHex).AsSpan(), CultureInfo.GetCultureInfo("tr-TR")));
        Assert.AreEqual(expected, ParseViaInterface<Signature>("0x" + EvmHex, null));
        Assert.AreEqual(expected, ParseSpanViaInterface<Signature>(("0x" + EvmHex).AsSpan(), null));
        Assert.AreEqual(Signature.FromSolanaBytes(Convert.FromHexString(SignatureTestData.SolanaDocsHex)), Signature.Parse(SignatureTestData.SolanaDocsBase58, null));
    }

    [TestMethod]
    public void ParseWithProvider_InvalidText_ThrowsFormatException_NullThrowsArgumentNull()
    {
        Assert.ThrowsExactly<FormatException>(() => Signature.Parse("nope", null));
        Assert.ThrowsExactly<FormatException>(() => Signature.Parse("nope".AsSpan(), null));
        var error = Assert.ThrowsExactly<ArgumentNullException>(() => Signature.Parse((string)null!, null));
        Assert.AreEqual("s", error.ParamName);
        Assert.ThrowsExactly<FormatException>(() => ParseViaInterface<Signature>("nope", null));
        Assert.ThrowsExactly<FormatException>(() => ParseSpanViaInterface<Signature>("nope".AsSpan(), null));
    }

    [TestMethod]
    public void TryParseWithProvider_HandlesNullAndValidText()
    {
        Assert.IsFalse(Signature.TryParse((string?)null, null, out Signature none));
        Assert.AreEqual(default, none);
        Assert.IsTrue(Signature.TryParse("0x" + EvmHex, CultureInfo.InvariantCulture, out Signature value));
        Assert.AreEqual(SignatureTestData.SampleEvm, value);
        Assert.IsTrue(Signature.TryParse(("0x" + EvmHex).AsSpan(), null, out value));
        Assert.AreEqual(SignatureTestData.SampleEvm, value);
    }

    [TestMethod]
    public void TryParseWithProvider_SpanOverload_RejectsInvalidText_AndAcceptsBase58AndPadding()
    {
        Assert.IsFalse(Signature.TryParse("nope".AsSpan(), null, out Signature invalid));
        Assert.AreEqual(default, invalid);
        Assert.IsFalse(Signature.TryParse(("0x" + EvmHex + "0").AsSpan(), CultureInfo.InvariantCulture, out invalid));
        Assert.AreEqual(default, invalid);

        Assert.IsTrue(Signature.TryParse(SignatureTestData.SolanaDocsBase58.AsSpan(), null, out Signature base58));
        Assert.AreEqual(SignatureType.Solana, base58.Type);
        Assert.IsTrue(Signature.TryParse(SignatureTestData.SolanaDocsBase58, null, out Signature fromString));
        Assert.AreEqual(base58, fromString);
        Assert.IsTrue(Signature.TryParse(" \t0x" + EvmHex + "\r\n", null, out Signature padded));
        Assert.AreEqual(SignatureTestData.SampleEvm, padded);
    }

    [TestMethod]
    public void TryParse_ThroughTheInterfaces_BehavesLikeTheDirectOverloads()
    {
        Assert.IsTrue(TryParseViaInterface("0x" + EvmHex, null, out Signature fromString));
        Assert.AreEqual(SignatureTestData.SampleEvm, fromString);
        Assert.IsTrue(TrySpanViaInterface(SolanaHex.AsSpan(), CultureInfo.GetCultureInfo("tr-TR"), out Signature fromSpan));
        Assert.AreEqual(SignatureTestData.SampleSolana, fromSpan);

        Assert.IsFalse(TryParseViaInterface<Signature>(null, null, out Signature none));
        Assert.AreEqual(default, none);
        Assert.IsFalse(TryParseViaInterface<Signature>("nope", null, out Signature bad));
        Assert.AreEqual(default, bad);
        Assert.IsFalse(TrySpanViaInterface<Signature>("nope".AsSpan(), null, out bad));
        Assert.AreEqual(default, bad);
    }
}
