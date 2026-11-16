namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class Hash32ComparisonTests
{
    // Pairs include a high-bit pair, which catches an accidental signed comparison.
    private static readonly (ulong Low, ulong High)[] LimbPairs =
    [
        (1UL, 2UL),
        (0x7FFFFFFFFFFFFFFFUL, 0x8000000000000000UL),
        (0UL, ulong.MaxValue),
    ];

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void CompareTo_DifferingInOneLimb_MatchesByteWiseOracle(int limb)
    {
        foreach (ulong filler in new ulong[] { 0, 0x0123456789ABCDEFUL, ulong.MaxValue })
        {
            foreach ((ulong low, ulong high) in LimbPairs)
            {
                Hash32 smaller = Hash32TestData.WithLimb(limb, low, filler);
                Hash32 larger = Hash32TestData.WithLimb(limb, high, filler);
                string context = $"limb={limb} filler={filler:X} {low:X}/{high:X}";

                Assert.AreEqual(-1, Hash32TestData.OracleCompare(smaller, larger), context);
                Assert.AreEqual(-1, smaller.CompareTo(larger), context);
                Assert.AreEqual(1, larger.CompareTo(smaller), context);
                Assert.IsFalse(smaller.Equals(larger), context);
                Assert.IsTrue(smaller < larger, context);
                Assert.IsTrue(smaller <= larger, context);
                Assert.IsFalse(smaller > larger, context);
                Assert.IsFalse(smaller >= larger, context);
                Assert.IsTrue(larger > smaller, context);
                Assert.IsTrue(larger >= smaller, context);
                Assert.IsFalse(larger < smaller, context);
                Assert.IsFalse(larger <= smaller, context);
            }
        }
    }

    [TestMethod]
    public void CompareTo_MoreSignificantLimbDominatesAllLessSignificantOnes()
    {
        var smaller = new Hash32(0, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue);
        var larger = new Hash32(1, 0, 0, 0);

        Assert.AreEqual(-1, smaller.CompareTo(larger));
        Assert.AreEqual(1, larger.CompareTo(smaller));
        Assert.IsTrue(smaller < larger);

        var smaller1 = new Hash32(5, 0, ulong.MaxValue, ulong.MaxValue);
        var larger1 = new Hash32(5, 1, 0, 0);
        Assert.AreEqual(-1, smaller1.CompareTo(larger1));
    }

    [TestMethod]
    public void EqualValues_CompareAsEqual_AndOperatorsFollowTheTruthTable()
    {
        Hash32 a = Hash32TestData.Value0To31();
        Hash32 same = Hash32TestData.Value0To31();

        Assert.AreEqual(0, a.CompareTo(a));
        Assert.AreEqual(0, a.CompareTo(same));
        Assert.IsFalse(a < same);
        Assert.IsFalse(a > same);
        Assert.IsTrue(a <= same);
        Assert.IsTrue(a >= same);
    }

    [TestMethod]
    public void Default_ComparesEqualToZero()
    {
        Hash32 zero = Hash32.Zero;
        Hash32 defaultValue = default;

        Assert.AreEqual(0, defaultValue.CompareTo(zero));
        Assert.IsTrue(defaultValue <= zero);
        Assert.IsTrue(defaultValue >= zero);
        Assert.IsFalse(defaultValue < zero);
        Assert.IsFalse(defaultValue > zero);
    }

    [TestMethod]
    public void Zero_IsTheSmallest_AndAllOnesIsTheLargest()
    {
        var max = new Hash32(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue);
        Hash32 any = Hash32TestData.Value0To31();

        Assert.IsTrue(Hash32.Zero < any);
        Assert.IsTrue(any < max);
        Assert.IsTrue(Hash32.Zero < max);
        Assert.IsTrue(max > Hash32.Zero);
    }

    [TestMethod]
    public void CompareTo_RandomPairs_MatchByteWiseOracle_AndAreAntisymmetric()
    {
        var random = new Random(31337);
        Hash32[] pool = Hash32TestData.RandomHashes(random, 200);

        for (int i = 0; i < 2_000; i++)
        {
            Hash32 a = pool[random.Next(pool.Length)];
            Hash32 b = pool[random.Next(pool.Length)];

            // Every third pair shares a prefix of one to three limbs, so deeper limbs decide the order.
            if (i % 3 == 0)
            {
                int shared = 1 + random.Next(3);
                byte[] bBytes = b.ToBigEndianBytes();
                Array.Copy(a.ToBigEndianBytes(), bBytes, shared * 8);
                b = new Hash32(bBytes);
            }

            int expected = Hash32TestData.OracleCompare(a, b);
            Assert.AreEqual(expected, a.CompareTo(b), $"pair {i}");
            Assert.AreEqual(-expected, b.CompareTo(a), $"pair {i}");
            Assert.AreEqual(expected < 0, a < b);
            Assert.AreEqual(expected > 0, a > b);
            Assert.AreEqual(expected <= 0, a <= b);
            Assert.AreEqual(expected >= 0, a >= b);
            Assert.AreEqual(expected == 0, a.Equals(b));
        }
    }

    [TestMethod]
    public void CompareTo_Object_BoxedHash32_ReturnsSignOfComparison()
    {
        IComparable value = new Hash32(0, 0, 5, 0);

        Assert.AreEqual(0, value.CompareTo((object)new Hash32(0, 0, 5, 0)));
        Assert.IsLessThan(0, value.CompareTo((object)new Hash32(0, 0, 6, 0)));
        Assert.IsGreaterThan(0, value.CompareTo((object)new Hash32(0, 0, 4, ulong.MaxValue)));
    }

    [TestMethod]
    public void CompareTo_Object_Null_IsGreaterThanNull()
    {
        Assert.IsGreaterThan(0, Hash32TestData.Value0To31().CompareTo(null));
        Assert.IsGreaterThan(0, Hash32.Zero.CompareTo(null));
    }

    [TestMethod]
    public void CompareTo_Object_WrongType_ThrowsArgumentException()
    {
        Hash32 value = Hash32TestData.Value0To31();

        var ex = Assert.ThrowsExactly<ArgumentException>(() => value.CompareTo("not a hash"));
        Assert.AreEqual("obj", ex.ParamName);
        Assert.ThrowsExactly<ArgumentException>(() => value.CompareTo(5));
        Assert.ThrowsExactly<ArgumentException>(() => value.CompareTo(value.ToUInt256()));
    }

    [TestMethod]
    public void Sorting_ArraySortListSortAndDefaultComparer_MatchByteWiseOrdering()
    {
        Hash32[] random = Hash32TestData.RandomHashes(new Random(4242), 500);
        // Include edge values so a signed comparison or a missed limb would reorder them.
        Hash32[] input =
        [
            .. random,
            Hash32.Zero,
            new Hash32(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue),
            new Hash32(0x8000000000000000UL, 0, 0, 0),
            new Hash32(0x7FFFFFFFFFFFFFFFUL, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue),
        ];

        byte[][] expectedBytes = input
            .Select(h => h.ToBigEndianBytes())
            .OrderBy(b => b, Comparer<byte[]>.Create((x, y) => x.AsSpan().SequenceCompareTo(y)))
            .ToArray();

        Hash32[] viaArraySort = (Hash32[])input.Clone();
        Array.Sort(viaArraySort);

        var viaListSort = new List<Hash32>(input);
        viaListSort.Sort();

        Hash32[] viaDefaultComparer = (Hash32[])input.Clone();
        Array.Sort(viaDefaultComparer, Comparer<Hash32>.Default);

        Hash32[] viaLinq = [.. input.OrderBy(h => h)];

        foreach (Hash32[] sorted in new[] { viaArraySort, viaListSort.ToArray(), viaDefaultComparer, viaLinq })
        {
            Assert.HasCount(expectedBytes.Length, sorted);
            for (int i = 0; i < sorted.Length; i++)
                CollectionAssert.AreEqual(expectedBytes[i], sorted[i].ToBigEndianBytes(), $"index {i}");
        }
    }
}
