namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// GetHashCode must not collapse structured, attacker-chosen values. It used to be HashCode.Combine over the four limbs,
/// which folds each ulong to 32 bits by XOR (so a limb with equal 32-bit halves hashed like zero) regardless of the
/// per-process seed. These are the regression tests for that defect. Hash codes are per-process seeded, so no test asserts
/// a specific value; distinctness checks only allow birthday-bound slack.
/// </summary>
[TestClass]
public sealed class Hash32HashCodeTests
{
    private const int FamilySize = 10_000;
    private const int MinDistinct = FamilySize * 999 / 1000;

    private static ulong Doubled(uint half) => ((ulong)half << 32) | half;

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void GetHashCode_LimbWithEqualHalves_DoesNotCollideWithZero(int limb)
    {
        // Under the old fold, (x << 32 | x) hashed exactly like 0.
        Assert.AreNotEqual(
            Hash32.Zero.GetHashCode(),
            Hash32TestData.WithLimb(limb, ulong.MaxValue, filler: 0).GetHashCode());
        Assert.AreNotEqual(
            Hash32.Zero.GetHashCode(),
            Hash32TestData.WithLimb(limb, Doubled(0x12345678), filler: 0).GetHashCode());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void GetHashCode_SwappedHalvesOfALimb_Differ(int limb)
    {
        Hash32 a = Hash32TestData.WithLimb(limb, 0x0000000100000002UL, filler: 0);
        Hash32 b = Hash32TestData.WithLimb(limb, 0x0000000200000001UL, filler: 0);

        Assert.AreNotEqual(a, b);
        Assert.AreNotEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void GetHashCode_ValuesDifferingInOneLimb_Differ()
    {
        for (int limb = 0; limb < 4; limb++)
        {
            Hash32 a = Hash32TestData.WithLimb(limb, 1, filler: 0);
            Hash32 b = Hash32TestData.WithLimb(limb, 9, filler: 0);
            Assert.AreNotEqual(a.GetHashCode(), b.GetHashCode(), $"limb={limb}");
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void GetHashCode_EqualHalvesInOneLimb_IsAlmostAllDistinct(int limb)
    {
        var codes = new HashSet<int>();
        for (uint i = 1; i <= FamilySize; i++)
            codes.Add(Hash32TestData.WithLimb(limb, Doubled(i), filler: 0).GetHashCode());

        // Old behaviour: exactly one distinct value. Birthday collisions among 10,000 random 32-bit codes: about 0.01.
        Assert.IsGreaterThanOrEqualTo(MinDistinct, codes.Count);
    }

    [TestMethod]
    public void GetHashCode_EqualHalvesInEveryLimb_IsAlmostAllDistinct()
    {
        var codes = new HashSet<int>();
        for (uint i = 1; i <= FamilySize; i++)
        {
            ulong d = Doubled(i);
            codes.Add(new Hash32(d, d, d, d).GetHashCode());
        }

        Assert.IsGreaterThanOrEqualTo(MinDistinct, codes.Count);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void GetHashCode_MultiplesOfTwoToThe32InOneLimb_AreAlmostAllDistinct(int limb)
    {
        var codes = new HashSet<int>();
        for (uint i = 1; i <= FamilySize; i++)
            codes.Add(Hash32TestData.WithLimb(limb, (ulong)i << 32, filler: 0).GetHashCode());

        Assert.IsGreaterThanOrEqualTo(MinDistinct, codes.Count);
    }

    [TestMethod]
    public void GetHashCode_RandomValues_AreAlmostAllDistinct()
    {
        var codes = new HashSet<int>();
        foreach (Hash32 value in Hash32TestData.RandomHashes(new Random(5), FamilySize))
            codes.Add(value.GetHashCode());

        Assert.IsGreaterThanOrEqualTo(MinDistinct, codes.Count);
    }

    [TestMethod]
    public void GetHashCode_EqualValues_HashEqual()
    {
        Hash32[] first = Hash32TestData.RandomHashes(new Random(77), 1_000);
        Hash32[] second = Hash32TestData.RandomHashes(new Random(77), 1_000);

        for (int i = 0; i < first.Length; i++)
        {
            Assert.AreEqual(first[i], second[i]);
            Assert.AreEqual(first[i].GetHashCode(), second[i].GetHashCode());
        }
    }

    [TestMethod]
    public void GetHashCode_IsStableAcrossRepeatedCalls()
    {
        Hash32 value = Hash32TestData.Value0To31();
        int first = value.GetHashCode();

        for (int i = 0; i < 100; i++)
            Assert.AreEqual(first, value.GetHashCode());
    }

    [TestMethod]
    public void DictionaryAndHashSet_RoundTrip_AdversarialEqualHalfKeys()
    {
        var dictionary = new Dictionary<Hash32, int>();
        var set = new HashSet<Hash32>();
        for (uint i = 1; i <= FamilySize; i++)
        {
            // Every limb pattern the old hash collapsed: equal halves in each limb position.
            Hash32 key = Hash32TestData.WithLimb((int)(i % 4), Doubled(i), filler: 0);
            dictionary.Add(key, (int)i);
            Assert.IsTrue(set.Add(key));
        }

        Assert.HasCount(FamilySize, dictionary);
        Assert.HasCount(FamilySize, set);

        for (uint i = 1; i <= FamilySize; i++)
        {
            Hash32 copy = Hash32TestData.WithLimb((int)(i % 4), Doubled(i), filler: 0);
            Assert.AreEqual((int)i, dictionary[copy]);
            Assert.IsTrue(set.Contains(copy));
        }

        // Near misses (one bit off) are not doubled-half values, so none of them is a key.
        for (uint i = 1; i <= 1_000; i++)
        {
            Hash32 nearMiss = Hash32TestData.WithLimb((int)(i % 4), Doubled(i) ^ 1UL, filler: 0);
            Assert.IsFalse(dictionary.ContainsKey(nearMiss), $"i={i}");
            Assert.IsFalse(set.Contains(nearMiss), $"i={i}");
        }
    }
}
