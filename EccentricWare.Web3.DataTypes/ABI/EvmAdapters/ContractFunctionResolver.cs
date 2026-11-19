using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace EccentricWare.Web3.DataTypes.ABI.EvmAdapters;

/// <summary>Result of <see cref="ContractFunctionResolver.TryResolve"/>. Every status except <see cref="Success"/> is a denial.</summary>
public enum ContractResolutionStatus : byte
{
    /// <summary>Exactly one function is registered for the chain, contract, code identity and selector.</summary>
    Success,

    /// <summary>The contract address is not an EVM address.</summary>
    InvalidArgument,

    /// <summary>Nothing is registered for this contract on this chain.</summary>
    UnknownContract,

    /// <summary>The contract is registered, but not for this code identity: its code (or its proxy implementation or delegation) changed, or the identity is wrong.</summary>
    UnknownCodeIdentity,

    /// <summary>The contract and code identity are registered, but no function has this selector. A public four-byte database match does not change this.</summary>
    UnknownFunction,

    /// <summary>Two or more registrations share the selector for this contract and code identity (a selector collision or a duplicate): never resolved by picking one.</summary>
    Ambiguous,
}

/// <summary>
/// The contract-scoped identity resolver of spec 3.1 and 9.2: binds a chain, a contract address, a verified code identity and a selector to exactly one
/// trusted <see cref="ExecutionDescriptor"/>. A selector alone is never enough; an unknown or ambiguous binding denies. Immutable once built (create it with
/// <see cref="ContractFunctionResolverBuilder"/> at startup), safe to share across threads, and allocation-free per lookup.
/// </summary>
/// <remarks>
/// <para>
/// The code identity is a caller-defined <see cref="Hash32"/> that names the code that actually runs, established from a trusted state source: typically the
/// Keccak-256 of the runtime bytecode; for a proxy, a digest that also covers the implementation's code; for an account with an EIP-7702 delegation, the
/// delegate's code. This library does not fetch code or compute identities (no RPC); the registry and the identity source are the caller's trust anchors.
/// </para>
/// <para>
/// The resolver is not a policy: <see cref="ContractResolutionStatus.Success"/> means "these bytes should be read with this schema and descriptor", not "this
/// call is allowed".
/// </para>
/// </remarks>
public sealed class ContractFunctionResolver
{
    private readonly FrozenSet<(ulong Chain, Address Contract)> _contracts;
    private readonly FrozenSet<(ulong Chain, Address Contract, Hash32 Identity)> _identities;
    private readonly FrozenDictionary<(ulong Chain, Address Contract, Hash32 Identity, uint Selector), ExecutionDescriptor[]> _functions;

    internal ContractFunctionResolver(IReadOnlyList<(ulong Chain, Address Contract, Hash32 Identity, ExecutionDescriptor Descriptor)> entries)
    {
        var functions = new Dictionary<(ulong, Address, Hash32, uint), List<ExecutionDescriptor>>();
        var contracts = new HashSet<(ulong, Address)>();
        var identities = new HashSet<(ulong, Address, Hash32)>();
        foreach (var (chain, contract, identity, descriptor) in entries)
        {
            contracts.Add((chain, contract));
            identities.Add((chain, contract, identity));
            var key = (chain, contract, identity, descriptor.Function.Selector);
            if (!functions.TryGetValue(key, out List<ExecutionDescriptor>? list))
            {
                functions[key] = list = [];
            }

            list.Add(descriptor);
        }

        _contracts = contracts.ToFrozenSet();
        _identities = identities.ToFrozenSet();
        _functions = functions.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        Count = entries.Count;
    }

    /// <summary>The number of registrations (duplicates included).</summary>
    public int Count { get; }

    /// <summary>
    /// Finds the one descriptor registered for <paramref name="chainId"/>, <paramref name="contract"/>, <paramref name="codeIdentity"/> and
    /// <paramref name="selector"/>. Fails, with a null descriptor, on any other outcome; see <see cref="ContractResolutionStatus"/>. Never throws.
    /// </summary>
    /// <param name="chainId">The chain the call runs on.</param>
    /// <param name="contract">The address whose code runs (for a <c>DELEGATECALL</c>, the code address, not the context).</param>
    /// <param name="codeIdentity">The verified identity of that code now, from the caller's trusted state source.</param>
    /// <param name="selector">The first four bytes of the calldata.</param>
    /// <param name="descriptor">The descriptor, or null.</param>
    /// <param name="status">The outcome.</param>
    public bool TryResolve(
        ulong chainId,
        in Address contract,
        in Hash32 codeIdentity,
        FunctionSelector selector,
        [NotNullWhen(true)] out ExecutionDescriptor? descriptor,
        out ContractResolutionStatus status)
    {
        descriptor = null;
        if (contract.Type != AddressType.Evm)
        {
            status = ContractResolutionStatus.InvalidArgument;
            return false;
        }

        if (!_functions.TryGetValue((chainId, contract, codeIdentity, selector.Value), out ExecutionDescriptor[]? candidates))
        {
            status = !_contracts.Contains((chainId, contract)) ? ContractResolutionStatus.UnknownContract
                : !_identities.Contains((chainId, contract, codeIdentity)) ? ContractResolutionStatus.UnknownCodeIdentity
                : ContractResolutionStatus.UnknownFunction;
            return false;
        }

        if (candidates.Length != 1)
        {
            status = ContractResolutionStatus.Ambiguous;
            return false;
        }

        descriptor = candidates[0];
        status = ContractResolutionStatus.Success;
        return true;
    }
}

/// <summary>
/// Collects registrations for a <see cref="ContractFunctionResolver"/>. Not thread-safe; build once at startup from trusted metadata. Every registration
/// is kept: two registrations with the same selector for the same chain, contract and code identity (a real four-byte collision, or the same function twice)
/// make that selector <see cref="ContractResolutionStatus.Ambiguous"/>, so a configuration mistake denies instead of silently choosing.
/// </summary>
public sealed class ContractFunctionResolverBuilder
{
    private readonly List<(ulong Chain, Address Contract, Hash32 Identity, ExecutionDescriptor Descriptor)> _entries = [];

    /// <summary>Registers <paramref name="descriptor"/> for one contract at one verified code identity on one chain.</summary>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="contract"/> is not an EVM address.</exception>
    public ContractFunctionResolverBuilder Add(ulong chainId, Address contract, Hash32 codeIdentity, ExecutionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (contract.Type != AddressType.Evm)
        {
            throw new ArgumentException("The contract must be an EVM address.", nameof(contract));
        }

        _entries.Add((chainId, contract, codeIdentity, descriptor));
        return this;
    }

    /// <summary>An immutable resolver over a snapshot of the registrations; later <see cref="Add"/> calls do not affect it.</summary>
    public ContractFunctionResolver Build() => new([.. _entries]);
}
