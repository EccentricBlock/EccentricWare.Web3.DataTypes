using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>Every rejection class of spec 4.1: grammar, widths, scales, T[0], cycles, overflow and each compile limit at limit-1, limit and limit+1.</summary>
[TestClass]
public sealed class ABISchemaRejectionTests
{
    private static void AssertRejected(ABISchemaError error, ABIStatus status, ABISchemaErrorReason reason, string path)
    {
        Assert.AreEqual(status, error.Status, error.ToString());
        Assert.AreEqual(reason, error.Reason, error.ToString());
        Assert.AreEqual(path, error.Path, error.ToString());
        Assert.AreEqual(-1, error.TextPosition);
    }

    // ------------------------------------------------------------------ widths, lengths, precision

    [TestMethod]
    [DataRow("uint0")]
    [DataRow("uint7")]
    [DataRow("uint9")]
    [DataRow("uint255")]
    [DataRow("uint257")]
    [DataRow("uint264")]
    [DataRow("uint512")]
    [DataRow("int0")]
    [DataRow("int7")]
    [DataRow("int12")]
    [DataRow("int264")]
    public void IntegerWidth_NotAMultipleOfEightInRange_IsInvalidSchema(string type)
    {
        var error = CompileTupleError($"(bool,{type})");
        AssertRejected(error, ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidWidth, "$/1");
    }

    [TestMethod]
    [DataRow(8), DataRow(16), DataRow(24), DataRow(128), DataRow(248), DataRow(256)]
    public void IntegerWidth_EveryLegalMultipleOfEight_Compiles(int bits)
    {
        Assert.AreEqual(bits, CompileTuple($"(uint{bits},int{bits})").GetNode(1).Width);
    }

    [TestMethod]
    public void IntegerWidth_AllWidthsFromZeroTo300_AreAcceptedExactlyWhenLegal()
    {
        for (int bits = -8; bits <= 300; bits++)
        {
            var definition = new ABITupleDefinition(ABITypeDefinition.UInt(bits), ABITypeDefinition.Int(bits));
            bool legal = bits >= 8 && bits <= 256 && bits % 8 == 0;
            Assert.AreEqual(legal, ABISchemaCompiler.TryCompileArguments(definition, ABICompileLimits.Default, out _, out var error), $"width {bits}");
            if (!legal)
            {
                AssertRejected(error, ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidWidth, "$/0");
            }
        }
    }

    [TestMethod]
    [DataRow(0), DataRow(33), DataRow(34), DataRow(64), DataRow(-1), DataRow(int.MinValue), DataRow(int.MaxValue)]
    public void FixedBytesLength_OutsideOneToThirtyTwo_IsRejected(int length)
    {
        var definition = new ABITupleDefinition(ABITypeDefinition.FixedBytes(length));
        AssertRejected(CompileDefinitionError(definition), ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidFixedBytesLength, "$/0");
    }

    [TestMethod]
    public void FixedBytesLength_Bytes33_FromText_IsRejected()
    {
        AssertRejected(CompileTupleError("(bytes33)"), ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidFixedBytesLength, "$/0");
        AssertRejected(CompileTupleError("(bytes0)"), ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidFixedBytesLength, "$/0");
    }

    [TestMethod]
    public void FixedPrecision_OutsideOneToEighty_IsRejected_AndTheEdgesAreAccepted()
    {
        AssertRejected(CompileTupleError("(fixed128x0)"), ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidFixedPrecision, "$/0");
        AssertRejected(CompileTupleError("(fixed128x81)"), ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidFixedPrecision, "$/0");
        AssertRejected(CompileTupleError("(ufixed128x0)"), ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidFixedPrecision, "$/0");
        AssertRejected(CompileTupleError("(ufixed128x81)"), ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidFixedPrecision, "$/0");
        AssertRejected(
            CompileDefinitionError(new ABITupleDefinition(ABITypeDefinition.Fixed(128, -1))),
            ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidFixedPrecision, "$/0");

        Assert.AreEqual(1, CompileTuple("(fixed128x1)").GetNode(1).Scale);
        Assert.AreEqual(80, CompileTuple("(ufixed128x80)").GetNode(1).Scale);
    }

    [TestMethod]
    public void FixedPoint_WithAnIllegalWidth_IsRejectedForTheWidthFirst()
    {
        AssertRejected(CompileTupleError("(fixed7x18)"), ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidWidth, "$/0");
        AssertRejected(CompileTupleError("(ufixed264x18)"), ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidWidth, "$/0");
    }

    // ------------------------------------------------------------------ array lengths

    [TestMethod]
    public void ZeroLengthFixedArray_IsUnsupportedSchema()
    {
        AssertRejected(CompileTupleError("(uint256[0])"), ABIStatus.UnsupportedSchema, ABISchemaErrorReason.ZeroLengthFixedArray, "$/0");
        AssertRejected(CompileTupleError("(bool,uint8[2][0])"), ABIStatus.UnsupportedSchema, ABISchemaErrorReason.ZeroLengthFixedArray, "$/1");
        AssertRejected(CompileTupleError("(uint8[0][])"), ABIStatus.UnsupportedSchema, ABISchemaErrorReason.ZeroLengthFixedArray, "$/0/[]");
        AssertRejected(CompileTupleError("(()[0])"), ABIStatus.UnsupportedSchema, ABISchemaErrorReason.ZeroLengthFixedArray, "$/0");

        var direct = new ABITupleDefinition(ABITypeDefinition.FixedArray(ABITypeDefinition.Bool, 0));
        AssertRejected(CompileDefinitionError(direct), ABIStatus.UnsupportedSchema, ABISchemaErrorReason.ZeroLengthFixedArray, "$/0");
    }

    [TestMethod]
    [DataRow(-1), DataRow(-32), DataRow(int.MinValue)]
    public void NegativeFixedArrayLength_IsInvalidSchema(int length)
    {
        var definition = new ABITupleDefinition(ABITypeDefinition.FixedArray(ABITypeDefinition.Bool, length));
        AssertRejected(CompileDefinitionError(definition), ABIStatus.InvalidSchema, ABISchemaErrorReason.NegativeArrayLength, "$/0");
    }

    // ------------------------------------------------------------------ cycles

    [TestMethod]
    public void DirectCycle_IsRejected()
    {
        var tuple = new ABITupleDefinition();
        tuple.Add(ABITypeDefinition.Tuple(tuple));

        AssertRejected(CompileDefinitionError(tuple), ABIStatus.InvalidSchema, ABISchemaErrorReason.Cycle, "$/0");
    }

    [TestMethod]
    public void IndirectCycle_ThroughTwoTuples_IsRejected()
    {
        var a = new ABITupleDefinition(ABITypeDefinition.UInt(8));
        var b = new ABITupleDefinition(ABITypeDefinition.Tuple(a));
        a.Add(ABITypeDefinition.Tuple(b));

        // root a -> member 1 is b -> member 0 is a again.
        AssertRejected(CompileDefinitionError(a), ABIStatus.InvalidSchema, ABISchemaErrorReason.Cycle, "$/1/0");
    }

    [TestMethod]
    public void CycleThroughArrays_IsRejected()
    {
        var tuple = new ABITupleDefinition(ABITypeDefinition.Bool);
        tuple.Add(ABITypeDefinition.DynamicArray(ABITypeDefinition.FixedArray(ABITypeDefinition.Tuple(tuple), 2)));

        AssertRejected(CompileDefinitionError(tuple), ABIStatus.InvalidSchema, ABISchemaErrorReason.Cycle, "$/1/[]/[]");
    }

    [TestMethod]
    public void Cycle_IsReportedBeforeAnyBudget_EvenWhenThatBudgetIsAlreadyExhausted()
    {
        // The cycle check precedes the depth and node budgets: each of these limits would also fail the schema, but the
        // cycle is the more fundamental defect and must be the reported reason.
        foreach (var limits in new[] { With(depth: 1), With(nodes: 1), With(depth: 2, nodes: 2) })
        {
            var tuple = new ABITupleDefinition();
            tuple.Add(ABITypeDefinition.Tuple(tuple));

            var error = CompileDefinitionError(tuple, limits);
            Assert.AreEqual(ABISchemaErrorReason.Cycle, error.Reason, limits.ToString());
            Assert.AreEqual("$/0", error.Path, limits.ToString());
        }
    }

    // ------------------------------------------------------------------ overflow

    [TestMethod]
    public void StaticSizeOverflow_InNestedArrays_IsRejected()
    {
        // 50,000,000 * 32 = 1.6e9 fits int; times 2 = 3.2e9 does not.
        var limits = With(fixedElements: int.MaxValue, staticBytes: int.MaxValue);

        Assert.IsTrue(ABITupleDefinition.TryParse("(uint256[50000000])", out var fits, out _));
        Assert.IsTrue(ABISchemaCompiler.TryCompileArguments(fits, in limits, out var schema, out _));
        Assert.AreEqual(1_600_000_000, schema.GetNode(1).StaticSize);

        AssertRejected(CompileTupleError("(uint256[50000000][2])", limits), ABIStatus.ArithmeticOverflow, ABISchemaErrorReason.SizeOverflow, "$/0");
        AssertRejected(CompileTupleError("(uint256[100000000])", limits), ABIStatus.ArithmeticOverflow, ABISchemaErrorReason.SizeOverflow, "$/0");
        AssertRejected(CompileTupleError("(uint256[2147483647])", limits), ABIStatus.ArithmeticOverflow, ABISchemaErrorReason.SizeOverflow, "$/0");
    }

    [TestMethod]
    public void HeadSizeOverflow_InATupleSum_IsRejected()
    {
        var limits = With(fixedElements: int.MaxValue, staticBytes: int.MaxValue);

        // Two members of 1.6e9 bytes each: the tuple's head size 3.2e9 overflows int.
        AssertRejected(
            CompileTupleError("(uint256[50000000],uint256[50000000])", limits),
            ABIStatus.ArithmeticOverflow, ABISchemaErrorReason.SizeOverflow, "$");

        // The same overflow one level down names the inner tuple.
        AssertRejected(
            CompileTupleError("(bool,(uint256[50000000],uint256[50000000]))", limits),
            ABIStatus.ArithmeticOverflow, ABISchemaErrorReason.SizeOverflow, "$/1");
    }

    [TestMethod]
    public void HeadOverflow_OfADynamicArrayOfPointers_IsRejected()
    {
        // bytes[k]: k offset words of 32 bytes; 2,147,483,647 * 32 overflows int even though the array is dynamic.
        var limits = With(fixedElements: int.MaxValue, staticBytes: int.MaxValue);
        AssertRejected(CompileTupleError("(bytes[2147483647])", limits), ABIStatus.ArithmeticOverflow, ABISchemaErrorReason.SizeOverflow, "$/0");
    }

    [TestMethod]
    public void ExactlyIntMaxValueStaticSize_IsAcceptedByCheckedArithmetic()
    {
        // 2^31 - 1 is not a multiple of 32, so the largest static size reachable is 2^31 - 32 (uint256[67108863]).
        var limits = With(fixedElements: int.MaxValue, staticBytes: int.MaxValue);
        var schema = CompileTuple("(uint256[67108863])", limits);
        Assert.AreEqual(2_147_483_616, schema.GetNode(1).StaticSize);
        AssertRejected(CompileTupleError("(uint256[67108864])", limits), ABIStatus.ArithmeticOverflow, ABISchemaErrorReason.SizeOverflow, "$/0");
    }

    // ------------------------------------------------------------------ limits at limit-1 / limit / limit+1

    [TestMethod]
    public void SchemaDepthLimit_AtMinusOneAndPlusOne()
    {
        // (((( uint256 )))) : tuples at depths 1..4, the uint256 leaf at depth 5.
        const string text = "((((uint256))))";
        Assert.AreEqual(5, CompileTuple(text).MaxDepth);

        AssertRejected(CompileTupleError(text, With(depth: 4)), ABIStatus.DepthLimitExceeded, ABISchemaErrorReason.SchemaTooDeep, "$/0/0/0/0");
        Assert.AreEqual(5, CompileTuple(text, With(depth: 5)).MaxDepth);
        Assert.AreEqual(5, CompileTuple(text, With(depth: 6)).MaxDepth);
    }

    [TestMethod]
    public void SchemaDepthLimit_OfOne_AllowsOnlyAnEmptyRoot()
    {
        Assert.AreEqual(1, CompileTuple("()", With(depth: 1)).MaxDepth);
        AssertRejected(CompileTupleError("(bool)", With(depth: 1)), ABIStatus.DepthLimitExceeded, ABISchemaErrorReason.SchemaTooDeep, "$/0");
    }

    [TestMethod]
    public void SchemaDepthLimit_AtTheHardCap_LeafAt32Passes_LeafAt33Fails()
    {
        // 31 nested tuples around a leaf puts the leaf at depth 32; 32 nested puts it at depth 33.
        string ok = new string('(', 31) + "bool" + new string(')', 31);
        string tooDeep = new string('(', 32) + "bool" + new string(')', 32);

        Assert.AreEqual(32, CompileTuple(ok, With(depth: 32)).MaxDepth);
        var error = CompileTupleError(tooDeep, With(depth: 32));
        Assert.AreEqual(ABIStatus.DepthLimitExceeded, error.Status);
        Assert.AreEqual(ABISchemaErrorReason.SchemaTooDeep, error.Reason);
        Assert.AreEqual("$" + string.Concat(Enumerable.Repeat("/0", 32)), error.Path);
    }

    [TestMethod]
    public void DeeplyNestedDefinition_WithoutTextParsing_IsRejectedWithoutStackOverflow()
    {
        // 50,000 nested single-member tuples built through the factories.
        var inner = new ABITupleDefinition(ABITypeDefinition.Bool);
        for (int i = 0; i < 50_000; i++)
        {
            inner = new ABITupleDefinition(ABITypeDefinition.Tuple(inner));
        }

        var error = CompileDefinitionError(inner);
        Assert.AreEqual(ABISchemaErrorReason.SchemaTooDeep, error.Reason);
    }

    [TestMethod]
    public void NodeLimit_AtMinusOneAndPlusOne()
    {
        // (uint256,uint256,uint256) has 4 nodes: the root and three leaves.
        const string text = "(uint256,uint256,uint256)";
        AssertRejected(CompileTupleError(text, With(nodes: 3)), ABIStatus.ElementLimitExceeded, ABISchemaErrorReason.TooManySchemaNodes, "$/2");
        Assert.AreEqual(4, CompileTuple(text, With(nodes: 4)).NodeCount);
        Assert.AreEqual(4, CompileTuple(text, With(nodes: 5)).NodeCount);
        AssertRejected(CompileTupleError(text, With(nodes: 1)), ABIStatus.ElementLimitExceeded, ABISchemaErrorReason.TooManySchemaNodes, "$/0");
    }

    [TestMethod]
    public void NodeLimit_CountsArrayElementsAndNestedTuples()
    {
        // ((bool,uint8[])[]) : root, DynArray, tuple, bool, DynArray, uint8 = 6 nodes.
        const string text = "((bool,uint8[])[])";
        Assert.AreEqual(6, CompileTuple(text, With(nodes: 6)).NodeCount);
        AssertRejected(CompileTupleError(text, With(nodes: 5)), ABIStatus.ElementLimitExceeded, ABISchemaErrorReason.TooManySchemaNodes, "$/0/[]/1/[]");
    }

    [TestMethod]
    public void ExponentialDefinitionDag_IsStoppedByTheNodeLimit()
    {
        // Each level references the previous tuple twice: 2^31 expanded nodes. A dynamic leaf keeps head sizes constant, so
        // the node budget is what stops it, long before the expansion is built.
        var level = new ABITupleDefinition(ABITypeDefinition.Bytes);
        for (int i = 0; i < 30; i++)
        {
            level = new ABITupleDefinition(ABITypeDefinition.Tuple(level), ABITypeDefinition.Tuple(level));
        }

        var error = CompileDefinitionError(level, With(nodes: 100_000, depth: 32));
        Assert.AreEqual(ABISchemaErrorReason.TooManySchemaNodes, error.Reason);
    }

    [TestMethod]
    public void ExponentialStaticDefinitionDag_IsStoppedByTheStaticSizeBudget()
    {
        // All-static variant: the encoded size doubles per level (32 * 2^k), so the static budget trips first.
        var level = new ABITupleDefinition(ABITypeDefinition.Bool);
        for (int i = 0; i < 30; i++)
        {
            level = new ABITupleDefinition(ABITypeDefinition.Tuple(level), ABITypeDefinition.Tuple(level));
        }

        var error = CompileDefinitionError(level, With(nodes: 100_000, depth: 32));
        Assert.AreEqual(ABISchemaErrorReason.StaticSizeLimit, error.Reason);
    }

    [TestMethod]
    public void TupleArityLimit_AtMinusOneAndPlusOne()
    {
        const string text = "(bool,bool,bool)";
        AssertRejected(CompileTupleError(text, With(tupleElements: 2)), ABIStatus.ElementLimitExceeded, ABISchemaErrorReason.TupleTooWide, "$");
        Assert.AreEqual(3, CompileTuple(text, With(tupleElements: 3)).ArgumentCount);
        Assert.AreEqual(3, CompileTuple(text, With(tupleElements: 4)).ArgumentCount);
        // The limit applies to nested tuples too.
        AssertRejected(CompileTupleError("(bool,(bool,bool,bool))", With(tupleElements: 2)), ABIStatus.ElementLimitExceeded, ABISchemaErrorReason.TupleTooWide, "$/1");
    }

    [TestMethod]
    public void DefaultTupleArity_AcceptsRealisticLargeStructs_AndRejectsPast1024()
    {
        // Real traffic has structs of a few dozen members; the default must never reject those.
        Assert.AreEqual(300, CompileTuple("(" + string.Join(',', Enumerable.Repeat("uint256", 300)) + ")").ArgumentCount);
        Assert.AreEqual(1024, CompileTuple("(" + string.Join(',', Enumerable.Repeat("bool", 1024)) + ")").ArgumentCount);
        var error = CompileTupleError("(" + string.Join(',', Enumerable.Repeat("bool", 1025)) + ")");
        Assert.AreEqual(ABISchemaErrorReason.TupleTooWide, error.Reason);
    }

    [TestMethod]
    public void FixedArrayLengthLimit_AtMinusOneAndPlusOne()
    {
        const string text = "(uint8[5])";
        AssertRejected(CompileTupleError(text, With(fixedElements: 4)), ABIStatus.ElementLimitExceeded, ABISchemaErrorReason.FixedArrayTooLong, "$/0");
        Assert.AreEqual(5, CompileTuple(text, With(fixedElements: 5)).GetNode(1).Count);
        Assert.AreEqual(5, CompileTuple(text, With(fixedElements: 6)).GetNode(1).Count);
    }

    [TestMethod]
    public void StaticSizeLimit_OnAFixedArray_AtMinusOneAndPlusOne()
    {
        // uint256[3] is 96 bytes.
        const string text = "(uint256[3])";
        AssertRejected(CompileTupleError(text, With(staticBytes: 95)), ABIStatus.ElementLimitExceeded, ABISchemaErrorReason.StaticSizeLimit, "$/0");
        Assert.AreEqual(96, CompileTuple(text, With(staticBytes: 96)).GetNode(1).StaticSize);
        Assert.AreEqual(96, CompileTuple(text, With(staticBytes: 97)).GetNode(1).StaticSize);
    }

    [TestMethod]
    public void StaticSizeLimit_OnATupleHead_AtMinusOneAndPlusOne()
    {
        // (uint256[3],bytes): the array is 96, the root head 96 + 32 = 128. The dynamic tuple's head counts too.
        const string text = "(uint256[3],bytes)";
        AssertRejected(CompileTupleError(text, With(staticBytes: 127)), ABIStatus.ElementLimitExceeded, ABISchemaErrorReason.StaticSizeLimit, "$");
        Assert.AreEqual(128, CompileTuple(text, With(staticBytes: 128)).Root.HeadSize);
        Assert.AreEqual(128, CompileTuple(text, With(staticBytes: 129)).Root.HeadSize);
    }

    [TestMethod]
    public void StaticSizeLimit_OnAStaticTuple_AtMinusOneAndPlusOne()
    {
        // (uint256,address) is 64 bytes.
        AssertRejected(CompileTupleError("(uint256,address)", With(staticBytes: 63)), ABIStatus.ElementLimitExceeded, ABISchemaErrorReason.StaticSizeLimit, "$");
        Assert.AreEqual(64, CompileTuple("(uint256,address)", With(staticBytes: 64)).Root.StaticSize);
        Assert.AreEqual(64, CompileTuple("(uint256,address)", With(staticBytes: 65)).Root.StaticSize);
    }

    [TestMethod]
    public void StaticSizeLimit_OnAPointerTableOfADynamicFixedArray_AtMinusOneAndPlusOne()
    {
        // string[3]: 3 offset words = 96 bytes of head even though the array is dynamic.
        AssertRejected(CompileTupleError("(string[3])", With(staticBytes: 95)), ABIStatus.ElementLimitExceeded, ABISchemaErrorReason.StaticSizeLimit, "$/0");
        Assert.AreEqual(96, CompileTuple("(string[3])", With(staticBytes: 96)).GetNode(1).HeadSize);
    }

    [TestMethod]
    public void StaticSizeLimit_EmptyRoot_NeedsNoBytes()
    {
        Assert.AreEqual(0, CompileTuple("()", With(staticBytes: 1)).Root.StaticSize);
    }

    // ------------------------------------------------------------------ invalid arguments

    [TestMethod]
    public void NullDefinition_IsInvalidArgument()
    {
        Assert.IsFalse(ABISchemaCompiler.TryCompileArguments(null!, ABICompileLimits.Default, out var schema, out var error));
        Assert.IsNull(schema);
        Assert.AreEqual(ABIStatus.InvalidArgument, error.Status);
        Assert.AreEqual(ABISchemaErrorReason.NullArgument, error.Reason);
    }

    [TestMethod]
    public void DefaultLimits_AreRejectedAsInvalid()
    {
        var definition = new ABITupleDefinition(ABITypeDefinition.Bool);
        Assert.IsFalse(ABISchemaCompiler.TryCompileArguments(definition, default(ABICompileLimits), out var schema, out var error));
        Assert.IsNull(schema);
        Assert.AreEqual(ABIStatus.InvalidArgument, error.Status);
        Assert.AreEqual(ABISchemaErrorReason.InvalidLimits, error.Reason);
    }

    [TestMethod]
    public void EveryInvalidLimitField_IsRejected_AndNeverReachesCompilation()
    {
        var definition = new ABITupleDefinition(ABITypeDefinition.Bool);
        var d = ABICompileLimits.Default;
        ABICompileLimits[] invalid =
        [
            d with { MaxSchemaNodes = 0 }, d with { MaxSchemaNodes = -1 },
            d with { MaxSchemaDepth = 0 }, d with { MaxSchemaDepth = 33 }, d with { MaxSchemaDepth = -1 },
            d with { MaxFixedArrayElements = 0 }, d with { MaxFixedArrayElements = -1 },
            d with { MaxTupleElements = 0 }, d with { MaxTupleElements = -1 },
            d with { MaxStaticEncodedBytes = 0 }, d with { MaxStaticEncodedBytes = -1 },
        ];

        foreach (var limits in invalid)
        {
            Assert.IsFalse(ABISchemaCompiler.TryCompileArguments(definition, in limits, out var schema, out var error), limits.ToString());
            Assert.IsNull(schema);
            Assert.AreEqual(ABISchemaErrorReason.InvalidLimits, error.Reason, limits.ToString());
        }
    }

    [TestMethod]
    public void DefinitionFactories_RejectNullReferencesImmediately()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ABITypeDefinition.Tuple(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ABITypeDefinition.FixedArray(null!, 2));
        Assert.ThrowsExactly<ArgumentNullException>(() => ABITypeDefinition.DynamicArray(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ABITupleDefinition().Add(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ABITupleDefinition(ABITypeDefinition.Bool, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ABITupleDefinition((ABITypeDefinition[])null!));
    }

    // ------------------------------------------------------------------ error reporting

    [TestMethod]
    public void ErrorPaths_NameTheFailingComponent_InTheDocumentedFormat()
    {
        AssertRejected(CompileTupleError("(bool,uint7)"), ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidWidth, "$/1");
        AssertRejected(CompileTupleError("(uint7)"), ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidWidth, "$/0");
        AssertRejected(CompileTupleError("(address,(bool,uint8[],bytes33[2]))"), ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidFixedBytesLength, "$/1/2/[]");
        AssertRejected(CompileTupleError("((uint7,bool)[])"), ABIStatus.InvalidSchema, ABISchemaErrorReason.InvalidWidth, "$/0/[]/0");
    }

    [TestMethod]
    public void ErrorPaths_MatchTheGrammar_AndCarryNoPayload()
    {
        string[] bad = ["(uint7)", "((bytes33[])[])", "(uint8[0])", "(bool,(bool,bool,bool))"];
        var limits = With(tupleElements: 2);
        foreach (string text in bad)
        {
            var error = CompileTupleError(text, limits);
            StringAssert.Matches(error.Path, new System.Text.RegularExpressions.Regex(@"^\$(/(\d+|\[\]))*$"), text);
        }

        // The error type holds a status, a reason, a schema path and a text position: structurally there is no byte payload.
        var members = typeof(ABISchemaError).GetProperties().Select(p => p.PropertyType).ToHashSet();
        Assert.IsFalse(members.Any(t => t == typeof(byte[]) || t == typeof(ReadOnlyMemory<byte>) || t == typeof(Memory<byte>)));
    }

    [TestMethod]
    public void NoneError_RepresentsSuccess()
    {
        Assert.AreEqual(ABIStatus.Success, ABISchemaError.None.Status);
        Assert.AreEqual(ABISchemaErrorReason.None, ABISchemaError.None.Reason);
        Assert.AreEqual(string.Empty, ABISchemaError.None.Path);
        Assert.AreEqual(ABIStatus.Success, default(ABISchemaError).Status);
        Assert.IsTrue(ABISchemaCompiler.TryCompileArguments(new ABITupleDefinition(), ABICompileLimits.Default, out _, out var error));
        Assert.AreEqual(ABISchemaError.None, error);
    }
}
