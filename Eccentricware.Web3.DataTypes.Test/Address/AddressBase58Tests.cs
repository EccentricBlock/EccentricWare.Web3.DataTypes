using System.Numerics;
using System.Text;
using static EccentricWare.Web3.DataTypes.Tests.AddressTestHelpers;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Solana (Base58) formatting and parsing of <see cref="Address"/> against known on-chain addresses and an independent
/// <see cref="BigInteger"/> reference encoder, including leading-zero-byte and rejection edge cases.
/// </summary>
[TestClass]
public sealed class AddressBase58Tests
{
    // Well-known Solana addresses with their raw 32-byte public keys.
    [TestMethod]
    [DataRow("11111111111111111111111111111111", "0000000000000000000000000000000000000000000000000000000000000000")] // System Program
    [DataRow("11111111111111111111111111111112", "0000000000000000000000000000000000000000000000000000000000000001")] // 31 leading zero bytes
    [DataRow("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA", "06ddf6e1d765a193d9cbe146ceeb79ac1cb485ed5f5b37913a8cf5857eff00a9")] // SPL Token
    [DataRow("So11111111111111111111111111111111111111112", "069b8857feab8184fb687f634618c035dac439dc1aeb3b5598a0f00000000001")] // Wrapped SOL mint
    // Vectors below from an independent Python Base58 reference.
    [DataRow("JEKNVnkbo3jma5nREBBJCDoXFVeKkD56V3xKrvRmWxFG", "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")] // maximum: 44 characters
    [DataRow("1thX6LZfHDZZKUs92febYZhYRcXddmzfzF2NvTkPNE", "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f")] // one leading zero byte
    [DataRow("11111111DYwPTp6PAnXhbaUeHgTXwYV4UNuLfA6Ea", "000000000000000089abcdef89abcdef89abcdef89abcdef89abcdef89abcdef")] // eight leading zero bytes
    public void KnownAddresses_ParseToExpectedBytes_And_FormatBack(string base58, string expectedHex)
    {
        byte[] expected = Convert.FromHexString(expectedHex);

        Address parsed = Address.Parse(base58);

        Assert.AreEqual(AddressType.Solana, parsed.Type);
        CollectionAssert.AreEqual(expected, parsed.ToBytes());
        Assert.AreEqual(base58, parsed.ToString());
        Assert.AreEqual(parsed, Address.FromSolanaBytes(expected));
    }

    [TestMethod]
    public void EveryLeadingZeroByteCount_RoundTrips_And_MatchesReference()
    {
        var random = new Random(58);
        for (int zeros = 0; zeros <= Address.SolanaByteLength; zeros++)
        {
            byte[] bytes = new byte[Address.SolanaByteLength];
            random.NextBytes(bytes);
            bytes.AsSpan(0, zeros).Clear();
            if (zeros < bytes.Length && bytes[zeros] == 0)
                bytes[zeros] = 0x01;

            AssertMatchesReference(bytes);
        }
    }

    [TestMethod]
    public void RandomValues_MatchReference_AcrossAllEntryPoints()
    {
        var random = new Random(20240607);
        byte[] bytes = new byte[Address.SolanaByteLength];

        for (int i = 0; i < 5_000; i++)
        {
            random.NextBytes(bytes);
            AssertMatchesReference(bytes);
        }
    }

    [TestMethod]
    public void ExtremeValues_MatchReference()
    {
        AssertMatchesReference(Enumerable.Repeat((byte)0xFF, 32).ToArray());              // maximum: 44 characters
        AssertMatchesReference(new byte[32].Select((_, i) => i == 31 ? (byte)1 : (byte)0).ToArray());
        AssertMatchesReference(new byte[32].Select((_, i) => i == 0 ? (byte)0x80 : (byte)0).ToArray());
        AssertMatchesReference(new byte[32].Select((_, i) => i == 0 ? (byte)1 : (byte)0).ToArray());
        AssertMatchesReference(new byte[32].Select((_, i) => i == 31 ? (byte)57 : (byte)0).ToArray());   // 'z' boundary
        AssertMatchesReference(new byte[32].Select((_, i) => i == 31 ? (byte)58 : (byte)0).ToArray());   // first carry into 2 digits
    }

    [TestMethod]
    public void MaximumValue_IsFortyFourCharacters()
    {
        Address max = Address.FromSolanaBytes(Enumerable.Repeat((byte)0xFF, 32).ToArray());

        Assert.AreEqual(Address.MaxBase58Length, max.ToString().Length);
    }

    // ----------------- rejection -----------------

    [TestMethod]
    public void Parse_Rejects_ValuesAbove256Bits()
    {
        // 44 'z' characters decode to far more than 2^256.
        Assert.IsFalse(Address.TryParse(new string('z', 44), out _));

        // 2^256 exactly is 44 characters but does not fit.
        string twoToThe256 = ReferenceEncode(BigInteger.Pow(2, 256), leadingZeroBytes: 0);
        Assert.AreEqual(44, twoToThe256.Length);
        Assert.IsFalse(Address.TryParse(twoToThe256, out _));

        // 2^256 - 1 fits.
        string max = ReferenceEncode(BigInteger.Pow(2, 256) - 1, leadingZeroBytes: 0);
        Assert.IsTrue(Address.TryParse(max, out Address a));
        Assert.IsTrue(a.ToBytes().All(b => b == 0xFF));
    }

    [TestMethod]
    public void Parse_Rejects_NonCanonicalLeadingOnes()
    {
        const string canonical = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";

        Assert.IsFalse(Address.TryParse("1" + canonical, out _));               // extra leading '1'
        Assert.IsFalse(Address.TryParse(canonical.Substring(0, canonical.Length - 1), out _)); // 42 characters: decodes to 31 bytes with no leading zero byte, not 32
        Assert.IsFalse(Address.TryParse(new string('1', 33), out _));           // 33 zero bytes
        Assert.IsFalse(Address.TryParse(new string('1', 32) + "2", out _));     // 32 ones: one leading zero byte too many for the value
        Assert.IsFalse(Address.TryParse(new string('1', 30) + "2" + new string('1', 1), out _)); // 30 ones then value 2 * 58 + 1 = 117 (one byte): 31 bytes in all, one short of 32
    }

    [TestMethod]
    public void Parse_Accepts_ThirtyOneOnesFollowedByTwo()
    {
        // 31 leading zero bytes then 0x01 is "1" * 31 + "2": canonical, 32 characters.
        Assert.IsTrue(Address.TryParse(new string('1', 31) + "2", out Address a));
        Assert.AreEqual(1, a.ToBytes()[31]);
    }

    [TestMethod]
    public void Parse_Rejects_OutOfAlphabetCharacters_AtEveryPosition()
    {
        string valid = Address.Parse("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA").ToString();

        foreach (char bad in new[] { '0', 'O', 'I', 'l', '+', '/', ' ', '\0', 'é', '１' })
        {
            for (int position = 0; position < valid.Length; position++)
            {
                char[] chars = valid.ToCharArray();
                chars[position] = bad;

                Assert.IsFalse(Address.TryParse(new string(chars), out _), $"char U+{(int)bad:X4} at {position}");
            }
        }
    }

    [TestMethod]
    public void Utf8Parse_Rejects_OutOfAlphabetBytes_AtEveryPosition()
    {
        byte[] valid = Encoding.ASCII.GetBytes(Address.Parse("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA").ToString());

        foreach (byte bad in new byte[] { (byte)'0', (byte)'O', (byte)'I', (byte)'l', 0x00, 0x7F, 0x80, 0xFF })
        {
            for (int position = 0; position < valid.Length; position++)
            {
                byte[] copy = (byte[])valid.Clone();
                copy[position] = bad;

                Assert.IsFalse(Address.TryParse(copy, out _), $"byte 0x{bad:X2} at {position}");
            }
        }
    }

    [TestMethod]
    [DataRow("11111111F")]
    [DataRow("11111111A")]
    [DataRow("111111119")]
    [DataRow("111111117")]
    public void FortyCharacterHexOnlyCanonicalBase58_ParsesAsSolana_NotEvm(string prefix)
    {
        // Eight leading '1' plus 32 digits whose first digit is >= 7 is a canonical 40-character Base58 value with exactly
        // eight leading zero bytes. Every character is also a hex digit and there is no '0', so it is ambiguous with
        // unprefixed EVM hex; the documented rule is that canonical Base58 wins.
        string text = prefix + new string('1', 40 - prefix.Length);
        Assert.AreEqual(40, text.Length);
        Assert.IsTrue(text.All(Uri.IsHexDigit));

        Assert.IsTrue(Address.TryParse(text, out Address fromChars));
        Assert.AreEqual(AddressType.Solana, fromChars.Type);
        Assert.AreEqual(text, fromChars.ToString());

        byte[] bytes = fromChars.ToBytes();
        Assert.IsTrue(bytes.Take(8).All(b => b == 0));
        Assert.AreNotEqual(0, bytes[8]);

        Assert.IsTrue(Address.TryParse(Encoding.ASCII.GetBytes(text), out Address fromUtf8));
        Assert.AreEqual(fromChars, fromUtf8);
    }

    [TestMethod]
    public void Decode_AgreesWithBigIntegerReference_ForRandomStrings()
    {
        // Random strings of 32..44 characters with random leading '1' runs: accept exactly when the value is below
        // 2^256 and the number of leading '1' equals the number of leading zero bytes of the 32-byte value.
        var random = new Random(4458);
        BigInteger limit = BigInteger.Pow(2, 256);
        int accepted = 0, rejected = 0;

        for (int i = 0; i < 20_000; i++)
        {
            int length = random.Next(32, Address.MaxBase58Length + 1);
            char[] chars = new char[length];
            int ones = random.Next(0, 3) == 0 ? random.Next(0, 12) : 0;
            for (int k = 0; k < length; k++)
                chars[k] = k < ones ? '1' : Base58Alphabet[random.Next(Base58Alphabet.Length)];

            string text = new string(chars);
            BigInteger value = BigInteger.Zero;
            foreach (char c in text)
                value = value * 58 + Base58Alphabet.IndexOf(c);

            int leadingOnes = text.TakeWhile(c => c == '1').Count();
            bool expected = false;
            if (value < limit && leadingOnes <= 32)
            {
                int significantBytes = value.IsZero ? 0 : value.GetByteCount(isUnsigned: true);
                expected = 32 - significantBytes == leadingOnes;
            }

            Assert.AreEqual(expected, Address.TryParse(text, out _), text);
            if (expected) accepted++; else rejected++;
        }

        Assert.IsGreaterThan(0, accepted);
        Assert.IsGreaterThan(0, rejected);
    }

    // ----------------- formatting into caller buffers -----------------

    [TestMethod]
    public void TryFormat_Solana_ExactFit_Succeeds_And_OneShort_Fails_ForBothEncodings()
    {
        foreach (byte[] bytes in new[] { Enumerable.Repeat((byte)0xFF, 32).ToArray(), new byte[32], Convert.FromHexString("06ddf6e1d765a193d9cbe146ceeb79ac1cb485ed5f5b37913a8cf5857eff00a9") })
        {
            Address a = Address.FromSolanaBytes(bytes);
            string text = a.ToString();

            Span<char> chars = new char[text.Length];
            Assert.IsTrue(a.TryFormat(chars, out int charsWritten));
            Assert.AreEqual(text, new string(chars.Slice(0, charsWritten)));

            Span<byte> utf8 = new byte[text.Length];
            Assert.IsTrue(a.TryFormat(utf8, out int bytesWritten));
            Assert.AreEqual(text, Encoding.ASCII.GetString(utf8.Slice(0, bytesWritten)));

            Span<char> shortChars = new char[text.Length - 1];
            Assert.IsFalse(a.TryFormat(shortChars, out charsWritten));
            Assert.AreEqual(0, charsWritten);

            Span<byte> shortUtf8 = new byte[text.Length - 1];
            Assert.IsFalse(a.TryFormat(shortUtf8, out bytesWritten));
            Assert.AreEqual(0, bytesWritten);
        }
    }

    [TestMethod]
    public void TryFormat_Solana_DoesNotWritePastTheReturnedLength()
    {
        Address a = Address.Parse("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");

        char[] chars = Enumerable.Repeat('#', 64).ToArray();
        Assert.IsTrue(a.TryFormat(chars, out int charsWritten));
        Assert.IsTrue(chars.Skip(charsWritten).All(c => c == '#'));

        byte[] utf8 = Enumerable.Repeat((byte)'#', 64).ToArray();
        Assert.IsTrue(a.TryFormat(utf8, out int bytesWritten));
        Assert.IsTrue(utf8.Skip(bytesWritten).All(b => b == (byte)'#'));
    }

    // ----------------- helpers -----------------

    private static void AssertMatchesReference(byte[] bytes)
    {
        string expected = ReferenceEncode(bytes);
        Address address = Address.FromSolanaBytes(bytes);

        // format
        Assert.AreEqual(expected, address.ToString());

        Span<char> chars = stackalloc char[Address.MaxBase58Length];
        Assert.IsTrue(address.TryFormat(chars, out int charsWritten));
        Assert.AreEqual(expected, new string(chars.Slice(0, charsWritten)));

        Span<byte> utf8 = stackalloc byte[Address.MaxBase58Length];
        Assert.IsTrue(address.TryFormat(utf8, out int bytesWritten));
        Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8.Slice(0, bytesWritten)));

        // parse: chars, string, UTF-8, quoted UTF-8
        Assert.IsTrue(Address.TryParse(expected.AsSpan(), out Address fromChars));
        Assert.AreEqual(address, fromChars);
        Assert.AreEqual(AddressType.Solana, fromChars.Type);

        Assert.AreEqual(address, Address.Parse(expected));

        Assert.IsTrue(Address.TryParse(Encoding.ASCII.GetBytes(expected), out Address fromUtf8));
        Assert.AreEqual(address, fromUtf8);

        Assert.IsTrue(Address.TryParse(Encoding.ASCII.GetBytes("\"" + expected + "\""), out Address fromQuoted));
        Assert.AreEqual(address, fromQuoted);

        CollectionAssert.AreEqual(bytes, fromChars.ToBytes());
    }
}
