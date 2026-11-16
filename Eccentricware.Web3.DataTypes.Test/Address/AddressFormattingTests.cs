using System.Globalization;
using System.Text;
using static EccentricWare.Web3.DataTypes.Tests.AddressTestHelpers;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>EVM format strings, Solana format handling, buffer boundaries and interface dispatch of <see cref="Address"/> formatting.</summary>
[TestClass]
public sealed class AddressFormattingTests
{
    // EIP-55 vector with a mixed-case checksum, so "c" is distinguishable from "0X".
    private const string Lower = "5aaeb6053f3e94c9b9a09f33669435e7ef1beaed";
    private const string Upper = "5AAEB6053F3E94C9B9A09F33669435E7EF1BEAED";
    private const string Checksum = "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed";

    private static readonly Address s_evm = Address.FromEvmBytes(Convert.FromHexString(Lower));
    private static readonly Address s_solana = Address.FromSolanaBytes(Sequence(32, i => (byte)i));
    private static readonly string s_solanaText = ReferenceEncode(Sequence(32, i => (byte)i));

    // ----------------- accepted formats -----------------

    // format, expected text, required length. The prefix is always lowercase "0x"; "0X"/"X" upper-case the digits only.
    [TestMethod]
    [DataRow("", "0x" + Lower, 42)]
    [DataRow("0x", "0x" + Lower, 42)]
    [DataRow("0X", "0x" + Upper, 42)]
    [DataRow("x", Lower, 40)]
    [DataRow("X", Upper, 40)]
    [DataRow("c", Checksum, 42)]
    [DataRow("C", Checksum, 42)]
    public void EvmFormat_ProducesTheExpectedText_ThroughEveryEntryPoint(string format, string expected, int required)
    {
        Assert.AreEqual(expected, s_evm.ToString(format, CultureInfo.InvariantCulture));
        Assert.AreEqual(expected, s_evm.ToString(format, null));
        Assert.AreEqual(required, expected.Length);

        Span<char> chars = new char[required];
        Assert.IsTrue(s_evm.TryFormat(chars, out int charsWritten, format));
        Assert.AreEqual(expected, new string(chars.Slice(0, charsWritten)));

        Span<byte> utf8 = new byte[required];
        Assert.IsTrue(s_evm.TryFormat(utf8, out int bytesWritten, format));
        Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8.Slice(0, bytesWritten)));
    }

    [TestMethod]
    public void ToString_NullFormat_And_EmptyFormat_AreTheDefault()
    {
        Assert.AreEqual("0x" + Lower, s_evm.ToString());
        Assert.AreEqual("0x" + Lower, s_evm.ToString(null, null));
        Assert.AreEqual("0x" + Lower, s_evm.ToString(string.Empty, null));
        Assert.AreEqual("0x" + Lower, s_evm.ToString(null, CultureInfo.GetCultureInfo("tr-TR")));
    }

    // ----------------- rejected formats -----------------

    [TestMethod]
    [DataRow("G")]
    [DataRow("g")]
    [DataRow("N")]
    [DataRow("z")]
    [DataRow("0")]
    [DataRow("xx")]
    [DataRow("XX")]
    [DataRow("cc")]
    [DataRow("Xx")]
    [DataRow("0c")]
    [DataRow("0y")]
    [DataRow("1x")]
    [DataRow("0xx")]
    [DataRow("0x0")]
    [DataRow(" x")]
    [DataRow("x ")]
    [DataRow("\0")]
    public void EvmFormat_Unknown_ThrowsFormatException_AndTryFormatFailsWithoutWriting(string format)
    {
        FormatException ex = Assert.ThrowsExactly<FormatException>(() => s_evm.ToString(format, CultureInfo.InvariantCulture));
        StringAssert.Contains(ex.Message, "Unknown format: \"" + format + "\"");

        char[] chars = Filled(64, '#');
        Assert.IsFalse(s_evm.TryFormat(chars, out int charsWritten, format));
        Assert.AreEqual(0, charsWritten);
        Assert.IsTrue(chars.All(c => c == '#'), "chars were written");

        byte[] utf8 = Filled(64, (byte)0xCC);
        Assert.IsFalse(s_evm.TryFormat(utf8, out int bytesWritten, format));
        Assert.AreEqual(0, bytesWritten);
        Assert.IsTrue(utf8.All(b => b == 0xCC), "bytes were written");
    }

    // ----------------- Solana ignores the format -----------------

    [TestMethod]
    [DataRow("")]
    [DataRow("x")]
    [DataRow("X")]
    [DataRow("c")]
    [DataRow("C")]
    [DataRow("0x")]
    [DataRow("0X")]
    [DataRow("z")]
    [DataRow("zzz")]
    public void SolanaFormat_IsIgnored_ByEveryEntryPoint(string format)
    {
        Assert.AreEqual(s_solanaText, s_solana.ToString());
        Assert.AreEqual(s_solanaText, s_solana.ToString(format, CultureInfo.InvariantCulture));

        Span<char> chars = new char[s_solanaText.Length];
        Assert.IsTrue(s_solana.TryFormat(chars, out int charsWritten, format));
        Assert.AreEqual(s_solanaText, new string(chars.Slice(0, charsWritten)));

        Span<byte> utf8 = new byte[s_solanaText.Length];
        Assert.IsTrue(s_solana.TryFormat(utf8, out int bytesWritten, format));
        Assert.AreEqual(s_solanaText, Encoding.ASCII.GetString(utf8.Slice(0, bytesWritten)));
    }

    // "c" formats Solana as Base58, while ToChecksumString() throws: the asymmetry is current behaviour, pinned here.
    [TestMethod]
    public void SolanaChecksumFormat_ReturnsBase58_WhileToChecksumStringThrows()
    {
        Assert.AreEqual(s_solanaText, s_solana.ToString("c", null));
        Assert.ThrowsExactly<InvalidOperationException>(() => s_solana.ToChecksumString());
    }

    [TestMethod]
    public void SolanaTryFormat_EmptyDestination_FailsWithoutWriting()
    {
        Assert.IsFalse(s_solana.TryFormat(Span<char>.Empty, out int charsWritten));
        Assert.AreEqual(0, charsWritten);
        Assert.IsFalse(s_solana.TryFormat(Span<byte>.Empty, out int bytesWritten));
        Assert.AreEqual(0, bytesWritten);
    }

    // ----------------- buffer boundaries -----------------

    // Required length 42 for default/"0x"/"0X"/"c"/"C" and 40 for "x"/"X": exact fit succeeds, one short and empty fail
    // and leave the buffer alone, and an oversized buffer keeps its tail.
    [TestMethod]
    [DataRow("", 42)]
    [DataRow("0x", 42)]
    [DataRow("0X", 42)]
    [DataRow("c", 42)]
    [DataRow("C", 42)]
    [DataRow("x", 40)]
    [DataRow("X", 40)]
    public void EvmTryFormat_BufferBoundaries_AreExact_ForBothEncodings(string format, int required)
    {
        string expected = s_evm.ToString(format, null);
        Assert.AreEqual(required, expected.Length);

        // chars
        char[] exact = Filled(required, '#');
        Assert.IsTrue(s_evm.TryFormat(exact, out int written, format));
        Assert.AreEqual(required, written);
        Assert.AreEqual(expected, new string(exact));

        foreach (int length in new[] { 0, 1, required - 1 })
        {
            char[] small = Filled(length, '#');
            Assert.IsFalse(s_evm.TryFormat(small, out written, format), $"chars length {length}");
            Assert.AreEqual(0, written);
            Assert.IsTrue(small.All(c => c == '#'), $"chars length {length}: written despite failure");
        }

        char[] large = Filled(required + 7, '#');
        Assert.IsTrue(s_evm.TryFormat(large, out written, format));
        Assert.AreEqual(required, written);
        Assert.AreEqual(expected, new string(large, 0, written));
        Assert.IsTrue(large.Skip(required).All(c => c == '#'), "chars tail overwritten");

        // UTF-8
        byte[] exactUtf8 = Filled(required, (byte)0xCC);
        Assert.IsTrue(s_evm.TryFormat(exactUtf8, out written, format));
        Assert.AreEqual(required, written);
        Assert.AreEqual(expected, Encoding.ASCII.GetString(exactUtf8));

        foreach (int length in new[] { 0, 1, required - 1 })
        {
            byte[] small = Filled(length, (byte)0xCC);
            Assert.IsFalse(s_evm.TryFormat(small, out written, format), $"utf8 length {length}");
            Assert.AreEqual(0, written);
            Assert.IsTrue(small.All(b => b == 0xCC), $"utf8 length {length}: written despite failure");
        }

        byte[] largeUtf8 = Filled(required + 7, (byte)0xCC);
        Assert.IsTrue(s_evm.TryFormat(largeUtf8, out written, format));
        Assert.AreEqual(required, written);
        Assert.AreEqual(expected, Encoding.ASCII.GetString(largeUtf8, 0, written));
        Assert.IsTrue(largeUtf8.Skip(required).All(b => b == 0xCC), "utf8 tail overwritten");
    }

    [TestMethod]
    public void TryFormat_OversizedDestination_ReportsExactLength_AndLeavesTheTailUntouched()
    {
        Address evm = Address.Parse(SampleEvmLower);
        Address sol = Address.Parse(SampleSolana);

        foreach (Address a in new[] { evm, sol })
        {
            string expected = a.ToString();

            char[] chars = Filled(100, '#');
            Assert.IsTrue(a.TryFormat(chars, out int c));
            Assert.AreEqual(expected.Length, c);
            Assert.AreEqual(expected, new string(chars, 0, c));
            Assert.IsTrue(chars.Skip(c).All(ch => ch == '#'));

            byte[] utf8 = Filled(100, (byte)0xCC);
            Assert.IsTrue(a.TryFormat(utf8, out int u));
            Assert.AreEqual(expected.Length, u);
            Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8, 0, u));
            Assert.IsTrue(utf8.Skip(u).All(b => b == 0xCC));
        }
    }

    // ----------------- consistency -----------------

    [TestMethod]
    public void Evm_Formats_AgreeAcrossCharsAndUtf8_ForRandomValues()
    {
        var random = new Random(55);
        byte[] bytes = new byte[Address.EvmByteLength];
        Span<char> chars = stackalloc char[Address.EvmHexLength + 2];
        Span<byte> utf8 = stackalloc byte[Address.EvmHexLength + 2];

        for (int i = 0; i < 2_000; i++)
        {
            random.NextBytes(bytes);
            Address a = Address.FromEvmBytes(bytes);
            string lower = Convert.ToHexStringLower(bytes);

            foreach (string format in new[] { "", "0x", "0X", "x", "X", "c" })
            {
                Assert.IsTrue(a.TryFormat(chars, out int c, format));
                Assert.IsTrue(a.TryFormat(utf8, out int u, format));
                Assert.AreEqual(new string(chars.Slice(0, c)), Encoding.ASCII.GetString(utf8.Slice(0, u)), $"format '{format}'");
            }

            Assert.AreEqual("0x" + lower, a.ToString());
            Assert.AreEqual("0x" + lower.ToUpperInvariant(), a.ToString("0X", null));
            Assert.AreEqual(lower.ToUpperInvariant(), a.ToString("X", CultureInfo.InvariantCulture));
            Assert.AreEqual(lower, a.ToString("x", CultureInfo.InvariantCulture));
            Assert.AreEqual(a, Address.Parse(a.ToChecksumString()));
            Assert.AreEqual(a, Address.Parse(lower));                  // unprefixed fallback
            Assert.AreEqual(a, Address.Parse(Utf8(lower)));            // unprefixed UTF-8
        }
    }

    // ----------------- interface dispatch -----------------

    [TestMethod]
    public void InterpolationAndStringFormat_UseTheFormatStrings()
    {
        Assert.AreEqual("0x" + Lower, $"{s_evm}");
        Assert.AreEqual(Lower, $"{s_evm:x}");
        Assert.AreEqual(Upper, $"{s_evm:X}");
        Assert.AreEqual("0x" + Upper, $"{s_evm:0X}");
        Assert.AreEqual(Checksum, $"{s_evm:c}");
        Assert.AreEqual(Checksum, string.Format("{0:C}", s_evm));
        Assert.AreEqual(s_solanaText, $"{s_solana}");
        Assert.AreEqual(s_solanaText, $"{s_solana:X}");
        Assert.ThrowsExactly<FormatException>(() => string.Format("{0:z}", s_evm));
    }

    [TestMethod]
    public void ISpanFormattable_AndIFormattable_DispatchThroughTheInterface()
    {
        Assert.AreEqual(Checksum, FormatGeneric(s_evm, "c"));
        Assert.AreEqual(s_solanaText, FormatGeneric(s_solana, "x"));

        Span<char> tooSmall = stackalloc char[41];
        Assert.IsFalse(TryFormatGeneric(s_evm, tooSmall, "c", out int written));
        Assert.AreEqual(0, written);

        Span<char> exact = stackalloc char[42];
        Assert.IsTrue(TryFormatGeneric(s_evm, exact, "c", out written));
        Assert.AreEqual(Checksum, new string(exact.Slice(0, written)));

        static string FormatGeneric<T>(T value, string format) where T : IFormattable => value.ToString(format, CultureInfo.InvariantCulture);
        static bool TryFormatGeneric<T>(T value, Span<char> destination, string format, out int charsWritten) where T : ISpanFormattable
            => value.TryFormat(destination, out charsWritten, format, CultureInfo.InvariantCulture);
    }

    [TestMethod]
    public void IUtf8SpanFormattable_IsUsedByUtf8TryWrite()
    {
        Span<byte> buffer = stackalloc byte[64];
        Address evm = s_evm;

        Assert.IsTrue(System.Text.Unicode.Utf8.TryWrite(buffer, $"{evm:x}", out int written));
        Assert.AreEqual(Lower, Encoding.ASCII.GetString(buffer.Slice(0, written)));

        Assert.IsTrue(System.Text.Unicode.Utf8.TryWrite(buffer, $"{evm}", out written));
        Assert.AreEqual("0x" + Lower, Encoding.ASCII.GetString(buffer.Slice(0, written)));

        Assert.IsTrue(System.Text.Unicode.Utf8.TryWrite(buffer, $"{s_solana}", out written));
        Assert.AreEqual(s_solanaText, Encoding.ASCII.GetString(buffer.Slice(0, written)));

        Span<byte> tooSmall = stackalloc byte[20];
        Assert.IsFalse(System.Text.Unicode.Utf8.TryWrite(tooSmall, $"{evm}", out _));
    }
}
