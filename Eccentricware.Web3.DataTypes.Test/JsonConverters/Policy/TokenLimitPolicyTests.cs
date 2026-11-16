using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// The raw-token cap of the eight converters that have a fixed one: pinned literals, the exact boundary for values,
/// split values and dictionary keys, the message, and that the cap never rejects a legal value. HexBytes has a
/// per-instance limit and is covered in its own classes.
/// </summary>
[TestClass]
public sealed class TokenLimitPolicyTests
{
    public static IEnumerable<JsonConverterCase> CappedCases => JsonConverterCases.Capped;

    // 6 bytes per character of the longest text (\uXXXX) plus 256 bytes of whitespace allowance.
    private static readonly Dictionary<string, int> PinnedCaps = new()
    {
        ["Address"] = 520, // 44 characters
        ["FunctionSelector"] = 316, // 10
        ["InstructionDiscriminator"] = 364, // 18
        ["Hash32"] = 652, // 66
        ["Signature"] = 1048, // 132
        ["uint256"] = 736, // 80
        ["int256"] = 736, // 80
        ["HexBigInteger"] = 24832, // 4096 digits
    };

    private static string TokenLimitMessage(JsonConverterCase c) =>
        $"The JSON string token for {c.Name} exceeds the {c.TokenCap.ToString("N0")}-byte limit.";

    [TestMethod]
    [DynamicData(nameof(CappedCases))]
    public void TheCap_IsThePinnedLiteral(JsonConverterCase c)
    {
        Assert.AreEqual(PinnedCaps[c.Name], c.TokenCap);
    }

    [TestMethod]
    public void EveryConverterWithAFixedCap_IsPinned()
    {
        CollectionAssert.AreEquivalent(PinnedCaps.Keys.ToArray(), JsonConverterCases.Capped.Select(c => c.Name).ToArray());
    }

    [TestMethod]
    [DynamicData(nameof(CappedCases))]
    public void EscapedValue_ExactlyAtTheCap_Parses_AndOneOverIsRejected(JsonConverterCase c)
    {
        byte[] atCap = JsonTokenBodies.Value(JsonTokenBodies.EscapedPaddedToRawLength(c.Canonical, c.TokenCap));
        byte[] overCap = JsonTokenBodies.Value(JsonTokenBodies.EscapedPaddedToRawLength(c.Canonical, c.TokenCap + 1));

        Assert.AreEqual(c.Expected, c.Read(atCap));
        JsonException error = Assert.ThrowsExactly<JsonException>(() => c.Read(overCap));
        Assert.AreEqual(TokenLimitMessage(c), error.Message);
    }

    [TestMethod]
    [DynamicData(nameof(CappedCases))]
    public void DictionaryKey_ExactlyAtTheCap_Parses_AndOneOverIsRejected(JsonConverterCase c)
    {
        byte[] atCap = JsonTokenBodies.Key(JsonTokenBodies.EscapedPaddedToRawLength(c.Canonical, c.TokenCap));
        byte[] overCap = JsonTokenBodies.Key(JsonTokenBodies.EscapedPaddedToRawLength(c.Canonical, c.TokenCap + 1));

        Assert.AreEqual(7, c.ReadDictionary(atCap)[c.Expected]);
        JsonException error = Assert.ThrowsExactly<JsonException>(() => c.ReadDictionary(overCap));
        Assert.AreEqual(TokenLimitMessage(c), error.Message);
    }

    [TestMethod]
    [DynamicData(nameof(CappedCases))]
    public void SplitValue_ExactlyAtTheCap_Parses_AndOneOverIsRejected(JsonConverterCase c)
    {
        foreach (bool escaped in new[] { false, true })
        {
            string Body(int rawLength) => escaped
                ? JsonTokenBodies.EscapedPaddedToRawLength(c.Canonical, rawLength)
                : c.Canonical + new string(' ', rawLength - c.Canonical.Length);

            byte[] atCap = JsonTokenBodies.Value(Body(c.TokenCap));
            byte[] overCap = JsonTokenBodies.Value(Body(c.TokenCap + 1));

            Assert.AreEqual(c.Expected, SegmentedUtf8Json.Deserialize(c.Type, atCap, 100), $"escaped={escaped}");
            JsonException error = Assert.ThrowsExactly<JsonException>(() => SegmentedUtf8Json.Deserialize(c.Type, overCap, 100), $"escaped={escaped}");
            Assert.AreEqual(TokenLimitMessage(c), error.Message);
        }
    }

    [TestMethod]
    [DynamicData(nameof(CappedCases))]
    public void SplitDictionaryKey_ExactlyAtTheCap_Parses_AndOneOverIsRejected(JsonConverterCase c)
    {
        byte[] atCap = JsonTokenBodies.Key(c.Canonical + new string(' ', c.TokenCap - c.Canonical.Length));
        byte[] overCap = JsonTokenBodies.Key(c.Canonical + new string(' ', c.TokenCap + 1 - c.Canonical.Length));

        var map = (System.Collections.IDictionary)SegmentedUtf8Json.Deserialize(c.DictionaryType, atCap, 100)!;
        Assert.AreEqual(7, map[c.Expected]);
        JsonException error = Assert.ThrowsExactly<JsonException>(() => SegmentedUtf8Json.Deserialize(c.DictionaryType, overCap, 100));
        Assert.AreEqual(TokenLimitMessage(c), error.Message);
    }

    [TestMethod]
    [DynamicData(nameof(CappedCases))]
    public void HugeEscapedValue_IsRejectedByLength_BeforeAnyCopy(JsonConverterCase c)
    {
        // 4 MiB of raw token: the answer must be the cap error, not an out-of-memory error or a parse error.
        string head = JsonTokenBodies.EscapeFirst(c.Canonical);
        byte[] json = new byte[4 * 1024 * 1024 + 2];
        json[0] = (byte)'"';
        Encoding.ASCII.GetBytes(head).CopyTo(json, 1);
        json.AsSpan(1 + head.Length, json.Length - 2 - head.Length).Fill((byte)' ');
        json[^1] = (byte)'"';

        JsonException error = Assert.ThrowsExactly<JsonException>(() => c.Read(json));

        Assert.AreEqual(TokenLimitMessage(c), error.Message);
    }

    [TestMethod]
    [DynamicData(nameof(CappedCases))]
    public void LongestLegalText_WithEveryCharacterEscaped_FitsTheCap_AsValueAndKey(JsonConverterCase c)
    {
        (string text, object expected) = c.Longest;
        string escaped = JsonTokenBodies.EscapeAll(text);

        Assert.IsLessThanOrEqualTo(c.TokenCap, escaped.Length, "the cap must leave room for the longest legal text, fully escaped");
        Assert.AreEqual(expected, c.Read(JsonTokenBodies.Value(escaped)));
        Assert.AreEqual(7, c.ReadDictionary(JsonTokenBodies.Key(escaped))[expected]);
        Assert.AreEqual(expected, SegmentedUtf8Json.Deserialize(c.Type, JsonTokenBodies.Value(escaped), 50));
    }

    [TestMethod]
    [DynamicData(nameof(CappedCases))]
    public void ContiguousUnescapedToken_IsNotSubjectToTheCap_ItIsParsedInPlace(JsonConverterCase c)
    {
        // Documents current behaviour: the cap bounds the copying slow path only, so an unescaped contiguous token with
        // trimmable padding far beyond the cap still reads (no buffer is taken for it).
        byte[] json = JsonTokenBodies.Value(c.Canonical + new string(' ', c.TokenCap + 500));

        Assert.AreEqual(c.Expected, c.Read(json));
    }
}
