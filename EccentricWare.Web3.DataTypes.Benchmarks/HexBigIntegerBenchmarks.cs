using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.JsonConverters;
using EccentricWare.Web3.DataTypes.ValueConverters;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// Scalar operations on <see cref="HexBigInteger"/>. Operand shapes mirror JSON-RPC traffic:
/// <c>Tiny</c> = a block number or gas figure (fits in 32 bits), <c>Small</c> = 1.5 ETH in wei (one 64-bit limb),
/// <c>Wide</c> = a hash-sized 256-bit value with the top bit set (the shape that exposes sign-bit mistakes),
/// <c>Huge</c> = 2,048 bits, past every stack threshold. <c>BigInteger_*</c> rows are the reference the library is built on.
/// </summary>
[MemoryDiagnoser]
public class HexBigIntegerBenchmarks
{
    private HexBigInteger _tiny;
    private HexBigInteger _small;
    private HexBigInteger _wide;
    private HexBigInteger _wideNegative;
    private HexBigInteger _huge;
    private HexBigInteger _wideOther;
    private HexBigInteger _wideEqual;

    private long _longValue = 0x10d4f2L; // a field, so the JIT cannot constant-fold the constructor away
    private BigInteger _bigWide;

    private string _hexTinyString = null!;
    private string _hexWideString = null!;
    private string _hexWideDigitsWithLeadingZero = null!; // BigInteger hex parsing is two's complement: a top nibble >= 8 needs a leading 0
    private char[] _hexTinyChars = null!;
    private char[] _hexSmallChars = null!;
    private char[] _hexWideChars = null!;
    private char[] _hexWideNegativeChars = null!;
    private char[] _hexHugeChars = null!;

    private byte[] _hexTinyUtf8 = null!;
    private byte[] _hexSmallUtf8 = null!;
    private byte[] _hexWideUtf8 = null!;
    private byte[] _hexWideNegativeUtf8 = null!;
    private byte[] _hexWideQuotedUtf8 = null!;
    private byte[] _hexHugeUtf8 = null!;

    private byte[] _bigEndian32 = null!;

    private char[] _charDestination = null!;
    private byte[] _utf8Destination = null!;

    private byte[] _jsonHexTiny = null!;
    private byte[] _jsonHexWide = null!;
    private byte[] _jsonHexHuge = null!;
    private byte[] _jsonNumberSmall = null!;
    private byte[] _jsonNumberWide = null!;
    private byte[] _jsonNumberUInt64Max = null!;
    private byte[] _jsonHexWideEscaped = null!;
    private byte[] _hex17Utf8 = null!;
    private byte[] _hexSmallNegativeUtf8 = null!;
    private byte[] _jsonPropertyNameWide = null!;
    private readonly ArrayBufferWriter<byte> _jsonBuffer = new(8192);
    private Utf8JsonWriter _jsonWriter = null!;
    private readonly HexBigIntegerJsonConverter _converter = new();
    private readonly JsonSerializerOptions _jsonOptions = new();

    private readonly HexBigIntegerToBytesConverter _efConverter = new();
    private Func<HexBigInteger, byte[]> _efToProvider = null!;
    private Func<byte[], HexBigInteger> _efFromProvider = null!;
    private byte[] _efProviderWide = null!;

    [GlobalSetup]
    public void Setup()
    {
        byte[] wideBytes = BenchmarkData.RandomBytes(32);
        wideBytes[0] |= 0x80; // top bit set: a 256-bit value whose leading hex digit is >= 8
        byte[] otherBytes = BenchmarkData.RandomBytes(32, seed: BenchmarkData.Seed + 1);
        byte[] hugeBytes = BenchmarkData.RandomBytes(256, seed: BenchmarkData.Seed + 2);
        hugeBytes[0] |= 0x80;

        _bigEndian32 = wideBytes;
        _bigWide = new BigInteger(wideBytes, isUnsigned: true, isBigEndian: true);

        _tiny = new HexBigInteger(0x10d4f2L);                 // block number
        _small = new HexBigInteger(0x14d1120d7b160000UL);     // 1.5 ETH in wei
        _wide = new HexBigInteger(_bigWide);
        _wideNegative = new HexBigInteger(-_bigWide);
        _wideOther = new HexBigInteger(new BigInteger(otherBytes, isUnsigned: true, isBigEndian: true));
        _wideEqual = new HexBigInteger(_bigWide);
        _huge = new HexBigInteger(new BigInteger(hugeBytes, isUnsigned: true, isBigEndian: true));

        string hexTiny = "0x10d4f2";
        string hexSmall = "0x14d1120d7b160000";
        string hexWide = "0x" + BenchmarkData.HexLower(wideBytes);
        string hexHuge = "0x" + BenchmarkData.HexLower(hugeBytes);
        string hexWideNegative = "-" + hexWide;

        _hexTinyString = hexTiny;
        _hexWideString = hexWide;
        _hexWideDigitsWithLeadingZero = "0" + hexWide.Substring(2);
        _hexTinyChars = hexTiny.ToCharArray();
        _hexSmallChars = hexSmall.ToCharArray();
        _hexWideChars = hexWide.ToCharArray();
        _hexWideNegativeChars = hexWideNegative.ToCharArray();
        _hexHugeChars = hexHuge.ToCharArray();

        _hexTinyUtf8 = BenchmarkData.Ascii(hexTiny);
        _hexSmallUtf8 = BenchmarkData.Ascii(hexSmall);
        _hexWideUtf8 = BenchmarkData.Ascii(hexWide);
        _hexWideNegativeUtf8 = BenchmarkData.Ascii(hexWideNegative);
        _hexWideQuotedUtf8 = BenchmarkData.Ascii("\"" + hexWide + "\"");
        _hexHugeUtf8 = BenchmarkData.Ascii(hexHuge);

        _charDestination = new char[hexHuge.Length + 8];
        _utf8Destination = new byte[hexHuge.Length + 8];

        _jsonHexTiny = BenchmarkData.Ascii("\"" + hexTiny + "\"");
        _jsonHexWide = BenchmarkData.Ascii("\"" + hexWide + "\"");
        _jsonHexHuge = BenchmarkData.Ascii("\"" + hexHuge + "\"");
        _jsonNumberSmall = BenchmarkData.Ascii(_small.ToString("d", CultureInfo.InvariantCulture));
        _jsonNumberWide = BenchmarkData.Ascii(_wide.ToString("d", CultureInfo.InvariantCulture));
        _jsonNumberUInt64Max = BenchmarkData.Ascii(ulong.MaxValue.ToString(CultureInfo.InvariantCulture));
        _jsonHexWideEscaped = BenchmarkData.Ascii("\"\\u0030" + hexWide.Substring(1) + "\""); // leading "0" escaped, so the reader takes the slow path
        _hex17Utf8 = BenchmarkData.Ascii("0x1" + hexSmall.Substring(2)); // 17 digits: first size past the single-limb parse
        _hexSmallNegativeUtf8 = BenchmarkData.Ascii("-" + hexSmall);
        _jsonPropertyNameWide = BenchmarkData.Ascii("{\"" + hexWide + "\":1}");
        _jsonWriter = new Utf8JsonWriter(_jsonBuffer);

        _efToProvider = _efConverter.ConvertToProviderTyped;
        _efFromProvider = _efConverter.ConvertFromProviderTyped;
        _efProviderWide = _efToProvider(_wide);

        // Sanity: the library must agree with BigInteger on every shape benchmarked, or the numbers are meaningless.
        // The reference is built independently (BenchmarkData hex, BigInteger arithmetic), not from the library's own output.
        if (_wide.Value != _bigWide
            || !HexBigInteger.TryParse(_hexTinyChars, null, out var t) || t != _tiny
            || !HexBigInteger.TryParse(_hexSmallChars, null, out var s) || s != _small
            || !HexBigInteger.TryParse(_hexWideChars, null, out var w) || w != _wide
            || !HexBigInteger.TryParse(_hexWideNegativeChars, null, out var wn) || wn != _wideNegative
            || !HexBigInteger.TryParse(_hexHugeChars, null, out var h) || h != _huge
            || !HexBigInteger.TryParseUtf8(_hexTinyUtf8, out var tu) || tu != _tiny
            || !HexBigInteger.TryParseUtf8(_hexSmallUtf8, out var su) || su != _small
            || !HexBigInteger.TryParseUtf8(_hexWideUtf8, out var wu) || wu != _wide
            || !HexBigInteger.TryParseUtf8(_hexWideNegativeUtf8, out var wnu) || wnu != _wideNegative
            || !HexBigInteger.TryParseUtf8(_hexWideQuotedUtf8, out var wq) || wq != _wide
            || !HexBigInteger.TryParseUtf8(_hexHugeUtf8, out var hu) || hu != _huge
            || _tiny.ToString() != hexTiny || _small.ToString() != hexSmall
            || _wide.ToString() != hexWide || _wideNegative.ToString() != hexWideNegative || _huge.ToString() != hexHuge
            || _wide.ToString("d", CultureInfo.InvariantCulture) != _bigWide.ToString(CultureInfo.InvariantCulture)
            || Format_Utf8_Wide() != _hexWideUtf8.Length || Format_Chars_Wide() != _hexWideChars.Length
            || Format_Utf8_Huge() != _hexHugeUtf8.Length || Format_Chars_Huge() != _hexHugeChars.Length
            || Format_Utf8_Decimal_Wide() != _jsonNumberWide.Length
            || ReadJson(_jsonHexTiny) != _tiny || ReadJson(_jsonHexWide) != _wide || ReadJson(_jsonHexHuge) != _huge
            || !HexBigInteger.TryParseUtf8(_hex17Utf8, out var h17) || h17.Value != BigInteger.Parse("1" + hexSmall.Substring(2), NumberStyles.HexNumber)
            || !HexBigInteger.TryParseUtf8(_hexSmallNegativeUtf8, out var sn) || sn != new HexBigInteger(-_small.Value)
            || ReadJson(_jsonNumberUInt64Max).Value != ulong.MaxValue || ReadJson(_jsonHexWideEscaped) != _wide
            || ReadJson(_jsonNumberSmall) != _small || ReadJson(_jsonNumberWide) != _wide
            || Json_ReadAsPropertyName_Wide() != _wide
            || Json_Write_Wide() != _hexWideUtf8.Length + 2 || Json_Write_Huge() != _hexHugeUtf8.Length + 2
            || Json_WriteAsPropertyName_Wide() != _hexWideUtf8.Length + 6 /* {"…":1} */
            || _efFromProvider(_efProviderWide) != _wide || _efFromProvider(_efToProvider(_wideNegative)) != _wideNegative
            || new HexBigInteger(_bigEndian32, isUnsigned: true) != _wide)
        {
            throw new InvalidOperationException("HexBigInteger benchmark inputs do not agree with BigInteger.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _jsonWriter.Dispose();

    // ---- construction ----

    [Benchmark]
    public HexBigInteger Ctor_FromLong() => new(_longValue);

    [Benchmark]
    public HexBigInteger Ctor_FromBigEndian32() => new(_bigEndian32, isUnsigned: true);

    // ---- equality / ordering / hashing ----

    [Benchmark]
    public bool Equals_Wide() => _wide.Equals(_wideEqual);

    [Benchmark]
    public int CompareTo_Wide() => _wide.CompareTo(_wideOther);

    [Benchmark]
    public int GetHashCode_Wide() => _wide.GetHashCode();

    [Benchmark]
    public int MagnitudeByteCount_Wide() => _wide.MagnitudeByteCount;

    // ---- parsing: chars ----

    [Benchmark]
    public HexBigInteger TryParse_Chars_Tiny() => HexBigInteger.TryParse(_hexTinyChars, null, out var v) ? v : default;

    [Benchmark]
    public HexBigInteger TryParse_Chars_Small() => HexBigInteger.TryParse(_hexSmallChars, null, out var v) ? v : default;

    [Benchmark]
    public HexBigInteger TryParse_Chars_Wide() => HexBigInteger.TryParse(_hexWideChars, null, out var v) ? v : default;

    [Benchmark]
    public HexBigInteger TryParse_Chars_WideNegative() => HexBigInteger.TryParse(_hexWideNegativeChars, null, out var v) ? v : default;

    [Benchmark]
    public HexBigInteger TryParse_Chars_Huge() => HexBigInteger.TryParse(_hexHugeChars, null, out var v) ? v : default;

    [Benchmark]
    public HexBigInteger Parse_String_Wide() => HexBigInteger.Parse(_hexWideString, null);

    // ---- parsing: UTF-8 ----

    [Benchmark]
    public HexBigInteger TryParseUtf8_Tiny() => HexBigInteger.TryParseUtf8(_hexTinyUtf8, out var v) ? v : default;

    [Benchmark]
    public HexBigInteger TryParseUtf8_Small() => HexBigInteger.TryParseUtf8(_hexSmallUtf8, out var v) ? v : default;

    [Benchmark]
    public HexBigInteger TryParseUtf8_SmallNegative() => HexBigInteger.TryParseUtf8(_hexSmallNegativeUtf8, out var v) ? v : default;

    /// <summary>17 digits: one past the single-limb fast path.</summary>
    [Benchmark]
    public HexBigInteger TryParseUtf8_Digits17() => HexBigInteger.TryParseUtf8(_hex17Utf8, out var v) ? v : default;

    [Benchmark]
    public HexBigInteger TryParseUtf8_Wide() => HexBigInteger.TryParseUtf8(_hexWideUtf8, out var v) ? v : default;

    [Benchmark]
    public HexBigInteger TryParseUtf8_WideNegative() => HexBigInteger.TryParseUtf8(_hexWideNegativeUtf8, out var v) ? v : default;

    [Benchmark]
    public HexBigInteger TryParseUtf8_WideQuoted() => HexBigInteger.TryParseUtf8(_hexWideQuotedUtf8, out var v) ? v : default;

    [Benchmark]
    public HexBigInteger TryParseUtf8_Huge() => HexBigInteger.TryParseUtf8(_hexHugeUtf8, out var v) ? v : default;

    /// <summary>Reference: the BCL's own hex parse of the same digits.</summary>
    [Benchmark]
    public BigInteger BigInteger_ParseHex_Wide() => BigInteger.Parse(_hexWideDigitsWithLeadingZero, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    // ---- formatting ----

    [Benchmark]
    public string ToString_Tiny() => _tiny.ToString();

    [Benchmark]
    public string ToString_Small() => _small.ToString();

    [Benchmark]
    public string ToString_Wide() => _wide.ToString();

    [Benchmark]
    public string ToString_WideNegative() => _wideNegative.ToString();

    [Benchmark]
    public string ToString_Huge() => _huge.ToString();

    [Benchmark]
    public string ToString_Decimal_Wide() => _wide.ToString("d", CultureInfo.InvariantCulture);

    [Benchmark]
    public int Format_Utf8_Tiny() => _tiny.TryFormat(_utf8Destination, out int written, default, null) ? written : -1;

    [Benchmark]
    public int Format_Utf8_Small() => _small.TryFormat(_utf8Destination, out int written, default, null) ? written : -1;

    [Benchmark]
    public int Format_Utf8_Wide() => _wide.TryFormat(_utf8Destination, out int written, default, null) ? written : -1;

    [Benchmark]
    public int Format_Utf8_WideNegative() => _wideNegative.TryFormat(_utf8Destination, out int written, default, null) ? written : -1;

    [Benchmark]
    public int Format_Utf8_Huge() => _huge.TryFormat(_utf8Destination, out int written, default, null) ? written : -1;

    [Benchmark]
    public int Format_Utf8_Decimal_Wide() => _wide.TryFormat(_utf8Destination, out int written, "d", CultureInfo.InvariantCulture) ? written : -1;

    [Benchmark]
    public int Format_Chars_Tiny() => _tiny.TryFormat(_charDestination, out int written, default, null) ? written : -1;

    [Benchmark]
    public int Format_Chars_Wide() => _wide.TryFormat(_charDestination, out int written, default, null) ? written : -1;

    [Benchmark]
    public int Format_Chars_Huge() => _huge.TryFormat(_charDestination, out int written, default, null) ? written : -1;

    /// <summary>Reference: the BCL's hex formatting of the same value (it yields a leading "0" for a top nibble of 8 or more).</summary>
    [Benchmark]
    public string BigInteger_ToHexString_Wide() => _bigWide.ToString("x", CultureInfo.InvariantCulture);

    // ---- JSON (converter invoked directly; JsonSerializer path is covered by unit tests) ----

    [Benchmark]
    public int Json_Write_Tiny()
    {
        _jsonBuffer.ResetWrittenCount();
        _jsonWriter.Reset(_jsonBuffer);
        _converter.Write(_jsonWriter, _tiny, _jsonOptions);
        _jsonWriter.Flush();
        return _jsonBuffer.WrittenCount;
    }

    [Benchmark]
    public int Json_Write_Wide()
    {
        _jsonBuffer.ResetWrittenCount();
        _jsonWriter.Reset(_jsonBuffer);
        _converter.Write(_jsonWriter, _wide, _jsonOptions);
        _jsonWriter.Flush();
        return _jsonBuffer.WrittenCount;
    }

    [Benchmark]
    public int Json_Write_Huge()
    {
        _jsonBuffer.ResetWrittenCount();
        _jsonWriter.Reset(_jsonBuffer);
        _converter.Write(_jsonWriter, _huge, _jsonOptions);
        _jsonWriter.Flush();
        return _jsonBuffer.WrittenCount;
    }

    [Benchmark]
    public int Json_WriteAsPropertyName_Wide()
    {
        _jsonBuffer.ResetWrittenCount();
        _jsonWriter.Reset(_jsonBuffer);
        _jsonWriter.WriteStartObject();
        _converter.WriteAsPropertyName(_jsonWriter, _wide, _jsonOptions);
        _jsonWriter.WriteNumberValue(1);
        _jsonWriter.WriteEndObject();
        _jsonWriter.Flush();
        return _jsonBuffer.WrittenCount;
    }

    [Benchmark]
    public HexBigInteger Json_Read_HexString_Tiny() => ReadJson(_jsonHexTiny);

    [Benchmark]
    public HexBigInteger Json_Read_HexString_Wide() => ReadJson(_jsonHexWide);

    [Benchmark]
    public HexBigInteger Json_Read_HexString_Huge() => ReadJson(_jsonHexHuge);

    [Benchmark]
    public HexBigInteger Json_Read_NumberToken_Small() => ReadJson(_jsonNumberSmall);

    /// <summary>Above int64, so it reaches <c>TryGetUInt64</c>.</summary>
    [Benchmark]
    public HexBigInteger Json_Read_NumberToken_UInt64Max() => ReadJson(_jsonNumberUInt64Max);

    /// <summary>An escape sequence in the token sends it through the shared unescaping slow path.</summary>
    [Benchmark]
    public HexBigInteger Json_Read_HexString_Wide_Escaped() => ReadJson(_jsonHexWideEscaped);

    [Benchmark]
    public HexBigInteger Json_Read_NumberToken_Wide() => ReadJson(_jsonNumberWide);

    [Benchmark]
    public HexBigInteger Json_ReadAsPropertyName_Wide()
    {
        var reader = new Utf8JsonReader(_jsonPropertyNameWide);
        reader.Read(); // StartObject
        reader.Read(); // PropertyName
        return _converter.ReadAsPropertyName(ref reader, typeof(HexBigInteger), _jsonOptions);
    }

    private HexBigInteger ReadJson(byte[] json)
    {
        var reader = new Utf8JsonReader(json);
        reader.Read();
        return _converter.Read(ref reader, typeof(HexBigInteger), _jsonOptions);
    }

    // ---- EF Core value converter (model <-> signed big-endian byte[]) ----

    [Benchmark]
    public byte[] EfConverter_ToProvider_Wide() => _efToProvider(_wide);

    [Benchmark]
    public byte[] EfConverter_ToProvider_Small() => _efToProvider(_small);

    [Benchmark]
    public HexBigInteger EfConverter_FromProvider_Wide() => _efFromProvider(_efProviderWide);
}
