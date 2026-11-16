using EccentricWare.Web3.DataTypes.Utils;
using static EccentricWare.Web3.DataTypes.Tests.AddressTestHelpers;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="AddressBulkMatcher.ContainsLinear"/> and <see cref="AddressBulkMatcher.Match"/>.</summary>
[TestClass]
public sealed class AddressBulkMatcherTests
{
    // ----------------- ContainsLinear -----------------

    [TestMethod]
    public void ContainsLinear_EmptyHaystack_IsFalse()
    {
        Assert.IsFalse(AddressBulkMatcher.ContainsLinear(ReadOnlySpan<Address>.Empty, Address.ZeroEvm));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(33)]
    public void ContainsLinear_FindsNeedleAtEveryPosition(int length)
    {
        Address[] haystack = RandomEvm(length, 11);
        Address missing = RandomEvm(1, 12)[0];

        for (int position = 0; position < length; position++)
            Assert.IsTrue(AddressBulkMatcher.ContainsLinear(haystack, haystack[position]), $"length {length}, position {position}");

        Assert.IsFalse(AddressBulkMatcher.ContainsLinear(haystack, missing));
    }

    [TestMethod]
    public void ContainsLinear_RespectsFamily_WhenPayloadsAreIdentical()
    {
        Address evm = RandomEvm(1, 13)[0];
        Address solana = SolanaWithSamePayloadAs(evm);

        Assert.IsFalse(AddressBulkMatcher.ContainsLinear([solana], evm));
        Assert.IsFalse(AddressBulkMatcher.ContainsLinear([evm], solana));
        Assert.IsTrue(AddressBulkMatcher.ContainsLinear([solana, evm], evm));
        Assert.IsTrue(AddressBulkMatcher.ContainsLinear([evm, solana], solana));
    }

    [TestMethod]
    public void ContainsLinear_ZeroAddressesOfDifferentFamilies_AreNotInterchangeable()
    {
        Assert.IsFalse(AddressBulkMatcher.ContainsLinear([Address.ZeroSolana], default));
        Assert.IsFalse(AddressBulkMatcher.ContainsLinear([default], Address.ZeroSolana));
        Assert.IsTrue(AddressBulkMatcher.ContainsLinear([default], Address.ZeroEvm));
    }

    [TestMethod]
    public void ContainsLinear_DetectsDifferenceInAnyLimb()
    {
        byte[] bytes = new byte[Address.SolanaByteLength];
        new Random(14).NextBytes(bytes);
        Address needle = Address.FromSolanaBytes(bytes);

        for (int i = 0; i < bytes.Length; i++)
        {
            byte[] changed = (byte[])bytes.Clone();
            changed[i] ^= 0x80;
            Assert.IsFalse(AddressBulkMatcher.ContainsLinear([Address.FromSolanaBytes(changed)], needle), $"byte {i}");
        }

        Assert.IsTrue(AddressBulkMatcher.ContainsLinear([Address.FromSolanaBytes(bytes)], needle));
    }

    [TestMethod]
    public void ContainsLinear_AgreesWithEquals_OnRandomMixedData()
    {
        Address[] haystack = [.. RandomEvm(40, 15), .. RandomSolana(40, 16)];
        Address[] probes = [.. RandomEvm(40, 15), .. RandomSolana(40, 17), .. RandomEvm(40, 18), haystack[3], haystack[77]];

        foreach (Address probe in probes)
            Assert.AreEqual(Array.IndexOf(haystack, probe) >= 0, AddressBulkMatcher.ContainsLinear(haystack, probe));
    }

    [TestMethod]
    public void ContainsLinear_LargeHaystack_FindsTheLastElement()
    {
        Address[] haystack = RandomEvm(65_536, 43);

        Assert.IsTrue(AddressBulkMatcher.ContainsLinear(haystack, haystack[^1]));
        Assert.IsFalse(AddressBulkMatcher.ContainsLinear(haystack, RandomEvm(1, 44)[0]));
    }

    // ----------------- Match -----------------

    [TestMethod]
    public void Match_WritesIndicesOfProbesInSet_InOrder()
    {
        Address[] members = RandomEvm(50, 19);
        Address[] outsiders = RandomEvm(50, 20);
        AddressSet set = AddressSet.Build(members);

        Address[] probes = [outsiders[0], members[5], outsiders[1], members[9], members[2], outsiders[2]];
        int[] matches = Filled(probes.Length, -1);

        int written = AddressBulkMatcher.Match(set, probes, matches);

        Assert.AreEqual(3, written);
        CollectionAssert.AreEqual(new[] { 1, 3, 4, -1, -1, -1 }, matches); // the tail keeps its sentinel
    }

    [TestMethod]
    public void Match_BufferExactlyAsLargeAsTheMatches_FillsItAndReturnsTheCount()
    {
        Address[] members = RandomEvm(10, 45);
        AddressSet set = AddressSet.Build(members);
        Address[] probes = [members[7], RandomEvm(1, 46)[0], members[0], members[3]];
        int[] matches = Filled(3, -1);

        int written = AddressBulkMatcher.Match(set, probes, matches);

        Assert.AreEqual(3, written);
        CollectionAssert.AreEqual(new[] { 0, 2, 3 }, matches);
    }

    [TestMethod]
    public void Match_StopsAtTheEndOfTheOutputBuffer()
    {
        Address[] members = RandomEvm(10, 21);
        AddressSet set = AddressSet.Build(members);
        int[] matches = new int[4];

        int written = AddressBulkMatcher.Match(set, members, matches);

        Assert.AreEqual(4, written);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, matches);
    }

    // A full buffer is not an error: later unmatched probes are skipped and nothing past the buffer is written.
    [TestMethod]
    public void Match_FullBufferFollowedByMisses_ReturnsTheBufferLength()
    {
        Address[] members = RandomEvm(5, 47);
        AddressSet set = AddressSet.Build(members);
        Address[] probes = [members[0], members[1], .. RandomEvm(20, 48)];
        int[] matches = Filled(2, -1);

        Assert.AreEqual(2, AddressBulkMatcher.Match(set, probes, matches));
        CollectionAssert.AreEqual(new[] { 0, 1 }, matches);
    }

    [TestMethod]
    public void Match_DuplicateProbes_ReportEveryOccurrence()
    {
        Address member = RandomEvm(1, 49)[0];
        Address outsider = RandomEvm(1, 50)[0];
        AddressSet set = AddressSet.Build([member]);
        int[] matches = Filled(6, -1);

        int written = AddressBulkMatcher.Match(set, [member, member, outsider, member], matches);

        Assert.AreEqual(3, written);
        CollectionAssert.AreEqual(new[] { 0, 1, 3, -1, -1, -1 }, matches);
    }

    [TestMethod]
    public void Match_NoProbeInTheSet_WritesNothing()
    {
        AddressSet set = AddressSet.Build(RandomEvm(20, 51));
        int[] matches = Filled(5, -1);

        Assert.AreEqual(0, AddressBulkMatcher.Match(set, RandomEvm(5, 52), matches));
        Assert.IsTrue(matches.All(m => m == -1));
    }

    [TestMethod]
    public void Match_SingleProbe_SingleSlotBuffer()
    {
        Address member = RandomEvm(1, 53)[0];
        AddressSet set = AddressSet.Build([member]);
        int[] matches = Filled(1, -1);

        Assert.AreEqual(1, AddressBulkMatcher.Match(set, [member], matches));
        Assert.AreEqual(0, matches[0]);
    }

    // The same payload in the other family is not a match.
    [TestMethod]
    public void Match_OnlyTheFamilyInTheSetMatches()
    {
        Address evm = RandomEvm(1, 54)[0];
        Address solanaTwin = SolanaWithSamePayloadAs(evm);
        int[] matches = Filled(4, -1);

        Assert.AreEqual(2, AddressBulkMatcher.Match(AddressSet.Build([evm]), [solanaTwin, evm, solanaTwin, evm], matches));
        CollectionAssert.AreEqual(new[] { 1, 3, -1, -1 }, matches);

        matches = Filled(4, -1);
        Assert.AreEqual(2, AddressBulkMatcher.Match(AddressSet.Build([solanaTwin]), [solanaTwin, evm, solanaTwin, evm], matches));
        CollectionAssert.AreEqual(new[] { 0, 2, -1, -1 }, matches);
    }

    [TestMethod]
    public void Match_EmptyInputs_WriteNothing()
    {
        AddressSet set = AddressSet.Build(RandomEvm(5, 22));
        int[] matches = Filled(5, -1);

        Assert.AreEqual(0, AddressBulkMatcher.Match(set, ReadOnlySpan<Address>.Empty, matches));
        Assert.AreEqual(0, AddressBulkMatcher.Match(set, RandomEvm(3, 22), Span<int>.Empty));
        Assert.AreEqual(0, AddressBulkMatcher.Match(AddressSet.Build(ReadOnlySpan<Address>.Empty), RandomEvm(3, 22), matches));
        Assert.IsTrue(matches.All(m => m == -1));
    }
}
