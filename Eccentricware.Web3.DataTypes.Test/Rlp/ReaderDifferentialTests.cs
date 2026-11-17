using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Differential tests of the strict reader against the independent recursive oracle (<see cref="RlpReference"/>): the
/// vector tables, seeded random trees, seeded mutation fuzz and random byte arrays. Status is always compared; offsets
/// only where the oracle marks them normative.
/// </summary>
[TestClass]
public sealed class ReaderDifferentialTests
{
    private const int RandomTreeCount = 20_000;
    private const int MutationCount = 50_000;
    private const int RandomArrayCount = 30_000;

    // ------------------------------------------------------------------ vector tables

    [TestMethod]
    [DynamicData(nameof(RlpVectors.CanonicalRows), typeof(RlpVectors))]
    public void CanonicalVectors_AgreeWithTheOracle_AndWalkToTheSameTree(CanonicalVector vector)
    {
        var expected = AssertDecodeMatchesOracle(vector.Encoded, Limits(maxItems: 9000), vector.Name, strictOffsets: true);

        Assert.IsTrue(expected.IsSuccess, vector.Name);
        Assert.AreEqual(vector.Tree, expected.Root, vector.Name);
        AssertTreeMatches(vector.Encoded, DecodeOk(vector.Encoded, Limits(maxItems: 9000)), expected.Root!, vector.Name);
    }

    [TestMethod]
    [DynamicData(nameof(RlpVectors.KnownEthereumRows), typeof(RlpVectors))]
    public void KnownEthereumVectors_AgreeWithTheOracle(KnownEthereumVector vector)
    {
        var expected = AssertDecodeMatchesOracle(vector.Encoded, Limits(), vector.Name, strictOffsets: true);

        Assert.AreEqual(vector.Tree, expected.Root, vector.Name);
        AssertTreeMatches(vector.Encoded, DecodeOk(vector.Encoded), vector.Tree, vector.Name);
    }

    [TestMethod]
    [DynamicData(nameof(RlpVectors.InvalidHandStatedRows), typeof(RlpVectors))]
    public void InvalidHandStatedVectors_AgreeWithTheOracle(DecodeVector vector) => AssertAgainstOracle(vector);

    [TestMethod]
    [DynamicData(nameof(RlpVectors.InvalidNestedDerivedRows), typeof(RlpVectors))]
    public void InvalidNestedDerivedVectors_AgreeWithTheOracle(DecodeVector vector) => AssertAgainstOracle(vector);

    [TestMethod]
    [DynamicData(nameof(RlpVectors.LimitVectorsRows), typeof(RlpVectors))]
    public void LimitVectors_AgreeWithTheOracle(DecodeVector vector) => AssertAgainstOracle(vector);

    [TestMethod]
    [DynamicData(nameof(RlpVectors.AmbiguousPrecedenceRows), typeof(RlpVectors))]
    public void AmbiguousPrecedenceVectors_AgreeWithTheOracle(DecodeVector vector) => AssertAgainstOracle(vector);

    [TestMethod]
    public void SingleByteInputsAndListWithOneByteChild_AgreeWithTheOracle()
    {
        foreach (var vector in RlpVectors.SingleByteInputs.Concat(RlpVectors.ListWithOneByteChild))
        {
            AssertAgainstOracle(vector);
        }
    }

    [TestMethod]
    [DynamicData(nameof(RlpVectors.IntegerRows), typeof(RlpVectors))]
    public void IntegerVectors_AgreeWithTheOracleInterpretation(IntegerVector vector)
    {
        var expected = RlpReference.Decode(vector.Encoded, OneMebibyte, 32, 8192);
        Assert.IsTrue(expected.IsSuccess, vector.Name);

        _ = RlpReference.TryInterpretAsUInt64(expected.Root!, out var oracleValue, out var oracleStatus);
        var status = DecodeOk(vector.Encoded).TryGetUInt64(out var value);

        Assert.AreEqual(oracleStatus, status, vector.Name);
        Assert.AreEqual(oracleValue, value, vector.Name);
        Assert.AreEqual(vector.Status, status, vector.Name);
    }

    private static void AssertAgainstOracle(DecodeVector vector)
    {
        var expected = AssertDecodeMatchesOracle(vector.Input, vector.Limits, vector.Name, strictOffsets: false);

        // The table and the oracle describe the same expectation.
        Assert.AreEqual(vector.Status, expected.Status, vector.Name);
        if (vector.Status == RLPStatus.Success)
        {
            _ = Decode(vector.Input, vector.Limits, out var document, out _);
            AssertTreeMatches(vector.Input, document, expected.Root!, vector.Name);
        }
    }

    // ------------------------------------------------------------------ random canonical trees

    [TestMethod]
    public void RandomCanonicalTrees_DecodeAndRoundTripByteIdentically()
    {
        var rng = new Random(20_260_716);
        for (var seed = 0; seed < RandomTreeCount; seed++)
        {
            var tree = RlpReference.GenerateRandomTree(rng, maxDepth: rng.Next(0, 7), maxItems: rng.Next(1, 70));
            var encoded = RlpReference.Encode(tree);
            var items = RlpReference.CountItems(tree);
            var depth = RlpReference.ListDepth(tree);
            var limits = Limits(maxBytes: Math.Max(encoded.Length, 1), maxDepth: Math.Max(depth, 1), maxItems: items);
            var context = $"seed {seed}";

            // The exact budget accepts; one less item or one less depth level rejects with the right status.
            Assert.AreEqual(RLPStatus.Success, Decode(encoded, limits, out var document, out var error), $"{context}: {error}");
            AssertTreeMatches(encoded, document, tree, context);

            if (items > 1)
            {
                Assert.AreEqual(RLPStatus.TooManyItems, Decode(encoded, limits with { MaxItems = items - 1 }, out _, out _), context);
            }

            if (depth > 1)
            {
                Assert.AreEqual(RLPStatus.TooDeep, Decode(encoded, limits with { MaxListDepth = depth - 1 }, out _, out _), context);
            }

            if (encoded.Length > 1)
            {
                Assert.AreEqual(RLPStatus.TooLarge, Decode(encoded, limits with { MaxEncodedBytes = encoded.Length - 1 }, out _, out _), context);
            }
        }
    }

    // ------------------------------------------------------------------ mutation fuzz

    [TestMethod]
    public void MutationFuzz_AcceptRejectAndStatusMatchTheOracle_WithoutExceptions()
    {
        var rng = new Random(0x524c50);
        var accepted = 0;
        var statuses = new HashSet<RLPStatus>();
        for (var i = 0; i < MutationCount; i++)
        {
            var tree = RlpReference.GenerateRandomTree(rng, maxDepth: rng.Next(0, 5), maxItems: rng.Next(1, 40), maxStringLength: 120);
            var mutated = RlpReference.MutateRandomly(rng, RlpReference.Encode(tree));

            // Half of the cases run under small random limits so the budget checks interleave with structural errors.
            var limits = rng.Next(2) == 0
                ? Limits()
                : Limits(maxBytes: rng.Next(1, 400), maxDepth: rng.Next(1, 7), maxItems: rng.Next(1, 30));
            var context = $"mutation {i}: {RlpReference.ToHex(mutated, true)} limits {limits}";

            var expected = AssertDecodeMatchesOracle(mutated, limits, context, strictOffsets: false);

            statuses.Add(expected.Status);
            if (expected.IsSuccess)
            {
                accepted++;
                _ = Decode(mutated, limits, out var document, out _);
                AssertTreeMatches(mutated, document, expected.Root!, context);
            }
        }

        // The corpus must exercise both outcomes and a spread of failure classes, or the comparison proves little.
        Assert.IsGreaterThan(1000, accepted, "accepted mutations");
        Assert.IsGreaterThanOrEqualTo(7, statuses.Count, $"distinct statuses: {string.Join(",", statuses)}");
    }

    // ------------------------------------------------------------------ random byte arrays

    [TestMethod]
    public void RandomByteArrays_AcceptRejectAndStatusMatchTheOracle_WithoutExceptions()
    {
        byte[] interesting = [0x00, 0x01, 0x37, 0x38, 0x7f, 0x80, 0x81, 0xb7, 0xb8, 0xb9, 0xbf, 0xc0, 0xc1, 0xc2, 0xf7, 0xf8, 0xf9, 0xff];
        var rng = new Random(0x4259544);
        var accepted = 0;
        var statuses = new HashSet<RLPStatus>();
        for (var i = 0; i < RandomArrayCount; i++)
        {
            var bytes = new byte[rng.Next(0, 33)];
            var mode = rng.Next(3);
            for (var b = 0; b < bytes.Length; b++)
            {
                bytes[b] = mode == 0
                    ? (byte)rng.Next(256)
                    : mode == 1 ? interesting[rng.Next(interesting.Length)] : (byte)(rng.Next(4) == 0 ? rng.Next(256) : 0xc0 + rng.Next(0, 3));
            }

            var context = $"array {i}: {RlpReference.ToHex(bytes, true)}";
            var expected = AssertDecodeMatchesOracle(bytes, Limits(maxDepth: rng.Next(1, 10), maxItems: rng.Next(1, 40)), context, strictOffsets: false);

            statuses.Add(expected.Status);
            if (expected.IsSuccess)
            {
                accepted++;
                _ = Decode(bytes, Limits(maxDepth: 64, maxItems: 1000), out var document, out _);
                AssertTreeMatches(bytes, document, expected.Root!, context);
            }
        }

        Assert.IsGreaterThan(200, accepted, "accepted arrays");
        Assert.IsGreaterThanOrEqualTo(6, statuses.Count, $"distinct statuses: {string.Join(",", statuses)}");
    }

    // ------------------------------------------------------------------ non-normative offsets (documented readings)

    [TestMethod]
    public void OffsetsFollowTheOracleReadingEverywhere_IncludingNonNormativeOnes()
    {
        // Pins binding design point 3: the oracle's documented offset choices are also the implementation's.
        var rng = new Random(77);
        for (var i = 0; i < 20_000; i++)
        {
            var tree = RlpReference.GenerateRandomTree(rng, maxDepth: rng.Next(0, 5), maxItems: rng.Next(1, 40), maxStringLength: 120);
            var mutated = RlpReference.MutateRandomly(rng, RlpReference.Encode(tree));
            var limits = Limits(maxBytes: rng.Next(1, 400), maxDepth: rng.Next(1, 7), maxItems: rng.Next(1, 30));

            AssertDecodeMatchesOracle(mutated, limits, $"offset case {i}: {RlpReference.ToHex(mutated, true)} {limits}", strictOffsets: true);
        }
    }
}
