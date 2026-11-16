using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>The converters over readers created by <see cref="JsonElement"/>, <see cref="JsonDocument"/> and <see cref="JsonNode"/>.</summary>
[TestClass]
public sealed class JsonDocumentInteropTests
{
    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void SerializeToElement_AndNode_ProduceTheCanonicalString(JsonConverterCase c)
    {
        JsonElement element = JsonSerializer.SerializeToElement(c.Expected, c.Type);
        JsonNode? node = JsonSerializer.SerializeToNode(c.Expected, c.Type);

        Assert.AreEqual(JsonValueKind.String, element.ValueKind);
        Assert.AreEqual(c.Canonical, element.GetString());
        Assert.AreEqual(c.Canonical, node!.GetValue<string>());
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void ElementsAndNodes_DeserializeEverySpelling(JsonConverterCase c)
    {
        foreach ((string text, object expected) in c.AllTexts())
        {
            using JsonDocument document = JsonDocument.Parse(JsonTokenBodies.Value(JsonTokenBodies.EscapeFirst(text)));

            Assert.AreEqual(expected, document.RootElement.Deserialize(c.Type), $"element '{text}'");
            Assert.AreEqual(expected, JsonNode.Parse(JsonTokenBodies.Value(text))!.Deserialize(c.Type), $"node '{text}'");
        }
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void NumberElementsAndNodes_DeserializeForNumericConverters(JsonConverterCase c)
    {
        foreach ((string text, object expected) in c.NumberTokens)
        {
            using JsonDocument document = JsonDocument.Parse(text);

            Assert.AreEqual(expected, document.RootElement.Deserialize(c.Type), text);
            Assert.AreEqual(expected, JsonNode.Parse(text)!.Deserialize(c.Type), text);
        }
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void DictionaryKeys_RoundTripThroughJsonObject(JsonConverterCase c)
    {
        var map = (IDictionary)Activator.CreateInstance(c.DictionaryType)!;
        map.Add(c.Expected, 1);

        JsonNode? node = JsonSerializer.SerializeToNode(map, c.DictionaryType);

        Assert.AreEqual(c.Canonical, node!.AsObject().Single().Key);
        var back = (IDictionary)node.Deserialize(c.DictionaryType)!;
        Assert.AreEqual(1, back[c.Expected]);
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void InvalidElement_ThrowsTheConverterMessage(JsonConverterCase c)
    {
        using JsonDocument document = JsonDocument.Parse("\"0xzz\"");

        JsonException error = Assert.ThrowsExactly<JsonException>(() => document.RootElement.Deserialize(c.Type));

        Assert.AreEqual($"Invalid value for {c.Name}.", error.Message);
    }
}
