using System.Numerics;
using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.TypedAdapterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Seeded differential tests of the typed reader adapters. Expected statuses and values are derived here from the
/// spec rules and <see cref="BigInteger"/>, independently of the adapters. A failing message carries the seed.
/// </summary>
[TestClass]
public sealed class TypedAdapterDifferentialTests
{
    private const int RandomCount = 20_000;
    private const int BaseSeed = 20261117;

    /// <summary>Random payload of length 0..40 (biased toward the interesting widths) with a quarter of them starting with zero.</summary>
    private static byte[] RandomPayload(Random rng)
    {
        var length = rng.Next(4) switch
        {
            0 => rng.Next(0, 41),
            1 => rng.Next(30, 35),
            2 => rng.Next(18, 23),
            _ => rng.Next(0, 10),
        };
        var payload = new byte[length];
        rng.NextBytes(payload);
        if (length > 0 && rng.Next(4) == 0)
        {
            payload[0] = 0;
        }

        if (rng.Next(16) == 0)
        {
            Array.Clear(payload); // all-zero payloads, including the 20-byte zero address and 32-byte zero hash
        }

        return payload;
    }

    private static RLPStatus ExpectedInteger(byte[] payload, int maxWidth)
    {
        if (payload.Length == 0)
        {
            return RLPStatus.Success;
        }

        if (payload[0] == 0)
        {
            return RLPStatus.NonCanonicalInteger;
        }

        return payload.Length > maxWidth ? RLPStatus.IntegerOverflow : RLPStatus.Success;
    }

    private static RLPStatus ExpectedFixed(byte[] payload, int width) =>
        payload.Length == width ? RLPStatus.Success : RLPStatus.InvalidFixedLength;

    [TestMethod]
    public void RandomPayloads_EveryReaderMatchesTheRules()
    {
        for (var iteration = 0; iteration < RandomCount; iteration++)
        {
            var seed = BaseSeed + iteration;
            var rng = new Random(seed);
            var payload = RandomPayload(rng);
            var item = StringItem(payload);
            var context = $"seed {seed} payload {Convert.ToHexString(payload)}";

            var expectedInteger = ExpectedInteger(payload, 32);
            Assert.AreEqual(expectedInteger, item.TryGetUInt256(out var number), context);
            if (expectedInteger == RLPStatus.Success)
            {
                var expected = payload.Length == 0 ? BigInteger.Zero : new BigInteger(payload, isUnsigned: true, isBigEndian: true);
                CollectionAssert.AreEqual(Padded32(expected), number.ToBigEndianBytes(), context);
            }
            else
            {
                Assert.AreEqual(default, number, context);
            }

            // The 64-bit accessor and the 256-bit accessor agree wherever both are defined.
            var narrow = item.TryGetUInt64(out var small);
            Assert.AreEqual(ExpectedInteger(payload, 8), narrow, context);
            if (narrow == RLPStatus.Success)
            {
                Assert.AreEqual(small, number.U0, context);
                Assert.AreEqual(0UL, number.U1 | number.U2 | number.U3, context);
            }

            Assert.AreEqual(ExpectedFixed(payload, 32), item.TryGetHash32(out var hash), context);
            if (payload.Length == 32)
            {
                var written = new byte[32];
                hash.WriteBigEndian(written);
                CollectionAssert.AreEqual(payload, written, context);
            }
            else
            {
                Assert.AreEqual(default, hash, context);
            }

            Assert.AreEqual(ExpectedFixed(payload, 20), item.TryGetAddress(out var address), context);
            if (payload.Length == 20)
            {
                CollectionAssert.AreEqual(payload, address.ToBytes(), context);
            }
            else
            {
                Assert.AreEqual(default, address, context);
            }

            var optionalStatus = item.TryGetOptionalAddress(out var optional, out var isEmpty);
            Assert.AreEqual(payload.Length == 0 ? RLPStatus.Success : ExpectedFixed(payload, 20), optionalStatus, context);
            Assert.AreEqual(payload.Length == 0, isEmpty, context);
            Assert.AreEqual(payload.Length == 20 ? address : default, optional, context);

            var selectorStatus = item.TryGetFunctionSelector(out var selector);
            Assert.AreEqual(payload.Length >= 4 ? RLPStatus.Success : RLPStatus.InvalidFixedLength, selectorStatus, context);
            if (payload.Length >= 4)
            {
                var written = new byte[4];
                selector.WriteBytes(written);
                CollectionAssert.AreEqual(payload.AsSpan(0, 4).ToArray(), written, context);
            }
            else
            {
                Assert.AreEqual(default, selector, context);
            }
        }
    }

    [TestMethod]
    public void RandomSignatureFields_MatchTheRulesOfBothReaders()
    {
        for (var iteration = 0; iteration < RandomCount; iteration++)
        {
            var seed = BaseSeed + 1_000_000 + iteration;
            var rng = new Random(seed);
            var v = RandomPayload(rng);
            var r = RandomPayload(rng);
            var s = RandomPayload(rng);
            var context = $"seed {seed} v {Convert.ToHexString(v)} r {Convert.ToHexString(r)} s {Convert.ToHexString(s)}";

            // The scalars decide only when v is accepted; v decides first.
            var scalarStatus = ExpectedInteger(r, 32);
            if (scalarStatus == RLPStatus.Success)
            {
                scalarStatus = ExpectedInteger(s, 32);
            }

            var legacy = EvmSignatureOracle.ExpectLegacyVPayload(v);
            var legacyExpected = legacy.Status != RLPStatus.Success ? legacy.Status : scalarStatus;
            var legacyStatus = RLPTypedExtensions.TryGetEvmLegacySignature(StringItem(v), StringItem(r), StringItem(s), out var signature, out var chainId);
            Assert.AreEqual(legacyExpected, legacyStatus, context);
            if (legacyStatus == RLPStatus.Success)
            {
                Assert.AreEqual(legacy.ChainId, chainId, context);
                Assert.AreEqual((byte)legacy.Parity, signature.V, context);
                Assert.AreEqual(new Hash32(Padded32(new BigInteger(r, isUnsigned: true, isBigEndian: true))), signature.R, context);
                Assert.AreEqual(new Hash32(Padded32(new BigInteger(s, isUnsigned: true, isBigEndian: true))), signature.S, context);
            }
            else
            {
                Assert.AreEqual(default, signature, context);
                Assert.IsNull(chainId, context);
            }

            var typedV = EvmSignatureOracle.ExpectYParityPayload(v);
            var typedExpected = typedV != RLPStatus.Success ? typedV : scalarStatus;
            var typedStatus = RLPTypedExtensions.TryGetEvmYParitySignature(StringItem(v), StringItem(r), StringItem(s), out signature);
            Assert.AreEqual(typedExpected, typedStatus, context);
            if (typedStatus == RLPStatus.Success)
            {
                Assert.AreEqual(v.Length == 0 ? (byte)0 : (byte)1, signature.V, context);
            }
            else
            {
                Assert.AreEqual(default, signature, context);
            }
        }
    }

    [TestMethod]
    public void RandomUInt256Values_DecodeExactly()
    {
        var rng = new Random(BaseSeed + 2_000_000);
        for (var iteration = 0; iteration < RandomCount; iteration++)
        {
            var expected = RandomUInt256(rng);
            Assert.AreEqual(RLPStatus.Success, StringItem(Minimal(expected)).TryGetUInt256(out var value), $"iteration {iteration}");
            Assert.AreEqual(expected, new BigInteger(value.ToBigEndianBytes(), isUnsigned: true, isBigEndian: true), $"iteration {iteration}");
        }
    }
}
