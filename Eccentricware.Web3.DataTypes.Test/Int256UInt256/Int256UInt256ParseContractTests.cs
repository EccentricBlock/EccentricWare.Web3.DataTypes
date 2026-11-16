using System.Globalization;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Argument and sign-handling contracts of uint256 / int256 parsing tightened after the coverage review.</summary>
[TestClass]
public sealed class Int256UInt256ParseContractTests
{
    [TestMethod]
    public void UInt256_ParseOfANullString_ThrowsArgumentNullException_OnBothOverloads()
    {
        // These used to return FormatException because a null string became an empty span; every other type already threw
        // ArgumentNullException for null.
        ArgumentNullException plain = Assert.ThrowsExactly<ArgumentNullException>(() => uint256.Parse((string)null!));
        ArgumentNullException withProvider = Assert.ThrowsExactly<ArgumentNullException>(() => uint256.Parse((string)null!, CultureInfo.InvariantCulture));
        Assert.AreEqual("s", plain.ParamName);
        Assert.AreEqual("s", withProvider.ParamName);

        // An empty (non-null) string is still a format error.
        Assert.ThrowsExactly<FormatException>(() => uint256.Parse(string.Empty));
        Assert.ThrowsExactly<FormatException>(() => uint256.Parse(string.Empty, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void Int256_ParseOfANullString_ThrowsArgumentNullException_OnBothOverloads()
    {
        ArgumentNullException plain = Assert.ThrowsExactly<ArgumentNullException>(() => int256.Parse((string)null!));
        ArgumentNullException withProvider = Assert.ThrowsExactly<ArgumentNullException>(() => int256.Parse((string)null!, CultureInfo.InvariantCulture));
        Assert.AreEqual("s", plain.ParamName);
        Assert.AreEqual("s", withProvider.ParamName);

        Assert.ThrowsExactly<FormatException>(() => int256.Parse(string.Empty));
        Assert.ThrowsExactly<FormatException>(() => int256.Parse(string.Empty, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void UInt256AndInt256_Parse_StillParseValidStrings()
    {
        Assert.AreEqual(new uint256(255UL), uint256.Parse("0xff"));
        Assert.AreEqual(new uint256(255UL), uint256.Parse("255", CultureInfo.InvariantCulture));
        Assert.AreEqual(new int256(-26L), int256.Parse("-26"));
        Assert.AreEqual(new int256(26L), int256.Parse("0x1a", CultureInfo.InvariantCulture));
    }

    [TestMethod]
    [DataRow("-5", true, -5)]
    [DataRow("5", true, 5)]
    [DataRow("+5", true, 5)]
    [DataRow("  -5  ", true, -5)] // surrounding whitespace is trimmed
    [DataRow("- 5", false, 0)] // no whitespace between the sign and the digits
    [DataRow("-\t5", false, 0)]
    [DataRow("+ 5", false, 0)]
    [DataRow("- ", false, 0)]
    [DataRow("-", false, 0)]
    [DataRow("", false, 0)]
    public void TryParseDecimal_AllowsOuterWhitespaceOnly_AndAgreesWithTryParse(string text, bool ok, long expected)
    {
        // TryParseDecimal("- 5") used to return -5 while TryParse("- 5") returned false.
        Assert.AreEqual(ok, int256.TryParseDecimal(text, out int256 viaDecimal), text);
        Assert.AreEqual(ok, int256.TryParse(text, CultureInfo.InvariantCulture, out int256 viaGeneral), text);
        Assert.AreEqual(new int256(expected), viaDecimal, text);
        Assert.AreEqual(new int256(expected), viaGeneral, text);
    }

    [TestMethod]
    public void Int256_ByteConstructor_RejectsMoreThan32Bytes_NamingTheRealParameter()
    {
        // The exception used to name "bytes", which is not a parameter of the public constructor.
        foreach (int length in new[] { 33, 34, 64 })
        {
            ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => _ = new int256(new byte[length]));
            Assert.AreEqual("bigEndianTwosComplementBytes", error.ParamName);
        }

        // 32 bytes and fewer are fine, sign-extended from the first byte.
        Assert.AreEqual(new int256(-1L), new int256(new byte[] { 0xFF }));
        Assert.AreEqual(new int256(0x7FL), new int256(new byte[] { 0x7F }));
        Assert.AreEqual(int256.Zero, new int256(ReadOnlySpan<byte>.Empty));
    }
}
