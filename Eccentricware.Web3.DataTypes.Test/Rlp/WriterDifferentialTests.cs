using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Differential tests of the encoder and writer against the independent oracle (<see cref="RlpReference"/>), and a
/// writer-to-reader round trip (spec 12.3 item 8, 12.4). All randomness is seeded; a failing message carries the seed.
/// </summary>
[TestClass]
public sealed class WriterDifferentialTests
{
    private const int RandomTreeCount = 20_000;
    private const int BaseSeed = 20261116;

    private static RLPDecodeLimits RoundTripLimits => Limits(maxBytes: 4 * OneMebibyte, maxDepth: 64, maxItems: 100_000);

    /// <summary>Rebuilds the oracle tree from a decoded view, so a round trip can be compared structurally.</summary>
    private static RlpNode ToTree(RLPData node)
    {
        if (node.Kind == RLPKind.ByteString)
        {
            return RlpNode.Bytes(node.Payload.ToArray());
        }

        Assert.AreEqual(RLPStatus.Success, node.TryGetChildren(out var children));
        var items = new List<RlpNode>();
        while (children.MoveNext())
        {
            items.Add(ToTree(children.Current));
        }

        return RlpNode.ListOf(items);
    }

    private static void AssertWriterMatchesOracleAndRoundTrips(RlpNode tree, string context)
    {
        var expected = RlpReference.Encode(tree);
        var built = BuildWithWriter(tree);

        Assert.IsTrue(built.AsSpan().SequenceEqual(expected), $"{context}: writer bytes differ from the oracle");

        var status = RLPData.TryDecodeComplete(built, RoundTripLimits, out var document, out var error);
        Assert.AreEqual(RLPStatus.Success, status, $"{context}: reader rejected the writer output ({error})");
        Assert.AreEqual(built.Length, document.EncodedLength, context);
        Assert.AreEqual(tree, ToTree(document), $"{context}: round trip changed the tree");
    }

    // ------------------------------------------------------------------ canonical vectors

    [TestMethod]
    [DynamicData(nameof(RlpVectors.CanonicalRows), typeof(RlpVectors))]
    public void CanonicalVector_WriterOutputEqualsOracle_AndRoundTripsThroughTheReader(CanonicalVector vector)
    {
        AssertWriterMatchesOracleAndRoundTrips(vector.Tree, vector.Name);
    }

    [TestMethod]
    [DynamicData(nameof(RlpVectors.KnownEthereumRows), typeof(RlpVectors))]
    public void KnownEthereumVector_WriterOutputEqualsOracle_AndRoundTripsThroughTheReader(KnownEthereumVector vector)
    {
        AssertWriterMatchesOracleAndRoundTrips(vector.Tree, vector.Name);
    }

    // ------------------------------------------------------------------ random trees

    [TestMethod]
    public void RandomOracleTrees_WriterOutputIsByteIdenticalToTheOracle_AndRoundTrips()
    {
        for (var i = 0; i < RandomTreeCount; i++)
        {
            var seed = BaseSeed + i;
            var rng = new Random(seed);
            var tree = RlpReference.GenerateRandomTree(rng, maxDepth: rng.Next(0, 7), maxItems: rng.Next(1, 80), maxStringLength: 300);

            AssertWriterMatchesOracleAndRoundTrips(tree, $"seed {seed}");
        }
    }

    [TestMethod]
    public void RandomLargeTrees_WriterOutputIsByteIdenticalToTheOracle()
    {
        for (var i = 0; i < 300; i++)
        {
            var seed = BaseSeed + 1_000_000 + i;
            var rng = new Random(seed);
            var tree = RlpReference.GenerateRandomTree(rng, maxDepth: rng.Next(1, 12), maxItems: rng.Next(100, 1500), maxStringLength: 1200);

            AssertWriterMatchesOracleAndRoundTrips(tree, $"seed {seed}");
        }
    }

    [TestMethod]
    public void RandomStringsAndIntegers_EncoderScalarsEqualTheOracle()
    {
        var rng = new Random(BaseSeed + 7);
        var boundaryLengths = new[] { 0, 1, 2, 54, 55, 56, 57, 254, 255, 256, 257 };
        for (var i = 0; i < RandomTreeCount; i++)
        {
            // Strings, biased to the header boundaries; a quarter of single-byte strings stay below 0x80.
            var length = i % 2 == 0 ? boundaryLengths[rng.Next(boundaryLengths.Length)] : rng.Next(0, 400);
            var payload = new byte[length];
            rng.NextBytes(payload);
            if (length == 1 && rng.Next(4) == 0)
            {
                payload[0] &= 0x7f;
            }

            var expected = RlpReference.Encode(RlpNode.Bytes(payload));
            var destination = Filled(expected.Length + 2);
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteByteString(payload, destination, out var written), $"i {i}");
            Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual(expected), $"string i {i}");
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetByteStringLength(payload, out var total));
            Assert.AreEqual(expected.Length, total);

            // Integers across every byte width, with edge values mixed in.
            var shift = rng.Next(0, 64);
            var value = rng.Next(8) switch
            {
                0 => 0UL,
                1 => ulong.MaxValue,
                2 => (1UL << shift) - 1,
                3 => 1UL << shift,
                _ => (ulong)rng.NextInt64() >> rng.Next(0, 64),
            };
            var expectedInteger = RlpReference.Encode(RlpNode.FromUInt64(value));
            destination = Filled(expectedInteger.Length + 2);
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUInt64(value, destination, out written), $"value {value}");
            Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual(expectedInteger), $"uint {value:x}");

            // The same value as a minimal magnitude.
            var magnitude = ((RlpString)RlpNode.FromUInt64(value)).Value;
            destination = Filled(expectedInteger.Length + 2);
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUnsignedMagnitude(magnitude, destination, out written));
            Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual(expectedInteger), $"magnitude {value:x}");
        }
    }

    [TestMethod]
    public void WriterIntegerScalars_EqualTheEncoderScalars()
    {
        var rng = new Random(BaseSeed + 9);
        for (var i = 0; i < 5_000; i++)
        {
            var value = rng.Next(4) == 0 ? (ulong)rng.Next(0, 300) : (ulong)rng.NextInt64() >> rng.Next(0, 64);
            var viaEncoder = Filled(12);
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUInt64(value, viaEncoder, out var encoderWritten));
            var viaWriter = Filled(12);
            var writer = new RLPWriter(viaWriter, new int[1]);

            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(value));
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var writerWritten));

            Assert.AreEqual(encoderWritten, writerWritten, $"{value:x}");
            CollectionAssert.AreEqual(viaEncoder, viaWriter, $"{value:x}");
        }
    }

    // ------------------------------------------------------------------ TryWriteEncoded

    /// <summary>Re-emits a decoded tree: lists with <c>TryBeginList</c>, every string verbatim with <c>TryWriteEncoded</c>.</summary>
    private static RLPStatus RewriteViaEncoded(ref RLPWriter writer, RLPData node)
    {
        if (node.Kind == RLPKind.ByteString)
        {
            return writer.TryWriteEncoded(in node);
        }

        var status = writer.TryBeginList(node.PayloadLength);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        Assert.AreEqual(RLPStatus.Success, node.TryGetChildren(out var children));
        while (children.MoveNext())
        {
            status = RewriteViaEncoded(ref writer, children.Current);
            if (status != RLPStatus.Success)
            {
                return status;
            }
        }

        return writer.TryEndList();
    }

    [TestMethod]
    public void WriteEncoded_OfEveryDecodedChild_ReproducesTheOriginalBytes()
    {
        for (var i = 0; i < 3_000; i++)
        {
            var seed = BaseSeed + 2_000_000 + i;
            var rng = new Random(seed);
            var tree = RlpReference.GenerateRandomTree(rng, maxDepth: rng.Next(1, 6), maxItems: rng.Next(2, 60));
            var original = RlpReference.Encode(tree);
            var document = DecodeOk(original, RoundTripLimits);
            if (document.Kind != RLPKind.List)
            {
                continue;
            }

            // (a) every direct child copied verbatim into a new list.
            var destination = Filled(original.Length + 3);
            var writer = new RLPWriter(destination, new int[2]);
            Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(document.PayloadLength), $"seed {seed}");
            Assert.AreEqual(RLPStatus.Success, document.TryGetChildren(out var children));
            while (children.MoveNext())
            {
                var child = children.Current;
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteEncoded(in child), $"seed {seed}");
            }

            Assert.AreEqual(RLPStatus.Success, writer.TryEndList(), $"seed {seed}");
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), $"seed {seed}");
            Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual(original), $"seed {seed}: children copy");

            // (b) the whole tree re-emitted, strings copied verbatim at every depth.
            destination = Filled(original.Length + 3);
            writer = new RLPWriter(destination, new int[AmpleFrames]);
            Assert.AreEqual(RLPStatus.Success, RewriteViaEncoded(ref writer, document), $"seed {seed}");
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out written), $"seed {seed}");
            Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual(original), $"seed {seed}: deep rewrite");

            // (c) the root itself as one verbatim item.
            destination = Filled(original.Length);
            writer = new RLPWriter(destination, new int[1]);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteEncoded(in document), $"seed {seed}");
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out written), $"seed {seed}");
            CollectionAssert.AreEqual(original, destination, $"seed {seed}: root copy");
        }
    }

    [TestMethod]
    public void WriteEncoded_DoesNotNormalise_RawZeroStringsAndLeadingZeroMagnitudesAreCopiedVerbatim()
    {
        var original = H("c8 00 82 00 01 83 00 00 00");
        var document = DecodeOk(original);
        var destination = Filled(original.Length);
        var writer = new RLPWriter(destination, new int[AmpleFrames]);

        Assert.AreEqual(RLPStatus.Success, RewriteViaEncoded(ref writer, document));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));

        Assert.AreEqual(original.Length, written);
        CollectionAssert.AreEqual(original, destination);
    }

    // ------------------------------------------------------------------ 55/56 transitions from genuine children

    [TestMethod]
    public void ListPayloadOf55And56Bytes_FromGenuineChildren_UsesTheShortAndLongForms()
    {
        var destination = Filled(80);
        var writer = new RLPWriter(destination, new int[1]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(55));
        for (var i = 0; i < 55; i++)
        {
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(1));
        }

        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(56, written);
        Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual([.. new byte[] { 0xf7 }, .. Repeat(1, 55)]));

        Array.Fill(destination, Sentinel);
        writer = new RLPWriter(destination, new int[1]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(56));
        for (var i = 0; i < 56; i++)
        {
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(1));
        }

        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out written));
        Assert.AreEqual(58, written);
        Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual([.. new byte[] { 0xf8, 0x38 }, .. Repeat(1, 56)]));
    }

    [TestMethod]
    public void ListsOfMixedChildrenAcrossPayloadBoundaries_MatchTheOracle()
    {
        // Strings of 53, 54, 55 and 56 bytes inside a list push the list payload across 55/56 and 255/256.
        foreach (var stringLength in new[] { 52, 53, 54, 55, 56, 57, 252, 253, 254, 255, 256, 257 })
        {
            foreach (var siblings in new[] { 0, 1, 2 })
            {
                var items = new List<RlpNode> { RlpNode.Bytes(Repeat(0xaa, stringLength)) };
                items.AddRange(Enumerable.Range(0, siblings).Select(_ => RlpNode.FromUInt64(5)));
                var nested = RlpNode.ListOf(RlpNode.ListOf(items), RlpNode.EmptyList);

                AssertWriterMatchesOracleAndRoundTrips(nested, $"string {stringLength}, siblings {siblings}");
            }
        }
    }

    // ------------------------------------------------------------------ frame workspace sizes

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(32)]
    public void FrameWorkspaceOfN_BuildsTreesUpToDepthN_AndRejectsDeeperOnesWithTooDeep(int frameSlots)
    {
        for (var i = 0; i < 400; i++)
        {
            var seed = BaseSeed + 3_000_000 + i;
            var rng = new Random(seed);
            var tree = RlpReference.GenerateRandomTree(rng, maxDepth: rng.Next(0, 36), maxItems: rng.Next(1, 120));
            var expected = RlpReference.Encode(tree);
            var destination = Filled(expected.Length + 1);
            var writer = new RLPWriter(destination, new int[frameSlots]);

            var status = WriteNode(ref writer, tree);

            if (RlpReference.ListDepth(tree) <= frameSlots)
            {
                Assert.AreEqual(RLPStatus.Success, status, $"seed {seed}");
                Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), $"seed {seed}");
                Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual(expected), $"seed {seed}");
            }
            else
            {
                Assert.AreEqual(RLPStatus.TooDeep, status, $"seed {seed}: depth {RlpReference.ListDepth(tree)} into {frameSlots} slots");
                Assert.AreNotEqual(RLPStatus.Success, writer.TryComplete(out var failedWritten), $"seed {seed}: a rejected tree is never complete");
                Assert.AreEqual(0, failedWritten);
            }
        }
    }

    // ------------------------------------------------------------------ exact size versus one byte short

    [TestMethod]
    public void DestinationOneByteShort_FailsCleanly_AndExactSizeSucceeds_ForEveryVectorAndRandomTree()
    {
        var trees = RlpVectors.Canonical.Where(v => v.Encoded.Length <= 70_000).Select(v => v.Tree).ToList();
        for (var i = 0; i < 400; i++)
        {
            var rng = new Random(BaseSeed + 4_000_000 + i);
            trees.Add(RlpReference.GenerateRandomTree(rng, maxDepth: rng.Next(0, 6), maxItems: rng.Next(1, 60)));
        }

        foreach (var tree in trees)
        {
            var expected = RlpReference.Encode(tree);

            var exact = Filled(expected.Length);
            var writer = new RLPWriter(exact, new int[AmpleFrames]);
            Assert.AreEqual(RLPStatus.Success, WriteNode(ref writer, tree), tree.ToString());
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
            Assert.AreEqual(expected.Length, written);
            CollectionAssert.AreEqual(expected, exact);

            var shortBuffer = Filled(expected.Length - 1);
            writer = new RLPWriter(shortBuffer, new int[AmpleFrames]);
            Assert.AreEqual(RLPStatus.DestinationTooSmall, WriteNode(ref writer, tree), "the first call that cannot fit fails at the root");
            Assert.IsTrue(IsFilled(shortBuffer), "nothing was written into the short buffer");
            Assert.AreEqual(RLPStatus.EmptyInput, writer.TryComplete(out _));
        }
    }

    [TestMethod]
    public void AtEveryPositionOfANestedEncode_AnItemOneByteTooLarge_IsRejectedWithoutMutation_AndTheRealItemThenSucceeds()
    {
        for (var i = 0; i < 300; i++)
        {
            var seed = BaseSeed + 5_000_000 + i;
            var rng = new Random(seed);
            var tree = RlpReference.GenerateRandomTree(rng, maxDepth: rng.Next(0, 6), maxItems: rng.Next(1, 40), maxStringLength: 120);
            AssertEveryPositionRejectsAnOversizedItem(tree, $"seed {seed}");
        }

        AssertEveryPositionRejectsAnOversizedItem(
            RlpNode.ListOf(RlpNode.ListOf(RlpNode.Bytes(Repeat(1, 60)), RlpNode.EmptyList), RlpNode.FromUInt64(300), RlpNode.ListOf()),
            "hand-built nested tree");
    }

    private static void AssertEveryPositionRejectsAnOversizedItem(RlpNode tree, string context)
    {
        var expected = RlpReference.Encode(tree);
        var destination = Filled(expected.Length);
        var writer = new RLPWriter(destination, new int[AmpleFrames]);
        var ends = new Stack<int>();
        var cursor = 0;
        var step = 0;

        foreach (var op in Flatten(tree))
        {
            if (op.Kind != WriterOpKind.EndList)
            {
                var room = (ends.Count > 0 ? ends.Peek() : expected.Length) - cursor;
                var expectedFailure = ends.Count > 0 ? RLPStatus.InvalidListBoundary : RLPStatus.DestinationTooSmall;
                var snapshot = (byte[])destination.Clone();
                var oversized = Repeat(0xff, room); // header + room bytes is always more than room

                Assert.AreEqual(expectedFailure, writer.TryWriteByteString(oversized), $"{context} step {step}: string");
                Assert.AreEqual(expectedFailure, writer.TryWriteUnsignedMagnitude(oversized), $"{context} step {step}: magnitude");
                Assert.AreEqual(expectedFailure, writer.TryBeginList(room), $"{context} step {step}: list");
                if (room < 9)
                {
                    Assert.AreEqual(expectedFailure, writer.TryWriteUInt64(ulong.MaxValue), $"{context} step {step}: uint64");
                }

                CollectionAssert.AreEqual(snapshot, destination, $"{context} step {step}: probe mutated the destination");
            }

            Assert.AreEqual(RLPStatus.Success, Apply(ref writer, in op), $"{context} step {step}: real op");
            switch (op.Kind)
            {
                case WriterOpKind.BeginList:
                    Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(op.ListPayloadLength, out var listTotal));
                    ends.Push(cursor + listTotal);
                    cursor += listTotal - op.ListPayloadLength;
                    break;
                case WriterOpKind.String:
                    cursor += RlpReference.Encode(RlpNode.Bytes(op.Payload)).Length;
                    break;
                default:
                    ends.Pop();
                    break;
            }

            step++;
        }

        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), context);
        Assert.AreEqual(expected.Length, written, context);
        CollectionAssert.AreEqual(expected, destination, context);
    }

    [TestMethod]
    public void WriterOutput_IsAcceptedByTheReaderUnderTheDefaultLimits()
    {
        for (var i = 0; i < 500; i++)
        {
            var seed = BaseSeed + 6_000_000 + i;
            var rng = new Random(seed);
            var tree = RlpReference.GenerateRandomTree(rng, maxDepth: rng.Next(0, 8), maxItems: rng.Next(1, 200));
            var built = BuildWithWriter(tree);

            Assert.IsLessThanOrEqualTo(32, RlpReference.ListDepth(tree), $"seed {seed}: generator exceeded the default depth");
            Assert.IsLessThanOrEqualTo(8192, RlpReference.CountItems(tree), $"seed {seed}: generator exceeded the default item budget");
            var limits = RLPDecodeLimits.Default;
            var status = RLPData.TryDecodeComplete(built, in limits, out _, out _);

            Assert.AreEqual(RLPStatus.Success, status, $"seed {seed}");
        }
    }
}
