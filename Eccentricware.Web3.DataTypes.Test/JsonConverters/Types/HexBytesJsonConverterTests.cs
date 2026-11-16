using EccentricWare.Web3.DataTypes.JsonConverters;
using System.Buffers;
using System.Text;
using System.Text.Json;
using static EccentricWare.Web3.DataTypes.Tests.HexBytesJsonTestKit;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// <see cref="HexBytesJsonConverter"/> value semantics: lenient reading (odd length, case, prefix, whitespace), split
/// tokens decoded across segments, and streamed writes of long values. The limits are in HexBytesJsonConverterLimitTests.
/// </summary>
[TestClass]
public sealed class HexBytesJsonConverterTests
{
    // (JSON string content, the bytes it decodes to as plain hex)
    private static readonly (string Text, string Hex)[] Spellings =
    [
        ("0xdeadbeef", "deadbeef"),
        ("0xdeadbee", "0deadbee"), // odd length is left-padded
        ("DEADBEEF", "deadbeef"),
        ("0Xabc", "0abc"),
        ("0x", ""),
        ("", ""),
        ("  0xAbCd12   ", "abcd12"),
        ("0x1", "01"),
        ("f", "0f"),
        ("   ", ""),
        ("0xabcdef0123456789abcdef", "abcdef0123456789abcdef"),
    ];

    // ----------------- reading -----------------

    [TestMethod]
    public void Read_AcceptsOddLength_CaseAndPrefixVariants_AndEmpty()
    {
        foreach ((string text, string hex) in Spellings)
            Assert.AreEqual(H(hex), JsonSerializer.Deserialize<HexBytes>("\"" + text + "\""), $"'{text}'");
    }

    [TestMethod]
    [DataRow("0x0x12")]
    [DataRow("nope")]
    [DataRow("0x12 34")]
    [DataRow("0xzz")]
    [DataRow("0x12g")]
    public void Read_InvalidText_ThrowsTheInvalidValueError(string text)
    {
        JsonException error = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<HexBytes>("\"" + text + "\""));

        Assert.AreEqual("Invalid value for HexBytes.", error.Message);
    }

    [TestMethod]
    public void Read_SplitValues_DecodeAtEverySplitPoint_ForEveryTextShape()
    {
        var converter = new HexBytesJsonConverter();

        foreach ((string text, string hex) in Spellings)
        {
            byte[] json = JsonTokenBodies.Value(text);
            for (int split = 1; split < json.Length; split++)
            {
                if (json.Length - split < 2 || split < 2)
                    continue; // a token must span both segments to be a ValueSequence

                Assert.AreEqual(H(hex), ReadSplit(converter, json, split), $"'{text}' split={split}");
            }
        }
    }

    [TestMethod]
    public void Read_SplitDictionaryKeys_DecodeAtEverySplitPoint()
    {
        byte[] json = Encoding.ASCII.GetBytes("{\"0xdeadbeef\":7}");

        for (int split = 1; split < json.Length; split++)
            Assert.AreEqual(7, SegmentedUtf8Json.Deserialize<Dictionary<HexBytes, int>>(json, split)[H("deadbeef")], $"split={split}");
    }

    [TestMethod]
    public void Read_EscapedAndSplit_LargeValue_Parses()
    {
        string text = PatternText(3000);
        byte[] escaped = JsonTokenBodies.Value(JsonTokenBodies.EscapeFirst(text));

        Assert.AreEqual(Pattern(3000), JsonSerializer.Deserialize<HexBytes>(escaped));
        Assert.AreEqual(Pattern(3000), ReadSplit(new HexBytesJsonConverter(), escaped, 1000));
    }

    [TestMethod]
    public void Read_SplitValue_InvalidDigitInAnySegment_ThrowsTheInvalidValueError()
    {
        byte[] good = JsonTokenBodies.Value("0x" + new string('a', 40));

        for (int bad = 3; bad < good.Length - 1; bad++)
        {
            byte[] json = (byte[])good.Clone();
            json[bad] = (byte)'g';

            JsonException error = Assert.ThrowsExactly<JsonException>(() => ReadSplit(new HexBytesJsonConverter(), json, 10), $"bad digit at {bad}");
            Assert.AreEqual("Invalid value for HexBytes.", error.Message);
        }
    }

    [TestMethod]
    public void SequenceParser_MatchesTheSpanParser_ForEveryChunking_ValidAndInvalid()
    {
        // The sequence decoder carries digit pairs across segment boundaries and trims across them; compare it with the
        // span decoder on valid and invalid text cut into 1-, 2-, 3- and 7-byte segments, with and without empty segments.
        var random = new Random(20260511);
        const string Alphabet = "0123456789abcdefABCDEF xXzg\t";
        const string Digits = "0123456789abcdefABCDEF";

        for (int round = 0; round < 3000; round++)
        {
            var text = new StringBuilder();
            if (random.Next(3) == 0)
                text.Append(random.Next(2) == 0 ? "0x" : "0X");
            int digits = random.Next(0, 40);
            for (int i = 0; i < digits; i++)
                text.Append(random.Next(25) == 0 ? Alphabet[random.Next(Alphabet.Length)] : Digits[random.Next(Digits.Length)]);
            if (random.Next(4) == 0)
                text.Insert(0, new string(' ', random.Next(1, 4)));
            if (random.Next(4) == 0)
                text.Append("\r\n");

            byte[] bytes = Encoding.ASCII.GetBytes(text.ToString());
            bool spanOk = HexBytes.TryParseUnquoted(bytes, out HexBytes expected);

            foreach (int chunk in new[] { 1, 2, 3, 7, 1000 })
            {
                foreach (bool empty in new[] { false, true })
                {
                    bool sequenceOk = HexBytes.TryParseUnquoted(SegmentedUtf8Json.Split(bytes, chunk, empty), out HexBytes actual);
                    Assert.AreEqual(spanOk, sequenceOk, $"'{text}' chunk={chunk} empty={empty}");
                    if (spanOk)
                        Assert.AreEqual(expected, actual, $"'{text}' chunk={chunk} empty={empty}");
                }
            }
        }
    }

    // ----------------- writing -----------------

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(4)]
    [DataRow(68)]
    [DataRow(254)]
    [DataRow(255)] // 512 bytes of text: the last single-piece size
    [DataRow(256)] // first streamed size; also exactly one chunk
    [DataRow(257)]
    [DataRow(511)]
    [DataRow(512)] // exactly two chunks
    [DataRow(513)]
    [DataRow(5000)]
    [DataRow(100_000)]
    public void Write_Value_ProducesTheCanonicalString_AcrossEveryBufferBoundary(int length)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            new HexBytesJsonConverter().Write(writer, Pattern(length), new JsonSerializerOptions());

        Assert.AreEqual("\"" + PatternText(length) + "\"", Encoding.UTF8.GetString(buffer.WrittenSpan));
        Assert.AreEqual(Pattern(length), JsonSerializer.Deserialize<HexBytes>(buffer.WrittenSpan));
    }

    [TestMethod]
    public void Write_LongValue_IsStreamedInSmallSegments_NotInOneBufferTheSizeOfTheValue()
    {
        // 2,000,000 bytes -> 4,000,002 characters. Formatting into one buffer would ask the sink for megabytes at once.
        const int Length = 2_000_000;
        var sink = new RecordingBufferWriter();

        using (var writer = new Utf8JsonWriter(sink))
            new HexBytesJsonConverter().Write(writer, Pattern(Length), new JsonSerializerOptions());

        Assert.AreEqual((2L * Length) + 4, sink.Written); // quotes + "0x" + digits
        Assert.IsLessThan(64 * 1024, sink.LargestSizeHint, $"largest size hint {sink.LargestSizeHint}");
    }

    private sealed class Model
    {
        public HexBytes First { get; set; }
        public HexBytes Second { get; set; }
        public List<HexBytes> Items { get; set; } = [];
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Write_LongValues_InsideArraysAndObjects_KeepTheDocumentValid(bool indented)
    {
        var model = new Model { First = Pattern(2000), Second = Pattern(300), Items = [Pattern(2000), HexBytes.Empty, Pattern(256)] };

        string json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = indented });
        Model back = JsonSerializer.Deserialize<Model>(json)!;

        Assert.AreEqual(model.First, back.First);
        Assert.AreEqual(model.Second, back.Second);
        CollectionAssert.AreEqual(model.Items, back.Items);
        StringAssert.Contains(json, "\"" + PatternText(2000) + "\"");
    }
}
