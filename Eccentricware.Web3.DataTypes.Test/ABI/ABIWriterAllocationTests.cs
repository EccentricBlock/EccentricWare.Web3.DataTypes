using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIWriteTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Spec 11.3: zero managed allocation in the caller-backed writer, measurer and call encoder, on the success path AND on every failure path,
/// after warm-up; no allocation proportional to counts. Schemas, spans, limits and sources are created before the measured region; each batch
/// folds its results into a checksum and no assertion runs inside the region. Serial: allocation counters are per thread.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ABIWriterAllocationTests
{
    private const int Iterations = 2_000;
    private const int Trials = 5;

    private static void AssertNoAllocation(string what, Func<long> batch)
    {
        long checksum = 0;
        for (int i = 0; i < 3; i++)
        {
            checksum += batch(); // JIT warm-up (tiering included)
        }

        long best = long.MaxValue;
        for (int trial = 0; trial < Trials; trial++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            checksum += batch();
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.AreEqual(0L, best, $"{what} allocated {best} bytes per batch of {Iterations} calls (smallest of {Trials} trials)");
        Assert.AreNotEqual(long.MinValue, checksum);
    }

    private static readonly ABISchema TransferSchema = CompileTuple("(address,uint256)");
    private static readonly ABISchema MixedSchema = CompileTuple("(uint256,(string,(uint8,bytes)[],address),uint256[2])");
    private static readonly ABISchema TinySchema = CompileTuple("(uint8,uint256)");
    private static readonly ABISchema Utf8Schema = CompileTuple("(string)");
    private static readonly ABISchema ArraySchema = CompileTuple("(uint256[])");
    private static readonly ABIFunctionSchema TransferFunction = Function("transfer(address,uint256)");
    private static readonly ABIFunctionSchema MulticallFunction = Function("multicall(bytes[])");

    private static readonly byte[] Vitalik = Convert.FromHexString("d8dA6BF26964aF9D7eEd9e03E53415D37aA96045");
    private static readonly byte[] HiText = "hi"u8.ToArray();
    private static readonly byte[] BadText = [0xff, 0xfe];
    private static readonly byte[] Child1 = Convert.FromHexString(ABIFixtures.TransferChild1);
    private static readonly byte[] Child2 = Convert.FromHexString(ABIFixtures.TransferChild2);
    private static readonly byte[] MixedExpected = Convert.FromHexString(ABIFixtures.NestedMixedTuples);

    private static ABIEncodeLimits Limits => ABIEncodeLimits.Default;

    // ------------------------------------------------------------------ operation sequences (no allocation of their own)

    private static int WriteMixed(Span<byte> destination, Span<ABIWriteFrame> frames, in ABIEncodeLimits limits)
    {
        ABIWriter.TryBegin(MixedSchema, destination, frames, in limits, out ABIWriter w, out _);
        w.TryWriteUInt64(7, out _);
        w.TryBeginTuple(out _);
        w.TryWriteStringUtf8(HiText, out _);
        w.TryBeginDynamicArray(2, out _);
        w.TryBeginTuple(out _);
        w.TryWriteUInt64(1, out _);
        w.TryWriteBytes([0xaa], out _);
        w.TryEndContainer(out _);
        w.TryBeginTuple(out _);
        w.TryWriteUInt64(2, out _);
        w.TryWriteBytes([0xbb, 0xcc], out _);
        w.TryEndContainer(out _);
        w.TryEndContainer(out _);
        w.TryWriteAddress(Vitalik, out _);
        w.TryEndContainer(out _);
        w.TryBeginFixedArray(out _);
        w.TryWriteUInt64(8, out _);
        w.TryWriteUInt64(9, out _);
        w.TryEndContainer(out _);
        w.TryFinish(out int written, out _);
        return written;
    }

    private static int MeasureMixed(Span<ABIMeasureFrame> frames, in ABIEncodeLimits limits)
    {
        ABIMeasurer.TryBegin(MixedSchema, frames, in limits, out ABIMeasurer m, out _);
        m.TryWriteUInt64(7, out _);
        m.TryBeginTuple(out _);
        m.TryWriteStringUtf8(HiText, out _);
        m.TryBeginDynamicArray(2, out _);
        m.TryBeginTuple(out _);
        m.TryWriteUInt64(1, out _);
        m.TryWriteBytes([0xaa], out _);
        m.TryEndContainer(out _);
        m.TryBeginTuple(out _);
        m.TryWriteUInt64(2, out _);
        m.TryWriteBytes([0xbb, 0xcc], out _);
        m.TryEndContainer(out _);
        m.TryEndContainer(out _);
        m.TryWriteAddress(Vitalik, out _);
        m.TryEndContainer(out _);
        m.TryBeginFixedArray(out _);
        m.TryWriteUInt64(8, out _);
        m.TryWriteUInt64(9, out _);
        m.TryEndContainer(out _);
        m.TryFinish(out int length, out _);
        return length;
    }

    // ------------------------------------------------------------------ success

    [TestMethod]
    public void Writer_StaticAndNestedDocuments_AllocateNothing()
    {
        // Sanity outside the measured region: the sequences really produce the documents.
        byte[] check = new byte[MixedExpected.Length];
        Assert.AreEqual(MixedExpected.Length, WriteMixed(check, new ABIWriteFrame[8], Limits));
        CollectionAssert.AreEqual(MixedExpected, check);

        byte[] destination = new byte[1024];
        ABIWriteFrame[] frames = new ABIWriteFrame[8];
        AssertNoAllocation("static writer", () =>
        {
            long sum = 0;
            var limits = Limits;
            for (int i = 0; i < Iterations; i++)
            {
                ABIWriter.TryBegin(TransferSchema, destination, frames, in limits, out ABIWriter w, out _);
                w.TryWriteAddress(Vitalik, out _);
                w.TryWriteUInt64(1_000_000_000_000_000_000, out _);
                w.TryFinish(out int written, out _);
                sum += written;
            }

            return sum;
        });

        AssertNoAllocation("nested writer", () =>
        {
            long sum = 0;
            var limits = Limits;
            for (int i = 0; i < Iterations; i++)
            {
                sum += WriteMixed(destination, frames, in limits);
            }

            return sum;
        });
    }

    [TestMethod]
    public void Measurer_AllocatesNothing()
    {
        ABIMeasureFrame[] frames = new ABIMeasureFrame[8];
        Assert.AreEqual(MixedExpected.Length, MeasureMixed(frames, Limits));
        AssertNoAllocation("measurer", () =>
        {
            long sum = 0;
            var limits = Limits;
            for (int i = 0; i < Iterations; i++)
            {
                sum += MeasureMixed(frames, in limits);
            }

            return sum;
        });
    }

    [TestMethod]
    public void CallEncoder_AllocatesNothing()
    {
        byte[] destination = new byte[512];
        ABIWriteFrame[] frames = new ABIWriteFrame[8];
        AssertNoAllocation("call encoder (transfer)", () =>
        {
            long sum = 0;
            var limits = Limits;
            for (int i = 0; i < Iterations; i++)
            {
                ABICallEncoder.TryBegin(TransferFunction, destination, frames, in limits, out ABICallEncoder c, out _);
                c.TryWriteAddress(Vitalik, out _);
                c.TryWriteUInt64(1, out _);
                c.TryFinish(out int written, out _);
                sum += written;
            }

            return sum;
        });

        AssertNoAllocation("call encoder (multicall, 388 bytes)", () =>
        {
            long sum = 0;
            var limits = Limits;
            for (int i = 0; i < Iterations; i++)
            {
                ABICallEncoder.TryBegin(MulticallFunction, destination, frames, in limits, out ABICallEncoder c, out _);
                c.TryBeginDynamicArray(2, out _);
                c.TryWriteBytes(Child1, out _);
                c.TryWriteBytes(Child2, out _);
                c.TryEndContainer(out _);
                c.TryFinish(out int written, out _);
                sum += written;
            }

            Assert.AreEqual(388L, sum / Iterations);
            return sum;
        });
    }

    [TestMethod]
    public void LargeCounts_AllocateNothing_NoAllocationProportionalToTheCount()
    {
        byte[] destination = new byte[200_000];
        ABIWriteFrame[] frames = new ABIWriteFrame[8];
        AssertNoAllocation("4096-element array", () =>
        {
            long sum = 0;
            var limits = Limits;
            for (int round = 0; round < 8; round++)
            {
                ABIWriter.TryBegin(ArraySchema, destination, frames, in limits, out ABIWriter w, out _);
                w.TryBeginDynamicArray(4096, out _);
                for (int i = 0; i < 4096; i++)
                {
                    w.TryWriteUInt64((ulong)i, out _);
                }

                w.TryEndContainer(out _);
                w.TryFinish(out int written, out _);
                sum += written;
            }

            return sum;
        });
    }

    // ------------------------------------------------------------------ failure

    [TestMethod]
    public void EveryFailurePath_AllocatesNothing()
    {
        byte[] destination = new byte[1024];
        byte[] tiny = new byte[63];
        ABIWriteFrame[] frames = new ABIWriteFrame[8];
        ABIMeasureFrame[] measureFrames = new ABIMeasureFrame[8];
        ABIEncodeLimits invalid = default;
        byte[] word = new byte[32];

        AssertNoAllocation("writer failures", () =>
        {
            long sum = 0;
            var limits = Limits;
            for (int i = 0; i < Iterations; i++)
            {
                // TryBegin failures: destination too small, invalid limits, null schema, no scratch.
                sum += ABIWriter.TryBegin(TransferSchema, tiny, frames, in limits, out ABIWriter w, out ABIStatus s) ? 1 : (int)s;
                sum += ABIWriter.TryBegin(TransferSchema, destination, frames, in invalid, out w, out s) ? 1 : (int)s;
                sum += ABIWriter.TryBegin(null!, destination, frames, in limits, out w, out s) ? 1 : (int)s;
                sum += ABIWriter.TryBegin(TransferSchema, destination, Span<ABIWriteFrame>.Empty, in limits, out w, out s) ? 1 : (int)s;

                // Type mismatch, then a faulted writer answering everything.
                ABIWriter.TryBegin(TransferSchema, destination, frames, in limits, out w, out _);
                sum += w.TryWriteBool(true, out s) ? 1 : (int)s;
                sum += w.TryWriteAddress(Vitalik, out s) ? 1 : (int)s;
                sum += w.TryFinish(out int written, out s) ? 1 : (int)s;
                sum += written;

                // Range, UTF-8, overlap, element count, scratch, early finish.
                ABIWriter.TryBegin(TinySchema, destination, frames, in limits, out w, out _);
                sum += w.TryWriteUInt64(256, out s) ? 1 : (int)s;
                ABIWriter.TryBegin(Utf8Schema, destination, frames, in limits, out w, out _);
                sum += w.TryWriteStringUtf8(BadText, out s) ? 1 : (int)s;
                ABIWriter.TryBegin(TransferSchema, destination, frames, in limits, out w, out _);
                sum += w.TryWriteAddress(destination.AsSpan(100, 20), out s) ? 1 : (int)s;
                ABIWriter.TryBegin(TinySchema, destination, frames, in limits, out w, out _);
                sum += w.TryWriteUInt256Word(word, out s) ? 1 : (int)s; // a zero word fits uint8: success, so the next call takes the faulted path below
                ABIWriter.TryBegin(ArraySchema, destination, frames.AsSpan(0, 1), in limits, out w, out _);
                sum += w.TryBeginDynamicArray(1, out s) ? 1 : (int)s; // insufficient scratch
                ABIWriter.TryBegin(ArraySchema, destination, frames, in limits, out w, out _);
                sum += w.TryBeginDynamicArray(5000, out s) ? 1 : (int)s; // element limit
                ABIWriter.TryBegin(ArraySchema, destination, frames, in limits, out w, out _);
                sum += w.TryBeginDynamicArray(-1, out s) ? 1 : (int)s;
                ABIWriter.TryBegin(TransferSchema, destination, frames, in limits, out w, out _);
                sum += w.TryFinish(out written, out s) ? 1 : (int)s; // incomplete

                // Default writer.
                ABIWriter dead = default;
                sum += dead.TryWriteUInt64(1, out s) ? 1 : (int)s;
                sum += dead.TryFinish(out written, out s) ? 1 : (int)s;
            }

            return sum;
        });

        AssertNoAllocation("measurer and call encoder failures", () =>
        {
            long sum = 0;
            var limits = Limits;
            for (int i = 0; i < Iterations; i++)
            {
                ABIMeasurer.TryBegin(TinySchema, measureFrames, in limits, out ABIMeasurer m, out _);
                sum += m.TryWriteUInt64(256, out ABIStatus s) ? 1 : (int)s;
                sum += m.TryFinish(out int length, out s) ? 1 : (int)s;
                ABIMeasurer.TryBegin(Utf8Schema, measureFrames, in limits, out m, out _);
                sum += m.TryWriteStringUtf8(BadText, out s) ? 1 : (int)s;
                sum += ABIMeasurer.TryBegin(null!, measureFrames, in limits, out m, out s) ? 1 : (int)s;

                sum += ABICallEncoder.TryBegin(TransferFunction, tiny.AsSpan(0, 40), frames, in limits, out ABICallEncoder c, out s) ? 1 : (int)s; // head does not fit
                sum += ABICallEncoder.TryBegin(TransferFunction, tiny.AsSpan(0, 3), frames, in limits, out c, out s) ? 1 : (int)s; // selector does not fit
                var small = limits with { MaxEncodedBytes = 3 };
                sum += ABICallEncoder.TryBegin(TransferFunction, destination, frames, in small, out c, out s) ? 1 : (int)s;
                sum += ABICallEncoder.TryBegin(null!, destination, frames, in limits, out c, out s) ? 1 : (int)s;
                ABICallEncoder.TryBegin(TransferFunction, destination, frames, in limits, out c, out _);
                sum += c.TryWriteUInt64(1, out s) ? 1 : (int)s; // type mismatch: the first slot is an address
                sum += c.TryFinish(out length, out s) ? 1 : (int)s;
                sum += length;
            }

            return sum;
        });
    }

    [TestMethod]
    public void ScratchFramesAndDestination_AreNotZeroedBeyondWhatIsUsed()
    {
        // 11.1: only the encoded bytes are written. A large destination and a long scratch span cost nothing extra.
        byte[] destination = new byte[1 << 20];
        Array.Fill(destination, Garbage);
        ABIWriteFrame[] frames = new ABIWriteFrame[32];
        ABIEncodeLimits limits = Limits;
        Assert.IsTrue(ABIWriter.TryBegin(TransferSchema, destination, frames, in limits, out ABIWriter w, out _));
        Assert.IsTrue(w.TryWriteAddress(Vitalik, out _));
        Assert.IsTrue(w.TryWriteUInt64(1, out _));
        Assert.IsTrue(w.TryFinish(out int written, out _));
        Assert.AreEqual(64, written);
        for (int i = written; i < destination.Length; i++)
        {
            if (destination[i] != Garbage)
            {
                Assert.Fail($"byte {i} beyond the document was modified");
            }
        }
    }
}
