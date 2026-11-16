using System.Collections;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Writing and reading each converter as a root value, an object property, a nullable property and a collection element.</summary>
[TestClass]
public sealed class PropertyAndCollectionPolicyTests
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

    private static (object Value, object? Optional) Unpack(JsonConverterCase c, object holder)
    {
        Type type = typeof(Holder<>).MakeGenericType(c.Type);
        return (type.GetProperty("Value")!.GetValue(holder)!, type.GetProperty("Optional")!.GetValue(holder));
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void RootValue_WritesTheCanonicalText_AndTheDefaultText_AndReadsThemBack(JsonConverterCase c)
    {
        Assert.AreEqual("\"" + c.Canonical + "\"", JsonSerializer.Serialize(c.Expected, c.Type));
        Assert.AreEqual("\"" + c.DefaultText + "\"", JsonSerializer.Serialize(c.Default, c.Type));

        Assert.AreEqual(c.Expected, c.Read("\"" + c.Canonical + "\""));
        Assert.AreEqual(c.Default, c.Read("\"" + c.DefaultText + "\""));
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void Property_WritesTheText_AndNullableProperty_WritesNullOrTheText(JsonConverterCase c)
    {
        Type holderType = typeof(Holder<>).MakeGenericType(c.Type);

        string withNull = JsonSerializer.Serialize(NewHolder(c, c.Expected, null), holderType);
        string withValue = JsonSerializer.Serialize(NewHolder(c, c.Default, c.Expected), holderType);

        Assert.AreEqual($"{{\"Value\":\"{c.Canonical}\",\"Optional\":null}}", withNull);
        Assert.AreEqual($"{{\"Value\":\"{c.DefaultText}\",\"Optional\":\"{c.Canonical}\"}}", withValue);
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void Property_AndNullableProperty_ReadBack_IncludingNullAndMissing(JsonConverterCase c)
    {
        Type holderType = typeof(Holder<>).MakeGenericType(c.Type);

        (object value, object? optional) = Unpack(c, JsonSerializer.Deserialize($"{{\"Value\":\"{c.Canonical}\",\"Optional\":null}}", holderType)!);
        Assert.AreEqual(c.Expected, value);
        Assert.IsNull(optional);

        (value, optional) = Unpack(c, JsonSerializer.Deserialize($"{{\"Value\":\"{c.DefaultText}\",\"Optional\":\"{c.Canonical}\"}}", holderType)!);
        Assert.AreEqual(c.Default, value);
        Assert.AreEqual(c.Expected, optional);

        (value, optional) = Unpack(c, JsonSerializer.Deserialize("{}", holderType)!);
        Assert.AreEqual(c.Default, value);
        Assert.IsNull(optional);
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void NullIntoANonNullableProperty_Throws_WithThePropertyPath(JsonConverterCase c)
    {
        Type holderType = typeof(Holder<>).MakeGenericType(c.Type);

        JsonException error = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize("{\"Value\":null}", holderType));

        Assert.AreEqual("$.Value", error.Path);
        StringAssert.Contains(error.Message, $"Cannot read null into {c.Name}.");
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void Collections_WriteAndReadElements_InOrder(JsonConverterCase c)
    {
        Type arrayType = c.Type.MakeArrayType();
        Type listType = typeof(List<>).MakeGenericType(c.Type);
        Type setType = typeof(HashSet<>).MakeGenericType(c.Type);
        string json = $"[\"{c.Canonical}\",\"{c.DefaultText}\",\"{c.Canonical}\"]";

        var array = (Array)Array.CreateInstance(c.Type, 2);
        array.SetValue(c.Expected, 0);
        array.SetValue(c.Default, 1);
        Assert.AreEqual($"[\"{c.Canonical}\",\"{c.DefaultText}\"]", JsonSerializer.Serialize(array, arrayType));

        var readArray = (Array)JsonSerializer.Deserialize(json, arrayType)!;
        Assert.AreEqual(3, readArray.Length);
        Assert.AreEqual(c.Expected, readArray.GetValue(0));
        Assert.AreEqual(c.Default, readArray.GetValue(1));
        Assert.AreEqual(c.Expected, readArray.GetValue(2));

        var list = (IList)JsonSerializer.Deserialize(json, listType)!;
        Assert.HasCount(3, list);

        var set = (IEnumerable)JsonSerializer.Deserialize(json, setType)!;
        Assert.AreEqual(2, set.Cast<object>().Count(), "the duplicate element collapses in a set");
    }
}
