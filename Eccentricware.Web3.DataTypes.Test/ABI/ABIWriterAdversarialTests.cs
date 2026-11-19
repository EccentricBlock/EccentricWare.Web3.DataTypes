using System.Diagnostics;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIWriteTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Adversarial and fuzz tests of the writer (spec 7.4, 7.7, 9.2 "Writer misuse"): randomly corrupted event scripts (deleted,
/// duplicated, swapped, inserted, truncated and replaced events, bad counts, corrupted values) checked against a small independent
/// model of the legal event grammar. Every run must be exception free; the writer, the measurer and the call encoder must agree
/// exactly; a failed document must stay failed (later calls answer InvalidWriterState) and a failing call must not change the
/// destination; and anything the writer accepts must be a strictly canonical document. Seeds are fixed; failing seeds are in the messages.
/// </summary>
[TestClass]
public sealed class ABIWriterAdversarialTests
{
    // ------------------------------------------------------------------ an independent model of the event grammar

    private sealed class ModelFrame(RType[]? types, RType? element, int count)
    {
        internal int Next;
        internal int Count { get; } = count;
        internal RType TypeAt(int index) => element ?? types![index];
    }

    private static bool Matches(RType type, EvKind kind) => type switch
    {
        RElementary e => e.Kind switch
        {
            ABITypeKind.UInt => kind is EvKind.UInt64 or EvKind.UInt256,
            ABITypeKind.Int => kind is EvKind.Int64 or EvKind.Int256,
            ABITypeKind.Address => kind == EvKind.Address,
            ABITypeKind.Bool => kind == EvKind.Bool,
            ABITypeKind.FixedBytes => kind == EvKind.FixedBytes,
            ABITypeKind.Function => kind == EvKind.Function,
            ABITypeKind.Fixed => kind == EvKind.SignedScaled,
            ABITypeKind.UFixed => kind == EvKind.UnsignedScaled,
            _ => false,
        },
        RBlobType b => kind == EvKind.Bytes || (b.IsString && kind == EvKind.String), // the raw entry point also serves string slots
        RTupleType => kind == EvKind.BeginTuple,
        RFixedArrayType => kind == EvKind.BeginFixedArray,
        RDynamicArrayType => kind == EvKind.BeginDynamicArray,
        _ => false,
    };

    /// <summary>The first structural failure the grammar predicts: its event index (script length for a failing finish) and status; (-1, Success) when the shape is legal.</summary>
    private static (int Index, ABIStatus Status) Predict(RTupleType root, IReadOnlyList<Ev> script)
    {
        var stack = new List<ModelFrame> { new(root.Items, null, root.Items.Length) };
        for (int i = 0; i < script.Count; i++)
        {
            Ev ev = script[i];
            if (ev.Kind == EvKind.End)
            {
                if (stack.Count == 1)
                {
                    return (i, ABIStatus.InvalidWriterState);
                }

                if (stack[^1].Next != stack[^1].Count)
                {
                    return (i, ABIStatus.IncompleteWrite);
                }

                stack.RemoveAt(stack.Count - 1);
                stack[^1].Next++;
                continue;
            }

            ModelFrame top = stack[^1];
            if (top.Next >= top.Count)
            {
                return (i, ABIStatus.ElementLimitExceeded);
            }

            RType child = top.TypeAt(top.Next);
            if (!Matches(child, ev.Kind))
            {
                return (i, ABIStatus.TypeMismatch);
            }

            switch (child)
            {
                case RTupleType tuple:
                    stack.Add(new ModelFrame(tuple.Items, null, tuple.Items.Length));
                    break;
                case RFixedArrayType fixedArray:
                    stack.Add(new ModelFrame(null, fixedArray.Element, fixedArray.Length));
                    break;
                case RDynamicArrayType dynamicArray:
                    stack.Add(new ModelFrame(null, dynamicArray.Element, ev.Count));
                    break;
                default:
                    top.Next++;
                    break;
            }
        }

        return stack.Count == 1 && stack[0].Next == stack[0].Count ? (-1, ABIStatus.Success) : (script.Count, ABIStatus.IncompleteWrite);
    }

    private static bool IsDataStatus(ABIStatus status) =>
        status is ABIStatus.ValueOutOfRange or ABIStatus.InvalidArgument or ABIStatus.InvalidUtf8;

    // ------------------------------------------------------------------ checked replay

    /// <summary>Replays a script on a writer, asserting after every event that a failing call left the destination unchanged and that every call after the first failure is dead.</summary>
    private static RunResult RunWriterChecked(List<Ev> script, ABISchema schema, byte[] destination, string label)
    {
        ABIEncodeLimits limits = GenerousEncode;
        if (!ABIWriter.TryBegin(schema, destination, new ABIWriteFrame[32], in limits, out ABIWriter w, out ABIStatus status))
        {
            return new RunResult(false, status, 0, -1);
        }

        int failedAt = -1;
        ABIStatus firstFailure = ABIStatus.Success;
        for (int i = 0; i < script.Count; i++)
        {
            byte[] before = destination.ToArray();
            bool ok = Apply(ref w, script[i], out status);
            if (failedAt < 0)
            {
                if (!ok)
                {
                    failedAt = i;
                    firstFailure = status;
                    CollectionAssert.AreEqual(before, destination, $"{label}: failing event {i} ({script[i].Kind}, {status}) modified the destination");
                }
                else
                {
                    Assert.AreEqual(ABIStatus.Success, status, label);
                }
            }
            else
            {
                Assert.IsFalse(ok, $"{label}: event {i} succeeded after the failure at {failedAt}");
                Assert.AreEqual(ABIStatus.InvalidWriterState, status, $"{label}: event {i} after the failure at {failedAt}");
                CollectionAssert.AreEqual(before, destination, $"{label}: a dead writer modified the destination at event {i}");
            }
        }

        byte[] beforeFinish = destination.ToArray();
        bool finished = w.TryFinish(out int length, out status);
        if (failedAt >= 0)
        {
            Assert.IsFalse(finished, label);
            Assert.AreEqual(ABIStatus.InvalidWriterState, status, label);
            Assert.AreEqual(0, length, label);
            CollectionAssert.AreEqual(beforeFinish, destination, label);
            return new RunResult(false, firstFailure, 0, failedAt);
        }

        if (!finished)
        {
            Assert.AreEqual(0, length, label);
            CollectionAssert.AreEqual(beforeFinish, destination, $"{label}: a failing finish modified the destination");
            // A finished-late writer is dead too.
            Assert.IsFalse(w.TryWriteBool(true, out ABIStatus after), label);
            Assert.AreEqual(ABIStatus.InvalidWriterState, after, label);
            return new RunResult(false, status, 0, script.Count);
        }

        return new RunResult(true, ABIStatus.Success, length, -1);
    }

    private static Ev RandomSimpleEvent(DeterministicRng rng) => rng.Next(14) switch
    {
        0 => Ev.U64(1),
        1 => Ev.I64(-1),
        2 => Ev.Uint256(new byte[32]),
        3 => Ev.Int256(new byte[32]),
        4 => Ev.Addr(new byte[20]),
        5 => Ev.Bool(true),
        6 => Ev.Func(new byte[20], 0),
        7 => Ev.Blob([1, 2, 3]),
        8 => Ev.Text(Ascii("a")),
        9 => new Ev(EvKind.SignedScaled, new byte[32]),
        10 => new Ev(EvKind.UnsignedScaled, new byte[32]),
        11 => Ev.Begin("tuple"),
        12 => Ev.Begin("fixed"),
        _ => Ev.Begin("dynamic", rng.Next(3)),
    };

    private static List<Ev> MutateStructure(List<Ev> original, DeterministicRng rng, out string description)
    {
        List<Ev> script = [.. original];
        int position = script.Count == 0 ? 0 : rng.Next(script.Count);
        switch (rng.Next(9))
        {
            case 0 when script.Count > 0:
                description = $"delete {position}";
                script.RemoveAt(position);
                break;
            case 1 when script.Count > 0:
                description = $"duplicate {position}";
                script.Insert(position, script[position]);
                break;
            case 2 when script.Count > 1:
                int other = (position + 1) % script.Count;
                description = $"swap {position},{other}";
                (script[position], script[other]) = (script[other], script[position]);
                break;
            case 3:
                description = $"insert End at {position}";
                script.Insert(position, Ev.End);
                break;
            case 4 when script.Count > 0:
                description = $"truncate at {position}";
                script.RemoveRange(position, script.Count - position);
                break;
            case 5 when script.Count > 0:
                description = $"replace {position}";
                script[position] = RandomSimpleEvent(rng);
                break;
            case 6:
                int begin = script.FindIndex(position, e => e.Kind == EvKind.BeginDynamicArray);
                if (begin >= 0)
                {
                    int delta = rng.Next(2) == 0 ? -1 : 1;
                    description = $"count of {begin} {delta:+0;-0}";
                    script[begin] = script[begin] with { Count = Math.Max(0, script[begin].Count + delta) };
                }
                else
                {
                    description = "noop";
                }

                break;
            case 7:
                description = $"insert random at {position}";
                script.Insert(position, RandomSimpleEvent(rng));
                break;
            default:
                description = "append random";
                script.Add(RandomSimpleEvent(rng));
                break;
        }

        return script;
    }

    [TestMethod]
    public void StructurallyMutatedScripts_FollowTheGrammarModel_AndAgreeAcrossWriterMeasurerAndCallEncoder()
    {
        int accepted = 0;
        var seenStatuses = new HashSet<ABIStatus>();
        for (ulong seed = 1; seed <= 250; seed++)
        {
            var (type, _, expected, schema, script) = Generate(seed * 7, depth: 3);
            ABIFunctionSchema function = Function("f" + type.Text);
            var rng = new DeterministicRng(seed * 977);
            for (int round = 0; round < 24; round++)
            {
                List<Ev> mutated = MutateStructure(script, rng, out string what);
                string label = $"seed {seed * 7} round {round} ({what}) {type.Text}";

                var buffer = new byte[Math.Max(expected.Length, 64) + 256];
                Array.Fill(buffer, Garbage);
                RunResult written = RunWriterChecked(mutated, schema, buffer, label);
                RunResult measured = RunMeasurer(mutated, schema, GenerousEncode);
                Assert.AreEqual(written, measured, $"{label}: measurer vs writer");

                var callBuffer = new byte[buffer.Length + 4];
                RunResult call = RunCall(mutated, function, callBuffer, GenerousEncode);
                Assert.AreEqual(written.Ok, call.Ok, $"{label}: call encoder");
                Assert.AreEqual(written.Status, call.Status, label);
                Assert.AreEqual(written.FailedAt, call.FailedAt, label);
                if (call.Ok)
                {
                    Assert.AreEqual(written.Length + 4, call.Length, label);
                }

                var (index, predicted) = Predict(type, mutated);
                if (predicted == ABIStatus.Success)
                {
                    // The shape is legal: only a data problem (a swapped value of another width) can fail it.
                    Assert.IsTrue(written.Ok || IsDataStatus(written.Status), $"{label}: model says legal, writer says {written.Status} at {written.FailedAt}");
                }
                else if (written.Ok)
                {
                    Assert.Fail($"{label}: the model predicts {predicted} at event {index} but the writer accepted the script");
                }
                else if (written.FailedAt == index)
                {
                    Assert.AreEqual(predicted, written.Status, $"{label}: status at event {index}");
                }
                else
                {
                    Assert.IsTrue(written.FailedAt < index && IsDataStatus(written.Status), $"{label}: model predicts {predicted} at {index}, writer {written.Status} at {written.FailedAt}");
                }

                if (written.Ok)
                {
                    accepted++;
                    byte[] document = buffer[..written.Length];
                    Assert.IsTrue(ABIData.TryDecodeComplete(document, schema, GenerousDecode(), out _, out ABIStatus decode), $"{label}: accepted script produced a non-canonical document ({decode})");
                }
                else
                {
                    seenStatuses.Add(written.Status);
                }
            }
        }

        Assert.IsTrue(accepted > 100, $"only {accepted} mutants were accepted");
        foreach (ABIStatus expected in new[] { ABIStatus.TypeMismatch, ABIStatus.IncompleteWrite, ABIStatus.ElementLimitExceeded, ABIStatus.InvalidWriterState })
        {
            Assert.IsTrue(seenStatuses.Contains(expected), $"no mutant produced {expected}");
        }
    }

    private static ABIDecodeLimits GenerousDecode() => Generous;

    // ------------------------------------------------------------------ value corruption

    [TestMethod]
    public void CorruptedValues_NeverThrow_AreRejectedWithADataStatus_AndAcceptedOutputIsCanonical()
    {
        int accepted = 0;
        int rejected = 0;
        for (ulong seed = 1; seed <= 250; seed++)
        {
            var (type, _, expected, schema, script) = Generate(seed * 11, depth: 3);
            var rng = new DeterministicRng(seed * 3571);
            var withData = Enumerable.Range(0, script.Count).Where(i => script[i].Data is { Length: > 0 }).ToArray();
            if (withData.Length == 0)
            {
                continue;
            }

            for (int round = 0; round < 24; round++)
            {
                List<Ev> mutated = [.. script];
                int index = withData[rng.Next(withData.Length)];
                byte[] data = mutated[index].Data!.ToArray();
                switch (rng.Next(5))
                {
                    case 0: data[rng.Next(data.Length)] ^= (byte)(1 << rng.Next(8)); break;
                    case 1: data = data[..^1]; break;
                    case 2: data = [.. data, 0x7f]; break;
                    case 3: Array.Fill(data, (byte)0xff); break;
                    default: data[0] = 0x80; break;
                }

                mutated[index] = mutated[index] with { Data = data };
                string label = $"seed {seed * 11} round {round} event {index} {mutated[index].Kind} {type.Text}";

                var buffer = new byte[expected.Length + 512];
                Array.Fill(buffer, Garbage);
                RunResult written = RunWriterChecked(mutated, schema, buffer, label);
                RunResult measured = RunMeasurer(mutated, schema, GenerousEncode);
                Assert.AreEqual(written, measured, label);

                if (written.Ok)
                {
                    accepted++;
                    Assert.IsTrue(ABIData.TryDecodeComplete(buffer[..written.Length], schema, GenerousDecode(), out _, out ABIStatus decode), $"{label}: {decode}");
                }
                else
                {
                    rejected++;
                    Assert.IsTrue(IsDataStatus(written.Status), $"{label}: {written.Status}");
                    Assert.AreEqual(index, written.FailedAt, label);
                }
            }
        }

        Assert.IsTrue(accepted > 100, $"only {accepted} corrupted scripts were accepted (data that stays valid)");
        Assert.IsTrue(rejected > 500, $"only {rejected} corrupted scripts were rejected");
    }

    // ------------------------------------------------------------------ shapes at the limits of the machinery

    [TestMethod]
    public void MaximumNesting_AtDepth32_Works_WithExactlyEnoughScratchAndDepthLimit()
    {
        const int nested = 30; // root (depth 1) + 30 tuples (depths 2..31) + a leaf at depth 32
        string tuple = "(" + string.Concat(Enumerable.Repeat("(", nested)) + "uint8" + string.Concat(Enumerable.Repeat(")", nested)) + ")";
        ABISchema schema = CompileTuple(tuple);
        List<Ev> script = [.. Enumerable.Repeat(Ev.Begin("tuple"), nested), Ev.U64(7), .. Enumerable.Repeat(Ev.End, nested)];

        byte[] buffer = new byte[32];
        RunResult ok = RunWriter(script, schema, buffer, EncLimits(depth: 32), frames: 31);
        Assert.IsTrue(ok.Ok, $"{ok.Status} at {ok.FailedAt}");
        Assert.AreEqual(7, buffer[31]);

        RunResult noDepth = RunWriter(script, schema, new byte[32], EncLimits(depth: 31), frames: 32);
        Assert.AreEqual(ABIStatus.DepthLimitExceeded, noDepth.Status);
        Assert.AreEqual(nested, noDepth.FailedAt, "the leaf is the first value at depth 32");

        RunResult noScratch = RunWriter(script, schema, new byte[32], EncLimits(depth: 32), frames: 30);
        Assert.AreEqual(ABIStatus.InsufficientScratch, noScratch.Status);
        Assert.AreEqual(-1, noScratch.FailedAt, "reported by TryBegin: the deepest container path needs 31 frames");
    }

    [TestMethod]
    public void ManyEmptyTuples_AreBoundedByTheArrayAndValueLimits_NotByBytes()
    {
        ABISchema schema = CompileTuple("(()[])");
        List<Ev> Build(int count)
        {
            var script = new List<Ev> { Ev.Begin("dynamic", count) };
            for (int i = 0; i < count; i++)
            {
                script.AddRange([Ev.Begin("tuple"), Ev.End]);
            }

            script.Add(Ev.End);
            return script;
        }

        byte[] buffer = new byte[64];
        RunResult atLimit = RunWriter(Build(4096), schema, buffer, ABIEncodeLimits.Default);
        Assert.IsTrue(atLimit.Ok, atLimit.Status.ToString());
        Assert.AreEqual(64, atLimit.Length, "a count word and nothing else");

        RunResult over = RunWriter(Build(4097), schema, buffer, ABIEncodeLimits.Default);
        Assert.AreEqual(ABIStatus.ElementLimitExceeded, over.Status);
        Assert.AreEqual(0, over.FailedAt);

        // Under a values budget of 100 the 98th empty tuple is value number 101.
        RunResult values = RunWriter(Build(200), schema, buffer, EncLimits(values: 100));
        Assert.AreEqual(ABIStatus.WorkLimitExceeded, values.Status);
    }

    [TestMethod]
    public void AnnouncingIntMaxValueZeroWidthElements_IsCheapAndHarmless()
    {
        // The writer has no loop over the announced count: opening is O(1), and a short supply is an IncompleteWrite.
        ABISchema schema = CompileTuple("(()[])");
        var limits = new ABIEncodeLimits(1024, 8, int.MaxValue, int.MaxValue, int.MaxValue);
        Span<ABIWriteFrame> scratch = stackalloc ABIWriteFrame[8];
        Span<byte> destination = stackalloc byte[64];
        Assert.IsTrue(ABIWriter.TryBegin(schema, destination, scratch, in limits, out ABIWriter w, out _));
        var clock = Stopwatch.StartNew();
        Assert.IsTrue(w.TryBeginDynamicArray(int.MaxValue, out ABIStatus status), status.ToString());
        Assert.IsFalse(w.TryEndContainer(out status));
        Assert.AreEqual(ABIStatus.IncompleteWrite, status);
        Assert.IsTrue(clock.ElapsedMilliseconds < 1_000, "opening must not scale with the announced count");
    }

    // ------------------------------------------------------------------ concurrency

    [TestMethod]
    public void SharedSchemas_AreSafeUnderConcurrentWriters_AndOutputIsDeterministic()
    {
        const int Threads = 8;
        var cases = Enumerable.Range(1, 40).Select(i => Generate((ulong)i * 61)).ToArray();
        long mismatches = 0;
        long runs = 0;
        var stop = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, Threads).Select(t => new Thread(() =>
        {
            var rng = new DeterministicRng((ulong)t + 1);
            while (stop.ElapsedMilliseconds < 1_500)
            {
                var (_, _, expected, schema, script) = cases[rng.Next(cases.Length)];
                var buffer = new byte[expected.Length + 16];
                RunResult result = RunWriter(script, schema, buffer, GenerousEncode);
                if (!result.Ok || !buffer.AsSpan(0, result.Length).SequenceEqual(expected))
                {
                    Interlocked.Increment(ref mismatches);
                }

                Interlocked.Increment(ref runs);
            }
        })).ToArray();
        foreach (Thread thread in workers)
        {
            thread.Start();
        }

        foreach (Thread thread in workers)
        {
            thread.Join();
        }

        Assert.AreEqual(0, Interlocked.Read(ref mismatches));
        Assert.IsTrue(Interlocked.Read(ref runs) > 1_000, $"only {runs} runs");
    }

    private static readonly ABIEncodeLimits[] SharedLimits = [GenerousEncode];

    [TestMethod]
    public void LimitsAreReadOnce_AConcurrentlyChangingLimitsVariableCannotSplitValidationFromUse()
    {
        // The shared variable flips between valid generous limits and the invalid all-zero default. If the writer validated one read and used another,
        // an accepted TryBegin could continue with zero budgets and fail with a limit status. It must either refuse at TryBegin or run to completion.
        ABISchema schema = CompileTuple("(address,uint256[],bytes)");
        List<Ev> script = [Ev.Addr(new byte[20]), Ev.Begin("dynamic", 2), Ev.U64(1), Ev.U64(2), Ev.End, Ev.Blob([1, 2, 3])];
        using var stop = new CancellationTokenSource();
        ABIEncodeLimits generous = GenerousEncode;
        var flipper = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                SharedLimits[0] = default;
                SharedLimits[0] = generous;
            }
        });
        flipper.Start();

        int begun = 0;
        int refused = 0;
        var clock = Stopwatch.StartNew();
        byte[] buffer = new byte[512];
        Span<ABIWriteFrame> scratch = new ABIWriteFrame[8];
        while (clock.ElapsedMilliseconds < 1_000)
        {
            if (ABIWriter.TryBegin(schema, buffer, scratch, in SharedLimits[0], out ABIWriter w, out ABIStatus status))
            {
                begun++;
                bool ok = true;
                foreach (Ev ev in script)
                {
                    ok &= Apply(ref w, ev, out status);
                    if (!ok)
                    {
                        Assert.Fail($"an accepted TryBegin must run to completion, but {ev.Kind} failed with {status}");
                    }
                }

                Assert.IsTrue(w.TryFinish(out int length, out status), status.ToString());
                Assert.AreEqual(96 + (32 + 64) + (32 + 32), length); // head, array (count + 2 words), bytes (length + 1 padded word)
            }
            else
            {
                refused++;
                Assert.AreEqual(ABIStatus.InvalidArgument, status);
            }
        }

        stop.Cancel();
        flipper.Join();
        Assert.IsTrue(begun > 0 && refused > 0, $"the flip must be observed in both directions (begun {begun}, refused {refused})");
    }
}
