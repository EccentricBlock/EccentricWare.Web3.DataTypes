using System.Globalization;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// EVM-specific tests for int256 behaviour where two's complement interpretation is material
/// (ABI words and 64-hex-digit JSON/trace representations).
/// </summary>
[TestClass]
public sealed class Int256EvmSpecificTests
{
    private static readonly string HexMinusOne64Lower = new string('f', 64);
    private static readonly string HexMinusOne64Upper = new string('F', 64);
    private static readonly string HexMinValue64Lower = "8" + new string('0', 63);

    [TestMethod]
    public void Evm_AbiEncoding_Int256_TwosComplement_WordEncodings_AreCorrect()
    {
        Span<byte> be = stackalloc byte[32];

        // int256(1) => 31x00 + 0x01
        int256.One.WriteBigEndian(be);
        Assert.IsTrue(be.Slice(0, 31).ToArray().All(b => b == 0x00));
        Assert.AreEqual(0x01, be[31]);

        // int256(-1) => 32xFF
        int256.MinusOne.WriteBigEndian(be);
        Assert.IsTrue(be.ToArray().All(b => b == 0xFF));

        // int256.MinValue => 0x80 followed by 31x00
        int256.MinValue.WriteBigEndian(be);
        Assert.AreEqual(0x80, be[0]);
        Assert.IsTrue(be.Slice(1).ToArray().All(b => b == 0x00));
    }

    [TestMethod]
    public void Evm_Int256_HexFormatting_Negatives_AreFullWidthTwosComplement()
    {
        Assert.AreEqual("0x" + HexMinusOne64Lower, int256.MinusOne.ToString("0x", CultureInfo.InvariantCulture));
        Assert.AreEqual("0x" + HexMinusOne64Upper, int256.MinusOne.ToString("0X", CultureInfo.InvariantCulture));
        Assert.AreEqual("0x" + HexMinValue64Lower, int256.MinValue.ToString("0x", CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void Evm_Int256_Parse_64HexDigits_IsRawTwosComplement()
    {
        // Exactly 64 hex digits should be interpreted as raw two's complement (ABI-like).
        Assert.IsTrue(int256.TryParse(("0x" + HexMinusOne64Lower).AsSpan(), CultureInfo.InvariantCulture, out var m1));
        Assert.AreEqual(int256.MinusOne, m1);

        Assert.IsTrue(int256.TryParse(("0x" + HexMinValue64Lower).AsSpan(), CultureInfo.InvariantCulture, out var min));
        Assert.AreEqual(int256.MinValue, min);
    }
}
