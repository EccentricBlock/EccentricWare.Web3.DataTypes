using System.Numerics;
using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.TypedAdapterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// The typed writer adapters: exact wire bytes at every boundary, equality with the oracle and with
/// <see cref="RLPEncoder.TryWriteUInt64"/>, and the writer's atomicity and state rules inherited through delegation.
/// </summary>
[TestClass]
public sealed class TypedAdapterWriterTests
{
    private delegate RLPStatus WriterAction(ref RLPWriter writer);

    private static readonly Hash32 SampleHash = new(Pattern(32, 0xee));
    private static readonly Address SampleAddress = Address.FromEvmBytes(Pattern(20, 0xa1));
    private static readonly Address SampleSolana = Address.FromSolanaBytes(Pattern(32, 0x01));
    private static readonly uint256 BigValue = new(RlpReference.FromHex("80" + new string('0', 62)));

    /// <summary>The adapters with an encoded size of at least 2 bytes, so a destination one byte short still has room for a literal.</summary>
    private static readonly (string Name, WriterAction Action, int Size)[] LargeWrites =
    [
        ("TryWriteUInt256", (ref RLPWriter w) => w.TryWriteUInt256(BigValue), 33),
        ("TryWriteHash32", (ref RLPWriter w) => w.TryWriteHash32(SampleHash), 33),
        ("TryWriteAddress", (ref RLPWriter w) => w.TryWriteAddress(SampleAddress), 21),
    ];

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

    // ------------------------------------------------------------------ uint256 wire format

    [TestMethod]
    public void UInt256_HandWrittenVectors()
    {
        var rows = new (string Value, string Hex)[]
        {
            ("0", "80"),
            ("1", "01"),
            ("127", "7f"),
            ("128", "81 80"),
            ("255", "81 ff"),
            ("256", "82 0100"),
            ("18446744073709551615", "88 ffffffffffffffff"),
            ("18446744073709551616", "89 010000000000000000"),
            ("57896044618658097711785492504343953926634992332820282019728792003956564819968", "a0 8000000000000000000000000000000000000000000000000000000000000000"),
            ("115792089237316195423570985008687907853269984665640564039457584007913129639935", "a0 ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"),
        };

        foreach (var (value, hex) in rows)
        {
            var expected = H(hex);
            var number = (uint256)BigInteger.Parse(value);
            CollectionAssert.AreEqual(expected, EncodeWith((ref RLPWriter w) => w.TryWriteUInt256(number), expected.Length), value);
        }
    }

    [TestMethod]
    public void UInt256_EveryBoundary_EqualsTheOracleOfTheMinimalMagnitude()
    {
        foreach (var value in BoundaryValues())
        {
            var expected = RlpReference.Encode(RlpNode.Bytes(Minimal(value)));
            var number = new uint256(Padded32(value));
            CollectionAssert.AreEqual(expected, EncodeWith((ref RLPWriter w) => w.TryWriteUInt256(number), expected.Length), value.ToString());
        }
    }

    [TestMethod]
    public void UInt256_EveryBitPosition_EqualsTheOracle()
    {
        for (var bit = 0; bit < 256; bit++)
        {
            foreach (var value in new[] { BigInteger.One << bit, (BigInteger.One << bit) - 1, (BigInteger.One << (bit + 1)) - 1 })
            {
                if (value.IsZero && bit > 0 || value >= TwoPow256)
                {
                    continue;
                }

                var expected = RlpReference.Encode(RlpNode.Bytes(Minimal(value)));
                var number = new uint256(Padded32(value));
                CollectionAssert.AreEqual(expected, EncodeWith((ref RLPWriter w) => w.TryWriteUInt256(number), expected.Length), $"bit {bit} value {value}");
            }
        }
    }

    [TestMethod]
    public void UInt256_ValuesFitting64Bits_EqualEncoderUInt64()
    {
        var values = new List<ulong> { 0, 1, 0x7f, 0x80, 0xff, 0x100, 0xffff, 0x10000, uint.MaxValue, 1UL << 32, ulong.MaxValue - 1, ulong.MaxValue };
        for (var bit = 0; bit < 64; bit++)
        {
            values.Add(1UL << bit);
            values.Add((1UL << bit) - 1);
        }

        var rng = new Random(20261118);
        for (var i = 0; i < 2_000; i++)
        {
            values.Add((ulong)rng.NextInt64() >> rng.Next(0, 64));
        }

        var expected = new byte[16];
        foreach (var value in values)
        {
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUInt64(value, expected, out var length));
            var number = new uint256(value);
            CollectionAssert.AreEqual(expected.AsSpan(0, length).ToArray(), EncodeWith((ref RLPWriter w) => w.TryWriteUInt256(number), length), $"value {value}");
        }
    }

    [TestMethod]
    public void UInt256_Output_AlwaysDecodesBackAsTheSameCanonicalInteger()
    {
        foreach (var value in BoundaryValues())
        {
            var encoded = EncodeWith((ref RLPWriter w) => w.TryWriteUInt256(new uint256(Padded32(value))), RlpReference.Encode(RlpNode.Bytes(Minimal(value))).Length);
            var item = DecodeOk(encoded);
            Assert.AreEqual(RLPStatus.Success, item.TryGetUInt256(out var decoded), value.ToString());
            CollectionAssert.AreEqual(Padded32(value), decoded.ToBigEndianBytes(), value.ToString());
        }
    }

    // ------------------------------------------------------------------ Hash32 and Address wire format

    [TestMethod]
    public void Hash32_WritesA0Prefix_ThenTheBytes_EvenWhenLeadingZero()
    {
        foreach (var first in new byte[] { 0x00, 0x01, 0x7f, 0x80, 0xff })
        {
            var payload = Pattern(32, first);
            var hash = new Hash32(payload);
            var expected = RlpReference.Concatenate([0xa0], payload);
            CollectionAssert.AreEqual(expected, EncodeWith((ref RLPWriter w) => w.TryWriteHash32(hash), 33), $"{first:x2}");
        }

        var zero = EncodeWith((ref RLPWriter w) => w.TryWriteHash32(default), 33);
        CollectionAssert.AreEqual(RlpReference.Concatenate([0xa0], new byte[32]), zero);
    }

    [TestMethod]
    public void Address_WritesEvmAs94Prefix_ThenTwentyBytes()
    {
        foreach (var first in new byte[] { 0x00, 0x01, 0x7f, 0x80, 0xff })
        {
            var payload = Pattern(20, first);
            var address = Address.FromEvmBytes(payload);
            var expected = RlpReference.Concatenate([0x94], payload);
            CollectionAssert.AreEqual(expected, EncodeWith((ref RLPWriter w) => w.TryWriteAddress(address), 21), $"{first:x2}");
        }

        var zero = EncodeWith((ref RLPWriter w) => w.TryWriteAddress(default), 21);
        CollectionAssert.AreEqual(RlpReference.Concatenate([0x94], new byte[20]), zero);
    }

    [TestMethod]
    public void EmptyTo_IsWrittenWithTryWriteByteString_AndReadsBackAsEmptyOptionalAddress()
    {
        var encoded = EncodeWith((ref RLPWriter w) => w.TryWriteByteString(default), 1);
        CollectionAssert.AreEqual(new byte[] { 0x80 }, encoded);
        Assert.AreEqual(RLPStatus.Success, DecodeOk(encoded).TryGetOptionalAddress(out _, out var isEmpty));
        Assert.IsTrue(isEmpty);
    }

    // ------------------------------------------------------------------ atomicity and state rules

    [TestMethod]
    public void DestinationTooSmall_LeavesBufferAndWriterUntouched_AndALaterWriteWorks()
    {
        foreach (var (name, action, size) in LargeWrites)
        {
            var destination = Filled(size - 1);
            var writer = new RLPWriter(destination, new int[4]);

            Assert.AreEqual(RLPStatus.DestinationTooSmall, action(ref writer), name);
            Assert.IsTrue(IsFilled(destination), $"{name}: buffer untouched");
            Assert.AreEqual(RLPStatus.EmptyInput, writer.TryComplete(out _), $"{name}: writer untouched");

            // Retry with something that fits.
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(new uint256(7)), name);
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), name);
            Assert.AreEqual(1, written, name);
            Assert.AreEqual(7, destination[0], name);
            Assert.IsTrue(IsFilled(destination.AsSpan(1)), name);
        }
    }

    [TestMethod]
    public void EmptyDestination_IsDestinationTooSmall_ForEveryAdapter()
    {
        var writer = new RLPWriter([], new int[4]);
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryWriteUInt256(uint256.One));
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryWriteUInt256(default));
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryWriteHash32(SampleHash));
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryWriteAddress(SampleAddress));
        Assert.AreEqual(RLPStatus.EmptyInput, writer.TryComplete(out _));
    }

    [TestMethod]
    public void ExactlyFittingDestination_Succeeds_AndOneByteShortFails()
    {
        foreach (var (name, action, size) in LargeWrites)
        {
            var exact = Filled(size);
            var writer = new RLPWriter(exact, new int[4]);
            Assert.AreEqual(RLPStatus.Success, action(ref writer), name);
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), name);
            Assert.AreEqual(size, written, name);

            var shortDestination = Filled(size - 1);
            var shortWriter = new RLPWriter(shortDestination, new int[4]);
            Assert.AreEqual(RLPStatus.DestinationTooSmall, action(ref shortWriter), name);
        }
    }

    [TestMethod]
    public void ParentLimit_InsideAList_IsInvalidListBoundary_AndTheListStaysRepairable()
    {
        foreach (var (name, action, size) in LargeWrites)
        {
            var destination = Filled(size + 1 + 8);
            var writer = new RLPWriter(destination, new int[4]);
            var announced = size - 1;
            Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(announced), name);
            var afterHeader = destination.ToArray();

            Assert.AreEqual(RLPStatus.InvalidListBoundary, action(ref writer), name);
            CollectionAssert.AreEqual(afterHeader, destination, $"{name}: buffer untouched");
            Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryEndList(), $"{name}: no child bytes were counted");

            // Fill the announced bytes exactly with a string of announced - 1 payload bytes, then close.
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(Pattern(announced - 1, 0x40)), name);
            Assert.AreEqual(RLPStatus.Success, writer.TryEndList(), name);
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), name);
            Assert.AreEqual(1 + announced, written, name);
        }
    }

    [TestMethod]
    public void WrongWriterState_DefaultCompletedAndExtraRoot_IsReportedWithoutMutation()
    {
        foreach (var (name, action, size) in LargeWrites)
        {
            // default(RLPWriter)
            var invalid = default(RLPWriter);
            Assert.AreEqual(RLPStatus.WriterStateInvalid, action(ref invalid), $"{name}: default");

            // A second root.
            var destination = Filled(size + 8);
            var writer = new RLPWriter(destination, new int[4]);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(1), name);
            var afterRoot = destination.ToArray();
            Assert.AreEqual(RLPStatus.ExtraRoot, action(ref writer), $"{name}: extra root");
            CollectionAssert.AreEqual(afterRoot, destination, $"{name}: extra root left the buffer");
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), name);
            Assert.AreEqual(1, written, name);

            // After completion.
            Assert.AreEqual(RLPStatus.WriterStateInvalid, action(ref writer), $"{name}: completed");
            CollectionAssert.AreEqual(afterRoot, destination, $"{name}: completed left the buffer");
        }
    }

    [TestMethod]
    public void SolanaAddress_IsInvalidArgument_WriterUntouched_AndAnEvmRetryWorks()
    {
        var destination = Filled(40);
        var writer = new RLPWriter(destination, new int[4]);

        Assert.AreEqual(RLPStatus.InvalidArgument, writer.TryWriteAddress(SampleSolana));
        Assert.AreEqual(RLPStatus.InvalidArgument, writer.TryWriteAddress(Address.ZeroSolana));
        Assert.IsTrue(IsFilled(destination));
        Assert.AreEqual(RLPStatus.EmptyInput, writer.TryComplete(out _), "no root was started");

        Assert.AreEqual(RLPStatus.Success, writer.TryWriteAddress(SampleAddress));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(21, written);
        Assert.IsTrue(IsFilled(destination.AsSpan(21)));
    }

    [TestMethod]
    public void SolanaAddress_KeepsTheWritersOwnStateOrder()
    {
        // State and root checks come before the argument check, exactly as in the core writer.
        var invalid = default(RLPWriter);
        Assert.AreEqual(RLPStatus.WriterStateInvalid, invalid.TryWriteAddress(SampleSolana));

        var destination = Filled(40);
        var writer = new RLPWriter(destination, new int[4]);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(1));
        Assert.AreEqual(RLPStatus.ExtraRoot, writer.TryWriteAddress(SampleSolana));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out _));
        Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryWriteAddress(SampleSolana));
    }

    [TestMethod]
    public void SolanaAddress_ArgumentErrorComesBeforeCapacityAndParentLimit()
    {
        // Destination too small and parent list full: a Solana address still reports InvalidArgument, untouched.
        var tiny = Filled(3);
        var tinyWriter = new RLPWriter(tiny, new int[4]);
        Assert.AreEqual(RLPStatus.InvalidArgument, tinyWriter.TryWriteAddress(SampleSolana));
        Assert.IsTrue(IsFilled(tiny));
        Assert.AreEqual(RLPStatus.DestinationTooSmall, tinyWriter.TryWriteAddress(SampleAddress));
        Assert.IsTrue(IsFilled(tiny));

        var destination = Filled(40);
        var writer = new RLPWriter(destination, new int[4]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(1));
        var afterHeader = destination.ToArray();
        Assert.AreEqual(RLPStatus.InvalidArgument, writer.TryWriteAddress(SampleSolana));
        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteAddress(SampleAddress));
        CollectionAssert.AreEqual(afterHeader, destination);
    }

    [TestMethod]
    public void SolanaAddress_InsideAList_ChangesNothing()
    {
        var destination = Filled(40);
        var writer = new RLPWriter(destination, new int[4]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(21));
        var afterHeader = destination.ToArray();

        Assert.AreEqual(RLPStatus.InvalidArgument, writer.TryWriteAddress(SampleSolana));
        CollectionAssert.AreEqual(afterHeader, destination);
        Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryEndList());

        Assert.AreEqual(RLPStatus.Success, writer.TryWriteAddress(SampleAddress));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(22, written);
    }

    [TestMethod]
    public void MultipleItemsInOneList_MatchTheOracle()
    {
        var children = new[]
        {
            RlpNode.Bytes(Minimal(0)),
            RlpNode.Bytes(Minimal(0x100)),
            RlpNode.Bytes(Pattern(32, 0xee)),
            RlpNode.Bytes(SampleAddress.ToBytes()),
            RlpNode.Bytes(Minimal(TwoPow256 - 1)),
        };
        var expected = RlpReference.Encode(RlpNode.ListOf(children));

        var destination = Filled(expected.Length + 4);
        var writer = new RLPWriter(destination, new int[4]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(expected.Length - 2));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(default));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(new uint256(0x100)));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteHash32(SampleHash));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteAddress(SampleAddress));
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(uint256.MaxValue));
        Assert.AreEqual(RLPStatus.Success, writer.TryEndList());
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(expected.Length, written);
        CollectionAssert.AreEqual(expected, destination.AsSpan(0, written).ToArray());
    }
}
