using System.Globalization;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class InstructionDiscriminatorTests
{
    private const ulong Sample = 0x0102030405060708UL;

    [TestMethod]
    [DataRow("", "0x0102030405060708")]
    [DataRow("0x", "0x0102030405060708")]
    [DataRow("0X", "0x0102030405060708")]
    [DataRow("x", "0102030405060708")]
    [DataRow("X", "0102030405060708")]
    public void TryFormat_WritesExpected_ForExactAndOversizedDestinations(string format, string expected)
    {
        var value = new InstructionDiscriminator(Sample);

        // Oversized buffers must work: callers routinely pass a larger scratch buffer.
        foreach (int size in new[] { expected.Length, expected.Length + 1, 64 })
        {
            char[] chars = new char[size];
            byte[] utf8 = new byte[size];

            Assert.IsTrue(value.TryFormat(chars, out int c, format), $"chars size={size}");
            Assert.IsTrue(value.TryFormat(utf8, out int b, format), $"utf8 size={size}");
            Assert.AreEqual(expected, new string(chars, 0, c));
            Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8, 0, b));
        }
    }

    [TestMethod]
    public void TryFormat_UppercaseDigits()
    {
        var value = new InstructionDiscriminator(0xAABBCCDDEEFF0011UL);
        char[] chars = new char[32];
        byte[] utf8 = new byte[32];

        Assert.IsTrue(value.TryFormat(chars, out int c, "X"));
        Assert.IsTrue(value.TryFormat(utf8, out int b, "X"));
        Assert.AreEqual("AABBCCDDEEFF0011", new string(chars, 0, c));
        Assert.AreEqual("AABBCCDDEEFF0011", Encoding.ASCII.GetString(utf8, 0, b));
    }

    [TestMethod]
    public void TryFormat_DestinationTooSmall_ReturnsFalse()
    {
        var value = new InstructionDiscriminator(Sample);

        Assert.IsFalse(value.TryFormat(new char[17], out int c));
        Assert.IsFalse(value.TryFormat(new byte[17], out int b));
        Assert.IsFalse(value.TryFormat(new char[15], out _, "x"));
        Assert.AreEqual(0, c);
        Assert.AreEqual(0, b);
    }

    [TestMethod]
    public void ToString_And_Parse_RoundTrip()
    {
        var value = new InstructionDiscriminator(Sample);

        Assert.AreEqual("0x0102030405060708", value.ToString());
        Assert.AreEqual(value, InstructionDiscriminator.Parse(value.ToString()));
        Assert.AreEqual(value, InstructionDiscriminator.Parse(value.ToString("x", CultureInfo.InvariantCulture)));
    }

    // ----------------- hex encode / decode against BCL reference -----------------

    private static ulong[] SampleValues()
    {
        var rng = new Random(2025);
        var values = new List<ulong>
        {
            0, 1, 0xF, 0x10, 0xFF, 0xFFFFFFFF, 0x100000000, 0x0FFFFFFFFFFFFFFF, 0x1000000000000000,
            0x7FFFFFFFFFFFFFFF, 0x8000000000000000, ulong.MaxValue, 0xAFAF6D1F0D989BED, 0x0123456789ABCDEF, 0xFEDCBA9876543210,
        };
        for (int i = 0; i < 2_000; i++)
            values.Add((ulong)rng.NextInt64() ^ ((ulong)rng.NextInt64() << 1));
        return [.. values];
    }

    [TestMethod]
    public void Format_AllFormats_MatchBclHexFormatting_ForCharsAndUtf8()
    {
        foreach (ulong v in SampleValues())
        {
            var value = new InstructionDiscriminator(v);
            string lower = v.ToString("x16", CultureInfo.InvariantCulture);
            string upper = v.ToString("X16", CultureInfo.InvariantCulture);

            foreach ((string format, string expected) in new[]
            {
                ("", "0x" + lower), ("0x", "0x" + lower), ("0X", "0x" + upper), ("x", lower), ("X", upper),
            })
            {
                Assert.AreEqual(expected, value.ToString(format, CultureInfo.InvariantCulture), $"{v:x16} '{format}'");

                char[] chars = new char[32];
                byte[] utf8 = new byte[32];
                Assert.IsTrue(value.TryFormat(chars, out int c, format));
                Assert.IsTrue(value.TryFormat(utf8, out int b, format));
                Assert.AreEqual(expected, new string(chars, 0, c), $"chars {v:x16} '{format}'");
                Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8, 0, b), $"utf8 {v:x16} '{format}'");
            }
        }
    }

    [TestMethod]
    public void Format_DoesNotTouchBytesBeyondTheOutput()
    {
        var value = new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL);
        char[] chars = new char[24];
        byte[] utf8 = new byte[24];
        chars.AsSpan().Fill('#');
        utf8.AsSpan().Fill((byte)'#');

        Assert.IsTrue(value.TryFormat(chars, out int c, "x"));
        Assert.IsTrue(value.TryFormat(utf8, out int b, "x"));

        Assert.AreEqual(16, c);
        Assert.AreEqual(16, b);
        for (int i = 16; i < 24; i++)
        {
            Assert.AreEqual('#', chars[i], $"chars[{i}]");
            Assert.AreEqual((byte)'#', utf8[i], $"utf8[{i}]");
        }
    }

    [TestMethod]
    public void Parse_RoundTripsEveryFormat_ForCharsAndUtf8()
    {
        foreach (ulong v in SampleValues())
        {
            var value = new InstructionDiscriminator(v);
            foreach (string format in new[] { "", "0x", "0X", "x", "X" })
            {
                string text = value.ToString(format, CultureInfo.InvariantCulture);

                Assert.IsTrue(InstructionDiscriminator.TryParse(text.AsSpan(), out var fromChars), text);
                Assert.IsTrue(InstructionDiscriminator.TryParse(Encoding.ASCII.GetBytes(text), out var fromUtf8), text);
                Assert.AreEqual(value, fromChars, text);
                Assert.AreEqual(value, fromUtf8, text);
            }
        }
    }

    [TestMethod]
    public void Parse_AcceptsMixedCase_PrefixAndAsciiWhitespace()
    {
        var expected = new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL);

        Assert.AreEqual(expected, InstructionDiscriminator.Parse("AfAf6D1f0d989BeD"));
        Assert.AreEqual(expected, InstructionDiscriminator.Parse("0XAfAf6D1f0d989BeD"));
        Assert.AreEqual(expected, InstructionDiscriminator.Parse("  \t0xafaf6d1f0d989bed\r\n"));
        Assert.AreEqual(expected, InstructionDiscriminator.Parse(Encoding.ASCII.GetBytes(" 0xafaf6d1f0d989bed ")));
    }

    [TestMethod]
    public void Parse_InvalidCharacterAtAnyPosition_IsRejected()
    {
        const string valid = "afaf6d1f0d989bed";

        for (int position = 0; position < valid.Length; position++)
        {
            foreach (char bad in new[] { 'g', 'G', 'x', '/', ':', '@', '`', ' ', '\0', '\u00B0', '\u0100', '\u0130', '\u0660', '\uFF10' })
            {
                string text = valid.Substring(0, position) + bad + valid.Substring(position + 1);

                Assert.IsFalse(InstructionDiscriminator.TryParse(text.AsSpan(), out var parsed), $"chars pos={position} bad={(int)bad}");
                Assert.AreEqual(InstructionDiscriminator.Zero, parsed);

                if (bad < 0x80)
                    Assert.IsFalse(InstructionDiscriminator.TryParse(Encoding.ASCII.GetBytes(text), out _), $"utf8 pos={position} bad={(int)bad}");
            }

            foreach (byte bad in new byte[] { 0x80, 0xA0, 0xC3, 0xFF })
            {
                byte[] utf8 = Encoding.ASCII.GetBytes(valid);
                utf8[position] = bad;
                Assert.IsFalse(InstructionDiscriminator.TryParse(utf8, out _), $"utf8 pos={position} byte={bad:x2}");
            }
        }
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("0x")]
    [DataRow("afaf6d1f0d989be")]        // 15 digits
    [DataRow("afaf6d1f0d989bedd")]      // 17 digits
    [DataRow("0x0xafaf6d1f0d989bed")]
    [DataRow("afaf6d1f 0d989bed")]
    public void Parse_WrongLengthOrShape_IsRejected(string text)
    {
        Assert.IsFalse(InstructionDiscriminator.TryParse(text.AsSpan(), out _));
        Assert.IsFalse(InstructionDiscriminator.TryParse(Encoding.ASCII.GetBytes(text), out _));
        Assert.ThrowsExactly<FormatException>(() => InstructionDiscriminator.Parse(text));
    }

    [TestMethod]
    public void Parse_NullString_ThrowsArgumentNullException_AndTryParseReturnsFalse()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => InstructionDiscriminator.Parse((string)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => InstructionDiscriminator.Parse((string)null!, CultureInfo.InvariantCulture));
        Assert.IsFalse(InstructionDiscriminator.TryParse((string?)null, out var parsed));
        Assert.AreEqual(InstructionDiscriminator.Zero, parsed);
    }

    [TestMethod]
    public void Format_UnknownFormat_ThrowsFormatException()
    {
        var value = new InstructionDiscriminator(Sample);

        foreach (string format in new[] { "G", "d", "0", "xx", "0y", "x8" })
        {
            Assert.ThrowsExactly<FormatException>(() => value.ToString(format), format);
            Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new char[32], out _, format), format);
            Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new byte[32], out _, format), format);
        }
    }

    // ----------------- bytes -----------------

    [TestMethod]
    public void Bytes_AreBigEndian_AndRoundTrip()
    {
        byte[] bytes = [0xAF, 0xAF, 0x6D, 0x1F, 0x0D, 0x98, 0x9B, 0xED, 0xFF, 0xFF];
        var value = new InstructionDiscriminator(bytes); // first 8 bytes; the tail is ignored

        Assert.AreEqual(0xAFAF6D1F0D989BEDUL, value.Value);

        byte[] written = new byte[8];
        value.WriteBytes(written);
        CollectionAssert.AreEqual(bytes[..8], written);

        Assert.ThrowsExactly<ArgumentException>(() => value.WriteBytes(new byte[7]));
        Assert.ThrowsExactly<ArgumentException>(() => _ = new InstructionDiscriminator(new byte[7]));
    }

    [TestMethod]
    public void FromInstructionData_ExtractsFirst8Bytes_AndRejectsShortData()
    {
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

        Assert.AreEqual(0x0102030405060708UL, InstructionDiscriminator.FromInstructionData(data).Value);
        Assert.IsTrue(InstructionDiscriminator.TryFromInstructionData(data, out var d));
        Assert.AreEqual(0x0102030405060708UL, d.Value);

        Assert.IsFalse(InstructionDiscriminator.TryFromInstructionData(data.AsSpan(0, 7), out d));
        Assert.AreEqual(InstructionDiscriminator.Zero, d);
        Assert.ThrowsExactly<ArgumentException>(() => InstructionDiscriminator.FromInstructionData(data.AsSpan(0, 7)));
    }

    // ----------------- equality / ordering -----------------

    [TestMethod]
    public void Equality_Ordering_AndHashing()
    {
        var a = new InstructionDiscriminator(1);
        var b = new InstructionDiscriminator(2);
        var big = new InstructionDiscriminator(0x8000000000000000UL);

        Assert.IsTrue(a == new InstructionDiscriminator(1));
        Assert.IsTrue(a != b);
        var aCopy = new InstructionDiscriminator(1);
        Assert.IsTrue(a < b && b > a && a <= aCopy && b >= new InstructionDiscriminator(2));
        Assert.IsTrue(b < big, "ordering is unsigned");
        Assert.AreEqual(-1, a.CompareTo(b));
        Assert.AreEqual(1, b.CompareTo(a));
        Assert.AreEqual(0, a.CompareTo(a));
        Assert.AreEqual(1, a.CompareTo((object?)null));
        Assert.ThrowsExactly<ArgumentException>(() => a.CompareTo("nope"));
        Assert.AreEqual(a.GetHashCode(), new InstructionDiscriminator(1).GetHashCode());
        Assert.IsTrue(a.Equals((object)new InstructionDiscriminator(1)));
        Assert.IsFalse(a.Equals("nope"));
    }

    [TestMethod]
    public void Ordering_MatchesHexStringOrdering()
    {
        var rng = new Random(9);
        var values = Enumerable.Range(0, 500).Select(_ => new InstructionDiscriminator((ulong)rng.NextInt64() * 2 + (ulong)rng.Next(2))).ToArray();

        string[] byValue = values.OrderBy(v => v).Select(v => v.ToString("x", CultureInfo.InvariantCulture)).ToArray();
        string[] byText = values.Select(v => v.ToString("x", CultureInfo.InvariantCulture)).OrderBy(s => s, StringComparer.Ordinal).ToArray();

        CollectionAssert.AreEqual(byText, byValue);
    }
}
