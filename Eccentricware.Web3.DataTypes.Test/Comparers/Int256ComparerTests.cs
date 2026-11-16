using EccentricWare.Web3.DataTypes.Comparers;
using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class Int256ComparerTests
{
    private static int256 Big(BigInteger v) => Int256TestValues.FromBig(v);

    // ---- type shape ----

    [TestMethod]
    public void Instance_IsNonNullSingleton_ImplementingBothComparerInterfaces()
    {
        object instance = Int256Comparer.Instance;
        object viaReflection = typeof(Int256Comparer).GetField(nameof(Int256Comparer.Instance))!.GetValue(null)!;
        Assert.AreSame(instance, viaReflection);
        Assert.IsInstanceOfType<IEqualityComparer<int256>>(instance);
        Assert.IsInstanceOfType<IComparer<int256>>(instance);
    }

    [TestMethod]
    public void Type_IsSealed_WithoutPublicConstructors()
    {
        Assert.IsTrue(typeof(Int256Comparer).IsSealed);
        Assert.IsEmpty(typeof(Int256Comparer).GetConstructors());
    }

    // ---- Compare: instance and static `in` overloads ----

    [TestMethod]
    public void Compare_SignedOrder_NotBitPatternOrder()
    {
        // An unsigned comparer would rank every negative value above every non-negative one.
        var c = Int256Comparer.Instance;
        Assert.IsLessThan(0, c.Compare(int256.MinValue, int256.MaxValue));
        Assert.IsGreaterThan(0, c.Compare(int256.MaxValue, int256.MinValue));
        Assert.IsLessThan(0, c.Compare(int256.MinusOne, int256.Zero));
        Assert.IsLessThan(0, c.Compare(int256.MinusOne, int256.One));
        Assert.IsLessThan(0, c.Compare(int256.MinValue, int256.MinusOne));
        Assert.IsGreaterThan(0, c.Compare(int256.Zero, int256.MinusOne));
        Assert.AreEqual(0, c.Compare(int256.MinValue, int256.MinValue));
        Assert.AreEqual(0, c.Compare(new int256(5), new int256(5)));
    }

    [TestMethod]
    public void StaticInOverloads_AgreeWithInstanceMembers()
    {
        int256 min = int256.MinValue, max = int256.MaxValue, minusOne = int256.MinusOne, zero = int256.Zero;
        var c = Int256Comparer.Instance;

        Assert.IsLessThan(0, Int256Comparer.Compare(in min, in max));
        Assert.IsGreaterThan(0, Int256Comparer.Compare(in max, in min));
        Assert.IsLessThan(0, Int256Comparer.Compare(in minusOne, in zero));
        Assert.AreEqual(0, Int256Comparer.Compare(in min, in min));

        Assert.IsTrue(Int256Comparer.Equals(in max, in max));
        Assert.IsFalse(Int256Comparer.Equals(in min, in max));
        Assert.IsFalse(Int256Comparer.Equals(in zero, in min)); // differ only in the sign bit

        foreach (BigInteger a in Int256TestValues.BoundaryValues.Where((_, i) => i % 3 == 0))
        {
            foreach (BigInteger b in Int256TestValues.BoundaryValues.Where((_, i) => i % 5 == 0))
            {
                int256 x = Big(a), y = Big(b);
                Assert.AreEqual(c.Compare(x, y), Int256Comparer.Compare(in x, in y), $"{a} vs {b}");
                Assert.AreEqual(c.Equals(x, y), Int256Comparer.Equals(in x, in y), $"{a} vs {b}");
            }
        }
    }

    [TestMethod]
    public void Compare_AllBoundaryPairs_MatchBigIntegerOrdering()
    {
        var c = Int256Comparer.Instance;
        foreach (BigInteger a in Int256TestValues.BoundaryValues)
        {
            foreach (BigInteger b in Int256TestValues.BoundaryValues)
            {
                int256 x = Big(a), y = Big(b);
                string context = $"{a} vs {b}";
                Assert.AreEqual(a.CompareTo(b), Math.Sign(c.Compare(x, y)), context);
                Assert.AreEqual(a == b, c.Equals(x, y), context);
                if (a == b)
                    Assert.AreEqual(c.GetHashCode(x), c.GetHashCode(y), context);
            }
        }
    }

    [TestMethod]
    public void Compare_IsAntisymmetricAndTransitive_OnBoundaryAndSparseValues()
    {
        var c = Int256Comparer.Instance;
        var rng = new Random(256_001);
        var pool = Int256TestValues.BoundaryValues.Select(Big).Concat(Enumerable.Range(0, 60).Select(_ => Int256TestValues.SparseValue(rng))).ToList();

        foreach (int256 a in pool)
        {
            foreach (int256 b in pool)
                Assert.AreEqual(Math.Sign(c.Compare(a, b)), -Math.Sign(c.Compare(b, a)));
        }

        for (int i = 0; i < 30_000; i++)
        {
            int256 a = pool[rng.Next(pool.Count)], b = pool[rng.Next(pool.Count)], d = pool[rng.Next(pool.Count)];
            if (c.Compare(a, b) <= 0 && c.Compare(b, d) <= 0)
                Assert.IsLessThanOrEqualTo(0, c.Compare(a, d), $"{a} <= {b} <= {d}");
        }
    }

    // ---- Equals / GetHashCode ----

    [TestMethod]
    public void Equals_DetectsDifferenceInAnySingleLimbAndInTheSignBit()
    {
        var c = Int256Comparer.Instance;
        var baseValue = new int256(1, 2, 3, 4);
        Assert.IsTrue(c.Equals(baseValue, new int256(1, 2, 3, 4)));
        Assert.IsFalse(c.Equals(baseValue, new int256(9, 2, 3, 4)));
        Assert.IsFalse(c.Equals(baseValue, new int256(1, 9, 3, 4)));
        Assert.IsFalse(c.Equals(baseValue, new int256(1, 2, 9, 4)));
        Assert.IsFalse(c.Equals(baseValue, new int256(1, 2, 3, 9)));
        Assert.IsFalse(c.Equals(int256.Zero, int256.MinValue));
        Assert.IsFalse(c.Equals(int256.MaxValue, int256.MinusOne));
        Assert.IsTrue(c.Equals(default, int256.Zero));
    }

    [TestMethod]
    public void GetHashCode_MatchesTheStructHash_AndIsStableAcrossConstructionRoutes()
    {
        var c = Int256Comparer.Instance;
        foreach (BigInteger v in Int256TestValues.BoundaryValues)
        {
            int256 viaLimbs = Big(v);
            int256 viaBytes = new(viaLimbs.ToBigEndianBytes());
            int256 viaParse = int256.Parse(v.ToString());
            Assert.AreEqual(viaLimbs.GetHashCode(), c.GetHashCode(viaLimbs), $"{v}");
            Assert.AreEqual(c.GetHashCode(viaLimbs), c.GetHashCode(viaBytes), $"{v} via bytes");
            Assert.AreEqual(c.GetHashCode(viaLimbs), c.GetHashCode(viaParse), $"{v} via parse");
            Assert.AreEqual(c.GetHashCode(viaLimbs), c.GetHashCode(viaLimbs), $"{v} repeated");
        }
    }

    [TestMethod]
    public void GetHashCode_DistinguishesTheSignBitOnlyKey()
    {
        // The hash is keyed per process; a 32-bit collision here has probability ~2^-32 per pair.
        var c = Int256Comparer.Instance;
        Assert.AreNotEqual(c.GetHashCode(int256.Zero), c.GetHashCode(int256.MinValue));
        Assert.AreNotEqual(c.GetHashCode(int256.MaxValue), c.GetHashCode(int256.MinusOne));
    }

    // ---- sorted collections and interface use ----

    private static readonly BigInteger[] ExpectedOrder = [-Int256TestValues.Two255, -1, 0, 1, Int256TestValues.Two255 - 1];

    private static int256[] ShuffledExtremes() => [int256.MaxValue, int256.Zero, int256.MinValue, int256.MinusOne, int256.One];

    private static void AssertSignedOrder(IEnumerable<int256> sorted)
        => CollectionAssert.AreEqual(ExpectedOrder, sorted.Select(Int256TestValues.ToBig).ToArray());

    [TestMethod]
    public void SortingWithTheComparer_ProducesSignedOrder_InEveryCollection()
    {
        var array = ShuffledExtremes();
        Array.Sort(array, Int256Comparer.Instance);
        AssertSignedOrder(array);

        var list = ShuffledExtremes().ToList();
        list.Sort(Int256Comparer.Instance);
        AssertSignedOrder(list);

        AssertSignedOrder(new SortedSet<int256>(ShuffledExtremes(), Int256Comparer.Instance));
        AssertSignedOrder(new SortedDictionary<int256, int>(ShuffledExtremes().ToDictionary(k => k, _ => 0), Int256Comparer.Instance).Keys);
        AssertSignedOrder(ShuffledExtremes().OrderBy(x => x, Int256Comparer.Instance));
        AssertSignedOrder(ShuffledExtremes().OrderByDescending(x => x, Int256Comparer.Instance).Reverse());
    }

    [TestMethod]
    public void SortingRandomValues_MatchesBigIntegerOrder()
    {
        var rng = new Random(256_002);
        var values = Enumerable.Range(0, 2_000).Select(_ => Int256TestValues.SparseValue(rng)).ToArray();
        var sorted = (int256[])values.Clone();
        Array.Sort(sorted, Int256Comparer.Instance);

        CollectionAssert.AreEqual(values.Select(Int256TestValues.ToBig).OrderBy(v => v).ToArray(), sorted.Select(Int256TestValues.ToBig).ToArray());
    }

    [TestMethod]
    public void UsedThroughTheInterfaces_BehavesLikeTheInstanceMembers()
    {
        IComparer<int256> comparer = Int256Comparer.Instance;
        IEqualityComparer<int256> equality = Int256Comparer.Instance;

        Assert.IsLessThan(0, comparer.Compare(int256.MinusOne, int256.One));
        Assert.IsTrue(equality.Equals(int256.MaxValue, Big(Int256TestValues.Two255 - 1)));
        Assert.AreEqual(int256.MaxValue.GetHashCode(), equality.GetHashCode(int256.MaxValue));
    }

    [TestMethod]
    public void HashSetAndDictionary_FindKeysBuiltByDifferentRoutes()
    {
        var set = new HashSet<int256>(Int256Comparer.Instance) { new int256(0, 0, 0, 0x8000_0000_0000_0000UL), int256.MaxValue, int256.MinusOne };
        Assert.Contains(int256.Parse("-57896044618658097711785492504343953926634992332820282019728792003956564819968"), set);
        Assert.Contains(int256.Parse("0x" + new string('f', 64)), set);
        Assert.Contains(Big(Int256TestValues.Two255 - 1), set);
        Assert.DoesNotContain(int256.Zero, set);
        Assert.HasCount(3, set);

        var dictionary = new Dictionary<int256, string>(Int256Comparer.Instance) { [int256.MinValue] = "min" };
        Assert.AreEqual("min", dictionary[Big(-Int256TestValues.Two255)]);
        Assert.IsFalse(dictionary.ContainsKey(int256.MaxValue));
    }
}
