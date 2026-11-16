using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class ByteUtils_HexNibbleAndValidationTests
{
    [TestMethod]
    [DataRow('0', 0)]
    [DataRow('9', 9)]
    [DataRow('a', 10)]
    [DataRow('f', 15)]
    [DataRow('A', 10)]
    [DataRow('F', 15)]
    [DataRow('g', -1)]
    [DataRow('G', -1)]
    [DataRow('/', -1)]
    public void ParseHexNibble_Chars(char c, int expected)
    {
        Assert.AreEqual(expected, ByteUtils.ParseHexNibble(c));
    }

    [TestMethod]
    [DataRow((byte)'0', 0)]
    [DataRow((byte)'9', 9)]
    [DataRow((byte)'a', 10)]
    [DataRow((byte)'f', 15)]
    [DataRow((byte)'A', 10)]
    [DataRow((byte)'F', 15)]
    [DataRow((byte)'g', -1)]
    [DataRow((byte)'/', -1)]
    public void ParseHexNibble_Utf8(byte b, int expected)
    {
        Assert.AreEqual(expected, ByteUtils.ParseHexNibbleUtf8(b));
    }

    [TestMethod]
    public void IsAllHexChars_TrueAndFalse()
    {
        Assert.IsTrue(ByteUtils.IsAllHexChars("deadBEEF".AsSpan()));
        Assert.IsFalse(ByteUtils.IsAllHexChars("deadZEEF".AsSpan()));
        Assert.IsTrue(ByteUtils.IsAllHexChars(ReadOnlySpan<char>.Empty));
    }

    [TestMethod]
    public void IsAllHexUtf8_MatchesTheNibbleParser_ForEveryByteValue()
    {
        // Covers the neighbours of the digit ranges ('/', ':', '@', 'G', '`', 'g') and every byte of 0x80 and above.
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            byte[] input = [(byte)'a', (byte)value, (byte)'1'];
            Assert.AreEqual(ByteUtils.ParseHexNibbleUtf8((byte)value) >= 0, ByteUtils.IsAllHexUtf8(input), $"byte 0x{value:x2}");
        }
    }

    [TestMethod]
    public void IsAllHexUtf8_TrueAndFalse()
    {
        Assert.IsTrue(ByteUtils.IsAllHexUtf8("deadBEEF"u8));
        Assert.IsFalse(ByteUtils.IsAllHexUtf8("deadZEEF"u8));
        Assert.IsTrue(ByteUtils.IsAllHexUtf8(ReadOnlySpan<byte>.Empty));
    }
}