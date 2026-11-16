using System.Buffers;
using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.JsonConverters;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>Scalar operations on <see cref="InstructionDiscriminator"/>.</summary>
[MemoryDiagnoser]
public class InstructionDiscriminatorBenchmarks
{
    private InstructionDiscriminator _value;
    private byte[] _bytes = null!;
    private byte[] _instructionData = null!;
    private byte[] _hexUtf8 = null!;
    private char[] _hexChars = null!;
    private string _hexString = null!;
    private byte[] _jsonQuotedHex = null!;
    private char[] _charDestination = null!;
    private byte[] _utf8Destination = null!;
    private byte[] _byteDestination = null!;

    private readonly ArrayBufferWriter<byte> _jsonBuffer = new(64);
    private Utf8JsonWriter _jsonWriter = null!;
    private readonly InstructionDiscriminatorJsonConverter _converter = new();
    private readonly JsonSerializerOptions _jsonOptions = new();

    [GlobalSetup]
    public void Setup()
    {
        _value = new InstructionDiscriminator(0xAF_AF_6D_1F_0D_98_9B_ED);
        _bytes = [0xAF, 0xAF, 0x6D, 0x1F, 0x0D, 0x98, 0x9B, 0xED];
        _instructionData = BenchmarkData.RandomBytes(40);
        _hexString = _value.ToString();
        _hexChars = _hexString.ToCharArray();
        _hexUtf8 = BenchmarkData.Ascii(_hexString);
        _jsonQuotedHex = BenchmarkData.Ascii("\"" + _hexString + "\"");
        _charDestination = new char[18];
        _utf8Destination = new byte[18];
        _byteDestination = new byte[8];
        _jsonWriter = new Utf8JsonWriter(_jsonBuffer);

        if (InstructionDiscriminator.Parse(_hexChars) != _value
            || !InstructionDiscriminator.TryParse(_hexUtf8, out var parsed) || parsed != _value
            || new InstructionDiscriminator(_bytes) != _value)
        {
            throw new InvalidOperationException("Benchmark inputs do not round-trip.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _jsonWriter.Dispose();

    [Benchmark]
    public InstructionDiscriminator Ctor_FromBytes() => new(_bytes);

    [Benchmark]
    public byte WriteBytes()
    {
        _value.WriteBytes(_byteDestination);
        return _byteDestination[0];
    }

    [Benchmark]
    public bool TryFromInstructionData() => InstructionDiscriminator.TryFromInstructionData(_instructionData, out _);

    [Benchmark]
    public bool TryParse_Chars() => InstructionDiscriminator.TryParse(_hexChars, out _);

    [Benchmark]
    public bool TryParse_Utf8() => InstructionDiscriminator.TryParse(_hexUtf8, out _);

    [Benchmark]
    public InstructionDiscriminator Parse_String() => InstructionDiscriminator.Parse(_hexString);

    [Benchmark]
    public string ToString_Default() => _value.ToString();

    [Benchmark]
    public string ToString_UpperNoPrefix() => _value.ToString("X", CultureInfo.InvariantCulture);

    [Benchmark]
    public bool TryFormat_Chars() => _value.TryFormat(_charDestination, out _);

    [Benchmark]
    public bool TryFormat_Utf8() => _value.TryFormat(_utf8Destination, out _);

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
    public InstructionDiscriminator Json_Read()
    {
        var reader = new Utf8JsonReader(_jsonQuotedHex);
        reader.Read();
        return _converter.Read(ref reader, typeof(InstructionDiscriminator), _jsonOptions);
    }
}
