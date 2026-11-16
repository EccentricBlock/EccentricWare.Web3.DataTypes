using EccentricWare.Web3.DataTypes.JsonConverters;
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// <see cref="Signature"/>-specific JSON behaviour: EVM (65 byte) and Solana (64 byte) hex, Solana Base58, and the
/// strict rejection of malformed text. The Base58 write option is covered in SignatureBase58JsonTests.
/// </summary>
[TestClass]
public sealed class SignatureJsonConverterTests
{
    // Bytes 0x00..0x40: each byte equals its offset, so any byte-order or length mistake shows up.
    private static readonly byte[] Ascending65 = Enumerable.Range(0, 65).Select(i => (byte)i).ToArray();
    private static readonly string EvmHex = Convert.ToHexString(Ascending65).ToLowerInvariant();
    private static readonly string SolanaHex = EvmHex[..128];
    private static readonly Signature SampleEvm = Signature.FromEvmBytes(Ascending65);
    private static readonly Signature SampleSolana = Signature.FromSolanaBytes(Ascending65.AsSpan(0, 64));

    [TestMethod]
    public void Serialize_WritesQuotedPrefixedLowercaseHex_ForBothTypes()
    {
        Assert.AreEqual("\"0x" + EvmHex + "\"", JsonSerializer.Serialize(SampleEvm));
        Assert.AreEqual("\"0x" + SolanaHex + "\"", JsonSerializer.Serialize(SampleSolana));
    }

    [TestMethod]
    public void DictionaryKeys_AreWrittenAsPrefixedHex_ForBothTypes_AndReadBack()
    {
        var map = new Dictionary<Signature, int> { [SampleEvm] = 1, [SampleSolana] = 2 };

        string json = JsonSerializer.Serialize(map);

        Assert.AreEqual("{\"0x" + EvmHex + "\":1,\"0x" + SolanaHex + "\":2}", json);
        var back = JsonSerializer.Deserialize<Dictionary<Signature, int>>(json)!;
        Assert.AreEqual(1, back[SampleEvm]);
        Assert.AreEqual(2, back[SampleSolana]);
    }

    [TestMethod]
    public void Write_ProducesNoEscaping_AndTheExactLength()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            new SignatureJsonConverter().Write(writer, SampleEvm, new JsonSerializerOptions());

        Assert.AreEqual(134, buffer.WrittenCount); // quote + 0x + 130 digits + quote
        Assert.AreEqual("\"0x" + EvmHex + "\"", Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    [TestMethod]
    public void TypeIsDecidedByTheDigitCount_AndSurvivesARoundTrip()
    {
        Signature evm = JsonSerializer.Deserialize<Signature>("\"0x" + EvmHex + "\"");
        Signature solana = JsonSerializer.Deserialize<Signature>("\"0x" + SolanaHex + "\"");

        Assert.AreEqual(SignatureType.Evm, evm.Type);
        Assert.AreEqual(SignatureType.Solana, solana.Type);
        Assert.AreEqual(SampleEvm, evm);
        Assert.AreEqual(SampleSolana, solana);
    }

    [TestMethod]
    public void RandomSignatures_OfBothTypes_RoundTrip_AndWriteTheirBytesAsHex()
    {
        var random = new Random(20260511);
        for (int i = 0; i < 300; i++)
        {
            byte[] bytes = new byte[i % 2 == 0 ? 65 : 64];
            random.NextBytes(bytes);
            bytes.AsSpan(0, i % 4).Clear(); // leading zero bytes, which Base58 encodes as '1'
            Signature value = bytes.Length == 65 ? Signature.FromEvmBytes(bytes) : Signature.FromSolanaBytes(bytes);

            string json = JsonSerializer.Serialize(value);

            Assert.AreEqual("\"0x" + Convert.ToHexString(bytes).ToLowerInvariant() + "\"", json, $"round {i}");
            Assert.AreEqual(value, JsonSerializer.Deserialize<Signature>(json), $"round {i}");
        }
    }

    [TestMethod]
    public void SolanaBase58_ReadsAsTheSolanaSignatureWithTheSameBytes()
    {
        // vector from independent reference implementation (python3 Base58 decode of the Solana documentation signature)
        Signature value = JsonSerializer.Deserialize<Signature>("\"" + JsonConverterCases.SolanaSignatureBase58 + "\"");

        Assert.AreEqual(SignatureType.Solana, value.Type);
        CollectionAssert.AreEqual(Convert.FromHexString(JsonConverterCases.SolanaSignatureHex), value.ToBytes());
    }

    [TestMethod]
    [DataRow("0x")]
    [DataRow("nope")]
    [DataRow("0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f4")] // 129 digits
    [DataRow("0x000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3fzz")]
    [DataRow("0x" + JsonConverterCases.SolanaSignatureBase58)] // regression: 0x followed by Base58
    [DataRow("22222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222")] // 200 Base58 digits
    public void InvalidText_ThrowsTheInvalidValueError_NotFormatException(string text)
    {
        JsonException error = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Signature>("\"" + text + "\""));

        Assert.AreEqual("Invalid value for Signature.", error.Message);
    }

    [TestMethod]
    public void SolanaBase58_WithAnEscapedCharacterInTheMiddle_ReadsTheSameSignature()
    {
        string base58 = JsonConverterCases.SolanaSignatureBase58;
        string escaped = base58[..10] + JsonTokenBodies.Escape(base58[10]) + base58[11..];

        Signature viaEscape = JsonSerializer.Deserialize<Signature>("\"" + escaped + "\"");

        CollectionAssert.AreEqual(Convert.FromHexString(JsonConverterCases.SolanaSignatureHex), viaEscape.ToBytes());
    }
}
