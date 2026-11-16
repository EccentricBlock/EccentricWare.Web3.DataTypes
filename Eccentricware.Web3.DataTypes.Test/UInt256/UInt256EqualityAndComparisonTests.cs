using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class UInt256EqualityAndComparisonTests
{
    private static uint256 U(BigInteger value) => UInt256TestValues.FromBig(value);

    // ---- equality ----

    [TestMethod]
    public void Equality_DifferingInASingleLimb_IsNotEqual()
    {
        uint256 a = new(1UL, 2UL, 3UL, 4UL);
        uint256[] different = [new(0UL, 2UL, 3UL, 4UL), new(1UL, 0UL, 3UL, 4UL), new(1UL, 2UL, 0UL, 4UL), new(1UL, 2UL, 3UL, 5UL)];

        Assert.IsTrue(a.Equals(new uint256(1UL, 2UL, 3UL, 4UL)));
        Assert.IsTrue(a == new uint256(1UL, 2UL, 3UL, 4UL));
        Assert.IsFalse(a != new uint256(1UL, 2UL, 3UL, 4UL));
        foreach (uint256 other in different)
        {
            Assert.IsFalse(a.Equals(other));
            Assert.IsFalse(a == other);
            Assert.IsTrue(a != other);
            Assert.IsFalse(other.Equals(a), "symmetry");
        }
    }

    [TestMethod]
    public void Equality_ObjectOverload_OnlyAcceptsABoxedUInt256()
    {
        uint256 a = new(123UL);
        object boxedEqual = new uint256(123UL), boxedDifferent = new uint256(124UL);

        Assert.IsTrue(a.Equals(boxedEqual));
        Assert.IsFalse(a.Equals(boxedDifferent));
        Assert.IsFalse(a.Equals(null));
        Assert.IsFalse(a.Equals((object)123));
        Assert.IsFalse(a.Equals((object)123UL));
        Assert.IsFalse(a.Equals((object)BigInteger.Parse("123")));
        Assert.IsFalse(a.Equals((object)"123"));
        Assert.IsTrue(object.Equals((object)a, boxedEqual));
    }

    [TestMethod]
    public void Hash_IsEqualForEqualValues_BuiltByEveryRoute()
    {
        uint256[] routes =
        [
            new(42UL), new(42UL, 0, 0, 0), 42UL, 42u, 42, 42L, (UInt128)42, new(new UInt128(0, 42), UInt128.Zero),
            new(new byte[] { 42 }), uint256.FromLittleEndian(new byte[] { 42 }), uint256.Parse("42"), uint256.Parse("0x2a"),
            uint256.ParseDecimal("42"), (uint256)new BigInteger(42), uint256.Zero + new uint256(42UL),
        ];

        foreach (uint256 value in routes)
        {
            Assert.AreEqual(routes[0], value);
            Assert.AreEqual(routes[0].GetHashCode(), value.GetHashCode());
        }
    }

    // ---- ordering ----

    [TestMethod]
    public void CompareTo_And_AllSixOperators_MatchBigInteger_OnBoundaryPairs()
    {
        foreach (BigInteger left in UInt256TestValues.Boundaries)
        {
            foreach (BigInteger right in UInt256TestValues.Boundaries)
            {
                uint256 a = U(left), b = U(right);
                int expected = left.CompareTo(right);
                string context = $"{left} vs {right}";

                Assert.AreEqual(Math.Sign(expected), Math.Sign(a.CompareTo(b)), context);
                Assert.AreEqual(Math.Sign(expected), Math.Sign(a.CompareTo((object)b)), context);
                Assert.AreEqual(expected < 0, a < b, context);
                Assert.AreEqual(expected <= 0, a <= b, context);
                Assert.AreEqual(expected > 0, a > b, context);
                Assert.AreEqual(expected >= 0, a >= b, context);
                Assert.AreEqual(expected == 0, a == b, context);
                Assert.AreEqual(expected != 0, a != b, context);
            }
        }
    }

    [TestMethod]
    public void CompareTo_ReturnsMinusOneZeroOrOne()
    {
        Assert.AreEqual(-1, uint256.Zero.CompareTo(uint256.One));
        Assert.AreEqual(0, uint256.One.CompareTo(uint256.One));
        Assert.AreEqual(1, uint256.MaxValue.CompareTo(uint256.One));
    }

    [TestMethod]
    public void CompareTo_ComparesTheTopLimbFirst_AndIsUnsigned()
    {
        Assert.IsTrue(new uint256(0, 0, 0, 1) > new uint256(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, 0));
        Assert.IsTrue(new uint256(0, 0, 1, 0) > new uint256(ulong.MaxValue, ulong.MaxValue, 0, 0));
        Assert.IsTrue(new uint256(0, 1, 0, 0) > new uint256(ulong.MaxValue, 0, 0, 0));
        Assert.IsTrue(uint256.One << 255 > uint256.One, "a set top bit is large, not negative");
    }

    [TestMethod]
    public void CompareToObject_NullBoxedAndForeignTypes()
    {
        uint256 v = new(1UL);

        Assert.AreEqual(1, v.CompareTo(null));
        Assert.AreEqual(0, v.CompareTo((object)new uint256(1UL)));
        Assert.AreEqual(-1, v.CompareTo((object)new uint256(2UL)));
        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => v.CompareTo("not a uint256"));
        Assert.AreEqual("obj", exception.ParamName);
        Assert.ThrowsExactly<ArgumentException>(() => v.CompareTo((object)1UL));
    }

    // ---- default comparers in collections ----

    [TestMethod]
    public void DefaultComparer_SortsLikeBigInteger()
    {
        var rng = new Random(99);
        uint256[] input = UInt256TestValues.Boundaries.Select(U).Concat(Enumerable.Range(0, 100).Select(_ => UInt256TestValues.SparseValue(rng))).ToArray();
        rng.Shuffle(input);
        BigInteger[] expected = input.Select(UInt256TestValues.ToBig).Order().ToArray();

        var list = new List<uint256>(input);
        list.Sort();
        uint256[] array = (uint256[])input.Clone();
        Array.Sort(array);

        CollectionAssert.AreEqual(expected, list.Select(UInt256TestValues.ToBig).ToArray());
        CollectionAssert.AreEqual(expected, array.Select(UInt256TestValues.ToBig).ToArray());
        Assert.AreEqual(Array.IndexOf(array, uint256.MaxValue), Array.BinarySearch(array, uint256.MaxValue));
        Assert.IsLessThan(0, Array.BinarySearch(array, U(BigInteger.One << 250)));
    }

    [TestMethod]
    public void DefaultComparer_DictionaryAndSortedSet()
    {
        var dictionary = new Dictionary<uint256, string> { [uint256.Parse("0x10")] = "sixteen", [uint256.MaxValue] = "max" };
        var sorted = new SortedSet<uint256> { uint256.MaxValue, uint256.Zero, uint256.One << 128, uint256.Zero };

        Assert.AreEqual("sixteen", dictionary[new uint256(16UL)]);
        Assert.IsFalse(dictionary.ContainsKey(new uint256(17UL)));
        CollectionAssert.AreEqual(new[] { uint256.Zero, uint256.One << 128, uint256.MaxValue }, sorted.ToArray());
    }
}
