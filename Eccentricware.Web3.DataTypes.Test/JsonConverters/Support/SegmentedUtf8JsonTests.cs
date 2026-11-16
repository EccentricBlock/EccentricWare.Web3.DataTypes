using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Guards the test helper itself: a segmented read must reach the converter as a <c>ValueSequence</c> (for values and
/// property names), otherwise every "split token" test would silently run the contiguous path.
/// </summary>
[TestClass]
public sealed class SegmentedUtf8JsonTests
{
    private readonly struct Marker;

    private sealed class SpyConverter : JsonConverter<Marker>
    {
        public bool? ValueHadSequence { get; private set; }
        public bool? KeyHadSequence { get; private set; }

        public override Marker Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            ValueHadSequence = reader.HasValueSequence;
            return default;
        }

        public override Marker ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            KeyHadSequence = reader.HasValueSequence;
            return default;
        }

        public override void Write(Utf8JsonWriter writer, Marker value, JsonSerializerOptions options) => throw new NotSupportedException();
    }

    private static byte[] Bytes(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++)
            data[i] = (byte)(i + 1);
        return data;
    }

    private static List<int> SegmentLengths(ReadOnlySequence<byte> sequence)
    {
        var lengths = new List<int>();
        foreach (ReadOnlyMemory<byte> segment in sequence)
            lengths.Add(segment.Length);
        return lengths;
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(7)]
    [DataRow(10)]
    public void Split_CutsIntoChunks_AndPreservesTheBytes(int chunk)
    {
        byte[] data = Bytes(10);

        ReadOnlySequence<byte> sequence = SegmentedUtf8Json.Split(data, chunk);

        CollectionAssert.AreEqual(data, sequence.ToArray());
        List<int> lengths = SegmentLengths(sequence);
        Assert.HasCount((10 + chunk - 1) / chunk, lengths);
        Assert.IsTrue(lengths.All(l => l > 0 && l <= chunk));
    }

    [TestMethod]
    public void Split_WithEmptySegments_InsertsEmptySegmentsAroundEveryChunk()
    {
        byte[] data = Bytes(7);

        ReadOnlySequence<byte> sequence = SegmentedUtf8Json.Split(data, 3, emptySegments: true);

        CollectionAssert.AreEqual(data, sequence.ToArray());
        // empty, 3, empty, 3, empty, 1, empty (enumeration may skip the empty segments)
        Assert.IsGreaterThanOrEqualTo(3, SegmentLengths(sequence).Count(l => l > 0));
        Assert.AreEqual(7L, sequence.Length);
    }

    [TestMethod]
    public void Split_OfNothing_IsTheEmptySequence()
    {
        Assert.IsTrue(SegmentedUtf8Json.Split([], 3).IsEmpty);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(5)]
    [DataRow(9)]
    public void SplitAt_MakesTwoSegments(int splitAt)
    {
        byte[] data = Bytes(10);

        ReadOnlySequence<byte> sequence = SegmentedUtf8Json.SplitAt(data, splitAt);

        Assert.IsFalse(sequence.IsSingleSegment);
        CollectionAssert.AreEqual(new[] { splitAt, 10 - splitAt }, SegmentLengths(sequence));
        CollectionAssert.AreEqual(data, sequence.ToArray());
    }

    [TestMethod]
    [DataRow(2, false)]
    [DataRow(3, false)]
    [DataRow(1, true)]
    [DataRow(4, true)]
    public void Deserialize_PresentsSplitValuesAndKeys_AsValueSequences(int chunk, bool emptySegments)
    {
        var spy = new SpyConverter();
        var options = new JsonSerializerOptions { Converters = { spy } };

        SegmentedUtf8Json.Deserialize<Marker>(Encoding.ASCII.GetBytes("\"abcdefgh\""), chunk, emptySegments, options);
        SegmentedUtf8Json.Deserialize<Dictionary<Marker, int>>(Encoding.ASCII.GetBytes("{\"abcdefgh\":1}"), chunk, emptySegments, options);

        Assert.IsTrue(spy.ValueHadSequence);
        Assert.IsTrue(spy.KeyHadSequence);
    }

    [TestMethod]
    public void Deserialize_WithOneChunkLargerThanTheInput_IsContiguous()
    {
        var spy = new SpyConverter();
        var options = new JsonSerializerOptions { Converters = { spy } };

        SegmentedUtf8Json.Deserialize<Marker>(Encoding.ASCII.GetBytes("\"abcdefgh\""), 100, options: options);

        Assert.IsFalse(spy.ValueHadSequence);
    }

    [TestMethod]
    public void DeserializeByType_MatchesTheGenericOverload()
    {
        byte[] json = Encoding.ASCII.GetBytes("[1,2,3]");

        object? viaType = SegmentedUtf8Json.Deserialize(typeof(int[]), json, 2);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, (int[])viaType!);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, SegmentedUtf8Json.Deserialize<int[]>(json, 2));
    }
}
