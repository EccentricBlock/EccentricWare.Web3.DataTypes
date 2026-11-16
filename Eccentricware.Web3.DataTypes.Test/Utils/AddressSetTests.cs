using EccentricWare.Web3.DataTypes.Utils;
using System.Buffers.Binary;
using static EccentricWare.Web3.DataTypes.Tests.AddressTestHelpers;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// <see cref="AddressSet"/>: membership, family separation, duplicates, argument bounds and hash-quality regressions.
/// Table hashing uses per-process random keys, so the two statistical tests cannot be seeded; their margins are many
/// standard deviations wide and the failure messages print the observed values.
/// </summary>
[TestClass]
public sealed class AddressSetTests
{
    [TestMethod]
    public void Set_Empty_ContainsNothing()
    {
        AddressSet set = AddressSet.Build(ReadOnlySpan<Address>.Empty);

        Assert.AreEqual(0, set.Count);
        Assert.IsFalse(set.Contains(Address.ZeroEvm));
        Assert.IsFalse(set.Contains(Address.ZeroSolana));
        Assert.IsFalse(set.Contains(default));
    }

    [TestMethod]
    public void Set_SingleElement_IsFound_AndOthersAreNot()
    {
        Address[] values = RandomEvm(2, 1);
        AddressSet set = AddressSet.Build(values.AsSpan(0, 1));

        Assert.AreEqual(1, set.Count);
        Assert.IsTrue(set.Contains(values[0]));
        Assert.IsFalse(set.Contains(values[1]));
    }

    [TestMethod]
    public void Set_AllInsertedAreFound_AndNoneOfTheOthers()
    {
        Address[] members = RandomEvm(10_000, 2);
        Address[] outsiders = RandomEvm(10_000, 3);
        AddressSet set = AddressSet.Build(members);

        Assert.AreEqual(members.Length, set.Count);
        foreach (Address member in members)
            Assert.IsTrue(set.Contains(member));
        foreach (Address outsider in outsiders)
            Assert.IsFalse(set.Contains(outsider));
    }

    [TestMethod]
    public void Set_MixedFamilies_AreFoundByTheirOwnFamilyOnly()
    {
        Address[] evm = RandomEvm(500, 4);
        Address[] solana = RandomSolana(500, 5);
        AddressSet evmOnly = AddressSet.Build(evm);
        AddressSet solanaOnly = AddressSet.Build(solana);
        AddressSet both = AddressSet.Build([.. evm, .. solana]);

        Assert.AreEqual(1000, both.Count);
        Assert.IsTrue(evm.All(a => both.Contains(a)));
        Assert.IsTrue(solana.All(a => both.Contains(a)));

        // Each single-family set rejects every member of the other family.
        Assert.IsFalse(solana.Any(a => evmOnly.Contains(a)));
        Assert.IsFalse(evm.Any(a => solanaOnly.Contains(a)));
    }

    [TestMethod]
    public void Set_SamePayloadDifferentFamily_AreDistinct()
    {
        Address evm = RandomEvm(1, 6)[0];
        Address solana = SolanaWithSamePayloadAs(evm);

        Assert.AreNotEqual(evm, solana);

        AddressSet evmOnly = AddressSet.Build([evm]);
        Assert.IsTrue(evmOnly.Contains(evm));
        Assert.IsFalse(evmOnly.Contains(solana));

        AddressSet solanaOnly = AddressSet.Build([solana]);
        Assert.IsTrue(solanaOnly.Contains(solana));
        Assert.IsFalse(solanaOnly.Contains(evm));

        AddressSet both = AddressSet.Build([evm, solana]);
        Assert.AreEqual(2, both.Count);
        Assert.IsTrue(both.Contains(evm));
        Assert.IsTrue(both.Contains(solana));
    }

    // Regression for a structural hash collision: an EVM address and the Solana address with the same 20 bytes, eleven
    // zero bytes and a final 0x01 feed identical limbs into the family-folded hash (u3 ^ family is 0 for both). The
    // table may probe one extra slot but must keep them apart. Vector from an independent Base58 reference.
    [TestMethod]
    public void Set_CrossFamilyPairWithIdenticalHashInput_IsStillSeparated()
    {
        Address evm = Address.Parse("0x52908400098527886e0f7030069857d2e4169ee7");
        Address solana = Address.Parse("6ZJF1dsTjHHs8DQU9Zb6JWUj1RPsrebYRQ3xcQZtQtHn");

        Assert.IsFalse(AddressSet.Build([evm]).Contains(solana));
        Assert.IsFalse(AddressSet.Build([solana]).Contains(evm));

        AddressSet both = AddressSet.Build([evm, solana]);
        Assert.AreEqual(2, both.Count);
        Assert.IsTrue(both.Contains(evm));
        Assert.IsTrue(both.Contains(solana));
    }

    [TestMethod]
    public void Set_ZeroAddresses_AreDistinctPerFamily()
    {
        AddressSet evmZero = AddressSet.Build([Address.ZeroEvm]);
        Assert.IsTrue(evmZero.Contains(Address.ZeroEvm));
        Assert.IsTrue(evmZero.Contains(default));
        Assert.IsFalse(evmZero.Contains(Address.ZeroSolana));

        AddressSet solanaZero = AddressSet.Build([Address.ZeroSolana]);
        Assert.IsTrue(solanaZero.Contains(Address.ZeroSolana));
        Assert.IsFalse(solanaZero.Contains(Address.ZeroEvm));
    }

    [TestMethod]
    public void Set_WithoutZeroAddress_DoesNotContainDefault_AndDefaultIsNotAnEmptySlot()
    {
        // default(Address) is the zero EVM address; the table must not mistake an all-zero empty slot for it.
        AddressSet set = AddressSet.Build(RandomEvm(100, 24));

        Assert.IsFalse(set.Contains(default));
        Assert.IsFalse(set.Contains(Address.ZeroSolana));

        AddressSet withZero = AddressSet.Build([.. RandomEvm(100, 24), default]);
        Assert.AreEqual(101, withZero.Count);
        Assert.IsTrue(withZero.Contains(default));
    }

    [TestMethod]
    public void Set_Duplicates_AreCountedOnce()
    {
        Address[] unique = RandomEvm(100, 7);
        Address[] doubled = [.. unique, .. unique, unique[0], unique[0]];

        AddressSet set = AddressSet.Build(doubled);

        Assert.AreEqual(100, set.Count);
        Assert.IsTrue(unique.All(a => set.Contains(a)));
    }

    [TestMethod]
    public void Set_DuplicatesAcrossFamilies_CountOncePerFamily()
    {
        Address evm = RandomEvm(1, 25)[0];
        Address solana = SolanaWithSamePayloadAs(evm);

        AddressSet set = AddressSet.Build([evm, solana, evm, solana, evm]);

        Assert.AreEqual(2, set.Count);
    }

    [TestMethod]
    public void Set_InputOrder_DoesNotAffectMembership()
    {
        Address[] members = [.. RandomEvm(300, 26), .. RandomSolana(300, 27)];
        Address[] outsiders = [.. RandomEvm(300, 28), .. RandomSolana(300, 29)];
        Address[] shuffled = (Address[])members.Clone();
        new Random(30).Shuffle(shuffled);

        AddressSet a = AddressSet.Build(members);
        AddressSet b = AddressSet.Build(shuffled);

        Assert.AreEqual(a.Count, b.Count);
        foreach (Address address in members.Concat(outsiders))
            Assert.AreEqual(a.Contains(address), b.Contains(address));
    }

    // The set copies what it needs: later changes to the input array must not leak in or out.
    [TestMethod]
    public void Set_DoesNotAliasTheInputArray()
    {
        Address[] original = RandomEvm(20, 31);
        Address[] input = (Address[])original.Clone();
        Address[] replacements = RandomEvm(20, 32);

        AddressSet set = AddressSet.Build(input);
        Array.Copy(replacements, input, input.Length);

        Assert.IsTrue(original.All(a => set.Contains(a)));
        Assert.IsFalse(replacements.Any(a => set.Contains(a)));
    }

    [TestMethod]
    public void Set_NearlyIdenticalAddresses_AreAllFound()
    {
        // Vanity / sequential addresses: only the last byte changes. A weak hash clusters these.
        var values = new Address[256];
        byte[] bytes = new byte[Address.EvmByteLength];
        for (int i = 0; i < values.Length; i++)
        {
            bytes[^1] = (byte)i;
            values[i] = Address.FromEvmBytes(bytes);
        }

        AddressSet set = AddressSet.Build(values);

        Assert.AreEqual(256, set.Count);
        Assert.IsTrue(values.All(a => set.Contains(a)));

        bytes[^1] = 0;
        bytes[0] = 1;
        Assert.IsFalse(set.Contains(Address.FromEvmBytes(bytes)));
    }

    // ----------------- load factor -----------------

    [TestMethod]
    public void Set_LoadFactor_OutOfRange_ThrowsArgumentOutOfRange_NamingTheParameter()
    {
        Address[] values = RandomEvm(10, 8);
        double[] rejected =
        [
            0.49, Math.BitDecrement(0.5), 0.91, Math.BitIncrement(0.9), 0.0, -0.0, 1.0, -1.0,
            double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, double.MinValue, double.Epsilon,
        ];

        foreach (double loadFactor in rejected)
        {
            ArgumentOutOfRangeException ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AddressSet.Build(values, loadFactor), $"load factor {loadFactor}");
            Assert.AreEqual("loadFactor", ex.ParamName, $"load factor {loadFactor}");
        }
    }

    [TestMethod]
    public void Set_LoadFactor_Bounds_AreRejected_EvenForAnEmptyInput()
    {
        foreach (double loadFactor in new[] { 2.0, double.NaN, double.PositiveInfinity, 0.49 })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AddressSet.Build(ReadOnlySpan<Address>.Empty, loadFactor), $"load factor {loadFactor}");

        Assert.AreEqual(0, AddressSet.Build(ReadOnlySpan<Address>.Empty, 0.72).Count);
    }

    [TestMethod]
    public void Set_LoadFactor_InclusiveBounds_AreAccepted_AndSetsAreCorrect()
    {
        Address[] members = RandomEvm(2_000, 33);
        Address[] outsiders = RandomEvm(2_000, 34);

        foreach (double loadFactor in new[] { 0.50, Math.BitIncrement(0.5), 0.72, Math.BitDecrement(0.9), 0.90 })
        {
            AddressSet set = AddressSet.Build(members, loadFactor);

            Assert.AreEqual(members.Length, set.Count, $"load factor {loadFactor}");
            Assert.IsTrue(members.All(a => set.Contains(a)), $"load factor {loadFactor}");
            Assert.IsFalse(outsiders.Any(a => set.Contains(a)), $"load factor {loadFactor}");
        }
    }

    // 0.90 leaves very few empty slots; building and missing lookups must still terminate.
    [TestMethod]
    public void Set_HighLoadFactor_StillTerminatesOnMisses_AtSmallAndLargeSizes()
    {
        foreach (int count in new[] { 1, 3, 7, 29, 115, 50_000 })
        {
            Address[] members = RandomEvm(count, 9 + count);
            Address[] outsiders = RandomEvm(Math.Min(count, 5_000) + 100, 10_000 + count);
            AddressSet set = AddressSet.Build(members, 0.90);

            Assert.AreEqual(count, set.Count);
            Assert.IsTrue(members.All(a => set.Contains(a)), $"{count} members");
            Assert.IsFalse(outsiders.Any(a => set.Contains(a)), $"{count} members");
        }
    }

    [TestMethod]
    public void Set_LargeSetAtLowLoadFactor_FindsEveryMember()
    {
        Address[] members = [.. RandomEvm(25_000, 35), .. RandomSolana(25_000, 36)];
        AddressSet set = AddressSet.Build(members, 0.50);

        Assert.AreEqual(50_000, set.Count);
        Assert.IsTrue(members.All(a => set.Contains(a)));
        Assert.IsFalse(RandomEvm(5_000, 37).Any(a => set.Contains(a)));
    }

    // ----------------- concurrency -----------------

    [TestMethod]
    public void Set_ConcurrentReads_AgreeWithAHashSetReference()
    {
        Address[] members = [.. RandomEvm(5_000, 38), .. RandomSolana(5_000, 39)];
        Address[] outsiders = [.. RandomEvm(5_000, 40), .. RandomSolana(5_000, 41)];
        AddressSet set = AddressSet.Build(members);
        var reference = new HashSet<Address>(members);
        int mismatches = 0;

        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, worker =>
        {
            for (int i = 0; i < 100_000; i++)
            {
                int index = (i * 7 + worker * 1_013) % members.Length;
                Address probe = (i & 1) == 0 ? members[index] : outsiders[index];
                if (set.Contains(probe) != reference.Contains(probe))
                    Interlocked.Increment(ref mismatches);
            }
        });

        Assert.AreEqual(0, mismatches);
        Assert.AreEqual(members.Length, set.Count);
    }

    // ----------------- placement statistics (internal) -----------------

    [TestMethod]
    public void GetPlacementStatistics_EmptySet_ReportsZero()
    {
        AddressSet.Build(ReadOnlySpan<Address>.Empty).GetPlacementStatistics(out int evenHomeSlots, out double averageProbeLength);

        Assert.AreEqual(0, evenHomeSlots);
        Assert.AreEqual(0.0, averageProbeLength);
    }

    [TestMethod]
    public void GetPlacementStatistics_SingleMember_FindsItInOneProbe()
    {
        AddressSet.Build([RandomEvm(1, 42)[0]]).GetPlacementStatistics(out int evenHomeSlots, out double averageProbeLength);

        Assert.AreEqual(1.0, averageProbeLength);
        Assert.IsTrue(evenHomeSlots is 0 or 1, $"even home slots {evenHomeSlots}");
    }

    // Statistical (keys are random per process): keys built so that u0 ^ rotl(u1, 13) is the same for all of them, which
    // an unkeyed hash starting with that XOR would map to one hash and one cluster. With per-process keys they spread
    // like random keys; for 1,024 keys in 2,048 slots the mean probe length is about 1.5, against a bound of 3.0.
    [TestMethod]
    public void Set_KeysThatCancelInAnXorOfRotations_DoNotCluster()
    {
        const int pairs = 10;
        var keys = new Address[1 << pairs];
        byte[] bytes = new byte[Address.SolanaByteLength];
        for (int mask = 0; mask < keys.Length; mask++)
        {
            ulong u0 = 0x0123456789ABCDEFUL, u1 = 0xFEDCBA9876543210UL;
            for (int j = 0; j < pairs; j++)
            {
                if ((mask & (1 << j)) == 0)
                    continue;

                u0 ^= 1UL << ((j + 13) % 64);
                u1 ^= 1UL << j;
            }

            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(0, 8), u0);
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8, 8), u1);
            keys[mask] = Address.FromSolanaBytes(bytes);
        }

        AddressSet set = AddressSet.Build(keys);

        Assert.AreEqual(keys.Length, set.Count);
        Assert.IsTrue(keys.All(k => set.Contains(k)));

        set.GetPlacementStatistics(out _, out double averageProbeLength);
        Assert.IsLessThan(3.0, averageProbeLength, $"structured keys must not collapse into one cluster (observed {averageProbeLength:F3})");
    }

    // Statistical (keys are random per process). Regression: the stored hash used to be forced odd with "| 1", and the
    // slot comes from the low bits, so every first probe landed on an odd slot. Even home slots are Binomial(10,000, 0.5):
    // sd 50, so the 4,000..6,000 band is 20 sd wide. At the actual load of about 0.61 the textbook mean probe length for
    // a hit is about 1.78, against a bound of 2.0.
    [TestMethod]
    public void Set_FirstProbeSlots_UseBothEvenAndOddSlots()
    {
        Address[] members = RandomEvm(10_000, 23);
        AddressSet set = AddressSet.Build(members);

        set.GetPlacementStatistics(out int evenHomeSlots, out double averageProbeLength);

        Assert.IsGreaterThan(members.Length * 4 / 10, evenHomeSlots, $"even home slots (observed {evenHomeSlots})");
        Assert.IsLessThan(members.Length * 6 / 10, evenHomeSlots, $"even home slots (observed {evenHomeSlots})");
        Assert.IsLessThan(2.0, averageProbeLength, $"average probe length at a load of about 0.61 (observed {averageProbeLength:F3})");
    }
}
