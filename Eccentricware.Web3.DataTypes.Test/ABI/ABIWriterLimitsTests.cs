using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIWriteTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// ABIEncodeLimits enforcement (spec 7.1, 7.5, 10.3): hand-computed pinned budgets at limit-1, limit and limit+1; a differential
/// proof that the writer charges its budgets exactly like the decoder (the smallest limit that lets a random document through is the
/// same number for the writer and the decoder, in every dimension); call-limit arithmetic; and default limits that must not reject
/// normal real-chain traffic.
/// </summary>
[TestClass]
public sealed class ABIWriterLimitsTests
{
    private enum Dimension
    {
        Bytes,
        Depth,
        Array,
        Values,
        Work,
    }

    private static ABIEncodeLimits Encode(Dimension dimension, int value)
    {
        ABIEncodeLimits g = GenerousEncode;
        return dimension switch
        {
            Dimension.Bytes => g with { MaxEncodedBytes = value },
            Dimension.Depth => g with { MaxValueDepth = value },
            Dimension.Array => g with { MaxArrayElements = value },
            Dimension.Values => g with { MaxWrittenValues = value },
            _ => g with { MaxWorkUnits = value },
        };
    }

    private static ABIDecodeLimits Decode(Dimension dimension, int value)
    {
        ABIDecodeLimits g = Generous;
        return dimension switch
        {
            Dimension.Bytes => g with { MaxEncodedBytes = value },
            Dimension.Depth => g with { MaxValueDepth = value },
            Dimension.Array => g with { MaxArrayElements = value },
            Dimension.Values => g with { MaxVisitedValues = value },
            _ => g with { MaxWorkUnits = value },
        };
    }

    private static ABIStatus Failure(Dimension dimension) => dimension switch
    {
        Dimension.Bytes => ABIStatus.InputTooLarge,
        Dimension.Depth => ABIStatus.DepthLimitExceeded,
        Dimension.Array => ABIStatus.ElementLimitExceeded,
        _ => ABIStatus.WorkLimitExceeded,
    };

    /// <summary>Asserts that the pinned budget is exactly the smallest one that lets the document through, for the writer and the measurer.</summary>
    private static void AssertBudget(string tuple, List<Ev> script, Dimension dimension, int exact)
    {
        ABISchema schema = CompileTuple(tuple);
        byte[] buffer = new byte[1 << 20];
        foreach (int value in dimension == Dimension.Depth ? new[] { exact, 32 } : new[] { exact, exact + 1, exact + 100 })
        {
            Assert.IsTrue(RunWriter(script, schema, buffer, Encode(dimension, value)).Ok, $"{tuple} {dimension} limit {value}");
            Assert.IsTrue(RunMeasurer(script, schema, Encode(dimension, value)).Ok, $"{tuple} {dimension} limit {value} (measurer)");
        }

        if (exact > 1)
        {
            RunResult written = RunWriter(script, schema, buffer, Encode(dimension, exact - 1));
            Assert.IsFalse(written.Ok, $"{tuple} {dimension} limit {exact - 1}");
            Assert.AreEqual(Failure(dimension), written.Status, $"{tuple} {dimension} limit {exact - 1}");
            RunResult measured = RunMeasurer(script, schema, Encode(dimension, exact - 1));
            Assert.AreEqual(written.Status, measured.Status, $"{tuple} {dimension}: measurer");
            Assert.AreEqual(written.FailedAt, measured.FailedAt, $"{tuple} {dimension}: failing event");
        }
    }

    // ------------------------------------------------------------------ pinned budgets (hand computed)

    [TestMethod]
    public void TwoStaticWords_NeedExactly_64Bytes_Depth2_3Values_5WorkUnits()
    {
        // (address,uint256): root (1 value, 1 work) + two scalars (1 value and 2 work each).
        List<Ev> script = [Ev.Addr(new byte[20]), Ev.U64(1)];
        AssertBudget("(address,uint256)", script, Dimension.Bytes, 64);
        AssertBudget("(address,uint256)", script, Dimension.Depth, 2);
        AssertBudget("(address,uint256)", script, Dimension.Values, 3);
        AssertBudget("(address,uint256)", script, Dimension.Work, 5);
        AssertBudget("(address,uint256)", script, Dimension.Array, 1); // no dynamic array: any positive limit passes
    }

    [TestMethod]
    public void DynamicBytes_ChargeTheLengthWordAndThePartialPaddingWord()
    {
        // 33 bytes: pointer + length + 2 data words of which the last is partial: 1 + (value 1 + pointer 1 + length 1 + padding 1) = 5 work.
        List<Ev> partial = [Ev.Blob(new byte[33])];
        AssertBudget("(bytes)", partial, Dimension.Bytes, 32 + 32 + 64);
        AssertBudget("(bytes)", partial, Dimension.Work, 5);
        AssertBudget("(bytes)", partial, Dimension.Values, 2);
        AssertBudget("(bytes)", partial, Dimension.Depth, 2);

        // 32 bytes: whole number of words, no padding to examine: 4 work.
        AssertBudget("(bytes)", [Ev.Blob(new byte[32])], Dimension.Work, 4);

        // Empty bytes: pointer + length only: 4 work, 64 bytes.
        AssertBudget("(bytes)", [Ev.Blob([])], Dimension.Work, 4);
        AssertBudget("(bytes)", [Ev.Blob([])], Dimension.Bytes, 64);

        // A string charges the same.
        AssertBudget("(string)", [Ev.Text(Ascii("abc"))], Dimension.Work, 5);
    }

    [TestMethod]
    public void DynamicArray_ChargesCountAndElements_AndBoundsOnlyTheCount()
    {
        List<Ev> script = [Ev.Begin("dynamic", 3), Ev.U64(1), Ev.U64(2), Ev.U64(3), Ev.End];
        AssertBudget("(uint256[])", script, Dimension.Array, 3);
        AssertBudget("(uint256[])", script, Dimension.Depth, 3);  // root 1, array 2, element 3
        AssertBudget("(uint256[])", script, Dimension.Values, 5); // root, array, 3 elements
        AssertBudget("(uint256[])", script, Dimension.Work, 10);  // root 1 + array (value, pointer, count) 3 + 3 * (value, word)
        AssertBudget("(uint256[])", script, Dimension.Bytes, 32 + 32 + 96);
    }

    [TestMethod]
    public void FixedArrays_AreNotBoundedByMaxArrayElements()
    {
        // A fixed length comes from the trusted schema, exactly as in the decoder.
        List<Ev> script = [Ev.Begin("fixed"), Ev.U64(1), Ev.U64(2), Ev.U64(3), Ev.End];
        ABISchema schema = CompileTuple("(uint256[3])");
        Assert.IsTrue(RunWriter(script, schema, new byte[96], EncLimits(array: 1)).Ok);
        AssertBudget("(uint256[3])", script, Dimension.Work, 8); // root 1 + array value 1 + 3 * 2
        AssertBudget("(uint256[3])", script, Dimension.Values, 5);
    }

    [TestMethod]
    public void StaticTupleInline_AddsADepthLevel_ButNoWordCharge()
    {
        List<Ev> script = [Ev.Begin("tuple"), Ev.U64(1), Ev.U64(2), Ev.End];
        AssertBudget("((uint256,uint256))", script, Dimension.Depth, 3);
        AssertBudget("((uint256,uint256))", script, Dimension.Values, 4);
        AssertBudget("((uint256,uint256))", script, Dimension.Work, 6); // root 1 + tuple value 1 + 2 * 2
        AssertBudget("((uint256,uint256))", script, Dimension.Bytes, 64);
    }

    [TestMethod]
    public void ZeroWidthTuples_AreBoundedByValuesAndWork_NotBytes()
    {
        // (()[]) with three empty elements: 32-byte count word only, yet 5 values and 7 work units.
        List<Ev> script = [Ev.Begin("dynamic", 3), Ev.Begin("tuple"), Ev.End, Ev.Begin("tuple"), Ev.End, Ev.Begin("tuple"), Ev.End, Ev.End];
        AssertBudget("(()[])", script, Dimension.Bytes, 64);
        AssertBudget("(()[])", script, Dimension.Values, 5);
        AssertBudget("(()[])", script, Dimension.Work, 7);
        AssertBudget("(()[])", script, Dimension.Depth, 3);
    }

    [TestMethod]
    public void DepthLimit_OnlyAdmitsAZeroArgumentRootAtDepth1()
    {
        ABISchema empty = CompileTuple("()");
        Assert.IsTrue(RunWriter([], empty, new byte[0], EncLimits(depth: 1)).Ok);

        ABISchema one = CompileTuple("(uint256)");
        RunResult result = RunWriter([Ev.U64(1)], one, new byte[32], EncLimits(depth: 1));
        Assert.AreEqual(ABIStatus.DepthLimitExceeded, result.Status);
        Assert.AreEqual(0, result.FailedAt);
        Assert.IsTrue(RunWriter([Ev.U64(1)], one, new byte[32], EncLimits(depth: 2)).Ok);
    }

    [TestMethod]
    public void ByteLimit_AppliesToTheDocument_WhateverTheDestinationSize()
    {
        ABISchema schema = CompileTuple("(uint256,uint256)");
        List<Ev> script = [Ev.U64(1), Ev.U64(2)];
        // The root head (64 bytes) is already above a 63-byte limit, so TryBegin refuses; 64 passes.
        RunResult small = RunWriter(script, schema, new byte[4096], EncLimits(bytes: 63));
        Assert.AreEqual(ABIStatus.InputTooLarge, small.Status);
        Assert.AreEqual(-1, small.FailedAt, "reported by TryBegin");
        Assert.IsTrue(RunWriter(script, schema, new byte[4096], EncLimits(bytes: 64)).Ok);

        // A limit above the destination size is not a reason to overrun the destination.
        RunResult shortDestination = RunWriter(script, schema, new byte[63], EncLimits(bytes: 4096));
        Assert.AreEqual(ABIStatus.InsufficientDestination, shortDestination.Status);
    }

    [TestMethod]
    public void BudgetsOfIntMaxValue_CannotWrapTheCounters()
    {
        var limits = new ABIEncodeLimits(int.MaxValue, 32, int.MaxValue, int.MaxValue, int.MaxValue);
        var (type, _, expected, schema, script) = Generate(99);
        byte[] buffer = new byte[expected.Length];
        Assert.IsTrue(RunWriter(script, schema, buffer, limits).Ok, type.Text);
        CollectionAssert.AreEqual(expected, buffer);
    }

    // ------------------------------------------------------------------ differential against the decoder budgets

    private static int Smallest(int low, int high, Func<int, bool> ok)
    {
        if (!ok(high))
        {
            return -1;
        }

        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (ok(mid))
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return low;
    }

    [TestMethod]
    public void WriterBudgets_AreChargedExactlyLikeTheDecoders_OnRandomDocuments()
    {
        (Dimension Dimension, int High)[] dimensions = [(Dimension.Bytes, 100_000), (Dimension.Depth, 32), (Dimension.Array, 5_000), (Dimension.Values, 100_000), (Dimension.Work, 1_000_000)];
        int compared = 0;
        for (ulong seed = 1; seed <= 150; seed++)
        {
            var (type, _, expected, schema, script) = Generate(seed * 3, depth: 4);
            byte[] buffer = new byte[expected.Length + 8];
            foreach (var (dimension, high) in dimensions)
            {
                int writerMin = Smallest(1, high, v => RunWriter(script, schema, buffer, Encode(dimension, v)).Ok);
                int measurerMin = Smallest(1, high, v => RunMeasurer(script, schema, Encode(dimension, v)).Ok);
                int decoderMin = Smallest(1, high, v =>
                {
                    ABIDecodeLimits limits = Decode(dimension, v);
                    return ABIData.TryDecodeComplete(expected, schema, in limits, out _, out _);
                });
                string label = $"seed {seed * 3} {dimension} {type.Text}";
                Assert.AreNotEqual(-1, decoderMin, label);
                Assert.AreEqual(decoderMin, writerMin, $"{label}: writer needs a different budget than the decoder");
                Assert.AreEqual(writerMin, measurerMin, $"{label}: measurer differs from writer");

                if (decoderMin > 1)
                {
                    RunResult below = RunWriter(script, schema, buffer, Encode(dimension, decoderMin - 1));
                    ABIEncodeLimits belowLimits = Encode(dimension, decoderMin - 1);
                    Assert.AreEqual(Failure(dimension), below.Status, label);
                    Assert.AreEqual(below.Status, RunMeasurer(script, schema, belowLimits).Status, label);
                    ABIDecodeLimits decodeBelow = Decode(dimension, decoderMin - 1);
                    ABIData.TryDecodeComplete(expected, schema, in decodeBelow, out _, out ABIStatus decodeStatus);
                    Assert.AreEqual(decodeStatus, below.Status, $"{label}: same failure status as the decoder");
                }

                compared++;
            }
        }

        Assert.AreEqual(750, compared);
    }

    [TestMethod]
    public void RandomLimitCombinations_AcceptTheSameDocuments_AsTheDecoder()
    {
        int accepted = 0;
        int rejected = 0;
        var rng = new DeterministicRng(2024);
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var (type, _, expected, schema, script) = Generate(seed * 5, depth: 4);
            for (int round = 0; round < 12; round++)
            {
                int bytes = Math.Max(1, expected.Length + rng.Next(41) - 20);
                int depth = 1 + rng.Next(6);
                int array = 1 + rng.Next(5);
                int values = 1 + rng.Next(40);
                int work = 1 + rng.Next(150);
                var enc = new ABIEncodeLimits(bytes, depth, array, values, work);
                var dec = new ABIDecodeLimits(bytes, depth, array, values, work);
                string label = $"seed {seed * 5} round {round} {type.Text} {enc}";

                bool decoderAccepts = ABIData.TryDecodeComplete(expected, schema, in dec, out _, out _);
                byte[] buffer = new byte[expected.Length + 4];
                RunResult written = RunWriter(script, schema, buffer, enc);
                RunResult measured = RunMeasurer(script, schema, enc);
                Assert.AreEqual(decoderAccepts, written.Ok, $"{label}: writer {written.Status}");
                Assert.AreEqual(written, measured with { }, $"{label}: measurer");
                if (written.Ok)
                {
                    accepted++;
                    CollectionAssert.AreEqual(expected, buffer[..written.Length], label);
                }
                else
                {
                    rejected++;
                }
            }
        }

        Assert.IsTrue(accepted > 200, $"only {accepted} accepted");
        Assert.IsTrue(rejected > 200, $"only {rejected} rejected");
    }

    // ------------------------------------------------------------------ call limits

    [TestMethod]
    public void CallLimit_CoversTheSelector_AndLeavesMaxMinus4ForTheArguments()
    {
        ABIFunctionSchema transfer = Function("transfer(address,uint256)");
        List<Ev> script = [Ev.Addr(new byte[20]), Ev.U64(1)];
        byte[] buffer = new byte[200];
        Assert.IsTrue(RunCall(script, transfer, buffer, EncLimits(bytes: 68)).Ok);
        RunResult tight = RunCall(script, transfer, buffer, EncLimits(bytes: 67));
        Assert.AreEqual(ABIStatus.InputTooLarge, tight.Status);

        // MaxEncodedBytes below 4 cannot even hold the selector.
        for (int max = 1; max < 4; max++)
        {
            byte[] untouched = new byte[16];
            Array.Fill(untouched, Garbage);
            RunResult result = RunCall([], Function("totalSupply()"), untouched, EncLimits(bytes: max));
            Assert.AreEqual(ABIStatus.InputTooLarge, result.Status, $"MaxEncodedBytes {max}");
            Assert.IsTrue(untouched.All(b => b == Garbage), "nothing may be written when the selector cannot fit the limit");
        }

        // Exactly 4: a selector-only call fits.
        RunResult selectorOnly = RunCall([], Function("totalSupply()"), new byte[16], EncLimits(bytes: 4));
        Assert.IsTrue(selectorOnly.Ok);
        Assert.AreEqual(4, selectorOnly.Length);
    }

    [TestMethod]
    public void CallEncoder_DestinationTooSmall_WritesNothing_NotEvenTheSelector()
    {
        ABIFunctionSchema transfer = Function("transfer(address,uint256)");
        foreach (int length in new[] { 0, 3, 4, 35, 67 })
        {
            byte[] destination = new byte[length];
            Array.Fill(destination, Garbage);
            RunResult result = RunCall([Ev.Addr(new byte[20]), Ev.U64(1)], transfer, destination, GenerousEncode);
            Assert.IsFalse(result.Ok, $"{length}");
            Assert.AreEqual(ABIStatus.InsufficientDestination, result.Status, $"{length}");
            Assert.AreEqual(-1, result.FailedAt, "reported by TryBegin");
            Assert.IsTrue(destination.All(b => b == Garbage), $"destination {length}: selector must not be written when the head does not fit");
        }

        // A selector-only function fits in exactly 4 bytes.
        byte[] four = new byte[4];
        Assert.IsTrue(RunCall([], Function("totalSupply()"), four, GenerousEncode).Ok);
        CollectionAssert.AreEqual(new byte[] { 0x18, 0x16, 0x0d, 0xdd }, four);
    }

    [TestMethod]
    public void MulticallCall_388Bytes_IsAcceptedAtExactlyTheLimit_AndRejectedBelow()
    {
        ABIFunctionSchema multicall = Function("multicall(bytes[])");
        List<Ev> script =
        [
            Ev.Begin("dynamic", 2), Ev.Blob(Convert.FromHexString(ABIFixtures.TransferChild1)), Ev.Blob(Convert.FromHexString(ABIFixtures.TransferChild2)), Ev.End,
        ];
        byte[] buffer = new byte[400];
        RunResult exact = RunCall(script, multicall, buffer, EncLimits(bytes: 388));
        Assert.IsTrue(exact.Ok);
        Assert.AreEqual(388, exact.Length);
        RunResult below = RunCall(script, multicall, new byte[400], EncLimits(bytes: 387));
        Assert.AreEqual(ABIStatus.InputTooLarge, below.Status);
    }

    // ------------------------------------------------------------------ real traffic under the default limits

    private static byte[] EncodeWithDefaults(string tuple, List<Ev> script, int capacity)
    {
        ABISchema schema = CompileTuple(tuple);
        byte[] buffer = new byte[capacity];
        RunResult result = RunWriter(script, schema, buffer, ABIEncodeLimits.Default);
        Assert.IsTrue(result.Ok, $"{tuple}: {result.Status} at {result.FailedAt}: a default limit must not reject real traffic");
        byte[] document = buffer[..result.Length];
        DecodeOk(document, schema); // the matching default decode limits accept it as well
        return document;
    }

    [TestMethod]
    public void DefaultLimits_AcceptRealisticLargeTraffic()
    {
        byte[] address = Addr20(0x11);

        // 4000 (address,uint256) pairs (an airdrop batch).
        var pairs = new List<Ev> { Ev.Begin("dynamic", 4000) };
        for (int i = 0; i < 4000; i++)
        {
            pairs.AddRange([Ev.Begin("tuple"), Ev.Addr(address), Ev.U64((ulong)i), Ev.End]);
        }

        pairs.Add(Ev.End);
        Assert.AreEqual(32 + 32 + 4000 * 64, EncodeWithDefaults("((address,uint256)[])", pairs, 300_000).Length);

        // 4096 bytes32 words (a merkle proof at the element limit).
        var proof = new List<Ev> { Ev.Begin("dynamic", 4096) };
        for (int i = 0; i < 4096; i++)
        {
            proof.Add(Ev.FixedB(new byte[32]));
        }

        proof.Add(Ev.End);
        Assert.AreEqual(32 + 32 + 4096 * 32, EncodeWithDefaults("(bytes32[])", proof, 200_000).Length);

        // A blob that makes the document exactly the 262,144-byte default limit, and one byte more is refused.
        byte[] blob = new byte[262_144 - 64];
        Assert.AreEqual(262_144, EncodeWithDefaults("(bytes)", [Ev.Blob(blob)], 262_144).Length);
        RunResult tooBig = RunWriter([Ev.Blob(new byte[blob.Length + 1])], CompileTuple("(bytes)"), new byte[400_000], ABIEncodeLimits.Default);
        Assert.AreEqual(ABIStatus.InputTooLarge, tooBig.Status);

        // A 100-child multicall of 68-byte transfers.
        byte[] transfer = Convert.FromHexString(ABIFixtures.TransferChild1);
        var multicall = new List<Ev> { Ev.Begin("dynamic", 100) };
        multicall.AddRange(Enumerable.Repeat(Ev.Blob(transfer), 100));
        multicall.Add(Ev.End);
        Assert.AreEqual(32 + 32 + 100 * 32 + 100 * 128, EncodeWithDefaults("(bytes[])", multicall, 40_000).Length);
    }
}
