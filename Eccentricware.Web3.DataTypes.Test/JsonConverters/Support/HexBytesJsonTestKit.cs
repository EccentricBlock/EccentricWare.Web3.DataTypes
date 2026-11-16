using EccentricWare.Web3.DataTypes.JsonConverters;
using System.Buffers;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Shared inputs and direct-converter helpers for the HexBytes JSON test classes.</summary>
internal static class HexBytesJsonTestKit
{
    /// <summary>Deterministic non-trivial bytes (offset * 31 + 7, wrapping).</summary>
    public static byte[] PatternBytes(int length)
    {
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
            bytes[i] = (byte)((i * 31) + 7);
        return bytes;
    }

    public static HexBytes Pattern(int length) => new(PatternBytes(length));

    /// <summary>Lowercase "0x" text of the pattern, built with the BCL.</summary>
    public static string PatternText(int length) => "0x" + Convert.ToHexString(PatternBytes(length)).ToLowerInvariant();

    /// <summary>A value from plain hex digits (no prefix), built with the BCL.</summary>
    public static HexBytes H(string hex) => new(Convert.FromHexString(hex));

    public static string N0(int value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture); // the library messages are culture-invariant

    public static string KeyLimitMessage => $"A HexBytes dictionary key exceeds the {N0(HexBytesJsonConverter.MaxDictionaryKeyBytes)}-byte limit.";

    public static string TokenLimitMessage(int limit) => $"The JSON string token for HexBytes exceeds the {N0(limit)}-byte limit.";

    public static HexBytes ReadDirect(HexBytesJsonConverter converter, byte[] json)
    {
        var reader = new Utf8JsonReader(json);
        Assert.IsTrue(reader.Read());
        return converter.Read(ref reader, typeof(HexBytes), new JsonSerializerOptions());
    }

    public static HexBytes ReadSplit(HexBytesJsonConverter converter, byte[] json, int split)
    {
        Utf8JsonReader reader = SegmentedUtf8Json.Reader(SegmentedUtf8Json.SplitAt(json, split));
        Assert.IsTrue(reader.Read());
        Assert.IsTrue(reader.HasValueSequence, "test setup: expected a multi-segment token");
        return converter.Read(ref reader, typeof(HexBytes), new JsonSerializerOptions());
    }

    /// <summary>Reads the first property name of <paramref name="json"/> (an object) as a key through the converter.</summary>
    public static HexBytes ReadKeyDirect(HexBytesJsonConverter converter, byte[] json)
    {
        var reader = new Utf8JsonReader(json);
        Assert.IsTrue(reader.Read());
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(JsonTokenType.PropertyName, reader.TokenType);
        return converter.ReadAsPropertyName(ref reader, typeof(HexBytes), new JsonSerializerOptions());
    }

    /// <summary>An <see cref="IBufferWriter{T}"/> that discards output and records the largest buffer it was asked for.</summary>
    public sealed class RecordingBufferWriter : IBufferWriter<byte>
    {
        private byte[] _buffer = new byte[16 * 1024];

        public long Written { get; private set; }

        public int LargestSizeHint { get; private set; }

        public void Advance(int count) => Written += count;

        public Memory<byte> GetMemory(int sizeHint = 0) => Ensure(sizeHint);

        public Span<byte> GetSpan(int sizeHint = 0) => Ensure(sizeHint);

        private byte[] Ensure(int sizeHint)
        {
            LargestSizeHint = Math.Max(LargestSizeHint, sizeHint);
            if (sizeHint > _buffer.Length)
                _buffer = new byte[sizeHint];
            return _buffer;
        }
    }
}
