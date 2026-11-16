using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.JsonConverters;
using EccentricWare.Web3.DataTypes.ValueConverters;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// Scalar operations on <see cref="uint256"/>. Operand shapes mirror real EVM data:
/// <c>Small</c> = one limb (1.5 ETH in wei), <c>Mid</c> = 100 bits (a token amount with 18 decimals),
/// <c>Wide</c> = a full 256-bit value (hash-sized). <c>BigInteger_*</c> rows are the reference the cold paths fall back to.
/// </summary>
[MemoryDiagnoser]
public class UInt256Benchmarks
{
    private uint256 _small;
    private uint256 _smallOther;
    private uint256 _mid;
    private uint256 _wide;
    private uint256 _wideOther;
    private uint256 _wideEqual;
    private uint256 _wideDiffersInLowLimb;
    private uint256 _wideDiffersInHighLimb;
    private uint256 _half1;          // 128-bit operands: product fits in 256 bits
    private uint256 _half2;
    private uint256 _divisor64;      // 10^18
    private uint256 _divisor128;     // exactly 128 bits wide
    private uint256 _divisor192;
    private uint256 _divisor256;       // 224 bits wide: four limbs, exercises the full Knuth path
    private ulong _factor64;
    private ulong _divisorU64;

    private BigInteger _bigWide;
    private BigInteger _bigHalf1;
    private BigInteger _bigHalf2;
    private BigInteger _bigDivisor64;
    private BigInteger _bigDivisor128;
    private BigInteger _bigDivisor192;
    private BigInteger _bigDivisor256;

    private byte[] _bigEndian32 = null!;
    private byte[] _bigEndian20 = null!;
    private byte[] _littleEndian32 = null!;

    private char[] _hexWideChars = null!;
    private char[] _hexSmallChars = null!;
    private byte[] _hexWideUtf8 = null!;
    private byte[] _hexSmallUtf8 = null!;
    private char[] _decWideChars = null!;
    private char[] _decSmallChars = null!;
    private char[] _decMidChars = null!;
    private byte[] _decMidUtf8 = null!;
    private byte[] _decWideUtf8 = null!;
    private byte[] _decSmallUtf8 = null!;

    private char[] _charDestination = null!;
    private byte[] _utf8Destination = null!;
    private byte[] _byteDestination = null!;

    private byte[] _jsonHexWide = null!;
    private byte[] _jsonHexSmall = null!;
    private byte[] _jsonNumberSmall = null!;
    private byte[] _jsonNumberWide = null!;
    private byte[] _jsonPropertyNameWide = null!;
    private uint256 _scaled;
    private readonly ArrayBufferWriter<byte> _jsonBuffer = new(128);
    private Utf8JsonWriter _jsonWriter = null!;
    private readonly UInt256JsonConverter _converter = new();
    private readonly JsonSerializerOptions _jsonOptions = new();

    private readonly UInt256BytesValueConverter _efConverter = new();
    private Func<uint256, byte[]> _efToProvider = null!;
    private Func<byte[], uint256> _efFromProvider = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(BenchmarkData.Seed);

        // Random limbs with the top bits forced on so the value really is 256 bits wide.
        _wide = new uint256(BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random) | 0x4000000000000000UL);
        _wideOther = new uint256(BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random) | 0x4000000000000000UL);
        _wideEqual = new uint256(_wide.ToBigEndianBytes());

        // Big-endian: the last byte is the lowest limb's low bits, the first byte the highest limb's high bits.
        byte[] lowFlipped = _wide.ToBigEndianBytes();
        lowFlipped[^1] ^= 0x01;
        _wideDiffersInLowLimb = new uint256(lowFlipped);
        byte[] highFlipped = _wide.ToBigEndianBytes();
        highFlipped[0] ^= 0x01;
        _wideDiffersInHighLimb = new uint256(highFlipped);

        _small = new uint256(1_500_000_000_000_000_000UL);
        _smallOther = new uint256(987_654_321UL);
        _mid = (uint256)(BigInteger.Pow(10, 30) + 12345);

        _half1 = new uint256(BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random) | 1UL << 62, 0, 0);
        _half2 = new uint256(BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random) | 1UL << 62, 0, 0);

        _divisor64 = new uint256(1_000_000_000_000_000_000UL);
        _divisor128 = (uint256)((BigInteger.One << 127) | 12345);
        _divisor256 = new uint256(BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), 0x0000_0000_FFFF_FFFFUL);
        _divisor192 = new uint256(BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random) | 1UL << 62, 0);
        _factor64 = 1_000_000_000_000_000_000UL;
        _divisorU64 = 1_000_000_000_000_000_000UL;

        _bigWide = _wide.ToBigInteger();
        _bigHalf1 = _half1.ToBigInteger();
        _bigHalf2 = _half2.ToBigInteger();
        _bigDivisor64 = _divisor64.ToBigInteger();
        _bigDivisor128 = _divisor128.ToBigInteger();
        _bigDivisor192 = _divisor192.ToBigInteger();
        _bigDivisor256 = _divisor256.ToBigInteger();

        _bigEndian32 = _wide.ToBigEndianBytes();
        _bigEndian20 = _bigEndian32.AsSpan(12).ToArray(); // address-sized input exercises the left-padding path
        _littleEndian32 = _wide.ToLittleEndianBytes();

        string hexWide = _wide.ToString();
        string hexSmall = _small.ToString();
        string decWide = _wide.ToDecimalString();
        string decSmall = _small.ToDecimalString();
        string decMid = _mid.ToDecimalString(); // 31 digits: a typical 18-decimal token amount
        _hexWideChars = hexWide.ToCharArray();
        _hexSmallChars = hexSmall.ToCharArray();
        _hexWideUtf8 = BenchmarkData.Ascii(hexWide);
        _hexSmallUtf8 = BenchmarkData.Ascii(hexSmall);
        _decWideChars = decWide.ToCharArray();
        _decSmallChars = decSmall.ToCharArray();
        _decMidChars = decMid.ToCharArray();
        _decMidUtf8 = BenchmarkData.Ascii(decMid);
        _decWideUtf8 = BenchmarkData.Ascii(decWide);
        _decSmallUtf8 = BenchmarkData.Ascii(decSmall);

        _charDestination = new char[80];
        _utf8Destination = new byte[80];
        _byteDestination = new byte[32];

        _jsonHexWide = BenchmarkData.Ascii("\"" + hexWide + "\"");
        _jsonHexSmall = BenchmarkData.Ascii("\"" + hexSmall + "\"");
        _jsonNumberSmall = BenchmarkData.Ascii(decSmall);
        _jsonNumberWide = BenchmarkData.Ascii(decWide);
        _jsonPropertyNameWide = BenchmarkData.Ascii("{\"" + hexWide + "\":1}");
        _scaled = _mid * _divisor64; // e.g. a token amount scaled by 10^18
        _jsonWriter = new Utf8JsonWriter(_jsonBuffer);

        _efToProvider = _efConverter.ConvertToProviderTyped;
        _efFromProvider = _efConverter.ConvertFromProviderTyped;

        // Sanity: the library must agree with BigInteger on every shape benchmarked, or the numbers are meaningless.
        BigInteger mask = (BigInteger.One << 256) - 1;
        if ((_half1 * _half2).ToBigInteger() != _bigHalf1 * _bigHalf2
            || uint256.MultiplyUnchecked(_wide, _wideOther).ToBigInteger() != (_bigWide * _wideOther.ToBigInteger()) % (BigInteger.One << 256)
            || (_wide / _divisor64).ToBigInteger() != _bigWide / _bigDivisor64
            || (_wide / _divisor128).ToBigInteger() != _bigWide / _bigDivisor128
            || (_wide / _divisor192).ToBigInteger() != _bigWide / _bigDivisor192
            || (_wide % _divisor192).ToBigInteger() != _bigWide % _bigDivisor192
            || (_wide / _divisor256).ToBigInteger() != _bigWide / _bigDivisor256
            || (_wide % _divisor256).ToBigInteger() != _bigWide % _bigDivisor256
            || uint256.AddUnchecked(_wide, _wideOther).ToBigInteger() != ((_bigWide + _wideOther.ToBigInteger()) & mask)
            || !uint256.TryParse(_hexWideChars, out var h) || h != _wide
            || !uint256.TryParse(_decWideChars, out var d) || d != _wide
            || !uint256.TryParse(_decWideUtf8, out var du) || du != _wide
            || new uint256(_bigEndian20).ToBigInteger() != (_bigWide & ((BigInteger.One << 160) - 1))
            || uint256.SubtractUnchecked(_wide, _wideOther).ToBigInteger() != (((_bigWide - _wideOther.ToBigInteger()) % (BigInteger.One << 256)) + (BigInteger.One << 256)) % (BigInteger.One << 256)
            || (_mid << 70).ToBigInteger() != (_mid.ToBigInteger() << 70) % (BigInteger.One << 256)
            || (_wide >> 70).ToBigInteger() != _bigWide >> 70
            || !_mid.TryMultiply(_factor64, out var scaled) || scaled.ToBigInteger() != _mid.ToBigInteger() * _factor64
            || _mid.DivRem(_divisorU64, out ulong midRemainder).ToBigInteger() != _mid.ToBigInteger() / _divisorU64 || new BigInteger(midRemainder) != _mid.ToBigInteger() % _divisorU64
            || _scaled / _divisor64 != _mid || _scaled % _divisor64 != uint256.Zero
            || !uint256.TryParse(_decMidChars, out var dm) || dm != _mid
            || !uint256.TryParse(_decMidUtf8, out var dmu) || dmu != _mid
            || _wide.ToString("D", CultureInfo.InvariantCulture) != _bigWide.ToString(CultureInfo.InvariantCulture)
            || _wide.ToString() != hexWide || new string(_hexWideChars) != hexWide
            || _small.ToString() != hexSmall || _mid.ToString("D", CultureInfo.InvariantCulture) != decMid
            || ReadJson(_jsonHexWide) != _wide || ReadJson(_jsonHexSmall) != _small
            || ReadJson(_jsonNumberSmall) != _small || ReadJson(_jsonNumberWide) != _wide
            || Json_ReadAsPropertyName_Wide() != _wide || Json_WriteAsPropertyName_Wide() != _hexWideUtf8.Length + 6 /* {"…":1} */
            || _efFromProvider(_efToProvider(_wide)) != _wide)
        {
            throw new InvalidOperationException("uint256 benchmark inputs do not agree with BigInteger.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _jsonWriter.Dispose();

    // ---- construction / byte conversion ----

    [Benchmark]
    public uint256 Ctor_FromBigEndian32() => new(_bigEndian32);

    [Benchmark]
    public uint256 Ctor_FromBigEndian20_Padded() => new(_bigEndian20);

    [Benchmark]
    public uint256 FromLittleEndian32() => uint256.FromLittleEndian(_littleEndian32);

    [Benchmark]
    public byte WriteBigEndian()
    {
        _wide.WriteBigEndian(_byteDestination);
        return _byteDestination[0];
    }

    [Benchmark]
    public byte WriteLittleEndian()
    {
        _wide.WriteLittleEndian(_byteDestination);
        return _byteDestination[0];
    }

    [Benchmark]
    public byte[] ToBigEndianBytes_Allocating() => _wide.ToBigEndianBytes();

    [Benchmark]
    public byte[] ToMinimalBigEndianBytes_Mid() => _mid.ToMinimalBigEndianBytes();

    [Benchmark]
    public int GetByteCount_Wide() => _wide.GetByteCount();

    // ---- equality / ordering / hashing ----

    [Benchmark]
    public bool Equals_Equal() => _wide.Equals(_wideEqual);

    [Benchmark]
    public bool Equals_DifferentInLowLimb() => _wide.Equals(_wideDiffersInLowLimb);

    [Benchmark]
    public int CompareTo_Equal() => _wide.CompareTo(_wideEqual);

    [Benchmark]
    public int CompareTo_DifferentInHighLimb() => _wide.CompareTo(_wideDiffersInHighLimb);

    [Benchmark]
    public int CompareTo_DifferentInLowLimb() => _wide.CompareTo(_wideDiffersInLowLimb);

    [Benchmark]
    public int GetHashCode_Wide() => _wide.GetHashCode();

    [Benchmark]
    public int GetHashCode_Small() => _small.GetHashCode();

    // ---- arithmetic: add / subtract ----

    [Benchmark]
    public uint256 Add_Checked_MidPlusSmall() => _mid + _small;

    [Benchmark]
    public uint256 AddUnchecked_Wide() => uint256.AddUnchecked(_wide, _wideOther);

    [Benchmark]
    public uint256 Subtract_Checked_WideMinusSmall() => _wide - _small;

    [Benchmark]
    public uint256 SubtractUnchecked_Wide() => uint256.SubtractUnchecked(_wide, _wideOther);

    // ---- arithmetic: multiply ----

    [Benchmark]
    public uint256 Multiply_Checked_SmallTimesSmall() => _small * _smallOther;

    [Benchmark]
    public uint256 Multiply_Checked_128x128() => _half1 * _half2;

    [Benchmark]
    public uint256 MultiplyUnchecked_Wide() => uint256.MultiplyUnchecked(_wide, _wideOther);

    [Benchmark]
    public uint256 Multiply_Checked_MidTimesPow10_18() => _mid * _divisor64;

    [Benchmark]
    public uint256 TryMultiply_MidByPow10_18() => _mid.TryMultiply(_factor64, out uint256 product) ? product : default;

    [Benchmark]
    public BigInteger BigInteger_Multiply_128x128() => _bigHalf1 * _bigHalf2;

    // ---- arithmetic: divide / modulo ----

    [Benchmark]
    public uint256 Divide_WideBy64BitDivisor() => _wide / _divisor64;

    [Benchmark]
    public uint256 Modulo_WideBy64BitDivisor() => _wide % _divisor64;

    [Benchmark]
    public uint256 DivRem_Ulong_Wide() => _wide.DivRem(_divisorU64, out _);

    [Benchmark]
    public uint256 DivRem_Ulong_MidBy1e18() => _mid.DivRem(_divisorU64, out _);

    [Benchmark]
    public uint256 Divide_ScaledAmountBy1e18() => _scaled / _divisor64;

    [Benchmark]
    public uint256 Divide_MidBy64BitDivisor() => _mid / _divisor64;

    [Benchmark]
    public uint256 DivRem_Ulong_Small() => _small.DivRem(_smallOther.Low64, out _);

    [Benchmark]
    public uint256 Divide_WideBy128BitDivisor() => _wide / _divisor128;

    [Benchmark]
    public uint256 Divide_WideBy192BitDivisor() => _wide / _divisor192;

    [Benchmark]
    public uint256 Divide_WideBy256BitDivisor() => _wide / _divisor256;

    [Benchmark]
    public (uint256, uint256) DivRem_WideBy256BitDivisor() => uint256.DivRem(_wide, _divisor256);

    [Benchmark]
    public BigInteger BigInteger_Divide_WideBy64BitDivisor() => _bigWide / _bigDivisor64;

    [Benchmark]
    public BigInteger BigInteger_Divide_WideBy256BitDivisor() => _bigWide / _bigDivisor256;

    // ---- bitwise / shifts ----

    [Benchmark]
    public uint256 And_Wide() => _wide & _wideOther;

    [Benchmark]
    public uint256 ShiftLeft_70() => _mid << 70;

    [Benchmark]
    public uint256 ShiftLeft_64_LimbAligned() => _mid << 64;

    [Benchmark]
    public uint256 ShiftRight_70() => _wide >> 70;

    [Benchmark]
    public int LeadingZeroCount_Wide() => _wide.LeadingZeroCount();

    [Benchmark]
    public int PopCount_Wide() => _wide.PopCount();

    // ---- parsing ----

    [Benchmark]
    public bool TryParse_HexChars_Wide() => uint256.TryParse(_hexWideChars, out _);

    [Benchmark]
    public bool TryParse_HexChars_Small() => uint256.TryParse(_hexSmallChars, out _);

    [Benchmark]
    public bool TryParse_HexUtf8_Wide() => uint256.TryParse(_hexWideUtf8, out _);

    [Benchmark]
    public bool TryParse_HexUtf8_Small() => uint256.TryParse(_hexSmallUtf8, out _);

    [Benchmark]
    public bool TryParse_DecimalChars_Wide() => uint256.TryParse(_decWideChars, out _);

    [Benchmark]
    public bool TryParse_DecimalChars_Small() => uint256.TryParse(_decSmallChars, out _);

    [Benchmark]
    public bool TryParse_DecimalUtf8_Wide() => uint256.TryParse(_decWideUtf8, out _);

    [Benchmark]
    public bool TryParse_DecimalUtf8_Small() => uint256.TryParse(_decSmallUtf8, out _);

    [Benchmark]
    public bool TryParse_DecimalChars_Mid() => uint256.TryParse(_decMidChars, out _);

    [Benchmark]
    public bool TryParse_DecimalUtf8_Mid() => uint256.TryParse(_decMidUtf8, out _);

    [Benchmark]
    public bool BigInteger_TryParse_DecimalChars_Wide() => BigInteger.TryParse(_decWideChars, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    // ---- formatting ----

    [Benchmark]
    public string ToString_Hex_Wide() => _wide.ToString();

    [Benchmark]
    public string ToString_Hex_Small() => _small.ToString();

    [Benchmark]
    public string ToString_Decimal_Wide() => _wide.ToString("D", CultureInfo.InvariantCulture);

    [Benchmark]
    public string ToString_Decimal_Mid() => _mid.ToString("D", CultureInfo.InvariantCulture);

    [Benchmark]
    public string ToString_Decimal_Small() => _small.ToString("D", CultureInfo.InvariantCulture);

    [Benchmark]
    public string ToFullHexString_Wide() => _wide.ToFullHexString();

    [Benchmark]
    public bool TryFormat_HexChars_Wide() => _wide.TryFormat(_charDestination, out _, "0x", CultureInfo.InvariantCulture);

    [Benchmark]
    public bool TryFormat_HexChars_Small() => _small.TryFormat(_charDestination, out _, "0x", CultureInfo.InvariantCulture);

    [Benchmark]
    public bool TryFormat_HexUtf8_Wide() => _wide.TryFormat(_utf8Destination, out _, "0x", CultureInfo.InvariantCulture);

    [Benchmark]
    public bool TryFormat_DecimalChars_Wide() => _wide.TryFormat(_charDestination, out _, "D", CultureInfo.InvariantCulture);

    // ---- BigInteger conversion ----

    [Benchmark]
    public BigInteger ToBigInteger_Wide() => _wide.ToBigInteger();

    [Benchmark]
    public uint256 FromBigInteger_Wide() => (uint256)_bigWide;

    // ---- JSON (converter invoked directly; JsonSerializer path is covered by unit tests) ----

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
    public int Json_Write_Small()
    {
        _jsonBuffer.ResetWrittenCount();
        _jsonWriter.Reset(_jsonBuffer);
        _converter.Write(_jsonWriter, _small, _jsonOptions);
        _jsonWriter.Flush();
        return _jsonBuffer.WrittenCount;
    }

    [Benchmark]
    public uint256 Json_Read_HexString_Wide() => ReadJson(_jsonHexWide);

    [Benchmark]
    public uint256 Json_Read_HexString_Small() => ReadJson(_jsonHexSmall);

    [Benchmark]
    public uint256 Json_Read_NumberToken_Small() => ReadJson(_jsonNumberSmall);

    [Benchmark]
    public uint256 Json_Read_NumberToken_Wide() => ReadJson(_jsonNumberWide);

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
    public uint256 Json_ReadAsPropertyName_Wide()
    {
        var reader = new Utf8JsonReader(_jsonPropertyNameWide);
        reader.Read(); // StartObject
        reader.Read(); // PropertyName
        return _converter.ReadAsPropertyName(ref reader, typeof(uint256), _jsonOptions);
    }

    private uint256 ReadJson(byte[] json)
    {
        var reader = new Utf8JsonReader(json);
        reader.Read();
        return _converter.Read(ref reader, typeof(uint256), _jsonOptions);
    }

    // ---- EF Core value converter (model <-> 32-byte provider value) ----

    [Benchmark]
    public byte[] EfConverter_ToProvider() => _efToProvider(_wide);

    [Benchmark]
    public uint256 EfConverter_FromProvider() => _efFromProvider(_bigEndian32);
}
