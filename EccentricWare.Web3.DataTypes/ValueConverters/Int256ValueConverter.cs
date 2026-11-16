using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.ValueConverters;

/// <summary>
/// EF Core provider converter for storing <see cref="int256"/> as a fixed-length 32-byte array that sorts in signed
/// numeric order: big-endian two's complement with the sign bit inverted (offset binary). <see cref="int256.MinValue"/>
/// is stored as all zero bytes, zero as <c>0x80 00 ...</c> and <see cref="int256.MaxValue"/> as all <c>0xFF</c>, so range
/// filters and <c>ORDER BY</c> on the column agree with in-memory comparison.
/// </summary>
/// <remarks>
/// The stored bytes are the standard big-endian two's-complement encoding with the high bit of the first byte flipped.
/// Plain two's complement would sort every negative value after every non-negative one, and EF Core translates
/// <c>&lt;</c>, <c>&gt;</c> and <c>OrderBy</c> on the property into SQL comparisons on the column, so the sign bit is
/// inverted to make those queries correct. Anything that reads the column outside this converter must undo the flip.
/// The converter declares a 32-byte size hint, so providers create a bounded column. Hints supplied by the caller are laid
/// over it. A stored value that is not exactly 32 bytes throws <see cref="FormatException"/>.
/// </remarks>
public sealed class Int256BytesValueConverter : ValueConverter<int256, byte[]>
{
    /// <summary>The fixed provider length in bytes.</summary>
    public const int StorageSize = 32;

    private static readonly ConverterMappingHints DefaultHints = new(size: StorageSize);

    /// <summary>
    /// Creates a converter that maps int256 &lt;-&gt; 32-byte sign-inverted big-endian array.
    /// </summary>
    public Int256BytesValueConverter() : this(null)
    {
    }

    /// <summary>
    /// Creates a converter that maps int256 &lt;-&gt; 32-byte sign-inverted big-endian array. Hints supplied by the caller
    /// take precedence over the defaults.
    /// </summary>
    public Int256BytesValueConverter(ConverterMappingHints? mappingHints)
        : base(
            model => Encode(model),
            provider => Decode(provider),
            ConverterHints.Merge(DefaultHints, mappingHints))
    {
    }

    private static byte[] Encode(int256 value)
    {
        byte[] bytes = value.ToBigEndianBytes();
        bytes[0] ^= 0x80;
        return bytes;
    }

    private static int256 Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length != StorageSize)
            throw new FormatException("int256 provider value must be exactly 32 bytes.");

        Span<byte> twosComplement = stackalloc byte[StorageSize];
        bytes.CopyTo(twosComplement);
        twosComplement[0] ^= 0x80;
        return new int256(twosComplement);
    }
}
