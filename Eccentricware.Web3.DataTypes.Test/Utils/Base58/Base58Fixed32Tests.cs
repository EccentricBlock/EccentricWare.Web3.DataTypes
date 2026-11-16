using EccentricWare.Web3.DataTypes.Utils;
using System.Numerics;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// 32-byte Base58 paths after they were moved onto shared limb routines: the non-canonical
/// <see cref="ByteUtils.TryDecodeBase58To32"/>, the canonical Hash32/Address decoder, and the shared 32-byte encoder.
/// All are checked against BigInteger references.
/// </summary>
[TestClass]
public sealed class Base58Fixed32Tests
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>Non-canonical reference: any run of valid digits, value below 2^256; leading '1' just add zero digits.</summary>
    private static byte[]? ReferenceNonCanonical(string text)
    {
        if (text.Length == 0)
            return null;

        BigInteger value = BigInteger.Zero;
        foreach (char c in text)
        {
            int digit = Alphabet.IndexOf(c);
            if (digit < 0)
                return null;
            value = value * 58 + digit;
        }

        if (value >= BigInteger.One << 256)
            return null;

        byte[] result = new byte[32];
        byte[] significant = value.IsZero ? [] : value.ToByteArray(isUnsigned: true, isBigEndian: true);
        significant.CopyTo(result, 32 - significant.Length);
        return result;
    }

    private static void AssertNonCanonicalAgrees(string text)
    {
        byte[]? expected = ReferenceNonCanonical(text);
        byte[] destination = new byte[32];
        bool ok = ByteUtils.TryDecodeBase58To32(Encoding.ASCII.GetBytes(text), destination);

        Assert.AreEqual(expected is not null, ok, $"'{text}'");
        if (expected is null)
            Assert.IsTrue(destination.All(b => b == 0), $"no partial output for '{text}'");
        else
            CollectionAssert.AreEqual(expected, destination, text);
    }

    [TestMethod]
    public void TryDecodeBase58To32_MatchesReference_ForRandomEncodings_WithAnyNumberOfExtraLeadingOnes()
    {
        var random = new Random(3232);
        for (int i = 0; i < 1500; i++)
        {
            byte[] bytes = SignatureTestData.RandomBytes(random, 32);
            bytes.AsSpan(0, i % 33 > 31 ? 31 : i % 32).Clear();
            string text = new string('1', random.Next(0, 4) * (i % 2)) + SignatureTestData.Base58(bytes);
            AssertNonCanonicalAgrees(text);
        }
    }

    [TestMethod]
    public void TryDecodeBase58To32_MatchesReference_AtOverflowBoundaries()
    {
        foreach (int bits in new[] { 1, 31, 32, 63, 64, 127, 128, 191, 192, 255, 256, 257, 300 })
        {
            BigInteger p = BigInteger.One << bits;
            foreach (BigInteger value in new[] { p - 1, p, p + 1 })
            {
                byte[] raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
                AssertNonCanonicalAgrees(SignatureTestData.Base58(raw));
            }
        }

        AssertNonCanonicalAgrees(new string('1', 100));        // all zero, far longer than 32 characters
        AssertNonCanonicalAgrees(new string('1', 1000) + "2"); // very many leading ones
        AssertNonCanonicalAgrees(new string('z', 44));         // overflows
    }

    [TestMethod]
    public void TryDecodeBase58To32_RandomAlphabetStrings_AndInvalidInput()
    {
        var random = new Random(32);
        for (int i = 0; i < 2000; i++)
        {
            int length = 1 + random.Next(70);
            var chars = new char[length];
            for (int c = 0; c < length; c++)
                chars[c] = Alphabet[random.Next(i % 3 == 0 ? 2 : 58)];
            AssertNonCanonicalAgrees(new string(chars));
        }

        foreach (string bad in new[] { "", "0", "O", "I", "l", "abc0def", "2" + "é" })
        {
            byte[] destination = new byte[32];
            Assert.IsFalse(ByteUtils.TryDecodeBase58To32(Encoding.UTF8.GetBytes(bad), destination), bad);
            Assert.IsTrue(destination.All(b => b == 0));
        }
    }

    [TestMethod]
    public void TryDecodeBase58To32_VeryLongRunOfOnes_IsLinearAndSucceedsAsZero()
    {
        byte[] destination = new byte[32];
        Assert.IsTrue(ByteUtils.TryDecodeBase58To32(Encoding.ASCII.GetBytes(new string('1', 1_000_000)), destination));
        Assert.IsTrue(destination.All(b => b == 0));
    }

    // ----------------- canonical decoder (Hash32 / Address) and the shared encoder -----------------

    [TestMethod]
    public void Hash32_ParseBase58_And_EncodedForm_MatchTheReference_ForRandomValues()
    {
        var random = new Random(32_32);
        Span<byte> utf8 = stackalloc byte[Address.MaxBase58Length];
        for (int i = 0; i < 1500; i++)
        {
            byte[] bytes = SignatureTestData.RandomBytes(random, 32);
            bytes.AsSpan(0, i % 33 > 31 ? 31 : i % 32).Clear();
            Hash32 value = new(bytes);

            string expected = SignatureTestData.Base58(bytes);
            int written = ByteUtils.EncodeBase58UInt256BigEndianToUtf8(
                System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(0)),
                System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8)),
                System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(16)),
                System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(24)),
                utf8);

            Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8.Slice(0, written)), "encode");
            Assert.IsTrue(Hash32.TryParseBase58(Encoding.ASCII.GetBytes(expected), out Hash32 parsed), "decode " + expected);
            Assert.AreEqual(value, parsed);
        }
    }

    [TestMethod]
    public void EncodeBase58UInt256_AtEveryDigitCountBoundary()
    {
        Span<byte> utf8 = stackalloc byte[Address.MaxBase58Length];
        for (int digits = 1; digits <= 44; digits++)
        {
            BigInteger power = BigInteger.Pow(58, digits);
            foreach (BigInteger value in new[] { power - 1, power, power + 1 })
            {
                if (value >= BigInteger.One << 256)
                    continue;

                byte[] bytes = new byte[32];
                byte[] raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
                raw.CopyTo(bytes, 32 - raw.Length);

                int written = ByteUtils.EncodeBase58UInt256BigEndianToUtf8(
                    System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(0)),
                    System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8)),
                    System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(16)),
                    System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(24)),
                    utf8);

                Assert.AreEqual(SignatureTestData.Base58(bytes), Encoding.ASCII.GetString(utf8.Slice(0, written)), $"58^{digits} {(value - power)}");
            }
        }
    }

    [TestMethod]
    public void EncodeBase58UInt256_ExtremeValues_AndTooSmallDestination()
    {
        Span<byte> utf8 = stackalloc byte[Address.MaxBase58Length];

        Assert.AreEqual(32, ByteUtils.EncodeBase58UInt256BigEndianToUtf8(0, 0, 0, 0, utf8));
        Assert.AreEqual(new string('1', 32), Encoding.ASCII.GetString(utf8.Slice(0, 32)));

        byte[] max = Enumerable.Repeat((byte)0xFF, 32).ToArray();
        int written = ByteUtils.EncodeBase58UInt256BigEndianToUtf8(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, utf8);
        Assert.AreEqual(SignatureTestData.Base58(max), Encoding.ASCII.GetString(utf8.Slice(0, written)));

        Assert.AreEqual(-1, ByteUtils.EncodeBase58UInt256BigEndianToUtf8(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, new byte[written - 1]));
        Assert.AreEqual(-1, ByteUtils.EncodeBase58UInt256BigEndianToUtf8(0, 0, 0, 0, new byte[31]));

        // A destination that is too small is left untouched.
        byte[] tooSmall = new byte[written - 1];
        tooSmall.AsSpan().Fill(0xEE);
        Assert.AreEqual(-1, ByteUtils.EncodeBase58UInt256BigEndianToUtf8(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, tooSmall));
        Assert.IsTrue(tooSmall.All(b => b == 0xEE), "no partial output on failure");
    }

    [TestMethod]
    public void SolanaAddress_ToString_RoundTrips_ForRandomKeys()
    {
        var random = new Random(44);
        for (int i = 0; i < 500; i++)
        {
            byte[] bytes = SignatureTestData.RandomBytes(random, 32);
            bytes.AsSpan(0, i % 4).Clear();
            Address address = Address.FromSolanaBytes(bytes);

            Assert.AreEqual(SignatureTestData.Base58(bytes), address.ToString());
            Assert.AreEqual(address, Address.Parse(address.ToString().AsSpan()));
        }
    }
}
