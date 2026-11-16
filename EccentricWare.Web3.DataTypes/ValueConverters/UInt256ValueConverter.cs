using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.ValueConverters;

/// <summary>
/// EF Core provider converter for storing <see cref="uint256"/> as a fixed-length 32-byte array.
/// Big-endian encoding preserves numeric ordering under lexicographic byte ordering, so range filters and
/// <c>ORDER BY</c> on the column agree with in-memory comparison.
/// </summary>
/// <remarks>
/// The converter declares a 32-byte size hint, so providers create a bounded column instead of an unbounded blob that
/// cannot be indexed efficiently. Hints supplied by the caller are laid over it. A stored value that is not exactly 32
/// bytes throws <see cref="FormatException"/>.
/// </remarks>
public sealed class UInt256BytesValueConverter : ValueConverter<uint256, byte[]>
{
    /// <summary>The fixed provider length in bytes.</summary>
    public const int StorageSize = 32;

    private static readonly ConverterMappingHints DefaultHints = new(size: StorageSize);

    /// <summary>
    /// Creates a converter that maps uint256 &lt;-&gt; 32-byte big-endian array.
    /// </summary>
    public UInt256BytesValueConverter() : this(null)
    {
    }

    /// <summary>
    /// Creates a converter that maps uint256 &lt;-&gt; 32-byte big-endian array. Hints supplied by the caller take
    /// precedence over the defaults.
    /// </summary>
    public UInt256BytesValueConverter(ConverterMappingHints? mappingHints)
        : base(
            model => Encode(model),
            provider => Decode(provider),
            ConverterHints.Merge(DefaultHints, mappingHints))
    {
    }

    private static byte[] Encode(uint256 value) => value.ToBigEndianBytes();

    private static uint256 Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length != StorageSize)
            throw new FormatException("uint256 provider value must be exactly 32 bytes.");

        return new uint256(bytes);
    }
}
