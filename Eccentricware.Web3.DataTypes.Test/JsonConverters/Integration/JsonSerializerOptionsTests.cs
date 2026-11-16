using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// <see cref="JsonSerializerOptions"/> variations must not change the token text the converters read or write:
/// indentation, naming policies, ignore conditions, encoders and the Web defaults only affect the surrounding JSON.
/// </summary>
[TestClass]
public sealed class JsonSerializerOptionsTests
{
    private sealed class Holder<T> where T : struct
    {
        public T Value { get; set; }
        public T? Optional { get; set; }
    }

    private static object NewHolder(JsonConverterCase c, object value, object? optional)
    {
        Type type = typeof(Holder<>).MakeGenericType(c.Type);
        object holder = Activator.CreateInstance(type)!;
        type.GetProperty("Value")!.SetValue(holder, value);
        type.GetProperty("Optional")!.SetValue(holder, optional);
        return holder;
    }

    private static Type HolderType(JsonConverterCase c) => typeof(Holder<>).MakeGenericType(c.Type);

    private static object? PropertyOf(JsonConverterCase c, object holder, string name) => HolderType(c).GetProperty(name)!.GetValue(holder);

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void WriteIndented_KeepsTheTokenText_AndReadsBack(JsonConverterCase c)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };

        string json = JsonSerializer.Serialize(NewHolder(c, c.Expected, c.Default), HolderType(c), options);

        StringAssert.Contains(json, $"\"Value\": \"{c.Canonical}\"");
        StringAssert.Contains(json, $"\"Optional\": \"{c.DefaultText}\"");
        object back = JsonSerializer.Deserialize(json, HolderType(c), options)!;
        Assert.AreEqual(c.Expected, PropertyOf(c, back, "Value"));
        Assert.AreEqual(c.Default, PropertyOf(c, back, "Optional"));
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void CamelCaseNaming_RenamesPropertiesOnly_NotTokens(JsonConverterCase c)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        string json = JsonSerializer.Serialize(NewHolder(c, c.Expected, null), HolderType(c), options);

        Assert.AreEqual($"{{\"value\":\"{c.Canonical}\",\"optional\":null}}", json);
        object back = JsonSerializer.Deserialize(json, HolderType(c), options)!;
        Assert.AreEqual(c.Expected, PropertyOf(c, back, "Value"));
        Assert.IsNull(PropertyOf(c, back, "Optional"));
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void WebDefaults_ReadCamelCaseProperties_AndWriteThemBack(JsonConverterCase c)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        object holder = JsonSerializer.Deserialize($"{{\"value\":\"{c.Canonical}\",\"optional\":null}}", HolderType(c), options)!;

        Assert.AreEqual(c.Expected, PropertyOf(c, holder, "Value"));
        Assert.IsNull(PropertyOf(c, holder, "Optional"));
        Assert.AreEqual($"{{\"value\":\"{c.Canonical}\",\"optional\":null}}", JsonSerializer.Serialize(holder, HolderType(c), options));
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void IgnoreConditions_DecideWhetherThePropertyIsWritten_NotHowTheConverterWritesIt(JsonConverterCase c)
    {
        var whenNull = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var whenDefault = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

        Assert.AreEqual($"{{\"Value\":\"{c.Canonical}\"}}", JsonSerializer.Serialize(NewHolder(c, c.Expected, null), HolderType(c), whenNull));
        Assert.AreEqual($"{{\"Value\":\"{c.DefaultText}\"}}", JsonSerializer.Serialize(NewHolder(c, c.Default, null), HolderType(c), whenNull), "a default value is not null");

        Assert.AreEqual("{}", JsonSerializer.Serialize(NewHolder(c, c.Default, null), HolderType(c), whenDefault));
        Assert.AreEqual($"{{\"Optional\":\"{c.Canonical}\"}}", JsonSerializer.Serialize(NewHolder(c, c.Default, c.Expected), HolderType(c), whenDefault));
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void Encoders_DoNotChangeTheWrittenText(JsonConverterCase c)
    {
        var relaxed = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var map = (IDictionary)Activator.CreateInstance(c.DictionaryType)!;
        map.Add(c.Expected, 1);

        Assert.AreEqual($"\"{c.Canonical}\"", JsonSerializer.Serialize(c.Expected, c.Type));
        Assert.AreEqual($"\"{c.Canonical}\"", JsonSerializer.Serialize(c.Expected, c.Type, relaxed));
        Assert.AreEqual($"{{\"{c.Canonical}\":1}}", JsonSerializer.Serialize(map, c.DictionaryType, relaxed));
    }

    [TestMethod]
    public void Collections_ReadWithDuplicates_FollowTheCollectionSemantics()
    {
        Assert.HasCount(1, JsonSerializer.Deserialize<HashSet<FunctionSelector>>("[\"0xa9059cbb\",\"0XA9059CBB\"]")!);
        Assert.HasCount(2, JsonSerializer.Deserialize<Hash32[]>($"[\"{new string('1', 32)}\",\"0x{new string('0', 64)}\"]")!); // equal values: arrays keep both
        Assert.AreEqual(1, JsonSerializer.Deserialize<Dictionary<Hash32, int>>($"{{\"{new string('1', 32)}\":1,\"0x{new string('0', 64)}\":2}}")!.Count);
    }
}
