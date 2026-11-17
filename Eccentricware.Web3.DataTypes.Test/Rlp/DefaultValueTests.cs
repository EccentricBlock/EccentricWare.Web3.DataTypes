using System.Runtime.CompilerServices;
using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>default(RLPData), default(RLPChildrenIndex), default(RLPChildEnumerator) and enumerator misuse never look like valid data.</summary>
[TestClass]
public sealed class DefaultValueTests
{
    [TestMethod]
    public void DefaultRlpData_IsInvalid_WithEmptySpansAndZeroRanges()
    {
        RLPData none = default;

        Assert.IsFalse(none.IsValid);
        Assert.AreEqual(RLPKind.None, none.Kind);
        Assert.IsTrue(none.Encoded.IsEmpty);
        Assert.IsTrue(none.Payload.IsEmpty);
        Assert.AreEqual(new RLPRange(0, 0), none.EncodedRange);
        Assert.AreEqual(new RLPRange(0, 0), none.PayloadRange);
        Assert.AreEqual(0, none.EncodedLength);
        Assert.AreEqual(0, none.PayloadLength);
    }

    [TestMethod]
    public void DefaultRlpData_RejectsEveryOperation_WithInitialisedOutputs()
    {
        RLPData none = default;
        var workspace = new RLPRange[3];
        Array.Fill(workspace, new RLPRange(-7, -7));
        var destination = new byte[4];
        Array.Fill(destination, (byte)0xee);

        Assert.AreEqual(RLPStatus.InvalidArgument, none.TryGetChildren(out var children));
        Assert.IsFalse(children.MoveNext());

        Assert.AreEqual(RLPStatus.InvalidArgument, none.TryIndexDirectChildren(workspace, out var index, out var requiredCount));
        Assert.IsFalse(index.IsValid);
        Assert.AreEqual(0, requiredCount);
        Assert.IsTrue(workspace.All(r => r == new RLPRange(-7, -7)), "workspace untouched");

        Assert.AreEqual(RLPStatus.InvalidArgument, none.TryGetUInt64(out var value));
        Assert.AreEqual(0UL, value);

        Assert.AreEqual(RLPStatus.InvalidArgument, none.TryCopyEncodedTo(destination, out var written));
        Assert.AreEqual(0, written);
        Assert.IsTrue(destination.All(b => b == 0xee), "destination untouched");
    }

    [TestMethod]
    public void DefaultRlpChildrenIndex_IsInvalid_AndRejectsLookups()
    {
        RLPChildrenIndex none = default;

        Assert.IsFalse(none.IsValid);
        Assert.AreEqual(0, none.Count);
        Assert.AreEqual(RLPStatus.InvalidArgument, none.TryGetAt(0, out var child));
        Assert.IsFalse(child.IsValid);
        Assert.AreEqual(RLPStatus.InvalidArgument, none.TryGetAt(-1, out _));
        Assert.AreEqual(RLPStatus.InvalidArgument, none.TryGetAt(int.MaxValue, out _));
    }

    [TestMethod]
    public void FailedDecode_ProducesTheSameInvalidValueAsDefault()
    {
        Assert.AreEqual(RLPStatus.NonCanonicalEncoding, Decode(H("81 00"), Limits(), out var failed, out _));

        Assert.IsFalse(failed.IsValid);
        Assert.AreEqual(RLPKind.None, failed.Kind);
        Assert.IsTrue(failed.Encoded.IsEmpty);
        Assert.IsTrue(failed.Payload.IsEmpty);
        Assert.AreEqual(RLPStatus.InvalidArgument, failed.TryGetChildren(out _));
    }

    [TestMethod]
    public void ValidDecode_IsValid_WithANonNoneKind()
    {
        Assert.IsTrue(DecodeOk(H("c0")).IsValid);
        Assert.IsTrue(DecodeOk(H("80")).IsValid);
        Assert.AreNotEqual(RLPKind.None, DecodeOk(H("00")).Kind);
    }

    [TestMethod]
    public void EnumeratorCurrent_BeforeTheFirstMoveNext_ThrowsInvalidOperation()
    {
        var root = DecodeOk(H("c2 01 02"));
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));

        Assert.IsTrue(CurrentThrows(children));
    }

    [TestMethod]
    public void EnumeratorCurrent_AfterMoveNextReturnedFalse_ThrowsInvalidOperation()
    {
        var root = DecodeOk(H("c2 01 02"));
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));

        Assert.IsTrue(children.MoveNext());
        Assert.IsFalse(CurrentThrows(children));
        Assert.IsTrue(children.MoveNext());
        Assert.IsFalse(CurrentThrows(children));
        Assert.IsFalse(children.MoveNext());
        Assert.IsTrue(CurrentThrows(children));
        Assert.IsFalse(children.MoveNext());
        Assert.IsTrue(CurrentThrows(children));
    }

    [TestMethod]
    public void EnumeratorCurrent_OnAnEmptyList_ThrowsBeforeAndAfterMoveNext()
    {
        var root = DecodeOk(H("c0"));
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));

        Assert.IsTrue(CurrentThrows(children));
        Assert.IsFalse(children.MoveNext());
        Assert.IsTrue(CurrentThrows(children));
    }

    [TestMethod]
    public void DefaultEnumerator_MoveNextIsFalse_AndCurrentThrows()
    {
        RLPChildEnumerator children = default;

        Assert.IsTrue(CurrentThrows(children));
        Assert.IsFalse(children.MoveNext());
        Assert.IsTrue(CurrentThrows(children));
    }

    [TestMethod]
    public void RlpData_FitsInThirtyTwoBytes_AndTheIndexAndEnumeratorStayCompact()
    {
        Assert.IsLessThanOrEqualTo(32, Unsafe.SizeOf<RLPData>());
        Assert.IsLessThanOrEqualTo(40, Unsafe.SizeOf<RLPChildEnumerator>());
        Assert.IsLessThanOrEqualTo(40, Unsafe.SizeOf<RLPChildrenIndex>());
    }

    [TestMethod]
    public void RlpData_HasNoPublicConstructorOtherThanTheImplicitDefault()
    {
        Assert.AreEqual(0, typeof(RLPData).GetConstructors().Length);
        Assert.AreEqual(0, typeof(RLPChildrenIndex).GetConstructors().Length);
        Assert.AreEqual(0, typeof(RLPChildEnumerator).GetConstructors().Length);
    }

    private static bool CurrentThrows(RLPChildEnumerator children)
    {
        try
        {
            _ = children.Current;
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
