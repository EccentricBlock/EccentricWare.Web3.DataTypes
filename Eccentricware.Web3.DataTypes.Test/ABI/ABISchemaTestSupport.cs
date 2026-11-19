using EccentricWare.Web3.DataTypes.ABI;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>Shared helpers for the schema tests. Expected values in tests are written by hand, never derived from these helpers.</summary>
internal static class ABISchemaTestSupport
{
    internal static ABISchema CompileTuple(string tupleText, ABICompileLimits? limits = null)
    {
        Assert.IsTrue(ABITupleDefinition.TryParse(tupleText, out var definition, out var parseError), $"parse '{tupleText}': {parseError}");
        var effective = limits ?? ABICompileLimits.Default;
        Assert.IsTrue(
            ABISchemaCompiler.TryCompileArguments(definition, in effective, out var schema, out var error),
            $"compile '{tupleText}': {error}");
        return schema;
    }

    internal static ABISchemaError CompileTupleError(string tupleText, ABICompileLimits? limits = null)
    {
        Assert.IsTrue(ABITupleDefinition.TryParse(tupleText, out var definition, out var parseError), $"parse '{tupleText}': {parseError}");
        return CompileDefinitionError(definition, limits);
    }

    internal static ABISchemaError CompileDefinitionError(ABITupleDefinition definition, ABICompileLimits? limits = null)
    {
        var effective = limits ?? ABICompileLimits.Default;
        Assert.IsFalse(ABISchemaCompiler.TryCompileArguments(definition, in effective, out var schema, out var error));
        Assert.IsNull(schema, "no schema on failure");
        Assert.AreNotEqual(ABIStatus.Success, error.Status);
        Assert.AreNotEqual(ABISchemaErrorReason.None, error.Reason);
        return error;
    }

    /// <summary>Asserts the full set of per-node fields (hand-computed by the caller).</summary>
    internal static void AssertNode(
        ABISchema schema,
        int id,
        ABITypeKind kind,
        bool dynamic,
        int staticSize,
        int headWidth,
        int headSize = 0,
        int count = 0,
        int element = ABISchema.NoNode,
        int elementHeadWidth = 0,
        int width = 0,
        int scale = 0)
    {
        ref readonly var node = ref schema.GetNode(id);
        string where = $"node {id} of {schema.CanonicalType}";
        Assert.AreEqual(kind, node.Kind, where + " kind");
        Assert.AreEqual(dynamic, node.IsDynamic, where + " IsDynamic");
        Assert.AreEqual(staticSize, node.StaticSize, where + " StaticSize");
        Assert.AreEqual(headWidth, node.HeadWidth, where + " HeadWidth");
        Assert.AreEqual(headSize, node.HeadSize, where + " HeadSize");
        Assert.AreEqual(count, node.Count, where + " Count");
        Assert.AreEqual(element, node.Element, where + " Element");
        Assert.AreEqual(elementHeadWidth, node.ElementHeadWidth, where + " ElementHeadWidth");
        Assert.AreEqual(width, node.Width, where + " Width");
        Assert.AreEqual(scale, node.Scale, where + " Scale");
    }

    /// <summary>Asserts the (node id, head offset, next dynamic sibling) triples of a tuple node.</summary>
    internal static void AssertChildren(ABISchema schema, int tupleId, params (int NodeId, int HeadOffset, int NextDynamic)[] expected)
    {
        var children = schema.GetTupleChildren(tupleId);
        Assert.AreEqual(expected.Length, children.Length, $"child count of node {tupleId} in {schema.CanonicalType}");
        for (int i = 0; i < expected.Length; i++)
        {
            string where = $"child {i} of node {tupleId} in {schema.CanonicalType}";
            Assert.AreEqual(expected[i].NodeId, children[i].NodeId, where + " NodeId");
            Assert.AreEqual(expected[i].HeadOffset, children[i].HeadOffset, where + " HeadOffset");
            Assert.AreEqual(expected[i].NextDynamic, children[i].NextDynamicSibling, where + " NextDynamicSibling");
        }
    }

    /// <summary>Limits that allow everything the specific test does not target.</summary>
    internal static ABICompileLimits With(
        int? nodes = null, int? depth = null, int? fixedElements = null, int? tupleElements = null, int? staticBytes = null)
    {
        var d = ABICompileLimits.Default;
        return new ABICompileLimits(
            nodes ?? d.MaxSchemaNodes,
            depth ?? d.MaxSchemaDepth,
            fixedElements ?? d.MaxFixedArrayElements,
            tupleElements ?? d.MaxTupleElements,
            staticBytes ?? d.MaxStaticEncodedBytes);
    }
}
