using System.Buffers;
using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.JsonConverters;
using EccentricWare.Web3.DataTypes.ValueConverters;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// Scalar (single value) operations on <see cref="Address"/>, for both families: construction, byte conversion,
/// parsing, formatting (including EIP-55 checksums), equality, ordering, hashing, JSON converter and EF Core value converter.
/// </summary>
[MemoryDiagnoser]
public class AddressBenchmarks
{
    private byte[] _evmBytes = null!;
    private byte[] _solanaBytes = null!;
    private Address _evm;
    private Address _evmEqual;
    private Address _evmDifferent;
    private Address _solana;
    private Address _solanaEqual;
    private Address _solanaDifferent;

    private string _evmHex = null!;           // 0x + 40 lowercase hex
    private string _evmHexNoPrefix = null!;   // 40 lowercase hex containing '0' (92% of real addresses)
    private string _evmHexNoPrefixNoZero = null!; // 40 lowercase hex with no '0': ambiguous with Base58, so Base58 is tried first
    private byte[] _evmHexNoPrefixNoZeroUtf8 = null!;
    private string _solanaBase58 = null!;
    private char[] _evmHexChars = null!;
    private char[] _solanaBase58Chars = null!;
    private byte[] _evmHexUtf8 = null!;
    private byte[] _evmHexNoPrefixUtf8 = null!;
    private byte[] _evmHexQuotedUtf8 = null!;
    private byte[] _solanaBase58Utf8 = null!;
    private byte[] _solanaBase58QuotedUtf8 = null!;

    private byte[] _evmEfPayload = null!;
    private byte[] _solanaEfPayload = null!;

    private readonly ArrayBufferWriter<byte> _jsonBuffer = new(128);
    private Utf8JsonWriter _jsonWriter = null!;
    private readonly AddressJsonConverter _converter = new();
    private readonly JsonSerializerOptions _jsonOptions = new();

    private char[] _charDestination = null!;
    private byte[] _utf8Destination = null!;
    private byte[] _byteDestination = null!;

    [GlobalSetup]
    public void Setup()
    {
        // A random 20-byte address whose hex contains a '0' nibble (92% of real addresses): the unprefixed form takes the
        // "contains '0', so it cannot be Base58" shortcut. The no-'0' form (the other 8%) is built separately below.
        byte[] evm = BenchmarkData.RandomBytes(Address.EvmByteLength);
        evm[0] = 0x0A;
        _evmBytes = evm;
        _solanaBytes = BenchmarkData.RandomBytes(Address.SolanaByteLength, BenchmarkData.Seed + 1);
        _solanaBytes[0] |= 0x80; // no leading zero byte: the common 43/44-character form

        _evm = Address.FromEvmBytes(_evmBytes);
        _evmEqual = Address.FromEvmBytes(_evmBytes);
        byte[] evmDifferent = (byte[])_evmBytes.Clone();
        evmDifferent[^1] ^= 0x01; // differs only in the last byte: worst case for short-circuit equality
        _evmDifferent = Address.FromEvmBytes(evmDifferent);

        _solana = Address.FromSolanaBytes(_solanaBytes);
        _solanaEqual = Address.FromSolanaBytes(_solanaBytes);
        byte[] solanaDifferent = (byte[])_solanaBytes.Clone();
        solanaDifferent[^1] ^= 0x01;
        _solanaDifferent = Address.FromSolanaBytes(solanaDifferent);

        // The other 8%: re-roll until the hex text has no '0' digit.
        byte[] noZero = new byte[Address.EvmByteLength];
        for (int seed = 1; ; seed++)
        {
            new Random(seed).NextBytes(noZero);
            _evmHexNoPrefixNoZero = Convert.ToHexStringLower(noZero);
            if (!_evmHexNoPrefixNoZero.Contains('0'))
                break;
        }
        _evmHexNoPrefixNoZeroUtf8 = BenchmarkData.Ascii(_evmHexNoPrefixNoZero);

        _evmHex = _evm.ToString();
        _evmHexNoPrefix = _evmHex.Substring(2);
        _solanaBase58 = _solana.ToString();

        _evmHexChars = _evmHex.ToCharArray();
        _solanaBase58Chars = _solanaBase58.ToCharArray();
        _evmHexUtf8 = BenchmarkData.Ascii(_evmHex);
        _evmHexNoPrefixUtf8 = BenchmarkData.Ascii(_evmHexNoPrefix);
        _evmHexQuotedUtf8 = BenchmarkData.Ascii("\"" + _evmHex + "\"");
        _solanaBase58Utf8 = BenchmarkData.Ascii(_solanaBase58);
        _solanaBase58QuotedUtf8 = BenchmarkData.Ascii("\"" + _solanaBase58 + "\"");

        _evmEfPayload = AddressValueConverter.AddressToBytes(_evm);
        _solanaEfPayload = AddressValueConverter.AddressToBytes(_solana);

        _jsonWriter = new Utf8JsonWriter(_jsonBuffer);

        _charDestination = new char[Address.MaxBase58Length];
        _utf8Destination = new byte[Address.MaxBase58Length];
        _byteDestination = new byte[Address.SolanaByteLength];

        // Sanity: every parse path must agree, otherwise the numbers are meaningless.
        if (_evmHexNoPrefix.Length != Address.EvmHexLength
            || _solanaBase58.Length < 43
            || !Address.TryParse(_evmHexChars, out var a) || a != _evm
            || !Address.TryParse(_evmHexNoPrefix, out var b) || b != _evm
            || !Address.TryParse(_evmHexNoPrefixUtf8, out var c) || c != _evm
            || !Address.TryParse(_evmHexNoPrefixNoZero, out var g) || g.Type != AddressType.Evm
            || !Address.TryParse(_evmHexNoPrefixNoZeroUtf8, out var h) || h != g
            || !Address.TryParse(_evmHexQuotedUtf8, out var d) || d != _evm
            || !Address.TryParse(_solanaBase58Chars, out var e) || e != _solana
            || !Address.TryParse(_solanaBase58QuotedUtf8, out var f) || f != _solana
            || AddressValueConverter.BytesToAddress(_solanaEfPayload) != _solana
            || _evm.Equals(_evmDifferent) || _solana.Equals(_solanaDifferent))
        {
            throw new InvalidOperationException("Address benchmark inputs do not round-trip.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _jsonWriter.Dispose();

    // ---- construction / byte conversion ----

    [Benchmark]
    public Address FromEvmBytes() => Address.FromEvmBytes(_evmBytes);

    [Benchmark]
    public Address FromSolanaBytes() => Address.FromSolanaBytes(_solanaBytes);

    [Benchmark]
    public byte WriteBytes_Evm()
    {
        _evm.WriteBytes(_byteDestination);
        return _byteDestination[0];
    }

    [Benchmark]
    public byte WriteBytes_Solana()
    {
        _solana.WriteBytes(_byteDestination);
        return _byteDestination[0];
    }

    [Benchmark]
    public byte[] ToBytes_Evm() => _evm.ToBytes();

    // ---- parsing ----

    [Benchmark]
    public bool TryParse_Chars_EvmPrefixed() => Address.TryParse(_evmHexChars, out _);

    [Benchmark]
    public bool TryParse_Chars_EvmUnprefixed() => Address.TryParse(_evmHexNoPrefix.AsSpan(), out _);

    [Benchmark]
    public bool TryParse_Chars_Solana() => Address.TryParse(_solanaBase58Chars, out _);

    [Benchmark]
    public bool TryParse_Chars_EvmUnprefixedNoZero() => Address.TryParse(_evmHexNoPrefixNoZero.AsSpan(), out _);

    [Benchmark]
    public bool TryParse_Utf8_EvmUnprefixedNoZero() => Address.TryParse(_evmHexNoPrefixNoZeroUtf8, out _);

    [Benchmark]
    public bool TryParse_Utf8_EvmPrefixed() => Address.TryParse(_evmHexUtf8, out _);

    [Benchmark]
    public bool TryParse_Utf8_EvmUnprefixed() => Address.TryParse(_evmHexNoPrefixUtf8, out _);

    [Benchmark]
    public bool TryParse_Utf8_EvmQuoted() => Address.TryParse(_evmHexQuotedUtf8, out _);

    [Benchmark]
    public bool TryParse_Utf8_Solana() => Address.TryParse(_solanaBase58Utf8, out _);

    [Benchmark]
    public bool TryParse_Utf8_SolanaQuoted() => Address.TryParse(_solanaBase58QuotedUtf8, out _);

    // ---- formatting ----

    [Benchmark]
    public string ToString_Evm() => _evm.ToString();

    [Benchmark]
    public string ToString_Solana() => _solana.ToString();

    [Benchmark]
    public string ToChecksumString_Evm() => _evm.ToChecksumString();

    [Benchmark]
    public bool TryFormat_Chars_Evm() => _evm.TryFormat(_charDestination, out _);

    [Benchmark]
    public bool TryFormat_Chars_Solana() => _solana.TryFormat(_charDestination, out _);

    [Benchmark]
    public bool TryFormat_Chars_EvmChecksum() => _evm.TryFormat(_charDestination, out _, "c", CultureInfo.InvariantCulture);

    [Benchmark]
    public bool TryFormat_Utf8_Evm() => _evm.TryFormat(_utf8Destination, out _);

    [Benchmark]
    public bool TryFormat_Utf8_Solana() => _solana.TryFormat(_utf8Destination, out _);

    [Benchmark]
    public bool TryFormat_Utf8_EvmChecksum() => _evm.TryFormat(_utf8Destination, out _, "c", CultureInfo.InvariantCulture);

    // ---- equality / ordering / hashing ----

    [Benchmark]
    public bool Equals_Evm_Equal() => _evm.Equals(_evmEqual);

    [Benchmark]
    public bool Equals_Evm_DifferentInLastByte() => _evm.Equals(_evmDifferent);

    [Benchmark]
    public bool Equals_Solana_Equal() => _solana.Equals(_solanaEqual);

    [Benchmark]
    public bool Equals_Solana_DifferentInLastByte() => _solana.Equals(_solanaDifferent);

    [Benchmark]
    public bool Equals_Evm_VsSolana() => _evm.Equals(_solana);

    [Benchmark]
    public int CompareTo_Evm_DifferentInLastByte() => _evm.CompareTo(_evmDifferent);

    [Benchmark]
    public int CompareTo_Solana_DifferentInLastByte() => _solana.CompareTo(_solanaDifferent);

    [Benchmark]
    public int GetHashCode_Evm() => _evm.GetHashCode();

    [Benchmark]
    public int GetHashCode_Solana() => _solana.GetHashCode();

    // ---- JSON (converter invoked directly; JsonSerializer path is covered by unit tests) ----

    [Benchmark]
    public int Json_Write_Evm()
    {
        _jsonBuffer.ResetWrittenCount();
        _jsonWriter.Reset(_jsonBuffer);
        _converter.Write(_jsonWriter, _evm, _jsonOptions);
        _jsonWriter.Flush();
        return _jsonBuffer.WrittenCount;
    }

    [Benchmark]
    public int Json_Write_Solana()
    {
        _jsonBuffer.ResetWrittenCount();
        _jsonWriter.Reset(_jsonBuffer);
        _converter.Write(_jsonWriter, _solana, _jsonOptions);
        _jsonWriter.Flush();
        return _jsonBuffer.WrittenCount;
    }

    [Benchmark]
    public Address Json_Read_Evm()
    {
        var reader = new Utf8JsonReader(_evmHexQuotedUtf8);
        reader.Read();
        return _converter.Read(ref reader, typeof(Address), _jsonOptions);
    }

    [Benchmark]
    public Address Json_Read_Solana()
    {
        var reader = new Utf8JsonReader(_solanaBase58QuotedUtf8);
        reader.Read();
        return _converter.Read(ref reader, typeof(Address), _jsonOptions);
    }

    // ---- EF Core value converter (the allocation of the provider byte[] is inherent to the boundary) ----

    [Benchmark]
    public byte[] ValueConverter_ToProvider_Evm() => AddressValueConverter.AddressToBytes(_evm);

    [Benchmark]
    public Address ValueConverter_FromProvider_Evm() => AddressValueConverter.BytesToAddress(_evmEfPayload);

    [Benchmark]
    public Address ValueConverter_FromProvider_Solana() => AddressValueConverter.BytesToAddress(_solanaEfPayload);
}
