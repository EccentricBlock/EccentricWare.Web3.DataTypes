using EccentricWare.Web3.DataTypes.JsonConverters;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// The <c>[JsonConverter]</c> attribute on each value type binds the intended converter without any options, and the
/// attribute-bound instances have the documented default configuration.
/// </summary>
[TestClass]
public sealed class JsonConverterBindingTests
{
    private static readonly Dictionary<Type, Type> ExpectedConverters = new()
    {
        [typeof(Address)] = typeof(AddressJsonConverter),
        [typeof(FunctionSelector)] = typeof(FunctionSelectorJsonConverter),
        [typeof(InstructionDiscriminator)] = typeof(InstructionDiscriminatorJsonConverter),
        [typeof(Hash32)] = typeof(Hash32JsonConverter),
        [typeof(Signature)] = typeof(SignatureJsonConverter),
        [typeof(uint256)] = typeof(UInt256JsonConverter),
        [typeof(int256)] = typeof(Int256JsonConverter),
        [typeof(HexBigInteger)] = typeof(HexBigIntegerJsonConverter),
        [typeof(HexBytes)] = typeof(HexBytesJsonConverter),
    };

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void TheAttribute_BindsTheMatchingConverter_WithoutOptions(JsonConverterCase c)
    {
        Assert.AreEqual(ExpectedConverters[c.Type], new JsonSerializerOptions().GetConverter(c.Type).GetType());
    }

    [TestMethod]
    public void EveryTableCase_HasAnExpectedConverter()
    {
        CollectionAssert.AreEquivalent(ExpectedConverters.Keys.ToArray(), JsonConverterCases.All.Select(c => c.Type).ToArray());
    }

    [TestMethod]
    public void NullableValueTypes_UseTheSameConverter_ThroughTheNullableWrapper()
    {
        // Nullable<T> converters delegate to the value converter, so a value written through T? reads as T.
        foreach (JsonConverterCase c in JsonConverterCases.All)
            Assert.AreEqual("\"" + c.Canonical + "\"", JsonSerializer.Serialize(c.Expected, c.NullableType), c.Name);
    }

    [TestMethod]
    public void AttributeBoundInstances_HaveTheDocumentedDefaults()
    {
        var options = new JsonSerializerOptions();

        Assert.IsTrue(Assert.IsInstanceOfType<Hash32JsonConverter>(options.GetConverter(typeof(Hash32))).WriteWith0xPrefix);
        Assert.IsFalse(Assert.IsInstanceOfType<SignatureJsonConverter>(options.GetConverter(typeof(Signature))).WriteSolanaAsBase58);
    }
}
