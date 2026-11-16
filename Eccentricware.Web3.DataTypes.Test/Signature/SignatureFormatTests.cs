using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Hex formatting of <see cref="Signature"/>: formats, case, the v byte, buffers and the formatting interfaces.</summary>
[TestClass]
public sealed class SignatureFormatTests
{
    private static readonly string[] Formats = ["", "x", "X", "0x", "0X"];

    private static string EvmHex => SignatureTestData.Ascending65Hex;
    private static string SolanaHex => SignatureTestData.Ascending64Hex;

    /// <summary>Expected text built from <c>Convert.ToHexString</c> only; the prefix stays lowercase "0x" even for "0X".</summary>
    private static string Expected(byte[] bytes, string format)
    {
        string upper = Convert.ToHexString(bytes);
        return format switch
        {
            "" or "0x" => "0x" + upper.ToLowerInvariant(),
            "x" => upper.ToLowerInvariant(),
            "X" => upper,
            "0X" => "0x" + upper,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    /// <summary>
    /// EVM signatures over rotated byte runs (together all 256 byte values appear in the payloads) with v bytes whose hex
    /// digits include letters, plus Solana signatures over the same payloads.
    /// </summary>
    private static List<(Signature Value, byte[] Bytes)> Cases()
    {
        var cases = new List<(Signature, byte[])>();
        for (int block = 0; block < 4; block++)
        {
            byte[] payload = new byte[64];
            for (int i = 0; i < 64; i++)
                payload[i] = (byte)(block * 64 + i);

            foreach (byte v in new byte[] { 0xAB, 0xFF, 0x1B, 0x0A, 0xA0, 0x00, 0x1C })
            {
                byte[] bytes = payload.Append(v).ToArray();
                cases.Add((Signature.FromEvmBytes(bytes), bytes));
            }

            cases.Add((Signature.FromSolanaBytes(payload), payload));
        }

        return cases;
    }

    private static bool TryFormatSpan<T>(T value, Span<char> destination, out int written, string format) where T : ISpanFormattable
        => value.TryFormat(destination, out written, format, null);

    private static bool TryFormatUtf8<T>(T value, Span<byte> destination, out int written, string format) where T : IUtf8SpanFormattable
        => value.TryFormat(destination, out written, format, null);

    // ----------------- ToString -----------------

    [TestMethod]
    public void ToString_DefaultIsPrefixedLowercaseHex_ForBothTypes()
    {
        Assert.AreEqual("0x" + EvmHex, SignatureTestData.SampleEvm.ToString());
        Assert.AreEqual("0x" + SolanaHex, SignatureTestData.SampleSolana.ToString());
        Assert.AreEqual(132, SignatureTestData.SampleEvm.ToString().Length);
        Assert.AreEqual(130, SignatureTestData.SampleSolana.ToString().Length);
    }

    [TestMethod]
    public void ToString_Formats_Evm()
    {
        Signature value = SignatureTestData.SampleEvm;
        Assert.AreEqual("0x" + EvmHex, value.ToString(""));
        Assert.AreEqual("0x" + EvmHex, value.ToString(null));
        Assert.AreEqual("0x" + EvmHex, value.ToString("0x"));
        Assert.AreEqual(EvmHex, value.ToString("x"));
        Assert.AreEqual(EvmHex.ToUpperInvariant(), value.ToString("X"));
        // Like every type in this library the prefix stays lowercase "0x" for "0X".
        Assert.AreEqual("0x" + EvmHex.ToUpperInvariant(), value.ToString("0X"));
        Assert.AreEqual($"0x{EvmHex}", $"{value}");
        Assert.AreEqual(EvmHex.ToUpperInvariant(), $"{value:X}");
    }

    [TestMethod]
    public void ToString_Formats_Solana_HaveNoVDigits()
    {
        Signature value = SignatureTestData.SampleSolana;
        Assert.AreEqual("0x" + SolanaHex, value.ToString(""));
        Assert.AreEqual("0x" + SolanaHex, value.ToString(null));
        Assert.AreEqual("0x" + SolanaHex, value.ToString("0x"));
        Assert.AreEqual(SolanaHex, value.ToString("x"));
        Assert.AreEqual(SolanaHex.ToUpperInvariant(), value.ToString("X"));
        Assert.AreEqual("0x" + SolanaHex.ToUpperInvariant(), value.ToString("0X"));
        Assert.AreEqual(128, value.ToString("x").Length);
        Assert.AreEqual(130, value.ToString("0X").Length);
    }

    [TestMethod]
    public void ToString_EveryFormat_MatchesTheIndependentHex_ForEveryByteValueAndLetterV()
    {
        foreach ((Signature value, byte[] bytes) in Cases())
        {
            foreach (string format in Formats)
                Assert.AreEqual(Expected(bytes, format), value.ToString(format), $"{value.Type} v={value.V:x2} '{format}'");

            Assert.AreEqual(Expected(bytes, ""), value.ToString());
            Assert.AreEqual(Expected(bytes, ""), $"{value}");
            Assert.AreEqual(Expected(bytes, "X"), $"{value:X}");
            Assert.AreEqual(Expected(bytes, "0X"), string.Format("{0:0X}", value));
        }
    }

    [TestMethod]
    [DataRow((byte)0x1b)]
    [DataRow((byte)0x1c)]
    [DataRow((byte)0xab)]
    public void VByte_IsFormattedInTheSameCaseAsThePayload(byte v)
    {
        Signature value = SignatureTestData.EvmFrom(SignatureTestData.Ascending64(), v);
        string lowerV = v.ToString("x2");

        StringAssert.EndsWith(value.ToString(), lowerV);
        StringAssert.EndsWith(value.ToString("x"), lowerV);
        StringAssert.EndsWith(value.ToString("X"), lowerV.ToUpperInvariant());
        StringAssert.EndsWith(value.ToString("0X"), lowerV.ToUpperInvariant());
        Assert.StartsWith("0x", value.ToString("0X"));
    }

    [TestMethod]
    public void ToString_ForEveryVValue_EndsWithTheTwoLowercaseDigitsOfV()
    {
        byte[] payload = SignatureTestData.Ascending64();
        for (int v = 0; v < 256; v++)
        {
            Signature value = SignatureTestData.EvmFrom(payload, (byte)v);
            string text = value.ToString("x");

            Assert.AreEqual(((byte)v).ToString("x2"), text[128..], $"v={v}");
            Assert.AreEqual(SignatureTestData.Ascending64Hex, text[..128]);
            Assert.AreEqual(((byte)v).ToString("X2"), value.ToString("X")[128..]);
        }
    }

    // ----------------- unknown formats -----------------

    [TestMethod]
    [DataRow("y")]
    [DataRow("0y")]
    [DataRow("xx")]
    [DataRow("x0")]
    [DataRow("0x0")]
    [DataRow("G")]
    [DataRow("X0")]
    [DataRow("0Xx")]
    [DataRow(" x")]
    [DataRow("x ")]
    [DataRow("00x")]
    [DataRow("0")]
    public void Formatting_UnknownFormat_ThrowsFormatException_ForEveryEntryPoint(string format)
    {
        foreach (Signature value in new[] { SignatureTestData.SampleEvm, SignatureTestData.SampleSolana })
        {
            Assert.ThrowsExactly<FormatException>(() => value.ToString(format));
            Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new char[200], out _, format));
            Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new byte[200], out _, format));

            // The format is validated before the destination size, so a too-small buffer does not turn it into 'false'.
            Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new char[1], out _, format));
            Assert.ThrowsExactly<FormatException>(() => value.TryFormat(new byte[1], out _, format));
        }
    }

    // ----------------- TryFormat -----------------

    [TestMethod]
    public void TryFormat_Chars_ExactSize_LargerBuffer_AndTooSmall_MatchTheIndependentHex()
    {
        foreach ((Signature value, byte[] bytes) in Cases())
        {
            foreach (string format in Formats)
            {
                string expected = Expected(bytes, format);
                string label = $"{value.Type} v={value.V:x2} '{format}'";

                char[] exact = new char[expected.Length];
                Assert.IsTrue(value.TryFormat(exact, out int written, format), label);
                Assert.AreEqual(expected.Length, written, label);
                Assert.AreEqual(expected, new string(exact), label);

                char[] larger = new char[expected.Length + 5];
                larger.AsSpan().Fill('#');
                Assert.IsTrue(value.TryFormat(larger, out written, format), label);
                Assert.AreEqual(expected.Length, written, label);
                Assert.AreEqual(expected, new string(larger, 0, written), label);
                Assert.IsTrue(larger.AsSpan(written).ToArray().All(c => c == '#'), "must not write past the result");

                char[] tooSmall = new char[expected.Length - 1];
                Assert.IsFalse(value.TryFormat(tooSmall, out written, format), label);
                Assert.AreEqual(0, written, label);
                Assert.IsTrue(tooSmall.All(c => c == '\0'), "must not write partial output");
            }
        }
    }

    [TestMethod]
    public void TryFormat_Utf8_ExactSize_LargerBuffer_AndTooSmall_MatchTheIndependentHex()
    {
        foreach ((Signature value, byte[] bytes) in Cases())
        {
            foreach (string format in Formats)
            {
                string expected = Expected(bytes, format);
                string label = $"{value.Type} v={value.V:x2} '{format}'";

                byte[] exact = new byte[expected.Length];
                Assert.IsTrue(value.TryFormat(exact, out int written, format), label);
                Assert.AreEqual(expected.Length, written, label);
                CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(expected), exact, label);

                byte[] larger = SignatureTestData.Filled(expected.Length + 5, 0xEE);
                Assert.IsTrue(value.TryFormat(larger, out written, format), label);
                Assert.AreEqual(expected.Length, written, label);
                CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(expected), larger.AsSpan(0, written).ToArray(), label);
                Assert.IsTrue(larger.AsSpan(written).ToArray().All(b => b == 0xEE), "must not write past the result");

                byte[] tooSmall = new byte[expected.Length - 1];
                Assert.IsFalse(value.TryFormat(tooSmall, out written, format), label);
                Assert.AreEqual(0, written, label);
                Assert.IsTrue(tooSmall.All(b => b == 0), "must not write partial output");
            }
        }
    }

    [TestMethod]
    public void TryFormat_EmptyDestination_ReturnsFalse_WithoutWriting()
    {
        Signature value = SignatureTestData.SampleEvm;
        Assert.IsFalse(value.TryFormat(Span<char>.Empty, out int chars));
        Assert.AreEqual(0, chars);
        Assert.IsFalse(value.TryFormat(Span<byte>.Empty, out int bytes));
        Assert.AreEqual(0, bytes);
    }

    // ----------------- the v byte through text -----------------

    [TestMethod]
    public void VByte_RoundTripsThroughText_ForAllTwoHundredFiftySixValues()
    {
        byte[] payload = SignatureTestData.Ascending64();
        for (int v = 0; v < 256; v++)
        {
            Signature original = SignatureTestData.EvmFrom(payload, (byte)v);
            string text = original.ToString();

            Signature fromChars = Signature.Parse(text.AsSpan());
            Assert.IsTrue(Signature.TryParse(SignatureTestData.Utf8(text), out Signature fromUtf8), $"v={v}");
            foreach (Signature parsed in new[] { fromChars, fromUtf8 })
            {
                Assert.AreEqual((byte)v, parsed.V, $"v={v}");
                Assert.AreEqual(SignatureType.Evm, parsed.Type);
                Assert.AreEqual(original, parsed, $"v={v}");
            }

            char[] chars = new char[132];
            byte[] utf8 = new byte[132];
            Assert.IsTrue(original.TryFormat(chars, out _));
            Assert.IsTrue(original.TryFormat(utf8, out _));
            Assert.AreEqual(text, new string(chars));
            Assert.AreEqual(text, Encoding.ASCII.GetString(utf8));
        }
    }

    [TestMethod]
    [DataRow("1b", (byte)27)]
    [DataRow("1B", (byte)27)]
    [DataRow("1c", (byte)28)]
    [DataRow("Fe", (byte)254)]
    [DataRow("fE", (byte)254)]
    [DataRow("aB", (byte)0xAB)]
    public void Parse_VDigitsInAnyCase_GiveTheSameByte(string vDigits, byte expectedV)
    {
        string text = "0x" + SolanaHex + vDigits;

        Assert.AreEqual(expectedV, Signature.Parse(text.AsSpan()).V);
        Assert.AreEqual(expectedV, Signature.Parse(SignatureTestData.Utf8(text)).V);
        Assert.AreEqual(SignatureTestData.EvmFrom(SignatureTestData.Ascending64(), expectedV), Signature.Parse(text.AsSpan()));
    }

    // ----------------- round trip -----------------

    [TestMethod]
    public void FormatThenParse_RoundTrips_ThroughEveryPath_ForRandomSignatures()
    {
        foreach (Signature value in SignatureTestData.RandomSignatures(300))
        {
            foreach (string format in new[] { "", "x", "X" })
            {
                string text = value.ToString(format);
                Assert.AreEqual(value, Signature.Parse(text.AsSpan()), text);
                Assert.IsTrue(Signature.TryParse(SignatureTestData.Utf8(text), out Signature utf8), text);
                Assert.AreEqual(value, utf8, text);
            }

            // Solana additionally round-trips through Base58, EVM does not (it is not 64 bytes).
            if (value.Type == SignatureType.Solana)
                Assert.AreEqual(value, Signature.Parse(SignatureTestData.Base58(value.ToBytes()).AsSpan()));
        }
    }

    // ----------------- formatting interfaces -----------------

    [TestMethod]
    public void ISpanFormattable_And_IUtf8SpanFormattable_DispatchToTheSameImplementation()
    {
        foreach ((Signature value, byte[] bytes) in Cases().Take(10))
        {
            foreach (string format in Formats)
            {
                string expected = Expected(bytes, format);

                char[] chars = new char[expected.Length];
                Assert.IsTrue(TryFormatSpan(value, chars, out int written, format));
                Assert.AreEqual(expected, new string(chars, 0, written));
                Assert.IsFalse(TryFormatSpan(value, new char[expected.Length - 1], out written, format));
                Assert.AreEqual(0, written);

                byte[] utf8 = new byte[expected.Length];
                Assert.IsTrue(TryFormatUtf8(value, utf8, out written, format));
                Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8, 0, written));
                Assert.IsFalse(TryFormatUtf8(value, new byte[expected.Length - 1], out written, format));
                Assert.AreEqual(0, written);
            }
        }
    }

    [TestMethod]
    public void Utf8TryWrite_AndFormattableToString_UseTheHexFormats_AndIgnoreTheProvider()
    {
        Signature value = SignatureTestData.EvmFrom(SignatureTestData.Ascending64(), 0x1b);
        byte[] bytes = SignatureTestData.Ascending65();
        bytes[64] = 0x1b;

        byte[] destination = new byte[200];
        Assert.IsTrue(Utf8.TryWrite(destination, $"{value:X}", out int written));
        Assert.AreEqual(Expected(bytes, "X"), Encoding.ASCII.GetString(destination, 0, written));

        Assert.IsFalse(Utf8.TryWrite(new byte[10], $"{value:X}", out _));

        // Culture-sensitive upper-casing (Turkish 'i') must not leak into hex output.
        CultureInfo turkish = CultureInfo.GetCultureInfo("tr-TR");
        Assert.AreEqual(Expected(bytes, "X"), ((IFormattable)value).ToString("X", turkish));
        Assert.AreEqual(Expected(bytes, "0X"), value.ToString("0X", turkish));
        Assert.AreEqual(Expected(bytes, ""), string.Format(turkish, "{0}", value));
        Assert.AreEqual(Expected(bytes, "X"), string.Format(turkish, "{0:X}", value));

        char[] chars = new char[132];
        Assert.IsTrue(value.TryFormat(chars, out written, "X", turkish));
        Assert.AreEqual(Expected(bytes, "X"), new string(chars, 0, written));
    }

    [TestMethod]
    public void EqualityComparer_AndComparerDefaults_UseTheSignatureImplementation()
    {
        Signature a = SignatureTestData.SampleEvm;
        Signature b = SignatureTestData.SampleEvm;
        Assert.IsTrue(EqualityComparer<Signature>.Default.Equals(a, b));
        Assert.AreEqual(EqualityComparer<Signature>.Default.GetHashCode(a), a.GetHashCode());
        Assert.IsFalse(EqualityComparer<Signature>.Default.Equals(a, SignatureTestData.SampleSolana));
        Assert.AreEqual(0, Comparer<Signature>.Default.Compare(a, b));
        Assert.IsLessThan(0, Comparer<Signature>.Default.Compare(a, SignatureTestData.SampleSolana));
    }
}
