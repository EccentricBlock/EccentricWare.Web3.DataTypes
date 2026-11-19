using System.Runtime.InteropServices;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIWriteTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// The scratch frames are caller-writable memory (spec 7.1: callers must not modify them, and cannot forge an in-progress state). Whatever a
/// caller, a bug or a stale copy puts into them, no public member of the writer, the measurer or the call encoder may throw, write outside the
/// destination, or report a length that is not a real position: the answer is a normal status (spec 5.2: invalid API use fails with statuses).
/// </summary>
[TestClass]
public sealed class ABIWriterForgedScratchTests
{
    private const int Canary = 64;
    private const byte CanaryByte = 0xC3;

    private static readonly int[] Specials =
    [
        0, 1, 2, 31, 32, 33, 63, 64, 65, 96, 255, 256, 257, 1000, -1, -2, -32, int.MaxValue, int.MaxValue - 1, int.MaxValue - 31, int.MinValue, int.MinValue + 1,
    ];

    private static Ev RandomEvent(DeterministicRng rng)
    {
        byte[] Bytes(int max) => rng.Bytes(rng.Next(max + 1));
        return rng.Next(16) switch
        {
            0 => Ev.U64(rng.NextUInt64()),
            1 => Ev.I64((long)rng.NextUInt64()),
            2 => Ev.Uint256(rng.Bytes(rng.Next(4) == 0 ? rng.Next(40) : 32)),
            3 => Ev.Int256(rng.Bytes(rng.Next(4) == 0 ? rng.Next(40) : 32)),
            4 => Ev.Addr(rng.Bytes(rng.Next(4) == 0 ? rng.Next(40) : 20)),
            5 => Ev.Bool(rng.Next(2) == 0),
            6 => Ev.FixedB(Bytes(36)),
            7 => Ev.Func(rng.Bytes(rng.Next(4) == 0 ? rng.Next(40) : 20), (uint)rng.NextUInt64()),
            8 => Ev.Blob(Bytes(70)),
            9 => Ev.Text(Bytes(20)),
            10 => new Ev(EvKind.SignedScaled, rng.Bytes(32)),
            11 => new Ev(EvKind.UnsignedScaled, rng.Bytes(32)),
            12 => Ev.Begin("tuple"),
            13 => Ev.Begin("fixed"),
            14 => Ev.Begin("dynamic", rng.Next(8) - 1),
            _ => Ev.End,
        };
    }

    private static readonly string[] Schemas =
    [
        "()", "(uint256)", "(bytes)", "(address,uint256)", "(uint256[])", "(bytes[])", "((uint256,bytes)[2])", "(uint256[][],string[])", "(uint256[][][],string[][])",
        "(uint256,(string,(uint8,bytes)[],address),uint256[2])", "(()[])", "(((uint256)))", "(bytes4,function,fixed128x18,ufixed128x18,bool)",
    ];

    /// <summary>Overwrites the caller-visible scratch in one of several ways.</summary>
    private static void Forge(Span<ABIWriteFrame> scratch, DeterministicRng rng, Span<ABIWriteFrame> foreign)
    {
        Span<ABIEncodeFrame> frames = MemoryMarshal.Cast<ABIWriteFrame, ABIEncodeFrame>(scratch);
        switch (rng.Next(5))
        {
            case 0: // random bytes everywhere
                rng.Bytes(MemoryMarshal.AsBytes(scratch).Length).CopyTo(MemoryMarshal.AsBytes(scratch));
                break;
            case 1: // one random field of one random frame set to an extreme or plausible value
                {
                    ref ABIEncodeFrame frame = ref frames[rng.Next(frames.Length)];
                    Span<int> fields = MemoryMarshal.Cast<ABIEncodeFrame, int>(MemoryMarshal.CreateSpan(ref frame, 1));
                    fields[rng.Next(fields.Length)] = Specials[rng.Next(Specials.Length)];
                    break;
                }
            case 2: // every field of every frame from the small set (frames that look almost real)
                foreach (ref ABIEncodeFrame frame in frames)
                {
                    foreach (ref int field in MemoryMarshal.Cast<ABIEncodeFrame, int>(MemoryMarshal.CreateSpan(ref frame, 1)))
                    {
                        field = Specials[rng.Next(Specials.Length)];
                    }
                }

                break;
            case 3: // byte copy of another writer's frames (a different schema and progress)
                MemoryMarshal.AsBytes(foreign).Slice(0, Math.Min(MemoryMarshal.AsBytes(foreign).Length, MemoryMarshal.AsBytes(scratch).Length)).CopyTo(MemoryMarshal.AsBytes(scratch));
                break;
            default: // keep the state word valid and the rest random: Open, matching epoch is not known, so scramble the cursors only
                foreach (ref ABIEncodeFrame frame in frames)
                {
                    frame.HeadCursor = Specials[rng.Next(Specials.Length)];
                    frame.TailCursor = Specials[rng.Next(Specials.Length)];
                    frame.NextChild = rng.Next(4) - 1;
                    frame.Top = rng.Next(frames.Length + 2) - 1;
                }

                break;
        }
    }

    private static List<Ev> PlannedEvents(string tuple, DeterministicRng rng)
    {
        RTupleType type = ABIReference.ParseTuple(tuple);
        List<Ev> plan = Script(type, ABIReferenceGenerator.RandomValue(rng, type), rawStrings: true);
        for (int i = 0; i < plan.Count; i++)
        {
            if (rng.Next(7) == 0)
            {
                plan[i] = RandomEvent(rng);
            }
        }

        for (int i = rng.Next(4); i > 0; i--)
        {
            plan.Add(RandomEvent(rng));
        }

        return plan;
    }

    private static void AssertNormal(ABIStatus status, string label) => Assert.IsTrue(Enum.IsDefined(status), $"{label}: {status}");

    private static void AssertCanaries(byte[] backing, int destinationLength, string label)
    {
        for (int i = 0; i < Canary; i++)
        {
            Assert.AreEqual(CanaryByte, backing[i], $"{label}: byte {i} before the destination was written");
            Assert.AreEqual(CanaryByte, backing[Canary + destinationLength + i], $"{label}: byte {i} after the destination was written");
        }
    }

    private static byte[] NewBacking(int destinationLength)
    {
        byte[] backing = new byte[Canary + destinationLength + Canary];
        Array.Fill(backing, CanaryByte);
        return backing;
    }

    [TestMethod]
    public void ForgedWriterFrames_NeverThrow_NeverWriteOutsideTheDestination_AndAnswerWithStatuses()
    {
        int rejected = 0;
        int accepted = 0;
        for (ulong seed = 1; seed <= 600; seed++)
        {
            var rng = new DeterministicRng(seed * 7919);
            string tuple = Schemas[rng.Next(Schemas.Length)];
            ABISchema schema = CompileTuple(tuple);
            int destinationLength = 32 * (1 + rng.Next(12));
            byte[] backing = NewBacking(destinationLength);
            var scratch = new ABIWriteFrame[32];
            var foreign = new ABIWriteFrame[32];
            ABIEncodeLimits limits = rng.Next(3) == 0 ? EncLimits(bytes: 64 + rng.Next(300), depth: 2 + rng.Next(5), array: 1 + rng.Next(6), values: 3 + rng.Next(40), work: 5 + rng.Next(120)) : GenerousEncode;
            string label = $"seed {seed * 7919} {tuple}";

            // A foreign writer, partly driven, whose frames are later copied over this one's.
            if (ABIWriter.TryBegin(CompileTuple(Schemas[rng.Next(Schemas.Length)]), new byte[512], foreign, in limits, out ABIWriter other, out _))
            {
                for (int i = 0; i < rng.Next(8); i++)
                {
                    Apply(ref other, RandomEvent(rng), out _);
                }
            }

            if (!ABIWriter.TryBegin(schema, backing.AsSpan(Canary, destinationLength), scratch, in limits, out ABIWriter w, out ABIStatus begin))
            {
                AssertNormal(begin, label);
                continue;
            }

            // Mostly the events of a real document for the schema (so deep, plausible states are reached), some of them replaced by random events.
            List<Ev> plan = PlannedEvents(tuple, rng);
            int steps = plan.Count;
            int forgeAt = rng.Next(steps + 1);
            for (int step = 0; step < steps; step++)
            {
                if (step == forgeAt)
                {
                    Forge(scratch, rng, foreign);
                }

                bool ok = Apply(ref w, plan[step], out ABIStatus status);
                AssertNormal(status, label);
                (ok ? ref accepted : ref rejected)++;
                AssertCanaries(backing, destinationLength, $"{label} step {step}");
            }

            bool finished = w.TryFinish(out int written, out ABIStatus finish);
            AssertNormal(finish, label);
            Assert.IsTrue(written >= 0 && written <= destinationLength, $"{label}: bytesWritten {written} is not a position inside the {destinationLength}-byte destination");
            if (!finished)
            {
                Assert.AreEqual(0, written, label);
            }

            AssertCanaries(backing, destinationLength, label);
        }

        Assert.IsTrue(rejected > 1000 && accepted > 300, $"accepted {accepted}, rejected {rejected}");
    }

    [TestMethod]
    public void ForgedMeasurerAndCallEncoderFrames_NeverThrow_AndAnswerWithStatuses()
    {
        for (ulong seed = 1; seed <= 400; seed++)
        {
            var rng = new DeterministicRng(seed * 104_729);
            string tuple = Schemas[rng.Next(Schemas.Length)];
            ABISchema schema = CompileTuple(tuple);
            ABIFunctionSchema function = Function("f" + tuple);
            ABIEncodeLimits limits = rng.Next(3) == 0 ? EncLimits(bytes: 64 + rng.Next(300), depth: 2 + rng.Next(5), array: 1 + rng.Next(6), values: 3 + rng.Next(40), work: 5 + rng.Next(120)) : GenerousEncode;
            string label = $"seed {seed * 104_729} {tuple}";

            // Measurer: no destination at all.
            var measureScratch = new ABIMeasureFrame[32];
            if (ABIMeasurer.TryBegin(schema, measureScratch, in limits, out ABIMeasurer m, out _))
            {
                List<Ev> measurePlan = PlannedEvents(tuple, rng);
                for (int step = 0; step < measurePlan.Count; step++)
                {
                    if (step == 3)
                    {
                        Span<ABIWriteFrame> asWrite = MemoryMarshal.Cast<ABIMeasureFrame, ABIWriteFrame>(measureScratch.AsSpan());
                        Forge(asWrite, rng, new ABIWriteFrame[32]);
                    }

                    Apply(ref m, measurePlan[step], out ABIStatus status);
                    AssertNormal(status, label);
                }

                bool finished = m.TryFinish(out int length, out ABIStatus finish);
                AssertNormal(finish, label);
                Assert.IsTrue(length >= 0 && length <= limits.MaxEncodedBytes, $"{label}: measured length {length}");
                Assert.IsTrue(finished || length == 0, label);
            }

            // Call encoder: the selector area is part of the guarded buffer.
            int destinationLength = 4 + 32 * (1 + rng.Next(12));
            byte[] backing = NewBacking(destinationLength);
            var scratch = new ABIWriteFrame[32];
            if (ABICallEncoder.TryBegin(function, backing.AsSpan(Canary, destinationLength), scratch, in limits, out ABICallEncoder c, out _))
            {
                List<Ev> callPlan = PlannedEvents(tuple, rng);
                for (int step = 0; step < callPlan.Count; step++)
                {
                    if (step == 3)
                    {
                        Forge(scratch, rng, new ABIWriteFrame[32]);
                    }

                    Apply(ref c, callPlan[step], out ABIStatus status);
                    AssertNormal(status, label);
                    AssertCanaries(backing, destinationLength, $"{label} call step {step}");
                }

                bool finished = c.TryFinish(out int written, out ABIStatus finish);
                AssertNormal(finish, label);
                Assert.IsTrue(written >= 0 && written <= destinationLength, $"{label}: call length {written}");
                Assert.IsTrue(finished || written == 0, label);
            }
        }
    }

    [TestMethod]
    public void AFrameCopiedFromAnotherWriter_CannotMakeAShortScratchWriterIndexOutOfRange()
    {
        // Writer A is inside an array (Top = 1). Its root frame, copied byte for byte over a writer B whose scratch has fewer frames,
        // used to make B index past its scratch (IndexOutOfRangeException). B now answers with a status and writes nothing.
        ABISchema schema = CompileTuple("(bytes[])");
        var limits = GenerousEncode;
        var framesA = new ABIWriteFrame[4];
        Assert.IsTrue(ABIWriter.TryBegin(schema, new byte[512], framesA, in limits, out ABIWriter a, out _));
        Assert.IsTrue(a.TryBeginDynamicArray(1, out _));

        var framesB = new ABIWriteFrame[2];
        byte[] destinationB = new byte[512];
        Assert.IsTrue(ABIWriter.TryBegin(schema, destinationB, framesB, in limits, out ABIWriter b, out _));
        int ownEpoch = MemoryMarshal.Cast<ABIWriteFrame, ABIEncodeFrame>(framesB.AsSpan())[0].Epoch;
        Span<ABIEncodeFrame> forgedB = MemoryMarshal.Cast<ABIWriteFrame, ABIEncodeFrame>(framesB.AsSpan());
        Span<ABIEncodeFrame> realA = MemoryMarshal.Cast<ABIWriteFrame, ABIEncodeFrame>(framesA.AsSpan());
        realA[0].Top = 9; // beyond B's scratch
        forgedB[0] = realA[0];
        forgedB[0].Epoch = ownEpoch; // keep B's own epoch, as a deliberate forger would

        Assert.IsFalse(b.TryWriteBytes([1, 2, 3], out ABIStatus status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.IsTrue(destinationB.All(x => x == 0), "no byte may be written through a forged frame");
        Assert.IsFalse(b.TryFinish(out int written, out status));
        Assert.AreEqual(0, written);
    }

    [TestMethod]
    public void ForgedCursors_AreRefused_BeforeAnySliceOrSumIsFormed()
    {
        ABISchema schema = CompileTuple("(uint256,bytes)");
        var limits = GenerousEncode;
        foreach (int bad in new[] { -1, int.MinValue, int.MaxValue, int.MaxValue - 31, 100_000 })
        {
            // HeadCursor: where the next head slot goes. A slot outside the destination must be refused by the very next write.
            byte[] backing = NewBacking(256);
            var scratch = new ABIWriteFrame[4];
            Assert.IsTrue(ABIWriter.TryBegin(schema, backing.AsSpan(Canary, 256), scratch, in limits, out ABIWriter w, out _));
            MemoryMarshal.Cast<ABIWriteFrame, ABIEncodeFrame>(scratch.AsSpan())[0].HeadCursor = bad;
            Assert.IsFalse(w.TryWriteUInt64(1, out ABIStatus status), $"HeadCursor = {bad}");
            Assert.AreEqual(ABIStatus.InvalidWriterState, status, $"HeadCursor = {bad}");
            AssertCanaries(backing, 256, $"HeadCursor = {bad}");

            // TailCursor: where the next dynamic value goes. A position outside the destination must be refused by the bytes write.
            backing = NewBacking(256);
            scratch = new ABIWriteFrame[4];
            Assert.IsTrue(ABIWriter.TryBegin(schema, backing.AsSpan(Canary, 256), scratch, in limits, out w, out _));
            Assert.IsTrue(w.TryWriteUInt64(1, out _));
            MemoryMarshal.Cast<ABIWriteFrame, ABIEncodeFrame>(scratch.AsSpan())[0].TailCursor = bad;
            Assert.IsFalse(w.TryWriteBytes([1, 2, 3], out status), $"TailCursor = {bad}");
            Assert.IsTrue(status is ABIStatus.InvalidWriterState or ABIStatus.InsufficientDestination, $"TailCursor = {bad}: {status}");
            AssertCanaries(backing, 256, $"TailCursor = {bad}");
            Assert.IsFalse(w.TryFinish(out int written, out _));
            Assert.AreEqual(0, written);

            // HeadBase only shapes the offset value written into a pointer: whatever it is, nothing may throw or leave the destination.
            backing = NewBacking(256);
            scratch = new ABIWriteFrame[4];
            Assert.IsTrue(ABIWriter.TryBegin(schema, backing.AsSpan(Canary, 256), scratch, in limits, out w, out _));
            MemoryMarshal.Cast<ABIWriteFrame, ABIEncodeFrame>(scratch.AsSpan())[0].HeadBase = bad;
            w.TryWriteUInt64(1, out _);
            w.TryWriteBytes([1, 2, 3], out status);
            Assert.IsTrue(Enum.IsDefined(status));
            AssertCanaries(backing, 256, $"HeadBase = {bad}");
            w.TryFinish(out _, out _);
        }

        // A forged TailCursor beyond the end of a small destination: refused by the bytes write.
        byte[] small = NewBacking(96);
        var frames = new ABIWriteFrame[4];
        Assert.IsTrue(ABIWriter.TryBegin(schema, small.AsSpan(Canary, 96), frames, in limits, out ABIWriter writer, out _));
        Assert.IsTrue(writer.TryWriteUInt64(1, out _));
        MemoryMarshal.Cast<ABIWriteFrame, ABIEncodeFrame>(frames.AsSpan())[0].TailCursor = 4096;
        Assert.IsFalse(writer.TryWriteBytes([1], out ABIStatus s2));
        Assert.IsTrue(s2 is ABIStatus.InvalidWriterState or ABIStatus.InsufficientDestination, s2.ToString());
        AssertCanaries(small, 96, "tail beyond the destination");
    }

    [TestMethod]
    public void ForgedFramesAtEndContainer_AreRefused_WithoutOverflowOrIndexing()
    {
        ABISchema schema = CompileTuple("((uint256))");
        var limits = GenerousEncode;
        string[] what = ["parent not waiting for a child", "parent head cursor at int.MaxValue", "child node out of range", "child head not fully consumed", "top beyond the scratch"];
        for (int variant = 0; variant < what.Length; variant++)
        {
            byte[] backing = NewBacking(64);
            var scratch = new ABIWriteFrame[4];
            Assert.IsTrue(ABIWriter.TryBegin(schema, backing.AsSpan(Canary, 64), scratch, in limits, out ABIWriter w, out _));
            Assert.IsTrue(w.TryBeginTuple(out _));
            Assert.IsTrue(w.TryWriteUInt64(1, out _));
            Span<ABIEncodeFrame> frames = MemoryMarshal.Cast<ABIWriteFrame, ABIEncodeFrame>(scratch.AsSpan());
            switch (variant)
            {
                case 0: frames[0].NextChild = frames[0].ChildCount; break;
                case 1: frames[0].HeadCursor = int.MaxValue; break;
                case 2: frames[1].Node = 9999; break;
                case 3: frames[1].HeadCursor = frames[1].HeadEnd + 1; break;
                default: frames[0].Top = 77; break;
            }

            bool ended = w.TryEndContainer(out ABIStatus status);
            Assert.IsTrue(Enum.IsDefined(status), what[variant]);
            AssertCanaries(backing, 64, what[variant]);
            if (variant == 1)
            {
                // The wrapped parent cursor is only used by the next slot, which the range checks refuse; ending the child itself is not an error.
                Assert.IsTrue(ended, what[variant]);
            }
            else
            {
                Assert.IsFalse(ended, what[variant]);
                Assert.AreEqual(ABIStatus.InvalidWriterState, status, what[variant]);
                Assert.IsFalse(w.TryFinish(out int written, out _), what[variant]);
                Assert.AreEqual(0, written, what[variant]);
            }
        }
    }

    [TestMethod]
    public void ForgedRootFrameAtFinish_NeverReportsAForgedLength()
    {
        ABISchema schema = CompileTuple("(uint256)");
        var limits = GenerousEncode;
        foreach (int forged in new[] { -1, int.MaxValue, 1_000_000, 40 })
        {
            var scratch = new ABIWriteFrame[2];
            byte[] backing = NewBacking(64);
            Assert.IsTrue(ABIWriter.TryBegin(schema, backing.AsSpan(Canary, 64), scratch, in limits, out ABIWriter w, out _));
            Assert.IsTrue(w.TryWriteUInt64(1, out _));
            MemoryMarshal.Cast<ABIWriteFrame, ABIEncodeFrame>(scratch.AsSpan())[0].TailCursor = forged;
            bool ok = w.TryFinish(out int written, out ABIStatus status);
            // 40 is a position inside the destination: sound as a frame, so it is accepted as is. Everything else is refused.
            if (forged == 40)
            {
                Assert.IsTrue(ok);
                Assert.AreEqual(40, written);
            }
            else
            {
                Assert.IsFalse(ok, $"forged tail {forged}");
                Assert.AreEqual(ABIStatus.InvalidWriterState, status, $"forged tail {forged}");
                Assert.AreEqual(0, written, $"forged tail {forged}");
            }
        }
    }
}
