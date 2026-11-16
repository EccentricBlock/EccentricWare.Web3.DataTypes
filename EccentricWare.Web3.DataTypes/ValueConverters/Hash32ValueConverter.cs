using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.ValueConverters;

/// <summary>
/// EF Core value converter for <see cref="Hash32"/> to <see cref="byte[]"/> using big-endian encoding, stored in a
/// fixed 32-byte column (BINARY(32) / VARBINARY(32)). Big-endian bytes sort in the same order as <see cref="Hash32"/>
/// itself, so range filters and <c>ORDER BY</c> on the column agree with in-memory comparison.
/// </summary>
/// <remarks>
/// The converter declares a 32-byte size hint, so providers create a bounded column that can be a key or index
/// (an unbounded <c>varbinary(max)</c> cannot be indexed on SQL Server). Hints supplied by the caller are laid over it.
/// A stored value that is not exactly 32 bytes throws <see cref="FormatException"/>.
/// </remarks>
public sealed class Hash32ValueConverter : ValueConverter<Hash32, byte[]>
{
    /// <summary>The storage size in bytes.</summary>
    public const int StorageSize = Hash32.ByteLength;

    private static readonly ConverterMappingHints DefaultHints = new(size: StorageSize);

    /// <summary>
    /// Creates a converter with the default mapping hints. EF Core instantiates converter types through a public
    /// parameterless constructor (optional parameters do not count), so one is declared explicitly.
    /// </summary>
    public Hash32ValueConverter() : this(null)
    {
    }

    /// <summary>
    /// Creates a converter that serialises <see cref="Hash32"/> to a 32-byte big-endian array and back. Hints supplied by
    /// the caller take precedence over the defaults.
    /// </summary>
    public Hash32ValueConverter(ConverterMappingHints? mappingHints)
        : base(
            model => Hash32ToBytes(model),
            provider => BytesToHash32(provider),
            ConverterHints.Merge(DefaultHints, mappingHints))
    {
    }

    private static byte[] Hash32ToBytes(Hash32 value)
    {
        // Provider expects a byte[]; allocation is unavoidable for this boundary.
        // Big-endian is canonical for EVM hashes and sorts lexicographically.
        var bytes = new byte[StorageSize];
        value.WriteBigEndian(bytes);
        return bytes;
    }

    private static Hash32 BytesToHash32(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length != StorageSize)
            throw new FormatException($"Expected {StorageSize} bytes for {nameof(Hash32)}.");

        return new Hash32(bytes);
    }
}
