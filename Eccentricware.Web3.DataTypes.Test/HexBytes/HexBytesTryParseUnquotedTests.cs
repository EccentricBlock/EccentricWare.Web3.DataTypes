using System.Buffers;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Internal <c>HexBytes.TryParseUnquoted</c> (span and <see cref="ReadOnlySequence{T}"/> overloads), used by the JSON converter:
/// quote characters are content, not framing.
/// </summary>
[TestClass]
public sealed class HexBytesTryParseUnquotedTests
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

    private static ReadOnlySequence<byte> SplitEvery(byte[] data, int chunk, bool withEmptySegments = false)
    {
        var first = new Segment(ReadOnlyMemory<byte>.Empty);
        Segment last = first;
        for (int i = 0; i < data.Length; i += chunk)
        {
            last = last.Append(data.AsMemory(i, Math.Min(chunk, data.Length - i)));
            if (withEmptySegments)
                last = last.Append(ReadOnlyMemory<byte>.Empty);
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    // ----------------- table -----------------

    [TestMethod]
    [DataRow("", true, new byte[0])]
    [DataRow("   ", true, new byte[0])]
    [DataRow("0x", true, new byte[0])]
    [DataRow("0X", true, new byte[0])]
    [DataRow(" 0x ", true, new byte[0])]
    [DataRow("0x1", true, new byte[] { 0x01 })]
    [DataRow("0X1F", true, new byte[] { 0x1F })]
    [DataRow("1f", true, new byte[] { 0x1F })]
    [DataRow("0x00", true, new byte[] { 0x00 })]
    [DataRow(" 0xAB\r\n", true, new byte[] { 0xAB })]
    [DataRow("\t0xabc\t", true, new byte[] { 0x0A, 0xBC })]
    [DataRow("\"ab\"", false, null)]          // quote characters are content here
    [DataRow("\"0xab\"", false, null)]
    [DataRow("0x\"\"", false, null)]
    [DataRow("ab cd", false, null)]
    [DataRow("0xzz", false, null)]
    [DataRow("0x0x1", false, null)]
    [DataRow("0xab ", false, null)]
    public void TryParseUnquoted_Table_SpanAndSequence(string text, bool expectedOk, byte[]? expected)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);

        Assert.AreEqual(expectedOk, HexBytes.TryParseUnquoted(bytes, out HexBytes fromSpan), "span");
        AssertResult(expectedOk, expected, fromSpan, "span");

        foreach (int chunk in new[] { 1, 2, 3, 1000 })
        {
            foreach (bool withEmpty in new[] { false, true })
            {
                string label = $"sequence chunk={chunk} empties={withEmpty}";
                Assert.AreEqual(expectedOk, HexBytes.TryParseUnquoted(SplitEvery(bytes, chunk, withEmpty), out HexBytes fromSequence), label);
                AssertResult(expectedOk, expected, fromSequence, label);
            }
        }

        // An entirely empty sequence behaves like empty text.
        if (bytes.Length == 0)
        {
            Assert.IsTrue(HexBytes.TryParseUnquoted(ReadOnlySequence<byte>.Empty, out HexBytes empty));
            Assert.IsTrue(empty.IsEmpty);
        }
    }

    private static void AssertResult(bool ok, byte[]? expected, HexBytes actual, string label)
    {
        if (ok)
            CollectionAssert.AreEqual(expected, actual.ToArray(), label);
        else
            Assert.AreEqual(HexBytes.Empty, actual, label);
    }

    // ----------------- agreement with the public parser -----------------

    [TestMethod]
    public void Sequence_MatchesTheSpanParser_ForEveryChunking()
    {
        // The sequence decoder carries digit pairs across segment boundaries and trims across them.
        var random = new Random(20260511);
        const string alphabet = "0123456789abcdefABCDEF xXzg\t";

        for (int round = 0; round < 1500; round++)
        {
            var text = new StringBuilder();
            if (random.Next(3) == 0) text.Append(random.Next(2) == 0 ? "0x" : "0X");
            int digits = random.Next(0, 40);
            for (int i = 0; i < digits; i++)
                text.Append(random.Next(25) == 0 ? alphabet[random.Next(alphabet.Length)] : "0123456789abcdefABCDEF"[random.Next(22)]);
            if (random.Next(4) == 0) text.Insert(0, new string(' ', random.Next(1, 4)));
            if (random.Next(4) == 0) text.Append("\r\n");

            byte[] bytes = Encoding.ASCII.GetBytes(text.ToString());
            bool spanOk = HexBytes.TryParseUnquoted(bytes, out HexBytes expected);

            foreach (int chunk in new[] { 1, 2, 3, 7, 1000 })
            {
                foreach (bool withEmpty in new[] { false, true })
                {
                    bool sequenceOk = HexBytes.TryParseUnquoted(SplitEvery(bytes, chunk, withEmpty), out HexBytes actual);
                    Assert.AreEqual(spanOk, sequenceOk, $"'{text}' chunk={chunk} empties={withEmpty}");
                    Assert.AreEqual(expected, actual, $"'{text}' chunk={chunk} empties={withEmpty}");
                }
            }
        }
    }

    [TestMethod]
    public void QuotedText_IsAcceptedByThePublicParser_ButRejectedHere()
    {
        byte[] quoted = "\"0xab\""u8.ToArray();

        Assert.IsTrue(HexBytes.TryParse(quoted, out _));
        Assert.IsFalse(HexBytes.TryParseUnquoted(quoted, out _));
    }

    // ----------------- Array.MaxLength guard -----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Sequence_TooLongForAnArray_IsRejectedWithoutAllocatingAResult(bool withPrefix)
    {
        // Array.MaxLength is 0x7FFFFFC7, so (digits + 1) / 2 above that cannot be an array: digits >= 2 * 0x7FFFFFC7 + 1. One digit fewer would pass the guard
        // and allocate about 2 GiB, so only the first rejected length is exercised.
        // The sequence is ~4 GiB of text built from segments that all reference one 1 MiB buffer, so the cost is tiny: the
        // whitespace scan stops at the first non-space byte of each segment and the guard returns before any validation.
        const int segmentSize = 1 << 20;
        long threshold = (2L * Array.MaxLength) + 1;
        long prefixLength = withPrefix ? 2 : 0;

        byte[] shared = new byte[segmentSize];
        shared.AsSpan().Fill((byte)'a');
        byte[] firstBuffer = withPrefix ? [(byte)'0', (byte)'x', .. shared.AsSpan(2)] : shared;

        foreach (long digitCount in new[] { threshold, threshold + 1_000_000 })
        {
            long total = digitCount + prefixLength;
            var first = new Segment(firstBuffer);
            Segment last = first;
            long remaining = total - segmentSize;
            while (remaining > segmentSize)
            {
                last = last.Append(shared);
                remaining -= segmentSize;
            }

            last = last.Append(shared.AsMemory(0, (int)remaining));
            var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
            Assert.AreEqual(total, sequence.Length);

            long before = GC.GetAllocatedBytesForCurrentThread();
            bool ok = HexBytes.TryParseUnquoted(sequence, out HexBytes value);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.IsFalse(ok, $"digits={digitCount}");
            Assert.AreEqual(HexBytes.Empty, value);
            Assert.IsLessThan(1 << 20, allocated, "no result-sized allocation");
        }
    }
}
