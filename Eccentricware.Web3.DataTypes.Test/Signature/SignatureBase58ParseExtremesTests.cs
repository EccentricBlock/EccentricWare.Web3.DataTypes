using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Parsing Solana signatures from Base58 at the edges of the 64-byte range: all zero, all 0xFF (the largest value, 88
/// characters), a leading and a trailing single bit, and values that no longer fit in 512 bits.
/// </summary>
[TestClass]
public sealed class SignatureBase58ParseExtremesTests
{
    private static byte[] Repeated(byte value, int count) => Enumerable.Repeat(value, count).ToArray();

    private static byte[] Padded(byte first, byte last)
    {
        byte[] bytes = new byte[64];
        bytes[0] = first;
        bytes[63] = last;
        return bytes;
    }

    // Vectors from an independent Base58 implementation (Bitcoin alphabet; each leading zero byte is one leading '1').
    public static IEnumerable<(string Base58, byte[] Payload)> Vectors { get; } =
    [
        (new string('1', 64), new byte[64]),
        ("67rpwLCuS5DGA8KGZXKsVQ7dnPb9goRLoKfgGbLfQg9WoLUgNY77E2jT11fem3coV9nAkguBACzrU1iyZM4B8roQ", Repeated(0xFF, 64)),
        (new string('1', 63) + "2", Padded(0x00, 0x01)),
        ("2AFv15MNPuA84RmU66xw2uMzGipcVxNpzAffoacGVvjFue3CBmf633fAWuiP9cwL9C3z3CJiGgRSFjJfeEcA6QX", Padded(0x01, 0x00)),
    ];

    [TestMethod]
    public void ExtremeValues_ParseToTheExactPayload_ThroughEveryEntryPoint()
    {
        foreach ((string text, byte[] payload) in Vectors)
        {
            Signature fromChars = Signature.Parse(text.AsSpan());
            Signature fromUtf8 = Signature.Parse(Encoding.ASCII.GetBytes(text));
            Signature fromJson = JsonSerializer.Deserialize<Signature>("\"" + text + "\"");

            foreach (Signature parsed in new[] { fromChars, fromUtf8, fromJson })
            {
                Assert.AreEqual(SignatureType.Solana, parsed.Type, text);
                CollectionAssert.AreEqual(payload, parsed.ToBytes(), text);
            }

            Assert.IsTrue(Signature.TryParse(text.AsSpan(), out Signature tried), text);
            Assert.AreEqual(fromChars, tried, text);
        }
    }

    [TestMethod]
    public void ExtremeValues_RoundTripThroughBase58()
    {
        foreach ((string text, byte[] payload) in Vectors)
            Assert.AreEqual(text, Signature.FromSolanaBytes(payload).ToBase58String(), text);
    }

    [TestMethod]
    public void ValuesAbove512Bits_AreRejected()
    {
        // 58^88 is about 2^515.5, so 88 'z' characters (the largest digit) exceed 2^512 - 1 while still being 88 characters long;
        // 89 characters are over the length limit.
        foreach (string text in new[] { new string('z', 88), new string('z', 89), "2" + new string('1', 88) })
        {
            Assert.IsFalse(Signature.TryParse(text.AsSpan(), out Signature fromChars), text);
            Assert.AreEqual(default, fromChars, text);
            Assert.IsFalse(Signature.TryParse(Encoding.ASCII.GetBytes(text), out Signature fromUtf8), text);
            Assert.AreEqual(default, fromUtf8, text);
            Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Signature>("\"" + text + "\""), text);
        }
    }
}
