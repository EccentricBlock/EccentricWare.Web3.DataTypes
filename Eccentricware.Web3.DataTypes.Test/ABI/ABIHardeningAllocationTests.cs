using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Spec 11.3 allocation assertions for the public entry points the decoder and writer allocation tests do not reach: the <see cref="ABIError"/>
/// overloads of both decoders, every leaf writer method that has no other coverage (<c>TryWriteInt64</c>, <c>TryWriteInt256Word</c>,
/// <c>TryWriteFixedBytes</c>, <c>TryWriteFunction</c> and the two scaled-word methods) on writer, measurer and call encoder on success and on each
/// failure kind, and the read-only schema properties. Same method as the sibling files: warm-up, then the smallest of five trials must allocate zero.
/// Serial: allocation counters are per thread.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ABIHardeningAllocationTests
{
    private const int Iterations = 2_000;
    private const int Trials = 5;

    private static void AssertNoAllocation(string what, Func<long> batch)
    {
        long checksum = 0;
        for (int i = 0; i < 3; i++)
        {
            checksum += batch();
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
    private static readonly ABIFunctionSchema TransferFunction = Function("transfer(address,uint256)");
    private static readonly byte[] TransferCall = Hex(ABIFixtures.TransferVitalik1Eth);

    // ------------------------------------------------------------------ decode with the diagnostic overloads

    [TestMethod]
    public void DecodeOverloadsWithABIError_AllocateNothing_OnSuccessAndFailure()
    {
        byte[] good = TransferCall[4..];
        byte[] truncated = good[..40];
        byte[] trailing = [.. good, 0x00];
        byte[] dirtyAddress = Cat(Filled(0xff), Word(1));

        AssertNoAllocation("TryDecodeComplete with ABIError", () =>
        {
            long sum = 0;
            var limits = ABIDecodeLimits.Default;
            for (int i = 0; i < Iterations; i++)
            {
                _ = ABIData.TryDecodeComplete(good, TransferSchema, in limits, out var d0, out var s0, out var e0);
                _ = ABIData.TryDecodeComplete(truncated, TransferSchema, in limits, out var d1, out var s1, out var e1);
                _ = ABIData.TryDecodeComplete(trailing, TransferSchema, in limits, out var d2, out var s2, out var e2);
                _ = ABIData.TryDecodeComplete(dirtyAddress, TransferSchema, in limits, out var d3, out var s3, out var e3);
                _ = ABIData.TryDecodeComplete(good, null!, in limits, out var d4, out var s4, out var e4);
                _ = ABIData.TryDecodeComplete(good, TransferSchema, in ZeroLimits.Value, out var d5, out var s5, out var e5);
                sum += d0.EncodedBytes.Length + d1.EncodedBytes.Length + d2.EncodedBytes.Length + d3.EncodedBytes.Length + d4.EncodedBytes.Length + d5.EncodedBytes.Length
                    + (int)s0 + (int)s1 + (int)s2 + (int)s3 + (int)s4 + (int)s5
                    + e0.DocumentOffset + e1.DocumentOffset + e2.DocumentOffset + e3.DocumentOffset + e4.SchemaNode + e5.SchemaNode;
            }

            return sum;
        });

        byte[] wrongSelector = (byte[])TransferCall.Clone();
        byte[] shortArguments = TransferCall[..40];
        wrongSelector[0] ^= 1;
        AssertNoAllocation("ABICallData.TryDecode with ABIError", () =>
        {
            long sum = 0;
            var limits = ABIDecodeLimits.Default;
            for (int i = 0; i < Iterations; i++)
            {
                _ = ABICallData.TryDecode(TransferCall, TransferFunction, in limits, out var c0, out var s0, out var e0);
                _ = ABICallData.TryDecode(wrongSelector, TransferFunction, in limits, out var c1, out var s1, out var e1);
                _ = ABICallData.TryDecode(TransferCall.AsSpan(0, 3), TransferFunction, in limits, out var c2, out var s2, out var e2);
                _ = ABICallData.TryDecode(shortArguments, TransferFunction, in limits, out var c3, out var s3, out var e3);
                _ = ABICallData.TryDecode(TransferCall, null!, in limits, out var c4, out var s4, out var e4);
                sum += c0.Selector + c1.Selector + c2.Selector + c3.Selector + c4.Selector + c0.EncodedCall.Length
                    + (int)s0 + (int)s1 + (int)s2 + (int)s3 + (int)s4 + e0.DocumentOffset + e1.DocumentOffset + e2.DocumentOffset + e3.DocumentOffset + e4.DocumentOffset;
            }

            return sum;
        });
    }

    /// <summary><c>default(ABIDecodeLimits)</c>, which is invalid: the decoder must answer InvalidArgument.</summary>
    private static class ZeroLimits
    {
        internal static readonly ABIDecodeLimits Value = default;
    }

    // ------------------------------------------------------------------ zero-width value budgets

    [TestMethod]
    public void ZeroWidthValueBudgetExhaustion_AndTheLargestAcceptedZeroWidthArray_AllocateNothing()
    {
        // Five arrays of 4,096 empty tuples: 20,486 values from 320 bytes, over the default visited budget (16,384).
        ABISchema five = CompileTuple("(()[],()[],()[],()[],()[])");
        byte[] bomb = Cat(Word(0xa0), Word(0xc0), Word(0xe0), Word(0x100), Word(0x120), Word(4096), Word(4096), Word(4096), Word(4096), Word(4096));
        ABISchema one = CompileTuple("(()[])");
        byte[] largest = Cat(Word(0x20), Word(4096));

        var limits = ABIDecodeLimits.Default;
        Assert.IsFalse(ABIData.TryDecodeComplete(bomb, five, in limits, out _, out ABIStatus status));
        Assert.AreEqual(ABIStatus.WorkLimitExceeded, status);
        Assert.IsTrue(ABIData.TryDecodeComplete(largest, one, in limits, out _, out status), status.ToString());

        AssertNoAllocation("zero-width value budget", () =>
        {
            long sum = 0;
            var l = ABIDecodeLimits.Default;
            for (int i = 0; i < 100; i++)
            {
                _ = ABIData.TryDecodeComplete(bomb, five, in l, out var d0, out var s0);
                _ = ABIData.TryDecodeComplete(largest, one, in l, out var d1, out var s1);
                sum += d0.EncodedBytes.Length + d1.EncodedBytes.Length + (int)s0 + (int)s1;
            }

            return sum;
        });
    }

    // ------------------------------------------------------------------ the remaining writer leaf methods

    /// <summary>One document touching every leaf kind the other writer allocation tests miss, followed by one failing call of each failure kind.</summary>
    private static readonly ABISchema LeafSchema = CompileTuple("(int8,int256,bytes4,function,fixed128x18,ufixed128x18)");

    private static readonly byte[] Four = [1, 2, 3, 4];
    private static readonly byte[] Target = new byte[20];
    private static readonly byte[] Word32 = new byte[32];
    private static readonly byte[] Short = new byte[31];
    private static readonly byte[] Long = new byte[33];

    private static long WriteLeaves(Span<byte> destination, Span<ABIWriteFrame> frames, in ABIEncodeLimits limits)
    {
        ABIWriter.TryBegin(LeafSchema, destination, frames, in limits, out ABIWriter w, out _);
        bool ok = w.TryWriteInt64(-5, out _) & w.TryWriteInt256Word(Word32, out _) & w.TryWriteFixedBytes(Four, out _)
            & w.TryWriteFunction(Target, 0xa9059cbb, out _) & w.TryWriteSignedScaledWord(Word32, out _) & w.TryWriteUnsignedScaledWord(Word32, out _);
        return ok && w.TryFinish(out int written, out _) ? written : -1;
    }

    private static long MeasureLeaves(Span<ABIMeasureFrame> frames, in ABIEncodeLimits limits)
    {
        ABIMeasurer.TryBegin(LeafSchema, frames, in limits, out ABIMeasurer m, out _);
        bool ok = m.TryWriteInt64(-5, out _) & m.TryWriteInt256Word(Word32, out _) & m.TryWriteFixedBytes(Four, out _)
            & m.TryWriteFunction(Target, 0xa9059cbb, out _) & m.TryWriteSignedScaledWord(Word32, out _) & m.TryWriteUnsignedScaledWord(Word32, out _);
        return ok && m.TryFinish(out int length, out _) ? length : -1;
    }

    private static long CallLeaves(Span<byte> destination, Span<ABIWriteFrame> frames, ABIFunctionSchema function, in ABIEncodeLimits limits)
    {
        ABICallEncoder.TryBegin(function, destination, frames, in limits, out ABICallEncoder c, out _);
        bool ok = c.TryWriteInt64(-5, out _) & c.TryWriteInt256Word(Word32, out _) & c.TryWriteFixedBytes(Four, out _)
            & c.TryWriteFunction(Target, 0xa9059cbb, out _) & c.TryWriteSignedScaledWord(Word32, out _) & c.TryWriteUnsignedScaledWord(Word32, out _);
        return ok && c.TryFinish(out int length, out _) ? length : -1;
    }

    [TestMethod]
    public void RemainingLeafWriters_AllocateNothing_OnWriterMeasurerAndCallEncoder()
    {
        ABIFunctionSchema function = Function("leaves(int8,int256,bytes4,function,fixed128x18,ufixed128x18)");
        var destination = new byte[512];
        var frames = new ABIWriteFrame[4];
        var measureFrames = new ABIMeasureFrame[4];
        var limits = ABIEncodeLimits.Default;

        // The sequence succeeds (six static words: 192 bytes of arguments, 196 for the call), so the measured calls are the success paths.
        Assert.AreEqual(192L, WriteLeaves(destination, frames, in limits));
        Assert.AreEqual(192L, MeasureLeaves(measureFrames, in limits));
        Assert.AreEqual(196L, CallLeaves(destination, frames, function, in limits));

        AssertNoAllocation("writer leaves", () =>
        {
            long sum = 0;
            var l = ABIEncodeLimits.Default;
            for (int i = 0; i < Iterations; i++)
            {
                sum += WriteLeaves(destination, frames, in l);
            }

            return sum;
        });
        AssertNoAllocation("measurer leaves", () =>
        {
            long sum = 0;
            var l = ABIEncodeLimits.Default;
            for (int i = 0; i < Iterations; i++)
            {
                sum += MeasureLeaves(measureFrames, in l);
            }

            return sum;
        });
        AssertNoAllocation("call encoder leaves", () =>
        {
            long sum = 0;
            var l = ABIEncodeLimits.Default;
            for (int i = 0; i < Iterations; i++)
            {
                sum += CallLeaves(destination, frames, function, in l);
            }

            return sum;
        });
    }

    [TestMethod]
    public void RemainingLeafWriters_FailurePaths_AllocateNothing()
    {
        var destination = new byte[512];
        var frames = new ABIWriteFrame[4];
        var measureFrames = new ABIMeasureFrame[4];
        ABISchema small = CompileTuple("(int8,bytes4,function,fixed8x1,ufixed8x1,int256)");

        // The statuses the failure sequences below produce (asserted once, outside the measured region), so the comments are checked.
        var l0 = ABIEncodeLimits.Default;
        ABIFunctionSchema smallFunction = Function("small(int8,bytes4,function,fixed8x1,ufixed8x1,int256)");
        {
            ABIWriter.TryBegin(small, destination, frames, in l0, out ABIWriter w, out _);
            Assert.IsFalse(w.TryWriteInt64(200, out ABIStatus s));
            Assert.AreEqual(ABIStatus.ValueOutOfRange, s);
            ABIWriter.TryBegin(small, destination, frames, in l0, out w, out _);
            w.TryWriteInt64(1, out _);
            Assert.IsFalse(w.TryWriteFixedBytes(Long, out s));
            Assert.AreEqual(ABIStatus.InvalidArgument, s);
            ABIWriter.TryBegin(small, destination, frames, in l0, out w, out _);
            w.TryWriteInt64(1, out _);
            w.TryWriteFixedBytes(Four, out _);
            Assert.IsFalse(w.TryWriteFunction(Short, 1, out s));
            Assert.AreEqual(ABIStatus.InvalidArgument, s);
            ABIWriter.TryBegin(small, destination, frames, in l0, out w, out _);
            Assert.IsFalse(w.TryWriteSignedScaledWord(Word32, out s));
            Assert.AreEqual(ABIStatus.TypeMismatch, s);
            Assert.IsFalse(w.TryWriteInt256Word(Short, out s));
            Assert.AreEqual(ABIStatus.InvalidWriterState, s);
            ABIWriter.TryBegin(small, destination, frames, in l0, out w, out _);
            w.TryWriteInt64(1, out _);
            w.TryWriteFixedBytes(Four, out _);
            w.TryWriteFunction(Target, 1, out _);
            Assert.IsFalse(w.TryWriteUnsignedScaledWord(Word32, out s));
            Assert.AreEqual(ABIStatus.TypeMismatch, s);
            ABIWriter.TryBegin(small, destination, frames, in l0, out w, out _);
            w.TryWriteInt64(1, out _);
            w.TryWriteFixedBytes(Four, out _);
            w.TryWriteFunction(Target, 1, out _);
            w.TryWriteSignedScaledWord(Word32, out _);
            Assert.IsFalse(w.TryWriteUnsignedScaledWord(Long, out s));
            Assert.AreEqual(ABIStatus.InvalidArgument, s);
            ABIWriter.TryBegin(small, destination, frames, in l0, out w, out _);
            w.TryWriteInt64(1, out _);
            w.TryWriteFixedBytes(Four, out _);
            w.TryWriteFunction(Target, 1, out _);
            w.TryWriteSignedScaledWord(Word32, out _);
            w.TryWriteUnsignedScaledWord(Word32, out _);
            Assert.IsFalse(w.TryWriteInt256Word(Long, out s));
            Assert.AreEqual(ABIStatus.InvalidArgument, s);
        }

        AssertNoAllocation("call encoder leaf failures", () =>
        {
            long sum = 0;
            var l = ABIEncodeLimits.Default;
            for (int i = 0; i < Iterations; i++)
            {
                ABICallEncoder.TryBegin(smallFunction, destination, frames, in l, out ABICallEncoder c, out _);
                sum += c.TryWriteInt64(200, out var s) ? 1 : (int)s;
                ABICallEncoder.TryBegin(smallFunction, destination, frames, in l, out c, out _);
                sum += c.TryWriteInt64(1, out s) ? 1 : (int)s;
                sum += c.TryWriteFixedBytes(Long, out s) ? 1 : (int)s;
                ABICallEncoder.TryBegin(smallFunction, destination, frames, in l, out c, out _);
                sum += c.TryWriteInt64(1, out s) ? 1 : (int)s;
                sum += c.TryWriteFixedBytes(Four, out s) ? 1 : (int)s;
                sum += c.TryWriteFunction(Short, 1, out s) ? 1 : (int)s;
                ABICallEncoder.TryBegin(smallFunction, destination, frames, in l, out c, out _);
                sum += c.TryWriteUnsignedScaledWord(Word32, out s) ? 1 : (int)s;
                sum += c.TryWriteInt256Word(Long, out s) ? 1 : (int)s;
            }

            return sum;
        });

        // Each statement fails for a different reason: value out of range, wrong source length (short and long), wrong slot kind.
        AssertNoAllocation("writer leaf failures", () =>
        {
            long sum = 0;
            var l = ABIEncodeLimits.Default;
            for (int i = 0; i < Iterations; i++)
            {
                ABIWriter.TryBegin(small, destination, frames, in l, out ABIWriter w, out _);
                sum += w.TryWriteInt64(200, out var s) ? 1 : (int)s;                       // ValueOutOfRange (int8)
                ABIWriter.TryBegin(small, destination, frames, in l, out w, out _);
                sum += w.TryWriteInt64(1, out s) ? 1 : (int)s;
                sum += w.TryWriteFixedBytes(Long, out s) ? 1 : (int)s;                     // InvalidArgument (33 bytes into bytes4)
                ABIWriter.TryBegin(small, destination, frames, in l, out w, out _);
                sum += w.TryWriteInt64(1, out s) ? 1 : (int)s;
                sum += w.TryWriteFixedBytes(Four, out s) ? 1 : (int)s;
                sum += w.TryWriteFunction(Short, 1, out s) ? 1 : (int)s;                   // InvalidArgument (address of 31 bytes)
                ABIWriter.TryBegin(small, destination, frames, in l, out w, out _);
                sum += w.TryWriteSignedScaledWord(Word32, out s) ? 1 : (int)s;             // TypeMismatch (int8 slot)
                sum += w.TryWriteInt256Word(Short, out s) ? 1 : (int)s;                    // InvalidWriterState (faulted)
                ABIWriter.TryBegin(small, destination, frames, in l, out w, out _);
                sum += w.TryWriteInt64(1, out s) ? 1 : (int)s;
                sum += w.TryWriteFixedBytes(Four, out s) ? 1 : (int)s;
                sum += w.TryWriteFunction(Target, 1, out s) ? 1 : (int)s;
                sum += w.TryWriteUnsignedScaledWord(Word32, out s) ? 1 : (int)s;           // TypeMismatch (fixed slot wants the signed method)
                ABIWriter.TryBegin(small, destination, frames, in l, out w, out _);
                sum += w.TryWriteInt64(1, out s) ? 1 : (int)s;
                sum += w.TryWriteFixedBytes(Four, out s) ? 1 : (int)s;
                sum += w.TryWriteFunction(Target, 1, out s) ? 1 : (int)s;
                sum += w.TryWriteSignedScaledWord(Word32, out s) ? 1 : (int)s;
                sum += w.TryWriteUnsignedScaledWord(Long, out s) ? 1 : (int)s;             // InvalidArgument (33-byte word)
            }

            return sum;
        });

        AssertNoAllocation("measurer leaf failures", () =>
        {
            long sum = 0;
            var l = ABIEncodeLimits.Default;
            for (int i = 0; i < Iterations; i++)
            {
                ABIMeasurer.TryBegin(small, measureFrames, in l, out ABIMeasurer m, out _);
                sum += m.TryWriteInt64(200, out var s) ? 1 : (int)s;
                ABIMeasurer.TryBegin(small, measureFrames, in l, out m, out _);
                sum += m.TryWriteInt64(1, out s) ? 1 : (int)s;
                sum += m.TryWriteFixedBytes(Long, out s) ? 1 : (int)s;
                ABIMeasurer.TryBegin(small, measureFrames, in l, out m, out _);
                sum += m.TryWriteInt64(1, out s) ? 1 : (int)s;
                sum += m.TryWriteFixedBytes(Four, out s) ? 1 : (int)s;
                sum += m.TryWriteFunction(Short, 1, out s) ? 1 : (int)s;
                ABIMeasurer.TryBegin(small, measureFrames, in l, out m, out _);
                sum += m.TryWriteSignedScaledWord(Word32, out s) ? 1 : (int)s;
                sum += m.TryWriteUnsignedScaledWord(Long, out s) ? 1 : (int)s;
            }

            return sum;
        });
    }

    // ------------------------------------------------------------------ schema properties

    [TestMethod]
    public void SchemaAndFunctionProperties_AllocateNothing()
    {
        ABISchema schema = CompileTuple("(address,uint256[],(bytes,bool))");
        ABIFunctionSchema function = Function("f(address,uint256[],(bytes,bool))");
        AssertNoAllocation("schema properties", () =>
        {
            long sum = 0;
            for (int i = 0; i < Iterations; i++)
            {
                sum += schema.CanonicalType.Length + schema.ArgumentCount + (schema.IsDynamic ? 1 : 0)
                    + function.Name.Length + function.CanonicalSignature.Length + function.Selector + function.InputSchema.ArgumentCount;
            }

            return sum;
        });
    }
}
