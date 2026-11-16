using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class Hash32Base64ParsingTests
{
    // Bytes whose standard Base64 contains both '+' and '/', so the URL-safe test really changes characters.
    // Base64 from an independent reference encoder: "++++////AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBk=".
    private static readonly byte[] SymbolBytes = Convert.FromHexString("fbefbeffffff000102030405060708090a0b0c0d0e0f10111213141516171819");
    private const string SymbolBase64 = "++++////AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBk=";

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    [TestMethod]
    public void Fixture_StandardBase64_ContainsPlusAndSlash()
    {
        Assert.AreEqual(SymbolBase64, Convert.ToBase64String(SymbolBytes));
        StringAssert.Contains(SymbolBase64, "+");
        StringAssert.Contains(SymbolBase64, "/");
    }

    [TestMethod]
    public void ParseBase64_StandardAlphabet_Works()
    {
        var expected = new Hash32(SymbolBytes);

        Assert.IsTrue(Hash32.TryParseBase64(Ascii(SymbolBase64), out Hash32 parsed));
        Assert.AreEqual(expected, parsed);
        Assert.AreEqual(expected, Hash32.ParseBase64(Ascii(SymbolBase64)));
    }

    [TestMethod]
    public void ParseBase64_UrlSafeAlphabet_Works()
    {
        var expected = new Hash32(SymbolBytes);
        string urlSafe = SymbolBase64.Replace('+', '-').Replace('/', '_');
        Assert.AreNotEqual(SymbolBase64, urlSafe);
        Assert.IsFalse(urlSafe.Contains('+') || urlSafe.Contains('/'));

        Assert.IsTrue(Hash32.TryParseBase64(Ascii(urlSafe), out Hash32 parsed));
        Assert.AreEqual(expected, parsed);
        Assert.AreEqual(expected, Hash32.ParseBase64(Ascii(urlSafe)));
    }

    [TestMethod]
    public void ParseBase64_UnpaddedForms_AreAcceptedByTheExplicitParser()
    {
        var expected = new Hash32(SymbolBytes);
        string unpadded = SymbolBase64.TrimEnd('=');

        Assert.IsTrue(Hash32.TryParseBase64(Ascii(unpadded), out Hash32 standard));
        Assert.IsTrue(Hash32.TryParseBase64(Ascii(unpadded.Replace('+', '-').Replace('/', '_')), out Hash32 urlSafe));
        Assert.AreEqual(expected, standard);
        Assert.AreEqual(expected, urlSafe);
    }

    [TestMethod]
    public void ParseBase64_BytesZeroTo31_RoundTripsThroughTheStandardEncoding()
    {
        byte[] bytes = Hash32TestData.Bytes0To31();
        string b64 = Convert.ToBase64String(bytes);

        Assert.AreEqual(Hash32TestData.Value0To31(), Hash32.ParseBase64(Ascii(b64)));
    }

    [TestMethod]
    public void TryParseBase64_TrimsAsciiWhitespace()
    {
        byte[] utf8 = Ascii("\r\n\t " + SymbolBase64 + "  \n");

        Assert.IsTrue(Hash32.TryParseBase64(utf8, out Hash32 parsed));
        Assert.AreEqual(new Hash32(SymbolBytes), parsed);
    }

    [TestMethod]
    public void TryParseBase64_InteriorWhitespace_IsSkippedByTheBclDecoder_DocumentsCurrentBehaviour()
    {
        // Unlike hex and Base58, embedded space or newlines are accepted (the BCL decoder ignores them, which suits
        // line-wrapped Base64). Pinned so a change of policy is a conscious decision.
        var expected = new Hash32(SymbolBytes);

        Assert.IsTrue(Hash32.TryParseBase64(Ascii(SymbolBase64.Insert(10, " ")), out Hash32 withSpace));
        Assert.IsTrue(Hash32.TryParseBase64(Ascii(SymbolBase64.Insert(10, "\n")), out Hash32 withNewline));
        Assert.AreEqual(expected, withSpace);
        Assert.AreEqual(expected, withNewline);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("!!!!")]
    [DataRow("***")]
    [DataRow("@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@")]
    public void TryParseBase64_EmptyWhitespaceOrInvalidCharacters_IsRejected(string text)
    {
        Assert.IsFalse(Hash32.TryParseBase64(Ascii(text), out Hash32 parsed), text);
        Assert.IsTrue(parsed.IsZero);
        Assert.ThrowsExactly<FormatException>(() => Hash32.ParseBase64(Ascii(text)));
    }

    public static IEnumerable<object[]> WrongDecodedLengths()
    {
        yield return ["31 bytes (padded with ==)", Convert.ToBase64String(Enumerable.Range(0, 31).Select(i => (byte)i).ToArray())];
        yield return ["33 bytes", Convert.ToBase64String(Enumerable.Repeat((byte)0xFF, 33).ToArray())];
        yield return ["33 bytes unpadded", Convert.ToBase64String(Enumerable.Repeat((byte)0xFF, 33).ToArray()).TrimEnd('=')];
        yield return ["64 bytes", Convert.ToBase64String(Enumerable.Repeat((byte)1, 64).ToArray())];
        yield return ["1 byte", Convert.ToBase64String([1])];
        yield return ["valid Base64 over 256 characters", new string('A', 300)];
    }

    [TestMethod]
    [DynamicData(nameof(WrongDecodedLengths))]
    public void TryParseBase64_WrongDecodedLength_IsRejected(string description, string text)
    {
        Assert.IsFalse(Hash32.TryParseBase64(Ascii(text), out Hash32 parsed), description);
        Assert.IsTrue(parsed.IsZero);
        Assert.ThrowsExactly<FormatException>(() => Hash32.ParseBase64(Ascii(text)), description);
    }

    [TestMethod]
    public void ParseBase64_RoundTripsRandomValues()
    {
        foreach (Hash32 value in Hash32TestData.RandomHashes(new Random(99), 500))
        {
            string b64 = Convert.ToBase64String(value.ToBigEndianBytes());
            Assert.AreEqual(value, Hash32.ParseBase64(Ascii(b64)), b64);
        }
    }
}
