using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.EvmSignatureOracle;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.TypedAdapterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// <see cref="RLPTypedExtensions.TryWriteEvmYParity"/> and <see cref="RLPTypedExtensions.TryWriteEvmLegacyV"/>: exact
/// bytes (oracle and hand-written), the writer's check order (state, root, parity argument, size, capacity), atomicity
/// and the round trip through the matching readers.
/// </summary>
[TestClass]
public sealed class EvmSignatureWriterTests
{
    private delegate RLPStatus WriterAction(ref RLPWriter writer);

    private static byte[] EncodeWith(WriterAction action, int size)
    {
        var destination = Filled(size + 3);
        var writer = new RLPWriter(destination, new int[4]);
        Assert.AreEqual(RLPStatus.Success, action(ref writer));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(size, written);
        Assert.IsTrue(IsFilled(destination.AsSpan(written)), "bytes past the item are untouched");
        return destination.AsSpan(0, written).ToArray();
    }

    // ------------------------------------------------------------------ wire bytes

    [TestMethod]
    public void YParity_ZeroIsEightyAndOneIsOne()
    {
        CollectionAssert.AreEqual(new byte[] { 0x80 }, EncodeWith((ref RLPWriter w) => w.TryWriteEvmYParity(0), 1));
        CollectionAssert.AreEqual(new byte[] { 0x01 }, EncodeWith((ref RLPWriter w) => w.TryWriteEvmYParity(1), 1));
        CollectionAssert.AreEqual(YParityItem(0), EncodeWith((ref RLPWriter w) => w.TryWriteEvmYParity(0), 1));
        CollectionAssert.AreEqual(YParityItem(1), EncodeWith((ref RLPWriter w) => w.TryWriteEvmYParity(1), 1));
    }

    [TestMethod]
    public void LegacyV_HandWrittenWireVectors()
    {
        var rows = new (ulong? Chain, byte Parity, string ItemHex)[]
        {
            (null, 0, "1b"),
            (null, 1, "1c"),
            (0, 0, "23"),
            (0, 1, "24"),
            (1, 0, "25"),
            (1, 1, "26"),
            (46, 0, "7f"),
            (46, 1, "81 80"),
            (110, 0, "81 ff"),
            (110, 1, "82 0100"),
            (137, 0, "82 0135"),
            (137, 1, "82 0136"),
            (8453, 1, "82 422e"),
            (42161, 0, "83 014985"),
            (98866, 0, "83 030487"),
            (98866, 1, "83 030488"),
            (ulong.MaxValue, 0, "89 020000000000000021"),
            (ulong.MaxValue, 1, "89 020000000000000022"),
        };

        foreach (var (chain, parity, itemHex) in rows)
        {
            var expected = H(itemHex);
            var actual = EncodeWith((ref RLPWriter w) => w.TryWriteEvmLegacyV(parity, chain), expected.Length);
            CollectionAssert.AreEqual(expected, actual, $"chain {chain?.ToString() ?? "none"} parity {parity}");
        }
    }

    [TestMethod]
    public void LegacyV_RealChainIdsAndWidthBoundaries_EqualTheOracle()
    {
        var chains = RealChainIds.Select(c => c.ChainId).Concat(WidthBoundaryChainIds()).Distinct().Select(c => (ulong?)c).Append(null);
        foreach (var chain in chains)
        {
            foreach (var parity in new byte[] { 0, 1 })
            {
                var expected = LegacyVItem(parity, chain);
                CollectionAssert.AreEqual(expected, EncodeWith((ref RLPWriter w) => w.TryWriteEvmLegacyV(parity, chain), expected.Length), $"chain {chain?.ToString() ?? "none"} parity {parity}");
            }
        }
    }

    [TestMethod]
    public void LegacyV_NeverOverflowsAUInt64_EvenForTheLargestChainId()
    {
        // 2 * ulong.MaxValue + 36 does not fit 64 bits: a naive ulong computation would wrap to a short value.
        var encoded = EncodeWith((ref RLPWriter w) => w.TryWriteEvmLegacyV(1, ulong.MaxValue), 10);
        Assert.HasCount(10, encoded);
        Assert.AreEqual((byte)0x89, encoded[0]);
        Assert.AreEqual((byte)0x02, encoded[1]);
    }

    [TestMethod]
    public void LegacyV_RoundTripsThroughTheLegacyReader()
    {
        var chains = RealChainIds.Select(c => c.ChainId).Concat(WidthBoundaryChainIds()).Distinct().Select(c => (ulong?)c).Append(null);
        foreach (var chain in chains)
        {
            foreach (var parity in new byte[] { 0, 1 })
            {
                var item = EncodeWith((ref RLPWriter w) => w.TryWriteEvmLegacyV(parity, chain), LegacyVItem(parity, chain).Length);
                var status = RLPTypedExtensions.TryGetEvmLegacySignature(DecodeOk(item), StringItem([1]), StringItem([1]), out var signature, out var chainId);
                Assert.AreEqual(RLPStatus.Success, status, $"chain {chain?.ToString() ?? "none"} parity {parity}");
                Assert.AreEqual(chain, chainId, $"chain {chain?.ToString() ?? "none"} parity {parity}");
                Assert.AreEqual(parity, signature.V, $"chain {chain?.ToString() ?? "none"} parity {parity}");
            }
        }
    }

    [TestMethod]
    public void YParity_RoundTripsThroughTheTypedReader()
    {
        foreach (var parity in new byte[] { 0, 1 })
        {
            var item = EncodeWith((ref RLPWriter w) => w.TryWriteEvmYParity(parity), 1);
            Assert.AreEqual(RLPStatus.Success, RLPTypedExtensions.TryGetEvmYParitySignature(DecodeOk(item), StringItem([1]), StringItem([1]), out var signature));
            Assert.AreEqual(parity, signature.V);
        }
    }

    // ------------------------------------------------------------------ invalid parity and the check order

    private static readonly (string Name, WriterAction Valid, WriterAction Invalid, int Size)[] ParityWriters =
    [
        ("TryWriteEvmYParity", (ref RLPWriter w) => w.TryWriteEvmYParity(1), (ref RLPWriter w) => w.TryWriteEvmYParity(2), 1),
        ("TryWriteEvmLegacyV none", (ref RLPWriter w) => w.TryWriteEvmLegacyV(1, null), (ref RLPWriter w) => w.TryWriteEvmLegacyV(2, null), 1),
        ("TryWriteEvmLegacyV 98866", (ref RLPWriter w) => w.TryWriteEvmLegacyV(1, 98866), (ref RLPWriter w) => w.TryWriteEvmLegacyV(2, 98866), 4),
        ("TryWriteEvmLegacyV max", (ref RLPWriter w) => w.TryWriteEvmLegacyV(1, ulong.MaxValue), (ref RLPWriter w) => w.TryWriteEvmLegacyV(255, ulong.MaxValue), 10),
    ];

    [TestMethod]
    public void ParityAbove1_IsInvalidSignatureV_WriterUntouched_AndAValidRetryWorks()
    {
        foreach (var (name, valid, invalid, size) in ParityWriters)
        {
            var destination = Filled(size + 4);
            var writer = new RLPWriter(destination, new int[4]);

            Assert.AreEqual(RLPStatus.InvalidSignatureV, invalid(ref writer), name);
            Assert.IsTrue(IsFilled(destination), $"{name}: buffer untouched");
            Assert.AreEqual(RLPStatus.EmptyInput, writer.TryComplete(out _), $"{name}: no root was started");

            Assert.AreEqual(RLPStatus.Success, valid(ref writer), name);
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), name);
            Assert.AreEqual(size, written, name);
            Assert.IsTrue(IsFilled(destination.AsSpan(size)), name);
        }
    }

    [TestMethod]
    public void EveryParityFrom2To255_IsInvalidSignatureV_ForBothWriters()
    {
        var destination = Filled(16);
        var writer = new RLPWriter(destination, new int[4]);
        for (var parity = 2; parity <= 255; parity++)
        {
            Assert.AreEqual(RLPStatus.InvalidSignatureV, writer.TryWriteEvmYParity((byte)parity), $"y parity {parity}");
            Assert.AreEqual(RLPStatus.InvalidSignatureV, writer.TryWriteEvmLegacyV((byte)parity, null), $"legacy {parity}");
            Assert.AreEqual(RLPStatus.InvalidSignatureV, writer.TryWriteEvmLegacyV((byte)parity, 1), $"legacy {parity} chain 1");
        }

        Assert.IsTrue(IsFilled(destination));
        Assert.AreEqual(RLPStatus.EmptyInput, writer.TryComplete(out _));
    }

    [TestMethod]
    public void StateAndRootChecksComeBeforeTheParityArgument()
    {
        foreach (var (name, valid, invalid, size) in ParityWriters)
        {
            // default(RLPWriter): state error for a valid and an invalid argument alike.
            var defaultWriter = default(RLPWriter);
            Assert.AreEqual(RLPStatus.WriterStateInvalid, invalid(ref defaultWriter), $"{name}: default, bad parity");
            Assert.AreEqual(RLPStatus.WriterStateInvalid, valid(ref defaultWriter), $"{name}: default, good parity");

            // A second root.
            var destination = Filled(size + 8);
            var writer = new RLPWriter(destination, new int[4]);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(1), name);
            var afterRoot = destination.ToArray();
            Assert.AreEqual(RLPStatus.ExtraRoot, invalid(ref writer), $"{name}: extra root, bad parity");
            Assert.AreEqual(RLPStatus.ExtraRoot, valid(ref writer), $"{name}: extra root, good parity");
            CollectionAssert.AreEqual(afterRoot, destination, $"{name}: extra root left the buffer");
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out _), name);

            // After completion.
            Assert.AreEqual(RLPStatus.WriterStateInvalid, invalid(ref writer), $"{name}: completed, bad parity");
            Assert.AreEqual(RLPStatus.WriterStateInvalid, valid(ref writer), $"{name}: completed, good parity");
            CollectionAssert.AreEqual(afterRoot, destination, $"{name}: completed left the buffer");
        }
    }

    [TestMethod]
    public void ParityArgumentComesBeforeCapacityAndParentLimit()
    {
        foreach (var (name, valid, invalid, size) in ParityWriters)
        {
            // Empty destination: a bad parity is still InvalidSignatureV, a good one is DestinationTooSmall.
            var empty = new RLPWriter([], new int[4]);
            Assert.AreEqual(RLPStatus.InvalidSignatureV, invalid(ref empty), $"{name}: empty destination");
            Assert.AreEqual(RLPStatus.DestinationTooSmall, valid(ref empty), $"{name}: empty destination, good parity");

            // A full parent list: a bad parity is InvalidSignatureV, a good one InvalidListBoundary.
            var destination = Filled(size + 8);
            var writer = new RLPWriter(destination, new int[4]);
            Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(size - 1), name);
            var afterHeader = destination.ToArray();
            Assert.AreEqual(RLPStatus.InvalidSignatureV, invalid(ref writer), $"{name}: parent limit");
            Assert.AreEqual(RLPStatus.InvalidListBoundary, valid(ref writer), $"{name}: parent limit, good parity");
            CollectionAssert.AreEqual(afterHeader, destination, $"{name}: buffer untouched");
            Assert.AreEqual(size == 1 ? RLPStatus.Success : RLPStatus.UnbalancedList, writer.TryEndList(), $"{name}: no child bytes counted");
        }
    }

    // ------------------------------------------------------------------ capacity, parent limit, atomicity

    [TestMethod]
    public void DestinationOneByteShort_IsDestinationTooSmall_Untouched_AndExactFitSucceeds()
    {
        var cases = new (string Name, WriterAction Action, int Size)[]
        {
            ("legacy 98866 p0", (ref RLPWriter w) => w.TryWriteEvmLegacyV(0, 98866), 4),
            ("legacy 137 p1", (ref RLPWriter w) => w.TryWriteEvmLegacyV(1, 137), 3),
            ("legacy max", (ref RLPWriter w) => w.TryWriteEvmLegacyV(0, ulong.MaxValue), 10),
            ("legacy chain 46 p1 (first two-byte v)", (ref RLPWriter w) => w.TryWriteEvmLegacyV(1, 46), 2),
        };

        foreach (var (name, action, size) in cases)
        {
            var shortDestination = Filled(size - 1);
            var shortWriter = new RLPWriter(shortDestination, new int[4]);
            Assert.AreEqual(RLPStatus.DestinationTooSmall, action(ref shortWriter), name);
            Assert.IsTrue(IsFilled(shortDestination), $"{name}: untouched");
            Assert.AreEqual(RLPStatus.EmptyInput, shortWriter.TryComplete(out _), $"{name}: writer untouched");

            var exact = Filled(size);
            var writer = new RLPWriter(exact, new int[4]);
            Assert.AreEqual(RLPStatus.Success, action(ref writer), name);
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), name);
            Assert.AreEqual(size, written, name);
        }
    }

    [TestMethod]
    public void ParentLimitInsideAList_IsInvalidListBoundary_AndTheListStaysRepairable()
    {
        var destination = Filled(20);
        var writer = new RLPWriter(destination, new int[4]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(3));
        var afterHeader = destination.ToArray();

        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteEvmLegacyV(0, 98866), "4 bytes into 3");
        CollectionAssert.AreEqual(afterHeader, destination);
        Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryEndList());

        // 8453 needs exactly 3 bytes.
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteEvmLegacyV(0, 8453));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(4, written);
    }

    [TestMethod]
    public void WriterAdapters_ComposeIntoASignatureTail_AndTheListClosesAtTheAnnouncedLength()
    {
        // [ yParity, r, s ] sized with the sizing helpers, as a transaction writer would.
        var r = new uint256(Padded32((System.Numerics.BigInteger.One << 255) + 5));
        var s = new uint256(Padded32(0x80));
        var length = 1 + RLPTypedExtensions.GetUInt256EncodedLength(r) + RLPTypedExtensions.GetUInt256EncodedLength(s);
        var destination = Filled(length + 1 + 2);
        var writer = new RLPWriter(destination, new int[4]);

        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(length));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteEvmYParity(1));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(r));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(s));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(length + 1, written);
    }
}
