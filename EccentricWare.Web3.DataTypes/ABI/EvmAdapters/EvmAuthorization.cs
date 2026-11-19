using EccentricWare.Web3.DataTypes.Rlp;
using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.ABI.EvmAdapters;

/// <summary>
/// What became of one EIP-7702 authorization tuple, decided by <see cref="EvmAuthorizationProcessor"/> in the EIP's processing order. Every outcome other
/// than <see cref="Applied"/> means the tuple leaves the authority's code unchanged (for <see cref="Superseded"/>, a later tuple of the same authority
/// replaced it). The first four failures are fixed by the transaction itself; <see cref="AuthorityHasCode"/> and <see cref="NonceMismatch"/> depend on
/// account state and can change before the transaction is included.
/// </summary>
public enum EvmAuthorizationOutcome : byte
{
    /// <summary>The <c>default</c> value: not processed.</summary>
    None,

    /// <summary>The last valid tuple of its authority: when the transaction's call runs, the authority's code is the delegation to <see cref="EvmAuthorization.DelegateAddress"/> (or no code, when that is zero).</summary>
    Applied,

    /// <summary>Valid (the authority's nonce advanced), but a later tuple of the same authority set the code that remains.</summary>
    Superseded,

    /// <summary>The tuple's chain id is neither 0 nor the transaction's chain id.</summary>
    WrongChain,

    /// <summary>The tuple's nonce is 2^64-1.</summary>
    NonceOutOfRange,

    /// <summary><c>y_parity</c> above 1, <c>r</c> or <c>s</c> zero or not below the secp256k1 order, or <c>s</c> above half the order (EIP-2).</summary>
    InvalidSignature,

    /// <summary>The host reported that no public key recovers from the signature.</summary>
    NotRecoverable,

    /// <summary>The authority has code that is not a delegation indicator, so it cannot be delegated.</summary>
    AuthorityHasCode,

    /// <summary>The authority's nonce (after the sender's increment and earlier tuples of this list) is not the tuple's nonce.</summary>
    NonceMismatch,
}

/// <summary>Result of <see cref="EvmAuthorizationProcessor.TryProcess"/>.</summary>
public enum EvmAuthorizationStatus : byte
{
    /// <summary>Every tuple was processed and has an outcome.</summary>
    Success,

    /// <summary>An invalid transaction view (<c>default</c>), a sender that is not an EVM address, or envelope bytes that changed after they were read.</summary>
    InvalidArgument,

    /// <summary>The destination holds fewer entries than <see cref="EvmTransactionView.AuthorizationCount"/>.</summary>
    DestinationTooSmall,

    /// <summary>The host could not perform a recovery (<see cref="EvmAuthorityRecovery.Unavailable"/>) or returned a non-EVM authority. Fails closed.</summary>
    RecoveryUnavailable,

    /// <summary>The host could not report an authority's code or nonce. Fails closed.</summary>
    AccountStateUnavailable,
}

/// <summary>The outcome of a host's signature recovery. The <c>default</c> value means the host could not decide, which fails closed.</summary>
public enum EvmAuthorityRecovery : byte
{
    /// <summary>The host could not perform the recovery (no implementation, an internal error). The processing stops.</summary>
    Unavailable,

    /// <summary>A public key recovers; the authority is its address.</summary>
    Recovered,

    /// <summary>The signature recovers no public key (for example <c>r</c> is not the x coordinate of a curve point). The tuple is skipped, as the EIP requires.</summary>
    NotRecoverable,
}

/// <summary>
/// The caller's side of EIP-7702 processing: signature recovery and account state. Implement it as a struct (or a <c>ref struct</c>) for an
/// allocation-free run; it can be the same type as the <see cref="INestedCallHost"/>, whose <see cref="INestedCallHost.TryGetCode"/> has the same contract.
/// Exceptions thrown here propagate.
/// </summary>
public interface IEvmAuthorizationHost
{
    /// <summary>
    /// secp256k1 public-key recovery: the address (the last 20 bytes of the Keccak-256 of the uncompressed public key) whose key signed
    /// <paramref name="signingHash"/> (<c>keccak256(0x05 || rlp([chain_id, address, nonce]))</c>, computed by the processor). The processor has already
    /// checked <paramref name="yParity"/> (0 or 1), <paramref name="r"/> (1 to n-1) and <paramref name="s"/> (1 to n/2).
    /// </summary>
    EvmAuthorityRecovery RecoverAuthority(in Hash32 signingHash, byte yParity, in uint256 r, in uint256 s, out Address authority);

    /// <summary>The account's nonce in the pinned state the transaction will execute against. Return false when it cannot be established.</summary>
    bool TryGetNonce(ulong chainId, in Address account, out ulong nonce);

    /// <summary>The account's code in the pinned state the transaction will execute against (see <see cref="EvmAccountCode"/>). Return false when it cannot be established.</summary>
    bool TryGetCode(ulong chainId, in Address account, out EvmAccountCode code);
}

/// <summary>
/// One EIP-7702 authorization tuple <c>[chain_id, address, nonce, y_parity, r, s]</c> exactly as the envelope states it, with the authority recovered from
/// it and its <see cref="Outcome"/>. Only <see cref="EvmAuthorizationProcessor"/> creates processed entries.
/// </summary>
public readonly struct EvmAuthorization
{
    private readonly Address _processedSender;
    private readonly ulong _processedTransactionNonce;
    private readonly ulong _processedChainId;

    internal EvmAuthorization(int index, uint256 chainId, Address delegateAddress, ulong nonce, byte yParity, uint256 r, uint256 s, Address authority, EvmAuthorizationOutcome outcome)
        : this(index, chainId, delegateAddress, nonce, yParity, r, s, authority, outcome, 0, default, 0, 0)
    {
    }

    private EvmAuthorization(
        int index, uint256 chainId, Address delegateAddress, ulong nonce, byte yParity, uint256 r, uint256 s, Address authority, EvmAuthorizationOutcome outcome,
        ulong authorityNonce, Address processedSender, ulong processedTransactionNonce, ulong processedChainId)
    {
        AuthorityNonce = authorityNonce;
        _processedSender = processedSender;
        _processedTransactionNonce = processedTransactionNonce;
        _processedChainId = processedChainId;
        Index = index;
        ChainId = chainId;
        DelegateAddress = delegateAddress;
        Nonce = nonce;
        YParity = yParity;
        R = r;
        S = s;
        Authority = authority;
        Outcome = outcome;
    }

    /// <summary>Position in the authorization list.</summary>
    public int Index { get; }

    /// <summary>The tuple's chain id: 0 (any chain) or one chain.</summary>
    public uint256 ChainId { get; }

    /// <summary>The tuple's <c>address</c>: the code the authority delegates to. The zero address clears the authority's delegation instead.</summary>
    public Address DelegateAddress { get; }

    /// <summary>The authority nonce the tuple was signed for.</summary>
    public ulong Nonce { get; }

    /// <summary>The signature's y parity (any value below 256 is a well-formed tuple; only 0 and 1 can be valid).</summary>
    public byte YParity { get; }

    /// <summary>The signature's <c>r</c>.</summary>
    public uint256 R { get; }

    /// <summary>The signature's <c>s</c>.</summary>
    public uint256 S { get; }

    /// <summary>The recovered signer whose account is delegated; <c>default</c> unless <see cref="IsRecovered"/>.</summary>
    public Address Authority { get; }

    /// <summary>What became of the tuple.</summary>
    public EvmAuthorizationOutcome Outcome { get; }

    /// <summary>
    /// The authority's nonce the tuple was checked against: the pinned nonce, the transaction's nonce plus one for the sender, or one more than an earlier
    /// applied tuple of the same authority. Meaningful for <see cref="EvmAuthorizationOutcome.Applied"/>, <see cref="EvmAuthorizationOutcome.Superseded"/>
    /// and <see cref="EvmAuthorizationOutcome.NonceMismatch"/>; 0 otherwise. A <see cref="EvmAuthorizationOutcome.NonceMismatch"/> tuple whose
    /// <see cref="Nonce"/> is above it is PENDING: it applies if the authority's nonce reaches it before inclusion (see <see cref="IsPending"/>).
    /// </summary>
    public ulong AuthorityNonce { get; }

    /// <summary>
    /// True for a <see cref="EvmAuthorizationOutcome.NonceMismatch"/> tuple of an authority other than the sender whose <see cref="Nonce"/> is above
    /// <see cref="AuthorityNonce"/>: skipped under the pinned state, but it takes effect if the authority's nonce advances to it before the transaction is
    /// included (the sender's nonce is fixed by the transaction, so a sender's tuple is never pending). The walker refuses to resolve such an authority (<see cref="NestedCallStatus.UnsettledDelegation"/>).
    /// </summary>
    public bool IsPending => Outcome == EvmAuthorizationOutcome.NonceMismatch && Authority != _processedSender && Nonce > AuthorityNonce;

    /// <summary>
    /// True when the tuple names an authority (<see cref="EvmAuthorizationOutcome.Applied"/>, <see cref="EvmAuthorizationOutcome.Superseded"/>,
    /// <see cref="EvmAuthorizationOutcome.AuthorityHasCode"/> or <see cref="EvmAuthorizationOutcome.NonceMismatch"/>). Such a tuple can take effect if the
    /// authority's state changes before inclusion, so a policy should judge every one of them, not only the applied ones.
    /// </summary>
    public bool IsRecovered => Outcome is EvmAuthorizationOutcome.Applied or EvmAuthorizationOutcome.Superseded
        or EvmAuthorizationOutcome.AuthorityHasCode or EvmAuthorizationOutcome.NonceMismatch;

    /// <summary>True when the tuple clears the authority's delegation (<see cref="DelegateAddress"/> is zero) instead of installing one.</summary>
    public bool ClearsDelegation => DelegateAddress.IsZero;

    internal EvmAuthorization WithOutcome(Address authority, EvmAuthorizationOutcome outcome, ulong authorityNonce = 0) =>
        new(Index, ChainId, DelegateAddress, Nonce, YParity, R, S, authority, outcome, authorityNonce, default, 0, 0);

    /// <summary>True when the authority is the sender the entry was processed for (its nonce is fixed by the transaction).</summary>
    internal bool IsSenderAuthority => Authority == _processedSender;

    /// <summary>Stamps the context the outcome was computed for: the sender, the transaction's nonce and its chain.</summary>
    internal EvmAuthorization ProcessedFor(Address sender, ulong transactionNonce, ulong chainId) =>
        new(Index, ChainId, DelegateAddress, Nonce, YParity, R, S, Authority, Outcome, AuthorityNonce, sender, transactionNonce, chainId);

    /// <summary>True when the entry was processed for this sender, transaction nonce and chain (the inputs of every outcome besides the tuples and the state).</summary>
    internal bool WasProcessedFor(in Address sender, ulong transactionNonce, ulong chainId) =>
        _processedSender == sender && _processedTransactionNonce == transactionNonce && _processedChainId == chainId;

    /// <summary>True when both entries describe the same tuple at the same position (the processed result belongs to the envelope).</summary>
    internal bool HasSameTuple(in EvmAuthorization other) =>
        Index == other.Index && ChainId == other.ChainId && DelegateAddress == other.DelegateAddress && Nonce == other.Nonce
        && YParity == other.YParity && R == other.R && S == other.S;
}

/// <summary>Reads and hashes one authorization tuple. Shared by the adapter (validation), the processor and the walker (binding check).</summary>
internal static class EvmAuthorizationTuple
{
    private const byte Magic = 0x05;

    /// <summary>The largest <c>rlp([chain_id, address, nonce])</c> payload: 33 + 21 + 9 bytes.</summary>
    private const int MaxSignedPayload = 63;

    /// <summary>
    /// Reads <c>[chain_id, address, nonce, y_parity, r, s]</c> within the EIP-7702 bounds. <paramref name="signedLength"/> is the length of the first three
    /// items' encodings at the start of the tuple's payload: canonical (the RLP layer and the integer readers prove it), so they are byte for byte the
    /// <c>rlp([chain_id, address, nonce])</c> payload a client re-encodes before hashing.
    /// </summary>
    internal static bool TryRead(scoped in RLPData tuple, out EvmAuthorization authorization, out int signedLength, int index = 0)
    {
        authorization = default;
        signedLength = 0;
        if (tuple.Kind != RLPKind.List || tuple.TryGetChildren(out RLPChildEnumerator fields) != RLPStatus.Success)
        {
            return false;
        }

        uint256 chainId = default, r = default, s = default;
        Address delegateAddress = default;
        ulong nonce = 0, yParity = 0;
        int count = 0;
        while (fields.MoveNext())
        {
            RLPData field = fields.Current;
            bool ok = count switch
            {
                0 => field.TryGetUInt256(out chainId) == RLPStatus.Success,
                1 => field.TryGetAddress(out delegateAddress) == RLPStatus.Success,
                2 => field.TryGetUInt64(out nonce) == RLPStatus.Success,
                3 => field.TryGetUInt64(out yParity) == RLPStatus.Success && yParity <= byte.MaxValue,
                4 => field.TryGetUInt256(out r) == RLPStatus.Success,
                5 => field.TryGetUInt256(out s) == RLPStatus.Success,
                _ => false, // a seventh field
            };
            if (!ok)
            {
                return false;
            }

            if (count == 2)
            {
                signedLength = field.EncodedRange.Offset + field.EncodedRange.Length - tuple.PayloadRange.Offset;
            }

            count++;
        }

        if (count != 6)
        {
            return false;
        }

        authorization = new EvmAuthorization(index, chainId, delegateAddress, nonce, (byte)yParity, r, s, default, EvmAuthorizationOutcome.None);
        return true;
    }

    /// <summary><c>keccak256(0x05 || rlp([chain_id, address, nonce]))</c> (EIP-7702), from the tuple's own canonical bytes.</summary>
    internal static Hash32 SigningHash(scoped in RLPData tuple, int signedLength)
    {
        Span<byte> message = stackalloc byte[3 + MaxSignedPayload];
        message[0] = Magic;
        int header;
        if (signedLength <= 55)
        {
            message[1] = (byte)(0xc0 + signedLength);
            header = 2;
        }
        else
        {
            message[1] = 0xf8;
            message[2] = (byte)signedLength;
            header = 3;
        }

        tuple.Payload[..signedLength].CopyTo(message[header..]);
        Span<byte> hash = stackalloc byte[32];
        Keccak256.ComputeHash(message[..(header + signedLength)], hash);
        return new Hash32(hash);
    }
}

/// <summary>
/// Applies an EIP-7702 authorization list the way the EIP does, against the pinned state a host reports, and records the outcome of every tuple. The result
/// serves two purposes: a policy judges the delegations the transaction asks for (they persist after the transaction), and
/// <see cref="NestedCallWalker"/> resolves calls with the code in effect after the list is applied.
/// </summary>
/// <remarks>
/// <para>
/// Per tuple, in list order (EIP-7702, "behavior"): the chain id must be 0 or the transaction's; the nonce below 2^64-1; <c>y_parity</c>, <c>r</c> and
/// <c>s</c> within the secp256k1 bounds with <c>s</c> at most half the order (EIP-2); the authority is recovered by the host; its code must be empty or a
/// delegation; its nonce must equal the tuple's. A tuple that fails a step is skipped with that outcome. A valid tuple delegates the authority (or clears
/// it, for the zero address) and increments its nonce, so a later tuple of the same authority sees the new code and nonce. The sender's nonce is
/// incremented before the list is processed, so a tuple whose authority is the sender must carry the transaction's nonce plus one; the processor uses that
/// value instead of asking the host.
/// </para>
/// <para>
/// State dependence: <see cref="EvmAuthorizationOutcome.AuthorityHasCode"/> and <see cref="EvmAuthorizationOutcome.NonceMismatch"/> depend on the
/// host's pinned state, which can change before the transaction is included: an authority other than the sender can spend the nonce a tuple needed (an
/// applied tuple then fails and the pinned code stays) or reach it (a pending tuple, <see cref="EvmAuthorization.IsPending"/>, then applies). The result
/// is exact for the pinned state only. The walker refuses both directions where they change the code that runs (<see cref="NestedCallStatus.UnsettledDelegation"/>):
/// an account with a pending tuple, and an authority other than the sender whose applied tuple would, on failure, leave pinned code that runs something.
/// Beyond the list, a delegated account's code, like a proxy's implementation, is in the hands of whoever controls it until inclusion.
/// </para>
/// <para>
/// Cost: one Keccak-256 and one host recovery per well-formed tuple, plus a scan of the earlier tuples per recovered authority (quadratic in the list
/// length, which the RLP limits on the envelope bound). Allocation-free with a struct host and a caller-owned destination.
/// </para>
/// </remarks>
public static class EvmAuthorizationProcessor
{
    /// <summary>
    /// Processes the authorization list of <paramref name="transaction"/> into <paramref name="destination"/>, one entry per tuple in list order. A transaction
    /// of any other type succeeds with nothing written. On failure nothing is written (the destination prefix is cleared) and <paramref name="written"/> is 0.
    /// </summary>
    /// <param name="transaction">A valid view from <see cref="RLPCalldataAdapter"/>.</param>
    /// <param name="sender">The transaction's sender (the signer knows it; the envelope does not state it).</param>
    /// <param name="host">Signature recovery and pinned account state.</param>
    /// <param name="destination">At least <see cref="EvmTransactionView.AuthorizationCount"/> entries.</param>
    /// <param name="written">The number of entries written: <see cref="EvmTransactionView.AuthorizationCount"/> on success.</param>
    /// <param name="status">The outcome.</param>
    public static bool TryProcess<THost>(
        scoped in EvmTransactionView transaction, Address sender, ref THost host, Span<EvmAuthorization> destination, out int written, out EvmAuthorizationStatus status)
        where THost : IEvmAuthorizationHost, allows ref struct
    {
        written = 0;
        if (!transaction.IsValid || sender.Type != AddressType.Evm)
        {
            status = EvmAuthorizationStatus.InvalidArgument;
            return false;
        }

        int count = transaction.AuthorizationCount;
        if (destination.Length < count)
        {
            status = EvmAuthorizationStatus.DestinationTooSmall;
            return false;
        }

        status = count == 0 ? EvmAuthorizationStatus.Success : Process(in transaction, sender, ref host, destination[..count]);
        if (status != EvmAuthorizationStatus.Success)
        {
            destination[..count].Clear();
            return false;
        }

        written = count;
        return true;
    }

    private static EvmAuthorizationStatus Process<THost>(scoped in EvmTransactionView transaction, Address sender, ref THost host, Span<EvmAuthorization> entries)
        where THost : IEvmAuthorizationHost, allows ref struct
    {
        // A typed transaction always has a chain id; the adapter guarantees count > 0 only for EIP-7702.
        ulong chainId = transaction.ChainId!.Value;
        var transactionChain = new uint256(chainId);
        RLPData list = transaction.AuthorizationList;
        if (list.TryGetChildren(out RLPChildEnumerator tuples) != RLPStatus.Success)
        {
            return EvmAuthorizationStatus.InvalidArgument;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            if (!tuples.MoveNext())
            {
                return EvmAuthorizationStatus.InvalidArgument;
            }

            RLPData tuple = tuples.Current;
            if (!EvmAuthorizationTuple.TryRead(in tuple, out EvmAuthorization entry, out int signedLength, i))
            {
                return EvmAuthorizationStatus.InvalidArgument; // only if the envelope changed after TryRead
            }

            // Steps 1 to 3 of the EIP: fixed by the transaction.
            if (!entry.ChainId.IsZero && entry.ChainId != transactionChain)
            {
                entries[i] = entry.WithOutcome(default, EvmAuthorizationOutcome.WrongChain);
                continue;
            }

            if (entry.Nonce == ulong.MaxValue)
            {
                entries[i] = entry.WithOutcome(default, EvmAuthorizationOutcome.NonceOutOfRange);
                continue;
            }

            if (entry.YParity > 1 || !Secp256k1Bounds.AreValid(entry.R, entry.S))
            {
                entries[i] = entry.WithOutcome(default, EvmAuthorizationOutcome.InvalidSignature);
                continue;
            }

            Hash32 signingHash = EvmAuthorizationTuple.SigningHash(in tuple, signedLength);
            uint256 r = entry.R, s = entry.S;
            switch (host.RecoverAuthority(in signingHash, entry.YParity, in r, in s, out Address authority))
            {
                case EvmAuthorityRecovery.Recovered when authority.Type == AddressType.Evm:
                    break;
                case EvmAuthorityRecovery.NotRecoverable:
                    entries[i] = entry.WithOutcome(default, EvmAuthorizationOutcome.NotRecoverable);
                    continue;
                default:
                    return EvmAuthorizationStatus.RecoveryUnavailable;
            }

            // Steps 5 and 6: state. An earlier valid tuple of this authority left its code delegated (or cleared) and its nonce one higher.
            int previous = LastApplied(entries[..i], in authority);
            ulong expectedNonce;
            if (previous >= 0)
            {
                expectedNonce = entries[previous].Nonce + 1; // below 2^64-1, so no overflow
            }
            else
            {
                if (!host.TryGetCode(chainId, in authority, out EvmAccountCode code) || code.Kind == EvmCodeKind.Unknown)
                {
                    return EvmAuthorizationStatus.AccountStateUnavailable;
                }

                if (code.Kind == EvmCodeKind.Contract)
                {
                    entries[i] = entry.WithOutcome(authority, EvmAuthorizationOutcome.AuthorityHasCode);
                    continue;
                }

                if (authority == sender)
                {
                    expectedNonce = transaction.Nonce + 1; // the sender's nonce is incremented first; the adapter proved Nonce < 2^64-1
                }
                else if (!host.TryGetNonce(chainId, in authority, out expectedNonce))
                {
                    return EvmAuthorizationStatus.AccountStateUnavailable;
                }
            }

            if (entry.Nonce != expectedNonce)
            {
                entries[i] = entry.WithOutcome(authority, EvmAuthorizationOutcome.NonceMismatch, expectedNonce);
                continue;
            }

            if (previous >= 0)
            {
                entries[previous] = entries[previous].WithOutcome(authority, EvmAuthorizationOutcome.Superseded, entries[previous].AuthorityNonce);
            }

            entries[i] = entry.WithOutcome(authority, EvmAuthorizationOutcome.Applied, expectedNonce);
        }

        if (tuples.MoveNext())
        {
            return EvmAuthorizationStatus.InvalidArgument;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            entries[i] = entries[i].ProcessedFor(sender, transaction.Nonce, chainId);
        }

        return EvmAuthorizationStatus.Success;
    }

    /// <summary>The index of the applied entry of <paramref name="authority"/>, or -1. At most one entry per authority is applied at any time.</summary>
    internal static int LastApplied(ReadOnlySpan<EvmAuthorization> entries, in Address authority)
    {
        for (int i = entries.Length - 1; i >= 0; i--)
        {
            if (entries[i].Outcome == EvmAuthorizationOutcome.Applied && entries[i].Authority == authority)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// The secp256k1 bounds every Ethereum transaction and EIP-7702 authorization signature must meet: <c>r</c> in 1..n-1 and <c>s</c> in 1..n/2 (the
/// Yellow Paper's signature validity, and EIP-2's low-s rule, which made every transaction signature with <c>s</c> above n/2 invalid).
/// </summary>
internal static class Secp256k1Bounds
{
    // The group order n and n / 2, as uint256 limbs (least significant first).
    internal static readonly uint256 Order = new(0xBFD25E8CD0364141, 0xBAAEDCE6AF48A03B, 0xFFFFFFFFFFFFFFFE, 0xFFFFFFFFFFFFFFFF);
    internal static readonly uint256 HalfOrder = new(0xDFE92F46681B20A0, 0x5D576E7357A4501D, 0xFFFFFFFFFFFFFFFF, 0x7FFFFFFFFFFFFFFF);

    internal static bool AreValid(in uint256 r, in uint256 s) => !r.IsZero && r < Order && !s.IsZero && s <= HalfOrder;

    // The same bounds for a Signature, whose r and s are Hash32 (big-endian; Hash32 compares numerically, most significant limb first).
    private static readonly Hash32 OrderHash = new(0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFE, 0xBAAEDCE6AF48A03B, 0xBFD25E8CD0364141);
    private static readonly Hash32 HalfOrderHash = new(0x7FFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF, 0x5D576E7357A4501D, 0xDFE92F46681B20A0);

    internal static bool AreValid(in Hash32 r, in Hash32 s) => !r.IsZero && r < OrderHash && !s.IsZero && s <= HalfOrderHash;
}
