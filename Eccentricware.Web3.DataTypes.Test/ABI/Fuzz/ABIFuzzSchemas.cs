using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>One entry of the fixed schema registry the raw-bytes fuzz targets index with their first input byte.</summary>
internal sealed class ABIFuzzSchema
{
    internal ABIFuzzSchema(string name, string tuple)
    {
        Name = name;
        Tuple = tuple;
        Type = ABIReference.ParseTuple(tuple);
        Schema = CompileTuple(tuple);
        Assert.IsTrue(ABITupleDefinition.TryParse(tuple, out var definition, out var parseError), $"{name}: {parseError}");
        var limits = ABICompileLimits.Default;
        Assert.IsTrue(ABISchemaCompiler.TryCompileFunction("fuzz", definition, in limits, out var function, out var error), $"{name}: {error}");
        Function = function;
    }

    internal string Name { get; }

    internal string Tuple { get; }

    /// <summary>The independent reference model's parse of <see cref="Tuple"/> (shares no code with the library).</summary>
    internal RTupleType Type { get; }

    internal ABISchema Schema { get; }

    /// <summary>The function <c>fuzz(...)</c> over the same arguments, for the call decoder and call encoder.</summary>
    internal ABIFunctionSchema Function { get; }

    internal int Index { get; set; }

    /// <summary>Random schema built on the fly by the generative tests (not part of the registry; <see cref="Index"/> is -1).</summary>
    internal static ABIFuzzSchema Ad(string name, string tuple) => new(name, tuple) { Index = -1 };
}

/// <summary>
/// The schema registry. APPEND ONLY: a raw fuzz input selects its schema as <c>input[0] % Count</c>, and every corpus file records the index
/// in its first byte and the name in its file name, so reordering or removing an entry invalidates the corpus (the replay test fails).
/// </summary>
internal static class ABIFuzzSchemas
{
    private static readonly (string Name, string Tuple)[] Definitions =
    [
        ("transfer", "(address,uint256)"),
        ("balanceOf", "(address)"),
        ("exactInputSingle", ABIFixtureCatalog.ExactInputSingleTuple),
        ("multicall", "(bytes[])"),
        ("seaport", ABIFixtureCatalog.SeaportOrderTuple),
        ("baz", "(uint32,bool)"),
        ("bar", "(bytes3[2])"),
        ("sam", "(bytes,bool,uint256[])"),
        ("docsF", "(uint256,uint32[],bytes10,bytes)"),
        ("docsG", "(uint256[][],string[])"),
        ("unsignedMix", "(uint8,uint16,uint64,uint128,uint256)"),
        ("signedMix", "(int8,int16,int64,int128,int256)"),
        ("scalarMix", "(bool,bool,address,bytes1,bytes24,bytes32)"),
        ("function", "(function)"),
        ("bytes", "(bytes)"),
        ("string", "(string)"),
        ("staticTupleInTuple", "((uint256,uint256),uint256)"),
        ("multiWordHead", "(uint256[3],bool,bytes)"),
        ("uintArray", "(uint256[])"),
        ("stringArray", "(string[])"),
        ("fixedBytesArray", "(bytes[2])"),
        ("tupleArray", "((address,uint256,bytes)[])"),
        ("fixedArrayOfDynamicTuples", "((uint256,bytes)[2])"),
        ("arrayOfArrays", "(uint256[][])"),
        ("fixedOfFixed", "(uint256[2][2])"),
        ("dynamicOfFixed", "(uint256[2][])"),
        ("fixedOfDynamic", "(uint256[][2])"),
        ("nestedMixed", "(uint256,(string,(uint8,bytes)[],address),uint256[2])"),
        ("emptyDynamics", "((uint256,string,uint256[],bytes),bool)"),
        ("dynamicThenStatic", "(bytes,uint256,string,uint256)"),
        ("empty", "()"),
        ("emptyTuple", "(())"),
        ("emptyTuples", "((),(()))"),
        ("emptyTupleArray", "(()[])"),
        ("emptyTupleFixedArray", "(()[3])"),
        ("emptyTupleArrayArray", "((()[])[])"),
        ("fixedPoint", "(fixed128x18,ufixed128x18,fixed8x1)"),
        ("deepArraysShallowFrames", "(uint256[][][][][][][])"),
        ("deepArraysDeepFrames", "(uint256[][][][][][][][][][][])"),
        ("deepMixedTuples", "((uint256,((bytes,(string,uint8[])[])[],bool)[]),address)"),
        ("wideStatic", "(uint256[64],bytes)"),
        ("safeBatchTransferFrom", "(address,address,uint256[],uint256[],bytes)"),
        ("swapExactTokens", "(uint256,uint256,address[],address,uint256)"),
        ("signedAndBoolArrays", "(int8[],bool[])"),
        ("bytesPairArrays", "(bytes[2][])"),
        ("arraysThenTupleArray", "(uint256[][2],(uint8,bytes)[2])"),
        ("tupleArrayWithString", "((uint256,(string,bytes)[])[])"),
        ("narrowIntegers", "(uint8,int8,uint24,int24,uint160,int160)"),
        ("bytes31Pair", "(bytes31[2])"),
        ("multicallDeadline", "(uint256,bytes[])"),
        ("transferFrom", "(address,address,uint256)"),
        ("fixedUintArray", "(uint256[3])"),
        ("emptyTupleArrays5", "(()[],()[],()[],()[],()[])"),
    ];

    internal static readonly ABIFuzzSchema[] All = Definitions
        .Select((d, i) => new ABIFuzzSchema(d.Name, d.Tuple) { Index = i })
        .ToArray();

    internal static ABIFuzzSchema ByName(string name) =>
        All.FirstOrDefault(s => s.Name == name) ?? throw new InvalidOperationException($"no fuzz schema named {name}");

    internal static ABIFuzzSchema ByTuple(string tuple) =>
        All.FirstOrDefault(s => s.Tuple == tuple) ?? throw new InvalidOperationException($"the fuzz schema registry has no entry for {tuple}; append one");
}
