using System.Globalization;
using static EccentricWare.Web3.DataTypes.Tests.AddressTestHelpers;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Hostile and exhaustive input for <see cref="Address"/> parsing: character and byte sweeps at every position, length
/// sweeps, huge inputs, and differential agreement of every parse route against an independent oracle.
/// </summary>
[TestClass]
public sealed class AddressParsingAdversarialTests
{
    // One input per parse route: prefixed EVM, unprefixed EVM (with and without a '0' digit, which pick different
    // routes), 43-character Solana Base58, and 40-character Solana Base58 with non-hex digits.
    private static readonly string[] s_parseShapes =
    [
        "0x52908400098527886e0f7030069857d2e4169ee7",
        "52908400098527886e0f7030069857d2e4169ee7",
        "52918499998527886e8f7a3a6a9857d2e4169ee7",
        "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA",
        "1111111112N8uVh5wJmg3Fwjpm5WQKLVUS4kug7V",
    ];

    [TestMethod]
    public void TryParse_Chars_EveryNonAsciiChar_AtEveryPosition_Fails()
    {
        // A parser that truncates a char to its low byte would accept U+0131 as '1', U+0141 as 'A' and so on. Every
        // non-ASCII char must be rejected on every route, at every position (including the 'x' of the prefix and each
        // hex segment boundary).
        foreach (string shape in s_parseShapes)
        {
            Assert.IsTrue(Address.TryParse(shape, out _), shape);

            for (int position = 0; position < shape.Length; position++)
            {
                char[] chars = shape.ToCharArray();
                for (int c = 0x80; c <= 0xFFFF; c++)
                {
                    chars[position] = (char)c;
                    if (Address.TryParse(chars, out _))
                        Assert.Fail($"'{shape}' with U+{c:X4} at {position} was accepted");
                }
            }
        }
    }

    [TestMethod]
    public void TryParse_Utf8_EveryNonAsciiByte_AtEveryPosition_Fails_ForQuotedAndUnquoted()
    {
        foreach (string shape in s_parseShapes)
        {
            for (int position = 0; position < shape.Length; position++)
            {
                byte[] bytes = Utf8(shape);
                for (int b = 0x80; b <= 0xFF; b++)
                {
                    bytes[position] = (byte)b;
                    if (Address.TryParse(bytes, out _) || Address.TryParseUnquotedUtf8(bytes, out _))
                        Assert.Fail($"'{shape}' with byte 0x{b:X2} at {position} was accepted");

                    byte[] quoted = [(byte)'"', .. bytes, (byte)'"'];
                    if (Address.TryParse(quoted, out _))
                        Assert.Fail($"quoted '{shape}' with byte 0x{b:X2} at {position} was accepted");
                }
            }
        }
    }

    // Every ASCII code unit at every position, including the neighbours of the hex and Base58 ranges ('/', ':', '@', 'G',
    // '`', 'g', '[', '{', DEL, NUL). Success must match the oracle exactly, with the same family and bytes.
    [TestMethod]
    public void TryParse_EveryAsciiChar_AtEveryPosition_MatchesTheOracle_ForCharsAndUtf8()
    {
        int accepted = 0, rejected = 0;

        foreach (string shape in s_parseShapes)
        {
            for (int position = 0; position < shape.Length; position++)
            {
                for (int c = 0; c <= 0x7F; c++)
                {
                    char[] chars = shape.ToCharArray();
                    chars[position] = (char)c;
                    string text = new(chars);

                    bool expectedOk = TryExpectedParse(text, out AddressType type, out byte[] bytes);

                    bool charsOk = Address.TryParse(text.AsSpan(), out Address fromChars);
                    AssertParseOutcome("chars", text, charsOk, fromChars, expectedOk, type, bytes);

                    bool utf8Ok = Address.TryParse(Utf8(text), out Address fromUtf8);
                    AssertParseOutcome("utf8", text, utf8Ok, fromUtf8, expectedOk, type, bytes);

                    if (expectedOk) accepted++; else rejected++;
                }
            }
        }

        // Substituting a hex or Base58 digit by another one succeeds, anything else fails: both must be exercised.
        Assert.IsGreaterThan(1_000, accepted);
        Assert.IsGreaterThan(1_000, rejected);
    }

    [TestMethod]
    public void TryParse_ByteAfterTheLeadingZero_OnlyXAndCapitalXFormAPrefix()
    {
        string hex = new('1', 40);

        for (int b = 0; b <= 0xFF; b++)
        {
            byte[] bytes = Utf8("0?" + hex);
            bytes[1] = (byte)b;

            bool isPrefixByte = b == 'x' || b == 'X';
            Assert.AreEqual(isPrefixByte, Address.TryParse(bytes, out _), $"byte 0x{b:X2}");
        }
    }

    // ----------------- lengths -----------------

    // Every length around and beyond the Base58 window, with fills that are valid Base58, invalid Base58, hex, and '0'.
    [TestMethod]
    public void TryParse_EveryLengthFrom0To64_MatchesTheOracle()
    {
        foreach (char fill in new[] { '1', 'z', '2', 'a', '0', 'f', 'F' })
        {
            for (int length = 0; length <= 64; length++)
            {
                string text = new(fill, length);
                bool expectedOk = TryExpectedParse(text, out AddressType type, out byte[] bytes);

                bool charsOk = Address.TryParse(text.AsSpan(), out Address fromChars);
                AssertParseOutcome("chars", text, charsOk, fromChars, expectedOk, type, bytes);

                bool utf8Ok = Address.TryParse(Utf8(text), out Address fromUtf8);
                AssertParseOutcome("utf8", text, utf8Ok, fromUtf8, expectedOk, type, bytes);
            }
        }
    }

    [TestMethod]
    public void TryParse_VeryLongInput_FailsWithoutThrowing()
    {
        foreach (int length in new[] { 65_536, 1_048_576 })
        {
            foreach (char fill in new[] { '1', 'a' })
            {
                string text = new(fill, length);

                Assert.IsFalse(Address.TryParse(text, out Address fromChars), $"{length} x '{fill}'");
                Assert.IsFalse(Address.TryParse(Utf8(text), out Address fromUtf8), $"{length} x '{fill}'");
                Assert.AreEqual(default, fromChars);
                Assert.AreEqual(default, fromUtf8);
            }
        }
    }

    // ----------------- control, BOM and surrogate input -----------------

    [TestMethod]
    public void TryParse_EmbeddedNul_Bom_AndSurrogates_Fail()
    {
        string evm = SampleEvmLower;

        string[] chars =
        [
            "﻿" + evm,                                   // BOM in front of a valid address
            evm + "\0",
            "\0" + evm.Substring(1),                          // NUL replacing the leading '0'
            evm.Substring(0, 20) + "\0" + evm.Substring(21),
            evm.Substring(0, 40) + "😀",            // surrogate pair in the last two slots (42 chars)
            evm.Substring(0, 41) + "\uD83D",                  // lone high surrogate
            new string('1', 31) + "\0",
            "﻿" + new string('1', 32),
        ];

        foreach (string text in chars)
        {
            Assert.IsFalse(Address.TryParse(text, out Address a), Convert.ToHexString(System.Text.Encoding.Unicode.GetBytes(text)));
            Assert.AreEqual(default, a);
        }

        byte[][] utf8 =
        [
            [0xEF, 0xBB, 0xBF, .. Utf8(evm)],                  // UTF-8 BOM
            [.. Utf8(evm), 0x00],
            [.. Utf8(evm.Substring(0, 40)), 0xF0, 0x9F],       // truncated 4-byte sequence
            [.. Utf8(evm.Substring(0, 38)), 0xF0, 0x9F, 0x98, 0x80], // 4-byte emoji in the last slots (42 bytes)
        ];

        foreach (byte[] bytes in utf8)
        {
            Assert.IsFalse(Address.TryParse(bytes, out Address a), Convert.ToHexString(bytes));
            Assert.AreEqual(default, a);
        }
    }

    // ----------------- differential parity -----------------

    // Every route that accepts text must give the oracle's verdict, family and bytes, and `default` on failure:
    // chars, string, string+provider, span+provider, UTF-8, unquoted UTF-8 (internal) and quoted UTF-8. A quoted
    // internal parse is always rejected, because '"' is in no accepted alphabet.
    [TestMethod]
    public void EveryParseRoute_AgreesWithTheOracle_OnASeededCorpus()
    {
        int accepted = 0, rejected = 0;

        foreach (string text in BuildCorpus())
        {
            bool expectedOk = TryExpectedParse(text, out AddressType type, out byte[] bytes);
            byte[] utf8 = Utf8(text);

            void Check(string route, bool ok, Address value) => AssertParseOutcome(route, text, ok, value, expectedOk, type, bytes);

            Check("chars", Address.TryParse(text.AsSpan(), out Address a1), a1);
            Check("string", Address.TryParse(text, out Address a2), a2);
            Check("string+provider", Address.TryParse(text, CultureInfo.InvariantCulture, out Address a3), a3);
            Check("span+provider", Address.TryParse(text.AsSpan(), CultureInfo.InvariantCulture, out Address a4), a4);
            Check("utf8", Address.TryParse(utf8, out Address a5), a5);
            Check("utf8 unquoted", Address.TryParseUnquotedUtf8(utf8, out Address a6), a6);
            Check("utf8 quoted", Address.TryParse(Quoted(text), out Address a7), a7);

            Assert.IsFalse(Address.TryParseUnquotedUtf8(Quoted(text), out Address quotedInternal), $"internal on quoted '{text}'");
            Assert.AreEqual(default, quotedInternal);

            if (expectedOk)
            {
                Assert.AreEqual(a1, Address.Parse(text));
                Assert.AreEqual(a1, Address.Parse(utf8));
                accepted++;
            }
            else
            {
                Assert.ThrowsExactly<FormatException>(() => Address.Parse(text));
                Assert.ThrowsExactly<FormatException>(() => Address.Parse(utf8));
                rejected++;
            }
        }

        Assert.IsGreaterThan(500, accepted);
        Assert.IsGreaterThan(5_000, rejected);
    }

    private static List<string> BuildCorpus()
    {
        var random = new Random(20260607);
        string[] alphabets =
        [
            "0123456789abcdef",
            "0123456789ABCDEF",
            "0123456789abcdefABCDEF",
            Base58Alphabet,
            Base58Alphabet + "0OIl",
            string.Concat(Enumerable.Range(0x20, 0x5F).Select(c => (char)c)),
        ];
        string[] prefixes = ["", "0x", "0X", "1x", "0y", "1111111"];

        var corpus = new List<string>();

        // Random text of every length 0..60 over each alphabet and prefix.
        for (int length = 0; length <= 60; length++)
        {
            foreach (string alphabet in alphabets)
            {
                foreach (string prefix in prefixes)
                {
                    if (prefix.Length > length)
                        continue;

                    for (int sample = 0; sample < 4; sample++)
                        corpus.Add(prefix + RandomText(random, alphabet, length - prefix.Length));
                }
            }
        }

        // Guaranteed positives: EVM in every spelling, and Solana with every number of leading zero bytes.
        byte[] evm = new byte[20];
        for (int i = 0; i < 300; i++)
        {
            random.NextBytes(evm);
            string lower = Convert.ToHexStringLower(evm);
            corpus.Add((i % 4) switch { 0 => "0x" + lower, 1 => "0X" + lower.ToUpperInvariant(), 2 => lower, _ => lower.ToUpperInvariant() });
        }

        byte[] solana = new byte[32];
        for (int i = 0; i < 300; i++)
        {
            random.NextBytes(solana);
            solana.AsSpan(0, i % 33).Clear();
            corpus.Add(ReferenceEncode(solana));
        }

        return corpus;
    }

    private static string RandomText(Random random, string alphabet, int length)
    {
        char[] chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = alphabet[random.Next(alphabet.Length)];
        return new string(chars);
    }
}
