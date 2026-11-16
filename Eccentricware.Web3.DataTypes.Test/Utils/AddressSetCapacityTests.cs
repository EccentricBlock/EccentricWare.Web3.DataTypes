using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Table sizing for <see cref="AddressSet"/> and the null guard of <see cref="AddressBulkMatcher.Match"/>.</summary>
[TestClass]
public sealed class AddressSetCapacityTests
{
    [TestMethod]
    [DataRow(1, 0.72, 4)] // never below 4 slots
    [DataRow(2, 0.72, 4)]
    [DataRow(3, 0.72, 8)] // ceil(3 / 0.72) = 5 -> 8
    [DataRow(100, 0.72, 256)] // ceil(138.9) = 139 -> 256
    [DataRow(1000, 0.5, 2048)] // exactly 2000 -> 2048
    [DataRow(1024, 0.5, 2048)] // exactly 2048 stays 2048
    [DataRow(1025, 0.5, 4096)]
    [DataRow(100, 0.9, 128)] // ceil(111.1) = 112 -> 128
    public void ComputeCapacity_IsTheNextPowerOfTwoOfCountOverLoadFactor(int count, double loadFactor, int expected)
        => Assert.AreEqual(expected, AddressSet.ComputeCapacity(count, loadFactor));

    [TestMethod]
    public void ComputeCapacity_AtTheLargestTable_IsAcceptedAndOneMoreMemberIsRejected()
    {
        // 2^30 slots is the largest power of two that fits an array length. At load factor 0.5 that is 2^29 members.
        Assert.AreEqual(AddressSet.MaxCapacity, AddressSet.ComputeCapacity(1 << 29, 0.5));
        Assert.AreEqual(1 << 30, AddressSet.ComputeCapacity(1 << 29, 0.5));

        // One more member needs more than 2^30 slots; this used to overflow to a negative size inside NextPowerOfTwo.
        ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AddressSet.ComputeCapacity((1 << 29) + 1, 0.5));
        Assert.AreEqual("count", error.ParamName);
    }

    [TestMethod]
    [DataRow(1_000_000_000, 0.72)] // the case that overflowed in practice: 1e9 / 0.72 is about 1.39e9
    [DataRow(int.MaxValue, 0.9)]
    [DataRow(int.MaxValue, 0.5)]
    public void ComputeCapacity_RejectsCountsThatNeedMoreThanTheLargestTable(int count, double loadFactor)
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AddressSet.ComputeCapacity(count, loadFactor));

    [TestMethod]
    public void Build_StillBuildsNormalSets_ThroughComputeCapacity()
    {
        // 1,000 distinct addresses: bytes 0-1 hold the index, the rest is a constant that no probe below uses.
        Address[] addresses = Enumerable.Range(1, 1000).Select(i =>
        {
            byte[] bytes = Enumerable.Repeat((byte)0x11, 20).ToArray();
            bytes[0] = (byte)i;
            bytes[1] = (byte)(i >> 8);
            return Address.FromEvmBytes(bytes);
        }).ToArray();
        AddressSet set = AddressSet.Build(addresses);

        foreach (Address address in addresses)
            Assert.IsTrue(set.Contains(in address));

        Address missing = Address.FromEvmBytes(Enumerable.Repeat((byte)0x22, 20).ToArray());
        Assert.IsFalse(set.Contains(in missing));
    }

    [TestMethod]
    public void Match_WithANullSet_ThrowsArgumentNullException_EvenWhenThereIsNothingToMatch()
    {
        // It used to throw NullReferenceException for non-empty probes and silently return 0 for empty ones.
        Address[] probes = [Address.FromEvmBytes(new byte[20])];
        int[] destination = new int[1];

        ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(() => AddressBulkMatcher.Match(null!, probes, destination));
        Assert.AreEqual("set", error.ParamName);
        Assert.ThrowsExactly<ArgumentNullException>(() => AddressBulkMatcher.Match(null!, [], destination));
        Assert.ThrowsExactly<ArgumentNullException>(() => AddressBulkMatcher.Match(null!, probes, []));
    }
}
