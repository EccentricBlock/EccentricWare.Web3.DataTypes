using EccentricWare.Web3.DataTypes.JsonConverters;
using System.Collections;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>One System.Text.Json converter under test with known-answer texts, used by the cross-converter policy tests.</summary>
/// <param name="Type">Value type the converter handles.</param>
/// <param name="Canonical">The text the converter writes for <paramref name="Expected"/> (without JSON quotes).</param>
/// <param name="Expected">The value <paramref name="Canonical"/> reads as, built from raw bytes or constants, not by parsing.</param>
/// <param name="Respellings">Other texts that read as <paramref name="Expected"/> (case, prefix, decimal, bare digits).</param>
/// <param name="Alternates">Texts in other families (Base58, Base64, decimal) with their independently known values.</param>
/// <param name="DefaultText">The text written for <c>default(T)</c>.</param>
/// <param name="Longest">The longest legal text of the type (its worst case for the token cap) and its value.</param>
/// <param name="TokenCap">The internal raw-token cap, or 0 for HexBytes, whose escaped-token limit is per instance.</param>
/// <param name="NumberTokens">Bare JSON numbers the converter accepts (empty for string-only types).</param>
/// <param name="EmptyTextIsValue">True when empty text is a valid value (HexBytes: the empty byte string).</param>
public sealed record JsonConverterCase(
    Type Type,
    string Canonical,
    object Expected,
    string[] Respellings,
    (string Text, object Value)[] Alternates,
    string DefaultText,
    (string Text, object Value) Longest,
    int TokenCap,
    (string Text, object Value)[] NumberTokens,
    bool EmptyTextIsValue = false)
{
    public string Name => Type.Name;

    public bool AcceptsNumbers => NumberTokens.Length > 0;

    public object Default => Activator.CreateInstance(Type)!;

    public Type DictionaryType => typeof(Dictionary<,>).MakeGenericType(Type, typeof(int));

    public Type NullableType => typeof(Nullable<>).MakeGenericType(Type);

    public override string ToString() => Name;

    public object? Read(string json) => JsonSerializer.Deserialize(json, Type);

    public object? Read(byte[] json) => JsonSerializer.Deserialize(json, Type);

    public IDictionary ReadDictionary(string json) => (IDictionary)JsonSerializer.Deserialize(json, DictionaryType)!;

    public IDictionary ReadDictionary(byte[] json) => (IDictionary)JsonSerializer.Deserialize(json, DictionaryType)!;

    /// <summary>The value a read of <paramref name="text"/> as a JSON string produces; the table's texts only.</summary>
    public IEnumerable<(string Text, object Value)> AllTexts()
    {
        yield return (Canonical, Expected);
        foreach (string respelling in Respellings)
            yield return (respelling, Expected);
        foreach ((string text, object value) in Alternates)
            yield return (text, value);
    }
}

/// <summary>The nine converters, one row each. Use with <c>[DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]</c>.</summary>
public static class JsonConverterCases
{
    // Vectors from an independent Base58/Base64 reference implementation (python3):
    //   Token program public key: 06ddf6e1d765a193d9cbe146ceeb79ac1cb485ed5f5b37913a8cf5857eff00a9
    //   Solana docs signature:    e069ef2f...c0f (64 bytes)
    //   bytes 1..32:              Base58 4wBqpZM9xaSheZzJSMawUKKwhdpChKbZ5eu5ky4Vigw, Base64 AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=
    public const string SolanaTokenProgramBase58 = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
    public const string SolanaTokenProgramHex = "06ddf6e1d765a193d9cbe146ceeb79ac1cb485ed5f5b37913a8cf5857eff00a9";
    public const string SolanaSignatureBase58 = "5VERv8NMvzbJMEkV8xnrLkEaWRtSz9CosKDYjCJjBRnbJLgp8uirBgmQpjKhoR4tjF3ZpRzrFmBV6UjKdiSZkQUW";
    public const string SolanaSignatureHex = "e069ef2fbe487298f22ffecbed57a45de555160d7152f1f9c3269cde05e428fcaa5e622f727c45cbd5446af507ae6394cab8e3a4e0787289698822583b479c0f";
    public const string AscendingHash32Base58 = "4wBqpZM9xaSheZzJSMawUKKwhdpChKbZ5eu5ky4Vigw";
    public const string AscendingHash32Base64 = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    public const string AscendingHash32Hex = "0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20";
    public const string Uint256MaxDecimal = "115792089237316195423570985008687907853269984665640564039457584007913129639935";
    public const string Int256MinDecimal = "-57896044618658097711785492504343953926634992332820282019728792003956564819968";

    private static string Zeros(int count) => new('0', count);

    public static IEnumerable<JsonConverterCase> All { get; } = Build();

    public static JsonConverterCase For<T>() => All.Single(c => c.Type == typeof(T));

    /// <summary>Cases whose converter has an internal raw-token cap (all but HexBytes).</summary>
    public static IEnumerable<JsonConverterCase> Capped => All.Where(c => c.TokenCap > 0);

    /// <summary>Cases that accept only JSON strings.</summary>
    public static IEnumerable<JsonConverterCase> StringOnly => All.Where(c => !c.AcceptsNumbers);

    private static JsonConverterCase[] Build()
    {
        var evm = Address.FromEvmBytes(Convert.FromHexString(new string('a', 40)));
        var solana = Address.FromSolanaBytes(Convert.FromHexString(SolanaTokenProgramHex));
        var ascending = new Hash32(Convert.FromHexString(AscendingHash32Hex));
        // URL-safe Base64 of 30 x 0xff, 0xfe, 0xff: '_' stands for '/'.
        var urlSafe = new Hash32(Convert.FromHexString(new string('f', 60) + "feff"));
        BigInteger hexMax = BigInteger.Pow(2, 4 * HexBigInteger.DefaultMaxHexDigits) - 1;

        return
        [
            new(typeof(Address), "0x" + new string('a', 40), evm,
                ["0X" + new string('A', 40), new string('a', 40)],
                [(SolanaTokenProgramBase58, solana), (new string('1', 32), Address.FromSolanaBytes(new byte[32]))],
                "0x" + Zeros(40),
                ("JEKNVnkbo3jma5nREBBJCDoXFVeKkD56V3xKrvRmWxFG", Address.FromSolanaBytes(Enumerable.Repeat((byte)0xFF, 32).ToArray())), // 44 chars
                AddressJsonConverter.MaxRawTokenLength, []),

            new(typeof(FunctionSelector), "0xa9059cbb", new FunctionSelector(0xA9059CBBu),
                ["0XA9059CBB", "a9059cbb", "A9059CBB"], [],
                "0x00000000",
                ("0xa9059cbb", new FunctionSelector(0xA9059CBBu)),
                FunctionSelectorJsonConverter.MaxRawTokenLength, []),

            new(typeof(InstructionDiscriminator), "0xafaf6d1f0d989bed", new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL),
                ["0XAFAF6D1F0D989BED", "afaf6d1f0d989bed"], [],
                "0x0000000000000000",
                ("0xafaf6d1f0d989bed", new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL)),
                InstructionDiscriminatorJsonConverter.MaxRawTokenLength, []),

            new(typeof(Hash32), "0x" + AscendingHash32Hex, ascending,
                ["0X" + AscendingHash32Hex.ToUpperInvariant(), AscendingHash32Hex],
                [(AscendingHash32Base58, ascending), (AscendingHash32Base64, ascending),
                 ("_________________________________________v8=", urlSafe), (new string('1', 32), Hash32.Zero)],
                "0x" + Zeros(64),
                ("0x" + AscendingHash32Hex, ascending),
                Hash32JsonConverter.MaxRawTokenLength, []),

            new(typeof(Signature), "0x" + new string('c', 130), Signature.FromEvmBytes(Convert.FromHexString(new string('c', 130))),
                ["0X" + new string('C', 130), new string('c', 130)],
                [(SolanaSignatureBase58, Signature.FromSolanaBytes(Convert.FromHexString(SolanaSignatureHex)))],
                "0x" + Zeros(130),
                ("0x" + new string('c', 130), Signature.FromEvmBytes(Convert.FromHexString(new string('c', 130)))),
                SignatureJsonConverter.MaxRawTokenLength, []),

            new(typeof(uint256), "0x2a", new uint256(42UL),
                ["0X2A", "42"],
                [("0", uint256.Zero), (Uint256MaxDecimal, uint256.MaxValue)],
                "0x0",
                (Uint256MaxDecimal, uint256.MaxValue),
                UInt256JsonConverter.MaxRawStringTokenLength,
                [("42", new uint256(42UL)), (Uint256MaxDecimal, uint256.MaxValue)]),

            new(typeof(int256), "0x2a", new int256(42L),
                ["0X2A", "42"],
                [("-42", new int256(-42L)), (Int256MinDecimal, int256.MinValue)],
                "0x0",
                (Int256MinDecimal, int256.MinValue),
                Int256JsonConverter.MaxRawStringTokenLength,
                [("42", new int256(42L)), ("-42", new int256(-42L)), (Int256MinDecimal, int256.MinValue)]),

            new(typeof(HexBigInteger), "0x2a", new HexBigInteger(42),
                ["0X2A", "2a"],
                [("-0x2a", new HexBigInteger(-42))],
                "0x0",
                ("0x" + new string('f', HexBigInteger.DefaultMaxHexDigits), new HexBigInteger(hexMax)),
                HexBigIntegerJsonConverter.MaxEscapedStringTokenLength,
                [("42", new HexBigInteger(42)), ("-42", new HexBigInteger(-42))]),

            new(typeof(HexBytes), "0x2a2b", new HexBytes(new byte[] { 0x2A, 0x2B }),
                ["0X2A2B", "2A2B", "2a2b"], [],
                "0x",
                ("0x" + new string('a', 200), new HexBytes(Enumerable.Repeat((byte)0xAA, 100).ToArray())),
                0, [], EmptyTextIsValue: true),
        ];
    }
}
