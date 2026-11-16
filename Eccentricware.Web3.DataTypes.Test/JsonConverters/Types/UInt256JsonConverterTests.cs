using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// <see cref="uint256"/>-specific JSON behaviour: minimal hex output, hex / decimal string and number inputs, range
/// and sign limits. Token shape, key and cap behaviour shared by all converters is in the Policy tests.
/// </summary>
[TestClass]
public sealed class UInt256JsonConverterTests
{
    [TestMethod]
    public void Write_IsMinimalLowercaseHex()
    {
        Assert.AreEqual("\"0x0\"", JsonSerializer.Serialize(uint256.Zero));
        Assert.AreEqual("\"0x2a\"", JsonSerializer.Serialize(new uint256(42UL)));
        Assert.AreEqual("\"0xabcdef\"", JsonSerializer.Serialize(new uint256(0xABCDEFUL)));
        Assert.AreEqual("\"0x" + new string('f', 64) + "\"", JsonSerializer.Serialize(uint256.MaxValue));
        Assert.AreEqual("{\"0xabcdef\":1}", JsonSerializer.Serialize(new Dictionary<uint256, int> { [new uint256(0xABCDEFUL)] = 1 }));
    }

    [TestMethod]
    public void Read_EmptyHexDigits_IsZero()
    {
        Assert.AreEqual(uint256.Zero, JsonSerializer.Deserialize<uint256>("\"0x\""));
    }

    [TestMethod]
    public void Read_NumberAndStringForms_OfTheSameValue_Agree()
    {
        string max = JsonConverterCases.Uint256MaxDecimal;
        string maxHex = "0x" + new string('f', 64);

        Assert.AreEqual(uint256.MaxValue, JsonSerializer.Deserialize<uint256>(max));
        Assert.AreEqual(uint256.MaxValue, JsonSerializer.Deserialize<uint256>("\"" + max + "\""));
        Assert.AreEqual(uint256.MaxValue, JsonSerializer.Deserialize<uint256>("\"" + maxHex + "\""));
    }

    [TestMethod]
    [DataRow("-1")]
    [DataRow("-0")]
    [DataRow("1.5")]
    [DataRow("1.0")]
    [DataRow("1e3")]
    [DataRow("1E0")]
    [DataRow("-0.0")]
    [DataRow("\"-1\"")]
    [DataRow("\"-0\"")]
    [DataRow("\"+5\"")]
    [DataRow("\"1e3\"")]
    [DataRow("\"1.5\"")]
    [DataRow("\"0xg\"")]
    [DataRow("\"0x-1\"")]
    [DataRow("\"not a number\"")]
    [DataRow("\"\\u0030xg\"")]
    public void Read_InvalidToken_ThrowsTheInvalidValueError(string json)
    {
        JsonException error = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<uint256>(json));

        Assert.AreEqual("Invalid value for uint256.", error.Message);
    }

    [TestMethod]
    public void Read_ValuesAboveTwoToThe256Minus1_AreRejected_InEveryForm()
    {
        string decimalTooBig = "115792089237316195423570985008687907853269984665640564039457584007913129639936"; // 2^256, python3
        string hexTooBig = "0x1" + new string('0', 64);

        foreach (string json in new[] { decimalTooBig, "\"" + decimalTooBig + "\"", "\"" + hexTooBig + "\"", "\"\\u0030x1" + new string('0', 64) + "\"" })
            Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<uint256>(json), json);

        // Nine digits more than 2^256 - 1 cannot fit even as a number token.
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<uint256>(new string('9', 78)));
    }

    [TestMethod]
    public void Read_NumberTokenLongerThanAnyValidValue_IsRejected_WhenSplitAcrossSegments()
    {
        byte[] tooLong = Encoding.ASCII.GetBytes(new string('9', 81)); // beyond the 80-byte copy buffer

        JsonException error = Assert.ThrowsExactly<JsonException>(() => SegmentedUtf8Json.Deserialize<uint256>(tooLong, 40));
        Assert.AreEqual("Invalid value for uint256.", error.Message);

        Assert.ThrowsExactly<JsonException>(() => SegmentedUtf8Json.Deserialize<uint256>(Encoding.ASCII.GetBytes(new string('9', 78)), 40)); // > 2^256 - 1
        Assert.AreEqual(uint256.MaxValue, SegmentedUtf8Json.Deserialize<uint256>(Encoding.ASCII.GetBytes(JsonConverterCases.Uint256MaxDecimal), 40));
    }

    [TestMethod]
    public void Read_DecimalStringWithLeadingZeros_ReadsTheValue_ButHexWithMoreThan64Digits_IsRejected()
    {
        // Documents current behaviour: leading zeros are fine in a decimal string, but hex text is limited to 64 digits.
        Assert.AreEqual(new uint256(1UL), JsonSerializer.Deserialize<uint256>("\"" + new string('0', 100) + "1\""));
        Assert.AreEqual(new uint256(1UL), JsonSerializer.Deserialize<uint256>("\"" + new string('0', 5000) + "1\""));

        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<uint256>("\"0x" + new string('0', 100) + "1\""));
        Assert.AreEqual(new uint256(1UL), JsonSerializer.Deserialize<uint256>("\"0x" + new string('0', 63) + "1\""));
    }

    [TestMethod]
    [DataRow("-1")]
    [DataRow("1.5")]
    [DataRow("0xzz")]
    [DataRow("+5")]
    public void DictionaryKey_InvalidText_Throws(string key)
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Dictionary<uint256, int>>("{\"" + key + "\":1}"));
    }

    [TestMethod]
    public void DictionaryKey_DecimalAndHexSpellings_OfTheSameValue_ShareOneEntry()
    {
        var map = JsonSerializer.Deserialize<Dictionary<uint256, int>>("{\"255\":1,\"0xff\":2,\"0X0FF\":3,\"256\":4}")!;

        Assert.HasCount(2, map);
        Assert.AreEqual(3, map[new uint256(255UL)]);
        Assert.AreEqual(4, map[new uint256(256UL)]);
    }
}
