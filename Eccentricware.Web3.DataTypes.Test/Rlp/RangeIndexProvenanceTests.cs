using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Direct-child index: exact and one-short capacity, all-or-nothing workspace behaviour, kind failures, coordinate
/// base, and rejection of forged or corrupted ranges (spec 4.2, 6, 10.1).
/// </summary>
[TestClass]
public sealed class RangeIndexProvenanceTests
{
    private static readonly RLPRange Sentinel = new(-12345, -54321);

    // c8 83 63 61 74 83 64 6f 67 = [cat, dog]
    private static readonly byte[] CatDog = H("c8 83 63 61 74 83 64 6f 67");

    private static RLPRange[] SentinelWorkspace(int length)
    {
        var workspace = new RLPRange[length];
        Array.Fill(workspace, Sentinel);
        return workspace;
    }

    // ------------------------------------------------------------------ capacity

    [TestMethod]
    public void ExactCapacity_Succeeds_WithSpecExampleRanges()
    {
        var root = DecodeOk(CatDog);
        var workspace = SentinelWorkspace(2);

        Assert.AreEqual(RLPStatus.Success, root.TryIndexDirectChildren(workspace, out var index, out var required));

        Assert.IsTrue(index.IsValid);
        Assert.AreEqual(2, index.Count);
        Assert.AreEqual(2, required);
        Assert.AreEqual(new RLPRange(1, 4), workspace[0]);
        Assert.AreEqual(new RLPRange(5, 4), workspace[1]);
        Assert.AreEqual(RLPStatus.Success, index.TryGetAt(0, out var cat));
        Assert.AreEqual(new RLPRange(2, 3), cat.PayloadRange);
        Assert.AreEqual(RLPStatus.Success, index.TryGetAt(1, out var dog));
        Assert.AreEqual(new RLPRange(6, 3), dog.PayloadRange);
    }

    [TestMethod]
    public void OneShort_ReportsIndexCapacityExceeded_WithTheExactCount_AndLeavesTheWorkspaceUntouched()
    {
        var root = DecodeOk(CatDog);
        var workspace = SentinelWorkspace(1);

        Assert.AreEqual(RLPStatus.IndexCapacityExceeded, root.TryIndexDirectChildren(workspace, out var index, out var required));

        Assert.IsFalse(index.IsValid);
        Assert.AreEqual(0, index.Count);
        Assert.AreEqual(2, required);
        Assert.AreEqual(Sentinel, workspace[0]);
    }

    [TestMethod]
    public void ZeroLengthWorkspace_ForANonEmptyList_ReportsTheRequiredCount()
    {
        var root = DecodeOk(CatDog);

        Assert.AreEqual(RLPStatus.IndexCapacityExceeded, root.TryIndexDirectChildren(Span<RLPRange>.Empty, out var index, out var required));
        Assert.IsFalse(index.IsValid);
        Assert.AreEqual(2, required);
    }

    [TestMethod]
    public void EmptyList_SucceedsWithZeroCapacity_AndYieldsAValidIndexOfCountZero()
    {
        var root = DecodeOk(H("c0"));

        Assert.AreEqual(RLPStatus.Success, root.TryIndexDirectChildren(Span<RLPRange>.Empty, out var index, out var required));

        Assert.IsTrue(index.IsValid);
        Assert.AreEqual(0, index.Count);
        Assert.AreEqual(0, required);
        Assert.AreEqual(RLPStatus.InvalidArgument, index.TryGetAt(0, out var child));
        Assert.IsFalse(child.IsValid);
    }

    [TestMethod]
    public void EmptyList_WithALargeWorkspace_LeavesItUntouched()
    {
        var workspace = SentinelWorkspace(4);

        Assert.AreEqual(RLPStatus.Success, DecodeOk(H("c0")).TryIndexDirectChildren(workspace, out var index, out _));

        Assert.IsTrue(index.IsValid);
        Assert.IsTrue(workspace.All(r => r == Sentinel));
    }

    [TestMethod]
    public void UnusedCapacity_IsLeftUntouched_AndCountIsTheChildCount()
    {
        var workspace = SentinelWorkspace(5);

        Assert.AreEqual(RLPStatus.Success, DecodeOk(CatDog).TryIndexDirectChildren(workspace, out var index, out var required));

        Assert.AreEqual(2, index.Count);
        Assert.AreEqual(2, required);
        Assert.AreEqual(Sentinel, workspace[2]);
        Assert.AreEqual(Sentinel, workspace[4]);
        Assert.AreEqual(RLPStatus.InvalidArgument, index.TryGetAt(2, out _));
    }

    [TestMethod]
    public void CapacityBoundary_AtChildCountMinusOneEqualAndPlusOne_ForManyChildren()
    {
        foreach (var children in new[] { 1, 2, 9, 55, 56, 255, 256, 1000 })
        {
            var input = WrapInList(Repeat(0x01, children));
            var root = DecodeOk(input, Limits(maxItems: 2000));

            var tooSmall = SentinelWorkspace(children - 1);
            Assert.AreEqual(RLPStatus.IndexCapacityExceeded, root.TryIndexDirectChildren(tooSmall, out var failed, out var required), $"{children}");
            Assert.AreEqual(children, required);
            Assert.IsFalse(failed.IsValid);
            Assert.IsTrue(tooSmall.All(r => r == Sentinel), $"{children}: workspace untouched");

            foreach (var length in new[] { children, children + 1 })
            {
                var fits = SentinelWorkspace(length);
                Assert.AreEqual(RLPStatus.Success, root.TryIndexDirectChildren(fits, out var index, out required), $"{children}/{length}");
                Assert.AreEqual(children, index.Count);
                Assert.AreEqual(children, required);
                Assert.AreEqual(RLPStatus.Success, index.TryGetAt(children - 1, out var last));
                Assert.AreEqual(input.Length - 1, last.EncodedRange.Offset);
            }
        }
    }

    // ------------------------------------------------------------------ kind failures

    [TestMethod]
    public void ByteStrings_AreWrongKind_WithRequiredCountZero_AndAnUntouchedWorkspace()
    {
        foreach (var hex in new[] { "80", "00", "81 80", "83 64 6f 67", "b8 38 aa*56" })
        {
            var workspace = SentinelWorkspace(3);

            Assert.AreEqual(RLPStatus.WrongKind, DecodeOk(H(hex)).TryIndexDirectChildren(workspace, out var index, out var required), hex);

            Assert.IsFalse(index.IsValid, hex);
            Assert.AreEqual(0, required, hex);
            Assert.IsTrue(workspace.All(r => r == Sentinel), hex);
        }
    }

    // ------------------------------------------------------------------ coordinate base and nesting

    [TestMethod]
    public void NestedList_Index_KeepsTheOriginalCoordinateBase()
    {
        // cc 83 63 61 74 c5 83 64 6f 67 c0 00 80
        var document = H("cc 83 63 61 74 c5 83 64 6f 67 c0 00 80");
        var root = DecodeOk(document);
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));
        Assert.IsTrue(children.MoveNext());
        Assert.IsTrue(children.MoveNext());
        var nested = children.Current;

        var workspace = new RLPRange[2];
        Assert.AreEqual(RLPStatus.Success, nested.TryIndexDirectChildren(workspace, out var index, out _));

        Assert.AreEqual(new RLPRange(6, 4), workspace[0]);
        Assert.AreEqual(new RLPRange(10, 1), workspace[1]);
        Assert.AreEqual(RLPStatus.Success, index.TryGetAt(0, out var dog));
        Assert.AreEqual(new RLPRange(6, 4), dog.EncodedRange);
        Assert.AreEqual(new RLPRange(7, 3), dog.PayloadRange);
        Assert.IsTrue(dog.Payload.SequenceEqual("dog"u8));
        Assert.AreEqual(RLPStatus.Success, index.TryGetAt(1, out var empty));
        Assert.AreEqual(RLPKind.List, empty.Kind);
        Assert.AreEqual(new RLPRange(10, 1), empty.EncodedRange);
    }

    [TestMethod]
    public void IndexedChild_OfAList_CanBeIndexedAgain_WithGrandchildRangesDocumentRelative()
    {
        var document = H("c5 01 c3 02 c1 03");
        var root = DecodeOk(document);
        var outer = new RLPRange[2];
        Assert.AreEqual(RLPStatus.Success, root.TryIndexDirectChildren(outer, out var outerIndex, out _));
        Assert.AreEqual(RLPStatus.Success, outerIndex.TryGetAt(1, out var middle));

        var inner = new RLPRange[2];
        Assert.AreEqual(RLPStatus.Success, middle.TryIndexDirectChildren(inner, out var innerIndex, out _));
        Assert.AreEqual(RLPStatus.Success, innerIndex.TryGetAt(1, out var deepest));

        Assert.AreEqual(new RLPRange(4, 2), deepest.EncodedRange);
        Assert.AreEqual(RLPKind.List, deepest.Kind);
        Assert.AreEqual(RLPStatus.Success, deepest.TryGetChildren(out var leaves));
        Assert.IsTrue(leaves.MoveNext());
        Assert.AreEqual(new RLPRange(5, 1), leaves.Current.EncodedRange);
    }

    [TestMethod]
    public void IndexedChildren_MatchEnumeratedChildren_ForEveryCanonicalVectorList()
    {
        foreach (var vector in RlpVectors.Canonical)
        {
            var root = DecodeOk(vector.Encoded, Limits(maxItems: 9000));
            if (root.Kind != RLPKind.List)
            {
                continue;
            }

            var enumerated = new List<RLPRange>();
            Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));
            while (children.MoveNext())
            {
                enumerated.Add(children.Current.EncodedRange);
            }

            var workspace = new RLPRange[enumerated.Count];
            Assert.AreEqual(RLPStatus.Success, root.TryIndexDirectChildren(workspace, out var index, out var required), vector.Name);
            Assert.AreEqual(enumerated.Count, required, vector.Name);
            CollectionAssert.AreEqual(enumerated, workspace, vector.Name);
            for (var i = 0; i < enumerated.Count; i++)
            {
                Assert.AreEqual(RLPStatus.Success, index.TryGetAt(i, out var child), vector.Name);
                Assert.AreEqual(enumerated[i], child.EncodedRange, vector.Name);
            }
        }
    }

    // ------------------------------------------------------------------ ordinals

    [TestMethod]
    public void TryGetAt_RejectsNegativeAndOutOfRangeOrdinals()
    {
        var workspace = new RLPRange[2];
        Assert.AreEqual(RLPStatus.Success, DecodeOk(CatDog).TryIndexDirectChildren(workspace, out var index, out _));

        foreach (var ordinal in new[] { -1, 2, 3, int.MinValue, int.MaxValue })
        {
            Assert.AreEqual(RLPStatus.InvalidArgument, index.TryGetAt(ordinal, out var child), $"{ordinal}");
            Assert.IsFalse(child.IsValid);
        }
    }

    // ------------------------------------------------------------------ forged and corrupted workspace

    [TestMethod]
    public void ForgedRange_InBoundsButNotOnAChildBoundary_IsNeverAccepted()
    {
        // [ "abc"(84 61 62 63), 01 ] with a payload that also contains a plausible literal: 83 01 02 03 inside c4.
        var document = H("c4 83 01 02 03");
        var workspace = new RLPRange[1];
        Assert.AreEqual(RLPStatus.Success, DecodeOk(document).TryIndexDirectChildren(workspace, out var index, out _));
        Assert.AreEqual(new RLPRange(1, 4), workspace[0]);
        Assert.AreEqual(RLPStatus.Success, index.TryGetAt(0, out _));

        // Every other in-bounds (offset, length) pair, including a literal 01 at offset 2, must be refused.
        for (var offset = -2; offset <= 6; offset++)
        {
            for (var length = -2; length <= 6; length++)
            {
                if (offset == 1 && length == 4)
                {
                    continue;
                }

                workspace[0] = new RLPRange(offset, length);
                var status = index.TryGetAt(0, out var child);

                Assert.AreEqual(RLPStatus.InvalidListBoundary, status, $"({offset},{length})");
                Assert.IsFalse(child.IsValid, $"({offset},{length})");
            }
        }
    }

    [TestMethod]
    public void ForgedRange_AmongSeveralChildren_IsRejected_WhenOneEntryIsCorrupted()
    {
        // [01, 82 aa bb, 03] -> children at 1 (len 1), 2 (len 3), 5 (len 1)
        var document = H("c5 01 82 aa bb 03");
        var workspace = new RLPRange[3];
        Assert.AreEqual(RLPStatus.Success, DecodeOk(document).TryIndexDirectChildren(workspace, out var index, out _));
        var original = (RLPRange[])workspace.Clone();

        // A literal-looking byte inside the string payload (offset 3 is 0xaa, offset 4 is 0xbb: not literals; use the
        // 0x03 sibling's neighbours and the inside of the string).
        var forged = new[]
        {
            new RLPRange(3, 1), new RLPRange(4, 1), new RLPRange(2, 1), new RLPRange(2, 4), new RLPRange(1, 2),
            new RLPRange(0, 6), new RLPRange(6, 0), new RLPRange(int.MaxValue, 1), new RLPRange(1, int.MaxValue),
            new RLPRange(int.MinValue, 1), new RLPRange(2, int.MinValue), new RLPRange(int.MaxValue, int.MaxValue),
        };
        for (var ordinal = 0; ordinal < 3; ordinal++)
        {
            foreach (var candidate in forged)
            {
                Array.Copy(original, workspace, original.Length);
                workspace[ordinal] = candidate;

                var status = index.TryGetAt(ordinal, out var child);

                Assert.AreEqual(RLPStatus.InvalidListBoundary, status, $"ordinal {ordinal}, {candidate}");
                Assert.IsFalse(child.IsValid);
            }
        }
    }

    [TestMethod]
    public void CorruptedWorkspace_NeverThrowsOrReadsOutOfBounds_ForAnyNeighbouringEntries()
    {
        var document = H("c5 01 82 aa bb 03");
        var workspace = new RLPRange[3];
        Assert.AreEqual(RLPStatus.Success, DecodeOk(document).TryIndexDirectChildren(workspace, out var index, out _));
        var values = new[] { -1, 0, 1, 2, 3, 4, 5, 6, 7, int.MaxValue, int.MinValue };

        foreach (var a in values)
        {
            foreach (var b in values)
            {
                foreach (var c in values)
                {
                    workspace[0] = new RLPRange(a, b);
                    workspace[1] = new RLPRange(c, a);
                    workspace[2] = new RLPRange(b, c);
                    for (var ordinal = -1; ordinal <= 3; ordinal++)
                    {
                        var status = index.TryGetAt(ordinal, out var child);

                        Assert.AreEqual(status == RLPStatus.Success, child.IsValid);
                        if (status == RLPStatus.Success)
                        {
                            // Whatever was accepted is a real slice of the document.
                            Assert.IsTrue(document.AsSpan(child.EncodedRange.Offset, child.EncodedRange.Length).SequenceEqual(child.Encoded));
                        }
                    }
                }
            }
        }
    }

    [TestMethod]
    public void DocumentCorruptedAfterIndexing_TryGetAt_ReturnsInvalidListBoundary_WhenTheHeaderNoLongerMatches()
    {
        var document = H("c8 83 63 61 74 83 64 6f 67");
        var workspace = new RLPRange[2];
        Assert.AreEqual(RLPStatus.Success, DecodeOk(document).TryIndexDirectChildren(workspace, out var index, out _));

        document[5] = 0x82; // second child now ends one byte early

        Assert.AreEqual(RLPStatus.InvalidListBoundary, index.TryGetAt(1, out var child));
        Assert.IsFalse(child.IsValid);
        Assert.AreEqual(RLPStatus.Success, index.TryGetAt(0, out _));
    }

    [TestMethod]
    public void DocumentCorruptedBeforeIndexing_TryIndexDirectChildren_ReturnsInvalidListBoundaryWithoutThrowing()
    {
        var document = H("c8 83 63 61 74 83 64 6f 67");
        var root = DecodeOk(document);
        document[5] = 0x85; // second child now claims bytes beyond the list

        var workspace = SentinelWorkspace(4);
        Assert.AreEqual(RLPStatus.InvalidListBoundary, root.TryIndexDirectChildren(workspace, out var index, out var required));

        Assert.IsFalse(index.IsValid);
        Assert.AreEqual(0, required);
        Assert.IsTrue(workspace.All(r => r == Sentinel), "damage is detected in the count pass, before any write");
    }

    // ------------------------------------------------------------------ index views are independent values

    [TestMethod]
    public void TwoIndexes_OverDifferentWorkspaces_AreIndependent()
    {
        var root = DecodeOk(CatDog);
        var first = new RLPRange[2];
        var second = new RLPRange[2];

        Assert.AreEqual(RLPStatus.Success, root.TryIndexDirectChildren(first, out var firstIndex, out _));
        Assert.AreEqual(RLPStatus.Success, root.TryIndexDirectChildren(second, out var secondIndex, out _));

        CollectionAssert.AreEqual(first, second);
        Assert.AreEqual(firstIndex.Count, secondIndex.Count);
    }
}
