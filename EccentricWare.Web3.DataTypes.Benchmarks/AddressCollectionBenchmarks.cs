using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// Bulk operations over many <see cref="Address"/> values: <see cref="AddressBulkMatcher"/> linear scans against a plain
/// scalar loop, <see cref="AddressSet"/> build and lookup against <see cref="HashSet{T}"/>, and sorting.
/// </summary>
[MemoryDiagnoser]
public class AddressCollectionBenchmarks
{
    // 8 and 64 are the "small rule pack" sizes AddressBulkMatcher.ContainsLinear targets; 1024 and 65536 exercise the
    // hash set (65536 x 40 bytes = 2.5 MiB of keys).
    [Params(8, 64, 1024, 65536)]
    public int Count { get; set; }

    private Address[] _haystack = null!;
    private Address[] _mixedHaystack = null!;
    private Address _lastItem;
    private Address _missing;
    private Address[] _missKeys = null!;
    private Address[] _shuffledHits = null!;
    private Address[] _sortBuffer = null!;
    private int[] _matchedIndices = null!;

    private AddressSet _set = null!;
    private HashSet<Address> _hashSet = null!;
    private Dictionary<Address, int> _dictionary = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(BenchmarkData.Seed);
        Span<byte> evm = stackalloc byte[Address.EvmByteLength];
        Span<byte> solana = stackalloc byte[Address.SolanaByteLength];

        _haystack = new Address[Count];
        _mixedHaystack = new Address[Count];
        for (int i = 0; i < Count; i++)
        {
            random.NextBytes(evm);
            _haystack[i] = Address.FromEvmBytes(evm);

            // Half EVM, half Solana: the scan has to reject on type as well as on payload.
            if ((i & 1) == 0)
            {
                _mixedHaystack[i] = _haystack[i];
            }
            else
            {
                random.NextBytes(solana);
                _mixedHaystack[i] = Address.FromSolanaBytes(solana);
            }
        }

        _lastItem = _haystack[^1];
        _missKeys = new Address[Count];
        for (int i = 0; i < Count; i++)
        {
            random.NextBytes(evm);
            _missKeys[i] = Address.FromEvmBytes(evm);
        }

        // The hit loops above visit keys in insertion order, which is also HashSet's storage order and so flatters it.
        // A shuffled copy gives every structure random-access lookups.
        _shuffledHits = (Address[])_haystack.Clone();
        new Random(BenchmarkData.Seed + 7).Shuffle(_shuffledHits);

        _missing = _missKeys[0];
        _sortBuffer = new Address[Count];
        _matchedIndices = new int[Count];

        _set = AddressSet.Build(_haystack);
        _hashSet = new HashSet<Address>(_haystack);
        _dictionary = new Dictionary<Address, int>(Count);
        for (int i = 0; i < Count; i++)
            _dictionary[_haystack[i]] = i;

        // Random 160-bit values collide with probability ~0, so every key hits and every miss key misses.
        if (_set.Count != Count
            || _hashSet.Count != Count
            || !AddressBulkMatcher.ContainsLinear(_haystack, _lastItem)
            || AddressBulkMatcher.ContainsLinear(_haystack, _missing)
            || !_set.Contains(_lastItem)
            || _set.Contains(_missing)
            || _hashSet.Contains(_missing))
        {
            throw new InvalidOperationException("Address collection benchmark inputs are inconsistent.");
        }
    }

    // ---- linear scans (the matcher is documented for dozens to a few hundred items; large Count shows the cost of ignoring that) ----

    [Benchmark]
    public bool ContainsLinear_HitAtEnd() => AddressBulkMatcher.ContainsLinear(_haystack, _lastItem);

    [Benchmark]
    public bool ContainsLinear_Miss() => AddressBulkMatcher.ContainsLinear(_haystack, _missing);

    [Benchmark]
    public bool ContainsLinear_Miss_MixedTypes() => AddressBulkMatcher.ContainsLinear(_mixedHaystack, _missing);

    /// <summary>Reference: a plain loop over <see cref="Address.Equals(Address)"/>.</summary>
    [Benchmark]
    public bool ScalarLoop_Miss()
    {
        Address needle = _missing;
        foreach (Address candidate in _haystack)
        {
            if (candidate.Equals(needle))
                return true;
        }
        return false;
    }

    /// <summary>Reference: <see cref="Array.IndexOf{T}(T[], T)"/> over the same data.</summary>
    [Benchmark]
    public bool ArrayIndexOf_Miss() => Array.IndexOf(_haystack, _missing) >= 0;

    // ---- hash lookups (each loops over all Count keys; divide by Count for a per-lookup figure) ----

    [Benchmark]
    public AddressSet AddressSet_Build() => AddressSet.Build(_haystack);

    [Benchmark]
    public int AddressSet_Contains_AllHits()
    {
        int hits = 0;
        AddressSet set = _set;
        foreach (Address key in _haystack)
        {
            if (set.Contains(in key))
                hits++;
        }
        return hits;
    }

    [Benchmark]
    public int AddressSet_Contains_ShuffledHits()
    {
        int hits = 0;
        AddressSet set = _set;
        foreach (Address key in _shuffledHits)
        {
            if (set.Contains(in key))
                hits++;
        }
        return hits;
    }

    [Benchmark]
    public int HashSet_Contains_ShuffledHits()
    {
        int hits = 0;
        HashSet<Address> set = _hashSet;
        foreach (Address key in _shuffledHits)
        {
            if (set.Contains(key))
                hits++;
        }
        return hits;
    }

    [Benchmark]
    public int AddressSet_Contains_AllMisses()
    {
        int hits = 0;
        AddressSet set = _set;
        foreach (Address key in _missKeys)
        {
            if (set.Contains(in key))
                hits++;
        }
        return hits;
    }

    [Benchmark]
    public int AddressSet_Match_AllHits() => AddressBulkMatcher.Match(_set, _shuffledHits, _matchedIndices);

    [Benchmark]
    public int AddressSet_Match_AllMisses() => AddressBulkMatcher.Match(_set, _missKeys, _matchedIndices);

    [Benchmark]
    public int HashSet_Contains_AllHits()
    {
        int hits = 0;
        HashSet<Address> set = _hashSet;
        foreach (Address key in _haystack)
        {
            if (set.Contains(key))
                hits++;
        }
        return hits;
    }

    [Benchmark]
    public int HashSet_Contains_AllMisses()
    {
        int hits = 0;
        HashSet<Address> set = _hashSet;
        foreach (Address key in _missKeys)
        {
            if (set.Contains(key))
                hits++;
        }
        return hits;
    }

    [Benchmark]
    public int Dictionary_TryGetValue_AllHits()
    {
        int sum = 0;
        Dictionary<Address, int> dictionary = _dictionary;
        foreach (Address key in _haystack)
        {
            if (dictionary.TryGetValue(key, out int v))
                sum += v;
        }
        return sum;
    }

    // ---- hashing and sorting ----

    [Benchmark]
    public int GetHashCode_All()
    {
        int h = 0;
        foreach (Address key in _mixedHaystack)
            h ^= key.GetHashCode();
        return h;
    }

    [Benchmark]
    public int Sort_Copy()
    {
        _haystack.CopyTo(_sortBuffer, 0);
        Array.Sort(_sortBuffer);
        return _sortBuffer.Length;
    }
}
