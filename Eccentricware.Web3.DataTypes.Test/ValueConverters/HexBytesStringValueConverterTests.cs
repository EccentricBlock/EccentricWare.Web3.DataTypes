using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="HexBytesStringValueConverter"/>: canonical lowercase <c>0x</c> text on write, lenient on read.</summary>
[TestClass]
public sealed class HexBytesStringValueConverterTests
{
    private static readonly HexBytesStringValueConverter Converter = new();

    // ----------------- write -----------------

    [TestMethod]
    public void ToProvider_WritesCanonicalLowercaseHex()
    {
        Assert.AreEqual("0xdeadbeef", Converter.ConvertToProviderTyped(HexBytes.Parse("0xDEADBEEF")));
        Assert.AreEqual("0x00", Converter.ConvertToProviderTyped(new HexBytes([0x00])));
        Assert.AreEqual("0x", Converter.ConvertToProviderTyped(HexBytes.Empty));
        Assert.AreEqual("0x", Converter.ConvertToProviderTyped(default));
    }

    // ----------------- lenient read -----------------

    [TestMethod]
    [DataRow("0xdeadbeef")]
    [DataRow("DEADBEEF")]
    [DataRow("0XdEaDbEeF")]
    [DataRow(" 0xdeadbeef ")]
    [DataRow("\"0xdeadbeef\"")]
    public void FromProvider_AcceptsPrefixCaseWhitespaceAndQuotes(string stored)
        => Assert.AreEqual(new HexBytes([0xde, 0xad, 0xbe, 0xef]), Converter.ConvertFromProviderTyped(stored));

    [TestMethod]
    public void FromProvider_OddDigitCount_IsLeftPaddedWithAZero()
        => Assert.AreEqual(new HexBytes([0x0a, 0xbc]), Converter.ConvertFromProviderTyped("0xabc"));

    [TestMethod]
    [DataRow("")]
    [DataRow("  ")]
    [DataRow("0x")]
    [DataRow("0X")]
    public void FromProvider_BlankOrPrefixOnly_ReadsAsEmpty(string stored)
        => Assert.AreEqual(HexBytes.Empty, Converter.ConvertFromProviderTyped(stored));

    // ----------------- invalid data -----------------

    [TestMethod]
    [DataRow("0xzz")]
    [DataRow("nope")]
    [DataRow("0xde ad")]
    [DataRow("0xde\0ad")]
    [DataRow("0xdeadbeeg")]
    public void FromProvider_NonHex_ThrowsFormatException(string stored)
        => Assert.ThrowsExactly<FormatException>(() => Converter.ConvertFromProviderTyped(stored));

    [TestMethod]
    public void FromProvider_FullWidthDigits_AreNotHex()
        => Assert.ThrowsExactly<FormatException>(() => Converter.ConvertFromProviderTyped("0x１２"));

    [TestMethod]
    public void FromProvider_MegabyteOfNonHex_ThrowsFormatException()
        => Assert.ThrowsExactly<FormatException>(() => Converter.ConvertFromProviderTyped("0x" + new string('z', 1024 * 1024)));

    [TestMethod]
    public void FromProvider_Null_ThrowsArgumentNullException()
        => Assert.ThrowsExactly<ArgumentNullException>(() => Converter.ConvertFromProviderTyped(null!));

    // ----------------- round trip -----------------

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(31)]
    [DataRow(32)]
    [DataRow(33)]
    [DataRow(5000)]
    [DataRow(512 * 1024)]
    public void RoundTrip_PreservesTheBytes_AtEveryLength(int length)
    {
        var value = new HexBytes(ConverterTestData.RandomBytes(new Random(length), length));

        string stored = Converter.ConvertToProviderTyped(value);

        Assert.AreEqual(2 + 2 * length, stored.Length);
        Assert.AreEqual(Convert.ToHexString(value.ToArray()).ToLowerInvariant(), stored[2..]);
        Assert.AreEqual(value, Converter.ConvertFromProviderTyped(stored));
    }

    [TestMethod]
    public void RoundTrip_Empty_AndUntyped()
    {
        Assert.AreEqual(HexBytes.Empty, Converter.ConvertFromProviderTyped(Converter.ConvertToProviderTyped(HexBytes.Empty)));

        HexBytes value = HexBytes.Parse("0xabcd");
        object? provider = Converter.ConvertToProvider(value);
        Assert.AreEqual("0xabcd", provider);
        Assert.AreEqual(value, Converter.ConvertFromProvider(provider));
        Assert.IsNull(Converter.ConvertToProvider(null));
        Assert.IsNull(Converter.ConvertFromProvider(null));
    }

    [TestMethod]
    public void MappingHints_AreOnlyWhatTheCallerSupplies()
    {
        Assert.IsNull(Converter.MappingHints);
        Assert.AreEqual(5, new HexBytesStringValueConverter(new ConverterMappingHints(size: 5)).MappingHints?.Size);
    }
}
