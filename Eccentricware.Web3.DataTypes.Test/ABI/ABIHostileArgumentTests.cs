using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Hostile ARGUMENTS (not hostile documents) to the public <c>Try*</c> entry points: null schemas and functions, default limits, default views, a scratch span
/// that claims more frames than any byte view can describe, overlapping spans, extreme path items. The contract (spec 5.1, 5.2: "invalid API use can fail with
/// statuses, not uncontrolled exceptions") is a status, never an exception. Regression for <c>ABIWriter.TryBegin</c>, whose byte view of the scratch threw
/// <see cref="OverflowException"/> for more than about 41 million frames.
/// </summary>
[TestClass]
public sealed class ABIHostileArgumentTests
{
    private static readonly int FrameBytes = Unsafe.SizeOf<ABIWriteFrame>();

    /// <summary>The first frame count whose byte length no longer fits an <see cref="int"/>: <c>MemoryMarshal.AsBytes</c> throws from here on.</summary>
    private static int FirstOverflowingFrameCount => int.MaxValue / FrameBytes + 1;

    private static readonly ABISchema OneUInt = ABISchemaTestSupport.CompileTuple("(uint256)");

    // ------------------------------------------------------------------ a scratch span that claims too many frames

    public static IEnumerable<object[]> ClaimedFrameCounts =>
    [
        [32], [1_000], [1_000_000], [FirstOverflowingFrameCount - 1], [FirstOverflowingFrameCount], [FirstOverflowingFrameCount + 1], [100_000_000], [int.MaxValue - 1], [int.MaxValue],
    ];

    [TestMethod]
    [DynamicData(nameof(ClaimedFrameCounts))]
    public void AScratchSpanClaimingMoreFramesThanAnyByteViewCanDescribe_IsAnsweredWithAStatus_AndOnlyTheUsableFramesAreTouched(int claimed)
    {
        // Real storage for 32 frames (the most any limits can use); the span CLAIMS `claimed` frames. A correct TryBegin touches at most
        // min(MaxValueDepth, 32) of them and never throws; with the default depth of 16 the frames from 16 on stay all-zero.
        var limits = ABIEncodeLimits.Default;
        byte[] destination = new byte[64];
        ABIWriteFrame[] backing = new ABIWriteFrame[32];
        Span<ABIWriteFrame> huge = MemoryMarshal.CreateSpan(ref backing[0], claimed);

        Assert.IsTrue(ABIWriter.TryBegin(OneUInt, destination, huge, in limits, out ABIWriter w, out ABIStatus status), $"writer, {claimed}: {status}");
        Assert.IsTrue(w.TryWriteUInt64(7, out status));
        Assert.IsTrue(w.TryFinish(out int written, out status));
        Assert.AreEqual(32, written);
        Assert.AreEqual(7, destination[31]);
        Assert.IsTrue(MemoryMarshal.AsBytes(backing.AsSpan(limits.MaxValueDepth)).ToArray().All(b => b == 0), "frames beyond MaxValueDepth were touched");

        ABIWriteFrame[] callBacking = new ABIWriteFrame[32];
        Span<ABIWriteFrame> callHuge = MemoryMarshal.CreateSpan(ref callBacking[0], claimed);
        ABIFunctionSchema f = Function("g(uint256)");
        Assert.IsTrue(ABICallEncoder.TryBegin(f, new byte[36], callHuge, in limits, out ABICallEncoder e, out status), $"call encoder, {claimed}: {status}");
        Assert.IsTrue(e.TryWriteUInt64(7, out status));
        Assert.IsTrue(e.TryFinish(out written, out status));
        Assert.AreEqual(36, written);
        Assert.IsTrue(MemoryMarshal.AsBytes(callBacking.AsSpan(limits.MaxValueDepth)).ToArray().All(b => b == 0));

        ABIMeasureFrame[] measureBacking = new ABIMeasureFrame[32];
        Span<ABIMeasureFrame> measureHuge = MemoryMarshal.CreateSpan(ref measureBacking[0], claimed);
        Assert.IsTrue(ABIMeasurer.TryBegin(OneUInt, measureHuge, in limits, out ABIMeasurer m, out status), $"measurer, {claimed}: {status}");
        Assert.IsTrue(m.TryWriteUInt64(7, out status));
        Assert.IsTrue(m.TryFinish(out written, out status));
        Assert.AreEqual(32, written);
        Assert.IsTrue(MemoryMarshal.AsBytes(measureBacking.AsSpan(limits.MaxValueDepth)).ToArray().All(b => b == 0));
    }

    [TestMethod]
    public void AHugeClaimedScratch_WithTheHardMaximumDepth_UsesAtMostThirtyTwoFrames()
    {
        // 31 nested tuples around one uint256: the leaf sits at depth 32, the hard maximum; 31 container frames are needed.
        ABISchema deep = ABISchemaTestSupport.CompileTuple(new string('(', 31) + "uint256" + new string(')', 31));
        var limits = new ABIEncodeLimits(262_144, ABIEncodeLimits.HardMaxValueDepth, 4096, 16_384, 100_000);
        ABIWriteFrame[] backing = new ABIWriteFrame[40];
        Span<ABIWriteFrame> huge = MemoryMarshal.CreateSpan(ref backing[0], FirstOverflowingFrameCount + 1);
        Assert.IsTrue(ABIWriter.TryBegin(deep, new byte[64], huge, in limits, out ABIWriter w, out ABIStatus status), status.ToString());
        Assert.IsTrue(MemoryMarshal.AsBytes(backing.AsSpan(ABIEncodeLimits.HardMaxValueDepth)).ToArray().All(b => b == 0), "no frame beyond the hard maximum depth is ever touched");
        Assert.IsFalse(w.TryFinish(out _, out status), "nothing was written");
    }

    [TestMethod]
    public void ScratchOverlap_IsRefusedWhereTheWriterCanTouchIt_AndIgnoredBeyondTheFramesItCanUse()
    {
        var limits = ABIEncodeLimits.Default; // MaxValueDepth 16: only the first 16 frames are ever used
        byte[] arena = new byte[FrameBytes * 32 + 4096];
        Span<ABIWriteFrame> scratch = MemoryMarshal.Cast<byte, ABIWriteFrame>(arena.AsSpan(0, FrameBytes * 32));

        // The destination covers frame 0: the frames would corrupt the output and vice versa.
        Assert.IsFalse(ABIWriter.TryBegin(OneUInt, arena.AsSpan(0, 64), scratch, in limits, out ABIWriter w, out ABIStatus status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.IsFalse(ABIWriter.TryBegin(OneUInt, arena.AsSpan(FrameBytes * 15, 64), scratch, in limits, out w, out status), "the last usable frame");
        Assert.AreEqual(ABIStatus.InvalidArgument, status);

        // The destination overlaps only frames the writer can never use (index 17 and above for depth 16): nothing can collide, so it is accepted.
        Assert.IsTrue(ABIWriter.TryBegin(OneUInt, arena.AsSpan(FrameBytes * 17, 64), scratch, in limits, out w, out status), status.ToString());
        Assert.IsTrue(w.TryWriteUInt64(1, out status));
        Assert.IsTrue(w.TryFinish(out int written, out status));
        Assert.AreEqual(32, written);

        // The same through the call encoder (its whole call buffer is the guard, selector included).
        ABIFunctionSchema f = Function("g(uint256)");
        Assert.IsFalse(ABICallEncoder.TryBegin(f, arena.AsSpan(FrameBytes * 2, 36), scratch, in limits, out ABICallEncoder e, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
    }

    // ------------------------------------------------------------------ null schemas, default limits, default views

    [TestMethod]
    public void NullSchemasFunctionsAndDefaultLimits_AreStatuses_ForEveryEntryPoint()
    {
        var goodEncode = ABIEncodeLimits.Default;
        var goodDecode = ABIDecodeLimits.Default;
        ABIEncodeLimits badEncode = default;
        ABIDecodeLimits badDecode = default;
        ABIFunctionSchema f = Function("g(uint256)");
        Span<ABIWriteFrame> frames = new ABIWriteFrame[4];
        Span<ABIMeasureFrame> measureFrames = new ABIMeasureFrame[4];

        Assert.IsFalse(ABIWriter.TryBegin(null!, new byte[64], frames, in goodEncode, out ABIWriter w, out ABIStatus status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.IsFalse(ABIWriter.TryBegin(OneUInt, new byte[64], frames, in badEncode, out w, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.IsFalse(ABIWriter.TryBegin(OneUInt, new byte[64], default, in goodEncode, out w, out status));
        Assert.AreEqual(ABIStatus.InsufficientScratch, status);
        Assert.IsFalse(ABIWriter.TryBegin(OneUInt, default, frames, in goodEncode, out w, out status));
        Assert.AreEqual(ABIStatus.InsufficientDestination, status);

        Assert.IsFalse(ABICallEncoder.TryBegin(null!, new byte[64], frames, in goodEncode, out ABICallEncoder e, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.IsFalse(ABICallEncoder.TryBegin(f, new byte[64], frames, in badEncode, out e, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.IsFalse(ABICallEncoder.TryBegin(f, default, frames, in goodEncode, out e, out status));
        Assert.AreEqual(ABIStatus.InsufficientDestination, status);

        Assert.IsFalse(ABIMeasurer.TryBegin(null!, measureFrames, in goodEncode, out ABIMeasurer m, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.IsFalse(ABIMeasurer.TryBegin(OneUInt, measureFrames, in badEncode, out m, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.IsFalse(ABIMeasurer.TryBegin(OneUInt, default, in goodEncode, out m, out status));
        Assert.AreEqual(ABIStatus.InsufficientScratch, status);

        // A writer that failed to begin is the invalid default writer and answers every call with a status.
        Assert.IsFalse(w.TryWriteUInt64(1, out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.IsFalse(w.TryFinish(out int length, out status));
        Assert.AreEqual(0, length);

        Assert.IsFalse(ABIData.TryDecodeComplete(new byte[32], null!, in goodDecode, out ABIData data, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.IsFalse(data.IsValid);
        Assert.IsFalse(ABIData.TryDecodeComplete(new byte[32], OneUInt, in badDecode, out data, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.IsFalse(ABICallData.TryDecode(new byte[36], null!, in goodDecode, out ABICallData call, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.IsFalse(ABICallData.TryDecode(new byte[36], f, in badDecode, out call, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.IsFalse(call.IsValid);

        var compile = ABICompileLimits.Default;
        ABICompileLimits badCompile = default;
        Assert.IsFalse(ABISchemaCompiler.TryCompileArguments(null!, in compile, out ABISchema? schema, out ABISchemaError error));
        Assert.IsNull(schema);
        Assert.AreEqual(ABIStatus.InvalidArgument, error.Status);
        Assert.IsFalse(ABISchemaCompiler.TryCompileArguments(new ABITupleDefinition(), in badCompile, out schema, out error));
        Assert.AreEqual(ABIStatus.InvalidArgument, error.Status);
        Assert.IsFalse(ABISchemaCompiler.TryCompileFunction(null!, new ABITupleDefinition(), in compile, out ABIFunctionSchema? function, out error));
        Assert.IsNull(function);
        Assert.IsFalse(ABISchemaCompiler.TryCompileFunction("g", null!, in compile, out function, out error));
        Assert.IsFalse(ABISchemaCompiler.TryCompileFunctionSignature(null!, in compile, out function, out error));
        Assert.IsNull(function);
        Assert.IsFalse(ABISchemaCompiler.TryCompileFunctionSignature(string.Empty, in compile, out function, out error));
        Assert.IsFalse(ABISchemaCompiler.TryCompileFunctionSignature("g(uint256)", in badCompile, out function, out error));
    }

    // ------------------------------------------------------------------ extreme path items and overlapping sources

    [TestMethod]
    public void ExtremePathsAndAHugePathLength_AreStatuses_OnAValidViewAndOnADefaultView()
    {
        ABIFunctionSchema f = Function("g(uint256,uint256[],bytes)");
        byte[] document = Hex("0000000000000000000000000000000000000000000000000000000000000001"
            + "0000000000000000000000000000000000000000000000000000000000000060"
            + "00000000000000000000000000000000000000000000000000000000000000a0"
            + "0000000000000000000000000000000000000000000000000000000000000001"
            + "0000000000000000000000000000000000000000000000000000000000000002"
            + "0000000000000000000000000000000000000000000000000000000000000000");
        var limits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABIData.TryDecodeComplete(document, f.InputSchema, in limits, out ABIData data, out ABIStatus status), status.ToString());
        ABIData invalid = default;

        int[][] paths =
        [
            [int.MinValue], [int.MaxValue], [-1], [3], [0, 0], [1, int.MaxValue], [1, -1], [1, 0, 0], [2, 0], [0, int.MaxValue, int.MinValue],
            Enumerable.Repeat(0, 100_000).ToArray(), Enumerable.Repeat(int.MaxValue, 100_000).ToArray(),
        ];
        foreach (int[] path in paths)
        {
            SweepEveryAccessor(in data, path);
            SweepEveryAccessor(in invalid, path);
        }

        // The deep paths fail on the very first step into a primitive: a hostile path length costs nothing like its length.
        Assert.IsFalse(data.TryLocate(paths[^2], out _, out status));
        Assert.AreEqual(ABIStatus.InvalidPath, status);
    }

    private static void SweepEveryAccessor(in ABIData view, int[] path)
    {
        _ = view.TryLocate(path, out _, out _);
        _ = view.TryGetArrayLength(path, out _, out _);
        _ = view.TryGetWord(path, out _, out _);
        _ = view.TryGetUInt64(path, out _, out _);
        _ = view.TryGetInt64(path, out _, out _);
        _ = view.TryGetAddress(path, out ReadOnlySpan<byte> _, out _);
        _ = view.TryGetBool(path, out _, out _);
        _ = view.TryGetBytePayload(path, out _, out _, out _);
        _ = view.TryGetStringUtf8Validated(path, out _, out _, out _);
        _ = view.TryGetFixedBytes(path, out _, out _);
        _ = view.TryGetFunction(path, out ReadOnlySpan<byte> _, out uint _, out _);
        _ = view.TryGetSignedScaledWord(path, out _, out _, out _);
        _ = view.TryGetUnsignedScaledWord(path, out _, out _, out _);
        _ = view.TryGetUInt256Word(path, out _, out _);
        _ = view.TryGetInt256Word(path, out _, out _);
        _ = view.TryGetUInt256(path, out _, out _);
        _ = view.TryGetInt256(path, out _, out _);
        _ = view.TryGetAddress(path, out Address _, out _);
        _ = view.TryGetBytes32(path, out _, out _);
        _ = view.TryGetBytes4(path, out _, out _);
        _ = view.TryGetFunction(path, out Address _, out FunctionSelector _, out _);
    }

    [TestMethod]
    public void SourcesOverlappingTheDestination_AreStatuses_AtEveryAlignmentIncludingTheEdges()
    {
        ABIFunctionSchema f = Function("g(bytes)");
        var limits = ABIEncodeLimits.Default;
        byte[] buffer = new byte[2048]; // roomy: capacity is checked before overlap (design note 42), so the overlap status is what the test sees
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[2];
        foreach (int start in new[] { 0, 1, 31, 32, 63, 64, 100, 200, 255, 1000, 2047 })
        {
            foreach (int length in new[] { 0, 1, 31, 32, 33, 255 })
            {
                if (start + length > buffer.Length)
                {
                    continue;
                }

                Assert.IsTrue(ABIWriter.TryBegin(f.InputSchema, buffer, frames, in limits, out ABIWriter w, out ABIStatus status));
                bool ok = w.TryWriteBytes(buffer.AsSpan(start, length), out status);
                Assert.AreEqual(length == 0, ok, $"start {start} length {length}: a zero-length source never overlaps");
                Assert.AreEqual(ok ? ABIStatus.Success : ABIStatus.SourceOverlapsDestination, status, $"start {start} length {length}");
            }
        }
    }
}
