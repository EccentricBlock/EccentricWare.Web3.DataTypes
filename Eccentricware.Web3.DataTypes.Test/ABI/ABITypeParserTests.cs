using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>Canonical type text: aliases, array suffix order, round trips and malformed grammar.</summary>
[TestClass]
public sealed class ABITypeParserTests
{
    // ------------------------------------------------------------------ elementary types and aliases

    [TestMethod]
    [DataRow("uint", ABITypeKind.UInt, 256, 0)]
    [DataRow("uint8", ABITypeKind.UInt, 8, 0)]
    [DataRow("uint256", ABITypeKind.UInt, 256, 0)]
    [DataRow("int", ABITypeKind.Int, 256, 0)]
    [DataRow("int64", ABITypeKind.Int, 64, 0)]
    [DataRow("bytes1", ABITypeKind.FixedBytes, 1, 0)]
    [DataRow("bytes32", ABITypeKind.FixedBytes, 32, 0)]
    [DataRow("fixed", ABITypeKind.Fixed, 128, 18)]
    [DataRow("ufixed", ABITypeKind.UFixed, 128, 18)]
    [DataRow("fixed8x1", ABITypeKind.Fixed, 8, 1)]
    [DataRow("ufixed256x80", ABITypeKind.UFixed, 256, 80)]
    [DataRow("address", ABITypeKind.Address, 0, 0)]
    [DataRow("bool", ABITypeKind.Bool, 0, 0)]
    [DataRow("function", ABITypeKind.Function, 0, 0)]
    [DataRow("bytes", ABITypeKind.Bytes, 0, 0)]
    [DataRow("string", ABITypeKind.String, 0, 0)]
    public void ElementaryTypes_ParseWithAliasesExpanded(string text, ABITypeKind kind, int width, int scale)
    {
        Assert.IsTrue(ABITypeDefinition.TryParse(text, out var type, out var error), error.ToString());
        Assert.AreEqual(kind, type.Kind);
        Assert.AreEqual(width, type.Width);
        Assert.AreEqual(scale, type.Scale);
        Assert.IsNull(type.Element);
        Assert.IsNull(type.TupleDefinition);
        Assert.AreEqual(ABISchemaError.None, error);
    }

    [TestMethod]
    public void ArraySuffixes_ApplyLeftToRight_LikeSolidity()
    {
        // uint256[2][] is a dynamic array whose elements are uint256[2]; uint256[][2] is a 2-array of dynamic arrays.
        Assert.IsTrue(ABITypeDefinition.TryParse("uint256[2][]", out var a, out _));
        Assert.AreEqual(ABITypeKind.DynamicArray, a.Kind);
        Assert.AreEqual(ABITypeKind.FixedArray, a.Element!.Kind);
        Assert.AreEqual(2, a.Element.Length);
        Assert.AreEqual(ABITypeKind.UInt, a.Element.Element!.Kind);

        Assert.IsTrue(ABITypeDefinition.TryParse("uint256[][2]", out var b, out _));
        Assert.AreEqual(ABITypeKind.FixedArray, b.Kind);
        Assert.AreEqual(2, b.Length);
        Assert.AreEqual(ABITypeKind.DynamicArray, b.Element!.Kind);
    }

    [TestMethod]
    public void TupleType_ParsesToAComponentList()
    {
        Assert.IsTrue(ABITypeDefinition.TryParse("(address,uint256)[]", out var type, out _));
        Assert.AreEqual(ABITypeKind.DynamicArray, type.Kind);
        var tuple = type.Element!.TupleDefinition!;
        Assert.AreEqual(2, tuple.Count);
        Assert.AreEqual(ABITypeKind.Address, tuple[0].Kind);
        Assert.AreEqual(256, tuple[1].Width);
    }

    // ------------------------------------------------------------------ canonical round trips (parse -> compile -> text)

    [TestMethod]
    [DataRow("()", "()")]
    [DataRow("(address,uint256)", "(address,uint256)")]
    [DataRow("(address,uint)", "(address,uint256)")]
    [DataRow("(int,fixed,ufixed)", "(int256,fixed128x18,ufixed128x18)")]
    [DataRow("(bytes3[2])", "(bytes3[2])")]
    [DataRow("((address,uint256)[])", "((address,uint256)[])")]
    [DataRow("(uint)", "(uint256)")]
    [DataRow("(uint[][3],string[])", "(uint256[][3],string[])")]
    [DataRow("(((uint8,bool),bytes),())", "(((uint8,bool),bytes),())")]
    [DataRow("(function,bytes32,int8,uint248)", "(function,bytes32,int8,uint248)")]
    [DataRow("(uint256[2][3])", "(uint256[2][3])")]
    public void CanonicalType_RoundTripsAndNormalisesAliases(string text, string canonical)
    {
        var schema = CompileTuple(text);
        Assert.AreEqual(canonical, schema.CanonicalType);
        Assert.AreEqual(canonical, schema.ToString());

        // Canonical text is a fixed point: compiling it again yields the same text.
        Assert.AreEqual(canonical, CompileTuple(canonical).CanonicalType);
        StringAssert.DoesNotMatch(canonical, new System.Text.RegularExpressions.Regex("tuple"));
    }

    [TestMethod]
    public void FactoryBuiltDefinition_ProducesTheSameCanonicalTextAsParsedText()
    {
        var factory = new ABITupleDefinition(
            ABITypeDefinition.DynamicArray(ABITypeDefinition.Tuple(new ABITupleDefinition(ABITypeDefinition.Address, ABITypeDefinition.UInt(256)))),
            ABITypeDefinition.FixedArray(ABITypeDefinition.FixedBytes(3), 2),
            ABITypeDefinition.Fixed(128, 18),
            ABITypeDefinition.String);
        Assert.IsTrue(ABISchemaCompiler.TryCompileArguments(factory, ABICompileLimits.Default, out var schema, out var error), error.ToString());
        Assert.AreEqual("((address,uint256)[],bytes3[2],fixed128x18,string)", schema.CanonicalType);
        Assert.AreEqual(CompileTuple("((address,uint256)[],bytes3[2],fixed,string)").CanonicalType, schema.CanonicalType);
    }

    // ------------------------------------------------------------------ function signatures

    [TestMethod]
    public void FunctionSignature_ParsesNameAndArguments()
    {
        Assert.IsTrue(ABISchemaCompiler.TryCompileFunctionSignature("multicall(bytes[])", ABICompileLimits.Default, out var f, out var error), error.ToString());
        Assert.AreEqual("multicall", f.Name);
        Assert.AreEqual("(bytes[])", f.InputSchema.CanonicalType);
        Assert.AreEqual(1, f.InputSchema.ArgumentCount);
        Assert.IsTrue(f.InputSchema.IsDynamic);
    }

    // ------------------------------------------------------------------ malformed text

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("uint256 ")]
    [DataRow(" uint256")]
    [DataRow("uint 256")]
    [DataRow("uint256,uint256")]
    [DataRow("tuple(uint256)")]
    [DataRow("(uint256,)")]
    [DataRow("(,uint256)")]
    [DataRow("(uint256,,bool)")]
    [DataRow("(uint256")]
    [DataRow("uint256)")]
    [DataRow("(uint256))")]
    [DataRow("((uint256)")]
    [DataRow("uint256[")]
    [DataRow("uint256]")]
    [DataRow("uint256[]]")]
    [DataRow("uint256[1")]
    [DataRow("uint256[-1]")]
    [DataRow("uint256[+1]")]
    [DataRow("uint256[01]")]
    [DataRow("uint256[0x2]")]
    [DataRow("uint256[ 2]")]
    [DataRow("uint256[a]")]
    [DataRow("[2]")]
    [DataRow("[]")]
    [DataRow("uint08")]
    [DataRow("uint+8")]
    [DataRow("uintx")]
    [DataRow("uint8x")]
    [DataRow("int08")]
    [DataRow("bytes08")]
    [DataRow("bytesx")]
    [DataRow("fixed128")]
    [DataRow("fixed128x")]
    [DataRow("fixedx18")]
    [DataRow("fixed128x18x2")]
    [DataRow("ufixed128")]
    [DataRow("fixed 128x18")]
    [DataRow("fixed128X18")]
    [DataRow("Uint256")]
    [DataRow("UINT256")]
    [DataRow("address payable")]
    [DataRow("addres")]
    [DataRow("interface")]
    [DataRow("uint256\u0000")]
    [DataRow("uint١")]
    [DataRow("uint256[٢]")]
    [DataRow("(uint256)x")]
    [DataRow("(uint256)(bool)")]
    public void MalformedTypeText_IsRejectedWithAReason(string text)
    {
        Assert.IsFalse(ABITypeDefinition.TryParse(text, out var type, out var error), $"'{text}' should not parse");
        Assert.IsNull(type);
        Assert.AreNotEqual(ABIStatus.Success, error.Status);
        Assert.AreNotEqual(ABISchemaErrorReason.None, error.Reason);
        Assert.IsTrue(error.TextPosition >= 0 && error.TextPosition <= text.Length, $"position {error.TextPosition} for '{text}'");
        Assert.AreEqual(string.Empty, error.Path);
    }

    [TestMethod]
    [DataRow("uint256")]
    [DataRow("(uint256)[]")]
    [DataRow("")]
    [DataRow("(uint256")]
    [DataRow("(uint256)x")]
    public void NonTupleOrMalformedText_IsRejectedByTupleParse(string text)
    {
        Assert.IsFalse(ABITupleDefinition.TryParse(text, out var tuple, out var error));
        Assert.IsNull(tuple);
        Assert.AreNotEqual(ABISchemaErrorReason.None, error.Reason);
    }

    [TestMethod]
    public void NullText_IsRejectedWithoutThrowing()
    {
        Assert.IsFalse(ABITypeDefinition.TryParse(null, out var type, out var e1));
        Assert.IsNull(type);
        Assert.AreEqual(ABISchemaErrorReason.NullArgument, e1.Reason);
        Assert.IsFalse(ABITupleDefinition.TryParse(null, out var tuple, out var e2));
        Assert.IsNull(tuple);
        Assert.AreEqual(ABISchemaErrorReason.NullArgument, e2.Reason);
        Assert.IsFalse(ABISchemaCompiler.TryCompileFunctionSignature(null!, ABICompileLimits.Default, out var f, out var e3));
        Assert.IsNull(f);
        Assert.AreEqual(ABISchemaErrorReason.NullArgument, e3.Reason);
    }

    [TestMethod]
    [DataRow("transfer", ABISchemaErrorReason.MalformedSyntax)]
    [DataRow("(address,uint256)", ABISchemaErrorReason.InvalidFunctionName)]
    [DataRow("1transfer(address)", ABISchemaErrorReason.InvalidFunctionName)]
    [DataRow("transfer (address)", ABISchemaErrorReason.InvalidFunctionName)]
    [DataRow("transfer(address,uint256) ", ABISchemaErrorReason.MalformedSyntax)]
    [DataRow("transfer(address,uint256)x", ABISchemaErrorReason.MalformedSyntax)]
    [DataRow("transfer(address to,uint256 amount)", ABISchemaErrorReason.MalformedSyntax)]
    [DataRow("transfer(address,uint256", ABISchemaErrorReason.MalformedSyntax)]
    [DataRow("transfer(address,)", ABISchemaErrorReason.MalformedSyntax)]
    [DataRow("transfer((address,uint256)", ABISchemaErrorReason.MalformedSyntax)]
    [DataRow("transfer(tuple(address))", ABISchemaErrorReason.UnknownType)]
    [DataRow("transfer(foo)", ABISchemaErrorReason.UnknownType)]
    [DataRow("transfer(uint256)(uint256)", ABISchemaErrorReason.MalformedSyntax)]
    public void MalformedSignature_IsRejected(string signature, ABISchemaErrorReason reason)
    {
        Assert.IsFalse(ABISchemaCompiler.TryCompileFunctionSignature(signature, ABICompileLimits.Default, out var function, out var error));
        Assert.IsNull(function);
        Assert.AreEqual(reason, error.Reason, signature);
    }

    [TestMethod]
    public void ArrayLengthOverflow_IsAnOverflowNotAMalformedNumber()
    {
        Assert.IsTrue(ABITypeDefinition.TryParse("uint8[2147483647]", out _, out _), "int.MaxValue parses (the compiler limits it)");
        Assert.IsFalse(ABITypeDefinition.TryParse("uint8[2147483648]", out _, out var error));
        Assert.AreEqual(ABIStatus.ArithmeticOverflow, error.Status);
        Assert.AreEqual(ABISchemaErrorReason.SizeOverflow, error.Reason);

        Assert.IsFalse(ABITypeDefinition.TryParse("uint8[99999999999999999999999999]", out _, out error));
        Assert.AreEqual(ABISchemaErrorReason.SizeOverflow, error.Reason);
    }

    [TestMethod]
    public void WidthOverflow_IsRejected()
    {
        Assert.IsFalse(ABITypeDefinition.TryParse("uint99999999999", out _, out var error));
        Assert.AreEqual(ABISchemaErrorReason.InvalidWidth, error.Reason);
        Assert.IsFalse(ABITypeDefinition.TryParse("fixed99999999999x18", out _, out error));
        Assert.AreEqual(ABISchemaErrorReason.InvalidWidth, error.Reason);
        Assert.IsFalse(ABITypeDefinition.TryParse("fixed128x99999999999", out _, out error));
        Assert.AreEqual(ABISchemaErrorReason.InvalidFixedPrecision, error.Reason);
    }

    [TestMethod]
    public void SyntacticallyLegalButSemanticallyIllegalWidths_ParseAndAreRejectedByTheCompiler()
    {
        // One rule, one place: the parser builds definitions, the compiler judges them.
        Assert.IsTrue(ABITypeDefinition.TryParse("uint7", out var t, out _));
        Assert.AreEqual(7, t.Width);
        Assert.IsTrue(ABITypeDefinition.TryParse("bytes33", out t, out _));
        Assert.IsTrue(ABITypeDefinition.TryParse("fixed128x0", out t, out _));
        Assert.IsTrue(ABITypeDefinition.TryParse("uint256[0]", out t, out _));
        Assert.AreEqual(0, t.Length);
    }

    [TestMethod]
    public void HostileNesting_DoesNotOverflowTheStack()
    {
        // 100,000 opening parentheses: recursion is capped at the hard schema depth.
        string text = new string('(', 100_000) + new string(')', 100_000);
        Assert.IsFalse(ABITypeDefinition.TryParse(text, out _, out var error));
        Assert.AreEqual(ABISchemaErrorReason.SchemaTooDeep, error.Reason);
        Assert.AreEqual(ABIStatus.DepthLimitExceeded, error.Status);

        // 32 nested tuples (parser cap) parse; 33 do not.
        Assert.IsTrue(ABITypeDefinition.TryParse(new string('(', 32) + new string(')', 32), out _, out _));
        Assert.IsFalse(ABITypeDefinition.TryParse(new string('(', 33) + new string(')', 33), out _, out _));
    }

    [TestMethod]
    public void LongArraySuffixChain_IsRejectedWithTheSchemaDepthReason_ByTheParserAndByTheCompiler()
    {
        // 20,000 suffixes: the parser stops at its cap (iteratively, no recursion) with the same status and reason the compiler uses for depth.
        string text = "(uint8" + string.Concat(Enumerable.Repeat("[]", 20_000)) + ")";
        Assert.IsFalse(ABITupleDefinition.TryParse(text, out _, out var parseError));
        Assert.AreEqual(ABISchemaErrorReason.SchemaTooDeep, parseError.Reason);
        Assert.AreEqual(ABIStatus.DepthLimitExceeded, parseError.Status);

        // The longest chain the parser accepts reaches the compiler, which rejects it by depth.
        string longest = "(uint8" + string.Concat(Enumerable.Repeat("[]", ABICompileLimits.HardMaxSchemaDepth)) + ")";
        Assert.IsTrue(ABITupleDefinition.TryParse(longest, out var tuple, out _));
        var error = CompileDefinitionError(tuple);
        Assert.AreEqual(ABISchemaErrorReason.SchemaTooDeep, error.Reason);
        Assert.AreEqual(ABIStatus.DepthLimitExceeded, error.Status);
    }

    [TestMethod]
    public void ArbitraryText_NeverThrows_FromParserOrCompiler()
    {
        var random = new Random(20260617);
        const string alphabet = "()[],0123456789uintbyeslfxdrcoaplg $_\u0000é";
        string[] words = ["uint", "int", "bytes", "fixed", "ufixed", "address", "bool", "string", "function", "tuple", "x", "256", "8", "[]", "[2]", "(", ")", ","];
        for (int i = 0; i < 20_000; i++)
        {
            var builder = new System.Text.StringBuilder();
            int pieces = random.Next(0, 14);
            for (int p = 0; p < pieces; p++)
            {
                if (random.Next(3) == 0)
                {
                    builder.Append(alphabet[random.Next(alphabet.Length)]);
                }
                else
                {
                    builder.Append(words[random.Next(words.Length)]);
                }
            }

            string text = builder.ToString();
            _ = ABITypeDefinition.TryParse(text, out _, out _);
            _ = ABITupleDefinition.TryParse(text, out _, out _);
            _ = ABISchemaCompiler.TryCompileFunctionSignature("f" + text, ABICompileLimits.Default, out var function, out var error);
            Assert.AreEqual(function is not null, error.Status == ABIStatus.Success, text);
        }
    }

    [TestMethod]
    public void ArraySuffixes_AreCappedAtTheHardSchemaDepth_SoTextLengthCannotDriveAllocation()
    {
        // Up to HardMaxSchemaDepth (32) suffixes still parse (the compiler then applies its own depth rule); one more is refused by the parser.
        string Suffixes(int count) => "(uint8" + string.Concat(Enumerable.Repeat("[]", count)) + ")";
        Assert.IsTrue(ABITupleDefinition.TryParse(Suffixes(ABICompileLimits.HardMaxSchemaDepth), out _, out var accepted), accepted.ToString());

        Assert.IsFalse(ABITupleDefinition.TryParse(Suffixes(ABICompileLimits.HardMaxSchemaDepth + 1), out var none, out var error));
        Assert.IsNull(none);
        Assert.AreEqual(ABIStatus.DepthLimitExceeded, error.Status);
        Assert.AreEqual(ABISchemaErrorReason.SchemaTooDeep, error.Reason);

        // Fixed-length suffixes count too.
        Assert.IsFalse(ABITupleDefinition.TryParse("(uint8" + string.Concat(Enumerable.Repeat("[2]", 40)) + ")", out _, out error));
        Assert.AreEqual(ABIStatus.DepthLimitExceeded, error.Status);

        // Nothing deeper than the cap can compile anyway: the compiler agrees for the largest accepted text.
        Assert.IsTrue(ABITupleDefinition.TryParse(Suffixes(ABICompileLimits.HardMaxSchemaDepth), out var definition, out _));
        var limits = ABICompileLimits.Default;
        Assert.IsFalse(ABISchemaCompiler.TryCompileArguments(definition!, in limits, out _, out var compileError));
        Assert.AreEqual(ABIStatus.DepthLimitExceeded, compileError.Status);
    }

    [TestMethod]
    [DoNotParallelize]
    public void HostileSuffixText_IsRejectedWithBoundedAllocation_NotProportionalToItsLength()
    {
        string hostile = "(uint8" + string.Concat(Enumerable.Repeat("[]", 4_000_000)) + ")"; // built outside the measured region
        ABITupleDefinition.TryParse("(uint8[])", out _, out _); // warm-up
        ABITupleDefinition.TryParse(hostile, out _, out _);

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool ok = ABITupleDefinition.TryParse(hostile, out var definition, out var error);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsFalse(ok);
        Assert.IsNull(definition);
        Assert.AreEqual(ABIStatus.DepthLimitExceeded, error.Status);
        Assert.IsTrue(allocated < 16 * 1024, $"parsing 4M array suffixes allocated {allocated} bytes (the cap should stop it after 33)");
    }
}
