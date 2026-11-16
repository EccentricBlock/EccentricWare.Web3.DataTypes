using static EccentricWare.Web3.DataTypes.Tests.AddressTestHelpers;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Byte factories, <see cref="Address.WriteBytes"/>, <see cref="Address.ToBytes"/>, <see cref="Address.IsZero"/> and the limb view.</summary>
[TestClass]
public sealed class AddressConstructionAndBytesTests
{
    private static readonly byte[] s_ascending20 = Sequence(20, i => (byte)i);
    private static readonly byte[] s_ascending32 = Sequence(32, i => (byte)i);

    // ----------------- factories -----------------

    [TestMethod]
    public void FromEvmBytes_RoundTrips_ToBytes_And_ToString_Default()
    {
        byte[] evm20 = Convert.FromHexString("52908400098527886e0f7030069857d2e4169ee7");

        Address a = Address.FromEvmBytes(evm20);

        Assert.AreEqual(AddressType.Evm, a.Type);
        Assert.AreEqual(20, a.ByteLength);
        Assert.IsFalse(a.IsZero);
        CollectionAssert.AreEqual(evm20, a.ToBytes());
        Assert.AreEqual("0x52908400098527886e0f7030069857d2e4169ee7", a.ToString());
    }

    [TestMethod]
    public void FromSolanaBytes_RoundTrips_ToBytes_And_Parse_Of_ToString()
    {
        Address a = Address.FromSolanaBytes(s_ascending32);

        Assert.AreEqual(AddressType.Solana, a.Type);
        Assert.AreEqual(32, a.ByteLength);
        Assert.IsFalse(a.IsZero);
        CollectionAssert.AreEqual(s_ascending32, a.ToBytes());

        string s = a.ToString();
        Assert.AreEqual(ReferenceEncode(s_ascending32), s);

        Address parsed = Address.Parse(s);
        Assert.AreEqual(AddressType.Solana, parsed.Type);
        Assert.AreEqual(a, parsed);
    }

    // Lengths 0 and 1, the neighbours of each size, and the cross-family confusion (32 bytes into the EVM factory, 20 into Solana's).
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(19)]
    [DataRow(21)]
    [DataRow(32)]
    public void FromEvmBytes_WrongLength_ThrowsArgumentException_NamingTheParameter(int length)
    {
        ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(() => Address.FromEvmBytes(new byte[length]));

        Assert.AreEqual("evmAddress20", ex.ParamName);
        StringAssert.Contains(ex.Message, "exactly 20 bytes");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(20)]
    [DataRow(31)]
    [DataRow(33)]
    public void FromSolanaBytes_WrongLength_ThrowsArgumentException_NamingTheParameter(int length)
    {
        ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(() => Address.FromSolanaBytes(new byte[length]));

        Assert.AreEqual("solanaPubkey32", ex.ParamName);
        StringAssert.Contains(ex.Message, "exactly 32 bytes");
    }

    [TestMethod]
    public void Factories_CopyTheInput_SoLaterMutationDoesNotChangeTheAddress()
    {
        byte[] evm = (byte[])s_ascending20.Clone();
        byte[] solana = (byte[])s_ascending32.Clone();
        Address a = Address.FromEvmBytes(evm);
        Address b = Address.FromSolanaBytes(solana);

        Array.Fill(evm, (byte)0xFF);
        Array.Fill(solana, (byte)0xFF);

        CollectionAssert.AreEqual(s_ascending20, a.ToBytes());
        CollectionAssert.AreEqual(s_ascending32, b.ToBytes());
    }

    // ----------------- WriteBytes / ToBytes -----------------

    [TestMethod]
    public void WriteBytes_Evm_Writes_Only_First_20_Bytes()
    {
        Address a = Address.FromEvmBytes(s_ascending20);

        byte[] dest = Filled(64, (byte)0xCC);
        a.WriteBytes(dest);

        CollectionAssert.AreEqual(s_ascending20, dest.Take(20).ToArray());
        Assert.IsTrue(dest.Skip(20).All(b => b == 0xCC));
    }

    [TestMethod]
    public void WriteBytes_Solana_Writes_Only_First_32_Bytes()
    {
        byte[] sol32 = Sequence(32, i => (byte)(255 - i));
        Address a = Address.FromSolanaBytes(sol32);

        byte[] dest = Filled(64, (byte)0xCC);
        a.WriteBytes(dest);

        CollectionAssert.AreEqual(sol32, dest.Take(32).ToArray());
        Assert.IsTrue(dest.Skip(32).All(b => b == 0xCC));
    }

    [TestMethod]
    public void WriteBytes_ExactLength_Buffers_Succeed_And_AreCorrect()
    {
        byte[] evmDest = new byte[20];
        Address.FromEvmBytes(s_ascending20).WriteBytes(evmDest);
        CollectionAssert.AreEqual(s_ascending20, evmDest);

        byte[] sol32 = Sequence(32, i => (byte)(255 - i));
        byte[] solDest = new byte[32];
        Address.FromSolanaBytes(sol32).WriteBytes(solDest);
        CollectionAssert.AreEqual(sol32, solDest);
    }

    [TestMethod]
    public void WriteBytes_DestinationTooSmall_Throws_NamingTheParameter_AndWritesNothing()
    {
        Address evm = Address.FromEvmBytes(s_ascending20);
        Address sol = Address.FromSolanaBytes(s_ascending32);

        foreach ((Address address, int required) in new[] { (evm, 20), (sol, 32) })
        {
            foreach (int length in new[] { 0, 1, required - 1 })
            {
                byte[] dest = Filled(length, (byte)0xCC);

                ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(() => address.WriteBytes(dest));

                Assert.AreEqual("destination", ex.ParamName, $"{address.Type}, length {length}");
                Assert.IsTrue(dest.All(b => b == 0xCC), $"{address.Type}, length {length}: partial write");
            }
        }
    }

    [TestMethod]
    public void ToBytes_Length_Matches_Type()
    {
        Assert.HasCount(20, Address.FromEvmBytes(s_ascending20).ToBytes());
        Assert.HasCount(32, Address.FromSolanaBytes(s_ascending32).ToBytes());
        Assert.HasCount(20, default(Address).ToBytes());
        Assert.HasCount(32, Address.ZeroSolana.ToBytes());
    }

    // Every byte position, so each 64-bit limb boundary (7/8, 15/16, 19/20, 23/24) is crossed in both directions.
    [TestMethod]
    public void EveryBytePosition_RoundTrips_AndLandsInTheExpectedLimb()
    {
        foreach (AddressType type in Enum.GetValues<AddressType>())
        {
            int length = type == AddressType.Evm ? 20 : 32;
            for (int p = 0; p < length; p++)
            {
                byte[] bytes = new byte[length];
                bytes[p] = 0xA5;
                Address a = type == AddressType.Evm ? Address.FromEvmBytes(bytes) : Address.FromSolanaBytes(bytes);

                CollectionAssert.AreEqual(bytes, a.ToBytes(), $"{type} byte {p}");

                // Limb p/8 holds the byte at bit offset 8 * (7 - p % 8); the other three limbs stay zero.
                ulong[] expected = new ulong[4];
                expected[p / 8] = 0xA5UL << (8 * (7 - p % 8));
                a.GetLimbs(out ulong u0, out ulong u1, out ulong u2, out ulong u3);
                CollectionAssert.AreEqual(expected, new[] { u0, u1, u2, u3 }, $"{type} byte {p}");

                string text = a.ToString();
                if (type == AddressType.Evm)
                    Assert.AreEqual("0x" + Convert.ToHexStringLower(bytes), text, $"byte {p}");
                else
                    Assert.AreEqual(ReferenceEncode(bytes), text, $"byte {p}");

                Assert.AreEqual(a, Address.Parse(text), $"{type} byte {p}");
            }
        }
    }

    // ----------------- IsZero -----------------

    [TestMethod]
    public void IsZero_IsFalse_WhenAnySingleByteIsSet()
    {
        // A getter that skipped a limb (for example _u3, bytes 24..31) would miss the bytes that live there.
        for (int p = 0; p < 20; p++)
        {
            byte[] bytes = new byte[20];
            bytes[p] = 1;
            Assert.IsFalse(Address.FromEvmBytes(bytes).IsZero, $"EVM byte {p}");
        }

        for (int p = 0; p < 32; p++)
        {
            byte[] bytes = new byte[32];
            bytes[p] = 1;
            Assert.IsFalse(Address.FromSolanaBytes(bytes).IsZero, $"Solana byte {p}");
        }
    }

    [TestMethod]
    public void IsZero_IsTrue_ForEveryRouteToAnAllZeroPayload_WhateverTheFamily()
    {
        Address[] zeros =
        [
            default,
            Address.ZeroEvm,
            Address.ZeroSolana,
            Address.FromEvmBytes(new byte[20]),
            Address.FromSolanaBytes(new byte[32]),
            Address.Parse("0x" + new string('0', 40)),
            Address.Parse(new string('0', 40)),
            Address.Parse(new string('1', 32)),
        ];

        foreach (Address zero in zeros)
            Assert.IsTrue(zero.IsZero, $"{zero.Type} {zero}");
    }

    // ----------------- GetLimbs (internal) -----------------

    [TestMethod]
    public void GetLimbs_Solana_ReturnsBigEndianWordsOfTheBytes()
    {
        Address.FromSolanaBytes(s_ascending32).GetLimbs(out ulong u0, out ulong u1, out ulong u2, out ulong u3);

        Assert.AreEqual(0x0001020304050607UL, u0);
        Assert.AreEqual(0x08090A0B0C0D0E0FUL, u1);
        Assert.AreEqual(0x1011121314151617UL, u2);
        Assert.AreEqual(0x18191A1B1C1D1E1FUL, u3);
    }

    [TestMethod]
    public void GetLimbs_Evm_PacksTheTwentyBytesIntoTheTopOfTheThirdLimb()
    {
        Address.FromEvmBytes(s_ascending20).GetLimbs(out ulong u0, out ulong u1, out ulong u2, out ulong u3);

        Assert.AreEqual(0x0001020304050607UL, u0);
        Assert.AreEqual(0x08090A0B0C0D0E0FUL, u1);
        Assert.AreEqual(0x1011121300000000UL, u2);
        Assert.AreEqual(0UL, u3);
    }

    [TestMethod]
    public void GetLimbs_ZeroValues_AreAllZero_AndDoNotExposeTheFamily()
    {
        foreach (Address zero in new[] { default, Address.ZeroEvm, Address.ZeroSolana })
        {
            zero.GetLimbs(out ulong u0, out ulong u1, out ulong u2, out ulong u3);
            Assert.AreEqual(0UL, u0 | u1 | u2 | u3, zero.Type.ToString());
        }
    }
}
