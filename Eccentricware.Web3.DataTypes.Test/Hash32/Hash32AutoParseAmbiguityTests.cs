using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// A 43-character unpadded Base64 string can also be a valid canonical Base58 string. <see cref="Hash32.TryParseAuto"/>
/// (and therefore the JSON converter) must not guess Base64 for unpadded text: it used to decode such strings to a
/// different hash about 1.3% of the time.
/// </summary>
[TestClass]
public sealed class Hash32AutoParseAmbiguityTests
{
    // Unpadded Base64 of this hash is also a valid Base58 string (43 characters, none of 0 O I l + / - _).
    private const string AmbiguousUnpadded = "cn9fxMRwMQcPgriiQNowBGktUBbZ3uSLC93kGKvFsBw";

    // Independent value: python3 base64.b64decode(AmbiguousUnpadded + "=").
    private static readonly Hash32 TrueHash = Hash32.Parse("0x727f5fc4c47031070f82b8a240da3004692d5016d9dee48b0bdde418abc5b01c");

    private static byte[] Utf8(string s) => Encoding.ASCII.GetBytes(s);

    [TestMethod]
    public void PaddedBase64_IsUnambiguous_AndParsesToTheTrueHash()
    {
        string padded = AmbiguousUnpadded + "=";

        Assert.IsTrue(Hash32.TryParseAuto(Utf8(padded), out Hash32 viaAuto));
        Assert.AreEqual(TrueHash, viaAuto);
        Assert.AreEqual(TrueHash, JsonSerializer.Deserialize<Hash32>("\"" + padded + "\""));
        Assert.AreEqual(7, JsonSerializer.Deserialize<Dictionary<Hash32, int>>("{\"" + padded + "\":7}")![TrueHash]);
    }

    [TestMethod]
    public void UnpaddedBase64_ThatIsAlsoValidBase58_IsReadAsBase58_NeverAsBase64()
    {
        // The documented ambiguity: valid Base58 wins. The explicit Base64 parser still reads it as Base64.
        Assert.IsTrue(Hash32.TryParseBase58(Utf8(AmbiguousUnpadded), out Hash32 asBase58));
        Assert.IsTrue(Hash32.TryParseAuto(Utf8(AmbiguousUnpadded), out Hash32 viaAuto));

        Assert.AreEqual(asBase58, viaAuto);
        Assert.AreNotEqual(TrueHash, viaAuto);
        Assert.IsTrue(Hash32.TryParseBase64(Utf8(AmbiguousUnpadded), out Hash32 asBase64));
        Assert.AreEqual(TrueHash, asBase64);
    }

    [TestMethod]
    public void UnpaddedBase64_ThatIsNotValidBase58_IsRejected_InsteadOfGuessed()
    {
        // 32 x 0xFF: "////...w" unpadded (43 chars). '/' is not a Base58 character.
        var allOnes = new Hash32(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue);
        string unpadded = Convert.ToBase64String(allOnes.ToBigEndianBytes()).TrimEnd('=');
        string urlSafeUnpadded = unpadded.Replace('/', '_').Replace('+', '-');
        Assert.AreEqual(43, unpadded.Length);

        foreach (string text in new[] { unpadded, urlSafeUnpadded })
        {
            Assert.IsFalse(Hash32.TryParseAuto(Utf8(text), out _), text);
            Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Hash32>("\"" + text + "\""), text);
            Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Dictionary<Hash32, int>>("{\"" + text + "\":1}"), text);

            // Padded forms of the same data (standard and URL-safe) still read.
            Assert.IsTrue(Hash32.TryParseAuto(Utf8(text + "="), out Hash32 padded), text);
            Assert.AreEqual(allOnes, padded, text);
            Assert.IsTrue(Hash32.TryParseBase64(Utf8(text), out Hash32 explicitBase64), text);
            Assert.AreEqual(allOnes, explicitBase64, text);
        }
    }

    [TestMethod]
    public void Auto_NeverReturnsABase64Guess_AcrossManyRandomHashes()
    {
        var random = new Random(12345);
        byte[] bytes = new byte[32];

        for (int i = 0; i < 20_000; i++)
        {
            random.NextBytes(bytes);
            var expected = new Hash32(bytes);
            string padded = Convert.ToBase64String(bytes);
            string unpadded = padded.TrimEnd('=');

            // Padded Base64 round-trips: '=' cannot appear in Base58 or hex. The one exception is text that starts with
            // "0x"/"0X" (about 1 in 4096), which is committed to hex by design and rejected (see the dedicated test).
            if (!padded.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                Assert.IsTrue(Hash32.TryParseAuto(Utf8(padded), out Hash32 viaPadded), padded);
                Assert.AreEqual(expected, viaPadded, padded);
            }

            // Unpadded text either is not accepted, or is accepted as exactly its Base58 reading, never as Base64.
            if (Hash32.TryParseAuto(Utf8(unpadded), out Hash32 viaUnpadded))
            {
                Assert.IsTrue(Hash32.TryParseBase58(Utf8(unpadded), out Hash32 asBase58), unpadded);
                Assert.AreEqual(asBase58, viaUnpadded, unpadded);
            }
        }
    }

    [TestMethod]
    public void PaddedBase64_StartingWith0x_IsCommittedToHex_AndRejectedLoudly()
    {
        // Existing rule (TryParseAuto_0xPrefix_ForcesHexOnly_NoFallback): a "0x" prefix means hex only. A padded Base64
        // string that happens to begin with "0x" therefore fails; it is never misread. Explicit Base64 still works.
        byte[] bytes = new byte[32];
        // 0xD3 0x1F 0x.. encodes to Base64 text starting with "0x" ('0' = 52 -> 110100, 'x' = 49 -> 110001 -> 0xD3 0x1_).
        bytes[0] = 0xD3;
        bytes[1] = 0x1F;
        string padded = Convert.ToBase64String(bytes);
        Assert.StartsWith("0x", padded);

        Assert.IsFalse(Hash32.TryParseAuto(Utf8(padded), out _));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Hash32>("\"" + padded + "\""));
        Assert.IsTrue(Hash32.TryParseBase64(Utf8(padded), out Hash32 explicitBase64));
        Assert.AreEqual(new Hash32(bytes), explicitBase64);
    }
}
