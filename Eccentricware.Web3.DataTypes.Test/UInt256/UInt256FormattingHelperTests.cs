using EccentricWare.Web3.DataTypes.Utils;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Direct tests of the helpers around <c>UInt256Formatting</c>'s formatters: <c>CreateString</c>, scratch-size failures,
/// the inline-array scratch types and inner-chunk zero padding of the decimal formatter.
/// </summary>
[TestClass]
public sealed class UInt256FormattingHelperTests
{
    // ---- CreateString ----

    [TestMethod]
    public void CreateString_WidensAsciiBytes()
    {
        Assert.AreEqual("", UInt256Formatting.CreateString(ReadOnlySpan<byte>.Empty));
        Assert.AreEqual("0", UInt256Formatting.CreateString("0"u8));
        Assert.AreEqual("0xdeadBEEF", UInt256Formatting.CreateString("0xdeadBEEF"u8));

        string maxDecimal = (UInt256TestValues.Two256 - 1).ToString();
        Assert.AreEqual(maxDecimal, UInt256Formatting.CreateString(Encoding.ASCII.GetBytes(maxDecimal)));
    }

    [TestMethod]
    public void CreateString_AcceptsTheFullEightyByteScratch()
    {
        string eighty = new('7', 80);

        string result = UInt256Formatting.CreateString(Encoding.ASCII.GetBytes(eighty));

        Assert.AreEqual(eighty, result);
    }

    // ---- scratch sizes ----

    [TestMethod]
    public void ScratchTypes_HaveTheDocumentedSizes_AndCoverTheirUsers()
    {
        Assert.AreEqual(80, Unsafe.SizeOf<AsciiBuffer80>());
        Assert.AreEqual(32, Unsafe.SizeOf<Bytes32>());
        Assert.AreEqual(160, Unsafe.SizeOf<CharBuffer80>());   // 80 UTF-16 chars
        Assert.AreEqual(40, Unsafe.SizeOf<Chunks5>());         // 5 ulong chunks

        AsciiBuffer80 ascii = default;
        Span<byte> asciiSpan = ascii;
        Assert.IsGreaterThanOrEqualTo(UInt256Formatting.MaxDecimalLength, asciiSpan.Length);
        Assert.IsGreaterThanOrEqualTo(66, asciiSpan.Length);   // "0x" plus 64 hex digits

        Chunks5 chunks = default;
        Span<ulong> chunkSpan = chunks;
        // 2^256 - 1 has 78 decimal digits: four full 19-digit chunks plus a 2-digit most significant chunk.
        Assert.AreEqual(5, chunkSpan.Length);
        Assert.AreEqual(4 * 19 + 2, (UInt256TestValues.Two256 - 1).ToString().Length);
    }

    [TestMethod]
    public void FormatHex_ScratchShorterThan66_Throws()
    {
        foreach (int length in new[] { 0, 1, 65 })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => UInt256Formatting.FormatHex(new byte[length], 1, 0, 0, 0, prefix: false, uppercase: false, fullWidth: false), $"scratch {length}");

        Assert.AreEqual("1", Encoding.ASCII.GetString(UInt256Formatting.FormatHex(new byte[66], 1, 0, 0, 0, prefix: false, uppercase: false, fullWidth: false)));
    }

    [TestMethod]
    public void FormatDecimal_ScratchTooShortForTheValue_Throws_AndSmallestLegalBuffersWork()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => UInt256Formatting.FormatDecimal(new byte[77], ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, negative: false));

        // The smallest legal scratch is the digit count (plus one for a sign).
        Assert.AreEqual("0", Encoding.ASCII.GetString(UInt256Formatting.FormatDecimal(new byte[1], 0, 0, 0, 0, negative: false)));
        Assert.AreEqual("-7", Encoding.ASCII.GetString(UInt256Formatting.FormatDecimal(new byte[2], 7, 0, 0, 0, negative: true)));
        Assert.AreEqual(78,
            UInt256Formatting.FormatDecimal(new byte[78], ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, negative: false).Length);
    }

    // ---- inner chunk padding ----

    [TestMethod]
    public void FormatDecimal_InnerChunksArePaddedToNineteenDigits()
    {
        BigInteger t19 = BigInteger.Pow(10, 19);
        List<BigInteger> values =
        [
            t19, 9 * t19, t19 + 1, BigInteger.Pow(10, 38) + 1, BigInteger.Pow(10, 57) + 1, BigInteger.Pow(10, 76) + 1,
            (t19 - 1) * t19, 7 * BigInteger.Pow(10, 40) + 3,
        ];
        foreach (int digits in new[] { 19, 20, 38, 39, 57, 58, 76, 77 })
            values.Add(BigInteger.Pow(10, digits) - 1);   // all nines

        foreach (BigInteger value in values)
        {
            uint256 u = UInt256TestValues.FromBig(value);
            byte[] scratch = new byte[80];

            ReadOnlySpan<byte> result = UInt256Formatting.FormatDecimal(scratch, u.U0, u.U1, u.U2, u.U3, negative: false);

            Assert.AreEqual(value.ToString(), Encoding.ASCII.GetString(result), value.ToString());
        }
    }
}
