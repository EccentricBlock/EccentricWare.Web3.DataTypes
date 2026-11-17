using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>Writer adapters of <see cref="RLPTypedExtensions"/>; see the remarks there.</summary>
public static partial class RLPTypedExtensions
{
    /// <summary>
    /// Writes <paramref name="value"/> as one canonical Ethereum unsigned integer: zero is <c>80</c>, a value below
    /// <c>0x80</c> is the literal byte, otherwise <c>0x80 + n</c> followed by the shortest big-endian magnitude
    /// (<c>n</c> is 1..32). Atomic like <see cref="RLPWriter.TryWriteUnsignedMagnitude"/>.
    /// </summary>
    public static RLPStatus TryWriteUInt256(this ref RLPWriter writer, uint256 value) => writer.TryWriteUInt256Limbs(in value);

    /// <summary>Writes <paramref name="value"/> as a 32-byte string (<c>a0</c> followed by the 32 bytes). Atomic like <see cref="RLPWriter.TryWriteByteString"/>.</summary>
    public static RLPStatus TryWriteHash32(this ref RLPWriter writer, Hash32 value)
    {
        RLPStatus status = writer.TryReserveShortByteString(Hash32ByteLength, out Span<byte> payload);
        if (status == RLPStatus.Success)
        {
            value.WriteBigEndian(payload);
        }

        return status;
    }

    /// <summary>
    /// Writes an EVM address as a 20-byte string (<c>94</c> followed by the 20 bytes). A Solana-type address is
    /// <see cref="RLPStatus.InvalidArgument"/> and the writer is untouched. The writer's own state and root checks
    /// still come first, so a completed or default writer reports <see cref="RLPStatus.WriterStateInvalid"/> and a
    /// second root <see cref="RLPStatus.ExtraRoot"/>, whatever the address type. Atomic like <see cref="RLPWriter.TryWriteByteString"/>.
    /// </summary>
    public static RLPStatus TryWriteAddress(this ref RLPWriter writer, Address value)
    {
        if (value.Type != AddressType.Evm)
        {
            return RejectWithArgumentError(ref writer, RLPStatus.InvalidArgument);
        }

        RLPStatus status = writer.TryReserveShortByteString(EvmAddressByteLength, out Span<byte> payload);
        if (status == RLPStatus.Success)
        {
            value.WriteBytes(payload);
        }

        return status;
    }

    /// <summary>
    /// Writes the y parity of a typed transaction signature (EIP-2930, EIP-1559, EIP-4844, EIP-7702) as one integer: 0
    /// is <c>80</c>, 1 is <c>01</c>. A parity above 1 is <see cref="RLPStatus.InvalidSignatureV"/> and the writer is
    /// untouched; the writer's own state and root checks still come first. Pass <see cref="Signature.V"/> of a signature read with <see cref="TryGetEvmYParitySignature"/>.
    /// Atomic like <see cref="RLPWriter.TryWriteUInt64"/>.
    /// </summary>
    public static RLPStatus TryWriteEvmYParity(this ref RLPWriter writer, byte yParity)
    {
        if (yParity > 1)
        {
            return RejectWithArgumentError(ref writer, RLPStatus.InvalidSignatureV);
        }

        return writer.TryWriteUInt64(yParity);
    }

    /// <summary>
    /// Writes the legacy <c>v</c> of a transaction signature as one integer: <c>27 + yParity</c> without a chain id,
    /// otherwise the EIP-155 value <c>35 + 2 * chainId + yParity</c>, computed in 128 bits so no <see cref="ulong"/>
    /// chain id overflows (the largest value is 9 bytes). A parity above 1 is <see cref="RLPStatus.InvalidSignatureV"/>
    /// and the writer is untouched. Check order: writer state (<see cref="RLPStatus.WriterStateInvalid"/>), root
    /// discipline (<see cref="RLPStatus.ExtraRoot"/>), the parity, then size and capacity as
    /// <see cref="RLPWriter.TryWriteUnsignedMagnitude"/> does. Atomic. The size is
    /// <see cref="TryGetEvmLegacyVEncodedLength(byte, ulong?, out int)"/>; <see cref="TryGetEvmLegacySignature"/> reads it back.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="yParity">The y parity, 0 or 1 (<see cref="Signature.V"/>).</param>
    /// <param name="chainId">The chain id, or <c>null</c> for a pre-EIP-155 signature.</param>
    public static RLPStatus TryWriteEvmLegacyV(this ref RLPWriter writer, byte yParity, ulong? chainId)
    {
        if (yParity > 1)
        {
            return RejectWithArgumentError(ref writer, RLPStatus.InvalidSignatureV);
        }

        UInt128 legacyV = BuildLegacyV(yParity, chainId);
        int byteCount = GetMagnitudeByteCount(legacyV);
        Span<byte> buffer = stackalloc byte[sizeof(ulong) * 2];
        BinaryPrimitives.WriteUInt128BigEndian(buffer, legacyV);
        return writer.TryWriteUnsignedMagnitude(buffer.Slice(buffer.Length - byteCount));
    }

    /// <summary>
    /// Writes the y parity of <paramref name="signature"/> as <see cref="TryWriteEvmYParity(ref RLPWriter, byte)"/> does, accepting
    /// both conventions of <see cref="Signature.V"/>: 0 or 1 is the parity (what the signature readers return), 27 or 28
    /// is parity <c>V - 27</c> (the standard 65-byte <c>r||s||v</c> form). Any other <c>V</c> is
    /// <see cref="RLPStatus.InvalidSignatureV"/>; a signature that is not <see cref="SignatureType.Evm"/> is
    /// <see cref="RLPStatus.InvalidArgument"/> (checked first). Check order: writer state, root discipline, then the
    /// signature argument, then size and capacity. Atomic.
    /// </summary>
    public static RLPStatus TryWriteEvmYParity(this ref RLPWriter writer, in Signature signature)
    {
        RLPStatus argumentStatus = GetYParity(in signature, out byte yParity);
        return argumentStatus == RLPStatus.Success
            ? writer.TryWriteUInt64(yParity)
            : RejectWithArgumentError(ref writer, argumentStatus);
    }

    /// <summary>
    /// Writes the legacy <c>v</c> for <paramref name="signature"/> and <paramref name="chainId"/> as
    /// <see cref="TryWriteEvmLegacyV(ref RLPWriter, byte, ulong?)"/> does, taking the parity from
    /// <see cref="Signature.V"/> the way <see cref="TryWriteEvmYParity(ref RLPWriter, in Signature)"/> does (0, 1, 27 or 28;
    /// anything else is <see cref="RLPStatus.InvalidSignatureV"/>, a non-EVM signature is
    /// <see cref="RLPStatus.InvalidArgument"/>). A <c>V</c> of 27 or 28 selects the parity only: the chain id comes
    /// from <paramref name="chainId"/>, never from <c>V</c>. Same check order and atomicity. The size is
    /// <see cref="TryGetEvmLegacyVEncodedLength(in Signature, ulong?, out int)"/>.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="signature">The EVM signature whose <see cref="Signature.V"/> is 0, 1, 27 or 28.</param>
    /// <param name="chainId">The chain id, or <c>null</c> for a pre-EIP-155 signature.</param>
    public static RLPStatus TryWriteEvmLegacyV(this ref RLPWriter writer, in Signature signature, ulong? chainId)
    {
        RLPStatus argumentStatus = GetYParity(in signature, out byte yParity);
        return argumentStatus == RLPStatus.Success
            ? writer.TryWriteEvmLegacyV(yParity, chainId)
            : RejectWithArgumentError(ref writer, argumentStatus);
    }

    /// <summary>
    /// Probes the writer with a magnitude it always rejects (a leading <c>00</c>) so its state and root checks run in
    /// their normal order, then reports <paramref name="argumentError"/>. The probe never writes. Relies on the core
    /// order: state, root discipline, then the leading-zero magnitude check, before size and capacity (pinned by the
    /// writer state-machine tests).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RLPStatus RejectWithArgumentError(ref RLPWriter writer, RLPStatus argumentError)
    {
        ReadOnlySpan<byte> alwaysRejectedMagnitude = [0];
        RLPStatus status = writer.TryWriteUnsignedMagnitude(alwaysRejectedMagnitude);
        return status == RLPStatus.NonCanonicalInteger ? argumentError : status;
    }
}
