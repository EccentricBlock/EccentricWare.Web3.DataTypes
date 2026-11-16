namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// The untyped <c>ConvertToProvider</c> / <c>ConvertFromProvider</c> delegates EF itself calls, for every converter: boxed
/// round trip, provider CLR type, null handling and wrong-type input.
/// </summary>
[TestClass]
public sealed class ValueConverterUntypedEntryPointTests
{
    private static void Run(string name, string method) => ConverterCases.Get(name).Invoke(typeof(ValueConverterUntypedEntryPointTests), method);

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void BoxedValues_RoundTrip_AsTheDeclaredProviderType(string name) => Run(name, nameof(BoxedRoundTripImpl));

    private static void BoxedRoundTripImpl<T>(ConverterCase<T> c) where T : struct
    {
        var converter = c.CreateConverter();

        Assert.AreEqual(typeof(T), converter.ModelClrType);
        foreach (T sample in c.CreateSamples())
        {
            object? provider = converter.ConvertToProvider(sample);

            Assert.IsNotNull(provider, c.Name);
            Assert.IsInstanceOfType(provider, converter.ProviderClrType, c.Name);
            Assert.AreEqual(sample, converter.ConvertFromProvider(provider), c.Name);
        }
    }

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void Null_PassesThroughInBothDirections_WithoutInvokingTheConversion(string name) => Run(name, nameof(NullImpl));

    private static void NullImpl<T>(ConverterCase<T> c) where T : struct
    {
        var converter = c.CreateConverter();

        Assert.IsNull(converter.ConvertToProvider(null));
        Assert.IsNull(converter.ConvertFromProvider(null));
    }

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void WrongClrType_ThrowsInvalidCastException(string name) => Run(name, nameof(WrongTypeImpl));

    private static void WrongTypeImpl<T>(ConverterCase<T> c) where T : struct
    {
        var converter = c.CreateConverter();

        Assert.ThrowsExactly<InvalidCastException>(() => converter.ConvertToProvider(new object()));
        Assert.ThrowsExactly<InvalidCastException>(() => converter.ConvertFromProvider(new object()));
    }
}
