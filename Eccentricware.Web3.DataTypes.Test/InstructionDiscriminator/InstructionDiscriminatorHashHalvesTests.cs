namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Both 32-bit halves of the 64-bit value must reach the hash code. The multiplier is random per process, so the tests
/// bound collisions instead of asserting values. A hash that ignored one half would map every key of that half's family
/// to one code (all but one key colliding).
/// Bounds and failure probability: the multiply-and-fold has a heavy tail across multipliers (a multiplier with a small
/// low or high word), so the families have at most 20,000 keys and the bounds are far from typical values.
/// A simulation of the hash (200,000 random multipliers) gave at most 136 collisions in 20,000 sequential low-half keys
/// (0.7%; the bound allows 10%), 0 for high-half-only families (the code is then a bijection of the key), 3 for
/// random 32-bit families, and 0 of 64 for single-bit keys. A second simulation drew multipliers log-uniformly in both
/// 32-bit words (so rare small words are covered) and estimated the failure probability of each bound at below 1e-10
/// per run. The byte-lane (>= 16 distinct of 256) and single-bit (>= 36 of 64) bounds sit above what ignoring a half
/// leaves (1 and 32 distinct codes).
/// </summary>
[TestClass]
public sealed class InstructionDiscriminatorHashHalvesTests
{
    private const int FamilySize = 20_000;

    private static int DistinctHashCodes(IEnumerable<ulong> values)
        => values.Select(v => new InstructionDiscriminator(v).GetHashCode()).Distinct().Count();

    private static ulong[] RandomUInt32s(int seed)
    {
        var random = new Random(seed);
        var seen = new HashSet<uint>();
        while (seen.Count < FamilySize)
            seen.Add((uint)random.NextInt64(0, 1L << 32));
        return seen.Select(v => (ulong)v).ToArray();
    }

    private static void AssertNearlyAllDistinct(IReadOnlyCollection<ulong> keys, string family)
    {
        int distinct = DistinctHashCodes(keys);
        Assert.IsGreaterThanOrEqualTo(keys.Count * 9 / 10, distinct, $"{family}: {distinct} distinct hash codes for {keys.Count} keys");
    }

    [TestMethod]
    public void HighHalfOnly_SequentialKeys_StayDistinct()
        => AssertNearlyAllDistinct(Enumerable.Range(0, FamilySize).Select(i => (ulong)i << 32).ToArray(), "high half sequential");

    [TestMethod]
    public void HighHalfOnly_RandomKeys_StayDistinct()
        => AssertNearlyAllDistinct(RandomUInt32s(31).Select(v => v << 32).ToArray(), "high half random");

    [TestMethod]
    public void LowHalfOnly_SequentialKeys_StayDistinct()
        => AssertNearlyAllDistinct(Enumerable.Range(0, FamilySize).Select(i => (ulong)i).ToArray(), "low half sequential");

    [TestMethod]
    public void LowHalfOnly_RandomKeys_StayDistinct()
        => AssertNearlyAllDistinct(RandomUInt32s(32), "low half random");

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    public void SingleByteLane_AllValues_ReachTheHash(int lane)
    {
        // 256 keys differing only in one byte of the discriminator; ignoring that byte would leave one code.
        ulong[] keys = Enumerable.Range(0, 256).Select(i => (ulong)i << (8 * lane)).ToArray();

        int distinct = DistinctHashCodes(keys);

        Assert.IsGreaterThanOrEqualTo(16, distinct, $"lane {lane}: {distinct} distinct hash codes for 256 keys");
    }

    [TestMethod]
    public void SingleBitKeys_AreDistinct()
    {
        ulong[] keys = Enumerable.Range(0, 64).Select(bit => 1UL << bit).ToArray();

        int distinct = DistinctHashCodes(keys);

        Assert.IsGreaterThanOrEqualTo(36, distinct, $"{distinct} distinct hash codes for 64 single-bit keys");
    }

    [TestMethod]
    public void EveryOutputBit_TakesBothValues_OverRandomKeys()
    {
        // 2,000 random keys: a bit that stays constant (for example a hash truncated to fewer bits) fails; a working hash
        // leaves a bit constant only if the multiplier is degenerate (probability negligible).
        var random = new Random(33);
        int[] codes = Enumerable.Range(0, 2_000)
            .Select(_ => new InstructionDiscriminator((ulong)random.NextInt64() ^ ((ulong)random.NextInt64() << 1)).GetHashCode())
            .ToArray();

        for (int bit = 0; bit < 32; bit++)
        {
            int ones = codes.Count(c => ((uint)c >> bit & 1u) != 0);
            Assert.IsTrue(ones > 0 && ones < codes.Length, $"output bit {bit} is constant (ones={ones})");
        }
    }
}
