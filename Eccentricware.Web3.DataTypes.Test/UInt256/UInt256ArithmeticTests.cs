using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Checked and unchecked arithmetic of uint256 against BigInteger, on limb-boundary and sparse operands.</summary>
[TestClass]
public sealed class UInt256ArithmeticTests
{
    private static readonly BigInteger Two256 = UInt256TestValues.Two256;

    private static uint256 U(BigInteger value) => UInt256TestValues.FromBig(value);

    private static BigInteger Big(uint256 value) => UInt256TestValues.ToBig(value);

    /// <summary>Boundary pairs plus seeded sparse pairs (sparse limbs keep many products below 2^256).</summary>
    private static IEnumerable<(uint256 Left, uint256 Right)> Pairs(int seed, int randomCount)
    {
        foreach (BigInteger a in UInt256TestValues.Boundaries)
            foreach (BigInteger b in UInt256TestValues.Boundaries)
                yield return (U(a), U(b));

        var rng = new Random(seed);
        for (int i = 0; i < randomCount; i++)
            yield return (UInt256TestValues.SparseValue(rng), UInt256TestValues.SparseValue(rng));
    }

    // ---- addition, subtraction, multiplication ----

    [TestMethod]
    public void Addition_CheckedAndUnchecked_MatchBigInteger()
    {
        int overflowed = 0, fitted = 0;
        foreach (var (x, y) in Pairs(222, 500))
        {
            BigInteger sum = Big(x) + Big(y);
            Assert.AreEqual(sum % Two256, Big(uint256.AddUnchecked(x, y)));
            if (sum >= Two256)
            {
                overflowed++;
                Assert.ThrowsExactly<OverflowException>(() => _ = x + y, $"{x} + {y}");
            }
            else
            {
                fitted++;
                Assert.AreEqual(sum, Big(x + y));
            }
        }

        Assert.IsGreaterThan(0, overflowed);
        Assert.IsGreaterThan(0, fitted);
    }

    [TestMethod]
    public void Subtraction_CheckedAndUnchecked_MatchBigInteger()
    {
        int underflowed = 0, fitted = 0;
        foreach (var (x, y) in Pairs(333, 500))
        {
            BigInteger difference = Big(x) - Big(y);
            Assert.AreEqual((difference % Two256 + Two256) % Two256, Big(uint256.SubtractUnchecked(x, y)));
            if (difference < 0)
            {
                underflowed++;
                Assert.ThrowsExactly<OverflowException>(() => _ = x - y, $"{x} - {y}");
            }
            else
            {
                fitted++;
                Assert.AreEqual(difference, Big(x - y));
            }
        }

        Assert.IsGreaterThan(0, underflowed);
        Assert.IsGreaterThan(0, fitted);
    }

    [TestMethod]
    public void Multiplication_CheckedAndUnchecked_MatchBigInteger_AndReachBothBranches()
    {
        int overflowed = 0, fitted = 0;
        foreach (var (x, y) in Pairs(444, 1000))
        {
            BigInteger product = Big(x) * Big(y);
            Assert.AreEqual(product % Two256, Big(uint256.MultiplyUnchecked(x, y)), $"{x} * {y} (unchecked)");
            if (product >= Two256)
            {
                overflowed++;
                Assert.ThrowsExactly<OverflowException>(() => _ = x * y, $"{x} * {y}");
            }
            else
            {
                fitted++;
                Assert.AreEqual(product, Big(x * y), $"{x} * {y}");
            }
        }

        // The sparse generator must produce non-overflowing products with many limbs, not only the trivial ones.
        Assert.IsGreaterThan(100, overflowed);
        Assert.IsGreaterThan(100, fitted);
    }

    [TestMethod]
    public void NamedBoundaryResults()
    {
        Assert.AreEqual(uint256.Zero, uint256.AddUnchecked(uint256.MaxValue, uint256.One));
        Assert.AreEqual(uint256.MaxValue, uint256.SubtractUnchecked(uint256.Zero, uint256.One));
        Assert.AreEqual(Two256 - 2, Big(uint256.MultiplyUnchecked(uint256.MaxValue, new uint256(2UL))));
        Assert.ThrowsExactly<OverflowException>(() => _ = uint256.MaxValue + uint256.One);
        Assert.ThrowsExactly<OverflowException>(() => _ = uint256.Zero - uint256.One);
        Assert.ThrowsExactly<OverflowException>(() => _ = uint256.MaxValue * new uint256(2UL));
        Assert.AreEqual(uint256.MaxValue, uint256.MaxValue * uint256.One);
    }

    // ---- division ----

    [TestMethod]
    public void Division_AllForms_MatchBigInteger()
    {
        foreach (var (x, y) in Pairs(555, 1000))
        {
            if (y.IsZero) continue;
            BigInteger expectedQuotient = BigInteger.DivRem(Big(x), Big(y), out BigInteger expectedRemainder);

            var (quotient, remainder) = uint256.DivRem(x, y);

            Assert.AreEqual(expectedQuotient, Big(quotient), $"{x} / {y}");
            Assert.AreEqual(expectedRemainder, Big(remainder), $"{x} % {y}");
            Assert.AreEqual(quotient, x / y);
            Assert.AreEqual(remainder, x % y);
        }
    }

    [TestMethod]
    public void Division_ByZero_Throws_ForEveryForm()
    {
        Assert.ThrowsExactly<DivideByZeroException>(() => _ = uint256.One / uint256.Zero);
        Assert.ThrowsExactly<DivideByZeroException>(() => _ = uint256.One % uint256.Zero);
        Assert.ThrowsExactly<DivideByZeroException>(() => _ = uint256.DivRem(uint256.One, uint256.Zero));
        Assert.ThrowsExactly<DivideByZeroException>(() => _ = uint256.MaxValue.DivRem(0, out _));
        Assert.ThrowsExactly<DivideByZeroException>(() => _ = uint256.Zero / uint256.Zero);
    }

    [TestMethod]
    public void DivRem_ByUlong_MatchesBigInteger()
    {
        var rng = new Random(1234);
        for (int i = 0; i < 500; i++)
        {
            uint256 value = UInt256TestValues.SparseValue(rng);
            ulong divisor = UInt256TestValues.SparseLimb(rng);
            if (divisor == 0) continue;

            uint256 quotient = value.DivRem(divisor, out ulong remainder);

            Assert.AreEqual(Big(value) / divisor, Big(quotient));
            Assert.AreEqual(Big(value) % divisor, new BigInteger(remainder));
        }
    }

    // ---- TryMultiply(ulong) ----

    [TestMethod]
    [DataRow(2UL)]
    [DataRow(3UL)]
    [DataRow(10UL)]
    [DataRow(1UL << 32)]
    [DataRow(1UL << 63)]
    [DataRow(ulong.MaxValue)]
    public void TryMultiply_ExactOverflowBoundary(ulong factor)
    {
        BigInteger largestThatFits = (Two256 - 1) / factor;

        Assert.IsTrue(U(largestThatFits).TryMultiply(factor, out uint256 fits));
        Assert.AreEqual(largestThatFits * factor, Big(fits));

        Assert.IsFalse(U(largestThatFits + 1).TryMultiply(factor, out uint256 overflow));
        Assert.AreEqual(uint256.Zero, overflow, "a failed multiply leaves the result at zero");
    }

    [TestMethod]
    public void TryMultiply_TrivialFactorsAndBoundaryValues_MatchBigInteger()
    {
        foreach (BigInteger value in UInt256TestValues.Boundaries)
        {
            foreach (ulong factor in new[] { 0UL, 1UL, 2UL, 10UL, ulong.MaxValue })
            {
                BigInteger product = value * factor;

                bool ok = U(value).TryMultiply(factor, out uint256 result);

                Assert.AreEqual(product < Two256, ok, $"{value} * {factor}");
                Assert.AreEqual(ok ? product : BigInteger.Zero, Big(result), $"{value} * {factor}");
            }
        }

        Assert.IsTrue(U(BigInteger.One << 192).TryMultiply(ulong.MaxValue, out _));
    }

    // ---- Pow10U64 ----

    [TestMethod]
    public void Pow10U64_FullTable()
    {
        for (int exponent = 0; exponent <= 19; exponent++)
            Assert.AreEqual((ulong)BigInteger.Pow(10, exponent), uint256.Pow10U64(exponent), $"10^{exponent}");
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(20)]
    [DataRow(int.MinValue)]
    [DataRow(int.MaxValue)]
    public void Pow10U64_OutOfRange_Throws(int exponent)
    {
        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = uint256.Pow10U64(exponent));

        Assert.AreEqual("exponent", exception.ParamName);
    }

    // ---- unary operators ----

    [TestMethod]
    public void UnaryNegation_WrapsModulo2Pow256()
    {
        foreach (BigInteger value in UInt256TestValues.Boundaries)
            Assert.AreEqual((Two256 - value) % Two256, Big(-U(value)), $"-{value}");
    }

    [TestMethod]
    public void IncrementAndDecrement_AreChecked()
    {
        uint256 x = uint256.Zero;
        Assert.AreEqual(uint256.One, ++x);
        Assert.AreEqual(uint256.Zero, --x);

        uint256 carry = new(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, 0);
        Assert.AreEqual(new uint256(0, 0, 0, 1), ++carry);
        Assert.AreEqual(new uint256(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, 0), --carry);

        Assert.ThrowsExactly<OverflowException>(() => { uint256 y = uint256.MaxValue; y++; });
        Assert.ThrowsExactly<OverflowException>(() => { uint256 y = uint256.Zero; y--; });
    }
}
