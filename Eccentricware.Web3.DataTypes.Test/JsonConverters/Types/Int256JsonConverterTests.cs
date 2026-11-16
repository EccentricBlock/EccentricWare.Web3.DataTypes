using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// <see cref="int256"/>-specific JSON behaviour: two's-complement hex output, signed decimal input, range limits.
/// Token shape, key and cap behaviour shared by all converters is in the Policy tests.
/// </summary>
[TestClass]
public sealed class Int256JsonConverterTests
{
    // Two's complement at full width, as 64 hex digits (python3: (-12345) % 2**256).
    private static readonly string MinusTwelveThousandHex = "0x" + new string('f', 60) + "cfc7";
    private static readonly string MinValueHex = "0x8" + new string('0', 63);
    private static readonly string MaxValueHex = "0x7" + new string('f', 63);

    [TestMethod]
    public void Write_UsesMinimalHexForNonNegative_AndFullWidthTwosComplementForNegative()
    {
        Assert.AreEqual("\"0x0\"", JsonSerializer.Serialize(int256.Zero));
        Assert.AreEqual("\"0x1\"", JsonSerializer.Serialize(int256.One));
        Assert.AreEqual("\"0xabcdef\"", JsonSerializer.Serialize(new int256(0xABCDEFL)));
        Assert.AreEqual("\"0x" + new string('f', 64) + "\"", JsonSerializer.Serialize(int256.MinusOne));
        Assert.AreEqual("\"" + MinusTwelveThousandHex + "\"", JsonSerializer.Serialize(new int256(-12345L)));
        Assert.AreEqual("\"" + MinValueHex + "\"", JsonSerializer.Serialize(int256.MinValue));
        Assert.AreEqual("\"" + MaxValueHex + "\"", JsonSerializer.Serialize(int256.MaxValue));
    }

    [TestMethod]
    public void WrittenKeys_UseTheSameTextAsValues_AndReadBack()
    {
        var map = new Dictionary<int256, int>
        {
            [int256.MinValue] = 1,
            [new int256(-12345L)] = 2,
            [int256.Zero] = 3,
            [int256.MaxValue] = 4,
        };

        string json = JsonSerializer.Serialize(map);

        Assert.AreEqual($"{{\"{MinValueHex}\":1,\"{MinusTwelveThousandHex}\":2,\"0x0\":3,\"{MaxValueHex}\":4}}", json);
        var back = JsonSerializer.Deserialize<Dictionary<int256, int>>(json)!;
        Assert.HasCount(4, back);
        Assert.AreEqual(2, back[new int256(-12345L)]);
        Assert.AreEqual(1, back[int256.MinValue]);
    }

    [TestMethod]
    public void RoundTrip_Extremes()
    {
        foreach (int256 value in new[] { int256.Zero, int256.One, int256.MinusOne, int256.MinValue, int256.MaxValue, new int256(-12345L), new int256(12345L) })
            Assert.AreEqual(value, JsonSerializer.Deserialize<int256>(JsonSerializer.Serialize(value)));
    }

    [TestMethod]
    public void Read_HexIsTwosComplementOnlyAtFullWidth()
    {
        Assert.AreEqual(new int256(255L), JsonSerializer.Deserialize<int256>("\"0xff\""));
        Assert.AreEqual(new int256(128L), JsonSerializer.Deserialize<int256>("\"0x80\""));
        Assert.AreEqual(int256.MinusOne, JsonSerializer.Deserialize<int256>("\"0x" + new string('f', 64) + "\""));
        Assert.AreEqual(int256.MinValue, JsonSerializer.Deserialize<int256>("\"" + MinValueHex + "\""));
        Assert.AreEqual(int256.MaxValue, JsonSerializer.Deserialize<int256>("\"" + MaxValueHex + "\""));
        Assert.AreEqual(new int256(-12345L), JsonSerializer.Deserialize<int256>("\"" + MinusTwelveThousandHex + "\""));
    }

    [TestMethod]
    public void Read_SignedDecimals_AsStringsAndNumbers()
    {
        Assert.AreEqual(new int256(-5L), JsonSerializer.Deserialize<int256>("\"-5\""));
        Assert.AreEqual(new int256(-5L), JsonSerializer.Deserialize<int256>("-5"));
        Assert.AreEqual(new int256(5L), JsonSerializer.Deserialize<int256>("\"+5\""));
        Assert.AreEqual(int256.Zero, JsonSerializer.Deserialize<int256>("\"-0\""));
        Assert.AreEqual(int256.Zero, JsonSerializer.Deserialize<int256>("-0"));
        Assert.AreEqual(int256.MaxValue, JsonSerializer.Deserialize<int256>("57896044618658097711785492504343953926634992332820282019728792003956564819967")); // 2^255 - 1, python3
        Assert.AreEqual(int256.MinValue, JsonSerializer.Deserialize<int256>(JsonConverterCases.Int256MinDecimal));
    }

    [TestMethod]
    [DataRow("1.5")]
    [DataRow("1.0")]
    [DataRow("1e3")]
    [DataRow("1E0")]
    [DataRow("-0.0")]
    [DataRow("\"--1\"")]
    [DataRow("\"- 1\"")]
    [DataRow("\"1.5\"")]
    [DataRow("\"0xg\"")]
    [DataRow("\"not a number\"")]
    public void Read_InvalidToken_ThrowsTheInvalidValueError(string json)
    {
        JsonException error = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<int256>(json));

        Assert.AreEqual("Invalid value for int256.", error.Message);
    }

    [TestMethod]
    public void Read_ValuesOutsideTheRange_AreRejected_InEveryForm()
    {
        string above = "57896044618658097711785492504343953926634992332820282019728792003956564819968"; // 2^255, python3
        string below = "-57896044618658097711785492504343953926634992332820282019728792003956564819969"; // -2^255 - 1, python3

        foreach (string json in new[] { above, below, "\"" + above + "\"", "\"" + below + "\"", "\"0x1" + new string('0', 64) + "\"" })
            Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<int256>(json), json);
    }

    [TestMethod]
    public void Read_NumberTokenLongerThanAnyValidValue_IsRejected_WhenSplitAcrossSegments()
    {
        byte[] tooLong = Encoding.ASCII.GetBytes(new string('9', 81));

        JsonException error = Assert.ThrowsExactly<JsonException>(() => SegmentedUtf8Json.Deserialize<int256>(tooLong, 40));
        Assert.AreEqual("Invalid value for int256.", error.Message);

        Assert.AreEqual(int256.MaxValue, SegmentedUtf8Json.Deserialize<int256>(Encoding.ASCII.GetBytes("57896044618658097711785492504343953926634992332820282019728792003956564819967"), 10));
    }

    [TestMethod]
    [DataRow("--1")]
    [DataRow("1.5")]
    [DataRow("0xzz")]
    public void DictionaryKey_InvalidText_Throws(string key)
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Dictionary<int256, int>>("{\"" + key + "\":1}"));
    }

    [TestMethod]
    public void DictionaryKey_NegativeDecimalAndHexSpellings_OfTheSameValue_ShareOneEntry()
    {
        var map = JsonSerializer.Deserialize<Dictionary<int256, int>>("{\"-5\":1,\"0x" + new string('f', 63) + "b\":2,\"7\":3}")!;

        Assert.HasCount(2, map);
        Assert.AreEqual(2, map[new int256(-5L)]);
        Assert.AreEqual(3, map[new int256(7L)]);
    }
}
