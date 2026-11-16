using EccentricWare.Web3.DataTypes.JsonConverters;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>The public surface of <see cref="HexBytesJsonConverter"/>: constants, constructor validation, attribute binding.</summary>
[TestClass]
public sealed class HexBytesJsonConverterApiTests
{
    [TestMethod]
    public void PublicConstants_HaveTheDocumentedValues()
    {
        Assert.AreEqual(33_554_432, HexBytesJsonConverter.DefaultMaxEscapedTokenBytes); // 32 MiB
        Assert.AreEqual(268_435_456, HexBytesJsonConverter.MaxConfigurableEscapedTokenBytes); // 256 MiB
        Assert.AreEqual(4096, HexBytesJsonConverter.MaxDictionaryKeyBytes);
        int fullHashBytes = HexBytes.MaxFullHashBytes; // the key limit is the size up to which GetHashCode hashes the whole value
        Assert.AreEqual(HexBytesJsonConverter.MaxDictionaryKeyBytes, fullHashBytes);
    }

    [TestMethod]
    public void ParameterlessConstructor_UsesTheDefaultLimit()
    {
        Assert.AreEqual(HexBytesJsonConverter.DefaultMaxEscapedTokenBytes, new HexBytesJsonConverter().MaxEscapedTokenBytes);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(1234)]
    [DataRow(HexBytesJsonConverter.DefaultMaxEscapedTokenBytes)]
    [DataRow(HexBytesJsonConverter.MaxConfigurableEscapedTokenBytes)]
    public void Constructor_AcceptsLimitsFromOneToTheCeiling(int limit)
    {
        Assert.AreEqual(limit, new HexBytesJsonConverter(limit).MaxEscapedTokenBytes);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    [DataRow(HexBytesJsonConverter.MaxConfigurableEscapedTokenBytes + 1)]
    [DataRow(int.MaxValue)]
    public void Constructor_RejectsLimitsOutsideTheRange_WithTheParameterNameAndRange(int limit)
    {
        ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HexBytesJsonConverter(limit));

        Assert.AreEqual("maxEscapedTokenBytes", error.ParamName);
        StringAssert.StartsWith(error.Message, $"The limit must be between 1 and {268_435_456.ToString("N0")}.");
    }

    [TestMethod]
    public void TheAttributeBoundConverter_IsTheDefaultInstance()
    {
        var converter = new JsonSerializerOptions().GetConverter(typeof(HexBytes));

        var hexBytesConverter = Assert.IsInstanceOfType<HexBytesJsonConverter>(converter);
        Assert.AreEqual(HexBytesJsonConverter.DefaultMaxEscapedTokenBytes, hexBytesConverter.MaxEscapedTokenBytes);
    }

    [TestMethod]
    public void ACustomConverterInTheOptions_ReplacesTheAttributeBoundOne()
    {
        var options = new JsonSerializerOptions { Converters = { new HexBytesJsonConverter(maxEscapedTokenBytes: 10) } };

        var converter = Assert.IsInstanceOfType<HexBytesJsonConverter>(options.GetConverter(typeof(HexBytes)));

        Assert.AreEqual(10, converter.MaxEscapedTokenBytes);
        JsonException error = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<HexBytes>("\"\\u0030xabcdef\"", options));
        Assert.AreEqual(HexBytesJsonTestKit.TokenLimitMessage(10), error.Message);
    }
}
