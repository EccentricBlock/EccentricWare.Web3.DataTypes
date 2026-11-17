using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.EvmSignatureOracle;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.TypedAdapterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// The <see cref="Signature"/>-taking overloads of <c>TryWriteEvmYParity</c>, <c>TryWriteEvmLegacyV</c> and
/// <c>TryGetEvmLegacyVEncodedLength</c>: a <see cref="Signature.V"/> of 0 or 1 is the y parity (what the readers return), 27 or
/// 28 is parity <c>V - 27</c> (the standard 65-byte form), everything else is <see cref="RLPStatus.InvalidSignatureV"/>, a Solana
/// signature is <see cref="RLPStatus.InvalidArgument"/>. The <see cref="byte"/> overloads stay strict (covered in
/// <see cref="EvmSignatureWriterTests"/>; re-pinned here for 27 and 28). Expected bytes come from the independent oracle.
/// </summary>
[TestClass]
public sealed class EvmSignatureValueOverloadTests
{
    private delegate RLPStatus WriterAction(ref RLPWriter writer);

    private static readonly ulong?[] Chains = [null, 0, 1, 46, 137, 98866, ulong.MaxValue];

    private static readonly byte[] R32 = Padded32((System.Numerics.BigInteger.One << 250) + 0x1234);
    private static readonly byte[] S32 = Padded32((System.Numerics.BigInteger.One << 120) + 0x77);

    private static Signature EvmWithV(byte v) => Signature.FromEvmComponents(R32, S32, v);

    private static Signature SolanaSignature() => Signature.FromSolanaBytes(Pattern(64, 0x21));

    private static bool IsAcceptedV(int v) => v is 0 or 1 or 27 or 28;

    private static int ParityOf(int v) => v <= 1 ? v : v - 27;

    private static WriterAction YParityOf(Signature signature) => (ref RLPWriter w) => w.TryWriteEvmYParity(signature);

    private static WriterAction LegacyVOf(Signature signature, ulong? chainId) => (ref RLPWriter w) => w.TryWriteEvmLegacyV(signature, chainId);

    // ------------------------------------------------------------------ accepted set and exact bytes

    [TestMethod]
    public void EveryV_From0To255_IsAcceptedExactlyFor_0_1_27_28_ByBothWriters()
    {
        var accepted = 0;
        for (var v = 0; v <= 255; v++)
        {
            var signature = EvmWithV((byte)v);

            var destination = Filled(16);
            var writer = new RLPWriter(destination, new int[4]);
            var status = writer.TryWriteEvmYParity(signature);
            if (IsAcceptedV(v))
            {
                accepted++;
                Assert.AreEqual(RLPStatus.Success, status, $"y parity writer, V {v}");
                Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), $"V {v}");
                CollectionAssert.AreEqual(YParityItem(ParityOf(v)), destination.AsSpan(0, written).ToArray(), $"y parity bytes, V {v}");
                Assert.IsTrue(IsFilled(destination.AsSpan(written)), $"y parity wrote past the item, V {v}");
            }
            else
            {
                Assert.AreEqual(RLPStatus.InvalidSignatureV, status, $"y parity writer, V {v}");
                Assert.IsTrue(IsFilled(destination), $"y parity writer touched the buffer, V {v}");
                Assert.AreEqual(RLPStatus.EmptyInput, writer.TryComplete(out _), $"y parity writer state changed, V {v}");
            }

            foreach (var chain in Chains)
            {
                var legacyDestination = Filled(16);
                var legacyWriter = new RLPWriter(legacyDestination, new int[4]);
                var legacyStatus = legacyWriter.TryWriteEvmLegacyV(signature, chain);
                var context = $"legacy writer, V {v}, chain {chain?.ToString() ?? "none"}";
                if (IsAcceptedV(v))
                {
                    Assert.AreEqual(RLPStatus.Success, legacyStatus, context);
                    Assert.AreEqual(RLPStatus.Success, legacyWriter.TryComplete(out var legacyWritten), context);
                    CollectionAssert.AreEqual(LegacyVItem(ParityOf(v), chain), legacyDestination.AsSpan(0, legacyWritten).ToArray(), context);
                    Assert.IsTrue(IsFilled(legacyDestination.AsSpan(legacyWritten)), context);
                }
                else
                {
                    Assert.AreEqual(RLPStatus.InvalidSignatureV, legacyStatus, context);
                    Assert.IsTrue(IsFilled(legacyDestination), context);
                    Assert.AreEqual(RLPStatus.EmptyInput, legacyWriter.TryComplete(out _), context);
                }
            }
        }

        Assert.AreEqual(4, accepted, "exactly 0, 1, 27 and 28 are accepted");
    }

    [TestMethod]
    public void V27And28_SelectOnlyTheParity_TheChainIdComesFromTheArgument()
    {
        // V of 27/28 in the signature plus a chain id must give the EIP-155 value, never a value derived from 27/28.
        foreach (var (v, parity) in new[] { (27, 0), (28, 1) })
        {
            var destination = Filled(16);
            var writer = new RLPWriter(destination, new int[4]);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteEvmLegacyV(EvmWithV((byte)v), 98866));
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
            CollectionAssert.AreEqual(H(parity == 0 ? "83 030487" : "83 030488"), destination.AsSpan(0, written).ToArray(), $"V {v}");
        }
    }

    [TestMethod]
    public void SignatureOverloads_WriteTheSameBytesAsTheStrictByteOverloads()
    {
        foreach (var v in new byte[] { 0, 1, 27, 28 })
        {
            var parity = (byte)ParityOf(v);
            var signature = EvmWithV(v);
            var byParity = Build((ref RLPWriter w) => w.TryWriteEvmYParity(parity));
            CollectionAssert.AreEqual(byParity, Build(YParityOf(signature)), $"y parity, V {v}");
            foreach (var chain in Chains)
            {
                CollectionAssert.AreEqual(Build((ref RLPWriter w) => w.TryWriteEvmLegacyV(parity, chain)), Build(LegacyVOf(signature, chain)), $"V {v}, chain {chain?.ToString() ?? "none"}");
            }
        }

        static byte[] Build(WriterAction action)
        {
            var destination = new byte[16];
            var writer = new RLPWriter(destination, new int[4]);
            Assert.AreEqual(RLPStatus.Success, action(ref writer));
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
            return destination.AsSpan(0, written).ToArray();
        }
    }

    [TestMethod]
    public void TheByteOverloads_RemainStrict_27And28AreStillInvalidSignatureV()
    {
        var destination = Filled(16);
        var writer = new RLPWriter(destination, new int[4]);
        foreach (byte v in new byte[] { 27, 28 })
        {
            Assert.AreEqual(RLPStatus.InvalidSignatureV, writer.TryWriteEvmYParity(v), $"V {v}");
            Assert.AreEqual(RLPStatus.InvalidSignatureV, writer.TryWriteEvmLegacyV(v, null), $"V {v}");
            Assert.AreEqual(RLPStatus.InvalidSignatureV, writer.TryWriteEvmLegacyV(v, 1), $"V {v}");
            Assert.AreEqual(RLPStatus.InvalidSignatureV, RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(v, 1, out _), $"V {v}");
        }

        Assert.IsTrue(IsFilled(destination));
    }

    // ------------------------------------------------------------------ solana signature

    [TestMethod]
    public void SolanaSignature_IsInvalidArgument_ForBothWritersAndTheSizer_AndNothingIsWritten()
    {
        foreach (var signature in new[] { SolanaSignature(), Signature.ZeroSolana })
        {
            Assert.AreEqual(SignatureType.Solana, signature.Type);
            var destination = Filled(16);
            var writer = new RLPWriter(destination, new int[4]);
            Assert.AreEqual(RLPStatus.InvalidArgument, writer.TryWriteEvmYParity(signature));
            foreach (var chain in Chains)
            {
                Assert.AreEqual(RLPStatus.InvalidArgument, writer.TryWriteEvmLegacyV(signature, chain), $"chain {chain?.ToString() ?? "none"}");
                var length = -1;
                Assert.AreEqual(RLPStatus.InvalidArgument, RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(signature, chain, out length));
                Assert.AreEqual(0, length);
            }

            Assert.IsTrue(IsFilled(destination));
            Assert.AreEqual(RLPStatus.EmptyInput, writer.TryComplete(out _));

            // A valid write still works on the same writer afterwards.
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteEvmYParity(EvmWithV(1)));
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
            Assert.AreEqual(1, written);
        }
    }

    // ------------------------------------------------------------------ check order and atomicity

    private static readonly (string Name, Func<Signature, WriterAction> Make, int GoodSize)[] SignatureWriters =
    [
        ("TryWriteEvmYParity(Signature)", s => YParityOf(s), 1),
        ("TryWriteEvmLegacyV(Signature, none)", s => LegacyVOf(s, null), 1),
        ("TryWriteEvmLegacyV(Signature, 98866)", s => LegacyVOf(s, 98866), 4),
        ("TryWriteEvmLegacyV(Signature, max)", s => LegacyVOf(s, ulong.MaxValue), 10),
    ];

    private static IEnumerable<(string Name, Signature Signature, RLPStatus Status)> BadSignatures()
    {
        foreach (byte v in new byte[] { 2, 26, 29, 35, 255 })
        {
            yield return ($"V {v}", EvmWithV(v), RLPStatus.InvalidSignatureV);
        }

        yield return ("solana", SolanaSignature(), RLPStatus.InvalidArgument);
    }

    [TestMethod]
    public void StateAndRootChecksComeBeforeTheSignatureArgument()
    {
        foreach (var (name, make, size) in SignatureWriters)
        {
            var good = EvmWithV(28);
            foreach (var (badName, bad, _) in BadSignatures())
            {
                var defaultWriter = default(RLPWriter);
                Assert.AreEqual(RLPStatus.WriterStateInvalid, make(bad)(ref defaultWriter), $"{name}: default writer, {badName}");
            }

            var defaultGood = default(RLPWriter);
            Assert.AreEqual(RLPStatus.WriterStateInvalid, make(good)(ref defaultGood), $"{name}: default writer, good");

            var destination = Filled(size + 8);
            var writer = new RLPWriter(destination, new int[4]);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(1), name);
            var afterRoot = destination.ToArray();
            foreach (var (badName, bad, _) in BadSignatures())
            {
                Assert.AreEqual(RLPStatus.ExtraRoot, make(bad)(ref writer), $"{name}: second root, {badName}");
            }

            Assert.AreEqual(RLPStatus.ExtraRoot, make(good)(ref writer), $"{name}: second root, good");
            CollectionAssert.AreEqual(afterRoot, destination, $"{name}: second root left the buffer");
            Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out _), name);

            foreach (var (badName, bad, _) in BadSignatures())
            {
                Assert.AreEqual(RLPStatus.WriterStateInvalid, make(bad)(ref writer), $"{name}: completed, {badName}");
            }

            Assert.AreEqual(RLPStatus.WriterStateInvalid, make(good)(ref writer), $"{name}: completed, good");
            CollectionAssert.AreEqual(afterRoot, destination, $"{name}: completed left the buffer");
        }
    }

    [TestMethod]
    public void TheSignatureArgumentComesBeforeCapacityAndParentLimit()
    {
        foreach (var (name, make, size) in SignatureWriters)
        {
            var good = EvmWithV(27);
            foreach (var (badName, bad, expected) in BadSignatures())
            {
                var empty = new RLPWriter([], new int[4]);
                Assert.AreEqual(expected, make(bad)(ref empty), $"{name}: empty destination, {badName}");
                Assert.AreEqual(RLPStatus.DestinationTooSmall, make(good)(ref empty), $"{name}: empty destination, good");

                var destination = Filled(size + 8);
                var writer = new RLPWriter(destination, new int[4]);
                Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(size - 1), name);
                var afterHeader = destination.ToArray();
                Assert.AreEqual(expected, make(bad)(ref writer), $"{name}: parent limit, {badName}");
                Assert.AreEqual(RLPStatus.InvalidListBoundary, make(good)(ref writer), $"{name}: parent limit, good");
                CollectionAssert.AreEqual(afterHeader, destination, $"{name}: buffer untouched");
            }
        }
    }

    [TestMethod]
    public void AFailedWrite_ChangesNothing_AndAValidRetryOnTheSameWriterSucceeds()
    {
        foreach (var (name, make, size) in SignatureWriters)
        {
            foreach (var (badName, bad, expected) in BadSignatures())
            {
                var destination = Filled(size + 4);
                var writer = new RLPWriter(destination, new int[4]);
                Assert.AreEqual(expected, make(bad)(ref writer), $"{name}: {badName}");
                Assert.IsTrue(IsFilled(destination), $"{name}: {badName}: buffer untouched");
                Assert.AreEqual(RLPStatus.EmptyInput, writer.TryComplete(out _), $"{name}: {badName}: no root started");

                Assert.AreEqual(RLPStatus.Success, make(EvmWithV(28))(ref writer), $"{name}: retry after {badName}");
                Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), name);
                Assert.AreEqual(size, written, name);
                Assert.IsTrue(IsFilled(destination.AsSpan(size)), name);
            }
        }
    }

    [TestMethod]
    public void AGoodSignatureIntoATooSmallDestination_IsDestinationTooSmall_AndUntouched()
    {
        var signature = EvmWithV(28);
        var destination = Filled(3);
        var writer = new RLPWriter(destination, new int[4]);
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryWriteEvmLegacyV(signature, 98866)); // 4 bytes needed
        Assert.IsTrue(IsFilled(destination));
        Assert.AreEqual(RLPStatus.EmptyInput, writer.TryComplete(out _));
    }

    // ------------------------------------------------------------------ sizer

    [TestMethod]
    public void TheSizer_EqualsTheBytesTheWriterEmits_AndReportsTheWriterStatusOnFailure()
    {
        var chains = RealChainIds.Select(c => c.ChainId).Concat(WidthBoundaryChainIds()).Distinct().Select(c => (ulong?)c).Append(null).ToArray();
        for (var v = 0; v <= 255; v++)
        {
            var signature = EvmWithV((byte)v);
            foreach (var chain in chains)
            {
                var context = $"V {v}, chain {chain?.ToString() ?? "none"}";
                var status = RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(signature, chain, out var sized);

                var destination = new byte[16];
                var writer = new RLPWriter(destination, new int[4]);
                var writerStatus = writer.TryWriteEvmLegacyV(signature, chain);
                Assert.AreEqual(writerStatus, status, context);
                if (status == RLPStatus.Success)
                {
                    Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), context);
                    Assert.AreEqual(written, sized, context);
                    Assert.AreEqual(LegacyVItem(ParityOf(v), chain).Length, sized, context);
                }
                else
                {
                    Assert.AreEqual(0, sized, context);
                }
            }
        }
    }

    [TestMethod]
    public void TheSizer_PinsChain46Parity1_AsTwoBytes_AndEveryOtherEdgeOfTheOneByteRange()
    {
        // chain 46 parity 0 is v = 127 (literal 7f), parity 1 is v = 128 (81 80); chain 45 stays 1 byte with both parities.
        var expected = new (ulong? Chain, byte V, int Length)[]
        {
            (null, 27, 1), (null, 28, 1), (45, 27, 1), (45, 28, 1), (46, 27, 1), (46, 28, 2), (47, 27, 2), (0, 0, 1), (46, 1, 2),
        };
        foreach (var (chain, v, length) in expected)
        {
            Assert.AreEqual(RLPStatus.Success, RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(EvmWithV(v), chain, out var actual));
            Assert.AreEqual(length, actual, $"V {v}, chain {chain?.ToString() ?? "none"}");
        }
    }

    // ------------------------------------------------------------------ round trips

    [TestMethod]
    public void SignaturesFromFromEvmComponents_27_28_0_1_RoundTripThroughTheLegacyReader()
    {
        foreach (byte v in new byte[] { 27, 28, 0, 1 })
        {
            foreach (var chain in Chains)
            {
                var item = BuildItem(LegacyVOf(EvmWithV(v), chain), LegacyVItem(ParityOf(v), chain).Length);
                var status = RLPTypedExtensions.TryGetEvmLegacySignature(DecodeOk(item), StringItem(Minimal(new System.Numerics.BigInteger(R32, isUnsigned: true, isBigEndian: true))),
                    StringItem(Minimal(new System.Numerics.BigInteger(S32, isUnsigned: true, isBigEndian: true))), out var read, out var chainId);
                var context = $"V {v}, chain {chain?.ToString() ?? "none"}";
                Assert.AreEqual(RLPStatus.Success, status, context);
                Assert.AreEqual(chain, chainId, context);
                Assert.AreEqual((byte)ParityOf(v), read.V, context);
                Assert.IsTrue(read.R.Equals(EvmWithV(v).R) && read.S.Equals(EvmWithV(v).S), context);
            }
        }
    }

    [TestMethod]
    public void SignaturesFromFromEvmComponents_RoundTripThroughTheTypedYParityReader()
    {
        foreach (byte v in new byte[] { 27, 28, 0, 1 })
        {
            var signature = EvmWithV(v);
            var item = BuildItem(YParityOf(signature), YParityItem(ParityOf(v)).Length);
            Assert.AreEqual(RLPStatus.Success, RLPTypedExtensions.TryGetEvmYParitySignature(DecodeOk(item),
                StringItem(Minimal(new System.Numerics.BigInteger(R32, isUnsigned: true, isBigEndian: true))),
                StringItem(Minimal(new System.Numerics.BigInteger(S32, isUnsigned: true, isBigEndian: true))), out var read), $"V {v}");
            Assert.AreEqual((byte)ParityOf(v), read.V, $"V {v}");
        }
    }

    [TestMethod]
    public void ASignatureFromThe65ByteForm_RebuildsTheLegacyVWithoutManualRewriting()
    {
        // r || s || 27/28 as produced by most signing libraries; chain id 137 from the transaction context.
        var raw = new byte[65];
        R32.CopyTo(raw, 0);
        S32.CopyTo(raw, 32);
        foreach (byte v in new byte[] { 27, 28 })
        {
            raw[64] = v;
            var signature = Signature.FromEvmBytes(raw);
            var expected = H(v == 27 ? "82 0135" : "82 0136");
            CollectionAssert.AreEqual(expected, BuildItem(LegacyVOf(signature, 137), expected.Length), $"V {v}");
        }
    }

    [TestMethod]
    public void AReaderBuiltSignature_FeedsTheSignatureOverloads_ToTheOriginalBytes()
    {
        // Read a signed legacy transaction's v, r, s, then rebuild the v from the signature the reader returned.
        var vItem = H("83 030488");
        var status = RLPTypedExtensions.TryGetEvmLegacySignature(DecodeOk(vItem), StringItem(Minimal(1)), StringItem(Minimal(2)), out var signature, out var chainId);
        Assert.AreEqual(RLPStatus.Success, status);
        Assert.AreEqual((byte)1, signature.V);
        Assert.AreEqual((ulong?)98866, chainId);
        CollectionAssert.AreEqual(vItem, BuildItem(LegacyVOf(signature, chainId), vItem.Length));
        CollectionAssert.AreEqual(H("01"), BuildItem(YParityOf(signature), 1));
    }

    private static byte[] BuildItem(WriterAction action, int size)
    {
        var destination = Filled(size + 3);
        var writer = new RLPWriter(destination, new int[4]);
        Assert.AreEqual(RLPStatus.Success, action(ref writer));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written));
        Assert.AreEqual(size, written);
        return destination.AsSpan(0, written).ToArray();
    }
}

/// <summary>The signature-taking writers and sizer allocate zero managed bytes after warm-up, on success and on every failure status.</summary>
[TestClass]
[DoNotParallelize]
public sealed class EvmSignatureValueOverloadAllocationTests
{
    private const int Iterations = 2_000;
    private const int Trials = 5;

    [TestMethod]
    public void SignatureOverloads_AllocateNothing_OnSuccessAndEveryFailure()
    {
        var destination = new byte[256];
        var frames = new int[4];
        var tiny = new byte[2];
        var rawParity = Signature.FromEvmComponents(Pattern(32, 0x11), Pattern(32, 0x22), 1);
        var raw27 = Signature.FromEvmComponents(Pattern(32, 0x11), Pattern(32, 0x22), 27);
        var bad = Signature.FromEvmComponents(Pattern(32, 0x11), Pattern(32, 0x22), 35);
        var solana = Signature.FromSolanaBytes(Pattern(64, 0x21));
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += Run(destination, frames, tiny, rawParity, raw27, bad, solana);
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                checksum += Run(destination, frames, tiny, rawParity, raw27, bad, solana);
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.AreEqual(0L, best, $"signature overloads allocated {best} bytes over {Iterations} calls (smallest of {Trials} trials)");
        Assert.AreNotEqual(0L, checksum);

        static long Run(byte[] destination, int[] frames, byte[] tiny, Signature rawParity, Signature raw27, Signature bad, Signature solana)
        {
            var writer = new RLPWriter(destination, frames);
            long sum = (int)writer.TryBeginList(1 + 4 + 10 + 1 + 1);
            sum += (int)writer.TryWriteEvmYParity(rawParity);
            sum += (int)writer.TryWriteEvmYParity(bad);                       // InvalidSignatureV
            sum += (int)writer.TryWriteEvmYParity(solana);                    // InvalidArgument
            sum += (int)writer.TryWriteEvmLegacyV(raw27, 98866);
            sum += (int)writer.TryWriteEvmLegacyV(rawParity, ulong.MaxValue);
            sum += (int)writer.TryWriteEvmLegacyV(raw27, null);
            sum += (int)writer.TryWriteEvmLegacyV(bad, 1);
            sum += (int)writer.TryWriteEvmLegacyV(solana, 1);
            sum += (int)writer.TryWriteEvmLegacyV(rawParity, null);          // fills the list
            sum += (int)writer.TryEndList();
            sum += (int)writer.TryWriteEvmYParity(raw27);                     // ExtraRoot
            sum += (int)writer.TryComplete(out var written) + written;
            sum += (int)writer.TryWriteEvmLegacyV(raw27, 1);                  // completed

            var small = new RLPWriter(tiny, frames);
            sum += (int)small.TryWriteEvmLegacyV(raw27, 98866);               // DestinationTooSmall

            var invalid = default(RLPWriter);
            sum += (int)invalid.TryWriteEvmYParity(raw27);

            sum += (int)RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(raw27, 98866, out var length) + length;
            sum += (int)RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(bad, 98866, out length) + length;
            sum += (int)RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(solana, null, out length) + length;
            return sum;
        }
    }
}
