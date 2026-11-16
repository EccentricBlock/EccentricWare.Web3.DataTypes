using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Checked and unchecked arithmetic of <see cref="int256"/>, including the MinValue and two's-complement boundaries.</summary>
[TestClass]
public sealed class Int256ArithmeticTests
{
    private static int256 Big(BigInteger v) => Int256TestValues.FromBig(v);

    private static BigInteger Val(int256 v) => Int256TestValues.ToBig(v);

    // ---- unchecked (wrapping) operations ----

    [TestMethod]
    public void AddUnchecked_Wraps_Mod2Pow256()
    {
        Assert.AreEqual(int256.MinValue, int256.AddUnchecked(int256.MaxValue, int256.One));
        Assert.AreEqual(int256.Zero, int256.AddUnchecked(int256.MinValue, int256.MinValue));
        Assert.AreEqual(int256.MaxValue, int256.AddUnchecked(int256.MinValue, int256.MinusOne));
    }

    [TestMethod]
    public void SubtractUnchecked_Wraps_Mod2Pow256()
    {
        Assert.AreEqual(int256.MaxValue, int256.SubtractUnchecked(int256.MinValue, int256.One));
        Assert.AreEqual(int256.MinValue, int256.SubtractUnchecked(int256.Zero, int256.MinValue));
        Assert.AreEqual(int256.MinusOne, int256.SubtractUnchecked(int256.MaxValue, int256.MinValue));
    }

    [TestMethod]
    public void NegateUnchecked_MinValue_StaysMinValue()
    {
        Assert.AreEqual(int256.MinValue, int256.NegateUnchecked(int256.MinValue));
        Assert.AreEqual(int256.Zero, int256.NegateUnchecked(int256.Zero));
    }

    [TestMethod]
    public void NegateUnchecked_IsAnInvolution_AndMatchesTwosComplementOracle()
    {
        foreach (BigInteger v in Int256TestValues.BoundaryValues)
        {
            int256 x = Big(v);
            Assert.AreEqual(Int256TestValues.Wrap(-v), Val(int256.NegateUnchecked(x)), $"-({v})");
            Assert.AreEqual(x, int256.NegateUnchecked(int256.NegateUnchecked(x)), $"--({v})");
        }
    }

    // ---- checked operations: overflow ----

    [TestMethod]
    public void Addition_Checked_Overflow_Throws()
    {
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.MaxValue + int256.One);
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.MinValue + int256.MinusOne);
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.MinValue + int256.MinValue);
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.MaxValue + int256.MaxValue);
    }

    [TestMethod]
    public void Subtraction_Checked_Overflow_Throws()
    {
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.MaxValue - int256.MinusOne);
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.MinValue - int256.One);
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.Zero - int256.MinValue);
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.MaxValue - int256.MinValue);
    }

    [TestMethod]
    public void Negation_Checked_OnlyMinValueThrows()
    {
        Assert.ThrowsExactly<OverflowException>(() => _ = -int256.MinValue);
        Assert.AreEqual(int256.MaxValue, -(int256.MinValue + int256.One));
        Assert.AreEqual(int256.MinValue + int256.One, -int256.MaxValue);
        Assert.AreEqual(int256.One, -int256.MinusOne);
        Assert.AreEqual(int256.Zero, -int256.Zero);
    }

    [TestMethod]
    public void CheckedResultsAtTheBoundaries_DoNotThrow()
    {
        Assert.AreEqual(int256.MinusOne, int256.MinValue + int256.MaxValue);
        Assert.AreEqual(int256.MaxValue, int256.MinusOne - int256.MinValue);
        Assert.AreEqual(int256.MinValue, int256.MinValue - int256.Zero);
        Assert.AreEqual(int256.MaxValue, int256.MaxValue + int256.Zero);
        Assert.AreEqual(int256.MinValue, int256.MinValue + int256.Zero);
    }

    [TestMethod]
    public void BasicArithmetic_SmallValues_AgreeWithBigInteger()
    {
        var a = new int256(123456);
        var b = new int256(-789);

        Assert.AreEqual(123456 + -789, Val(a + b));
        Assert.AreEqual(123456 - -789, Val(a - b));
        Assert.AreEqual(-123456, Val(-a));
    }

    [TestMethod]
    public void MultiplyDivideModulo_ColdPath_AgreeWithBigInteger_ForSmallValues()
    {
        var a = new int256(123456789);
        var b = new int256(-1000);

        Assert.AreEqual(123456789L * -1000L, Val(a * b));
        Assert.AreEqual(123456789L / -1000L, Val(a / b));
        Assert.AreEqual(123456789L % -1000L, Val(a % b));
    }

    [TestMethod]
    public void Multiply_Overflow_Throws()
    {
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.MaxValue * new int256(2));
        Assert.ThrowsExactly<OverflowException>(() => _ = int256.MinValue * new int256(2));
    }

    [TestMethod]
    public void DivideByZero_Throws()
    {
        Assert.ThrowsExactly<DivideByZeroException>(() => _ = int256.One / int256.Zero);
        Assert.ThrowsExactly<DivideByZeroException>(() => _ = int256.One % int256.Zero);
        Assert.ThrowsExactly<DivideByZeroException>(() => _ = int256.MinValue / int256.Zero);
        Assert.ThrowsExactly<DivideByZeroException>(() => _ = int256.Zero / int256.Zero);
        Assert.ThrowsExactly<DivideByZeroException>(() => _ = int256.MinValue % int256.Zero);
    }

    // ---- MinValue / MaxValue against independent BigInteger results ----

    public static IEnumerable<object[]> Divisors()
    {
        foreach (long d in new long[] { 1, -1, 2, -2, 3, -3, 7, 10, -10, 1_000_000_007, long.MaxValue, long.MinValue })
            yield return new object[] { new BigInteger(d) };
        yield return new object[] { Int256TestValues.Pow2(64) };
        yield return new object[] { -Int256TestValues.Pow2(128) - 1 };
        yield return new object[] { Int256TestValues.Two255 - 1 };
        yield return new object[] { -Int256TestValues.Two255 };
    }

    [TestMethod]
    [DynamicData(nameof(Divisors))]
    public void MinAndMaxValue_DivisionAndRemainder_MatchBigInteger(BigInteger divisor)
    {
        int256 d = Big(divisor);
        foreach (BigInteger dividend in new[] { -Int256TestValues.Two255, Int256TestValues.Two255 - 1 })
        {
            BigInteger quotient = BigInteger.Divide(dividend, divisor); // truncates toward zero, like EVM SDIV
            BigInteger remainder = BigInteger.Remainder(dividend, divisor); // sign of the dividend, like SMOD
            int256 x = Big(dividend);

            if (Int256TestValues.Fits(quotient))
                Assert.AreEqual(quotient, Val(x / d), $"{dividend} / {divisor}");
            else
                Assert.ThrowsExactly<OverflowException>(() => _ = x / d);

            Assert.AreEqual(remainder, Val(x % d), $"{dividend} % {divisor}");
        }
    }

    [TestMethod]
    public void MinValue_Multiplication_MatchesBigIntegerForSmallFactors()
    {
        foreach (long f in new long[] { -2, -1, 0, 1, 2 })
        {
            BigInteger product = -Int256TestValues.Two255 * f;
            if (Int256TestValues.Fits(product))
                Assert.AreEqual(product, Val(int256.MinValue * new int256(f)), $"MinValue * {f}");
            else
                Assert.ThrowsExactly<OverflowException>(() => _ = int256.MinValue * new int256(f));
        }
    }

    // ---- exhaustive boundary pairs against the independent oracle ----

    [TestMethod]
    public void AllBoundaryPairs_CheckedAndUncheckedArithmetic_MatchBigInteger()
    {
        foreach (BigInteger bx in Int256TestValues.BoundaryValues)
        {
            foreach (BigInteger by in Int256TestValues.BoundaryValues)
            {
                int256 x = Big(bx), y = Big(by);
                string context = $"x={bx}, y={by}";

                Assert.AreEqual(Int256TestValues.Wrap(bx + by), Val(int256.AddUnchecked(x, y)), context + " addU");
                Assert.AreEqual(Int256TestValues.Wrap(bx - by), Val(int256.SubtractUnchecked(x, y)), context + " subU");

                AssertChecked(bx + by, () => Val(x + y), context + " add");
                AssertChecked(bx - by, () => Val(x - y), context + " sub");
                AssertChecked(bx * by, () => Val(x * y), context + " mul");

                if (by.IsZero)
                {
                    Assert.ThrowsExactly<DivideByZeroException>(() => _ = x / y);
                    Assert.ThrowsExactly<DivideByZeroException>(() => _ = x % y);
                    continue;
                }

                AssertChecked(BigInteger.Divide(bx, by), () => Val(x / y), context + " div");
                Assert.AreEqual(BigInteger.Remainder(bx, by), Val(x % y), context + " rem");
            }
        }
    }

    private static void AssertChecked(BigInteger expected, Func<BigInteger> actual, string context)
    {
        if (Int256TestValues.Fits(expected))
            Assert.AreEqual(expected, actual(), context);
        else
            Assert.ThrowsExactly<OverflowException>(() => actual(), context);
    }

    [TestMethod]
    public void SparseRandomLimbs_ArithmeticMatchesBigInteger()
    {
        var rng = new Random(256_020);
        for (int i = 0; i < 3_000; i++)
        {
            int256 x = Int256TestValues.SparseValue(rng), y = Int256TestValues.SparseValue(rng);
            BigInteger bx = Val(x), by = Val(y);
            string context = $"x={bx}, y={by}";

            Assert.AreEqual(Int256TestValues.Wrap(bx + by), Val(int256.AddUnchecked(x, y)), context);
            Assert.AreEqual(Int256TestValues.Wrap(bx - by), Val(int256.SubtractUnchecked(x, y)), context);
            AssertChecked(bx * by, () => Val(x * y), context + " mul");
            if (!by.IsZero)
            {
                AssertChecked(BigInteger.Divide(bx, by), () => Val(x / y), context + " div");
                Assert.AreEqual(BigInteger.Remainder(bx, by), Val(x % y), context + " rem");
            }
        }
    }

    // ---- bitwise ----

    [TestMethod]
    public void BitwiseOps_BasicIdentities()
    {
        var a = new int256(0x55);
        var b = new int256(0x0F);

        Assert.AreEqual(new int256(0x05), a & b);
        Assert.AreEqual(new int256(0x5F), a | b);
        Assert.AreEqual(new int256(0x5A), a ^ b);
        Assert.AreEqual(new int256(~0x55), ~a);
    }

    [TestMethod]
    public void BitwiseOps_AllBoundaryPairs_MatchBigIntegerTwosComplement()
    {
        foreach (BigInteger bx in Int256TestValues.BoundaryValues)
        {
            Assert.AreEqual(~bx, Val(~Big(bx)), $"~({bx})");
            foreach (BigInteger by in Int256TestValues.BoundaryValues)
            {
                int256 x = Big(bx), y = Big(by);
                // BigInteger bitwise operators use infinite two's complement, so the results stay in range.
                Assert.AreEqual(bx & by, Val(x & y), $"{bx} & {by}");
                Assert.AreEqual(bx | by, Val(x | y), $"{bx} | {by}");
                Assert.AreEqual(bx ^ by, Val(x ^ y), $"{bx} ^ {by}");
            }
        }
    }

    [TestMethod]
    public void BitwiseIdentities_HoldForExtremes()
    {
        Assert.AreEqual(int256.MaxValue, ~int256.MinValue);
        Assert.AreEqual(int256.MinValue, ~int256.MaxValue);
        Assert.AreEqual(int256.MinusOne, int256.MinValue | int256.MaxValue);
        Assert.AreEqual(int256.Zero, int256.MinValue & int256.MaxValue);
        Assert.AreEqual(int256.MinusOne, int256.MinValue ^ int256.MaxValue);
        Assert.AreEqual(int256.Zero, int256.MinusOne ^ int256.MinusOne);
    }
}
