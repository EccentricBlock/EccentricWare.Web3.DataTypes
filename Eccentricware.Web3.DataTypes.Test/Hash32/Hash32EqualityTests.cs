using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class Hash32EqualityTests
{
    [TestMethod]
    public void Equality_Operators_AndEquals_AgreeForEqualValues()
    {
        Hash32 a = Hash32TestData.Value0To31();
        Hash32 b = Hash32TestData.Value0To31();

        Assert.IsTrue(a.Equals(b));
        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void Equality_DiffersInSingleLimb_IsNotEqual(int limb)
    {
        foreach (ulong filler in new ulong[] { 0, 0x0123456789ABCDEFUL })
        {
            Hash32 a = Hash32TestData.WithLimb(limb, 1, filler);
            Hash32 b = Hash32TestData.WithLimb(limb, 2, filler);

            Assert.IsFalse(a.Equals(b), $"limb={limb}");
            Assert.IsFalse(b.Equals(a));
            Assert.IsFalse(a == b);
            Assert.IsTrue(a != b);
            Assert.IsFalse(a.Equals((object)b));
        }
    }

    [TestMethod]
    public void Equality_DiffersInSingleBit_IsNotEqual()
    {
        byte[] bytes = Hash32TestData.Bytes0To31();
        for (int bit = 0; bit < 256; bit++)
        {
            byte[] flipped = (byte[])bytes.Clone();
            flipped[bit / 8] ^= (byte)(1 << (bit % 8));

            Assert.AreNotEqual(new Hash32(bytes), new Hash32(flipped), $"bit={bit}");
        }
    }

    [TestMethod]
    public void Equals_Object_FollowsTheContract()
    {
        Hash32 value = Hash32TestData.Value0To31();

        Assert.IsTrue(value.Equals((object)new Hash32(Hash32TestData.Bytes0To31())));
        Assert.IsFalse(value.Equals((object?)null));
        Assert.IsFalse(value.Equals("0x" + Hash32TestData.Hex(Hash32TestData.Bytes0To31())));
        Assert.IsFalse(value.Equals((object)Hash32.Zero));
        // A boxed uint256 holding the same number is a different type, so it is not equal.
        Assert.IsFalse(value.Equals((object)value.ToUInt256()));
        Assert.IsFalse(value.Equals((object)new BigInteger(5)));
    }

    [TestMethod]
    public void Default_AndZero_AreEqual_AndHashEqually()
    {
        Hash32 zero = Hash32.Zero;
        Hash32 defaultValue = default;

        Assert.IsTrue(zero.Equals(defaultValue));
        Assert.AreEqual(zero.GetHashCode(), defaultValue.GetHashCode());
    }

    [TestMethod]
    public void SameValueFromEveryRoute_IsEqual_AndHashesAndFormatsIdentically()
    {
        byte[] bytes = Hash32TestData.Bytes0To31();
        string hex = Hash32TestData.Hex(bytes);
        Hash32 reference = new(0x0001020304050607UL, 0x08090A0B0C0D0E0FUL, 0x1011121314151617UL, 0x18191A1B1C1D1E1FUL);

        Hash32[] routes =
        [
            new Hash32(bytes),
            Hash32.FromLittleEndian(Hash32TestData.Reversed(bytes)),
            Hash32.Parse(hex),
            Hash32.Parse(Hash32TestData.Utf8("0x" + hex)),
            Hash32.ParseBase58(Hash32TestData.Utf8(Hash32TestData.Base58Encode(bytes))),
            Hash32.ParseBase64(Hash32TestData.Utf8(Convert.ToBase64String(bytes))),
            Hash32.FromUInt256(new uint256(0x18191A1B1C1D1E1FUL, 0x1011121314151617UL, 0x08090A0B0C0D0E0FUL, 0x0001020304050607UL)),
            (Hash32)bytes,
        ];

        foreach (Hash32 route in routes)
        {
            Assert.AreEqual(reference, route);
            Assert.AreEqual(reference.GetHashCode(), route.GetHashCode());
            Assert.AreEqual("0x" + hex, route.ToString());
        }

        Assert.HasCount(1, new HashSet<Hash32>(routes));
    }
}
