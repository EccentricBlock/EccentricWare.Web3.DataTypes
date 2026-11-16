using EccentricWare.Web3.DataTypes.Utils;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Hash256 is a per-process keyed hash, so tests never assert specific hash values. Distinctness and distribution
/// checks use margins far wider than the random variation they allow for.
/// </summary>
[TestClass]
public sealed class Hash256Tests
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly string[] KeyFieldNames = ["s_key0", "s_key1", "s_key2", "s_key3", "s_finalMultiplier"];

    private static ulong NextUlong(Random random)
    {
        Span<byte> bytes = stackalloc byte[8];
        random.NextBytes(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    private static ulong[] RandomLimbs(Random random)
    {
        var limbs = new ulong[4];
        for (int i = 0; i < 4; i++)
            limbs[i] = NextUlong(random);
        return limbs;
    }

    private static int Compute(ulong[] limbs) => Hash256.Compute(limbs[0], limbs[1], limbs[2], limbs[3]);

    private static ulong ReadStatic(Type type, string name)
        => (ulong)type.GetField(name, PrivateStatic)!.GetValue(null)!;

    // ---- determinism and sensitivity ----

    [TestMethod]
    public void Compute_IsDeterministicWithinAProcess()
    {
        var random = new Random(1);
        for (int i = 0; i < 10_000; i++)
        {
            ulong[] limbs = RandomLimbs(random);
            Assert.AreEqual(Compute(limbs), Compute(limbs));
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void Compute_ChangingASingleLimb_ChangesTheHash(int limb)
    {
        var random = new Random(100 + limb);
        for (int i = 0; i < 1_000; i++)
        {
            ulong[] original = RandomLimbs(random);
            ulong[] changed = (ulong[])original.Clone();
            changed[limb] ^= NextUlong(random) | 1UL;

            // A false failure needs a 32-bit hash collision: about 2^-32 per sample.
            Assert.AreNotEqual(Compute(original), Compute(changed), $"limb={limb} i={i}");
        }
    }

    [TestMethod]
    public void Compute_EverySingleInputBit_ChangesTheHash()
    {
        ulong[] baseLimbs = [0x0123456789ABCDEFUL, 0xFEDCBA9876543210UL, 0x0F1E2D3C4B5A6978UL, 0x8899AABBCCDDEEFFUL];
        int baseline = Compute(baseLimbs);

        for (int bit = 0; bit < 256; bit++)
        {
            ulong[] flipped = (ulong[])baseLimbs.Clone();
            flipped[bit / 64] ^= 1UL << (bit % 64);
            Assert.AreNotEqual(baseline, Compute(flipped), $"bit={bit}");
        }
    }

    [TestMethod]
    [DataRow(1UL)]
    [DataRow(ulong.MaxValue)]
    [DataRow(0x8000000000000000UL)]
    public void Compute_LimbPositionMatters(ulong x)
    {
        int[] hashes =
        [
            Hash256.Compute(x, 0, 0, 0),
            Hash256.Compute(0, x, 0, 0),
            Hash256.Compute(0, 0, x, 0),
            Hash256.Compute(0, 0, 0, x),
        ];

        Assert.HasCount(4, hashes.Distinct());
    }

    [TestMethod]
    public void Compute_AllPermutationsOfFourLimbs_AreDistinct()
    {
        ulong[] values = [1, 2, 3, 4];
        var hashes = new HashSet<int>();
        int permutations = 0;
        foreach (int a in Enumerable.Range(0, 4))
            foreach (int b in Enumerable.Range(0, 4).Where(i => i != a))
                foreach (int c in Enumerable.Range(0, 4).Where(i => i != a && i != b))
                {
                    int d = 6 - a - b - c;
                    hashes.Add(Hash256.Compute(values[a], values[b], values[c], values[d]));
                    permutations++;
                }

        Assert.AreEqual(24, permutations);
        Assert.HasCount(24, hashes);
    }

    [TestMethod]
    public void Compute_SwappingTheTwoHalvesOfTheInput_ChangesTheHash()
    {
        var random = new Random(7);
        for (int i = 0; i < 1_000; i++)
        {
            ulong[] l = RandomLimbs(random);
            Assert.AreNotEqual(
                Hash256.Compute(l[0], l[1], l[2], l[3]),
                Hash256.Compute(l[2], l[3], l[0], l[1]),
                $"i={i}");
        }
    }

    [TestMethod]
    public void Compute_ZeroAndAllOnes_AreStable_AndDiffer()
    {
        int zero = Hash256.Compute(0, 0, 0, 0);
        int ones = Hash256.Compute(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue);

        Assert.AreEqual(zero, Hash256.Compute(0, 0, 0, 0));
        Assert.AreEqual(ones, Hash256.Compute(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue));
        Assert.AreNotEqual(zero, ones);
    }

    // ---- structured / narrow-range inputs ----

    public static IEnumerable<object[]> NarrowFamilies()
    {
        yield return ["sequential u3", (Func<ulong, int>)(i => Hash256.Compute(0, 0, 0, i))];
        yield return ["shifted u0 (i<<32)", (Func<ulong, int>)(i => Hash256.Compute(i << 32, 0, 0, 0))];
        yield return ["u3 = i*1021", (Func<ulong, int>)(i => Hash256.Compute(0, 0, 0, i * 1021))];
        yield return ["u3 = i*10^18", (Func<ulong, int>)(i => Hash256.Compute(0, 0, 0, i * 1_000_000_000_000_000_000UL))];
        yield return ["equal halves in u2", (Func<ulong, int>)(i => Hash256.Compute(0, 0, (i << 32) | i, 0))];
        yield return ["address pattern u3 ^ type 1", (Func<ulong, int>)(i => Hash256.Compute(i, 0, 0, i ^ 1UL))];
    }

    [TestMethod]
    [DynamicData(nameof(NarrowFamilies))]
    public void Compute_NarrowRangeInputs_AreDistinct_AndSpreadOverBuckets(string family, Func<ulong, int> hash)
    {
        const int count = 10_000;
        int[] hashes = new int[count];
        for (int i = 0; i < count; i++)
            hashes[i] = hash((ulong)i + 1);

        // Expected birthday collisions among 10,000 random 32-bit codes: about 0.01.
        Assert.IsGreaterThanOrEqualTo(count - 40, hashes.Distinct().Count(), family);

        foreach (int buckets in new[] { 1021, 1024 })
        {
            int[] load = new int[buckets];
            foreach (int h in hashes)
                load[(uint)h % (uint)buckets]++;

            double mean = count / (double)buckets;
            Assert.IsLessThan(4 * mean, load.Max(), $"{family}: {buckets} buckets");
        }
    }

    [TestMethod]
    public void Compute_RandomInputs_AreAlmostAllDistinct()
    {
        var random = new Random(11);
        var hashes = new HashSet<int>();
        for (int i = 0; i < 10_000; i++)
            hashes.Add(Compute(RandomLimbs(random)));

        Assert.IsGreaterThanOrEqualTo(9_960, hashes.Count);
    }

    // ---- avalanche ----

    [TestMethod]
    public void Compute_SingleBitFlip_ChangesAboutHalfTheOutputBits()
    {
        var random = new Random(21);
        const int trials = 5_000;
        long flippedBits = 0;

        for (int i = 0; i < trials; i++)
        {
            ulong[] limbs = RandomLimbs(random);
            int bit = random.Next(256);
            ulong[] flipped = (ulong[])limbs.Clone();
            flipped[bit / 64] ^= 1UL << (bit % 64);

            flippedBits += BitOperations.PopCount((uint)(Compute(limbs) ^ Compute(flipped)));
        }

        double mean = flippedBits / (double)trials;
        Assert.IsTrue(mean is > 14 and < 18, $"mean flipped output bits {mean}");
    }

    [TestMethod]
    public void Compute_EachOutputBit_FlipsBetween35And65PercentOfTheTime()
    {
        var random = new Random(22);
        const int trials = 20_000;
        int[] flips = new int[32];

        for (int i = 0; i < trials; i++)
        {
            ulong[] limbs = RandomLimbs(random);
            int bit = random.Next(256);
            ulong[] flipped = (ulong[])limbs.Clone();
            flipped[bit / 64] ^= 1UL << (bit % 64);

            uint diff = (uint)(Compute(limbs) ^ Compute(flipped));
            for (int outputBit = 0; outputBit < 32; outputBit++)
                flips[outputBit] += (int)((diff >> outputBit) & 1U);
        }

        for (int outputBit = 0; outputBit < 32; outputBit++)
        {
            double fraction = flips[outputBit] / (double)trials;
            Assert.IsTrue(fraction is > 0.35 and < 0.65, $"output bit {outputBit} flips {fraction:P1} of the time");
        }
    }

    // ---- keys ----

    [TestMethod]
    public void StaticKeys_AreNonZero_Distinct_AndTheFinalMultiplierIsOdd()
    {
        ulong[] values = KeyFieldNames.Select(n => ReadStatic(typeof(Hash256), n)).ToArray();

        Assert.IsTrue(values.All(v => v != 0), "a key is zero");
        Assert.HasCount(5, values.Distinct());
        Assert.AreEqual(1UL, values[4] & 1UL, "final multiplier must be odd");
        foreach (string name in KeyFieldNames)
            Assert.AreEqual(typeof(ulong), typeof(Hash256).GetField(name, PrivateStatic)!.FieldType, name);
    }

    [TestMethod]
    [DataRow(0UL, 12345UL, 0UL)]
    [DataRow(1UL, 987654321UL, 987654321UL)]
    [DataRow(ulong.MaxValue, 1UL, ulong.MaxValue)]
    [DataRow(1UL << 32, 1UL << 32, 1UL)]
    [DataRow(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue)]
    public void Mix_FoldsTheFull128BitProduct(ulong a, ulong b, ulong expected)
    {
        // Expected: high 64 bits xor low 64 bits of a*b, worked out by hand:
        // 2^64-1 squared = 2^128 - 2^65 + 1 -> high 0xFFFF_FFFF_FFFF_FFFE, low 1 -> xor all ones; 2^32 * 2^32 = 2^64 -> 1.
        MethodInfo mix = typeof(Hash256).GetMethod("Mix", PrivateStatic)!;

        Assert.AreEqual(expected, (ulong)mix.Invoke(null, [a, b])!);
    }

    [TestMethod]
    public void Compute_MatchesAnIndependentUInt128Model_ForRandomInputs()
    {
        // Refactor guard: the model follows the documented algorithm and catches a truncated 128-bit product.
        ulong k0 = ReadStatic(typeof(Hash256), "s_key0");
        ulong k1 = ReadStatic(typeof(Hash256), "s_key1");
        ulong k2 = ReadStatic(typeof(Hash256), "s_key2");
        ulong k3 = ReadStatic(typeof(Hash256), "s_key3");
        ulong multiplier = ReadStatic(typeof(Hash256), "s_finalMultiplier");

        static ulong FoldedProduct(ulong x, ulong y)
        {
            UInt128 product = (UInt128)x * y;
            return (ulong)(product >> 64) ^ (ulong)product;
        }

        var random = new Random(31);
        for (int i = 0; i < 1_000; i++)
        {
            ulong[] l = RandomLimbs(random);
            unchecked
            {
                ulong h = FoldedProduct(l[0] ^ k0, l[1] ^ k1) + FoldedProduct(l[2] ^ k2, l[3] ^ k3);
                h ^= h >> 32;
                h *= multiplier;
                Assert.AreEqual((int)(h >> 32), Compute(l), $"i={i}");
            }
        }
    }

    // ---- concurrency ----

    [TestMethod]
    public void Compute_IsThreadSafe_ParallelResultsEqualSequentialResults()
    {
        const int workers = 16;
        const int perWorker = 10_000;
        var random = new Random(41);
        ulong[][] inputs = Enumerable.Range(0, perWorker).Select(_ => RandomLimbs(random)).ToArray();
        int[] sequential = inputs.Select(Compute).ToArray();
        int[][] parallel = new int[workers][];

        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, worker =>
        {
            parallel[worker] = inputs.Select(Compute).ToArray();
        });

        foreach (int[] result in parallel)
            CollectionAssert.AreEqual(sequential, result);
    }

    // ---- per-process randomness ----

    [TestMethod]
    public void IsolatedCopiesOfTheAssembly_GetIndependentRandomKeys()
    {
        // Each AssemblyLoadContext holds its own copy of the static state, standing in for a second process.
        // A regression to a fixed seed (or a constant key) would make the copies agree.
        string path = typeof(Hash256).Assembly.Location;
        Type defaultContext = typeof(Hash256);
        var contexts = new List<AssemblyLoadContext>();
        try
        {
            var copies = new List<Type>();
            for (int i = 0; i < 2; i++)
            {
                var context = new AssemblyLoadContext($"hash256-isolated-{i}", isCollectible: true);
                contexts.Add(context);
                Assembly copy = context.LoadFromAssemblyPath(path);
                Assert.AreNotSame(defaultContext.Assembly, copy);
                Type type = copy.GetType(defaultContext.FullName!, throwOnError: true)!;
                Assert.AreNotSame(defaultContext, type);
                copies.Add(type);
            }

            Type[] all = [defaultContext, .. copies];
            foreach (string field in KeyFieldNames)
            {
                // Probability that two independent 64-bit draws agree: 2^-64 per pair.
                Assert.HasCount(3, all.Select(t => ReadStatic(t, field)).Distinct(), field);
            }

            int[] hashes = all
                .Select(t => (int)t.GetMethod("Compute")!.Invoke(null, [1UL, 2UL, 3UL, 4UL])!)
                .ToArray();
            // 3 pairs, each colliding with probability 2^-32.
            Assert.HasCount(3, hashes.Distinct());
        }
        finally
        {
            foreach (AssemblyLoadContext context in contexts)
                context.Unload();
        }
    }
}
