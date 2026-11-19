namespace EccentricWare.Web3.DataTypes.ABI.EvmAdapters;

/// <summary>What an account's code is, as a host reports it in <see cref="EvmAccountCode"/>.</summary>
public enum EvmCodeKind : byte
{
    /// <summary>The <c>default</c> value: nothing reported. Every consumer treats it as "cannot be established" and fails closed.</summary>
    Unknown,

    /// <summary>No code: an externally owned account without delegation, a precompile, or an account that does not exist.</summary>
    Empty,

    /// <summary>Contract code other than a delegation indicator, identified by <see cref="EvmAccountCode.Identity"/>.</summary>
    Contract,

    /// <summary>An EIP-7702 delegation indicator (<c>0xef0100 || address</c>): calls to the account run the code of <see cref="EvmAccountCode.Delegate"/>.</summary>
    Delegated,
}

/// <summary>
/// The code of one account, from a trusted state source pinned for the request (spec 9.2): empty, a contract with a verified identity, or an EIP-7702
/// delegation to another address. <see cref="INestedCallHost"/> and <see cref="IEvmAuthorizationHost"/> report it; the walker and the authorization
/// processor interpret it. A delegation must be reported as <see cref="EvmCodeKind.Delegated"/>, never as a contract whose identity is the indicator's hash,
/// so that calls are resolved by the code that actually runs.
/// </summary>
public readonly struct EvmAccountCode
{
    private EvmAccountCode(EvmCodeKind kind, Hash32 identity, Address delegateAddress)
    {
        Kind = kind;
        Identity = identity;
        Delegate = delegateAddress;
    }

    /// <summary>No code (see <see cref="EvmCodeKind.Empty"/>).</summary>
    public static EvmAccountCode Empty => new(EvmCodeKind.Empty, default, default);

    /// <summary>What the code is; <see cref="EvmCodeKind.Unknown"/> for <c>default</c>.</summary>
    public EvmCodeKind Kind { get; }

    /// <summary>The verified code identity, in the convention the <see cref="ContractFunctionResolver"/> was built with; meaningful only for <see cref="EvmCodeKind.Contract"/>.</summary>
    public Hash32 Identity { get; }

    /// <summary>The delegation target; meaningful only for <see cref="EvmCodeKind.Delegated"/>.</summary>
    public Address Delegate { get; }

    /// <summary>Contract code with a verified identity.</summary>
    public static EvmAccountCode Contract(in Hash32 identity) => new(EvmCodeKind.Contract, identity, default);

    /// <summary>An EIP-7702 delegation indicator pointing at <paramref name="delegateAddress"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="delegateAddress"/> is not an EVM address.</exception>
    public static EvmAccountCode DelegatedTo(Address delegateAddress)
    {
        if (delegateAddress.Type != AddressType.Evm)
        {
            throw new ArgumentException("A delegation target must be an EVM address.", nameof(delegateAddress));
        }

        return new EvmAccountCode(EvmCodeKind.Delegated, default, delegateAddress);
    }
}
