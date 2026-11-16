using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class Hash32FormattingTests
{
    private static readonly string LowerHex = Hash32TestData.Hex(Hash32TestData.Bytes0To31());
    private static readonly string UpperHex = Hash32TestData.Hex(Hash32TestData.Bytes0To31(), uppercase: true);

    /// <summary>Format specifier, expected text (the prefix is always lowercase "0x", even for "0X"), required length.</summary>
    public static IEnumerable<object[]> Formats()
    {
        yield return ["", "0x" + LowerHex, 66];
        yield return ["0x", "0x" + LowerHex, 66];
        yield return ["0X", "0x" + UpperHex, 66];
        yield return ["x", LowerHex, 64];
        yield return ["X", UpperHex, 64];
    }

    [TestMethod]
    public void ToString_Default_IsLowercaseWithPrefix_AndMatchesLiteral()
    {
        Assert.AreEqual("0x" + LowerHex, Hash32TestData.Value0To31().ToString());
        Assert.AreEqual("0x000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", Hash32TestData.Value0To31().ToString());
        Assert.AreEqual("0x" + new string('0', 64), Hash32.Zero.ToString());
    }

    [TestMethod]
    [DynamicData(nameof(Formats))]
    public void ToString_Format_ProducesExpectedText(string format, string expected, int length)
    {
        Hash32 value = Hash32TestData.Value0To31();

        Assert.AreEqual(expected, value.ToString(format, CultureInfo.InvariantCulture));
        Assert.AreEqual(length, expected.Length);
    }

    [TestMethod]
    public void ToString_NullFormat_IsTheDefaultFormat()
    {
        Hash32 value = Hash32TestData.Value0To31();

        Assert.AreEqual(value.ToString(), value.ToString(null));
        Assert.AreEqual(value.ToString(), value.ToString(null, null));
    }

    [TestMethod]
    [DataRow("G")]
    [DataRow("g")]
    [DataRow("xx")]
    [DataRow("0y")]
    [DataRow("0")]
    [DataRow("x ")]
    [DataRow("0x0")]
    public void InvalidFormat_ThrowsFormatException_ForAllFormattingEntryPoints(string format)
    {
        Hash32 value = Hash32TestData.Value0To31();
        char[] chars = new char[66];
        byte[] utf8 = new byte[66];

        Assert.ThrowsExactly<FormatException>(() => value.ToString(format, CultureInfo.InvariantCulture), format);
        Assert.ThrowsExactly<FormatException>(() => value.TryFormat(chars, out _, format), format);
        Assert.ThrowsExactly<FormatException>(() => value.TryFormat(utf8, out _, format), format);
    }

    [TestMethod]
    public void InvalidFormat_IsReportedBeforeTheBufferSizeIsChecked()
    {
        // The format is parsed first, so even a too-small buffer throws instead of returning false (current behaviour).
        Hash32 value = Hash32TestData.Value0To31();

        Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new char[1], out _, "G"));
        Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new byte[1], out _, "G"));
    }

    [TestMethod]
    [DynamicData(nameof(Formats))]
    public void TryFormat_Chars_FillsExactlyTheRequiredLength(string format, string expected, int required)
    {
        Hash32 value = Hash32TestData.Value0To31();
        char[] buffer = new char[required + 10];
        Array.Fill(buffer, '#');

        Assert.IsTrue(value.TryFormat(buffer, out int written, format));

        Assert.AreEqual(required, written);
        Assert.AreEqual(expected, new string(buffer, 0, written));
        Assert.IsTrue(buffer.AsSpan(required).ToArray().All(c => c == '#'), "tail must be untouched");
    }

    [TestMethod]
    [DynamicData(nameof(Formats))]
    public void TryFormat_Chars_ExactSizeSucceeds_OneShortFails_AndEmptyFails(string format, string expected, int required)
    {
        Hash32 value = Hash32TestData.Value0To31();

        char[] exact = new char[required];
        Assert.IsTrue(value.TryFormat(exact, out int written, format));
        Assert.AreEqual(required, written);
        Assert.AreEqual(expected, new string(exact));

        char[] shortBuffer = new char[required - 1];
        Array.Fill(shortBuffer, '#');
        Assert.IsFalse(value.TryFormat(shortBuffer, out written, format));
        Assert.AreEqual(0, written);
        Assert.IsTrue(shortBuffer.All(c => c == '#'), "a failed format must not write");

        Assert.IsFalse(value.TryFormat(Span<char>.Empty, out written, format));
        Assert.AreEqual(0, written);
    }

    [TestMethod]
    [DynamicData(nameof(Formats))]
    public void TryFormat_Utf8_FillsExactlyTheRequiredLength(string format, string expected, int required)
    {
        Hash32 value = Hash32TestData.Value0To31();
        byte[] buffer = new byte[required + 10];
        Array.Fill(buffer, (byte)'#');

        Assert.IsTrue(value.TryFormat(buffer, out int written, format));

        Assert.AreEqual(required, written);
        Assert.AreEqual(expected, Encoding.ASCII.GetString(buffer, 0, written));
        Assert.IsTrue(buffer.AsSpan(required).ToArray().All(b => b == (byte)'#'), "tail must be untouched");
    }

    [TestMethod]
    [DynamicData(nameof(Formats))]
    public void TryFormat_Utf8_ExactSizeSucceeds_OneShortFails_AndEmptyFails(string format, string expected, int required)
    {
        Hash32 value = Hash32TestData.Value0To31();

        byte[] exact = new byte[required];
        Assert.IsTrue(value.TryFormat(exact, out int written, format));
        Assert.AreEqual(required, written);
        Assert.AreEqual(expected, Encoding.ASCII.GetString(exact));

        byte[] shortBuffer = new byte[required - 1];
        Array.Fill(shortBuffer, (byte)'#');
        Assert.IsFalse(value.TryFormat(shortBuffer, out written, format));
        Assert.AreEqual(0, written);
        Assert.IsTrue(shortBuffer.All(b => b == (byte)'#'), "a failed format must not write");

        Assert.IsFalse(value.TryFormat(Span<byte>.Empty, out written, format));
        Assert.AreEqual(0, written);
    }

    [TestMethod]
    public void TryFormat_DefaultFormat_RequiresSixtySixCharacters()
    {
        Hash32 value = Hash32TestData.Value0To31();

        Assert.IsFalse(value.TryFormat(new char[65], out _));
        Assert.IsTrue(value.TryFormat(new char[66], out int written));
        Assert.AreEqual(66, written);
        Assert.IsFalse(value.TryFormat(new byte[65], out _));
        Assert.IsTrue(value.TryFormat(new byte[66], out written));
        Assert.AreEqual(66, written);
    }

    // ---- interface dispatch ----

    [TestMethod]
    public void IFormattable_Dispatch_MatchesToString()
    {
        Hash32 value = Hash32TestData.Value0To31();
        IFormattable formattable = value;

        Assert.AreEqual(value.ToString(), formattable.ToString(null, null));
        Assert.AreEqual(LowerHex, formattable.ToString("x", CultureInfo.InvariantCulture));
        Assert.AreEqual(UpperHex, formattable.ToString("X", null));
        Assert.ThrowsExactly<FormatException>(() => formattable.ToString("G", null));
    }

    [TestMethod]
    public void ISpanFormattable_Dispatch_WritesTheFormattedText()
    {
        ISpanFormattable formattable = Hash32TestData.Value0To31();
        char[] buffer = new char[66];

        Assert.IsTrue(formattable.TryFormat(buffer, out int written, default, null));
        Assert.AreEqual("0x" + LowerHex, new string(buffer, 0, written));
        Assert.IsTrue(formattable.TryFormat(buffer, out written, "X", null));
        Assert.AreEqual(UpperHex, new string(buffer, 0, written));
    }

    [TestMethod]
    public void IUtf8SpanFormattable_Dispatch_WritesTheFormattedBytes()
    {
        IUtf8SpanFormattable formattable = Hash32TestData.Value0To31();
        byte[] buffer = new byte[66];

        Assert.IsTrue(formattable.TryFormat(buffer, out int written, "0x", null));
        Assert.AreEqual("0x" + LowerHex, Encoding.ASCII.GetString(buffer, 0, written));
    }

    [TestMethod]
    public void StringFormatAndInterpolation_UseTheFormatSpecifier()
    {
        Hash32 value = Hash32TestData.Value0To31();

        Assert.AreEqual(UpperHex, string.Format(CultureInfo.InvariantCulture, "{0:X}", value));
        Assert.AreEqual("0x" + LowerHex, string.Format(CultureInfo.InvariantCulture, "{0}", value));
        Assert.AreEqual("0x" + LowerHex, $"{value}");
        Assert.AreEqual("0x" + UpperHex, $"{value:0X}");
        Assert.AreEqual(LowerHex, $"{value:x}");
    }

    [TestMethod]
    public void Utf8Interpolation_WritesHexBytes()
    {
        Hash32 value = Hash32TestData.Value0To31();
        byte[] buffer = new byte[64];

        Assert.IsTrue(Utf8.TryWrite(buffer, $"{value:x}", out int written));

        Assert.AreEqual(64, written);
        Assert.AreEqual(LowerHex, Encoding.ASCII.GetString(buffer));
    }

    [TestMethod]
    public void Formatting_CharUtf8AndToString_AreIdentical_ForRandomValues()
    {
        foreach (Hash32 value in Hash32TestData.RandomHashes(new Random(606), 300))
        {
            foreach (string format in new[] { "", "0x", "0X", "x", "X" })
            {
                string expected = value.ToString(format, CultureInfo.InvariantCulture);
                char[] chars = new char[66];
                byte[] utf8 = new byte[66];

                Assert.IsTrue(value.TryFormat(chars, out int c, format));
                Assert.IsTrue(value.TryFormat(utf8, out int b, format));
                Assert.AreEqual(expected, new string(chars, 0, c), format);
                Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8, 0, b), format);
            }
        }
    }

    [TestMethod]
    public void TryFormat_WithExplicitFormat_DoesNotAllocate()
    {
        Hash32 value = Hash32TestData.Value0To31();
        char[] chars = new char[66];
        byte[] utf8 = new byte[66];

        // Warm up so JIT and static initialisation are not attributed to the measured loops.
        for (int i = 0; i < 200; i++)
        {
            value.TryFormat(chars, out _, "0x");
            value.TryFormat(utf8, out _, "x");
        }

        // Minimum over several trials: the thread-local counter can pick up unrelated runtime allocations once.
        long minimum = long.MaxValue;
        for (int trial = 0; trial < 5; trial++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                value.TryFormat(chars, out _, "0x");
                value.TryFormat(chars, out _, "X");
                value.TryFormat(utf8, out _, "0X");
                value.TryFormat(utf8, out _, "x");
            }

            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        // 4,000 calls: any per-call allocation (a 24-byte object at the very least) would exceed this by far.
        Assert.IsLessThan(2_048, minimum);
    }
}
