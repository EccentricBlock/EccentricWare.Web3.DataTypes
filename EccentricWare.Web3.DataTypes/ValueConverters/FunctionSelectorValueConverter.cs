using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.ValueConverters;

/// <summary>
/// EF Core value converter storing a <see cref="FunctionSelector"/> as a 32-bit integer (INT) that sorts like the selector.
/// </summary>
/// <remarks>
/// A 4-byte integer column needs no byte-array allocation per value or query parameter. The selector is unsigned but the
/// column is signed, so the stored value is the selector minus 2^31 (the sign bit is inverted): selector 0x00000000 is
/// stored as <see cref="int.MinValue"/> and 0xFFFFFFFF as <see cref="int.MaxValue"/>. That keeps EF Core's
/// <c>&lt;</c>, <c>&gt;</c> and <c>OrderBy</c> translations correct, but the stored number is not the selector's own
/// value, so anything that reads the column outside this converter must add 2^31 back (or use
/// <see cref="FunctionSelectorBinaryValueConverter"/>, whose bytes are the selector itself).
/// <para>
/// Example usage in DbContext.OnModelCreating:
/// </para>
/// <code>
/// modelBuilder.Entity&lt;Transaction&gt;()
///     .Property(t => t.FunctionSelector)
///     .HasConversion&lt;FunctionSelectorValueConverter&gt;();
/// </code>
/// </remarks>
public sealed class FunctionSelectorValueConverter : ValueConverter<FunctionSelector, int>
{
    private static readonly Expression<Func<FunctionSelector, int>> ToProviderExpr = 
        static v => Encode(v);
    
    private static readonly Expression<Func<int, FunctionSelector>> FromProviderExpr = 
        static v => Decode(v);

    /// <summary>Creates the converter.</summary>
    public FunctionSelectorValueConverter() : base(ToProviderExpr, FromProviderExpr)
    {
    }

    /// <summary>Creates the converter with explicit mapping hints.</summary>
    public FunctionSelectorValueConverter(ConverterMappingHints? mappingHints) 
        : base(ToProviderExpr, FromProviderExpr, mappingHints)
    {
    }

    private static int Encode(FunctionSelector value) => value.AsInt32 ^ int.MinValue;

    private static FunctionSelector Decode(int stored) => FunctionSelector.FromInt32(stored ^ int.MinValue);
}

/// <summary>
/// EF Core value converter storing a <see cref="FunctionSelector"/> as fixed 4-byte big-endian binary (BINARY(4)).
/// The bytes sort in the same order as the unsigned selector, so range filters and <c>ORDER BY</c> on the column agree
/// with in-memory comparison. Each conversion allocates a 4-byte array.
/// </summary>
/// <remarks>
/// The converter declares a 4-byte size hint, so providers create a bounded column. Hints supplied by the caller are laid
/// over it. A stored value that is not exactly 4 bytes throws <see cref="FormatException"/>.
/// </remarks>
public sealed class FunctionSelectorBinaryValueConverter : ValueConverter<FunctionSelector, byte[]>
{
    /// <summary>
    /// The fixed storage size in bytes.
    /// </summary>
    public const int StorageSize = FunctionSelector.ByteLength; // 4

    private static readonly Expression<Func<FunctionSelector, byte[]>> ToProviderExpr = 
        static v => ToBytes(v);
    
    private static readonly Expression<Func<byte[], FunctionSelector>> FromProviderExpr = 
        static v => FromBytes(v);

    /// <summary>
    /// Default mapping hints for database column configuration.
    /// Specifies fixed 4-byte binary storage.
    /// </summary>
    public static readonly ConverterMappingHints DefaultHints = new(size: StorageSize);

    /// <summary>Creates the converter with the default mapping hints.</summary>
    public FunctionSelectorBinaryValueConverter() : this(null)
    {
    }

    /// <summary>Creates the converter; hints supplied by the caller take precedence over <see cref="DefaultHints"/>.</summary>
    public FunctionSelectorBinaryValueConverter(ConverterMappingHints? mappingHints) 
        : base(ToProviderExpr, FromProviderExpr, ConverterHints.Merge(DefaultHints, mappingHints))
    {
    }

    private static byte[] ToBytes(FunctionSelector value)
    {
        var bytes = new byte[StorageSize];
        value.WriteBytes(bytes);
        return bytes;
    }

    private static FunctionSelector FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        // FunctionSelector's span constructor accepts "at least 4" bytes, which would silently read the first four of a
        // longer value; a stored value of any other length means the column is not what this converter wrote.
        if (bytes.Length != StorageSize)
            throw new FormatException($"Expected {StorageSize} bytes for {nameof(FunctionSelector)}.");

        return new FunctionSelector(bytes);
    }
}
