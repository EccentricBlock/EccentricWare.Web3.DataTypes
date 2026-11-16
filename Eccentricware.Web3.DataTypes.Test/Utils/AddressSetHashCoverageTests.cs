using EccentricWare.Web3.DataTypes.Utils;
using System.Buffers.Binary;
using static EccentricWare.Web3.DataTypes.Tests.AddressTestHelpers;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Every limb of the 32-byte key, and the address family, must reach <see cref="AddressSet"/>'s hash. Each test builds a
/// set from keys that differ in ONE part only; a hash that ignored that part gives every key the same slot, so the mean
/// probe length would be about N/2 (2,048 here) instead of about 1.5.
/// Table hashing uses per-process random keys, so the probe-length bound cannot be seeded. Simulation (port of the
/// hash and of Build, 300,000 random seeds per family at N = 4,096 over exactly these families) gave a mean probe length
/// of 1.50 with a worst case of 1.66, and 0 of 4.2 million runs reached 2.0, about 12 standard deviations (0.03) above
/// the mean. The broken hashes score 2,048 (a limb ignored) or 2.5 (the family ignored on same-payload EVM/Solana pairs).
/// </summary>
[TestClass]
public sealed class AddressSetHashCoverageTests
{
    private const int MemberCount = 4_096;
    private const double ProbeLengthBound = 2.0;

    private static Address Solana(ulong u0, ulong u1, ulong u2, ulong u3)
    {
        byte[] bytes = new byte[Address.SolanaByteLength];
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(0, 8), u0);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8, 8), u1);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(16, 8), u2);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(24, 8), u3);
        return Address.FromSolanaBytes(bytes);
    }

    // An EVM address stores bytes 16..19 in the top half of the third limb and nothing in the fourth.
    private static Address Evm(ulong u0, ulong u1, uint tail)
    {
        byte[] bytes = new byte[Address.EvmByteLength];
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(0, 8), u0);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8, 8), u1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), tail);
        return Address.FromEvmBytes(bytes);
    }

    private const ulong FixedU0 = 0x0123456789ABCDEFUL;
    private const ulong FixedU1 = 0xFEDCBA9876543210UL;
    private const ulong FixedU2 = 0x1111111111111111UL;
    private const ulong FixedU3 = 0x7777777777777777UL;

    /// <summary>The first 2 * MemberCount keys of a family: the first half are members, the second half outsiders.</summary>
    private static Address[] Family(string family)
    {
        var random = new Random(77);
        ulong[] r = new ulong[4 * MemberCount];
        for (int i = 0; i < r.Length; i++)
            r[i] = (ulong)random.NextInt64() ^ ((ulong)random.NextInt64() << 1);

        return Enumerable.Range(0, 2 * MemberCount).Select(i => family switch
        {
            "Solana u0 random" => Solana(r[i], FixedU1, FixedU2, FixedU3),
            "Solana u1 random" => Solana(FixedU0, r[i], FixedU2, FixedU3),
            "Solana u0 sequential" => Solana((ulong)i, FixedU1, FixedU2, FixedU3),
            "Solana u1 sequential" => Solana(FixedU0, (ulong)i, FixedU2, FixedU3),
            "Solana bytes 16..31 random" => Solana(FixedU0, FixedU1, r[2 * i], r[2 * i + 1]),
            "Solana u2 random" => Solana(FixedU0, FixedU1, r[i], FixedU3),
            "Solana u3 random" => Solana(FixedU0, FixedU1, FixedU2, r[i]),
            "Solana u2 sequential" => Solana(FixedU0, FixedU1, (ulong)i, FixedU3),
            "Solana u3 sequential" => Solana(FixedU0, FixedU1, FixedU2, (ulong)i),
            "Solana u2 sequential high half" => Solana(FixedU0, FixedU1, (ulong)i << 32, FixedU3),
            "Solana u3 sequential high half" => Solana(FixedU0, FixedU1, FixedU2, (ulong)i << 32),
            "EVM bytes 16..19 sequential" => Evm(FixedU0, FixedU1, (uint)i),
            "EVM bytes 16..19 random" => Evm(FixedU0, FixedU1, (uint)r[i]),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, null),
        }).ToArray();
    }

    private static void AssertCorrectAndProbesStayShort(AddressSet set, Address[] members, Address[] outsiders, string what)
    {
        Assert.AreEqual(members.Length, set.Count, what);
        Assert.IsTrue(members.All(m => set.Contains(m)), $"{what}: a member is missing");
        Assert.IsFalse(outsiders.Any(o => set.Contains(o)), $"{what}: an outsider is reported present");

        set.GetPlacementStatistics(out _, out double averageProbeLength);
        Assert.IsLessThan(ProbeLengthBound, averageProbeLength, $"{what}: mean probe length {averageProbeLength:F3} for {members.Length} members");
    }

    [TestMethod]
    [DataRow("Solana u0 random")]
    [DataRow("Solana u1 random")]
    [DataRow("Solana u0 sequential")]
    [DataRow("Solana u1 sequential")]
    [DataRow("Solana bytes 16..31 random")]
    [DataRow("Solana u2 random")]
    [DataRow("Solana u3 random")]
    [DataRow("Solana u2 sequential")]
    [DataRow("Solana u3 sequential")]
    [DataRow("Solana u2 sequential high half")]
    [DataRow("Solana u3 sequential high half")]
    [DataRow("EVM bytes 16..19 sequential")]
    [DataRow("EVM bytes 16..19 random")]
    public void KeysDifferingInOnlyOnePartOfTheKey_BuildCorrectlyAndDoNotCluster(string family)
    {
        Address[] keys = Family(family);
        Address[] members = keys[..MemberCount];
        Address[] outsiders = keys[MemberCount..];
        Assert.AreEqual(2 * MemberCount, keys.Distinct().Count(), "family must consist of distinct keys");

        AssertCorrectAndProbesStayShort(AddressSet.Build(members), members, outsiders, family);
    }

    [TestMethod]
    [DataRow("Solana bytes 16..31 random")]
    [DataRow("Solana u2 sequential")]
    [DataRow("Solana u3 sequential")]
    [DataRow("EVM bytes 16..19 random")]
    public void MatchReportsExactlyTheMembers_ForKeysDifferingInOnlyOnePartOfTheKey(string family)
    {
        Address[] keys = Family(family);
        Address[] members = keys[..MemberCount];
        // Probes alternate member, outsider; only the even positions are members.
        Address[] probes = Enumerable.Range(0, 2 * MemberCount).Select(i => i % 2 == 0 ? members[i / 2] : keys[MemberCount + i / 2]).ToArray();
        int[] matched = new int[probes.Length];

        int written = AddressBulkMatcher.Match(AddressSet.Build(members), probes, matched);

        Assert.AreEqual(MemberCount, written);
        CollectionAssert.AreEqual(Enumerable.Range(0, MemberCount).Select(i => 2 * i).ToArray(), matched[..written]);
    }

    [TestMethod]
    public void SamePayloadEvmAndSolanaPairs_AreSeparateMembers_AndDoNotCluster()
    {
        Address[] evm = RandomEvm(MemberCount / 2, 91);
        Address[] solana = evm.Select(SolanaWithSamePayloadAs).ToArray();
        Address[] both = [.. evm, .. solana];

        AddressSet set = AddressSet.Build(both);

        Assert.AreEqual(both.Length, set.Count);
        Assert.IsTrue(both.All(a => set.Contains(a)), "a pair member is missing");
        set.GetPlacementStatistics(out _, out double averageProbeLength);
        Assert.IsLessThan(ProbeLengthBound, averageProbeLength, $"mean probe length {averageProbeLength:F3} for {both.Length} members");
    }

    [TestMethod]
    public void SamePayloadEvmAndSolanaPairs_AreNotConfused_WhenOnlyOneFamilyIsInTheSet()
    {
        Address[] evm = RandomEvm(MemberCount / 2, 92);
        Address[] solana = evm.Select(SolanaWithSamePayloadAs).ToArray();

        AddressSet evmOnly = AddressSet.Build(evm);
        AddressSet solanaOnly = AddressSet.Build(solana);

        Assert.IsTrue(evm.All(a => evmOnly.Contains(a)));
        Assert.IsFalse(solana.Any(a => evmOnly.Contains(a)), "a Solana address matched its EVM twin");
        Assert.IsTrue(solana.All(a => solanaOnly.Contains(a)));
        Assert.IsFalse(evm.Any(a => solanaOnly.Contains(a)), "an EVM address matched its Solana twin");
    }
}
