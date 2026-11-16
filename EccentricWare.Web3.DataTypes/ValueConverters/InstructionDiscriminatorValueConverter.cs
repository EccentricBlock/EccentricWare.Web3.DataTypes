using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.ValueConverters;

/// <summary>
/// EF Core value converter storing an <see cref="InstructionDiscriminator"/> as a 64-bit integer (BIGINT) that sorts like
/// the discriminator.
/// </summary>
/// <remarks>
/// A BIGINT column needs no byte-array allocation per value or query parameter. The discriminator is unsigned but the
/// column is signed, so the stored value is the discriminator minus 2^63 (the sign bit is inverted): 0 is stored as
/// <see cref="long.MinValue"/> and <see cref="ulong.MaxValue"/> as <see cref="long.MaxValue"/>. That keeps EF Core's
/// <c>&lt;</c>, <c>&gt;</c> and <c>OrderBy</c> translations correct, but the stored number is not the discriminator's own
/// value, so anything that reads the column outside this converter must add 2^63 back (or use
/// <see cref="InstructionDiscriminatorBinaryValueConverter"/>, whose bytes are the discriminator itself).
/// </remarks>
public sealed class InstructionDiscriminatorValueConverter : ValueConverter<InstructionDiscriminator, long>
{
    private static readonly Expression<Func<InstructionDiscriminator, long>> ToProviderExpr =
        static v => Encode(v);

    private static readonly Expression<Func<long, InstructionDiscriminator>> FromProviderExpr =
        static v => Decode(v);

    /// <summary>Creates the converter.</summary>
    public InstructionDiscriminatorValueConverter() : base(ToProviderExpr, FromProviderExpr)
    {
    }

    /// <summary>Creates the converter with explicit mapping hints.</summary>
    public InstructionDiscriminatorValueConverter(ConverterMappingHints? mappingHints)
        : base(ToProviderExpr, FromProviderExpr, mappingHints)
    {
    }

    private static long Encode(InstructionDiscriminator value) => value.AsInt64 ^ long.MinValue;

    private static InstructionDiscriminator Decode(long stored) => InstructionDiscriminator.FromInt64(stored ^ long.MinValue);
}

/// <summary>
/// EF Core value converter storing an <see cref="InstructionDiscriminator"/> as 8 big-endian bytes (BINARY(8)),
/// which sorts in the same order as the unsigned value and the hex string, so range filters and <c>ORDER BY</c> on the
/// column agree with in-memory comparison. Each conversion allocates an 8-byte array.
/// </summary>
/// <remarks>
/// The converter declares an 8-byte size hint, so providers create a bounded column. Hints supplied by the caller are
/// laid over it. A stored value that is not exactly 8 bytes throws <see cref="FormatException"/>.
/// </remarks>
public sealed class InstructionDiscriminatorBinaryValueConverter : ValueConverter<InstructionDiscriminator, byte[]>
{
    /// <summary>Storage size in bytes.</summary>
    public const int StorageSize = InstructionDiscriminator.ByteLength; // 8

    private static readonly Expression<Func<InstructionDiscriminator, byte[]>> ToProviderExpr =
        static v => ToBytes(v);

    private static readonly Expression<Func<byte[], InstructionDiscriminator>> FromProviderExpr =
        static v => FromBytes(v);

    /// <summary>Default mapping hints (fixed size 8).</summary>
    public static readonly ConverterMappingHints DefaultHints = new(size: StorageSize);

    /// <summary>Creates the converter with the default mapping hints.</summary>
    public InstructionDiscriminatorBinaryValueConverter() : this(null)
    {
    }

    /// <summary>Creates the converter; hints supplied by the caller take precedence over <see cref="DefaultHints"/>.</summary>
    public InstructionDiscriminatorBinaryValueConverter(ConverterMappingHints? mappingHints)
        : base(ToProviderExpr, FromProviderExpr, ConverterHints.Merge(DefaultHints, mappingHints))
    {
    }

    private static byte[] ToBytes(InstructionDiscriminator value)
    {
        var bytes = new byte[StorageSize];
        value.WriteBytes(bytes);
        return bytes;
    }

    private static InstructionDiscriminator FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length != StorageSize)
            throw new FormatException($"Expected {StorageSize} bytes for {nameof(InstructionDiscriminator)}.");

        return new InstructionDiscriminator(bytes);
    }
}
