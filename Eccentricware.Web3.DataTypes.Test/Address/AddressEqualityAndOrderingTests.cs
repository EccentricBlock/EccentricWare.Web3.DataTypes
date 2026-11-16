using static EccentricWare.Web3.DataTypes.Tests.AddressTestHelpers;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Equality, ordering and the relational operators of <see cref="Address"/>.</summary>
[TestClass]
public sealed class AddressEqualityAndOrderingTests
{
    // Strictly increasing in the documented order: all EVM before all Solana, byte-wise within a family.
    private static readonly Address[] s_ascending =
    [
        Address.ZeroEvm,
        Address.FromEvmBytes(Sequence(20, i => i == 19 ? (byte)1 : (byte)0)),
        Address.FromEvmBytes(Sequence(20, i => i == 0 ? (byte)1 : (byte)0)),
        Address.FromEvmBytes(Sequence(20, _ => 0xFF)),
        Address.ZeroSolana,
        Address.FromSolanaBytes(Sequence(32, i => i == 31 ? (byte)1 : (byte)0)),
        Address.FromSolanaBytes(Sequence(32, i => i == 0 ? (byte)1 : (byte)0)),
        Address.FromSolanaBytes(Sequence(32, _ => 0xFF)),
    ];

    // ----------------- equality -----------------

    [TestMethod]
    public void Equality_And_HashCode_Agree_For_Equal_Values()
    {
        Address a = Address.FromEvmBytes(Sequence(20, i => i == 19 ? (byte)1 : (byte)0));
        Address b = Address.Parse("0x" + new string('0', 39) + "1");
        Address c = Address.Parse("0x" + new string('0', 39) + "2");

        Assert.IsTrue(a.Equals(b));
        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);

        Assert.IsFalse(a.Equals(c));
        Assert.IsFalse(a == c);
        Assert.IsTrue(a != c);

        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    // A difference in any single byte, in either family, must break equality (each limb and the EVM tail included).
    [TestMethod]
    public void Equality_DetectsADifferenceInAnySingleByte()
    {
        foreach (AddressType type in Enum.GetValues<AddressType>())
        {
            int length = type == AddressType.Evm ? 20 : 32;
            byte[] original = Sequence(length, i => (byte)(i + 1));
            Address a = type == AddressType.Evm ? Address.FromEvmBytes(original) : Address.FromSolanaBytes(original);

            for (int p = 0; p < length; p++)
            {
                byte[] changed = (byte[])original.Clone();
                changed[p] ^= 0x01;
                Address b = type == AddressType.Evm ? Address.FromEvmBytes(changed) : Address.FromSolanaBytes(changed);

                Assert.IsFalse(a.Equals(b), $"{type} byte {p}");
                Assert.IsFalse(a == b, $"{type} byte {p}");
                Assert.IsTrue(a != b, $"{type} byte {p}");
                Assert.IsFalse(a.Equals((object)b), $"{type} byte {p}");
            }
        }
    }

    [TestMethod]
    public void Equality_FamilyAloneDistinguishes_SamePayloadAddresses()
    {
        Address evm = Address.FromEvmBytes(Sequence(20, i => (byte)(i + 1)));
        Address solana = SolanaWithSamePayloadAs(evm);

        Assert.IsFalse(evm.Equals(solana));
        Assert.IsFalse(solana.Equals(evm));
        Assert.IsTrue(evm != solana);
        Assert.AreNotEqual(0, evm.CompareTo(solana));
    }

    [TestMethod]
    public void ZeroEvm_And_ZeroSolana_AreNotEqual_But_Both_IsZero()
    {
        Assert.IsTrue(Address.ZeroEvm.IsZero);
        Assert.IsTrue(Address.ZeroSolana.IsZero);

        Assert.AreNotEqual(Address.ZeroEvm, Address.ZeroSolana);
        Assert.IsTrue(Address.ZeroEvm != Address.ZeroSolana);
        Assert.AreEqual(Address.ZeroEvm, default);
    }

    [TestMethod]
    public void Equals_Object_HandlesNullBoxedAndForeignTypes()
    {
        Address a = Address.Parse(SampleEvmLower);

        Assert.IsTrue(a.Equals((object)Address.Parse(SampleEvmLower)));
        Assert.IsFalse(a.Equals((object?)null));
        Assert.IsFalse(a.Equals((object)a.ToString()));
        Assert.IsFalse(a.Equals((object)a.ToBytes()));
    }

    // ----------------- ordering -----------------

    [TestMethod]
    public void CompareTo_Orders_By_Type_Then_Lexicographic()
    {
        Address evm = Address.Parse("0x" + new string('0', 39) + "1");
        Address sol = Address.ZeroSolana;

        Assert.IsTrue(evm < sol);
        Assert.IsTrue(sol > evm);
        Assert.IsLessThan(0, evm.CompareTo(sol));

        // Within EVM: lexicographic big-endian compare.
        Address a = Address.Parse("0x" + new string('0', 39) + "1");
        Address b = Address.Parse("0x" + new string('0', 39) + "2");
        Assert.IsTrue(a < b);
        Assert.IsLessThan(0, a.CompareTo(b));

        // Within Solana: compare backing 32 bytes.
        Address s1 = Address.FromSolanaBytes(Sequence(32, i => i == 31 ? (byte)1 : (byte)0));
        Address s2 = Address.FromSolanaBytes(Sequence(32, i => i == 31 ? (byte)2 : (byte)0));
        Assert.IsTrue(s1 < s2);
    }

    // The family decides before the payload does: the largest EVM value is still below the smallest Solana value.
    [TestMethod]
    public void CompareTo_FamilyDominatesPayload()
    {
        Address largestEvm = Address.FromEvmBytes(Sequence(20, _ => 0xFF));

        Assert.IsTrue(largestEvm < Address.ZeroSolana);
        Assert.IsTrue(largestEvm <= Address.ZeroSolana);
        Assert.IsTrue(Address.ZeroSolana >= largestEvm);
        Assert.IsTrue(Address.ZeroSolana > largestEvm);
        Assert.AreEqual(-1, largestEvm.CompareTo(Address.ZeroSolana));
        Assert.AreEqual(1, Address.ZeroSolana.CompareTo(largestEvm));
    }

    // The most significant differing byte decides, whatever the bytes after it hold: at every position and in each limb.
    [TestMethod]
    public void CompareTo_MostSignificantDifferingByteDecides_AtEveryPosition()
    {
        foreach (AddressType type in Enum.GetValues<AddressType>())
        {
            int length = type == AddressType.Evm ? 20 : 32;
            for (int p = 0; p < length; p++)
            {
                byte[] smaller = new byte[length];
                byte[] larger = new byte[length];
                smaller[p] = 1;
                Array.Fill(smaller, (byte)0xFF, p + 1, length - p - 1); // later bytes favour `smaller`
                larger[p] = 2;

                Address lo = type == AddressType.Evm ? Address.FromEvmBytes(smaller) : Address.FromSolanaBytes(smaller);
                Address hi = type == AddressType.Evm ? Address.FromEvmBytes(larger) : Address.FromSolanaBytes(larger);

                Assert.AreEqual(-1, lo.CompareTo(hi), $"{type} byte {p}");
                Assert.AreEqual(1, hi.CompareTo(lo), $"{type} byte {p}");
            }
        }
    }

    [TestMethod]
    public void RelationalOperators_AgreeWithTheOrderOfTheAscendingList_ForEveryPair()
    {
        for (int i = 0; i < s_ascending.Length; i++)
        {
            for (int j = 0; j < s_ascending.Length; j++)
            {
                Address x = s_ascending[i], y = s_ascending[j];
                string pair = $"[{i}] vs [{j}]";

                Assert.AreEqual(i < j, x < y, pair);
                Assert.AreEqual(i > j, x > y, pair);
                Assert.AreEqual(i <= j, x <= y, pair);
                Assert.AreEqual(i >= j, x >= y, pair);
                Assert.AreEqual(i == j, x == y, pair);
                Assert.AreEqual(i != j, x != y, pair);
                Assert.AreEqual(Math.Sign(i.CompareTo(j)), Math.Sign(x.CompareTo(y)), pair);
            }
        }
    }

    [TestMethod]
    public void RelationalOperators_EqualOperands_AreLessOrEqualAndGreaterOrEqual_ButNotStrict()
    {
        foreach (Address a in new[] { Address.ZeroEvm, Address.ZeroSolana, RandomEvm(1, 61)[0], RandomSolana(1, 62)[0] })
        {
            Address copy = Address.Parse(a.ToString());

            Assert.IsTrue(a <= copy, a.ToString());
            Assert.IsTrue(a >= copy, a.ToString());
            Assert.IsFalse(a < copy, a.ToString());
            Assert.IsFalse(a > copy, a.ToString());
            Assert.AreEqual(0, a.CompareTo(copy));
        }
    }

    // ----------------- IComparable (object) -----------------

    [TestMethod]
    public void CompareTo_Object_BoxedAddress_MatchesTheTypedComparison()
    {
        foreach (Address x in s_ascending)
        {
            foreach (Address y in s_ascending)
                Assert.AreEqual(Math.Sign(x.CompareTo(y)), Math.Sign(((IComparable)x).CompareTo((object)y)));
        }
    }

    [TestMethod]
    public void CompareTo_Object_Null_Returns_One()
    {
        Assert.AreEqual(1, Address.ZeroEvm.CompareTo((object?)null));
    }

    [TestMethod]
    public void CompareTo_Object_WrongType_Throws_NamingTheParameter()
    {
        ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(() => Address.ZeroEvm.CompareTo(new object()));

        Assert.AreEqual("obj", ex.ParamName);
        StringAssert.Contains(ex.Message, "Address");
        Assert.ThrowsExactly<ArgumentException>(() => Address.ZeroEvm.CompareTo((object)"0x00"));
    }

    // ----------------- random contract -----------------

    [TestMethod]
    public void CompareTo_AgreesWithTypeThenByteOrder_AndIsAntisymmetric_ForRandomValues()
    {
        var random = new Random(77);
        var values = new List<Address>();
        byte[] evm = new byte[Address.EvmByteLength];
        byte[] solana = new byte[Address.SolanaByteLength];
        for (int i = 0; i < 60; i++)
        {
            random.NextBytes(evm);
            random.NextBytes(solana);
            values.Add(Address.FromEvmBytes(evm));
            values.Add(Address.FromSolanaBytes(solana));

            // Neighbours that differ in exactly one byte exercise every limb boundary.
            evm[random.Next(evm.Length)] ^= 0x01;
            solana[random.Next(solana.Length)] ^= 0x01;
            values.Add(Address.FromEvmBytes(evm));
            values.Add(Address.FromSolanaBytes(solana));
        }

        static int Reference(Address x, Address y)
            => x.Type != y.Type ? x.Type.CompareTo(y.Type) : x.ToBytes().AsSpan().SequenceCompareTo(y.ToBytes());

        foreach (Address x in values)
        {
            foreach (Address y in values)
            {
                int actual = Math.Sign(x.CompareTo(y));

                Assert.AreEqual(Math.Sign(Reference(x, y)), actual);
                Assert.AreEqual(-actual, Math.Sign(y.CompareTo(x)));
                Assert.AreEqual(actual == 0, x.Equals(y));
                Assert.AreEqual(actual < 0, x < y);
                Assert.AreEqual(actual <= 0, x <= y);
                Assert.AreEqual(actual > 0, x > y);
                Assert.AreEqual(actual >= 0, x >= y);
                if (x.Equals(y))
                    Assert.AreEqual(x.GetHashCode(), y.GetHashCode());
            }
        }
    }
}
