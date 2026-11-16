using System.Numerics;
using System.Text;
using EccentricWare.Web3.DataTypes.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Direct tests of <see cref="Base58Encoder"/> (big-endian 32-bit limbs to Base58 UTF-8).</summary>
[TestClass]
public class Base58EncoderTests
{
    /// <summary>Encodes a copy of <paramref name="limbs"/> (the encoder destroys its input); null when -1 is returned.</summary>
    private static string? Encode(uint[] limbs, int destinationLength = 128)
    {
        byte[] destination = new byte[destinationLength];
        int written = Base58Encoder.Encode((uint[])limbs.Clone(), destination);
        return written < 0 ? null : Encoding.ASCII.GetString(destination, 0, written);
    }

    private static byte[] RandomBytes(Random random, int length, int leadingZeroBytes)
    {
        byte[] bytes = new byte[length];
        random.NextBytes(bytes);
        bytes.AsSpan(0, leadingZeroBytes).Clear();
        if (leadingZeroBytes < length)
            bytes[leadingZeroBytes] = (byte)random.Next(1, 256);
        return bytes;
    }

    [TestMethod]
    public void Reference_MatchesPublishedBitcoinVectors()
    {
        // Guards the oracle used by the other tests in this class.
        foreach ((string hex, string text) in Base58Reference.PublishedVectors)
            Assert.AreEqual(text, Base58Reference.Encode(Convert.FromHexString(hex)), hex);
    }

    // Vectors from an independent reference implementation; 0x572E4794 and 0x10C8511E are published Bitcoin vectors.
    // A limb is four bytes, so every leading zero byte of the limb array shows up as a '1'.
    [TestMethod]
    [DataRow(new uint[] { 0 }, "1111")]
    [DataRow(new uint[] { 1 }, "1112")]
    [DataRow(new uint[] { 0x61 }, "1112g")]
    [DataRow(new uint[] { 0x00010000 }, "1LUw")]
    [DataRow(new uint[] { 0xFFFFFFFF }, "7YXq9G")]
    [DataRow(new uint[] { 0x572E4794 }, "3EFU7m")]
    [DataRow(new uint[] { 0x10C8511E }, "Rt5zm")]
    [DataRow(new uint[] { 0, 1 }, "11111112")]
    [DataRow(new uint[] { 1, 0 }, "1117YXq9H")]
    [DataRow(new uint[] { 0x01020304, 0x05060708 }, "An6UebxCZd")]
    [DataRow(new uint[] { 0, 0, 1 }, "111111111112")]
    [DataRow(new uint[] { 0, 0, 0xFFFFFFFF }, "111111117YXq9G")]
    public void Encode_KnownAnswers(uint[] limbs, string expected)
        => Assert.AreEqual(expected, Encode(limbs));

    [TestMethod]
    public void Encode_PublishedBitcoinVectors_LeftPaddedToWholeLimbs()
    {
        foreach ((string hex, string text) in Base58Reference.PublishedVectors)
        {
            byte[] bytes = Convert.FromHexString(hex);
            int padding = (4 - bytes.Length % 4) % 4;
            byte[] padded = new byte[bytes.Length + padding];
            bytes.CopyTo(padded, padding);

            Assert.AreEqual(new string('1', padding) + text, Encode(Base58Reference.ToBigEndianLimbs(padded)), hex);
        }
    }

    [TestMethod]
    public void Encode_SolanaDocsSignature_MatchesPublishedText()
    {
        byte[] bytes = Convert.FromHexString("e069ef2fbe487298f22ffecbed57a45de555160d7152f1f9c3269cde05e428fcaa5e622f727c45cbd5446af507ae6394cab8e3a4e0787289698822583b479c0f");

        Assert.AreEqual(
            "5VERv8NMvzbJMEkV8xnrLkEaWRtSz9CosKDYjCJjBRnbJLgp8uirBgmQpjKhoR4tjF3ZpRzrFmBV6UjKdiSZkQUW",
            Encode(Base58Reference.ToBigEndianLimbs(bytes)));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(10)]
    [DataRow(11)]
    [DataRow(12)]
    [DataRow(13)]
    [DataRow(14)]
    [DataRow(15)]
    [DataRow(16)]
    public void Encode_ZeroValue_IsOneCharacterPerZeroByte(int limbCount)
    {
        Assert.AreEqual(new string('1', 4 * limbCount), Encode(new uint[limbCount]));
    }

    [TestMethod]
    public void Encode_NoLimbs_WritesNothingAndReturnsZero()
    {
        byte[] destination = [0xEE];

        Assert.AreEqual(0, Base58Encoder.Encode(Span<uint>.Empty, destination));
        Assert.AreEqual(0, Base58Encoder.Encode(Span<uint>.Empty, Span<byte>.Empty));
        Assert.AreEqual(0xEE, destination[0]);
    }

    [TestMethod]
    public void Encode_MaxValue_HasPinnedLengthForEveryLimbCount()
    {
        // Lengths from the independent reference (ceil(32n / log2 58) digits, no leading zero bytes).
        int[] expectedLengths = [6, 11, 17, 22, 28, 33, 39, 44, 50, 55, 61, 66, 72, 77, 82, 88];
        for (int limbCount = 1; limbCount <= 16; limbCount++)
        {
            uint[] limbs = new uint[limbCount];
            Array.Fill(limbs, uint.MaxValue);

            string? text = Encode(limbs);

            Assert.IsNotNull(text);
            Assert.AreEqual(expectedLengths[limbCount - 1], text.Length, $"limbs={limbCount}");
            Assert.AreEqual(Base58Reference.Encode(Convert.FromHexString(new string('F', 8 * limbCount))), text);
        }
    }

    [TestMethod]
    [DataRow(8, "JEKNVnkbo3jma5nREBBJCDoXFVeKkD56V3xKrvRmWxFG")]
    [DataRow(16, "67rpwLCuS5DGA8KGZXKsVQ7dnPb9goRLoKfgGbLfQg9WoLUgNY77E2jT11fem3coV9nAkguBACzrU1iyZM4B8roQ")]
    public void Encode_MaxValueOf256And512Bits_MatchesKnownText(int limbCount, string expected)
    {
        uint[] limbs = new uint[limbCount];
        Array.Fill(limbs, uint.MaxValue);

        Assert.AreEqual(expected, Encode(limbs));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(8)]
    [DataRow(16)]
    public void Encode_EveryLeadingZeroByteCount_MatchesReference(int limbCount)
    {
        var random = new Random(5800 + limbCount);
        for (int zeroBytes = 0; zeroBytes < 4 * limbCount; zeroBytes++)
        {
            for (int sample = 0; sample < 5; sample++)
            {
                byte[] bytes = RandomBytes(random, 4 * limbCount, zeroBytes);

                string? text = Encode(Base58Reference.ToBigEndianLimbs(bytes));

                Assert.AreEqual(Base58Reference.Encode(bytes), text);
                Assert.IsTrue(text!.AsSpan(0, zeroBytes).ToString().All(c => c == '1'));
                Assert.AreNotEqual('1', text![zeroBytes], $"exactly {zeroBytes} leading ones expected");
            }
        }
    }

    // 58^k has the digits "2" followed by k times "1"; 58^k - 1 is k times "z". The padding '1's carry the leading zero
    // bytes. Crossing every power exercises each digit position, including the 15-digit fused-sweep boundaries.
    [TestMethod]
    [DataRow(3, 16)]
    [DataRow(8, 43)]
    [DataRow(16, 87)]
    public void Encode_PowersOf58AndTheirPredecessors_HaveExactDigitStructure(int limbCount, int maxExponent)
    {
        int byteLength = 4 * limbCount;
        for (int k = 1; k <= maxExponent; k++)
        {
            BigInteger power = BigInteger.Pow(58, k);
            foreach ((BigInteger value, string digits) in new[] { (power, "2" + new string('1', k)), (power - 1, new string('z', k)) })
            {
                byte[] significant = value.ToByteArray(isUnsigned: true, isBigEndian: true);
                byte[] bytes = new byte[byteLength];
                significant.CopyTo(bytes, byteLength - significant.Length);

                Assert.AreEqual(new string('1', byteLength - significant.Length) + digits, Encode(Base58Reference.ToBigEndianLimbs(bytes)), $"limbs={limbCount} k={k} value={value}");
            }
        }
    }

    // 58^2 = 3364 is where the pair table runs out; vectors from an independent reference implementation.
    [TestMethod]
    [DataRow(57u, "111z")]
    [DataRow(58u, "11121")]
    [DataRow(3363u, "11zz")]
    [DataRow(3364u, "11211")]
    public void Encode_SingleLimbAroundPairTableBoundary_MatchesKnownText(uint value, string expected)
        => Assert.AreEqual(expected, Encode([value]));

    [TestMethod]
    public void Encode_FirstTwelveThousandSingleLimbValues_MatchReference()
    {
        for (uint value = 0; value <= 12_000; value++)
            Assert.AreEqual(Base58Reference.Encode(ToBigEndian(value)), Encode([value]), $"value={value}");
    }

    [TestMethod]
    public void Encode_RandomLimbs_UseOnlyTheBase58AlphabetAndMatchReference()
    {
        var random = new Random(58058);
        for (int iteration = 0; iteration < 5000; iteration++)
        {
            int limbCount = random.Next(1, 17);
            byte[] bytes = RandomBytes(random, 4 * limbCount, random.Next(0, 4 * limbCount));

            string? text = Encode(Base58Reference.ToBigEndianLimbs(bytes));

            Assert.IsNotNull(text);
            Assert.IsTrue(text.All(c => Base58Reference.Alphabet.Contains(c)), text);
            Assert.AreEqual(Base58Reference.Encode(bytes), text);
        }
    }

    [TestMethod]
    public void Encode_ThenDecode64_RoundTripsBytes()
    {
        var random = new Random(5864);
        for (int iteration = 0; iteration < 300; iteration++)
        {
            byte[] bytes = RandomBytes(random, 64, random.Next(0, 64));

            string? text = Encode(Base58Reference.ToBigEndianLimbs(bytes));

            ulong[] limbs = new ulong[8];
            Assert.IsTrue(Base58Decoder.TryDecode64(text!.AsSpan(), limbs));
            byte[] decoded = new byte[64];
            Base58Decoder.WriteBigEndian(limbs, decoded);
            CollectionAssert.AreEqual(bytes, decoded);
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(8)]
    [DataRow(16)]
    public void Encode_DestinationSizing_ExactFitSucceedsAndOneShortFailsUntouched(int limbCount)
    {
        var random = new Random(5870 + limbCount);
        // Nonzero values (with and without leading zero bytes) and the zero value, whose text is only leading ones.
        var cases = new List<uint[]> { new uint[limbCount] };
        for (int zeroBytes = 0; zeroBytes < 4 * limbCount; zeroBytes += 3)
            cases.Add(Base58Reference.ToBigEndianLimbs(RandomBytes(random, 4 * limbCount, zeroBytes)));

        foreach (uint[] limbs in cases)
        {
            string expected = Base58Reference.Encode(limbs.SelectMany(l => ToBigEndian(l)).ToArray());

            byte[] exact = new byte[expected.Length];
            Assert.AreEqual(expected.Length, Base58Encoder.Encode((uint[])limbs.Clone(), exact));
            Assert.AreEqual(expected, Encoding.ASCII.GetString(exact));

            byte[] tooSmall = new byte[expected.Length - 1];
            Array.Fill(tooSmall, (byte)0xEE);
            Assert.AreEqual(-1, Base58Encoder.Encode((uint[])limbs.Clone(), tooSmall));
            Assert.IsTrue(tooSmall.All(b => b == 0xEE), "nothing may be written when the destination is too small");

            byte[] oversize = new byte[expected.Length + 5];
            Array.Fill(oversize, (byte)0xEE);
            Assert.AreEqual(expected.Length, Base58Encoder.Encode((uint[])limbs.Clone(), oversize));
            Assert.AreEqual(expected, Encoding.ASCII.GetString(oversize, 0, expected.Length));
            Assert.IsTrue(oversize.AsSpan(expected.Length).ToArray().All(b => b == 0xEE), "bytes past the text must stay untouched");
        }
    }

    [TestMethod]
    public void Encode_EmptyDestination_FailsForAnyNonEmptyText()
    {
        Assert.AreEqual(-1, Base58Encoder.Encode(new uint[1], Span<byte>.Empty)); // "1111" needed
        Assert.AreEqual(-1, Base58Encoder.Encode(new uint[] { 1 }, Span<byte>.Empty));
    }

    [TestMethod]
    public void Encode_UsesLimbsAsScratch_SoTheInputIsNotPreserved()
    {
        // Documented contract: the limbs are destroyed, callers must pass a copy.
        uint[] limbs = [0, 0x01020304, 0x05060708];
        uint[] original = (uint[])limbs.Clone();

        Assert.IsGreaterThan(0, Base58Encoder.Encode(limbs, new byte[64]));

        CollectionAssert.AreNotEqual(original, limbs);
    }

    private static byte[] ToBigEndian(uint value)
    {
        byte[] bytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }
}
