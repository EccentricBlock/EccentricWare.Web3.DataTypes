using System.Collections;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>The converters used through source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> metadata.</summary>
[TestClass]
public sealed class JsonSourceGenerationTests
{
    private static readonly GeneratedConvertersJsonContext Context = GeneratedConvertersJsonContext.Default;

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void RootValues_WriteTheCanonicalText_AndReadEverySpelling(JsonConverterCase c)
    {
        Assert.AreEqual($"\"{c.Canonical}\"", JsonSerializer.Serialize(c.Expected, c.Type, Context));
        Assert.AreEqual($"\"{c.DefaultText}\"", JsonSerializer.Serialize(c.Default, c.Type, Context));

        foreach ((string text, object expected) in c.AllTexts())
            Assert.AreEqual(expected, JsonSerializer.Deserialize($"\"{text}\"", c.Type, Context), text);
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void DictionaryKeys_WriteAndReadThroughTheGeneratedMetadata(JsonConverterCase c)
    {
        var map = (IDictionary)Activator.CreateInstance(c.DictionaryType)!;
        map.Add(c.Expected, 1);
        map.Add(c.Default, 2);

        string json = JsonSerializer.Serialize(map, c.DictionaryType, Context);

        Assert.AreEqual($"{{\"{c.Canonical}\":1,\"{c.DefaultText}\":2}}", json);
        var back = (IDictionary)JsonSerializer.Deserialize(json, c.DictionaryType, Context)!;
        Assert.AreEqual(1, back[c.Expected]);
        Assert.AreEqual(2, back[c.Default]);
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void InvalidText_ThrowsTheConverterMessage(JsonConverterCase c)
    {
        JsonException error = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize("\"0xzz\"", c.Type, Context));

        Assert.AreEqual($"Invalid value for {c.Name}.", error.Message);
    }

    [TestMethod]
    public void ModelWithPlainNullableAndListProperties_RoundTrips()
    {
        var address = JsonConverterCases.For<Address>();
        var hash = JsonConverterCases.For<Hash32>();
        var model = new GeneratedHolder { Addr = (Address)address.Expected, Opt = null, List = [(Hash32)hash.Expected, Hash32.Zero] };

        string json = JsonSerializer.Serialize(model, Context.GeneratedHolder);

        Assert.AreEqual($"{{\"Addr\":\"{address.Canonical}\",\"Opt\":null,\"List\":[\"{hash.Canonical}\",\"{hash.DefaultText}\"]}}", json);
        GeneratedHolder back = JsonSerializer.Deserialize(json, Context.GeneratedHolder)!;
        Assert.AreEqual(model.Addr, back.Addr);
        Assert.IsNull(back.Opt);
        CollectionAssert.AreEqual(model.List, back.List);
    }

    [TestMethod]
    public void GenerationTimeOptions_ChangeTheDocumentShape_NotTheTokens()
    {
        var address = JsonConverterCases.For<Address>();
        var model = new GeneratedHolder { Addr = (Address)address.Expected, Opt = (Address)address.Expected, List = [Hash32.Zero] };

        string json = JsonSerializer.Serialize(model, GeneratedIndentedCamelCaseJsonContext.Default.GeneratedHolder);

        StringAssert.Contains(json, $"\"addr\": \"{address.Canonical}\"");
        StringAssert.Contains(json, $"\"opt\": \"{address.Canonical}\"");
        StringAssert.Contains(json, $"\"{JsonConverterCases.For<Hash32>().DefaultText}\"");
        StringAssert.Contains(json, "\n");
        GeneratedHolder back = JsonSerializer.Deserialize(json, GeneratedIndentedCamelCaseJsonContext.Default.GeneratedHolder)!;
        Assert.AreEqual(model.Addr, back.Addr);
        Assert.AreEqual(model.Opt, back.Opt);
    }
}
