using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.ValueConverters;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// EF Core value converters that have no benchmark in their type's suite: <see cref="Hash32"/>, <see cref="FunctionSelector"/>
/// (int and binary), <see cref="InstructionDiscriminator"/> (long and binary), <see cref="int256"/> and both
/// <see cref="HexBytes"/> converters. Each pair measures the write (model to provider) and the read (provider to model) that EF Core runs per query parameter and per materialised row.
/// </summary>
[MemoryDiagnoser]
public class ValueConverterBenchmarks
{
    private readonly Func<Hash32, byte[]> _hashToProvider;
    private readonly Func<byte[], Hash32> _hashFromProvider;
    private readonly Func<FunctionSelector, int> _selectorIntToProvider;
    private readonly Func<int, FunctionSelector> _selectorIntFromProvider;
    private readonly Func<FunctionSelector, byte[]> _selectorBinaryToProvider;
    private readonly Func<byte[], FunctionSelector> _selectorBinaryFromProvider;
    private readonly Func<InstructionDiscriminator, long> _discriminatorLongToProvider;
    private readonly Func<long, InstructionDiscriminator> _discriminatorLongFromProvider;
    private readonly Func<InstructionDiscriminator, byte[]> _discriminatorBinaryToProvider;
    private readonly Func<byte[], InstructionDiscriminator> _discriminatorBinaryFromProvider;
    private readonly Func<int256, byte[]> _int256ToProvider;
    private readonly Func<byte[], int256> _int256FromProvider;
    private readonly Func<HexBytes, byte[]> _bytesToProvider;
    private readonly Func<byte[], HexBytes> _bytesFromProvider;
    private readonly Func<HexBytes, string> _bytesTextToProvider;
    private readonly Func<string, HexBytes> _bytesTextFromProvider;

    private Hash32 _hash;
    private byte[] _hashProvider = null!;
    private FunctionSelector _selector;
    private byte[] _selectorProvider = null!;
    private InstructionDiscriminator _discriminator;
    private byte[] _discriminatorProvider = null!;
    private int256 _signed;
    private byte[] _signedProvider = null!;
    private HexBytes _calldata;
    private byte[] _calldataProvider = null!;
    private string _calldataText = null!;

    /// <summary>Size of the <see cref="HexBytes"/> payload: an <c>approve(spender, MAX)</c>-sized call and a larger one.</summary>
    [Params(68, 4096)]
    public int PayloadBytes { get; set; }

    public ValueConverterBenchmarks()
    {
        var hash = new Hash32ValueConverter();
        _hashToProvider = hash.ConvertToProviderTyped;
        _hashFromProvider = hash.ConvertFromProviderTyped;
        var selectorInt = new FunctionSelectorValueConverter();
        _selectorIntToProvider = selectorInt.ConvertToProviderTyped;
        _selectorIntFromProvider = selectorInt.ConvertFromProviderTyped;
        var selectorBinary = new FunctionSelectorBinaryValueConverter();
        _selectorBinaryToProvider = selectorBinary.ConvertToProviderTyped;
        _selectorBinaryFromProvider = selectorBinary.ConvertFromProviderTyped;
        var discriminatorLong = new InstructionDiscriminatorValueConverter();
        _discriminatorLongToProvider = discriminatorLong.ConvertToProviderTyped;
        _discriminatorLongFromProvider = discriminatorLong.ConvertFromProviderTyped;
        var discriminatorBinary = new InstructionDiscriminatorBinaryValueConverter();
        _discriminatorBinaryToProvider = discriminatorBinary.ConvertToProviderTyped;
        _discriminatorBinaryFromProvider = discriminatorBinary.ConvertFromProviderTyped;
        var int256Converter = new Int256BytesValueConverter();
        _int256ToProvider = int256Converter.ConvertToProviderTyped;
        _int256FromProvider = int256Converter.ConvertFromProviderTyped;
        var bytes = new HexBytesValueConverter();
        _bytesToProvider = bytes.ConvertToProviderTyped;
        _bytesFromProvider = bytes.ConvertFromProviderTyped;
        var bytesText = new HexBytesStringValueConverter();
        _bytesTextToProvider = bytesText.ConvertToProviderTyped;
        _bytesTextFromProvider = bytesText.ConvertFromProviderTyped;
    }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(20260511);
        byte[] Random(int length)
        {
            byte[] bytes = new byte[length];
            random.NextBytes(bytes);
            return bytes;
        }

        _hash = new Hash32(Random(32));
        _selector = new FunctionSelector(0xa9059cbbu);
        _discriminator = new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL);
        _signed = new int256(Random(32));
        _calldata = new HexBytes(Random(PayloadBytes));

        _hashProvider = _hashToProvider(_hash);
        _selectorProvider = _selectorBinaryToProvider(_selector);
        _discriminatorProvider = _discriminatorBinaryToProvider(_discriminator);
        _signedProvider = _int256ToProvider(_signed);
        _calldataProvider = _bytesToProvider(_calldata);
        _calldataText = _bytesTextToProvider(_calldata);

        // Sanity: every read must return what was written, so a broken benchmark fails loudly instead of timing a no-op.
        if (_hashFromProvider(_hashProvider) != _hash
            || _selectorBinaryFromProvider(_selectorProvider) != _selector
            || _selectorIntFromProvider(_selectorIntToProvider(_selector)) != _selector
            || _discriminatorBinaryFromProvider(_discriminatorProvider) != _discriminator
            || _discriminatorLongFromProvider(_discriminatorLongToProvider(_discriminator)) != _discriminator
            || _int256FromProvider(_signedProvider) != _signed
            || _bytesFromProvider(_calldataProvider) != _calldata
            || _bytesTextFromProvider(_calldataText) != _calldata)
        {
            throw new InvalidOperationException("A converter benchmark input did not round-trip.");
        }
    }

    [Benchmark] public byte[] Hash32_ToProvider() => _hashToProvider(_hash);
    [Benchmark] public Hash32 Hash32_FromProvider() => _hashFromProvider(_hashProvider);

    [Benchmark] public int FunctionSelectorInt_ToProvider() => _selectorIntToProvider(_selector);
    [Benchmark] public FunctionSelector FunctionSelectorInt_FromProvider() => _selectorIntFromProvider(unchecked((int)0xa9059cbbu));
    [Benchmark] public byte[] FunctionSelectorBinary_ToProvider() => _selectorBinaryToProvider(_selector);
    [Benchmark] public FunctionSelector FunctionSelectorBinary_FromProvider() => _selectorBinaryFromProvider(_selectorProvider);

    [Benchmark] public long InstructionDiscriminatorLong_ToProvider() => _discriminatorLongToProvider(_discriminator);
    [Benchmark] public InstructionDiscriminator InstructionDiscriminatorLong_FromProvider() => _discriminatorLongFromProvider(unchecked((long)0xAFAF6D1F0D989BEDUL));
    [Benchmark] public byte[] InstructionDiscriminatorBinary_ToProvider() => _discriminatorBinaryToProvider(_discriminator);
    [Benchmark] public InstructionDiscriminator InstructionDiscriminatorBinary_FromProvider() => _discriminatorBinaryFromProvider(_discriminatorProvider);

    [Benchmark] public byte[] Int256_ToProvider() => _int256ToProvider(_signed);
    [Benchmark] public int256 Int256_FromProvider() => _int256FromProvider(_signedProvider);

    [Benchmark] public byte[] HexBytesBinary_ToProvider() => _bytesToProvider(_calldata);
    [Benchmark] public HexBytes HexBytesBinary_FromProvider() => _bytesFromProvider(_calldataProvider);
    [Benchmark] public string HexBytesText_ToProvider() => _bytesTextToProvider(_calldata);
    [Benchmark] public HexBytes HexBytesText_FromProvider() => _bytesTextFromProvider(_calldataText);
}
