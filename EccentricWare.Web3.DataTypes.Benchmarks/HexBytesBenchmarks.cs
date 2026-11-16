using System.Buffers;
using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.JsonConverters;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>Operations on <see cref="HexBytes"/> across payload sizes typical for EVM data.</summary>
[MemoryDiagnoser]
public class HexBytesBenchmarks
{
    // 4 = selector only, 68 = approve()/transfer() calldata, 1024 = small event/ABI payload, 24576 = max contract size.
    [Params(4, 68, 1024, 24576)]
    public int Size { get; set; }

    private byte[] _bytes = null!;
    private HexBytes _value;
    private HexBytes _equalCopy;
    private HexBytes _differentAtEnd;
    private HexBytes _other;

    private string _hexPrefixed = null!;
    private char[] _hexChars = null!;
    private byte[] _hexUtf8 = null!;
    private byte[] _jsonQuotedHex = null!;

    private char[] _charDestination = null!;
    private byte[] _utf8Destination = null!;
    private byte[] _byteDestination = null!;

    private readonly ArrayBufferWriter<byte> _jsonBuffer = new(1024);
    private Utf8JsonWriter _jsonWriter = null!;
    private readonly HexBytesJsonConverter _converter = new();
    private readonly JsonSerializerOptions _jsonOptions = new();

    [GlobalSetup]
    public void Setup()
    {
        _bytes = BenchmarkData.Calldata(Size);
        _value = new HexBytes(_bytes);
        _equalCopy = new HexBytes(_bytes);

        byte[] different = (byte[])_bytes.Clone();
        different[^1] ^= 0x01; // differs only in the final byte: forces a full scan in equality/comparison
        _differentAtEnd = new HexBytes(different);

        _other = new HexBytes(BenchmarkData.Calldata(Size, seed: 7));

        _hexPrefixed = _value.ToString();
        _hexChars = _hexPrefixed.ToCharArray();
        _hexUtf8 = BenchmarkData.Ascii(_hexPrefixed);
        _jsonQuotedHex = BenchmarkData.Ascii("\"" + _hexPrefixed + "\"");

        _charDestination = new char[_hexPrefixed.Length];
        _utf8Destination = new byte[_hexPrefixed.Length];
        _byteDestination = new byte[Size];

        _jsonWriter = new Utf8JsonWriter(_jsonBuffer);

        if (HexBytes.Parse(_hexChars) != _value || !HexBytes.TryParse(_hexUtf8, out var utf8Parsed) || utf8Parsed != _value)
            throw new InvalidOperationException("Benchmark inputs do not round-trip.");
    }

    [GlobalCleanup]
    public void Cleanup() => _jsonWriter.Dispose();

    // ---- construction ----

    [Benchmark]
    public HexBytes Ctor_FromArray_Copies() => new(_bytes);

    [Benchmark]
    public HexBytes Ctor_FromSpan_Copies() => new(_bytes.AsSpan());

    [Benchmark]
    public bool TryCopyTo() => _value.TryCopyTo(_byteDestination, out _);

    // ---- parsing ----

    [Benchmark]
    public HexBytes Parse_Chars() => HexBytes.Parse(_hexChars);

    [Benchmark]
    public bool TryParse_Utf8() => HexBytes.TryParse(_hexUtf8, out _);

    // ---- formatting ----

    [Benchmark]
    public string ToString_Default() => _value.ToString();

    [Benchmark]
    public bool TryFormat_Chars() => _value.TryFormat(_charDestination, out _);

    [Benchmark]
    public bool TryFormat_Utf8() => _value.TryFormat(_utf8Destination, out _);

    // ---- equality / ordering / hashing ----

    [Benchmark]
    public bool Equals_Equal() => _value.Equals(_equalCopy);

    [Benchmark]
    public bool Equals_DifferentAtEnd() => _value.Equals(_differentAtEnd);

    [Benchmark]
    public int CompareTo_DifferentAtEnd() => _value.CompareTo(_differentAtEnd);

    [Benchmark]
    public int CompareTo_Random() => _value.CompareTo(_other);

    [Benchmark]
    public int GetHashCode_() => _value.GetHashCode();

    // ---- slicing / concat / EVM helpers ----

    [Benchmark]
    public HexBytes Slice_DropSelector() => _value.Slice(Math.Min(4, Size));

    [Benchmark]
    public HexBytes Concat() => HexBytes.Concat(_value, _equalCopy);

    [Benchmark]
    public HexBytes PadToWord() => _value.PadToWord();

    [Benchmark]
    public bool TryGetFunctionSelector() => _value.TryGetFunctionSelector(out _);

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
    public HexBytes Json_Read()
    {
        var reader = new Utf8JsonReader(_jsonQuotedHex);
        reader.Read();
        return _converter.Read(ref reader, typeof(HexBytes), _jsonOptions);
    }
}

/// <summary>
/// Dictionary behaviour of <see cref="HexBytes"/> keys. Uses approve(spender, MAX) calldata, where only the
/// middle of the payload differs, so hash quality (not just equality speed) decides the cost.
/// </summary>
[MemoryDiagnoser]
public class HexBytesDictionaryBenchmarks
{
    private const int KeyCount = 1024;

    private HexBytes[] _keys = null!;
    private Dictionary<HexBytes, int> _dictionary = null!;

    [GlobalSetup]
    public void Setup()
    {
        _keys = new HexBytes[KeyCount];
        _dictionary = new Dictionary<HexBytes, int>(KeyCount);
        for (int i = 0; i < KeyCount; i++)
        {
            _keys[i] = new HexBytes(BenchmarkData.ApproveMaxCalldata(spenderSeed: i + 1));
            _dictionary[_keys[i]] = i;
        }
    }

    [Benchmark(OperationsPerInvoke = KeyCount)]
    public int TryGetValue_ApproveMaxCalldata()
    {
        int sum = 0;
        var dictionary = _dictionary;
        foreach (HexBytes key in _keys)
        {
            if (dictionary.TryGetValue(key, out int v))
                sum += v;
        }
        return sum;
    }

    [Benchmark]
    public int DistinctHashCodes()
    {
        var seen = new HashSet<int>(KeyCount);
        foreach (HexBytes key in _keys)
            seen.Add(key.GetHashCode());
        return seen.Count;
    }
}
