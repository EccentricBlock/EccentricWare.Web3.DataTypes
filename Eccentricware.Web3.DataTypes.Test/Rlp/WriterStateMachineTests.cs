using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>Framed writer state machine (spec 7.3, 12.3 items 1 to 6): root discipline, list framing, frame capacity, recoverability.</summary>
[TestClass]
public sealed class WriterStateMachineTests
{
    private static readonly byte[] Cat = [0x63, 0x61, 0x74];
    private static readonly byte[] Dog = [0x64, 0x6f, 0x67];

    // ------------------------------------------------------------------ default and construction

    [TestMethod]
    public void DefaultWriter_RejectsEveryOperation_WithWriterStateInvalid()
    {
        var item = DecodeOk(H("83 64 6f 67"));
        var writer = default(RLPWriter);

        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryBeginList(0));
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryWriteByteString(Cat));
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryWriteByteString([]));
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryWriteUInt64(1));
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryWriteUnsignedMagnitude([1]));
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryWriteEncoded(in item));
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryEndList());
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryComplete(out var written));
        Assert.AreEqual(0, written);
    }

    [TestMethod]
    public void DefaultWriter_ChecksStateBeforeArguments()
    {
        var writer = default(RLPWriter);

        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryBeginList(-1));
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryWriteUnsignedMagnitude([0, 1]));
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryWriteEncoded(default));
    }

    [TestMethod]
    public void ExplicitlyConstructedWriter_OverEmptyBuffers_IsValid_AndFailsOnCapacity()
    {
        var writer = new RLPWriter(Span<byte>.Empty, Span<int>.Empty);

        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryWriteByteString([]));
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryWriteUInt64(0));
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryBeginList(0));
        Assert.AreEqual(RLPStatus.EmptyInput, writer.TryComplete(out _));
        Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryEndList());
    }

    // ------------------------------------------------------------------ 12.3 item 1: empty root, one root, second root

    [TestMethod]
    public void EmptyRoot_TryCompleteIsEmptyInput_AndTheWriterStaysUsable()
    {
        var destination = Filled(8);
        var writer = new RLPWriter(destination, new int[4]);

        Assert.AreEqual(RLPStatus.EmptyInput, writer.TryComplete(out var written));
        Assert.AreEqual(0, written);
        Assert.IsTrue(IsFilled(destination));

        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(0));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out written));
        Assert.AreEqual(1, written);
        Assert.AreEqual((byte)0x80, destination[0]);
    }

    [TestMethod]
    public void OneRoot_EachItemKind_CompletesWithTheExactEncoding()
    {
        var item = DecodeOk(H("c2 01 02"));
        var cases = new (string Name, Func<byte[], string> Run)[]
        {
            ("string", d => Root(d, (ref RLPWriter w) => w.TryWriteByteString(Dog))),
            ("empty string", d => Root(d, (ref RLPWriter w) => w.TryWriteByteString([]))),
            ("uint64", d => Root(d, (ref RLPWriter w) => w.TryWriteUInt64(256))),
            ("magnitude", d => Root(d, (ref RLPWriter w) => w.TryWriteUnsignedMagnitude([1, 0]))),
        };
        var expected = new[] { "83 64 6f 67", "80", "82 01 00", "82 01 00" };

        for (var i = 0; i < cases.Length; i++)
        {
            Assert.AreEqual(RlpReference.ToHex(H(expected[i]), true), cases[i].Run(Filled(16)), cases[i].Name);
        }

        // TryWriteEncoded as the only root.
        var destination = Filled(16);
        var writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteEncoded(in item));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual("c2 01 02", RlpReference.ToHex(destination.AsSpan(0, written), true));
    }

    private delegate RLPStatus WriterAction(ref RLPWriter writer);

    private static string Root(byte[] destination, WriterAction action)
    {
        var writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, action(ref writer));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        return RlpReference.ToHex(destination.AsSpan(0, written), true);
    }

    [TestMethod]
    public void SecondRoot_AfterAScalar_IsExtraRoot_ForEveryItemOperation_WithoutMutation()
    {
        var item = DecodeOk(H("c0"));
        var destination = Filled(32);
        var writer = new RLPWriter(destination, new int[4]);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(Cat));
        var snapshot = (byte[])destination.Clone();

        Assert.AreEqual(RLPStatus.ExtraRoot, writer.TryWriteByteString(Dog));
        Assert.AreEqual(RLPStatus.ExtraRoot, writer.TryWriteUInt64(7));
        Assert.AreEqual(RLPStatus.ExtraRoot, writer.TryWriteUnsignedMagnitude([7]));
        Assert.AreEqual(RLPStatus.ExtraRoot, writer.TryWriteEncoded(in item));
        Assert.AreEqual(RLPStatus.ExtraRoot, writer.TryBeginList(0));

        CollectionAssert.AreEqual(snapshot, destination);
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(4, written);
    }

    [TestMethod]
    public void SecondRoot_AfterAClosedList_IsExtraRoot()
    {
        var destination = Filled(32);
        var writer = new RLPWriter(destination, new int[4]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(0));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        var snapshot = (byte[])destination.Clone();

        Assert.AreEqual(RLPStatus.ExtraRoot, writer.TryWriteUInt64(1));
        Assert.AreEqual(RLPStatus.ExtraRoot, writer.TryBeginList(0));

        CollectionAssert.AreEqual(snapshot, destination);
        Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryEndList(), "no open list after the root closed");
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(1, written);
        Assert.AreEqual((byte)0xc0, destination[0]);
    }

    [TestMethod]
    public void ExtraRoot_IsReportedBeforeArgumentSizeAndCapacityErrors()
    {
        var destination = Filled(4);
        var writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(5));

        Assert.AreEqual(RLPStatus.ExtraRoot, writer.TryBeginList(-1));
        Assert.AreEqual(RLPStatus.ExtraRoot, writer.TryBeginList(int.MaxValue));
        Assert.AreEqual(RLPStatus.ExtraRoot, writer.TryWriteUnsignedMagnitude([0, 1]));
        Assert.AreEqual(RLPStatus.ExtraRoot, writer.TryWriteEncoded(default));
        Assert.AreEqual(RLPStatus.ExtraRoot, writer.TryWriteByteString(new byte[100]));
    }

    // ------------------------------------------------------------------ 12.3 item 2: exact payload, premature end, shortfall, overrun

    [TestMethod]
    public void SpecExample_CatDog_EncodesAndCompletesWithNine()
    {
        var destination = Filled(16);
        var writer = new RLPWriter(destination, new int[2]);

        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetByteStringLength(Cat, out var catLength));
        Assert.AreEqual(4, catLength);
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(8, out var listLength));
        Assert.AreEqual(9, listLength);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(8));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(Cat));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(Dog));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));

        Assert.AreEqual(9, written);
        Assert.AreEqual("c8 83 63 61 74 83 64 6f 67", RlpReference.ToHex(destination.AsSpan(0, written), true));
        Assert.IsTrue(IsFilled(destination.AsSpan(9)));
    }

    [TestMethod]
    public void EmptyList_BeginZeroEmitsC0_EndSucceedsImmediately_CompleteReturnsOne()
    {
        var destination = Filled(4);
        var writer = new RLPWriter(destination, new int[1]);

        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(0));
        Assert.AreEqual((byte)0xc0, destination[0]);
        Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryComplete(out _), "the frame is still open");
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(1, written);
    }

    [TestMethod]
    public void EmptyListChild_AnyChildOfAZeroLengthList_IsInvalidListBoundary()
    {
        var item = DecodeOk(H("80"));
        var destination = Filled(16);
        var writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(0));
        var snapshot = (byte[])destination.Clone();

        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteByteString([]));
        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteUInt64(0));
        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteUnsignedMagnitude([]));
        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteEncoded(in item));
        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryBeginList(0));

        CollectionAssert.AreEqual(snapshot, destination);
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
    }

    [TestMethod]
    public void PrematureEndList_IsUnbalanced_DoesNotPopTheFrame_AndTheCallerCanFinish()
    {
        var destination = Filled(16);
        var writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(8));
        var snapshot = (byte[])destination.Clone();

        Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryEndList());
        Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryEndList(), "repeating the failed call changes nothing");
        CollectionAssert.AreEqual(snapshot, destination);

        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(Cat));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(Dog));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual("c8 83 63 61 74 83 64 6f 67", RlpReference.ToHex(destination.AsSpan(0, written), true));
    }

    [TestMethod]
    public void InsufficientChildBytes_EndAndCompleteFail_UntilTheShortfallIsWritten()
    {
        var destination = Filled(16);
        var writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(8));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(Cat));

        Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryEndList());
        Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryComplete(out var written));
        Assert.AreEqual(0, written);

        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(Dog));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out written));
        Assert.AreEqual(9, written);
    }

    [TestMethod]
    public void AttemptedOverrun_ChildBeyondTheAnnouncedPayload_IsInvalidListBoundary_WithoutMutation()
    {
        var destination = Filled(32);
        var writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(4));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(Cat));
        var snapshot = (byte[])destination.Clone();

        // The destination has plenty of room, so only the parent limit can reject this.
        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteByteString(Dog));
        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteUInt64(1));
        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryBeginList(0));

        CollectionAssert.AreEqual(snapshot, destination);
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(5, written);
    }

    [TestMethod]
    public void SingleChildLargerThanTheAnnouncedPayload_ByOneByte_IsRejected()
    {
        var destination = Filled(32);
        var writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(3));

        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteByteString(Cat)); // encodes to 4 bytes
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString([0x82, 0x83])); // 3 bytes: 82 82 83 fits exactly
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
    }

    [TestMethod]
    public void NestedList_AnnouncedBeyondTheParentRemainder_IsInvalidListBoundary_AndConsumesNoFrame()
    {
        var destination = Filled(32);
        var frames = new int[2];
        var writer = new RLPWriter(destination, frames);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(4)); // room for 4 bytes
        var snapshot = (byte[])destination.Clone();

        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryBeginList(4)); // c4 + 4 = 5 bytes
        CollectionAssert.AreEqual(snapshot, destination);

        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(3)); // c3 + 3 = 4 bytes: exactly fits
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(Cat[..2])); // 82 63 61 = 3 bytes
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(5, written);
        Assert.AreEqual("c4 c3 82 63 61", RlpReference.ToHex(destination.AsSpan(0, written), true));
    }

    [TestMethod]
    public void LongFormListHeader_IsWrittenAndFrameEndIncludesTheHeader()
    {
        // 56 literal children -> payload 56 -> f8 38.
        var destination = Filled(80);
        var writer = new RLPWriter(destination, new int[1]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(56));
        Assert.AreEqual((byte)0xf8, destination[0]);
        Assert.AreEqual((byte)0x38, destination[1]);
        for (var i = 0; i < 56; i++)
        {
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(1), $"child {i}");
        }

        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteUInt64(1), "the 57th child overruns");
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(58, written);
        Assert.IsTrue(IsFilled(destination.AsSpan(58)));
    }

    // ------------------------------------------------------------------ 12.3 item 3: frame capacity

    /// <summary>Nests <paramref name="lists"/> lists; the innermost announces one spare byte (<c>c0</c>) so one more list fits by size.</summary>
    private static int[] NestedPayloadLengths(int lists)
    {
        var payloads = new int[lists];
        payloads[lists - 1] = 1;
        for (var i = lists - 2; i >= 0; i--)
        {
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(payloads[i + 1], out payloads[i]));
        }

        return payloads;
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(32)]
    public void FrameWorkspace_AcceptsExactlyItsLengthInNestedLists_AndRejectsOneBeyondWithTooDeep(int frameSlots)
    {
        var destination = Filled(256);
        var writer = new RLPWriter(destination, new int[frameSlots]);

        if (frameSlots == 0)
        {
            // Zero slots: a list of any kind is TooDeep, scalar roots still work.
            Assert.AreEqual(RLPStatus.TooDeep, writer.TryBeginList(0));
            Assert.IsTrue(IsFilled(destination));
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(9));
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var scalarWritten));
            Assert.AreEqual(1, scalarWritten);
            return;
        }

        var payloads = NestedPayloadLengths(frameSlots);
        for (var level = 0; level < frameSlots; level++)
        {
            Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(payloads[level]), $"level {level}");
        }

        var snapshot = (byte[])destination.Clone();
        Assert.AreEqual(RLPStatus.TooDeep, writer.TryBeginList(0), "one beyond capacity");
        CollectionAssert.AreEqual(snapshot, destination);

        // Scalars still fit at full depth, and the whole stack closes cleanly.
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(5));
        for (var level = 0; level < frameSlots; level++)
        {
            Assert.AreEqual(RLPStatus.Success, writer.TryEndList(), $"close {level}");
        }

        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(frameSlots + 1, written, "one header byte per list plus the literal");
        var decoded = DecodeOk(destination.AsSpan(0, written).ToArray(), Limits(maxDepth: 64));
        Assert.AreEqual(written, decoded.EncodedLength);
    }

    [TestMethod]
    public void FrameSlotCheck_FollowsTheParentLimitCheck_AndIsReusedAfterEndList()
    {
        var destination = Filled(64);
        var writer = new RLPWriter(destination, new int[1]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(0)); // no room for any child and no free slot
        var snapshot = (byte[])destination.Clone();

        // Both limits apply: the parent limit is reported first.
        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryBeginList(0));
        CollectionAssert.AreEqual(snapshot, destination);

        Assert.AreEqual(RLPStatus.Success, writer.TryEndList()); // frees the slot, completes the root
        Assert.AreEqual(RLPStatus.ExtraRoot, writer.TryBeginList(0));
    }

    [TestMethod]
    public void FrameSlot_IsFreedByEndList_SoSiblingListsReuseIt()
    {
        var destination = Filled(64);
        var writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(6));
        for (var i = 0; i < 3; i++)
        {
            Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(1), $"sibling {i}");
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(1));
            Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        }

        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual("c6 c1 01 c1 01 c1 01", RlpReference.ToHex(destination.AsSpan(0, written), true));
    }

    // ------------------------------------------------------------------ 12.3 item 4: atomic failure and retry

    [TestMethod]
    public void FailedWrites_LeaveDestinationAndWriterUnchanged_AndRetryWithValidDataSucceeds()
    {
        var destination = Filled(32);
        var writer = new RLPWriter(destination, new int[4]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(8));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(Cat));
        var snapshot = (byte[])destination.Clone();

        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteByteString(new byte[10]));
        Assert.AreEqual(RLPStatus.NonCanonicalInteger, writer.TryWriteUnsignedMagnitude([0, 1]));
        Assert.AreEqual(RLPStatus.InvalidArgument, writer.TryWriteEncoded(default));
        Assert.AreEqual(RLPStatus.InvalidArgument, writer.TryBeginList(-5));
        Assert.AreEqual(RLPStatus.LengthOverflow, writer.TryBeginList(int.MaxValue));
        Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryEndList());
        Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryComplete(out _));
        CollectionAssert.AreEqual(snapshot, destination);

        // The cursor is where it was: the next valid child lands directly after "cat".
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(Dog));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual("c8 83 63 61 74 83 64 6f 67", RlpReference.ToHex(destination.AsSpan(0, written), true));
    }

    [TestMethod]
    public void RootDestinationTooSmall_ForEveryItemKind_LeavesEverythingUnchanged_AndAnEnlargedRetryIsNotNeeded()
    {
        var item = DecodeOk(H("c8 83 63 61 74 83 64 6f 67"));
        var destination = Filled(8); // every item below needs 9 or more
        var writer = new RLPWriter(destination, new int[4]);

        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryWriteEncoded(in item));
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryBeginList(8));
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryWriteByteString(new byte[8]));
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryWriteUInt64(ulong.MaxValue));
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryWriteUnsignedMagnitude(new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 }));
        Assert.IsTrue(IsFilled(destination));

        // Still a fresh writer in effect: something small fits.
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(Dog));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(4, written);
    }

    // ------------------------------------------------------------------ 12.3 item 5: TryComplete

    [TestMethod]
    public void TryComplete_FailsWithAnyOpenFrame_AtEveryDepth_AndSucceedsOnlyWhenAllAreClosed()
    {
        const int depth = 5;
        var destination = Filled(64);
        var writer = new RLPWriter(destination, new int[8]);
        int[] payloads = [4, 3, 2, 1, 0];

        for (var level = 0; level < depth; level++)
        {
            Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(payloads[level]));
            Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryComplete(out var written), $"open frames: {level + 1}");
            Assert.AreEqual(0, written);
        }

        for (var level = 0; level < depth; level++)
        {
            Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryComplete(out _), $"still open: {depth - level}");
            Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        }

        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var total));
        Assert.AreEqual(depth, total);
        Assert.AreEqual("c4 c3 c2 c1 c0", RlpReference.ToHex(destination.AsSpan(0, total), true));
    }

    [TestMethod]
    public void AfterSuccessfulComplete_EveryOperationIsWriterStateInvalid_AndNothingChanges()
    {
        var item = DecodeOk(H("01"));
        var destination = Filled(16);
        var writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(1));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(1));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(2, written);
        var snapshot = (byte[])destination.Clone();

        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryBeginList(0));
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryWriteByteString(Cat));
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryWriteUInt64(1));
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryWriteUnsignedMagnitude([1]));
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryWriteEncoded(in item));
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryEndList());
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryComplete(out var again));
        Assert.AreEqual(0, again);
        CollectionAssert.AreEqual(snapshot, destination);
    }

    [TestMethod]
    public void TryComplete_Failure_DoesNotChangeState_AndWrittenIsZero()
    {
        var destination = Filled(16);
        var writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(1));
        Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryComplete(out var written));
        Assert.AreEqual(0, written);

        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(2));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out written));
        Assert.AreEqual(2, written);
    }

    [TestMethod]
    public void TryEndList_OnReadyAndOnWriterWithOnlyAScalarRoot_IsUnbalanced()
    {
        var writer = new RLPWriter(new byte[8], new int[2]);
        Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryEndList());

        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(3));
        Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out _));
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryEndList());
    }

    // ------------------------------------------------------------------ 12.3 item 6: invalid magnitudes

    [TestMethod]
    public void InvalidMagnitudes_ProduceNoMutation_AndTheWriterRemainsUsable()
    {
        foreach (var bad in new[] { new byte[] { 0 }, [0, 0], [0, 1], [0, 0x7f], [0, 0xff, 0xff] })
        {
            var destination = Filled(16);
            var writer = new RLPWriter(destination, new int[2]);

            Assert.AreEqual(RLPStatus.NonCanonicalInteger, writer.TryWriteUnsignedMagnitude(bad), RlpReference.ToHex(bad));

            Assert.IsTrue(IsFilled(destination), RlpReference.ToHex(bad));
            Assert.AreEqual(RLPStatus.EmptyInput, writer.TryComplete(out _));
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUnsignedMagnitude([1, 0]));
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
            Assert.AreEqual(3, written);
        }
    }

    [TestMethod]
    public void NonCanonicalMagnitude_IsReportedBeforeSizeAndCapacity_AtTheRootAndInsideAList()
    {
        var writer = new RLPWriter(Span<byte>.Empty, new int[2]);
        Assert.AreEqual(RLPStatus.NonCanonicalInteger, writer.TryWriteUnsignedMagnitude([0, 1]));

        var destination = Filled(8);
        writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(0));
        Assert.AreEqual(RLPStatus.NonCanonicalInteger, writer.TryWriteUnsignedMagnitude([0, 1]), "argument errors precede the parent limit");
    }

    // ------------------------------------------------------------------ sizes and argument order

    [TestMethod]
    public void TryBeginList_ArgumentAndSizeErrors_AreInvalidArgumentThenLengthOverflowThenCapacity()
    {
        var destination = Filled(8);
        var writer = new RLPWriter(destination, new int[2]);

        Assert.AreEqual(RLPStatus.InvalidArgument, writer.TryBeginList(-1));
        Assert.AreEqual(RLPStatus.InvalidArgument, writer.TryBeginList(int.MinValue));
        Assert.AreEqual(RLPStatus.LengthOverflow, writer.TryBeginList(int.MaxValue));
        Assert.AreEqual(RLPStatus.LengthOverflow, writer.TryBeginList(int.MaxValue - 4)); // 5-byte header + payload > int.MaxValue
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryBeginList(int.MaxValue - 5)); // total == int.MaxValue
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryBeginList(8)); // 9 bytes into 8
        Assert.IsTrue(IsFilled(destination));

        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(7)); // 8 bytes exactly
    }

    [TestMethod]
    public void TryBeginList_PayloadAtTheLongFormBoundary_SizesTheHeaderFromTheAnnouncedLength()
    {
        // 55 -> c0+55 header (1 byte); 56 -> f8 38; 255 -> f8 ff; 256 -> f9 01 00.
        foreach (var (payload, header) in new[] { (55, "f7"), (56, "f8 38"), (255, "f8 ff"), (256, "f9 01 00"), (65535, "f9 ff ff"), (65536, "fa 01 00 00") })
        {
            var headerBytes = H(header);
            var destination = Filled(headerBytes.Length + payload + 2);
            var writer = new RLPWriter(destination, new int[1]);

            Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(payload), header);

            Assert.IsTrue(destination.AsSpan(0, headerBytes.Length).SequenceEqual(headerBytes), header);
            Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryEndList(), header);
        }
    }

    [TestMethod]
    public void StringWrites_InsideAList_AtTheParentLimitBoundary_55And56()
    {
        // A list payload of exactly 56 bytes holding one 55-byte string (b7 + 55 = 56 bytes).
        var destination = Filled(80);
        var writer = new RLPWriter(destination, new int[1]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(56));
        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteByteString(new byte[56])); // b8 38 + 56 = 58
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(Repeat(0xaa, 55)));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(58, written);
        Assert.AreEqual(0xb7, destination[2]);
    }

    [TestMethod]
    public void WriteEncoded_OfAChildAndOfLists_AppendsVerbatim_AndRejectsDefaultWithoutMutation()
    {
        var source = DecodeOk(H("c5 83 64 6f 67 01"));
        Assert.AreEqual(RLPStatus.Success, source.TryGetChildren(out var children));
        Assert.IsTrue(children.MoveNext());
        var dog = children.Current;

        var destination = Filled(32);
        var writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(10));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteEncoded(in dog));
        var snapshot = (byte[])destination.Clone();
        Assert.AreEqual(RLPStatus.InvalidArgument, writer.TryWriteEncoded(default));
        CollectionAssert.AreEqual(snapshot, destination);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteEncoded(in source)); // the whole 6-byte list: 4 + 6 = 10
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual("ca 83 64 6f 67 c5 83 64 6f 67 01", RlpReference.ToHex(destination.AsSpan(0, written), true));
    }

    [TestMethod]
    public void WriteEncoded_BeyondTheAnnouncedPayload_IsInvalidListBoundary()
    {
        var source = DecodeOk(H("c5 83 64 6f 67 01"));
        var destination = Filled(32);
        var writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(9));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(Dog));
        var snapshot = (byte[])destination.Clone();

        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteEncoded(in source)); // 6 bytes into the remaining 5

        CollectionAssert.AreEqual(snapshot, destination);
    }
}
