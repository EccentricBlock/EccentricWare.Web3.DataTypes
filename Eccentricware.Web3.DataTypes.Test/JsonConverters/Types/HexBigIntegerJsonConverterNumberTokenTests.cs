using EccentricWare.Web3.DataTypes.JsonConverters;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// <see cref="HexBigIntegerJsonConverter"/> number-token limits and the hex-first string versus decimal number rule,
/// including numbers split across buffer segments (the 4,934-byte copy limit) and wide dictionary keys.
/// </summary>
[TestClass]
public sealed class HexBigIntegerJsonConverterNumberTokenTests
{
    // The widest value (16,384 bits = 4,096 hex digits) as decimal text: 4,933 digits, 4,934 with a minus sign.
    private static readonly BigInteger WidestMagnitude = (BigInteger.One << (4 * HexBigInteger.DefaultMaxHexDigits)) - 1;

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    [TestMethod]
    public void WidestNegativeNumber_IsExactlyTheMaximumTokenLength_AndReadsWhenContiguousOrSplit()
    {
        string text = (-WidestMagnitude).ToString(); // computed with BigInteger, independent of the converter
        Assert.HasCount(4934, text);

        Assert.AreEqual(new HexBigInteger(-WidestMagnitude), JsonSerializer.Deserialize<HexBigInteger>(text));
        foreach (int chunk in new[] { 1000, 4933, 1 })
            Assert.AreEqual(new HexBigInteger(-WidestMagnitude), SegmentedUtf8Json.Deserialize<HexBigInteger>(Ascii(text), chunk), $"chunk={chunk}");
    }

    [TestMethod]
    public void OneBitWiderThanTheMaximum_IsRejected_WhenContiguousOrSplit()
    {
        string text = (WidestMagnitude + 1).ToString(); // 2^16384
        string negative = (-(WidestMagnitude + 1)).ToString();

        foreach (string number in new[] { text, negative })
        {
            Assert.AreEqual("Invalid value for HexBigInteger.", Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<HexBigInteger>(number)).Message);
            Assert.AreEqual("Invalid value for HexBigInteger.", Assert.ThrowsExactly<JsonException>(() => SegmentedUtf8Json.Deserialize<HexBigInteger>(Ascii(number), 1000)).Message);
        }
    }

    [TestMethod]
    public void NumberTokenLongerThanTheCopyLimit_IsRejectedBeforeAnyCopy_WhenSplit()
    {
        // 6,000 digits split into 1,000-byte segments: longer than 4,934 bytes, so it is refused without parsing.
        byte[] tooLong = Ascii(new string('9', 6000));

        JsonException error = Assert.ThrowsExactly<JsonException>(() => SegmentedUtf8Json.Deserialize<HexBigInteger>(tooLong, 1000));

        Assert.AreEqual("Invalid value for HexBigInteger.", error.Message);
    }

    [TestMethod]
    public void NumberTokenLongerThanTheCopyLimit_IsRejected_WhenContiguous()
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<HexBigInteger>(new string('9', 6000)));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<HexBigInteger>("-" + new string('9', 4934)));
    }

    [TestMethod]
    public void NumbersBeyond64Bits_ReadExactly()
    {
        Assert.AreEqual(new HexBigInteger(BigInteger.Pow(2, 64)), JsonSerializer.Deserialize<HexBigInteger>("18446744073709551616"));
        Assert.AreEqual(new HexBigInteger(-BigInteger.Pow(2, 63) - 1), JsonSerializer.Deserialize<HexBigInteger>("-9223372036854775809"));
        Assert.AreEqual(new HexBigInteger(ulong.MaxValue), JsonSerializer.Deserialize<HexBigInteger>("18446744073709551615"));
        Assert.AreEqual(new HexBigInteger(long.MinValue), JsonSerializer.Deserialize<HexBigInteger>("-9223372036854775808"));
    }

    [TestMethod]
    [DataRow("1.0")]
    [DataRow("1e0")]
    [DataRow("1E3")]
    [DataRow("-0.5")]
    [DataRow("18446744073709551616.0")] // wider than 64 bits and not an integer
    public void NonIntegerNumberTokens_AreRejected(string json)
    {
        Assert.AreEqual("Invalid value for HexBigInteger.", Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<HexBigInteger>(json)).Message);
    }

    [TestMethod]
    public void Strings_AreHexFirst_WhileNumbersAreDecimal()
    {
        // A JSON-RPC footgun worth pinning: the string "26" is 0x26 (38), the number 26 is twenty-six.
        Assert.AreEqual(new HexBigInteger(38), JsonSerializer.Deserialize<HexBigInteger>("\"26\""));
        Assert.AreEqual(new HexBigInteger(26), JsonSerializer.Deserialize<HexBigInteger>("26"));
        Assert.AreEqual(1, JsonSerializer.Deserialize<Dictionary<HexBigInteger, int>>("{\"26\":1}")![new HexBigInteger(38)]);
    }

    [TestMethod]
    public void WidestValue_AsADictionaryKey_UsesThePooledWritePath_AndReadsBack()
    {
        var wide = new HexBigInteger(WidestMagnitude);
        var map = new Dictionary<HexBigInteger, int> { [wide] = 1, [new HexBigInteger(-WidestMagnitude)] = 2 };

        string json = JsonSerializer.Serialize(map);

        Assert.AreEqual("{\"0x" + new string('f', 4096) + "\":1,\"-0x" + new string('f', 4096) + "\":2}", json);
        var back = JsonSerializer.Deserialize<Dictionary<HexBigInteger, int>>(json)!;
        Assert.AreEqual(1, back[wide]);
        Assert.AreEqual(2, back[new HexBigInteger(-WidestMagnitude)]);
    }

    [TestMethod]
    public void WriteIsUnbounded_ButAValueWiderThanTheReadLimit_CannotBeReadBack()
    {
        var tooWide = new HexBigInteger(BigInteger.One << (4 * HexBigInteger.DefaultMaxHexDigits)); // 4,097 hex digits

        string json = JsonSerializer.Serialize(tooWide);

        Assert.AreEqual("\"0x1" + new string('0', 4096) + "\"", json);
        Assert.AreEqual("Invalid value for HexBigInteger.", Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<HexBigInteger>(json)).Message);
    }
}
