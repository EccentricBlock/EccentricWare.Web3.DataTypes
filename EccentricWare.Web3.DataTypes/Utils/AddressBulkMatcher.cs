using System.Numerics;
using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.Utils;

/// <summary>
/// Bulk matching utilities for <see cref="Address"/>.
/// Provides hash-based (compiled) membership and a linear scan for small sets.
/// </summary>
public static class AddressBulkMatcher
{
    /// <summary>
    /// Returns true if <paramref name="needle"/> is contained in <paramref name="haystack"/> (same family and payload).
    /// A plain scan. <see cref="Address"/> is 40 bytes, so a vector compare per element saved little: a benchmark taken
    /// before the former AVX2 byte-compare was removed found it about 2.5 ns faster at 8 items, and slower than or level
    /// with <c>Span.Contains</c> at 64, 1,024 and 65,536 items. The ISA-specific path (and its dependence on the struct
    /// layout) was removed for that trade. Use <see cref="AddressSet"/> for more than a few hundred addresses or for
    /// repeated lookups.
    /// </summary>
    public static bool ContainsLinear(ReadOnlySpan<Address> haystack, Address needle)
        => haystack.Contains(needle);

    /// <summary>
    /// Writes indices of probes that are contained in <paramref name="set"/> into <paramref name="matchedProbeIndices"/>.
    /// Returns the number of indices written.
    /// </summary>
    public static int Match(AddressSet set, ReadOnlySpan<Address> probes, Span<int> matchedProbeIndices)
    {
        ArgumentNullException.ThrowIfNull(set);

        if (matchedProbeIndices.Length == 0 || probes.IsEmpty)
            return 0;

        int written = 0;
        for (int i = 0; i < probes.Length; i++)
        {
            if (set.Contains(in probes[i]))
            {
                if (written == matchedProbeIndices.Length)
                    break;

                matchedProbeIndices[written++] = i;
            }
        }

        return written;
    }
}

/// <summary>
/// A compiled, allocation-stable hash set for <see cref="Address"/> membership checks.
/// Optimised for large rule packs (thousands to millions) with predictable performance.
/// </summary>
public sealed class AddressSet
{
    // Hashes are probed on their own (8 per cache line) so a miss, the common case for a deny/allow list, touches one line.
    // The key lives in one 40-byte entry rather than five parallel arrays, so a hit touches two lines instead of six.
    private readonly ulong[] _hashes; // 0 = empty; a stored hash always has bit 63 set (see Hash)
    private readonly Address[] _entries;
    private readonly int _mask;
    private readonly int _count;

    /// <summary>Returns the number of elements inserted into the set.</summary>
    public int Count => _count;

    private AddressSet(ulong[] hashes, Address[] entries, int count)
    {
        _hashes = hashes;
        _entries = entries;
        _mask = hashes.Length - 1;
        _count = count;
    }

    /// <summary>
    /// Builds a compiled <see cref="AddressSet"/> from the provided <paramref name="addresses"/>.
    /// </summary>
    /// <param name="addresses">Addresses to insert.</param>
    /// <param name="loadFactor">Hash table load factor (0.50..0.90). Lower is faster but uses more memory.</param>
    public static AddressSet Build(ReadOnlySpan<Address> addresses, double loadFactor = 0.72)
    {
        // Written as a positive range test so NaN is rejected too (a NaN load factor would size a 4-slot table and
        // make Build spin forever once it fills).
        if (!(loadFactor >= 0.50 && loadFactor <= 0.90))
            throw new ArgumentOutOfRangeException(nameof(loadFactor), "Load factor must be between 0.50 and 0.90.");

        if (addresses.IsEmpty)
            return new AddressSet(Array.Empty<ulong>(), Array.Empty<Address>(), 0);

        int capacity = ComputeCapacity(addresses.Length, loadFactor);

        var hashes = new ulong[capacity];
        var entries = new Address[capacity];

        int inserted = 0;

        for (int i = 0; i < addresses.Length; i++)
        {
            ref readonly Address address = ref addresses[i];
            ulong h = Hash(in address);
            int slot = (int)h & (capacity - 1);

            // Linear probing.
            while (hashes[slot] != 0UL)
            {
                // Optional de-dup: if identical, skip.
                if (hashes[slot] == h && entries[slot].Equals(address))
                    goto Next;

                slot = (slot + 1) & (capacity - 1);
            }

            hashes[slot] = h;
            entries[slot] = address;
            inserted++;

        Next:
            continue;
        }

        return new AddressSet(hashes, entries, inserted);
    }

    /// <summary>
    /// Test diagnostic: how many entries have an even home slot, and the mean number of slots a successful lookup
    /// examines. Derived from the stored hashes, so it describes the real table without duplicating the lookup loop.
    /// </summary>
    internal void GetPlacementStatistics(out int evenHomeSlots, out double averageProbeLength)
    {
        evenHomeSlots = 0;
        long probes = 0;
        for (int slot = 0; slot < _hashes.Length; slot++)
        {
            ulong stored = _hashes[slot];
            if (stored == 0UL)
                continue;

            int home = (int)stored & _mask;
            if ((home & 1) == 0)
                evenHomeSlots++;

            probes += ((slot - home) & _mask) + 1;
        }

        averageProbeLength = _count == 0 ? 0 : (double)probes / _count;
    }

    /// <summary>
    /// Returns true if the set contains <paramref name="address"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(in Address address)
    {
        if (_hashes.Length == 0)
            return false;

        ulong h = Hash(in address);
        int slot = (int)h & _mask;

        while (true)
        {
            ulong stored = _hashes[slot];
            if (stored == 0UL)
                return false;

            if (stored == h && _entries[slot].Equals(address))
                return true;

            slot = (slot + 1) & _mask;
        }
    }

    // Per-process random keys, one per limb (see HashSeed). The table is probed with addresses taken from transactions,
    // and Solana account keys are arbitrary 32-byte values, so a fixed, public hash would let an attacker precompute
    // keys that all land in one cluster and turn Build and Contains into O(n) per operation.
    private static readonly ulong s_key0 = HashSeed.NextKey();
    private static readonly ulong s_key1 = HashSeed.NextKey();
    private static readonly ulong s_key2 = HashSeed.NextKey();
    private static readonly ulong s_key3 = HashSeed.NextKey();
    private static readonly ulong s_finalMultiplier = HashSeed.NextOddMultiplier();

    /// <summary>Folded multiply: the full 128-bit product, high half xor low half.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Mix(ulong a, ulong b)
    {
        ulong high = Math.BigMul(a, b, out ulong low);
        return high ^ low;
    }

    /// <summary>
    /// Computes a keyed, non-zero 64-bit hash (bit 63 always set) of the 32-byte key and the family. Stable only
    /// within the process, which is all a table that is built and probed in-process needs.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Hash(in Address address)
    {
        address.GetLimbs(out ulong u0, out ulong u1, out ulong u2, out ulong u3);
        byte type = (byte)address.Type;

        // Two independent keyed folded multiplies. The family is added afterwards (zero for EVM): XORing it into the last limb
        // would cancel for the Solana key whose last limb is 1 and give it the hash of the matching EVM address.
        ulong h = Mix(u0 ^ s_key0, u1 ^ s_key1) + Mix(u2 ^ s_key2, u3 ^ s_key3) + ((ulong)type * 0x9E3779B97F4A7C15UL);

        // Final avalanche so the low bits, which pick the slot, depend on every input bit.
        h ^= h >> 32;
        h *= s_finalMultiplier;
        h ^= h >> 29;

        // Mark non-zero with the top bit: the slot index comes from the low bits, so forcing bit 0 instead would send
        // every first probe to an odd slot and double the effective load.
        return h | (1UL << 63);
    }

    /// <summary>The largest table size: 2^30 slots (a power of two that still fits an array length).</summary>
    internal const int MaxCapacity = 1 << 30;

    /// <summary>
    /// The table size for <paramref name="count"/> members at <paramref name="loadFactor"/>: the next power of two at or above
    /// <c>count / loadFactor</c>, at least 4. Throws <see cref="ArgumentOutOfRangeException"/> when that exceeds
    /// <see cref="MaxCapacity"/> (it used to overflow to a negative size).
    /// </summary>
    internal static int ComputeCapacity(int count, double loadFactor)
    {
        double desired = Math.Ceiling(count / loadFactor);
        if (desired > MaxCapacity)
            throw new ArgumentOutOfRangeException(nameof(count), $"Too many addresses for a table of at most {MaxCapacity} slots at load factor {loadFactor}.");

        return NextPowerOfTwo(Math.Max(4, (int)desired));
    }

    /// <summary>Returns the next power-of-two greater than or equal to <paramref name="value"/>.</summary>
    private static int NextPowerOfTwo(int value)
    {
        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return value + 1;
    }
}