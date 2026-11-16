using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// EF Core creates converter types itself (<c>HasConversion&lt;TConverter&gt;()</c>) through a public parameterless
/// constructor; optional parameters do not count. This pins that, for every converter the library ships, in a model.
/// </summary>
[TestClass]
public sealed class ValueConverterModelConfigurationTests
{
    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void HasConversion_ByType_IsAcceptedForEveryConverter(string name)
        => ConverterCases.Get(name).Invoke(typeof(ValueConverterModelConfigurationTests), nameof(ByTypeImpl));

    private static void ByTypeImpl<T>(ConverterCase<T> c) where T : struct
    {
        Type converterType = c.CreateConverter().GetType();
        var builder = new ModelBuilder();
        builder.Entity<CaseRow<T>>().Property(r => r.Value).HasConversion(converterType);

        var model = (IModel)builder.Model;
        var converter = model.FindEntityType(typeof(CaseRow<T>))!.FindProperty(nameof(CaseRow<T>.Value))!.GetValueConverter();

        Assert.IsNotNull(converter, c.Name);
        Assert.AreEqual(converterType, converter.GetType());
        Assert.AreEqual(typeof(T), converter.ModelClrType);
        Assert.AreEqual(c.CreateConverter().ProviderClrType, converter.ProviderClrType);
    }

    [TestMethod]
    public void Hash32AndAddressConverters_RoundTrip_AfterTheParameterlessConstructor()
    {
        var hashConverter = new Hash32ValueConverter();
        Hash32 hash = new(1, 2, 3, 4);
        Assert.AreEqual(hash, hashConverter.ConvertFromProviderTyped(hashConverter.ConvertToProviderTyped(hash)));

        var addressConverter = new AddressValueConverter();
        Address evm = Address.FromEvmBytes(ConverterTestData.Ascending(20));
        Address solana = Address.FromSolanaBytes(ConverterTestData.Ascending(32));
        Assert.AreEqual(evm, addressConverter.ConvertFromProviderTyped(addressConverter.ConvertToProviderTyped(evm)));
        Assert.AreEqual(solana, addressConverter.ConvertFromProviderTyped(addressConverter.ConvertToProviderTyped(solana)));
    }

    [TestMethod]
    public void HexBigIntegerConverter_DefaultInstance_Initialises_AndRoundTrips()
    {
        // The static Default used to be initialised before the expressions it needs, so touching the type threw
        // TypeInitializationException.
        HexBigIntegerToBytesConverter converter = HexBigIntegerToBytesConverter.Default;
        foreach (var value in new[] { new HexBigInteger(0), new HexBigInteger(1), new HexBigInteger(-1), new HexBigInteger(System.Numerics.BigInteger.Pow(2, 200)) })
            Assert.AreEqual(value, converter.ConvertFromProviderTyped(converter.ConvertToProviderTyped(value)));
    }

    [TestMethod]
    public void HexBigIntegerConverter_DefaultInstance_CanBeSharedByTwoModels()
    {
        // Converter instances are stateless and EF shares them across contexts; one singleton must serve both models.
        foreach (string label in new[] { "first", "second" })
        {
            var builder = new ModelBuilder();
            builder.Entity<CaseRow<HexBigInteger>>().Property(r => r.Value).HasConversion(HexBigIntegerToBytesConverter.Default);

            var model = (IModel)builder.Model;
            var converter = model.FindEntityType(typeof(CaseRow<HexBigInteger>))!.FindProperty(nameof(CaseRow<HexBigInteger>.Value))!.GetValueConverter();

            Assert.AreSame(HexBigIntegerToBytesConverter.Default, converter, label);
        }
    }
}
