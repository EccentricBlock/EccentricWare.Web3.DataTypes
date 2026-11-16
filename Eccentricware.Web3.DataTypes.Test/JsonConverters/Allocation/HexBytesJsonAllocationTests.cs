using EccentricWare.Web3.DataTypes.JsonConverters;
using System.Text;
using System.Text.Json;
using static EccentricWare.Web3.DataTypes.Tests.HexBytesJsonTestKit;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Memory behaviour of <see cref="HexBytesJsonConverter"/> on large tokens: only the result array is allocated, invalid
/// or oversized input allocates no result, and the default escaped-token limit is 32 MiB. These tests use multi-megabyte
/// payloads, so they run serially to keep the working set bounded.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class HexBytesJsonAllocationTests
{
    private const int DataBytes = 64 * 1024;

    private static long BestOf(int trials, Func<long> measure)
    {
        long best = long.MaxValue;
        for (int i = 0; i < trials; i++)
            best = Math.Min(best, measure());
        return best;
    }

    private static long Allocated(Action action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [TestMethod]
    public void ContiguousToken_AllocatesOnlyTheResultArray()
    {
        byte[] json = JsonTokenBodies.Value(PatternText(DataBytes));
        var converter = new HexBytesJsonConverter();
        HexBytes value = default;

        ReadDirect(converter, json); // warm up
        long best = BestOf(5, () => Allocated(() => value = ReadDirect(converter, json)));

        Assert.AreEqual(DataBytes, value.Length);
        // The result array plus its header, and nothing proportional to the token (which is twice as long).
        Assert.IsLessThan(DataBytes + 256, best, $"allocated {best} bytes");
    }

    [TestMethod]
    public void SplitAndEscapedTokens_DoNotAllocateACopyOfTheToken()
    {
        var converter = new HexBytesJsonConverter();
        byte[] split = JsonTokenBodies.Value(PatternText(DataBytes));
        byte[] escaped = JsonTokenBodies.Value(JsonTokenBodies.EscapeFirst(PatternText(DataBytes)));
        HexBytes viaSplit = default, viaEscaped = default;

        ReadSplit(converter, split, 1000);
        ReadDirect(converter, escaped); // warm up, including the pooled buffer

        // Best of a few runs: a pool trim between warm-up and measurement adds a one-off allocation to one run.
        long splitBest = BestOf(5, () => Allocated(() => viaSplit = ReadSplit(converter, split, 1000)));
        long escapedBest = BestOf(5, () => Allocated(() => viaEscaped = ReadDirect(converter, escaped)));

        Assert.AreEqual(DataBytes, viaSplit.Length);
        Assert.AreEqual(DataBytes, viaEscaped.Length);
        // The token is 2x the result; an implementation that copied it would allocate about 3x in total.
        Assert.IsLessThan(DataBytes + 1024, splitBest, $"split allocated {splitBest} bytes");
        Assert.IsLessThan(DataBytes + 1024, escapedBest, $"escaped allocated {escapedBest} bytes");
    }

    [TestMethod]
    public void SplitUnescapedToken_IsNotSubjectToATinyEscapedLimit_AndAllocatesOnlyTheResult()
    {
        var converter = new HexBytesJsonConverter(maxEscapedTokenBytes: 64);
        byte[] json = JsonTokenBodies.Value(PatternText(DataBytes));
        HexBytes value = default;

        ReadSplit(converter, json, 1000);
        long best = BestOf(5, () => Allocated(() => value = ReadSplit(converter, json, 1000)));

        Assert.AreEqual(Pattern(DataBytes), value);
        Assert.IsLessThan(DataBytes + 1024, best, $"allocated {best} bytes");
    }

    [TestMethod]
    public void InvalidLargeToken_FailsWithoutAllocatingAResultArray()
    {
        // The digits are validated before the result array is allocated: a 4 MiB token whose last digit is bad must not
        // cost the 2 MiB result before being rejected.
        const int Digits = 4 * 1024 * 1024;
        byte[] json = new byte[Digits + 4];
        json[0] = (byte)'"';
        json.AsSpan(1, Digits + 1).Fill((byte)'a');
        json[Digits + 1] = (byte)'z';
        json[Digits + 2] = (byte)'a';
        json[Digits + 3] = (byte)'"';
        var converter = new HexBytesJsonConverter();

        long Measure(Action action) => Allocated(() => Assert.ThrowsExactly<JsonException>(action));

        Measure(() => ReadDirect(converter, json)); // warm up the exception machinery
        Measure(() => ReadSplit(converter, json, 1_000_000));
        long contiguous = BestOf(5, () => Measure(() => ReadDirect(converter, json)));
        long split = BestOf(5, () => Measure(() => ReadSplit(converter, json, 1_000_000)));

        Assert.IsLessThan(64 * 1024, contiguous, $"contiguous allocated {contiguous} bytes");
        Assert.IsLessThan(64 * 1024, split, $"split allocated {split} bytes");
    }

    [TestMethod]
    public void OversizedDictionaryKey_IsRejectedUnparsed_WithoutAllocatingAResult()
    {
        // 4 Mi digits: far above the raw-token guard. Rejecting by raw length must not decode the 2 MiB key first
        // (the decoded-length check alone would only fire after the result array had been allocated).
        const int Digits = 4 * 1024 * 1024;
        byte[] json = new byte[Digits + 9];
        Encoding.ASCII.GetBytes("{\"0x").CopyTo(json, 0);
        json.AsSpan(4, Digits).Fill((byte)'a');
        Encoding.ASCII.GetBytes("\":1}").CopyTo(json, 4 + Digits);
        var converter = new HexBytesJsonConverter();

        long Measure() => Allocated(() =>
            Assert.AreEqual(KeyLimitMessage, Assert.ThrowsExactly<JsonException>(() => ReadKeyDirect(converter, json)).Message));

        Measure(); // warm up
        long best = BestOf(5, Measure);

        Assert.IsLessThan(64 * 1024, best, $"allocated {best} bytes");
    }

    [TestMethod]
    public void DefaultLimit_Accepts_A16MebibyteEscapedValue()
    {
        const int Pairs = 8 * 1024 * 1024;
        byte[] prefix = Encoding.ASCII.GetBytes("\"\\u0030x");
        byte[] json = new byte[prefix.Length + (Pairs * 2) + 1];
        prefix.CopyTo(json, 0);
        json.AsSpan(prefix.Length, Pairs * 2).Fill((byte)'a');
        json[^1] = (byte)'"';

        HexBytes value = JsonSerializer.Deserialize<HexBytes>(json);

        Assert.AreEqual(Pairs, value.Length);
        Assert.AreEqual(0xAA, value.Span[0]);
        Assert.AreEqual(0xAA, value.Span[^1]);
    }

    [TestMethod]
    public void DefaultLimit_Rejects_AnEscapedTokenOneByteOver32Mebibytes()
    {
        int limit = HexBytesJsonConverter.DefaultMaxEscapedTokenBytes;
        string head = "\\u0030x";
        byte[] atLimit = new byte[limit + 2];
        byte[] overLimit = new byte[limit + 3];
        foreach (byte[] json in new[] { atLimit, overLimit })
        {
            json[0] = (byte)'"';
            Encoding.ASCII.GetBytes(head).CopyTo(json, 1);
            json.AsSpan(1 + head.Length, json.Length - 2 - head.Length).Fill((byte)'a');
            json[^1] = (byte)'"';
        }

        JsonException error = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<HexBytes>(overLimit));
        Assert.AreEqual(TokenLimitMessage(limit), error.Message);

        // an odd digit count is left-padded to whole bytes
        Assert.AreEqual((limit - head.Length + 1) / 2, JsonSerializer.Deserialize<HexBytes>(atLimit).Length);
    }

    [TestMethod]
    public void WriteOfALongValue_AllocatesNothingProportionalToTheValue()
    {
        // 20,000,000 bytes -> 40,000,004 bytes of JSON. Formatting into one buffer (or asking the sink for one) would allocate tens of
        // megabytes; the streamed write needs only the writer and the sink's fixed buffer. The sink counts as an allocation when it grows.
        const int Length = 20_000_000;
        HexBytes value = Pattern(Length);
        var converter = new HexBytesJsonConverter();
        var options = new JsonSerializerOptions();

        using (var warmUpWriter = new Utf8JsonWriter(new RecordingBufferWriter()))
            converter.Write(warmUpWriter, Pattern(1000), options);

        long best = long.MaxValue;
        for (int trial = 0; trial < 3; trial++)
        {
            var sink = new RecordingBufferWriter();
            long before = GC.GetAllocatedBytesForCurrentThread();
            using (var writer = new Utf8JsonWriter(sink))
                converter.Write(writer, value, options);
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);

            Assert.AreEqual((2L * Length) + 4, sink.Written); // quotes + "0x" + digits
        }

        Assert.IsLessThan(64 * 1024, best, $"allocated {best} bytes");
    }
}
