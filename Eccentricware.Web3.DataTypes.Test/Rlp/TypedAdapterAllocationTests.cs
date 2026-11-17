using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.TypedAdapterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// The typed reader adapters allocate zero managed bytes after warm-up, on success and on every failure status. The
/// measured region is exactly the calls under test: inputs are decoded beforehand and results are folded into a local.
/// Runs serially because allocation counters are per thread.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TypedAdapterAllocationTests
{
    private const int Iterations = 2_000;
    private const int Trials = 5;

    private static void AssertNoAllocation(long smallestTrial, string what) =>
        Assert.AreEqual(0L, smallestTrial, $"{what} allocated {smallestTrial} bytes over {Iterations} calls (smallest of {Trials} trials)");

    [TestMethod]
    public void UInt256Reader_AllocatesNothing_OnSuccessAndEveryFailure()
    {
        var zero = StringItem([]);
        var small = StringItem([0x7f]);
        var full = StringItem(Pattern(32, 0xff));
        var leadingZero = StringItem([0x00, 0x01]);
        var tooWide = StringItem(Pattern(33, 0x01));
        var list = HexItem("c0");
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += Run(zero, small, full, leadingZero, tooWide, list);
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                checksum += Run(zero, small, full, leadingZero, tooWide, list);
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "TryGetUInt256");
        Assert.AreNotEqual(0L, checksum);

        static long Run(RLPData a, RLPData b, RLPData c, RLPData d, RLPData e, RLPData f)
        {
            long sum = (int)a.TryGetUInt256(out var value) + (long)value.U0;
            sum += (int)b.TryGetUInt256(out value) + (long)value.U0;
            sum += (int)c.TryGetUInt256(out value) + (long)value.U3;
            sum += (int)d.TryGetUInt256(out value) + (long)value.U0;
            sum += (int)e.TryGetUInt256(out value) + (long)value.U0;
            sum += (int)f.TryGetUInt256(out value) + (long)value.U0;
            sum += (int)default(RLPData).TryGetUInt256(out value) + (long)value.U0;
            return sum;
        }
    }

    [TestMethod]
    public void FixedWidthReaders_AllocateNothing_OnSuccessAndEveryFailure()
    {
        var hash = StringItem(Pattern(32, 0xee));
        var address = StringItem(Pattern(20, 0xa1));
        var empty = StringItem([]);
        var calldata = StringItem(Pattern(68, 0xa9));
        var shortData = StringItem(Pattern(3, 0xa9));
        var wrongWidth = StringItem(Pattern(21, 0x01));
        var list = HexItem("c0");
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += Run(hash, address, empty, calldata, shortData, wrongWidth, list);
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                checksum += Run(hash, address, empty, calldata, shortData, wrongWidth, list);
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "TryGetHash32/TryGetAddress/TryGetOptionalAddress/TryGetFunctionSelector");
        Assert.AreNotEqual(0L, checksum);

        static long Run(RLPData hash, RLPData address, RLPData empty, RLPData calldata, RLPData shortData, RLPData wrongWidth, RLPData list)
        {
            long sum = (int)hash.TryGetHash32(out var h) + (h.IsZero ? 0 : 1);
            sum += (int)wrongWidth.TryGetHash32(out h) + (h.IsZero ? 0 : 1);
            sum += (int)list.TryGetHash32(out h) + (h.IsZero ? 0 : 1);
            sum += (int)address.TryGetAddress(out var a) + (a.IsZero ? 0 : 1);
            sum += (int)wrongWidth.TryGetAddress(out a) + (a.IsZero ? 0 : 1);
            sum += (int)address.TryGetOptionalAddress(out a, out var isEmpty) + (isEmpty ? 1 : 0);
            sum += (int)empty.TryGetOptionalAddress(out a, out isEmpty) + (isEmpty ? 1 : 0);
            sum += (int)wrongWidth.TryGetOptionalAddress(out a, out isEmpty) + (isEmpty ? 1 : 0);
            sum += (int)calldata.TryGetFunctionSelector(out var selector) + (int)selector.Value;
            sum += (int)shortData.TryGetFunctionSelector(out selector) + (int)selector.Value;
            sum += (int)list.TryGetFunctionSelector(out selector) + (int)selector.Value;
            return sum;
        }
    }

    [TestMethod]
    public void SignatureReaders_AllocateNothing_OnSuccessAndEveryFailure_IncludingTheNullableChainId()
    {
        var legacyV = StringItem(Minimal(EvmSignatureOracle.LegacyV(1, 98866)));   // 4 bytes, chain id 98866
        var legacyVWide = StringItem(Minimal(EvmSignatureOracle.LegacyV(0, ulong.MaxValue))); // 9 bytes
        var unprotectedV = StringItem([0x1b]);
        var legacyVOverflow = StringItem(Minimal((System.Numerics.BigInteger.One << 65) + 35));
        var legacyVTooWide = StringItem(Pattern(10, 1));
        var invalidV = StringItem([0x1d]);
        var yParity = StringItem([0x01]);
        var yParityInvalid = StringItem([0x02]);
        var r = StringItem(Pattern(32, 0x7f));
        var s = StringItem(Pattern(31, 0x7f));
        var badS = StringItem([0x00, 0x01]);
        var list = HexItem("c0");
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += Run(legacyV, legacyVWide, unprotectedV, legacyVOverflow, legacyVTooWide, invalidV, yParity, yParityInvalid, r, s, badS, list);
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                checksum += Run(legacyV, legacyVWide, unprotectedV, legacyVOverflow, legacyVTooWide, invalidV, yParity, yParityInvalid, r, s, badS, list);
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "TryGetEvmLegacySignature/TryGetEvmYParitySignature");
        Assert.AreNotEqual(0L, checksum);

        static long Run(RLPData legacyV, RLPData legacyVWide, RLPData unprotectedV, RLPData legacyVOverflow, RLPData legacyVTooWide, RLPData invalidV,
            RLPData yParity, RLPData yParityInvalid, RLPData r, RLPData s, RLPData badS, RLPData list)
        {
            long sum = (int)RLPTypedExtensions.TryGetEvmLegacySignature(legacyV, r, s, out var signature, out var chainId) + signature.V + (long)(chainId ?? 0);
            sum += (int)RLPTypedExtensions.TryGetEvmLegacySignature(legacyVWide, r, s, out signature, out chainId) + signature.V + (long)(chainId ?? 0);
            sum += (int)RLPTypedExtensions.TryGetEvmLegacySignature(unprotectedV, r, s, out signature, out chainId) + signature.V + (chainId.HasValue ? 1 : 0);
            sum += (int)RLPTypedExtensions.TryGetEvmLegacySignature(legacyVOverflow, r, s, out signature, out chainId) + signature.V + (long)(chainId ?? 0);
            sum += (int)RLPTypedExtensions.TryGetEvmLegacySignature(legacyVTooWide, r, s, out signature, out chainId) + signature.V + (long)(chainId ?? 0);
            sum += (int)RLPTypedExtensions.TryGetEvmLegacySignature(invalidV, r, s, out signature, out chainId) + signature.V + (long)(chainId ?? 0);
            sum += (int)RLPTypedExtensions.TryGetEvmLegacySignature(legacyV, r, badS, out signature, out chainId) + signature.V + (long)(chainId ?? 0);
            sum += (int)RLPTypedExtensions.TryGetEvmLegacySignature(list, r, s, out signature, out chainId) + signature.V + (long)(chainId ?? 0);
            sum += (int)RLPTypedExtensions.TryGetEvmLegacySignature(default, r, s, out signature, out chainId) + signature.V + (long)(chainId ?? 0);
            sum += (int)RLPTypedExtensions.TryGetEvmYParitySignature(yParity, r, s, out signature) + signature.V;
            sum += (int)RLPTypedExtensions.TryGetEvmYParitySignature(yParityInvalid, r, s, out signature) + signature.V;
            sum += (int)RLPTypedExtensions.TryGetEvmYParitySignature(yParity, r, badS, out signature) + signature.V;
            sum += (int)RLPTypedExtensions.TryGetEvmYParitySignature(list, r, s, out signature) + signature.V;
            return sum;
        }
    }
}
