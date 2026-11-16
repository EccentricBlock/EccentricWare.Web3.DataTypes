using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// FunctionSelector, InstructionDiscriminator and AddressSet hash with seeds drawn once per process, so that an attacker
/// who chooses keys cannot precompute collisions. Each isolated copy of the library draws its own seeds and stands in
/// for another process (see <see cref="IsolatedLibraryCopies"/>). No test asserts a specific hash value.
/// Failure probabilities: two independent 64-bit seeds are equal with probability 2^-63; two copies give the same hash
/// code for one random key with probability about 2^-32, so agreement at a quarter of 1,000 keys, or identical tables of
/// 256 members, needs the seeds to be effectively equal (probability far below 1e-9).
/// </summary>
[TestClass]
public sealed class PerProcessHashSeedTests
{
    private const int KeyCount = 1_000;

    private static List<object> RandomUInt32Keys()
    {
        var random = new Random(2024);
        return Enumerable.Range(0, KeyCount).Select(_ => (object)(uint)random.NextInt64(0, 1L << 32)).ToList();
    }

    private static List<object> RandomUInt64Keys()
    {
        var random = new Random(2025);
        return Enumerable.Range(0, KeyCount).Select(_ => (object)(ulong)random.NextInt64()).ToList();
    }

    // Two copies agree at a given random key with probability about 2^-32, so a quarter of the keys is far beyond chance;
    // a constant seed makes every key agree.
    private static void AssertEveryPairOfCopiesDisagrees<T>(T[][] observations, string what)
    {
        for (int a = 0; a < observations.Length; a++)
        {
            for (int b = a + 1; b < observations.Length; b++)
            {
                int equal = observations[a].Zip(observations[b]).Count(pair => EqualityComparer<T>.Default.Equals(pair.First, pair.Second));
                Assert.IsLessThan(observations[a].Length / 4, equal, $"{what}: copies {a} and {b} agree at {equal} of {observations[a].Length} positions");
            }
        }
    }

    private static void AssertAllDistinct(ulong[] values, string what)
        => Assert.HasCount(values.Length, values.Distinct(), $"{what}: every copy must draw its own value");

    // ---- FunctionSelector ----

    [TestMethod]
    public void FunctionSelector_EachCopyDrawsItsOwnMultiplier()
    {
        using var copies = new IsolatedLibraryCopies();

        ulong[] multipliers = copies.ReadStaticFromEveryCopy(typeof(FunctionSelector), "s_hashMultiplier");

        AssertAllDistinct(multipliers, "FunctionSelector multiplier");
        Assert.IsTrue(multipliers.All(m => (m & 1UL) == 1UL), "multipliers stay odd");
    }

    [TestMethod]
    public void FunctionSelector_CopiesHashTheSameKeysDifferently()
    {
        using var copies = new IsolatedLibraryCopies();

        int[][] hashes = copies.HashCodesFromEveryCopy(typeof(FunctionSelector), RandomUInt32Keys());

        AssertEveryPairOfCopiesDisagrees(hashes, "FunctionSelector.GetHashCode");
    }

    // ---- InstructionDiscriminator ----

    [TestMethod]
    public void InstructionDiscriminator_EachCopyDrawsItsOwnMultiplier()
    {
        using var copies = new IsolatedLibraryCopies();

        ulong[] multipliers = copies.ReadStaticFromEveryCopy(typeof(InstructionDiscriminator), "s_hashMultiplier");

        AssertAllDistinct(multipliers, "InstructionDiscriminator multiplier");
        Assert.IsTrue(multipliers.All(m => (m & 1UL) == 1UL), "multipliers stay odd");
    }

    [TestMethod]
    public void InstructionDiscriminator_CopiesHashTheSameKeysDifferently()
    {
        using var copies = new IsolatedLibraryCopies();

        int[][] hashes = copies.HashCodesFromEveryCopy(typeof(InstructionDiscriminator), RandomUInt64Keys());

        AssertEveryPairOfCopiesDisagrees(hashes, "InstructionDiscriminator.GetHashCode");
    }

    // ---- AddressSet ----

    [TestMethod]
    [DataRow("s_key0")]
    [DataRow("s_key1")]
    [DataRow("s_key2")]
    [DataRow("s_key3")]
    [DataRow("s_finalMultiplier")]
    public void AddressSet_EachCopyDrawsItsOwnSeed(string field)
    {
        using var copies = new IsolatedLibraryCopies();

        AssertAllDistinct(copies.ReadStaticFromEveryCopy(typeof(AddressSet), field), $"AddressSet.{field}");
    }

    [TestMethod]
    public void AddressSet_CopiesPlaceTheSameMembersInDifferentSlots()
    {
        // Identical tables would need the same hash for all 256 members (probability below 2^-200 for independent seeds).
        var random = new Random(2026);
        byte[] bytes = new byte[Address.EvmByteLength];
        string[] members = Enumerable.Range(0, 256)
            .Select(_ => { random.NextBytes(bytes); return "0x" + Convert.ToHexString(bytes).ToLowerInvariant(); })
            .ToArray();
        using var copies = new IsolatedLibraryCopies();

        ulong[][] tables = copies.AddressSetTablesFromEveryCopy(members);

        foreach (ulong[] table in tables)
            Assert.AreEqual(members.Length, table.Count(slot => slot != 0), "every member is stored");
        for (int a = 0; a < tables.Length; a++)
        {
            for (int b = a + 1; b < tables.Length; b++)
            {
                CollectionAssert.AreNotEqual(tables[a], tables[b], $"copies {a} and {b} stored identical hashes");
                bool[] occupiedA = tables[a].Select(slot => slot != 0).ToArray();
                bool[] occupiedB = tables[b].Select(slot => slot != 0).ToArray();
                CollectionAssert.AreNotEqual(occupiedA, occupiedB, $"copies {a} and {b} used identical slots");
            }
        }
    }
}
