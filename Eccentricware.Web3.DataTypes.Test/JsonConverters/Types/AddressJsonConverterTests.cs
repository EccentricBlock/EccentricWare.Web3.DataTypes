using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// <see cref="Address"/>-specific JSON behaviour (EVM and Solana families). Token shapes, keys, limits and error
/// contracts shared by all converters live in the Policy tests.
/// </summary>
[TestClass]
public sealed class AddressJsonConverterTests
{
    private const string EvmLower = "0x52908400098527886e0f7030069857d2e4169ee7";

    private static readonly Address Evm = Address.FromEvmBytes(Convert.FromHexString(EvmLower[2..]));
    private static readonly Address Solana = Address.FromSolanaBytes(Convert.FromHexString(JsonConverterCases.SolanaTokenProgramHex));

    [TestMethod]
    public void Evm_IsWrittenAsLowercasePrefixedHex_AndRoundTrips()
    {
        string json = JsonSerializer.Serialize(Evm);

        Assert.AreEqual("\"" + EvmLower + "\"", json);
        Address back = JsonSerializer.Deserialize<Address>(json);
        Assert.AreEqual(Evm, back);
        Assert.AreEqual(AddressType.Evm, back.Type);
    }

    [TestMethod]
    public void Solana_IsWrittenAsBase58_AndRoundTrips()
    {
        string json = JsonSerializer.Serialize(Solana);

        Assert.AreEqual("\"" + JsonConverterCases.SolanaTokenProgramBase58 + "\"", json);
        Address back = JsonSerializer.Deserialize<Address>(json);
        Assert.AreEqual(AddressType.Solana, back.Type);
        Assert.AreEqual(Solana, back);
    }

    [TestMethod]
    public void ZeroAddresses_KeepTheirFamily()
    {
        string zeroEvm = "\"0x" + new string('0', 40) + "\"";
        string zeroSolana = "\"" + new string('1', 32) + "\"";

        Assert.AreEqual(zeroEvm, JsonSerializer.Serialize(Address.ZeroEvm));
        Assert.AreEqual(zeroSolana, JsonSerializer.Serialize(Address.ZeroSolana));
        Assert.AreEqual(AddressType.Evm, JsonSerializer.Deserialize<Address>(zeroEvm).Type);
        Assert.AreEqual(AddressType.Solana, JsonSerializer.Deserialize<Address>(zeroSolana).Type);
    }

    [TestMethod]
    public void MixedCaseChecksummedText_ReadsAsTheLowercaseAddress()
    {
        // EIP-55 example address; the checksum is not verified, the value is the same address.
        Address expected = Address.FromEvmBytes(Convert.FromHexString("5aaeb6053f3e94c9b9a09f33669435e7ef1beaed"));

        Address checksummed = JsonSerializer.Deserialize<Address>("\"0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed\"");

        Assert.AreEqual(expected, checksummed);
        Assert.AreEqual("0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed", checksummed.ToString());
    }

    [TestMethod]
    public void MixedFamilies_InAnArray_RoundTrip_InOrder()
    {
        Address[] values = [Evm, Solana, Address.ZeroEvm, Address.ZeroSolana];

        string json = JsonSerializer.Serialize(values);

        Assert.AreEqual($"[\"{EvmLower}\",\"{JsonConverterCases.SolanaTokenProgramBase58}\",\"0x{new string('0', 40)}\",\"{new string('1', 32)}\"]", json);
        CollectionAssert.AreEqual(values, JsonSerializer.Deserialize<Address[]>(json));
    }

    [TestMethod]
    public void MixedFamilies_AsDictionaryKeys_AreDistinct_AndKeepTheirText()
    {
        var map = new Dictionary<Address, int> { [Evm] = 1, [Solana] = 2 };

        string json = JsonSerializer.Serialize(map);

        Assert.AreEqual($"{{\"{EvmLower}\":1,\"{JsonConverterCases.SolanaTokenProgramBase58}\":2}}", json);
        CollectionAssert.AreEquivalent(map, JsonSerializer.Deserialize<Dictionary<Address, int>>(json));
    }

    [TestMethod]
    [DataRow("0x12")] // too short
    [DataRow("0x52908400098527886e0f7030069857d2e4169ee")] // 39 digits
    [DataRow("0x52908400098527886e0f7030069857d2e4169ee77")] // 41 digits
    [DataRow("not-an-address")]
    [DataRow("0xzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [DataRow(EvmLower + "zz")]
    [DataRow("1" + JsonConverterCases.SolanaTokenProgramBase58)] // 45 characters
    [DataRow("11111111111111111111111111111111" + "1")] // 33 characters
    [DataRow("1111111111111111111111111111111")] // 31 characters
    [DataRow("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")] // 44 characters outside the Base58 alphabet and too large
    [DataRow("0OIl0OIl0OIl0OIl0OIl0OIl0OIl0OIl0OIl")] // characters Base58 excludes
    public void InvalidText_ThrowsTheInvalidValueError(string text)
    {
        JsonException error = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Address>("\"" + text + "\""));
        Assert.AreEqual("Invalid value for Address.", error.Message);

        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Dictionary<Address, int>>("{\"" + text + "\":1}"));
    }
}
