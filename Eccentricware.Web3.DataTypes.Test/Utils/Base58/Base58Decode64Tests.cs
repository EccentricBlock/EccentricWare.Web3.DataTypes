using EccentricWare.Web3.DataTypes.Utils;
using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Differential tests of the shared 64-byte Base58 decoder (<see cref="ByteUtils.TryDecodeBase58To64"/>, its char
/// overload, and <see cref="Signature.TryParse(ReadOnlySpan{char}, out Signature)"/>) against a straightforward
/// <see cref="BigInteger"/> reference of the canonical-encoding rule.
/// </summary>
[TestClass]
public sealed class Base58Decode64Tests
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>Reference: returns the 64 bytes if <paramref name="text"/> is a canonical 64-byte encoding, otherwise null.</summary>
    private static byte[]? Reference(string text)
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

        int leadingOnes = text.TakeWhile(c => c == '1').Count();
        if (value >= BigInteger.One << 512)
            return null;

        byte[] significant = value.IsZero ? [] : value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (64 - significant.Length != leadingOnes)
            return null;

        byte[] result = new byte[64];
        significant.CopyTo(result, 64 - significant.Length);
        return result;
    }

    private static void AssertAgrees(string text)
    {
        byte[]? expected = Reference(text);

        byte[] utf8Out = new byte[64];
        byte[] charOut = new byte[64];
        bool utf8Ok = ByteUtils.TryDecodeBase58To64(SignatureTestData.Utf8(text), utf8Out);
        bool charOk = ByteUtils.TryDecodeBase58To64Chars(text.AsSpan(), charOut);
        bool signatureOk = Signature.TryParse(text.AsSpan(), out Signature signature);

        Assert.AreEqual(expected is not null, utf8Ok, $"utf8 '{text}'");
        Assert.AreEqual(expected is not null, charOk, $"chars '{text}'");

        if (expected is null)
        {
            // On failure the destination holds no partial garbage.
            Assert.IsTrue(utf8Out.All(b => b == 0), $"utf8 leftovers '{text}'");
            Assert.IsTrue(charOut.All(b => b == 0), $"char leftovers '{text}'");
            Assert.IsFalse(signatureOk, $"Signature accepted '{text}'");
            return;
        }

        CollectionAssert.AreEqual(expected, utf8Out, text);
        CollectionAssert.AreEqual(expected, charOut, text);
        // Every canonical 64-byte encoding is at most 88 characters, below Signature.SolanaMaxBase58Length.
        Assert.IsLessThanOrEqualTo(Signature.SolanaMaxBase58Length, text.Length);
        Assert.IsTrue(signatureOk, $"Signature rejected '{text}'");
        CollectionAssert.AreEqual(expected, signature.ToBytes(), text);
        Assert.IsTrue(Signature.TryParse(SignatureTestData.Utf8(text), out Signature fromUtf8));
        Assert.AreEqual(signature, fromUtf8);
    }

    [TestMethod]
    public void Decodes_RandomValidEncodings_WithEveryLeadingZeroCount()
    {
        var random = new Random(58);
        for (int i = 0; i < 1500; i++)
        {
            byte[] bytes = SignatureTestData.RandomBytes(random, 64);
            int zeros = i % 70 < 65 ? i % 65 : 0; // 0..64 leading zero bytes (64 = all zero)
            bytes.AsSpan(0, Math.Min(zeros, 64)).Clear();
            if (zeros < 64 && bytes[zeros] == 0)
                bytes[zeros] = (byte)(1 + random.Next(255));

            string text = SignatureTestData.Base58(bytes);
            Assert.IsNotNull(Reference(text), "generator must produce canonical text");
            AssertAgrees(text);
        }
    }

    [TestMethod]
    public void Decodes_PowerOfFiftyEightBoundaries_AndLimbEdges()
    {
        var values = new List<BigInteger>();
        for (int bits = 1; bits <= 512; bits += 31)
        {
            BigInteger p = BigInteger.One << bits;
            values.Add(p - 1);
            values.Add(p);
            values.Add(p + 1);
        }
        for (int digits = 1; digits <= 88; digits++)
        {
            BigInteger p = BigInteger.Pow(58, digits);
            values.Add(p - 1);
            values.Add(p);
        }
        values.Add((BigInteger.One << 512) - 1);
        values.Add(BigInteger.One << 512); // overflow by one

        foreach (BigInteger value in values.Where(v => v >= 0))
        {
            byte[] raw = value.IsZero ? [] : value.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (raw.Length > 64)
            {
                // Out of range: encode without the length cap and expect rejection.
                AssertAgrees(SignatureTestData.Base58(raw));
                continue;
            }

            byte[] padded = new byte[64];
            raw.CopyTo(padded, 64 - raw.Length);
            AssertAgrees(SignatureTestData.Base58(padded));
        }
    }

    [TestMethod]
    public void RejectsOrAgrees_ForRandomAlphabetStrings_OfAnyLength()
    {
        var random = new Random(5858);
        for (int i = 0; i < 3000; i++)
        {
            int length = 1 + random.Next(100);
            var chars = new char[length];
            for (int c = 0; c < length; c++)
                chars[c] = Alphabet[random.Next(i % 3 == 0 ? 2 : 58)]; // every third string is mostly '1' and '2'

            AssertAgrees(new string(chars));
        }
    }

    [TestMethod]
    public void RejectsOrAgrees_ForMutatedValidEncodings()
    {
        var random = new Random(585858);
        foreach (Signature signature in SignatureTestData.RandomSignatures(200).Where(s => s.Type == SignatureType.Solana))
        {
            string text = SignatureTestData.Base58(signature.ToBytes());
            char[] chars = text.ToCharArray();

            // Replace one digit, drop the first, append one.
            chars[random.Next(chars.Length)] = Alphabet[random.Next(58)];
            AssertAgrees(new string(chars));
            AssertAgrees(text[1..]);
            AssertAgrees(text + Alphabet[random.Next(58)]);
            AssertAgrees("1" + text);
        }
    }

    [TestMethod]
    public void VeryLongInput_IsRejected_WithoutScanningOrAllocatingByLength()
    {
        string text = new('z', 1_000_000);
        byte[] output = new byte[64];

        Assert.IsFalse(ByteUtils.TryDecodeBase58To64(SignatureTestData.Utf8(text), output));
        Assert.IsFalse(ByteUtils.TryDecodeBase58To64Chars(text.AsSpan(), output));
        Assert.IsTrue(output.All(b => b == 0));
    }

    [TestMethod]
    public void WrongDestinationLength_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ByteUtils.TryDecodeBase58To64(SignatureTestData.Utf8("11"), new byte[63]));
        Assert.ThrowsExactly<ArgumentException>(() => ByteUtils.TryDecodeBase58To64Chars("11".AsSpan(), new byte[65]));
    }

    [TestMethod]
    public void CharactersAbove0xFF_AreRejected_EvenWhenTheirLowByteIsAValidDigit()
    {
        // 'ı' (U+0131) has low byte 0x31 == '1', and U+0141 has low byte 0x41 == 'A': truncation would alias them.
        string valid = SignatureTestData.SolanaDocsBase58;
        foreach (char alias in new[] { '\u0131', '\u0141', '\u0161' })
        {
            string corrupted = string.Concat(valid.AsSpan(0, 30), alias.ToString(), valid.AsSpan(31));
            byte[] output = new byte[64];

            Assert.IsFalse(ByteUtils.TryDecodeBase58To64Chars(corrupted.AsSpan(), output), $"U+{(int)alias:X4}");
            Assert.IsTrue(output.All(b => b == 0));
            Assert.IsFalse(Signature.TryParse(corrupted.AsSpan(), out _), $"Signature U+{(int)alias:X4}");
            Assert.IsFalse(Signature.TryParse(("\u0131" + valid[1..]).AsSpan(), out _));
        }
    }
}
