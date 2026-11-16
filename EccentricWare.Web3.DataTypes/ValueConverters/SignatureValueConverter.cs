using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.ValueConverters;


/// <summary>
/// EF Core value converter for <see cref="Signature"/> using a compact fixed-length binary encoding.
/// </summary>
/// <remarks>
/// Provider layout (66 bytes):
/// - [0]      : SignatureType (1 byte)
/// - [1..64]  : Signature payload (64 bytes)
/// - [65]     : EVM v byte (0 for Solana)
///
/// The layout is ordered like <see cref="Signature.CompareTo(Signature)"/> (type, payload, v), so ORDER BY on the
/// binary column and in-memory sorting agree.
///
/// Recommended column types:
/// - SQL Server: BINARY(66) or VARBINARY(66)
/// - PostgreSQL: BYTEA (enforce length via constraint if needed)
///
/// The converter declares a 66-byte size hint; hints supplied by the caller are laid over it. A stored value that is not a
/// well-formed row (wrong length, unknown type byte, or a non-zero v byte on a Solana row) throws
/// <see cref="FormatException"/>; a null value throws <see cref="ArgumentNullException"/>.
/// </remarks>
public sealed class SignatureValueConverter : ValueConverter<Signature, byte[]>
{
    /// <summary>The fixed provider length in bytes.</summary>
    public const int StorageSize = 66;

    private static readonly ConverterMappingHints DefaultHints = new(size: StorageSize);

    /// <summary>
    /// Creates the converter with the default 66-byte mapping hint. EF Core instantiates converter types through a
    /// public parameterless constructor (optional parameters do not count), so one is declared explicitly.
    /// </summary>
    public SignatureValueConverter() : this(null)
    {
    }

    /// <summary>
    /// Creates the converter; hints supplied by the caller take precedence over the default 66-byte size.
    /// </summary>
    public SignatureValueConverter(ConverterMappingHints? mappingHints)
        : base(ToProviderExpression, FromProviderExpression, ConverterHints.Merge(DefaultHints, mappingHints))
    {
    }

    private static readonly Expression<Func<Signature, byte[]>> ToProviderExpression
        = sig => ToProviderBytes(sig);

    private static readonly Expression<Func<byte[], Signature>> FromProviderExpression
        = bytes => FromProviderBytes(bytes);

    /// <summary>
    /// Converts a <see cref="Signature"/> to the 66-byte provider encoding.
    /// </summary>
    private static byte[] ToProviderBytes(Signature signature)
    {
        byte[] bytes = new byte[StorageSize];
        bytes[0] = (byte)signature.Type;

        // Writes 64 payload bytes; for EVM writes v into offset 64 of this span (i.e. bytes[65]).
        signature.WriteBytes(bytes.AsSpan(1));
        return bytes;
    }

    /// <summary>
    /// Converts the 66-byte provider encoding to a <see cref="Signature"/>.
    /// </summary>
    private static Signature FromProviderBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length != StorageSize)
            throw new FormatException($"Invalid signature byte length; expected {StorageSize} bytes.");

        switch ((SignatureType)bytes[0])
        {
            case SignatureType.Evm:
                return Signature.FromEvmBytes(bytes.AsSpan(1, Signature.EvmByteLength));

            case SignatureType.Solana:
                // A Solana signature has no v. A non-zero byte 65 means the row was not written by this converter;
                // dropping it silently would hide corruption.
                if (bytes[65] != 0)
                    throw new FormatException("Invalid Solana signature row; byte 65 (v) must be zero.");

                return Signature.FromSolanaBytes(bytes.AsSpan(1, Signature.SolanaByteLength));

            default:
                throw new FormatException("Unknown signature type byte.");
        }
    }
}
