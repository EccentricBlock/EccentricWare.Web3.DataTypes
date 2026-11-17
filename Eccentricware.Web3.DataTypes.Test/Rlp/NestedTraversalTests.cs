using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>Direct-child enumeration over nested documents, coordinate base, and the enumerator contract.</summary>
[TestClass]
public sealed class NestedTraversalTests
{
    // [cat, [dog, []], 00, ""] = cc 83 63 61 74 | c5 83 64 6f 67 c0 | 00 | 80
    private static readonly byte[] Mixed = H("cc 83 63 61 74 c5 83 64 6f 67 c0 00 80");

    [TestMethod]
    public void TryGetChildren_EnumeratesDirectChildrenInOrder_WithDocumentRelativeRanges()
    {
        var root = DecodeOk(Mixed);

        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));
        var seen = new List<(RLPKind Kind, RLPRange Encoded, RLPRange Payload)>();
        while (children.MoveNext())
        {
            var child = children.Current;
            seen.Add((child.Kind, child.EncodedRange, child.PayloadRange));
        }

        CollectionAssert.AreEqual(
            new (RLPKind, RLPRange, RLPRange)[]
            {
                (RLPKind.ByteString, new RLPRange(1, 4), new RLPRange(2, 3)),
                (RLPKind.List, new RLPRange(5, 6), new RLPRange(6, 5)),
                (RLPKind.ByteString, new RLPRange(11, 1), new RLPRange(11, 1)),
                (RLPKind.ByteString, new RLPRange(12, 1), new RLPRange(13, 0)),
            },
            seen);
    }

    [TestMethod]
    public void GrandchildRanges_AreRelativeToTheOriginalDocument_NotToTheirParent()
    {
        var root = DecodeOk(Mixed);
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var rootChildren));
        Assert.IsTrue(rootChildren.MoveNext()); // cat
        Assert.IsTrue(rootChildren.MoveNext()); // [dog, []]
        var nested = rootChildren.Current;

        Assert.AreEqual(RLPStatus.Success, nested.TryGetChildren(out var grandchildren));
        Assert.IsTrue(grandchildren.MoveNext());
        var dog = grandchildren.Current;
        Assert.AreEqual(new RLPRange(6, 4), dog.EncodedRange);
        Assert.AreEqual(new RLPRange(7, 3), dog.PayloadRange);
        Assert.IsTrue(Mixed.AsSpan(dog.EncodedRange.Offset, dog.EncodedRange.Length).SequenceEqual(dog.Encoded));
        Assert.IsTrue(grandchildren.MoveNext());
        var empty = grandchildren.Current;
        Assert.AreEqual(RLPKind.List, empty.Kind);
        Assert.AreEqual(new RLPRange(10, 1), empty.EncodedRange);
        Assert.IsFalse(grandchildren.MoveNext());
    }

    [TestMethod]
    public void DeepDescent_KeepsTheOriginalCoordinateBase()
    {
        var input = NestedLists(20, out var offsets);
        var node = DecodeOk(input, Limits(maxDepth: 32));

        for (var level = 0; level < 20; level++)
        {
            Assert.AreEqual(offsets[level], node.EncodedRange.Offset, $"level {level}");
            Assert.IsTrue(input.AsSpan(node.EncodedRange.Offset, node.EncodedLength).SequenceEqual(node.Encoded));
            if (level == 19)
            {
                Assert.AreEqual(0, node.PayloadLength);
                break;
            }

            Assert.AreEqual(RLPStatus.Success, node.TryGetChildren(out var children));
            Assert.IsTrue(children.MoveNext());
            node = children.Current;
            Assert.IsFalse(children.MoveNext());
        }
    }

    [TestMethod]
    public void EmptyList_EnumeratesNothing()
    {
        var root = DecodeOk(H("c0"));

        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));
        Assert.IsFalse(children.MoveNext());
    }

    [TestMethod]
    public void MoveNext_AfterTheEnd_KeepsReturningFalse()
    {
        var root = DecodeOk(H("c2 01 02"));
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));

        Assert.IsTrue(children.MoveNext());
        Assert.IsTrue(children.MoveNext());
        Assert.IsFalse(children.MoveNext());
        Assert.IsFalse(children.MoveNext());
        Assert.IsFalse(children.MoveNext());
    }

    [TestMethod]
    public void TryGetChildren_OnAByteString_IsWrongKind_AndOnDefaultInvalidArgument()
    {
        Assert.AreEqual(RLPStatus.WrongKind, DecodeOk(H("83 64 6f 67")).TryGetChildren(out var fromString));
        Assert.IsFalse(fromString.MoveNext());
        Assert.AreEqual(RLPStatus.WrongKind, DecodeOk(H("00")).TryGetChildren(out _));
        Assert.AreEqual(RLPStatus.WrongKind, DecodeOk(H("80")).TryGetChildren(out _));
        RLPData none = default;
        Assert.AreEqual(RLPStatus.InvalidArgument, none.TryGetChildren(out var fromDefault));
        Assert.IsFalse(fromDefault.MoveNext());
    }

    [TestMethod]
    public void Current_IsStableBetweenMoveNextCalls()
    {
        var root = DecodeOk(H("c3 01 02 03"));
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));
        Assert.IsTrue(children.MoveNext());

        Assert.AreEqual(new RLPRange(1, 1), children.Current.EncodedRange);
        Assert.AreEqual(new RLPRange(1, 1), children.Current.EncodedRange);
        Assert.IsTrue(children.MoveNext());
        Assert.AreEqual(new RLPRange(2, 1), children.Current.EncodedRange);
    }

    [TestMethod]
    public void Enumeration_OfTheOracleTrees_MatchesStructure()
    {
        foreach (var vector in RlpVectors.Canonical)
        {
            var document = DecodeOk(vector.Encoded, Limits(maxItems: 9000));
            AssertTreeMatches(vector.Encoded, document, vector.Tree, vector.Name);
        }
    }

    [TestMethod]
    public void ChildrenOfALongList_AreEnumeratedAcrossMultiByteHeaders()
    {
        // f9 01 69 b8 38 <56> b9 01 2c <300>
        var vector = RlpVectors.Canonical.Single(v => v.Name == "list of two long strings");
        var root = DecodeOk(vector.Encoded);

        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));
        Assert.IsTrue(children.MoveNext());
        Assert.AreEqual(new RLPRange(3, 58), children.Current.EncodedRange);
        Assert.AreEqual(new RLPRange(5, 56), children.Current.PayloadRange);
        Assert.IsTrue(children.MoveNext());
        Assert.AreEqual(new RLPRange(61, 303), children.Current.EncodedRange);
        Assert.AreEqual(new RLPRange(64, 300), children.Current.PayloadRange);
        Assert.IsFalse(children.MoveNext());
    }

    [TestMethod]
    public void ViewIsNotASnapshot_ItReflectsBytesMutatedAfterValidation()
    {
        // Documents the stable-buffer contract (spec 3.3): the view borrows the caller's bytes and never copies them.
        var bytes = H("c4 83 01 02 03");
        var root = DecodeOk(bytes);
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));
        Assert.IsTrue(children.MoveNext());

        bytes[3] = 0x7f; // payload byte, structure unchanged

        Assert.AreEqual((byte)0x7f, children.Current.Payload[1]);
        Assert.AreEqual((byte)0x7f, root.Payload[2]);
    }

    [TestMethod]
    public void DocumentCorruptedAfterValidation_MoveNextThrowsInvalidOperation_InsteadOfEndingTheList()
    {
        var bytes = H("c4 83 01 02 03");
        var root = DecodeOk(bytes);
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));

        bytes[1] = 0x84; // the child now claims one byte more than its list holds

        var thrown = false;
        try
        {
            _ = children.MoveNext();
        }
        catch (InvalidOperationException)
        {
            thrown = true;
        }

        Assert.IsTrue(thrown);
    }

    [TestMethod]
    public void DocumentCorruptedAfterValidation_NeverReadsOutOfBounds_ForAnySingleByteChange()
    {
        // Every one-byte change of a valid document: enumeration either completes or throws InvalidOperationException; nothing else.
        var original = H("cc 83 63 61 74 c5 83 64 6f 67 c0 00 80");
        for (var position = 0; position < original.Length; position++)
        {
            for (var value = 0; value < 256; value++)
            {
                var bytes = (byte[])original.Clone();
                var root = DecodeOk(bytes);
                bytes[position] = (byte)value;

                try
                {
                    Walk(root);
                }
                catch (InvalidOperationException)
                {
                    // acceptable: damage detected
                }
            }
        }

        static void Walk(RLPData node)
        {
            if (node.TryGetChildren(out var children) != RLPStatus.Success)
            {
                return;
            }

            var guard = 0;
            while (children.MoveNext())
            {
                Walk(children.Current);
                Assert.IsLessThan(100, ++guard);
            }
        }
    }
}
