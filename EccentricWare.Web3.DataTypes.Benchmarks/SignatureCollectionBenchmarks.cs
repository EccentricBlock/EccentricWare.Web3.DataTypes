using BenchmarkDotNet.Attributes;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// <see cref="Signature"/> as a dictionary / set key and as a sort key. The struct is 72 bytes, so copy cost
/// matters as much as hash quality here. Each Dictionary/HashSet benchmark loops over all <c>Count</c> keys:
/// divide by <c>Count</c> for a per-lookup figure.
/// </summary>
[MemoryDiagnoser]
public class SignatureCollectionBenchmarks
{
    [Params(8, 1024, 65536)]
    public int Count { get; set; }

    private Signature[] _items = null!;
    private Signature[] _missKeys = null!;
    private Dictionary<Signature, int> _dictionary = null!;
    private HashSet<Signature> _set = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(BenchmarkData.Seed);
        _items = new Signature[Count];
        Span<byte> buffer = stackalloc byte[Signature.EvmByteLength];
        for (int i = 0; i < _items.Length; i++)
        {
            random.NextBytes(buffer);
            // Mixed population: roughly half EVM, half Solana, as a multi-chain indexer would see.
            _items[i] = (i & 1) == 0
                ? Signature.FromEvmBytes(buffer)
                : Signature.FromSolanaBytes(buffer.Slice(0, Signature.SolanaByteLength));
        }

        _dictionary = new Dictionary<Signature, int>(Count);
        _set = new HashSet<Signature>(Count);
        for (int i = 0; i < _items.Length; i++)
        {
            _dictionary[_items[i]] = i;
            _set.Add(_items[i]);
        }

        _missKeys = new Signature[Count];
        for (int i = 0; i < _missKeys.Length; i++)
        {
            do
            {
                random.NextBytes(buffer);
                _missKeys[i] = (i & 1) == 0
                    ? Signature.FromEvmBytes(buffer)
                    : Signature.FromSolanaBytes(buffer.Slice(0, Signature.SolanaByteLength));
            }
            while (_set.Contains(_missKeys[i]));
        }
    }

    [Benchmark]
    public int Dictionary_TryGetValue_AllHits()
    {
        int sum = 0;
        var dictionary = _dictionary;
        foreach (Signature key in _items)
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
        foreach (Signature key in _missKeys)
        {
            if (set.Contains(key))
                hits++;
        }
        return hits;
    }

    [Benchmark]
    public int GetHashCode_All()
    {
        int h = 0;
        foreach (Signature key in _items)
            h ^= key.GetHashCode();
        return h;
    }

    [Benchmark]
    public int Sort_Copy()
    {
        Signature[] copy = (Signature[])_items.Clone();
        Array.Sort(copy);
        return copy.Length;
    }
}
