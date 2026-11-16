using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class Hash32Base58ParsingTests
{
    private const string ValidBase58Of43 = "SysvarC1ock11111111111111111111111111111111";

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    [TestMethod]
    public void ParseBase58_ThirtyTwoOnes_IsZero()
    {
        // Canonical Base58 of 32 zero bytes is 32 '1' characters (also the Solana System Program id).
        byte[] zero = Ascii(new string('1', 32));

        Assert.IsTrue(Hash32.ParseBase58(zero).IsZero);
        Assert.IsTrue(Hash32.TryParseBase58(zero, out Hash32 parsed));
        Assert.IsTrue(parsed.IsZero);
    }

    [TestMethod]
    [DataRow("SysvarC1ock11111111111111111111111111111111", "06a7d51718c774c928566398691d5eb68b5eb8a39b4b6d5c73555b2100000000")]
    [DataRow("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA", "06ddf6e1d765a193d9cbe146ceeb79ac1cb485ed5f5b37913a8cf5857eff00a9")]
    [DataRow("So11111111111111111111111111111111111111112", "069b8857feab8184fb687f634618c035dac439dc1aeb3b5598a0f00000000001")]
    [DataRow("JEKNVnkbo3jma5nREBBJCDoXFVeKkD56V3xKrvRmWxFG", "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
    public void ParseBase58_KnownVectors_DecodeToPinnedBytes(string base58, string expectedHex)
    {
        // Vectors from an independent python3 big-integer decode (not taken from the library or Solana docs).
        Hash32 parsed = Hash32.ParseBase58(Ascii(base58));

        Assert.AreEqual(expectedHex, parsed.ToString("x", null));
        Assert.IsTrue(Hash32.TryParseBase58(Ascii(base58), out Hash32 viaTry));
        Assert.AreEqual(parsed, viaTry);
        Assert.IsTrue(Hash32.TryParseAuto(Ascii(base58), out Hash32 viaAuto));
        Assert.AreEqual(parsed, viaAuto);
    }

    [TestMethod]
    public void ParseBase58_MaxValue_MapsToAllOnesLimbs()
    {
        Hash32 parsed = Hash32.ParseBase58(Ascii("JEKNVnkbo3jma5nREBBJCDoXFVeKkD56V3xKrvRmWxFG"));

        Assert.AreEqual(new Hash32(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue), parsed);
    }

    [TestMethod]
    public void TryParseBase58_TwoToThe256_IsRejectedAsOverflow()
    {
        // 2^256 is the max value plus one: the same length but a carry out of the top limb.
        Assert.IsFalse(Hash32.TryParseBase58(Ascii("JEKNVnkbo3jma5nREBBJCDoXFVeKkD56V3xKrvRmWxFH"), out Hash32 parsed));
        Assert.IsTrue(parsed.IsZero);
    }

    [TestMethod]
    public void TryParseBase58_FortyFiveValidCharacters_IsRejected()
    {
        Assert.IsFalse(Hash32.TryParseBase58(Ascii(new string('2', 45)), out Hash32 parsed));
        Assert.IsTrue(parsed.IsZero);
        Assert.IsFalse(Hash32.TryParseBase58(Ascii("1" + new string('1', 44)), out _));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(" \t\r\n")]
    public void TryParseBase58_EmptyOrWhitespaceOnly_IsRejected(string text)
    {
        Assert.IsFalse(Hash32.TryParseBase58(Ascii(text), out Hash32 parsed));
        Assert.IsTrue(parsed.IsZero);
        Assert.ThrowsExactly<FormatException>(() => Hash32.ParseBase58(Ascii(text)));
    }

    [TestMethod]
    [DataRow('0')]
    [DataRow('O')]
    [DataRow('I')]
    [DataRow('l')]
    [DataRow('+')]
    [DataRow('/')]
    [DataRow('_')]
    [DataRow('=')]
    public void TryParseBase58_CharactersOutsideTheAlphabet_AreRejected(char bad)
    {
        string text = ValidBase58Of43[..20] + bad + ValidBase58Of43[21..];

        Assert.IsFalse(Hash32.TryParseBase58(Ascii(text), out Hash32 parsed), text);
        Assert.IsTrue(parsed.IsZero);
    }

    [TestMethod]
    [DataRow(0x00)]
    [DataRow(0x80)]
    [DataRow(0xC3)]
    [DataRow(0xFF)]
    public void TryParseBase58_NonAsciiOrNulByte_IsRejected(int badByte)
    {
        byte[] utf8 = Ascii(ValidBase58Of43);
        utf8[20] = (byte)badByte;

        Assert.IsFalse(Hash32.TryParseBase58(utf8, out Hash32 parsed));
        Assert.IsTrue(parsed.IsZero);
    }

    [TestMethod]
    public void TryParseBase58_InteriorWhitespace_IsRejected()
    {
        string text = ValidBase58Of43[..20] + " " + ValidBase58Of43[20..];

        Assert.IsFalse(Hash32.TryParseBase58(Ascii(text), out _));
    }

    [TestMethod]
    public void TryParseBase58_TrimsAsciiWhitespace()
    {
        byte[] utf8 = Ascii(" \t\r\n" + new string('1', 32) + "\n");

        Assert.IsTrue(Hash32.TryParseBase58(utf8, out Hash32 parsed));
        Assert.IsTrue(parsed.IsZero);
    }

    [TestMethod]
    public void ParseBase58_Invalid_ThrowsFormatException()
    {
        Assert.ThrowsExactly<FormatException>(() => Hash32.ParseBase58(Ascii("10")));
    }

    [TestMethod]
    public void TryParseBase58_RoundTripsRandomCanonicalValues_IncludingLeadingZeroBytes()
    {
        var rng = new Random(1234);
        for (int i = 0; i < 500; i++)
        {
            byte[] bytes = new byte[32];
            rng.NextBytes(bytes);
            for (int z = 0; z < i % 4; z++)
                bytes[z] = 0; // 0..3 leading zero bytes => 0..3 leading '1' characters

            string base58 = Hash32TestData.Base58Encode(bytes);

            Assert.IsTrue(Hash32.TryParseBase58(Ascii(base58), out Hash32 parsed), base58);
            Assert.AreEqual(new Hash32(bytes), parsed, base58);
            Assert.AreEqual(new Hash32(bytes), Hash32.ParseBase58(Ascii(base58)));
        }
    }

    [TestMethod]
    [DataRow("1")]                                                  // would decode to zero if length were not enforced
    [DataRow("2")]                                                  // would decode to 1
    [DataRow("abc")]
    [DataRow("1111111111111111111111111111111")]                    // 31 ones = 31 zero bytes
    [DataRow("111111111111111111111111111111111")]                  // 33 ones = 33 zero bytes
    [DataRow("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]       // 44 chars, value exceeds 2^256
    public void TryParseBase58_RejectsWrongLengthOrNonCanonical(string text)
    {
        byte[] utf8 = Ascii(text);

        Assert.IsFalse(Hash32.TryParseBase58(utf8, out Hash32 parsed), text);
        Assert.IsTrue(parsed.IsZero);
        Assert.ThrowsExactly<FormatException>(() => Hash32.ParseBase58(utf8));
    }

    [TestMethod]
    public void TryParseBase58_RejectsExtraLeadingOneOnCanonicalValue()
    {
        // Top byte 0x01 => 43 characters, so a prepended '1' (a 33rd byte) still fits the 44-char length cap
        // and is rejected only by the canonical leading-zero check.
        byte[] bytes = Hash32TestData.Bytes0To31();
        bytes[0] = 0x01;
        string canonical = Hash32TestData.Base58Encode(bytes);
        Assert.AreEqual(43, canonical.Length);
        Assert.IsTrue(Hash32.TryParseBase58(Ascii(canonical), out _));

        Assert.IsFalse(Hash32.TryParseBase58(Ascii("1" + canonical), out _));
    }
}
