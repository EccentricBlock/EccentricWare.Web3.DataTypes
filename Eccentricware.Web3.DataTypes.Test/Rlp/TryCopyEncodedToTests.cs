using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>Verbatim copy of a validated item: exactness, capacity, alias detection against the region written (spec 7.5).</summary>
[TestClass]
public sealed class TryCopyEncodedToTests
{
    private const byte Fill = 0xee;

    private static byte[] FilledBuffer(int length)
    {
        var buffer = new byte[length];
        Array.Fill(buffer, Fill);
        return buffer;
    }

    [TestMethod]
    public void EveryCanonicalVector_CopiesExactlyTheOriginalBytes()
    {
        foreach (var vector in RlpVectors.Canonical)
        {
            var document = DecodeOk(vector.Encoded, Limits(maxItems: 9000));
            var destination = FilledBuffer(vector.Encoded.Length + 3);

            Assert.AreEqual(RLPStatus.Success, document.TryCopyEncodedTo(destination, out var written), vector.Name);

            Assert.AreEqual(vector.Encoded.Length, written, vector.Name);
            Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual(vector.Encoded), vector.Name);
            Assert.IsTrue(destination.AsSpan(written).ToArray().All(b => b == Fill), $"{vector.Name}: bytes past the item untouched");
        }
    }

    [TestMethod]
    public void ExactSizeDestination_Succeeds()
    {
        var input = H("c8 83 63 61 74 83 64 6f 67");
        var destination = new byte[input.Length];

        Assert.AreEqual(RLPStatus.Success, DecodeOk(input).TryCopyEncodedTo(destination, out var written));
        Assert.AreEqual(input.Length, written);
        CollectionAssert.AreEqual(input, destination);
    }

    [TestMethod]
    public void OneByteShort_IsDestinationTooSmall_WrittenZero_AndTheDestinationIsUntouched()
    {
        var input = H("c8 83 63 61 74 83 64 6f 67");
        var destination = FilledBuffer(input.Length - 1);

        Assert.AreEqual(RLPStatus.DestinationTooSmall, DecodeOk(input).TryCopyEncodedTo(destination, out var written));

        Assert.AreEqual(0, written);
        Assert.IsTrue(destination.All(b => b == Fill));
        Assert.AreEqual(RLPStatus.DestinationTooSmall, DecodeOk(input).TryCopyEncodedTo(Span<byte>.Empty, out written));
        Assert.AreEqual(0, written);
    }

    [TestMethod]
    public void NonNormalisedRawBytes_AreCopiedVerbatim()
    {
        foreach (var hex in new[] { "00", "82 00 01", "83 00 00 00", "c4 00 82 00 01" })
        {
            var input = H(hex);
            var destination = new byte[input.Length];

            Assert.AreEqual(RLPStatus.Success, DecodeOk(input).TryCopyEncodedTo(destination, out var written), hex);
            Assert.AreEqual(input.Length, written, hex);
            CollectionAssert.AreEqual(input, destination, hex);
        }
    }

    [TestMethod]
    public void ChildItem_CopiesOnlyItsOwnEncodedBytes()
    {
        var root = DecodeOk(H("cc 83 63 61 74 c5 83 64 6f 67 c0 00 80"));
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));
        Assert.IsTrue(children.MoveNext());
        Assert.IsTrue(children.MoveNext());
        var destination = FilledBuffer(10);

        Assert.AreEqual(RLPStatus.Success, children.Current.TryCopyEncodedTo(destination, out var written));

        Assert.AreEqual(6, written);
        Assert.IsTrue(destination.AsSpan(0, 6).SequenceEqual(H("c5 83 64 6f 67 c0")));
        Assert.AreEqual(Fill, destination[6]);
    }

    [TestMethod]
    public void DestinationExactlyOnTheSource_IsOverlappingBuffers_AndNothingChanges()
    {
        var buffer = H("c8 83 63 61 74 83 64 6f 67");
        var snapshot = (byte[])buffer.Clone();
        var root = DecodeOk(buffer);

        Assert.AreEqual(RLPStatus.OverlappingBuffers, root.TryCopyEncodedTo(buffer, out var written));

        Assert.AreEqual(0, written);
        CollectionAssert.AreEqual(snapshot, buffer);
    }

    [TestMethod]
    public void DestinationOverlappingTheSourcePartially_IsOverlappingBuffers()
    {
        var exact = H("c8 83 63 61 74 83 64 6f 67");
        var snapshot = (byte[])exact.Clone();
        var view = DecodeOk(exact);
        Assert.AreEqual(RLPStatus.Success, view.TryGetChildren(out var children));
        Assert.IsTrue(children.MoveNext());
        var child = children.Current; // 83 63 61 74 at [1,5), backed by `exact`

        // Destinations carved out of the same array: each written region shares at least one byte with [1,5).
        Assert.AreEqual(RLPStatus.OverlappingBuffers, child.TryCopyEncodedTo(exact.AsSpan(0), out var written)); // [0,4)
        Assert.AreEqual(0, written);
        Assert.AreEqual(RLPStatus.OverlappingBuffers, child.TryCopyEncodedTo(exact.AsSpan(2), out written)); // [2,6)
        Assert.AreEqual(0, written);
        Assert.AreEqual(RLPStatus.OverlappingBuffers, child.TryCopyEncodedTo(exact.AsSpan(1), out written)); // exactly the source
        Assert.AreEqual(0, written);
        Assert.AreEqual(RLPStatus.OverlappingBuffers, child.TryCopyEncodedTo(exact.AsSpan(4), out written)); // [4,8) shares byte 4
        Assert.AreEqual(0, written);
        CollectionAssert.AreEqual(snapshot, exact);
    }

    [TestMethod]
    public void AdjacentDestination_DoesNotOverlap_AndIsWritten()
    {
        var exact = H("c8 83 63 61 74 83 64 6f 67");
        var view = DecodeOk(exact);
        Assert.AreEqual(RLPStatus.Success, view.TryGetChildren(out var children));
        Assert.IsTrue(children.MoveNext());
        var cat = children.Current; // [1,5)

        // Destination [5, 9) directly after the source: no shared byte. It overwrites the second child, which is the caller's choice.
        Assert.AreEqual(RLPStatus.Success, cat.TryCopyEncodedTo(exact.AsSpan(5), out var written));

        Assert.AreEqual(4, written);
        Assert.IsTrue(exact.AsSpan(5, 4).SequenceEqual(H("83 63 61 74")));
    }

    [TestMethod]
    public void OnlyTheRegionThatWouldBeWritten_IsCheckedForOverlap()
    {
        // c3 01 02 03: child 03 at offset 3. Destination = exact[1..] is 3 bytes long and reaches the child,
        // but the one-byte region written is [1,2), which does not touch [3,4).
        var exact = H("c3 01 02 03");
        var view = DecodeOk(exact);
        Assert.AreEqual(RLPStatus.Success, view.TryGetChildren(out var children));
        Assert.IsTrue(children.MoveNext());
        Assert.IsTrue(children.MoveNext());
        Assert.IsTrue(children.MoveNext());
        var last = children.Current;
        Assert.AreEqual(new RLPRange(3, 1), last.EncodedRange);

        Assert.AreEqual(RLPStatus.Success, last.TryCopyEncodedTo(exact.AsSpan(1), out var written));

        Assert.AreEqual(1, written);
        Assert.AreEqual((byte)0x03, exact[1]);
    }

    [TestMethod]
    public void SeparateArrays_NeverOverlap()
    {
        var source = H("83 64 6f 67");
        var destination = new byte[4];

        Assert.AreEqual(RLPStatus.Success, DecodeOk(source).TryCopyEncodedTo(destination, out _));
        CollectionAssert.AreEqual(source, destination);
    }
}
