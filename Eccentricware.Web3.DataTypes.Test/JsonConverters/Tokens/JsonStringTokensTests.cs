using EccentricWare.Web3.DataTypes.JsonConverters;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Direct tests of the shared slow path (<see cref="JsonStringTokens.ReadUnescaped{T}"/>): the raw-length cap, the
/// stack/pool switch at 256 bytes, the mapping of unescape failures to <see cref="JsonException"/>, the pooled buffer
/// being returned on every exit, and the parser contract.
/// </summary>
[TestClass]
public sealed class JsonStringTokensTests
{
    private const string TypeName = "X";

    /// <summary>Records what the parser delegate saw; <see cref="Parse"/> is passed as the parser.</summary>
    private sealed class Probe
    {
        public int Calls { get; private set; }
        public int LastLength { get; private set; } = -1;
        public bool Result { get; init; } = true;
        public bool CaptureText { get; init; } = true;
        public Exception? Throw { get; init; }

        public bool Parse(ReadOnlySpan<byte> utf8, out string value)
        {
            Calls++;
            LastLength = utf8.Length;
            value = CaptureText ? Encoding.UTF8.GetString(utf8) : "";
            if (Throw is not null)
                throw Throw;
            return Result;
        }
    }

    private static byte[] Json(string body) => Encoding.UTF8.GetBytes("\"" + body + "\"");

    private static string N0(int value) => value.ToString("N0");

    /// <summary>Reads the first (or, for a property name, second) token of <paramref name="json"/>; chunk 0 means contiguous.</summary>
    private static T Read<T>(byte[] json, int chunk, JsonStringTokens.TryParseUtf8<T> parse, int maxRawLength, string typeName = TypeName, bool propertyName = false)
    {
        Utf8JsonReader reader = chunk == 0 ? new Utf8JsonReader(json) : SegmentedUtf8Json.Reader(SegmentedUtf8Json.Split(json, chunk));
        Assert.IsTrue(reader.Read());
        if (propertyName)
            Assert.IsTrue(reader.Read());

        JsonTokenType tokenType = reader.TokenType;
        T result = JsonStringTokens.ReadUnescaped(ref reader, parse, typeName, maxRawLength);

        Assert.AreEqual(tokenType, reader.TokenType, "the reader must stay on the token");
        return result;
    }

    [TestMethod]
    public void Constants_AreTheDocumentedLiterals()
    {
        Assert.AreEqual(6, JsonStringTokens.MaxEscapedCharBytes); // \uXXXX
        Assert.AreEqual(256, JsonStringTokens.WhitespaceSlackBytes);
    }

    // ----------------- the raw-length cap -----------------

    // Every shape reaches the same limit check: contiguous or segmented, plain or escaped.
    private const int Cap = 20;

    private static string BodyOfRawLength(bool escaped, int rawLength) =>
        escaped ? "\\u0061" + new string('a', rawLength - 6) : new string('a', rawLength);

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(false, 4)]
    [DataRow(true, 0)]
    [DataRow(true, 4)]
    public void RawLengthExactlyAtTheCap_ReachesTheParser_WithTheUnescapedText(bool escaped, int chunk)
    {
        var probe = new Probe();

        string text = Read<string>(Json(BodyOfRawLength(escaped, Cap)), chunk, probe.Parse, Cap);

        Assert.AreEqual(1, probe.Calls);
        Assert.AreEqual(escaped ? Cap - 5 : Cap, probe.LastLength); // "\u0061" unescapes to one byte
        Assert.AreEqual(new string('a', probe.LastLength), text);
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(false, 4)]
    [DataRow(true, 0)]
    [DataRow(true, 4)]
    public void RawLengthOneOverTheCap_ThrowsTheTokenLimitMessage_AndNeverCallsTheParser(bool escaped, int chunk)
    {
        var probe = new Probe();
        byte[] json = Json(BodyOfRawLength(escaped, Cap + 1));

        JsonException error = Assert.ThrowsExactly<JsonException>(() => Read<string>(json, chunk, probe.Parse, Cap));

        Assert.AreEqual("The JSON string token for X exceeds the 20-byte limit.", error.Message);
        Assert.AreEqual(0, probe.Calls);
    }

    [TestMethod]
    public void TokenLimitMessage_NamesTheTypeAndFormatsTheLimitWithGroupSeparators()
    {
        var probe = new Probe();
        byte[] json = Json(new string('a', 1001));

        JsonException error = Assert.ThrowsExactly<JsonException>(() => Read<string>(json, 0, probe.Parse, 1000, "Widget"));

        Assert.AreEqual($"The JSON string token for Widget exceeds the {N0(1000)}-byte limit.", error.Message);
    }

    [TestMethod]
    public void EmptyToken_WithAZeroCap_IsParsedAsEmpty()
    {
        var probe = new Probe();

        string text = Read<string>(Json(""), 0, probe.Parse, 0);

        Assert.AreEqual(1, probe.Calls);
        Assert.AreEqual(0, probe.LastLength);
        Assert.AreEqual("", text);
    }

    [TestMethod]
    public void AnyNonEmptyToken_WithAZeroCap_IsRejected()
    {
        var probe = new Probe();

        JsonException error = Assert.ThrowsExactly<JsonException>(() => Read<string>(Json("a"), 0, probe.Parse, 0));

        Assert.AreEqual("The JSON string token for X exceeds the 0-byte limit.", error.Message);
        Assert.AreEqual(0, probe.Calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TheCapIsCheckedBeforeAnyBufferIsTaken(bool segmented)
    {
        // A 4 MiB token with a 10-byte cap: rejecting it must not rent or copy anything proportional to the token.
        byte[] json = Json(new string('a', 4 * 1024 * 1024));
        ReadOnlySequence<byte> sequence = SegmentedUtf8Json.Split(json, 1024 * 1024);
        var probe = new Probe();

        void Attempt()
        {
            Utf8JsonReader reader = segmented ? SegmentedUtf8Json.Reader(sequence) : new Utf8JsonReader(json);
            reader.Read();
            try
            {
                JsonStringTokens.ReadUnescaped<string>(ref reader, probe.Parse, TypeName, 10);
                Assert.Fail("expected JsonException");
            }
            catch (JsonException)
            {
            }
        }

        Attempt(); // warm up the exception machinery
        long best = long.MaxValue;
        for (int trial = 0; trial < 5; trial++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Attempt();
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.IsLessThan(64 * 1024, best, $"allocated {best} bytes");
        Assert.AreEqual(0, probe.Calls);
    }

    // ----------------- unescaping -----------------

    [TestMethod]
    [DataRow("\\u0041bc", "Abc")]
    [DataRow("\\u0030x\\u0061b", "0xab")]
    [DataRow("a\\/b\\\\c\\\"d", "a/b\\c\"d")]
    [DataRow("\\t\\r\\n x", "\t\r\n x")]
    [DataRow("\\ud83d\\ude00", "\U0001F600")] // a valid surrogate pair becomes one 4-byte UTF-8 sequence
    public void EscapesAreResolved_AndTheParserSeesTheUnescapedLength(string body, string expected)
    {
        foreach (int chunk in new[] { 0, 3 })
        {
            var probe = new Probe();

            string text = Read<string>(Json(body), chunk, probe.Parse, 1000);

            Assert.AreEqual(expected, text, $"chunk {chunk}");
            Assert.AreEqual(Encoding.UTF8.GetByteCount(expected), probe.LastLength, $"chunk {chunk}");
        }
    }

    [TestMethod]
    public void MultiByteUtf8_IsPassedThroughAsUtf8_NotAsRawTokenLength()
    {
        var probe = new Probe();

        string text = Read<string>(Json("\u00e9\\u0041"), 0, probe.Parse, 100); // 'e acute' literal (2 bytes) + escaped 'A'

        Assert.AreEqual("\u00e9A", text);
        Assert.AreEqual(3, probe.LastLength);
    }

    [TestMethod]
    public void PropertyNameTokens_AreUnescaped_AndTheReaderStaysOnTheName()
    {
        byte[] json = Encoding.UTF8.GetBytes("{\"\\u0041b\":1}");

        foreach (int chunk in new[] { 0, 3 })
        {
            var probe = new Probe();

            string text = Read<string>(json, chunk, probe.Parse, 100, propertyName: true);

            Assert.AreEqual("Ab", text, $"chunk {chunk}");
        }
    }

    // ----------------- stack versus pooled buffer -----------------

    private static (bool Aliased, int Length) ReadAndCheckBufferOrigin(int rawLength, out bool everAliased)
    {
        byte[] sentinel = [];
        JsonStringTokens.TryParseUtf8<(bool Aliased, int Length)> parse = (ReadOnlySpan<byte> utf8, out (bool Aliased, int Length) value) =>
        {
            value = (Unsafe.AreSame(ref MemoryMarshal.GetReference(utf8), ref MemoryMarshal.GetArrayDataReference(sentinel)), utf8.Length);
            return true;
        };

        byte[] json = Json("\\u0030" + new string(' ', rawLength - 6)); // escaped, so the token takes the copy path
        (bool Aliased, int Length) result = default;
        everAliased = false;

        // Putting an array of the right bucket into this thread's pool cache first makes the rent in ReadUnescaped return that very
        // array; a trimmed cache just costs another attempt.
        for (int attempt = 0; attempt < 5 && !everAliased; attempt++)
        {
            sentinel = ArrayPool<byte>.Shared.Rent(rawLength); // same size bucket as the rent inside ReadUnescaped
            ArrayPool<byte>.Shared.Return(sentinel);
            result = Read(json, 0, parse, 100_000);
            everAliased |= result.Aliased;
        }

        return result;
    }

    [TestMethod]
    [DataRow(255, false)]
    [DataRow(256, false)]
    [DataRow(257, true)]
    [DataRow(258, true)]
    [DataRow(1000, true)]
    public void BufferIsStackAllocatedUpTo256Bytes_AndPooledAbove(int rawLength, bool pooled)
    {
        (bool _, int length) = ReadAndCheckBufferOrigin(rawLength, out bool aliased);

        Assert.AreEqual(rawLength - 5, length, "unescaped length");
        Assert.AreEqual(pooled, aliased);
    }

    [TestMethod]
    [DataRow(254)]
    [DataRow(255)]
    [DataRow(256)]
    [DataRow(257)]
    [DataRow(258)]
    [DataRow(300)]
    public void TokensAroundTheStackThreshold_ParseTheirWholeText(int rawLength)
    {
        var probe = new Probe();
        string body = "ab" + new string(' ', rawLength - 2);

        string text = Read<string>(Json(body), 0, probe.Parse, 100_000);

        Assert.AreEqual(body, text);
        Assert.AreEqual(rawLength, probe.LastLength);
    }

    // ----------------- failures map to JsonException -----------------

    [TestMethod]
    [DataRow("\\ud800", 0)]
    [DataRow("\\udc00", 0)]
    [DataRow("\\ud800\\u0041", 0)]
    [DataRow("\\ud800", 300)] // pooled path
    [DataRow("\\ud83d\\u0041", 300)]
    public void LoneSurrogateEscape_BecomesTheInvalidValueError_AndTheParserIsNotCalled(string body, int paddingSpaces)
    {
        var probe = new Probe();
        byte[] json = Json(body + new string(' ', paddingSpaces));

        JsonException error = Assert.ThrowsExactly<JsonException>(() => Read<string>(json, 0, probe.Parse, 1000));

        Assert.AreEqual("Invalid value for X.", error.Message);
        Assert.AreEqual(0, probe.Calls);
    }

    [TestMethod]
    [DataRow("123")]
    [DataRow("-1.5e3")]
    [DataRow("null")]
    [DataRow("true")]
    [DataRow("false")]
    [DataRow("{}")]
    [DataRow("[]")]
    public void NonStringTokens_BecomeTheInvalidValueError_NotInvalidOperationException(string json)
    {
        var probe = new Probe();

        JsonException error = Assert.ThrowsExactly<JsonException>(() => Read<string>(Encoding.ASCII.GetBytes(json), 0, probe.Parse, 1000));

        Assert.AreEqual("Invalid value for X.", error.Message);
        Assert.AreEqual(0, probe.Calls);
    }

    [TestMethod]
    public void ParserReturningFalse_ThrowsTheInvalidValueError_WithTheSuppliedTypeName()
    {
        var probe = new Probe { Result = false };

        JsonException error = Assert.ThrowsExactly<JsonException>(() => Read<string>(Json("abc"), 0, probe.Parse, 100, "Tn"));

        Assert.AreEqual("Invalid value for Tn.", error.Message);
        Assert.AreEqual(1, probe.Calls);
    }

    [TestMethod]
    public void ParserThatThrows_PropagatesUnchanged_BecauseParsersAreExpectedToBeTotal()
    {
        // Documents current behaviour: only the bool contract is mapped; every converter's parser returns false instead of throwing.
        var boom = new FormatException("boom");
        var probe = new Probe { Throw = boom };

        FormatException caught = Assert.ThrowsExactly<FormatException>(() => Read<string>(Json("abc"), 0, probe.Parse, 100));

        Assert.AreSame(boom, caught);
    }

    // ----------------- the pooled buffer is always returned -----------------

    private const int PooledTokenLength = 64 * 1024;

    private static byte[] PooledTokenJson(string prefix) => Json(prefix + new string(' ', PooledTokenLength));

    private static long BestAllocationPer100Calls(Action call)
    {
        for (int i = 0; i < 20; i++)
            Swallow(call);

        long best = long.MaxValue;
        for (int trial = 0; trial < 5; trial++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
                Swallow(call);
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return best;

        static void Swallow(Action action)
        {
            try { action(); }
            catch (Exception) { /* the failure paths are the point */ }
        }
    }

    [TestMethod]
    [DataRow("success")]
    [DataRow("parser-false")]
    [DataRow("parser-throws")]
    [DataRow("lone-surrogate")]
    public void PooledBuffer_IsReturnedOnEveryExit(string exit)
    {
        // A leaked 64 KiB rent costs a fresh 128 KiB array per call (>12 MiB over 100 calls); the exceptions cost about 1 KiB.
        byte[] json = PooledTokenJson(exit == "lone-surrogate" ? "\\ud800" : "\\u0061");
        var probe = new Probe
        {
            CaptureText = false, // decoding 64 KiB of text would swamp the measurement
            Result = exit != "parser-false",
            Throw = exit == "parser-throws" ? new FormatException("boom") : null,
        };

        long best = BestAllocationPer100Calls(() => Read<string>(json, 0, probe.Parse, 1 << 20));

        Assert.IsLessThan(100 * 8 * 1024, best, $"{exit}: allocated {best} bytes over 100 calls");
    }
}
