using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>Resource budgets at limit-1, limit and limit+1, invalid configurations, and budget precedence (spec 5.3, 9, 12.2).</summary>
[TestClass]
public sealed class BoundaryAndLimitsTests
{
    [TestMethod]
    [DynamicData(nameof(RlpVectors.LimitVectorsRows), typeof(RlpVectors))]
    public void LimitVector_ReturnsTheExpectedStatus(DecodeVector vector)
    {
        var limits = vector.Limits;
        var status = RLPData.TryDecodeComplete(vector.Input, in limits, out var document, out var error);

        Assert.AreEqual(vector.Status, status, vector.Name);
        Assert.AreEqual(vector.Status, error.Status, vector.Name);
        Assert.AreEqual(vector.Status == RLPStatus.Success, document.IsValid, vector.Name);
        if (vector.OffsetIsNormative || vector.Status != RLPStatus.Success)
        {
            Assert.AreEqual(vector.Offset, error.Offset, vector.Name);
        }
    }

    // ------------------------------------------------------------------ invalid limits

    [TestMethod]
    [DataRow(0, 32, 8192)]
    [DataRow(-1, 32, 8192)]
    [DataRow(1024, 0, 8192)]
    [DataRow(1024, -1, 8192)]
    [DataRow(1024, 65, 8192)]
    [DataRow(1024, int.MaxValue, 8192)]
    [DataRow(1024, 32, 0)]
    [DataRow(1024, 32, -1)]
    [DataRow(0, 0, 0)]
    public void InvalidLimits_AreRejectedBeforeTheInputIsRead(int maxBytes, int maxDepth, int maxItems)
    {
        var limits = new RLPDecodeLimits(maxBytes, maxDepth, maxItems);
        Assert.IsFalse(limits.IsValid);

        foreach (var input in new[] { Array.Empty<byte>(), H("80"), H("81 7f"), H("c8 83 63 61 74 83 64 6f 67 00") })
        {
            var status = RLPData.TryDecodeComplete(input, in limits, out var document, out var error);

            Assert.AreEqual(RLPStatus.InvalidArgument, status);
            Assert.AreEqual(new RLPError(RLPStatus.InvalidArgument, -1), error);
            Assert.IsFalse(document.IsValid);
        }
    }

    [TestMethod]
    public void DefaultLimitsStruct_IsInvalid_AndTheNamedDefaultIsValid()
    {
        RLPDecodeLimits zero = default;

        Assert.AreEqual(RLPStatus.InvalidArgument, RLPData.TryDecodeComplete(H("80"), in zero, out _, out var error));
        Assert.AreEqual(-1, error.Offset);
        Assert.IsTrue(RLPDecodeLimits.Default.IsValid);
        Assert.AreEqual(1024 * 1024, RLPDecodeLimits.Default.MaxEncodedBytes);
        Assert.AreEqual(32, RLPDecodeLimits.Default.MaxListDepth);
        Assert.AreEqual(8192, RLPDecodeLimits.Default.MaxItems);
    }

    [TestMethod]
    public void ExtremeValidLimits_AreAccepted()
    {
        var limits = new RLPDecodeLimits(int.MaxValue, RLPDecodeLimits.HardMaxListDepth, int.MaxValue);

        Assert.IsTrue(limits.IsValid);
        Assert.AreEqual(RLPStatus.Success, RLPData.TryDecodeComplete(H("c2 80 01"), in limits, out var document, out _));
        Assert.AreEqual(3, document.EncodedLength);
    }

    // ------------------------------------------------------------------ MaxEncodedBytes

    [TestMethod]
    public void MaxEncodedBytes_LimitMinusOneEqualAndPlusOne()
    {
        var input = H("83 64 6f 67");

        Assert.AreEqual(RLPStatus.TooLarge, Decode(input, Limits(maxBytes: 3), out _, out var error));
        Assert.AreEqual(3, error.Offset);
        Assert.AreEqual(RLPStatus.Success, Decode(input, Limits(maxBytes: 4), out _, out _));
        Assert.AreEqual(RLPStatus.Success, Decode(input, Limits(maxBytes: 5), out _, out _));
    }

    [TestMethod]
    public void TooLarge_IsCheckedBeforeAnyContent()
    {
        // All of these are also malformed; the size budget wins and no content is examined.
        foreach (var hex in new[] { "81 7f 00 00", "bf ff ff ff ff ff ff ff ff", "c8 83", "f8 37 00 00" })
        {
            var input = H(hex);
            Assert.AreEqual(RLPStatus.TooLarge, Decode(input, Limits(maxBytes: input.Length - 1), out _, out var error), hex);
            Assert.AreEqual(input.Length - 1, error.Offset, hex);
        }
    }

    [TestMethod]
    public void EmptyInput_WithinTheBudget_IsEmptyInput()
    {
        Assert.AreEqual(RLPStatus.EmptyInput, Decode([], Limits(maxBytes: 1), out _, out var error));
        Assert.AreEqual(new RLPError(RLPStatus.EmptyInput, 0), error);
    }

    [TestMethod]
    public void DefaultBudget_AcceptsExactlyOneMebibyte_AndRejectsOneMoreByte()
    {
        // ba 0f ff fc + 0x0ffffc payload bytes = 4 + 1,048,572 = 1 MiB exactly.
        var atLimit = RlpReference.Concatenate(H("ba 0f ff fc"), Repeat(0x11, 0x0ffffc));
        var overLimit = RlpReference.Concatenate(H("ba 0f ff fd"), Repeat(0x11, 0x0ffffd));
        Assert.AreEqual(OneMebibyte, atLimit.Length);

        Assert.AreEqual(RLPStatus.Success, Decode(atLimit, RLPDecodeLimits.Default, out var document, out _));
        Assert.AreEqual(0x0ffffc, document.PayloadLength);
        Assert.AreEqual(RLPStatus.TooLarge, Decode(overLimit, RLPDecodeLimits.Default, out _, out var error));
        Assert.AreEqual(OneMebibyte, error.Offset);
    }

    // ------------------------------------------------------------------ MaxListDepth (stack bucket sizes 8 / 32 / 64)

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(31)]
    [DataRow(32)]
    [DataRow(33)]
    [DataRow(63)]
    [DataRow(64)]
    public void MaxListDepth_LimitMinusOneEqualAndPlusOne(int limit)
    {
        var within = NestedLists(limit, out _);
        var over = NestedLists(limit + 1, out var overOffsets);

        Assert.AreEqual(RLPStatus.Success, Decode(within, Limits(maxDepth: limit), out var document, out _), $"limit {limit}");
        Assert.AreEqual(RLPKind.List, document.Kind);
        if (limit > 1)
        {
            Assert.AreEqual(RLPStatus.Success, Decode(NestedLists(limit - 1, out _), Limits(maxDepth: limit), out _, out _));
        }

        Assert.AreEqual(RLPStatus.TooDeep, Decode(over, Limits(maxDepth: limit), out var rejected, out var error), $"limit {limit}");
        Assert.AreEqual(overOffsets[limit], error.Offset, "the first list over the limit");
        Assert.IsFalse(rejected.IsValid);
    }

    [TestMethod]
    public void MaxListDepth64_AcceptsExactly64Levels_Walkable()
    {
        var input = NestedLists(64, out var offsets);
        var document = DecodeOk(input, Limits(maxDepth: 64));

        var depth = 1;
        while (document.PayloadLength > 0)
        {
            Assert.AreEqual(offsets[depth - 1], document.EncodedRange.Offset);
            Assert.AreEqual(RLPStatus.Success, document.TryGetChildren(out var children));
            Assert.IsTrue(children.MoveNext());
            document = children.Current;
            Assert.IsFalse(children.MoveNext());
            depth++;
        }

        Assert.AreEqual(64, depth);
    }

    [TestMethod]
    public void ScalarRoot_NeverExceedsTheDepthLimit()
    {
        Assert.AreEqual(RLPStatus.Success, Decode(H("b8 38 aa*56"), Limits(maxDepth: 1), out _, out _));
        Assert.AreEqual(RLPStatus.Success, Decode(H("00"), Limits(maxDepth: 1, maxItems: 1), out _, out _));
    }

    [TestMethod]
    public void EmptyNestedList_CountsTowardDepth()
    {
        // c1 c0 is depth 2; limit 1 rejects it at the inner list.
        Assert.AreEqual(RLPStatus.TooDeep, Decode(H("c1 c0"), Limits(maxDepth: 1), out _, out var error));
        Assert.AreEqual(1, error.Offset);
        Assert.AreEqual(RLPStatus.Success, Decode(H("c1 c0"), Limits(maxDepth: 2), out _, out _));
    }

    // ------------------------------------------------------------------ MaxItems

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(55)]
    [DataRow(56)]
    [DataRow(57)]
    [DataRow(8192)]
    public void MaxItems_LimitMinusOneEqualAndPlusOne(int limit)
    {
        // A list of n literal children holds n + 1 items; the root counts.
        foreach (var children in new[] { limit - 2, limit - 1, limit })
        {
            if (children < 0)
            {
                continue;
            }

            var input = WrapInList(Repeat(0x01, children));
            var fits = children + 1 <= limit;
            var status = Decode(input, Limits(maxItems: limit), out _, out var error);

            Assert.AreEqual(fits ? RLPStatus.Success : RLPStatus.TooManyItems, status, $"limit {limit}, {children} children");
            if (!fits)
            {
                // The offending item is the first child past the budget.
                Assert.AreEqual(input.Length - children + limit - 1, error.Offset);
            }
        }
    }

    [TestMethod]
    public void MaxItems_CountsRootNestedListsAndStrings()
    {
        // c3 c2 80 80: root list, inner list, two empty strings = 4 items.
        var input = H("c3 c2 80 80");

        Assert.AreEqual(RLPStatus.TooManyItems, Decode(input, Limits(maxItems: 3), out _, out var error));
        Assert.AreEqual(3, error.Offset);
        Assert.AreEqual(RLPStatus.Success, Decode(input, Limits(maxItems: 4), out _, out _));
        Assert.AreEqual(RLPStatus.Success, Decode(H("83 64 6f 67"), Limits(maxItems: 1), out _, out _));
    }

    [TestMethod]
    public void ItemBudget_IsCheckedAfterTheHeaderParses_AndBeforeDepth()
    {
        Assert.AreEqual(RLPStatus.InvalidListBoundary, Decode(H("c1 82"), Limits(maxItems: 1), out _, out var boundary));
        Assert.AreEqual(1, boundary.Offset);
        Assert.AreEqual(RLPStatus.TooManyItems, Decode(H("c1 c0"), Limits(maxDepth: 1, maxItems: 1), out _, out var budget));
        Assert.AreEqual(1, budget.Offset);
        Assert.AreEqual(RLPStatus.TooDeep, Decode(H("c1 c0"), Limits(maxDepth: 1, maxItems: 2), out _, out var depth));
        Assert.AreEqual(1, depth.Offset);
    }

    [TestMethod]
    public void FirstErrorInDocumentOrderWins()
    {
        // The depth violation (offset 2) precedes the malformed sibling later in the document.
        Assert.AreEqual(RLPStatus.TooDeep, Decode(H("c5 c1 c0 83 01 02"), Limits(maxDepth: 2), out _, out var error));
        Assert.AreEqual(2, error.Offset);
        Assert.AreEqual(RLPStatus.InvalidListBoundary, Decode(H("c5 c1 c0 83 01 02"), Limits(maxDepth: 32), out _, out error));
        Assert.AreEqual(3, error.Offset);
    }

    // ------------------------------------------------------------------ results

    [TestMethod]
    public void Success_LeavesErrorDefault_AndFailureLeavesNoDocument()
    {
        Assert.AreEqual(RLPStatus.Success, Decode(H("c0"), Limits(), out var good, out var goodError));
        Assert.AreEqual(default(RLPError), goodError);
        Assert.IsTrue(good.IsValid);

        Assert.AreEqual(RLPStatus.TrailingData, Decode(H("c0 c0"), Limits(), out var bad, out var badError));
        Assert.AreNotEqual(default(RLPError), badError);
        Assert.IsFalse(bad.IsValid);
        Assert.IsTrue(bad.Encoded.IsEmpty);
        Assert.AreEqual(new RLPRange(0, 0), bad.EncodedRange);
    }
}
