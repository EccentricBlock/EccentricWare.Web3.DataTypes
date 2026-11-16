using System.Globalization;
using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class FunctionSelector_AdditionalTests
{
    // ----------------- hex encode / decode against BCL reference -----------------

    private static uint[] SampleValues()
    {
        var rng = new Random(2024);
        var values = new List<uint> { 0, 1, 0xF, 0x10, 0xFF, 0x100, 0x0FFFFFFF, 0x10000000, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF, 0xa9059cbb, 0x095ea7b3, 0x0a0b0c0d, 0xdeadbeef, 0xC0FFEE00 };
        for (int i = 0; i < 2_000; i++)
            values.Add((uint)rng.NextInt64(0, uint.MaxValue + 1L));
        return [.. values];
    }

    [TestMethod]
    public void Format_AllFormats_MatchBclHexFormatting_ForCharsAndUtf8()
    {
        foreach (uint v in SampleValues())
        {
            var selector = new FunctionSelector(v);
            string lower = v.ToString("x8", CultureInfo.InvariantCulture);
            string upper = v.ToString("X8", CultureInfo.InvariantCulture);

            foreach ((string format, string expected) in new[]
            {
                ("", "0x" + lower), ("0x", "0x" + lower), ("0X", "0x" + upper), ("x", lower), ("X", upper),
            })
            {
                Assert.AreEqual(expected, selector.ToString(format, CultureInfo.InvariantCulture), $"{v:x8} '{format}'");

                char[] chars = new char[20];
                byte[] utf8 = new byte[20];
                Assert.IsTrue(selector.TryFormat(chars, out int c, format));
                Assert.IsTrue(selector.TryFormat(utf8, out int b, format));
                Assert.AreEqual(expected, new string(chars, 0, c), $"chars {v:x8} '{format}'");
                Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8, 0, b), $"utf8 {v:x8} '{format}'");
            }
        }
    }

    [TestMethod]
    public void Format_DoesNotTouchBytesBeyondTheOutput()
    {
        var selector = new FunctionSelector(0xa9059cbb);
        char[] chars = new char[16];
        byte[] utf8 = new byte[16];
        chars.AsSpan().Fill('#');
        utf8.AsSpan().Fill((byte)'#');

        Assert.IsTrue(selector.TryFormat(chars, out int c, "x"));
        Assert.IsTrue(selector.TryFormat(utf8, out int b, "x"));

        Assert.AreEqual(8, c);
        Assert.AreEqual(8, b);
        for (int i = 8; i < 16; i++)
        {
            Assert.AreEqual('#', chars[i], $"chars[{i}]");
            Assert.AreEqual((byte)'#', utf8[i], $"utf8[{i}]");
        }
    }

    [TestMethod]
    public void Parse_RoundTripsEveryFormat_ForCharsAndUtf8()
    {
        foreach (uint v in SampleValues())
        {
            var selector = new FunctionSelector(v);
            foreach (string format in new[] { "", "0x", "0X", "x", "X" })
            {
                string text = selector.ToString(format, CultureInfo.InvariantCulture);

                Assert.IsTrue(FunctionSelector.TryParse(text.AsSpan(), out var fromChars), text);
                Assert.IsTrue(FunctionSelector.TryParse(Encoding.ASCII.GetBytes(text), out var fromUtf8), text);
                Assert.AreEqual(selector, fromChars, text);
                Assert.AreEqual(selector, fromUtf8, text);
            }
        }
    }

    [TestMethod]
    public void Parse_MixedCaseDigits_Work()
    {
        Assert.AreEqual(0xdeadbeefu, FunctionSelector.Parse("0xDeAdBeEf").Value);
        Assert.AreEqual(0xdeadbeefu, FunctionSelector.Parse("dEaDbEeF").Value);
        Assert.AreEqual(0xdeadbeefu, FunctionSelector.Parse(Encoding.ASCII.GetBytes("0XdEaDbEeF")).Value);
    }

    [TestMethod]
    public void Parse_InvalidCharacterAtAnyPosition_IsRejected()
    {
        const string valid = "a9059cbb";

        for (int position = 0; position < valid.Length; position++)
        {
            foreach (char bad in new[] { 'g', 'G', 'x', '/', ':', '@', '`', ' ', '\0', '\u007F', '\u00B0', '\u0100', '\u0130', '\u0660', '\uFF10', '\uFF21' })
            {
                string text = valid.Substring(0, position) + bad + valid.Substring(position + 1);

                Assert.IsFalse(FunctionSelector.TryParse(text.AsSpan(), out var parsed), $"chars pos={position} bad={(int)bad}");
                Assert.AreEqual(FunctionSelector.Zero, parsed);

                if (bad < 0x80)
                    Assert.IsFalse(FunctionSelector.TryParse(Encoding.ASCII.GetBytes(text), out _), $"utf8 pos={position} bad={(int)bad}");
            }

            // Bytes >= 0x80 are never valid hex in UTF-8 either (including the 0xA0 of an NBSP and a UTF-8 lead byte).
            foreach (byte bad in new byte[] { 0x80, 0xA0, 0xB0, 0xC3, 0xFF })
            {
                byte[] utf8 = Encoding.ASCII.GetBytes(valid);
                utf8[position] = bad;
                Assert.IsFalse(FunctionSelector.TryParse(utf8, out _), $"utf8 pos={position} byte={bad:x2}");
            }
        }
    }

    // ----------------- string parsing / null handling -----------------

    [TestMethod]
    public void Parse_NullString_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => FunctionSelector.Parse((string)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => FunctionSelector.Parse((string)null!, CultureInfo.InvariantCulture));
    }

    // ----------------- ContainsSimd -----------------

}
