using System.Numerics;
using static EccentricWare.Web3.DataTypes.Tests.Int256TestValues;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Hash contract of <see cref="int256"/>. The hash is keyed per process, so these tests assert consistency and sensitivity,
/// never specific values. Distinctness assertions involve a handful of values, where a 32-bit chance collision is ~1e-9.
/// </summary>
[TestClass]
public sealed class Int256HashTests
{
    [TestMethod]
    public void EqualValues_BuiltByDifferentRoutes_HaveEqualHashes_ForBoundaryValues()
    {
        foreach (BigInteger v in BoundaryValues)
        {
            int256 viaLimbs = FromBig(v);
            int hash = viaLimbs.GetHashCode();
            Assert.AreEqual(hash, ((int256)v).GetHashCode(), $"{v} via BigInteger");
            Assert.AreEqual(hash, new int256(viaLimbs.ToBigEndianBytes()).GetHashCode(), $"{v} via bytes");
            Assert.AreEqual(hash, int256.Parse(v.ToString()).GetHashCode(), $"{v} via decimal text");
            Assert.AreEqual(hash, int256.Parse(viaLimbs.ToString("0x", null)).GetHashCode(), $"{v} via hex text");
            Assert.AreEqual(hash, viaLimbs.GetHashCode(), $"{v} repeated");
        }
    }

    [TestMethod]
    public void DefaultValue_HashesLikeZero()
    {
        Assert.AreEqual(int256.Zero.GetHashCode(), default(int256).GetHashCode());
        Assert.AreEqual(int256.Zero.GetHashCode(), new int256(0).GetHashCode());
    }

    [TestMethod]
    public void EveryLimb_AndEveryLimbPermutation_AffectsTheHash()
    {
        int hash = new int256(1, 2, 3, 4).GetHashCode();
        Assert.AreNotEqual(hash, new int256(9, 2, 3, 4).GetHashCode());
        Assert.AreNotEqual(hash, new int256(1, 9, 3, 4).GetHashCode());
        Assert.AreNotEqual(hash, new int256(1, 2, 9, 4).GetHashCode());
        Assert.AreNotEqual(hash, new int256(1, 2, 3, 9).GetHashCode());
        Assert.AreNotEqual(hash, new int256(3, 4, 1, 2).GetHashCode()); // 128-bit halves swapped

        ulong[] limbs = [1, 2, 3, 4];
        var hashes = new HashSet<int>();
        foreach (int a in Enumerable.Range(0, 4))
            foreach (int b in Enumerable.Range(0, 4).Where(i => i != a))
                foreach (int c in Enumerable.Range(0, 4).Where(i => i != a && i != b))
                {
                    int d = 6 - a - b - c;
                    hashes.Add(new int256(limbs[a], limbs[b], limbs[c], limbs[d]).GetHashCode());
                }

        Assert.HasCount(24, hashes);
    }

    [TestMethod]
    public void SignBit_AffectsTheHash()
    {
        Assert.AreNotEqual(int256.Zero.GetHashCode(), int256.MinValue.GetHashCode());
        Assert.AreNotEqual(int256.MinusOne.GetHashCode(), int256.MaxValue.GetHashCode());

        // x and -x differ in many bits; with a 32-bit hash essentially none of 10,000 pairs may collide (allow a couple).
        int collisions = 0;
        for (int x = 1; x <= 10_000; x++)
            if (new int256(x).GetHashCode() == new int256(-x).GetHashCode())
                collisions++;
        Assert.IsLessThanOrEqualTo(2, collisions);
    }

    [TestMethod]
    public void NegativeKeyFamilies_AreSpreadAcrossBuckets()
    {
        const int Count = 1 << 16;
        const int Buckets = 1021;
        var families = new Dictionary<string, Func<int, int256>>
        {
            ["sequential negatives"] = i => new int256(-(i + 1)),
            ["negative multiples of 2^32"] = i => new int256(-((long)i << 32) - 1),
            ["negative multiples of the bucket count"] = i => new int256(-(long)(i + 1) * Buckets),
        };

        foreach ((string name, Func<int, int256> make) in families)
        {
            var buckets = new int[Buckets];
            var distinct = new HashSet<int>();
            for (int i = 0; i < Count; i++)
            {
                int hash = make(i).GetHashCode();
                buckets[(uint)hash % Buckets]++;
                distinct.Add(hash);
            }

            // Uniform expectation is 64 per bucket (about 116 worst case in simulation); 4x the mean leaves wide headroom.
            Assert.IsLessThan(4 * Count / Buckets, buckets.Max(), $"{name}: worst bucket");
            Assert.IsGreaterThan(Count - 40, distinct.Count, $"{name}: distinct hashes");
        }
    }

    [TestMethod]
    public void Hash_IsStableAcrossThreads_WithinTheProcess()
    {
        int256[] values = BoundaryValues.Select(FromBig).ToArray();
        int[] expected = values.Select(v => v.GetHashCode()).ToArray();
        var mismatches = new System.Collections.Concurrent.ConcurrentBag<int>();

        Parallel.For(0, 20_000, i =>
        {
            int k = i % values.Length;
            if (values[k].GetHashCode() != expected[k]) mismatches.Add(k);
        });

        Assert.IsEmpty(mismatches);
    }

    [TestMethod]
    public void DefaultHashSetAndDictionary_FindBoundaryKeys()
    {
        var set = new HashSet<int256> { int256.MinValue, int256.MaxValue };
        Assert.Contains(FromBig(-Two255), set);
        Assert.Contains(new int256(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, 0x7FFF_FFFF_FFFF_FFFFUL), set);
        Assert.DoesNotContain(int256.MinusOne, set);

        var dictionary = BoundaryValues.ToDictionary(FromBig, v => v);
        foreach (BigInteger v in BoundaryValues)
            Assert.AreEqual(v, dictionary[new int256(FromBig(v).ToBigEndianBytes())]);
    }
}
