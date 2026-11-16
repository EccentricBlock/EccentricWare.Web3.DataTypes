using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Base58 formatting of Solana <see cref="Signature"/> values, checked against an independent BigInteger encoder.</summary>
[TestClass]
public sealed class SignatureBase58EncodeTests
{
    [TestMethod]
    public void ToBase58String_MatchesIndependentEncoder_ForRandomSignatures_IncludingLeadingZeroBytes()
    {
        foreach (Signature signature in SignatureTestData.RandomSignatures(600).Where(s => s.Type == SignatureType.Solana))
        {
            string expected = SignatureTestData.Base58(signature.ToBytes());
            Assert.AreEqual(expected, signature.ToBase58String());
        }
    }

    [TestMethod]
    public void ToBase58String_DocsExample_RoundTrips()
    {
        Signature parsed = Signature.Parse(SignatureTestData.SolanaDocsBase58.AsSpan());
        Assert.AreEqual(SignatureTestData.SolanaDocsBase58, parsed.ToBase58String());
    }

    [TestMethod]
    public void ToBase58String_ExtremeValues()
    {
        Assert.AreEqual(new string('1', 64), Signature.ZeroSolana.ToBase58String());

        byte[] allOnes = Enumerable.Repeat((byte)0xFF, 64).ToArray();
        Signature max = Signature.FromSolanaBytes(allOnes);
        Assert.AreEqual(SignatureTestData.Base58(allOnes), max.ToBase58String());
        Assert.AreEqual(Signature.SolanaMaxBase58EncodedLength, max.ToBase58String().Length);

        // Smallest non-zero values: 63 leading ones then one digit; one bit in each byte position.
        for (int index = 0; index < 64; index++)
        {
            byte[] bytes = new byte[64];
            bytes[index] = 1;
            Signature value = Signature.FromSolanaBytes(bytes);
            Assert.AreEqual(SignatureTestData.Base58(bytes), value.ToBase58String(), $"byte {index}");
            bytes[index] = 0xFF;
            Assert.AreEqual(SignatureTestData.Base58(bytes), Signature.FromSolanaBytes(bytes).ToBase58String(), $"byte {index} = FF");
        }
    }

    [TestMethod]
    public void ToBase58String_AtEveryDigitCountBoundary_AndAcrossFusedSweeps()
    {
        // The encoder works in sweeps of 15 digits; values straddling 58^k for every k exercise each partial final sweep,
        // padding trimming, and the transitions between digit counts.
        for (int digits = 1; digits <= 88; digits++)
        {
            System.Numerics.BigInteger power = System.Numerics.BigInteger.Pow(58, digits);
            foreach (System.Numerics.BigInteger value in new[] { power - 1, power, power + 1 })
            {
                if (value >= System.Numerics.BigInteger.One << 512)
                    continue;

                byte[] bytes = new byte[64];
                byte[] raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
                raw.CopyTo(bytes, 64 - raw.Length);

                Assert.AreEqual(SignatureTestData.Base58(bytes), Signature.FromSolanaBytes(bytes).ToBase58String(), $"58^{digits} {(value - power)}");
            }
        }
    }

    [TestMethod]
    public void ToBase58String_NeverExceedsTheDocumentedMaximum()
    {
        var random = new Random(88);
        int longest = 0;
        for (int i = 0; i < 5000; i++)
        {
            Signature value = Signature.FromSolanaBytes(SignatureTestData.RandomBytes(random, 64));
            longest = Math.Max(longest, value.ToBase58String().Length);
        }

        Assert.IsLessThanOrEqualTo(Signature.SolanaMaxBase58EncodedLength, longest);
        Assert.IsGreaterThanOrEqualTo(86, longest, "random 64-byte values are almost always 87 or 88 characters");
    }

    [TestMethod]
    public void EncodeThenParse_RoundTrips_ThroughEveryParsePath()
    {
        foreach (Signature signature in SignatureTestData.RandomSignatures(400).Where(s => s.Type == SignatureType.Solana))
        {
            string text = signature.ToBase58String();
            Assert.AreEqual(signature, Signature.Parse(text.AsSpan()));
            Assert.IsTrue(Signature.TryParse(Encoding.ASCII.GetBytes(text), out Signature fromUtf8));
            Assert.AreEqual(signature, fromUtf8);
            Assert.AreEqual(signature, JsonSerializer.Deserialize<Signature>("\"" + text + "\""));
        }
    }

    [TestMethod]
    public void TryFormatBase58_Chars_And_Utf8_AgreeWithToBase58String()
    {
        foreach (Signature signature in SignatureTestData.RandomSignatures(200).Where(s => s.Type == SignatureType.Solana))
        {
            string expected = signature.ToBase58String();

            char[] chars = new char[Signature.SolanaMaxBase58EncodedLength];
            Assert.IsTrue(signature.TryFormatBase58(chars, out int charsWritten));
            Assert.AreEqual(expected, new string(chars, 0, charsWritten));

            byte[] utf8 = new byte[Signature.SolanaMaxBase58EncodedLength];
            Assert.IsTrue(signature.TryFormatBase58(utf8, out int bytesWritten));
            Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8, 0, bytesWritten));
        }
    }

    [TestMethod]
    public void TryFormatBase58_ExactSize_LargerBuffer_AndTooSmall()
    {
        Signature value = SignatureTestData.SampleSolana;
        string expected = value.ToBase58String();

        char[] exact = new char[expected.Length];
        Assert.IsTrue(value.TryFormatBase58(exact, out int written));
        Assert.AreEqual(expected.Length, written);

        char[] larger = new char[expected.Length + 4];
        larger.AsSpan().Fill('#');
        Assert.IsTrue(value.TryFormatBase58(larger, out written));
        Assert.IsTrue(larger.AsSpan(written).ToArray().All(c => c == '#'), "must not write past the result");

        char[] tooSmallChars = new char[expected.Length - 1];
        Assert.IsFalse(value.TryFormatBase58(tooSmallChars, out written));
        Assert.AreEqual(0, written);
        Assert.IsTrue(tooSmallChars.All(c => c == '\0'), "must not write partial output");

        byte[] tooSmallBytes = new byte[expected.Length - 1];
        Assert.IsFalse(value.TryFormatBase58(tooSmallBytes, out written));
        Assert.AreEqual(0, written);
        Assert.IsTrue(tooSmallBytes.All(b => b == 0), "must not write partial output");

        Assert.IsFalse(value.TryFormatBase58(Span<char>.Empty, out _));
    }

    [TestMethod]
    public void EvmSignature_HasNoBase58Form()
    {
        Signature evm = SignatureTestData.SampleEvm;

        Assert.ThrowsExactly<InvalidOperationException>(() => evm.ToBase58String());
        Assert.IsFalse(evm.TryFormatBase58(new char[100], out int chars));
        Assert.AreEqual(0, chars);
        Assert.IsFalse(evm.TryFormatBase58(new byte[100], out int bytes));
        Assert.AreEqual(0, bytes);
    }

    // ----------------- independent known answers -----------------

    [TestMethod]
    public void ToBase58String_MatchesTheIndependentVectors()
    {
        // vectors from an independent python Base58 implementation (including the RFC 8032 test 1 signature)
        foreach ((string name, string hex, string base58) in SignatureTestData.Base58Vectors)
        {
            Signature signature = Signature.FromSolanaBytes(Convert.FromHexString(hex));

            Assert.AreEqual(base58, signature.ToBase58String(), name);

            char[] chars = new char[base58.Length];
            Assert.IsTrue(signature.TryFormatBase58(chars, out int charsWritten), name);
            Assert.AreEqual(base58, new string(chars, 0, charsWritten), name);

            byte[] utf8 = new byte[base58.Length];
            Assert.IsTrue(signature.TryFormatBase58(utf8, out int bytesWritten), name);
            Assert.AreEqual(base58, Encoding.ASCII.GetString(utf8, 0, bytesWritten), name);
        }
    }

    // ----------------- TryFormatBase58 buffer boundaries -----------------

    [TestMethod]
    public void TryFormatBase58_TightBuffers_SucceedAtExactlyTheEncodedLength_ForBothOverloads()
    {
        // The leading-'1' heavy value needs 64 characters, the largest needs 88, the others sit in between.
        foreach ((string name, string hex, string base58) in SignatureTestData.Base58Vectors)
        {
            Signature signature = Signature.FromSolanaBytes(Convert.FromHexString(hex));
            int length = base58.Length;

            char[] exactChars = new char[length];
            Assert.IsTrue(signature.TryFormatBase58(exactChars, out int written), name);
            Assert.AreEqual(length, written, name);
            Assert.AreEqual(base58, new string(exactChars), name);

            char[] shortChars = new char[length - 1];
            Assert.IsFalse(signature.TryFormatBase58(shortChars, out written), name);
            Assert.AreEqual(0, written, name);
            Assert.IsTrue(shortChars.All(c => c == '\0'), name);

            byte[] exactBytes = new byte[length];
            Assert.IsTrue(signature.TryFormatBase58(exactBytes, out written), name);
            Assert.AreEqual(length, written, name);
            Assert.AreEqual(base58, Encoding.ASCII.GetString(exactBytes), name);

            byte[] shortBytes = new byte[length - 1];
            Assert.IsFalse(signature.TryFormatBase58(shortBytes, out written), name);
            Assert.AreEqual(0, written, name);
            Assert.IsTrue(shortBytes.All(b => b == 0), name);

            Assert.IsFalse(signature.TryFormatBase58(Span<byte>.Empty, out written), name);
            Assert.AreEqual(0, written, name);
        }
    }

    [TestMethod]
    public void TryFormatBase58_LargerUtf8Buffer_LeavesTheTailUntouched()
    {
        foreach ((string name, string hex, string base58) in SignatureTestData.Base58Vectors)
        {
            Signature signature = Signature.FromSolanaBytes(Convert.FromHexString(hex));

            byte[] larger = SignatureTestData.Filled(base58.Length + 6, 0xEE);
            Assert.IsTrue(signature.TryFormatBase58(larger, out int written), name);
            Assert.AreEqual(base58.Length, written, name);
            Assert.AreEqual(base58, Encoding.ASCII.GetString(larger, 0, written), name);
            Assert.IsTrue(larger.AsSpan(written).ToArray().All(b => b == 0xEE), name);

            char[] largerChars = new char[base58.Length + 6];
            largerChars.AsSpan().Fill('#');
            Assert.IsTrue(signature.TryFormatBase58(largerChars, out written), name);
            Assert.IsTrue(largerChars.AsSpan(written).ToArray().All(c => c == '#'), name);
        }
    }

    // ----------------- EVM signatures -----------------

    [TestMethod]
    public void EvmSignature_HasNoBase58Form_ForEveryEvmValue()
    {
        foreach (Signature evm in new[] { SignatureTestData.SampleEvm, default, Signature.ZeroEvm, Signature.FromEvmBytes(SignatureTestData.Filled(65, 0xFF)) })
        {
            var error = Assert.ThrowsExactly<InvalidOperationException>(() => evm.ToBase58String());
            StringAssert.Contains(error.Message, "Solana");
        }
    }
}
