using EccentricWare.Web3.DataTypes;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Chain-specific behavioural tests for uint256 in EVM and Solana contexts.
/// Focus: JSON-RPC token shapes, ABI word encoding, and canonical constants.
/// </summary>
[TestClass]
public sealed class UInt256EvmSolanaTests
{
    private static readonly BigInteger TenPow18 = BigInteger.Pow(10, 18);
    private static readonly BigInteger TenPow9 = BigInteger.Pow(10, 9);

    [TestMethod]
    public void Evm_WeiPerEther_And_WeiPerGwei_Constants_AreCorrect()
    {
        Assert.AreEqual(TenPow18, uint256.Evm.WeiPerEther.ToBigInteger());
        Assert.AreEqual(TenPow9, uint256.Evm.WeiPerGwei.ToBigInteger());

        // Canonical EVM-style minimal hex (lowercase).
        Assert.AreEqual("0xde0b6b3a7640000", uint256.Evm.WeiPerEther.ToEvmHex());
        Assert.AreEqual("0x3b9aca00", uint256.Evm.WeiPerGwei.ToEvmHex());
    }

    [TestMethod]
    public void Solana_LamportsPerSol_Constant_IsCorrect()
    {
        Assert.AreEqual(TenPow9, uint256.Solana.LamportsPerSol.ToBigInteger());
        Assert.AreEqual("0x3b9aca00", uint256.Solana.LamportsPerSol.ToString("0x", CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void Evm_ToEvmHex_IsMinimal_NoLeadingZeros()
    {
        Assert.AreEqual("0x0", uint256.Zero.ToEvmHex());
        Assert.AreEqual("0x1", uint256.One.ToEvmHex());

        var v = uint256.Parse("0x0001".AsSpan(), CultureInfo.InvariantCulture);
        Assert.AreEqual(uint256.One, v);
        Assert.AreEqual("0x1", v.ToEvmHex());

        var v2 = uint256.Parse("0x0100".AsSpan(), CultureInfo.InvariantCulture);
        Assert.AreEqual(new BigInteger(256), v2.ToBigInteger());
        Assert.AreEqual("0x100", v2.ToEvmHex());
    }

    [TestMethod]
    public void Evm_JsonRpcHexTokens_CommonForms_Parse()
    {
        Assert.IsTrue(uint256.TryParse("0x0".AsSpan(), CultureInfo.InvariantCulture, out var a));
        Assert.AreEqual(uint256.Zero, a);

        Assert.IsTrue(uint256.TryParse("0x1".AsSpan(), CultureInfo.InvariantCulture, out var b));
        Assert.AreEqual(uint256.One, b);

        Assert.IsTrue(uint256.TryParse("0XDE0B6B3A7640000".AsSpan(), CultureInfo.InvariantCulture, out var c));
        Assert.AreEqual(uint256.Evm.WeiPerEther, c);

        // Non-canonical but frequently observed: "0x" => treat as zero.
        Assert.IsTrue(uint256.TryParse("0x".AsSpan(), CultureInfo.InvariantCulture, out var z));
        Assert.AreEqual(uint256.Zero, z);

        // Whitespace trimming.
        Assert.IsTrue(uint256.TryParse("   0x10   ".AsSpan(), CultureInfo.InvariantCulture, out var d));
        Assert.AreEqual(new BigInteger(16), d.ToBigInteger());
    }

    [TestMethod]
    public void Evm_AbiEncoding_Uint256_Is32ByteBigEndianWord_LeftPadded()
    {
        // ABI uint256 is a 32-byte big-endian word.
        byte[] abi = uint256.One.ToAbiEncoded();
        Assert.HasCount(32, abi);

        Assert.IsTrue(abi.Take(31).All(b => b == 0x00));
        Assert.AreEqual(0x01, abi[31]);

        // Round-trip.
        var rt = uint256.FromAbiEncoded(abi);
        Assert.AreEqual(uint256.One, rt);
    }

    [TestMethod]
    public void Evm_AbiEncoding_FromAbiEncoded_IgnoresExtraBytes()
    {
        var v = uint256.Parse("0x1234567890abcdef".AsSpan(), CultureInfo.InvariantCulture);
        byte[] abi = v.ToAbiEncoded();

        // Append extra bytes (e.g., concatenated ABI words) - FromAbiEncoded reads the first 32 bytes.
        byte[] extended = new byte[64];
        Buffer.BlockCopy(abi, 0, extended, 0, 32);
        // second word intentionally non-zero to ensure it is ignored
        extended[63] = 0xAA;

        var parsed = uint256.FromAbiEncoded(extended);
        Assert.AreEqual(v, parsed);
    }

    [TestMethod]
    public void Solana_Lamports_DivRem_ToSol_QuotientAndRemainder()
    {
        // 1.5 SOL expressed as lamports.
        var lamports = new uint256(1_500_000_000UL);
        var (q, r) = uint256.DivRem(lamports, uint256.Solana.LamportsPerSol);

        Assert.AreEqual(uint256.One, q);
        Assert.AreEqual(new uint256(500_000_000UL), r);

        // Also validate against BigInteger.
        BigInteger biLamports = lamports.ToBigInteger();
        BigInteger biDiv = uint256.Solana.LamportsPerSol.ToBigInteger();
        Assert.AreEqual(biLamports / biDiv, q.ToBigInteger());
        Assert.AreEqual(biLamports % biDiv, r.ToBigInteger());
    }

    [TestMethod]
    public void Solana_Lamports_TryMultiply_SolToLamports()
    {
        // Multiply 2 SOL (integer SOL units) into lamports (10^9).
        var sol = new uint256(2UL);

        Assert.IsTrue(sol.TryMultiply(1_000_000_000UL, out var lamports));
        Assert.AreEqual(new uint256(2_000_000_000UL), lamports);

        // Validate against the chain constant.
        Assert.IsTrue(sol.TryMultiply((ulong)uint256.Solana.LamportsPerSol.ToBigInteger(), out var lamports2));
        Assert.AreEqual(lamports, lamports2);
    }

    [TestMethod]
    public void Solana_JsonRpcDecimalTokens_Utf8_QuotedAndUnquoted_Parse()
    {
        // Solana JSON-RPC commonly returns decimals (slot, blockHeight, lamports) as JSON numbers or strings.
        Assert.IsTrue(uint256.TryParse(Encoding.UTF8.GetBytes("123456789"), out var a));
        Assert.AreEqual(new BigInteger(123456789), a.ToBigInteger());

        Assert.IsTrue(uint256.TryParse(Encoding.UTF8.GetBytes("\"123456789\""), out var b));
        Assert.AreEqual(a, b);

        // Whitespace just inside the quotes is trimmed after unquoting.
        Assert.IsTrue(uint256.TryParse(Encoding.UTF8.GetBytes("\"  123456789  \""), out var c));
        Assert.AreEqual(a, c);

        // Decimal lamports example.
        Assert.IsTrue(uint256.TryParse(Encoding.UTF8.GetBytes("1000000000"), out var oneSolLamports));
        Assert.AreEqual(uint256.Solana.LamportsPerSol, oneSolLamports);
    }

    [TestMethod]
    public void Evm_AbiEncoding_FromAbiEncoded_RequiresAtLeast32Bytes()
    {
        foreach (int length in new[] { 0, 1, 31 })
        {
            ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => _ = uint256.FromAbiEncoded(new byte[length]), $"length {length}");
            Assert.AreEqual("data", exception.ParamName);
        }

        Assert.AreEqual(uint256.MaxValue, uint256.FromAbiEncoded(Enumerable.Repeat((byte)0xFF, 32).ToArray()));
    }

    [TestMethod]
    public void Evm_AbiEncoding_RoundTripsAndReturnsFreshArrays()
    {
        var v = uint256.Parse("0x1234567890abcdef");

        byte[] first = v.ToAbiEncoded();
        first[31] ^= 0xFF;

        Assert.AreEqual(v, uint256.FromAbiEncoded(v.ToAbiEncoded()));
        Assert.AreNotEqual(first[31], v.ToAbiEncoded()[31]);
        CollectionAssert.AreEqual(Convert.FromHexString("000000000000000000000000000000000000000000000000" + "1234567890ABCDEF"), v.ToAbiEncoded());
    }
}
