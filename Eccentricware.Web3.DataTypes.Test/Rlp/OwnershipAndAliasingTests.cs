using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Source/destination aliasing and buffer ownership on the writing side (spec 3.3, 7.2 step 3, 7.5, 12.3 item 7).
/// Only the region that would be written is compared with the source; an overlap is rejected before any byte changes.
/// </summary>
[TestClass]
public sealed class OwnershipAndAliasingTests
{
    // ------------------------------------------------------------------ RLPEncoder scalar writes

    [TestMethod]
    public void EncoderByteString_SourceInsideTheOutputRegion_IsOverlappingBuffers_BeforeAnyWrite()
    {
        // "abc" (3 bytes) needs a 4-byte region [0,4). A source at offsets 0..3 shares a byte with it.
        for (var offset = 0; offset <= 3; offset++)
        {
            var buffer = Filled(16);
            buffer[offset] = 0x61;
            buffer[offset + 1] = 0x62;
            buffer[offset + 2] = 0x63;
            var snapshot = (byte[])buffer.Clone();

            var status = RLPEncoder.TryWriteByteString(buffer.AsSpan(offset, 3), buffer, out var written);

            Assert.AreEqual(RLPStatus.OverlappingBuffers, status, $"offset {offset}");
            Assert.AreEqual(0, written);
            CollectionAssert.AreEqual(snapshot, buffer, $"offset {offset}");
        }
    }

    [TestMethod]
    public void EncoderByteString_SourceDirectlyAfterTheOutputRegion_DoesNotOverlap_AndIsPreserved()
    {
        var buffer = Filled(16);
        buffer[4] = 0x61;
        buffer[5] = 0x62;
        buffer[6] = 0x63;

        var status = RLPEncoder.TryWriteByteString(buffer.AsSpan(4, 3), buffer, out var written);

        Assert.AreEqual(RLPStatus.Success, status);
        Assert.AreEqual(4, written);
        Assert.AreEqual("83 61 62 63 61 62 63", RlpReference.ToHex(buffer.AsSpan(0, 7), true));
    }

    [TestMethod]
    public void EncoderByteString_OnlyTheRegionThatWouldBeWritten_IsComparedWithTheSource()
    {
        // The destination is 16 bytes, but the item needs 4: a source at [8,11) is outside [0,4).
        var buffer = Filled(16);
        buffer.AsSpan(8, 3).Fill(0x41);

        var status = RLPEncoder.TryWriteByteString(buffer.AsSpan(8, 3), buffer, out var written);

        Assert.AreEqual(RLPStatus.Success, status);
        Assert.AreEqual(4, written);
        Assert.AreEqual("83 41 41 41", RlpReference.ToHex(buffer.AsSpan(0, 4), true));
    }

    [TestMethod]
    public void EncoderByteString_SourceBeforeTheDestination_WithAGap_Succeeds()
    {
        var buffer = Filled(16);
        buffer.AsSpan(0, 3).Fill(0x41);

        var status = RLPEncoder.TryWriteByteString(buffer.AsSpan(0, 3), buffer.AsSpan(3), out var written);

        Assert.AreEqual(RLPStatus.Success, status);
        Assert.AreEqual(4, written);
        Assert.AreEqual("41 41 41 83 41 41 41", RlpReference.ToHex(buffer.AsSpan(0, 7), true));
    }

    [TestMethod]
    public void EncoderByteString_LiteralSourceWrittenOntoItself_IsOverlappingBuffers()
    {
        // In-place "encoding" of a literal byte is still unsupported: the output region is the source byte.
        var buffer = Filled(4);
        buffer[0] = 0x05;

        Assert.AreEqual(RLPStatus.OverlappingBuffers, RLPEncoder.TryWriteByteString(buffer.AsSpan(0, 1), buffer, out var written));

        Assert.AreEqual(0, written);
        Assert.AreEqual((byte)0x05, buffer[0]);
    }

    [TestMethod]
    public void EncoderMagnitude_SourceInsideTheOutputRegion_IsOverlappingBuffers()
    {
        var buffer = Filled(16);
        buffer[1] = 0x01;
        buffer[2] = 0x00;
        var snapshot = (byte[])buffer.Clone();

        var status = RLPEncoder.TryWriteUnsignedMagnitude(buffer.AsSpan(1, 2), buffer, out var written);

        Assert.AreEqual(RLPStatus.OverlappingBuffers, status);
        Assert.AreEqual(0, written);
        CollectionAssert.AreEqual(snapshot, buffer);
    }

    [TestMethod]
    public void EncoderEmptySource_NeverOverlaps()
    {
        var buffer = Filled(4);

        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteByteString(buffer.AsSpan(0, 0), buffer, out var written));
        Assert.AreEqual(1, written);
        Assert.AreEqual((byte)0x80, buffer[0]);
    }

    // ------------------------------------------------------------------ RLPWriter scalar writes

    [TestMethod]
    public void WriterByteString_AtTheRoot_SourceInsideTheRegion_IsOverlappingBuffers_AndTheWriterIsUnchanged()
    {
        var buffer = Filled(16);
        buffer.AsSpan(2, 3).Fill(0x41);
        var snapshot = (byte[])buffer.Clone();
        var writer = new RLPWriter(buffer, new int[2]);

        Assert.AreEqual(RLPStatus.OverlappingBuffers, writer.TryWriteByteString(buffer.AsSpan(2, 3)));
        Assert.AreEqual(RLPStatus.OverlappingBuffers, writer.TryWriteUnsignedMagnitude(buffer.AsSpan(2, 3)));
        CollectionAssert.AreEqual(snapshot, buffer);

        // Still Ready: a clean source goes through at the same cursor.
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(7));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(1, written);
        Assert.AreEqual((byte)7, buffer[0]);
    }

    [TestMethod]
    public void WriterByteString_InsideAList_ComparesAgainstTheRegionAtTheCursorOnly()
    {
        var buffer = Filled(32);
        buffer.AsSpan(20, 3).Fill(0x41);
        var writer = new RLPWriter(buffer, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(10));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(1)); // cursor 2

        // Region [2,6): sources overlapping it are rejected, a source at [20,23) is fine.
        buffer.AsSpan(4, 3).Fill(0x42);
        var snapshot = (byte[])buffer.Clone();
        Assert.AreEqual(RLPStatus.OverlappingBuffers, writer.TryWriteByteString(buffer.AsSpan(4, 3)));
        Assert.AreEqual(RLPStatus.OverlappingBuffers, writer.TryWriteByteString(buffer.AsSpan(0, 3)));
        CollectionAssert.AreEqual(snapshot, buffer);

        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(buffer.AsSpan(20, 3)));
        Assert.AreEqual("83 41 41 41", RlpReference.ToHex(buffer.AsSpan(2, 4), true));
    }

    [TestMethod]
    public void WriterByteString_SourceAdjacentToTheRegion_IsAccepted()
    {
        var buffer = Filled(32);
        var writer = new RLPWriter(buffer, new int[1]);
        buffer.AsSpan(4, 3).Fill(0x43);

        // Region [0,4) ends exactly where the source begins.
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(buffer.AsSpan(4, 3)));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual("83 43 43 43 43 43 43", RlpReference.ToHex(buffer.AsSpan(0, 7), true));
        Assert.AreEqual(4, written);
    }

    // ------------------------------------------------------------------ TryWriteEncoded and TryCopyEncodedTo

    private const string CatDogHex = "c8 83 63 61 74 83 64 6f 67";

    [TestMethod]
    public void WriterEncoded_SourceBackedByTheDestination_OverlappingTheRegion_IsOverlappingBuffers()
    {
        var buffer = H(CatDogHex);
        var snapshot = (byte[])buffer.Clone();
        var root = DecodeOk(buffer);
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));
        Assert.IsTrue(children.MoveNext());
        var cat = children.Current; // [1,5)

        // Root writer at cursor 0: region [0,4) shares byte 1..3 with cat.
        var writer = new RLPWriter(buffer, new int[2]);
        Assert.AreEqual(RLPStatus.OverlappingBuffers, writer.TryWriteEncoded(in cat));
        // The whole document written onto itself.
        Assert.AreEqual(RLPStatus.OverlappingBuffers, writer.TryWriteEncoded(in root));

        CollectionAssert.AreEqual(snapshot, buffer);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(9), "the writer was not changed by the failed calls");
    }

    [TestMethod]
    public void WriterEncoded_SourceBeyondTheRegion_IsAccepted_AndTheSourceBytesAreIntact()
    {
        var buffer = H(CatDogHex);
        var root = DecodeOk(buffer);
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));
        Assert.IsTrue(children.MoveNext());
        Assert.IsTrue(children.MoveNext());
        var dog = children.Current; // [5,9)

        var writer = new RLPWriter(buffer, new int[2]);

        // Region [0,4) is disjoint from [5,9): allowed, and it does not damage the source.
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteEncoded(in dog));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(4, written);
        Assert.AreEqual("83 64 6f 67", RlpReference.ToHex(buffer.AsSpan(0, 4), true));
        Assert.AreEqual("83 64 6f 67", RlpReference.ToHex(buffer.AsSpan(5, 4), true));
    }

    [TestMethod]
    public void WriterEncoded_AndCopyEncodedTo_AgreeOnEveryDestinationOffset()
    {
        var original = H(CatDogHex);
        for (var childOrdinal = -1; childOrdinal < 2; childOrdinal++)
        {
            for (var offset = 0; offset <= original.Length; offset++)
            {
                // Two independent copies of the same document so each API sees a pristine buffer.
                var viaCopy = (byte[])original.Clone();
                var viaWriter = (byte[])original.Clone();
                var itemForCopy = Child(DecodeOk(viaCopy), childOrdinal);
                var itemForWriter = Child(DecodeOk(viaWriter), childOrdinal);

                var copyStatus = itemForCopy.TryCopyEncodedTo(viaCopy.AsSpan(offset), out _);
                var writer = new RLPWriter(viaWriter.AsSpan(offset), new int[1]);
                var writeStatus = writer.TryWriteEncoded(in itemForWriter);

                Assert.AreEqual(copyStatus, writeStatus, $"child {childOrdinal} at offset {offset}");
                CollectionAssert.AreEqual(viaCopy, viaWriter, $"child {childOrdinal} at offset {offset}");
            }
        }
    }

    private static RLPData Child(RLPData root, int ordinal)
    {
        if (ordinal < 0)
        {
            return root;
        }

        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));
        for (var i = 0; i <= ordinal; i++)
        {
            Assert.IsTrue(children.MoveNext());
        }

        return children.Current;
    }

    [TestMethod]
    public void WriterEncoded_FromAnotherBuffer_NeverOverlaps()
    {
        var source = H("c8 83 63 61 74 83 64 6f 67");
        var destination = Filled(16);
        var item = DecodeOk(source);
        var writer = new RLPWriter(destination, new int[1]);

        Assert.AreEqual(RLPStatus.Success, writer.TryWriteEncoded(in item));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual(source));
    }

    // ------------------------------------------------------------------ ownership and the documented hazards

    [TestMethod]
    public void Writer_WritesStraightIntoTheCallerDestination_AndNeverBeyondTheEncoding()
    {
        var destination = Filled(64);
        var guardedFrames = new int[6];
        Array.Fill(guardedFrames, int.MinValue);
        var writer = new RLPWriter(destination, guardedFrames);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(8));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString([0x63, 0x61, 0x74]));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString([0x64, 0x6f, 0x67]));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));

        Assert.AreEqual("c8 83 63 61 74 83 64 6f 67", RlpReference.ToHex(destination.AsSpan(0, written), true));
        Assert.IsTrue(IsFilled(destination.AsSpan(written)));
        for (var slot = 1; slot < guardedFrames.Length; slot++)
        {
            Assert.AreEqual(int.MinValue, guardedFrames[slot], $"frame slot {slot} was never used");
        }
    }

    [TestMethod]
    public void TooDeepAndOtherFailures_DoNotWriteTheFrameWorkspace()
    {
        var destination = Filled(64);
        var frames = new[] { int.MinValue, int.MinValue };
        var writer = new RLPWriter(destination, frames);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(10));
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(5));
        var frameSnapshot = (int[])frames.Clone();

        Assert.AreEqual(RLPStatus.TooDeep, writer.TryBeginList(2));
        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryBeginList(9));
        Assert.AreEqual(RLPStatus.InvalidArgument, writer.TryBeginList(-1));

        CollectionAssert.AreEqual(frameSnapshot, frames);
    }

    [TestMethod]
    public void CopyingAWriter_ForksItsState_SoTheCopyAndTheOriginalDiverge()
    {
        // Documents the hazard in the XML docs: a ref struct copy has its own cursor, so never copy a writer in use.
        var destination = Filled(16);
        var original = new RLPWriter(destination, new int[2]);
        var copy = original;

        Assert.AreEqual(RLPStatus.Success, copy.TryWriteUInt64(1));
        Assert.AreEqual(RLPStatus.Success, copy.TryComplete(out var copyWritten));

        Assert.AreEqual(1, copyWritten);
        Assert.AreEqual(RLPStatus.EmptyInput, original.TryComplete(out _), "the original never saw the write");
    }

    [TestMethod]
    public void TwoWritersOnDisjointBuffers_ShareNothing()
    {
        var firstBuffer = Filled(8);
        var secondBuffer = Filled(8);
        var first = new RLPWriter(firstBuffer, new int[1]);
        var second = new RLPWriter(secondBuffer, new int[1]);

        Assert.AreEqual(RLPStatus.Success, first.TryWriteUInt64(1));
        Assert.AreEqual(RLPStatus.Success, first.TryComplete(out _));

        Assert.AreEqual(RLPStatus.EmptyInput, second.TryComplete(out _));
        Assert.IsTrue(IsFilled(secondBuffer));
        Assert.AreEqual(RLPStatus.Success, second.TryWriteUInt64(2));
        Assert.AreEqual((byte)2, secondBuffer[0]);
    }

    [TestMethod]
    public void WriterOutput_IsNotAffectedByLaterChangesToTheSourcePayload()
    {
        // The writer copies bytes; it does not retain the source span.
        var payload = new byte[] { 1, 2, 3 };
        var destination = Filled(16);
        var writer = new RLPWriter(destination, new int[1]);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(payload));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));

        payload[0] = 0xff;

        Assert.AreEqual("83 01 02 03", RlpReference.ToHex(destination.AsSpan(0, written), true));
    }
}
