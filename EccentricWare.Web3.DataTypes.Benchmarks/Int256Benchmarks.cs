using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.JsonConverters;
using EccentricWare.Web3.DataTypes.ValueConverters;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// Scalar operations on <see cref="int256"/>. Operand shapes: <c>Small</c> = a small negative/positive delta (one limb),
/// <c>Wide</c> = a full 256-bit magnitude (negative and positive variants). Multiply, divide and modulo currently go
/// through <see cref="BigInteger"/>, so the <c>BigInteger_*</c> rows show how much of each figure is the fallback itself.
/// </summary>
[MemoryDiagnoser]
public class Int256Benchmarks
{
    private int256 _smallNegative;
    private int256 _smallPositive;
    private int256 _widePositive;
    private int256 _widePositiveOther;
    private int256 _wideNegative;
    private int256 _wideNegativeEqual;
    private int256 _wideNegativeDiffersInLowLimb;
    private int256 _half1;           // ~120-bit magnitudes: product fits in int256
    private int256 _half2Negative;
    private int256 _divisorSmall;
    private int256 _divisorWide;
    private long _longValue;

    private BigInteger _bigWideNegative;
    private BigInteger _bigHalf1;
    private BigInteger _bigHalf2Negative;
    private BigInteger _bigDivisorSmall;
    private BigInteger _bigDivisorWide;

    private byte[] _bigEndian32 = null!;
    private byte[] _bigEndianShort = null!;

    private char[] _decNegativeChars = null!;
    private char[] _decSmallNegativeChars = null!;
    private byte[] _decNegativeUtf8 = null!;
    private byte[] _decSmallNegativeUtf8 = null!;
    private char[] _hexNegativeChars = null!;
    private byte[] _hexNegativeUtf8 = null!;
    private char[] _hexPositiveChars = null!;

    private char[] _charDestination = null!;
    private byte[] _utf8Destination = null!;
    private byte[] _byteDestination = null!;

    private byte[] _jsonHexNegative = null!;
    private byte[] _jsonHexPositive = null!;
    private byte[] _jsonNumberNegative = null!;
    private byte[] _jsonNumberSmallNegative = null!;
    private readonly ArrayBufferWriter<byte> _jsonBuffer = new(128);
    private Utf8JsonWriter _jsonWriter = null!;
    private readonly Int256JsonConverter _converter = new();
    private readonly JsonSerializerOptions _jsonOptions = new();

    private readonly Int256BytesValueConverter _efConverter = new();
    private Func<int256, byte[]> _efToProvider = null!;
    private Func<byte[], int256> _efFromProvider = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(BenchmarkData.Seed);

        // Magnitudes below 2^255, forced wide (bit 253 set) so every limb is populated.
        ulong Top() => (BenchmarkData.NextUInt64(random) & 0x3FFFFFFFFFFFFFFFUL) | 0x2000000000000000UL;
        _widePositive = new int256(BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), Top());
        _widePositiveOther = new int256(BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), Top());
        _wideNegative = int256.NegateUnchecked(new int256(BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), BenchmarkData.NextUInt64(random), Top()));

        byte[] negativeBytes = _wideNegative.ToBigEndianBytes();
        _wideNegativeEqual = new int256(negativeBytes);
        byte[] flipped = (byte[])negativeBytes.Clone();
        flipped[^1] ^= 0x01;
        _wideNegativeDiffersInLowLimb = new int256(flipped);

        _smallNegative = new int256(-1_500_000_000_000_000_000L);
        _smallPositive = new int256(987_654_321L);
        _longValue = -123_456_789L;

        _half1 = (int256)(BigInteger.Pow(10, 36) + 12345);
        _half2Negative = (int256)(-(BigInteger.Pow(10, 36) + 6789));
        _divisorSmall = new int256(-1_000_000_000_000_000_000L);
        _divisorWide = (int256)(-(BigInteger.One << 200) - 99);

        _bigWideNegative = _wideNegative.ToBigInteger();
        _bigHalf1 = _half1.ToBigInteger();
        _bigHalf2Negative = _half2Negative.ToBigInteger();
        _bigDivisorSmall = _divisorSmall.ToBigInteger();
        _bigDivisorWide = _divisorWide.ToBigInteger();

        _bigEndian32 = negativeBytes;
        _bigEndianShort = new byte[] { 0xFF, 0xFE, 0x70 }; // sign-extended from 3 bytes

        string decNegative = _wideNegative.ToString();
        string decSmall = _smallNegative.ToString();
        string hexNegative = _wideNegative.ToString("0x", CultureInfo.InvariantCulture);
        string hexPositive = _widePositive.ToString("0x", CultureInfo.InvariantCulture);
        _decNegativeChars = decNegative.ToCharArray();
        _decSmallNegativeChars = decSmall.ToCharArray();
        _decNegativeUtf8 = BenchmarkData.Ascii(decNegative);
        _decSmallNegativeUtf8 = BenchmarkData.Ascii(decSmall);
        _hexNegativeChars = hexNegative.ToCharArray();
        _hexNegativeUtf8 = BenchmarkData.Ascii(hexNegative);
        _hexPositiveChars = hexPositive.ToCharArray();

        _charDestination = new char[80];
        _utf8Destination = new byte[80];
        _byteDestination = new byte[32];

        _jsonHexNegative = BenchmarkData.Ascii("\"" + hexNegative + "\"");
        _jsonHexPositive = BenchmarkData.Ascii("\"" + hexPositive + "\"");
        _jsonNumberNegative = BenchmarkData.Ascii(decNegative);
        _jsonNumberSmallNegative = BenchmarkData.Ascii(decSmall);
        _jsonWriter = new Utf8JsonWriter(_jsonBuffer);

        _efToProvider = _efConverter.ConvertToProviderTyped;
        _efFromProvider = _efConverter.ConvertFromProviderTyped;

        // Sanity: the library must agree with BigInteger on every shape benchmarked.
        if ((_half1 * _half2Negative).ToBigInteger() != _bigHalf1 * _bigHalf2Negative
            || (_wideNegative / _divisorSmall).ToBigInteger() != BigInteger.Divide(_bigWideNegative, _bigDivisorSmall)
            || (_wideNegative % _divisorWide).ToBigInteger() != BigInteger.Remainder(_bigWideNegative, _bigDivisorWide)
            || !int256.TryParse(_decNegativeChars, null, out var d) || d != _wideNegative
            || !int256.TryParse(_hexNegativeChars, null, out var h) || h != _wideNegative
            || !int256.TryParse(_hexNegativeUtf8, out var hu) || hu != _wideNegative
            || !int256.TryParse(_decNegativeUtf8, out var du) || du != _wideNegative
            || new int256(_bigEndianShort).ToBigInteger() != new BigInteger(-400)
            || (long)new int256(_longValue) != _longValue
            || (_wideNegative >> 70).ToBigInteger() != _bigWideNegative >> 70
            || (_smallPositive << 70).ToBigInteger() != _smallPositive.ToBigInteger() << 70
            || (_widePositive - _smallPositive).ToBigInteger() != _widePositive.ToBigInteger() - _smallPositive.ToBigInteger()
            || (-_wideNegative).ToBigInteger() != -_bigWideNegative
            || _wideNegative.CompareTo(_widePositive) >= 0 || _widePositive.CompareTo(_wideNegative) <= 0
            || _efFromProvider(_efToProvider(_wideNegative)) != _wideNegative)
        {
            throw new InvalidOperationException("int256 benchmark inputs do not agree with BigInteger.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _jsonWriter.Dispose();

    // ---- construction / byte conversion ----

    [Benchmark]
    public int256 Ctor_FromLong() => new(_longValue);

    [Benchmark]
    public int256 Ctor_FromBigEndian32() => new(_bigEndian32);

    [Benchmark]
    public int256 Ctor_FromBigEndianShort_SignExtended() => new(_bigEndianShort);

    [Benchmark]
    public byte WriteBigEndian()
    {
        _wideNegative.WriteBigEndian(_byteDestination);
        return _byteDestination[0];
    }

    [Benchmark]
    public byte WriteLittleEndian()
    {
        _wideNegative.WriteLittleEndian(_byteDestination);
        return _byteDestination[0];
    }

    [Benchmark]
    public byte[] ToBigEndianBytes_Allocating() => _wideNegative.ToBigEndianBytes();

    // ---- equality / ordering / hashing ----

    [Benchmark]
    public bool Equals_Equal() => _wideNegative.Equals(_wideNegativeEqual);

    [Benchmark]
    public bool Equals_DifferentInLowLimb() => _wideNegative.Equals(_wideNegativeDiffersInLowLimb);

    [Benchmark]
    public int CompareTo_OppositeSigns() => _wideNegative.CompareTo(_widePositive);

    [Benchmark]
    public int CompareTo_SameSign_DifferentInLowLimb() => _wideNegative.CompareTo(_wideNegativeDiffersInLowLimb);

    [Benchmark]
    public int CompareTo_Equal() => _wideNegative.CompareTo(_wideNegativeEqual);

    [Benchmark]
    public int GetHashCode_Wide() => _wideNegative.GetHashCode();

    // ---- arithmetic ----

    [Benchmark]
    public int256 Add_Checked_Wide() => _wideNegative + _widePositive;

    [Benchmark]
    public int256 AddUnchecked_Wide() => int256.AddUnchecked(_widePositive, _widePositiveOther);

    [Benchmark]
    public int256 Subtract_Checked_WideMinusSmall() => _widePositive - _smallPositive;

    [Benchmark]
    public int256 SubtractUnchecked_Wide() => int256.SubtractUnchecked(_widePositive, _widePositiveOther);

    [Benchmark]
    public int256 Negate_Checked_Wide() => -_wideNegative;

    [Benchmark]
    public int256 Multiply_Checked_SmallTimesSmall() => _smallNegative * _smallPositive;

    [Benchmark]
    public int256 Multiply_Checked_120BitTimes120Bit() => _half1 * _half2Negative;

    [Benchmark]
    public BigInteger BigInteger_Multiply_120BitTimes120Bit() => _bigHalf1 * _bigHalf2Negative;

    [Benchmark]
    public int256 Divide_WideBySmall() => _wideNegative / _divisorSmall;

    [Benchmark]
    public int256 Divide_WideByWide() => _wideNegative / _divisorWide;

    [Benchmark]
    public int256 Modulo_WideBySmall() => _wideNegative % _divisorSmall;

    [Benchmark]
    public BigInteger BigInteger_Divide_WideBySmall() => BigInteger.Divide(_bigWideNegative, _bigDivisorSmall);

    [Benchmark]
    public BigInteger BigInteger_Divide_WideByWide() => BigInteger.Divide(_bigWideNegative, _bigDivisorWide);

    // ---- bitwise / shifts ----

    [Benchmark]
    public int256 And_Wide() => _wideNegative & _widePositive;

    [Benchmark]
    public int256 ShiftLeft_70() => _smallPositive << 70;

    [Benchmark]
    public int256 ShiftRight_Arithmetic_70_Negative() => _wideNegative >> 70;

    [Benchmark]
    public int256 ShiftRight_Arithmetic_64_Aligned_Negative() => _wideNegative >> 64;

    // ---- conversions ----

    [Benchmark]
    public long ExplicitToLong_InRange() => (long)_smallNegative;

    [Benchmark]
    public BigInteger ToBigInteger_Wide() => _wideNegative.ToBigInteger();

    [Benchmark]
    public int256 FromBigInteger_Negative() => (int256)_bigWideNegative;

    // ---- parsing ----

    [Benchmark]
    public bool TryParse_DecimalChars_WideNegative() => int256.TryParse(_decNegativeChars, null, out _);

    [Benchmark]
    public bool TryParse_DecimalChars_SmallNegative() => int256.TryParse(_decSmallNegativeChars, null, out _);

    [Benchmark]
    public bool TryParse_DecimalUtf8_WideNegative() => int256.TryParse(_decNegativeUtf8, out _);

    [Benchmark]
    public bool TryParse_DecimalUtf8_SmallNegative() => int256.TryParse(_decSmallNegativeUtf8, out _);

    [Benchmark]
    public bool TryParse_HexChars_NegativeTwosComplement() => int256.TryParse(_hexNegativeChars, null, out _);

    [Benchmark]
    public bool TryParse_HexChars_Positive() => int256.TryParse(_hexPositiveChars, null, out _);

    [Benchmark]
    public bool TryParse_HexUtf8_NegativeTwosComplement() => int256.TryParse(_hexNegativeUtf8, out _);

    // ---- formatting ----

    [Benchmark]
    public string ToString_Decimal_WideNegative() => _wideNegative.ToString();

    [Benchmark]
    public string ToString_Hex_WideNegative() => _wideNegative.ToString("0x", CultureInfo.InvariantCulture);

    [Benchmark]
    public string ToString_Hex_WidePositive() => _widePositive.ToString("0x", CultureInfo.InvariantCulture);

    [Benchmark]
    public bool TryFormat_DecimalChars_WideNegative() => _wideNegative.TryFormat(_charDestination, out _, "D", CultureInfo.InvariantCulture);

    [Benchmark]
    public bool TryFormat_HexChars_WideNegative() => _wideNegative.TryFormat(_charDestination, out _, "0x", CultureInfo.InvariantCulture);

    [Benchmark]
    public bool TryFormat_HexChars_WidePositive() => _widePositive.TryFormat(_charDestination, out _, "0x", CultureInfo.InvariantCulture);

    [Benchmark]
    public bool TryFormat_HexUtf8_WideNegative() => _wideNegative.TryFormat(_utf8Destination, out _, "0x", CultureInfo.InvariantCulture);

    // ---- JSON (converter invoked directly; JsonSerializer path is covered by unit tests) ----

    [Benchmark]
    public int Json_Write_WideNegative()
    {
        _jsonBuffer.ResetWrittenCount();
        _jsonWriter.Reset(_jsonBuffer);
        _converter.Write(_jsonWriter, _wideNegative, _jsonOptions);
        _jsonWriter.Flush();
        return _jsonBuffer.WrittenCount;
    }

    [Benchmark]
    public int Json_Write_WidePositive()
    {
        _jsonBuffer.ResetWrittenCount();
        _jsonWriter.Reset(_jsonBuffer);
        _converter.Write(_jsonWriter, _widePositive, _jsonOptions);
        _jsonWriter.Flush();
        return _jsonBuffer.WrittenCount;
    }

    [Benchmark]
    public int256 Json_Read_HexString_Negative() => ReadJson(_jsonHexNegative);

    [Benchmark]
    public int256 Json_Read_HexString_Positive() => ReadJson(_jsonHexPositive);

    [Benchmark]
    public int256 Json_Read_NumberToken_SmallNegative() => ReadJson(_jsonNumberSmallNegative);

    [Benchmark]
    public int256 Json_Read_NumberToken_WideNegative() => ReadJson(_jsonNumberNegative);

    private int256 ReadJson(byte[] json)
    {
        var reader = new Utf8JsonReader(json);
        reader.Read();
        return _converter.Read(ref reader, typeof(int256), _jsonOptions);
    }

    // ---- EF Core value converter ----

    [Benchmark]
    public byte[] EfConverter_ToProvider() => _efToProvider(_wideNegative);

    [Benchmark]
    public int256 EfConverter_FromProvider() => _efFromProvider(_bigEndian32);
}
