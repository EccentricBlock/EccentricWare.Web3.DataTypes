using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using EccentricWare.Web3.DataTypes.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Direct tests of <see cref="Base58Decoder"/> (Base58 text to little-endian 64-bit limbs).</summary>
[TestClass]
public class Base58DecoderTests
{
    private const string Max512Text = "67rpwLCuS5DGA8KGZXKsVQ7dnPb9goRLoKfgGbLfQg9WoLUgNY77E2jT11fem3coV9nAkguBACzrU1iyZM4B8roQ";
    private const string Max256Text = "JEKNVnkbo3jma5nREBBJCDoXFVeKkD56V3xKrvRmWxFG";

    private static ulong[] SentinelLimbs(int count)
    {
        ulong[] limbs = new ulong[count];
        Array.Fill(limbs, ulong.MaxValue); // proves the decoder clears its output
        return limbs;
    }

    /// <summary>
    /// Runs both TryDecode64 overloads (the text must be Latin-1) and asserts they agree; limbs are all zero on failure.
    /// Returns the limbs, or null when decoding failed.
    /// </summary>
    private static ulong[]? Decode64(string text)
    {
        ulong[] fromBytes = SentinelLimbs(8);
        ulong[] fromChars = SentinelLimbs(8);
        bool byteResult = Base58Decoder.TryDecode64(Encoding.Latin1.GetBytes(text), fromBytes);
        bool charResult = Base58Decoder.TryDecode64(text.AsSpan(), fromChars);

        Assert.AreEqual(byteResult, charResult, "byte and char overloads disagree");
        CollectionAssert.AreEqual(fromBytes, fromChars);
        if (!byteResult)
        {
            Assert.IsTrue(fromBytes.All(l => l == 0), "limbs must be zero on failure");
            return null;
        }

        return fromBytes;
    }

    private static ulong[]? Decode32(string text)
    {
        ulong[] limbs = SentinelLimbs(4);
        if (Base58Decoder.TryDecode32NonCanonical(Encoding.Latin1.GetBytes(text), limbs))
            return limbs;

        Assert.IsTrue(limbs.All(l => l == 0), "limbs must be zero on failure");
        return null;
    }

    private static byte[] BigEndian(ulong[] limbs)
    {
        byte[] bytes = new byte[8 * limbs.Length];
        Base58Decoder.WriteBigEndian(limbs, bytes);
        return bytes;
    }

    private static byte[] Bytes64(int zeroBytes, byte fill, byte first)
    {
        byte[] bytes = new byte[64];
        Array.Fill(bytes, fill, zeroBytes, 64 - zeroBytes);
        if (zeroBytes < 64)
            bytes[zeroBytes] = first;
        return bytes;
    }

    // ------------------------------------------------------------------ TryDecode64

    [TestMethod]
    public void TryDecode64_EmptyInput_FailsWithZeroLimbs()
    {
        Assert.IsNull(Decode64(""));
    }

    // Vectors from an independent reference implementation (the last one is the Solana documentation signature).
    [TestMethod]
    [DataRow("0000000000000000000000000000000000000000000000000000000000000000" + "0000000000000000000000000000000000000000000000000000000000000000", "1111111111111111111111111111111111111111111111111111111111111111")]
    [DataRow("0000000000000000000000000000000000000000000000000000000000000000" + "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "11111111111111111111111111111111" + Max256Text)]
    [DataRow("00" + "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "12AFv15MNPuA84RmU66xw2uMzGipcVxNpzAffoacGVvjFue3CBmf633fAWuiP9cwL9C3z3CJiGgRSFjJfeEcA6QW")]
    [DataRow("01" + "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000", "2AFv15MNPuA84RmU66xw2uMzGipcVxNpzAffoacGVvjFue3CBmf633fAWuiP9cwL9C3z3CJiGgRSFjJfeEcA6QX")]
    [DataRow("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff" + "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", Max512Text)]
    [DataRow("e069ef2fbe487298f22ffecbed57a45de555160d7152f1f9c3269cde05e428fcaa5e622f727c45cbd5446af507ae6394cab8e3a4e0787289698822583b479c0f", "5VERv8NMvzbJMEkV8xnrLkEaWRtSz9CosKDYjCJjBRnbJLgp8uirBgmQpjKhoR4tjF3ZpRzrFmBV6UjKdiSZkQUW")]
    public void TryDecode64_KnownVectors_DecodeToExpectedBytes(string expectedHex, string text)
    {
        Assert.AreEqual(64, expectedHex.Length / 2);

        ulong[]? limbs = Decode64(text);

        Assert.IsNotNull(limbs);
        Assert.AreEqual(expectedHex, Convert.ToHexStringLower(BigEndian(limbs)));
    }

    [TestMethod]
    public void TryDecode64_LimbZeroIsLeastSignificant()
    {
        // 63 leading '1' + "2": the value 1 in a 64-byte field needs 63 leading ones.
        ulong[]? one = Decode64(new string('1', 63) + "2");
        CollectionAssert.AreEqual(new ulong[] { 1, 0, 0, 0, 0, 0, 0, 0 }, one);

        // Put a recognisable 64-bit pattern into each limb position in turn, text built by the independent reference.
        for (int limb = 0; limb < 8; limb++)
        {
            const ulong pattern = 0x0123456789ABCDEFUL;
            byte[] bytes = new byte[64];
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(64 - 8 * (limb + 1), 8), pattern);

            ulong[]? limbs = Decode64(Base58Reference.Encode(bytes));

            Assert.IsNotNull(limbs);
            for (int i = 0; i < 8; i++)
                Assert.AreEqual(i == limb ? pattern : 0UL, limbs[i], $"limb {i} when pattern is in limb {limb}");
        }
    }

    [TestMethod]
    public void TryDecode64_MaxValue_FillsEveryLimb()
    {
        CollectionAssert.AreEqual(Enumerable.Repeat(ulong.MaxValue, 8).ToArray(), Decode64(Max512Text));
    }

    [TestMethod]
    public void TryDecode64_PowerOfTwoTo64_SetsOnlyLimbOne()
    {
        byte[] bytes = new byte[64];
        bytes[64 - 9] = 1; // 2^64

        CollectionAssert.AreEqual(new ulong[] { 0, 1, 0, 0, 0, 0, 0, 0 }, Decode64(Base58Reference.Encode(bytes)));
    }

    [TestMethod]
    public void TryDecode64_LengthGate_RejectsTextBeyondTheCanonicalMaximum()
    {
        CollectionAssert.AreEqual(new ulong[8], Decode64(new string('1', 64)));
        Assert.IsNull(Decode64(new string('1', 65)));
        Assert.IsNull(Decode64(new string('1', 152)));
        Assert.IsNull(Decode64(new string('1', 153)));
        Assert.IsNull(Decode64(new string('z', 88)), "58^88 exceeds 2^512");
        Assert.IsNull(Decode64(new string('1', 64) + new string('z', 88)), "152 characters pass the length gate and overflow");
        Assert.IsNull(Decode64(new string('1', 64) + new string('z', 89)), "153 characters fail the length gate");
        Assert.IsNull(Decode64(new string('z', 100_000)));
    }

    [TestMethod]
    public void TryDecode64_ValueOf2To512_Overflows_AndAllOnesBytesFit()
    {
        Assert.IsNull(Decode64("67rpwLCuS5DGA8KGZXKsVQ7dnPb9goRLoKfgGbLfQg9WoLUgNY77E2jT11fem3coV9nAkguBACzrU1iyZM4B8roR"));
        Assert.IsNotNull(Decode64(Max512Text));
    }

    [TestMethod]
    public void TryDecode64_LeadingOneCountMustEqualLeadingZeroByteCount_ForEveryCount()
    {
        var random = new Random(6464);
        for (int zeroBytes = 0; zeroBytes <= 64; zeroBytes++)
        {
            byte[] bytes = Bytes64(zeroBytes, 0, (byte)random.Next(1, 256));
            if (zeroBytes < 63)
                random.NextBytes(bytes.AsSpan(zeroBytes + 1));
            string text = Base58Reference.Encode(bytes);

            ulong[]? limbs = Decode64(text);
            Assert.IsNotNull(limbs, $"canonical text with {zeroBytes} leading ones");
            CollectionAssert.AreEqual(bytes, BigEndian(limbs));

            Assert.IsNull(Decode64("1" + text), $"{zeroBytes + 1} ones for {zeroBytes} zero bytes");
            if (zeroBytes > 0)
                Assert.IsNull(Decode64(text[1..]), $"{zeroBytes - 1} ones for {zeroBytes} zero bytes");
        }
    }

    [TestMethod]
    public void TryDecode64_RandomText_AgreesWithReferenceCanonicalDecoder()
    {
        var random = new Random(58064);
        int accepted = 0;
        for (int iteration = 0; iteration < 4000; iteration++)
        {
            // Mostly canonical texts, some with an extra or missing leading '1' or a swapped digit.
            byte[] bytes = new byte[64];
            random.NextBytes(bytes);
            int zeros = random.Next(0, 64);
            bytes.AsSpan(0, zeros).Clear();
            string text = Base58Reference.Encode(bytes[(random.Next(0, 3) == 0 ? 0 : zeros)..]);
            text = random.Next(0, 4) switch
            {
                0 => "1" + text,
                1 when text.Length > 1 => text[1..],
                2 => text[..^1] + Base58Reference.Alphabet[random.Next(58)],
                _ => text,
            };
            if (text.Length == 0)
                continue;

            byte[]? expected = Base58Reference.DecodeCanonical(text, 64);
            ulong[]? limbs = Decode64(text);

            Assert.AreEqual(expected is not null, limbs is not null, text);
            if (expected is not null)
            {
                accepted++;
                CollectionAssert.AreEqual(expected, BigEndian(limbs!));
            }
        }

        Assert.IsGreaterThan(500, accepted, "the generator must produce plenty of accepted texts");
    }

    // ---------------------------------------------------------------- invalid characters

    [TestMethod]
    public void TryDecode64_EveryInvalidLatin1Character_IsRejectedAtAnyPosition()
    {
        string valid = Max512Text; // 88 characters, no leading ones
        string withLeadingOnes = Base58Reference.Encode(Bytes64(3, 0x9A, 0x81)); // "111" + digits

        foreach (string template in new[] { valid, withLeadingOnes })
        {
            foreach (int position in new[] { 0, 1, template.Length / 2, template.Length - 1 })
            {
                for (int c = 0; c <= 0xFF; c++)
                {
                    if (Base58Reference.Alphabet.Contains((char)c))
                        continue;

                    char[] mutated = template.ToCharArray();
                    mutated[position] = (char)c;
                    Assert.IsNull(Decode64(new string(mutated)), $"char 0x{c:X2} at {position}");
                }
            }
        }
    }

    [TestMethod]
    [DataRow('0')]
    [DataRow('O')]
    [DataRow('I')]
    [DataRow('l')]
    [DataRow('+')]
    [DataRow('/')]
    [DataRow(' ')]
    [DataRow('\0')]
    [DataRow('\n')]
    public void TryDecode64_NamedInvalidCharacters_AreRejected(char invalid)
    {
        Assert.IsNull(Decode64(invalid + Max512Text[1..]));
        Assert.IsNull(Decode64(Max512Text[..^1] + invalid));
        Assert.IsNull(Decode64(new string(invalid, 1)));
    }

    [TestMethod]
    [DataRow('Ā')]
    [DataRow('ı')] // low byte 0x31 is '1'
    [DataRow('Ł')] // low byte 0x41 is 'A'
    [DataRow('š')] // low byte 0x61 is 'a'
    [DataRow('１')] // fullwidth digit one
    [DataRow('\uD800')] // lone surrogate
    [DataRow(char.MaxValue)]
    public void TryDecode64Chars_CharacterAboveLatin1_IsRejectedAtAnyPositionWithZeroLimbs(char wide)
    {
        string leadingOnes = Base58Reference.Encode(Bytes64(5, 0x42, 0x42));
        foreach (string template in new[] { Max512Text, leadingOnes })
        {
            foreach (int position in new[] { 0, 1, 3, template.Length / 2, template.Length - 1 })
            {
                char[] mutated = template.ToCharArray();
                mutated[position] = wide;
                ulong[] limbs = SentinelLimbs(8);

                Assert.IsFalse(Base58Decoder.TryDecode64(mutated.AsSpan(), limbs), $"U+{(int)wide:X4} at {position}");
                Assert.IsTrue(limbs.All(l => l == 0));
            }
        }
    }

    [TestMethod]
    public void TryDecode64Bytes_NonAsciiBytes_AreRejected()
    {
        foreach (byte invalid in new byte[] { 0x80, 0xBF, 0xC3, 0xFF })
        {
            byte[] text = Encoding.ASCII.GetBytes(Max512Text);
            text[40] = invalid;
            ulong[] limbs = SentinelLimbs(8);

            Assert.IsFalse(Base58Decoder.TryDecode64(text, limbs));
            Assert.IsTrue(limbs.All(l => l == 0));
        }
    }

    // ----------------------------------------------------------------- WriteBigEndian

    [TestMethod]
    public void WriteBigEndian_PlacesLimbZeroLastAndEachLimbBigEndian()
    {
        ulong[] limbs = [0x0807060504030201UL, 0x100F0E0D0C0B0A09UL, 0x1817161514131211UL, 0x201F1E1D1C1B1A19UL];
        byte[] destination = new byte[32];

        Base58Decoder.WriteBigEndian(limbs, destination);

        Assert.AreEqual("201f1e1d1c1b1a19" + "1817161514131211" + "100f0e0d0c0b0a09" + "0807060504030201", Convert.ToHexStringLower(destination));
    }

    [TestMethod]
    public void WriteBigEndian_EightLimbs_FillsSixtyFourBytes()
    {
        ulong[] limbs = Enumerable.Range(0, 8).Select(j => 0x0101010101010101UL * (ulong)(j + 1)).ToArray();
        byte[] destination = new byte[64];

        Base58Decoder.WriteBigEndian(limbs, destination);

        for (int block = 0; block < 8; block++)
            Assert.IsTrue(destination.AsSpan(block * 8, 8).ToArray().All(b => b == 8 - block), $"block {block}");
    }

    [TestMethod]
    public void WriteBigEndian_DestinationTooShort_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Base58Decoder.WriteBigEndian(new ulong[4], new byte[31]));
    }

    // -------------------------------------------------------------- TryDecode32NonCanonical

    [TestMethod]
    public void TryDecode32NonCanonical_PublishedBitcoinVectors_DecodeToLeftPaddedBytes()
    {
        foreach ((string hex, string text) in Base58Reference.PublishedVectors)
        {
            ulong[]? limbs = Decode32(text);

            Assert.IsNotNull(limbs, text);
            Assert.AreEqual(hex.PadLeft(64, '0'), Convert.ToHexStringLower(BigEndian(limbs)), text);
        }
    }

    [TestMethod]
    public void TryDecode32NonCanonical_LeadingOnesAreIgnored_NotCounted()
    {
        CollectionAssert.AreEqual(new ulong[] { 1, 0, 0, 0 }, Decode32("2"));
        CollectionAssert.AreEqual(new ulong[] { 1, 0, 0, 0 }, Decode32("1112"));
        CollectionAssert.AreEqual(new ulong[4], Decode32("1"));
        CollectionAssert.AreEqual(new ulong[4], Decode32(new string('1', 5000)));
    }

    [TestMethod]
    public void TryDecode32NonCanonical_LimbZeroIsLeastSignificant()
    {
        for (int limb = 0; limb < 4; limb++)
        {
            byte[] bytes = new byte[32];
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(32 - 8 * (limb + 1), 8), 0xFEDCBA9876543210UL);

            ulong[]? limbs = Decode32(Base58Reference.Encode(bytes));

            Assert.IsNotNull(limbs);
            for (int i = 0; i < 4; i++)
                Assert.AreEqual(i == limb ? 0xFEDCBA9876543210UL : 0UL, limbs[i]);
        }
    }

    [TestMethod]
    public void TryDecode32NonCanonical_Boundary_Accepts2To256MinusOne_RejectsTwoTo256()
    {
        CollectionAssert.AreEqual(Enumerable.Repeat(ulong.MaxValue, 4).ToArray(), Decode32(Max256Text));
        Assert.IsNull(Decode32("JEKNVnkbo3jma5nREBBJCDoXFVeKkD56V3xKrvRmWxFH"));
        Assert.IsNull(Decode32(new string('z', 100_000)));
        Assert.IsNull(Decode32("11" + new string('z', 60)));
    }

    [TestMethod]
    public void TryDecode32NonCanonical_EmptyInput_Fails()
    {
        Assert.IsNull(Decode32(""));
    }

    [TestMethod]
    public void TryDecode32NonCanonical_InvalidCharacterAtAnyPosition_Fails()
    {
        foreach (string template in new[] { Max256Text, "111" + Max256Text[..20] })
        {
            foreach (int position in new[] { 0, 1, 2, template.Length / 2, template.Length - 1 })
            {
                for (int c = 0; c <= 0xFF; c++)
                {
                    if (Base58Reference.Alphabet.Contains((char)c))
                        continue;

                    char[] mutated = template.ToCharArray();
                    mutated[position] = (char)c;
                    Assert.IsNull(Decode32(new string(mutated)), $"char 0x{c:X2} at {position}");
                }
            }
        }
    }

    [TestMethod]
    public void TryDecode32NonCanonical_RandomText_AgreesWithBigIntegerOracle()
    {
        var random = new Random(58032);
        BigInteger limit = BigInteger.One << 256;
        int accepted = 0, rejected = 0;
        for (int iteration = 0; iteration < 4000; iteration++)
        {
            int length = random.Next(1, 50);
            var builder = new StringBuilder();
            for (int i = 0; i < length; i++)
                builder.Append(random.Next(0, 5) == 0 ? '1' : Base58Reference.Alphabet[random.Next(58)]);

            string text = builder.ToString();
            BigInteger value = text.Aggregate(BigInteger.Zero, (acc, c) => acc * 58 + Base58Reference.Alphabet.IndexOf(c));

            ulong[]? limbs = Decode32(text);

            Assert.AreEqual(value < limit, limbs is not null, text);
            if (limbs is null)
            {
                rejected++;
                continue;
            }

            accepted++;
            Assert.AreEqual(value, new BigInteger(BigEndian(limbs), isUnsigned: true, isBigEndian: true), text);
        }

        Assert.IsGreaterThan(100, accepted);
        Assert.IsGreaterThan(100, rejected);
    }
}
