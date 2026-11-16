using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.Comparers;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>Bulk operations over many <see cref="int256"/> values: hash-table lookups and sorting (mixed signs).</summary>
[MemoryDiagnoser]
public class Int256CollectionBenchmarks
{
    [Params(8, 1024, 65536)]
    public int Count { get; set; }

    private int256[] _values = null!;
    private int256[] _sequential = null!;
    private int256[] _sortBuffer = null!;
    private Dictionary<int256, int> _dictionary = null!;
    private Dictionary<int256, int> _dictionaryWithComparer = null!;
    private Dictionary<int256, int> _sequentialDictionary = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(BenchmarkData.Seed);

        // Random two's complement limbs: roughly half the values are negative.
        _values = new int256[Count];
        for (int i = 0; i < _values.Length; i++)
            _values[i] = new int256(BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random));

        // Signed deltas around zero (-N/2 .. N/2): only low limb and sign extension vary.
        _sequential = new int256[Count];
        for (int i = 0; i < _sequential.Length; i++)
            _sequential[i] = new int256((long)i - Count / 2);

        _sortBuffer = new int256[Count];
        _dictionary = new Dictionary<int256, int>(Count);
        _dictionaryWithComparer = new Dictionary<int256, int>(Count, Int256Comparer.Instance);
        _sequentialDictionary = new Dictionary<int256, int>(Count);
        for (int i = 0; i < Count; i++)
        {
            _dictionary[_values[i]] = i;
            _dictionaryWithComparer[_values[i]] = i;
            _sequentialDictionary[_sequential[i]] = i;
        }

        if (_dictionary.Count != Count || _sequentialDictionary.Count != Count)
            throw new InvalidOperationException("int256 collection benchmark inputs contain duplicate keys.");
    }

    // ---- hash tables (each benchmark performs Count lookups: divide by Count for a per-lookup figure) ----

    [Benchmark]
    public int Dictionary_Random_AllHits()
    {
        int sum = 0;
        Dictionary<int256, int> dictionary = _dictionary;
        foreach (int256 key in _values)
            sum += dictionary[key];

        return sum;
    }

    [Benchmark]
    public int Dictionary_Random_AllHits_WithComparerInstance()
    {
        int sum = 0;
        Dictionary<int256, int> dictionary = _dictionaryWithComparer;
        foreach (int256 key in _values)
            sum += dictionary[key];

        return sum;
    }

    [Benchmark]
    public int Dictionary_SignedSequential_AllHits()
    {
        int sum = 0;
        Dictionary<int256, int> dictionary = _sequentialDictionary;
        foreach (int256 key in _sequential)
            sum += dictionary[key];

        return sum;
    }

    // ---- sorting ----

    [Benchmark]
    public int256 Sort_DefaultComparer()
    {
        _values.CopyTo(_sortBuffer, 0);
        Array.Sort(_sortBuffer);
        return _sortBuffer[0];
    }

    [Benchmark]
    public int256 Sort_ComparerInstance()
    {
        _values.CopyTo(_sortBuffer, 0);
        Array.Sort(_sortBuffer, Int256Comparer.Instance);
        return _sortBuffer[0];
    }
}
