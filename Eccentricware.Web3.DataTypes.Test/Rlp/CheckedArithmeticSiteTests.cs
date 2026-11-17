using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// One probe per place where the codec wraps an integer on purpose (an unsigned reinterpretation of a possibly negative int, or a
/// byte truncation). Each wrap is marked <c>unchecked(...)</c> in the library so the codec behaves the same when the library is
/// compiled with overflow checking (<c>-p:RlpCheckedArithmetic=true</c>, the CI job <c>checked-arithmetic</c>). These tests pass in
/// both builds; in a checked build they fail with <see cref="OverflowException"/> if an <c>unchecked(...)</c> is ever dropped.
/// </summary>
[TestClass]
public sealed class CheckedArithmeticSiteTests
{
    // ---- RLPHeaderReader.TryRead: (uint)itemStart >= (uint)containerEnd || (uint)containerEnd > (uint)document.Length

    [TestMethod]
    [DataRow(-1, 3)]
    [DataRow(int.MinValue, 3)]
    [DataRow(0, -1)]
    [DataRow(0, int.MinValue)]
    [DataRow(-1, -1)]
    [DataRow(0, 4)]
    [DataRow(3, 3)]
    [DataRow(0, int.MaxValue)]
    public void HeaderReader_RejectsOutOfRangeWindows_WithIncompleteAndNeverThrows(int itemStart, int containerEnd)
    {
        byte[] document = [0xc2, 0x01, 0x02];

        RLPHeaderStatus status = RLPHeaderReader.TryRead(document, itemStart, containerEnd, out RLPHeader header);

        Assert.AreEqual(RLPHeaderStatus.Incomplete, status);
        Assert.AreEqual(RLPKind.None, header.Kind, "header is default on failure");
        Assert.AreEqual(0, header.PayloadLength);
    }

    // ---- RLPHeaderReader.TryRead: (uint)(first - 0xc0), negative for the long-string prefixes b8..bf

    [TestMethod]
    public void HeaderReader_ClassifiesEveryLongStringPrefix_FromB8ToBf_WithoutThrowing()
    {
        for (int lengthOfLength = 1; lengthOfLength <= 8; lengthOfLength++)
        {
            // The length is 56 for one length byte, otherwise 0x01 followed by zeros (256, 65536, ...). No payload follows, so a
            // length that fits an int is Incomplete and a longer one is LengthOverflow; either way the prefix was classified.
            byte[] document = new byte[1 + lengthOfLength];
            document[0] = (byte)(0xb7 + lengthOfLength);
            document[1] = lengthOfLength == 1 ? (byte)56 : (byte)1;
            RLPHeaderStatus status = RLPHeaderReader.TryRead(document, 0, document.Length, out _);
            Assert.AreEqual(
                lengthOfLength <= 4 ? RLPHeaderStatus.Incomplete : RLPHeaderStatus.LengthOverflow,
                status,
                $"prefix {0xb7 + lengthOfLength:x2}");
        }

        byte[] valid = [0xb8, 56, .. Repeat(7, 56)];
        Assert.AreEqual(RLPHeaderStatus.Valid, RLPHeaderReader.TryRead(valid, 0, valid.Length, out RLPHeader header));
        Assert.AreEqual(RLPKind.ByteString, header.Kind);
        Assert.AreEqual(56, header.PayloadLength);
    }

    [TestMethod]
    public void Decode_AcceptsLongStringAndLongListPrefixes_AndRejectsTheRest()
    {
        byte[] longString = [0xb8, 56, .. Repeat(7, 56)];
        byte[] longList = [0xf8, 56, .. Repeat(1, 56)];
        Assert.AreEqual(RLPStatus.Success, Decode(longString, Limits(), out RLPData stringDocument, out _));
        Assert.AreEqual(RLPKind.ByteString, stringDocument.Kind);
        Assert.AreEqual(RLPStatus.Success, Decode(longList, Limits(), out RLPData listDocument, out _));
        Assert.AreEqual(RLPKind.List, listDocument.Kind);
        Assert.AreEqual(RLPStatus.TruncatedInput, Decode([0xbf], Limits(), out _, out _));
        Assert.AreEqual(RLPStatus.TruncatedInput, Decode([0xff], Limits(), out _, out _));
    }

    // ---- RLPDecodeLimits.IsValid: (uint)(MaxListDepth - 1)

    [TestMethod]
    [DataRow(int.MinValue, false)]
    [DataRow(-1, false)]
    [DataRow(0, false)]
    [DataRow(1, true)]
    [DataRow(RLPDecodeLimits.HardMaxListDepth, true)]
    [DataRow(RLPDecodeLimits.HardMaxListDepth + 1, false)]
    [DataRow(int.MaxValue, false)]
    public void Limits_IsValid_NeverThrows_ForAnyListDepth(int maxListDepth, bool expectedValid)
    {
        Assert.AreEqual(expectedValid, new RLPDecodeLimits(1024, maxListDepth, 16).IsValid);
        Assert.AreEqual(
            expectedValid ? RLPStatus.Success : RLPStatus.InvalidArgument,
            Decode([0x01], new RLPDecodeLimits(1024, maxListDepth, 16), out _, out _));
    }

    // ---- RLPChildrenIndex.TryGetAt: (uint)ordinal

    [TestMethod]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    [DataRow(2)]
    [DataRow(int.MaxValue)]
    public void ChildrenIndex_TryGetAt_RejectsOutOfRangeOrdinals_WithInvalidArgument(int ordinal)
    {
        RLPData document = DecodeOk(H("c2 01 02"));
        Assert.AreEqual(RLPStatus.Success, document.TryIndexDirectChildren(new RLPRange[2], out RLPChildrenIndex index, out _));

        Assert.AreEqual(RLPStatus.InvalidArgument, index.TryGetAt(ordinal, out RLPData child));
        Assert.IsFalse(child.IsValid);
        Assert.AreEqual(RLPStatus.Success, index.TryGetAt(1, out RLPData second));
        Assert.AreEqual(2, second.Payload[0]);
    }

    // ---- RLPHeaderWriter.WriteHeader: (byte)payloadLength keeps the low byte of each length byte

    [TestMethod]
    [DataRow(56, 2)]
    [DataRow(255, 2)]
    [DataRow(256, 3)]
    [DataRow(65_535, 3)]
    [DataRow(65_536, 4)]
    public void ByteStringHeader_HoldsTheBigEndianLength_ForEveryLengthWidth(int length, int expectedHeaderLength)
    {
        byte[] payload = Repeat(0x55, length);
        byte[] destination = new byte[length + 5];

        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteByteString(payload, destination, out int written));

        Assert.AreEqual(length + expectedHeaderLength, written);
        int lengthOfLength = expectedHeaderLength - 1;
        Assert.AreEqual(0xb7 + lengthOfLength, destination[0]);
        int decoded = 0;
        for (int i = 1; i <= lengthOfLength; i++)
        {
            decoded = (decoded << 8) | destination[i];
        }

        Assert.AreEqual(length, decoded);
    }

    [TestMethod]
    public void ByteStringHeader_FourLengthBytes_AtSixteenMebibytes()
    {
        const int length = 1 << 24;
        byte[] payload = new byte[length];
        byte[] destination = new byte[length + 5];

        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteByteString(payload, destination, out int written));

        Assert.AreEqual(length + 5, written);
        CollectionAssert.AreEqual(new byte[] { 0xbb, 0x01, 0x00, 0x00, 0x00 }, destination[..5]);
    }

    [TestMethod]
    [DataRow(56, 2)]
    [DataRow(256, 3)]
    [DataRow(70_000, 4)]
    public void ListHeader_HoldsTheBigEndianLength_ForEveryLengthWidth(int payloadLength, int expectedHeaderLength)
    {
        byte[] destination = new byte[payloadLength + expectedHeaderLength];
        int[] frames = new int[1];
        var writer = new RLPWriter(destination, frames);

        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(payloadLength));

        int lengthOfLength = expectedHeaderLength - 1;
        Assert.AreEqual(0xf7 + lengthOfLength, destination[0]);
        int decoded = 0;
        for (int i = 1; i <= lengthOfLength; i++)
        {
            decoded = (decoded << 8) | destination[i];
        }

        Assert.AreEqual(payloadLength, decoded);
    }

    // ---- RLPHeaderWriter.WriteUInt64: (byte)value

    [TestMethod]
    public void UInt64_WritesEveryWidth_AsTheBigEndianMagnitude()
    {
        (ulong Value, byte[] Expected)[] cases =
        [
            (0x7f, [0x7f]),
            (0x80, [0x81, 0x80]),
            (0xff, [0x81, 0xff]),
            (0x100, [0x82, 0x01, 0x00]),
            (0x0102_0304, [0x84, 0x01, 0x02, 0x03, 0x04]),
            (0x0102_0304_0506_0708, [0x88, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]),
            (ulong.MaxValue, [0x88, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff]),
        ];

        foreach ((ulong value, byte[] expected) in cases)
        {
            byte[] destination = new byte[9];
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUInt64(value, destination, out int written), $"{value:x}");
            CollectionAssert.AreEqual(expected, destination[..written], $"{value:x}");

            byte[] viaWriter = new byte[9];
            var writer = new RLPWriter(viaWriter, new int[1]);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(value));
            CollectionAssert.AreEqual(expected, viaWriter[..expected.Length], $"{value:x} through the writer");
        }
    }

    // ---- RLPWriter.WriteMagnitude: (byte)(limb >> shift) for 1..7 byte top limbs

    [TestMethod]
    public void UInt256Writer_EqualsTheMagnitudeWriter_ForEveryByteCount()
    {
        for (int byteCount = 1; byteCount <= 32; byteCount++)
        {
            foreach (byte topByte in new byte[] { 0x01, 0x7f, 0x80, 0xff })
            {
                byte[] magnitude = Repeat(0xa5, byteCount);
                magnitude[0] = topByte;
                var value = new uint256(magnitude);

                byte[] expected = new byte[34];
                Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUnsignedMagnitude(magnitude, expected, out int expectedLength));

                byte[] actual = new byte[34];
                var writer = new RLPWriter(actual, new int[1]);
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(value), $"{byteCount} bytes, top {topByte:x2}");
                Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out int actualLength));

                Assert.AreEqual(expectedLength, actualLength);
                CollectionAssert.AreEqual(expected[..expectedLength], actual[..actualLength], $"{byteCount} bytes, top {topByte:x2}");
            }
        }
    }

    // ---- RLPWriter.CheckFits: a negative room. Regression test for a real defect (see below), not a deliberate wrap.
    //
    // CheckFits used to compare (uint)total > (uint)(limit - _cursor). A negative room became a huge unsigned room, so the parent
    // limit was NOT enforced (the write succeeded past the end of the innermost list, only the destination bound still applied)
    // although the comment claimed it was rejected; with overflow checking compiled in, the cast threw OverflowException instead.

    [TestMethod]
    public void Writer_RejectsANegativeRoom_AsInvalidListBoundary_WithoutThrowingOrWriting()
    {
        // Misuse the type documents against: a copy of a writer in use keeps its own cursor while sharing the frame workspace, so
        // another writer can lower the shared frame end below the stale copy's cursor and leave a negative room.
        byte[] destination = Filled(64);
        int[] sharedFrames = new int[2];
        var original = new RLPWriter(destination, sharedFrames);
        Assert.AreEqual(RLPStatus.Success, original.TryBeginList(40));
        Assert.AreEqual(RLPStatus.Success, original.TryWriteByteString(Repeat(0x01, 30)));
        var staleCopy = original;

        var other = new RLPWriter(Filled(64), sharedFrames);
        Assert.AreEqual(RLPStatus.Success, other.TryBeginList(1));

        byte[] before = (byte[])destination.Clone();
        Assert.AreEqual(RLPStatus.InvalidListBoundary, staleCopy.TryWriteByteString([0x80]));
        Assert.AreEqual(RLPStatus.InvalidListBoundary, staleCopy.TryWriteUInt64(300));
        Assert.AreEqual(RLPStatus.InvalidListBoundary, staleCopy.TryBeginList(0));
        CollectionAssert.AreEqual(before, destination, "a rejected write leaves the destination untouched");
    }

    [TestMethod]
    [DataRow(int.MinValue)]
    [DataRow(-5)]
    [DataRow(-1)]
    [DataRow(0)] // the cursor is already past a frame end of 0
    public void Writer_RejectsATamperedNegativeRoom_AsInvalidListBoundary_AndNeverThrows(int tamperedFrameEnd)
    {
        byte[] destination = Filled(16);
        int[] frames = new int[2];
        var writer = new RLPWriter(destination, frames);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(2));
        frames[0] = tamperedFrameEnd;
        byte[] before = (byte[])destination.Clone();

        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteUInt64(300));
        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteByteString([1, 2]));
        CollectionAssert.AreEqual(before, destination);
    }
}
