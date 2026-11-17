using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>
/// Thin, allocation-free adapters between the RLP core and this library's value types (<see cref="uint256"/>,
/// <see cref="Hash32"/>, <see cref="Address"/>, <see cref="FunctionSelector"/>, <see cref="Signature"/>). The core types
/// do not depend on this class. Readers extend <see cref="RLPData"/>; writers extend <c>ref</c> <see cref="RLPWriter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Readers never throw on malformed input and set every <c>out</c> value to default on failure. Widths are checked
/// before any value-type constructor runs, so the constructors' argument exceptions are unreachable. Readers apply no
/// schema: a field count, a field position or an EIP-2718 type byte is the caller's concern (spec 10.1, 11).
/// </para>
/// <para>
/// Each writer writes exactly one item and emits the bytes straight into the destination through an internal writer
/// member (no stack temporary): the <see cref="uint256"/> writer from the limbs, the address and hash writers into a
/// reserved payload region. These members apply the same checks in the same order as
/// <see cref="RLPWriter.TryWriteUnsignedMagnitude"/> and <see cref="RLPWriter.TryWriteByteString"/>, so the writer's
/// state, parent-limit and atomicity rules apply unchanged: on any failure no byte and no writer field changes. The
/// legacy <c>v</c> writer is the exception to the "no stack temporary" rule: it builds the value in a 16-byte stack
/// buffer and writes it as a magnitude through the public writer path.
/// </para>
/// <para>
/// <see cref="Signature.V"/> from the signature readers is always the y parity, 0 or 1. The legacy wire value
/// (<c>27 + parity</c>, or EIP-155 <c>35 + 2 * chainId + parity</c>) is rebuilt from the parity and the chain id by
/// <see cref="TryWriteEvmLegacyV(ref RLPWriter, byte, ulong?)"/>; add 27 for the common 65-byte <c>r||s||v</c> form. The <c>byte</c> overloads
/// accept only a parity of 0 or 1; the <see cref="Signature"/> overloads of the same members also accept a <see cref="Signature.V"/> of 27 or 28
/// (parity <c>V - 27</c>) and reject a non-EVM signature, so a signature from elsewhere needs no manual rewriting. Sizing members
/// (<see cref="AddressEncodedLength"/>, <see cref="Hash32EncodedLength"/>, <see cref="GetUInt256EncodedLength"/>,
/// <see cref="TryGetEvmLegacyVEncodedLength(byte, ulong?, out int)"/>, <see cref="RLPEncoder.GetUInt64EncodedLength"/>) let a caller announce
/// exact list lengths to <see cref="RLPWriter.TryBeginList"/>; each equals what its writer emits.
/// </para>
/// </remarks>
public static partial class RLPTypedExtensions
{
    private const int Uint256ByteLength = 32;
    private const int Hash32ByteLength = 32;
    private const int EvmAddressByteLength = 20;
    private const int FunctionSelectorByteLength = 4;

    // ------------------------------------------------------------------ readers

    /// <summary>
    /// Interprets a byte string as an Ethereum unsigned integer up to 256 bits, with the rules of
    /// <see cref="RLPData.TryGetUInt64"/>: <see cref="RLPStatus.WrongKind"/> for a list,
    /// <see cref="RLPStatus.InvalidArgument"/> for <c>default</c>, the empty string is zero,
    /// <see cref="RLPStatus.NonCanonicalInteger"/> for a payload with a leading <c>00</c> byte (including the raw
    /// literal <c>00</c>; checked before the width), <see cref="RLPStatus.IntegerOverflow"/> for more than 32 payload
    /// bytes. <paramref name="value"/> is zero on failure.
    /// </summary>
    public static RLPStatus TryGetUInt256(this in RLPData data, out uint256 value)
    {
        value = default;
        RLPStatus status = RequireByteString(in data);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        ReadOnlySpan<byte> payload = data.Payload;
        if (payload.IsEmpty)
        {
            return RLPStatus.Success;
        }

        if (payload[0] == 0)
        {
            return RLPStatus.NonCanonicalInteger;
        }

        if (payload.Length > Uint256ByteLength)
        {
            return RLPStatus.IntegerOverflow;
        }

        if (payload.Length <= sizeof(ulong))
        {
            // One limb: the uint256 constructor's padded path is a non-inlined call, this one is not.
            value = new uint256(RLPData.ReadMagnitude(payload));
        }
        else
        {
            value = new uint256(payload);
        }

        return RLPStatus.Success;
    }

    /// <summary>
    /// Reads a byte string of exactly 32 bytes as a <see cref="Hash32"/>. <see cref="RLPStatus.WrongKind"/> for a list,
    /// <see cref="RLPStatus.InvalidArgument"/> for <c>default</c>, <see cref="RLPStatus.InvalidFixedLength"/> for any
    /// other length (including the empty string).
    /// </summary>
    public static RLPStatus TryGetHash32(this in RLPData data, out Hash32 value)
    {
        value = default;
        RLPStatus status = TryGetFixedWidthPayload(in data, Hash32ByteLength, out ReadOnlySpan<byte> payload);
        if (status == RLPStatus.Success)
        {
            value = new Hash32(payload);
        }

        return status;
    }

    /// <summary>
    /// Reads a byte string of exactly 20 bytes as an EVM <see cref="Address"/>. <see cref="RLPStatus.WrongKind"/> for a
    /// list, <see cref="RLPStatus.InvalidArgument"/> for <c>default</c>, <see cref="RLPStatus.InvalidFixedLength"/> for
    /// any other length (including the empty string; use <see cref="TryGetOptionalAddress"/> for a <c>to</c> field).
    /// </summary>
    public static RLPStatus TryGetAddress(this in RLPData data, out Address value)
    {
        value = default;
        RLPStatus status = TryGetFixedWidthPayload(in data, EvmAddressByteLength, out ReadOnlySpan<byte> payload);
        if (status == RLPStatus.Success)
        {
            value = Address.FromEvmBytes(payload);
        }

        return status;
    }

    /// <summary>
    /// Reads an optional EVM address, the shape of the transaction <c>to</c> field: the empty string succeeds with
    /// <paramref name="isEmpty"/> true and a default <paramref name="value"/> (contract creation); exactly 20 bytes
    /// succeed with <paramref name="isEmpty"/> false. Statuses otherwise as <see cref="TryGetAddress"/>. On failure
    /// <paramref name="value"/> is default and <paramref name="isEmpty"/> is false.
    /// </summary>
    /// <remarks>An empty <c>to</c> is written with <c>writer.TryWriteByteString(default)</c>; there is no dedicated writer.</remarks>
    public static RLPStatus TryGetOptionalAddress(this in RLPData data, out Address value, out bool isEmpty)
    {
        value = default;
        isEmpty = false;
        RLPStatus status = RequireByteString(in data);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        if (data.PayloadLength == 0)
        {
            isEmpty = true;
            return RLPStatus.Success;
        }

        return TryGetAddress(in data, out value);
    }

    /// <summary>
    /// Reads the 4-byte function selector from the start of a byte string, the calldata <c>data</c> field. Any payload
    /// of at least 4 bytes succeeds and the rest of the payload stays opaque (it is never interpreted here).
    /// <see cref="RLPStatus.InvalidFixedLength"/> for a shorter payload (including empty),
    /// <see cref="RLPStatus.WrongKind"/> for a list, <see cref="RLPStatus.InvalidArgument"/> for <c>default</c>.
    /// </summary>
    /// <remarks>
    /// Empty and 1..3 byte calldata are legal on the wire (plain transfers, fallback calls): branch on
    /// <see cref="RLPData.PayloadLength"/> before treating <see cref="RLPStatus.InvalidFixedLength"/> as a rejection.
    /// </remarks>
    public static RLPStatus TryGetFunctionSelector(this in RLPData data, out FunctionSelector selector)
    {
        selector = default;
        RLPStatus status = RequireByteString(in data);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        ReadOnlySpan<byte> payload = data.Payload;
        if (payload.Length < FunctionSelectorByteLength)
        {
            return RLPStatus.InvalidFixedLength;
        }

        selector = new FunctionSelector(payload);
        return RLPStatus.Success;
    }

    /// <summary>
    /// Builds an EVM <see cref="Signature"/> from the three signature fields of a typed transaction (EIP-2930,
    /// EIP-1559, EIP-4844, EIP-7702): <paramref name="yParity"/> is the y parity, the empty string (0) or <c>01</c> (1).
    /// <see cref="Signature.V"/> is that parity, 0 or 1 (not 27/28). Check order, first failure wins:
    /// <paramref name="yParity"/> (default is <see cref="RLPStatus.InvalidArgument"/>, a list is
    /// <see cref="RLPStatus.WrongKind"/>, a leading zero byte is <see cref="RLPStatus.NonCanonicalInteger"/>, more than
    /// 8 bytes is <see cref="RLPStatus.IntegerOverflow"/>, any other value is <see cref="RLPStatus.InvalidSignatureV"/>),
    /// then <paramref name="r"/>, then <paramref name="s"/>, each read as <see cref="TryGetUInt256"/> reads an integer
    /// (leading zero is <see cref="RLPStatus.NonCanonicalInteger"/>, more than 32 bytes is
    /// <see cref="RLPStatus.IntegerOverflow"/>) and left-padded to 32 bytes. <paramref name="signature"/> is default on failure.
    /// </summary>
    /// <remarks>
    /// There is deliberately no signature writer: it would add three items and could not be atomic. Write the fields as
    /// three integers. <see cref="Signature.R"/> and <see cref="Signature.S"/> are <see cref="Hash32"/> values, but the
    /// wire form is an integer: never write them with <c>TryWriteHash32</c>, which keeps leading zero bytes and
    /// produces a non-canonical integer for about one signature in 128.
    /// <code>
    /// writer.TryWriteEvmYParity(signature.V);
    /// writer.TryWriteUInt256(signature.R.ToUInt256());
    /// writer.TryWriteUInt256(signature.S.ToUInt256());
    /// </code>
    /// </remarks>
    public static RLPStatus TryGetEvmYParitySignature(in RLPData yParity, in RLPData r, in RLPData s, out Signature signature)
    {
        signature = default;
        RLPStatus status = TryGetYParity(in yParity, out byte parity);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        return TryGetSignatureWithParity(in r, in s, parity, out signature);
    }

    /// <summary>
    /// Builds an EVM <see cref="Signature"/> from the three signature fields of a legacy transaction and returns the
    /// chain id carried by <c>v</c>. <see cref="Signature.V"/> is the y parity, 0 or 1, never the legacy wire value (27,
    /// 28 or <c>35 + 2 * chainId + parity</c>); add 27 for the common 65-byte <c>r||s||v</c> form. To write it back, pass the signature to the <see cref="TryWriteEvmLegacyV(ref RLPWriter, in Signature, ulong?)"/> overload (V of 0, 1, 27 or 28).
    /// </summary>
    /// <param name="v">The legacy <c>v</c> field.</param>
    /// <param name="r">The <c>r</c> field.</param>
    /// <param name="s">The <c>s</c> field.</param>
    /// <param name="signature">The signature with <see cref="Signature.V"/> = y parity; default on failure.</param>
    /// <param name="chainId"><c>null</c> for a pre-EIP-155 signature (<c>v</c> of 27 or 28), otherwise the chain id (0 is possible for <c>v</c> of 35 or 36); <c>null</c> on failure.</param>
    /// <remarks>
    /// <para>
    /// Rules of <paramref name="v"/> (the arithmetic is EIP-155's):
    /// 27 or 28 is parity <c>v - 27</c> without a chain id; <c>v &gt;= 35</c> is parity <c>(v - 35) &amp; 1</c> and chain
    /// id <c>(v - 35) &gt;&gt; 1</c>; any other value, including the empty string, is
    /// <see cref="RLPStatus.InvalidSignatureV"/>. This domain check is deliberately stricter than go-ethereum's
    /// transaction decoding, which structurally accepts a <c>v</c> of 0, 1 and 29..34 and fails only later, at sender
    /// recovery; this adapter rejects them here so a <see cref="Signature"/> it returns always has a parity and, when
    /// present, a chain id. A chain id above <see cref="ulong.MaxValue"/> and a payload longer
    /// than 9 bytes (the width of the largest <c>v</c> a 64-bit chain id needs, <c>2^65 + 34</c>) are
    /// <see cref="RLPStatus.IntegerOverflow"/>. Check order, first failure wins: <paramref name="v"/> (default is
    /// <see cref="RLPStatus.InvalidArgument"/>, a list <see cref="RLPStatus.WrongKind"/>, a leading zero byte
    /// <see cref="RLPStatus.NonCanonicalInteger"/> before the width rule, then the width and domain rules), then
    /// <paramref name="r"/>, then <paramref name="s"/> with the rules of <see cref="TryGetUInt256"/>.
    /// </para>
    /// <para>
    /// This reads only the signature fields. Whether the chain id is the one the node expects, and whether the signature
    /// recovers a sender, are the caller's checks. The wire value is rebuilt with <see cref="TryWriteEvmLegacyV(ref RLPWriter, byte, ulong?)"/>.
    /// </para>
    /// </remarks>
    public static RLPStatus TryGetEvmLegacySignature(in RLPData v, in RLPData r, in RLPData s, out Signature signature, out ulong? chainId)
    {
        signature = default;
        chainId = null;
        RLPStatus status = TryGetLegacyV(in v, out byte parity, out ulong? chain);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        status = TryGetSignatureWithParity(in r, in s, parity, out signature);
        if (status == RLPStatus.Success)
        {
            chainId = chain;
        }

        return status;
    }

    /// <summary>Encoded size of an EVM address item: the <c>94</c> prefix plus 20 bytes. Equals what <see cref="TryWriteAddress"/> emits.</summary>
    public const int AddressEncodedLength = 1 + EvmAddressByteLength;

    /// <summary>Encoded size of a <see cref="Hash32"/> item: the <c>a0</c> prefix plus 32 bytes. Equals what <see cref="TryWriteHash32"/> emits.</summary>
    public const int Hash32EncodedLength = 1 + Hash32ByteLength;

    /// <summary>
    /// Encoded size of <paramref name="value"/> as an integer item, 1..33: 1 for zero (<c>80</c>) and for 1..127 (the
    /// literal byte), otherwise 1 plus its minimal byte count. Equals what <see cref="TryWriteUInt256"/> emits, so a
    /// caller can announce exact list lengths to <see cref="RLPWriter.TryBeginList"/>.
    /// </summary>
    public static int GetUInt256EncodedLength(uint256 value)
    {
        int byteCount = value.GetByteCount();
        return byteCount == 0 || (byteCount == 1 && value.U0 < 0x80) ? 1 : 1 + byteCount;
    }

    /// <summary>
    /// Encoded size of the legacy <c>v</c> item <see cref="TryWriteEvmLegacyV(ref RLPWriter, byte, ulong?)"/> writes for the same arguments: 1 for
    /// every <c>v</c> below 128, otherwise 1 plus its minimal byte count (up to 10). That is 1 byte without a chain id and for chain ids
    /// 0..45 with either parity; chain id 46 is 1 byte only for parity 0 (<c>v</c> = 127, the literal <c>7f</c>), while parity 1 gives
    /// <c>v</c> = 128, which is <c>81 80</c> (2 bytes).
    /// </summary>
    /// <param name="yParity">The y parity, 0 or 1.</param>
    /// <param name="chainId">The chain id, or <c>null</c> for a pre-EIP-155 signature.</param>
    /// <param name="totalLength">The size in bytes; 0 on failure.</param>
    /// <returns><see cref="RLPStatus.InvalidSignatureV"/> when <paramref name="yParity"/> is above 1.</returns>
    public static RLPStatus TryGetEvmLegacyVEncodedLength(byte yParity, ulong? chainId, out int totalLength)
    {
        if (yParity > 1)
        {
            totalLength = 0;
            return RLPStatus.InvalidSignatureV;
        }

        UInt128 legacyV = BuildLegacyV(yParity, chainId);
        totalLength = legacyV < 0x80 ? 1 : 1 + GetMagnitudeByteCount(legacyV);
        return RLPStatus.Success;
    }

    /// <summary>
    /// Encoded size of the legacy <c>v</c> item <see cref="TryWriteEvmLegacyV(ref RLPWriter, in Signature, ulong?)"/> writes for the same
    /// arguments (see <see cref="TryGetEvmLegacyVEncodedLength(byte, ulong?, out int)"/> for the sizes).
    /// </summary>
    /// <param name="signature">The EVM signature; <see cref="Signature.V"/> must be 0, 1, 27 or 28.</param>
    /// <param name="chainId">The chain id, or <c>null</c> for a pre-EIP-155 signature.</param>
    /// <param name="totalLength">The size in bytes; 0 on failure.</param>
    /// <returns><see cref="RLPStatus.InvalidArgument"/> for a non-EVM signature, <see cref="RLPStatus.InvalidSignatureV"/> for any other <c>V</c>.</returns>
    public static RLPStatus TryGetEvmLegacyVEncodedLength(in Signature signature, ulong? chainId, out int totalLength)
    {
        RLPStatus status = GetYParity(in signature, out byte yParity);
        if (status != RLPStatus.Success)
        {
            totalLength = 0;
            return status;
        }

        return TryGetEvmLegacyVEncodedLength(yParity, chainId, out totalLength);
    }

    // ------------------------------------------------------------------ helpers

    // A ulong chain id needs v = 2 * (2^64 - 1) + 35 + 1 = 2^65 + 34, which is 9 bytes.
    private const int MaxLegacyVByteLength = 9;
    private const int LegacyVUnprotectedBase = 27;
    private const int LegacyVProtectedBase = 35;

    /// <summary>
    /// The y parity carried by <see cref="Signature.V"/> for the signature-taking writers and sizer: 0 or 1 is the parity,
    /// 27 or 28 is <c>V - 27</c>. A non-EVM signature is <see cref="RLPStatus.InvalidArgument"/>, any other <c>V</c>
    /// <see cref="RLPStatus.InvalidSignatureV"/>; <paramref name="yParity"/> is 0 on failure.
    /// </summary>
    private static RLPStatus GetYParity(in Signature signature, out byte yParity)
    {
        yParity = 0;
        if (signature.Type != SignatureType.Evm)
        {
            return RLPStatus.InvalidArgument;
        }

        byte v = signature.V;
        if (v <= 1)
        {
            yParity = v;
            return RLPStatus.Success;
        }

        if (v == LegacyVUnprotectedBase || v == LegacyVUnprotectedBase + 1)
        {
            yParity = (byte)(v - LegacyVUnprotectedBase);
            return RLPStatus.Success;
        }

        return RLPStatus.InvalidSignatureV;
    }

    /// <summary>Typed y parity: empty (0) or <c>01</c> (1). Leading zero is checked before the width and the domain.</summary>
    private static RLPStatus TryGetYParity(in RLPData data, out byte parity)
    {
        parity = 0;
        RLPStatus status = RequireByteString(in data);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        ReadOnlySpan<byte> payload = data.Payload;
        if (payload.IsEmpty)
        {
            return RLPStatus.Success;
        }

        if (payload[0] == 0)
        {
            return RLPStatus.NonCanonicalInteger;
        }

        if (payload.Length > sizeof(ulong))
        {
            return RLPStatus.IntegerOverflow;
        }

        if (payload.Length == 1 && payload[0] == 1)
        {
            parity = 1;
            return RLPStatus.Success;
        }

        return RLPStatus.InvalidSignatureV;
    }

    /// <summary>Legacy <c>v</c> to (parity, optional chain id); see <see cref="TryGetEvmLegacySignature"/>.</summary>
    private static RLPStatus TryGetLegacyV(in RLPData data, out byte parity, out ulong? chainId)
    {
        parity = 0;
        chainId = null;
        RLPStatus status = RequireByteString(in data);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        ReadOnlySpan<byte> payload = data.Payload;
        if (payload.IsEmpty)
        {
            return RLPStatus.InvalidSignatureV; // the empty string is v = 0
        }

        if (payload[0] == 0)
        {
            return RLPStatus.NonCanonicalInteger;
        }

        if (payload.Length > MaxLegacyVByteLength)
        {
            return RLPStatus.IntegerOverflow;
        }

        UInt128 value = 0;
        foreach (byte b in payload)
        {
            value = (value << 8) | b;
        }

        if (value == LegacyVUnprotectedBase || value == LegacyVUnprotectedBase + 1)
        {
            parity = (byte)(value - LegacyVUnprotectedBase);
            return RLPStatus.Success;
        }

        if (value < LegacyVProtectedBase)
        {
            return RLPStatus.InvalidSignatureV;
        }

        UInt128 offset = value - LegacyVProtectedBase;
        UInt128 chain = offset >> 1;
        if (chain > ulong.MaxValue)
        {
            return RLPStatus.IntegerOverflow;
        }

        parity = (byte)(offset & 1);
        chainId = (ulong)chain;
        return RLPStatus.Success;
    }

    /// <summary>Reads <c>r</c> then <c>s</c> as integers and builds the signature around an already validated parity.</summary>
    private static RLPStatus TryGetSignatureWithParity(in RLPData r, in RLPData s, byte parity, out Signature signature)
    {
        signature = default;
        RLPStatus status = r.TryGetUInt256(out uint256 rValue);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        status = s.TryGetUInt256(out uint256 sValue);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        Span<byte> r32 = stackalloc byte[Uint256ByteLength];
        Span<byte> s32 = stackalloc byte[Uint256ByteLength];
        rValue.WriteBigEndian(r32);
        sValue.WriteBigEndian(s32);
        signature = Signature.FromEvmComponents(r32, s32, parity);
        return RLPStatus.Success;
    }

    /// <summary>The legacy wire value: <c>27 + parity</c>, or <c>35 + 2 * chainId + parity</c> in 128 bits so no <see cref="ulong"/> chain id overflows. <paramref name="yParity"/> must be 0 or 1.</summary>
    private static UInt128 BuildLegacyV(byte yParity, ulong? chainId) =>
        chainId is ulong chain
            ? LegacyVProtectedBase + ((UInt128)chain << 1) + yParity
            : (UInt128)(LegacyVUnprotectedBase + yParity);

    /// <summary>Minimal big-endian byte count of a non-zero value, 1..16.</summary>
    private static int GetMagnitudeByteCount(UInt128 value) => (128 - (int)UInt128.LeadingZeroCount(value) + 7) >> 3;

    /// <summary>Kind gate shared by every byte-string accessor: <c>default</c> is InvalidArgument, a list is WrongKind.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RLPStatus RequireByteString(in RLPData data)
    {
        RLPKind kind = data.Kind;
        if (kind == RLPKind.ByteString)
        {
            return RLPStatus.Success;
        }

        return kind == RLPKind.None ? RLPStatus.InvalidArgument : RLPStatus.WrongKind;
    }

    private static RLPStatus TryGetFixedWidthPayload(in RLPData data, int requiredLength, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        RLPStatus status = RequireByteString(in data);
        if (status != RLPStatus.Success)
        {
            return status;
        }

        ReadOnlySpan<byte> candidate = data.Payload;
        if (candidate.Length != requiredLength)
        {
            return RLPStatus.InvalidFixedLength;
        }

        payload = candidate;
        return RLPStatus.Success;
    }
}
