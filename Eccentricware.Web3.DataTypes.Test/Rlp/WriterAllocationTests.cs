using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// The encoder and writer allocate zero managed bytes after warm-up (spec 1.1 item 3, 13.2), on success and failure
/// paths. Each measured region is exactly the calls under test: buffers, frame workspaces and decoded sources are
/// created beforehand and results are folded into a local so no assertion or formatting runs inside the region. Runs
/// serially because allocation counters are per thread.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WriterAllocationTests
{
    private const int Iterations = 2_000;
    private const int Trials = 5;

    private static void AssertNoAllocation(long smallestTrial, string what) =>
        Assert.AreEqual(0L, smallestTrial, $"{what} allocated {smallestTrial} bytes over {Iterations} calls (smallest of {Trials} trials)");

    [TestMethod]
    public void Sizing_AllocatesNothing()
    {
        byte[] empty = [];
        byte[] one = [0x05];
        byte[] medium = Repeat(0xab, 55);
        byte[] large = Repeat(0xab, 300);
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += Sizes(empty, one, medium, large);
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                checksum += Sizes(empty, one, medium, large);
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "TryGetByteStringLength/TryGetListLength");
        Assert.AreNotEqual(0L, checksum);

        static long Sizes(byte[] a, byte[] b, byte[] c, byte[] d)
        {
            long sum = (int)RLPEncoder.TryGetByteStringLength(a, out var total);
            sum += total;
            sum += (int)RLPEncoder.TryGetByteStringLength(b, out total) + total;
            sum += (int)RLPEncoder.TryGetByteStringLength(c, out total) + total;
            sum += (int)RLPEncoder.TryGetByteStringLength(d, out total) + total;
            sum += (int)RLPEncoder.TryGetListLength(300, out total) + total;
            sum += (int)RLPEncoder.TryGetListLength(-1, out total) + total;
            return sum;
        }
    }

    [TestMethod]
    public void EncoderScalarWrites_AllocateNothing()
    {
        byte[] destination = new byte[512];
        byte[] shortString = [0x64, 0x6f, 0x67];
        byte[] longString = Repeat(0xab, 300);
        byte[] magnitude = Repeat(0xcd, 33);
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += WriteAll(destination, shortString, longString, magnitude);
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                checksum += WriteAll(destination, shortString, longString, magnitude);
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "RLPEncoder.TryWrite*");
        Assert.AreNotEqual(0L, checksum);

        static long WriteAll(Span<byte> destination, byte[] shortString, byte[] longString, byte[] magnitude)
        {
            long sum = (int)RLPEncoder.TryWriteByteString([], destination, out var written) + written;
            sum += (int)RLPEncoder.TryWriteByteString(shortString, destination, out written) + written;
            sum += (int)RLPEncoder.TryWriteByteString(longString, destination, out written) + written;
            sum += (int)RLPEncoder.TryWriteUInt64(0, destination, out written) + written;
            sum += (int)RLPEncoder.TryWriteUInt64(ulong.MaxValue, destination, out written) + written;
            sum += (int)RLPEncoder.TryWriteUnsignedMagnitude(magnitude, destination, out written) + written;
            return sum;
        }
    }

    [TestMethod]
    public void EncoderFailurePaths_AllocateNothing()
    {
        byte[] destination = new byte[512];
        byte[] tooSmall = new byte[2];
        byte[] leadingZero = [0, 1, 2];
        byte[] payload = [1, 2, 3];
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += FailAll(destination, tooSmall, leadingZero, payload);
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                checksum += FailAll(destination, tooSmall, leadingZero, payload);
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "RLPEncoder failure paths");
        Assert.AreNotEqual(0L, checksum);

        static long FailAll(byte[] destination, byte[] tooSmall, byte[] leadingZero, byte[] payload)
        {
            long sum = (int)RLPEncoder.TryWriteByteString(payload, tooSmall, out var written);
            sum += (int)RLPEncoder.TryWriteUInt64(ulong.MaxValue, tooSmall, out written);
            sum += (int)RLPEncoder.TryWriteUnsignedMagnitude(leadingZero, destination, out written);
            // Source inside the destination: overlap.
            sum += (int)RLPEncoder.TryWriteByteString(destination.AsSpan(1, 3), destination, out written);
            return sum + written;
        }
    }

    /// <summary>A transaction-like nested session: a list with scalars, a nested list, a verbatim child and a long string.</summary>
    private static int RunSession(Span<byte> destination, Span<int> frames, int outerPayload, int innerPayload, ReadOnlySpan<byte> address, ReadOnlySpan<byte> data, in RLPData embedded)
    {
        var writer = new RLPWriter(destination, frames);
        var status = writer.TryBeginList(outerPayload);
        status |= writer.TryWriteUInt64(9);
        status |= writer.TryWriteUInt64(20_000_000_000);
        status |= writer.TryWriteByteString(address);
        status |= writer.TryWriteUnsignedMagnitude([0x01, 0x00]);
        status |= writer.TryWriteByteString(data);
        status |= writer.TryBeginList(innerPayload);
        status |= writer.TryWriteUInt64(1);
        status |= writer.TryWriteByteString([0xaa, 0xbb]);
        status |= writer.TryWriteEncoded(in embedded);
        status |= writer.TryEndList();
        status |= writer.TryWriteUInt64(37);
        status |= writer.TryEndList();
        status |= writer.TryComplete(out var written);
        return status == RLPStatus.Success ? written : -1;
    }

    [TestMethod]
    public void FullWriterSession_AllocatesNothing()
    {
        byte[] addressBytes = Repeat(0x35, 20);
        byte[] dataBytes = Repeat(0xab, 70);
        var embeddedBytes = H("83 64 6f 67");
        var embedded = DecodeOk(embeddedBytes);
        var inner = RlpNode.ListOf(RlpNode.FromUInt64(1), RlpNode.Bytes(0xaa, 0xbb), RlpNode.Bytes(0x64, 0x6f, 0x67));
        var outer = RlpNode.ListOf(
            RlpNode.FromUInt64(9),
            RlpNode.FromUInt64(20_000_000_000),
            RlpNode.Bytes(addressBytes),
            RlpNode.FromUInt64(256),
            RlpNode.Bytes(dataBytes),
            inner,
            RlpNode.FromUInt64(37));
        var expected = RlpReference.Encode(outer);
        var innerPayload = RlpReference.PayloadLength(inner);
        var outerPayload = RlpReference.PayloadLength(outer);
        byte[] destination = new byte[expected.Length + 16];
        Span<int> frames = stackalloc int[8];

        // Correctness of the session itself, outside the measured region.
        Assert.AreEqual(expected.Length, RunSession(destination, frames, outerPayload, innerPayload, addressBytes, dataBytes, in embedded));
        Assert.IsTrue(destination.AsSpan(0, expected.Length).SequenceEqual(expected));

        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += RunSession(destination, frames, outerPayload, innerPayload, addressBytes, dataBytes, in embedded);
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                checksum += RunSession(destination, frames, outerPayload, innerPayload, addressBytes, dataBytes, in embedded);
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "a full RLPWriter session");
        Assert.AreEqual((long)expected.Length * Iterations * (Trials + 1), checksum);
    }

    [TestMethod]
    public void WriterFailurePaths_AllocateNothing()
    {
        byte[] destination = new byte[64];
        byte[] leadingZero = [0, 1];
        byte[] bigPayload = new byte[100];
        var item = DecodeOk(H("c2 01 02"));
        Span<int> frames = stackalloc int[1];
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += FailAll(destination, frames, leadingZero, bigPayload, in item);
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                checksum += FailAll(destination, frames, leadingZero, bigPayload, in item);
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "RLPWriter failure paths");
        Assert.AreNotEqual(0L, checksum);

        static long FailAll(byte[] destination, Span<int> frames, byte[] leadingZero, byte[] bigPayload, in RLPData item)
        {
            var writer = new RLPWriter(destination, frames);
            long sum = (int)writer.TryComplete(out _); // EmptyInput
            sum += (int)writer.TryEndList(); // Unbalanced
            sum += (int)writer.TryWriteUnsignedMagnitude(leadingZero); // NonCanonicalInteger
            sum += (int)writer.TryWriteByteString(bigPayload); // 102 encoded bytes into 64: DestinationTooSmall
            sum += (int)writer.TryBeginList(-1); // InvalidArgument
            sum += (int)writer.TryWriteEncoded(default); // InvalidArgument
            sum += (int)writer.TryBeginList(1); // opens the only frame
            sum += (int)writer.TryBeginList(0); // InvalidListBoundary
            sum += (int)writer.TryWriteEncoded(in item); // InvalidListBoundary
            sum += (int)writer.TryWriteUInt64(1);
            sum += (int)writer.TryEndList();
            sum += (int)writer.TryWriteUInt64(1); // ExtraRoot
            sum += (int)writer.TryComplete(out _);
            sum += (int)writer.TryComplete(out _); // WriterStateInvalid
            var invalid = default(RLPWriter);
            sum += (int)invalid.TryWriteUInt64(1); // WriterStateInvalid
            return sum;
        }
    }
}
