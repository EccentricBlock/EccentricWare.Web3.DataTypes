using System.Diagnostics;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Limit validation, error ordering (spec 5.2) and the exact work accounting of spec 6.4: for every budget, limit - 1, limit and limit + 1.
/// Visited and work counts below are derived by hand: one visited value and one work unit per value entered; one work unit per 32-byte word
/// examined (scalar, offset, length, count, and one for the partially used last word when padding exists); payload data words cost nothing.
/// </summary>
[TestClass]
public sealed class ABIDecodeLimitsTests
{
    private sealed record Case(string Name, string Tuple, byte[] Document, int Visited, int Work);

    private static readonly Case[] Cases =
    [
        // root 1/1; each primitive: entry 1/1 + word 0/1.
        new("transfer args", "(address,uint256)", Cat(Word(1), Word(2)), Visited: 3, Work: 5),
        new("zero arguments", "()", [], 1, 1),
        // root; bytes entry 1/1; pointer word 0/1; length word 0/1; no padding.
        new("empty bytes", "(bytes)", Cat(Word(0x20), Word(0)), 2, 4),
        new("bytes of 32", "(bytes)", Cat(Word(0x20), BytesValue(new byte[32])), 2, 4),
        // as above plus one work unit for the partially used last word.
        new("bytes of 1", "(bytes)", Cat(Word(0x20), BytesValue(new byte[1])), 2, 5),
        new("bytes of 33", "(bytes)", Cat(Word(0x20), BytesValue(new byte[33])), 2, 5),
        new("bytes of 64", "(bytes)", Cat(Word(0x20), BytesValue(new byte[64])), 2, 4),
        // root 1/1; array entry 1/1, pointer 0/1, count 0/1; 3 elements * (1/1 + 0/1).
        new("uint256[3] dynamic", "(uint256[])", Cat(Word(0x20), Word(3), Word(1), Word(2), Word(3)), 5, 10),
        // root 1/1; array entry 1/1 (no pointer word: static); 3 * (1/2).
        new("uint256[3] static", "(uint256[3])", Cat(Word(1), Word(2), Word(3)), 5, 8),
        // root 1/1; tuple 1/1; two primitives 2/4; one primitive 1/2.
        new("static tuple in tuple", "((uint256,uint256),uint256)", Cat(Word(1), Word(2), Word(3)), 5, 8),
        new("empty tuples", "((),(()))", [], 4, 4),
        // root 1/1; array 1/1 + pointer 1 + count 1; 5 zero-width elements 5/5.
        new("5 empty tuples", "(()[])", Cat(Word(0x20), Word(5)), 7, 9),
        // root 1/1; string 1/1 + pointer + length + padding = 4; second string same; plus the bool 1/2.
        new("string,bool,string", "(string,bool,string)", Cat(Word(0x60), Word(1), Word(0xa0), BytesValue(Ascii("a")), BytesValue(Ascii("b"))), 4, 1 + 4 + 2 + 4),
    ];

    [TestMethod]
    public void WorkAndVisitedBudgets_AreExact_AtLimitMinusOneLimitAndLimitPlusOne()
    {
        foreach (Case c in Cases)
        {
            ABISchema schema = CompileTuple(c.Tuple);
            // Visited: the exact count passes, one less fails; work is generous.
            DecodeOk(c.Document, schema, Limits(visited: c.Visited));
            DecodeOk(c.Document, schema, Limits(visited: c.Visited + 1));
            if (c.Visited > 1)
            {
                DecodeFails(c.Document, schema, ABIStatus.WorkLimitExceeded, Limits(visited: c.Visited - 1), c.Name + " visited - 1");
            }

            // Work: likewise.
            DecodeOk(c.Document, schema, Limits(work: c.Work));
            DecodeOk(c.Document, schema, Limits(work: c.Work + 1));
            if (c.Work > 1)
            {
                DecodeFails(c.Document, schema, ABIStatus.WorkLimitExceeded, Limits(work: c.Work - 1), c.Name + " work - 1");
            }
        }
    }

    [TestMethod]
    public void WorkExhaustion_IsReportedAtTheFirstChargeThatExceedsTheBudget()
    {
        // (uint256,uint256) = 1 root + (1 + word) per primitive. Work 4 allows the root and the first primitive entirely, then fails at the second one.
        ABISchema schema = CompileTuple("(uint256,uint256)");
        byte[] doc = Cat(Word(1), Word(2));
        ABIError atSecondEntry = DecodeFails(doc, schema, ABIStatus.WorkLimitExceeded, Limits(work: 3), "root + first entry + first word");
        Assert.AreEqual(32, atSecondEntry.DocumentOffset);
        ABIError atSecondWord = DecodeFails(doc, schema, ABIStatus.WorkLimitExceeded, Limits(work: 4));
        Assert.AreEqual(32, atSecondWord.DocumentOffset);
        Assert.AreEqual(2, atSecondWord.SchemaNode);
    }

    [TestMethod]
    public void BudgetsDoNotMaskStructuralErrorsThatComeFirst()
    {
        // A corrupt first value fails with its structural status before a tiny budget could fail the second one.
        ABISchema schema = CompileTuple("(uint8,uint8)");
        DecodeFails(Cat(Word(256), Word(1)), schema, ABIStatus.InvalidScalar, Limits(work: 100));
        // And with the budget exhausted first, the budget status wins at the first attempted charge.
        DecodeFails(Cat(Word(256), Word(1)), schema, ABIStatus.WorkLimitExceeded, Limits(work: 2));
    }

    [TestMethod]
    public void MaxValueDepth_LimitMinusOneLimitAndLimitPlusOne()
    {
        ABISchema schema = CompileTuple("((uint256[]))"); // root 1 > tuple 2 > array 3 > element 4 (when present)
        byte[] oneElement = Cat(Word(0x20), Word(0x20), Word(1), Word(7));
        byte[] noElement = Cat(Word(0x20), Word(0x20), Word(0));
        DecodeOk(oneElement, schema, Limits(depth: 4));
        DecodeOk(oneElement, schema, Limits(depth: 5));
        DecodeFails(oneElement, schema, ABIStatus.DepthLimitExceeded, Limits(depth: 3));
        DecodeOk(noElement, schema, Limits(depth: 3));
        DecodeFails(noElement, schema, ABIStatus.DepthLimitExceeded, Limits(depth: 2));
        DecodeFails(noElement, schema, ABIStatus.DepthLimitExceeded, Limits(depth: 1));
    }

    [TestMethod]
    public void MaxEncodedBytes_LimitMinusOneLimitAndLimitPlusOne()
    {
        ABISchema schema = CompileTuple("(uint256,uint256)");
        byte[] doc = Cat(Word(1), Word(2));
        DecodeOk(doc, schema, Limits(bytes: 64));
        DecodeOk(doc, schema, Limits(bytes: 65));
        DecodeFails(doc, schema, ABIStatus.InputTooLarge, Limits(bytes: 63));
        DecodeFails(doc, schema, ABIStatus.InputTooLarge, Limits(bytes: 1));
        DecodeOk(doc, schema, Limits(bytes: int.MaxValue));
        DecodeOk([], CompileTuple("()"), Limits(bytes: 1));
    }

    [TestMethod]
    public void InvalidLimits_AreInvalidArgument_ForEveryFieldAndEveryTypeOfBadValue()
    {
        ABISchema schema = CompileTuple("(uint256)");
        byte[] doc = Word(1);
        foreach (ABIDecodeLimits bad in new[]
        {
            default,
            Limits(bytes: 0), Limits(bytes: -1), Limits(bytes: int.MinValue),
            Limits(depth: 0), Limits(depth: -1), Limits(depth: 33), Limits(depth: int.MaxValue), Limits(depth: int.MinValue),
            Limits(array: 0), Limits(array: -1), Limits(array: int.MinValue),
            Limits(visited: 0), Limits(visited: -1), Limits(visited: int.MinValue),
            Limits(work: 0), Limits(work: -1), Limits(work: int.MinValue),
        })
        {
            DecodeFails(doc, schema, ABIStatus.InvalidArgument, bad, bad.ToString());
        }

        DecodeOk(doc, schema, Limits(depth: 1 + 1));
        DecodeOk(doc, schema, Limits(depth: 32));
    }

    [TestMethod]
    public void NullSchema_IsInvalidArgument()
    {
        var limits = ABIDecodeLimits.Default;
        Assert.IsFalse(ABIData.TryDecodeComplete(Word(1), null!, in limits, out var data, out var status, out var error));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.AreEqual(ABIStatus.InvalidArgument, error.Status);
        Assert.IsFalse(data.IsValid);
    }

    [TestMethod]
    public void ErrorOrdering_SchemaAndLimits_ThenSize_ThenContent()
    {
        ABISchema schema = CompileTuple("(uint8)");
        byte[] garbage = Filled(0xff); // content error: invalid scalar
        // (1) invalid limits beat an oversized input.
        DecodeFails([.. garbage, .. garbage], schema, ABIStatus.InvalidArgument, Limits(bytes: 8, work: 0));
        // (2) size beats content.
        DecodeFails(garbage, schema, ABIStatus.InputTooLarge, Limits(bytes: 31));
        // (4) then content.
        DecodeFails(garbage, schema, ABIStatus.InvalidScalar);
    }

    [TestMethod]
    public void CounterArithmetic_AtInt32Maximum_DoesNotOverflow()
    {
        // Budgets of int.MaxValue are legal; the counters are compared before they are incremented, so they can never wrap.
        var huge = Limits(bytes: int.MaxValue, depth: 32, array: int.MaxValue, visited: int.MaxValue, work: int.MaxValue);
        foreach (Case c in Cases)
        {
            DecodeOk(c.Document, CompileTuple(c.Tuple), huge);
        }
    }

    [TestMethod]
    public void DefaultLimits_AcceptRealisticLargeBatches()
    {
        // Real traffic must not be rejected by a "documented limit" (project rule): airdrop-style batches, Merkle proofs and large byte blobs.
        var pairs = new RSeq([new RSeq(Enumerable.Range(0, 4000).Select(i => (RValue)new RSeq([new RWord(Cat(new byte[12], Filled((byte)i)[..20])), new RWord(Word((ulong)i * 1_000_000_007))])).ToArray())]);
        var pairsType = ABIReference.ParseTuple("((address,uint256)[])");
        byte[] batch = ABIReference.EncodeRoot(pairsType, pairs);
        Assert.IsTrue(batch.Length < 262_144, "4000 (address,uint256) pairs are 256,064 bytes");
        DecodeOk("((address,uint256)[])", batch);

        byte[] proof = ABIReference.EncodeRoot(ABIReference.ParseTuple("(bytes32[])"), new RSeq([new RSeq(Enumerable.Range(0, 4096).Select(i => (RValue)new RWord(Filled((byte)i))).ToArray())]));
        DecodeOk("(bytes32[])", proof); // 4096 words: 131,136 bytes

        byte[] largestBlob = Cat(Word(0x20), BytesValue(new byte[262_144 - 64]));
        Assert.AreEqual(262_144, largestBlob.Length);
        DecodeOk("(bytes)", largestBlob);

        // Multicall with 100 children of 260 bytes each (a DEX aggregator batch).
        var children = Enumerable.Range(0, 100).Select(i => (RValue)new RBlob(new byte[260])).ToArray();
        byte[] multicall = ABIReference.EncodeRoot(ABIReference.ParseTuple("(bytes[])"), new RSeq([new RSeq(children)]));
        DecodeOk("(bytes[])", multicall);
    }

    [TestMethod]
    public void TheValidationFrame_IsAnUnmanagedFixedSizeStruct_AndTheDeepTierMatchesTheHardDepthCap()
    {
        Assert.IsFalse(System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ABIValidationFrame>(), "no managed references in a frame (spec 6.2)");
        Assert.AreEqual(8 * sizeof(int), System.Runtime.CompilerServices.Unsafe.SizeOf<ABIValidationFrame>());
        Assert.AreEqual(32, ABIDecodeLimits.HardMaxValueDepth, "spec 6.1: ABIValidationFrame[32]");
        Assert.AreEqual(ABIDecodeLimits.HardMaxValueDepth, SchemaDepthCap, "a compiled schema can never need more frames than the deep tier holds");
    }

    [TestMethod]
    public void ASchemaDeeperThanTheDepthLimit_StillDecodesDocumentsThatStayShallow_OnEveryFrameTier()
    {
        // 30 array dimensions: the schema needs 32 frames in the worst case, but an empty outer array enters only depth 2.
        string deep = "(uint256" + string.Concat(Enumerable.Repeat("[]", 30)) + ")";
        ABISchema schema = CompileTuple(deep);
        byte[] shallowDocument = Cat(Word(0x20), Word(0));
        foreach (int depth in new[] { 2, 3, 7, 8, 9, 31, 32 })
        {
            DecodeOk(shallowDocument, schema, Limits(depth: depth));
        }

        DecodeFails(shallowDocument, schema, ABIStatus.DepthLimitExceeded, Limits(depth: 1));
    }

    // ------------------------------------------------------------------ the limits struct is read once

    // Read through a field so the assertion above compares two real values (the analyzer rejects constant-only assertions).
    private static readonly int SchemaDepthCap = ABICompileLimits.HardMaxSchemaDepth;

    private static ABIDecodeLimits sharedLimits = ABIDecodeLimits.Default;

    [TestMethod]
    [DoNotParallelize]
    public void LimitsFlippedByAnotherThread_NeverCauseAnExceptionOrAWrongFrameBucket()
    {
        // Deep schema so the frame bucket (8 vs 32 frames) depends on the depth limit; the writer flips it between 1 and 32 continuously.
        string deepTuple = NestedTuple(12);
        ABISchema schema = CompileTuple(deepTuple);
        byte[] doc = []; // nested empty-ish static tuples occupy zero bytes
        var shallow = new ABIDecodeLimits(1 << 20, 3, 4096, 16384, 100_000);
        var deep = new ABIDecodeLimits(1 << 20, 32, 4096, 16384, 100_000);
        sharedLimits = deep;
        using var stop = new CancellationTokenSource();
        var flipper = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                sharedLimits = shallow;
                sharedLimits = deep;
            }
        });

        try
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 400)
            {
                bool ok = ABIData.TryDecodeComplete(doc, schema, in sharedLimits, out var data, out var status);
                Assert.IsTrue(ok ? data.IsValid && status == ABIStatus.Success : status == ABIStatus.DepthLimitExceeded, status.ToString());
            }
        }
        finally
        {
            stop.Cancel();
            flipper.Wait();
        }
    }

    private static string NestedTuple(int depth) => depth == 0 ? "()" : "(" + NestedTuple(depth - 1) + ")";
}
