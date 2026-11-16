namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Every byte of the 32-bit selector, and every bit of the 32-bit hash code, must matter. The multiplier is random per
/// process, so the tests bound collisions. A hash ignoring a byte collapses that byte's 256-key lane to one code.
/// Bounds and failure probability: simulation of the multiply-and-fold over 300,000 random multipliers gave at most 9
/// collisions in 256 single-byte-lane keys, 34 in 20,000 sequential keys (0.2%; the bound allows 10%) and 3 in 20,000
/// random keys; a second simulation drew multipliers log-uniformly in both 32-bit words (covering the heavy tail of
/// small words) and estimated the failure probability at below 1e-10 per run for each bound.
/// </summary>
[TestClass]
public sealed class FunctionSelectorHashValueCoverageTests
{
    private static int DistinctHashCodes(IEnumerable<uint> values)
        => values.Select(v => new FunctionSelector(v).GetHashCode()).Distinct().Count();

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void SingleByteLane_AllValues_ReachTheHash(int lane)
    {
        uint[] keys = Enumerable.Range(0, 256).Select(i => (uint)i << (8 * lane)).ToArray();

        int distinct = DistinctHashCodes(keys);

        Assert.IsGreaterThanOrEqualTo(16, distinct, $"lane {lane}: {distinct} distinct hash codes for 256 keys");
    }

    [TestMethod]
    public void SequentialAndRandomKeys_StayNearlyDistinct()
    {
        const int count = 20_000;
        var random = new Random(41);
        uint[] randomKeys = Enumerable.Range(0, count).Select(_ => (uint)random.NextInt64(0, 1L << 32)).Distinct().ToArray();
        uint[] sequentialKeys = Enumerable.Range(0, count).Select(i => (uint)i).ToArray();

        int distinctSequential = DistinctHashCodes(sequentialKeys);
        int distinctRandom = DistinctHashCodes(randomKeys);

        Assert.IsGreaterThanOrEqualTo(count * 9 / 10, distinctSequential, $"sequential: {distinctSequential} distinct of {count}");
        Assert.IsGreaterThanOrEqualTo(randomKeys.Length * 9 / 10, distinctRandom, $"random: {distinctRandom} distinct of {randomKeys.Length}");
    }

    [TestMethod]
    public void EveryOutputBit_TakesBothValues_OverRandomKeys()
    {
        // A hash truncated to fewer than 32 bits leaves its top bits constant; with 2,000 random keys a working hash
        // leaves a bit constant only for a degenerate multiplier (probability negligible).
        var random = new Random(42);
        int[] codes = Enumerable.Range(0, 2_000)
            .Select(_ => new FunctionSelector((uint)random.NextInt64(0, 1L << 32)).GetHashCode())
            .ToArray();

        for (int bit = 0; bit < 32; bit++)
        {
            int ones = codes.Count(c => ((uint)c >> bit & 1u) != 0);
            Assert.IsTrue(ones > 0 && ones < codes.Length, $"output bit {bit} is constant (ones={ones})");
        }
    }
}
