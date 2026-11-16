namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="InstructionDiscriminator"/> parity members: <c>IsZero</c> and the <c>AsInt64</c> / <c>FromInt64</c> pair.</summary>
[TestClass]
public sealed class InstructionDiscriminatorInt64Tests
{
    [TestMethod]
    public void IsZero()
    {
        Assert.IsTrue(InstructionDiscriminator.Zero.IsZero);
        Assert.IsTrue(default(InstructionDiscriminator).IsZero);
        Assert.IsFalse(new InstructionDiscriminator(1).IsZero);
        Assert.IsFalse(new InstructionDiscriminator(0x8000000000000000UL).IsZero);
    }

    [TestMethod]
    public void Int64_RoundTrips_IncludingTopBit()
    {
        Assert.AreEqual(0L, InstructionDiscriminator.Zero.AsInt64);
        Assert.AreEqual(1L, new InstructionDiscriminator(1).AsInt64);
        Assert.AreEqual(-1L, new InstructionDiscriminator(ulong.MaxValue).AsInt64);
        Assert.AreEqual(long.MinValue, new InstructionDiscriminator(0x8000000000000000UL).AsInt64);

        foreach (ulong v in new ulong[] { 0, 1, 0x7FFFFFFFFFFFFFFF, 0x8000000000000000, 0xAFAF6D1F0D989BED, ulong.MaxValue })
        {
            var d = new InstructionDiscriminator(v);
            Assert.AreEqual(d, InstructionDiscriminator.FromInt64(d.AsInt64));
        }
    }

    [TestMethod]
    public void FromInt64_ReinterpretsTheBits_AtTheBoundaries()
    {
        Assert.AreEqual(ulong.MaxValue, InstructionDiscriminator.FromInt64(-1L).Value);
        Assert.AreEqual(0x8000000000000000UL, InstructionDiscriminator.FromInt64(long.MinValue).Value);
        Assert.AreEqual(0x7FFFFFFFFFFFFFFFUL, InstructionDiscriminator.FromInt64(long.MaxValue).Value);
        Assert.AreEqual(InstructionDiscriminator.Zero, InstructionDiscriminator.FromInt64(0L));
    }
}
