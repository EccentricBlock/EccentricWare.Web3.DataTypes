using System.Collections;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Null and wrong-token-type handling, identical for all nine converters.</summary>
[TestClass]
public sealed class NullAndTokenTypePolicyTests
{
    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void Null_IntoANonNullableValue_ThrowsCannotReadNull(JsonConverterCase c)
    {
        JsonException error = Assert.ThrowsExactly<JsonException>(() => c.Read("null"));

        Assert.AreEqual($"Cannot read null into {c.Name}.", error.Message);
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void Null_IntoANullableValue_IsNull_AndValuesStillRead(JsonConverterCase c)
    {
        Assert.IsNull(JsonSerializer.Deserialize("null", c.NullableType));
        Assert.AreEqual(c.Expected, JsonSerializer.Deserialize("\"" + c.Canonical + "\"", c.NullableType));
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void Null_InsideACollection_ThrowsForValues_AndIsNullForNullableElements(JsonConverterCase c)
    {
        string json = "[\"" + c.Canonical + "\",null]";

        JsonException error = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(json, typeof(List<>).MakeGenericType(c.Type)));
        Assert.AreEqual("$[1]", error.Path);

        var nullable = (IList)JsonSerializer.Deserialize(json, typeof(List<>).MakeGenericType(c.NullableType))!;
        Assert.HasCount(2, nullable);
        Assert.AreEqual(c.Expected, nullable[0]);
        Assert.IsNull(nullable[1]);
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void NonStringTokens_AreRejected_WithTheKindOfTokenTheConverterAccepts(JsonConverterCase c)
    {
        string expected = c.AcceptsNumbers
            ? $"Expected JSON string or number for {c.Name}."
            : $"Expected JSON string for {c.Name}.";

        foreach (string json in new[] { "true", "false", "[]", "{}" })
        {
            JsonException error = Assert.ThrowsExactly<JsonException>(() => c.Read(json), json);
            Assert.AreEqual(expected, error.Message, json);
        }
    }

    [TestMethod]
    [DynamicData(nameof(StringOnlyCases))]
    public void NumberTokens_AreRejected_ByStringOnlyConverters(JsonConverterCase c)
    {
        foreach (string json in new[] { "123", "1.5", "-1", "1e3", "0" })
        {
            JsonException error = Assert.ThrowsExactly<JsonException>(() => c.Read(json), json);
            Assert.AreEqual($"Expected JSON string for {c.Name}.", error.Message, json);
        }
    }

    public static IEnumerable<JsonConverterCase> StringOnlyCases => JsonConverterCases.StringOnly;

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void WrongTokenTypeAsAPropertyValue_ReportsThePathOfTheProperty(JsonConverterCase c)
    {
        Type map = typeof(Dictionary<,>).MakeGenericType(typeof(string), c.Type);

        JsonException error = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize("{\"k\":true}", map));

        Assert.AreEqual("$.k", error.Path);
    }
}
