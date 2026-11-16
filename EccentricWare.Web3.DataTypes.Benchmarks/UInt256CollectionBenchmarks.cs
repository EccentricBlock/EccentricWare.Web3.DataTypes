using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.Comparers;
using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// Bulk operations over many <see cref="uint256"/> values: SIMD span scans, hash-table lookups (random and sequential
/// keys, default vs <see cref="UInt256Comparer"/>) and sorting.
/// </summary>
[MemoryDiagnoser]
public class UInt256CollectionBenchmarks
{
    // 8 sits at a small-scan threshold; 1024 fits L1/L2; 65536 (2 MiB of keys) exercises memory bandwidth.
    [Params(8, 1024, 65536)]
    public int Count { get; set; }

    private uint256[] _haystack = null!;
    private uint256[] _sequential = null!;
    private uint256 _lastItem;
    private uint256 _missing;
    private uint256 _repeated;
    private uint256[] _repeatedHaystack = null!;
    private uint256[] _unsorted = null!;
    private uint256[] _sortBuffer = null!;
    private int[] _indices = null!;

    private Dictionary<uint256, int> _randomDictionary = null!;
    private Dictionary<uint256, int> _randomDictionaryWithComparer = null!;
    private Dictionary<uint256, int> _sequentialDictionary = null!;
    private HashSet<uint256> _randomSet = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(BenchmarkData.Seed);

        _haystack = new uint256[Count];
        for (int i = 0; i < _haystack.Length; i++)
            _haystack[i] = NextWide(random);

        // NFT-style token ids / nonces: 1..N, only the low limb varies. A weak hash clusters these.
        _sequential = new uint256[Count];
        for (int i = 0; i < _sequential.Length; i++)
            _sequential[i] = new uint256((ulong)i + 1);

        _lastItem = _haystack[^1];
        _missing = NextWide(random);

        // 1-in-8 items equal the needle: CountEquals must visit every element either way.
        _repeated = _haystack[0];
        _repeatedHaystack = (uint256[])_haystack.Clone();
        for (int i = 0; i < _repeatedHaystack.Length; i += 8)
            _repeatedHaystack[i] = _repeated;

        _indices = new int[Count];
        _unsorted = _haystack;
        _sortBuffer = new uint256[Count];

        _randomDictionary = new Dictionary<uint256, int>(Count);
        _randomDictionaryWithComparer = new Dictionary<uint256, int>(Count, UInt256Comparer.Instance);
        _sequentialDictionary = new Dictionary<uint256, int>(Count);
        _randomSet = new HashSet<uint256>(Count);
        for (int i = 0; i < Count; i++)
        {
            _randomDictionary[_haystack[i]] = i;
            _randomDictionaryWithComparer[_haystack[i]] = i;
            _sequentialDictionary[_sequential[i]] = i;
            _randomSet.Add(_haystack[i]);
        }

        if (UInt256BulkSimd.IndexOf(_haystack, _lastItem) != _haystack.Length - 1
            || UInt256BulkSimd.IndexOf(_haystack, _missing) != -1
            || UInt256BulkSimd.CountEquals(_repeatedHaystack, _repeated) < Count / 8
            || _randomDictionary.Count != Count
            || _sequentialDictionary.Count != Count)
        {
            throw new InvalidOperationException("uint256 collection benchmark inputs are inconsistent.");
        }
    }

    private static uint256 NextWide(Random random)
        => new(BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random));

    // ---- SIMD span scans ----

    [Benchmark]
    public int IndexOf_HitAtEnd() => UInt256BulkSimd.IndexOf(_haystack, _lastItem);

    [Benchmark]
    public int IndexOf_Miss() => UInt256BulkSimd.IndexOf(_haystack, _missing);

    [Benchmark]
    public int CountEquals_OneInEight() => UInt256BulkSimd.CountEquals(_repeatedHaystack, _repeated);

    [Benchmark]
    public int FindAllEquals_OneInEight() => UInt256BulkSimd.FindAllEquals(_repeatedHaystack, _repeated, _indices);

    [Benchmark]
    public int IndexOf_ScalarLoop_Miss()
    {
        // What a caller would write without the SIMD helper: the baseline the helper has to beat.
        uint256[] haystack = _haystack;
        uint256 needle = _missing;
        for (int i = 0; i < haystack.Length; i++)
        {
            if (haystack[i] == needle)
                return i;
        }

        return -1;
    }

    // ---- hash tables (each benchmark performs Count lookups: divide by Count for a per-lookup figure) ----

    [Benchmark]
    public int Dictionary_Random_AllHits()
    {
        int sum = 0;
        Dictionary<uint256, int> dictionary = _randomDictionary;
        foreach (uint256 key in _haystack)
            sum += dictionary[key];

        return sum;
    }

    [Benchmark]
    public int Dictionary_Random_AllHits_WithComparerInstance()
    {
        int sum = 0;
        Dictionary<uint256, int> dictionary = _randomDictionaryWithComparer;
        foreach (uint256 key in _haystack)
            sum += dictionary[key];

        return sum;
    }

    [Benchmark]
    public int Dictionary_Sequential_AllHits()
    {
        int sum = 0;
        Dictionary<uint256, int> dictionary = _sequentialDictionary;
        foreach (uint256 key in _sequential)
            sum += dictionary[key];

        return sum;
    }

    [Benchmark]
    public int HashSet_Random_Contains()
    {
        int hits = 0;
        HashSet<uint256> set = _randomSet;
        foreach (uint256 key in _haystack)
            hits += set.Contains(key) ? 1 : 0;

        return hits;
    }

    [Benchmark]
    public int Dictionary_Build_Sequential()
    {
        var dictionary = new Dictionary<uint256, int>(Count);
        for (int i = 0; i < _sequential.Length; i++)
            dictionary[_sequential[i]] = i;

        return dictionary.Count;
    }

    [Benchmark]
    public int GetHashCode_All()
    {
        int sum = 0;
        foreach (uint256 key in _haystack)
            sum += key.GetHashCode();

        return sum;
    }

    // ---- sorting ----

    [Benchmark]
    public uint256 Sort_DefaultComparer()
    {
        _unsorted.CopyTo(_sortBuffer, 0);
        Array.Sort(_sortBuffer);
        return _sortBuffer[0];
    }

    [Benchmark]
    public uint256 Sort_ComparerInstance()
    {
        _unsorted.CopyTo(_sortBuffer, 0);
        Array.Sort(_sortBuffer, UInt256Comparer.Instance);
        return _sortBuffer[0];
    }
}
