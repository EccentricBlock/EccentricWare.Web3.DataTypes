using BenchmarkDotNet.Attributes;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>Bulk operations over many <see cref="Hash32"/> values: span scans and hash-set/dictionary lookups.</summary>
[MemoryDiagnoser]
public class Hash32CollectionBenchmarks
{
    // 8 is the SIMD threshold in Hash32.IndexOf; 1024 fits L1/L2; 65536 (2 MiB) exercises memory bandwidth.
    [Params(8, 1024, 65536)]
    public int Count { get; set; }

    private Hash32[] _haystack = null!;
    private Hash32 _lastItem;
    private Hash32 _missing;
    private Hash32[] _missKeys = null!;
    private Hash32 _repeated;
    private Hash32[] _repeatedHaystack = null!;
    private Dictionary<Hash32, int> _dictionary = null!;
    private HashSet<Hash32> _set = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(BenchmarkData.Seed);
        _haystack = new Hash32[Count];
        Span<byte> buffer = stackalloc byte[Hash32.ByteLength];
        for (int i = 0; i < _haystack.Length; i++)
        {
            random.NextBytes(buffer);
            _haystack[i] = new Hash32(buffer);
        }

        _lastItem = _haystack[^1];
        random.NextBytes(buffer);
        _missing = new Hash32(buffer);

        // 1-in-8 items equal the needle: CountEquals must visit every element either way.
        _repeated = _haystack[0];
        _repeatedHaystack = (Hash32[])_haystack.Clone();
        for (int i = 0; i < _repeatedHaystack.Length; i += 8)
            _repeatedHaystack[i] = _repeated;

        _dictionary = new Dictionary<Hash32, int>(Count);
        _set = new HashSet<Hash32>(Count);
        for (int i = 0; i < _haystack.Length; i++)
        {
            _dictionary[_haystack[i]] = i;
            _set.Add(_haystack[i]);
        }

        // Keys guaranteed absent from the set: random values re-rolled on the (astronomically unlikely) collision.
        _missKeys = new Hash32[Count];
        for (int i = 0; i < _missKeys.Length; i++)
        {
            do
            {
                random.NextBytes(buffer);
                _missKeys[i] = new Hash32(buffer);
            }
            while (_set.Contains(_missKeys[i]));
        }
    }

    [Benchmark]
    public int IndexOf_HitAtEnd() => Hash32.IndexOf(_haystack, _lastItem);

    [Benchmark]
    public int IndexOf_Miss() => Hash32.IndexOf(_haystack, _missing);

    [Benchmark]
    public int CountEquals_OneInEight() => Hash32.CountEquals(_repeatedHaystack, _repeated);

    [Benchmark]
    public int Dictionary_TryGetValue_AllHits()
    {
        int sum = 0;
        var dictionary = _dictionary;
        foreach (Hash32 key in _haystack)
        {
            if (dictionary.TryGetValue(key, out int v))
                sum += v;
        }
        return sum;
    }

    [Benchmark]
    public int HashSet_Contains_AllMisses()
    {
        int hits = 0;
        var set = _set;
        foreach (Hash32 key in _missKeys)
        {
            if (set.Contains(key))
                hits++;
        }
        return hits;
    }

    [Benchmark]
    public int Sort_Copy()
    {
        Hash32[] copy = (Hash32[])_haystack.Clone();
        Array.Sort(copy);
        return copy.Length;
    }
}
