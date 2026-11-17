using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Scalar <see cref="RLPEncoder"/> writes are atomic (spec 7.2, 12.3 items 4 and 6): every failure leaves the
/// destination bytes untouched and reports <c>written == 0</c>. Check order: argument/minimality, size, capacity, overlap.
/// </summary>
[TestClass]
public sealed class EncoderAtomicityTests
{
    private delegate RLPStatus ScalarWrite(Span<byte> destination, out int written);

    /// <summary>Each scalar encoder call paired with the exact bytes it must produce.</summary>
    private static IEnumerable<(string Name, ScalarWrite Write, byte[] Expected)> ScalarCases()
    {
        var long300 = Repeat(0xab, 300);
        yield return ("empty string", (Span<byte> d, out int w) => RLPEncoder.TryWriteByteString([], d, out w), [0x80]);
        yield return ("literal", (Span<byte> d, out int w) => RLPEncoder.TryWriteByteString([0x7f], d, out w), [0x7f]);
        yield return ("81 80", (Span<byte> d, out int w) => RLPEncoder.TryWriteByteString([0x80], d, out w), [0x81, 0x80]);
        yield return ("dog", (Span<byte> d, out int w) => RLPEncoder.TryWriteByteString([0x64, 0x6f, 0x67], d, out w), [0x83, 0x64, 0x6f, 0x67]);
        yield return ("55 bytes", (Span<byte> d, out int w) => RLPEncoder.TryWriteByteString(Repeat(1, 55), d, out w), [.. new byte[] { 0xb7 }, .. Repeat(1, 55)]);
        yield return ("56 bytes", (Span<byte> d, out int w) => RLPEncoder.TryWriteByteString(Repeat(1, 56), d, out w), [.. new byte[] { 0xb8, 0x38 }, .. Repeat(1, 56)]);
        yield return ("300 bytes", (Span<byte> d, out int w) => RLPEncoder.TryWriteByteString(long300, d, out w), [.. new byte[] { 0xb9, 0x01, 0x2c }, .. long300]);
        yield return ("uint 0", (Span<byte> d, out int w) => RLPEncoder.TryWriteUInt64(0, d, out w), [0x80]);
        yield return ("uint 127", (Span<byte> d, out int w) => RLPEncoder.TryWriteUInt64(127, d, out w), [0x7f]);
        yield return ("uint 256", (Span<byte> d, out int w) => RLPEncoder.TryWriteUInt64(256, d, out w), [0x82, 0x01, 0x00]);
        yield return ("uint max", (Span<byte> d, out int w) => RLPEncoder.TryWriteUInt64(ulong.MaxValue, d, out w), H("88 ff*8"));
        yield return ("magnitude empty", (Span<byte> d, out int w) => RLPEncoder.TryWriteUnsignedMagnitude([], d, out w), [0x80]);
        yield return ("magnitude 01", (Span<byte> d, out int w) => RLPEncoder.TryWriteUnsignedMagnitude([1], d, out w), [0x01]);
        yield return ("magnitude 80", (Span<byte> d, out int w) => RLPEncoder.TryWriteUnsignedMagnitude([0x80], d, out w), [0x81, 0x80]);
        yield return ("magnitude 0100", (Span<byte> d, out int w) => RLPEncoder.TryWriteUnsignedMagnitude([1, 0], d, out w), [0x82, 0x01, 0x00]);
        yield return ("magnitude 33 bytes", (Span<byte> d, out int w) => RLPEncoder.TryWriteUnsignedMagnitude(Repeat(0xff, 33), d, out w), [.. new byte[] { 0xa1 }, .. Repeat(0xff, 33)]);
    }

    [TestMethod]
    public void EveryScalarCase_WritesExactBytes_AtExactSize_AndLeavesTheRestUntouched()
    {
        foreach (var (name, write, expected) in ScalarCases())
        {
            foreach (var slack in new[] { 0, 1, 7 })
            {
                var destination = Filled(expected.Length + slack);

                Assert.AreEqual(RLPStatus.Success, write(destination, out var written), name);

                Assert.AreEqual(expected.Length, written, name);
                Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual(expected), name);
                Assert.IsTrue(IsFilled(destination.AsSpan(written)), $"{name}: bytes past the item are untouched");
            }
        }
    }

    [TestMethod]
    public void EveryScalarCase_OneByteShortOrLess_IsDestinationTooSmall_WithNoMutation()
    {
        foreach (var (name, write, expected) in ScalarCases())
        {
            // Every shorter length for small items, a spread for the 300-byte case.
            var lengths = expected.Length <= 70
                ? Enumerable.Range(0, expected.Length)
                : [0, 1, 2, 3, expected.Length / 2, expected.Length - 2, expected.Length - 1];
            foreach (var length in lengths)
            {
                var destination = Filled(length);

                var status = write(destination, out var written);

                Assert.AreEqual(RLPStatus.DestinationTooSmall, status, $"{name} into {length}");
                Assert.AreEqual(0, written, $"{name} into {length}");
                Assert.IsTrue(IsFilled(destination), $"{name} into {length}: destination untouched");
            }
        }
    }

    [TestMethod]
    public void WrittenIsZero_OnFailure_EvenWhenTheCallerPassedAStaleNonZeroValue()
    {
        var destination = Filled(1);
        var written = 12345;

        Assert.AreEqual(RLPStatus.DestinationTooSmall, RLPEncoder.TryWriteByteString(Repeat(1, 5), destination, out written));
        Assert.AreEqual(0, written);
        written = 99;
        Assert.AreEqual(RLPStatus.DestinationTooSmall, RLPEncoder.TryWriteUInt64(1000, destination, out written));
        Assert.AreEqual(0, written);
        written = 99;
        Assert.AreEqual(RLPStatus.NonCanonicalInteger, RLPEncoder.TryWriteUnsignedMagnitude([0], destination, out written));
        Assert.AreEqual(0, written);
    }

    // ------------------------------------------------------------------ 12.3 item 6: invalid magnitudes

    [TestMethod]
    public void LeadingZeroMagnitude_IsNonCanonicalInteger_WithNoMutation_AndNeverNormalised()
    {
        var cases = new byte[][]
        {
            [0],
            [0, 0],
            [0, 1],
            [0, 0x7f],
            [0, 0x80],
            [0, 0, 1],
            [0, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff],
            Repeat(0, 40),
            [.. new byte[] { 0 }, .. Repeat(0xff, 32)],
        };

        foreach (var magnitude in cases)
        {
            var destination = Filled(64);

            var status = RLPEncoder.TryWriteUnsignedMagnitude(magnitude, destination, out var written);

            Assert.AreEqual(RLPStatus.NonCanonicalInteger, status, RlpReference.ToHex(magnitude));
            Assert.AreEqual(0, written);
            Assert.IsTrue(IsFilled(destination), "no byte may be written, in particular not a normalised form");
        }
    }

    [TestMethod]
    public void EmptyMagnitude_IsZero_AndASingleNonZeroByteIsItsOwnValue()
    {
        var destination = Filled(4);
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUnsignedMagnitude([], destination, out var written));
        Assert.AreEqual(1, written);
        Assert.AreEqual((byte)0x80, destination[0]);

        for (var b = 1; b < 256; b++)
        {
            Array.Fill(destination, Sentinel);
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUnsignedMagnitude([(byte)b], destination, out written), $"{b:x2}");
            Assert.AreEqual(b < 0x80 ? 1 : 2, written, $"{b:x2}");
            Assert.AreEqual(b < 0x80 ? (byte)b : (byte)0x81, destination[0], $"{b:x2}");
        }
    }

    // ------------------------------------------------------------------ check order

    [TestMethod]
    public void MinimalityIsCheckedBeforeCapacityAndOverlap()
    {
        var buffer = H("00 01 02 03");

        // Empty destination and a source inside the destination: still the argument error.
        Assert.AreEqual(RLPStatus.NonCanonicalInteger, RLPEncoder.TryWriteUnsignedMagnitude(buffer, Span<byte>.Empty, out _));
        Assert.AreEqual(RLPStatus.NonCanonicalInteger, RLPEncoder.TryWriteUnsignedMagnitude(buffer, buffer, out _));
        CollectionAssert.AreEqual(H("00 01 02 03"), buffer);
    }

    [TestMethod]
    public void SizeOverflow_IsReportedBeforeCapacity()
    {
        var destination = Filled(2);

        var status = RLPEncoder.TryWriteByteString(FakeLengthSpan(int.MaxValue), destination, out var written);

        Assert.AreEqual(RLPStatus.LengthOverflow, status);
        Assert.AreEqual(0, written);
        Assert.IsTrue(IsFilled(destination));
        Assert.AreEqual(RLPStatus.LengthOverflow, RLPEncoder.TryWriteByteString(FakeLengthSpan(int.MaxValue - 4), destination, out _));
    }

    [TestMethod]
    public void LargestRepresentableTotal_ReachesTheCapacityCheck_WithoutTouchingTheSource()
    {
        var destination = Filled(2);

        // Total is exactly int.MaxValue, so sizing succeeds and the small destination rejects it.
        var status = RLPEncoder.TryWriteByteString(FakeLengthSpan(int.MaxValue - 5), destination, out var written);

        Assert.AreEqual(RLPStatus.DestinationTooSmall, status);
        Assert.AreEqual(0, written);
        Assert.IsTrue(IsFilled(destination));
    }

    [TestMethod]
    public void CapacityIsCheckedBeforeOverlap()
    {
        // The source sits inside a destination that is too small for the item: the output region is undefined, so capacity wins.
        var buffer = Repeat(0xaa, 6);
        var source = buffer.AsSpan(2, 3); // needs 4 output bytes
        var destination = buffer.AsSpan(0, 3);

        var status = RLPEncoder.TryWriteByteString(source, destination, out var written);

        Assert.AreEqual(RLPStatus.DestinationTooSmall, status);
        Assert.AreEqual(0, written);
        Assert.IsTrue(IsFilled(buffer, 0xaa));
    }

    [TestMethod]
    public void ZeroLengthDestination_RejectsEveryScalarWrite()
    {
        Assert.AreEqual(RLPStatus.DestinationTooSmall, RLPEncoder.TryWriteByteString([], Span<byte>.Empty, out var written));
        Assert.AreEqual(0, written);
        Assert.AreEqual(RLPStatus.DestinationTooSmall, RLPEncoder.TryWriteUInt64(0, Span<byte>.Empty, out written));
        Assert.AreEqual(0, written);
        Assert.AreEqual(RLPStatus.DestinationTooSmall, RLPEncoder.TryWriteUnsignedMagnitude([], Span<byte>.Empty, out written));
        Assert.AreEqual(0, written);
    }

    [TestMethod]
    public void RepeatedCallsOnTheSameDestination_AreIndependent()
    {
        var destination = Filled(8);

        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUInt64(1000, destination, out var first));
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUInt64(0, destination, out var second));

        Assert.AreEqual(3, first);
        Assert.AreEqual(1, second);
        Assert.AreEqual("80 03 e8", RlpReference.ToHex(destination.AsSpan(0, 3), true), "the second write overwrites only its own byte");
    }
}
