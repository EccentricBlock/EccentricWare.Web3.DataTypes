using System.Reflection;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>Compiled schemas are immutable and independent of their source definitions (spec 4.1).</summary>
[TestClass]
public sealed class ABISchemaImmutabilityTests
{
    private static ABISchema Compile(ABITupleDefinition definition)
    {
        Assert.IsTrue(ABISchemaCompiler.TryCompileArguments(definition, ABICompileLimits.Default, out var schema, out var error), error.ToString());
        return schema;
    }

    [TestMethod]
    public void MutatingTheRootDefinitionAfterCompile_ChangesNothing()
    {
        var definition = new ABITupleDefinition(ABITypeDefinition.Address, ABITypeDefinition.UInt(256));
        var schema = Compile(definition);

        definition.Add(ABITypeDefinition.Bytes).Add(ABITypeDefinition.String);

        Assert.AreEqual(4, definition.Count);
        Assert.AreEqual("(address,uint256)", schema.CanonicalType);
        Assert.AreEqual(2, schema.ArgumentCount);
        Assert.IsFalse(schema.IsDynamic);
        Assert.AreEqual(3, schema.NodeCount);
        AssertNode(schema, 0, ABITypeKind.Tuple, false, 64, 64, headSize: 64, count: 2);
        AssertChildren(schema, 0, (1, 0, ABISchema.NoSibling), (2, 32, ABISchema.NoSibling));

        // A fresh compile sees the change, proving the original really is independent.
        var recompiled = Compile(definition);
        Assert.AreEqual("(address,uint256,bytes,string)", recompiled.CanonicalType);
        Assert.IsTrue(recompiled.IsDynamic);
        Assert.AreEqual("(address,uint256)", schema.CanonicalType);
    }

    [TestMethod]
    public void MutatingANestedTupleDefinitionAfterCompile_ChangesNothing()
    {
        var inner = new ABITupleDefinition(ABITypeDefinition.UInt(8));
        var root = new ABITupleDefinition(ABITypeDefinition.DynamicArray(ABITypeDefinition.Tuple(inner)), ABITypeDefinition.Bool);
        var schema = Compile(root);
        string before = schema.CanonicalType;

        inner.Add(ABITypeDefinition.Bytes);

        Assert.AreEqual("((uint8)[],bool)", before);
        Assert.AreEqual(before, schema.CanonicalType);
        AssertNode(schema, 2, ABITypeKind.Tuple, false, 32, 32, headSize: 32, count: 1);
        Assert.AreEqual(5, schema.NodeCount); // root, DynArray, tuple, uint8, bool
        Assert.AreEqual("((uint8,bytes)[],bool)", Compile(root).CanonicalType);
    }

    [TestMethod]
    public void MutatingADefinitionAfterCompileFunction_LeavesSelectorAndSignatureUntouched()
    {
        var args = new ABITupleDefinition(ABITypeDefinition.Address, ABITypeDefinition.UInt(256));
        Assert.IsTrue(ABISchemaCompiler.TryCompileFunction("transfer", args, ABICompileLimits.Default, out var function, out _));

        args.Add(ABITypeDefinition.Bool);

        Assert.AreEqual("transfer(address,uint256)", function.CanonicalSignature);
        Assert.AreEqual(0xa9059cbbu, function.Selector);
        Assert.AreEqual("(address,uint256)", function.InputSchema.CanonicalType);
    }

    [TestMethod]
    public void ConstructorArray_IsCopied()
    {
        ABITypeDefinition[] source = [ABITypeDefinition.Bool, ABITypeDefinition.Address];
        var definition = new ABITupleDefinition(source);
        source[0] = ABITypeDefinition.Bytes;
        Assert.AreEqual(ABITypeKind.Bool, definition[0].Kind);
    }

    [TestMethod]
    public void TwoCompilesOfOneDefinition_AreIndependentButEquivalentObjects()
    {
        var definition = new ABITupleDefinition(ABITypeDefinition.DynamicArray(ABITypeDefinition.Bytes));
        var a = Compile(definition);
        var b = Compile(definition);

        Assert.AreNotSame(a, b);
        Assert.AreEqual(a.CanonicalType, b.CanonicalType);
        Assert.AreEqual(a.NodeCount, b.NodeCount);
    }

    [TestMethod]
    public void PublicSurface_ExposesNoMutableTable()
    {
        foreach (var type in new[] { typeof(ABISchema), typeof(ABIFunctionSchema), typeof(ABITypeDefinition) })
        {
            Assert.AreEqual(0, type.GetConstructors().Length, type.Name + " public constructors");

            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Type? exposed = member switch
                {
                    PropertyInfo p => p.PropertyType,
                    MethodInfo m when !m.IsSpecialName => m.ReturnType,
                    FieldInfo f => f.FieldType,
                    _ => null,
                };

                Assert.IsFalse(exposed is { IsArray: true }, $"{type.Name}.{member.Name} exposes an array");
                Assert.IsFalse(
                    exposed is { IsGenericType: true } && exposed.GetGenericTypeDefinition() == typeof(List<>),
                    $"{type.Name}.{member.Name} exposes a List");
                Assert.IsFalse(member is FieldInfo { IsPublic: true, IsInitOnly: false }, $"{type.Name}.{member.Name} is a mutable public field");
                Assert.IsFalse(member is PropertyInfo { SetMethod.IsPublic: true }, $"{type.Name}.{member.Name} has a public setter");
            }
        }
    }

    [TestMethod]
    public void SchemaAndFunction_AreSealed_SoNoSubclassCanAddState()
    {
        Assert.IsTrue(typeof(ABISchema).IsSealed);
        Assert.IsTrue(typeof(ABIFunctionSchema).IsSealed);
        Assert.IsTrue(typeof(ABITypeDefinition).IsSealed);
        Assert.IsTrue(typeof(ABITupleDefinition).IsSealed);
        Assert.IsTrue(typeof(ABISchemaCompiler).IsAbstract && typeof(ABISchemaCompiler).IsSealed, "static class");
    }

    [TestMethod]
    public void CompiledSchema_ReadsAreConsistentAcrossThreads()
    {
        // Read-only sharing check: every thread walks the whole table and must see the same hand-computed values.
        var schema = CompileTuple("((address,uint256,bytes)[],uint256[2])");

        Parallel.For(0, 200, _ =>
        {
            Assert.AreEqual("((address,uint256,bytes)[],uint256[2])", schema.CanonicalType);
            Assert.AreEqual(8, schema.NodeCount);
            AssertNode(schema, 2, ABITypeKind.Tuple, true, ABISchema.NotStatic, 32, headSize: 96, count: 3);
            AssertNode(schema, 6, ABITypeKind.FixedArray, false, 64, 64, headSize: 64, count: 2, element: 7, elementHeadWidth: 32);
            AssertChildren(schema, 0, (1, 0, ABISchema.NoSibling), (6, 32, ABISchema.NoSibling));
        });
    }

    [TestMethod]
    public void LimitsPassedByReference_AreSnapshottedBeforeUse()
    {
        // Behavioural half of the snapshot guarantee: a later change to the caller's variable cannot alter a finished compile,
        // and an invalid struct is never accepted. (A true mid-compile race is not testable deterministically.)
        var limits = ABICompileLimits.Default;
        var definition = new ABITupleDefinition(ABITypeDefinition.UInt(8), ABITypeDefinition.Bool);
        Assert.IsTrue(ABISchemaCompiler.TryCompileArguments(definition, in limits, out var first, out _));
        limits = default;
        Assert.AreEqual("(uint8,bool)", first.CanonicalType);
        Assert.IsFalse(ABISchemaCompiler.TryCompileArguments(definition, in limits, out _, out _));
    }
}
