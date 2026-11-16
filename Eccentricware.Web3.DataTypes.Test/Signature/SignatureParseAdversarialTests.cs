using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Adversarial and boundary input for <see cref="Signature"/> parsing: characters, lengths, whitespace, quotes, prefixes.</summary>
[TestClass]
public sealed class SignatureParseAdversarialTests
{
    private static string EvmHex => SignatureTestData.Ascending65Hex;
    private static string SolanaHex => SignatureTestData.Ascending64Hex;
    private static string Docs => SignatureTestData.SolanaDocsBase58;

    private static string Describe(string text) => text.Length > 48 ? $"[{text.Length} chars] {text[..16]}..." : $"'{text}'";

    /// <summary>The UTF-8 entry points only see ASCII here: other characters have no single-byte form.</summary>
    private static bool IsAscii(string text) => text.All(c => c < 128);

    private static void AssertRejectedEverywhere(string text, string label)
    {
        Assert.IsFalse(Signature.TryParse(text.AsSpan(), out Signature chars), $"chars {label}");
        Assert.AreEqual(default, chars, label);
        if (IsAscii(text))
        {
            Assert.IsFalse(Signature.TryParse(SignatureTestData.Utf8(text), out Signature utf8), $"utf8 {label}");
            Assert.AreEqual(default, utf8, label);
        }

        Assert.ThrowsExactly<FormatException>(() => Signature.Parse(text.AsSpan()), $"Parse {label}");
    }

    private static void AssertParsesEverywhere(string text, Signature expected, string label)
    {
        Assert.IsTrue(Signature.TryParse(text.AsSpan(), out Signature chars), $"chars {label}");
        Assert.AreEqual(expected, chars, label);
        if (IsAscii(text))
        {
            Assert.IsTrue(Signature.TryParse(SignatureTestData.Utf8(text), out Signature utf8), $"utf8 {label}");
            Assert.AreEqual(expected, utf8, label);
        }
    }

    // ----------------- invalid characters at every position -----------------

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void TryParse_RejectsEveryAsciiNonHexCharacter_AtEveryPosition(bool solana, bool prefixed)
    {
        // Includes the characters on both sides of each hex class: '/' ':' '@' '`' 'g' 'G'.
        char[] bad = ['g', 'G', '/', ':', '@', '`', ' ', '\0', '-', '+'];
        string digits = solana ? SolanaHex : EvmHex;
        string prefix = prefixed ? "0x" : "";

        for (int position = 0; position < digits.Length; position++)
        {
            foreach (char replacement in bad)
            {
                char[] corrupted = digits.ToCharArray();
                corrupted[position] = replacement;
                AssertRejectedEverywhere(prefix + new string(corrupted), $"position {position} char U+{(int)replacement:X4}");
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TryParse_RejectsCharactersWhoseLowByteIsAHexDigit_AtEveryLimbEdge_AndInV(bool solana)
    {
        // U+0661 -> 0x61 'a', U+0141 -> 0x41 'A', U+0131 -> 0x31 '1', U+0130 -> 0x30 '0': they must not alias after truncation.
        char[] aliases = ['١', 'Ł', 'ı', 'İ', '１'];
        string digits = solana ? SolanaHex : EvmHex;

        var positions = new List<int>();
        for (int limb = 0; limb < 8; limb++)
        {
            positions.Add(limb * 16);
            positions.Add(limb * 16 + 15);
        }

        if (!solana)
        {
            positions.Add(128);
            positions.Add(129);
        }

        foreach (int position in positions)
        {
            foreach (char alias in aliases)
            {
                char[] corrupted = digits.ToCharArray();
                corrupted[position] = alias;
                string text = new(corrupted);

                Assert.IsFalse(Signature.TryParse(text.AsSpan(), out Signature value), $"position {position} U+{(int)alias:X4}");
                Assert.AreEqual(default, value);
                Assert.IsFalse(Signature.TryParse(("0x" + text).AsSpan(), out _), $"prefixed position {position} U+{(int)alias:X4}");
                Assert.ThrowsExactly<FormatException>(() => Signature.Parse(text.AsSpan()));
            }
        }
    }

    [TestMethod]
    public void TryParse_Utf8_RejectsHighBytes_AtEveryPosition()
    {
        foreach (string digits in new[] { EvmHex, SolanaHex })
        {
            byte[] utf8 = SignatureTestData.Utf8(digits);
            for (int index = 0; index < utf8.Length; index++)
            {
                foreach (byte high in new byte[] { 0x80, 0xC3, 0xFF })
                {
                    byte[] corrupted = (byte[])utf8.Clone();
                    corrupted[index] = high;
                    Assert.IsFalse(Signature.TryParse(corrupted, out Signature value), $"index {index} byte {high:x2}");
                    Assert.AreEqual(default, value);
                }
            }
        }
    }

    // ----------------- dispatch by length -----------------

    [TestMethod]
    public void TryParse_OnlyHexLengthsTakeTheHexPath_AndNonHexTextNeverFallsThroughToBase58()
    {
        // 'z' is a Base58 digit but not a hex digit. 58^87 - 1 is 87 'z' characters: the only length where it is a valid
        // 64-byte value. At 128 and 130 'z' must not be retried as Base58.
        byte[] fiftyEightTo87Minus1 = Convert.FromHexString(SignatureTestData.Base58Vectors.Single(v => v.Name == "FiftyEightToThe87Minus1").Hex);

        for (int length = 0; length <= 140; length++)
        {
            string text = new('z', length);
            if (length == 87)
            {
                Assert.IsTrue(Signature.TryParse(text.AsSpan(), out Signature chars));
                Assert.IsTrue(Signature.TryParse(SignatureTestData.Utf8(text), out Signature utf8));
                CollectionAssert.AreEqual(fiftyEightTo87Minus1, chars.ToBytes());
                Assert.AreEqual(chars, utf8);
                continue;
            }

            AssertRejectedEverywhere(text, $"z x {length}");
        }

        // "0x" means hex only, so no length of 'z' succeeds with a prefix (including the Base58-valid 87).
        for (int length = 0; length <= 140; length++)
        {
            AssertRejectedEverywhere("0x" + new string('z', length), $"0x + z x {length}");
            AssertRejectedEverywhere("0X" + new string('z', length), $"0X + z x {length}");
        }
    }

    [TestMethod]
    public void TryParse_RepeatedTwos_AreValidHexAtTheHexLengths()
    {
        // '2' is a hex digit too: 128 and 130 of them are valid hex, whatever Base58 would make of them.
        byte[] payload = SignatureTestData.Filled(64, 0x22);

        AssertParsesEverywhere(new string('2', 128), Signature.FromSolanaBytes(payload), "128 twos");
        AssertParsesEverywhere(new string('2', 130), SignatureTestData.EvmFrom(payload, 0x22), "130 twos");
    }

    [TestMethod]
    public void TryParse_Base58Lengths89To96_AreRejected_WithoutThrowing()
    {
        // No canonical 64-byte value needs more than 88 digits: lengths up to the 96 cap fail in the decoder.
        foreach (char digit in new[] { '2', 'z' })
        {
            for (int length = 89; length <= Signature.SolanaMaxBase58Length; length++)
                AssertRejectedEverywhere(new string(digit, length), $"{digit} x {length}");
        }
    }

    [TestMethod]
    public void TryParse_Base58At87And88Digits_AcceptsTheBoundaryValues()
    {
        foreach (string name in new[] { "FiftyEightToThe87Minus1", "FiftyEightToThe87", "AllOneBits", "FiftyEightToThe86" })
        {
            (string _, string hex, string base58) = SignatureTestData.Base58Vectors.Single(v => v.Name == name);
            Assert.IsTrue(base58.Length is 87 or 88, name);
            AssertParsesEverywhere(base58, Signature.FromSolanaBytes(Convert.FromHexString(hex)), name);
        }
    }

    // ----------------- hex case -----------------

    [TestMethod]
    public void TryParse_MixedCaseHex_EqualsTheLowercaseParse()
    {
        static string Mixed(string hex, int period) => string.Concat(hex.Select((c, i) => i % period == 0 ? char.ToUpperInvariant(c) : c));

        foreach (int period in new[] { 2, 3, 5 })
        {
            AssertParsesEverywhere(Mixed(EvmHex, period), SignatureTestData.SampleEvm, $"evm period {period}");
            AssertParsesEverywhere("0X" + Mixed(EvmHex, period), SignatureTestData.SampleEvm, $"evm 0X period {period}");
            AssertParsesEverywhere("0x" + Mixed(SolanaHex, period), SignatureTestData.SampleSolana, $"solana period {period}");

            Signature real = Signature.FromEvmBytes(Convert.FromHexString(SignatureTestData.EvmRealHex));
            AssertParsesEverywhere("0x" + Mixed(SignatureTestData.EvmRealHex, period), real, $"real period {period}");
        }
    }

    // ----------------- whitespace policy -----------------

    [TestMethod]
    public void TryParse_TrimsExactlySpaceTabCarriageReturnLineFeed_AroundEveryForm()
    {
        Signature docs = Signature.Parse(Docs.AsSpan());
        foreach (string padding in new[] { " ", "\t", "\r", "\n", "\r\n \t", "          " })
        {
            AssertParsesEverywhere(padding + Docs + padding, docs, $"base58 {padding.Length}");
            AssertParsesEverywhere(padding + EvmHex + padding, SignatureTestData.SampleEvm, $"evm {padding.Length}");
            AssertParsesEverywhere(padding + "0x" + SolanaHex + padding, SignatureTestData.SampleSolana, $"solana {padding.Length}");
        }

        // Whitespace outside the quotes is trimmed by the UTF-8 entry point; inside the quotes it is content.
        Assert.IsTrue(Signature.TryParse(SignatureTestData.Utf8(" \r\n\"" + Docs + "\"\t"), out Signature quoted));
        Assert.AreEqual(docs, quoted);
        Assert.IsFalse(Signature.TryParse(SignatureTestData.Utf8("\"" + Docs + " \""), out _));
        Assert.IsFalse(Signature.TryParse(SignatureTestData.Utf8("\"\n" + Docs + "\""), out _));
    }

    [TestMethod]
    public void TryParse_DoesNotTrimOtherWhitespaceOrControlCharacters()
    {
        foreach (char other in new[] { '\v', '\f', ' ', '\u0085', ' ', '\0' })
        {
            foreach (string core in new[] { EvmHex, "0x" + SolanaHex, Docs })
            {
                AssertRejectedEverywhere(other + core, $"leading U+{(int)other:X4} {Describe(core)}");
                AssertRejectedEverywhere(core + other, $"trailing U+{(int)other:X4} {Describe(core)}");
            }
        }
    }

    [TestMethod]
    public void TryParse_RejectsWhitespaceOnlyAndInteriorWhitespace()
    {
        foreach (string text in new[] { " ", "\t", "\r\n", new string(' ', 200), "\" \"" })
            AssertRejectedEverywhere(text, Describe(text));

        // Inside Base58 text: inserting a space or newline, either way the digits no longer form one token.
        foreach (string separator in new[] { " ", "\n", "\t" })
            AssertRejectedEverywhere(Docs[..40] + separator + Docs[40..], $"base58 separator {(int)separator[0]}");

        AssertRejectedEverywhere("0x" + SolanaHex[..64] + " " + SolanaHex[64..], "interior space in solana hex");
    }

    [TestMethod]
    public void TryParse_VeryLongPadding_StillParses_WithoutThrowing()
    {
        // Documents the policy: any amount of surrounding ASCII whitespace is trimmed (the caller bounds the input size).
        string padding = new(' ', 100_000);
        AssertParsesEverywhere(padding + "0x" + EvmHex + padding, SignatureTestData.SampleEvm, "100000 spaces");
        AssertRejectedEverywhere(padding, "only spaces");
    }

    // ----------------- UTF-8 quote handling -----------------

    [TestMethod]
    public void TryParse_Utf8_QuoteVariants()
    {
        Signature docs = Signature.Parse(Docs.AsSpan());

        foreach ((string text, bool valid) in new[]
        {
            ("\"" + Docs + "\"", true),
            ("\"" + SolanaHex + "\"", true),
            ("\"0X" + EvmHex + "\"", true),
            ("\"", false),                                  // a lone quote
            ("\"\"" + EvmHex + "\"\"", false),              // only one pair is stripped
            ("'" + EvmHex + "'", false),                    // single quotes are not framing
            (EvmHex[..64] + "\"" + EvmHex[64..], false),    // quote inside the token
            ("\"" + Docs, false),
            (Docs + "\"", false),
            ("\"0x" + Docs + "\"", false),                  // 0x never prefixes Base58
        })
        {
            bool parsed = Signature.TryParse(SignatureTestData.Utf8(text), out Signature value);
            Assert.AreEqual(valid, parsed, text);
            if (!valid)
                Assert.AreEqual(default, value);
        }

        Assert.IsTrue(Signature.TryParse(SignatureTestData.Utf8("\"" + Docs + "\""), out Signature quoted));
        Assert.AreEqual(docs, quoted);
    }

    // ----------------- 0x prefix variants -----------------

    [TestMethod]
    public void TryParse_PrefixVariants()
    {
        foreach (string text in new[]
        {
            "x" + EvmHex, "0" + EvmHex, "00x" + EvmHex, "0xx" + EvmHex, "x0" + EvmHex, "0x0x" + SolanaHex,
            "0x0X" + EvmHex, "o" + "x" + EvmHex, "0x" + SolanaHex + "0",
        })
            AssertRejectedEverywhere(text, Describe(text));

        // Prefix case, and the digit count (not the prefix) decides the type.
        AssertParsesEverywhere("0x" + new string('0', 128), Signature.ZeroSolana, "0x + 128 zeros");
        AssertParsesEverywhere("0X" + SolanaHex, SignatureTestData.SampleSolana, "0X solana");
        AssertParsesEverywhere("0X" + new string('0', 130), Signature.ZeroEvm, "0X + 130 zeros");
        AssertParsesEverywhere("  0x" + EvmHex, SignatureTestData.SampleEvm, "prefix after whitespace");
    }

    // ----------------- TryParseToken (JSON string content) -----------------

    [TestMethod]
    public void TryParseToken_AcceptsEverySpelling_AndTrimsAsciiWhitespace()
    {
        foreach (string text in new[] { "0x" + EvmHex, EvmHex, "0X" + EvmHex.ToUpperInvariant(), "  0x" + EvmHex + "\t" })
        {
            Assert.IsTrue(Signature.TryParseToken(SignatureTestData.Utf8(text), out Signature value), text);
            Assert.AreEqual(SignatureTestData.SampleEvm, value, text);
        }

        foreach (string text in new[] { "0x" + SolanaHex, SolanaHex })
        {
            Assert.IsTrue(Signature.TryParseToken(SignatureTestData.Utf8(text), out Signature value), text);
            Assert.AreEqual(SignatureTestData.SampleSolana, value, text);
        }

        Assert.IsTrue(Signature.TryParseToken(SignatureTestData.Utf8(Docs), out Signature docs));
        CollectionAssert.AreEqual(Convert.FromHexString(SignatureTestData.SolanaDocsHex), docs.ToBytes());
        Assert.AreEqual(SignatureType.Solana, docs.Type);
        Assert.AreEqual(0, docs.V);
    }

    [TestMethod]
    public void TryParseToken_TreatsQuotesAsContent_UnlikeTryParse()
    {
        // Inside a JSON string token a quote is content, so quoted text is invalid here but valid for TryParse(utf8).
        foreach (string text in new[] { "\"0x" + EvmHex + "\"", "\"" + Docs + "\"", "\"" + SolanaHex + "\"" })
        {
            Assert.IsFalse(Signature.TryParseToken(SignatureTestData.Utf8(text), out Signature value), text);
            Assert.AreEqual(default, value);
            Assert.IsTrue(Signature.TryParse(SignatureTestData.Utf8(text), out _), text);
        }

        // Whitespace is trimmed by TryParseToken even next to quotes it does not strip: only the outer whitespace goes.
        Assert.IsFalse(Signature.TryParseToken(SignatureTestData.Utf8(" \"" + Docs + "\" "), out _));
    }

    [TestMethod]
    public void TryParseToken_RejectsEmptyWhitespaceHighBytesAndHugeInput_WithoutThrowing()
    {
        foreach (byte[] input in new[]
        {
            Array.Empty<byte>(),
            SignatureTestData.Utf8("   \r\n"),
            SignatureTestData.Utf8("0x"),
            SignatureTestData.Utf8("nope"),
            SignatureTestData.Utf8("0x" + EvmHex[..^1]),
            SignatureTestData.Filled(100_000, (byte)'a'),
            SignatureTestData.Filled(100_000, (byte)' '),
        })
        {
            Assert.IsFalse(Signature.TryParseToken(input, out Signature value), $"length {input.Length}");
            Assert.AreEqual(default, value);
        }

        byte[] highByte = SignatureTestData.Utf8("0x" + EvmHex);
        highByte[70] = 0xC3;
        Assert.IsFalse(Signature.TryParseToken(highByte, out _));
    }

    // ----------------- fuzz -----------------

    [TestMethod]
    public void TryParse_NeverThrows_AndEverythingItAcceptsRoundTrips()
    {
        const string HexAlphabet = "0123456789abcdefABCDEF";
        const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        const string Junk = "0123456789abcdefxXgG zZ_-+\t\r\n\0é";

        var random = new Random(8032);
        int accepted = 0;
        int[] hexLengths = [128, 130];
        for (int i = 0; i < 6000; i++)
        {
            string alphabet = random.Next(3) switch { 0 => HexAlphabet, 1 => Base58Alphabet, _ => Junk };
            int length = random.Next(2) == 0 && alphabet == HexAlphabet ? hexLengths[random.Next(2)] : random.Next(0, 141);
            string text = string.Create(length, (alphabet, random.Next()), static (span, state) =>
            {
                var local = new Random(state.Item2);
                for (int j = 0; j < span.Length; j++)
                    span[j] = state.alphabet[local.Next(state.alphabet.Length)];
            });

            bool chars = Signature.TryParse(text.AsSpan(), out Signature fromChars);
            if (IsAscii(text))
            {
                bool utf8 = Signature.TryParse(Encoding.ASCII.GetBytes(text), out Signature fromUtf8);
                Assert.AreEqual(chars, utf8, text);
                Assert.AreEqual(fromChars, fromUtf8, text);
            }

            if (chars)
            {
                accepted++;
                Assert.AreEqual(fromChars, Signature.Parse(text.AsSpan()), text);
                Assert.AreEqual(fromChars, Signature.Parse(fromChars.ToString().AsSpan()), text);
                if (fromChars.Type == SignatureType.Solana)
                    Assert.AreEqual(0, fromChars.V, text);
            }
            else
            {
                Assert.AreEqual(default, fromChars, text);
            }
        }

        Assert.IsGreaterThan(1000, accepted, "the fuzz must produce plenty of valid inputs");
    }
}
