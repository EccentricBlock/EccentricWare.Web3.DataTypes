using EccentricWare.Web3.DataTypes.ValueConverters;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Equality, hashing and ordering skip the type check on v because every producer stores v == 0 for Solana. These tests
/// build one value through every producer and require identical behaviour, so a stray v from any path fails loudly.
/// </summary>
[TestClass]
public sealed class SignatureProducerConsistencyTests
{
    private static readonly SignatureValueConverter Converter = new();

    private static Signature FromChars(string text) => Signature.Parse(text.AsSpan());

    private static Signature FromUtf8(string text) => Signature.Parse(SignatureTestData.Utf8(text));

    private static Signature FromToken(string text)
    {
        Assert.IsTrue(Signature.TryParseToken(SignatureTestData.Utf8(text), out Signature value), text);
        return value;
    }

    private static Signature FromJson(string text) => JsonSerializer.Deserialize<Signature>("\"" + text + "\"");

    private static Signature ThroughConverter(Signature value) => Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(value));

    private static IEnumerable<(string Name, Signature Value)> SolanaProducers(byte[] payload)
    {
        string lower = Convert.ToHexString(payload).ToLowerInvariant();
        string upper = lower.ToUpperInvariant();
        string base58 = SignatureTestData.Base58(payload);

        yield return ("FromSolanaBytes", Signature.FromSolanaBytes(payload));

        foreach (string hex in new[] { lower, "0x" + lower, "0X" + lower, upper, "0x" + upper, " 0x" + lower + "\r\n" })
        {
            yield return ($"chars hex {hex[..Math.Min(6, hex.Length)]}", FromChars(hex));
            yield return ($"utf8 hex {hex[..Math.Min(6, hex.Length)]}", FromUtf8(hex));
            yield return ($"quoted utf8 hex {hex[..Math.Min(6, hex.Length)]}", FromUtf8("\"" + hex.Trim() + "\""));
            yield return ($"token hex {hex[..Math.Min(6, hex.Length)]}", FromToken(hex));
            yield return ($"json hex {hex[..Math.Min(6, hex.Length)]}", FromJson(hex.Trim()));
        }

        yield return ("chars base58", FromChars(base58));
        yield return ("utf8 base58", FromUtf8(base58));
        yield return ("quoted utf8 base58", FromUtf8("\"" + base58 + "\""));
        yield return ("token base58", FromToken(base58));
        yield return ("json base58", FromJson(base58));
        yield return ("Parse(string, provider)", Signature.Parse(base58, null));
        yield return ("IParsable via hex", Signature.Parse("0x" + lower, null));
        yield return ("value converter", ThroughConverter(Signature.FromSolanaBytes(payload)));
        yield return ("bytes round trip", Signature.FromSolanaBytes(Signature.FromSolanaBytes(payload).ToBytes()));
        yield return ("format then parse", FromChars(Signature.FromSolanaBytes(payload).ToString("X")));
    }

    private static IEnumerable<(string Name, Signature Value)> EvmProducers(byte[] payload, byte v)
    {
        byte[] bytes = payload.Append(v).ToArray();
        string lower = Convert.ToHexString(bytes).ToLowerInvariant();
        string upper = lower.ToUpperInvariant();

        yield return ("FromEvmBytes", Signature.FromEvmBytes(bytes));
        yield return ("FromEvmComponents", Signature.FromEvmComponents(payload.AsSpan(0, 32), payload.AsSpan(32, 32), v));

        foreach (string hex in new[] { lower, "0x" + lower, "0X" + upper, " 0x" + lower + "\t" })
        {
            yield return ($"chars {hex[..6]}", FromChars(hex));
            yield return ($"utf8 {hex[..6]}", FromUtf8(hex));
            yield return ($"quoted utf8 {hex[..6]}", FromUtf8("\"" + hex.Trim() + "\""));
            yield return ($"token {hex[..6]}", FromToken(hex));
            yield return ($"json {hex[..6]}", FromJson(hex.Trim()));
        }

        yield return ("value converter", ThroughConverter(Signature.FromEvmBytes(bytes)));
        yield return ("bytes round trip", Signature.FromEvmBytes(Signature.FromEvmBytes(bytes).ToBytes()));
    }

    private static void AssertSame(Signature reference, Signature candidate, string name, SignatureType type, byte[] expectedBytes)
    {
        Assert.AreEqual(type, candidate.Type, name);
        Assert.AreEqual(reference.V, candidate.V, name);
        Assert.AreEqual(expectedBytes.Length, candidate.ToBytes().Length, name);
        CollectionAssert.AreEqual(expectedBytes, candidate.ToBytes(), name);
        Assert.AreEqual(reference, candidate, name);
        Assert.IsTrue(candidate.Equals(reference), name);
        Assert.IsTrue(reference == candidate, name);
        Assert.AreEqual(reference.GetHashCode(), candidate.GetHashCode(), name);
        Assert.AreEqual(0, reference.CompareTo(candidate), name);
        Assert.AreEqual(0, candidate.CompareTo(reference), name);
        Assert.IsTrue(new Dictionary<Signature, int> { [reference] = 1 }.ContainsKey(candidate), name);
        Assert.IsTrue(new HashSet<Signature> { reference }.Contains(candidate), name);
    }

    [TestMethod]
    public void EverySolanaProducer_GivesTheSameValue_WithVZero()
    {
        foreach (byte[] payload in SignatureTestData.Payloads64())
        {
            Signature reference = Signature.FromSolanaBytes(payload);
            Assert.AreEqual(0, reference.V);

            int count = 0;
            foreach ((string name, Signature candidate) in SolanaProducers(payload))
            {
                AssertSame(reference, candidate, $"{name} [{Convert.ToHexString(payload, 0, 3)}]", SignatureType.Solana, payload);
                Assert.AreEqual(0, candidate.V, name);
                count++;
            }

            Assert.IsGreaterThan(30, count, "the producer list must not silently shrink");
        }
    }

    [TestMethod]
    public void EverySolanaProducer_OfTheAllZeroPayload_EqualsZeroSolana()
    {
        foreach ((string name, Signature candidate) in SolanaProducers(new byte[64]))
        {
            AssertSame(Signature.ZeroSolana, candidate, name, SignatureType.Solana, new byte[64]);
            Assert.IsTrue(candidate.IsZero, name);
            Assert.AreNotEqual(Signature.ZeroEvm, candidate, name);
        }

        // The same value has three spellings: 128 zero digits, "0x" + 128 zero digits and 64 '1' characters.
        Assert.AreEqual(Signature.ZeroSolana, FromChars(new string('0', 128)));
        Assert.AreEqual(Signature.ZeroSolana, FromChars(new string('1', 64)));
    }

    [TestMethod]
    [DataRow((byte)0x1b)]
    [DataRow((byte)0x00)]
    [DataRow((byte)0xFF)]
    public void EveryEvmProducer_GivesTheSameValue_IncludingV(byte v)
    {
        foreach (byte[] payload in SignatureTestData.Payloads64())
        {
            Signature reference = SignatureTestData.EvmFrom(payload, v);

            int count = 0;
            foreach ((string name, Signature candidate) in EvmProducers(payload, v))
            {
                AssertSame(reference, candidate, $"{name} [{Convert.ToHexString(payload, 0, 3)}] v={v:x2}", SignatureType.Evm, payload.Append(v).ToArray());
                Assert.AreEqual(v, candidate.V, name);
                count++;
            }

            Assert.IsGreaterThan(15, count, "the producer list must not silently shrink");
        }
    }
}
