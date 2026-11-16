namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="FunctionSelector"/> hash codes against attacker-chosen keys, and use as a dictionary key.</summary>
[TestClass]
public sealed class FunctionSelectorHashingTests
{
    private static int MaxBucket(IEnumerable<int> hashCodes, uint bucketCount)
    {
        var buckets = new int[bucketCount];
        foreach (int code in hashCodes)
            buckets[(uint)code % bucketCount]++;
        return buckets.Max();
    }

    [TestMethod]
    public void GetHashCode_IsStableWithinProcess_AndNotTheIdentity()
    {
        int differsFromIdentity = 0;
        for (uint v = 0; v < 64; v++)
        {
            var selector = new FunctionSelector(v * 0x01010101u + 1);
            Assert.AreEqual(selector.GetHashCode(), new FunctionSelector(selector.Value).GetHashCode());
            if (unchecked((int)selector.Value) != selector.GetHashCode())
                differsFromIdentity++;
        }

        // An identity hash differs for none of the 64 keys; a seeded one equals the identity for a key with probability 2^-32.
        Assert.IsGreaterThanOrEqualTo(60, differsFromIdentity);
    }

    [TestMethod]
    public void GetHashCode_SpreadsKeysThatCollapseAnIdentityHash()
    {
        // Multiples of a prime bucket count all land in bucket 0 under an identity hash (Dictionary reduces hash codes
        // modulo a prime). With a per-process multiplier they must spread like random data (expected ~4 per bucket,
        // random worst case ~16).
        foreach (uint prime in new uint[] { 1031, 4099, 16411 })
        {
            IEnumerable<int> codes = Enumerable.Range(1, 3999).Select(i => new FunctionSelector((uint)i * prime).GetHashCode());
            Assert.IsLessThan(45, MaxBucket(codes, prime), $"prime={prime}");
        }
    }

    [TestMethod]
    public void GetHashCode_SequentialKeys_RarelyCollide()
    {
        var codes = new HashSet<int>();
        for (uint i = 0; i < 100_000; i++)
            codes.Add(new FunctionSelector(i).GetHashCode());

        // The hash is multiplicative with a per-process random multiplier, so the collision count over 100,000 sequential keys depends on the
        // process. Measured over 4,000 random multipliers: mean 1.2, p99 8, maximum 44, with a heavier tail than a random hash would have
        // (about 1 multiplier in 2,000 exceeds 20). A real defect (identity-like, ignoring bits, constant) collides thousands of keys, so
        // the bound is 1% of the keys: it fails for a broken hash and not for an unlucky multiplier.
        Assert.IsGreaterThanOrEqualTo(100_000 - 1_000, codes.Count);
    }

    [TestMethod]
    public void Dictionary_WithSelectorKeys_WorksByValue()
    {
        var selectors = new Dictionary<FunctionSelector, string> { [FunctionSelector.Erc20.Transfer] = "transfer" };

        Assert.AreEqual("transfer", selectors[new FunctionSelector(0xa9059cbb)]);
        Assert.IsFalse(selectors.ContainsKey(new FunctionSelector(0xa9059cbc)));
    }
}
