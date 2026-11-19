using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Spec 11.3: zero managed allocation per decode, locate and accessor call, on success AND failure, after warm-up. Inputs, schemas, limits and
/// paths are created before the measured region; each batch folds results into a checksum so nothing is optimised away and no assertion runs
/// inside the region. Serial: allocation counters are per thread and a parallel test must not disturb the warm-up.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ABIDecodeAllocationTests
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
    private static readonly ABIFunctionSchema TransferFunction = Function("transfer(address,uint256)");
    private static readonly ABIFunctionSchema MulticallFunction = Function("multicall(bytes[])");
    private static readonly byte[] TransferCall = Hex(ABIFixtures.TransferVitalik1Eth);
    private static readonly byte[] MulticallCall = Hex(ABIFixtures.MulticallTwoTransfers388);
    private static readonly byte[] MixedDocument = Hex(ABIFixtures.NestedMixedTuples);
    private static readonly ABIDecodeLimits Defaults = ABIDecodeLimits.Default;

    // Failure inputs: one per failure class, all against the transfer / mixed / bytes schemas.
    private static readonly ABISchema BytesSchema = CompileTuple("(bytes)");
    private static readonly byte[][] FailingDocuments =
    [
        [],                                                              // Truncated
        Cat(Word(1), Word(2), [0x00]),                                   // trailing data (against the transfer schema)
        Cat(Filled(0xff), Word(2)),                                      // InvalidScalar (address high bytes)
    ];

    private static readonly byte[][] FailingBytesDocuments =
    [
        Cat(Word(0x40), Word(0)),                                        // NonCanonicalLayout
        Cat(Word(0x01), Word(0)),                                        // InvalidOffset
        Cat(Word(0x20), Filled(0xff)),                                   // Truncated (length 2^256 - 1)
        Cat(Word(0x20), Word(1), Cat([0x01], Filled(0x01)[..31])),       // InvalidPadding
    ];

    [TestMethod]
    public void TryDecodeComplete_Success_AllocatesNothing()
    {
        AssertNoAllocation("static decode", () =>
        {
            long sum = 0;
            byte[] input = TransferCall;
            var limits = Defaults;
            for (int i = 0; i < Iterations; i++)
            {
                _ = ABIData.TryDecodeComplete(input.AsSpan(4), TransferSchema, in limits, out var data, out _);
                sum += data.EncodedBytes.Length;
            }

            return sum;
        });

        AssertNoAllocation("dynamic nested decode", () =>
        {
            long sum = 0;
            var limits = Defaults;
            for (int i = 0; i < Iterations; i++)
            {
                _ = ABIData.TryDecodeComplete(MixedDocument, MixedSchema, in limits, out var data, out _, out var error);
                sum += data.EncodedBytes.Length + (int)error.Status;
            }

            return sum;
        });
    }

    [TestMethod]
    public void TryDecodeComplete_Failures_AllocateNothing()
    {
        AssertNoAllocation("static schema failures", () =>
        {
            long sum = 0;
            var limits = Defaults;
            for (int i = 0; i < Iterations; i++)
            {
                foreach (byte[] input in FailingDocuments)
                {
                    _ = ABIData.TryDecodeComplete(input, TransferSchema, in limits, out _, out var status, out var error);
                    sum += (int)status + error.DocumentOffset;
                }
            }

            return sum;
        });

        AssertNoAllocation("bytes schema failures", () =>
        {
            long sum = 0;
            var limits = Defaults;
            for (int i = 0; i < Iterations; i++)
            {
                foreach (byte[] input in FailingBytesDocuments)
                {
                    _ = ABIData.TryDecodeComplete(input, BytesSchema, in limits, out _, out var status);
                    sum += (int)status;
                }
            }

            return sum;
        });

        AssertNoAllocation("limit failures and invalid arguments", () =>
        {
            long sum = 0;
            var tooSmall = new ABIDecodeLimits(32, 16, 4096, 16384, 100_000);
            var shallow = new ABIDecodeLimits(262_144, 2, 4096, 16384, 100_000);
            var invalid = default(ABIDecodeLimits);
            var limits = Defaults;
            for (int i = 0; i < Iterations; i++)
            {
                _ = ABIData.TryDecodeComplete(MixedDocument, MixedSchema, in tooSmall, out _, out var s1);
                _ = ABIData.TryDecodeComplete(MixedDocument, MixedSchema, in shallow, out _, out var s2);
                _ = ABIData.TryDecodeComplete(MixedDocument, MixedSchema, in invalid, out _, out var s3);
                _ = ABIData.TryDecodeComplete(MixedDocument, null!, in limits, out _, out var s4);
                sum += (int)s1 + (int)s2 + (int)s3 + (int)s4;
            }

            return sum;
        });
    }

    [TestMethod]
    public void AttackerSizedCounts_AllocateNothing_WhateverTheirValue()
    {
        // Counts and lengths of 2^31, 2^32, 2^255 and 4096 must not allocate anything proportional to them (spec 11.3).
        ABISchema array = CompileTuple("(uint256[])");
        ABISchema empties = CompileTuple("(()[])");
        byte[][] documents = [Cat(Word(0x20), Filled(0xff)), Cat(Word(0x20), Word(0x8000_0000)), Cat(Word(0x20), Word(4096)), Cat(Word(0x20), Cat([0x80], new byte[31]))];
        byte[] zeroWidth = Cat(Word(0x20), Word(4096));
        AssertNoAllocation("huge counts", () =>
        {
            long sum = 0;
            var limits = Defaults;
            var generous = new ABIDecodeLimits(262_144, 16, int.MaxValue, 1_000_000, 10_000_000);
            for (int i = 0; i < 100; i++)
            {
                foreach (byte[] input in documents)
                {
                    _ = ABIData.TryDecodeComplete(input, array, in limits, out _, out var a);
                    _ = ABIData.TryDecodeComplete(input, array, in generous, out _, out var b);
                    sum += (int)a + (int)b;
                }

                _ = ABIData.TryDecodeComplete(zeroWidth, empties, in limits, out var data, out var c);
                sum += (int)c + data.EncodedBytes.Length;
            }

            return sum;
        });
    }

    [TestMethod]
    public void TryDecode_OfACall_AllocatesNothing_OnSuccessAndFailure()
    {
        byte[] wrongSelector = (byte[])TransferCall.Clone();
        wrongSelector[0] ^= 1;
        byte[] truncated = TransferCall[..40];
        AssertNoAllocation("call decode", () =>
        {
            long sum = 0;
            var limits = Defaults;
            for (int i = 0; i < Iterations; i++)
            {
                _ = ABICallData.TryDecode(TransferCall, TransferFunction, in limits, out var ok, out _);
                _ = ABICallData.TryDecode(MulticallCall, MulticallFunction, in limits, out var multi, out _);
                _ = ABICallData.TryDecode(wrongSelector, TransferFunction, in limits, out _, out var s1);
                _ = ABICallData.TryDecode(truncated, TransferFunction, in limits, out _, out var s2);
                _ = ABICallData.TryDecode([], TransferFunction, in limits, out _, out var s3);
                sum += ok.EncodedCall.Length + multi.Arguments.EncodedBytes.Length + (int)s1 + (int)s2 + (int)s3;
            }

            return sum;
        });
    }

    [TestMethod]
    public void LocateAndEveryAccessor_AllocateNothing_OnSuccessAndFailure()
    {
        var limits = Defaults;
        int[] address = [0];
        int[] amount = [1];
        int[] root = [];
        int[] missing = [5];
        int[] throughPrimitive = [0, 0];
        int[] negative = [-1];

        AssertNoAllocation("transfer accessors", () =>
        {
            long sum = 0;
            var l = Defaults;
            _ = ABICallData.TryDecode(TransferCall, TransferFunction, in l, out var call, out _);
            ABIData data = call.Arguments;
            for (int i = 0; i < Iterations; i++)
            {
                _ = data.TryLocate(root, out var rootField, out var s0);
                _ = data.TryLocate(amount, out var field, out var s1);
                _ = data.TryGetAddress(address, out var a, out var s2);
                _ = data.TryGetWord(amount, out var w, out var s3);
                _ = data.TryGetUInt64(amount, out ulong v, out var s4);
                _ = data.TryGetUInt256Word(amount, out var w2, out var s5);
                _ = data.TryGetInt64(amount, out long bad, out var s6);                  // TypeMismatch
                _ = data.TryGetBool(address, out bool flag, out var s7);                 // TypeMismatch
                _ = data.TryLocate(missing, out var none, out var s8);                   // InvalidPath
                _ = data.TryLocate(throughPrimitive, out none, out var s9);              // InvalidPath
                _ = data.TryLocate(negative, out none, out var s10);                     // InvalidPath
                _ = call.TryTranslateArgumentRange(field.ValueRange, out var inCall);
                sum += rootField.ValueRange.Length + field.ValueRange.Offset + a.Length + w.Length + (long)v + w2.Length + bad + (flag ? 1 : 0)
                    + (int)s0 + (int)s1 + (int)s2 + (int)s3 + (int)s4 + (int)s5 + (int)s6 + (int)s7 + (int)s8 + (int)s9 + (int)s10 + inCall.Offset;
            }

            return sum;
        });

        int[] firstChild = [0, 0];
        int[] lastChild = [0, 1];
        int[] arrayPath = [0];
        AssertNoAllocation("multicall payload and array accessors", () =>
        {
            long sum = 0;
            var l = Defaults;
            _ = ABICallData.TryDecode(MulticallCall, MulticallFunction, in l, out var call, out _);
            ABIData data = call.Arguments;
            for (int i = 0; i < Iterations; i++)
            {
                _ = data.TryGetBytePayload(firstChild, out var p1, out var r1, out var s1);
                _ = data.TryGetBytePayload(lastChild, out var p2, out var r2, out var s2);
                _ = data.TryGetArrayLength(arrayPath, out int length, out var s3);
                _ = data.TryGetStringUtf8Validated(firstChild, out var text, out _, out var s4); // TypeMismatch: bytes, not string
                sum += p1.Length + p2.Length + r1.Offset + r2.Length + length + text.Length + (int)s1 + (int)s2 + (int)s3 + (int)s4;
            }

            return sum;
        });

        int[] text = [1, 0];
        int[] inner = [1, 1, 1, 1];
        int[] fixedElement = [2, 1];
        AssertNoAllocation("mixed document accessors", () =>
        {
            long sum = 0;
            var l = Defaults;
            _ = ABIData.TryDecodeComplete(MixedDocument, MixedSchema, in l, out var data, out _);
            for (int i = 0; i < Iterations; i++)
            {
                _ = data.TryGetStringUtf8Validated(text, out var utf8, out _, out var s1);
                _ = data.TryGetBytePayload(inner, out var payload, out _, out var s2);
                _ = data.TryGetUInt64(fixedElement, out ulong v, out var s3);
                _ = data.TryLocate(inner, out var field, out var s4);
                sum += utf8.Length + payload.Length + (long)v + field.ValueRange.Length + (int)s1 + (int)s2 + (int)s3 + (int)s4;
            }

            return sum;
        });

        // The scalar accessors that return spans or scales.
        ABISchema scalars = CompileTuple("(bytes4,function,fixed128x18,ufixed128x18,int256,bool)");
        byte[] scalarDoc = Cat(Cat(Hex("deadbeef"), new byte[28]), Cat(Filled(0x22)[..20], Hex("01020304"), new byte[8]), Word(1), Word(2), Filled(0xff), Word(1));
        int[] p0 = [0], p1 = [1], p2 = [2], p3 = [3], p4 = [4], p5 = [5];
        AssertNoAllocation("scalar accessors", () =>
        {
            long sum = 0;
            var l = Defaults;
            _ = ABIData.TryDecodeComplete(scalarDoc, scalars, in l, out var data, out _);
            for (int i = 0; i < Iterations; i++)
            {
                _ = data.TryGetFixedBytes(p0, out var four, out var s1);
                _ = data.TryGetFunction(p1, out var target, out uint selector, out var s2);
                _ = data.TryGetSignedScaledWord(p2, out var fixedWord, out byte scale, out var s3);
                _ = data.TryGetUnsignedScaledWord(p3, out var ufixedWord, out byte uscale, out var s4);
                _ = data.TryGetInt256Word(p4, out var intWord, out var s5);
                _ = data.TryGetBool(p5, out bool flag, out var s6);
                _ = ABIData.TryCompareUnsigned256(fixedWord, ufixedWord, out int c1, out var s7);
                _ = ABIData.TryCompareSigned256(intWord, fixedWord, out int c2, out var s8);
                sum += four.Length + target.Length + selector + fixedWord.Length + scale + ufixedWord.Length + uscale + intWord.Length + (flag ? 1 : 0) + c1 + c2
                    + (int)s1 + (int)s2 + (int)s3 + (int)s4 + (int)s5 + (int)s6 + (int)s7 + (int)s8;
            }

            return sum;
        });

        Assert.IsTrue(limits.IsValid);
    }

    [TestMethod]
    public void DefaultViews_AllocateNothing()
    {
        int[] path = [0];
        AssertNoAllocation("default view accessors", () =>
        {
            long sum = 0;
            ABIData data = default;
            ABICallData call = default;
            for (int i = 0; i < Iterations; i++)
            {
                _ = data.TryLocate(path, out var f, out var s1);
                _ = data.TryGetUInt64(path, out ulong v, out var s2);
                _ = data.TryGetBytePayload(path, out var p, out _, out var s3);
                _ = call.TryTranslateArgumentRange(new ABIRange(0, 0), out var r);
                sum += f.ValueRange.Length + (long)v + p.Length + r.Length + (int)s1 + (int)s2 + (int)s3;
            }

            return sum;
        });
    }
}
