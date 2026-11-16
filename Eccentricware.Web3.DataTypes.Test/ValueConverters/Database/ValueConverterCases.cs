using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Numerics;
using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>One converter under test together with sample data, so the same database checks run for every converter.</summary>
internal abstract class ConverterCase
{
    protected ConverterCase(string name, string storeType)
    {
        Name = name;
        StoreType = storeType;
    }

    public string Name { get; }

    /// <summary>The SQLite column type EF creates (BLOB, INTEGER or TEXT).</summary>
    public string StoreType { get; }

    /// <summary>True when the samples are in ascending order and the column sorts like the value.</summary>
    public bool IsOrdered { get; init; } = true;

    /// <summary>The size hint the converter declares; null for scalar and variable-length storage.</summary>
    public int? SizeHint { get; init; }

    /// <summary>A stored value the converter must reject (a byte array or a string), or null when every stored value decodes.</summary>
    public object? CorruptStoredValue { get; init; }

    public abstract Type ClrType { get; }

    /// <summary>
    /// Runs the static generic method <paramref name="method"/> of <paramref name="owner"/> with this case's CLR type. The
    /// method takes this case as its only argument. Exceptions surface unwrapped, so assertion failures read normally.
    /// </summary>
    public void Invoke(Type owner, string method)
    {
        MethodInfo target = owner.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .MakeGenericMethod(ClrType);
        try
        {
            target.Invoke(null, [this]);
        }
        catch (TargetInvocationException ex)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
        }
    }

    public override string ToString() => Name;
}

internal sealed class ConverterCase<T> : ConverterCase where T : struct
{
    public ConverterCase(string name, Func<ValueConverter> createConverter, Func<T[]> createSamples, Func<T> createAbsent, string storeType)
        : base(name, storeType)
    {
        CreateConverter = createConverter;
        CreateSamples = createSamples;
        CreateAbsent = createAbsent;
    }

    public override Type ClrType => typeof(T);

    public Func<ValueConverter> CreateConverter { get; }

    /// <summary>Fresh instances every call (so equal values are never the same object), at least five, all distinct.</summary>
    public Func<T[]> CreateSamples { get; }

    /// <summary>A value that is not one of the samples.</summary>
    public Func<T> CreateAbsent { get; }
}

internal static class ConverterCases
{
    private static byte[] Ascending64() => Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    private static byte[] Ones(int length) => ConverterTestData.Repeated(0xFF, length);

    private static readonly ConverterCase[] Cases =
    [
        new ConverterCase<Address>("Address", () => new AddressValueConverter(),
            () =>
            [
                Address.ZeroEvm,
                Address.FromEvmBytes(Hex("0000000000000000000000000000000000000001")),
                Address.Parse("0x52908400098527886e0f7030069857d2e4169ee7"),
                Address.FromEvmBytes(Ones(20)),
                Address.ZeroSolana,
                Address.FromSolanaBytes(Hex(new string('0', 62) + "01")),
                Address.Parse("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA"),
                Address.FromSolanaBytes(Ones(32)),
            ],
            () => Address.FromEvmBytes(Hex("1234567890123456789012345678901234567890")), "BLOB")
        { SizeHint = 33, CorruptStoredValue = ConverterTestData.Concat([7], new byte[20]) },

        new ConverterCase<FunctionSelector>("FunctionSelectorInt", () => new FunctionSelectorValueConverter(),
            () => [new(0u), new(1u), new(0x7FFFFFFFu), new(0x80000000u), new(0xa9059cbbu), new(0xFFFFFFFFu)],
            () => new FunctionSelector(0x12345678u), "INTEGER"),

        new ConverterCase<FunctionSelector>("FunctionSelectorBinary", () => new FunctionSelectorBinaryValueConverter(),
            () => [new(0u), new(1u), new(0x7FFFFFFFu), new(0x80000000u), new(0xa9059cbbu), new(0xFFFFFFFFu)],
            () => new FunctionSelector(0x12345678u), "BLOB")
        { SizeHint = 4, CorruptStoredValue = new byte[3] },

        new ConverterCase<Hash32>("Hash32", () => new Hash32ValueConverter(),
            () => [Hash32.Zero, new(0, 0, 0, 1), new(0, 0, 1, 0), new(ConverterTestData.Ascending(32)), new(0x8000000000000000UL, 0, 0, 0), new(Ones(32))],
            () => new Hash32(9, 9, 9, 9), "BLOB")
        { SizeHint = 32, CorruptStoredValue = new byte[1] },

        new ConverterCase<HexBigInteger>("HexBigInteger", () => new HexBigIntegerToBytesConverter(),
            () => [HexBigInteger.Zero, HexBigInteger.One, HexBigInteger.MinusOne, new(BigInteger.One << 255), new(-(BigInteger.One << 255)), new(BigInteger.One << 2000), new(long.MinValue)],
            () => new HexBigInteger(12345), "BLOB")
        { IsOrdered = false },

        new ConverterCase<HexBytes>("HexBytesBinary", () => new HexBytesValueConverter(), HexBytesSamples, () => new HexBytes([0xde, 0xad]), "BLOB")
        { },

        new ConverterCase<HexBytes>("HexBytesText", () => new HexBytesStringValueConverter(), HexBytesSamples, () => new HexBytes([0xde, 0xad]), "TEXT")
        { CorruptStoredValue = "zz" },

        new ConverterCase<InstructionDiscriminator>("InstructionDiscriminatorLong", () => new InstructionDiscriminatorValueConverter(),
            () => [new(0UL), new(1UL), new(0x7FFFFFFFFFFFFFFFUL), new(0x8000000000000000UL), new(0xAFAF6D1F0D989BEDUL), new(ulong.MaxValue)],
            () => new InstructionDiscriminator(0x123456789abcdef0UL), "INTEGER"),

        new ConverterCase<InstructionDiscriminator>("InstructionDiscriminatorBinary", () => new InstructionDiscriminatorBinaryValueConverter(),
            () => [new(0UL), new(1UL), new(0x7FFFFFFFFFFFFFFFUL), new(0x8000000000000000UL), new(0xAFAF6D1F0D989BEDUL), new(ulong.MaxValue)],
            () => new InstructionDiscriminator(0x123456789abcdef0UL), "BLOB")
        { SizeHint = 8, CorruptStoredValue = new byte[9] },

        new ConverterCase<int256>("Int256", () => new Int256BytesValueConverter(),
            () => [int256.MinValue, new(Hex(new string('f', 48) + "0000000000000000")), int256.MinusOne, int256.Zero, int256.One, new(long.MaxValue), int256.MaxValue],
            () => new int256(12345L), "BLOB")
        { SizeHint = 32, CorruptStoredValue = new byte[31] },

        new ConverterCase<Signature>("Signature", () => new SignatureValueConverter(),
            () =>
            [
                Signature.ZeroEvm,
                Signature.FromEvmBytes([.. Ascending64(), 0x40]),
                Signature.FromEvmBytes(Ones(65)),
                Signature.ZeroSolana,
                Signature.FromSolanaBytes(Ascending64()),
                Signature.FromSolanaBytes(Ones(64)),
            ],
            () => Signature.FromEvmBytes(ConverterTestData.Repeated(0x11, 65)), "BLOB")
        { SizeHint = 66, CorruptStoredValue = ConverterTestData.Concat([9], new byte[65]) },

        new ConverterCase<uint256>("UInt256", () => new UInt256BytesValueConverter(),
            () => [uint256.Zero, uint256.One, new(ulong.MaxValue), new(0, 1, 0, 0), new(0, 0, 1, 0), new(0, 0, 0, 1), uint256.MaxValue],
            () => new uint256(12345UL), "BLOB")
        { SizeHint = 32, CorruptStoredValue = new byte[33] },
    ];

    /// <summary>Lexicographically ascending, including values that are prefixes of others.</summary>
    private static HexBytes[] HexBytesSamples() =>
    [
        HexBytes.Empty,
        new([0x00]),
        new([0x00, 0x00]),
        new([0x01]),
        new([0x01, 0x02]),
        new([0x7f]),
        new([0xff]),
        new([0xff, 0xff]),
    ];

    public static IReadOnlyList<ConverterCase> All => Cases;

    /// <summary>Names as [DynamicData] rows (one string argument per row), so each converter is its own reported test case.</summary>
    public static IEnumerable<object[]> Names => Cases.Select(c => new object[] { c.Name });

    public static IEnumerable<object[]> OrderedNames => Cases.Where(c => c.IsOrdered).Select(c => new object[] { c.Name });

    public static IEnumerable<object[]> CorruptibleNames => Cases.Where(c => c.CorruptStoredValue is not null).Select(c => new object[] { c.Name });

    public static ConverterCase Get(string name) => Cases.Single(c => c.Name == name);
}
