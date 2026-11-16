namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="InstructionDiscriminator"/> hash codes against attacker-chosen keys, and use as a dictionary key.</summary>
[TestClass]
public sealed class InstructionDiscriminatorHashingTests
{
    private static int MaxBucket(IEnumerable<int> hashCodes, uint bucketCount)
    {
        var buckets = new int[bucketCount];
        foreach (int code in hashCodes)
            buckets[(uint)code % bucketCount]++;
        return buckets.Max();
    }

    [TestMethod]
    public void GetHashCode_SpreadsKeysThatCollapseTheOldHash()
    {
        // Values whose low and high halves are equal XOR to zero under (int)v ^ (int)(v >> 32), so every one of them
        // used to hash to 0. They must now spread too.
        IEnumerable<ulong> keys = Enumerable.Range(1, 3999).Select(i => ((ulong)(uint)i << 32) | (uint)i);
        Assert.IsLessThan(45, MaxBucket(keys.Select(k => new InstructionDiscriminator(k).GetHashCode()), 1031));

        foreach (uint prime in new uint[] { 1031, 4099 })
        {
            IEnumerable<int> codes = Enumerable.Range(1, 3999).Select(i => new InstructionDiscriminator((ulong)i * prime).GetHashCode());
            Assert.IsLessThan(45, MaxBucket(codes, prime), $"prime={prime}");
        }
    }

    [TestMethod]
    public void GetHashCode_SequentialKeys_RarelyCollide()
    {
        var codes = new HashSet<int>();
        for (uint i = 0; i < 100_000; i++)
            codes.Add(new InstructionDiscriminator(i).GetHashCode());

        // The hash is multiplicative with a per-process random multiplier, so the collision count over 100,000 sequential keys depends on the
        // process. Measured over 4,000 random multipliers: mean 1.2, p99 8, maximum 44, with a heavier tail than a random hash would have
        // (about 1 multiplier in 2,000 exceeds 20). A real defect (identity-like, ignoring bits, constant) collides thousands of keys, so
        // the bound is 1% of the keys: it fails for a broken hash and not for an unlucky multiplier.
        Assert.IsGreaterThanOrEqualTo(100_000 - 1_000, codes.Count);
    }

    [TestMethod]
    public void Dictionary_WithDiscriminatorKeys_WorksByValue()
    {
        var discriminators = new Dictionary<InstructionDiscriminator, string> { [new InstructionDiscriminator(42)] = "init" };

        Assert.AreEqual("init", discriminators[new InstructionDiscriminator(42)]);
        Assert.IsFalse(discriminators.ContainsKey(new InstructionDiscriminator(43)));
    }
}
