using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class Hash32TryParseAutoTests
{
    private static readonly byte[] Bytes = Hash32TestData.Bytes0To31();
    private static readonly string LowerHex = Hash32TestData.Hex(Bytes);

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    [TestMethod]
    public void PrefixedHex_Parses()
    {
        Assert.IsTrue(Hash32.TryParseAuto(Ascii("0x" + LowerHex), out Hash32 parsed));
        Assert.AreEqual(Hash32TestData.Value0To31(), parsed);
    }

    [TestMethod]
    public void UppercasePrefixAndDigits_Parse()
    {
        Assert.IsTrue(Hash32.TryParseAuto(Ascii("0X" + LowerHex.ToUpperInvariant()), out Hash32 parsed));
        Assert.AreEqual(Hash32TestData.Value0To31(), parsed);
    }

    [TestMethod]
    public void BareSixtyFourHex_IsTreatedAsHex()
    {
        // EVM systems sometimes emit 64 hex characters without a prefix.
        Assert.IsTrue(Hash32.TryParseAuto(Ascii(LowerHex), out Hash32 parsed));
        Assert.AreEqual("0x" + LowerHex, parsed.ToString());
        Assert.AreEqual(Hash32.Parse(LowerHex.AsSpan()), parsed);
    }

    [TestMethod]
    public void ThirtyTwoOnes_IsBase58Zero()
    {
        // Solana System Program id.
        Assert.IsTrue(Hash32.TryParseAuto(Ascii(new string('1', 32)), out Hash32 parsed));
        Assert.IsTrue(parsed.IsZero);
        Assert.AreEqual(Hash32.Zero.ToString(), parsed.ToString());
    }

    [TestMethod]
    public void PaddedBase64_IsParsed_AfterBase58Fails()
    {
        string b64 = Convert.ToBase64String(Bytes);
        Assert.EndsWith("=", b64);

        Assert.IsTrue(Hash32.TryParseAuto(Ascii(b64), out Hash32 parsed));
        Assert.AreEqual(Hash32TestData.Value0To31(), parsed);
    }

    [TestMethod]
    public void PaddedBase64_WithSurroundingWhitespace_IsParsed()
    {
        string b64 = Convert.ToBase64String(Bytes);

        Assert.IsTrue(Hash32.TryParseAuto(Ascii("  " + b64 + "\r\n"), out Hash32 parsed));
        Assert.AreEqual(Hash32TestData.Value0To31(), parsed);
    }

    [TestMethod]
    public void HexWithSurroundingWhitespace_IsParsed()
    {
        Assert.IsTrue(Hash32.TryParseAuto(Ascii(" \t0x" + LowerHex + "\n"), out Hash32 parsed));
        Assert.AreEqual(Hash32TestData.Value0To31(), parsed);
    }

    [TestMethod]
    public void ZeroXPrefix_ForcesHexOnly_NoBase58Fallback()
    {
        // Starts with 0x but is not valid 64-hex => must fail and not attempt Base58/Base64.
        Assert.IsFalse(Hash32.TryParseAuto(Ascii("0x" + new string('1', 32)), out Hash32 parsed));
        Assert.IsTrue(parsed.IsZero);
    }

    [TestMethod]
    public void Hex_WithOneInvalidCharacter_IsRejected()
    {
        Assert.IsFalse(Hash32.TryParseAuto(Ascii("0x" + LowerHex[..63] + "g"), out Hash32 prefixed));
        Assert.IsTrue(prefixed.IsZero);

        // 64 characters that are not all hex and are not Base58 (contains '0') cannot parse by any route.
        Assert.IsFalse(Hash32.TryParseAuto(Ascii(LowerHex[..63] + "g"), out Hash32 bare));
        Assert.IsTrue(bare.IsZero);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("1")]
    [DataRow("2")]
    [DataRow("0")]
    [DataRow("x")]
    [DataRow("=")]
    [DataRow("abc")]
    [DataRow("abcd")]
    [DataRow("0x")]
    [DataRow("0X")]
    [DataRow("0x12")]
    public void ShortOrMalformedInput_IsRejected_AndResultIsZero(string text)
    {
        Assert.IsFalse(Hash32.TryParseAuto(Ascii(text), out Hash32 parsed), text);
        Assert.IsTrue(parsed.IsZero);
    }

    [TestMethod]
    public void WrongLengthHexDigits_AreRejected()
    {
        Assert.IsFalse(Hash32.TryParseAuto(Ascii(new string('f', 63)), out _));
        Assert.IsFalse(Hash32.TryParseAuto(Ascii(new string('f', 65)), out _));
    }

    [TestMethod]
    public void UnpaddedBase64Of33Bytes_IsRejected()
    {
        // 33 bytes of 0xFF => 44 unpadded Base64 characters, containing '/', which is not Base58.
        string text = Convert.ToBase64String(Enumerable.Repeat((byte)0xFF, 33).ToArray());
        Assert.AreEqual(44, text.Length);

        Assert.IsFalse(Hash32.TryParseAuto(Ascii(text), out Hash32 parsed));
        Assert.IsTrue(parsed.IsZero);
    }

    [TestMethod]
    public void PaddedBase64_DecodingToTheWrongLength_IsRejected()
    {
        // 31 bytes => 44 characters ending in "==": the padded path decodes it, then the length check rejects it.
        string text = Convert.ToBase64String(Enumerable.Range(0, 31).Select(i => (byte)i).ToArray());
        Assert.AreEqual("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHg==", text);

        Assert.IsFalse(Hash32.TryParseAuto(Ascii(text), out Hash32 parsed));
        Assert.IsTrue(parsed.IsZero);
    }

    [TestMethod]
    public void Base58Vector_IsParsedAsBase58()
    {
        // Pinned independently (python3 big-integer decode).
        Assert.IsTrue(Hash32.TryParseAuto(Ascii("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA"), out Hash32 parsed));
        Assert.AreEqual("0x06ddf6e1d765a193d9cbe146ceeb79ac1cb485ed5f5b37913a8cf5857eff00a9", parsed.ToString());
    }
}
