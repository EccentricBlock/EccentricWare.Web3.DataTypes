using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// The validator keeps its enclosing-list ends in a fixed frame chosen from <see cref="RLPDecodeLimits.MaxListDepth"/>
/// (buckets of 8, 32 and 64 slots). Every configured depth from 1 to the hard cap must accept a document nested exactly to
/// that depth and reject one level more with <see cref="RLPStatus.TooDeep"/>, so a bucket cutoff that is one too small
/// shows as a failure here instead of an out-of-range access.
/// </summary>
[TestClass]
public sealed class ValidatorFrameBucketTests
{
    [TestMethod]
    public void EveryConfiguredDepth_AcceptsTheExactDepth_AndRejectsOneMore()
    {
        for (var limit = 1; limit <= RLPDecodeLimits.HardMaxListDepth; limit++)
        {
            var within = NestedLists(limit, out _);
            var over = NestedLists(limit + 1, out var overOffsets);
            var limits = Limits(maxDepth: limit);

            Assert.AreEqual(RLPStatus.Success, Decode(within, limits, out var accepted, out var acceptedError), $"limit {limit}: {acceptedError}");
            Assert.IsTrue(accepted.IsValid, $"limit {limit}");
            Assert.AreEqual(within.Length, accepted.EncodedLength, $"limit {limit}");

            Assert.AreEqual(RLPStatus.TooDeep, Decode(over, limits, out var rejected, out var error), $"limit {limit}");
            Assert.AreEqual(overOffsets[limit], error.Offset, $"limit {limit}: the first list over the limit");
            Assert.IsFalse(rejected.IsValid, $"limit {limit}");
        }
    }

    [TestMethod]
    public void EveryConfiguredDepth_WithNonEmptyInnermostContent_AcceptsTheExactDepth()
    {
        // A literal inside the innermost list makes every level non-empty, so each level pushes a frame slot.
        for (var limit = 1; limit <= RLPDecodeLimits.HardMaxListDepth; limit++)
        {
            byte[] node = [0xc1, 0x01];
            for (var level = 1; level < limit; level++)
            {
                node = RlpReaderTestSupport.WrapInList(node);
            }

            Assert.AreEqual(RLPStatus.Success, Decode(node, Limits(maxDepth: limit), out var document, out var error), $"limit {limit}: {error}");
            Assert.IsTrue(document.IsValid, $"limit {limit}");

            var over = RlpReaderTestSupport.WrapInList(node);
            Assert.AreEqual(RLPStatus.TooDeep, Decode(over, Limits(maxDepth: limit), out var rejected, out _), $"limit {limit}");
            Assert.IsFalse(rejected.IsValid, $"limit {limit}");
        }
    }
}
