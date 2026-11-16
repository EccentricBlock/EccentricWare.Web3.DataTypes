using EccentricWare.Web3.DataTypes.Comparers;
using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Hash behaviour of <see cref="uint256"/> and <see cref="int256"/>. The hash is keyed per process, so these tests
/// check structural properties (consistency, sensitivity, distribution) rather than fixed values.
/// </summary>
[TestClass]
public sealed class Int256UInt256HashingTests
{
    [TestMethod]
    public void EqualValues_BuiltByDifferentRoutes_HaveEqualHashes()
    {
        var rng = new Random(11);
        for (int i = 0; i < 2_000; i++)
        {
            var a = new uint256((ulong)rng.NextInt64(), (ulong)rng.NextInt64(), (ulong)rng.NextInt64(), (ulong)rng.NextInt64());
            uint256 viaBytes = new(a.ToBigEndianBytes());
            uint256 viaHalves = new(a.Low128, a.High128);
            uint256 viaBigInteger = (uint256)a.ToBigInteger();
            Assert.AreEqual(a.GetHashCode(), viaBytes.GetHashCode());
            Assert.AreEqual(a.GetHashCode(), viaHalves.GetHashCode());
            Assert.AreEqual(a.GetHashCode(), viaBigInteger.GetHashCode());
            Assert.AreEqual(a.GetHashCode(), a.GetHashCode());

            int256 s = new(a.U0, a.U1, a.U2, a.U3);
            Assert.AreEqual(s.GetHashCode(), new int256(s.ToBigEndianBytes()).GetHashCode());
            Assert.AreEqual(s.GetHashCode(), ((int256)s.ToBigInteger()).GetHashCode());
        }
    }

    [TestMethod]
    public void EveryLimbAndLimbOrderAffectsTheHash()
    {
        var baseValue = new uint256(1, 2, 3, 4);
        int hash = baseValue.GetHashCode();

        Assert.AreNotEqual(hash, new uint256(9, 2, 3, 4).GetHashCode());
        Assert.AreNotEqual(hash, new uint256(1, 9, 3, 4).GetHashCode());
        Assert.AreNotEqual(hash, new uint256(1, 2, 9, 4).GetHashCode());
        Assert.AreNotEqual(hash, new uint256(1, 2, 3, 9).GetHashCode());

        // Permutations must not collide (a symmetric combiner would make several of these equal).
        var permutations = new HashSet<int>();
        ulong[] limbs = [1, 2, 3, 4];
        void Permute(int k)
        {
            if (k == limbs.Length)
            {
                permutations.Add(new uint256(limbs[0], limbs[1], limbs[2], limbs[3]).GetHashCode());
                return;
            }

            for (int i = k; i < limbs.Length; i++)
            {
                (limbs[k], limbs[i]) = (limbs[i], limbs[k]);
                Permute(k + 1);
                (limbs[k], limbs[i]) = (limbs[i], limbs[k]);
            }
        }

        Permute(0);
        Assert.AreEqual(24, permutations.Count, "all 24 limb permutations should hash differently");

        // Swapping the two 128-bit halves.
        Assert.AreNotEqual(new uint256(1, 2, 3, 4).GetHashCode(), new uint256(3, 4, 1, 2).GetHashCode());
    }

    [TestMethod]
    public void StructuredKeyFamilies_AreSpreadAcrossBuckets()
    {
        const int Count = 1 << 16;
        const int PrimeBuckets = 1021;
        const int PowerOfTwoBuckets = 1024;

        var families = new Dictionary<string, Func<int, uint256>>
        {
            ["sequential low limb"] = i => new uint256((ulong)i),
            ["sequential limb 1"] = i => new uint256(0, (ulong)i, 0, 0),
            ["sequential limb 3"] = i => new uint256(0, 0, 0, (ulong)i),
            ["multiples of the prime bucket count"] = i => new uint256((ulong)i * PrimeBuckets),
            ["multiples of 2^32"] = i => new uint256((ulong)i << 32),
            ["wei amounts (multiples of 10^18)"] = i => new uint256((ulong)i * 1_000_000_000_000_000_000UL),
            ["single high bit per key"] = i => new uint256(0, 0, 0, 1UL << (i % 64)),
        };

        foreach ((string name, Func<int, uint256> make) in families)
        {
            var prime = new int[PrimeBuckets];
            var pow2 = new int[PowerOfTwoBuckets];
            var distinct = new HashSet<int>();
            int keys = name.StartsWith("single", StringComparison.Ordinal) ? 64 : Count;

            for (int i = 0; i < keys; i++)
            {
                int hash = make(i).GetHashCode();
                prime[(uint)hash % PrimeBuckets]++;
                pow2[hash & (PowerOfTwoBuckets - 1)]++;
                distinct.Add(hash);
            }

            if (keys == Count)
            {
                // Uniform random expectation: mean 64 per bucket (worst of 1,024 about 100). In 8,000 simulated key sets the
                // worst bucket was 116; the 4x-mean limit leaves more than twice that headroom, so chance failures are negligible.
                Assert.IsLessThan(4 * Count / PrimeBuckets, prime.Max(), $"{name}: prime buckets");
                Assert.IsLessThan(4 * Count / PowerOfTwoBuckets, pow2.Max(), $"{name}: power-of-two buckets");
                Assert.IsGreaterThan(Count - 40, distinct.Count, $"{name}: distinct hashes"); // ~0.5 expected 32-bit collisions
            }
            else
            {
                Assert.IsGreaterThan(keys - 4, distinct.Count, $"{name}: distinct hashes");
            }
        }
    }

    [TestMethod]
    public void SingleBitFlips_ChangeAboutHalfTheHashBits()
    {
        var rng = new Random(3);
        long flippedBits = 0;
        long trials = 0;
        for (int i = 0; i < 200; i++)
        {
            var value = new uint256((ulong)rng.NextInt64(), (ulong)rng.NextInt64(), (ulong)rng.NextInt64(), (ulong)rng.NextInt64());
            int hash = value.GetHashCode();
            for (int bit = 0; bit < 256; bit++)
            {
                BigInteger flipped = value.ToBigInteger() ^ (BigInteger.One << bit);
                flippedBits += BitOperations.PopCount((uint)(hash ^ ((uint256)flipped).GetHashCode()));
                trials++;
            }
        }

        double average = (double)flippedBits / trials;
        Assert.IsTrue(average is > 14.0 and < 18.0, $"average flipped hash bits = {average:F2}, expected about 16");
    }

    [TestMethod]
    public void Dictionary_WithAdversarialLookingKeys_BehavesAndFindsEverything()
    {
        var dictionary = new Dictionary<uint256, int>();
        for (int i = 0; i < 50_000; i++)
            dictionary[new uint256((ulong)i * 1021UL)] = i;

        for (int i = 0; i < 50_000; i++)
            Assert.AreEqual(i, dictionary[new uint256((ulong)i * 1021UL)]);

        var signed = new Dictionary<int256, int>(Int256Comparer.Instance);
        for (int i = -25_000; i < 25_000; i++)
            signed[new int256(i)] = i;
        for (int i = -25_000; i < 25_000; i++)
            Assert.AreEqual(i, signed[new int256(i)]);
    }

    [TestMethod]
    public void ProcessKeys_AreNonZeroAndDistinct_AndFinalMultiplierIsOdd()
    {
        // A constant or zero key would defeat the per-process seeding without failing any behavioural test.
        var type = typeof(uint256).Assembly.GetType("EccentricWare.Web3.DataTypes.Utils.Hash256", throwOnError: true)!;
        var keys = new List<ulong>();
        foreach (string name in new[] { "s_key0", "s_key1", "s_key2", "s_key3" })
        {
            var field = type.GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(field, name);
            keys.Add((ulong)field.GetValue(null)!);
        }

        foreach (ulong key in keys)
            Assert.AreNotEqual(0UL, key);
        Assert.HasCount(4, keys.Distinct());

        var multiplier = type.GetField("s_finalMultiplier", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(multiplier);
        Assert.AreEqual(1UL, (ulong)multiplier.GetValue(null)! & 1UL, "the avalanche multiplier must be odd to be a bijection");
    }
}
