using EccentricWare.Web3.DataTypes.JsonConverters;
using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using static EccentricWare.Web3.DataTypes.Tests.HexBigIntegerTestData;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="HexBigIntegerJsonConverter"/> behaviour through <see cref="JsonSerializer"/> (moved unchanged from the former combined test class).</summary>
[TestClass]
public sealed class HexBigIntegerJsonTests
{
    private static HexBigInteger Read(string json) => JsonSerializer.Deserialize<HexBigInteger>(json);

    [TestMethod]
    public void Json_Write_IsMinimalLowerHexString()
    {
        Assert.AreEqual("\"0x0\"", JsonSerializer.Serialize(HexBigInteger.Zero));
        Assert.AreEqual("\"0xff\"", JsonSerializer.Serialize(new HexBigInteger(255)));
        Assert.AreEqual("\"-0x1\"", JsonSerializer.Serialize(HexBigInteger.MinusOne));
        Assert.AreEqual("\"0x" + new string('f', 64) + "\"", JsonSerializer.Serialize(new HexBigInteger(BigInteger.Pow(2, 256) - 1)));
    }

    [TestMethod]
    public void Json_Write_ValueLargerThanStackBuffer_IsNotTruncated()
    {
        BigInteger value = BigInteger.Pow(2, 16384) - 1; // 4096 digits: well past the converter's stack buffer
        string json = JsonSerializer.Serialize(new HexBigInteger(value));
        Assert.AreEqual('"' + ReferenceHex(value) + '"', json);
        Assert.AreEqual(value, Read(json).Value);

        json = JsonSerializer.Serialize(new HexBigInteger(-value));
        Assert.AreEqual('"' + ReferenceHex(-value) + '"', json);
        Assert.AreEqual(-value, Read(json).Value);
    }

    [TestMethod]
    public void Json_RoundTrip_InterestingValues()
    {
        foreach (BigInteger value in InterestingSigned())
        {
            string json = JsonSerializer.Serialize(new HexBigInteger(value));
            Assert.AreEqual(value, Read(json).Value, json);
        }
    }

    [TestMethod]
    public void Json_Read_StringForms()
    {
        Assert.AreEqual(26, Read("\"0x1a\"").Value);
        Assert.AreEqual(26, Read("\"0X1A\"").Value);
        Assert.AreEqual(26, Read("\"1a\"").Value);
        Assert.AreEqual(-26, Read("\"-0x1a\"").Value);
        Assert.AreEqual(BigInteger.Zero, Read("\"0x\"").Value);
        Assert.AreEqual(255, Read("\"0xff\"").Value);
        Assert.AreEqual(BigInteger.Pow(2, 255), Read("\"0x8" + new string('0', 63) + "\"").Value);
    }

    [TestMethod]
    [DataRow("\"\"")]
    [DataRow("\"-\"")]
    [DataRow("\"0xzz\"")]
    [DataRow("\"null\"")] // the text "null" is not a quantity; only the JSON null token is special
    [DataRow("true")]
    [DataRow("[]")]
    [DataRow("{}")]
    public void Json_Read_RejectsInvalidTokens(string json)
    {
        Assert.ThrowsExactly<JsonException>(() => Read(json));
    }

    [TestMethod]
    public void Json_Read_EscapedString_IsUnescapedBeforeParsing()
    {
        Assert.AreEqual(1, Read("\"\\u0030x1\"").Value);
        Assert.AreEqual(-255, Read("\"-0x\\u0066f\"").Value);
    }

    [TestMethod]
    public void Json_Read_ValueSplitAcrossSegments()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("\"0xdeadbeefcafe\"");
        var first = new TestSegment(bytes.AsMemory(0, 7));
        TestSegment last = first.Append(bytes.AsMemory(7));
        var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);

        var reader = new Utf8JsonReader(sequence, isFinalBlock: true, state: default);
        Assert.IsTrue(reader.Read());
        Assert.IsTrue(reader.HasValueSequence);
        HexBigInteger value = new HexBigIntegerJsonConverter().Read(ref reader, typeof(HexBigInteger), new JsonSerializerOptions());
        Assert.AreEqual(new BigInteger(0xdeadbeefcafeL), value.Value);
    }

    private sealed class TestSegment : ReadOnlySequenceSegment<byte>
    {
        public TestSegment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public TestSegment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new TestSegment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }

    [TestMethod]
    public void Json_Read_NumberTokens_AreDecimal()
    {
        Assert.AreEqual(0, Read("0").Value);
        Assert.AreEqual(26, Read("26").Value);
        Assert.AreEqual(-26, Read("-26").Value);
        Assert.AreEqual(new BigInteger(long.MaxValue), Read(long.MaxValue.ToString(CultureInfo.InvariantCulture)).Value);
        Assert.AreEqual(new BigInteger(long.MinValue), Read(long.MinValue.ToString(CultureInfo.InvariantCulture)).Value);
        Assert.AreEqual(new BigInteger(ulong.MaxValue), Read(ulong.MaxValue.ToString(CultureInfo.InvariantCulture)).Value);

        BigInteger wide = BigInteger.Pow(10, 77) + 12345;
        Assert.AreEqual(wide, Read(wide.ToString(CultureInfo.InvariantCulture)).Value);
        Assert.AreEqual(-wide, Read((-wide).ToString(CultureInfo.InvariantCulture)).Value);
    }

    [TestMethod]
    [DataRow("1.5")]
    [DataRow("1e3")]
    [DataRow("1E3")]
    [DataRow("-0.0")]
    public void Json_Read_NonIntegerNumberTokens_AreRejected(string json)
    {
        Assert.ThrowsExactly<JsonException>(() => Read(json));
    }

    [TestMethod]
    public void Json_Read_OversizedNumberToken_IsRejected()
    {
        Assert.ThrowsExactly<JsonException>(() => Read(new string('9', 6000)));
    }

    [TestMethod]
    public void Json_Read_OversizedHexString_IsRejected()
    {
        Assert.ThrowsExactly<JsonException>(() => Read("\"0x" + new string('f', HexBigInteger.DefaultMaxHexDigits + 1) + "\""));
        Assert.AreEqual(BigInteger.Pow(2, 4 * HexBigInteger.DefaultMaxHexDigits) - 1, Read("\"0x" + new string('f', HexBigInteger.DefaultMaxHexDigits) + "\"").Value);
    }

    [TestMethod]
    public void Json_Read_NumberToken_WidthBoundary_MatchesHexDigitLimit()
    {
        BigInteger widest = BigInteger.Pow(2, 4 * HexBigInteger.DefaultMaxHexDigits) - 1; // 4,933 decimal digits
        Assert.AreEqual(widest, Read(widest.ToString(CultureInfo.InvariantCulture)).Value);
        Assert.AreEqual(-widest, Read((-widest).ToString(CultureInfo.InvariantCulture)).Value);
        Assert.ThrowsExactly<JsonException>(() => Read((widest + 1).ToString(CultureInfo.InvariantCulture)));
        Assert.ThrowsExactly<JsonException>(() => Read((-widest - 1).ToString(CultureInfo.InvariantCulture)));
    }

    [TestMethod]
    public void Json_Read_WideNumberToken_SplitAcrossSegments()
    {
        BigInteger wide = BigInteger.Pow(10, 77) + 12345;
        string json = wide.ToString(CultureInfo.InvariantCulture);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        var first = new TestSegment(bytes.AsMemory(0, 30));
        TestSegment last = first.Append(bytes.AsMemory(30));
        var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);

        var reader = new Utf8JsonReader(sequence, isFinalBlock: true, state: default);
        Assert.IsTrue(reader.Read());
        Assert.IsTrue(reader.HasValueSequence);
        Assert.AreEqual(wide, new HexBigIntegerJsonConverter().Read(ref reader, typeof(HexBigInteger), new JsonSerializerOptions()).Value);
    }

    [TestMethod]
    public void Json_Read_EscapedQuotesAroundTheValue_AreRejected()
    {
        // The unescaped text is "0x1" including the quote characters; a quantity never contains quotes.
        Assert.ThrowsExactly<JsonException>(() => Read("\"\\\"0x1\\\"\""));
        Assert.ThrowsExactly<JsonException>(() => Read("\"\\\"0x1\""));
        Assert.AreEqual(1, Read("\"0x1\"").Value);
    }

    [TestMethod]
    public void Json_Read_OverlongEscapedToken_IsRejectedBeforeAnyLargeBuffer()
    {
        // Valid digits behind a huge run of escaped whitespace: far past anything a 4096-digit value needs.
        string padded = "\"" + string.Concat(Enumerable.Repeat("\\u0020", 100_000)) + "0x1\"";
        Assert.ThrowsExactly<JsonException>(() => Read(padded));

        // At the digit limit, fully escaped, the value still reads.
        string maxEscaped = "\"0x" + string.Concat(Enumerable.Repeat("\\u0066", HexBigInteger.DefaultMaxHexDigits)) + "\"";
        Assert.AreEqual(BigInteger.Pow(2, 4 * HexBigInteger.DefaultMaxHexDigits) - 1, Read(maxEscaped).Value);
    }

    [TestMethod]
    public void Json_PropertyName_EscapedAndSegmented()
    {
        var map = JsonSerializer.Deserialize<Dictionary<HexBigInteger, int>>("{\"\\u0030x1a\":1,\"-0xff\":2}")!;
        Assert.AreEqual(1, map[new HexBigInteger(26)]);
        Assert.AreEqual(2, map[new HexBigInteger(-255)]);
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Dictionary<HexBigInteger, int>>("{\"zz\":1}"));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Dictionary<HexBigInteger, int>>("{\"null\":1}"));
    }

    [TestMethod]
    public void Json_Read_NullToken_Throws_AndNegativeZeroNumber_IsZero()
    {
        Assert.ThrowsExactly<JsonException>(() => Read("null"));
        Assert.AreEqual(HexBigInteger.Zero, Read("-0"));
        Assert.IsFalse(Read("-0").IsNegative);
    }

    [TestMethod]
    public void Json_Write_RoundTrips_AcrossTheStackBufferBoundary()
    {
        // The converter formats on a 256-byte stack buffer and falls back to a pooled one beyond it (about 1,012 bits).
        for (int bits = 990; bits <= 1030; bits++)
        {
            foreach (BigInteger value in new[] { BigInteger.Pow(2, bits) - 1, BigInteger.Pow(2, bits), -BigInteger.Pow(2, bits), -BigInteger.Pow(2, bits) - 1 })
            {
                string json = JsonSerializer.Serialize(new HexBigInteger(value));
                Assert.AreEqual('"' + ReferenceHex(value) + '"', json, $"bits={bits}");
                Assert.AreEqual(value, Read(json).Value, $"bits={bits}");

                string key = JsonSerializer.Serialize(new Dictionary<HexBigInteger, int> { [new HexBigInteger(value)] = 1 });
                Assert.AreEqual("{\"" + ReferenceHex(value) + "\":1}", key, $"bits={bits}");
            }
        }
    }

    [TestMethod]
    public void Json_NullableProperty_MapsNullToNull_AndValuesThrough()
    {
        var model = JsonSerializer.Deserialize<Model>("{\"BlockNumber\":null,\"GasPrice\":\"0x3b9aca00\",\"Delta\":\"-0x5\"}")!;
        Assert.IsNull(model.BlockNumber);
        Assert.AreEqual(1_000_000_000, model.GasPrice.Value);
        Assert.AreEqual(-5, model.Delta.Value);

        string json = JsonSerializer.Serialize(model);
        Assert.AreEqual("{\"BlockNumber\":null,\"GasPrice\":\"0x3b9aca00\",\"Delta\":\"-0x5\"}", json);
    }

    private sealed class Model
    {
        public HexBigInteger? BlockNumber { get; set; }
        public HexBigInteger GasPrice { get; set; }
        public HexBigInteger Delta { get; set; }
    }

    [TestMethod]
    public void Json_DictionaryKey_RoundTrips()
    {
        var map = new Dictionary<HexBigInteger, int>
        {
            [new HexBigInteger(255)] = 1,
            [new HexBigInteger(-255)] = 2,
            [HexBigInteger.Zero] = 3,
            [new HexBigInteger(BigInteger.Pow(2, 200))] = 4,
        };

        string json = JsonSerializer.Serialize(map);
        Assert.IsTrue(json.Contains("\"0xff\":1"), json);
        Assert.IsTrue(json.Contains("\"-0xff\":2"), json);

        Dictionary<HexBigInteger, int> back = JsonSerializer.Deserialize<Dictionary<HexBigInteger, int>>(json)!;
        Assert.AreEqual(map.Count, back.Count);
        foreach ((HexBigInteger key, int value) in map)
            Assert.AreEqual(value, back[key]);
    }
}
