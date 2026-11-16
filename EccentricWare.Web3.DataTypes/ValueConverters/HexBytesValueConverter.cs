using System.Linq.Expressions;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.ValueConverters;

/// <summary>
/// EF Core ValueConverter for HexBytes storing as variable-length binary (VARBINARY).
/// </summary>
/// <remarks>
/// Neither direction copies the data. Writing hands the value's own array to the provider, and reading wraps the array
/// the provider returns, so a large payload (calldata, logs) costs no allocation per conversion or query parameter. This
/// relies on the provider byte array being treated as read-only, which EF Core and the ADO.NET providers do; code that
/// mutates a command parameter's array (for example a <c>DbCommandInterceptor</c>) would change the <see cref="HexBytes"/>
/// it came from.
/// <para>
/// The column is variable length and unbounded by default. For indexed columns, consider:
/// - Using a hash column for equality searches
/// - Limiting indexed prefix length for LIKE queries
/// - Using specialized columns for known fixed-size data (e.g., FunctionSelector)
/// </para>
/// </remarks>
public sealed class HexBytesValueConverter : ValueConverter<HexBytes, byte[]>
{
    private static readonly Expression<Func<HexBytes, byte[]>> ToProviderExpr = 
        static v => ToBytes(v);
    
    private static readonly Expression<Func<byte[], HexBytes>> FromProviderExpr = 
        static v => FromBytes(v);

    /// <summary>Creates the converter.</summary>
    public HexBytesValueConverter() : base(ToProviderExpr, FromProviderExpr)
    {
    }

    /// <summary>Creates the converter with explicit mapping hints.</summary>
    public HexBytesValueConverter(ConverterMappingHints? mappingHints) 
        : base(ToProviderExpr, FromProviderExpr, mappingHints)
    {
    }

    private static byte[] ToBytes(HexBytes value)
    {
        // HexBytes is immutable and owns an exactly sized array, so that array can go to the provider as it is.
        if (MemoryMarshal.TryGetArray(value.Memory, out ArraySegment<byte> segment)
            && segment.Offset == 0
            && segment.Count == segment.Array!.Length)
        {
            return segment.Array;
        }

        return value.ToArray();
    }

    private static HexBytes FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        // The array comes from the provider for this value only, so it can be wrapped without a copy.
        return HexBytes.FromArrayUnsafe(bytes);
    }
}

/// <summary>
/// EF Core ValueConverter for HexBytes storing as a lowercase <c>0x</c>-prefixed hex string.
/// Use when database TEXT storage is preferred over BINARY.
/// </summary>
/// <remarks>
/// Trade-offs vs binary storage:
/// - 2x storage size (hex encoding), and more on providers whose text type is UTF-16
/// - Human-readable in database tools
/// - Slower conversion (hex encode/decode, one string per value)
/// - The text can be matched with SQL text operators such as <c>LIKE</c> (in raw SQL), which binary columns do not offer
/// <para>
/// Reading is lenient: the parser accepts an optional <c>0x</c> prefix, either case, an odd digit count (left-padded with
/// a zero), surrounding ASCII whitespace and surrounding quote characters, so rows written by other tools still load.
/// Text that is not hex throws <see cref="FormatException"/>. Writing always produces the canonical lowercase form.
/// </para>
/// </remarks>
public sealed class HexBytesStringValueConverter : ValueConverter<HexBytes, string>
{
    private static readonly Expression<Func<HexBytes, string>> ToProviderExpr = 
        static v => v.ToString();
    
    private static readonly Expression<Func<string, HexBytes>> FromProviderExpr = 
        static v => HexBytes.Parse(v);

    /// <summary>Creates the converter.</summary>
    public HexBytesStringValueConverter() : base(ToProviderExpr, FromProviderExpr)
    {
    }

    /// <summary>Creates the converter with explicit mapping hints.</summary>
    public HexBytesStringValueConverter(ConverterMappingHints? mappingHints) 
        : base(ToProviderExpr, FromProviderExpr, mappingHints)
    {
    }
}
