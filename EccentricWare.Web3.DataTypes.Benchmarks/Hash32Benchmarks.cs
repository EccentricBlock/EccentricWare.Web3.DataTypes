using System.Buffers;
using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.JsonConverters;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>Scalar (single value) operations on <see cref="Hash32"/>: parse, format, convert, compare, JSON.</summary>
[MemoryDiagnoser]
public class Hash32Benchmarks
{
    private byte[] _bigEndian = null!;
    private byte[] _littleEndian = null!;
    private Hash32 _value;
    private Hash32 _equal;
    private Hash32 _different;

    private string _hexPrefixed = null!;
    private char[] _hexChars = null!;
    private byte[] _hexUtf8 = null!;
    private byte[] _base58Utf8 = null!;
    private byte[] _base64Utf8 = null!;

    private byte[] _jsonQuotedHex = null!;
    private readonly ArrayBufferWriter<byte> _jsonBuffer = new(128);
    private Utf8JsonWriter _jsonWriter = null!;
    private readonly Hash32JsonConverter _converter = new();
    private readonly JsonSerializerOptions _jsonOptions = new();

    private char[] _charDestination = null!;
    private byte[] _utf8Destination = null!;
    private byte[] _byteDestination = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Random limbs with a non-zero most-significant limb (a typical hash / public key).
        var random = new Random(BenchmarkData.Seed);
        ulong u0 = (ulong)random.NextInt64() | 0x8000000000000000UL;
        ulong u1 = (ulong)random.NextInt64();
        ulong u2 = (ulong)random.NextInt64();
        ulong u3 = (ulong)random.NextInt64();

        _value = new Hash32(u0, u1, u2, u3);
        _bigEndian = _value.ToBigEndianBytes();
        _equal = new Hash32(_bigEndian);
        _littleEndian = _value.ToLittleEndianBytes();

        byte[] differentBytes = (byte[])_bigEndian.Clone();
        differentBytes[^1] ^= 0x01; // differs only in the last limb: worst case for short-circuit equality
        _different = new Hash32(differentBytes);

        _hexPrefixed = _value.ToString();
        _hexChars = _hexPrefixed.ToCharArray();
        _hexUtf8 = BenchmarkData.Ascii(_hexPrefixed);

        // Base58 of the same 32 bytes, produced by the library's own encoder (round-trip verified in the unit tests).
        Span<byte> base58 = stackalloc byte[Address.MaxBase58Length];
        int written = Utils.ByteUtils.EncodeBase58UInt256BigEndianToUtf8(u0, u1, u2, u3, base58);
        if (written <= 0) throw new InvalidOperationException("Base58 encode failed during benchmark setup.");
        _base58Utf8 = base58.Slice(0, written).ToArray();

        _base64Utf8 = BenchmarkData.Ascii(Convert.ToBase64String(_bigEndian));

        _jsonQuotedHex = BenchmarkData.Ascii("\"" + _hexPrefixed + "\"");
        _jsonWriter = new Utf8JsonWriter(_jsonBuffer);

        _charDestination = new char[66];
        _utf8Destination = new byte[66];
        _byteDestination = new byte[Hash32.ByteLength];

        // Sanity: every parse path must agree, otherwise the numbers are meaningless.
        if (Hash32.Parse(_hexChars) != _value
            || !Hash32.TryParseHexUtf8(_hexUtf8, out var h) || h != _value
            || !Hash32.TryParseBase58(_base58Utf8, out var b) || b != _value
            || !Hash32.TryParseBase64(_base64Utf8, out var c) || c != _value)
        {
            throw new InvalidOperationException("Benchmark inputs do not round-trip.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _jsonWriter.Dispose();

    // ---- construction / byte conversion ----

    [Benchmark]
    public Hash32 Ctor_FromBigEndianBytes() => new(_bigEndian);

    [Benchmark]
    public Hash32 FromLittleEndian() => Hash32.FromLittleEndian(_littleEndian);

    [Benchmark]
    public byte WriteBigEndian()
    {
        _value.WriteBigEndian(_byteDestination);
        return _byteDestination[0];
    }

    [Benchmark]
    public byte WriteLittleEndian()
    {
        _value.WriteLittleEndian(_byteDestination);
        return _byteDestination[0];
    }

    // ---- parsing ----

    [Benchmark]
    public Hash32 Parse_HexChars() => Hash32.Parse(_hexChars);

    [Benchmark]
    public bool TryParse_HexChars() => Hash32.TryParse(_hexChars, out _);

    [Benchmark]
    public bool TryParse_HexUtf8() => Hash32.TryParseHexUtf8(_hexUtf8, out _);

    [Benchmark]
    public bool TryParse_Base58() => Hash32.TryParseBase58(_base58Utf8, out _);

    // The public 32-byte utility (does not enforce canonical form); Hash32.TryParseBase58 uses a different, canonical decoder.
    [Benchmark]
    public bool ByteUtils_TryDecodeBase58To32() => Utils.ByteUtils.TryDecodeBase58To32(_base58Utf8, _byteDestination);

    [Benchmark]
    public bool TryParse_Base64() => Hash32.TryParseBase64(_base64Utf8, out _);

    [Benchmark]
    public bool TryParseAuto_Hex() => Hash32.TryParseAuto(_hexUtf8, out _);

    [Benchmark]
    public bool TryParseAuto_Base58() => Hash32.TryParseAuto(_base58Utf8, out _);

    // ---- formatting ----

    [Benchmark]
    public string ToString_Default() => _value.ToString();

    [Benchmark]
    public string ToString_NoPrefixUpper() => _value.ToString("X", CultureInfo.InvariantCulture);

    [Benchmark]
    public bool TryFormat_Chars() => _value.TryFormat(_charDestination, out _);

    [Benchmark]
    public bool TryFormat_Chars_ExplicitFormat() => _value.TryFormat(_charDestination, out _, "0x", CultureInfo.InvariantCulture);

    [Benchmark]
    public bool TryFormat_Utf8() => _value.TryFormat(_utf8Destination, out _);

    [Benchmark]
    public bool TryFormat_Utf8_ExplicitFormat() => _value.TryFormat(_utf8Destination, out _, "0x", CultureInfo.InvariantCulture);

    // ---- equality / ordering / hashing ----

    [Benchmark]
    public bool Equals_Equal() => _value.Equals(_equal);

    [Benchmark]
    public bool Equals_DifferentInLastLimb() => _value.Equals(_different);

    [Benchmark]
    public int CompareTo_Equal() => _value.CompareTo(_equal);

    [Benchmark]
    public int CompareTo_DifferentInLastLimb() => _value.CompareTo(_different);

    [Benchmark]
    public int GetHashCode_() => _value.GetHashCode();

    // ---- JSON (converter invoked directly; JsonSerializer path is covered by unit tests) ----

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
    public Hash32 Json_Read()
    {
        var reader = new Utf8JsonReader(_jsonQuotedHex);
        reader.Read();
        return _converter.Read(ref reader, typeof(Hash32), _jsonOptions);
    }
}
