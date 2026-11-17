using System.Numerics;
using System.Runtime.InteropServices;
using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.EvmSignatureOracle;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.TypedAdapterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// The sizing helpers a framed-writer caller uses to announce exact list lengths. Each one must equal, byte for byte, what
/// the matching writer emits (and what the oracle encodes), over every width boundary.
/// </summary>
[TestClass]
public sealed class EncodedLengthSizingTests
{
    private static List<ulong> UInt64Probes()
    {
        var values = new HashSet<ulong> { 0, 1, 2, 0x7e, 0x7f, 0x80, 0x81, 0xff, 0x100, ulong.MaxValue - 1, ulong.MaxValue };
        for (var bit = 0; bit < 64; bit++)
        {
            values.Add(1UL << bit);
            values.Add((1UL << bit) - 1);
            values.Add((1UL << bit) + 1);
        }

        for (var bytes = 1; bytes <= 8; bytes++)
        {
            var power = bytes == 8 ? 0UL : 1UL << (8 * bytes);
            values.Add(power - 1);
            values.Add(power);
            values.Add(power + 1);
        }

        var rng = new Random(20261121);
        for (var i = 0; i < 2_000; i++)
        {
            values.Add((ulong)rng.NextInt64() >> rng.Next(0, 64));
        }

        return values.OrderBy(v => v).ToList();
    }

    // ------------------------------------------------------------------ RLPEncoder.GetUInt64EncodedLength

    [TestMethod]
    public void GetUInt64EncodedLength_EqualsEncoderWriterAndOracle_OnEveryBoundary()
    {
        var scratch = new byte[16];
        foreach (var value in UInt64Probes())
        {
            var length = RLPEncoder.GetUInt64EncodedLength(value);
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUInt64(value, scratch, out var written), $"value {value}");
            Assert.AreEqual(written, length, $"encoder, value {value}");
            Assert.AreEqual(RlpReference.Encode(RlpNode.Bytes(Minimal(value))).Length, length, $"oracle, value {value}");

            var destination = Filled(length + 2);
            var writer = new RLPWriter(destination, new int[2]);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(value), $"value {value}");
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var writerWritten), $"value {value}");
            Assert.AreEqual(writerWritten, length, $"writer, value {value}");
        }
    }

    [TestMethod]
    public void GetUInt64EncodedLength_NamedValues()
    {
        var rows = new (ulong Value, int Length)[]
        {
            (0, 1), (1, 1), (0x7f, 1), (0x80, 2), (0xff, 2), (0x100, 3), (0xffff, 3), (0x10000, 4), (uint.MaxValue, 5),
            (1UL << 32, 6), ((1UL << 56) - 1, 8), (1UL << 56, 9), (ulong.MaxValue, 9),
        };

        foreach (var (value, length) in rows)
        {
            Assert.AreEqual(length, RLPEncoder.GetUInt64EncodedLength(value), value.ToString());
        }
    }

    // ------------------------------------------------------------------ RLPEncoder.TryGetUnsignedMagnitudeLength

    private static ReadOnlySpan<byte> FakeLengthSpanWithFirstByte(int length)
    {
        // Backing array of 8 bytes starting with 0x01; the sizing code reads only the first byte and the length.
        var backing = new byte[8];
        backing[0] = 0x01;
        return MemoryMarshal.CreateReadOnlySpan(ref MemoryMarshal.GetArrayDataReference(backing), length);
    }

    [TestMethod]
    public void UnsignedMagnitudeLength_EmptyIsZero_SizeOne()
    {
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetUnsignedMagnitudeLength([], out var length));
        Assert.AreEqual(1, length);
    }

    [TestMethod]
    public void UnsignedMagnitudeLength_LeadingZero_IsNonCanonicalInteger_WithLengthZero()
    {
        foreach (var magnitude in new byte[][] { new byte[] { 0x00 }, [0x00, 0x01], [0x00, 0x00], Pattern(32, 0), Pattern(300, 0) })
        {
            var length = 123;
            Assert.AreEqual(RLPStatus.NonCanonicalInteger, RLPEncoder.TryGetUnsignedMagnitudeLength(magnitude, out length), Convert.ToHexString(magnitude));
            Assert.AreEqual(0, length, Convert.ToHexString(magnitude));

            // The writer rejects the same inputs with the same status.
            var destination = new byte[400];
            Assert.AreEqual(RLPStatus.NonCanonicalInteger, RLPEncoder.TryWriteUnsignedMagnitude(magnitude, destination, out _), Convert.ToHexString(magnitude));
        }
    }

    [TestMethod]
    public void UnsignedMagnitudeLength_EqualsEncoderWriterAndOracle_OnEveryWidth()
    {
        var magnitudes = new List<byte[]> { new byte[] { 0x01 }, new byte[] { 0x7f }, new byte[] { 0x80 }, new byte[] { 0xff } };
        foreach (var width in new[] { 2, 3, 8, 9, 31, 32, 33, 54, 55, 56, 57, 58, 255, 256, 257, 300, 1000, 65535, 65536, 70000 })
        {
            foreach (var first in new byte[] { 0x01, 0x7f, 0x80, 0xff })
            {
                magnitudes.Add(Pattern(width, first));
            }
        }

        var destination = new byte[70_100];
        foreach (var magnitude in magnitudes)
        {
            var context = $"width {magnitude.Length} first {magnitude[0]:x2}";
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetUnsignedMagnitudeLength(magnitude, out var length), context);
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUnsignedMagnitude(magnitude, destination, out var written), context);
            Assert.AreEqual(written, length, $"encoder, {context}");
            Assert.AreEqual(RlpReference.Encode(RlpNode.Bytes(magnitude)).Length, length, $"oracle, {context}");

            var viaWriter = Filled(length + 1);
            var writer = new RLPWriter(viaWriter, new int[2]);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUnsignedMagnitude(magnitude), context);
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var writerWritten), context);
            Assert.AreEqual(writerWritten, length, $"writer, {context}");
        }
    }

    [TestMethod]
    public void UnsignedMagnitudeLength_LengthOverflowBoundary_MatchesTheByteStringSizing()
    {
        // header for a length near int.MaxValue is 5 bytes (prefix + 4 length bytes): total = length + 5.
        var fits = FakeLengthSpanWithFirstByte(int.MaxValue - 5);
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetUnsignedMagnitudeLength(fits, out var length));
        Assert.AreEqual(int.MaxValue, length);

        var overflows = FakeLengthSpanWithFirstByte(int.MaxValue - 4);
        length = 77;
        Assert.AreEqual(RLPStatus.LengthOverflow, RLPEncoder.TryGetUnsignedMagnitudeLength(overflows, out length));
        Assert.AreEqual(0, length);

        length = 77;
        Assert.AreEqual(RLPStatus.LengthOverflow, RLPEncoder.TryGetUnsignedMagnitudeLength(FakeLengthSpanWithFirstByte(int.MaxValue), out length));
        Assert.AreEqual(0, length);

        // The generic byte-string sizing agrees at the same boundary.
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetByteStringLength(fits, out length));
        Assert.AreEqual(RLPStatus.LengthOverflow, RLPEncoder.TryGetByteStringLength(overflows, out _));
    }

    [TestMethod]
    public void UnsignedMagnitudeLength_ByteBoundaryOfTheLiteral_OneByteBelow0x80IsOne_AboveIsTwo()
    {
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetUnsignedMagnitudeLength([0x7f], out var length));
        Assert.AreEqual(1, length);
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetUnsignedMagnitudeLength([0x80], out length));
        Assert.AreEqual(2, length);
    }

    // ------------------------------------------------------------------ typed constants and GetUInt256EncodedLength

    [TestMethod]
    public void AddressAndHash32EncodedLength_EqualTheWriterOutput()
    {
        Assert.AreEqual(21, RLPTypedExtensions.AddressEncodedLength);
        Assert.AreEqual(33, RLPTypedExtensions.Hash32EncodedLength);

        var addressDestination = Filled(RLPTypedExtensions.AddressEncodedLength + 2);
        var addressWriter = new RLPWriter(addressDestination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, addressWriter.TryWriteAddress(Address.FromEvmBytes(Pattern(20, 0x00))));
        Assert.AreEqual(RLPStatus.Success, addressWriter.TryComplete(out var written));
        Assert.AreEqual(RLPTypedExtensions.AddressEncodedLength, written);

        var hashDestination = Filled(RLPTypedExtensions.Hash32EncodedLength + 2);
        var hashWriter = new RLPWriter(hashDestination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, hashWriter.TryWriteHash32(new Hash32(Pattern(32, 0x00))));
        Assert.AreEqual(RLPStatus.Success, hashWriter.TryComplete(out written));
        Assert.AreEqual(RLPTypedExtensions.Hash32EncodedLength, written);

        Assert.AreEqual(21, RlpReference.Encode(RlpNode.Bytes(Pattern(20, 0x00))).Length);
        Assert.AreEqual(33, RlpReference.Encode(RlpNode.Bytes(Pattern(32, 0x00))).Length);
    }

    private static void AssertUInt256LengthEqualsWriterAndOracle(BigInteger value, string context)
    {
        var number = new uint256(Padded32(value));
        var length = RLPTypedExtensions.GetUInt256EncodedLength(number);
        Assert.AreEqual(RlpReference.Encode(RlpNode.Bytes(Minimal(value))).Length, length, $"oracle, {context}");

        var destination = Filled(length + 2);
        var writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(number), context);
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), context);
        Assert.AreEqual(written, length, $"writer, {context}");
    }

    [TestMethod]
    public void GetUInt256EncodedLength_EveryByteWidth0To32_EqualsWriterAndOracle()
    {
        AssertUInt256LengthEqualsWriterAndOracle(0, "zero");
        for (var width = 1; width <= 32; width++)
        {
            var top = BigInteger.One << (8 * (width - 1));
            var max = (BigInteger.One << (8 * width)) - 1;
            foreach (var value in new[] { top, top + 1, max, max - 1, (top * 0x7f), top * 0x80 })
            {
                if (value >= TwoPow256 || value.IsZero || Minimal(value).Length != width)
                {
                    continue;
                }

                AssertUInt256LengthEqualsWriterAndOracle(value, $"width {width} value {value}");
            }
        }
    }

    [TestMethod]
    public void GetUInt256EncodedLength_BoundaryValuesAndSingleByteLiteralEdge()
    {
        foreach (var value in BoundaryValues())
        {
            AssertUInt256LengthEqualsWriterAndOracle(value, value.ToString());
        }

        Assert.AreEqual(1, RLPTypedExtensions.GetUInt256EncodedLength(default));
        Assert.AreEqual(1, RLPTypedExtensions.GetUInt256EncodedLength(new uint256(1)));
        Assert.AreEqual(1, RLPTypedExtensions.GetUInt256EncodedLength(new uint256(0x7f)));
        Assert.AreEqual(2, RLPTypedExtensions.GetUInt256EncodedLength(new uint256(0x80)));
        Assert.AreEqual(2, RLPTypedExtensions.GetUInt256EncodedLength(new uint256(0xff)));
        Assert.AreEqual(3, RLPTypedExtensions.GetUInt256EncodedLength(new uint256(0x100)));
        Assert.AreEqual(33, RLPTypedExtensions.GetUInt256EncodedLength(uint256.MaxValue));
    }

    [TestMethod]
    public void GetUInt256EncodedLength_RandomValues_EqualWriter()
    {
        var rng = new Random(20261122);
        for (var i = 0; i < 5_000; i++)
        {
            AssertUInt256LengthEqualsWriterAndOracle(RandomUInt256(rng), $"iteration {i}");
        }
    }

    // ------------------------------------------------------------------ TryGetEvmLegacyVEncodedLength

    [TestMethod]
    public void LegacyVEncodedLength_EqualsWriterAndOracle_ForRealChainsAndEveryWidthBoundary()
    {
        var chains = RealChainIds.Select(c => c.ChainId).Concat(WidthBoundaryChainIds()).Distinct().Select(c => (ulong?)c).Append(null);
        foreach (var chain in chains)
        {
            foreach (var parity in new byte[] { 0, 1 })
            {
                var context = $"chain {chain?.ToString() ?? "none"} parity {parity}";
                var length = -1;
                Assert.AreEqual(RLPStatus.Success, RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(parity, chain, out length), context);
                Assert.AreEqual(LegacyVItem(parity, chain).Length, length, $"oracle, {context}");

                var destination = Filled(length + 2);
                var writer = new RLPWriter(destination, new int[2]);
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteEvmLegacyV(parity, chain), context);
                Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), context);
                Assert.AreEqual(written, length, $"writer, {context}");
            }
        }
    }

    [TestMethod]
    public void LegacyVEncodedLength_NamedValues()
    {
        var rows = new (ulong? Chain, byte Parity, int Length)[]
        {
            (null, 0, 1), (null, 1, 1), (0, 0, 1), (1, 1, 1), (46, 0, 1), (46, 1, 2), (47, 0, 2), (110, 0, 2), (110, 1, 3),
            (137, 0, 3), (8453, 0, 3), (42161, 1, 4), (98866, 0, 4), (98866, 1, 4), (ulong.MaxValue, 0, 10), (ulong.MaxValue, 1, 10),
        };

        foreach (var (chain, parity, length) in rows)
        {
            Assert.AreEqual(RLPStatus.Success, RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(parity, chain, out var actual));
            Assert.AreEqual(length, actual, $"chain {chain?.ToString() ?? "none"} parity {parity}");
        }
    }

    [TestMethod]
    public void LegacyVEncodedLength_ParityAbove1_IsInvalidSignatureV_WithLengthZero()
    {
        foreach (var parity in new byte[] { 2, 3, 27, 28, 127, 128, 255 })
        {
            foreach (var chain in new ulong?[] { null, 0, 1, 98866, ulong.MaxValue })
            {
                var length = 55;
                Assert.AreEqual(RLPStatus.InvalidSignatureV, RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(parity, chain, out length), $"parity {parity} chain {chain?.ToString() ?? "none"}");
                Assert.AreEqual(0, length);
            }
        }
    }

    // ------------------------------------------------------------------ the sizers announce an exact list

    [TestMethod]
    public void SizersAnnounceAnExactLegacyTransactionList_AndTheWriterClosesIt()
    {
        foreach (var (_, chain) in RealChainIds)
        {
            foreach (var parity in new byte[] { 0, 1 })
            {
                var nonce = 7UL;
                var gasPrice = new uint256(30_000_000_000UL);
                var gasLimit = 21_000UL;
                var value = new uint256(Padded32(BigInteger.Pow(10, 18)));
                var r = new uint256(Padded32((BigInteger.One << 250) + 3));
                var s = new uint256(Padded32((BigInteger.One << 200) + 9));
                Assert.AreEqual(RLPStatus.Success, RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(parity, chain, out var vLength));

                var childrenLength = RLPEncoder.GetUInt64EncodedLength(nonce)
                    + RLPTypedExtensions.GetUInt256EncodedLength(gasPrice)
                    + RLPEncoder.GetUInt64EncodedLength(gasLimit)
                    + RLPTypedExtensions.AddressEncodedLength
                    + RLPTypedExtensions.GetUInt256EncodedLength(value)
                    + 1 // empty data
                    + vLength
                    + RLPTypedExtensions.GetUInt256EncodedLength(r)
                    + RLPTypedExtensions.GetUInt256EncodedLength(s);
                Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(childrenLength, out var total));

                var destination = Filled(total + 4);
                var writer = new RLPWriter(destination, new int[4]);
                var context = $"chain {chain} parity {parity}";
                Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(childrenLength), context);
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(nonce), context);
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(gasPrice), context);
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(gasLimit), context);
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteAddress(Address.FromEvmBytes(Pattern(20, 0x35))), context);
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(value), context);
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(default), context);
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteEvmLegacyV(parity, chain), context);
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(r), context);
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(s), context);
                Assert.AreEqual(RLPStatus.Success, writer.TryEndList(), $"{context}: the announced length is exact");
                Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), context);
                Assert.AreEqual(total, written, context);
            }
        }
    }
}
