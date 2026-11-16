using EccentricWare.Web3.DataTypes.Utils;
using System.Collections.Concurrent;
using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// HashSeed draws from the OS RNG, so these tests check invariants and statistical sanity with margins that are many
/// standard deviations wide (a false failure is astronomically unlikely).
/// shortcut: the retry loop in NextKey for a zero draw is not reachable (RandomNumberGenerator.Fill has no seam and a
/// zero draw has probability 2^-64); add an RNG seam if one is ever introduced.
/// </summary>
[TestClass]
public sealed class HashSeedTests
{
    private static ulong[] Draw(Func<ulong> next, int count)
    {
        var values = new ulong[count];
        for (int i = 0; i < count; i++)
            values[i] = next();
        return values;
    }

    // ---- NextOddMultiplier ----

    [TestMethod]
    public void NextOddMultiplier_IsAlwaysOdd_AndThereforeNonZero()
    {
        foreach (ulong value in Draw(HashSeed.NextOddMultiplier, 10_000))
        {
            Assert.AreEqual(1UL, value & 1UL);
            Assert.AreNotEqual(0UL, value);
        }
    }

    [TestMethod]
    public void NextOddMultiplier_IsRandomAcrossCalls()
    {
        ulong[] values = Draw(HashSeed.NextOddMultiplier, 1_000);

        // 1,000 draws from 2^63 odd values: a repeat has probability about 3e-14.
        Assert.IsGreaterThanOrEqualTo(999, values.Distinct().Count());
    }

    [TestMethod]
    public void NextOddMultiplier_FillsAll64Bits_WithBitZeroForcedToOne()
    {
        ulong[] values = Draw(HashSeed.NextOddMultiplier, 1_000);
        Assert.IsTrue(values.All(v => (v & 1UL) == 1UL));

        // A buffer only partly filled would leave high bytes at zero.
        for (int bit = 1; bit < 64; bit++)
        {
            int ones = values.Count(v => ((v >> bit) & 1UL) == 1UL);
            Assert.IsTrue(ones > 0 && ones < values.Length, $"bit {bit} never varied (ones={ones})");
        }
    }

    [TestMethod]
    public void NextOddMultiplier_BitsAreRoughlyBalanced()
    {
        ulong[] values = Draw(HashSeed.NextOddMultiplier, 4_096);

        for (int bit = 1; bit < 64; bit++)
        {
            double fraction = values.Count(v => ((v >> bit) & 1UL) == 1UL) / (double)values.Length;
            Assert.IsTrue(fraction is > 0.35 and < 0.65, $"bit {bit} fraction {fraction}");
        }

        // Bit 0 is forced to 1, so the expected popcount is 32.5.
        double meanPopCount = values.Average(v => (double)BitOperations.PopCount(v));
        Assert.IsTrue(Math.Abs(meanPopCount - 32.5) < 1.5, $"mean popcount {meanPopCount}");
    }

    // ---- NextKey ----

    [TestMethod]
    public void NextKey_IsNeverZero()
    {
        foreach (ulong value in Draw(HashSeed.NextKey, 10_000))
            Assert.AreNotEqual(0UL, value);
    }

    [TestMethod]
    public void NextKey_IsRandomAcrossCalls()
    {
        ulong[] values = Draw(HashSeed.NextKey, 1_000);

        Assert.IsGreaterThanOrEqualTo(999, values.Distinct().Count());
    }

    [TestMethod]
    public void NextKey_FillsAll64Bits_IncludingBitZero()
    {
        ulong[] values = Draw(HashSeed.NextKey, 1_000);

        // Bit 0 varying shows the key is not accidentally forced odd like the multiplier.
        for (int bit = 0; bit < 64; bit++)
        {
            int ones = values.Count(v => ((v >> bit) & 1UL) == 1UL);
            Assert.IsTrue(ones > 0 && ones < values.Length, $"bit {bit} never varied (ones={ones})");
        }
    }

    [TestMethod]
    public void NextKey_BitsAreRoughlyBalanced()
    {
        ulong[] values = Draw(HashSeed.NextKey, 4_096);

        for (int bit = 0; bit < 64; bit++)
        {
            double fraction = values.Count(v => ((v >> bit) & 1UL) == 1UL) / (double)values.Length;
            Assert.IsTrue(fraction is > 0.35 and < 0.65, $"bit {bit} fraction {fraction}");
        }

        double meanPopCount = values.Average(v => (double)BitOperations.PopCount(v));
        Assert.IsTrue(Math.Abs(meanPopCount - 32.0) < 1.5, $"mean popcount {meanPopCount}");
    }

    [TestMethod]
    public void NextKey_AndNextOddMultiplier_AreIndependentDraws()
    {
        // The same call sequence never yields a shared value pattern: keys and multipliers drawn together do not collide.
        var all = new HashSet<ulong>();
        for (int i = 0; i < 500; i++)
        {
            Assert.IsTrue(all.Add(HashSeed.NextKey()));
            Assert.IsTrue(all.Add(HashSeed.NextOddMultiplier()));
        }
    }

    // ---- thread safety ----

    [TestMethod]
    public void ParallelCalls_AreThreadSafe_ProduceValidDistinctValues()
    {
        const int threads = 8;
        const int perThread = 1_000;
        var keys = new ConcurrentBag<ulong>();
        var multipliers = new ConcurrentBag<ulong>();

        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, _ =>
        {
            for (int i = 0; i < perThread; i++)
            {
                keys.Add(HashSeed.NextKey());
                multipliers.Add(HashSeed.NextOddMultiplier());
            }
        });

        Assert.HasCount(threads * perThread, keys);
        Assert.HasCount(threads * perThread, multipliers);
        Assert.IsTrue(keys.All(k => k != 0));
        Assert.IsTrue(multipliers.All(m => (m & 1UL) == 1UL));
        // 8,000 draws from 2^63..2^64 values: a repeat has probability about 1e-12.
        Assert.AreEqual(threads * perThread, keys.Distinct().Count());
        Assert.AreEqual(threads * perThread, multipliers.Distinct().Count());
    }
}
