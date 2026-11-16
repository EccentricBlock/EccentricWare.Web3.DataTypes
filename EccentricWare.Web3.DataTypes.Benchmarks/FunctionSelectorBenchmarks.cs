using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.JsonConverters;
using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>Scalar operations on <see cref="FunctionSelector"/>.</summary>
[MemoryDiagnoser]
public class FunctionSelectorBenchmarks
{
    private const string ShortSignature = "transfer(address,uint256)";

    // Uniswap V3 exactInputSingle: a common real-world tuple signature (82 chars, 82 UTF-8 bytes).
    private const string TupleSignature =
        "exactInputSingle((address,address,uint24,address,uint256,uint256,uint256,uint160))";

    private static readonly string VeryLongSignature =
        "f(" + string.Join(",", Enumerable.Repeat("uint256", 200)) + ")";

    private FunctionSelector _value;
    private byte[] _bytes = null!;
    private byte[] _calldataBytes = null!;
    private byte[] _hexUtf8 = null!;
    private byte[] _calldataHexUtf8 = null!;
    private char[] _hexChars = null!;
    private string _hexString = null!;
    private byte[] _shortSignatureUtf8 = null!;
    private byte[] _jsonQuotedHex = null!;
    private char[] _charDestination = null!;
    private byte[] _utf8Destination = null!;
    private byte[] _byteDestination = null!;

    private readonly ArrayBufferWriter<byte> _jsonBuffer = new(64);
    private Utf8JsonWriter _jsonWriter = null!;
    private readonly FunctionSelectorJsonConverter _converter = new();
    private readonly JsonSerializerOptions _jsonOptions = new();

    [GlobalSetup]
    public void Setup()
    {
        _value = new FunctionSelector(0xa9059cbb);
        _bytes = [0xa9, 0x05, 0x9c, 0xbb];
        _calldataBytes = BenchmarkData.Calldata(68);
        _hexString = _value.ToString();
        _hexChars = _hexString.ToCharArray();
        _hexUtf8 = BenchmarkData.Ascii(_hexString);
        _calldataHexUtf8 = BenchmarkData.Ascii("0x" + BenchmarkData.HexLower(BenchmarkData.Calldata(68)));
        _shortSignatureUtf8 = BenchmarkData.Ascii(ShortSignature);
        _jsonQuotedHex = BenchmarkData.Ascii("\"" + _hexString + "\"");
        _charDestination = new char[10];
        _utf8Destination = new byte[10];
        _byteDestination = new byte[4];
        _jsonWriter = new Utf8JsonWriter(_jsonBuffer);

        if (FunctionSelector.FromSignature(ShortSignature) != _value
            || FunctionSelector.Parse(_hexChars) != _value
            || !FunctionSelector.TryParse(_hexUtf8, out var parsed) || parsed != _value)
        {
            throw new InvalidOperationException("Benchmark inputs do not round-trip.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _jsonWriter.Dispose();

    // ---- construction ----

    [Benchmark]
    public FunctionSelector Ctor_FromBytes() => new(_bytes);

    [Benchmark]
    public FunctionSelector Ctor_FromFourBytes() => new(_bytes[0], _bytes[1], _bytes[2], _bytes[3]);

    [Benchmark]
    public byte WriteBytes()
    {
        _value.WriteBytes(_byteDestination);
        return _byteDestination[0];
    }

    [Benchmark]
    public bool TryFromCalldata() => FunctionSelector.TryFromCalldata(_calldataBytes, out _);

    // ---- keccak-derived selectors ----

    [Benchmark]
    public FunctionSelector FromSignature_String_Short() => FunctionSelector.FromSignature(ShortSignature);

    [Benchmark]
    public FunctionSelector FromSignature_Utf8_Short() => FunctionSelector.FromSignature(_shortSignatureUtf8);

    [Benchmark]
    public FunctionSelector FromSignature_String_Tuple() => FunctionSelector.FromSignature(TupleSignature);

    [Benchmark]
    public FunctionSelector FromSignature_String_VeryLong() => FunctionSelector.FromSignature(VeryLongSignature);

    // ---- parsing ----

    [Benchmark]
    public bool TryParse_Chars() => FunctionSelector.TryParse(_hexChars, out _);

    [Benchmark]
    public bool TryParse_Utf8() => FunctionSelector.TryParse(_hexUtf8, out _);

    [Benchmark]
    public FunctionSelector Parse_String() => FunctionSelector.Parse(_hexString);

    [Benchmark]
    public bool TryParseFromCalldataHexUtf8() => FunctionSelector.TryParseFromCalldataHexUtf8(_calldataHexUtf8, out _);

    // ---- formatting ----

    [Benchmark]
    public string ToString_Default() => _value.ToString();

    [Benchmark]
    public string ToString_UpperNoPrefix() => _value.ToString("X", CultureInfo.InvariantCulture);

    [Benchmark]
    public bool TryFormat_Chars() => _value.TryFormat(_charDestination, out _);

    [Benchmark]
    public bool TryFormat_Utf8() => _value.TryFormat(_utf8Destination, out _);

    // ---- JSON (converter invoked directly) ----

    [Benchmark]
    public int Json_Write()
    {
        _jsonBuffer.ResetWrittenCount();
        _jsonWriter.Reset(_jsonBuffer);
        _converter.Write(_jsonWriter, _value, _jsonOptions);
        _jsonWriter.Flush();
        return _jsonBuffer.WrittenCount;
    }

    [Benchmark]
    public FunctionSelector Json_Read()
    {
        var reader = new Utf8JsonReader(_jsonQuotedHex);
        reader.Read();
        return _converter.Read(ref reader, typeof(FunctionSelector), _jsonOptions);
    }
}

/// <summary>Allow-list style scans and lookups over many selectors.</summary>
[MemoryDiagnoser]
public class FunctionSelectorCollectionBenchmarks
{
    [Params(8, 64, 1024, 65536)]
    public int Count { get; set; }

    private FunctionSelector[] _unsorted = null!;
    private FunctionSelector[] _sorted = null!;
    private FunctionSelector _lastUnsorted;
    private FunctionSelector _missing;
    private FunctionSelector _middleSorted;
    private Dictionary<FunctionSelector, int> _dictionary = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(BenchmarkData.Seed);
        var seen = new HashSet<uint>();
        _unsorted = new FunctionSelector[Count];
        for (int i = 0; i < Count; i++)
        {
            uint v;
            do { v = (uint)random.NextInt64(0, uint.MaxValue); } while (!seen.Add(v));
            _unsorted[i] = new FunctionSelector(v);
        }

        _sorted = (FunctionSelector[])_unsorted.Clone();
        Array.Sort(_sorted);
        _lastUnsorted = _unsorted[^1];
        _middleSorted = _sorted[Count / 2];

        uint missing;
        do { missing = (uint)random.NextInt64(0, uint.MaxValue); } while (seen.Contains(missing));
        _missing = new FunctionSelector(missing);

        _dictionary = new Dictionary<FunctionSelector, int>(Count);
        for (int i = 0; i < Count; i++)
            _dictionary[_unsorted[i]] = i;
    }

    [Benchmark]
    public bool ContainsSimd_Miss() => FunctionSelector.ContainsSimd(_unsorted, _missing);

    [Benchmark]
    public bool ContainsSimd_HitAtEnd() => FunctionSelector.ContainsSimd(_unsorted, _lastUnsorted);

    [Benchmark]
    public int BinarySearchSorted_Hit() => FunctionSelector.BinarySearchSorted(_sorted, _middleSorted);

    [Benchmark]
    public int BinarySearchSorted_Miss() => FunctionSelector.BinarySearchSorted(_sorted, _missing);

    [Benchmark]
    public int Dictionary_TryGetValue_AllHits()
    {
        int sum = 0;
        var dictionary = _dictionary;
        foreach (FunctionSelector key in _unsorted)
        {
            if (dictionary.TryGetValue(key, out int v))
                sum += v;
        }
        return sum;
    }
}
