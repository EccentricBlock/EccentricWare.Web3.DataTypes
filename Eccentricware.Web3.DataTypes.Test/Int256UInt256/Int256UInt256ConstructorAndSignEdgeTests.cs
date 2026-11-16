using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class Int256UInt256ConstructorAndSignEdgeTests
{
    // ---- int256 sign edges on the native multiply / divide ----

    [TestMethod]
    public void Int256_MinValue_EdgeCases_Multiply()
    {
        Assert.AreEqual(int256.MinValue, int256.MinValue * int256.One);
        Assert.AreEqual(int256.MinValue, int256.One * int256.MinValue);
        Assert.AreEqual(int256.Zero, int256.MinValue * int256.Zero);
        Assert.AreEqual(int256.Zero, int256.Zero * int256.MinValue);
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.MinValue * int256.MinusOne);
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.MinusOne * int256.MinValue);
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.MinValue * new int256(2));
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.MinValue * int256.MinValue);
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.MaxValue * int256.MaxValue);

        // -2^254 * 2 = -2^255 fits exactly; 2^254 * 2 = 2^255 does not.
        int256 minusTwoPow254 = (int256)(-(BigInteger.One << 254));
        int256 twoPow254 = (int256)(BigInteger.One << 254);
        Assert.AreEqual(int256.MinValue, minusTwoPow254 * new int256(2));
        Assert.ThrowsExactly<OverflowException>(() => _ = twoPow254 * new int256(2));
        Assert.AreEqual(new int256(-1) * int256.MaxValue, -int256.MaxValue);
    }

    [TestMethod]
    public void Int256_MinValue_EdgeCases_DivideAndModulo()
    {
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.MinValue / int256.MinusOne);
        Assert.AreEqual(int256.Zero, int256.MinValue % int256.MinusOne);
        Assert.AreEqual(int256.MinValue, int256.MinValue / int256.One);
        Assert.AreEqual(int256.One, int256.MinValue / int256.MinValue);
        Assert.AreEqual(int256.Zero, int256.MinValue % int256.MinValue);
        Assert.AreEqual(int256.Zero, int256.MaxValue / int256.MinValue);
        Assert.AreEqual(int256.MaxValue, int256.MaxValue % int256.MinValue);
        Assert.AreEqual(int256.MinusOne, int256.MinValue / int256.MaxValue); // -2^255 / (2^255 - 1) truncates to -1
        Assert.AreEqual(int256.MinusOne, int256.MinValue % int256.MaxValue);
        Assert.AreEqual(int256.MinusOne, int256.MaxValue / (int256)(-(BigInteger)int256.MaxValue));
    }

    [TestMethod]
    public void Int256_Division_TruncatesTowardZero_RemainderTakesDividendSign()
    {
        // The four sign combinations (EVM SDIV / SMOD semantics).
        Assert.AreEqual(new int256(2), new int256(7) / new int256(3));
        Assert.AreEqual(new int256(1), new int256(7) % new int256(3));
        Assert.AreEqual(new int256(-2), new int256(-7) / new int256(3));
        Assert.AreEqual(new int256(-1), new int256(-7) % new int256(3));
        Assert.AreEqual(new int256(-2), new int256(7) / new int256(-3));
        Assert.AreEqual(new int256(1), new int256(7) % new int256(-3));
        Assert.AreEqual(new int256(2), new int256(-7) / new int256(-3));
        Assert.AreEqual(new int256(-1), new int256(-7) % new int256(-3));
        Assert.AreEqual(int256.Zero, new int256(-1) / new int256(2)); // no negative zero artefacts
    }

    [TestMethod]
    public void Int256_Multiply_NegativeZeroProduct_IsZero()
    {
        Assert.AreEqual(int256.Zero, int256.Zero * new int256(-5));
        Assert.AreEqual(int256.Zero, new int256(-5) * int256.Zero);
        Assert.IsTrue((int256.Zero * int256.MinusOne).IsZero);
    }

    // ---- uint256 division shapes ----

    [TestMethod]
    public void UInt256_Division_ByPowersOfTwoAndNearMultiples_MatchesBigInteger()
    {
        uint256 dividend = new(0xDEADBEEFDEADBEEFUL, 0xCAFEBABECAFEBABEUL, 0x0123456789ABCDEFUL, 0xFEDCBA9876543210UL);
        BigInteger big = dividend.ToBigInteger();

        for (int bits = 0; bits < 256; bits += 7)
        {
            foreach (BigInteger divisor in new[] { BigInteger.One << bits, (BigInteger.One << bits) - 1, (BigInteger.One << bits) + 1 })
            {
                if (divisor.IsZero) continue;
                uint256 d = (uint256)divisor;
                Assert.AreEqual(big / divisor, (dividend / d).ToBigInteger(), $"/ {divisor}");
                Assert.AreEqual(big % divisor, (dividend % d).ToBigInteger(), $"% {divisor}");
            }
        }
    }

    [TestMethod]
    public void UInt256_Division_DividendEqualsOrBelowDivisor()
    {
        uint256 value = new(1, 2, 3, 4);
        Assert.AreEqual(uint256.One, value / value);
        Assert.AreEqual(uint256.Zero, value % value);
        Assert.AreEqual(uint256.Zero, (value - uint256.One) / value);
        Assert.AreEqual(value - uint256.One, (value - uint256.One) % value);
        Assert.AreEqual(uint256.Zero, uint256.Zero / value);
        Assert.AreEqual(uint256.MaxValue, uint256.MaxValue / uint256.One);
        Assert.AreEqual(uint256.One, uint256.MaxValue / uint256.MaxValue);
        Assert.AreEqual(uint256.Zero, uint256.MaxValue % uint256.MaxValue);
    }

    // ---- constructors at every length ----

    [TestMethod]
    public void UInt256_Constructors_AllInputLengths_MatchBigInteger()
    {
        var rng = new Random(21);
        for (int length = 0; length <= 32; length++)
        {
            byte[] bytes = new byte[length];
            rng.NextBytes(bytes);
            if (length > 0) bytes[0] |= 0x80; // exercise the top bit

            BigInteger beExpected = new(bytes, isUnsigned: true, isBigEndian: true);
            BigInteger leExpected = new(bytes, isUnsigned: true, isBigEndian: false);

            Assert.AreEqual(beExpected, new uint256(bytes).ToBigInteger(), $"big-endian length {length}");
            Assert.AreEqual(leExpected, uint256.FromLittleEndian(bytes).ToBigInteger(), $"little-endian length {length}");
        }

        Assert.ThrowsExactly<ArgumentException>(() => new uint256(new byte[33]));
        Assert.ThrowsExactly<ArgumentException>(() => uint256.FromLittleEndian(new byte[33]));
    }

    [TestMethod]
    public void Int256_Constructor_AllInputLengths_SignExtendsLikeBigInteger()
    {
        var rng = new Random(22);
        for (int length = 0; length <= 32; length++)
        {
            foreach (bool negative in new[] { false, true })
            {
                byte[] bytes = new byte[length];
                rng.NextBytes(bytes);
                if (length > 0)
                    bytes[0] = (byte)(negative ? bytes[0] | 0x80 : bytes[0] & 0x7F);

                BigInteger expected = new(bytes, isUnsigned: false, isBigEndian: true);
                Assert.AreEqual(expected, new int256(bytes).ToBigInteger(), $"length {length}, negative {negative}");
            }
        }

        Assert.ThrowsExactly<ArgumentException>(() => new int256(new byte[33]));
    }
}
