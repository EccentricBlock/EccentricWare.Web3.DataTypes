using System.Runtime.InteropServices;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIWriteTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Writer, measurer and call-encoder state machine and failure behaviour (spec 7.4, 10.3): invalid default, creation failures,
/// Faulted and Completed are terminal and mutate nothing, every kind of misuse faults, a failed leaf leaves the destination untouched.
/// </summary>
[TestClass]
public sealed class ABIWriterStateTests
{
    private static byte[] Garbled(int length)
    {
        var buffer = new byte[length];
        Array.Fill(buffer, Garbage);
        return buffer;
    }

    private static ABIWriter Open(string tuple, byte[] destination, int frames = 32, ABIEncodeLimits? limits = null)
    {
        ABIEncodeLimits effective = limits ?? GenerousEncode;
        Assert.IsTrue(ABIWriter.TryBegin(CompileTuple(tuple), destination, new ABIWriteFrame[frames], in effective, out ABIWriter writer, out ABIStatus status), status.ToString());
        Assert.AreEqual(ABIStatus.Success, status);
        return writer;
    }

    /// <summary>Calls every operation on a writer that must be dead: each answers InvalidWriterState, zeroes its outputs and writes nothing.</summary>
    private static void AssertDead(ref ABIWriter w, byte[] destination, string why)
    {
        byte[] before = destination.ToArray();
        byte[] word = new byte[32];
        byte[] address = new byte[20];
        var results = new List<(bool Ok, ABIStatus Status)>
        {
            (w.TryWriteUInt64(1, out ABIStatus s0), s0),
            (w.TryWriteInt64(1, out ABIStatus s1), s1),
            (w.TryWriteUInt256Word(word, out ABIStatus s2), s2),
            (w.TryWriteInt256Word(word, out ABIStatus s3), s3),
            (w.TryWriteAddress(address, out ABIStatus s4), s4),
            (w.TryWriteBool(true, out ABIStatus s5), s5),
            (w.TryWriteFixedBytes(new byte[4], out ABIStatus s6), s6),
            (w.TryWriteFunction(address, 1, out ABIStatus s7), s7),
            (w.TryWriteBytes(new byte[3], out ABIStatus s8), s8),
            (w.TryWriteStringUtf8(new byte[3], out ABIStatus s9), s9),
            (w.TryWriteSignedScaledWord(word, out ABIStatus s10), s10),
            (w.TryWriteUnsignedScaledWord(word, out ABIStatus s11), s11),
            (w.TryBeginTuple(out ABIStatus s12), s12),
            (w.TryBeginFixedArray(out ABIStatus s13), s13),
            (w.TryBeginDynamicArray(1, out ABIStatus s14), s14),
            (w.TryEndContainer(out ABIStatus s15), s15),
        };
        for (int i = 0; i < results.Count; i++)
        {
            Assert.IsFalse(results[i].Ok, $"{why}: operation {i} succeeded");
            Assert.AreEqual(ABIStatus.InvalidWriterState, results[i].Status, $"{why}: operation {i}");
        }

        Assert.IsFalse(w.TryFinish(out int written, out ABIStatus finish), why);
        Assert.AreEqual(ABIStatus.InvalidWriterState, finish, why);
        Assert.AreEqual(0, written, why);
        CollectionAssert.AreEqual(before, destination, $"{why}: a dead writer modified the destination");
    }

    // ------------------------------------------------------------------ creation

    [TestMethod]
    public void DefaultWriter_AnswersInvalidWriterState_ToEveryOperation()
    {
        ABIWriter w = default;
        AssertDead(ref w, [], "default writer");
    }

    [TestMethod]
    public void DefaultMeasurerAndCallEncoder_AnswerInvalidWriterState_ToEveryOperation()
    {
        ABIMeasurer m = default;
        Assert.IsFalse(m.TryWriteUInt64(1, out ABIStatus s));
        Assert.AreEqual(ABIStatus.InvalidWriterState, s);
        Assert.IsFalse(m.TryBeginTuple(out s));
        Assert.AreEqual(ABIStatus.InvalidWriterState, s);
        Assert.IsFalse(m.TryEndContainer(out s));
        Assert.AreEqual(ABIStatus.InvalidWriterState, s);
        Assert.IsFalse(m.TryFinish(out int length, out s));
        Assert.AreEqual(ABIStatus.InvalidWriterState, s);
        Assert.AreEqual(0, length);

        ABICallEncoder c = default;
        Assert.IsFalse(c.TryWriteBytes([1], out s));
        Assert.AreEqual(ABIStatus.InvalidWriterState, s);
        Assert.IsFalse(c.TryBeginDynamicArray(0, out s));
        Assert.AreEqual(ABIStatus.InvalidWriterState, s);
        Assert.IsFalse(c.TryFinish(out length, out s));
        Assert.AreEqual(ABIStatus.InvalidWriterState, s);
        Assert.AreEqual(0, length);
    }

    [TestMethod]
    public void TryBegin_InsufficientRootHead_LeavesDestinationUnchanged_AndTheWriterInvalid()
    {
        // (uint256,uint256) has a 64-byte head: 63 bytes cannot hold it.
        byte[] destination = Garbled(63);
        var limits = GenerousEncode;
        Assert.IsFalse(ABIWriter.TryBegin(CompileTuple("(uint256,uint256)"), destination, new ABIWriteFrame[4], in limits, out ABIWriter w, out ABIStatus status));
        Assert.AreEqual(ABIStatus.InsufficientDestination, status);
        Assert.IsTrue(destination.All(b => b == Garbage));
        AssertDead(ref w, destination, "failed TryBegin");

        // A dynamic root needs its head too: (bytes) is one pointer word.
        Assert.IsFalse(ABIWriter.TryBegin(CompileTuple("(bytes)"), new byte[31], new ABIWriteFrame[4], in limits, out w, out status));
        Assert.AreEqual(ABIStatus.InsufficientDestination, status);
        Assert.IsTrue(ABIWriter.TryBegin(CompileTuple("(bytes)"), new byte[32], new ABIWriteFrame[4], in limits, out _, out status), "exactly the head is enough to begin");
    }

    [TestMethod]
    public void TryBegin_RejectsNullSchemaInvalidLimitsEmptyScratchAndOversizedHead()
    {
        byte[] destination = Garbled(256);
        ABISchema schema = CompileTuple("(uint256)");
        var good = GenerousEncode;

        Assert.IsFalse(ABIWriter.TryBegin(null!, destination, new ABIWriteFrame[4], in good, out ABIWriter w, out ABIStatus status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);

        var bad = default(ABIEncodeLimits);
        Assert.IsFalse(ABIWriter.TryBegin(schema, destination, new ABIWriteFrame[4], in bad, out w, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);

        foreach (ABIEncodeLimits invalid in new[]
        {
            good with { MaxEncodedBytes = 0 }, good with { MaxEncodedBytes = -1 }, good with { MaxValueDepth = 0 }, good with { MaxValueDepth = 33 },
            good with { MaxArrayElements = 0 }, good with { MaxWrittenValues = 0 }, good with { MaxWorkUnits = 0 }, good with { MaxWorkUnits = -5 },
        })
        {
            ABIEncodeLimits copy = invalid;
            Assert.IsFalse(ABIWriter.TryBegin(schema, destination, new ABIWriteFrame[4], in copy, out w, out status), invalid.ToString());
            Assert.AreEqual(ABIStatus.InvalidArgument, status, invalid.ToString());
        }

        Assert.IsFalse(ABIWriter.TryBegin(schema, destination, Span<ABIWriteFrame>.Empty, in good, out w, out status));
        Assert.AreEqual(ABIStatus.InsufficientScratch, status);

        // A head larger than MaxEncodedBytes is a limit failure, reported before the destination size.
        var tiny = good with { MaxEncodedBytes = 31 };
        Assert.IsFalse(ABIWriter.TryBegin(schema, destination, new ABIWriteFrame[4], in tiny, out w, out status));
        Assert.AreEqual(ABIStatus.InputTooLarge, status);
        Assert.IsTrue(destination.All(b => b == Garbage), "no failed TryBegin may touch the destination");
    }

    [TestMethod]
    public void TryBegin_ScratchOverlappingTheDestination_IsRejected()
    {
        // The same memory seen as bytes and as frames: a write through one would corrupt the other.
        var memory = new ABIWriteFrame[8];
        Span<byte> asBytes = MemoryMarshal.AsBytes(memory.AsSpan());
        var limits = GenerousEncode;
        Assert.IsFalse(ABIWriter.TryBegin(CompileTuple("(uint256)"), asBytes, memory, in limits, out ABIWriter w, out ABIStatus status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        AssertDead(ref w, [], "overlapping scratch");

        // Partial overlap: the destination is the tail of the frames.
        Assert.IsFalse(ABIWriter.TryBegin(CompileTuple("(uint256)"), asBytes.Slice(asBytes.Length - 32), memory, in limits, out w, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);

        // Disjoint memory is fine.
        Assert.IsTrue(ABIWriter.TryBegin(CompileTuple("(uint256)"), new byte[32], memory, in limits, out _, out status), status.ToString());
    }

    // ------------------------------------------------------------------ capacity after earlier success

    [TestMethod]
    public void InsufficientTailCapacity_AfterSuccessfulOperations_FaultsTheWriter_AndTheFailedLeafWritesNothing()
    {
        // (uint256,bytes): head 64, then a bytes tail of 32 + padded data. Room for the head and one tail word only.
        byte[] destination = Garbled(96);
        ABIWriter w = Open("(uint256,bytes)", destination);
        Assert.IsTrue(w.TryWriteUInt64(7, out ABIStatus status));
        byte[] afterFirst = destination.ToArray();

        Assert.IsFalse(w.TryWriteBytes(new byte[1], out status));
        Assert.AreEqual(ABIStatus.InsufficientDestination, status);
        CollectionAssert.AreEqual(afterFirst, destination, "the failed leaf must not write a single byte (no pointer, no length)");

        Assert.IsFalse(w.TryFinish(out int written, out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.AreEqual(0, written);
        AssertDead(ref w, destination, "faulted by capacity");
    }

    [TestMethod]
    public void CapacityFailureInsideANestedContainer_ReportsInsufficientDestination_BeforeAnyHeadByteIsWritten()
    {
        // (uint256[]) needs 32 (pointer) + 32 (count) + 3*32 (elements) = 160: give 96, the open of the array must fail up front.
        byte[] destination = Garbled(96);
        ABIWriter w = Open("(uint256[])", destination);
        byte[] before = destination.ToArray();
        Assert.IsFalse(w.TryBeginDynamicArray(3, out ABIStatus status));
        Assert.AreEqual(ABIStatus.InsufficientDestination, status);
        CollectionAssert.AreEqual(before, destination, "a rejected container open writes neither pointer nor count");
        AssertDead(ref w, destination, "after rejected open");
    }

    // ------------------------------------------------------------------ scratch

    [TestMethod]
    public void TryBegin_RefusesAScratchTooShortForTheSchemasDeepestContainerPath()
    {
        // Spec 7.4: scratch capacity is checked before success. Frames needed = the schema's deepest container path (root = 1), capped by MaxValueDepth.
        (string Tuple, int Needed)[] cases =
        [
            ("()", 1), ("(uint256)", 1), ("(bytes)", 1), ("((uint256))", 2), ("(uint256[])", 2), ("(uint256[2],bytes)", 2),
            ("(((uint256)))", 3), ("(uint256[][])", 3), ("(uint256[][][],string[][])", 4), ("(uint8[][][][][])", 6),
            ("((()))", 3), // an empty tuple is still a container
        ];
        var limits = GenerousEncode;
        foreach (var (tuple, needed) in cases)
        {
            ABISchema schema = CompileTuple(tuple);
            for (int frames = 0; frames < needed; frames++)
            {
                byte[] destination = Garbled(256);
                Assert.IsFalse(ABIWriter.TryBegin(schema, destination, new ABIWriteFrame[frames], in limits, out ABIWriter w, out ABIStatus status), $"{tuple} with {frames} frames");
                Assert.AreEqual(ABIStatus.InsufficientScratch, status, $"{tuple} with {frames} frames");
                Assert.IsTrue(destination.All(b => b == Garbage), "a failed TryBegin writes nothing");
                AssertDead(ref w, destination, $"{tuple} with {frames} frames");

                Assert.IsFalse(ABIMeasurer.TryBegin(schema, new ABIMeasureFrame[frames], in limits, out _, out status), $"{tuple} measurer with {frames} frames");
                Assert.AreEqual(ABIStatus.InsufficientScratch, status);
                Assert.IsFalse(ABICallEncoder.TryBegin(Function("f" + tuple), destination, new ABIWriteFrame[frames], in limits, out _, out status), $"{tuple} call encoder with {frames} frames");
                Assert.AreEqual(ABIStatus.InsufficientScratch, status);
                Assert.IsTrue(destination.All(b => b == Garbage), "a failed call TryBegin writes not even the selector");
            }

            Assert.IsTrue(ABIWriter.TryBegin(schema, Garbled(256), new ABIWriteFrame[needed], in limits, out _, out ABIStatus ok), $"{tuple} with exactly {needed} frames: {ok}");
            Assert.IsTrue(ABIMeasurer.TryBegin(schema, new ABIMeasureFrame[needed], in limits, out _, out ok), $"{tuple} measurer with exactly {needed} frames");
        }
    }

    [TestMethod]
    public void TryBegin_ScratchRuleIsCappedByMaxValueDepth_AndExactScratchIsEnoughToFinish()
    {
        // (((uint256))) has 3 container levels, but under MaxValueDepth 2 a third container can never open, so 2 frames are enough and
        // the writer reports the depth limit (not a scratch shortage) when the third container is attempted.
        ABISchema schema = CompileTuple("(((uint256)))");
        var shallow = EncLimits(depth: 2);
        Assert.IsTrue(ABIWriter.TryBegin(schema, Garbled(64), new ABIWriteFrame[2], in shallow, out ABIWriter w, out ABIStatus status), status.ToString());
        Assert.IsTrue(w.TryBeginTuple(out _));
        Assert.IsFalse(w.TryBeginTuple(out status));
        Assert.AreEqual(ABIStatus.DepthLimitExceeded, status);
        Assert.IsFalse(ABIWriter.TryBegin(schema, Garbled(64), new ABIWriteFrame[1], in shallow, out _, out status));
        Assert.AreEqual(ABIStatus.InsufficientScratch, status);

        // Exactly the needed frames finish a document.
        var limits = GenerousEncode;
        byte[] destination = Garbled(64);
        Assert.IsTrue(ABIWriter.TryBegin(schema, destination, new ABIWriteFrame[3], in limits, out w, out _));
        Assert.IsTrue(w.TryBeginTuple(out _));
        Assert.IsTrue(w.TryBeginTuple(out _));
        Assert.IsTrue(w.TryWriteUInt64(1, out _));
        Assert.IsTrue(w.TryEndContainer(out _));
        Assert.IsTrue(w.TryEndContainer(out _));
        Assert.IsTrue(w.TryFinish(out int written, out _));
        Assert.AreEqual(32, written);
    }

    [TestMethod]
    public void MaxValueDepthFrames_AlwaysSuffice()
    {
        // MaxValueDepth frames for a schema nested exactly as deep as the limit allows.
        string tuple = "(" + new string('(', 5) + "uint8" + new string(')', 5) + ")";
        ABISchema schema = CompileTuple(tuple);
        var limits = EncLimits(depth: 7);
        byte[] destination = Garbled(32);
        List<Ev> script = [.. Enumerable.Repeat(Ev.Begin("tuple"), 5), Ev.U64(5), .. Enumerable.Repeat(Ev.End, 5)];
        RunResult result = RunWriter(script, schema, destination, limits, frames: 7);
        Assert.IsTrue(result.Ok, $"{result.Status} at {result.FailedAt}");
        Assert.AreEqual(32, result.Length);
    }

    // ------------------------------------------------------------------ element counts and container balance

    [TestMethod]
    public void TooManyElements_ForTupleFixedArrayDynamicArrayAndRoot_AreElementLimitExceeded()
    {
        // root arity
        ABIWriter w = Open("(uint256)", Garbled(64));
        Assert.IsTrue(w.TryWriteUInt64(1, out ABIStatus status));
        Assert.IsFalse(w.TryWriteUInt64(2, out status));
        Assert.AreEqual(ABIStatus.ElementLimitExceeded, status);

        // nested tuple arity
        w = Open("((uint256))", Garbled(64));
        Assert.IsTrue(w.TryBeginTuple(out _));
        Assert.IsTrue(w.TryWriteUInt64(1, out _));
        Assert.IsFalse(w.TryWriteUInt64(2, out status));
        Assert.AreEqual(ABIStatus.ElementLimitExceeded, status);

        // fixed array length
        w = Open("(uint256[2])", Garbled(128));
        Assert.IsTrue(w.TryBeginFixedArray(out _));
        Assert.IsTrue(w.TryWriteUInt64(1, out _));
        Assert.IsTrue(w.TryWriteUInt64(2, out _));
        Assert.IsFalse(w.TryWriteUInt64(3, out status));
        Assert.AreEqual(ABIStatus.ElementLimitExceeded, status);

        // announced dynamic count
        w = Open("(uint256[])", Garbled(256));
        Assert.IsTrue(w.TryBeginDynamicArray(1, out _));
        Assert.IsTrue(w.TryWriteUInt64(1, out _));
        Assert.IsFalse(w.TryWriteUInt64(2, out status));
        Assert.AreEqual(ABIStatus.ElementLimitExceeded, status);
        AssertDead(ref w, new byte[0], "after element overflow");
    }

    [TestMethod]
    public void TooFewElements_AtEndContainerAndAtFinish_AreIncompleteWrite()
    {
        ABIWriter w = Open("((uint256,uint256))", Garbled(128));
        Assert.IsTrue(w.TryBeginTuple(out _));
        Assert.IsTrue(w.TryWriteUInt64(1, out _));
        Assert.IsFalse(w.TryEndContainer(out ABIStatus status)); // premature end
        Assert.AreEqual(ABIStatus.IncompleteWrite, status);
        AssertDead(ref w, Array.Empty<byte>(), "premature end");

        w = Open("(uint256[])", Garbled(256));
        Assert.IsTrue(w.TryBeginDynamicArray(2, out _));
        Assert.IsTrue(w.TryWriteUInt64(1, out _));
        Assert.IsFalse(w.TryEndContainer(out status));
        Assert.AreEqual(ABIStatus.IncompleteWrite, status);

        // missing root argument
        w = Open("(uint256,uint256)", Garbled(128));
        Assert.IsTrue(w.TryWriteUInt64(1, out _));
        Assert.IsFalse(w.TryFinish(out int written, out status));
        Assert.AreEqual(ABIStatus.IncompleteWrite, status);
        Assert.AreEqual(0, written);
        AssertDead(ref w, Array.Empty<byte>(), "incomplete finish");

        // missing end of an open container
        w = Open("((uint256))", Garbled(128));
        Assert.IsTrue(w.TryBeginTuple(out _));
        Assert.IsTrue(w.TryWriteUInt64(1, out _));
        Assert.IsFalse(w.TryFinish(out written, out status));
        Assert.AreEqual(ABIStatus.IncompleteWrite, status);

        // nothing written at all
        w = Open("(uint256)", Garbled(128));
        Assert.IsFalse(w.TryFinish(out written, out status));
        Assert.AreEqual(ABIStatus.IncompleteWrite, status);
    }

    [TestMethod]
    public void FinishedWriter_IsComplete_AndEveryFurtherOperationIsRejectedWithoutMutation()
    {
        byte[] destination = Garbled(64);
        ABIWriter w = Open("(uint256)", destination);
        Assert.IsTrue(w.TryWriteUInt64(9, out _));
        Assert.IsTrue(w.TryFinish(out int written, out ABIStatus status));
        Assert.AreEqual(32, written);
        Assert.AreEqual(ABIStatus.Success, status);

        AssertDead(ref w, destination, "completed writer"); // includes a second finish and writes after completion
        Assert.AreEqual(9, destination[31]);
    }

    [TestMethod]
    public void EndContainer_AtTheRoot_OrTwice_IsInvalidWriterState_AndFaults()
    {
        ABIWriter w = Open("(uint256)", Garbled(64));
        Assert.IsTrue(w.TryWriteUInt64(1, out _));
        Assert.IsFalse(w.TryEndContainer(out ABIStatus status), "the root is closed by TryFinish");
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.IsFalse(w.TryFinish(out _, out status), "the writer is faulted: finishing now must fail");
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);

        w = Open("((uint256))", Garbled(64));
        Assert.IsTrue(w.TryBeginTuple(out _));
        Assert.IsTrue(w.TryWriteUInt64(1, out _));
        Assert.IsTrue(w.TryEndContainer(out _));
        Assert.IsFalse(w.TryEndContainer(out status), "duplicate end");
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.IsFalse(w.TryFinish(out _, out _));

        w = Open("(uint256)", Garbled(64));
        Assert.IsFalse(w.TryEndContainer(out status), "end before anything was opened");
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
    }

    [TestMethod]
    public void AnyFailure_FaultsForGood_EvenOnceTheCauseIsRemoved()
    {
        byte[] destination = Garbled(128);
        ABIWriter w = Open("(uint8,uint256)", destination);
        Assert.IsFalse(w.TryWriteUInt64(256, out ABIStatus status)); // does not fit uint8
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
        Assert.IsFalse(w.TryWriteUInt64(255, out status), "a corrected retry must not resurrect a faulted writer");
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        AssertDead(ref w, destination, "after a rejected value");
    }

    // ------------------------------------------------------------------ type mismatch matrix

    private static readonly string[] SlotTypes =
    [
        "uint256", "int256", "address", "bool", "bytes4", "function", "fixed128x18", "ufixed128x18", "bytes", "string", "(uint8)", "uint8[2]", "uint8[]",
    ];

    // The op index serves exactly this slot (set by hand from the API documentation, independent of the code under test).
    private static readonly string[] OpServes =
    [
        "uint256",      // TryWriteUInt64
        "int256",       // TryWriteInt64
        "uint256",      // TryWriteUInt256Word
        "int256",       // TryWriteInt256Word
        "address",      // TryWriteAddress
        "bool",         // TryWriteBool
        "bytes4",       // TryWriteFixedBytes
        "function",     // TryWriteFunction
        "bytes",        // TryWriteBytes
        "string",       // TryWriteStringUtf8
        "fixed128x18",  // TryWriteSignedScaledWord
        "ufixed128x18", // TryWriteUnsignedScaledWord
        "(uint8)",      // TryBeginTuple
        "uint8[2]",     // TryBeginFixedArray
        "uint8[]",      // TryBeginDynamicArray
    ];

    private static ABIStatus Invoke(ref ABIWriter w, int op)
    {
        ABIStatus s;
        byte[] word = new byte[32];
        byte[] address = new byte[20];
        _ = op switch
        {
            0 => w.TryWriteUInt64(1, out s),
            1 => w.TryWriteInt64(1, out s),
            2 => w.TryWriteUInt256Word(word, out s),
            3 => w.TryWriteInt256Word(word, out s),
            4 => w.TryWriteAddress(address, out s),
            5 => w.TryWriteBool(true, out s),
            6 => w.TryWriteFixedBytes(new byte[4], out s),
            7 => w.TryWriteFunction(address, 7, out s),
            8 => w.TryWriteBytes(new byte[2], out s),
            9 => w.TryWriteStringUtf8(new byte[2], out s),
            10 => w.TryWriteSignedScaledWord(word, out s),
            11 => w.TryWriteUnsignedScaledWord(word, out s),
            12 => w.TryBeginTuple(out s),
            13 => w.TryBeginFixedArray(out s),
            _ => w.TryBeginDynamicArray(1, out s),
        };
        return s;
    }

    [TestMethod]
    public void EveryOperation_AgainstEverySlotKind_SucceedsExactlyWhereTheApiServesThatKind()
    {
        int matches = 0;
        int mismatches = 0;
        foreach (string slot in SlotTypes)
        {
            for (int op = 0; op < OpServes.Length; op++)
            {
                byte[] destination = Garbled(512);
                ABIWriter w = Open($"({slot})", destination);
                byte[] before = destination.ToArray();
                ABIStatus status = Invoke(ref w, op);
                // TryWriteBytes (op 8) also serves a string slot (raw, no text claim); TryWriteStringUtf8 serves string slots only.
                bool serves = OpServes[op] == slot || (op == 8 && slot == "string");
                if (serves)
                {
                    Assert.AreEqual(ABIStatus.Success, status, $"op {op} on {slot}");
                    matches++;
                }
                else
                {
                    Assert.AreEqual(ABIStatus.TypeMismatch, status, $"op {op} on {slot}");
                    CollectionAssert.AreEqual(before, destination, $"op {op} on {slot}: a mismatched write changed the destination");
                    AssertDead(ref w, destination, $"op {op} on {slot}");
                    mismatches++;
                }
            }
        }

        Assert.AreEqual(OpServes.Length + 1, matches, "every operation serves exactly one slot kind (the two integer kinds have two operations each), and the raw bytes operation also serves string");
        Assert.AreEqual(SlotTypes.Length * OpServes.Length - matches, mismatches);
    }

    // ------------------------------------------------------------------ copies and scratch

    [TestMethod]
    public void ACopyOfAWriter_SharesBudgetsPositionAndFaultState_WithTheOriginal()
    {
        // All mutable state lives in the scratch frames, so a struct copy cannot diverge from the original.
        byte[] destination = Garbled(256);
        ABIWriter w = Open("(uint8,uint8,uint8,uint8,uint8)", destination, limits: EncLimits(values: 4)); // the root plus three scalars
        ABIWriter copy = w;
        Assert.IsTrue(w.TryWriteUInt64(1, out ABIStatus status));
        Assert.IsTrue(copy.TryWriteUInt64(2, out status), "the copy continues where the original is");
        Assert.IsTrue(w.TryWriteUInt64(3, out status));
        Assert.IsFalse(copy.TryWriteUInt64(4, out status), "the value budget is shared: the 4th scalar is value number 5");
        Assert.AreEqual(ABIStatus.WorkLimitExceeded, status);
        Assert.IsFalse(w.TryWriteUInt64(5, out status), "a fault seen through the copy is a fault of the original");
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.IsFalse(w.TryFinish(out int written, out status));
        Assert.AreEqual(0, written);

        // Completion is shared too.
        w = Open("(uint8)", Garbled(64));
        copy = w;
        Assert.IsTrue(copy.TryWriteUInt64(9, out _));
        Assert.IsTrue(copy.TryFinish(out written, out _));
        Assert.IsFalse(w.TryWriteUInt64(1, out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.IsFalse(w.TryFinish(out _, out status), "a document cannot be finished twice, through either value");
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);

        // An open container is shared as well: the copy sees the original's nesting.
        w = Open("((uint8))", Garbled(64));
        copy = w;
        Assert.IsTrue(w.TryBeginTuple(out _));
        Assert.IsTrue(copy.TryWriteUInt64(1, out status), "the copy writes inside the tuple the original opened");
        Assert.IsTrue(copy.TryEndContainer(out _));
        Assert.IsTrue(w.TryFinish(out written, out _));
        Assert.AreEqual(32, written);
    }

    [TestMethod]
    public void AWriterWhoseScratchWasReusedByANewWriter_IsDead_AndCannotTouchTheNewOne()
    {
        // Reusing scratch after a document is finished is legitimate; a stale writer variable must not come back to life through it.
        var scratch = new ABIWriteFrame[8];
        ABIEncodeLimits limits = GenerousEncode;
        ABISchema schema = CompileTuple("(uint8)");
        byte[] first = Garbled(64);
        Assert.IsTrue(ABIWriter.TryBegin(schema, first, scratch, in limits, out ABIWriter stale, out _));
        Assert.IsTrue(stale.TryWriteUInt64(1, out _));
        Assert.IsTrue(stale.TryFinish(out _, out _));

        byte[] second = Garbled(64);
        Assert.IsTrue(ABIWriter.TryBegin(schema, second, scratch, in limits, out ABIWriter current, out _));
        byte[] firstBefore = first.ToArray();
        AssertDead(ref stale, first, "stale writer (completed, scratch reused)");
        CollectionAssert.AreEqual(firstBefore, first);

        Assert.IsFalse(current.TryFinish(out _, out ABIStatus status), "the stale calls must not have advanced the new writer");
        Assert.AreEqual(ABIStatus.IncompleteWrite, status);

        // The same for a stale writer that was open (not finished) when its scratch was taken over.
        var scratch2 = new ABIWriteFrame[8];
        Assert.IsTrue(ABIWriter.TryBegin(schema, Garbled(64), scratch2, in limits, out ABIWriter abandoned, out _));
        byte[] third = Garbled(64);
        Assert.IsTrue(ABIWriter.TryBegin(schema, third, scratch2, in limits, out ABIWriter owner, out _));
        Assert.IsFalse(abandoned.TryWriteUInt64(7, out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.IsTrue(owner.TryWriteUInt64(2, out _));
        Assert.IsTrue(owner.TryFinish(out int written, out _));
        Assert.AreEqual(32, written);
        Assert.AreEqual(2, third[31]);

        // A copy made before the takeover is stale as well; a copy made after is live.
        Assert.IsTrue(ABIWriter.TryBegin(schema, Garbled(64), scratch, in limits, out ABIWriter original, out _));
        ABIWriter copy = original;
        Assert.IsTrue(ABIWriter.TryBegin(schema, Garbled(64), scratch, in limits, out ABIWriter replacement, out _));
        Assert.IsFalse(copy.TryWriteUInt64(1, out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        ABIWriter liveCopy = replacement;
        Assert.IsTrue(liveCopy.TryWriteUInt64(1, out _));
    }

    [TestMethod]
    public void TryBegin_InitialisesTheFramesItCanUse_AndLeavesTheRestOfALongScratchAlone()
    {
        var scratch = new ABIWriteFrame[32];
        MemoryMarshal.AsBytes(scratch.AsSpan()).Fill(0xff); // stale caller content, including a bogus "Open" state value
        var limits = EncLimits(depth: 4);
        Assert.IsTrue(ABIWriter.TryBegin(CompileTuple("(uint8)"), new byte[32], scratch, in limits, out ABIWriter w, out _));
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(scratch.AsSpan());
        int frameSize = bytes.Length / scratch.Length;
        Assert.AreEqual(52, frameSize);
        for (int frame = 1; frame < 4; frame++)
        {
            Assert.IsTrue(bytes.Slice(frame * frameSize, frameSize).IndexOfAnyExcept((byte)0) < 0, $"frame {frame} (usable under depth 4) must be initialised");
        }

        Assert.IsTrue(bytes.Slice(4 * frameSize).IndexOfAnyExcept((byte)0xff) < 0, "frames no depth can reach are not touched (no cost for a long scratch span)");
        Assert.IsTrue(w.TryWriteUInt64(1, out _));
        Assert.IsTrue(w.TryFinish(out int written, out _));
        Assert.AreEqual(32, written);
    }

    [TestMethod]
    public void GarbageInTheScratch_NeverChangesTheOutput()
    {
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var (_, _, expected, schema, script) = Generate(seed * 43);
            var scratch = new ABIWriteFrame[32];
            foreach (byte fill in new byte[] { 0x00, 0xff, 0x5a })
            {
                MemoryMarshal.AsBytes(scratch.AsSpan()).Fill(fill);
                byte[] destination = new byte[expected.Length];
                ABIEncodeLimits limits = GenerousEncode;
                Assert.IsTrue(ABIWriter.TryBegin(schema, destination, scratch, in limits, out ABIWriter w, out ABIStatus status), status.ToString());
                foreach (Ev ev in script)
                {
                    Assert.IsTrue(Apply(ref w, ev, out status), $"seed {seed * 43} fill {fill:x2}: {status}");
                }

                Assert.IsTrue(w.TryFinish(out int written, out _));
                Assert.AreEqual(expected.Length, written);
                CollectionAssert.AreEqual(expected, destination, $"seed {seed * 43} fill {fill:x2}");
            }
        }
    }

    // ------------------------------------------------------------------ measurer parity

    [TestMethod]
    public void Measurer_FaultsAndRejectsLikeTheWriter()
    {
        var limits = GenerousEncode;
        Assert.IsTrue(ABIMeasurer.TryBegin(CompileTuple("(uint8,bytes)"), new ABIMeasureFrame[4], in limits, out ABIMeasurer m, out ABIStatus status));
        Assert.IsFalse(m.TryWriteUInt64(256, out status));
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
        Assert.IsFalse(m.TryWriteUInt64(1, out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.IsFalse(m.TryFinish(out int length, out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.AreEqual(0, length);

        Assert.IsFalse(ABIMeasurer.TryBegin(null!, new ABIMeasureFrame[4], in limits, out m, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        var bad = default(ABIEncodeLimits);
        Assert.IsFalse(ABIMeasurer.TryBegin(CompileTuple("(uint8)"), new ABIMeasureFrame[4], in bad, out m, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.IsFalse(ABIMeasurer.TryBegin(CompileTuple("(uint8)"), Span<ABIMeasureFrame>.Empty, in limits, out m, out status));
        Assert.AreEqual(ABIStatus.InsufficientScratch, status);
        var tiny = limits with { MaxEncodedBytes = 16 };
        Assert.IsFalse(ABIMeasurer.TryBegin(CompileTuple("(uint8)"), new ABIMeasureFrame[4], in tiny, out m, out status));
        Assert.AreEqual(ABIStatus.InputTooLarge, status);
    }
}
