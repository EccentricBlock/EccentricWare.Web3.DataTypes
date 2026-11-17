using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// The reader paths allocate zero managed bytes after warm-up (spec 1.1 item 3, 13.2). Each measured region is exactly
/// the calls under test: inputs, limits, destination buffers and stack-backed workspaces are created beforehand, and
/// results are folded into a local so no assertion or formatting runs inside the region. Runs serially because
/// allocation counters are per thread and a parallel test must not distort the warm-up.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ReaderAllocationTests
{
    private const int Iterations = 2_000;
    private const int Trials = 5;

    // A nine-field transaction-like list: nonce, gas price, gas limit, to (20 bytes), value, data (a long string), v, r, s.
    private static readonly byte[] TransactionLike = BuildTransactionLike();

    private static byte[] BuildTransactionLike()
    {
        var fields = new[]
        {
            RlpNode.FromUInt64(9),
            RlpNode.FromUInt64(20_000_000_000),
            RlpNode.FromUInt64(21_000),
            RlpNode.Bytes(Repeat(0x35, 20)),
            RlpNode.FromUInt64(1_000_000_000_000_000_000),
            RlpNode.Bytes(Repeat(0xab, 70)),
            RlpNode.FromUInt64(37),
            RlpNode.Bytes(Repeat(0x11, 32)),
            RlpNode.Bytes(Repeat(0x22, 32)),
        };
        return RlpReference.Encode(RlpNode.ListOf(fields));
    }

    private static void AssertNoAllocation(long smallestTrial, string what) =>
        Assert.AreEqual(0L, smallestTrial, $"{what} allocated {smallestTrial} bytes over {Iterations} calls (smallest of {Trials} trials)");

    [TestMethod]
    public void TryDecodeComplete_ValidDocument_AllocatesNothing()
    {
        var input = TransactionLike;
        var limits = RLPDecodeLimits.Default;
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            _ = RLPData.TryDecodeComplete(input, in limits, out var warm, out _);
            checksum += warm.EncodedLength;
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                _ = RLPData.TryDecodeComplete(input, in limits, out var document, out _);
                checksum += document.EncodedLength;
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "TryDecodeComplete");
        Assert.AreEqual((long)input.Length * Iterations * (Trials + 1), checksum);
    }

    [TestMethod]
    public void TryDecodeComplete_DeepAndRejectedInputs_AllocateNothing()
    {
        var deep = NestedLists(64, out _);
        var tooDeep = NestedLists(65, out _);
        byte[] malformed = [0xc1, 0x82];
        var limits = new RLPDecodeLimits(OneMebibyte, 64, 8192);
        var invalid = default(RLPDecodeLimits);
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += (int)RLPData.TryDecodeComplete(deep, in limits, out _, out _);
            checksum += (int)RLPData.TryDecodeComplete(tooDeep, in limits, out _, out _);
            checksum += (int)RLPData.TryDecodeComplete(malformed, in limits, out _, out _);
            checksum += (int)RLPData.TryDecodeComplete(malformed, in invalid, out _, out _);
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                checksum += (int)RLPData.TryDecodeComplete(deep, in limits, out _, out _);
                checksum += (int)RLPData.TryDecodeComplete(tooDeep, in limits, out _, out _);
                checksum += (int)RLPData.TryDecodeComplete(malformed, in limits, out _, out _);
                checksum += (int)RLPData.TryDecodeComplete(malformed, in invalid, out _, out _);
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "TryDecodeComplete on deep and rejected inputs");
        Assert.AreNotEqual(0L, checksum);
    }

    [TestMethod]
    public void ChildEnumeration_AllocatesNothing()
    {
        var input = TransactionLike;
        var limits = RLPDecodeLimits.Default;
        Assert.AreEqual(RLPStatus.Success, RLPData.TryDecodeComplete(input, in limits, out var root, out _));
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += EnumerateOnce(root);
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                _ = root.TryGetChildren(out var children);
                while (children.MoveNext())
                {
                    checksum += children.Current.EncodedLength;
                }
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "TryGetChildren/MoveNext/Current");
        Assert.AreNotEqual(0L, checksum);

        static int EnumerateOnce(RLPData node)
        {
            var total = 0;
            _ = node.TryGetChildren(out var children);
            while (children.MoveNext())
            {
                total += children.Current.EncodedLength;
            }

            return total;
        }
    }

    [TestMethod]
    public void IndexIntoCallerStorage_AllocatesNothing()
    {
        var input = TransactionLike;
        var limits = RLPDecodeLimits.Default;
        Assert.AreEqual(RLPStatus.Success, RLPData.TryDecodeComplete(input, in limits, out var root, out _));
        Span<RLPRange> workspace = stackalloc RLPRange[16];
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            _ = root.TryIndexDirectChildren(workspace, out var warm, out var warmCount);
            _ = warm.TryGetAt(warmCount - 1, out var warmChild);
            checksum += warmChild.EncodedLength;
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                _ = root.TryIndexDirectChildren(workspace, out var index, out var count);
                for (var ordinal = 0; ordinal < count; ordinal++)
                {
                    _ = index.TryGetAt(ordinal, out var child);
                    checksum += child.EncodedLength;
                }
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "TryIndexDirectChildren/TryGetAt");
        Assert.AreNotEqual(0L, checksum);
    }

    [TestMethod]
    public void IndexCapacityFailure_AllocatesNothing()
    {
        var input = TransactionLike;
        var limits = RLPDecodeLimits.Default;
        Assert.AreEqual(RLPStatus.Success, RLPData.TryDecodeComplete(input, in limits, out var root, out _));
        Span<RLPRange> tooSmall = stackalloc RLPRange[2];
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += (int)root.TryIndexDirectChildren(tooSmall, out _, out var warm) + warm;
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                checksum += (int)root.TryIndexDirectChildren(tooSmall, out _, out var required) + required;
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "TryIndexDirectChildren capacity failure");
        Assert.AreNotEqual(0L, checksum);
    }

    [TestMethod]
    public void TryGetUInt64_AllocatesNothing()
    {
        var integers = new[] { H("80"), H("01"), H("81 80"), H("88 ff*8"), H("00"), H("89 01 02 03 04 05 06 07 08 09"), H("c0") };
        var limits = RLPDecodeLimits.Default;
        var documents = new List<byte[]>(integers);
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            foreach (var bytes in documents)
            {
                _ = RLPData.TryDecodeComplete(bytes, in limits, out var warm, out _);
                _ = warm.TryGetUInt64(out var warmValue);
                checksum += (long)warmValue;
            }
        }

        // Decode once outside the region; only the accessor is measured.
        _ = RLPData.TryDecodeComplete(integers[3], in limits, out var maxValue, out _);
        _ = RLPData.TryDecodeComplete(integers[4], in limits, out var rawZero, out _);
        _ = RLPData.TryDecodeComplete(integers[5], in limits, out var tooWide, out _);
        _ = RLPData.TryDecodeComplete(integers[6], in limits, out var list, out _);
        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                checksum += (int)maxValue.TryGetUInt64(out var value);
                checksum += (long)value;
                checksum += (int)rawZero.TryGetUInt64(out _);
                checksum += (int)tooWide.TryGetUInt64(out _);
                checksum += (int)list.TryGetUInt64(out _);
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "TryGetUInt64");
        Assert.AreNotEqual(0L, checksum);
    }

    [TestMethod]
    public void TryCopyEncodedTo_AllocatesNothing()
    {
        var input = TransactionLike;
        var limits = RLPDecodeLimits.Default;
        Assert.AreEqual(RLPStatus.Success, RLPData.TryDecodeComplete(input, in limits, out var root, out _));
        var destination = new byte[input.Length];
        Span<byte> tooSmall = stackalloc byte[4];
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            _ = root.TryCopyEncodedTo(destination, out var warm);
            checksum += warm;
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                _ = root.TryCopyEncodedTo(destination, out var written);
                checksum += written;
                checksum += (int)root.TryCopyEncodedTo(tooSmall, out _);
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "TryCopyEncodedTo");
        Assert.AreNotEqual(0L, checksum);
        CollectionAssert.AreEqual(input, destination);
    }
}
