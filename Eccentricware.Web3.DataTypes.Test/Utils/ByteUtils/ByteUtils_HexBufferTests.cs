using EccentricWare.Web3.DataTypes.Utils;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>TryDecodeHex*, TryEncodeHex* and GetHexDecodedByteCount: matrices checked against Convert.ToHexString/FromHexString.</summary>
[TestClass]
public sealed class ByteUtils_HexBufferTests
{
    // Runs both the UTF-16 and the UTF-8 decoder so the two overloads are held to the same table.
    private static (bool Ok, int Written, byte[] Buffer) DecodeChars(string hex, int destinationSize, bool allowOdd)
    {
        byte[] dst = new byte[destinationSize];
        bool ok = ByteUtils.TryDecodeHexChars(hex, dst, out int written, allowOdd);
        return (ok, written, dst);
    }

    private static (bool Ok, int Written, byte[] Buffer) DecodeUtf8(string hex, int destinationSize, bool allowOdd)
    {
        byte[] dst = new byte[destinationSize];
        bool ok = ByteUtils.TryDecodeHexUtf8(Encoding.UTF8.GetBytes(hex), dst, out int written, allowOdd);
        return (ok, written, dst);
    }

    private static readonly Func<string, int, bool, (bool Ok, int Written, byte[] Buffer)>[] Decoders = [DecodeChars, DecodeUtf8];

    // ---- GetHexDecodedByteCount ------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow(0, false, 0)]
    [DataRow(0, true, 0)]
    [DataRow(1, true, 1)]
    [DataRow(1, false, -1)]
    [DataRow(2, false, 1)]
    [DataRow(2, true, 1)]
    [DataRow(3, true, 2)]
    [DataRow(3, false, -1)]
    [DataRow(64, false, 32)]
    [DataRow(65, true, 33)]
    [DataRow(-1, true, -1)]
    [DataRow(-1, false, -1)]
    [DataRow(int.MinValue, true, -1)]
    [DataRow(int.MinValue, false, -1)]
    [DataRow(int.MaxValue - 1, false, 1073741823)]
    public void GetHexDecodedByteCount_ReturnsCeilingHalfOrMinusOne(int digits, bool allowOdd, int expected)
    {
        Assert.AreEqual(expected, ByteUtils.GetHexDecodedByteCount(digits, allowOdd));
    }

    // ---- TryDecodeHexChars / TryDecodeHexUtf8 ------------------------------------------------------------------------

    [TestMethod]
    [DataRow("abc", true, "0ABC")]
    [DataRow("f", true, "0F")]
    [DataRow("aBcD", false, "ABCD")]
    [DataRow("ABCDEF", false, "ABCDEF")]
    [DataRow("abcdef", true, "ABCDEF")]
    [DataRow("0", true, "00")]
    [DataRow("00", false, "00")]
    [DataRow("123456789", true, "0123456789")]
    public void Decode_ValidInput_MatchesBclAndPadsOddLengthOnTheLeft(string hex, bool allowOdd, string expectedHex)
    {
        byte[] expected = Convert.FromHexString(expectedHex);
        foreach (var decode in Decoders)
        {
            var (ok, written, buffer) = decode(hex, expected.Length, allowOdd);

            Assert.IsTrue(ok, hex);
            Assert.AreEqual(expected.Length, written, hex);
            CollectionAssert.AreEqual(expected, buffer, hex);
        }
    }

    [TestMethod]
    [DataRow("abc", false)]
    [DataRow("a", false)]
    [DataRow("0x12", true)]
    [DataRow("0x12", false)]
    [DataRow("zz", true)]
    [DataRow("g12", true)]
    [DataRow("xyz", true)]
    [DataRow("12g", true)]
    [DataRow("ab zz", true)]
    [DataRow("abzzz", true)]
    [DataRow(" 12", true)]
    [DataRow("12 ", false)]
    [DataRow("+12", true)]
    [DataRow("-1", false)]
    public void Decode_InvalidInput_ReturnsFalseWithZeroBytesWritten(string hex, bool allowOdd)
    {
        // Destination content after a failure is unspecified (the odd-length first nibble may already be written), so only
        // the return value and bytesWritten are asserted.
        foreach (var decode in Decoders)
        {
            var (ok, written, _) = decode(hex, 64, allowOdd);

            Assert.IsFalse(ok, hex);
            Assert.AreEqual(0, written, hex);
        }
    }

    [TestMethod]
    public void Decode_FullwidthAndNonAsciiDigits_AreRejected()
    {
        Assert.IsFalse(DecodeChars("\uFF21\uFF21", 8, false).Ok, "fullwidth A");
        Assert.IsFalse(DecodeChars("\uFF11", 8, true).Ok, "fullwidth 1 as odd first digit");
        Assert.IsFalse(DecodeChars("1\u0661", 8, false).Ok, "Arabic-Indic digit");
        Assert.IsFalse(DecodeUtf8("\u00E9\u00E9", 8, false).Ok, "UTF-8 multibyte");
        Assert.IsFalse(ByteUtils.TryDecodeHexUtf8([0xC3, 0xA9], new byte[8], out _, false), "raw high bytes");
        Assert.IsFalse(ByteUtils.TryDecodeHexUtf8([(byte)'1', 0xFF, (byte)'2'], new byte[8], out _, true), "high byte in odd length");
    }

    [TestMethod]
    public void Decode_EmptyInput_SucceedsWithNothingWritten()
    {
        foreach (var decode in Decoders)
            foreach (bool allowOdd in new[] { false, true })
            {
                var (ok, written, _) = decode("", 0, allowOdd);
                Assert.IsTrue(ok);
                Assert.AreEqual(0, written);
            }
    }

    [TestMethod]
    [DataRow("abcd", 2, true)]
    [DataRow("abcd", 3, true)]
    [DataRow("abcd", 1, false)]
    [DataRow("abcd", 0, false)]
    [DataRow("abc", 2, true)]
    [DataRow("abc", 1, false)]
    public void Decode_DestinationSize_BoundaryIsExactBytesRequired(string hex, int destinationSize, bool expectedOk)
    {
        foreach (var decode in Decoders)
        {
            var (ok, written, _) = decode(hex, destinationSize, true);

            Assert.AreEqual(expectedOk, ok, hex);
            Assert.AreEqual(expectedOk ? (hex.Length + 1) / 2 : 0, written, hex);
        }
    }

    [TestMethod]
    public void Decode_LargeRandomPayload_RoundTripsAgainstBcl()
    {
        byte[] payload = new byte[1024];
        new Random(2024).NextBytes(payload);
        string upper = Convert.ToHexString(payload);

        foreach (var decode in Decoders)
            foreach (string hex in new[] { upper, upper.ToLowerInvariant() })
            {
                var (ok, written, buffer) = decode(hex, payload.Length, false);

                Assert.IsTrue(ok);
                Assert.AreEqual(payload.Length, written);
                CollectionAssert.AreEqual(payload, buffer);
            }
    }

    [TestMethod]
    public void Decode_OddLengthWithOddFlagOff_IsRejectedEvenWhenHexIsValid()
    {
        byte[] payload = new byte[33];
        new Random(7).NextBytes(payload);
        string oddHex = Convert.ToHexString(payload)[1..];

        foreach (var decode in Decoders)
        {
            Assert.IsFalse(decode(oddHex, 64, false).Ok);
            var (ok, written, buffer) = decode(oddHex, 64, true);
            Assert.IsTrue(ok);
            Assert.AreEqual(33, written);
            Assert.AreEqual(payload[0] & 0x0F, buffer[0]);
            CollectionAssert.AreEqual(payload[1..], buffer[1..33]);
        }
    }

    // ---- TryEncodeHexChars / TryEncodeHexUtf8 ------------------------------------------------------------------------

    [TestMethod]
    public void Encode_AllByteValues_MatchBcl_ForBothCasesAndBothOverloads()
    {
        byte[] all = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

        foreach (bool uppercase in new[] { false, true })
        {
            string expected = uppercase ? Convert.ToHexString(all) : Convert.ToHexStringLower(all);

            char[] chars = new char[512];
            Assert.IsTrue(ByteUtils.TryEncodeHexChars(all, chars, uppercase));
            Assert.AreEqual(expected, new string(chars));

            byte[] utf8 = new byte[512];
            Assert.IsTrue(ByteUtils.TryEncodeHexUtf8(all, utf8, uppercase));
            Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8));
        }
    }

    [TestMethod]
    public void Encode_EmptySource_SucceedsAndWritesNothing()
    {
        Assert.IsTrue(ByteUtils.TryEncodeHexChars([], [], true));
        Assert.IsTrue(ByteUtils.TryEncodeHexUtf8([], [], false));

        char[] chars = ['#'];
        Assert.IsTrue(ByteUtils.TryEncodeHexChars([], chars, true));
        Assert.AreEqual('#', chars[0]);
    }

    [TestMethod]
    public void Encode_DestinationLargerThanNeeded_LeavesTheTailUntouched()
    {
        char[] chars = "########".ToCharArray();
        byte[] utf8 = Enumerable.Repeat((byte)'#', 8).ToArray();

        Assert.IsTrue(ByteUtils.TryEncodeHexChars([0xAB], chars, true));
        Assert.IsTrue(ByteUtils.TryEncodeHexUtf8([0xAB], utf8, true));

        Assert.AreEqual("AB######", new string(chars));
        Assert.AreEqual("AB######", Encoding.ASCII.GetString(utf8));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(16)]
    [DataRow(1024)]
    public void Encode_DestinationOneShort_ReturnsFalseAndWritesNothing(int sourceLength)
    {
        byte[] source = Enumerable.Repeat((byte)0xCD, sourceLength).ToArray();
        char[] chars = Enumerable.Repeat('#', sourceLength * 2 - 1).ToArray();
        byte[] utf8 = Enumerable.Repeat((byte)'#', sourceLength * 2 - 1).ToArray();

        Assert.IsFalse(ByteUtils.TryEncodeHexChars(source, chars, false));
        Assert.IsFalse(ByteUtils.TryEncodeHexUtf8(source, utf8, false));

        Assert.IsTrue(chars.All(c => c == '#'));
        Assert.IsTrue(utf8.All(b => b == '#'));
    }

    [TestMethod]
    public void Encode_ThenDecode_RoundTripsRandomBuffers()
    {
        var random = new Random(31337);
        for (int length = 0; length <= 70; length++)
        {
            byte[] source = new byte[length];
            random.NextBytes(source);
            char[] chars = new char[length * 2];
            Assert.IsTrue(ByteUtils.TryEncodeHexChars(source, chars, length % 2 == 0));

            var (ok, written, buffer) = DecodeChars(new string(chars), length, false);

            Assert.IsTrue(ok);
            Assert.AreEqual(length, written);
            CollectionAssert.AreEqual(source, buffer);
        }
    }
}
