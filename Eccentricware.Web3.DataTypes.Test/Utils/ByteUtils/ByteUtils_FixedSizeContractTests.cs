using EccentricWare.Web3.DataTypes.Utils;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Wrong-length contract of the fixed-size writers/decoders (exact exception, ParamName, message, destination untouched),
/// custom-alphabet behaviour, and exhaustive byte (Fixed2) round trips against Convert.ToHexString.
/// </summary>
[TestClass]
public sealed class ByteUtils_FixedSizeContractTests
{
    private const byte Sentinel = 0xAA;

    private delegate void Call(int length);

    // Name, call for a destination of the given length, expected ParamName, required length, valid length.
    private static readonly (string Name, Call Invoke, string ParamName, int Required)[] Contracts =
    [
        (nameof(ByteUtils.WriteHexUInt32CharsFixed8), n => ByteUtils.WriteHexUInt32CharsFixed8(new char[n], 1, false), "destination8", 8),
        (nameof(ByteUtils.WriteHexUInt32Utf8Fixed8), n => ByteUtils.WriteHexUInt32Utf8Fixed8(new byte[n], 1, false), "destination8", 8),
        (nameof(ByteUtils.WriteHexUInt64), n => ByteUtils.WriteHexUInt64(new char[n], 1, true), "destination", 16),
        (nameof(ByteUtils.WriteHexUInt64Utf8), n => ByteUtils.WriteHexUInt64Utf8(new byte[n], 1, ByteUtils.HexBytesLower), "destination", 16),
        (nameof(ByteUtils.WriteHexUInt64CharsFixed16), n => ByteUtils.WriteHexUInt64CharsFixed16(new char[n], 1, false), "destination16", 16),
        (nameof(ByteUtils.WriteHexUInt64Utf8Fixed16), n => ByteUtils.WriteHexUInt64Utf8Fixed16(new byte[n], 1, ByteUtils.HexBytesUpper), "destination16", 16),
        (nameof(ByteUtils.WriteHexByteCharsFixed2), n => ByteUtils.WriteHexByteCharsFixed2(new char[n], 1, false), "destination2", 2),
        (nameof(ByteUtils.WriteHexByteUtf8Fixed2), n => ByteUtils.WriteHexByteUtf8Fixed2(new byte[n], 1, ByteUtils.HexBytesLower), "destination2", 2),
        (nameof(ByteUtils.TryDecodeBase58To32), n => ByteUtils.TryDecodeBase58To32("2"u8, new byte[n]), "destination32", 32),
        (nameof(ByteUtils.TryDecodeBase58To64), n => ByteUtils.TryDecodeBase58To64("2"u8, new byte[n]), "destination64", 64),
        (nameof(ByteUtils.TryDecodeBase58To64Chars), n => ByteUtils.TryDecodeBase58To64Chars("2", new byte[n]), "destination64", 64),
    ];

    public static IEnumerable<object[]> WrongLengthCases()
    {
        foreach (var c in Contracts)
        {
            foreach (int delta in new[] { -1, 1 })
                yield return [c.Name, c.Required + delta];
            yield return [c.Name, 0];
            yield return [c.Name, c.Required * 4];
        }
    }

    [TestMethod]
    [DynamicData(nameof(WrongLengthCases))]
    public void WrongLengthDestination_ThrowsArgumentExceptionWithParamNameAndRequiredLength(string name, int length)
    {
        var contract = Contracts.Single(c => c.Name == name);

        var ex = Assert.ThrowsExactly<ArgumentException>(() => contract.Invoke(length));

        Assert.AreEqual(contract.ParamName, ex.ParamName, name);
        Assert.StartsWith($"Destination must be exactly {contract.Required} elements long.", ex.Message, name);
    }

    [TestMethod]
    public void EveryContractRow_AcceptsItsRequiredLength()
    {
        foreach (var c in Contracts)
            c.Invoke(c.Required);
    }

    [TestMethod]
    public void WrongLengthWriters_LeaveTheDestinationUntouched()
    {
        byte[] utf8 = Enumerable.Repeat(Sentinel, 17).ToArray();
        char[] chars = Enumerable.Repeat('#', 17).ToArray();

        Assert.ThrowsExactly<ArgumentException>(() => ByteUtils.WriteHexUInt64Utf8(utf8, ulong.MaxValue, ByteUtils.HexBytesLower));
        Assert.ThrowsExactly<ArgumentException>(() => ByteUtils.WriteHexUInt64Utf8Fixed16(utf8, ulong.MaxValue, ByteUtils.HexBytesLower));
        Assert.ThrowsExactly<ArgumentException>(() => ByteUtils.WriteHexUInt64(chars, ulong.MaxValue, false));
        Assert.ThrowsExactly<ArgumentException>(() => ByteUtils.WriteHexUInt64CharsFixed16(chars, ulong.MaxValue, false));

        Assert.IsTrue(utf8.All(b => b == Sentinel));
        Assert.IsTrue(chars.All(c => c == '#'));
    }

    // ---- alphabet parameter is honoured ----------------------------------------------------------------------------

    [TestMethod]
    public void Utf8Writers_UseTheSuppliedAlphabet()
    {
        ReadOnlySpan<byte> alphabet = "ABCDEFGHIJKLMNOP"u8; // nibble n -> 'A' + n

        byte[] sixteen = new byte[16];
        ByteUtils.WriteHexUInt64Utf8(sixteen, 0x0123456789ABCDEF, alphabet);
        Assert.AreEqual("ABCDEFGHIJKLMNOP", Encoding.ASCII.GetString(sixteen));

        byte[] fixed16 = new byte[16];
        ByteUtils.WriteHexUInt64Utf8Fixed16(fixed16, 0xFEDCBA9876543210, alphabet);
        Assert.AreEqual("PONMLKJIHGFEDCBA", Encoding.ASCII.GetString(fixed16));

        byte[] two = new byte[2];
        ByteUtils.WriteHexByteUtf8Fixed2(two, 0xA5, alphabet);
        Assert.AreEqual("KF", Encoding.ASCII.GetString(two)); // 0xA -> 'K', 0x5 -> 'F'
    }

    [TestMethod]
    public void Utf8Writers_WithLowerAndUpperAlphabet_MatchTheBclForBothCases()
    {
        foreach (ulong value in new[] { 0UL, 1UL, 0xABCDEFUL, 0x0123456789ABCDEFUL, ulong.MaxValue })
        {
            byte[] lower = new byte[16], upper = new byte[16], lowerFixed = new byte[16], upperFixed = new byte[16];
            ByteUtils.WriteHexUInt64Utf8(lower, value, ByteUtils.HexBytesLower);
            ByteUtils.WriteHexUInt64Utf8(upper, value, ByteUtils.HexBytesUpper);
            ByteUtils.WriteHexUInt64Utf8Fixed16(lowerFixed, value, ByteUtils.HexBytesLower);
            ByteUtils.WriteHexUInt64Utf8Fixed16(upperFixed, value, ByteUtils.HexBytesUpper);

            Assert.AreEqual(value.ToString("x16"), Encoding.ASCII.GetString(lower));
            Assert.AreEqual(value.ToString("X16"), Encoding.ASCII.GetString(upper));
            CollectionAssert.AreEqual(lower, lowerFixed);
            CollectionAssert.AreEqual(upper, upperFixed);
        }
    }

    // ---- Fixed2 byte helpers: all 256 values --------------------------------------------------------------------------

    [TestMethod]
    public void WriteHexByte_AllValues_MatchConvertToHexString_ForCharsAndUtf8_BothCases()
    {
        for (int i = 0; i < 256; i++)
        {
            byte value = (byte)i;
            string upper = Convert.ToHexString([value]);
            string lower = Convert.ToHexStringLower([value]);

            char[] chars = new char[2];
            ByteUtils.WriteHexByteCharsFixed2(chars, value, uppercase: true);
            Assert.AreEqual(upper, new string(chars));
            ByteUtils.WriteHexByteCharsFixed2(chars, value, uppercase: false);
            Assert.AreEqual(lower, new string(chars));

            byte[] utf8 = new byte[2];
            ByteUtils.WriteHexByteUtf8Fixed2(utf8, value, ByteUtils.HexBytesUpper);
            Assert.AreEqual(upper, Encoding.ASCII.GetString(utf8));
            ByteUtils.WriteHexByteUtf8Fixed2(utf8, value, ByteUtils.HexBytesLower);
            Assert.AreEqual(lower, Encoding.ASCII.GetString(utf8));
        }
    }

    [TestMethod]
    public void TryParseHexByte_AllValues_AcceptBothCases_ForCharsAndUtf8()
    {
        for (int i = 0; i < 256; i++)
        {
            foreach (string text in new[] { i.ToString("x2"), i.ToString("X2") })
            {
                Assert.IsTrue(ByteUtils.TryParseHexByteCharsFixed2(text, out byte fromChars), text);
                Assert.IsTrue(ByteUtils.TryParseHexByteUtf8Fixed2(Encoding.ASCII.GetBytes(text), out byte fromUtf8), text);
                Assert.AreEqual((byte)i, fromChars, text);
                Assert.AreEqual((byte)i, fromUtf8, text);
            }
        }
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("a")]
    [DataRow("abc")]
    [DataRow("0x")]
    [DataRow("g0")]
    [DataRow("0g")]
    [DataRow("gg")]
    [DataRow(" 1")]
    [DataRow("1 ")]
    [DataRow("-1")]
    public void TryParseHexByte_WrongLengthOrBadNibble_ReturnsFalseWithZeroValue(string text)
    {
        Assert.IsFalse(ByteUtils.TryParseHexByteCharsFixed2(text, out byte fromChars));
        Assert.IsFalse(ByteUtils.TryParseHexByteUtf8Fixed2(Encoding.ASCII.GetBytes(text), out byte fromUtf8));
        Assert.AreEqual(0, fromChars);
        Assert.AreEqual(0, fromUtf8);
    }

    [TestMethod]
    public void TryParseHexByte_NonAsciiCharactersAndBytes_AreRejected_InEitherPosition()
    {
        foreach (char bad in new[] { '\u00E9', '\uFF10', '\uFF21', '\u0130', '\u0661' })
        {
            Assert.IsFalse(ByteUtils.TryParseHexByteCharsFixed2($"{bad}0", out _), $"U+{(int)bad:X4} high");
            Assert.IsFalse(ByteUtils.TryParseHexByteCharsFixed2($"0{bad}", out _), $"U+{(int)bad:X4} low");
        }

        for (int b = 0x80; b < 256; b++)
        {
            Assert.IsFalse(ByteUtils.TryParseHexByteUtf8Fixed2([(byte)b, (byte)'0'], out _), $"0x{b:X2} high");
            Assert.IsFalse(ByteUtils.TryParseHexByteUtf8Fixed2([(byte)'0', (byte)b], out _), $"0x{b:X2} low");
        }
    }
}
