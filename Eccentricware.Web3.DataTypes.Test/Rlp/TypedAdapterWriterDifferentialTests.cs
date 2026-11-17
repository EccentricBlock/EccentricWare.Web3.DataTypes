using System.Numerics;
using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.TypedAdapterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Seeded write -> decode round trips of the typed adapters, compared with the oracle encoding and with
/// <see cref="BigInteger"/>-derived minimal magnitudes. A failing message carries the seed.
/// </summary>
[TestClass]
public sealed class TypedAdapterWriterDifferentialTests
{
    private const int RandomCount = 20_000;
    private const int BaseSeed = 20261119;

    [TestMethod]
    public void RandomUInt256_WriterEqualsOracle_AndRoundTrips()
    {
        for (var iteration = 0; iteration < RandomCount; iteration++)
        {
            var seed = BaseSeed + iteration;
            var value = RandomUInt256(new Random(seed));
            var expected = RlpReference.Encode(RlpNode.Bytes(Minimal(value)));
            var number = new uint256(Padded32(value));

            var destination = Filled(expected.Length);
            var writer = new RLPWriter(destination, new int[2]);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(number), $"seed {seed}");
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), $"seed {seed}");
            Assert.AreEqual(expected.Length, written, $"seed {seed}");
            CollectionAssert.AreEqual(expected, destination, $"seed {seed} value {value}");

            Assert.AreEqual(RLPStatus.Success, DecodeOk(destination).TryGetUInt256(out var decoded), $"seed {seed}");
            Assert.AreEqual(number, decoded, $"seed {seed}");
        }
    }

    [TestMethod]
    public void RandomHash32AndAddress_RoundTripThroughAList()
    {
        for (var iteration = 0; iteration < RandomCount; iteration++)
        {
            var seed = BaseSeed + 5_000_000 + iteration;
            var rng = new Random(seed);
            var hashBytes = new byte[32];
            var addressBytes = new byte[20];
            rng.NextBytes(hashBytes);
            rng.NextBytes(addressBytes);
            var value = RandomUInt256(rng);
            var hash = new Hash32(hashBytes);
            var address = Address.FromEvmBytes(addressBytes);
            var number = new uint256(Padded32(value));

            var expected = RlpReference.Encode(RlpNode.ListOf(RlpNode.Bytes(Minimal(value)), RlpNode.Bytes(hashBytes), RlpNode.Bytes(addressBytes)));
            var childrenLength = RlpReference.PayloadLength((RlpList)RlpNode.ListOf(RlpNode.Bytes(Minimal(value)), RlpNode.Bytes(hashBytes), RlpNode.Bytes(addressBytes)));

            var destination = Filled(expected.Length);
            var writer = new RLPWriter(destination, new int[2]);
            Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(childrenLength), $"seed {seed}");
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(number), $"seed {seed}");
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteHash32(hash), $"seed {seed}");
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteAddress(address), $"seed {seed}");
            Assert.AreEqual(RLPStatus.Success, writer.TryEndList(), $"seed {seed}");
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), $"seed {seed}");
            Assert.AreEqual(expected.Length, written, $"seed {seed}");
            CollectionAssert.AreEqual(expected, destination, $"seed {seed}");

            var root = DecodeOk(destination);
            Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children), $"seed {seed}");
            Assert.IsTrue(children.MoveNext(), $"seed {seed}");
            Assert.AreEqual(RLPStatus.Success, children.Current.TryGetUInt256(out var numberBack), $"seed {seed}");
            Assert.AreEqual(number, numberBack, $"seed {seed}");
            Assert.IsTrue(children.MoveNext(), $"seed {seed}");
            Assert.AreEqual(RLPStatus.Success, children.Current.TryGetHash32(out var hashBack), $"seed {seed}");
            Assert.AreEqual(hash, hashBack, $"seed {seed}");
            Assert.IsTrue(children.MoveNext(), $"seed {seed}");
            Assert.AreEqual(RLPStatus.Success, children.Current.TryGetAddress(out var addressBack), $"seed {seed}");
            Assert.AreEqual(address, addressBack, $"seed {seed}");
            Assert.IsFalse(children.MoveNext(), $"seed {seed}");
        }
    }

    [TestMethod]
    public void RandomValuesFitting64Bits_EqualEncoderUInt64()
    {
        var expected = new byte[16];
        for (var iteration = 0; iteration < RandomCount; iteration++)
        {
            var seed = BaseSeed + 9_000_000 + iteration;
            var rng = new Random(seed);
            var value = (ulong)rng.NextInt64() >> rng.Next(0, 64);
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUInt64(value, expected, out var length), $"seed {seed}");

            var destination = Filled(length);
            var writer = new RLPWriter(destination, new int[2]);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(new uint256(value)), $"seed {seed}");
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out _), $"seed {seed}");
            CollectionAssert.AreEqual(expected.AsSpan(0, length).ToArray(), destination, $"seed {seed} value {value}");
        }
    }
}
