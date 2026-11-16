using EccentricWare.Web3.DataTypes.JsonConverters;
using System.Text;
using System.Text.Json;
using static EccentricWare.Web3.DataTypes.Tests.HexBytesJsonTestKit;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// The two HexBytes limits and the two different messages they produce: the escaped-token limit (per converter
/// instance, applies to values and keys) and the dictionary-key limit (4,096 bytes, plus a raw-token guard of 49,420 bytes).
/// </summary>
[TestClass]
public sealed class HexBytesJsonConverterLimitTests
{
    // 6 bytes per character of the longest legal key ("0x" + 8,192 digits) as \uXXXX, plus 256 bytes of whitespace.
    private const int MaxKeyRawTokenLength = 49_420;

    private static byte[] KeyJson(string body) => JsonTokenBodies.Key(body);

    private static Dictionary<HexBytes, int> ReadKeys(byte[] json, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize<Dictionary<HexBytes, int>>(json, options)!;

    // ----------------- escaped-token limit -----------------

    [TestMethod]
    public void EscapedToken_AtTheLimit_Parses_AndOneOverIsRejectedWithTheTokenMessage()
    {
        var converter = new HexBytesJsonConverter(maxEscapedTokenBytes: 1000);

        byte[] Escaped(int rawLength) => JsonTokenBodies.Value("\\u0030xabcd" + new string(' ', rawLength - "\\u0030xabcd".Length));

        Assert.AreEqual(H("abcd"), ReadDirect(converter, Escaped(1000)));
        JsonException error = Assert.ThrowsExactly<JsonException>(() => ReadDirect(converter, Escaped(1001)));
        Assert.AreEqual(TokenLimitMessage(1000), error.Message);
    }

    [TestMethod]
    public void LimitOfOne_AcceptsUnescapedText_AndRejectsAnyEscape()
    {
        var converter = new HexBytesJsonConverter(maxEscapedTokenBytes: 1);

        Assert.AreEqual(HexBytes.Empty, ReadDirect(converter, JsonTokenBodies.Value("0x")));
        JsonException error = Assert.ThrowsExactly<JsonException>(() => ReadDirect(converter, JsonTokenBodies.Value("\\u0030"))); // 6 raw bytes
        Assert.AreEqual(TokenLimitMessage(1), error.Message);
    }

    [TestMethod]
    public void EscapedLimit_AppliesToEscapedTokens_ContiguousOrSplit_ButNotToUnescapedOnes()
    {
        var converter = new HexBytesJsonConverter(maxEscapedTokenBytes: 64);
        byte[] plain = JsonTokenBodies.Value("0x" + new string('a', 200));
        byte[] escaped = JsonTokenBodies.Value("\\u0030x" + new string('a', 200));

        // Unescaped, contiguous or split: decoded in place with no buffer, so the limit does not apply.
        Assert.AreEqual(100, ReadDirect(converter, plain).Length);
        Assert.AreEqual(100, ReadSplit(converter, plain, 50).Length);

        // Escaped: it must be unescaped into a buffer first, so the limit applies however it arrives.
        Assert.AreEqual(TokenLimitMessage(64), Assert.ThrowsExactly<JsonException>(() => ReadDirect(converter, escaped)).Message);
        Assert.AreEqual(TokenLimitMessage(64), Assert.ThrowsExactly<JsonException>(() => ReadSplit(converter, escaped, 50)).Message);
    }

    [TestMethod]
    public void EscapedLimit_AppliesToDictionaryKeys_Too_AndTheDefaultConverterAcceptsTheSameKey()
    {
        byte[] json = KeyJson("\\u0030xabcdef"); // 13 raw bytes
        var limited = new JsonSerializerOptions { Converters = { new HexBytesJsonConverter(maxEscapedTokenBytes: 10) } };

        JsonException error = Assert.ThrowsExactly<JsonException>(() => ReadKeys(json, limited));

        Assert.AreEqual(TokenLimitMessage(10), error.Message, "the per-converter limit wins over the key limit");
        Assert.AreEqual(7, ReadKeys(json)[H("abcdef")]);
    }

    // ----------------- dictionary-key limit -----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void KeyRawTokenGuard_AtTheLimitParses_OneOverIsRejectedWithTheKeyMessage(bool escaped)
    {
        string head = escaped ? "\\u0030xab" : "0xab";
        byte[] Key(int rawLength) => KeyJson(head + new string(' ', rawLength - head.Length));

        Dictionary<HexBytes, int> atLimit = ReadKeys(Key(MaxKeyRawTokenLength));
        JsonException error = Assert.ThrowsExactly<JsonException>(() => ReadKeys(Key(MaxKeyRawTokenLength + 1)));

        Assert.AreEqual(1, atLimit.Count);
        Assert.AreEqual(7, atLimit[H("ab")]);
        Assert.AreEqual(KeyLimitMessage, error.Message);
    }

    [TestMethod]
    [DataRow(8192, true)] // exactly 4,096 bytes
    [DataRow(8191, true)] // odd digit count, still 4,096 bytes after left padding
    [DataRow(8193, false)] // 4,097 bytes
    [DataRow(8194, false)]
    [DataRow(1_000_000, false)]
    public void DecodedKeyLength_UpTo4096Bytes_IsAccepted_AboveIsRejectedWithTheKeyMessage(int digits, bool accepted)
    {
        byte[] json = KeyJson("0x" + new string('a', digits));

        if (accepted)
        {
            Dictionary<HexBytes, int> map = ReadKeys(json);
            Assert.AreEqual(1, map.Count);
            Assert.AreEqual((digits + 1) / 2, map.Keys.Single().Length);
        }
        else
        {
            Assert.AreEqual(KeyLimitMessage, Assert.ThrowsExactly<JsonException>(() => ReadKeys(json)).Message);
        }
    }

    [TestMethod]
    public void OversizedKey_IsRejectedWhetherEscapedOrSplit_AndTheSameValueIsFineAsAValue()
    {
        string tooLarge = PatternText(HexBytesJsonConverter.MaxDictionaryKeyBytes + 1);
        byte[] plain = KeyJson(tooLarge);
        byte[] escaped = KeyJson("\\u0030" + tooLarge[1..]);

        Assert.AreEqual(KeyLimitMessage, Assert.ThrowsExactly<JsonException>(() => ReadKeys(plain)).Message);
        Assert.AreEqual(KeyLimitMessage, Assert.ThrowsExactly<JsonException>(() => ReadKeys(escaped)).Message);
        Assert.AreEqual(KeyLimitMessage, Assert.ThrowsExactly<JsonException>(() => SegmentedUtf8Json.Deserialize<Dictionary<HexBytes, int>>(plain, 100)).Message);
        Assert.AreEqual(KeyLimitMessage, Assert.ThrowsExactly<JsonException>(() => SegmentedUtf8Json.Deserialize<Dictionary<HexBytes, int>>(escaped, 100)).Message);

        Assert.AreEqual(HexBytesJsonConverter.MaxDictionaryKeyBytes + 1, JsonSerializer.Deserialize<HexBytes>("\"" + tooLarge + "\"").Length);
    }

    [TestMethod]
    public void LongestLegalKey_WithEveryCharacterEscaped_Parses()
    {
        string text = PatternText(HexBytesJsonConverter.MaxDictionaryKeyBytes); // "0x" + 8,192 digits
        byte[] json = KeyJson(JsonTokenBodies.EscapeAll(text)); // 8,194 characters x 6 bytes = 49,164 raw bytes

        Dictionary<HexBytes, int> map = ReadKeys(json);

        Assert.AreEqual(1, map.Count);
        Assert.AreEqual(HexBytesJsonConverter.MaxDictionaryKeyBytes, map.Keys.Single().Length);
        CollectionAssert.AreEqual(PatternBytes(HexBytesJsonConverter.MaxDictionaryKeyBytes), map.Keys.Single().Span.ToArray());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("0x")]
    [DataRow("\\t")]
    [DataRow("   ")]
    public void EmptyOrWhitespaceOnlyKey_IsTheEmptyByteString(string body)
    {
        Dictionary<HexBytes, int> map = ReadKeys(Encoding.ASCII.GetBytes("{\"" + body + "\":1}"));

        Assert.AreEqual(1, map[HexBytes.Empty]);
    }

    [TestMethod]
    public void KeysThatDifferOnlyInTheMiddle_AreDistinctUpToTheKeyLimit()
    {
        // The limit exists so keys are hashed over their whole content; both keys must survive as two entries.
        byte[] a = PatternBytes(HexBytesJsonConverter.MaxDictionaryKeyBytes);
        byte[] b = (byte[])a.Clone();
        b[a.Length / 2] ^= 0xFF;
        string json = "{\"0x" + Convert.ToHexString(a) + "\":1,\"0x" + Convert.ToHexString(b) + "\":2}";

        Dictionary<HexBytes, int> map = JsonSerializer.Deserialize<Dictionary<HexBytes, int>>(json)!;

        Assert.AreEqual(2, map.Count);
        Assert.AreEqual(1, map[new HexBytes(a)]);
        Assert.AreEqual(2, map[new HexBytes(b)]);
    }

    // ----------------- writing keys -----------------

    [TestMethod]
    [DataRow(1)]
    [DataRow(255)] // largest key formatted in one piece on the stack
    [DataRow(256)] // first key that needs the pooled buffer
    [DataRow(HexBytesJsonConverter.MaxDictionaryKeyBytes)] // largest key that reads back
    public void WrittenKeys_AreTheCanonicalText_AcrossTheBufferBoundaries_AndReadBack(int length)
    {
        HexBytes key = Pattern(length);

        string json = JsonSerializer.Serialize(new Dictionary<HexBytes, string> { [key] = "v" });

        Assert.AreEqual("{\"" + PatternText(length) + "\":\"v\"}", json);
        Assert.AreEqual("v", JsonSerializer.Deserialize<Dictionary<HexBytes, string>>(json)![key]);
    }

    [TestMethod]
    [DataRow(HexBytesJsonConverter.MaxDictionaryKeyBytes + 1)]
    [DataRow(100_000)]
    public void WritingAKeyAboveTheLimit_ThrowsTheKeyMessage_SoWrittenDocumentsAlwaysReadBack(int length)
    {
        var map = new Dictionary<HexBytes, int> { [Pattern(length)] = 1 };

        JsonException error = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Serialize(map));

        Assert.AreEqual(KeyLimitMessage, error.Message);
    }
}
