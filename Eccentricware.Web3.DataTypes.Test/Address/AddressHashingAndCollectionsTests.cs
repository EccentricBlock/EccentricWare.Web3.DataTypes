using System.Globalization;
using static EccentricWare.Web3.DataTypes.Tests.AddressTestHelpers;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// <see cref="Address.GetHashCode"/> and use as a key or sort subject. Hash codes are keyed per process, so no test
/// asserts a specific value; the statistical checks use margins of many standard deviations.
/// </summary>
[TestClass]
public sealed class AddressHashingAndCollectionsTests
{
    // ----------------- hash contract -----------------

    [TestMethod]
    public void GetHashCode_IsIdenticalAcrossConstructionRoutes()
    {
        byte[] sol32 = Sequence(32, i => (byte)(i * 7));
        Address fromBytes = Address.FromSolanaBytes(sol32);
        Address fromText = Address.Parse(fromBytes.ToString());
        Address fromUtf8 = Address.Parse(Quoted(fromBytes.ToString()));

        Assert.AreEqual(fromBytes.GetHashCode(), fromText.GetHashCode());
        Assert.AreEqual(fromBytes.GetHashCode(), fromUtf8.GetHashCode());

        Address evmBytes = Address.FromEvmBytes(sol32.AsSpan(0, Address.EvmByteLength));
        Assert.AreEqual(evmBytes.GetHashCode(), Address.Parse(evmBytes.ToString("X", CultureInfo.InvariantCulture)).GetHashCode());
        Assert.AreEqual(Address.ZeroEvm.GetHashCode(), default(Address).GetHashCode());
        Assert.AreEqual(Address.ZeroSolana.GetHashCode(), Address.FromSolanaBytes(new byte[32]).GetHashCode());
    }

    // Distinctness checks are statistical: for N random 32-bit codes the expected number of collisions is about N^2 / 2^33.
    [TestMethod]
    public void GetHashCode_SpreadsRandomAddresses_AcrossTheHashSpace()
    {
        Address[] values = [.. RandomEvm(50_000, 701), .. RandomSolana(50_000, 702)];

        int distinct = values.Select(v => v.GetHashCode()).Distinct().Count();

        // Expected collisions about 1.2; the bound of 20 is exceeded with probability below 1e-15.
        Assert.IsGreaterThan(values.Length - 20, distinct, $"{values.Length - distinct} collisions among {values.Length} addresses");
    }

    // The family is folded into the hash (Address.GetHashCode remarks): same payload, different family -> different code.
    [TestMethod]
    public void GetHashCode_DependsOnTheFamily_ForSamePayloadAddresses()
    {
        Address[] evm = RandomEvm(1_000, 703);

        int equalCodes = evm.Count(a => a.GetHashCode() == SolanaWithSamePayloadAs(a).GetHashCode());

        Assert.IsLessThanOrEqualTo(1, equalCodes, $"{equalCodes} same-payload pairs share a hash code");
    }

    [TestMethod]
    public void GetHashCode_ChangesWhenAnySingleByteChanges()
    {
        foreach (AddressType type in Enum.GetValues<AddressType>())
        {
            int length = type == AddressType.Evm ? 20 : 32;
            byte[] original = Sequence(length, i => (byte)(i * 11 + 3));
            Address a = type == AddressType.Evm ? Address.FromEvmBytes(original) : Address.FromSolanaBytes(original);

            int unchanged = 0;
            for (int p = 0; p < length; p++)
            {
                byte[] changed = (byte[])original.Clone();
                changed[p] ^= 0x80;
                Address b = type == AddressType.Evm ? Address.FromEvmBytes(changed) : Address.FromSolanaBytes(changed);
                if (a.GetHashCode() == b.GetHashCode())
                    unchanged++;
            }

            // A byte the hash ignores would show up at least once; chance collisions are about 2^-32 per position.
            Assert.AreEqual(0, unchanged, $"{type}: hash ignored {unchanged} byte positions");
        }
    }

    // ----------------- as a collection key -----------------

    [TestMethod]
    public void HashSetAndDictionary_FindEveryKey_AndKeepTheFamiliesApart()
    {
        Address[] keys = [.. RandomEvm(25_000, 711), .. RandomSolana(25_000, 712), Address.ZeroEvm, Address.ZeroSolana];
        var set = new HashSet<Address>(keys);
        var dictionary = new Dictionary<Address, int>();
        for (int i = 0; i < keys.Length; i++)
            dictionary[keys[i]] = i;

        Assert.HasCount(keys.Length, set);
        Assert.HasCount(keys.Length, dictionary);
        for (int i = 0; i < keys.Length; i++)
        {
            Assert.IsTrue(set.Contains(keys[i]), $"key {i}");
            Assert.AreEqual(i, dictionary[keys[i]], $"key {i}");
        }

        Assert.IsFalse(set.Contains(RandomEvm(1, 713)[0]));
        Assert.IsTrue(EqualityComparer<Address>.Default.Equals(keys[0], Address.Parse(keys[0].ToString())));
    }

    [TestMethod]
    public void BoxedAddress_WorksAsAnObjectDictionaryKey()
    {
        Address a = Address.Parse(SampleEvmLower);
        var dictionary = new Dictionary<object, string> { [a] = "found" };

        Assert.AreEqual("found", dictionary[Address.Parse(SampleEvmLower)]);
        Assert.IsFalse(dictionary.ContainsKey(SolanaWithSamePayloadAs(a)));
    }

    // EVM 0x5290...9ee7 and the Solana address with the same 20 bytes, eleven zero bytes, then 0x01: the family bit and
    // the last payload bit cancel in the hash input (Address.cs GetHashCode), so these two collide by construction.
    // Equality must still tell them apart, so no hash-keyed collection may merge them. Vector from an independent
    // Base58 reference.
    [TestMethod]
    public void CrossFamilyPairWithIdenticalHashInput_StaysDistinctInCollections()
    {
        Address evm = Address.Parse(SampleEvmLower);
        Address solana = Address.Parse("6ZJF1dsTjHHs8DQU9Zb6JWUj1RPsrebYRQ3xcQZtQtHn");

        CollectionAssert.AreEqual(Convert.FromHexString("52908400098527886e0f7030069857d2e4169ee7000000000000000000000001"), solana.ToBytes());
        Assert.AreEqual(AddressType.Solana, solana.Type);

        Assert.IsFalse(evm.Equals(solana));
        var set = new HashSet<Address> { evm, solana };
        Assert.HasCount(2, set);
        Assert.IsTrue(set.Contains(evm));
        Assert.IsTrue(set.Contains(solana));
    }

    // ----------------- sorting -----------------

    [TestMethod]
    public void Sorting_ArrayListAndSortedSet_PutEvmFirstThenSolana_ByteWise()
    {
        Address[] unsorted = [.. RandomSolana(200, 721), .. RandomEvm(200, 722), Address.ZeroSolana, Address.ZeroEvm];
        new Random(723).Shuffle(unsorted);

        // Reference order: family, then the payload bytes as lowercase hex (equal-length hex sorts like the bytes).
        Address[] expected = [.. unsorted.OrderBy(a => (byte)a.Type).ThenBy(a => Convert.ToHexStringLower(a.ToBytes()), StringComparer.Ordinal)];

        Address[] array = (Address[])unsorted.Clone();
        Array.Sort(array);
        CollectionAssert.AreEqual(expected, array);

        var list = new List<Address>(unsorted);
        list.Sort();
        CollectionAssert.AreEqual(expected, list);

        CollectionAssert.AreEqual(expected, new SortedSet<Address>(unsorted).ToArray());

        Assert.IsTrue(array.Take(201).All(a => a.Type == AddressType.Evm));
        Assert.IsTrue(array.Skip(201).All(a => a.Type == AddressType.Solana));
    }

    [TestMethod]
    public void Sorting_ThroughTheNonGenericIComparable_Works()
    {
        object[] boxed = [Address.ZeroSolana, Address.ZeroEvm, Address.FromEvmBytes(Sequence(20, _ => 0xFF))];

        Array.Sort(boxed);

        CollectionAssert.AreEqual(new object[] { Address.ZeroEvm, Address.FromEvmBytes(Sequence(20, _ => 0xFF)), Address.ZeroSolana }, boxed);
    }
}
