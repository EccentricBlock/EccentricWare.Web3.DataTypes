using EccentricWare.Web3.DataTypes.Utils;
using System.Numerics;
using System.Reflection;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Diffusion of <see cref="Hash256"/> and of the types that hash with it. The keys are random per process, so no hash
/// value is asserted, and every bound below is many standard deviations wide. Bit-flip counts of a good hash are
/// Binomial(32, 1/2) (mean 16, sd 2.83). Simulation of the hash over 20,000 random key sets gave the observed ranges
/// quoted at each test.
/// </summary>
[TestClass]
public sealed class Hash256AvalancheTests
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    private static ulong ReadKey(string field) => (ulong)typeof(Hash256).GetField(field, PrivateStatic)!.GetValue(null)!;

    private static ulong RandomUlong(Random random) => (ulong)random.NextInt64() ^ ((ulong)random.NextInt64() << 1);

    private static int Compute(ulong[] limbs) => Hash256.Compute(limbs[0], limbs[1], limbs[2], limbs[3]);

    private static int FlippedBits(int a, int b) => BitOperations.PopCount((uint)(a ^ b));

    // ---- avalanche ----

    // 256 fixed inputs x 64 bit positions per limb. The mean over one limb averages 16,384 flips (sd 0.02 for an ideal
    // hash), the mean of one bit position averages 256 flips (sd 0.18).
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void FlippingAnyInputBitOfALimb_ChangesAboutHalfTheOutputBits(int limb)
    {
        const int inputCount = 256;
        var random = new Random(61);
        ulong[][] inputs = Enumerable.Range(0, inputCount)
            .Select(_ => Enumerable.Range(0, 4).Select(__ => RandomUlong(random)).ToArray())
            .ToArray();

        double limbTotal = 0;
        for (int bit = 0; bit < 64; bit++)
        {
            double bitTotal = 0;
            foreach (ulong[] input in inputs)
            {
                ulong[] flipped = (ulong[])input.Clone();
                flipped[limb] ^= 1UL << bit;
                bitTotal += FlippedBits(Compute(input), Compute(flipped));
            }

            double bitMean = bitTotal / inputCount;
            Assert.IsTrue(bitMean is >= 12 and <= 20, $"limb {limb} bit {bit}: mean {bitMean:F2} output bits flipped");
            limbTotal += bitTotal;
        }

        double limbMean = limbTotal / (64 * inputCount);
        Assert.IsTrue(limbMean is >= 14 and <= 18, $"limb {limb}: mean {limbMean:F3} output bits flipped");
    }

    // ---- structured inputs that expose a missing final avalanche ----

    // Hash256 mixes (u0, u1) and (u2, u3) as two keyed 64x64 -> 128-bit folded products. Without the final avalanche, a
    // limb varying in a narrow range against a partner whose keyed word has many trailing zero bits leaves the low
    // output bits constant: with partner word 2^40 the 64 values below produce one hash code (2^63: half as many codes
    // as inputs). The partner is built from the process's own key (read by reflection), so every per-process key
    // exercises the weak case.
    // Why 64 keys and these bounds: the final multiplier is random, and a multiplicative hash of a short arithmetic-like
    // progression loses keys when the multiplier is close to a rational with a small denominator. For D lost keys that
    // needs a ~2^-32 coincidence for one of D offsets, so the per-run failure probability is about D * 2^-32: D = 48
    // (distinct < 16, shift 40) or 32 (distinct <= 32, shift 63) gives about 1e-8, and the rows share one multiplier.
    // Simulation of the hash over 20,000,000 random key sets: 0 violations, fewest distinct codes 58 of 64 (the
    // same simulation with the final avalanche removed gives 1 and 32). 4,096 keys with a 75% bound failed 3 times in
    // 2,000,000 key sets, which is why the key count is small.
    [TestMethod]
    [DataRow(0, 40, 16)]
    [DataRow(1, 40, 16)]
    [DataRow(2, 40, 16)]
    [DataRow(3, 40, 16)]
    [DataRow(0, 63, 33)]
    [DataRow(1, 63, 33)]
    [DataRow(2, 63, 33)]
    [DataRow(3, 63, 33)]
    public void NarrowRangeAgainstAPartnerWithTrailingZeroKeyedBits_StaysSpread(int varyingLimb, int partnerBit, int minimumDistinct)
    {
        const int count = 64;
        ulong[] keys = [ReadKey("s_key0"), ReadKey("s_key1"), ReadKey("s_key2"), ReadKey("s_key3")];
        int partner = varyingLimb ^ 1;
        var codes = new HashSet<int>();
        for (int value = 0; value < count; value++)
        {
            ulong[] limbs = new ulong[4];
            limbs[varyingLimb] = (ulong)value;
            limbs[partner] = keys[partner] ^ (1UL << partnerBit);
            codes.Add(Compute(limbs));
        }

        Assert.IsGreaterThanOrEqualTo(minimumDistinct, codes.Count, $"limb {varyingLimb} varying against partner bit {partnerBit}: {codes.Count} distinct codes of {count}");
    }

    // ---- bucket distribution ----

    // 65,536 random inputs over 256 buckets (mean 256, sd 16): the bounds are about +-6.5 sd, so a false failure over the
    // 1,024 bucket counts has probability below 1e-6 for any key (random inputs make the output uniform whatever the key).
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RandomInputs_FillEveryBucketEvenly(bool byHighByte)
    {
        const int count = 65_536;
        var random = new Random(63);
        var buckets = new int[256];
        for (int i = 0; i < count; i++)
        {
            int code = Hash256.Compute(RandomUlong(random), RandomUlong(random), RandomUlong(random), RandomUlong(random));
            buckets[byHighByte ? (int)((uint)code >> 24) : code & 0xFF]++;
        }

        Assert.IsTrue(buckets.Min() >= 150 && buckets.Max() <= 362, $"bucket counts range {buckets.Min()}..{buckets.Max()} (mean 256)");
    }

    // Each type feeds its bytes through Hash256, so wiring errors (a limb passed twice, one dropped, a flipped limb order
    // that loses a half) show as a bit position that does not move the hash. 32 random bases per bit position (sd 0.5).
    [TestMethod]
    [DataRow("uint256", 32)]
    [DataRow("int256", 32)]
    [DataRow("Hash32", 32)]
    [DataRow("Address Solana", 32)]
    [DataRow("Address EVM", 20)]
    [DataRow("Signature Solana", 64)]
    [DataRow("Signature EVM", 65)]
    public void FlippingAnyInputBitOfAValue_ChangesAboutHalfTheHashBits(string type, int byteLength)
    {
        const int baseCount = 32;
        Func<byte[], int> hash = type switch
        {
            "uint256" => b => new uint256(b).GetHashCode(),
            "int256" => b => new int256(b).GetHashCode(),
            "Hash32" => b => new Hash32(b).GetHashCode(),
            "Address Solana" => b => Address.FromSolanaBytes(b).GetHashCode(),
            "Address EVM" => b => Address.FromEvmBytes(b).GetHashCode(),
            "Signature Solana" => b => Signature.FromSolanaBytes(b).GetHashCode(),
            "Signature EVM" => b => Signature.FromEvmBytes(b).GetHashCode(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
        var random = new Random(62);
        byte[][] bases = Enumerable.Range(0, baseCount).Select(_ => { byte[] b = new byte[byteLength]; random.NextBytes(b); return b; }).ToArray();
        // An EVM signature's recovery id enters the hash as a small additive term, not through Hash256, so it is excluded.
        int flippableBits = type == "Signature EVM" ? 8 * (byteLength - 1) : 8 * byteLength;

        double total = 0;
        for (int bit = 0; bit < flippableBits; bit++)
        {
            double bitTotal = 0;
            foreach (byte[] baseBytes in bases)
            {
                byte[] flipped = (byte[])baseBytes.Clone();
                flipped[bit / 8] ^= (byte)(1 << (bit % 8));
                bitTotal += FlippedBits(hash(baseBytes), hash(flipped));
            }

            double bitMean = bitTotal / baseCount;
            Assert.IsTrue(bitMean is >= 9 and <= 23, $"{type} bit {bit}: mean {bitMean:F2} hash bits flipped");
            total += bitTotal;
        }

        double mean = total / (flippableBits * baseCount);
        Assert.IsTrue(mean is >= 14 and <= 18, $"{type}: mean {mean:F3} hash bits flipped");
    }
}
