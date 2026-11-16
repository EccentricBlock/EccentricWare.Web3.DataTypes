using System.Globalization;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Cross-type guard: every type that writes hex uses a lowercase "0x" prefix. Format "0X" selects uppercase digits only,
/// through all three formatting paths (string, UTF-16 span, UTF-8 span).
/// </summary>
[TestClass]
public sealed class HexPrefixConsistencyTests
{
    private static void AssertLowercasePrefix<T>(string name, T value)
        where T : IFormattable, ISpanFormattable, IUtf8SpanFormattable
    {
        // "0X": lowercase prefix, digits with no lowercase letters left.
        foreach (string text in FormatAll(value, "0X"))
        {
            Assert.StartsWith("0x", text, $"{name}: \"0X\" output '{text}' must start with a lowercase 0x");
            string digits = text.Substring(2);
            Assert.AreEqual(digits.ToUpperInvariant(), digits, $"{name}: \"0X\" output '{text}' must have uppercase digits");
            Assert.IsTrue(digits.Any(char.IsLetter), $"{name}: sample value must contain hex letters for this check to mean anything");
        }

        // "0x": lowercase prefix and lowercase digits.
        foreach (string text in FormatAll(value, "0x"))
        {
            Assert.StartsWith("0x", text, $"{name}: \"0x\" output '{text}'");
            Assert.AreEqual(text.ToLowerInvariant(), text, $"{name}: \"0x\" output '{text}' must be all lowercase");
        }
    }

    private static string[] FormatAll<T>(T value, string format)
        where T : IFormattable, ISpanFormattable, IUtf8SpanFormattable
    {
        string viaString = value.ToString(format, CultureInfo.InvariantCulture);

        Span<char> chars = stackalloc char[256];
        Assert.IsTrue(value.TryFormat(chars, out int charsWritten, format, CultureInfo.InvariantCulture));

        Span<byte> utf8 = stackalloc byte[256];
        Assert.IsTrue(value.TryFormat(utf8, out int bytesWritten, format, CultureInfo.InvariantCulture));

        return [viaString, new string(chars.Slice(0, charsWritten)), Encoding.ASCII.GetString(utf8.Slice(0, bytesWritten))];
    }

    [TestMethod]
    public void UInt256_UsesLowercasePrefix() => AssertLowercasePrefix("uint256", new uint256(0xABCDEFUL));

    [TestMethod]
    public void Int256_UsesLowercasePrefix_ForPositiveAndNegativeValues()
    {
        AssertLowercasePrefix("int256 positive", new int256(0xABCDEFL));
        AssertLowercasePrefix("int256 negative", new int256(-0xABCDEFL));
    }

    [TestMethod]
    public void Hash32_UsesLowercasePrefix() => AssertLowercasePrefix("Hash32", new Hash32(0xABCDEF0123456789UL, 0xFEDCBA9876543210UL, 0xAAAAAAAAAAAAAAAAUL, 0xBBBBBBBBBBBBBBBBUL));

    [TestMethod]
    public void HexBytes_UsesLowercasePrefix() => AssertLowercasePrefix("HexBytes", new HexBytes([0xAB, 0xCD, 0xEF]));

    [TestMethod]
    public void FunctionSelector_UsesLowercasePrefix() => AssertLowercasePrefix("FunctionSelector", FunctionSelector.Erc20.Transfer);

    [TestMethod]
    public void InstructionDiscriminator_UsesLowercasePrefix() => AssertLowercasePrefix("InstructionDiscriminator", new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL));

    [TestMethod]
    public void HexBigInteger_UsesLowercasePrefix() => AssertLowercasePrefix("HexBigInteger", new HexBigInteger(0xABCDEFUL));

    [TestMethod]
    public void Address_UsesLowercasePrefix()
        => AssertLowercasePrefix("Address", Address.Parse("0x52908400098527886e0f7030069857d2e4169ee7"));

    [TestMethod]
    public void Address_ChecksumFormat_UsesLowercasePrefix()
    {
        Address address = Address.Parse("0x52908400098527886e0f7030069857d2e4169ee7");
        foreach (string format in new[] { "c", "C" })
        {
            foreach (string text in FormatAll(address, format))
            {
                Assert.StartsWith("0x", text, $"checksum '{format}' output '{text}'");
                Assert.AreEqual("0x52908400098527886E0F7030069857D2E4169EE7", text); // the EIP-55 specification example
            }
        }
    }

    [TestMethod]
    public void Signature_UsesLowercasePrefix()
        => AssertLowercasePrefix("Signature", Signature.Parse("0x" + string.Concat(Enumerable.Repeat("ab", 65))));
}
