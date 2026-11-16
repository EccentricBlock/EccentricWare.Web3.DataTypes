using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Public constants, the <see cref="AddressType"/> storage contract, layout and the zero values of <see cref="Address"/>.</summary>
[TestClass]
public sealed class AddressConstantsAndTypeTests
{
    // The numeric values are persisted by the EF value converter and drive CompareTo, so they are a contract.
    [TestMethod]
    public void AddressType_Values_AreAStableStorageContract()
    {
        // Read through the enum's runtime values so the pins compare real data, not compile-time constants.
        AddressType[] values = Enum.GetValues<AddressType>();
        CollectionAssert.AreEquivalent(new[] { AddressType.Evm, AddressType.Solana }, values);
        CollectionAssert.AreEqual(new byte[] { 0, 1 }, values.OrderBy(v => v).Select(v => (byte)v).ToArray());
        Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(AddressType)));
        Assert.AreEqual("Evm", Enum.GetName(typeof(AddressType), (byte)0));
        Assert.AreEqual("Solana", Enum.GetName(typeof(AddressType), (byte)1));
    }

    [TestMethod]
    public void Constants_ArePinnedToTheirLiteralValues()
    {
        int[] actual = [Address.EvmByteLength, Address.SolanaByteLength, Address.EvmHexLength, Address.MaxBase58Length];
        CollectionAssert.AreEqual(new[] { 20, 32, 40, 44 }, actual);

        Assert.AreEqual(2 * actual[0], actual[2]);
        // Longest Base58 text for 256 bits: ceil(256 / log2(58)).
        Assert.AreEqual((int)Math.Ceiling(actual[1] * 8 / Math.Log2(58)), actual[3]);
    }

    [TestMethod]
    public void Layout_Size_Is_40_Bytes()
    {
        // 32 payload bytes + 1 type byte, padded to the 8-byte alignment of the limbs (Pack = 8).
        Assert.AreEqual(40, Unsafe.SizeOf<Address>());
        Assert.AreEqual(40, Marshal.SizeOf<Address>());
    }

    [TestMethod]
    public void Default_Is_ZeroEvm()
    {
        Address d = default;

        Assert.AreEqual(AddressType.Evm, d.Type);
        Assert.AreEqual(20, d.ByteLength);
        Assert.IsTrue(d.IsZero);

        Assert.AreEqual(Address.ZeroEvm, d);
        Assert.AreEqual("0x0000000000000000000000000000000000000000", d.ToString());
    }

    [TestMethod]
    public void ZeroSolana_ToString_Is_32_Ones_And_RoundTrips()
    {
        Address a = Address.ZeroSolana;

        Assert.AreEqual(AddressType.Solana, a.Type);
        Assert.AreEqual(32, a.ByteLength);
        Assert.IsTrue(a.IsZero);

        string s = a.ToString();
        Assert.AreEqual(new string('1', 32), s);

        Address parsed = Address.Parse(s);
        Assert.AreEqual(AddressType.Solana, parsed.Type);
        Assert.AreEqual(a, parsed);
    }

    [TestMethod]
    public void ByteLength_FollowsTheFamily()
    {
        Assert.AreEqual(Address.EvmByteLength, Address.FromEvmBytes(new byte[20]).ByteLength);
        Assert.AreEqual(Address.SolanaByteLength, Address.FromSolanaBytes(new byte[32]).ByteLength);
        Assert.AreEqual(Address.EvmByteLength, Address.ZeroEvm.ByteLength);
        Assert.AreEqual(Address.SolanaByteLength, Address.ZeroSolana.ByteLength);
    }
}
