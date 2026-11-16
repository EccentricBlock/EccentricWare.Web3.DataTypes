using System.Collections;
using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Dictionary-key behaviour (<c>WriteAsPropertyName</c> / <c>ReadAsPropertyName</c>) for all nine converters.</summary>
[TestClass]
public sealed class DictionaryKeyPolicyTests
{
    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void Keys_AreWrittenAsTheExactCanonicalText_AndReadBack(JsonConverterCase c)
    {
        var map = (IDictionary)Activator.CreateInstance(c.DictionaryType)!;
        map.Add(c.Expected, 1);
        map.Add(c.Default, 2);

        string json = JsonSerializer.Serialize(map, c.DictionaryType);

        Assert.AreEqual($"{{\"{c.Canonical}\":1,\"{c.DefaultText}\":2}}", json);
        IDictionary back = c.ReadDictionary(json);
        Assert.AreEqual(2, back.Count);
        Assert.AreEqual(1, back[c.Expected]);
        Assert.AreEqual(2, back[c.Default]);
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void Keys_AcceptEverySpellingTheValueConverterAccepts(JsonConverterCase c)
    {
        foreach ((string text, object expected) in c.AllTexts())
        {
            IDictionary map = c.ReadDictionary("{\"" + text + "\":7}");

            Assert.AreEqual(1, map.Count, text);
            Assert.AreEqual(7, map[expected], text);
        }
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void TwoSpellingsOfOneKey_CollapseToOneEntry_LastWriteWins(JsonConverterCase c)
    {
        var json = new StringBuilder("{\"" + c.Canonical + "\":0");
        for (int i = 0; i < c.Respellings.Length; i++)
            json.Append($",\"{c.Respellings[i]}\":{i + 1}");
        json.Append(",\" " + c.Canonical + " \":99}");

        IDictionary map = c.ReadDictionary(json.ToString());

        Assert.AreEqual(1, map.Count);
        Assert.AreEqual(99, map[c.Expected]);
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void InvalidKeys_ThrowJsonException(JsonConverterCase c)
    {
        foreach (string key in new[] { "not a value", "0xzz", "\\ud800", "\\udc00", "-", "0x-1" })
            Assert.ThrowsExactly<JsonException>(() => c.ReadDictionary("{\"" + key + "\":1}"), key);
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void EmptyKey_IsInvalid_ExceptForTheEmptyByteString(JsonConverterCase c)
    {
        if (c.EmptyTextIsValue)
        {
            IDictionary map = c.ReadDictionary("{\"\":1}");
            Assert.AreEqual(1, map[c.Default]);
        }
        else
        {
            Assert.ThrowsExactly<JsonException>(() => c.ReadDictionary("{\"\":1}"));
        }
    }

    [TestMethod]
    public void DictionaryKeyPolicy_DoesNotRewriteTheTextTheConverterWrites()
    {
        // Solana Base58 is case-sensitive: a camelCase key policy would corrupt it if it applied.
        JsonConverterCase address = JsonConverterCases.For<Address>();
        var map = new Dictionary<Address, int>
        {
            [(Address)address.Alternates[0].Value] = 1,
            [(Address)address.Expected] = 2,
        };
        var options = new JsonSerializerOptions { DictionaryKeyPolicy = JsonNamingPolicy.CamelCase };

        string json = JsonSerializer.Serialize(map, options);

        Assert.AreEqual($"{{\"{JsonConverterCases.SolanaTokenProgramBase58}\":1,\"0x{new string('a', 40)}\":2}}", json);
        Assert.AreEqual(2, JsonSerializer.Deserialize<Dictionary<Address, int>>(json, options)!.Count);
    }
}
