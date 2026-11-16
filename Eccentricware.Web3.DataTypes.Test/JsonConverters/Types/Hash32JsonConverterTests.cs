using EccentricWare.Web3.DataTypes.JsonConverters;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// <see cref="Hash32"/>-specific JSON behaviour: the Base58 / padded Base64 / bare hex spellings and the optional
/// "0x" prefix on write. Shared token and key behaviour is in the Policy tests.
/// </summary>
[TestClass]
public sealed class Hash32JsonConverterTests
{
    private static readonly Hash32 Ascending = new(Convert.FromHexString(JsonConverterCases.AscendingHash32Hex));

    private static readonly Hash32 AllOnes = new(Enumerable.Repeat((byte)0xFF, 32).ToArray());

    [TestMethod]
    public void RootValue_IsWrittenAsLowercasePrefixedHex_AndRoundTrips()
    {
        string json = JsonSerializer.Serialize(Ascending);

        Assert.AreEqual("\"0x" + JsonConverterCases.AscendingHash32Hex + "\"", json);
        Assert.AreEqual(Ascending, JsonSerializer.Deserialize<Hash32>(json));
    }

    [TestMethod]
    [DataRow("0x12")]
    [DataRow("1")] // a lone Base58 digit
    [DataRow("0x0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f")] // 62 digits
    [DataRow("0x0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f2021")] // 66 digits
    [DataRow("AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA")] // Base64 without its padding: not supported
    [DataRow("AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA==")] // too much padding
    [DataRow("0x0g02030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20")]
    public void InvalidText_ThrowsTheInvalidValueError(string text)
    {
        JsonException error = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Hash32>("\"" + text + "\""));

        Assert.AreEqual("Invalid value for Hash32.", error.Message);
    }

    [TestMethod]
    public void Base64WithEscapedSlashes_ReadsThroughTheEscapedPath()
    {
        // 32 bytes of 0xFF encode to "//////...8="; JSON allows "\/" for '/', which makes the token escaped.
        const string Base64 = "//////////////////////////////////////////8=";

        Assert.AreEqual(AllOnes, JsonSerializer.Deserialize<Hash32>("\"" + Base64 + "\""));
        Assert.AreEqual(AllOnes, JsonSerializer.Deserialize<Hash32>("\"" + Base64.Replace("/", "\\/") + "\""));
    }

    [TestMethod]
    public void UrlSafeBase64_ReadsTheSameBytesAsStandardBase64()
    {
        var expected = new Hash32(Convert.FromHexString(new string('f', 60) + "feff"));

        Assert.AreEqual(expected, JsonSerializer.Deserialize<Hash32>("\"_________________________________________v8=\""));
        Assert.AreEqual(expected, JsonSerializer.Deserialize<Hash32>("\"" + new string('/', 41) + "v8=\""));
    }

    // ----------------- the 0x prefix option -----------------

    [TestMethod]
    public void Converter_DefaultsToWritingThe0xPrefix()
    {
        Assert.IsTrue(new Hash32JsonConverter().WriteWith0xPrefix);
        Assert.IsTrue(new Hash32JsonConverter(true).WriteWith0xPrefix);
        Assert.IsFalse(new Hash32JsonConverter(writeWith0xPrefix: false).WriteWith0xPrefix);
    }

    [TestMethod]
    public void ConverterWithoutPrefix_WritesBareLowercaseHex_ForValuesAndKeys_AndStillReadsBothSpellings()
    {
        var options = new JsonSerializerOptions { Converters = { new Hash32JsonConverter(writeWith0xPrefix: false) } };
        string bare = JsonConverterCases.AscendingHash32Hex;

        Assert.AreEqual("\"" + bare + "\"", JsonSerializer.Serialize(Ascending, options));
        Assert.AreEqual("{\"" + bare + "\":1}", JsonSerializer.Serialize(new Dictionary<Hash32, int> { [Ascending] = 1 }, options));
        Assert.AreEqual("\"" + new string('0', 64) + "\"", JsonSerializer.Serialize(Hash32.Zero, options));

        Assert.AreEqual(Ascending, JsonSerializer.Deserialize<Hash32>("\"" + bare + "\"", options));
        Assert.AreEqual(Ascending, JsonSerializer.Deserialize<Hash32>("\"0x" + bare + "\"", options));
        Assert.AreEqual(1, JsonSerializer.Deserialize<Dictionary<Hash32, int>>("{\"0x" + bare + "\":1}", options)![Ascending]);
    }
}
