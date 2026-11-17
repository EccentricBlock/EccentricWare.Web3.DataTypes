using System.Numerics;
using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.EvmSignatureOracle;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.TypedAdapterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Seeded sweeps of (chain id, parity) pairs compared with <see cref="EvmSignatureOracle"/> in both directions: the
/// writer's bytes against the oracle's, and the readers' result on oracle-built bytes. A failing message carries the seed.
/// </summary>
[TestClass]
public sealed class EvmSignatureOracleDifferentialTests
{
    private const int RandomCount = 20_000;
    private const int BaseSeed = 20261123;

    private static (byte Parity, ulong? Chain) RandomPair(Random rng)
    {
        var parity = (byte)rng.Next(0, 2);
        var kind = rng.Next(16);
        ulong? chain = kind switch
        {
            0 => null,
            1 => ulong.MaxValue - (ulong)rng.Next(0, 4),
            2 => (ulong)rng.Next(0, 300),
            _ => (ulong)rng.NextInt64() >> rng.Next(0, 64),
        };
        return (parity, chain);
    }

    [TestMethod]
    public void EveryRealChainId_BothDirections_MatchTheOracle()
    {
        foreach (var (name, chain) in RealChainIds)
        {
            foreach (byte parity in new byte[] { 0, 1 })
            {
                AssertBothDirections(parity, chain, $"{name} ({chain}) parity {parity}");
            }
        }

        AssertBothDirections(0, null, "no chain id parity 0");
        AssertBothDirections(1, null, "no chain id parity 1");
    }

    [TestMethod]
    public void RandomChainIdAndParity_WriteThenRead_AndReadOracleBytes()
    {
        for (var iteration = 0; iteration < RandomCount; iteration++)
        {
            var seed = BaseSeed + iteration;
            var (parity, chain) = RandomPair(new Random(seed));
            AssertBothDirections(parity, chain, $"seed {seed} chain {chain?.ToString() ?? "none"} parity {parity}");
        }
    }

    private static void AssertBothDirections(byte parity, ulong? chain, string context)
    {
        var expected = LegacyVItem(parity, chain);

        // write, then compare with the oracle bytes
        var destination = Filled(expected.Length + 2);
        var writer = new RLPWriter(destination, new int[2]);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteEvmLegacyV(parity, chain), context);
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), context);
        Assert.AreEqual(expected.Length, written, context);
        CollectionAssert.AreEqual(expected, destination.AsSpan(0, written).ToArray(), context);

        // the sizer agrees
        Assert.AreEqual(RLPStatus.Success, RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(parity, chain, out var sized), context);
        Assert.AreEqual(expected.Length, sized, context);

        // read the oracle's bytes (not the writer's)
        var status = RLPTypedExtensions.TryGetEvmLegacySignature(DecodeOk(expected), StringItem(SampleScalar), StringItem(SampleScalar), out var signature, out var chainId);
        Assert.AreEqual(RLPStatus.Success, status, context);
        Assert.AreEqual(chain, chainId, context);
        Assert.AreEqual(parity, signature.V, context);

        // the oracle's own inverse closes the loop
        var inverse = ExpectLegacyV(LegacyV(parity, chain));
        Assert.AreEqual(RLPStatus.Success, inverse.Status, context);
        Assert.AreEqual(chain, inverse.ChainId, context);
        Assert.AreEqual(parity, inverse.Parity, context);
    }

    private static readonly byte[] SampleScalar = Pattern(32, 0x33);

    [TestMethod]
    public void RandomVPayloads_ReaderMatchesTheOracleStatusAndValues()
    {
        for (var iteration = 0; iteration < RandomCount; iteration++)
        {
            var seed = BaseSeed + 1_000_000 + iteration;
            var rng = new Random(seed);
            var payload = RandomVPayload(rng);
            var context = $"seed {seed} v {Convert.ToHexString(payload)}";

            var expected = ExpectLegacyVPayload(payload);
            var status = RLPTypedExtensions.TryGetEvmLegacySignature(StringItem(payload), StringItem(SampleScalar), StringItem(SampleScalar), out var signature, out var chainId);
            Assert.AreEqual(expected.Status, status, context);
            if (expected.Status == RLPStatus.Success)
            {
                Assert.AreEqual(expected.ChainId, chainId, context);
                Assert.AreEqual((byte)expected.Parity, signature.V, context);
            }
            else
            {
                Assert.AreEqual(default, signature, context);
                Assert.IsNull(chainId, context);
            }

            var typedExpected = ExpectYParityPayload(payload);
            Assert.AreEqual(typedExpected, RLPTypedExtensions.TryGetEvmYParitySignature(StringItem(payload), StringItem(SampleScalar), StringItem(SampleScalar), out signature), context);
            Assert.AreEqual(typedExpected == RLPStatus.Success ? (byte)(payload.Length == 0 ? 0 : 1) : (byte)0, signature.V, context);
        }
    }

    /// <summary>Random payloads concentrated around the interesting values: 0..40, 27/28, 35.., the 8/9/10-byte widths and the ulong chain id limit.</summary>
    private static byte[] RandomVPayload(Random rng)
    {
        switch (rng.Next(6))
        {
            case 0:
                return Minimal(rng.Next(0, 70));
            case 1:
            {
                // v built from a chain id near the 64-bit limit, then nudged by -3..+3 (crosses the overflow boundary).
                var chain = new BigInteger(ulong.MaxValue) + rng.Next(-3, 4);
                var v = (chain * 2) + 35 + rng.Next(0, 2) + rng.Next(-3, 4);
                return v.Sign < 0 ? [] : Minimal(v);
            }

            case 2:
            {
                var length = rng.Next(7, 12);
                var bytes = new byte[length];
                rng.NextBytes(bytes);
                if (rng.Next(5) == 0)
                {
                    bytes[0] = 0;
                }

                return bytes;
            }

            case 3:
                return Minimal(new BigInteger((ulong)rng.NextInt64() >> rng.Next(0, 64)) * 2 + 35 + rng.Next(0, 2));
            case 4:
                return rng.Next(8) == 0 ? [] : Minimal(rng.Next(25, 40));
            default:
            {
                var bytes = new byte[rng.Next(0, 4)];
                rng.NextBytes(bytes);
                return bytes;
            }
        }
    }

    [TestMethod]
    public void RandomWriterCalls_WithInvalidParity_NeverWrite()
    {
        for (var iteration = 0; iteration < 2_000; iteration++)
        {
            var seed = BaseSeed + 2_000_000 + iteration;
            var rng = new Random(seed);
            var parity = (byte)rng.Next(2, 256);
            ulong? chain = rng.Next(3) == 0 ? null : (ulong)rng.NextInt64() >> rng.Next(0, 64);
            var destination = Filled(16);
            var writer = new RLPWriter(destination, new int[2]);
            Assert.AreEqual(RLPStatus.InvalidSignatureV, writer.TryWriteEvmLegacyV(parity, chain), $"seed {seed}");
            Assert.AreEqual(RLPStatus.InvalidSignatureV, writer.TryWriteEvmYParity(parity), $"seed {seed}");
            Assert.AreEqual(RLPStatus.InvalidSignatureV, RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(parity, chain, out _), $"seed {seed}");
            Assert.IsTrue(IsFilled(destination), $"seed {seed}");
        }
    }
}
