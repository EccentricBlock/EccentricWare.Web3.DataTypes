namespace EccentricWare.Web3.DataTypes.ABI.EvmAdapters;

/// <summary>
/// One call the <see cref="NestedCallWalker"/> resolved and validated: the transaction's own call (index 0) or a nested call a trusted
/// <see cref="ExecutionDescriptor"/> declared. It records who runs what, as whom, in whose context, with what value, and where the calldata lies.
/// </summary>
/// <remarks>
/// For <see cref="NestedCallMode.Call"/>: the context is the target, the sender is the calling contract's context address, and <see cref="Value"/> moves
/// to the target. For <see cref="NestedCallMode.DelegateCall"/>: the context, sender and <see cref="Value"/> are inherited from the caller, and nothing
/// moves (<see cref="TransfersValue"/> is false), so summing <see cref="Value"/> over every call would double count. In both, the code that runs is the
/// target's, except when the target is an EIP-7702 delegated account: then <see cref="CodeAddress"/> is the delegate, <see cref="TargetAddress"/> stays
/// the account, and (for a call) the account is still the context.
/// </remarks>
public readonly struct NestedCall
{
    internal NestedCall(
        int index,
        int parentIndex,
        int depth,
        ulong chainId,
        NestedCallMode mode,
        Address targetAddress,
        Address codeAddress,
        Address contextAddress,
        Address sender,
        uint256 value,
        Hash32 codeIdentity,
        ExecutionDescriptor descriptor,
        ABIRange calldataRange)
    {
        Index = index;
        ParentIndex = parentIndex;
        Depth = depth;
        ChainId = chainId;
        Mode = mode;
        TargetAddress = targetAddress;
        CodeAddress = codeAddress;
        ContextAddress = contextAddress;
        Sender = sender;
        Value = value;
        CodeIdentity = codeIdentity;
        Descriptor = descriptor;
        CalldataRange = calldataRange;
    }

    /// <summary>Position in the walk (pre-order: a call before its children); the transaction's call is 0.</summary>
    public int Index { get; }

    /// <summary>The <see cref="Index"/> of the call that made this one, or -1 for the transaction's call.</summary>
    public int ParentIndex { get; }

    /// <summary>Nesting depth: 0 for the transaction's call, 1 for its children, and so on.</summary>
    public int Depth { get; }

    /// <summary>The chain the walk resolved against.</summary>
    public ulong ChainId { get; }

    /// <summary>The call type (the transaction's own call is a <see cref="NestedCallMode.Call"/>).</summary>
    public NestedCallMode Mode { get; }

    /// <summary>The address the call names (the transaction's <c>to</c>, or the target a descriptor rule read).</summary>
    public Address TargetAddress { get; }

    /// <summary>
    /// The address whose code runs, resolved in the registry with <see cref="CodeIdentity"/>: <see cref="TargetAddress"/>, or its EIP-7702 delegate when
    /// the target's code (after the transaction's own authorization list) is a delegation indicator.
    /// </summary>
    public Address CodeAddress { get; }

    /// <summary>True when the target is an EIP-7702 delegated account, so the delegate's code runs (<see cref="CodeAddress"/> differs from <see cref="TargetAddress"/>).</summary>
    public bool IsDelegated => CodeAddress != TargetAddress;

    /// <summary><c>address(this)</c> for the running code: the account whose storage and balance it uses.</summary>
    public Address ContextAddress { get; }

    /// <summary><c>msg.sender</c> for the running code.</summary>
    public Address Sender { get; }

    /// <summary><c>msg.value</c> for the running code (inherited, not transferred, under <see cref="NestedCallMode.DelegateCall"/>).</summary>
    public uint256 Value { get; }

    /// <summary>True when <see cref="Value"/> moves with this call (every <see cref="NestedCallMode.Call"/>); false under <see cref="NestedCallMode.DelegateCall"/>.</summary>
    public bool TransfersValue => Mode == NestedCallMode.Call;

    /// <summary>The verified code identity the host reported for the code at <see cref="CodeAddress"/>, which the registry matched.</summary>
    public Hash32 CodeIdentity { get; }

    /// <summary>The trusted descriptor the call resolved to.</summary>
    public ExecutionDescriptor Descriptor { get; }

    /// <summary>The function the call resolved to.</summary>
    public ABIFunctionSchema Function => Descriptor.Function;

    /// <summary>
    /// Where this call's calldata lies in the TRANSACTION's calldata (byte zero is the transaction's selector). Map it to envelope coordinates with
    /// <see cref="EvmTransactionView.TryMapCalldataRange"/>.
    /// </summary>
    public ABIRange CalldataRange { get; }
}

/// <summary>Bounds on one walk. Every bound is enforced before the call that would exceed it is resolved.</summary>
/// <param name="MaxCalls">The most calls (the transaction's call included) the walk validates; spec 6.4 suggests 256.</param>
/// <param name="MaxDepth">The deepest nesting allowed (0: the transaction's call only), at most <see cref="HardMaxDepth"/>.</param>
public readonly record struct NestedCallLimits(int MaxCalls, int MaxDepth)
{
    /// <summary>The largest <see cref="MaxDepth"/>. The walker recurses once per level, so this also bounds its stack use.</summary>
    public const int HardMaxDepth = 16;

    /// <summary>256 calls (spec 6.4, <c>MaxNestedExecutableCalls</c>) and depth 8.</summary>
    public static NestedCallLimits Default => new(256, 8);

    /// <summary>True when <see cref="MaxCalls"/> is positive and <see cref="MaxDepth"/> is in 0..<see cref="HardMaxDepth"/>.</summary>
    public bool IsValid => MaxCalls > 0 && MaxDepth >= 0 && MaxDepth <= HardMaxDepth;
}

/// <summary>Result of a walk. Every status except <see cref="Success"/> is a denial: the walk stops at the first failure (fail closed).</summary>
public enum NestedCallStatus : byte
{
    /// <summary>Every call was resolved, validated and allowed by the host.</summary>
    Success,

    /// <summary>A sender, target or contract address that is not an EVM address.</summary>
    InvalidArgument,

    /// <summary>The transaction is not a function call (<see cref="EvmTransactionView.Kind"/>), or a nested call has fewer than four bytes (a <c>fallback</c> or <c>receive</c> call).</summary>
    NotAFunctionCall,

    /// <summary>The transaction carries no chain id (a legacy transaction without EIP-155 replay protection).</summary>
    MissingChainId,

    /// <summary>
    /// The transaction carries an EIP-7702 authorization list and the walk was not given its processed result from
    /// <see cref="EvmAuthorizationProcessor.TryProcess"/> (none, entries that do not match this envelope's tuples, or entries processed for another
    /// sender, transaction nonce or chain). Code observed before the transaction
    /// may not be the code that runs, so the walk refuses.
    /// </summary>
    AuthorizationsNotProcessed,

    /// <summary>
    /// A call reaches an account whose code when the transaction runs depends on that account's nonce at inclusion, so the code checked here may not be
    /// the code that runs: the list holds a pending tuple for it (<see cref="EvmAuthorization.IsPending"/>), or it is an authority other than the sender
    /// with an applied tuple whose failure (the authority spends the nonce first) would leave pinned code other than no code or the same delegation (a
    /// sponsored re-delegation). The sender's own tuples are settled: the transaction fixes its nonce.
    /// </summary>
    UnsettledDelegation,

    /// <summary>The host could not establish an account's code (it returned false, or <see cref="EvmCodeKind.Unknown"/>).</summary>
    CodeIdentityUnavailable,

    /// <summary>
    /// The call reaches no code: the target has none, or it is delegated (EIP-7702) to an account without code or to a precompile. Nothing would run, so no
    /// function can be resolved.
    /// </summary>
    NoCode,

    /// <summary>
    /// The target is delegated to an account whose own code is a delegation indicator. The EVM follows one delegation only and would execute the indicator
    /// bytes, which fail; the walk refuses rather than guess.
    /// </summary>
    ChainedDelegation,

    /// <summary>The registry knows nothing for the code address on this chain.</summary>
    UnknownContract,

    /// <summary>The registry knows the code address, but not at the identity the host reported (upgraded proxy or implementation).</summary>
    UnknownCodeIdentity,

    /// <summary>The registry has no function with the call's selector for that code.</summary>
    UnknownFunction,

    /// <summary>The selector resolves to more than one registration.</summary>
    AmbiguousFunction,

    /// <summary>The call's bytes failed strict ABI validation against the resolved function; <see cref="NestedCallWalkResult.DecodeStatus"/> says why.</summary>
    MalformedCalldata,

    /// <summary>The resolved function is registered as <see cref="ExecutionKind.Undescribed"/>.</summary>
    UndescribedExecution,

    /// <summary>
    /// A rule's path could not be read in the validated call (an index past the end of a dynamic array, or iterated arrays of different lengths);
    /// <see cref="NestedCallWalkResult.DecodeStatus"/> holds the accessor's status where there is one.
    /// </summary>
    DescriptorMismatch,

    /// <summary>An operation argument other than 0 or 1, or a <c>DELEGATECALL</c> with a non-zero value argument.</summary>
    UnsupportedCallMode,

    /// <summary>A nested call would exceed <see cref="NestedCallLimits.MaxDepth"/>.</summary>
    DepthLimitExceeded,

    /// <summary>The walk would validate more than <see cref="NestedCallLimits.MaxCalls"/> calls.</summary>
    CallLimitExceeded,

    /// <summary>The host's policy refused a call.</summary>
    DeniedByHost,
}

/// <summary>The outcome of a walk.</summary>
/// <param name="Status">The first failure, or <see cref="NestedCallStatus.Success"/>.</param>
/// <param name="CallCount">The calls the host allowed before the walk stopped (all of them on success).</param>
/// <param name="DecodeStatus">The ABI status behind <see cref="NestedCallStatus.MalformedCalldata"/> or <see cref="NestedCallStatus.DescriptorMismatch"/>; otherwise <see cref="ABIStatus.Success"/>.</param>
/// <param name="FailedCalldataRange">
/// On failure, the calldata range (transaction calldata coordinates) of the call being processed: the refused call itself, or for a rule failure
/// (<see cref="NestedCallStatus.DescriptorMismatch"/>, <see cref="NestedCallStatus.UnsupportedCallMode"/>, <see cref="NestedCallStatus.DepthLimitExceeded"/>)
/// the parent whose rule failed. Default on success.
/// </param>
public readonly record struct NestedCallWalkResult(NestedCallStatus Status, int CallCount, ABIStatus DecodeStatus, ABIRange FailedCalldataRange);

/// <summary>
/// The caller's side of a walk: the trusted source of code identities and the policy. The walker never decides what is allowed; it only refuses what it
/// cannot interpret. Implement it as a struct (or a <c>ref struct</c>) for an allocation-free walk. Exceptions thrown here propagate and abandon the walk.
/// </summary>
public interface INestedCallHost
{
    /// <summary>
    /// The code of <paramref name="account"/> on <paramref name="chainId"/>, from a trusted state source pinned for this request, BEFORE the transaction:
    /// <see cref="EvmAccountCode.Empty"/>, <see cref="EvmAccountCode.Contract"/> with the verified identity in the same convention the registry was built
    /// with, or <see cref="EvmAccountCode.DelegatedTo"/> for an EIP-7702 delegation indicator. The walker applies the transaction's own authorization list
    /// on top. Return false when it cannot be established (the walk then fails closed).
    /// </summary>
    bool TryGetCode(ulong chainId, in Address account, out EvmAccountCode code);

    /// <summary>
    /// The policy decision for one resolved, validated call, before its children are visited. <paramref name="calldata"/> is the strictly validated view of
    /// exactly <see cref="NestedCall.CalldataRange"/>, valid only during this call. Return false to deny (the walk stops with <see cref="NestedCallStatus.DeniedByHost"/>).
    /// </summary>
    bool Allows(in NestedCall nestedCall, scoped in ABICallData calldata);
}
