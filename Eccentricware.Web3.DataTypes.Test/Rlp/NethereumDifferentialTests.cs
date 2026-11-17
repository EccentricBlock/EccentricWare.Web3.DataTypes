using System.Numerics;
using EccentricWare.Web3.DataTypes.Rlp;
using Nethereum.RLP;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Differential tests of the strict codec against <c>Nethereum.RLP</c> 7.1.0, an RLP implementation this project does
/// not control (spec 12.4 asks for one; go-ethereum cannot be called from .NET tests). Every other RLP test compares
/// with <see cref="RlpReference"/>, which the same project wrote from the same specification. Four things are compared:
/// acceptance and decoded structure, rejection, encoded bytes, and the recorded probe table of
/// <c>docs/rlp-performance.md</c>. All inputs are fixed or seeded, so every run is identical.
///
/// Divergence register. Across every vector, 6,000+ seeded structural mutations and every 1- and 2-byte input the
/// two decoders disagree on accept/reject in exactly three patterns, all caused by limit policy, none by the wire rules
/// (see <see cref="NethereumDivergenceClassifier.DocumentedPatterns"/> for the reasons):
/// <list type="table">
/// <item><term>LibraryRejectsNethereumAccepts.InvalidArgument</term><description>library limits out of range; Nethereum has no limits.</description></item>
/// <item><term>LibraryRejectsNethereumAccepts.TooDeep</term><description>nesting above the configured depth but at most 64; Nethereum's cap is a fixed 64.</description></item>
/// <item><term>LibraryRejectsNethereumAccepts.TooLarge</term><description>input above MaxEncodedBytes; Nethereum has no size limit.</description></item>
/// </list>
/// No input was found that the library accepts and Nethereum rejects, and none that Nethereum accepts and the library
/// rejects for a non-canonical or malformed encoding. When the limits are matched (depth 64, same item budget, size
/// not binding) the two decoders agreed on every mutation, so the three patterns above only appear for the limit rows
/// of <see cref="RlpVectors.LimitVectors"/>. Where both accept, the trees are equal. (A one-off run over all 16,777,216 three-byte
/// inputs with matched limits, not repeated in the suite because it takes about a minute, also found no divergence.)
/// </summary>
[TestClass]
public sealed class NethereumDifferentialTests
{
    private const int RandomTreeCount = 2_500;
    private const int MutationCount = 6_300;
    private const int TreeSeed = 20260511;
    private const int MutationSeed = 20260512;

    /// <summary>Limits that remove every policy difference: Nethereum's depth cap, its item budget, and a size that never binds.</summary>
    private static RLPDecodeLimits MatchedLimits => new(1 << 24, RLPDecodeLimits.HardMaxListDepth, NethereumRlpAdapter.ItemBudget);

    public TestContext TestContext { get; set; } = null!;

    // ================================================================ 1. acceptance and structure

    [TestMethod]
    [DynamicData(nameof(RlpVectors.CanonicalRows), typeof(RlpVectors))]
    public void CanonicalVectors_BothDecodersAccept_WithIdenticalStructure(CanonicalVector vector) =>
        AssertBothAcceptSameTree(vector.Encoded, vector.Tree, vector.Name);

    [TestMethod]
    [DynamicData(nameof(RlpVectors.KnownEthereumRows), typeof(RlpVectors))]
    public void KnownEthereumVectors_BothDecodersAccept_WithIdenticalStructure(KnownEthereumVector vector) =>
        AssertBothAcceptSameTree(vector.Encoded, vector.Tree, vector.Name);

    [TestMethod]
    public void SeededRandomCanonicalTrees_BothDecodersAccept_WithIdenticalStructure()
    {
        var accepted = 0;
        foreach (var tree in GenerateTrees(TreeSeed, RandomTreeCount))
        {
            var encoded = RlpReference.Encode(tree);
            AssertBothAcceptSameTree(encoded, tree, RlpReference.ToHex(encoded.AsSpan(0, Math.Min(encoded.Length, 24))));
            accepted++;
        }

        Assert.IsGreaterThanOrEqualTo(2_000, accepted);
    }

    private static void AssertBothAcceptSameTree(byte[] encoded, RlpNode expected, string context)
    {
        var limits = RLPDecodeLimits.Default;
        Assert.AreEqual(RLPStatus.Success, RLPData.TryDecodeComplete(encoded, in limits, out var document, out var error), $"{context}: library {error}");
        Assert.IsTrue(
            NethereumRlpAdapter.TryDecode(encoded, NethereumRlpAdapter.ItemBudget, out var nethereumTree, out var failure),
            $"{context}: Nethereum rejected a canonical document ({failure})");

        var libraryTree = NethereumRlpAdapter.ReadLibraryTree(document);
        Assert.AreEqual(expected, libraryTree, $"{context}: library tree differs from the expected tree");
        Assert.AreEqual(expected, nethereumTree, $"{context}: Nethereum tree differs from the expected tree");
        Assert.AreEqual(nethereumTree, libraryTree, $"{context}: the two decoders disagree on structure");
    }

    // ================================================================ 2. rejection

    [TestMethod]
    public void VectorTables_DivergenceSetEqualsTheDocumentedSet()
    {
        var observed = new Dictionary<string, string>();
        var bothReject = 0;
        var bothAccept = 0;
        var vectors = RlpVectors.InvalidHandStated
            .Concat(RlpVectors.InvalidNestedDerived)
            .Concat(RlpVectors.AmbiguousPrecedence)
            .Concat(RlpVectors.SingleByteInputs)
            .Concat(RlpVectors.ListWithOneByteChild)
            .Concat(RlpVectors.LimitVectors)
            .ToArray();

        foreach (var vector in vectors)
        {
            // Nethereum accepts any element budget >= 1; use the vector's own item limit when it is meaningful.
            var nethereumItems = vector.Limits.MaxItems > 0 ? vector.Limits.MaxItems : NethereumRlpAdapter.ItemBudget;
            var pattern = NethereumDivergenceClassifier.Compare(vector.Input, vector.Limits, nethereumItems);
            if (pattern is not null)
            {
                observed.TryAdd(pattern, vector.Name);
            }
            else if (vector.Status == RLPStatus.Success)
            {
                bothAccept++;
            }
            else
            {
                bothReject++;
            }
        }

        var documented = NethereumDivergenceClassifier.DocumentedPatterns.Select(p => p.Key).Order().ToArray();
        CollectionAssert.AreEqual(
            documented,
            observed.Keys.Order().ToArray(),
            "Observed divergence patterns (with an example vector each): " + string.Join("; ", observed.Select(o => $"{o.Key} <- {o.Value}")));

        // The net must have caught something: most negative vectors are rejected by both.
        Assert.IsGreaterThan(vectors.Length / 2, bothReject);
        Assert.IsGreaterThan(vectors.Length / 10, bothAccept);
        TestContext.WriteLine($"vector rows: {vectors.Length}, both reject {bothReject}, both accept {bothAccept}, divergent patterns {observed.Count}");
    }

    [TestMethod]
    public void NegativeVectors_NethereumRejectsEveryNonLimitMalformation()
    {
        // Narrower than the register above: no malformed vector may be accepted by Nethereum unless its status is a
        // limit status. This is what makes "the library is strict by design" independently checkable.
        var limitStatuses = new[] { RLPStatus.InvalidArgument, RLPStatus.TooLarge, RLPStatus.TooDeep, RLPStatus.TooManyItems };
        foreach (var vector in RlpVectors.InvalidHandStated.Concat(RlpVectors.InvalidNestedDerived).Concat(RlpVectors.AmbiguousPrecedence))
        {
            if (limitStatuses.Contains(vector.Status))
            {
                continue;
            }

            Assert.IsFalse(
                NethereumRlpAdapter.TryDecode(vector.Input, NethereumRlpAdapter.ItemBudget, out _, out _),
                $"Nethereum accepted '{vector.Name}' which the library rejects with {vector.Status}");
        }
    }

    /// <summary>Mutation schedule: the header-rule families (prefix, length bytes, long form, child boundary) get most of the budget.</summary>
    private static readonly RlpMutationKind[] MutationSchedule =
    [
        RlpMutationKind.Prefix, RlpMutationKind.LengthBytes, RlpMutationKind.LongForm, RlpMutationKind.ChildBoundary,
        RlpMutationKind.Prefix, RlpMutationKind.LengthBytes, RlpMutationKind.LongForm, RlpMutationKind.ChildBoundary,
        RlpMutationKind.Truncate, RlpMutationKind.Append, RlpMutationKind.Nesting, RlpMutationKind.Generic,
    ];

    [TestMethod]
    public void SeededStructuralMutations_WithMatchedLimits_NeverDiverge()
    {
        var rng = new Random(MutationSeed);
        var sources = MutationSources();
        var matched = MatchedLimits;
        var defaults = RLPDecodeLimits.Default;
        var perKind = new Dictionary<RlpMutationKind, int>();
        var perStatus = new Dictionary<RLPStatus, int>();
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        var distinctAccepted = new HashSet<string>(StringComparer.Ordinal);
        var policyPatterns = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < MutationCount; i++)
        {
            var kind = MutationSchedule[i % MutationSchedule.Length];
            var source = sources[rng.Next(sources.Count)];
            var mutated = RlpStructuralMutator.Mutate(rng, source, kind);
            for (var retry = 0; retry < 3 && mutated.AsSpan().SequenceEqual(source); retry++)
            {
                mutated = RlpStructuralMutator.Mutate(rng, source, kind);
            }

            perKind[kind] = perKind.GetValueOrDefault(kind) + 1;
            var key = Convert.ToHexString(mutated);
            distinct.Add(key);

            // Matched limits: every disagreement is a defect.
            var pattern = NethereumDivergenceClassifier.Compare(mutated, matched, NethereumRlpAdapter.ItemBudget);
            Assert.IsNull(pattern, $"{kind} mutation diverged ({pattern}): {RlpReference.ToHex(mutated, true)}");
            var status = RLPData.TryDecodeComplete(mutated, in matched, out _, out _);
            perStatus[status] = perStatus.GetValueOrDefault(status) + 1;
            if (status == RLPStatus.Success)
            {
                distinctAccepted.Add(key);
            }

            // Default limits: only the documented policy patterns may appear.
            var policy = NethereumDivergenceClassifier.Compare(mutated, defaults, NethereumRlpAdapter.ItemBudget);
            if (policy is not null)
            {
                Assert.IsTrue(
                    NethereumDivergenceClassifier.DocumentedPatterns.Any(p => p.Key == policy),
                    $"{kind} mutation diverged under default limits ({policy}): {RlpReference.ToHex(mutated, true)}");
                policyPatterns.Add(policy);
            }
        }

        foreach (var kind in MutationSchedule.Distinct())
        {
            Assert.IsGreaterThanOrEqualTo(MutationCount / (MutationSchedule.Length * 2), perKind[kind], kind.ToString());
        }

        // Power checks: the inputs are varied, both outcomes are common, and the rules that matter are actually hit.
        Assert.IsGreaterThan(MutationCount * 2 / 3, distinct.Count, "distinct mutated inputs");
        Assert.IsGreaterThan(400, distinctAccepted.Count, "distinct accepted mutants");
        Assert.IsGreaterThan(2_000, perStatus.GetValueOrDefault(RLPStatus.TruncatedInput) + perStatus.GetValueOrDefault(RLPStatus.TrailingData) + perStatus.GetValueOrDefault(RLPStatus.InvalidListBoundary) + perStatus.GetValueOrDefault(RLPStatus.NonCanonicalEncoding), "rejected");
        Assert.IsGreaterThan(600, perStatus.GetValueOrDefault(RLPStatus.NonCanonicalEncoding), "NonCanonicalEncoding outcomes");
        Assert.IsGreaterThan(300, perStatus.GetValueOrDefault(RLPStatus.InvalidListBoundary), "InvalidListBoundary outcomes");
        Assert.IsGreaterThan(50, perStatus.GetValueOrDefault(RLPStatus.LengthOverflow), "LengthOverflow outcomes");
        CollectionAssert.Contains(policyPatterns.ToArray(), NethereumDivergenceClassifier.LibraryRejectsDepthBelowNethereumCap, "nesting mutations reach the depth policy pattern");
        TestContext.WriteLine($"mutations: {MutationCount}, distinct {distinct.Count}, distinct accepted {distinctAccepted.Count}; "
            + string.Join(", ", perStatus.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")));
    }

    [TestMethod]
    public void EveryOneAndTwoByteInput_AgreesWithNethereum()
    {
        var limits = MatchedLimits;
        var count = 0;
        Assert.IsNull(NethereumDivergenceClassifier.Compare([], limits, NethereumRlpAdapter.ItemBudget), "empty input");
        for (var first = 0; first < 256; first++)
        {
            Assert.IsNull(NethereumDivergenceClassifier.Compare([(byte)first], limits, NethereumRlpAdapter.ItemBudget), $"{first:x2}");
            count++;
            for (var second = 0; second < 256; second++)
            {
                var pattern = NethereumDivergenceClassifier.Compare([(byte)first, (byte)second], limits, NethereumRlpAdapter.ItemBudget);
                Assert.IsNull(pattern, $"{first:x2} {second:x2} ({pattern})");
                count++;
            }
        }

        Assert.AreEqual(256 + 65_536, count);
    }

    [TestMethod]
    public void ThreeByteInputsWithBoundaryTailBytes_AgreeWithNethereum()
    {
        // First byte 0x80..0xff (every header class), any second byte, tail byte from the values that flip a decision.
        var limits = MatchedLimits;
        byte[] tails = [0x00, 0x01, 0x38, 0x7f, 0x80, 0xc0, 0xff];
        foreach (var tail in tails)
        {
            for (var first = 0x80; first < 256; first++)
            {
                for (var second = 0; second < 256; second++)
                {
                    var pattern = NethereumDivergenceClassifier.Compare([(byte)first, (byte)second, tail], limits, NethereumRlpAdapter.ItemBudget);
                    Assert.IsNull(pattern, $"{first:x2} {second:x2} {tail:x2} ({pattern})");
                }
            }
        }
    }

    [TestMethod]
    public void ItemBudget_CountsEveryStringAndList_LikeTheLibrary()
    {
        // Boundary of 8,192 items for several element shapes: items = 1 (root) + children * itemsPerChild.
        (string Name, byte[] Child, int ItemsPerChild)[] shapes =
        [
            ("literal", [0x01], 1),
            ("empty string", [0x80], 1),
            ("empty list", [0xc0], 1),
            ("string abc", [0x83, 0x61, 0x62, 0x63], 1),
            ("[80, 80]", [0xc2, 0x80, 0x80], 3),
            ("[[]]", [0xc1, 0xc0], 2),
        ];
        var limits = new RLPDecodeLimits(1 << 24, 32, NethereumRlpAdapter.ItemBudget);
        foreach (var (name, child, itemsPerChild) in shapes)
        {
            var maxChildren = (NethereumRlpAdapter.ItemBudget - 1) / itemsPerChild;
            foreach (var children in new[] { maxChildren - 1, maxChildren, maxChildren + 1 })
            {
                var document = WrapInList(Enumerable.Range(0, children).SelectMany(_ => child).ToArray());
                var items = 1 + (children * itemsPerChild);
                var fits = items <= NethereumRlpAdapter.ItemBudget;
                var libraryStatus = RLPData.TryDecodeComplete(document, in limits, out _, out _);
                var nethereumAccepts = NethereumRlpAdapter.TryDecode(document, NethereumRlpAdapter.ItemBudget, out _, out _);
                Assert.AreEqual(fits, libraryStatus == RLPStatus.Success, $"{name} x{children} ({items} items): library {libraryStatus}");
                Assert.AreEqual(fits, nethereumAccepts, $"{name} x{children} ({items} items): Nethereum");
            }
        }
    }

    [TestMethod]
    public void DepthBoundary_At64And65_AgreesWhenLimitsAreMatched()
    {
        var limits = MatchedLimits;
        foreach (var depth in new[] { 1, 32, 33, 63, 64, 65, 66, 100 })
        {
            var document = NestedLists(depth, out _);
            var fits = depth <= 64;
            Assert.AreEqual(fits, RLPData.TryDecodeComplete(document, in limits, out _, out _) == RLPStatus.Success, $"library depth {depth}");
            Assert.AreEqual(fits, NethereumRlpAdapter.TryDecode(document, NethereumRlpAdapter.ItemBudget, out _, out _), $"Nethereum depth {depth}");
        }
    }

    [TestMethod]
    public void Classifier_FlagsDivergencesThatAreNotLimitPolicy()
    {
        // The register only means something if a new kind of divergence cannot be filed under a known one.
        var valid = new RlpString([0x01]);
        var other = new RlpString([0x02]);
        var defaults = RLPDecodeLimits.Default;
        var ok = RLPStatus.Success;

        Assert.IsNull(NethereumDivergenceClassifier.Classify(new(ok, valid, true, valid), [0x01], defaults));
        Assert.IsNull(NethereumDivergenceClassifier.Classify(new(RLPStatus.NonCanonicalEncoding, null, false, null), [0x81, 0x7f], defaults));
        Assert.AreEqual(
            NethereumDivergenceClassifier.BothAcceptDifferentTree,
            NethereumDivergenceClassifier.Classify(new(ok, valid, true, other), [0x01], defaults));
        Assert.AreEqual(
            "Unexplained.LibraryAcceptsNethereumRejects.Success",
            NethereumDivergenceClassifier.Classify(new(ok, valid, false, null), [0x81, 0x7f], defaults));
        Assert.AreEqual(
            "Unexplained.LibraryRejectsNethereumAccepts.NonCanonicalEncoding",
            NethereumDivergenceClassifier.Classify(new(RLPStatus.NonCanonicalEncoding, null, true, valid), [0x81, 0x7f], defaults));
        Assert.AreEqual(
            "Unexplained.LibraryRejectsNethereumAccepts.TruncatedInput",
            NethereumDivergenceClassifier.Classify(new(RLPStatus.TruncatedInput, null, true, valid), [0x81], defaults));
        Assert.AreEqual(
            "Unexplained.LibraryRejectsNethereumAccepts.TooManyItems",
            NethereumDivergenceClassifier.Classify(new(RLPStatus.TooManyItems, null, true, valid), [0x01], defaults));

        // The three documented patterns are accepted only when their stated reason holds.
        var nested = RlpNode.ListOf(RlpNode.ListOf(RlpNode.EmptyList));
        Assert.AreEqual(
            NethereumDivergenceClassifier.LibraryRejectsDepthBelowNethereumCap,
            NethereumDivergenceClassifier.Classify(new(RLPStatus.TooDeep, null, true, nested), [0xc1, 0xc1, 0xc0], new RLPDecodeLimits(1024, 2, 100)));
        Assert.AreEqual(
            "Unexplained.LibraryRejectsNethereumAccepts.TooDeep",
            NethereumDivergenceClassifier.Classify(new(RLPStatus.TooDeep, null, true, nested), [0xc1, 0xc1, 0xc0], new RLPDecodeLimits(1024, 3, 100)));
        Assert.AreEqual(
            NethereumDivergenceClassifier.LibraryRejectsSizeBudget,
            NethereumDivergenceClassifier.Classify(new(RLPStatus.TooLarge, null, true, valid), [0x83, 1, 2, 3], new RLPDecodeLimits(3, 32, 100)));
        Assert.AreEqual(
            "Unexplained.LibraryRejectsNethereumAccepts.TooLarge",
            NethereumDivergenceClassifier.Classify(new(RLPStatus.TooLarge, null, true, valid), [0x01], new RLPDecodeLimits(3, 32, 100)));
        Assert.AreEqual(
            NethereumDivergenceClassifier.LibraryRejectsInvalidLimits,
            NethereumDivergenceClassifier.Classify(new(RLPStatus.InvalidArgument, null, true, valid), [0x01], default));
        Assert.AreEqual(
            "Unexplained.LibraryRejectsNethereumAccepts.InvalidArgument",
            NethereumDivergenceClassifier.Classify(new(RLPStatus.InvalidArgument, null, true, valid), [0x01], defaults));
    }

    // ================================================================ 3. encoding

    [TestMethod]
    public void SeededRandomTrees_EncodeToIdenticalBytes_WithTheWriterNethereumAndTheOracle()
    {
        var checkedTrees = 0;
        foreach (var tree in GenerateTrees(TreeSeed, RandomTreeCount))
        {
            var oracle = RlpReference.Encode(tree);
            var nethereum = NethereumRlpAdapter.Encode(tree);
            var writer = RlpWriterTestSupport.BuildWithWriter(tree);
            var context = RlpReference.ToHex(oracle.AsSpan(0, Math.Min(oracle.Length, 24)));
            CollectionAssert.AreEqual(nethereum, oracle, $"{context}: Nethereum against oracle");
            CollectionAssert.AreEqual(nethereum, writer, $"{context}: Nethereum against RLPWriter");

            // The static encoder at the root: scalars are written by it, lists are sized by it.
            if (tree is RlpString text)
            {
                var destination = new byte[nethereum.Length];
                Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteByteString(text.Value, destination, out var written), context);
                Assert.AreEqual(nethereum.Length, written, context);
                CollectionAssert.AreEqual(nethereum, destination, $"{context}: Nethereum against RLPEncoder");
            }
            else
            {
                Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(RlpReference.PayloadLength(tree), out var total), context);
                Assert.AreEqual(nethereum.Length, total, $"{context}: RLPEncoder list sizing");
            }

            checkedTrees++;
        }

        Assert.IsGreaterThanOrEqualTo(2_000, checkedTrees);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(54)]
    [DataRow(55)]
    [DataRow(56)]
    [DataRow(57)]
    [DataRow(254)]
    [DataRow(255)]
    [DataRow(256)]
    [DataRow(257)]
    [DataRow(65_535)]
    [DataRow(65_536)]
    [DataRow(65_537)]
    public void ByteStringsAtLengthBoundaries_EncodeLikeNethereum(int length)
    {
        foreach (var highFirstByte in new[] { false, true })
        {
            var payload = new byte[length];
            for (var i = 0; i < length; i++)
            {
                payload[i] = (byte)((i * 7) + 3);
            }

            if (length > 0)
            {
                payload[0] = highFirstByte ? (byte)0x80 : (byte)0x05;
            }

            var expected = RLP.EncodeElement(payload);
            var destination = new byte[expected.Length + 2];
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetByteStringLength(payload, out var total));
            Assert.AreEqual(expected.Length, total, $"length {length} sizing");
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteByteString(payload, destination, out var written));
            CollectionAssert.AreEqual(expected, destination.AsSpan(0, written).ToArray(), $"length {length} RLPEncoder");

            var viaWriter = new byte[expected.Length];
            var writer = new RLPWriter(viaWriter, new int[2]);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(payload));
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var writerWritten));
            Assert.AreEqual(expected.Length, writerWritten);
            CollectionAssert.AreEqual(expected, viaWriter, $"length {length} RLPWriter");
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(54)]
    [DataRow(55)]
    [DataRow(56)]
    [DataRow(57)]
    [DataRow(255)]
    [DataRow(256)]
    [DataRow(257)]
    [DataRow(65_535)]
    [DataRow(65_536)]
    public void ListPayloadLengths_EncodeLikeNethereum(int payloadLength)
    {
        // Two shapes per payload length: many literal children, and (where it fits) one string child sized to fill it.
        var literalChildren = RlpNode.ListOf(Enumerable.Range(0, payloadLength).Select(_ => RlpNode.Bytes(0x01)));
        AssertListEncodesIdentically(literalChildren, payloadLength);

        // 0x80 bytes, so a one-byte string needs its prefix (a one-byte string below 0x80 would encode to a single byte).
        // No string encodes to exactly 57 bytes (a 55-byte string takes 56, a 56-byte string takes 58), so that row has one shape.
        var stringLength = Enumerable.Range(Math.Max(0, payloadLength - 6), 7)
            .Where(candidate => RlpReference.Encode(RlpNode.Bytes(Repeat(0x80, candidate))).Length == payloadLength)
            .Select(candidate => (int?)candidate)
            .FirstOrDefault();
        Assert.AreEqual(payloadLength is not (0 or 57), stringLength.HasValue, "a string-filled list exists for every tested size except 0 and 57");
        if (stringLength.HasValue)
        {
            AssertListEncodesIdentically(RlpNode.ListOf(RlpNode.Bytes(Repeat(0x80, stringLength.Value))), payloadLength);
        }
    }

    private static void AssertListEncodesIdentically(RlpNode list, int payloadLength)
    {
        Assert.AreEqual(payloadLength, RlpReference.PayloadLength(list), "test shape has the intended payload length");
        var nethereum = NethereumRlpAdapter.Encode(list);
        CollectionAssert.AreEqual(nethereum, RlpReference.Encode(list), $"payload {payloadLength}: oracle");
        CollectionAssert.AreEqual(nethereum, RlpWriterTestSupport.BuildWithWriter(list), $"payload {payloadLength}: RLPWriter");
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(payloadLength, out var total));
        Assert.AreEqual(nethereum.Length, total, $"payload {payloadLength}: sizing");
    }

    [TestMethod]
    public void UnsignedMagnitudesOfEveryWidthFrom0To32Bytes_EncodeLikeNethereum()
    {
        var rng = new Random(TreeSeed);
        for (var width = 0; width <= 32; width++)
        {
            var allFf = Enumerable.Repeat((byte)0xff, width).ToArray();
            var leadingOne = new byte[width];
            if (width > 0)
            {
                leadingOne[0] = 0x01;
            }

            var random = new byte[width];
            rng.NextBytes(random);
            if (width > 0 && random[0] == 0)
            {
                random[0] = 0x80;
            }

            foreach (var magnitude in new[] { allFf, leadingOne, random })
            {
                var expected = RLP.EncodeElement(magnitude);
                var destination = new byte[expected.Length + 1];
                Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUnsignedMagnitude(magnitude, destination, out var written), $"width {width}");
                CollectionAssert.AreEqual(expected, destination.AsSpan(0, written).ToArray(), $"width {width} RLPEncoder");

                var viaWriter = new byte[expected.Length];
                var writer = new RLPWriter(viaWriter, new int[2]);
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteUnsignedMagnitude(magnitude));
                Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out _));
                CollectionAssert.AreEqual(expected, viaWriter, $"width {width} RLPWriter");
            }
        }
    }

    [TestMethod]
    public void UInt64Values_EncodeLikeNethereum_IncludingItsOwnIntegerConversion()
    {
        var values = new List<ulong> { 0, 1, 127, 128, 255, 256, 65_535, 65_536, 1UL << 32, 1UL << 56, ulong.MaxValue };
        for (var bit = 0; bit < 64; bit++)
        {
            values.Add(1UL << bit);
            values.Add((1UL << bit) - 1);
            values.Add((1UL << bit) + 1);
        }

        foreach (var value in values.Distinct())
        {
            var nethereumMagnitude = value.ToBytesForRLPEncoding();
            var ourMagnitude = RlpNode.FromUInt64(value) is RlpString text ? text.Value : [];
            CollectionAssert.AreEqual(ourMagnitude, nethereumMagnitude, $"{value}: minimal magnitude");

            var expected = RLP.EncodeElement(nethereumMagnitude);
            var destination = new byte[expected.Length + 1];
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUInt64(value, destination, out var written), $"{value}");
            CollectionAssert.AreEqual(expected, destination.AsSpan(0, written).ToArray(), $"{value} RLPEncoder");

            var viaWriter = new byte[expected.Length];
            var writer = new RLPWriter(viaWriter, new int[2]);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(value));
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out _));
            CollectionAssert.AreEqual(expected, viaWriter, $"{value} RLPWriter");

            // Decoding the same bytes gives the integer back in both libraries.
            Assert.AreEqual(RLPStatus.Success, DecodeOk(expected).TryGetUInt64(out var decoded));
            Assert.AreEqual(value, decoded);
            Assert.AreEqual(value, (ulong)new BigInteger(nethereumMagnitude, isUnsigned: true, isBigEndian: true), $"{value}: Nethereum magnitude as BigInteger");
        }
    }

    // ================================================================ 4. reproducible probe table (docs/rlp-performance.md)

    public sealed record ProbeRow(string Name, byte[] Input, RLPStatus LibraryStatus, int LibraryOffset, bool NethereumAccepts)
    {
        public override string ToString() => Name;
    }

    private static ProbeRow Probe(string name, string hex, RLPStatus status, int offset, bool nethereumAccepts) =>
        new(name, H(hex), status, offset, nethereumAccepts);

    private static ProbeRow Probe(string name, byte[] input, RLPStatus status, int offset, bool nethereumAccepts) =>
        new(name, input, status, offset, nethereumAccepts);

    private static IEnumerable<ProbeRow> BuildProbeRows()
    {
        yield return Probe("empty input", "", RLPStatus.EmptyInput, 0, false);
        yield return Probe("80 empty string", "80", RLPStatus.Success, 0, true);
        yield return Probe("c0 empty list", "c0", RLPStatus.Success, 0, true);
        yield return Probe("00 literal", "00", RLPStatus.Success, 0, true);
        yield return Probe("7f literal", "7f", RLPStatus.Success, 0, true);
        yield return Probe("81 05 single byte below 0x80 with prefix", "81 05", RLPStatus.NonCanonicalEncoding, 0, false);
        yield return Probe("81 80 valid", "81 80", RLPStatus.Success, 0, true);
        yield return Probe("b8 05 + 5 bytes long form below 56", "b8 05 aa*5", RLPStatus.NonCanonicalEncoding, 1, false);
        yield return Probe("b9 00 38 + 56 bytes leading zero length byte", "b9 00 38 aa*56", RLPStatus.NonCanonicalEncoding, 1, false);
        yield return Probe("b8 38 + 56 bytes valid long string", "b8 38 aa*56", RLPStatus.Success, 0, true);
        yield return Probe("bb ff ff ff ff length 2^32-1", "bb ff ff ff ff", RLPStatus.LengthOverflow, 1, false);
        yield return Probe("bf ff*8", "bf ff*8", RLPStatus.LengthOverflow, 1, false);
        yield return Probe("c8 83 63 61 74 83 64 6f 67 + 00 trailing byte", "c8 83 63 61 74 83 64 6f 67 00", RLPStatus.TrailingData, 9, false);
        yield return Probe("83 61 62 truncated string", "83 61 62", RLPStatus.TruncatedInput, 3, false);
        yield return Probe("c8 83 63 61 74 truncated list", "c8 83 63 61 74", RLPStatus.TruncatedInput, 5, false);
        yield return Probe("c3 83 61 62 child overruns its list", "c3 83 61 62", RLPStatus.InvalidListBoundary, 1, false);
        yield return Probe("c2 81 05 non-canonical child", "c2 81 05", RLPStatus.NonCanonicalEncoding, 1, false);
        yield return Probe("c4 c3 83 61 62 nested child overruns", "c4 c3 83 61 62", RLPStatus.InvalidListBoundary, 2, false);
        yield return Probe("c8 83 63 61 74 83 64 6f 67 cat, dog", "c8 83 63 61 74 83 64 6f 67", RLPStatus.Success, 0, true);

        var nested32 = NestedLists(32, out _);
        var nested33 = NestedLists(33, out _);
        var nested64 = NestedLists(64, out _);
        var nested65 = NestedLists(65, out _);
        yield return Probe("nested 32 lists", nested32, RLPStatus.Success, 0, true);
        yield return Probe("nested 33 lists", nested33, RLPStatus.TooDeep, 32, true);

        // docs/rlp-performance.md records these two offsets as 43 and 44. That is wrong: the 33rd list prefix sits after 32
        // enclosing headers, of which 8 (64 lists) or 9 (65 lists) are two-byte f8 headers because their payload is at
        // least 56 bytes, so the offsets are 32 + 8 = 40 and 32 + 9 = 41. The statuses and the Nethereum outcomes match the document.
        yield return Probe("nested 64 lists", nested64, RLPStatus.TooDeep, 40, true);
        yield return Probe("nested 65 lists", nested65, RLPStatus.TooDeep, 41, false);

        // 8,191 literals under one list is exactly 8,192 items; 8,192 literals is 8,193.
        yield return Probe("list of 8191 literals (8192 items)", WrapInList(Repeat(0x01, 8191)), RLPStatus.Success, 0, true);
        yield return Probe("list of 8192 literals (8193 items)", WrapInList(Repeat(0x01, 8192)), RLPStatus.TooManyItems, 3 + 8191, false);
    }

    public static IEnumerable<object[]> ProbeRows => BuildProbeRows().Select(row => new object[] { row });

    [TestMethod]
    [DynamicData(nameof(ProbeRows))]
    public void ProbeTable_RecordedOutcomesHoldForBothLibraries(ProbeRow row)
    {
        var limits = RLPDecodeLimits.Default;
        var status = RLPData.TryDecodeComplete(row.Input, in limits, out _, out var error);
        Assert.AreEqual(row.LibraryStatus, status, $"{row.Name}: library status");
        Assert.AreEqual(row.LibraryOffset, error.Offset, $"{row.Name}: library offset");

        var accepts = NethereumRlpAdapter.TryDecode(row.Input, NethereumRlpAdapter.ItemBudget, out _, out var failure);
        Assert.AreEqual(row.NethereumAccepts, accepts, $"{row.Name}: Nethereum accepts ({failure})");
        if (!accepts)
        {
            // The documented claim: every rejection is the same plain Exception type.
            Assert.AreEqual(typeof(Exception).FullName, failure, row.Name);
        }
    }

    [TestMethod]
    public void ProbeTable_NethereumRejectionMessageNamesTheInputHex()
    {
        // docs/rlp-performance.md: "reports every failure as the same Exception type with the input hex in the message".
        foreach (var hex in new[] { "81 05", "b8 05 aa*5", "c3 83 61 62", "83 61 62" })
        {
            var input = H(hex);
            var exception = Assert.ThrowsExactly<Exception>(() => RLP.Decode(input, NethereumRlpAdapter.ItemBudget));
            StringAssert.Contains(exception.Message.ToLowerInvariant(), RlpReference.ToHex(input), hex);
        }
    }

    [TestMethod]
    public void ProbeTable_DefaultNethereumOverloadHasNoItemLimit_ExplicitBudgetDoes()
    {
        // docs/rlp-performance.md: "RLP.Decode(data) (no explicit budget) accepted the 8,193-item list".
        var document = WrapInList(Repeat(0x01, 8192));
        _ = RLP.Decode(document);
        Assert.ThrowsExactly<Exception>(() => RLP.Decode(document, NethereumRlpAdapter.ItemBudget));
        _ = RLP.Decode(document, NethereumRlpAdapter.ItemBudget + 1);

        // And the fixed depth cap the document states for Nethereum.
        Assert.AreEqual(64, RLP.MaxDecodeDepth);
    }

    public sealed record IntegerProbeRow(string Name, byte[] Input, RLPStatus LibraryStatus, ulong LibraryValue, bool NethereumReads, ulong NethereumValue)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<object[]> IntegerProbeRows =>
    [
        [new IntegerProbeRow("80 zero", H("80"), RLPStatus.Success, 0, true, 0)],
        [new IntegerProbeRow("00 raw zero byte", H("00"), RLPStatus.NonCanonicalInteger, 0, false, 0)],
        [new IntegerProbeRow("82 00 01 leading zero", H("82 00 01"), RLPStatus.NonCanonicalInteger, 0, false, 0)],
        [new IntegerProbeRow("88 ff*8", H("88 ff*8"), RLPStatus.Success, ulong.MaxValue, true, ulong.MaxValue)],
        [new IntegerProbeRow("89 01 00*8 nine bytes", H("89 01 00*8"), RLPStatus.IntegerOverflow, 0, false, 0)],
        [new IntegerProbeRow("81 80", H("81 80"), RLPStatus.Success, 128, true, 128)],
    ];

    [TestMethod]
    [DynamicData(nameof(IntegerProbeRows))]
    public void IntegerProbe_LibraryAndNethereumReaderBehaveAsRecorded(IntegerProbeRow row)
    {
        var libraryStatus = DecodeOk(row.Input).TryGetUInt64(out var libraryValue);
        Assert.AreEqual(row.LibraryStatus, libraryStatus, row.Name);
        Assert.AreEqual(row.LibraryValue, libraryValue, row.Name);

        if (row.NethereumReads)
        {
            Assert.AreEqual(row.NethereumValue, new RlpReader(row.Input).ReadUInt64(), row.Name);
        }
        else
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => new RlpReader(row.Input).ReadUInt64(), row.Name);
        }
    }

    [TestMethod]
    public void CursorReaderProbe_ThrowsOnTheMalformedFormsTheDocumentLists()
    {
        // docs/rlp-performance.md: RlpReader "also threw on 81 05, on a child overrunning its list, on a long form below 56,
        // and on a trailing byte after c0 with EnsureEnd".
        Assert.ThrowsExactly<InvalidOperationException>(() => new RlpReader(H("81 05")).ReadBytes());
        Assert.ThrowsExactly<InvalidOperationException>(() => new RlpReader(H("b8 05 aa*5")).ReadBytes());

        // Child overrunning its list: the list header itself reads fine; only the child read throws.
        var overrun = new RlpReader(H("c3 83 61 62"));
        _ = overrun.ReadListHeader();
        Assert.ThrowsExactly<InvalidOperationException>(() => overrun.ReadBytes());

        // Trailing byte after c0: the list scope ends cleanly; only the whole-input EnsureEnd throws.
        var trailing = new RlpReader(H("c0 00"));
        var trailingScope = trailing.ReadListHeader();
        trailing.EnsureEnd(trailingScope);
        Assert.ThrowsExactly<InvalidOperationException>(() => trailing.EnsureEnd());

        // The valid counterpart does not throw.
        var valid = new RlpReader(H("c0"));
        var validScope = valid.ReadListHeader();
        valid.EnsureEnd(validScope);
        valid.EnsureEnd();
    }

    // ================================================================ shared inputs

    /// <summary>Seeded random canonical trees: mostly small, every 50th larger with deeper nesting.</summary>
    internal static IEnumerable<RlpNode> GenerateTrees(int seed, int count)
    {
        var rng = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            yield return i % 50 == 49
                ? RlpReference.GenerateRandomTree(rng, 10, 600)
                : RlpReference.GenerateRandomTree(rng, rng.Next(0, 7), rng.Next(1, 120));
        }
    }

    private static List<byte[]> MutationSources()
    {
        var sources = RlpVectors.Canonical.Select(v => v.Encoded)
            .Concat(RlpVectors.KnownEthereum.Select(v => v.Encoded))
            .ToList();
        var rng = new Random(MutationSeed ^ 0x5eed);
        for (var i = 0; i < 600; i++)
        {
            sources.Add(RlpReference.Encode(RlpReference.GenerateRandomTree(rng, rng.Next(0, 7), rng.Next(1, 80))));
        }

        return sources;
    }
}
