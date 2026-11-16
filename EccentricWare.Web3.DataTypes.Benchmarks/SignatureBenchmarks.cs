using System.Buffers;
using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.JsonConverters;
using EccentricWare.Web3.DataTypes.ValueConverters;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// Scalar operations on <see cref="Signature"/> for both shapes: EVM (65 bytes, r || s || v) and Solana (64 bytes).
/// Solana Base58 parsing is the dominant cost on a Solana JSON-RPC path, so it is measured separately from hex.
/// </summary>
[MemoryDiagnoser]
public class SignatureBenchmarks
{
    private byte[] _evmBytes = null!;
    private byte[] _solanaBytes = null!;
    private byte[] _r = null!;
    private byte[] _s = null!;

    private Signature _evm;
    private Signature _evmEqual;
    private Signature _evmDifferent;
    private Signature _solana;
    private Signature _solanaEqual;
    private Signature _solanaDifferent;

    private string _evmHex = null!;
    private char[] _evmHexChars = null!;
    private byte[] _evmHexUtf8 = null!;
    private char[] _solanaHexChars = null!;
    private byte[] _solanaHexUtf8 = null!;
    private char[] _solanaBase58Chars = null!;
    private byte[] _solanaBase58Utf8 = null!;

    private byte[] _jsonEvm = null!;
    private byte[] _jsonSolanaHex = null!;
    private byte[] _jsonSolanaBase58 = null!;
    private readonly ArrayBufferWriter<byte> _jsonBuffer = new(160);
    private Utf8JsonWriter _jsonWriter = null!;
    private readonly SignatureJsonConverter _converter = new();
    private readonly SignatureJsonConverter _base58Converter = new(writeSolanaAsBase58: true);
    private readonly JsonSerializerOptions _jsonOptions = new();

    private readonly SignatureValueConverter _efConverter = new();
    private Func<Signature, byte[]> _efToProvider = null!;
    private Func<byte[], Signature> _efFromProvider = null!;
    private byte[] _efEvmProvider = null!;
    private byte[] _efSolanaProvider = null!;

    private char[] _base58CharDestination = null!;
    private byte[] _base58Utf8Destination = null!;
    private char[] _charDestination = null!;
    private byte[] _utf8Destination = null!;
    private byte[] _byteDestination = null!;
    private byte[] _componentDestination = null!;

    [GlobalSetup]
    public void Setup()
    {
        _evmBytes = BenchmarkData.RandomBytes(Signature.EvmByteLength, BenchmarkData.Seed);
        _evmBytes[64] = 27; // typical legacy v
        _solanaBytes = BenchmarkData.RandomBytes(Signature.SolanaByteLength, BenchmarkData.Seed + 1);
        _r = _evmBytes.AsSpan(0, 32).ToArray();
        _s = _evmBytes.AsSpan(32, 32).ToArray();

        _evm = Signature.FromEvmBytes(_evmBytes);
        _evmEqual = Signature.FromEvmBytes(_evmBytes);
        byte[] evmDifferent = (byte[])_evmBytes.Clone();
        evmDifferent[63] ^= 0x01; // last payload byte: worst case for a short-circuiting comparison
        _evmDifferent = Signature.FromEvmBytes(evmDifferent);

        _solana = Signature.FromSolanaBytes(_solanaBytes);
        _solanaEqual = Signature.FromSolanaBytes(_solanaBytes);
        byte[] solanaDifferent = (byte[])_solanaBytes.Clone();
        solanaDifferent[63] ^= 0x01;
        _solanaDifferent = Signature.FromSolanaBytes(solanaDifferent);

        _evmHex = _evm.ToString();
        _evmHexChars = _evmHex.ToCharArray();
        _evmHexUtf8 = BenchmarkData.Ascii(_evmHex);

        string solanaHex = _solana.ToString();
        _solanaHexChars = solanaHex.ToCharArray();
        _solanaHexUtf8 = BenchmarkData.Ascii(solanaHex);

        string base58 = BenchmarkData.Base58(_solanaBytes);
        _solanaBase58Chars = base58.ToCharArray();
        _solanaBase58Utf8 = BenchmarkData.Ascii(base58);

        _jsonEvm = BenchmarkData.Ascii("\"" + _evmHex + "\"");
        _jsonSolanaHex = BenchmarkData.Ascii("\"" + solanaHex + "\"");
        _jsonSolanaBase58 = BenchmarkData.Ascii("\"" + base58 + "\"");
        _jsonWriter = new Utf8JsonWriter(_jsonBuffer);

        _efToProvider = _efConverter.ConvertToProviderTyped;
        _efFromProvider = _efConverter.ConvertFromProviderTyped;
        _efEvmProvider = _efToProvider(_evm);
        _efSolanaProvider = _efToProvider(_solana);

        _base58CharDestination = new char[Signature.SolanaMaxBase58EncodedLength];
        _base58Utf8Destination = new byte[Signature.SolanaMaxBase58EncodedLength];
        _charDestination = new char[2 + Signature.EvmHexLength];
        _utf8Destination = new byte[2 + Signature.EvmHexLength];
        _byteDestination = new byte[Signature.EvmByteLength];
        _componentDestination = new byte[32];

        // Sanity: every parse path must agree, otherwise the numbers are meaningless.
        if (!Signature.TryParse(_evmHexChars, out var a) || a != _evm
            || !Signature.TryParse(_evmHexUtf8, out var b) || b != _evm
            || !Signature.TryParse(_solanaHexChars, out var c) || c != _solana
            || !Signature.TryParse(_solanaHexUtf8, out var d) || d != _solana
            || !Signature.TryParse(_solanaBase58Chars, out var e) || e != _solana
            || !Signature.TryParse(_solanaBase58Utf8, out var f) || f != _solana
            || _efFromProvider(_efEvmProvider) != _evm
            || _efFromProvider(_efSolanaProvider) != _solana
            || !_evm.TryFormat(_charDestination, out int charCount) || charCount != _charDestination.Length
            || !_evm.TryFormat(_utf8Destination, out int byteCount) || byteCount != _utf8Destination.Length
            || !_solana.TryFormat(_utf8Destination, out _)
            || JsonRoundTrip(_jsonEvm) != _evm
            || JsonRoundTrip(_jsonSolanaHex) != _solana
            || JsonRoundTrip(_jsonSolanaBase58) != _solana
            || _solana.ToBase58String() != base58
            || !_solana.TryFormatBase58(_base58CharDestination, out int base58Chars) || new string(_base58CharDestination, 0, base58Chars) != base58
            || !_solana.TryFormatBase58(_base58Utf8Destination, out int base58Bytes) || !_base58Utf8Destination.AsSpan(0, base58Bytes).SequenceEqual(_solanaBase58Utf8))
        {
            throw new InvalidOperationException("Benchmark inputs do not round-trip.");
        }

        if (!JsonWriteBytes(_solana, _base58Converter).AsSpan().SequenceEqual(_jsonSolanaBase58))
            throw new InvalidOperationException("Base58 JSON write output does not match the expected string.");

        if (!JsonWriteBytes(_evm).AsSpan().SequenceEqual(_jsonEvm))
            throw new InvalidOperationException("JSON write output does not match the expected string.");
    }

    private Signature JsonRoundTrip(byte[] json)
    {
        var reader = new Utf8JsonReader(json);
        reader.Read();
        return _converter.Read(ref reader, typeof(Signature), _jsonOptions);
    }

    private byte[] JsonWriteBytes(Signature value, SignatureJsonConverter? converter = null)
    {
        var buffer = new ArrayBufferWriter<byte>(160);
        using (var writer = new Utf8JsonWriter(buffer))
            (converter ?? _converter).Write(writer, value, _jsonOptions);
        return buffer.WrittenSpan.ToArray();
    }

    [GlobalCleanup]
    public void Cleanup() => _jsonWriter.Dispose();

    // ---- construction / byte conversion ----

    [Benchmark]
    public Signature FromEvmBytes() => Signature.FromEvmBytes(_evmBytes);

    [Benchmark]
    public Signature FromSolanaBytes() => Signature.FromSolanaBytes(_solanaBytes);

    [Benchmark]
    public Signature FromEvmComponents() => Signature.FromEvmComponents(_r, _s, 27);

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
    public byte WriteR()
    {
        _evm.WriteR(_componentDestination);
        return _componentDestination[0];
    }

    [Benchmark]
    public Hash32 R_Property() => _evm.R;

    // ---- parsing ----

    [Benchmark]
    public bool TryParse_EvmHex_Chars() => Signature.TryParse(_evmHexChars, out _);

    [Benchmark]
    public bool TryParse_EvmHex_Utf8() => Signature.TryParse(_evmHexUtf8, out _);

    [Benchmark]
    public bool TryParse_SolanaHex_Chars() => Signature.TryParse(_solanaHexChars, out _);

    [Benchmark]
    public bool TryParse_SolanaHex_Utf8() => Signature.TryParse(_solanaHexUtf8, out _);

    [Benchmark]
    public bool TryParse_SolanaBase58_Chars() => Signature.TryParse(_solanaBase58Chars, out _);

    [Benchmark]
    public bool TryParse_SolanaBase58_Utf8() => Signature.TryParse(_solanaBase58Utf8, out _);

    [Benchmark]
    public Signature Parse_EvmHex_Chars() => Signature.Parse(_evmHexChars.AsSpan());

    // ---- formatting ----

    [Benchmark]
    public string ToString_Evm() => _evm.ToString();

    [Benchmark]
    public string ToString_Solana() => _solana.ToString();

    [Benchmark]
    public string ToString_EvmNoPrefixUpper() => _evm.ToString("X", CultureInfo.InvariantCulture);

    [Benchmark]
    public bool TryFormat_Evm_Chars() => _evm.TryFormat(_charDestination, out _);

    [Benchmark]
    public bool TryFormat_Evm_Utf8() => _evm.TryFormat(_utf8Destination, out _);

    [Benchmark]
    public bool TryFormat_Solana_Utf8() => _solana.TryFormat(_utf8Destination, out _);

    // ---- Base58 formatting (Solana only) ----

    [Benchmark]
    public string ToBase58String_Solana() => _solana.ToBase58String();

    [Benchmark]
    public bool TryFormatBase58_Solana_Chars() => _solana.TryFormatBase58(_base58CharDestination, out _);

    [Benchmark]
    public bool TryFormatBase58_Solana_Utf8() => _solana.TryFormatBase58(_base58Utf8Destination, out _);

    // ---- equality / ordering / hashing ----

    [Benchmark]
    public bool Equals_Evm_Equal() => _evm.Equals(_evmEqual);

    [Benchmark]
    public bool Equals_Evm_DifferentInLastLimb() => _evm.Equals(_evmDifferent);

    [Benchmark]
    public bool Equals_Solana_Equal() => _solana.Equals(_solanaEqual);

    [Benchmark]
    public bool Equals_Solana_DifferentInLastLimb() => _solana.Equals(_solanaDifferent);

    [Benchmark]
    public bool Equals_DifferentType() => _evm.Equals(_solana);

    [Benchmark]
    public int CompareTo_Evm_Equal() => _evm.CompareTo(_evmEqual);

    [Benchmark]
    public int CompareTo_Evm_DifferentInLastLimb() => _evm.CompareTo(_evmDifferent);

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
    public int Json_Write_Solana_Base58()
    {
        _jsonBuffer.ResetWrittenCount();
        _jsonWriter.Reset(_jsonBuffer);
        _base58Converter.Write(_jsonWriter, _solana, _jsonOptions);
        _jsonWriter.Flush();
        return _jsonBuffer.WrittenCount;
    }

    [Benchmark]
    public Signature Json_Read_EvmHex()
    {
        var reader = new Utf8JsonReader(_jsonEvm);
        reader.Read();
        return _converter.Read(ref reader, typeof(Signature), _jsonOptions);
    }

    [Benchmark]
    public Signature Json_Read_SolanaHex()
    {
        var reader = new Utf8JsonReader(_jsonSolanaHex);
        reader.Read();
        return _converter.Read(ref reader, typeof(Signature), _jsonOptions);
    }

    [Benchmark]
    public Signature Json_Read_SolanaBase58()
    {
        var reader = new Utf8JsonReader(_jsonSolanaBase58);
        reader.Read();
        return _converter.Read(ref reader, typeof(Signature), _jsonOptions);
    }

    // ---- EF Core value converter ----

    [Benchmark]
    public byte[] EfConverter_ToProvider_Evm() => _efToProvider(_evm);

    [Benchmark]
    public Signature EfConverter_FromProvider_Evm() => _efFromProvider(_efEvmProvider);

    [Benchmark]
    public Signature EfConverter_FromProvider_Solana() => _efFromProvider(_efSolanaProvider);
}
