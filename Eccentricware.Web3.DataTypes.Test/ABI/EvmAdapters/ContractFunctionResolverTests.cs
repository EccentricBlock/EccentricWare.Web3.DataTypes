using EccentricWare.Web3.DataTypes.ABI;
using EccentricWare.Web3.DataTypes.ABI.EvmAdapters;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.EvmAdapters.EvmAdapterFixtures;

namespace EccentricWare.Web3.DataTypes.Tests.ABI.EvmAdapters;

/// <summary>
/// <see cref="ContractFunctionResolver"/> (spec 3.1, 9.2 "selector collision"): a selector resolves only within a chain, a contract and a verified code
/// identity, to exactly one registration; everything else is a distinct denial. Lookups never throw and allocate nothing.
/// </summary>
[TestClass]
public sealed class ContractFunctionResolverTests
{
    private static readonly Hash32 CodeV1 = new(0, 0, 0, 1);
    private static readonly Hash32 CodeV2 = new(0, 0, 0, 2);
    private static readonly ABIFunctionSchema Transfer = Function("transfer(address,uint256)");
    private static readonly ABIFunctionSchema Approve = Function("approve(address,uint256)");
    private static readonly ABIFunctionSchema Burn = Function("burn(uint256)");
    private static readonly ABIFunctionSchema Collate = Function("collate_propagate_storage(bytes16)");

    private static ContractFunctionResolver Registry() => new ContractFunctionResolverBuilder()
        .Add(ChainId, Router, CodeV1, ExecutionDescriptor.NoNestedCalls(Transfer))
        .Add(ChainId, Router, CodeV1, ExecutionDescriptor.NoNestedCalls(Approve))
        .Build();

    private static ContractResolutionStatus Resolve(ContractFunctionResolver resolver, ulong chain, Address contract, Hash32 code, uint selector, out ExecutionDescriptor? descriptor)
    {
        bool ok = resolver.TryResolve(chain, in contract, in code, new FunctionSelector(selector), out descriptor, out ContractResolutionStatus status);
        Assert.AreEqual(ok, status == ContractResolutionStatus.Success);
        Assert.AreEqual(ok, descriptor is not null, "a descriptor only on success");
        return status;
    }

    [TestMethod]
    public void AFullyBoundSelector_ResolvesToItsOneDescriptor()
    {
        ContractFunctionResolver resolver = Registry();
        Assert.AreEqual(2, resolver.Count);
        Assert.AreEqual(ContractResolutionStatus.Success, Resolve(resolver, ChainId, Router, CodeV1, 0xa9059cbb, out ExecutionDescriptor? transfer));
        Assert.AreSame(Transfer, transfer!.Function);
        Assert.AreEqual(ContractResolutionStatus.Success, Resolve(resolver, ChainId, Router, CodeV1, 0x095ea7b3, out ExecutionDescriptor? approve));
        Assert.AreSame(Approve, approve!.Function);
    }

    [TestMethod]
    public void EveryMissingBinding_IsItsOwnDenial()
    {
        ContractFunctionResolver resolver = Registry();
        Assert.AreEqual(ContractResolutionStatus.UnknownContract, Resolve(resolver, ChainId, Recipient, CodeV1, 0xa9059cbb, out _), "another contract");
        Assert.AreEqual(ContractResolutionStatus.UnknownContract, Resolve(resolver, 1, Router, CodeV1, 0xa9059cbb, out _), "the same contract on another chain");
        Assert.AreEqual(ContractResolutionStatus.UnknownCodeIdentity, Resolve(resolver, ChainId, Router, CodeV2, 0xa9059cbb, out _), "upgraded implementation or changed delegation");
        Assert.AreEqual(ContractResolutionStatus.UnknownCodeIdentity, Resolve(resolver, ChainId, Router, Hash32.Zero, 0xa9059cbb, out _));
        Assert.AreEqual(ContractResolutionStatus.UnknownFunction, Resolve(resolver, ChainId, Router, CodeV1, 0xdeadbeef, out _), "a selector nobody registered");
        Assert.AreEqual(ContractResolutionStatus.UnknownFunction, Resolve(resolver, ChainId, Router, CodeV1, 0x42966c68, out _), "a selector a public four-byte database knows (burn) is still unknown here");
        Assert.AreEqual(ContractResolutionStatus.UnknownContract, Resolve(new ContractFunctionResolverBuilder().Build(), ChainId, Router, CodeV1, 0xa9059cbb, out _), "an empty registry");

        Address solana = Address.ZeroSolana;
        Assert.IsFalse(resolver.TryResolve(ChainId, in solana, in CodeV1, new FunctionSelector(0xa9059cbb), out ExecutionDescriptor? none, out ContractResolutionStatus status));
        Assert.AreEqual(ContractResolutionStatus.InvalidArgument, status);
        Assert.IsNull(none);
    }

    [TestMethod]
    public void ARealFourByteCollision_IsAmbiguous_AndSoIsADuplicate()
    {
        // burn(uint256) and collate_propagate_storage(bytes16) share 0x42966c68 (Keccak-256; checked independently with Foundry `cast sig`).
        Assert.AreEqual(0x42966c68u, Burn.Selector);
        Assert.AreEqual(Burn.Selector, Collate.Selector);
        ContractFunctionResolver collision = new ContractFunctionResolverBuilder()
            .Add(ChainId, Router, CodeV1, ExecutionDescriptor.NoNestedCalls(Burn))
            .Add(ChainId, Router, CodeV1, ExecutionDescriptor.NoNestedCalls(Collate))
            .Build();
        Assert.AreEqual(ContractResolutionStatus.Ambiguous, Resolve(collision, ChainId, Router, CodeV1, 0x42966c68, out _), "never resolved by picking one");

        ContractFunctionResolver duplicate = new ContractFunctionResolverBuilder()
            .Add(ChainId, Router, CodeV1, ExecutionDescriptor.NoNestedCalls(Burn))
            .Add(ChainId, Router, CodeV1, ExecutionDescriptor.NoNestedCalls(Burn))
            .Build();
        Assert.AreEqual(ContractResolutionStatus.Ambiguous, Resolve(duplicate, ChainId, Router, CodeV1, 0x42966c68, out _), "a duplicate registration is a configuration error, so it denies");

        // The same selector under different code identities or contracts is not a collision: the binding separates them.
        ContractFunctionResolver separated = new ContractFunctionResolverBuilder()
            .Add(ChainId, Router, CodeV1, ExecutionDescriptor.NoNestedCalls(Burn))
            .Add(ChainId, Router, CodeV2, ExecutionDescriptor.NoNestedCalls(Collate))
            .Add(ChainId, Recipient, CodeV1, ExecutionDescriptor.NoNestedCalls(Collate))
            .Build();
        Assert.AreEqual(ContractResolutionStatus.Success, Resolve(separated, ChainId, Router, CodeV1, 0x42966c68, out ExecutionDescriptor? v1));
        Assert.AreSame(Burn, v1!.Function);
        Assert.AreEqual(ContractResolutionStatus.Success, Resolve(separated, ChainId, Router, CodeV2, 0x42966c68, out ExecutionDescriptor? v2));
        Assert.AreSame(Collate, v2!.Function);
        Assert.AreEqual(ContractResolutionStatus.Success, Resolve(separated, ChainId, Recipient, CodeV1, 0x42966c68, out ExecutionDescriptor? other));
        Assert.AreSame(Collate, other!.Function);
    }

    [TestMethod]
    public void TheBuilder_RefusesProgrammingErrors_AndTheResolverIsASnapshot()
    {
        var builder = new ContractFunctionResolverBuilder();
        Assert.ThrowsExactly<ArgumentNullException>(() => builder.Add(ChainId, Router, CodeV1, null!));
        Assert.ThrowsExactly<ArgumentException>(() => builder.Add(ChainId, Address.ZeroSolana, CodeV1, ExecutionDescriptor.NoNestedCalls(Transfer)));

        builder.Add(ChainId, Router, CodeV1, ExecutionDescriptor.NoNestedCalls(Transfer));
        ContractFunctionResolver first = builder.Build();
        builder.Add(ChainId, Router, CodeV1, ExecutionDescriptor.NoNestedCalls(Approve));
        Assert.AreEqual(1, first.Count, "a later Add does not reach a built resolver");
        Assert.AreEqual(ContractResolutionStatus.UnknownFunction, Resolve(first, ChainId, Router, CodeV1, 0x095ea7b3, out _));
        Assert.AreEqual(ContractResolutionStatus.Success, Resolve(builder.Build(), ChainId, Router, CodeV1, 0x095ea7b3, out _));
    }

    [TestMethod]
    public void TheKeyHashCodes_NeverThrow_EvenInTheCheckedArithmeticBuild()
    {
        // Regression: Hash256.Compute (behind Address, Hash32, uint256 and int256 GetHashCode) wrapped without `unchecked`, so in the checked build
        // (-p:RlpCheckedArithmetic=true, run by CI with this filter) building a resolver threw OverflowException. Extreme limbs make overflow certain.
        Address[] addresses = [Address.FromEvmBytes(Enumerable.Repeat((byte)0xff, 20).ToArray()), Router, default, Address.ZeroSolana];
        Hash32[] identities = [new(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue), CodeV1, Hash32.Zero];
        var seen = new HashSet<int>();
        foreach (Address address in addresses)
        {
            seen.Add(address.GetHashCode());
        }

        foreach (Hash32 identity in identities)
        {
            seen.Add(identity.GetHashCode());
        }

        seen.Add(uint256.MaxValue.GetHashCode());
        seen.Add(int256.MinValue.GetHashCode());
        Assert.IsGreaterThan(1, seen.Count);
        Assert.AreEqual(ContractResolutionStatus.Success, Resolve(new ContractFunctionResolverBuilder()
            .Add(ulong.MaxValue, addresses[0], identities[0], ExecutionDescriptor.NoNestedCalls(Transfer)).Build(), ulong.MaxValue, addresses[0], identities[0], 0xa9059cbb, out _));
    }

    [TestMethod]
    public void TryResolve_AllocatesNothing_OnSuccessOrFailure()
    {
        ContractFunctionResolver resolver = Registry();
        Address router = Router;
        Address other = Recipient;
        int found = 0;
        void Run()
        {
            for (int i = 0; i < 16; i++)
            {
                found += resolver.TryResolve(ChainId, in router, in CodeV1, new FunctionSelector(0xa9059cbb), out _, out _) ? 1 : 0;
                resolver.TryResolve(ChainId, in router, in CodeV2, new FunctionSelector(0xa9059cbb), out _, out _);
                resolver.TryResolve(ChainId, in other, in CodeV1, new FunctionSelector(0xa9059cbb), out _, out _);
                resolver.TryResolve(ChainId, in router, in CodeV1, new FunctionSelector(0xdeadbeef), out _, out _);
            }
        }

        Run();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            Run();
        }

        Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.AreEqual(16 * 101, found);
    }
}
