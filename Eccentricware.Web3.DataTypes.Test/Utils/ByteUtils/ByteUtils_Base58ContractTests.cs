using EccentricWare.Web3.DataTypes.Utils;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Canonical 256-bit Base58 decode matrix, the reverse map, leading-zero counting and the 32-byte non-canonical decoder.
/// Expected encodings come from a BigInteger-based reference encoder defined here, not from the library.
/// </summary>
[TestClass]
public sealed class ByteUtils_Base58ContractTests
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>Reference Bitcoin-style encoder: one '1' per leading zero byte, then the value in base 58.</summary>
    private static string EncodeReference(byte[] bigEndian)
    {
        int leadingZeros = bigEndian.TakeWhile(b => b == 0).Count();
        BigInteger value = new(bigEndian, isUnsigned: true, isBigEndian: true);
        var digits = new StringBuilder();
        while (value > 0)
        {
            digits.Insert(0, Alphabet[(int)(value % 58)]);
            value /= 58;
        }

        return new string('1', leadingZeros) + digits;
    }

    private static ulong[] LimbsMsbFirst(byte[] bigEndian32) =>
    [
        BinaryPrimitives.ReadUInt64BigEndian(bigEndian32.AsSpan(0)),
        BinaryPrimitives.ReadUInt64BigEndian(bigEndian32.AsSpan(8)),
        BinaryPrimitives.ReadUInt64BigEndian(bigEndian32.AsSpan(16)),
        BinaryPrimitives.ReadUInt64BigEndian(bigEndian32.AsSpan(24)),
    ];

    private static bool Decode(string text, out ulong[] limbs) => Decode(Encoding.ASCII.GetBytes(text), out limbs);

    private static bool Decode(byte[] utf8, out ulong[] limbs)
    {
        bool ok = ByteUtils.TryDecodeBase58ToUInt256BigEndian(utf8, out ulong a, out ulong b, out ulong c, out ulong d);
        limbs = [a, b, c, d];
        return ok;
    }

    // ---- Base58ReverseMap ----------------------------------------------------------------------------------------------

    [TestMethod]
    public void Base58ReverseMap_MapsExactlyTheFiftyEightAlphabetBytesToTheirIndex()
    {
        ReadOnlySpan<byte> map = ByteUtils.Base58ReverseMap;
        Assert.AreEqual(256, map.Length);

        for (int b = 0; b < 256; b++)
        {
            int index = b < 128 ? Alphabet.IndexOf((char)b) : -1;
            Assert.AreEqual(index < 0 ? 0xFF : index, map[b], $"byte 0x{b:X2}");
        }
    }

    [TestMethod]
    public void Base58ReverseMap_ExcludesTheAmbiguousCharacters()
    {
        foreach (char excluded in "0OIl")
            Assert.AreEqual(0xFF, ByteUtils.Base58ReverseMap[excluded], excluded.ToString());
        Assert.AreEqual(58, ByteUtils.Base58ReverseMap.ToArray().Count(b => b != 0xFF));
    }

    // ---- CountLeadingZeroBytes256BigEndian -----------------------------------------------------------------------------

    [TestMethod]
    [DataRow(0x01)]
    [DataRow(0x0F)]
    [DataRow(0x10)]
    [DataRow(0x80)]
    [DataRow(0xFF)]
    public void CountLeadingZeroBytes256BigEndian_SingleNonZeroByteAtEveryPosition(int value)
    {
        for (int position = 0; position < 32; position++)
        {
            byte[] bytes = new byte[32];
            bytes[position] = (byte)value;
            ulong[] l = LimbsMsbFirst(bytes);

            Assert.AreEqual(position, ByteUtils.CountLeadingZeroBytes256BigEndian(l[0], l[1], l[2], l[3]), $"0x{value:X2}@{position}");
        }
    }

    [TestMethod]
    public void CountLeadingZeroBytes256BigEndian_AllZero_Is32_AndLaterBytesDoNotMatter()
    {
        Assert.AreEqual(32, ByteUtils.CountLeadingZeroBytes256BigEndian(0, 0, 0, 0));
        Assert.AreEqual(0, ByteUtils.CountLeadingZeroBytes256BigEndian(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue));
        Assert.AreEqual(1, ByteUtils.CountLeadingZeroBytes256BigEndian(0x00FF_FFFF_FFFF_FFFF, 0, 0, 0));
        Assert.AreEqual(16, ByteUtils.CountLeadingZeroBytes256BigEndian(0, 0, 0x8000_0000_0000_0000, ulong.MaxValue));
    }

    [TestMethod]
    public void CountLeadingZeroBytes256BigEndian_RandomValues_MatchByteScan()
    {
        var random = new Random(58);
        for (int i = 0; i < 500; i++)
        {
            byte[] bytes = new byte[32];
            random.NextBytes(bytes);
            int zeros = random.Next(0, 33);
            Array.Clear(bytes, 0, zeros);
            ulong[] l = LimbsMsbFirst(bytes);

            Assert.AreEqual(bytes.TakeWhile(b => b == 0).Count(), ByteUtils.CountLeadingZeroBytes256BigEndian(l[0], l[1], l[2], l[3]));
        }
    }

    // ---- TryDecodeBase58ToUInt256BigEndian -------------------------------------------------------------------------------

    [TestMethod]
    public void TryDecodeBase58ToUInt256BigEndian_RandomCanonicalValues_MatchReferenceEncoder()
    {
        var random = new Random(256);
        for (int i = 0; i < 400; i++)
        {
            byte[] bytes = new byte[32];
            random.NextBytes(bytes);
            Array.Clear(bytes, 0, i % 33); // 0..32 leading zero bytes
            string text = EncodeReference(bytes);
            if (text.Length > 44) continue; // longer than Address.MaxBase58Length is rejected by contract: covered separately

            Assert.IsTrue(Decode(text, out ulong[] limbs), text);
            CollectionAssert.AreEqual(LimbsMsbFirst(bytes), limbs, text);
        }
    }

    [TestMethod]
    public void TryDecodeBase58ToUInt256BigEndian_MaxValue_Is44Characters_AndDecodes()
    {
        byte[] max = Enumerable.Repeat((byte)0xFF, 32).ToArray();
        string text = EncodeReference(max);
        Assert.AreEqual(44, text.Length, "oracle self-check");

        Assert.IsTrue(Decode(text, out ulong[] limbs));
        CollectionAssert.AreEqual(new[] { ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue }, limbs);
    }

    [TestMethod]
    public void TryDecodeBase58ToUInt256BigEndian_ThirtyTwoOnes_IsZero()
    {
        Assert.IsTrue(Decode(new string('1', 32), out ulong[] limbs));
        CollectionAssert.AreEqual(new ulong[4], limbs);
    }

    [TestMethod]
    public void TryDecodeBase58ToUInt256BigEndian_ZeroLeadingByteValues_RoundTripWithReference()
    {
        foreach (int zeros in new[] { 1, 5, 31 })
        {
            byte[] bytes = new byte[32];
            bytes[zeros] = 7;
            string text = EncodeReference(bytes);
            Assert.AreEqual(zeros, text.TakeWhile(c => c == '1').Count());

            Assert.IsTrue(Decode(text, out ulong[] limbs), text);
            CollectionAssert.AreEqual(LimbsMsbFirst(bytes), limbs, text);
        }
    }

    [TestMethod]
    public void TryDecodeBase58ToUInt256BigEndian_WrongLeadingOneCount_IsNonCanonicalAndRejected()
    {
        byte[] bytes = new byte[32];
        bytes[3] = 0x42; // three leading zero bytes
        string canonical = EncodeReference(bytes);

        Assert.IsTrue(Decode(canonical, out _));
        Assert.IsFalse(Decode("1" + canonical, out _), "one '1' too many");
        Assert.IsFalse(Decode(canonical[1..], out _), "one '1' too few");
        Assert.IsFalse(Decode(canonical.TrimStart('1'), out _), "all '1' stripped");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("1")]                      // one zero byte, but the value is 0 (32 zero bytes expected)
    [DataRow("2")]                      // value 1 with no leading ones: 31 zero bytes expected
    public void TryDecodeBase58ToUInt256BigEndian_TooShortOrEmpty_IsRejected_AndOutputsAreZero(string text)
    {
        Assert.IsFalse(Decode(text, out ulong[] limbs));
        CollectionAssert.AreEqual(new ulong[4], limbs);
    }

    [TestMethod]
    public void TryDecodeBase58ToUInt256BigEndian_LengthAndLeadingOneLimits()
    {
        Assert.IsFalse(Decode(new string('2', 45), out _), "45 characters exceed the 44 cap");
        Assert.IsFalse(Decode(new string('1', 33), out _), "33 leading ones cannot be 33 zero bytes");
        Assert.IsFalse(Decode(new string('1', 33) + "2", out _));
        Assert.IsFalse(Decode(new string('1', 32) + "2", out _), "32 zero bytes cannot also hold a non-zero value");
    }

    [TestMethod]
    public void TryDecodeBase58ToUInt256BigEndian_ValueReaching2Pow256_IsRejectedAsOverflow()
    {
        Assert.IsFalse(Decode(new string('z', 44), out ulong[] limbs));
        CollectionAssert.AreEqual(new ulong[4], limbs);

        // 2^256 exactly, encoded by the reference (no leading zeros, 44 digits) must overflow; 2^256 - 1 must not.
        string overflow = EncodeBigInteger(BigInteger.Pow(2, 256));
        Assert.AreEqual(44, overflow.Length, "oracle self-check");
        Assert.IsFalse(Decode(overflow, out _));
        Assert.IsTrue(Decode(EncodeBigInteger(BigInteger.Pow(2, 256) - 1), out _));
    }

    private static string EncodeBigInteger(BigInteger value)
    {
        var digits = new StringBuilder();
        while (value > 0)
        {
            digits.Insert(0, Alphabet[(int)(value % 58)]);
            value /= 58;
        }

        return digits.ToString();
    }

    [TestMethod]
    public void TryDecodeBase58ToUInt256BigEndian_InvalidDigits_AreRejected_AtEveryPosition_WithZeroOutputs()
    {
        byte[] bytes = new byte[32];
        new Random(8).NextBytes(bytes);
        bytes[0] = 0x7F;
        string valid = EncodeReference(bytes);
        Assert.IsTrue(Decode(valid, out _), "oracle self-check");

        foreach (char bad in new[] { '0', 'O', 'I', 'l', '+', '/', ' ', '-', '=' })
            for (int position = 0; position < valid.Length; position++)
            {
                string text = valid[..position] + bad + valid[(position + 1)..];

                Assert.IsFalse(Decode(text, out ulong[] limbs), $"'{bad}'@{position}");
                CollectionAssert.AreEqual(new ulong[4], limbs, $"'{bad}'@{position}");
            }
    }

    [TestMethod]
    public void TryDecodeBase58ToUInt256BigEndian_HighBytes_AreRejected()
    {
        byte[] bytes = new byte[32];
        new Random(9).NextBytes(bytes);
        bytes[0] = 0x7F;
        byte[] valid = Encoding.ASCII.GetBytes(EncodeReference(bytes));

        for (int b = 0x80; b < 256; b++)
        {
            byte[] text = (byte[])valid.Clone();
            text[10] = (byte)b;
            Assert.IsFalse(Decode(text, out _), $"0x{b:X2}");
        }
    }

    // ---- TryDecodeBase58To32 (non-canonical) ---------------------------------------------------------------------------

    [TestMethod]
    public void TryDecodeBase58To32_RandomValues_MatchReferenceBytes()
    {
        var random = new Random(32);
        for (int i = 0; i < 200; i++)
        {
            byte[] bytes = new byte[32];
            random.NextBytes(bytes);
            string text = EncodeReference(bytes);
            byte[] destination = new byte[32];

            Assert.IsTrue(ByteUtils.TryDecodeBase58To32(Encoding.ASCII.GetBytes(text), destination), text);
            CollectionAssert.AreEqual(bytes, destination, text);
        }
    }
}
