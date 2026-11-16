using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="HexBytes"/>: <c>ToString</c>, <c>TryFormat</c> (chars and UTF-8) and interface dispatch.</summary>
[TestClass]
public sealed class HexBytesFormattingTests
{
    private static readonly string[] AllFormats = ["", "0x", "0X", "x", "X"];

    private static string Expected(byte[] bytes, string format)
    {
        string hex = format is "X" or "0X" ? Convert.ToHexString(bytes) : Convert.ToHexStringLower(bytes);
        return format is "" or "0x" or "0X" ? "0x" + hex : hex; // the prefix is always lowercase
    }

    [TestMethod]
    public void ToString_Formats()
    {
        var value = new HexBytes(new byte[] { 0xAB, 0xCD });

        Assert.AreEqual("0xabcd", value.ToString());
        Assert.AreEqual("0xabcd", value.ToString(null));
        Assert.AreEqual("0xabcd", value.ToString(""));
        Assert.AreEqual("0xabcd", value.ToString("0x", CultureInfo.InvariantCulture));
        Assert.AreEqual("0xABCD", value.ToString("0X"));
        Assert.AreEqual("abcd", value.ToString("x"));
        Assert.AreEqual("ABCD", value.ToString("X"));
    }

    [TestMethod]
    public void ToString_Empty_Formats()
    {
        Assert.AreEqual("0x", HexBytes.Empty.ToString());
        Assert.AreEqual("0x", HexBytes.Empty.ToString(""));
        Assert.AreEqual("0x", HexBytes.Empty.ToString("0x"));
        Assert.AreEqual("0x", HexBytes.Empty.ToString("0X"));
        Assert.AreEqual(string.Empty, HexBytes.Empty.ToString("x"));
        Assert.AreEqual(string.Empty, HexBytes.Empty.ToString("X"));
    }

    [TestMethod]
    [DataRow("G")]
    [DataRow("0y")]
    [DataRow("xx")]
    [DataRow("0")]
    [DataRow("d")]
    public void Format_UnknownFormat_ThrowsFormatException_ForEveryOverloadAndForEmpty(string format)
    {
        var value = new HexBytes(new byte[] { 0xAB });
        char[] chars = new char[16];
        byte[] utf8 = new byte[16];

        Assert.ThrowsExactly<FormatException>(() => value.ToString(format));
        Assert.ThrowsExactly<FormatException>(() => HexBytes.Empty.ToString(format));
        Assert.ThrowsExactly<FormatException>(() => value.TryFormat(chars, out _, format));
        Assert.ThrowsExactly<FormatException>(() => value.TryFormat(utf8, out _, format));
        Assert.ThrowsExactly<FormatException>(() => HexBytes.Empty.TryFormat(chars, out _, format));
        Assert.ThrowsExactly<FormatException>(() => HexBytes.Empty.TryFormat(utf8, out _, format));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(31)]
    [DataRow(32)]
    [DataRow(33)]
    public void TryFormat_ExactFit_OneShort_AndOversized_ForEveryFormat(int length)
    {
        byte[] bytes = new byte[length];
        new Random(length + 100).NextBytes(bytes);
        var value = new HexBytes(bytes);

        foreach (string format in AllFormats)
        {
            string expected = Expected(bytes, format);
            int n = expected.Length;

            // Exact fit (chars and UTF-8).
            char[] exactChars = new char[n];
            byte[] exactUtf8 = new byte[n];
            Assert.IsTrue(value.TryFormat(exactChars, out int c, format), $"{format} chars");
            Assert.IsTrue(value.TryFormat(exactUtf8, out int b, format), $"{format} utf8");
            Assert.AreEqual(expected, new string(exactChars, 0, c));
            Assert.AreEqual(expected, Encoding.ASCII.GetString(exactUtf8, 0, b));

            // Oversized: exactly n written, the sentinel tail is untouched.
            char[] roomyChars = new char[n + 3];
            byte[] roomyUtf8 = new byte[n + 3];
            Array.Fill(roomyChars, '#');
            Array.Fill(roomyUtf8, (byte)'#');
            Assert.IsTrue(value.TryFormat(roomyChars, out c, format));
            Assert.IsTrue(value.TryFormat(roomyUtf8, out b, format));
            Assert.AreEqual(n, c);
            Assert.AreEqual(n, b);
            Assert.AreEqual(expected + "###", new string(roomyChars));
            Assert.AreEqual(expected + "###", Encoding.ASCII.GetString(roomyUtf8));

            // One short: false, nothing written, destination untouched.
            if (n > 0)
            {
                char[] shortChars = new char[n - 1];
                byte[] shortUtf8 = new byte[n - 1];
                Array.Fill(shortChars, '#');
                Array.Fill(shortUtf8, (byte)'#');
                Assert.IsFalse(value.TryFormat(shortChars, out c, format), $"{format} chars short");
                Assert.IsFalse(value.TryFormat(shortUtf8, out b, format), $"{format} utf8 short");
                Assert.AreEqual(0, c);
                Assert.AreEqual(0, b);
                Assert.IsTrue(shortChars.All(ch => ch == '#'));
                Assert.IsTrue(shortUtf8.All(x => x == '#'));
            }
        }
    }

    [TestMethod]
    public void TryFormat_Empty_BothOverloads_AllFormats()
    {
        foreach ((string format, string expected) in new[] { ("", "0x"), ("0x", "0x"), ("0X", "0x"), ("x", ""), ("X", "") })
        {
            byte[] utf8 = new byte[4];
            char[] chars = new char[4];
            Assert.IsTrue(HexBytes.Empty.TryFormat(utf8, out int b, format), format);
            Assert.IsTrue(HexBytes.Empty.TryFormat(chars, out int c, format), format);
            Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8, 0, b));
            Assert.AreEqual(expected, new string(chars, 0, c));
        }

        Assert.IsFalse(HexBytes.Empty.TryFormat(new byte[1], out int written));
        Assert.AreEqual(0, written);
        Assert.IsFalse(HexBytes.Empty.TryFormat(new char[1], out written));
        Assert.IsTrue(HexBytes.Empty.TryFormat(Span<byte>.Empty, out written, "x"));
        Assert.AreEqual(0, written);
    }

    [TestMethod]
    public void TryFormat_DoesNotAllocate()
    {
        var value = new HexBytes(new byte[68]);
        char[] chars = new char[value.HexLength + 2];
        byte[] utf8 = new byte[value.HexLength + 2];

        for (int i = 0; i < 100; i++)
        {
            value.TryFormat(chars, out _, "0x");
            value.TryFormat(utf8, out _, "X");
        }

        long minimum = long.MaxValue;
        for (int trial = 0; trial < 5; trial++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                value.TryFormat(chars, out _, "0x");
                value.TryFormat(utf8, out _, "X");
                value.TryFormat(chars, out _);
                value.TryFormat(utf8, out _);
            }

            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        // 4,000 calls: any per-call allocation (24 bytes or more) would exceed this bound by far.
        Assert.IsLessThan(2048, minimum);
    }

    [TestMethod]
    public void InterfaceDispatch_ProducesTheSameTextAsToString()
    {
        var value = new HexBytes(new byte[] { 0xAB, 0xCD });

        Assert.AreEqual("v=0xabcd", $"v={value}");
        Assert.AreEqual("v=ABCD", $"v={value:X}");
        Assert.AreEqual("0xabcd|abcd", string.Format(CultureInfo.InvariantCulture, "{0}|{0:x}", value));

        Span<char> chars = stackalloc char[16];
        Assert.IsTrue(((ISpanFormattable)value).TryFormat(chars, out int c, "0X", null));
        Assert.AreEqual("0xABCD", new string(chars[..c]));

        Span<byte> utf8 = stackalloc byte[16];
        Assert.IsTrue(((IUtf8SpanFormattable)value).TryFormat(utf8, out int b, "x", null));
        Assert.AreEqual("abcd", Encoding.ASCII.GetString(utf8[..b]));

        Assert.IsTrue(Utf8.TryWrite(utf8, $"{value:x}", out b));
        Assert.AreEqual("abcd", Encoding.ASCII.GetString(utf8[..b]));
    }

    [TestMethod]
    public void Format_IsSafeFromManyThreads()
    {
        byte[] bytes = new byte[200];
        new Random(5).NextBytes(bytes);
        var value = new HexBytes(bytes);
        string expected = Convert.ToHexStringLower(bytes);
        int hash = value.GetHashCode();

        Parallel.For(0, 200, _ =>
        {
            Assert.AreEqual("0x" + expected, value.ToString());
            Assert.AreEqual(hash, value.GetHashCode());
        });
    }
}
