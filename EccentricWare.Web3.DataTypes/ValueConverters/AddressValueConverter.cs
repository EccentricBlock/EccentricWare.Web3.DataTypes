using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.ValueConverters;

/// <summary>
/// EF Core value converter for persisting <see cref="Address"/> into a single binary column.
/// Encoding: 1 byte <see cref="AddressType"/> + raw address bytes (20 for EVM, 32 for Solana).
/// </summary>
public sealed class AddressValueConverter : ValueConverter<Address, byte[]>
{
    /// <summary>
    /// The largest payload is 33 bytes (type byte + 32-byte Solana key). Declaring it lets providers use a bounded
    /// binary column (for example <c>varbinary(33)</c>) that can be indexed or used as a key, instead of an unbounded one.
    /// EVM values are 21 bytes, so the column is variable length rather than fixed.
    /// </summary>
    public const int MaxPayloadLength = 1 + Address.SolanaByteLength;

    private static readonly ConverterMappingHints DefaultHints = new(size: MaxPayloadLength);

    /// <summary>
    /// Creates a converter with the default mapping hints. EF Core instantiates converter types through a public
    /// parameterless constructor (optional parameters do not count), so one is declared explicitly.
    /// </summary>
    public AddressValueConverter() : this(null)
    {
    }

    /// <summary>
    /// Creates a converter that stores <see cref="Address"/> as a compact binary payload. Hints supplied by the caller
    /// (for example a size configured on the property) take precedence over the defaults.
    /// </summary>
    public AddressValueConverter(ConverterMappingHints? mappingHints)
        : base(
            model => AddressToBytes(model),
            provider => BytesToAddress(provider),
            ConverterHints.Merge(DefaultHints, mappingHints))
    {
    }

    /// <summary>Encodes an <see cref="Address"/> into a compact binary representation.</summary>
    public static byte[] AddressToBytes(Address address)
    {
        int addressLength = address.ByteLength;
        byte[] payload = new byte[1 + addressLength];

        payload[0] = (byte)address.Type;
        address.WriteBytes(payload.AsSpan(1));

        return payload;
    }

    /// <summary>
    /// Decodes an <see cref="Address"/> from the compact binary representation. A null payload throws
    /// <see cref="ArgumentNullException"/>; any other malformed payload (empty, unknown type byte, wrong length for the
    /// type) throws <see cref="FormatException"/>.
    /// </summary>
    public static Address BytesToAddress(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Length < 1)
            throw new FormatException("Address payload is empty.");

        AddressType addressType = (AddressType)payload[0];
        ReadOnlySpan<byte> bytes = payload.AsSpan(1);

        return addressType switch
        {
            AddressType.Evm when bytes.Length == Address.EvmByteLength => Address.FromEvmBytes(bytes),
            AddressType.Solana when bytes.Length == Address.SolanaByteLength => Address.FromSolanaBytes(bytes),
            _ => throw new FormatException($"Invalid {nameof(Address)} payload length {bytes.Length} for type {addressType}.")
        };
    }
}