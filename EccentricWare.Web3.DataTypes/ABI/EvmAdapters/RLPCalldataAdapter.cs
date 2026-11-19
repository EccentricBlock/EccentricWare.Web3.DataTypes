using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.ABI.EvmAdapters;

/// <summary>
/// The RLP-to-ABI boundary of spec 9.4 (named <c>RlpCalldataAdapter</c> in spec 12; spelled with the repository's <c>RLP</c> casing). It validates an EVM
/// transaction envelope with <see cref="RLPData"/> and returns an <see cref="EvmTransactionView"/> whose <c>data</c> is the UNCHANGED span of the envelope
/// bytes, never a copy or a re-encoding, classified so only a function call can reach the ABI codec.
/// </summary>
/// <remarks>
/// <para>
/// Field layouts, as the EIPs give them (signed form; the unsigned form of a typed transaction drops the last three fields):
/// <list type="table">
/// <listheader><term>Type</term><description>Fields (count)</description></listheader>
/// <item><term>legacy</term><description><c>nonce, gasPrice, gasLimit, to, value, data, v, r, s</c> (9); unsigned: the first 6, or the first 6 plus <c>chainId, 0, 0</c> (EIP-155).</description></item>
/// <item><term>EIP-2930</term><description><c>chainId, nonce, gasPrice, gasLimit, to, value, data, accessList, yParity, r, s</c> (11)</description></item>
/// <item><term>EIP-1559</term><description><c>chainId, nonce, maxPriorityFee, maxFee, gasLimit, to, value, data, accessList, yParity, r, s</c> (12)</description></item>
/// <item><term>EIP-4844</term><description><c>chainId, nonce, maxPriorityFee, maxFee, gasLimit, to, value, data, accessList, maxFeePerBlobGas, blobVersionedHashes, yParity, r, s</c> (14)</description></item>
/// <item><term>EIP-7702</term><description><c>chainId, nonce, maxPriorityFee, maxFee, gasLimit, destination, value, data, accessList, authorizationList, yParity, r, s</c> (13)</description></item>
/// </list>
/// EIP-4844 and EIP-7702 transactions cannot create contracts: an empty <c>to</c> is <see cref="EvmTransactionStatus.InvalidTo"/>. The EIP-4844 network
/// wrapper (<c>[tx_payload_body, blobs, commitments, proofs]</c>) is not a transaction body and fails with <see cref="EvmTransactionStatus.WrongFieldCount"/>.
/// EIP-4844 <c>blob_versioned_hashes</c> must be non-empty, each 32 bytes starting with <c>0x01</c> (<see cref="EvmTransactionStatus.InvalidBlobHashes"/>).
/// An EIP-7702 <c>authorizationList</c> must be non-empty, and each entry must be <c>[chain_id, address, nonce, y_parity, r, s]</c> within the EIP's bounds
/// (<see cref="EvmTransactionStatus.InvalidAuthorizationList"/>). Layouts and bounds checked against the EIP-2718, EIP-2930, EIP-1559, EIP-4844, EIP-7702
/// and EIP-2681 texts.
/// </para>
/// <para>
/// What is checked: the type byte, complete canonical RLP within the limits (the RLP layer's contract), the exact field count for the type and declared form,
/// the RLP kind (byte string or list) of every field, the chain id, the nonce, <c>to</c>, <c>value</c>, the signature fields (with the secp256k1 bounds)
/// or their unsigned placeholders, the blob hashes and every authorization tuple. What is not: gas and fee values beyond their kind, the access list beyond
/// its kind, other consensus rules, sender recovery.
/// Never throws on malformed input; on failure the view is <c>default</c> (invalid).
/// </para>
/// </remarks>
public static class RLPCalldataAdapter
{
    private const int MaxFieldCount = 14;

    private readonly record struct Layout(
        int SignedCount, int Nonce, int To, int Value, int Data, uint ListFields, bool AllowsCreation, int AuthorizationList, int BlobHashes = -1);

    /// <summary>EIP-4844 <c>VERSIONED_HASH_VERSION_KZG</c>: the first byte of every blob versioned hash.</summary>
    private const byte BlobHashVersion = 0x01;

    private static Layout LayoutOf(EvmTransactionType type) => type switch
    {
        EvmTransactionType.Legacy => new(9, 0, 3, 4, 5, 0, AllowsCreation: true, AuthorizationList: -1),
        EvmTransactionType.Eip2930 => new(11, 1, 4, 5, 6, 1u << 7, AllowsCreation: true, AuthorizationList: -1),
        EvmTransactionType.Eip1559 => new(12, 1, 5, 6, 7, 1u << 8, AllowsCreation: true, AuthorizationList: -1),
        EvmTransactionType.Eip4844 => new(14, 1, 5, 6, 7, (1u << 8) | (1u << 10), AllowsCreation: false, AuthorizationList: -1, BlobHashes: 10),
        _ => new(13, 1, 5, 6, 7, (1u << 8) | (1u << 9), AllowsCreation: false, AuthorizationList: 9), // Eip7702
    };

    /// <summary>
    /// Reads <paramref name="envelope"/> as a transaction of the declared <paramref name="form"/>. On success <paramref name="transaction"/> borrows the
    /// envelope; on failure it is <c>default</c> and <paramref name="status"/> names the first rule broken, in this order: arguments, empty input, type byte,
    /// RLP, field count, field kinds (in field order), then chain id, signature fields, nonce, <c>to</c>, <c>value</c>, the blob hashes, the authorization list.
    /// </summary>
    /// <param name="envelope">The exact transaction bytes, including the EIP-2718 type byte for a typed transaction.</param>
    /// <param name="form">Whether the bytes are the signed transaction or the unsigned signing payload.</param>
    /// <param name="limits">RLP limits for the body; <see cref="RLPDecodeLimits.Default"/> suits one transaction from an untrusted peer.</param>
    /// <param name="transaction">The view, or <c>default</c> on failure.</param>
    /// <param name="status">The outcome.</param>
    public static bool TryRead(
        ReadOnlySpan<byte> envelope,
        EvmTransactionForm form,
        scoped in RLPDecodeLimits limits,
        out EvmTransactionView transaction,
        out EvmTransactionStatus status)
    {
        transaction = default;
        status = Read(envelope, form, in limits, out transaction);
        if (status != EvmTransactionStatus.Success)
        {
            transaction = default;
            return false;
        }

        return true;
    }

    private static EvmTransactionStatus Read(ReadOnlySpan<byte> envelope, EvmTransactionForm form, scoped in RLPDecodeLimits limits, out EvmTransactionView transaction)
    {
        transaction = default;
        if (!limits.IsValid || form > EvmTransactionForm.SigningPayload)
        {
            return EvmTransactionStatus.InvalidArgument;
        }

        if (envelope.IsEmpty)
        {
            return EvmTransactionStatus.EmptyEnvelope;
        }

        // EIP-2718: a first byte of 0x00..0x7f is a type byte. Anything else must be a legacy RLP list; a byte string (0x80..0xbf) fails below as MalformedRlp.
        EvmTransactionType type = EvmTransactionType.Legacy;
        int bodyStart = 0;
        if (envelope[0] <= 0x7f)
        {
            if (envelope[0] is < (byte)EvmTransactionType.Eip2930 or > (byte)EvmTransactionType.Eip7702)
            {
                return EvmTransactionStatus.UnsupportedType;
            }

            type = (EvmTransactionType)envelope[0];
            bodyStart = 1;
        }

        if (RLPData.TryDecodeComplete(envelope[bodyStart..], in limits, out RLPData body, out _) != RLPStatus.Success || body.Kind != RLPKind.List)
        {
            return EvmTransactionStatus.MalformedRlp;
        }

        // Pass 1: the field count (stops early past the largest layout). Pass 2 reads the fields. The enumerator, unlike a stack-backed index, yields
        // items with the envelope's lifetime, so the data span can be returned.
        int count = 0;
        if (body.TryGetChildren(out RLPChildEnumerator counter) != RLPStatus.Success)
        {
            return EvmTransactionStatus.MalformedRlp;
        }

        while (count <= MaxFieldCount && counter.MoveNext())
        {
            count++;
        }

        Layout layout = LayoutOf(type);
        bool signed = form == EvmTransactionForm.SignedTransaction;
        bool countMatches = type == EvmTransactionType.Legacy
            ? count == 9 || (!signed && count == 6)
            : count == (signed ? layout.SignedCount : layout.SignedCount - 3);
        if (!countMatches)
        {
            return EvmTransactionStatus.WrongFieldCount;
        }

        // The fields the adapter interprets; the chain id field is field 0 of a typed transaction and field 6 of an unsigned EIP-155 legacy payload.
        int chainIndex = type != EvmTransactionType.Legacy ? 0 : !signed && count == 9 ? 6 : -1;
        int signatureStart = type == EvmTransactionType.Legacy ? (count == 9 ? 6 : -1) : signed ? count - 3 : -1;
        RLPData nonceField = default, toField = default, valueField = default, dataField = default, chainField = default, authorizations = default, blobHashes = default;
        RLPData signature0 = default, signature1 = default, signature2 = default;

        _ = body.TryGetChildren(out RLPChildEnumerator reader);
        for (int i = 0; i < count && reader.MoveNext(); i++)
        {
            RLPData field = reader.Current;
            if ((field.Kind == RLPKind.List) != ((layout.ListFields & (1u << i)) != 0))
            {
                return FieldStatus(type, signed, count, layout, i);
            }

            if (i == layout.Nonce) { nonceField = field; }
            else if (i == layout.To) { toField = field; }
            else if (i == layout.Value) { valueField = field; }
            else if (i == layout.Data) { dataField = field; }
            else if (i == chainIndex) { chainField = field; }
            else if (i == layout.AuthorizationList) { authorizations = field; }
            else if (i == layout.BlobHashes) { blobHashes = field; }
            else if (signatureStart >= 0 && i == signatureStart) { signature0 = field; }
            else if (signatureStart >= 0 && i == signatureStart + 1) { signature1 = field; }
            else if (signatureStart >= 0 && i == signatureStart + 2) { signature2 = field; }
        }

        if (dataField.Kind != RLPKind.ByteString)
        {
            return EvmTransactionStatus.MalformedRlp; // only reachable if the bytes changed during the call
        }

        EvmTransactionStatus fieldStatus = ReadChainIdAndSignature(type, signed, count, in chainField, in signature0, in signature1, in signature2, out ulong? chainId);
        if (fieldStatus != EvmTransactionStatus.Success)
        {
            return fieldStatus;
        }

        // EIP-2681: a nonce of 2^64-1 or more makes the transaction invalid; below it, nonce + 1 cannot overflow (EIP-7702 processing relies on that).
        if (nonceField.TryGetUInt64(out ulong nonce) != RLPStatus.Success || nonce == ulong.MaxValue)
        {
            return EvmTransactionStatus.InvalidNonce;
        }

        if (toField.TryGetOptionalAddress(out Address to, out bool isCreation) != RLPStatus.Success || (isCreation && !layout.AllowsCreation))
        {
            return EvmTransactionStatus.InvalidTo;
        }

        if (valueField.TryGetUInt256(out uint256 value) != RLPStatus.Success)
        {
            return EvmTransactionStatus.InvalidValue;
        }

        if (layout.BlobHashes >= 0 && !BlobHashesAreValid(in blobHashes))
        {
            return EvmTransactionStatus.InvalidBlobHashes;
        }

        int authorizationCount = 0;
        if (layout.AuthorizationList >= 0)
        {
            if (authorizations.TryGetChildren(out RLPChildEnumerator entries) != RLPStatus.Success)
            {
                return EvmTransactionStatus.InvalidAuthorizationList;
            }

            while (entries.MoveNext())
            {
                RLPData entry = entries.Current;
                if (!EvmAuthorizationTuple.TryRead(in entry, out _, out _))
                {
                    return EvmTransactionStatus.InvalidAuthorizationList;
                }

                authorizationCount++;
            }

            if (authorizationCount == 0)
            {
                return EvmTransactionStatus.InvalidAuthorizationList; // EIP-7702: an empty list makes the transaction invalid
            }
        }

        ReadOnlySpan<byte> data = dataField.Payload;
        EvmTransactionKind kind = isCreation ? EvmTransactionKind.ContractCreation
            : data.Length == 0 ? EvmTransactionKind.ValueTransfer
            : data.Length < 4 ? EvmTransactionKind.Fallback
            : EvmTransactionKind.FunctionCall;

        // The data item's payload offset is relative to the RLP body, which starts after the type byte.
        transaction = new EvmTransactionView(
            type, form, kind, chainId, nonce, isCreation ? default : to, value, data, bodyStart + dataField.PayloadRange.Offset, authorizations, authorizationCount);
        return EvmTransactionStatus.Success;
    }

    private static EvmTransactionStatus ReadChainIdAndSignature(
        EvmTransactionType type, bool signed, int count, scoped in RLPData chainField, scoped in RLPData signature0, scoped in RLPData signature1, scoped in RLPData signature2,
        out ulong? chainId)
    {
        chainId = null;
        if (type != EvmTransactionType.Legacy)
        {
            if (chainField.TryGetUInt64(out ulong typedChainId) != RLPStatus.Success)
            {
                return EvmTransactionStatus.InvalidChainId;
            }

            if (signed && (RLPTypedExtensions.TryGetEvmYParitySignature(in signature0, in signature1, in signature2, out Signature typed) != RLPStatus.Success
                || !Secp256k1Bounds.AreValid(typed.R, typed.S)))
            {
                return EvmTransactionStatus.InvalidSignature;
            }

            chainId = typedChainId;
            return EvmTransactionStatus.Success;
        }

        if (signed)
        {
            // r = 0 or s = 0 is never a valid secp256k1 signature, and it is exactly what an unsigned EIP-155 payload ([..., chainId, 0, 0]) has in those
            // positions: rejecting it keeps a signing payload from being read as a signed transaction whose "v" is its chain id.
            if (RLPTypedExtensions.TryGetEvmLegacySignature(in signature0, in signature1, in signature2, out Signature legacy, out ulong? legacyChainId) != RLPStatus.Success
                || !Secp256k1Bounds.AreValid(legacy.R, legacy.S))
            {
                return EvmTransactionStatus.InvalidSignature;
            }

            chainId = legacyChainId;
            return EvmTransactionStatus.Success;
        }

        if (count == 6)
        {
            return EvmTransactionStatus.Success; // unsigned pre-EIP-155 payload: no chain id
        }

        // Unsigned EIP-155 payload: [..., chainId, 0, 0]. Field 6 was captured as the chain id (signature0 stays unused); the placeholders are fields 7 and 8.
        if (chainField.TryGetUInt64(out ulong eip155ChainId) != RLPStatus.Success)
        {
            return EvmTransactionStatus.InvalidChainId;
        }

        if (signature1.TryGetUInt64(out ulong zeroR) != RLPStatus.Success || zeroR != 0
            || signature2.TryGetUInt64(out ulong zeroS) != RLPStatus.Success || zeroS != 0)
        {
            return EvmTransactionStatus.InvalidSignature;
        }

        chainId = eip155ChainId;
        return EvmTransactionStatus.Success;
    }

    /// <summary>EIP-4844: at least one versioned hash, each exactly 32 bytes with the KZG version byte first.</summary>
    private static bool BlobHashesAreValid(scoped in RLPData list)
    {
        if (list.TryGetChildren(out RLPChildEnumerator hashes) != RLPStatus.Success)
        {
            return false;
        }

        int count = 0;
        while (hashes.MoveNext())
        {
            ReadOnlySpan<byte> hash = hashes.Current.Payload;
            if (hashes.Current.Kind != RLPKind.ByteString || hash.Length != 32 || hash[0] != BlobHashVersion)
            {
                return false;
            }

            count++;
        }

        return count > 0;
    }

    /// <summary>The status for a field of the wrong RLP kind, named after the field when the adapter interprets it.</summary>
    private static EvmTransactionStatus FieldStatus(EvmTransactionType type, bool signed, int count, Layout layout, int index)
    {
        if (index == layout.Nonce)
        {
            return EvmTransactionStatus.InvalidNonce;
        }

        if (index == layout.AuthorizationList)
        {
            return EvmTransactionStatus.InvalidAuthorizationList;
        }

        if (index == layout.To)
        {
            return EvmTransactionStatus.InvalidTo;
        }

        if (index == layout.Value)
        {
            return EvmTransactionStatus.InvalidValue;
        }

        if (index == layout.Data)
        {
            return EvmTransactionStatus.InvalidData;
        }

        bool legacy = type == EvmTransactionType.Legacy;
        if ((!legacy && index == 0) || (legacy && !signed && count == 9 && index == 6))
        {
            return EvmTransactionStatus.InvalidChainId;
        }

        bool signatureField = legacy ? count == 9 && index >= 6 : signed && index >= count - 3;
        return signatureField ? EvmTransactionStatus.InvalidSignature : EvmTransactionStatus.InvalidField;
    }
}
