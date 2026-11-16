using EccentricWare.Web3.DataTypes.Comparers;
using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Tests for <see cref="UInt256Comparer"/>. The instance members implement the BCL comparer interfaces; the static
/// <c>in</c> overloads are called with explicit <c>in</c> arguments (a type-qualified call with plain arguments binds to
/// the by-value instance overload and does not compile).
/// </summary>
[TestClass]
public sealed class UInt256ComparerTests
{
    private static readonly BigInteger[] Boundaries = UInt256TestValues.Boundaries;

    private static uint256 U(BigInteger value) => UInt256TestValues.FromBig(value);

    private static IEnumerable<uint256> SampleValues()
    {
        foreach (BigInteger boundary in Boundaries)
            yield return U(boundary);

        var rng = new Random(2468);
        for (int i = 0; i < 100; i++)
            yield return UInt256TestValues.SparseValue(rng);
    }

    // ---- singleton and shape ----

    [TestMethod]
    public void Instance_IsASingletonThatImplementsBothComparerInterfaces()
    {
        Assert.AreSame(UInt256Comparer.Instance, typeof(UInt256Comparer).GetField(nameof(UInt256Comparer.Instance))!.GetValue(null));
        Assert.IsInstanceOfType<IEqualityComparer<uint256>>(UInt256Comparer.Instance);
        Assert.IsInstanceOfType<IComparer<uint256>>(UInt256Comparer.Instance);
        Assert.IsTrue(typeof(UInt256Comparer).IsSealed);
    }

    [TestMethod]
    public void Constructor_IsPrivate_SoOnlyTheSingletonExists()
    {
        var constructors = typeof(UInt256Comparer).GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.HasCount(1, constructors);
        Assert.IsTrue(constructors[0].IsPrivate);
    }

    // ---- equality ----

    [TestMethod]
    public void Equals_TableOfPairs()
    {
        (uint256 a, uint256 b, bool expected)[] cases =
        [
            (uint256.Zero, uint256.Zero, true),
            (uint256.MaxValue, uint256.MaxValue, true),
            (new(1UL), new(2UL), false),
            (new(1UL, 2UL, 3UL, 4UL), new(1UL, 2UL, 3UL, 4UL), true),
            (new(1UL, 2UL, 3UL, 4UL), new(1UL, 2UL, 3UL, 5UL), false),   // only u3 differs
            (new(1UL, 2UL, 3UL, 4UL), new(0UL, 2UL, 3UL, 4UL), false),   // only u0 differs
            (new(1UL, 2UL, 3UL, 4UL), new(1UL, 3UL, 3UL, 4UL), false),
            (new(1UL, 2UL, 3UL, 4UL), new(1UL, 2UL, 4UL, 4UL), false),
        ];

        foreach (var (a, b, expected) in cases)
        {
            uint256 x = a, y = b;
            Assert.AreEqual(expected, UInt256Comparer.Instance.Equals(a, b), $"{a} vs {b}");
            Assert.AreEqual(expected, UInt256Comparer.Instance.Equals(b, a), $"{b} vs {a} (symmetry)");
            Assert.AreEqual(expected, UInt256Comparer.Equals(in x, in y), $"{a} vs {b} (static)");
            Assert.AreEqual(expected, ((IEqualityComparer<uint256>)UInt256Comparer.Instance).Equals(a, b), $"{a} vs {b} (interface)");
        }
    }

    // ---- hashing ----

    [TestMethod]
    public void GetHashCode_EqualValuesBuiltByDifferentRoutes_HashEqually_AndMatchTheValue()
    {
        uint256 viaLimbs = new(0xDEADBEEFUL, 0, 0, 0);
        uint256 viaParse = uint256.Parse("0xdeadbeef");
        uint256 viaBytes = new(Convert.FromHexString("00000000000000000000000000000000000000000000000000000000DEADBEEF"));

        int hash = UInt256Comparer.Instance.GetHashCode(viaLimbs);

        Assert.AreEqual(hash, UInt256Comparer.Instance.GetHashCode(viaParse));
        Assert.AreEqual(hash, UInt256Comparer.Instance.GetHashCode(viaBytes));
        Assert.AreEqual(viaLimbs.GetHashCode(), hash);
        Assert.AreEqual(hash, UInt256Comparer.Instance.GetHashCode(viaLimbs), "stable within the process");
    }

    [TestMethod]
    public void GetHashCode_ManyDistinctValues_RarelyCollide()
    {
        // The hash is keyed per process, so only a loose collision bound is asserted, never a literal hash.
        var hashes = new HashSet<int>();
        for (int i = 0; i < 10_000; i++)
            hashes.Add(UInt256Comparer.Instance.GetHashCode(new uint256((ulong)i, (ulong)i * 31, 0, (ulong)i << 40)));

        Assert.IsGreaterThan(9_990, hashes.Count);
    }

    // ---- ordering ----

    [TestMethod]
    public void Compare_IsUnsignedAndNumeric()
    {
        uint256 topBit = U(BigInteger.One << 255);
        (uint256 smaller, uint256 larger)[] orderedPairs =
        [
            (uint256.Zero, uint256.One),
            (uint256.One, topBit),                                                    // a signed compare would invert this
            (uint256.One, uint256.MaxValue),
            (new(ulong.MaxValue, 0, 0, 0), new(0, 1, 0, 0)),
            (new(ulong.MaxValue, ulong.MaxValue, 0, 0), new(0, 0, 1, 0)),
            (new(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, 0), new(0, 0, 0, 1)),   // top limb dominates
            (new(0, 5, 0, 0), new(0, 6, 0, 0)),                                       // only u1 differs
            (new(0, 0, 5, 0), new(0, 0, 6, 0)),                                       // only u2 differs
            (new(0, 0, 0, 5), new(0, 0, 0, 6)),                                       // only u3 differs
            (new(0, 0, 0, 5), new(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, 6)),
        ];

        foreach (var (smaller, larger) in orderedPairs)
        {
            uint256 s = smaller, l = larger;
            Assert.IsLessThan(0, UInt256Comparer.Instance.Compare(smaller, larger), $"{smaller} < {larger}");
            Assert.IsGreaterThan(0, UInt256Comparer.Instance.Compare(larger, smaller), $"{larger} > {smaller}");
            Assert.IsLessThan(0, UInt256Comparer.Compare(in s, in l), $"{smaller} < {larger} (static)");
            Assert.IsGreaterThan(0, UInt256Comparer.Compare(in l, in s), $"{larger} > {smaller} (static)");
            Assert.IsLessThan(0, ((IComparer<uint256>)UInt256Comparer.Instance).Compare(smaller, larger), "interface");
        }
    }

    [TestMethod]
    public void Compare_EqualValues_ReturnsZero()
    {
        foreach (uint256 value in SampleValues())
        {
            uint256 copy = new(value.U0, value.U1, value.U2, value.U3);
            Assert.AreEqual(0, UInt256Comparer.Instance.Compare(value, copy));
            Assert.AreEqual(0, UInt256Comparer.Compare(in value, in copy));
        }
    }

    [TestMethod]
    public void Compare_AgreesWithBigInteger_AntisymmetricAndTransitive()
    {
        uint256[] values = SampleValues().ToArray();

        foreach (uint256 a in values)
        {
            foreach (uint256 b in values)
            {
                int expected = Math.Sign(UInt256TestValues.ToBig(a).CompareTo(UInt256TestValues.ToBig(b)));
                int actual = Math.Sign(UInt256Comparer.Instance.Compare(a, b));
                Assert.AreEqual(expected, actual, $"{a} vs {b}");
                Assert.AreEqual(-actual, Math.Sign(UInt256Comparer.Instance.Compare(b, a)), $"antisymmetry {a} vs {b}");
            }
        }

        // Transitivity over a deterministic subset (all triples would be ~1.5M comparisons).
        uint256[] subset = values.Take(40).ToArray();
        foreach (uint256 a in subset)
            foreach (uint256 b in subset)
                foreach (uint256 c in subset)
                    if (UInt256Comparer.Instance.Compare(a, b) <= 0 && UInt256Comparer.Instance.Compare(b, c) <= 0)
                        Assert.IsLessThanOrEqualTo(0, UInt256Comparer.Instance.Compare(a, c), $"{a} <= {b} <= {c}");
    }

    // ---- as a collection comparer ----

    [TestMethod]
    public void Dictionary_WithComparer_AddLookupRemove()
    {
        var map = new Dictionary<uint256, int>(UInt256Comparer.Instance);
        map[U(BigInteger.One << 200)] = 1;
        map[uint256.Zero] = 2;
        map[uint256.MaxValue] = 3;

        Assert.AreEqual(1, map[new uint256(0, 0, 0, 1UL << 8)]);   // equal value built separately
        Assert.AreEqual(2, map[new uint256()]);
        Assert.IsFalse(map.ContainsKey(uint256.One));
        Assert.IsTrue(map.Remove(uint256.MaxValue));
        Assert.IsFalse(map.TryGetValue(uint256.MaxValue, out _));
        Assert.HasCount(2, map);
    }

    [TestMethod]
    public void HashSet_WithComparer_DeduplicatesEqualValuesBuiltSeparately()
    {
        var set = new HashSet<uint256>(UInt256Comparer.Instance)
        {
            new uint256(7UL), uint256.Parse("7"), uint256.Parse("0x7"), new uint256(7UL, 0, 0, 0), new uint256(7UL, 0, 0, 1),
        };

        Assert.HasCount(2, set);
    }

    [TestMethod]
    public void Sorting_WithComparer_MatchesBigIntegerOrder()
    {
        var rng = new Random(13579);
        uint256[] input = SampleValues().Concat(Enumerable.Range(0, 50).Select(_ => UInt256TestValues.SparseValue(rng))).ToArray();
        rng.Shuffle(input);
        BigInteger[] expected = input.Select(UInt256TestValues.ToBig).Order().ToArray();

        uint256[] viaArray = (uint256[])input.Clone();
        Array.Sort(viaArray, UInt256Comparer.Instance);

        var viaList = new List<uint256>(input);
        viaList.Sort(UInt256Comparer.Instance);

        uint256[] viaSortedSet = new SortedSet<uint256>(input, UInt256Comparer.Instance).ToArray();
        BigInteger[] distinctExpected = expected.Distinct().ToArray();

        CollectionAssert.AreEqual(expected, viaArray.Select(UInt256TestValues.ToBig).ToArray());
        CollectionAssert.AreEqual(expected, viaList.Select(UInt256TestValues.ToBig).ToArray());
        CollectionAssert.AreEqual(distinctExpected, viaSortedSet.Select(UInt256TestValues.ToBig).ToArray());
    }

    [TestMethod]
    public void SortedSet_WithComparer_TreatsComparerEqualValuesAsOne()
    {
        var set = new SortedSet<uint256>(UInt256Comparer.Instance);

        Assert.IsTrue(set.Add(U(BigInteger.One << 64)));
        Assert.IsFalse(set.Add(new uint256(0, 1, 0, 0)));
        Assert.AreEqual(uint256.One, new SortedSet<uint256>([uint256.MaxValue, uint256.One, uint256.Zero], UInt256Comparer.Instance).ElementAt(1));
        Assert.HasCount(1, set);
    }
}
