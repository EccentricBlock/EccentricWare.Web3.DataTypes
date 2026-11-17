using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Self-tests of the RLP test oracle (<see cref="RlpReference"/>) and its vector tables (<see cref="RlpVectors"/>).
/// The oracle is only trustworthy for differential testing if these pass.
/// </summary>
[TestClass]
public sealed class RlpReferenceTests
{
    private const int DefaultMaxBytes = 1024 * 1024;

    private static RlpReferenceResult DecodeWithDefaults(byte[] input) =>
        RlpReference.Decode(input, DefaultMaxBytes, 32, 8192);

    private static RlpReferenceResult DecodeVector(DecodeVector vector) =>
        RlpReference.Decode(vector.Input, vector.Limits.MaxEncodedBytes, vector.Limits.MaxListDepth, vector.Limits.MaxItems);

    // ------------------------------------------------------------------ canonical vectors

    [TestMethod]
    [DynamicData(nameof(RlpVectors.CanonicalRows), typeof(RlpVectors))]
    public void Canonical_EncodeProducesTheHandStatedBytes(CanonicalVector vector)
    {
        CollectionAssert.AreEqual(vector.Encoded, RlpReference.Encode(vector.Tree), vector.Name);
    }

    [TestMethod]
    [DynamicData(nameof(RlpVectors.CanonicalRows), typeof(RlpVectors))]
    public void Canonical_DecodeReturnsTheTreeAndReEncodesIdentically(CanonicalVector vector)
    {
        var result = DecodeWithDefaults(vector.Encoded);

        Assert.AreEqual(RLPStatus.Success, result.Status, vector.Name);
        Assert.IsNotNull(result.Root);
        Assert.AreEqual(vector.Tree, result.Root, vector.Name);
        Assert.AreEqual(RlpReference.CountItems(vector.Tree), result.ItemCount, vector.Name);
        Assert.AreEqual(RlpReference.ListDepth(vector.Tree), result.ListDepth, vector.Name);
        CollectionAssert.AreEqual(vector.Encoded, RlpReference.Encode(result.Root), vector.Name);
    }

    [TestMethod]
    public void Canonical_CoversEverySpecAndCompletenessRow()
    {
        var names = RlpVectors.Canonical.Select(v => v.Name).ToList();

        Assert.AreEqual(15, names.Count(n => n.StartsWith("spec2.3 ", StringComparison.Ordinal)), "spec 2.3 has 15 rows");
        Assert.AreEqual(128, names.Count(n => n.StartsWith("literal ", StringComparison.Ordinal)));
        Assert.AreEqual(128, names.Count(n => n.StartsWith("one-byte string ", StringComparison.Ordinal)));
        foreach (var length in new[] { 0, 1, 2, 54, 55, 56, 57, 254, 255, 256, 65535, 65536 })
        {
            CollectionAssert.Contains(names, $"string length {length}" + (length == 1 ? " (>= 0x80)" : string.Empty));
        }

        foreach (var payload in new[] { 0, 1, 54, 55, 56, 57, 255, 256 })
        {
            CollectionAssert.Contains(names, $"list payload {payload} of literal 01 children");
        }

        Assert.AreEqual(names.Count, names.Distinct().Count(), "vector names must be unique");
    }

    [TestMethod]
    public void Canonical_ListPayloadsAreBuiltFromGenuineChildren()
    {
        foreach (var vector in RlpVectors.Canonical.Where(v => v.Name.StartsWith("list payload", StringComparison.Ordinal)))
        {
            var root = (RlpList)vector.Tree;
            var claimedPayload = int.Parse(vector.Name.Split(' ')[2]);

            Assert.AreEqual(claimedPayload, RlpReference.PayloadLength(root), vector.Name);
            Assert.AreEqual(claimedPayload, root.Items.Sum(child => RlpReference.Encode(child).Length), vector.Name);
            Assert.IsTrue(DecodeWithDefaults(vector.Encoded).IsSuccess, vector.Name);
        }
    }

    [TestMethod]
    public void EmptyStringAndEmptyListAreDistinctKinds()
    {
        var emptyString = DecodeWithDefaults([0x80]);
        var emptyList = DecodeWithDefaults([0xc0]);

        Assert.IsFalse(emptyString.Root!.IsList);
        Assert.IsTrue(emptyList.Root!.IsList);
        Assert.AreNotEqual(emptyString.Root, emptyList.Root);
        Assert.AreEqual(0, emptyString.ListDepth);
        Assert.AreEqual(1, emptyList.ListDepth);
    }

    [TestMethod]
    public void RawZeroByteIsValidGenericRlp()
    {
        var result = DecodeWithDefaults([0x00]);

        Assert.AreEqual(RLPStatus.Success, result.Status);
        Assert.AreEqual(RlpNode.Bytes(0x00), result.Root);
    }

    // ------------------------------------------------------------------ independent Ethereum vectors

    [TestMethod]
    public void KnownEthereum_HasAtLeastTwentyVectorsWithSources()
    {
        Assert.IsGreaterThanOrEqualTo(20, RlpVectors.KnownEthereum.Count);
        foreach (var vector in RlpVectors.KnownEthereum)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(vector.Source), vector.Name);
        }
    }

    [TestMethod]
    [DynamicData(nameof(RlpVectors.KnownEthereumRows), typeof(RlpVectors))]
    public void KnownEthereum_EncodeAndDecodeMatchByteForByte(KnownEthereumVector vector)
    {
        CollectionAssert.AreEqual(vector.Encoded, RlpReference.Encode(vector.Tree), $"{vector.Name} ({vector.Source})");

        var decoded = DecodeWithDefaults(vector.Encoded);

        Assert.AreEqual(RLPStatus.Success, decoded.Status, vector.Name);
        Assert.AreEqual(vector.Tree, decoded.Root, vector.Name);
    }

    [TestMethod]
    public void KnownEthereum_LoremIpsumStringsHaveTheStatedLengths()
    {
        var lorem = "Lorem ipsum dolor sit amet, consectetur adipisicing elit";

        Assert.AreEqual(56, lorem.Length);
        Assert.AreEqual(55, lorem[..^1].Length);
    }

    // ------------------------------------------------------------------ decode expectations

    [TestMethod]
    [DynamicData(nameof(RlpVectors.InvalidHandStatedRows), typeof(RlpVectors))]
    [DynamicData(nameof(RlpVectors.InvalidNestedDerivedRows), typeof(RlpVectors))]
    [DynamicData(nameof(RlpVectors.LimitVectorsRows), typeof(RlpVectors))]
    [DynamicData(nameof(RlpVectors.AmbiguousPrecedenceRows), typeof(RlpVectors))]
    public void Decode_MatchesTheVectorExpectation(DecodeVector vector)
    {
        var result = DecodeVector(vector);

        Assert.AreEqual(vector.Status, result.Status, $"{vector.Name}: status");
        Assert.AreEqual(vector.Offset, result.Offset, $"{vector.Name}: offset");
        if (vector.Status == RLPStatus.Success)
        {
            Assert.IsNotNull(result.Root, vector.Name);
            CollectionAssert.AreEqual(vector.Input, RlpReference.Encode(result.Root), vector.Name);
        }
        else
        {
            Assert.IsNull(result.Root, vector.Name);
        }
    }

    [TestMethod]
    public void Decode_OffsetNormativeFlagMatchesTheSpecDerivedStatuses()
    {
        var rows = RlpVectors.InvalidHandStated
            .Concat(RlpVectors.LimitVectors)
            .Concat(RlpVectors.AmbiguousPrecedence)
            .Where(v => v.Status != RLPStatus.Success);

        foreach (var vector in rows)
        {
            var result = DecodeVector(vector);
            var specFixesOffset = vector.Status is RLPStatus.InvalidArgument or RLPStatus.EmptyInput or RLPStatus.TrailingData or RLPStatus.TruncatedInput;

            Assert.AreEqual(specFixesOffset, result.OffsetIsNormative, vector.Name);
            Assert.AreEqual(vector.OffsetIsNormative, result.OffsetIsNormative, vector.Name);
        }
    }

    [TestMethod]
    public void Decode_EverySpec12_2RowIsPresent()
    {
        var specRows = RlpVectors.InvalidHandStated.Where(v => v.Name.StartsWith("spec12.2 ", StringComparison.Ordinal)).ToList();

        Assert.AreEqual(13, specRows.Count);
        var expected = new Dictionary<string, RLPStatus>
        {
            ["spec12.2 empty input"] = RLPStatus.EmptyInput,
            ["spec12.2 81 7f"] = RLPStatus.NonCanonicalEncoding,
            ["spec12.2 b8 37 + 55 bytes"] = RLPStatus.NonCanonicalEncoding,
            ["spec12.2 f8 37 + 55 valid child bytes"] = RLPStatus.NonCanonicalEncoding,
            ["spec12.2 b9 00 38 + 56 bytes"] = RLPStatus.NonCanonicalEncoding,
            ["spec12.2 f9 00 38 + 56 valid child bytes"] = RLPStatus.NonCanonicalEncoding,
            ["spec12.2 b8"] = RLPStatus.TruncatedInput,
            ["spec12.2 b8 38 (no payload)"] = RLPStatus.TruncatedInput,
            ["spec12.2 c1 82"] = RLPStatus.InvalidListBoundary,
            ["spec12.2 80 00"] = RLPStatus.TrailingData,
            ["spec12.2 c0 00"] = RLPStatus.TrailingData,
            ["spec12.2 bf ff*8"] = RLPStatus.LengthOverflow,
            ["spec12.2 ff ff*8"] = RLPStatus.LengthOverflow,
        };

        foreach (var (name, status) in expected)
        {
            Assert.AreEqual(status, specRows.Single(r => r.Name == name).Status, name);
        }
    }

    [TestMethod]
    public void Decode_AtLeastFortyAdversarialInvalidRowsBeyondTheSpecTable()
    {
        var extra = RlpVectors.InvalidHandStated.Count(v => !v.Name.StartsWith("spec12.2 ", StringComparison.Ordinal));

        Assert.IsGreaterThanOrEqualTo(40, extra);
        Assert.IsTrue(RlpVectors.InvalidHandStated.All(v => v.Status != RLPStatus.Success));
        Assert.AreEqual(RlpVectors.InvalidHandStated.Count, RlpVectors.InvalidHandStated.Select(v => v.Name).Distinct().Count(), "names must be unique");
    }

    [TestMethod]
    public void Decode_LeadingZeroLengthIsCoveredAtEveryLengthOfLength()
    {
        for (var lengthOfLength = 1; lengthOfLength <= 8; lengthOfLength++)
        {
            foreach (var kind in new[] { "string", "list" })
            {
                var row = RlpVectors.InvalidHandStated.Single(v => v.Name == $"leading-zero length, {kind}, length-of-length {lengthOfLength}");

                Assert.AreEqual(RLPStatus.NonCanonicalEncoding, DecodeWithDefaults(row.Input).Status, row.Name);
                Assert.AreEqual((byte)((kind == "string" ? 0xb7 : 0xf7) + lengthOfLength), row.Input[0], row.Name);
            }
        }
    }

    [TestMethod]
    public void Decode_NestedDerivedRowsExistForEveryStructuralErrorRow()
    {
        var structural = RlpVectors.InvalidHandStated.Count(v => v.Status is RLPStatus.TruncatedInput or RLPStatus.NonCanonicalEncoding or RLPStatus.LengthOverflow or RLPStatus.InvalidListBoundary);

        Assert.AreEqual(structural * 2, RlpVectors.InvalidNestedDerived.Count);
    }

    [TestMethod]
    public void Decode_HandVerifiedNestedRowsAgreeWithTheSpecAndTheDerivationRule()
    {
        // Hand-checked against spec 5.1 (a child is bounded by its list) and 5.3 rules 3 to 5, independent of the
        // derivation rule used for InvalidNestedDerived. Offsets follow the oracle's documented choices.
        var cases = new (string Hex, RLPStatus Status, int Offset)[]
        {
            ("c2 81 7f", RLPStatus.NonCanonicalEncoding, 1),
            ("c2 b8 38", RLPStatus.InvalidListBoundary, 1),
            ("c1 b8", RLPStatus.InvalidListBoundary, 1),
            ("c2 c1 82", RLPStatus.InvalidListBoundary, 2),
            ("c3 c2 81 7f", RLPStatus.NonCanonicalEncoding, 2),
            ("c4 b8 37 aa aa", RLPStatus.NonCanonicalEncoding, 2),
            ("c1 80 00", RLPStatus.TrailingData, 2),
        };

        foreach (var (hex, status, offset) in cases)
        {
            var result = DecodeWithDefaults(RlpReference.FromHex(hex));

            Assert.AreEqual(status, result.Status, hex);
            Assert.AreEqual(offset, result.Offset, hex);
        }
    }

    [TestMethod]
    public void Decode_TrailingPatternBecomesAnOrdinaryListWhenWrapped()
    {
        // 80 00 is TrailingData at the root but a valid two-element list once wrapped.
        var result = DecodeWithDefaults(RlpReference.FromHex("c2 80 00"));

        Assert.AreEqual(RLPStatus.Success, result.Status);
        Assert.AreEqual(RlpNode.ListOf(RlpNode.EmptyString, RlpNode.Bytes(0x00)), result.Root);
    }

    [TestMethod]
    public void Decode_EverySingleByteInputIsClassified()
    {
        Assert.AreEqual(256, RlpVectors.SingleByteInputs.Count);
        foreach (var vector in RlpVectors.SingleByteInputs)
        {
            var result = DecodeWithDefaults(vector.Input);

            Assert.AreEqual(vector.Status, result.Status, vector.Name);
            Assert.AreEqual(vector.Offset, result.Offset, vector.Name);
        }

        Assert.AreEqual(130, RlpVectors.SingleByteInputs.Count(v => v.Status == RLPStatus.Success));
    }

    [TestMethod]
    public void Decode_EveryOneByteChildOfAListIsClassified()
    {
        Assert.AreEqual(256, RlpVectors.ListWithOneByteChild.Count);
        foreach (var vector in RlpVectors.ListWithOneByteChild)
        {
            var result = DecodeWithDefaults(vector.Input);

            Assert.AreEqual(vector.Status, result.Status, vector.Name);
            Assert.AreEqual(vector.Offset, result.Offset, vector.Name);
        }
    }

    [TestMethod]
    public void Decode_LimitsAreCheckedAtOneBelowEqualAndOneAbove()
    {
        // MaxItems: [01 x n] has n + 1 items.
        foreach (var children in new[] { 0, 1, 2, 54, 55, 56, 300 })
        {
            var input = RlpReference.Encode(RlpNode.ListOf(Enumerable.Repeat(RlpNode.Bytes(0x01), children)));
            var items = children + 1;

            Assert.AreEqual(RLPStatus.Success, RlpReference.Decode(input, DefaultMaxBytes, 32, items + 1).Status);
            Assert.AreEqual(RLPStatus.Success, RlpReference.Decode(input, DefaultMaxBytes, 32, items).Status);
            if (items > 1)
            {
                Assert.AreEqual(RLPStatus.TooManyItems, RlpReference.Decode(input, DefaultMaxBytes, 32, items - 1).Status);
            }

            Assert.AreEqual(RLPStatus.Success, RlpReference.Decode(input, input.Length + 1, 32, items).Status);
            Assert.AreEqual(RLPStatus.Success, RlpReference.Decode(input, input.Length, 32, items).Status);
            if (input.Length > 1)
            {
                Assert.AreEqual(RLPStatus.TooLarge, RlpReference.Decode(input, input.Length - 1, 32, items).Status);
            }
        }
    }

    [TestMethod]
    public void Decode_SizeLimitOneBelowRejectsBeforeContentIsExamined()
    {
        var input = RlpReference.FromHex("81 7f 00 00");

        Assert.AreEqual(RLPStatus.TooLarge, RlpReference.Decode(input, input.Length - 1, 32, 8192).Status);
        Assert.AreEqual(RLPStatus.NonCanonicalEncoding, RlpReference.Decode(input, input.Length, 32, 8192).Status);
    }

    [TestMethod]
    public void Decode_OverDepthIsRejectedBeforeRecursingIntoHugeStructures()
    {
        // Genuinely nested 5000 levels with limit 64: the oracle must report TooDeep, not overflow its stack.
        var vector = RlpVectors.LimitVectors.Single(v => v.Name.StartsWith("5000 nested lists with depth limit 64", StringComparison.Ordinal));

        var result = RlpReference.Decode(vector.Input, DefaultMaxBytes, 64, 8192);

        Assert.AreEqual(RLPStatus.TooDeep, result.Status);
    }

    [TestMethod]
    public void Decode_InvalidLimitsNeverThrowAndReportMinusOne()
    {
        foreach (var vector in RlpVectors.LimitVectors.Where(v => v.Status == RLPStatus.InvalidArgument))
        {
            var result = RlpReference.Decode(vector.Input, vector.Limits.MaxEncodedBytes, vector.Limits.MaxListDepth, vector.Limits.MaxItems);

            Assert.AreEqual(RLPStatus.InvalidArgument, result.Status, vector.Name);
            Assert.AreEqual(-1, result.Offset, vector.Name);
        }
    }

    // ------------------------------------------------------------------ integers

    [TestMethod]
    [DynamicData(nameof(RlpVectors.IntegerRows), typeof(RlpVectors))]
    [DynamicData(nameof(RlpVectors.AmbiguousIntegerRows), typeof(RlpVectors))]
    public void Integer_InterpretationMatchesTheVector(IntegerVector vector)
    {
        var decoded = DecodeWithDefaults(vector.Encoded);
        Assert.AreEqual(RLPStatus.Success, decoded.Status, $"{vector.Name}: the document itself is valid generic RLP");

        var ok = RlpReference.TryInterpretAsUInt64(decoded.Root!, out var value, out var status);

        Assert.AreEqual(vector.Status, status, vector.Name);
        Assert.AreEqual(vector.Status == RLPStatus.Success, ok, vector.Name);
        Assert.AreEqual(vector.Value, value, vector.Name);
    }

    [TestMethod]
    [DynamicData(nameof(RlpVectors.IntegerRows), typeof(RlpVectors))]
    public void Integer_SuccessRowsAreTheCanonicalEncodingOfTheValue(IntegerVector vector)
    {
        if (vector.Status != RLPStatus.Success)
        {
            return;
        }

        CollectionAssert.AreEqual(vector.Encoded, RlpReference.Encode(RlpNode.FromUInt64(vector.Value)), vector.Name);
    }

    [TestMethod]
    public void Integer_SpecialRowsFromSpec12_2ArePresent()
    {
        var byName = RlpVectors.Integer.ToDictionary(v => v.Name);

        Assert.AreEqual(RLPStatus.NonCanonicalInteger, byName["raw string 00 is not an integer"].Status);
        Assert.AreEqual(RLPStatus.NonCanonicalInteger, byName["82 00 01 leading zero"].Status);
        Assert.AreEqual(RLPStatus.IntegerOverflow, byName["89 + 9 nonzero bytes"].Status);
        Assert.AreEqual(ulong.MaxValue, byName["ulong.MaxValue"].Value);
    }

    [TestMethod]
    public void Integer_RandomValuesRoundTripAtEveryWidth()
    {
        var rng = new Random(20260416);
        for (var i = 0; i < 5000; i++)
        {
            var width = 1 + (i % 8);
            var bytes = new byte[8];
            rng.NextBytes(bytes);
            var value = BitConverter.ToUInt64(bytes) >> (8 * (8 - width));
            var node = RlpNode.FromUInt64(value);

            Assert.IsTrue(RlpReference.TryInterpretAsUInt64(node, out var back, out var status));
            Assert.AreEqual(RLPStatus.Success, status);
            Assert.AreEqual(value, back);
        }
    }

    [TestMethod]
    public void Integer_ListsAndZeroPaddedStringsAreRejected()
    {
        Assert.IsFalse(RlpReference.TryInterpretAsUInt64(RlpNode.EmptyList, out var listValue, out var listStatus));
        Assert.AreEqual(RLPStatus.WrongKind, listStatus);
        Assert.AreEqual(0UL, listValue);

        Assert.IsFalse(RlpReference.TryInterpretAsUInt64(RlpNode.Bytes(0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01), out _, out var nineStatus));
        Assert.AreEqual(RLPStatus.NonCanonicalInteger, nineStatus);

        Assert.IsFalse(RlpReference.TryInterpretAsUInt64(RlpNode.Bytes(1, 2, 3, 4, 5, 6, 7, 8, 9), out _, out var overflowStatus));
        Assert.AreEqual(RLPStatus.IntegerOverflow, overflowStatus);

        Assert.IsTrue(RlpReference.TryInterpretAsUInt64(RlpNode.EmptyString, out var zero, out _));
        Assert.AreEqual(0UL, zero);
    }

    // ------------------------------------------------------------------ encoder transitions

    [TestMethod]
    public void Encode_StringLengthTransitionsUseTheStatedPrefixes()
    {
        Assert.AreEqual("80", RlpReference.ToHex(RlpReference.Encode(RlpNode.EmptyString)));
        Assert.AreEqual("c0", RlpReference.ToHex(RlpReference.Encode(RlpNode.EmptyList)));
        Assert.AreEqual("00", RlpReference.ToHex(RlpReference.Encode(RlpNode.Bytes(0x00))));
        Assert.AreEqual("7f", RlpReference.ToHex(RlpReference.Encode(RlpNode.Bytes(0x7f))));
        Assert.AreEqual("8180", RlpReference.ToHex(RlpReference.Encode(RlpNode.Bytes(0x80))));
        Assert.AreEqual("b7", RlpReference.ToHex(RlpReference.Encode(RlpNode.Bytes(new byte[55]))[..1]));
        Assert.AreEqual("b838", RlpReference.ToHex(RlpReference.Encode(RlpNode.Bytes(new byte[56]))[..2]));
        Assert.AreEqual("b9ffff", RlpReference.ToHex(RlpReference.Encode(RlpNode.Bytes(new byte[65535]))[..3]));
        Assert.AreEqual("ba010000", RlpReference.ToHex(RlpReference.Encode(RlpNode.Bytes(new byte[65536]))[..4]));
    }

    [TestMethod]
    public void Encode_ListLengthTransitionsUseTheStatedPrefixes()
    {
        string ListPrefix(int children) =>
            RlpReference.ToHex(RlpReference.Encode(RlpNode.ListOf(Enumerable.Repeat(RlpNode.Bytes(0x01), children))));

        Assert.AreEqual("f7", ListPrefix(55)[..2]);
        Assert.AreEqual("f838", ListPrefix(56)[..4]);
        Assert.AreEqual("f8ff", ListPrefix(255)[..4]);
        Assert.AreEqual("f90100", ListPrefix(256)[..6]);
    }

    [TestMethod]
    public void Encode_EveryStringLengthUpTo1200RoundTrips()
    {
        for (var length = 0; length <= 1200; length++)
        {
            var node = RlpNode.Bytes(Enumerable.Range(0, length).Select(i => (byte)(0x80 + (i % 0x80))).ToArray());
            var encoded = RlpReference.Encode(node);
            var decoded = DecodeWithDefaults(encoded);

            Assert.AreEqual(RLPStatus.Success, decoded.Status, $"length {length}");
            Assert.AreEqual(node, decoded.Root, $"length {length}");
            CollectionAssert.AreEqual(encoded, RlpReference.Encode(decoded.Root!), $"length {length}");
        }
    }

    // ------------------------------------------------------------------ generator, round trip, mutation

    [TestMethod]
    public void RandomTrees_RoundTripByteIdenticallyAndRespectTheirBudgets()
    {
        for (var seed = 0; seed < 6000; seed++)
        {
            var rng = new Random(seed);
            var maxDepth = seed % 9;
            var maxItems = 1 + ((seed * 7) % 80);
            var tree = RlpReference.GenerateRandomTree(rng, maxDepth, maxItems);

            var items = RlpReference.CountItems(tree);
            var depth = RlpReference.ListDepth(tree);
            Assert.IsLessThanOrEqualTo(maxItems, items, $"seed {seed}");
            Assert.IsLessThanOrEqualTo(maxDepth, depth, $"seed {seed}");

            var encoded = RlpReference.Encode(tree);
            var decoded = RlpReference.Decode(encoded, DefaultMaxBytes, Math.Max(maxDepth, 1), maxItems);

            Assert.AreEqual(RLPStatus.Success, decoded.Status, $"seed {seed}");
            Assert.AreEqual(tree, decoded.Root, $"seed {seed}");
            Assert.AreEqual(items, decoded.ItemCount, $"seed {seed}");
            Assert.AreEqual(depth, decoded.ListDepth, $"seed {seed}");
            CollectionAssert.AreEqual(encoded, RlpReference.Encode(decoded.Root!), $"seed {seed}");
        }
    }

    [TestMethod]
    public void RandomTrees_LimitsAtExactlyTheirMeasuredValuesAreTight()
    {
        for (var seed = 0; seed < 3000; seed++)
        {
            var tree = RlpReference.GenerateRandomTree(new Random(seed + 100_000), 8, 60);
            var encoded = RlpReference.Encode(tree);
            var items = RlpReference.CountItems(tree);
            var depth = RlpReference.ListDepth(tree);
            var depthLimit = Math.Max(depth, 1);

            Assert.AreEqual(RLPStatus.Success, RlpReference.Decode(encoded, encoded.Length, depthLimit, items).Status, $"seed {seed}: exact limits");
            Assert.AreEqual(RLPStatus.Success, RlpReference.Decode(encoded, encoded.Length + 1, depthLimit + 1, items + 1).Status, $"seed {seed}: limit + 1");
            if (items > 1)
            {
                Assert.AreEqual(RLPStatus.TooManyItems, RlpReference.Decode(encoded, DefaultMaxBytes, 64, items - 1).Status, $"seed {seed}: items - 1");
            }

            if (depth >= 2)
            {
                Assert.AreEqual(RLPStatus.TooDeep, RlpReference.Decode(encoded, DefaultMaxBytes, depth - 1, items).Status, $"seed {seed}: depth - 1");
            }

            if (encoded.Length > 1)
            {
                Assert.AreEqual(RLPStatus.TooLarge, RlpReference.Decode(encoded, encoded.Length - 1, depthLimit, items).Status, $"seed {seed}: size - 1");
            }
        }
    }

    [TestMethod]
    public void RandomTrees_TruncatedAndTrailingVariantsAreRejectedAtTheStatedOffset()
    {
        for (var seed = 0; seed < 3000; seed++)
        {
            var tree = RlpReference.GenerateRandomTree(new Random(seed + 200_000), 6, 40);
            var encoded = RlpReference.Encode(tree);

            var trailing = RlpReference.Concatenate(encoded, [0x00]);
            var trailingResult = DecodeWithDefaults(trailing);
            Assert.AreEqual(RLPStatus.TrailingData, trailingResult.Status, $"seed {seed}");
            Assert.AreEqual(encoded.Length, trailingResult.Offset, $"seed {seed}");

            var truncated = encoded[..^1];
            var truncatedResult = DecodeWithDefaults(truncated);
            if (truncated.Length == 0)
            {
                Assert.AreEqual(RLPStatus.EmptyInput, truncatedResult.Status, $"seed {seed}");
            }
            else
            {
                Assert.AreEqual(RLPStatus.TruncatedInput, truncatedResult.Status, $"seed {seed}");
                Assert.AreEqual(truncated.Length, truncatedResult.Offset, $"seed {seed}");
            }
        }
    }

    [TestMethod]
    public void RandomTrees_HitEveryBoundaryPayloadSizeAndShape()
    {
        var stringLengths = new HashSet<int>();
        var listPayloads = new HashSet<int>();
        var sawLiteral = false;
        var sawHighByte = false;
        var sawEmptyList = false;
        var sawLeadingZeroString = false;
        var deepest = 0;

        void Visit(RlpNode node)
        {
            if (node is RlpString text)
            {
                stringLengths.Add(text.Value.Length);
                sawLiteral |= text.Value.Length == 1 && text.Value[0] < 0x80;
                sawHighByte |= text.Value.Length == 1 && text.Value[0] >= 0x80;
                sawLeadingZeroString |= text.Value.Length > 1 && text.Value[0] == 0;
                return;
            }

            var list = (RlpList)node;
            listPayloads.Add(RlpReference.PayloadLength(list));
            sawEmptyList |= list.Items.Count == 0;
            foreach (var child in list.Items)
            {
                Visit(child);
            }
        }

        for (var seed = 0; seed < 6000; seed++)
        {
            var tree = RlpReference.GenerateRandomTree(new Random(seed), seed % 9, 1 + ((seed * 7) % 80));
            deepest = Math.Max(deepest, RlpReference.ListDepth(tree));
            Visit(tree);
        }

        foreach (var length in new[] { 0, 1, 2, 54, 55, 56, 57, 255, 256 })
        {
            Assert.IsTrue(stringLengths.Contains(length), $"string length {length} never generated");
        }

        foreach (var payload in new[] { 0, 1, 54, 55, 56, 57, 255, 256 })
        {
            Assert.IsTrue(listPayloads.Contains(payload), $"list payload {payload} never generated");
        }

        Assert.IsTrue(sawLiteral && sawHighByte && sawEmptyList && sawLeadingZeroString);
        Assert.IsGreaterThanOrEqualTo(5, deepest);
    }

    [TestMethod]
    public void RandomTrees_GeneratorIsDeterministicPerSeed()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var first = RlpReference.GenerateRandomTree(new Random(seed), 6, 50);
            var second = RlpReference.GenerateRandomTree(new Random(seed), 6, 50);

            Assert.AreEqual(first, second, $"seed {seed}");
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode(), $"seed {seed}");
        }
    }

    [TestMethod]
    public void RandomTrees_DepthZeroGivesScalarsAndItemBudgetOneGivesOneItem()
    {
        for (var seed = 0; seed < 500; seed++)
        {
            Assert.IsFalse(RlpReference.GenerateRandomTree(new Random(seed), 0, 50).IsList, $"seed {seed}");
            Assert.AreEqual(1, RlpReference.CountItems(RlpReference.GenerateRandomTree(new Random(seed), 6, 1)), $"seed {seed}");
        }
    }

    [TestMethod]
    public void MutationFuzz_NeverThrowsAndAcceptedInputIsCanonical()
    {
        var statuses = new Dictionary<RLPStatus, int>();
        var accepted = 0;
        for (var seed = 0; seed < 30000; seed++)
        {
            var rng = new Random(seed);
            var tree = RlpReference.GenerateRandomTree(rng, 5, 30, 120);
            var mutated = RlpReference.MutateRandomly(rng, RlpReference.Encode(tree));

            var result = RlpReference.Decode(mutated, DefaultMaxBytes, 16, 200);

            statuses[result.Status] = statuses.GetValueOrDefault(result.Status) + 1;
            AssertDecodeResultIsConsistent(mutated, result, $"seed {seed}");
            if (result.IsSuccess)
            {
                accepted++;
                CollectionAssert.AreEqual(mutated, RlpReference.Encode(result.Root!), $"seed {seed}: accepted input must equal its canonical re-encoding");
            }
        }

        Assert.IsGreaterThan(0, accepted, "fuzz never produced an accepted input");
        foreach (var status in new[] { RLPStatus.NonCanonicalEncoding, RLPStatus.TruncatedInput, RLPStatus.InvalidListBoundary, RLPStatus.TrailingData })
        {
            Assert.IsTrue(statuses.ContainsKey(status), $"fuzz never produced {status}");
        }
    }

    [TestMethod]
    public void RandomBytesFuzz_NeverThrowsAndAcceptedInputIsCanonical()
    {
        for (var seed = 0; seed < 30000; seed++)
        {
            var rng = new Random(seed + 500_000);
            var bytes = new byte[rng.Next(0, 40)];
            rng.NextBytes(bytes);
            if (bytes.Length > 0 && seed % 3 == 0)
            {
                bytes[0] = (byte)(0xc0 + rng.Next(0, 56)); // bias towards list prefixes that actually descend
            }

            var result = RlpReference.Decode(bytes, DefaultMaxBytes, 8, 64);

            AssertDecodeResultIsConsistent(bytes, result, $"seed {seed}");
            if (result.IsSuccess)
            {
                CollectionAssert.AreEqual(bytes, RlpReference.Encode(result.Root!), $"seed {seed}");
            }
        }
    }

    [TestMethod]
    public void MutateRandomly_IsDeterministicChangesInputAndMayEmptyIt()
    {
        var original = RlpReference.Encode(RlpReference.GenerateRandomTree(new Random(1), 4, 20));
        var changed = 0;
        for (var seed = 0; seed < 2000; seed++)
        {
            var first = RlpReference.MutateRandomly(new Random(seed), original);
            var second = RlpReference.MutateRandomly(new Random(seed), original);

            CollectionAssert.AreEqual(first, second, $"seed {seed}");
            if (!first.AsSpan().SequenceEqual(original))
            {
                changed++;
            }
        }

        Assert.IsGreaterThan(1500, changed);
        var tiny = false;
        for (var seed = 0; seed < 2000 && !tiny; seed++)
        {
            tiny = RlpReference.MutateRandomly(new Random(seed), [0x80]).Length == 0;
        }

        Assert.IsTrue(tiny, "the mutator never produced an empty input from a one-byte input");
    }

    private static void AssertDecodeResultIsConsistent(byte[] input, RlpReferenceResult result, string context)
    {
        if (result.IsSuccess)
        {
            Assert.IsNotNull(result.Root, context);
            return;
        }

        Assert.IsNull(result.Root, context);
        CollectionAssert.Contains(
            new[]
            {
                RLPStatus.EmptyInput, RLPStatus.TruncatedInput, RLPStatus.NonCanonicalEncoding, RLPStatus.TrailingData,
                RLPStatus.InvalidListBoundary, RLPStatus.LengthOverflow, RLPStatus.TooDeep, RLPStatus.TooManyItems,
            },
            result.Status,
            context);
        if (result.Status is RLPStatus.TruncatedInput or RLPStatus.EmptyInput)
        {
            Assert.AreEqual(result.Status == RLPStatus.EmptyInput ? 0 : input.Length, result.Offset, context);
        }
        else
        {
            Assert.IsTrue(result.Offset >= 0 && result.Offset < input.Length, $"{context}: offset {result.Offset} outside input of {input.Length}");
        }
    }

    // ------------------------------------------------------------------ helpers

    [TestMethod]
    public void Hex_ToHexAndFromHexRoundTripAndAcceptSeparatorsAndCase()
    {
        var bytes = new byte[] { 0x00, 0x0a, 0xb7, 0xff };

        Assert.AreEqual("000ab7ff", RlpReference.ToHex(bytes));
        Assert.AreEqual("00 0a b7 ff", RlpReference.ToHex(bytes, spaced: true));
        CollectionAssert.AreEqual(bytes, RlpReference.FromHex("000ab7ff"));
        CollectionAssert.AreEqual(bytes, RlpReference.FromHex("00 0A b7   FF"));
        CollectionAssert.AreEqual(bytes, RlpReference.FromHex("  00\t0a\nB7 ff  "));
        CollectionAssert.AreEqual(Array.Empty<byte>(), RlpReference.FromHex(string.Empty));
        CollectionAssert.AreEqual(new byte[] { 1, 1, 1 }, RlpReference.FromHex("01*3"));
        CollectionAssert.AreEqual(new byte[] { 0xab, 0xcd, 0xab, 0xcd }, RlpReference.FromHex("abcd*2"));
        CollectionAssert.AreEqual(Array.Empty<byte>(), RlpReference.FromHex("ff*0"));
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("abc")]
    [DataRow("zz")]
    [DataRow("0x00")]
    [DataRow("01*")]
    [DataRow("01*-1")]
    [DataRow("01*x")]
    public void Hex_FromHexRejectsMalformedText(string text)
    {
        Assert.ThrowsExactly<FormatException>(() => RlpReference.FromHex(text));
    }

    [TestMethod]
    public void Nodes_EqualityIsDeepAndByContent()
    {
        var a = RlpNode.ListOf(RlpNode.Ascii("cat"), RlpNode.ListOf(RlpNode.Bytes(1, 2)));
        var b = RlpNode.ListOf(RlpNode.Ascii("cat"), RlpNode.ListOf(RlpNode.Bytes(1, 2)));
        var differentLeaf = RlpNode.ListOf(RlpNode.Ascii("cat"), RlpNode.ListOf(RlpNode.Bytes(1, 3)));
        var differentShape = RlpNode.ListOf(RlpNode.Ascii("cat"), RlpNode.Bytes(1, 2));

        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.AreNotEqual(a, differentLeaf);
        Assert.AreNotEqual(a, differentShape);
        Assert.AreNotEqual(RlpNode.EmptyString, RlpNode.EmptyList);
        Assert.AreNotEqual(RlpNode.Bytes(0), RlpNode.EmptyString);
        Assert.AreNotEqual(RlpNode.ListOf(RlpNode.EmptyList), RlpNode.EmptyList);
    }

    [TestMethod]
    public void Nodes_FactoriesCopyTheirInputs()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var node = RlpNode.Bytes(bytes);
        bytes[0] = 9;

        Assert.AreEqual(RlpNode.Bytes(1, 2, 3), node);
        Assert.AreEqual(RlpNode.FromUInt64(0), RlpNode.EmptyString);
        Assert.AreEqual(RlpNode.FromUInt64(0x0100), RlpNode.Bytes(1, 0));
        Assert.AreEqual(RlpNode.FromUInt64(ulong.MaxValue), RlpNode.Bytes(0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff));
    }
}
