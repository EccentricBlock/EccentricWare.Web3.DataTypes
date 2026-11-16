using EccentricWare.Web3.DataTypes.Utils;
using System.Globalization;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Contracts of the fixed-width hex helpers: exact-length writers, strict parsers, BCL-identical digits.</summary>
[TestClass]
public sealed class ByteUtils_FixedWidthHexTests
{
    private static readonly uint[] Values32 = [0, 1, 0xF, 0x10, 0xA9059CBB, 0xDEADBEEF, 0x0A0B0C0D, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF];
    private static readonly ulong[] Values64 = [0, 1, 0xF, 0x10, 0xAFAF6D1F0D989BED, 0x0123456789ABCDEF, 0xFEDCBA9876543210, 0x8000000000000000, ulong.MaxValue];

    [TestMethod]
    public void Writers_ProduceBclIdenticalDigits_ForCharsAndUtf8()
    {
        foreach (uint v in Values32)
        {
            foreach (bool upper in new[] { false, true })
            {
                string expected = v.ToString(upper ? "X8" : "x8", CultureInfo.InvariantCulture);
                char[] chars = new char[8];
                byte[] utf8 = new byte[8];

                ByteUtils.WriteHexUInt32CharsFixed8(chars, v, upper);
                ByteUtils.WriteHexUInt32Utf8Fixed8(utf8, v, upper);

                Assert.AreEqual(expected, new string(chars));
                Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8));
            }
        }

        foreach (ulong v in Values64)
        {
            foreach (bool upper in new[] { false, true })
            {
                string expected = v.ToString(upper ? "X16" : "x16", CultureInfo.InvariantCulture);
                char[] a = new char[16];
                char[] b = new char[16];

                ByteUtils.WriteHexUInt64(a, v, upper);
                ByteUtils.WriteHexUInt64CharsFixed16(b, v, upper);

                Assert.AreEqual(expected, new string(a));
                Assert.AreEqual(expected, new string(b));
            }
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(7)]
    [DataRow(9)]
    [DataRow(16)]
    public void Writers32_RequireExactlyEightElements(int length)
    {
        Assert.ThrowsExactly<ArgumentException>(() => ByteUtils.WriteHexUInt32CharsFixed8(new char[length], 1, false));
        Assert.ThrowsExactly<ArgumentException>(() => ByteUtils.WriteHexUInt32Utf8Fixed8(new byte[length], 1, false));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(15)]
    [DataRow(17)]
    [DataRow(32)]
    public void Writers64_RequireExactlySixteenElements(int length)
    {
        Assert.ThrowsExactly<ArgumentException>(() => ByteUtils.WriteHexUInt64(new char[length], 1, false));
        Assert.ThrowsExactly<ArgumentException>(() => ByteUtils.WriteHexUInt64CharsFixed16(new char[length], 1, false));
    }

    [TestMethod]
    public void Parsers_RoundTripWriters_InBothCases()
    {
        foreach (uint v in Values32)
        {
            foreach (bool upper in new[] { false, true })
            {
                string text = v.ToString(upper ? "X8" : "x8", CultureInfo.InvariantCulture);

                Assert.IsTrue(ByteUtils.TryParseHexUInt32CharsFixed8(text, out uint fromChars));
                Assert.IsTrue(ByteUtils.TryParseHexUInt32Utf8Fixed8(Encoding.ASCII.GetBytes(text), out uint fromUtf8));
                Assert.AreEqual(v, fromChars);
                Assert.AreEqual(v, fromUtf8);
            }
        }

        foreach (ulong v in Values64)
        {
            foreach (bool upper in new[] { false, true })
            {
                string text = v.ToString(upper ? "X16" : "x16", CultureInfo.InvariantCulture);

                Assert.IsTrue(ByteUtils.TryParseHexUInt64CharsFixed16(text, out ulong fromChars));
                Assert.IsTrue(ByteUtils.TryParseHexUInt64Utf8Fixed16(Encoding.ASCII.GetBytes(text), out ulong fromUtf8));
                Assert.AreEqual(v, fromChars);
                Assert.AreEqual(v, fromUtf8);
            }
        }
    }

    [TestMethod]
    public void Parsers_WrongLength_ReturnFalseWithZeroValue()
    {
        foreach (int length in new[] { 0, 1, 7, 9, 15, 17, 32 })
        {
            string text = new('a', length);
            byte[] utf8 = Encoding.ASCII.GetBytes(text);

            if (length != 8)
            {
                Assert.IsFalse(ByteUtils.TryParseHexUInt32CharsFixed8(text, out uint v32));
                Assert.IsFalse(ByteUtils.TryParseHexUInt32Utf8Fixed8(utf8, out uint u32));
                Assert.AreEqual(0u, v32);
                Assert.AreEqual(0u, u32);
            }

            if (length != 16)
            {
                Assert.IsFalse(ByteUtils.TryParseHexUInt64CharsFixed16(text, out ulong v64));
                Assert.IsFalse(ByteUtils.TryParseHexUInt64Utf8Fixed16(utf8, out ulong u64));
                Assert.AreEqual(0UL, v64);
                Assert.AreEqual(0UL, u64);
            }
        }
    }

    [TestMethod]
    public void Parsers_NonHexCharacter_ReturnsFalseWithZeroValue_AtEveryPosition()
    {
        foreach (char bad in new[] { 'g', 'G', 'x', '/', ':', '@', '`', ' ', '\0', '°', 'Ā', 'İ', '٠', '０' })
        {
            for (int position = 0; position < 16; position++)
            {
                char[] sixteen = "0123456789abcdef".ToCharArray();
                sixteen[position] = bad;
                Assert.IsFalse(ByteUtils.TryParseHexUInt64CharsFixed16(sixteen, out ulong v64), $"16 chars pos={position} bad={(int)bad}");
                Assert.AreEqual(0UL, v64);

                if (position < 8)
                {
                    char[] eight = "01234567".ToCharArray();
                    eight[position] = bad;
                    Assert.IsFalse(ByteUtils.TryParseHexUInt32CharsFixed8(eight, out uint v32), $"8 chars pos={position} bad={(int)bad}");
                    Assert.AreEqual(0u, v32);
                }
            }
        }
    }
}
