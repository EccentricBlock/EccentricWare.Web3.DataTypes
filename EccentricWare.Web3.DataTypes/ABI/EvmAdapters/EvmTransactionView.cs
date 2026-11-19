using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.ABI.EvmAdapters;

/// <summary>The EIP-2718 transaction type. The numeric value is the type byte; <see cref="Legacy"/> has no type byte on the wire.</summary>
public enum EvmTransactionType : byte
{
    /// <summary>A legacy transaction: a bare RLP list (first byte <c>0xc0</c> or more), with or without EIP-155 replay protection.</summary>
    Legacy = 0,

    /// <summary>EIP-2930 access-list transaction, type <c>0x01</c>.</summary>
    Eip2930 = 1,

    /// <summary>EIP-1559 fee-market transaction, type <c>0x02</c>.</summary>
    Eip1559 = 2,

    /// <summary>EIP-4844 blob transaction, type <c>0x03</c> (the signed body only, never the network wrapper with blobs).</summary>
    Eip4844 = 3,

    /// <summary>EIP-7702 set-code transaction, type <c>0x04</c>.</summary>
    Eip7702 = 4,
}

/// <summary>Which encoding of a transaction the caller holds. The caller states it; the adapter never guesses.</summary>
public enum EvmTransactionForm : byte
{
    /// <summary>The signed transaction, including its signature fields (what a node broadcasts).</summary>
    SignedTransaction,

    /// <summary>
    /// The signing payload, without signature fields (what a signer hashes): a legacy transaction as 6 fields (no replay protection) or as the 9 EIP-155 fields
    /// <c>[..., chainId, 0, 0]</c>; a typed transaction as its type byte and its fields without <c>yParity, r, s</c>.
    /// </summary>
    SigningPayload,
}

/// <summary>
/// What the transaction does with its <c>data</c> field. Only <see cref="FunctionCall"/> may be handed to the ABI codec (spec 9.4): the others are different
/// contexts that need their own handling, and treating their bytes as a function call is a misclassification.
/// </summary>
public enum EvmTransactionKind : byte
{
    /// <summary>The <c>default</c> view: no transaction.</summary>
    None,

    /// <summary>A call to <see cref="EvmTransactionView.To"/> with at least four bytes of data (a selector and arguments).</summary>
    FunctionCall,

    /// <summary>An empty <c>to</c>: the data is contract initcode, never calldata.</summary>
    ContractCreation,

    /// <summary>A call with no data: a plain value transfer, <c>receive()</c> or a transfer to an account without code.</summary>
    ValueTransfer,

    /// <summary>One to three bytes of data: too short for a selector, so the contract's <c>fallback()</c> runs.</summary>
    Fallback,
}

/// <summary>Result of <see cref="RLPCalldataAdapter.TryRead"/>.</summary>
public enum EvmTransactionStatus : byte
{
    /// <summary>The envelope is a well-formed transaction of a supported type and form.</summary>
    Success,

    /// <summary>Invalid <see cref="Rlp.RLPDecodeLimits"/> or an undefined <see cref="EvmTransactionForm"/>.</summary>
    InvalidArgument,

    /// <summary>Zero bytes.</summary>
    EmptyEnvelope,

    /// <summary>A first byte of <c>0x00</c> or <c>0x05</c> to <c>0x7f</c>: an EIP-2718 type this adapter does not know. Refused, never guessed.</summary>
    UnsupportedType,

    /// <summary>The RLP layer rejected the body (its own failure contract), or the body is not a list.</summary>
    MalformedRlp,

    /// <summary>The field count does not match the type and form (for example the four-item EIP-4844 network wrapper, or a signed transaction declared unsigned).</summary>
    WrongFieldCount,

    /// <summary>The chain id is a list, not a canonical integer, or above <see cref="ulong.MaxValue"/>.</summary>
    InvalidChainId,

    /// <summary>The nonce is a list or not a canonical integer, or is 2^64-1 or more (EIP-2681: such a transaction is invalid).</summary>
    InvalidNonce,

    /// <summary><c>to</c> is a list or neither empty nor 20 bytes, or is empty in an EIP-4844 or EIP-7702 transaction (which cannot create contracts).</summary>
    InvalidTo,

    /// <summary><c>value</c> is a list or not a canonical integer of at most 32 bytes.</summary>
    InvalidValue,

    /// <summary><c>data</c> is a list.</summary>
    InvalidData,

    /// <summary>
    /// A signature field is malformed: signed typed transactions need <c>yParity</c> 0 or 1, signed legacy transactions a valid <c>v</c> (27, 28 or
    /// EIP-155), and both canonical <c>r</c> in 1..n-1 and <c>s</c> in 1..n/2 (secp256k1 order n; EIP-2 made a high <c>s</c> invalid); the 9-field
    /// unsigned legacy form needs its two placeholder fields to be zero. (Whether the signature recovers a sender is not checked.) The low-s rule is the
    /// rule since Homestead (block 1,150,000): about half of the transactions in earlier blocks have a high <c>s</c> and read as invalid here, which is
    /// right for signing or authorising today and wrong for indexing that history.
    /// </summary>
    InvalidSignature,

    /// <summary>Any other field has the wrong RLP kind (a list where a byte string belongs, or the reverse).</summary>
    InvalidField,

    /// <summary>
    /// The EIP-7702 authorization list is not a list, is empty, or holds an entry that is not <c>[chain_id, address, nonce, y_parity, r, s]</c> within
    /// the EIP's bounds: six byte strings, canonical integers, <c>chain_id</c>, <c>r</c> and <c>s</c> below 2^256, <c>nonce</c> below 2^64, a 20-byte
    /// <c>address</c> and <c>y_parity</c> below 2^8. Each makes the transaction invalid (EIP-7702); whether an entry takes effect is decided later, by
    /// <see cref="EvmAuthorizationProcessor"/>.
    /// </summary>
    InvalidAuthorizationList,

    /// <summary>The EIP-4844 <c>blob_versioned_hashes</c> list is empty, or holds an entry that is not 32 bytes starting with the version byte <c>0x01</c> (EIP-4844: each makes the transaction invalid).</summary>
    InvalidBlobHashes,
}

/// <summary>
/// A borrowed, read-only view of one validated EVM transaction envelope, produced only by <see cref="RLPCalldataAdapter.TryRead"/>. It holds no copy:
/// <see cref="Data"/> is a slice of the caller's envelope bytes, valid only while those bytes stay unchanged (spec 3.2).
/// </summary>
/// <remarks>
/// <para>
/// The view proves structure only: the envelope is complete canonical RLP, the field count matches the type and form, and the fields it reads are well
/// formed, including every EIP-7702 authorization tuple. It does not check the signature against a sender, gas or fee fields beyond their RLP kind,
/// other consensus rules, or whether <see cref="ChainId"/> is the chain the caller serves.
/// </para>
/// <para>
/// Coordinates: <see cref="DataRange"/> is in ENVELOPE coordinates (byte zero is the first byte of the envelope, the EIP-2718 type byte when there is
/// one). ABI ranges are in calldata or argument coordinates; <see cref="TryMapCalldataRange"/> is the one explicit translation between them.
/// </para>
/// </remarks>
public readonly ref struct EvmTransactionView
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly RLPData _authorizationList;

    internal EvmTransactionView(
        EvmTransactionType type,
        EvmTransactionForm form,
        EvmTransactionKind kind,
        ulong? chainId,
        ulong nonce,
        Address to,
        uint256 value,
        ReadOnlySpan<byte> data,
        int dataOffset,
        RLPData authorizationList,
        int authorizationCount)
    {
        Type = type;
        Form = form;
        Kind = kind;
        ChainId = chainId;
        Nonce = nonce;
        To = to;
        Value = value;
        _data = data;
        DataRange = new ABIRange(dataOffset, data.Length);
        _authorizationList = authorizationList;
        AuthorizationCount = authorizationCount;
    }

    /// <summary>False for <c>default</c>; true for every view <see cref="RLPCalldataAdapter.TryRead"/> returned with success.</summary>
    public bool IsValid => Kind != EvmTransactionKind.None;

    /// <summary>The transaction type.</summary>
    public EvmTransactionType Type { get; }

    /// <summary>The form the caller declared and the adapter verified.</summary>
    public EvmTransactionForm Form { get; }

    /// <summary>What the transaction does with <see cref="Data"/>. Only <see cref="EvmTransactionKind.FunctionCall"/> is a function call.</summary>
    public EvmTransactionKind Kind { get; }

    /// <summary>
    /// The chain id: field 0 of a typed transaction, the EIP-155 chain id of a legacy one (from <c>v</c> when signed, from field 6 when unsigned), or
    /// <c>null</c> for a legacy transaction without replay protection. It is the transaction's claim; comparing it with the chain served is the caller's check.
    /// </summary>
    public ulong? ChainId { get; }

    /// <summary>The sender's nonce the transaction claims (below 2^64-1, EIP-2681). EIP-7702 processing needs it: the sender's nonce is incremented before the authorization list is applied.</summary>
    public ulong Nonce { get; }

    /// <summary>The 20-byte destination. Meaningful only when <see cref="Kind"/> is not <see cref="EvmTransactionKind.ContractCreation"/> (then it is the zero address, which is also a valid destination).</summary>
    public Address To { get; }

    /// <summary>The native value in wei.</summary>
    public uint256 Value { get; }

    /// <summary>
    /// The raw <c>data</c> field: calldata for a function call, initcode for a contract creation. Use <see cref="TryGetFunctionCalldata"/> to obtain calldata,
    /// so initcode or a fallback payload can never reach the ABI codec by accident.
    /// </summary>
    public ReadOnlySpan<byte> Data => _data;

    /// <summary>Where <see cref="Data"/> sits in the envelope (envelope coordinates).</summary>
    public ABIRange DataRange { get; }

    /// <summary>
    /// The number of entries in the EIP-7702 authorization list: at least 1 for <see cref="EvmTransactionType.Eip7702"/>, 0 for every other type. Each entry
    /// may install or clear delegation code before the call runs, so code observed before the transaction may not be the code that runs (spec 9.2, "proxy or
    /// delegated-account changes"): <see cref="EvmAuthorizationProcessor.TryProcess"/> decides which entries take effect, and
    /// <see cref="NestedCallWalker"/> walks such a transaction only with that result.
    /// </summary>
    public int AuthorizationCount { get; }

    /// <summary>The validated authorization list item (EIP-7702 only; <c>default</c> otherwise).</summary>
    internal RLPData AuthorizationList => _authorizationList;

    /// <summary>
    /// The calldata to hand to <see cref="ABICallData.TryDecode(ReadOnlySpan{byte}, ABIFunctionSchema, in ABIDecodeLimits, out ABICallData, out ABIStatus)"/>:
    /// the unchanged <c>data</c> span, only when <see cref="Kind"/> is <see cref="EvmTransactionKind.FunctionCall"/>. Empty and <c>false</c> otherwise.
    /// </summary>
    public bool TryGetFunctionCalldata(out ReadOnlySpan<byte> calldata)
    {
        if (Kind != EvmTransactionKind.FunctionCall)
        {
            calldata = default;
            return false;
        }

        calldata = _data;
        return true;
    }

    /// <summary>
    /// Translates a range in CALLDATA coordinates (byte zero is the selector; <see cref="ABICallData.TryTranslateArgumentRange"/> produces these from
    /// argument ranges, and <see cref="NestedCall.CalldataRange"/> is already in them) to envelope coordinates. Fails, with a default range, when the view is
    /// invalid or the range is negative or does not lie inside <see cref="Data"/>. This translates; it certifies nothing about the range.
    /// </summary>
    public bool TryMapCalldataRange(ABIRange calldataRange, out ABIRange envelopeRange)
    {
        envelopeRange = default;
        if (!IsValid || calldataRange.Offset < 0 || calldataRange.Length < 0 || calldataRange.Offset > _data.Length - calldataRange.Length)
        {
            return false;
        }

        // DataRange.Offset + Data.Length is inside the envelope, which is at most int.MaxValue bytes long, so the sum cannot overflow.
        envelopeRange = new ABIRange(DataRange.Offset + calldataRange.Offset, calldataRange.Length);
        return true;
    }
}
