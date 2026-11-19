using System.Security.Cryptography;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Selector derivation against INDEPENDENT sources. Rows marked "published" come from the Solidity ABI documentation and the
/// ERC-20 / Uniswap interfaces. Rows marked "cast+openssl" were computed on this machine with Foundry <c>cast sig</c> (1.9.0) and,
/// separately, <c>openssl dgst -provider default -keccak-256</c>; both tools agreed on every row.
/// </summary>
[TestClass]
public sealed class ABISelectorTests
{
    public static IEnumerable<object[]> SelectorVectors =>
    [
        // published: Solidity ABI specification examples and ERC-20 / multicall interfaces
        ["transfer(address,uint256)", 0xa9059cbbu],
        ["approve(address,uint256)", 0x095ea7b3u],
        ["balanceOf(address)", 0x70a08231u],
        ["multicall(bytes[])", 0xac9650d8u],
        ["baz(uint32,bool)", 0xcdcd77c0u],
        ["bar(bytes3[2])", 0xfce353f6u],
        ["sam(bytes,bool,uint256[])", 0xa5643bf2u],
        ["f(uint256,uint32[],bytes10,bytes)", 0x8be65246u],
        ["g(uint256[][],string[])", 0x2289b18cu],
        // cast+openssl: ERC-20/721 and router functions, a tuple, aliases expanded, odd identifiers, every elementary kind
        ["transferFrom(address,address,uint256)", 0x23b872ddu],
        ["totalSupply()", 0x18160dddu],
        ["setApprovalForAll(address,bool)", 0xa22cb465u],
        ["swapExactTokensForTokens(uint256,uint256,address[],address,uint256)", 0x38ed1739u],
        ["exactInputSingle((address,address,uint24,address,uint256,uint256,uint160))", 0x04e45aafu],
        ["h((uint256,bytes)[],function,fixed128x18,ufixed128x18,int256[2][])", 0xe0bdaa1cu],
        ["_a$b(uint256)", 0x14293c4fu],
        ["f()", 0x26121ff0u],
        ["m(uint8,int8,bytes1,bytes32,uint248,int256[2][3])", 0x08bab07eu],
        ["n(fixed8x1,ufixed256x80)", 0xfdc73c48u],
        // cast+openssl: signatures straddling the 136-byte Keccak rate (135, 136 and 137 characters), exercising block padding
        ["f" + new string('a', 125) + "(uint256)", 0xd1336efeu],
        ["f" + new string('a', 126) + "(uint256)", 0xd1b271eau],
        ["f" + new string('a', 127) + "(uint256)", 0x6dfb3c01u],
    ];

    [TestMethod]
    [DynamicData(nameof(SelectorVectors))]
    public void SelectorOfCanonicalSignature_MatchesTheIndependentValue(string signature, uint expected)
    {
        Assert.AreEqual(expected, FunctionSelector.FromSignature(signature).Value, signature);
    }

    [TestMethod]
    [DynamicData(nameof(SelectorVectors))]
    public void CompiledFunction_HasTheIndependentSelector_AndKeepsTheCanonicalSignature(string signature, uint expected)
    {
        Assert.IsTrue(
            ABISchemaCompiler.TryCompileFunctionSignature(signature, ABICompileLimits.Default, out var function, out var error),
            error.ToString());

        Assert.AreEqual(expected, function.Selector, signature);
        Assert.AreEqual(signature, function.CanonicalSignature);
        Assert.AreEqual(signature, function.ToString());
        Assert.AreEqual(signature.Substring(0, signature.IndexOf('(')), function.Name);
        Assert.AreEqual(function.InputSchema.CanonicalType, signature.Substring(signature.IndexOf('(')));
    }

    [TestMethod]
    public void Selector_IsKeccak256_NotSha3_256()
    {
        const string signature = "transfer(address,uint256)";

        // Independent: NIST SHA3-256 of this text begins 4b40e901 (python3 hashlib.sha3_256 and openssl -sha3-256 agree).
        const uint nistSha3Prefix = 0x4b40e901u;
        Assert.AreNotEqual(nistSha3Prefix, FunctionSelector.FromSignature(signature).Value, "selector must not be SHA3-256");
        Assert.AreEqual(0xa9059cbbu, FunctionSelector.FromSignature(signature).Value);

        if (SHA3_256.IsSupported)
        {
            byte[] sha3 = SHA3_256.HashData(System.Text.Encoding.ASCII.GetBytes(signature));
            Assert.AreEqual(nistSha3Prefix, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(sha3));
            Assert.AreNotEqual(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(sha3), FunctionSelector.FromSignature(signature).Value);
        }
    }

    [TestMethod]
    public void AliasesAreNormalised_SoSelectorAndCanonicalSignatureMatchTheExpandedForm()
    {
        Assert.IsTrue(ABISchemaCompiler.TryCompileFunctionSignature("transfer(address,uint)", ABICompileLimits.Default, out var alias, out _));
        Assert.AreEqual("transfer(address,uint256)", alias.CanonicalSignature);
        Assert.AreEqual(0xa9059cbbu, alias.Selector);

        Assert.IsTrue(ABISchemaCompiler.TryCompileFunctionSignature(
            "h((uint256,bytes)[],function,fixed,ufixed,int[2][])", ABICompileLimits.Default, out var h, out _));
        Assert.AreEqual("h((uint256,bytes)[],function,fixed128x18,ufixed128x18,int256[2][])", h.CanonicalSignature);
        Assert.AreEqual(0xe0bdaa1cu, h.Selector);
    }

    [TestMethod]
    public void FunctionSchemas_HaveNoPublicConstructor_SoTheSelectorIsNeverCallerSupplied()
    {
        Assert.AreEqual(0, typeof(ABIFunctionSchema).GetConstructors().Length);
        Assert.AreEqual(0, typeof(ABISchema).GetConstructors().Length);
        Assert.IsNull(typeof(ABIFunctionSchema).GetProperty(nameof(ABIFunctionSchema.Selector))!.SetMethod, "no setter");

        // The compiler entry points take no selector parameter at all.
        foreach (var method in typeof(ABISchemaCompiler).GetMethods().Where(m => m.Name.StartsWith("TryCompileFunction", StringComparison.Ordinal)))
        {
            Assert.IsFalse(
                method.GetParameters().Any(p => p.ParameterType == typeof(uint) || p.ParameterType == typeof(FunctionSelector)),
                method.Name);
        }
    }

    [TestMethod]
    public void TwoSignaturesWithOneSelectorPrefixAreIndependentObjects()
    {
        // Collisions are the registry's problem; the compiler must simply derive each selector from its own signature.
        Assert.IsTrue(ABISchemaCompiler.TryCompileFunctionSignature("a(uint256)", ABICompileLimits.Default, out var a, out _));
        Assert.IsTrue(ABISchemaCompiler.TryCompileFunctionSignature("b(uint256)", ABICompileLimits.Default, out var b, out _));
        Assert.AreNotEqual(a.Selector, b.Selector);
        Assert.AreEqual(FunctionSelector.FromSignature("a(uint256)").Value, a.Selector);
        Assert.AreEqual(FunctionSelector.FromSignature("b(uint256)").Value, b.Selector);
    }

    [TestMethod]
    public void ARealFourByteCollision_CompilesToTwoDifferentSchemasWithOneSelector_AndEitherSchemaDecodesTheSameCall()
    {
        // burn(uint256) and collate_propagate_storage(bytes16) are a well-known genuine collision (both 0x42966c68). The expected value is independent:
        // Foundry `cast sig 'burn(uint256)'` and `cast sig 'collate_propagate_storage(bytes16)'` both print 0x42966c68.
        const uint Collision = 0x42966c68u;
        ABIFunctionSchema burn = ABIDecodeTestSupport.Function("burn(uint256)");
        ABIFunctionSchema collate = ABIDecodeTestSupport.Function("collate_propagate_storage(bytes16)");
        Assert.AreEqual(Collision, burn.Selector);
        Assert.AreEqual(Collision, collate.Selector);
        Assert.AreEqual(burn.FunctionSelector, collate.FunctionSelector);
        Assert.AreEqual(FunctionSelector.FromSignature("burn(uint256)"), burn.FunctionSelector);
        Assert.AreNotEqual(burn.CanonicalSignature, collate.CanonicalSignature);
        Assert.AreNotEqual(burn.InputSchema.CanonicalType, collate.InputSchema.CanonicalType);

        // The compiler does not detect or hide the collision (that is the registry's job, spec 4.2): both compile independently, and a call whose
        // word is a valid uint256 AND a valid bytes16 decodes under either schema, so decoding success never resolves the function.
        byte[] call = [.. new byte[] { 0x42, 0x96, 0x6c, 0x68 }, .. new byte[15], 0x05, .. new byte[16]];
        var limits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABICallData.TryDecode(call, burn, in limits, out ABICallData asBurn, out ABIStatus status), status.ToString());
        Assert.IsTrue(ABICallData.TryDecode(call, collate, in limits, out ABICallData asCollate, out status), status.ToString());
        Assert.IsTrue(asBurn.Arguments.TryGetUInt256Word([0], out ReadOnlySpan<byte> word, out _));
        Assert.IsTrue(asCollate.Arguments.TryGetFixedBytes([0], out ReadOnlySpan<byte> sixteen, out _));
        Assert.IsTrue(word[..16].SequenceEqual(sixteen), "the same bytes, two different meanings");
        Assert.AreEqual(16, sixteen.Length);
    }

    [TestMethod]
    public void TheFunctionSelectorProperties_AreTheSelectorOfTheCanonicalSignature_AndOfTheDecodedCall()
    {
        foreach (string signature in new[] { "transfer(address,uint256)", "approve(address,uint256)", "multicall(bytes[])", "noArguments()", "f(uint8,int8,address)" })
        {
            ABIFunctionSchema f = ABIDecodeTestSupport.Function(signature);
            Assert.AreEqual(FunctionSelector.FromSignature(f.CanonicalSignature), f.FunctionSelector, signature);
            Assert.AreEqual(f.FunctionSelector.Value, f.Selector, signature);
        }

        ABIFunctionSchema transfer = ABIDecodeTestSupport.Function("transfer(address,uint256)");
        Assert.AreEqual(new FunctionSelector(0xa9059cbbu), transfer.FunctionSelector, "independent constant");
        byte[] call = [.. new byte[] { 0xa9, 0x05, 0x9c, 0xbb }, .. new byte[64]];
        var limits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABICallData.TryDecode(call, transfer, in limits, out ABICallData decoded, out _));
        Assert.AreEqual(transfer.FunctionSelector, decoded.FunctionSelector);
        Assert.AreEqual(decoded.Selector, decoded.FunctionSelector.Value);
        ABICallData invalid = default;
        Assert.AreEqual(default, invalid.FunctionSelector, "an invalid view has the zero selector");
    }

    // ------------------------------------------------------------------ function names

    [TestMethod]
    [DataRow("transfer")]
    [DataRow("_")]
    [DataRow("$")]
    [DataRow("a1")]
    [DataRow("_a$b")]
    [DataRow("X")]
    public void ValidFunctionNames_Compile(string name)
    {
        Assert.IsTrue(ABISchemaCompiler.TryCompileFunction(name, new ABITupleDefinition(), ABICompileLimits.Default, out var function, out var error), error.ToString());
        Assert.AreEqual(name + "()", function.CanonicalSignature);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("1a")]
    [DataRow("a b")]
    [DataRow("a-b")]
    [DataRow("a(")]
    [DataRow("é")]
    [DataRow("a\u0000")]
    [DataRow(" transfer")]
    public void InvalidFunctionNames_AreRejected(string name)
    {
        Assert.IsFalse(ABISchemaCompiler.TryCompileFunction(name, new ABITupleDefinition(), ABICompileLimits.Default, out var function, out var error));
        Assert.IsNull(function);
        Assert.AreEqual(ABISchemaErrorReason.InvalidFunctionName, error.Reason);
        Assert.AreEqual(ABIStatus.InvalidSchema, error.Status);
    }

    [TestMethod]
    public void NullNameOrArguments_AreRejectedWithoutThrowing()
    {
        Assert.IsFalse(ABISchemaCompiler.TryCompileFunction(null!, new ABITupleDefinition(), ABICompileLimits.Default, out var f1, out var e1));
        Assert.IsNull(f1);
        Assert.AreEqual(ABIStatus.InvalidArgument, e1.Status);
        Assert.AreEqual(ABISchemaErrorReason.NullArgument, e1.Reason);

        Assert.IsFalse(ABISchemaCompiler.TryCompileFunction("f", null!, ABICompileLimits.Default, out var f2, out var e2));
        Assert.IsNull(f2);
        Assert.AreEqual(ABISchemaErrorReason.NullArgument, e2.Reason);

        Assert.IsFalse(ABISchemaCompiler.TryCompileFunction("f", new ABITupleDefinition(), default(ABICompileLimits), out var f3, out var e3));
        Assert.IsNull(f3);
        Assert.AreEqual(ABISchemaErrorReason.InvalidLimits, e3.Reason);
    }

    [TestMethod]
    public void FunctionWithAFailingArgumentSchema_ReportsTheComponentPath()
    {
        var args = new ABITupleDefinition(ABITypeDefinition.Address, ABITypeDefinition.UInt(7));
        Assert.IsFalse(ABISchemaCompiler.TryCompileFunction("f", args, ABICompileLimits.Default, out var function, out var error));
        Assert.IsNull(function);
        Assert.AreEqual("$/1", error.Path);
        Assert.AreEqual(ABISchemaErrorReason.InvalidWidth, error.Reason);
    }

    [TestMethod]
    public void ZeroArgumentFunction_HasTheSelectorOfEmptyParentheses()
    {
        var schema = CompileTuple("()");
        Assert.AreEqual(0, schema.ArgumentCount);
        Assert.IsTrue(ABISchemaCompiler.TryCompileFunctionSignature("totalSupply()", ABICompileLimits.Default, out var function, out _));
        Assert.AreEqual(0x18160dddu, function.Selector);
        Assert.AreEqual(0, function.InputSchema.ArgumentCount);
        Assert.IsFalse(function.InputSchema.IsDynamic);
    }
}
