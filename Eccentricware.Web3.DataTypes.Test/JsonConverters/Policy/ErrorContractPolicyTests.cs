using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// The error contract of all nine converters: bad content is always a <see cref="JsonException"/> with the
/// documented message and a useful <see cref="JsonException.Path"/>, never another exception type.
/// </summary>
[TestClass]
public sealed class ErrorContractPolicyTests
{
    private const string BadText = "0xzz";

    private static string InvalidMessage(JsonConverterCase c) => $"Invalid value for {c.Name}.";

    private static byte[] WithRawByte(JsonConverterCase c, string body, byte raw, int position)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        var json = new List<byte> { (byte)'"' };
        json.AddRange(bodyBytes.Take(position));
        json.Add(raw);
        json.AddRange(bodyBytes.Skip(position));
        json.Add((byte)'"');
        return [.. json];
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void InvalidText_ThrowsTheInvalidValueMessage_WhateverTheTokenShape(JsonConverterCase c)
    {
        foreach (TokenVariant variant in new[] { TokenVariant.Plain, TokenVariant.EscapedFirst, TokenVariant.EscapedAll, TokenVariant.PaddedSpaces })
        {
            byte[] json = JsonTokenBodies.Value(JsonTokenBodies.Apply(BadText, variant));

            Assert.AreEqual(InvalidMessage(c), Assert.ThrowsExactly<JsonException>(() => c.Read(json), $"{variant}").Message, $"{variant}");
            Assert.AreEqual(InvalidMessage(c), Assert.ThrowsExactly<JsonException>(() => SegmentedUtf8Json.Deserialize(c.Type, json, 2), $"split {variant}").Message, $"split {variant}");
        }
    }

    public static IEnumerable<JsonConverterCase> FixedWidthCases => JsonConverterCases.Capped.Where(c => !c.AcceptsNumbers);

    [TestMethod]
    [DynamicData(nameof(FixedWidthCases))]
    public void FixedWidthTypes_RejectOneDigitTooFewOrTooMany(JsonConverterCase c)
    {
        foreach (string text in new[] { c.Canonical[..^1], c.Canonical + "0", c.Canonical[..4] })
        {
            Assert.AreEqual(InvalidMessage(c), Assert.ThrowsExactly<JsonException>(() => c.Read(JsonTokenBodies.Value(text)), text).Message, text);
            Assert.ThrowsExactly<JsonException>(() => c.ReadDictionary(JsonTokenBodies.Key(text)), text);
        }
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void Path_NamesTheFailingElement_PropertyOrKey(JsonConverterCase c)
    {
        string good = "\"" + c.Canonical + "\"";
        string bad = "\"" + BadText + "\"";

        JsonException inList = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize($"[{good},{bad}]", typeof(List<>).MakeGenericType(c.Type)));
        Assert.AreEqual("$[1]", inList.Path);
        Assert.AreEqual(InvalidMessage(c), inList.Message.Split(" Path:")[0]);

        Type stringMap = typeof(Dictionary<,>).MakeGenericType(typeof(string), c.Type);
        Assert.AreEqual("$.k", Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize($"{{\"k\":{bad}}}", stringMap)).Path);

        Type nested = typeof(Dictionary<,>).MakeGenericType(typeof(string), typeof(List<>).MakeGenericType(c.Type));
        Assert.AreEqual("$.L[1]", Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize($"{{\"L\":[{good},true]}}", nested)).Path);

        JsonException badKey = Assert.ThrowsExactly<JsonException>(() => c.ReadDictionary($"{{\"{BadText}\":1}}"));
        Assert.AreEqual("$." + BadText, badKey.Path);
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void RawInvalidUtf8InsideTheToken_IsAJsonException(JsonConverterCase c)
    {
        foreach (int position in new[] { 0, 2, c.Canonical.Length })
        {
            byte[] value = WithRawByte(c, c.Canonical, 0xFF, position);
            Assert.AreEqual(InvalidMessage(c), Assert.ThrowsExactly<JsonException>(() => c.Read(value), $"pos {position}").Message, $"pos {position}");
            Assert.ThrowsExactly<JsonException>(() => SegmentedUtf8Json.Deserialize(c.Type, value, 3), $"split pos {position}");

            // The same byte inside an escaped token: the escape forces the copying slow path.
            byte[] escaped = WithRawByte(c, JsonTokenBodies.EscapeFirst(c.Canonical), 0xFF, 6 + position);
            Assert.AreEqual(InvalidMessage(c), Assert.ThrowsExactly<JsonException>(() => c.Read(escaped), $"escaped pos {position}").Message, $"escaped pos {position}");

            // As a dictionary key System.Text.Json reports it with its own message, so only the type is asserted.
            byte[] keyJson = [.. Encoding.ASCII.GetBytes("{"), .. value, .. Encoding.ASCII.GetBytes(":1}")];
            Assert.ThrowsExactly<JsonException>(() => c.ReadDictionary(keyJson), $"key pos {position}");
        }
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void LookAlikeCharacters_AreRejected(JsonConverterCase c)
    {
        string fullwidthZero = "０" + c.Canonical[1..]; // U+FF10 FULLWIDTH DIGIT ZERO in place of '0'
        string nulInside = c.Canonical.Insert(c.Canonical.Length - 1, "\\u0000");
        string zeroWidthSpace = "​" + c.Canonical;

        foreach (string body in new[] { fullwidthZero, nulInside, zeroWidthSpace })
        {
            Assert.AreEqual(InvalidMessage(c), Assert.ThrowsExactly<JsonException>(() => c.Read(JsonTokenBodies.Value(body)), body).Message, body);
            Assert.ThrowsExactly<JsonException>(() => c.ReadDictionary(JsonTokenBodies.Key(body)), body);
        }
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void SurrogateEscapes_AreAJsonException_AsValuesAndKeys(JsonConverterCase c)
    {
        // Lone surrogates cannot be unescaped to UTF-8; a valid pair unescapes fine but is not a legal value.
        foreach (string body in new[] { "\\ud800", "\\udc00", "\\ud800\\u0041", "\\ud800" + c.Canonical, c.Canonical + "\\udfff", "\\ud83d\\ude00" })
        {
            Assert.AreEqual(InvalidMessage(c), Assert.ThrowsExactly<JsonException>(() => c.Read(JsonTokenBodies.Value(body)), body).Message, body);
            Assert.AreEqual(InvalidMessage(c), Assert.ThrowsExactly<JsonException>(() => SegmentedUtf8Json.Deserialize(c.Type, JsonTokenBodies.Value(body), 2), $"split {body}").Message, $"split {body}");
            Assert.ThrowsExactly<JsonException>(() => c.ReadDictionary(JsonTokenBodies.Key(body)), body);
        }
    }
}
