using System.Buffers;
using System.Text.Json;
using EccentricWare.Web3.DataTypes.JsonConverters;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Values of 2^30 bytes (1 GiB) or more: the hex length no longer fits an <see cref="int"/>. <c>HexLength</c> is
/// <c>Length * 2</c> and the sizes derived from it (<c>+ 2</c> for the prefix) used to wrap negative, so formatting returned
/// false for no reason and the JSON converter failed with "Failed to format HexBytes." instead of streaming. These tests need a
/// 1 GiB array, so they run serially and are inconclusive when the machine has less than 3 GiB of free memory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class HexBytesHugeValueTests
{
    private const int GiB = 1 << 30;

    // The two arrays are shared by every test in the class (they run serially) and released afterwards, so the class costs at most
    // 2 GiB of address space once, not one array per test. Untouched pages of a fresh array are not backed by physical memory.
    private static HexBytes? s_gib;
    private static HexBytes? s_gibMinusOne;

    private static HexBytes Huge(int length)
    {
        // Physical memory in use right now, system-wide, against what the GC may use: skip rather than push a busy machine into swap.
        GCMemoryInfo info = GC.GetGCMemoryInfo();
        if (info.TotalAvailableMemoryBytes - info.MemoryLoadBytes < 3L * GiB)
            Assert.Inconclusive("Needs about 3 GiB of free memory for a 1 GiB array.");

        return length == GiB
            ? s_gib ??= HexBytes.FromArrayUnsafe(new byte[GiB])
            : s_gibMinusOne ??= HexBytes.FromArrayUnsafe(new byte[GiB - 1]);
    }

    [ClassCleanup]
    public static void ReleaseTheArrays()
    {
        s_gib = null;
        s_gibMinusOne = null;
        GC.Collect();
    }

    private sealed class SinkAbort : Exception;

    /// <summary>Accepts output through a small fixed buffer and aborts once <paramref name="limit"/> bytes have been written.</summary>
    private sealed class AbortingSink(long limit) : IBufferWriter<byte>
    {
        private readonly byte[] _buffer = new byte[16 * 1024];
        public long Written { get; private set; }

        public void Advance(int count)
        {
            Written += count;
            if (Written > limit)
                throw new SinkAbort();
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => _buffer;
        public Span<byte> GetSpan(int sizeHint = 0) => _buffer;
    }

    [TestMethod]
    public void HexLength_OfA1GiBValue_ThrowsOverflowException_InsteadOfWrappingNegative()
    {
        HexBytes value = Huge(GiB);

        Assert.AreEqual(GiB, value.Length);
        Assert.ThrowsExactly<OverflowException>(() => _ = value.HexLength);
    }

    [TestMethod]
    public void HexLength_JustBelowTheLimit_IsExact()
    {
        HexBytes value = Huge(GiB - 1);

        Assert.AreEqual(int.MaxValue - 1, value.HexLength); // 2 * (2^30 - 1)
    }

    [TestMethod]
    [DataRow(GiB)]
    [DataRow(GiB - 1)]
    public void TryFormat_ReturnsFalse_NotAnExceptionOrAWrongAnswer_ForEveryFormat(int length)
    {
        HexBytes value = Huge(length);

        foreach (string format in new[] { "", "x", "0x", "X", "0X" })
        {
            Assert.IsFalse(value.TryFormat(new char[16], out int chars, format), $"chars '{format}'");
            Assert.AreEqual(0, chars);
            Assert.IsFalse(value.TryFormat(new byte[16], out int bytes, format), $"utf8 '{format}'");
            Assert.AreEqual(0, bytes);
        }
    }

    [TestMethod]
    [DataRow(GiB)]
    [DataRow(GiB - 1)]
    public void ToString_FailsWithTheRuntimesOutOfMemory_NotAWrappedLength(int length)
    {
        // No .NET string can hold 2^31 characters; the runtime's own error is the honest answer (the length used to wrap to an
        // ArgumentOutOfRangeException from string.Create).
        HexBytes value = Huge(length);

        Assert.ThrowsExactly<OutOfMemoryException>(() => value.ToString());
        Assert.ThrowsExactly<OutOfMemoryException>(() => value.ToString("x", null));
    }

    [TestMethod]
    [DataRow(GiB)]
    [DataRow(GiB - 1)]
    public void JsonConverter_StreamsTheValue_InsteadOfFailingToFormat(int length)
    {
        HexBytes value = Huge(length);
        var sink = new AbortingSink(limit: 8 * 1024 * 1024);

        // Reaching the sink's abort proves the streaming path started writing: the old code threw JsonException
        // ("Failed to format HexBytes.") before writing a single byte.
        Assert.ThrowsExactly<SinkAbort>(() =>
        {
            using var writer = new Utf8JsonWriter(sink);
            new HexBytesJsonConverter().Write(writer, value, new JsonSerializerOptions());
        });

        Assert.IsGreaterThan(8L * 1024 * 1024, sink.Written);
    }

    [TestMethod]
    public void JsonConverter_AsAPropertyName_RejectsAHugeValueWithTheKeyLimit()
    {
        HexBytes value = Huge(GiB);

        JsonException error = Assert.ThrowsExactly<JsonException>(() =>
        {
            using var writer = new Utf8JsonWriter(new ArrayBufferWriter<byte>());
            writer.WriteStartObject();
            new HexBytesJsonConverter().WriteAsPropertyName(writer, value, new JsonSerializerOptions());
        });
        StringAssert.Contains(error.Message, "4,096-byte limit");
    }
}
