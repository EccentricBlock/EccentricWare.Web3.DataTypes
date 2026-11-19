using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.Utils;

/// <summary>
/// Keyed 32-bit hash of four 64-bit limbs, shared by <see cref="uint256"/> and <see cref="int256"/>.
/// Keys are random per process, so an attacker who controls the values (token ids, nonces, amounts taken from RPC
/// responses) cannot precompute colliding inputs for a <see cref="Dictionary{TKey,TValue}"/>.
/// Like <see cref="HashSeed"/>'s other users this is an empirical anti-collision measure, not a cryptographic hash.
/// </summary>
internal static class Hash256
{
    // A distinct key per limb: swapping limbs, or the two halves, changes the hash.
    private static readonly ulong s_key0 = HashSeed.NextKey();
    private static readonly ulong s_key1 = HashSeed.NextKey();
    private static readonly ulong s_key2 = HashSeed.NextKey();
    private static readonly ulong s_key3 = HashSeed.NextKey();

    /// <summary>Odd multiplier for the final avalanche step (see <see cref="Compute"/>).</summary>
    private static readonly ulong s_finalMultiplier = HashSeed.NextOddMultiplier();

    /// <summary>Folded multiply: the full 128-bit product of the two keyed words, high half xor low half.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Mix(ulong a, ulong b)
    {
        ulong high = Math.BigMul(a, b, out ulong low);
        return high ^ low;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Compute(ulong u0, ulong u1, ulong u2, ulong u3)
    {
        // Wrapping is the point of a hash: without `unchecked` the opt-in checked-arithmetic build (-p:RlpCheckedArithmetic=true)
        // threw OverflowException from every GetHashCode of uint256, int256, Address and Hash32, so any dictionary keyed by them failed.
        unchecked
        {
            // The two multiplies are independent, so they overlap in the pipeline.
            ulong h = Mix(u0 ^ s_key0, u1 ^ s_key1) + Mix(u2 ^ s_key2, u3 ^ s_key3);

            // Final avalanche: when one limb varies over a narrow range and the other keyed word has trailing zero bits, the
            // folded products have weak low bits. Fold, multiply by a random odd constant and keep the well-mixed high half.
            h ^= h >> 32;
            h *= s_finalMultiplier;
            return (int)(h >> 32);
        }
    }
}
