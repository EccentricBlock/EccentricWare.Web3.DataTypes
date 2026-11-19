using System.Buffers.Binary;
using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.ABI.EvmAdapters;

/// <summary>
/// The nested-call walker of spec 8.3: starting from a transaction's function call, it resolves every call against the trusted
/// <see cref="ContractFunctionResolver"/>, validates its bytes strictly against the resolved function, asks the host's policy, and follows ONLY the nested
/// calls the resolved <see cref="ExecutionDescriptor"/> declares, each of which is resolved, validated and evaluated in turn. It fails closed: an unknown,
/// ambiguous, malformed, undescribed or over-limit call stops the walk, and so does the first refusal by the host.
/// </summary>
/// <remarks>
/// <para>
/// The walk is depth-first and pre-order (a call is evaluated before its children); children of one call are visited in rule order, then element order.
/// That is the order of execution for single-rule descriptors; for several rules it need not be.
/// </para>
/// <para>
/// What it preserves per call (<see cref="NestedCall"/>): target, code address, context address, sender, value and call type under <c>CALL</c> and
/// <c>DELEGATECALL</c> semantics, the parent, the depth, the resolved code identity and descriptor, and the exact calldata range in the transaction's
/// calldata. No byte is copied or re-encoded: every child is a slice of the transaction's calldata, located through a validated parent.
/// </para>
/// <para>
/// EIP-7702: every call, whether <c>CALL</c> or <c>DELEGATECALL</c> and including the transaction's own, runs the code of its target after the
/// transaction's authorization list is applied. When that code is a delegation indicator, the delegate's code runs in the target's place: the walker
/// resolves the call by the delegate (its address and the identity the host reports for it), records the account as <see cref="NestedCall.TargetAddress"/>
/// and keeps the account as the context of a <c>CALL</c>. One hop only, as in the EVM: a delegate that is itself delegated is
/// <see cref="NestedCallStatus.ChainedDelegation"/>, and a target or delegate without code is <see cref="NestedCallStatus.NoCode"/>. A registry therefore
/// registers a delegate's functions once, under the delegate's address, for every account delegated to it.
/// </para>
/// <para>
/// What it does NOT prove (spec 8.3): that no other call happens. Calls made by code paths, storage, proxies or off-chain state are invisible to calldata
/// analysis; a descriptor covers only what its author declared. The walker is not a simulator and not a policy.
/// </para>
/// <para>
/// Cost: each call is validated once under the decode limits, and the number of calls and the depth are bounded by <see cref="NestedCallLimits"/>. Recursion
/// depth is at most <see cref="NestedCallLimits.HardMaxDepth"/> + 1. With a struct host the walk allocates nothing.
/// </para>
/// </remarks>
public sealed class NestedCallWalker
{
    private readonly ContractFunctionResolver _resolver;
    private readonly NestedCallLimits _limits;
    private readonly ABIDecodeLimits _decodeLimits;

    /// <summary>Creates a walker over a trusted registry. Immutable and shareable across threads.</summary>
    /// <param name="resolver">The trusted registry.</param>
    /// <param name="limits">Call-count and depth bounds.</param>
    /// <param name="decodeLimits">The ABI limits applied to each call's bytes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resolver"/> is null.</exception>
    /// <exception cref="ArgumentException">Either limits value is invalid.</exception>
    public NestedCallWalker(ContractFunctionResolver resolver, NestedCallLimits limits, ABIDecodeLimits decodeLimits)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        if (!limits.IsValid)
        {
            throw new ArgumentException("The nested-call limits are invalid.", nameof(limits));
        }

        if (!decodeLimits.IsValid)
        {
            throw new ArgumentException("The ABI decode limits are invalid.", nameof(decodeLimits));
        }

        _resolver = resolver;
        _limits = limits;
        _decodeLimits = decodeLimits;
    }

    /// <summary>
    /// Walks the function call of a transaction read by <see cref="RLPCalldataAdapter"/> that carries no EIP-7702 authorization list. An EIP-7702
    /// transaction is refused with <see cref="NestedCallStatus.AuthorizationsNotProcessed"/>; walk it with the overload that takes the processed list.
    /// </summary>
    /// <param name="transaction">A valid transaction view.</param>
    /// <param name="sender">The account that signs the transaction (the signer knows it; the envelope does not state it).</param>
    /// <param name="host">The code source and policy; passed by reference so a struct host keeps its state.</param>
    /// <param name="result">The outcome.</param>
    /// <returns>True only when every call was allowed.</returns>
    public bool TryWalk<THost>(scoped in EvmTransactionView transaction, Address sender, ref THost host, out NestedCallWalkResult result)
        where THost : INestedCallHost, allows ref struct
        => TryWalk(in transaction, sender, default, ref host, out result);

    /// <summary>
    /// Walks the function call of a transaction read by <see cref="RLPCalldataAdapter"/>, with the code each account has after the transaction's
    /// EIP-7702 authorization list is applied. Refuses, before resolving anything, a transaction that is not a function call
    /// (<see cref="NestedCallStatus.NotAFunctionCall"/>), has no chain id (<see cref="NestedCallStatus.MissingChainId"/>), or whose
    /// <paramref name="authorizations"/> are not the processed list of this envelope (<see cref="NestedCallStatus.AuthorizationsNotProcessed"/>: the count
    /// must equal <see cref="EvmTransactionView.AuthorizationCount"/>, every entry must have an outcome, every tuple must match the envelope's, and the
    /// entries must have been processed for <paramref name="sender"/> and this transaction's nonce and chain). A call that reaches an account whose code at
    /// inclusion depends on its nonce is <see cref="NestedCallStatus.UnsettledDelegation"/>.
    /// </summary>
    /// <param name="transaction">A valid transaction view.</param>
    /// <param name="sender">The account that signs the transaction; the same sender the authorizations were processed for.</param>
    /// <param name="authorizations">
    /// The entries <see cref="EvmAuthorizationProcessor.TryProcess"/> wrote for this transaction and sender (empty for a transaction of another type). Calls
    /// to an authority of an <see cref="EvmAuthorizationOutcome.Applied"/> entry run its delegate's code; every other account's code comes from the host.
    /// </param>
    /// <param name="host">The code source and policy; passed by reference so a struct host keeps its state.</param>
    /// <param name="result">The outcome.</param>
    /// <returns>True only when every call was allowed.</returns>
    public bool TryWalk<THost>(
        scoped in EvmTransactionView transaction, Address sender, ReadOnlySpan<EvmAuthorization> authorizations, ref THost host, out NestedCallWalkResult result)
        where THost : INestedCallHost, allows ref struct
    {
        NestedCallStatus refusal =
            !transaction.TryGetFunctionCalldata(out ReadOnlySpan<byte> calldata) ? NestedCallStatus.NotAFunctionCall
            : transaction.ChainId is null ? NestedCallStatus.MissingChainId
            : !BelongsTo(authorizations, in transaction, in sender) ? NestedCallStatus.AuthorizationsNotProcessed
            : NestedCallStatus.Success;
        if (refusal != NestedCallStatus.Success)
        {
            result = new NestedCallWalkResult(refusal, 0, ABIStatus.Success, default);
            return false;
        }

        return Walk(transaction.ChainId!.Value, sender, transaction.To, transaction.Value, calldata, authorizations, ref host, out result);
    }

    /// <summary>
    /// Walks a function call given its root context explicitly, for calldata that did not come from an envelope. The caller asserts that
    /// <paramref name="calldata"/> is a function call to <paramref name="to"/>, and that the host's code is the code in effect when it runs (no EIP-7702
    /// authorization list is applied here; walk an EIP-7702 transaction through its view).
    /// </summary>
    /// <param name="chainId">The chain to resolve against.</param>
    /// <param name="sender">The transaction's sender.</param>
    /// <param name="to">The called contract.</param>
    /// <param name="value">The transaction's value.</param>
    /// <param name="calldata">The call's bytes; every reported range is relative to its first byte.</param>
    /// <param name="host">The identity source and policy.</param>
    /// <param name="result">The outcome.</param>
    /// <returns>True only when every call was allowed.</returns>
    public bool TryWalk<THost>(ulong chainId, Address sender, Address to, uint256 value, ReadOnlySpan<byte> calldata, ref THost host, out NestedCallWalkResult result)
        where THost : INestedCallHost, allows ref struct
        => Walk(chainId, sender, to, value, calldata, default, ref host, out result);

    private bool Walk<THost>(
        ulong chainId, Address sender, Address to, uint256 value, ReadOnlySpan<byte> calldata, ReadOnlySpan<EvmAuthorization> authorizations, ref THost host,
        out NestedCallWalkResult result)
        where THost : INestedCallHost, allows ref struct
    {
        if (sender.Type != AddressType.Evm || to.Type != AddressType.Evm)
        {
            result = new NestedCallWalkResult(NestedCallStatus.InvalidArgument, 0, ABIStatus.Success, default);
            return false;
        }

        var state = new WalkState(chainId, authorizations);
        var root = new Frame(to, to, sender, value, NestedCallMode.Call, depth: 0, parentIndex: -1, new ABIRange(0, calldata.Length));
        NestedCallStatus status = Visit(ref state, in root, calldata, ref host);
        result = status == NestedCallStatus.Success
            ? new NestedCallWalkResult(status, state.CallCount, ABIStatus.Success, default)
            : new NestedCallWalkResult(status, state.CallCount, state.DecodeStatus, state.FailedRange);
        return status == NestedCallStatus.Success;
    }

    /// <summary>
    /// True when <paramref name="authorizations"/> is the processed authorization list of <paramref name="transaction"/> for <paramref name="sender"/>: one
    /// entry per tuple, each with an outcome, the very tuple the envelope holds at its position, and processed for this sender, transaction nonce and chain.
    /// Entries can only come from <see cref="EvmAuthorizationProcessor"/>, and an outcome depends only on those inputs, the tuples up to it and the pinned
    /// state, so this catches a missing list and any list whose outcomes could differ for this transaction.
    /// </summary>
    private static bool BelongsTo(scoped ReadOnlySpan<EvmAuthorization> authorizations, scoped in EvmTransactionView transaction, in Address sender)
    {
        if (authorizations.Length != transaction.AuthorizationCount)
        {
            return false;
        }

        if (authorizations.IsEmpty)
        {
            return true;
        }

        RLPData list = transaction.AuthorizationList;
        if (list.TryGetChildren(out RLPChildEnumerator tuples) != RLPStatus.Success)
        {
            return false;
        }

        for (int i = 0; i < authorizations.Length; i++)
        {
            if (!tuples.MoveNext())
            {
                return false;
            }

            RLPData tuple = tuples.Current;
            if (authorizations[i].Outcome == EvmAuthorizationOutcome.None
                || !authorizations[i].WasProcessedFor(in sender, transaction.Nonce, transaction.ChainId!.Value)
                || !EvmAuthorizationTuple.TryRead(in tuple, out EvmAuthorization stated, out _, i)
                || !authorizations[i].HasSameTuple(in stated))
            {
                return false;
            }
        }

        return true;
    }

    private ref struct WalkState(ulong chainId, ReadOnlySpan<EvmAuthorization> authorizations)
    {
        public readonly ulong ChainId = chainId;
        public readonly ReadOnlySpan<EvmAuthorization> Authorizations = authorizations;
        public int CallCount;
        public ABIStatus DecodeStatus;
        public ABIRange FailedRange;
    }

    private readonly struct Frame(Address target, Address context, Address sender, uint256 value, NestedCallMode mode, int depth, int parentIndex, ABIRange range)
    {
        public readonly Address Target = target;
        public readonly Address Context = context;
        public readonly Address Sender = sender;
        public readonly uint256 Value = value;
        public readonly NestedCallMode Mode = mode;
        public readonly int Depth = depth;
        public readonly int ParentIndex = parentIndex;
        public readonly ABIRange Range = range; // in root calldata coordinates
    }

    private NestedCallStatus Visit<THost>(ref WalkState state, in Frame frame, ReadOnlySpan<byte> root, ref THost host)
        where THost : INestedCallHost, allows ref struct
    {
        state.FailedRange = frame.Range;
        if (state.CallCount >= _limits.MaxCalls)
        {
            return NestedCallStatus.CallLimitExceeded;
        }

        ReadOnlySpan<byte> calldata = root.Slice(frame.Range.Offset, frame.Range.Length);
        if (calldata.Length < 4)
        {
            return NestedCallStatus.NotAFunctionCall;
        }

        NestedCallStatus codeStatus = ResolveCode(ref state, in frame.Target, ref host, out Address codeAddress, out Hash32 identity);
        if (codeStatus != NestedCallStatus.Success)
        {
            return codeStatus;
        }

        var selector = new FunctionSelector(BinaryPrimitives.ReadUInt32BigEndian(calldata));
        if (!_resolver.TryResolve(state.ChainId, in codeAddress, in identity, selector, out ExecutionDescriptor? descriptor, out ContractResolutionStatus resolution))
        {
            return resolution switch
            {
                ContractResolutionStatus.UnknownContract => NestedCallStatus.UnknownContract,
                ContractResolutionStatus.UnknownCodeIdentity => NestedCallStatus.UnknownCodeIdentity,
                ContractResolutionStatus.UnknownFunction => NestedCallStatus.UnknownFunction,
                ContractResolutionStatus.Ambiguous => NestedCallStatus.AmbiguousFunction,
                _ => NestedCallStatus.InvalidArgument,
            };
        }

        if (!ABICallData.TryDecode(calldata, descriptor.Function, in _decodeLimits, out ABICallData call, out ABIStatus decodeStatus))
        {
            state.DecodeStatus = decodeStatus;
            return NestedCallStatus.MalformedCalldata;
        }

        if (descriptor.Kind == ExecutionKind.Undescribed)
        {
            return NestedCallStatus.UndescribedExecution;
        }

        int index = state.CallCount;
        var nested = new NestedCall(
            index, frame.ParentIndex, frame.Depth, state.ChainId, frame.Mode, frame.Target, codeAddress, frame.Context, frame.Sender, frame.Value, identity, descriptor, frame.Range);
        if (!host.Allows(in nested, in call))
        {
            return NestedCallStatus.DeniedByHost;
        }

        state.CallCount++;
        if (descriptor.Kind == ExecutionKind.NoNestedCalls)
        {
            return NestedCallStatus.Success;
        }

        Span<int> path = stackalloc int[ABIDecodeLimits.HardMaxValueDepth];
        foreach (NestedCallRule rule in descriptor.Rules)
        {
            NestedCallStatus status = VisitRule(ref state, in frame, index, rule, in call, path, root, ref host);
            if (status != NestedCallStatus.Success)
            {
                return status;
            }
        }

        return NestedCallStatus.Success;
    }

    private NestedCallStatus VisitRule<THost>(
        ref WalkState state, in Frame parent, int parentIndex, NestedCallRule rule, scoped in ABICallData call, scoped Span<int> path, ReadOnlySpan<byte> root, ref THost host)
        where THost : INestedCallHost, allows ref struct
    {
        int each = rule.CalldataPath.IndexOf(NestedCallRule.EachElement);
        if (each < 0)
        {
            return VisitChild(ref state, in parent, parentIndex, rule, element: 0, in call, path, root, ref host);
        }

        ABIData arguments = call.Arguments;
        if (!arguments.TryGetArrayLength(rule.CalldataPath[..each], out int count, out ABIStatus lengthStatus))
        {
            return Mismatch(ref state, in parent, lengthStatus);
        }

        // Iterated target, value or operation arrays must line up with the calldata array element for element (ERC-4337 executeBatch style).
        if (!SameLength(in arguments, rule.TargetPath, count, ref state)
            || !SameLength(in arguments, rule.ValuePath, count, ref state)
            || !SameLength(in arguments, rule.OperationPath, count, ref state))
        {
            state.FailedRange = parent.Range;
            return NestedCallStatus.DescriptorMismatch;
        }

        for (int i = 0; i < count; i++)
        {
            NestedCallStatus status = VisitChild(ref state, in parent, parentIndex, rule, i, in call, path, root, ref host);
            if (status != NestedCallStatus.Success)
            {
                return status;
            }
        }

        return NestedCallStatus.Success;
    }

    private static bool SameLength(scoped in ABIData arguments, ReadOnlySpan<int> template, int count, ref WalkState state)
    {
        int each = template.IndexOf(NestedCallRule.EachElement);
        if (each < 0)
        {
            return true;
        }

        if (!arguments.TryGetArrayLength(template[..each], out int length, out ABIStatus status))
        {
            state.DecodeStatus = status;
            return false;
        }

        return length == count;
    }

    private NestedCallStatus VisitChild<THost>(
        ref WalkState state, in Frame parent, int parentIndex, NestedCallRule rule, int element, scoped in ABICallData call, scoped Span<int> path, ReadOnlySpan<byte> root, ref THost host)
        where THost : INestedCallHost, allows ref struct
    {
        if (parent.Depth >= _limits.MaxDepth)
        {
            state.FailedRange = parent.Range;
            return NestedCallStatus.DepthLimitExceeded;
        }

        ABIData arguments = call.Arguments;
        if (!arguments.TryGetBytePayload(Fill(rule.CalldataPath, element, path), out _, out ABIRange payloadRange, out ABIStatus status)
            || !call.TryTranslateArgumentRange(payloadRange, out ABIRange callRange))
        {
            return Mismatch(ref state, in parent, status);
        }

        Address target = parent.Context;
        if (!rule.TargetsSelf && !arguments.TryGetAddress(Fill(rule.TargetPath, element, path), out target, out status))
        {
            return Mismatch(ref state, in parent, status);
        }

        uint256 value = uint256.Zero;
        if (rule.HasValuePath && !arguments.TryGetUInt256(Fill(rule.ValuePath, element, path), out value, out status))
        {
            return Mismatch(ref state, in parent, status);
        }

        NestedCallMode mode = rule.Mode;
        if (rule.HasOperationPath)
        {
            if (!arguments.TryGetUInt256(Fill(rule.OperationPath, element, path), out uint256 operation, out status))
            {
                return Mismatch(ref state, in parent, status);
            }

            if (operation > uint256.One)
            {
                state.FailedRange = parent.Range;
                return NestedCallStatus.UnsupportedCallMode;
            }

            mode = operation.IsZero ? NestedCallMode.Call : NestedCallMode.DelegateCall;
        }

        Frame child;
        if (mode == NestedCallMode.Call)
        {
            child = new Frame(target, target, parent.Context, value, mode, parent.Depth + 1, parentIndex, Offset(in parent, callRange));
        }
        else
        {
            if (!value.IsZero)
            {
                state.FailedRange = parent.Range;
                return NestedCallStatus.UnsupportedCallMode; // a DELEGATECALL cannot move value; a non-zero value argument is a contradiction
            }

            child = new Frame(target, parent.Context, parent.Sender, parent.Value, mode, parent.Depth + 1, parentIndex, Offset(in parent, callRange));
        }

        return Visit(ref state, in child, root, ref host);
    }

    /// <summary>
    /// The code a call to <paramref name="target"/> runs: the target's own contract code, or (EIP-7702) its delegate's, one hop only. The code of an account
    /// is the transaction's applied delegation when it has one, otherwise the host's pinned state.
    /// </summary>
    private static NestedCallStatus ResolveCode<THost>(ref WalkState state, in Address target, ref THost host, out Address codeAddress, out Hash32 identity)
        where THost : INestedCallHost, allows ref struct
    {
        codeAddress = target;
        identity = default;
        NestedCallStatus status = GetCode(ref state, in target, ref host, out EvmAccountCode code);
        if (status != NestedCallStatus.Success)
        {
            return status;
        }

        if (code.Kind == EvmCodeKind.Delegated)
        {
            codeAddress = code.Delegate;
            status = GetCode(ref state, in codeAddress, ref host, out code);
            if (status != NestedCallStatus.Success)
            {
                return status;
            }

            if (code.Kind == EvmCodeKind.Delegated)
            {
                return NestedCallStatus.ChainedDelegation;
            }
        }

        if (code.Kind != EvmCodeKind.Contract)
        {
            return NestedCallStatus.NoCode;
        }

        identity = code.Identity;
        return NestedCallStatus.Success;
    }

    /// <summary>
    /// The account's code after the transaction's authorization list: an applied delegation wins over the host's pre-transaction state. Refused as
    /// unsettled: an account with a pending tuple, and an authority other than the sender whose applied tuples, if its nonce moved first, would leave
    /// pinned code that runs something else.
    /// </summary>
    private static NestedCallStatus GetCode<THost>(ref WalkState state, in Address account, ref THost host, out EvmAccountCode code)
        where THost : INestedCallHost, allows ref struct
    {
        code = default;
        int applied = -1;
        for (int i = 0; i < state.Authorizations.Length; i++)
        {
            ref readonly EvmAuthorization entry = ref state.Authorizations[i];
            if (entry.Authority != account)
            {
                continue;
            }

            if (entry.IsPending)
            {
                return NestedCallStatus.UnsettledDelegation;
            }

            if (entry.Outcome == EvmAuthorizationOutcome.Applied)
            {
                applied = i;
            }
        }

        bool pinned = host.TryGetCode(state.ChainId, in account, out EvmAccountCode pinnedCode) && pinnedCode.Kind != EvmCodeKind.Unknown;
        if (applied < 0)
        {
            code = pinnedCode;
            return pinned ? NestedCallStatus.Success : NestedCallStatus.CodeIdentityUnavailable;
        }

        ref readonly EvmAuthorization winner = ref state.Authorizations[applied];
        code = winner.DelegateAddress.IsZero ? EvmAccountCode.Empty : EvmAccountCode.DelegatedTo(winner.DelegateAddress);
        if (winner.IsSenderAuthority)
        {
            return NestedCallStatus.Success; // the transaction fixes the sender's nonce, so its applied tuples apply whenever the transaction does
        }

        // Another authority can spend the nonce its applied tuples need before inclusion; every tuple of the chain then fails and the pinned code stays
        // (a chain of consecutive nonces ends at the last applied tuple or at none). That is harmless when the pinned code runs nothing, or is the same
        // delegation; otherwise the code that runs is not settled.
        if (!pinned)
        {
            return NestedCallStatus.CodeIdentityUnavailable;
        }

        bool sameCode = pinnedCode.Kind == code.Kind && (code.Kind == EvmCodeKind.Empty || pinnedCode.Delegate == code.Delegate);
        return pinnedCode.Kind == EvmCodeKind.Empty || sameCode ? NestedCallStatus.Success : NestedCallStatus.UnsettledDelegation;
    }

    /// <summary>A child range in the parent's calldata coordinates, moved to root coordinates. The child lies inside the parent, so the sum fits an int.</summary>
    private static ABIRange Offset(in Frame parent, ABIRange callRange) => new(parent.Range.Offset + callRange.Offset, callRange.Length);

    private static NestedCallStatus Mismatch(ref WalkState state, in Frame parent, ABIStatus status)
    {
        state.DecodeStatus = status;
        state.FailedRange = parent.Range;
        return NestedCallStatus.DescriptorMismatch;
    }

    /// <summary>Copies <paramref name="template"/> into <paramref name="buffer"/> with every EachElement step replaced by <paramref name="element"/>.</summary>
    private static ReadOnlySpan<int> Fill(ReadOnlySpan<int> template, int element, Span<int> buffer)
    {
        Span<int> path = buffer[..template.Length];
        for (int i = 0; i < template.Length; i++)
        {
            path[i] = template[i] == NestedCallRule.EachElement ? element : template[i];
        }

        return path;
    }
}
