using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>Sizing and scalar encoding against hand-stated vectors (spec 2.3, 7.1, 7.2, 12.1) and boundary tables.</summary>
[TestClass]
public sealed class EncoderVectorTests
{
    // ------------------------------------------------------------------ sizing

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(2, 3)]
    [DataRow(54, 55)]
    [DataRow(55, 56)]
    [DataRow(56, 58)]
    [DataRow(57, 59)]
    [DataRow(254, 256)]
    [DataRow(255, 257)]
    [DataRow(256, 259)]
    [DataRow(65535, 65538)]
    [DataRow(65536, 65540)]
    [DataRow(16777215, 16777219)]
    [DataRow(16777216, 16777221)]
    public void TryGetByteStringLength_AtEveryHeaderBoundary(int payloadLength, int expectedTotal)
    {
        // First byte 0xff so the one-byte literal rule never applies at length 1.
        var payload = Repeat(0xff, Math.Min(payloadLength, 70_000));
        var span = payloadLength <= 70_000 ? payload : FakeLengthSpan(payloadLength);

        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetByteStringLength(span, out var total));

        Assert.AreEqual(expectedTotal, total);
    }

    [TestMethod]
    public void TryGetByteStringLength_OneByte_LiteralBelow0x80_OtherwiseTwo()
    {
        for (var b = 0; b < 256; b++)
        {
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetByteStringLength([(byte)b], out var total));
            Assert.AreEqual(b < 0x80 ? 1 : 2, total, $"{b:x2}");
        }
    }

    [TestMethod]
    public void TryGetByteStringLength_EmptyPayload_IsAValidEmptyString()
    {
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetByteStringLength([], out var total));
        Assert.AreEqual(1, total);
    }

    [TestMethod]
    public void TryGetByteStringLength_NearIntOverflow_IsSuccessThenLengthOverflow()
    {
        // Payloads of 2^24 or more carry a 5-byte header: int.MaxValue - 5 is the largest representable total.
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetByteStringLength(FakeLengthSpan(int.MaxValue - 5), out var total));
        Assert.AreEqual(int.MaxValue, total);

        foreach (var length in new[] { int.MaxValue - 4, int.MaxValue - 1, int.MaxValue })
        {
            Assert.AreEqual(RLPStatus.LengthOverflow, RLPEncoder.TryGetByteStringLength(FakeLengthSpan(length), out total), $"{length}");
            Assert.AreEqual(0, total, "totalLength is 0 on failure");
        }
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 2)]
    [DataRow(54, 55)]
    [DataRow(55, 56)]
    [DataRow(56, 58)]
    [DataRow(57, 59)]
    [DataRow(255, 257)]
    [DataRow(256, 259)]
    [DataRow(65535, 65538)]
    [DataRow(65536, 65540)]
    [DataRow(16777215, 16777219)]
    [DataRow(16777216, 16777221)]
    [DataRow(int.MaxValue - 5, int.MaxValue)]
    public void TryGetListLength_AtEveryHeaderBoundary(int childrenLength, int expectedTotal)
    {
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(childrenLength, out var total));

        Assert.AreEqual(expectedTotal, total);
    }

    [TestMethod]
    [DataRow(int.MaxValue - 4)]
    [DataRow(int.MaxValue - 1)]
    [DataRow(int.MaxValue)]
    public void TryGetListLength_TotalsAboveIntMaxValue_AreLengthOverflow(int childrenLength)
    {
        Assert.AreEqual(RLPStatus.LengthOverflow, RLPEncoder.TryGetListLength(childrenLength, out var total));
        Assert.AreEqual(0, total);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(-56)]
    [DataRow(int.MinValue)]
    public void TryGetListLength_NegativeLength_IsInvalidArgument(int childrenLength)
    {
        Assert.AreEqual(RLPStatus.InvalidArgument, RLPEncoder.TryGetListLength(childrenLength, out var total));
        Assert.AreEqual(0, total);
    }

    [TestMethod]
    public void SizingMatchesTheOracle_ForEveryLengthUpTo600()
    {
        for (var length = 0; length <= 600; length++)
        {
            var payload = Repeat(0xab, length);
            var node = RlpNode.Bytes(payload);

            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetByteStringLength(payload, out var stringTotal));
            Assert.AreEqual(RlpReference.Encode(node).Length, stringTotal, $"string {length}");

            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(length, out var listTotal));
            Assert.AreEqual(length + (length <= 55 ? 1 : length <= 255 ? 2 : 3), listTotal, $"list {length}");
        }
    }

    // ------------------------------------------------------------------ hand-stated vectors

    [TestMethod]
    [DynamicData(nameof(RlpVectors.CanonicalRows), typeof(RlpVectors))]
    public void CanonicalVector_IsBuiltByTheWriter_ByteIdentically(CanonicalVector vector)
    {
        var built = BuildWithWriter(vector.Tree);

        CollectionAssert.AreEqual(vector.Encoded, built, vector.Name);
    }

    [TestMethod]
    [DynamicData(nameof(RlpVectors.CanonicalRows), typeof(RlpVectors))]
    public void CanonicalVector_SizesAndScalarEncodesToTheStatedBytes(CanonicalVector vector)
    {
        if (vector.Tree is RlpString text)
        {
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetByteStringLength(text.Value, out var total), vector.Name);
            Assert.AreEqual(vector.Encoded.Length, total, vector.Name);

            var destination = Filled(vector.Encoded.Length + 2);
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteByteString(text.Value, destination, out var written), vector.Name);
            Assert.AreEqual(vector.Encoded.Length, written, vector.Name);
            Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual(vector.Encoded), vector.Name);
            Assert.IsTrue(IsFilled(destination.AsSpan(written)), vector.Name);
        }
        else
        {
            var list = (RlpList)vector.Tree;
            var payloadLength = RlpReference.PayloadLength(list);
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(payloadLength, out var total), vector.Name);
            Assert.AreEqual(vector.Encoded.Length, total, vector.Name);
        }
    }

    [TestMethod]
    [DynamicData(nameof(RlpVectors.KnownEthereumRows), typeof(RlpVectors))]
    public void KnownEthereumVector_IsBuiltByTheWriter_ByteIdentically(KnownEthereumVector vector)
    {
        var built = BuildWithWriter(vector.Tree);

        CollectionAssert.AreEqual(vector.Encoded, built, $"{vector.Name} ({vector.Source})");
    }

    [TestMethod]
    public void AllLiteralBytes_00To7f_EncodeAsThemselves_AndOneByteStrings_80ToFf_GetThe81Prefix()
    {
        var destination = Filled(4);
        for (var b = 0; b < 256; b++)
        {
            Array.Fill(destination, Sentinel);
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteByteString([(byte)b], destination, out var written), $"{b:x2}");

            if (b < 0x80)
            {
                Assert.AreEqual(1, written, $"{b:x2}");
                Assert.AreEqual((byte)b, destination[0]);
                Assert.IsTrue(IsFilled(destination.AsSpan(1)));
            }
            else
            {
                Assert.AreEqual(2, written, $"{b:x2}");
                Assert.AreEqual((byte)0x81, destination[0]);
                Assert.AreEqual((byte)b, destination[1]);
                Assert.IsTrue(IsFilled(destination.AsSpan(2)));
            }
        }
    }

    [TestMethod]
    public void EmptyByteString_IsEighty_AndDiffersFromEmptyList()
    {
        var destination = Filled(2);

        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteByteString([], destination, out var written));

        Assert.AreEqual(1, written);
        Assert.AreEqual((byte)0x80, destination[0]);
        Assert.AreEqual(Sentinel, destination[1]);
    }

    [TestMethod]
    [DataRow(0, "80")]
    [DataRow(2, "82")]
    [DataRow(54, "b6")]
    [DataRow(55, "b7")]
    [DataRow(56, "b8 38")]
    [DataRow(57, "b8 39")]
    [DataRow(254, "b8 fe")]
    [DataRow(255, "b8 ff")]
    [DataRow(256, "b9 01 00")]
    [DataRow(65535, "b9 ff ff")]
    [DataRow(65536, "ba 01 00 00")]
    public void ByteStringPrefix_AtTheSpecLengths_IsExact(int length, string expectedPrefixHex)
    {
        var payload = Repeat(0xaa, length);
        var prefix = H(expectedPrefixHex);
        var destination = Filled(prefix.Length + length + 3);

        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteByteString(payload, destination, out var written));

        Assert.AreEqual(prefix.Length + length, written);
        Assert.IsTrue(destination.AsSpan(0, prefix.Length).SequenceEqual(prefix));
        Assert.IsTrue(destination.AsSpan(prefix.Length, length).SequenceEqual(payload));
        Assert.IsTrue(IsFilled(destination.AsSpan(written)));
    }

    [TestMethod]
    public void OneByteStringAbove0x7f_IsPrefixedWith81()
    {
        var destination = Filled(4);

        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteByteString([0xaa], destination, out var written));

        Assert.AreEqual(2, written);
        Assert.AreEqual("81 aa", RlpReference.ToHex(destination.AsSpan(0, written), true));
    }

    [TestMethod]
    public void ByteStringOf55Zeroes_IsB7Then55Zeroes_AndOf56IsB838()
    {
        var destination = Filled(70);

        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteByteString(new byte[55], destination, out var written));
        Assert.AreEqual(56, written);
        Assert.AreEqual((byte)0xb7, destination[0]);
        Assert.IsTrue(destination.AsSpan(1, 55).SequenceEqual(new byte[55]));

        Array.Fill(destination, Sentinel);
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteByteString(new byte[56], destination, out written));
        Assert.AreEqual(58, written);
        Assert.AreEqual((byte)0xb8, destination[0]);
        Assert.AreEqual((byte)0x38, destination[1]);
    }

    [TestMethod]
    public void ByteStringWriteOfLargeStrings_RoundTripsThroughTheStrictReader()
    {
        foreach (var length in new[] { 255, 256, 65535, 65536, 300_000 })
        {
            var payload = new byte[length];
            for (var i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i * 31 + 7);
            }

            var destination = new byte[length + 5];
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteByteString(payload, destination, out var written));

            var document = DecodeOk(destination.AsSpan(0, written).ToArray(), Limits(maxBytes: 2 * OneMebibyte));
            Assert.IsTrue(document.Payload.SequenceEqual(payload), $"length {length}");
        }
    }

    // ------------------------------------------------------------------ unsigned integers

    [TestMethod]
    [DataRow(0UL, "80")]
    [DataRow(1UL, "01")]
    [DataRow(127UL, "7f")]
    [DataRow(128UL, "81 80")]
    [DataRow(255UL, "81 ff")]
    [DataRow(256UL, "82 01 00")]
    [DataRow(1024UL, "82 04 00")]
    [DataRow(0xffffUL, "82 ff ff")]
    [DataRow(0x10000UL, "83 01 00 00")]
    [DataRow(0xdeadbeefUL, "84 de ad be ef")]
    [DataRow(1UL << 56, "88 01 00 00 00 00 00 00 00")]
    [DataRow(1UL << 63, "88 80 00 00 00 00 00 00 00")]
    [DataRow(ulong.MaxValue, "88 ff*8")]
    public void TryWriteUInt64_EncodesTheStatedBytes(ulong value, string hex)
    {
        var expected = H(hex);
        var destination = Filled(12);

        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUInt64(value, destination, out var written));

        Assert.AreEqual(expected.Length, written);
        Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual(expected));
        Assert.IsTrue(IsFilled(destination.AsSpan(written)));
    }

    [TestMethod]
    public void TryWriteUInt64_EveryByteWidth_AtBothEdgesOfTheWidth()
    {
        for (var width = 1; width <= 8; width++)
        {
            var low = 1UL << (8 * (width - 1));
            var high = width == 8 ? ulong.MaxValue : (1UL << (8 * width)) - 1;
            foreach (var value in new[] { low, low + 1, high - 1, high })
            {
                if (value < 0x80 && width > 1)
                {
                    continue;
                }

                var destination = Filled(12);
                Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUInt64(value, destination, out var written));

                var expected = RlpReference.Encode(RlpNode.FromUInt64(value));
                Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual(expected), $"{value:x}");
            }
        }
    }

    // ------------------------------------------------------------------ list lengths from genuine children

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(54)]
    [DataRow(55)]
    [DataRow(56)]
    [DataRow(57)]
    [DataRow(255)]
    [DataRow(256)]
    public void ListOfLiteralChildren_AtEveryPayloadBoundary_MatchesTheOracleAndTheSizing(int payloadLength)
    {
        var children = Enumerable.Range(0, payloadLength).Select(_ => RlpNode.FromUInt64(1)).ToArray();
        var tree = RlpNode.ListOf(children);
        var built = BuildWithWriter(tree);

        Assert.IsTrue(built.AsSpan().SequenceEqual(RlpReference.Encode(tree)));
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(payloadLength, out var total));
        Assert.AreEqual(built.Length, total);
    }
}
