using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Whitespace tolerance (ASCII space, tab, CR, LF only) and quote characters inside the token, for values and
/// dictionary keys of all nine converters.
/// </summary>
[TestClass]
public sealed class WhitespaceAndQuotePolicyTests
{
    private static readonly TokenVariant[] PaddedVariants = [TokenVariant.PaddedSpaces, TokenVariant.PaddedEscapedWhitespace];

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void AsciiWhitespaceAroundAValue_IsIgnored_ForEverySpellingOfTheValue(JsonConverterCase c)
    {
        foreach ((string text, object expected) in c.AllTexts())
        {
            foreach (TokenVariant variant in PaddedVariants)
            {
                byte[] json = JsonTokenBodies.Value(JsonTokenBodies.Apply(text, variant));
                Assert.AreEqual(expected, c.Read(json), $"{variant} '{text}'");
            }

            // Escaped first character together with padding: the escaped slow path with trimming.
            byte[] escapedAndPadded = JsonTokenBodies.Value("\\t " + JsonTokenBodies.EscapeFirst(text) + "\\r\\n");
            Assert.AreEqual(expected, c.Read(escapedAndPadded), $"escaped first character and padding '{text}'");
        }
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void AsciiWhitespaceAroundADictionaryKey_IsIgnored_ForEverySpellingOfTheValue(JsonConverterCase c)
    {
        foreach ((string text, object expected) in c.AllTexts())
        {
            foreach (TokenVariant variant in PaddedVariants)
            {
                byte[] json = JsonTokenBodies.Key(JsonTokenBodies.Apply(text, variant));
                Assert.AreEqual(7, c.ReadDictionary(json)[expected], $"{variant} '{text}'");
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void WhitespaceOnlyOrEmptyText_IsInvalid_ExceptForTheEmptyByteString(JsonConverterCase c)
    {
        foreach (string body in new[] { "", " ", "   ", "\\t\\r\\n" })
        {
            byte[] json = JsonTokenBodies.Value(body);
            if (c.EmptyTextIsValue)
                Assert.AreEqual(c.Default, c.Read(json), $"'{body}'");
            else
                Assert.AreEqual($"Invalid value for {c.Name}.", Assert.ThrowsExactly<JsonException>(() => c.Read(json), $"'{body}'").Message);
        }
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void WhitespaceInsideTheText_StaysInvalid(JsonConverterCase c)
    {
        string beforeLast = c.Canonical.Insert(c.Canonical.Length - 1, " ");
        string afterPrefix = c.Canonical.Insert(2, " ");

        foreach (string broken in new[] { beforeLast, afterPrefix })
        {
            JsonException valueError = Assert.ThrowsExactly<JsonException>(() => c.Read(JsonTokenBodies.Value(broken)), broken);
            Assert.AreEqual($"Invalid value for {c.Name}.", valueError.Message);
            Assert.ThrowsExactly<JsonException>(() => c.ReadDictionary(JsonTokenBodies.Key(broken)), broken);
        }
    }

    // Only space, tab, CR and LF are trimmed. Form feed, vertical tab, NBSP, NEL, EM space and NUL are not.
    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void OtherWhitespaceAndControlCharacters_AroundTheValue_AreRejected(JsonConverterCase c)
    {
        foreach (string pad in new[] { "\\f", "\\u000b", "\\u00a0", "\\u0085", "\\u2003", "\\u0000", " ", " " })
        {
            foreach (string body in new[] { pad + c.Canonical, c.Canonical + pad, pad + c.Canonical + pad })
            {
                JsonException error = Assert.ThrowsExactly<JsonException>(() => c.Read(JsonTokenBodies.Value(body)), body);
                Assert.AreEqual($"Invalid value for {c.Name}.", error.Message, body);
                Assert.ThrowsExactly<JsonException>(() => c.ReadDictionary(JsonTokenBodies.Key(body)), body);
            }
        }
    }

    // ----------------- quote characters are content, not framing -----------------

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void QuoteCharactersInsideTheToken_AreRejected_WhateverTheSpelling(JsonConverterCase c)
    {
        string[] bodies =
        [
            "\\\"" + c.Canonical + "\\\"", // both sides, as \"
            "\\\"" + c.Canonical, // leading only
            c.Canonical + "\\\"", // trailing only
            "\\u0022" + c.Canonical + "\\u0022", // both sides, as "
            c.Canonical.Insert(2, "\\\""), // inside
        ];

        foreach (string body in bodies)
        {
            JsonException error = Assert.ThrowsExactly<JsonException>(() => c.Read(JsonTokenBodies.Value(body)), body);
            Assert.AreEqual($"Invalid value for {c.Name}.", error.Message, body);
            Assert.ThrowsExactly<JsonException>(() => c.ReadDictionary(JsonTokenBodies.Key(body)), body);
        }
    }
}
