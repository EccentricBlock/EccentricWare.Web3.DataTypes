using System.Buffers;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Builds multi-segment <see cref="ReadOnlySequence{T}"/> inputs so readers expose <c>ValueSequence</c> tokens.</summary>
internal static class SegmentedUtf8Json
{
    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }

    /// <summary>Cuts <paramref name="data"/> into segments of <paramref name="chunk"/> bytes; optionally adds an empty segment before, between and after.</summary>
    public static ReadOnlySequence<byte> Split(byte[] data, int chunk, bool emptySegments = false)
    {
        if (!emptySegments)
        {
            Segment? first = null, last = null;
            for (int i = 0; i < data.Length; i += chunk)
            {
                ReadOnlyMemory<byte> part = data.AsMemory(i, Math.Min(chunk, data.Length - i));
                last = last is null ? (first = new Segment(part)) : last.Append(part);
            }

            return first is null ? ReadOnlySequence<byte>.Empty : new ReadOnlySequence<byte>(first, 0, last!, last!.Memory.Length);
        }

        var head = new Segment(ReadOnlyMemory<byte>.Empty);
        Segment tail = head;
        for (int i = 0; i < data.Length; i += chunk)
        {
            tail = tail.Append(data.AsMemory(i, Math.Min(chunk, data.Length - i)));
            tail = tail.Append(ReadOnlyMemory<byte>.Empty);
        }

        return new ReadOnlySequence<byte>(head, 0, tail, 0);
    }

    /// <summary>Two segments, cut after <paramref name="splitAt"/> bytes.</summary>
    public static ReadOnlySequence<byte> SplitAt(byte[] data, int splitAt)
    {
        var first = new Segment(data.AsMemory(0, splitAt));
        Segment last = first.Append(data.AsMemory(splitAt));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    public static Utf8JsonReader Reader(ReadOnlySequence<byte> sequence) => new(sequence, isFinalBlock: true, state: default);

    public static T Deserialize<T>(byte[] json, int chunk, bool emptySegments = false, JsonSerializerOptions? options = null)
    {
        Utf8JsonReader reader = Reader(Split(json, chunk, emptySegments));
        return JsonSerializer.Deserialize<T>(ref reader, options)!;
    }

    public static object? Deserialize(Type type, byte[] json, int chunk, bool emptySegments = false, JsonSerializerOptions? options = null)
    {
        Utf8JsonReader reader = Reader(Split(json, chunk, emptySegments));
        return JsonSerializer.Deserialize(ref reader, type, options);
    }
}
