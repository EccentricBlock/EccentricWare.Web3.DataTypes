using System.Reflection;
using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// The address family (EVM or Solana) is mixed into the hash after the keyed mix. Folding it into the last limb with an XOR
/// cancelled for the Solana key whose last limb is 1, which then hashed exactly like the EVM address with the same first three
/// limbs.
/// </summary>
[TestClass]
public sealed class AddressFamilyHashTests
{
    private static (Address Evm, Address SolanaWithLastLimbOne, Address SolanaWithLastLimbZero) Family(Random random)
    {
        byte[] evm = new byte[20];
        random.NextBytes(evm);

        byte[] solanaOne = new byte[32];
        evm.CopyTo(solanaOne, 0); // limbs u0, u1 and the high half of u2 are the EVM payload
        solanaOne[31] = 0x01; // last limb == 1

        byte[] solanaZero = new byte[32];
        evm.CopyTo(solanaZero, 0);

        return (Address.FromEvmBytes(evm), Address.FromSolanaBytes(solanaOne), Address.FromSolanaBytes(solanaZero));
    }

    [TestMethod]
    public void EvmAddress_AndTheSolanaKeyThatUsedToCancelTheFamily_HashDifferently()
    {
        // Equal hash codes by chance would need a 2^-32 coincidence per pair; 32 pairs make a false failure about 1e-8.
        var random = new Random(4242);
        for (int i = 0; i < 32; i++)
        {
            var (evm, solanaOne, _) = Family(random);

            Assert.AreNotEqual(evm, solanaOne); // equality always separated them
            Assert.AreNotEqual(evm.GetHashCode(), solanaOne.GetHashCode(), $"pair {i}");
        }
    }

    [TestMethod]
    public void SamePayloadInBothFamilies_HashesDifferently_ForEveryLastLimb()
    {
        var random = new Random(4243);
        for (int i = 0; i < 32; i++)
        {
            var (evm, _, solanaZero) = Family(random);
            Assert.AreNotEqual(evm.GetHashCode(), solanaZero.GetHashCode(), $"pair {i}");
        }
    }

    private static readonly MethodInfo? AddressSetHash =
        typeof(AddressSet).GetMethod("Hash", BindingFlags.NonPublic | BindingFlags.Static, [typeof(Address).MakeByRefType()]);

    private static ulong SetHash(Address address) => (ulong)AddressSetHash!.Invoke(null, [address])!;

    [TestMethod]
    public void AddressSetHash_SeparatesTheFamilies_IncludingTheKeyThatUsedToCancel()
    {
        // The set's own hash is private and a collision there is invisible through Contains (equality still decides), so it is
        // read by reflection; the guard below fails loudly if the method is renamed.
        Assert.IsNotNull(AddressSetHash, "AddressSet.Hash(in Address) was not found: update this test with the new name.");

        var random = new Random(4246);
        for (int i = 0; i < 32; i++)
        {
            var (evm, solanaOne, solanaZero) = Family(random);

            Assert.AreNotEqual(SetHash(evm), SetHash(solanaOne), $"cancelling pair {i}");
            Assert.AreNotEqual(SetHash(evm), SetHash(solanaZero), $"same-payload pair {i}");
            Assert.AreEqual(SetHash(evm), SetHash(evm), "the hash is deterministic within the process");
        }
    }

    [TestMethod]
    public void SolanaHash_IsTheSameValue_ForEqualAddresses_AndStableAcrossCalls()
    {
        byte[] key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        Address a = Address.FromSolanaBytes(key), b = Address.FromSolanaBytes(key);

        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.AreEqual(a.GetHashCode(), a.GetHashCode());
    }

    [TestMethod]
    public void AddressSet_SeparatesTheTwoFamilies_WhateverTheCancellingKeyLooksLike()
    {
        var random = new Random(4244);
        for (int i = 0; i < 16; i++)
        {
            var (evm, solanaOne, solanaZero) = Family(random);

            AddressSet evmOnly = AddressSet.Build([evm]);
            Assert.IsTrue(evmOnly.Contains(in evm));
            Assert.IsFalse(evmOnly.Contains(in solanaOne), "EVM set must not contain the colliding-looking Solana key");
            Assert.IsFalse(evmOnly.Contains(in solanaZero));

            AddressSet both = AddressSet.Build([evm, solanaOne, solanaZero]);
            Assert.IsTrue(both.Contains(in evm));
            Assert.IsTrue(both.Contains(in solanaOne));
            Assert.IsTrue(both.Contains(in solanaZero));
        }
    }

    [TestMethod]
    public void Dictionary_KeepsTheThreeAddressesApart()
    {
        var (evm, solanaOne, solanaZero) = Family(new Random(4245));
        var map = new Dictionary<Address, string> { [evm] = "evm", [solanaOne] = "solana-1", [solanaZero] = "solana-0" };

        Assert.HasCount(3, map);
        Assert.AreEqual("evm", map[evm]);
        Assert.AreEqual("solana-1", map[solanaOne]);
        Assert.AreEqual("solana-0", map[solanaZero]);
    }
}
