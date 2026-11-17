using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.TypedAdapterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// The typed writer adapters allocate zero managed bytes after warm-up, on success and on every failure status. The
/// destination and frame workspace exist before the measured region. Runs serially because allocation counters are per thread.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TypedAdapterWriterAllocationTests
{
    private const int Iterations = 2_000;
    private const int Trials = 5;

    private static void AssertNoAllocation(long smallestTrial, string what) =>
        Assert.AreEqual(0L, smallestTrial, $"{what} allocated {smallestTrial} bytes over {Iterations} calls (smallest of {Trials} trials)");

    [TestMethod]
    public void WriterAdapters_AllocateNothing_OnSuccessAndEveryFailure()
    {
        var destination = new byte[256];
        var frames = new int[4];
        var tiny = new byte[4];
        var hash = new Hash32(Pattern(32, 0xee));
        var address = Address.FromEvmBytes(Pattern(20, 0xa1));
        var solana = Address.FromSolanaBytes(Pattern(32, 0x01));
        var big = uint256.MaxValue;
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += Run(destination, frames, tiny, hash, address, solana, big);
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                checksum += Run(destination, frames, tiny, hash, address, solana, big);
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "TryWriteUInt256/TryWriteHash32/TryWriteAddress");
        Assert.AreNotEqual(0L, checksum);

        static long Run(byte[] destination, int[] frames, byte[] tiny, Hash32 hash, Address address, Address solana, uint256 big)
        {
            var writer = new RLPWriter(destination, frames);
            long sum = (int)writer.TryBeginList(33 + 33 + 21 + 1 + 1);
            sum += (int)writer.TryWriteUInt256(big);
            sum += (int)writer.TryWriteHash32(hash);
            sum += (int)writer.TryWriteAddress(address);
            sum += (int)writer.TryWriteAddress(solana);
            sum += (int)writer.TryWriteUInt256(default);
            sum += (int)writer.TryWriteUInt256(new uint256(0x7f));
            sum += (int)writer.TryEndList();
            sum += (int)writer.TryWriteHash32(hash);              // ExtraRoot
            sum += (int)writer.TryComplete(out var written) + written;
            sum += (int)writer.TryWriteUInt256(big);              // completed

            var small = new RLPWriter(tiny, frames);
            sum += (int)small.TryWriteUInt256(big);               // DestinationTooSmall
            sum += (int)small.TryWriteHash32(hash);
            sum += (int)small.TryWriteAddress(address);
            sum += (int)small.TryWriteAddress(solana);

            var invalid = default(RLPWriter);
            sum += (int)invalid.TryWriteUInt256(big);
            sum += (int)invalid.TryWriteAddress(solana);
            return sum;
        }
    }

    [TestMethod]
    public void SignatureWritersAndSizers_AllocateNothing_OnSuccessAndEveryFailure_IncludingTheNullableChainId()
    {
        var destination = new byte[256];
        var frames = new int[4];
        var tiny = new byte[2];
        var value = new uint256(Padded32((System.Numerics.BigInteger.One << 200) + 9));
        var magnitude = Pattern(33, 0x41);
        var leadingZero = new byte[] { 0x00, 0x01 };
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += Run(destination, frames, tiny, value, magnitude, leadingZero);
        }

        var best = long.MaxValue;
        for (var trial = 0; trial < Trials; trial++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++)
            {
                checksum += Run(destination, frames, tiny, value, magnitude, leadingZero);
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        AssertNoAllocation(best, "TryWriteEvmYParity/TryWriteEvmLegacyV and the sizing helpers");
        Assert.AreNotEqual(0L, checksum);

        static long Run(byte[] destination, int[] frames, byte[] tiny, uint256 value, byte[] magnitude, byte[] leadingZero)
        {
            var writer = new RLPWriter(destination, frames);
            long sum = (int)writer.TryBeginList(1 + 4 + 10 + 1 + 3 + 1);
            sum += (int)writer.TryWriteEvmYParity(1);
            sum += (int)writer.TryWriteEvmYParity(2);                         // InvalidSignatureV
            sum += (int)writer.TryWriteEvmLegacyV(1, 98866);
            sum += (int)writer.TryWriteEvmLegacyV(0, ulong.MaxValue);
            sum += (int)writer.TryWriteEvmLegacyV(0, null);
            sum += (int)writer.TryWriteEvmLegacyV(1, 8453);
            sum += (int)writer.TryWriteEvmLegacyV(7, 1);                      // InvalidSignatureV
            sum += (int)writer.TryWriteEvmLegacyV(0, 1);                      // the last child fills the list
            sum += (int)writer.TryEndList();
            sum += (int)writer.TryWriteEvmYParity(0);                         // ExtraRoot
            sum += (int)writer.TryWriteEvmLegacyV(9, null);                   // ExtraRoot wins over the argument
            sum += (int)writer.TryComplete(out var written) + written;
            sum += (int)writer.TryWriteEvmLegacyV(0, 1);                      // completed

            var small = new RLPWriter(tiny, frames);
            sum += (int)small.TryWriteEvmLegacyV(1, 98866);                   // DestinationTooSmall
            sum += (int)small.TryWriteEvmLegacyV(3, 98866);                   // InvalidSignatureV before capacity

            var bounded = new RLPWriter(destination, frames);
            sum += (int)bounded.TryBeginList(1);
            sum += (int)bounded.TryWriteEvmLegacyV(1, 98866);                 // InvalidListBoundary

            var invalid = default(RLPWriter);
            sum += (int)invalid.TryWriteEvmYParity(1);
            sum += (int)invalid.TryWriteEvmLegacyV(1, null);

            sum += RLPEncoder.GetUInt64EncodedLength(ulong.MaxValue) + RLPEncoder.GetUInt64EncodedLength(0x7f);
            sum += (int)RLPEncoder.TryGetUnsignedMagnitudeLength(magnitude, out var length) + length;
            sum += (int)RLPEncoder.TryGetUnsignedMagnitudeLength(leadingZero, out length) + length;
            sum += (int)RLPEncoder.TryGetUnsignedMagnitudeLength([], out length) + length;
            sum += RLPTypedExtensions.GetUInt256EncodedLength(value) + RLPTypedExtensions.GetUInt256EncodedLength(default);
            sum += (int)RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(1, 98866, out length) + length;
            sum += (int)RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(0, null, out length) + length;
            sum += (int)RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(2, 98866, out length) + length;
            sum += RLPTypedExtensions.AddressEncodedLength + RLPTypedExtensions.Hash32EncodedLength;
            return sum;
        }
    }
}
