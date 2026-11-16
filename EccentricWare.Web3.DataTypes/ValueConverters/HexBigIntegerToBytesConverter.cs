using System.Linq.Expressions;
using System.Numerics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.ValueConverters;

/// <summary>
/// Converts <see cref="HexBigInteger"/> to and from a compact, signed, big-endian two's-complement <see cref="byte"/> array
/// for database persistence (e.g., VARBINARY/BLOB).
/// </summary>
/// <remarks>
/// This converter is optimised for round-trip correctness and storage efficiency.
/// <para>
/// Ordering: the stored bytes are variable length and signed, so their order does not match numeric order. EF Core still
/// translates <c>&lt;</c>, <c>&gt;</c> and <c>OrderBy</c> on the property into SQL comparisons on the column, so such a
/// query returns the wrong rows or the wrong order. Use this converter for equality and <c>IN</c> lookups only. For a
/// column that must be ordered or range-filtered, use a fixed-width type: <see cref="uint256"/> with
/// <see cref="UInt256BytesValueConverter"/>, or <see cref="int256"/> with <see cref="Int256BytesValueConverter"/>.
/// </para>
/// <para>
/// Zero is stored as an empty array, and an empty or null provider value reads back as zero. Providers that treat an empty
/// binary value as NULL (Oracle, for example) therefore store zero as NULL, which a non-nullable column rejects. The
/// column has no size hint because the value has no fixed width.
/// </para>
/// </remarks>
public sealed class HexBigIntegerToBytesConverter : ValueConverter<HexBigInteger, byte[]>
{
    // The expression fields must be declared before anything that constructs the converter (such as Default): static
    // initialisers run in textual order, and the base constructor rejects null expressions.
    private static readonly Expression<Func<HexBigInteger, byte[]>> ToProviderExpression =
        modelValue =>
            modelValue.Value.IsZero
                ? Array.Empty<byte>()
                : modelValue.Value.ToByteArray(isUnsigned: false, isBigEndian: true);

    private static readonly Expression<Func<byte[], HexBigInteger>> FromProviderExpression =
        providerValue =>
            providerValue == null || providerValue.Length == 0
                ? default
                : new HexBigInteger(new BigInteger(providerValue, isUnsigned: false, isBigEndian: true));

    /// <summary>
    /// Default singleton instance for reuse in model configuration.
    /// </summary>
    public static readonly HexBigIntegerToBytesConverter Default = new();

    /// <summary>
    /// Creates a converter with no mapping hints. EF Core instantiates converter types through a public parameterless
    /// constructor (optional parameters do not count), so one is declared explicitly.
    /// </summary>
    public HexBigIntegerToBytesConverter() : this(null)
    {
    }

    /// <summary>
    /// Creates a converter that stores <see cref="HexBigInteger"/> as a signed big-endian two's-complement byte array.
    /// </summary>
    /// <param name="mappingHints">Optional EF mapping hints (e.g., fixed size).</param>
    public HexBigIntegerToBytesConverter(ConverterMappingHints? mappingHints)
        : base(ToProviderExpression, FromProviderExpression, mappingHints)
    {
    }
}
