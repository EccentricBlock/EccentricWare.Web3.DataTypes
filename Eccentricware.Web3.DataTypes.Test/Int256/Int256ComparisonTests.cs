using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Ordering of <see cref="int256"/>: CompareTo, the relational operators and default-comparer sorting.</summary>
[TestClass]
public sealed class Int256ComparisonTests
{
    private static int256 Big(BigInteger v) => Int256TestValues.FromBig(v);

    private static BigInteger P(int bits) => Int256TestValues.Pow2(bits);

    public static IEnumerable<object[]> OrderedPairs()
    {
        // (smaller, larger): adjacent values, sign boundary, and every limb boundary in both signs.
        yield return new object[] { -Int256TestValues.Two255, -Int256TestValues.Two255 + 1 };
        yield return new object[] { Int256TestValues.Two255 - 2, Int256TestValues.Two255 - 1 };
        yield return new object[] { BigInteger.MinusOne, BigInteger.Zero };
        yield return new object[] { BigInteger.Zero, BigInteger.One };
        yield return new object[] { BigInteger.MinusOne, BigInteger.One };
        yield return new object[] { P(64) - 1, P(64) };
        yield return new object[] { -P(64), -P(64) + 1 };
        yield return new object[] { -P(64) - 1, -P(64) };
        yield return new object[] { P(128) - 1, P(128) };
        yield return new object[] { P(192) - 1, P(192) };
        yield return new object[] { -P(192) - 1, -P(192) };
        yield return new object[] { -P(255), P(255) - 1 };
        yield return new object[] { P(200), P(201) };
        yield return new object[] { -P(201), -P(200) };
    }

    [TestMethod]
    [DynamicData(nameof(OrderedPairs))]
    public void OrderedPair_SatisfiesTheFullTruthTable_InBothDirections(BigInteger smaller, BigInteger larger)
    {
        int256 a = Big(smaller), b = Big(larger);

        Assert.IsTrue(a < b);
        Assert.IsTrue(a <= b);
        Assert.IsFalse(a > b);
        Assert.IsFalse(a >= b);
        Assert.IsFalse(a == b);
        Assert.IsTrue(a != b);
        Assert.IsLessThan(0, a.CompareTo(b));

        Assert.IsFalse(b < a);
        Assert.IsFalse(b <= a);
        Assert.IsTrue(b > a);
        Assert.IsTrue(b >= a);
        Assert.IsFalse(b == a);
        Assert.IsGreaterThan(0, b.CompareTo(a));
    }

    [TestMethod]
    [DataRow(5L)]
    [DataRow(0L)]
    [DataRow(-5L)]
    [DataRow(long.MinValue)]
    public void EqualPair_SatisfiesTheFullTruthTable(long v)
    {
        int256 a = new(v), b = new(v);
        Assert.IsFalse(a < b);
        Assert.IsTrue(a <= b);
        Assert.IsFalse(a > b);
        Assert.IsTrue(a >= b);
        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
        Assert.AreEqual(0, a.CompareTo(b));
    }

    [TestMethod]
    public void RelationalOperatorsAndCompareTo_MatchBigInteger_ForAllBoundaryPairs()
    {
        foreach (BigInteger bx in Int256TestValues.BoundaryValues)
        {
            foreach (BigInteger by in Int256TestValues.BoundaryValues)
            {
                int256 x = Big(bx), y = Big(by);
                string context = $"{bx} vs {by}";
                Assert.AreEqual(bx.CompareTo(by), Math.Sign(x.CompareTo(y)), context);
                Assert.AreEqual(bx < by, x < y, context);
                Assert.AreEqual(bx <= by, x <= y, context);
                Assert.AreEqual(bx > by, x > y, context);
                Assert.AreEqual(bx >= by, x >= y, context);
                Assert.AreEqual(bx == by, x == y, context);
                Assert.AreEqual(bx != by, x != y, context);
            }
        }
    }

    [TestMethod]
    public void CompareTo_OnRandomSparseValues_MatchesBigInteger()
    {
        var rng = new Random(256_010);
        for (int i = 0; i < 5_000; i++)
        {
            int256 x = Int256TestValues.SparseValue(rng), y = Int256TestValues.SparseValue(rng);
            Assert.AreEqual(Int256TestValues.ToBig(x).CompareTo(Int256TestValues.ToBig(y)), Math.Sign(x.CompareTo(y)), $"{x} vs {y}");
        }
    }

    [TestMethod]
    public void CompareTo_SignedOrdering_WorksAcrossNegativesAndPositives()
    {
        Assert.IsTrue(new int256(-3) < new int256(-2));
        Assert.IsTrue(new int256(-2) < new int256(-1));
        Assert.IsTrue(new int256(-1) < int256.Zero);
        Assert.IsTrue(int256.Zero < int256.One);
        Assert.IsTrue(int256.One < new int256(2));
        Assert.IsTrue(int256.MinValue < int256.MinusOne);
        Assert.IsTrue(int256.MaxValue > int256.One);
        Assert.IsTrue(int256.MinValue < int256.Zero);
        Assert.IsTrue(int256.MaxValue > int256.Zero);
        Assert.IsTrue(int256.MinValue < int256.MaxValue);
    }

    [TestMethod]
    public void CompareTo_ObjectOverload()
    {
        var v = new int256(1);

        Assert.IsGreaterThan(0, v.CompareTo(null));
        Assert.AreEqual(0, v.CompareTo((object)new int256(1)));
        Assert.IsLessThan(0, v.CompareTo((object)new int256(2)));
        Assert.IsGreaterThan(0, ((IComparable)int256.Zero).CompareTo(int256.MinValue));
    }

    [TestMethod]
    public void CompareTo_ObjectOfAnotherType_ThrowsArgumentExceptionNamingTheParameter()
    {
        var v = new int256(1);
        foreach (object other in new object[] { "not an int256", 1L, new uint256(1UL), new BigInteger(1) })
        {
            var ex = Assert.ThrowsExactly<ArgumentException>(() => v.CompareTo(other));
            Assert.AreEqual("obj", ex.ParamName);
        }
    }

    [TestMethod]
    public void DefaultComparer_SortsSignedValues_InEveryApi()
    {
        var rng = new Random(256_011);
        int256[] values = Int256TestValues.BoundaryValues.Select(Big).OrderBy(_ => rng.Next()).ToArray();
        BigInteger[] expected = Int256TestValues.BoundaryValues.OrderBy(v => v).ToArray();

        int256[] array = (int256[])values.Clone();
        Array.Sort(array);
        CollectionAssert.AreEqual(expected, array.Select(Int256TestValues.ToBig).ToArray());

        var list = values.ToList();
        list.Sort();
        CollectionAssert.AreEqual(expected, list.Select(Int256TestValues.ToBig).ToArray());

        CollectionAssert.AreEqual(expected, values.OrderBy(v => v).Select(Int256TestValues.ToBig).ToArray());
        Assert.AreEqual(expected[0], Int256TestValues.ToBig(values.Min()));
        Assert.AreEqual(expected[^1], Int256TestValues.ToBig(values.Max()));
        Assert.IsGreaterThan(0, Comparer<int256>.Default.Compare(int256.Zero, int256.MinusOne));
        Assert.IsLessThan(0, Comparer<object>.Default.Compare(int256.MinValue, int256.MaxValue));
    }
}
