namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// The AVX2 bulk scans test four items at a time on their first limb and compare a candidate in full. These tests make the
/// first limb useless (every item shares the needle's), put matches before, at and after the point where a scan gives up
/// filtering, and check every operation against plain loops, for uint256 on every implementation and for Hash32.
/// </summary>
[TestClass]
public sealed class FirstLimbCollisionScanTests
{
    private static readonly uint256 Needle = new(0x1111_1111_1111_1111UL, 0x2222_2222_2222_2222UL, 0x3333_3333_3333_3333UL, 0x4444_4444_4444_4444UL);
    private static readonly Hash32 HashNeedle = new(0x1111_1111_1111_1111UL, 0x2222_2222_2222_2222UL, 0x3333_3333_3333_3333UL, 0x4444_4444_4444_4444UL);

    private static readonly int[] MatchPositions = [0, 1, 2, 3, 4, 5, 7, 8, 9, 11, 12, 13, 16, 17, 31, 32];

    // Items that agree with the needle on limb 0 (every third item: also on limb 1) and differ somewhere after it.
    private static uint256 Colliding(int i)
        => i % 3 == 0
            ? new uint256(Needle.U0, Needle.U1, (ulong)(i + 1), 7)
            : new uint256(Needle.U0, (ulong)(i + 1), 0, 0);

    private static Hash32 CollidingHash(int i)
        => i % 3 == 0
            ? new Hash32(0x1111_1111_1111_1111UL, 0x2222_2222_2222_2222UL, (ulong)(i + 1), 7)
            : new Hash32(0x1111_1111_1111_1111UL, (ulong)(i + 1), 0, 0);

    private static bool Same(uint256 a, uint256 b) => a.U0 == b.U0 && a.U1 == b.U1 && a.U2 == b.U2 && a.U3 == b.U3;

    private static IEnumerable<int[]> MatchSets(int length)
    {
        yield return [];
        foreach (int p in MatchPositions.Where(p => p < length))
        {
            yield return [p];
            yield return [p, Math.Min(length - 1, p + 9)];       // a second match after the scan has given up filtering
            yield return [p, length - 1];
        }
    }

    [TestMethod]
    public void UInt256_CollidingFirstLimbs_EveryOperationAgreesWithPlainLoops_OnEveryImplementation()
    {
        foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
        {
            foreach (int length in Enumerable.Range(0, 71).Append(257))
            {
                foreach (int[] matches in MatchSets(length))
                {
                    uint256[] haystack = Enumerable.Range(0, length).Select(Colliding).ToArray();
                    foreach (int m in matches)
                        haystack[m] = Needle;

                    int[] expected = Enumerable.Range(0, length).Where(i => Same(haystack[i], Needle)).ToArray();
                    string context = $"{impl.Name} length {length} matches [{string.Join(",", matches)}]";

                    if (length == 0)
                        continue; // the public methods return before reaching the implementation for an empty haystack

                    Assert.AreEqual(expected.Length == 0 ? -1 : expected[0], impl.IndexOf(haystack, in Needle), context + " IndexOf");
                    Assert.AreEqual(expected.Length, impl.CountEquals(haystack, in Needle), context + " CountEquals");

                    foreach (int capacity in new[] { 1, 2, 3, length + 1 })
                    {
                        int[] destination = new int[capacity];
                        int written = impl.FindAllEquals(haystack, in Needle, destination);
                        Assert.AreEqual(Math.Min(capacity, expected.Length), written, context + $" FindAll({capacity}) count");
                        CollectionAssert.AreEqual(expected.Take(written).ToArray(), destination.Take(written).ToArray(), context + $" FindAll({capacity}) indices");
                    }
                }
            }
        }
    }

    [TestMethod]
    public void Hash32_CollidingFirstLimbs_IndexOfAndCountEquals_AgreeWithPlainLoops()
    {
        foreach (int length in Enumerable.Range(0, 71).Append(257))
        {
            foreach (int[] matches in MatchSets(length))
            {
                Hash32[] haystack = Enumerable.Range(0, length).Select(CollidingHash).ToArray();
                foreach (int m in matches)
                    haystack[m] = HashNeedle;

                int[] expected = Enumerable.Range(0, length).Where(i => haystack[i].Equals(HashNeedle)).ToArray();
                string context = $"length {length} matches [{string.Join(",", matches)}]";

                Assert.AreEqual(expected.Length == 0 ? -1 : expected[0], Hash32.IndexOf(haystack, HashNeedle), context + " IndexOf");
                Assert.AreEqual(expected.Length, Hash32.CountEquals(haystack, HashNeedle), context + " CountEquals");
            }
        }
    }

    [TestMethod]
    public void UInt256_NeedleWhoseFirstLimbOnlyAppearsInOtherLimbs_IsNotMatched()
    {
        // The needle's first limb appears in later limbs of the items, and other items hold the needle's later limbs first: the
        // gather of first limbs must not confuse limb positions.
        uint256[] haystack = Enumerable.Range(0, 40).Select(i => i % 2 == 0
            ? new uint256((ulong)(i + 5), Needle.U0, Needle.U1, Needle.U2)
            : new uint256(Needle.U1, Needle.U0, (ulong)i, Needle.U3)).ToArray();

        foreach (UInt256BulkImplementation impl in UInt256BulkSimdImplementations.Supported())
        {
            Assert.AreEqual(-1, impl.IndexOf(haystack, in Needle), impl.Name);
            Assert.AreEqual(0, impl.CountEquals(haystack, in Needle), impl.Name);
            Assert.AreEqual(0, impl.FindAllEquals(haystack, in Needle, new int[4]), impl.Name);
        }
    }
}
