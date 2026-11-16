using System.Security.Cryptography;

namespace EccentricWare.Web3.DataTypes.Utils;

/// <summary>
/// Per-process random multiplier for hashing fixed-width keys that an attacker may choose.
/// </summary>
internal static class HashSeed
{
    /// <summary>
    /// Returns a random odd 64-bit multiplier. With <c>h = x * multiplier</c> and code <c>(h >> 32) ^ h</c>, an identity
    /// hash's attack set (keys that are multiples of a dictionary's prime bucket count, all landing in one bucket) spreads
    /// like random data, and the mapping cannot be precomputed without the per-process multiplier. Selectors and
    /// discriminators come from untrusted calldata / instruction data, so keys are attacker-chosen.
    /// This is an empirical anti-collision measure, not a cryptographic or formally universal hash.
    /// </summary>
    public static ulong NextOddMultiplier()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes) | 1UL;
    }

    /// <summary>
    /// Returns a random non-zero 64-bit key for keyed hash mixing (see <see cref="Hash256"/>).
    /// </summary>
    public static ulong NextKey()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        ulong key;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            key = BitConverter.ToUInt64(bytes);
        }
        while (key == 0);

        return key;
    }
}
