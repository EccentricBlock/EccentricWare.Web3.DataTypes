using EccentricWare.Web3.DataTypes.JsonConverters;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>The Base58 write option of <see cref="SignatureJsonConverter"/>.</summary>
[TestClass]
public sealed class SignatureBase58JsonTests
{
    [TestMethod]
    public void JsonConverter_DefaultsToHex_ForSolana()
    {
        Assert.IsFalse(new SignatureJsonConverter().WriteSolanaAsBase58);
        Assert.AreEqual("\"0x" + SignatureTestData.Ascending64Hex + "\"", JsonSerializer.Serialize(SignatureTestData.SampleSolana));
    }

    [TestMethod]
    public void JsonConverter_Base58Option_WritesSolanaAsBase58_AndEvmStaysHex()
    {
        var options = new JsonSerializerOptions { Converters = { new SignatureJsonConverter(writeSolanaAsBase58: true) } };

        Signature solana = SignatureTestData.SampleSolana;
        Assert.AreEqual("\"" + solana.ToBase58String() + "\"", JsonSerializer.Serialize(solana, options));
        Assert.AreEqual("\"0x" + SignatureTestData.Ascending65Hex + "\"", JsonSerializer.Serialize(SignatureTestData.SampleEvm, options));

        foreach (Signature value in SignatureTestData.RandomSignatures(200))
            Assert.AreEqual(value, JsonSerializer.Deserialize<Signature>(JsonSerializer.Serialize(value, options), options));
    }

    [TestMethod]
    public void JsonConverter_Base58Option_AppliesToDictionaryKeys_AndStaysReadableByTheDefaultConverter()
    {
        var options = new JsonSerializerOptions { Converters = { new SignatureJsonConverter(writeSolanaAsBase58: true) } };
        var dictionary = new Dictionary<Signature, int> { [SignatureTestData.SampleSolana] = 1, [SignatureTestData.SampleEvm] = 2 };

        string json = JsonSerializer.Serialize(dictionary, options);
        Assert.AreEqual("{\"" + SignatureTestData.SampleSolana.ToBase58String() + "\":1,\"0x" + SignatureTestData.Ascending65Hex + "\":2}", json);

        // Reading never depends on the write option.
        var back = JsonSerializer.Deserialize<Dictionary<Signature, int>>(json)!;
        Assert.AreEqual(1, back[SignatureTestData.SampleSolana]);
        Assert.AreEqual(2, back[SignatureTestData.SampleEvm]);
    }
}
