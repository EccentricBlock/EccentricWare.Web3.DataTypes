using System.Reflection;
using System.Runtime.CompilerServices;
using EccentricWare.Web3.DataTypes.ABI;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Compile-time-style contract tests (spec 7.1, 7.6, 14): the public surface of ABIWriter, ABIMeasurer and ABICallEncoder is exactly the
/// specified event grammar and the three share it signature for signature; the frames are unmanaged value types; the types are
/// stack-only; no public member beyond the specification exists.
/// </summary>
[TestClass]
public sealed class ABIWriterContractTests
{
    private static readonly (string Name, Type[] Parameters)[] Grammar =
    [
        ("TryWriteUInt64", [typeof(ulong), typeof(ABIStatus).MakeByRefType()]),
        ("TryWriteInt64", [typeof(long), typeof(ABIStatus).MakeByRefType()]),
        ("TryWriteUInt256Word", [typeof(ReadOnlySpan<byte>), typeof(ABIStatus).MakeByRefType()]),
        ("TryWriteInt256Word", [typeof(ReadOnlySpan<byte>), typeof(ABIStatus).MakeByRefType()]),
        ("TryWriteAddress", [typeof(ReadOnlySpan<byte>), typeof(ABIStatus).MakeByRefType()]),
        ("TryWriteBool", [typeof(bool), typeof(ABIStatus).MakeByRefType()]),
        ("TryWriteFixedBytes", [typeof(ReadOnlySpan<byte>), typeof(ABIStatus).MakeByRefType()]),
        ("TryWriteFunction", [typeof(ReadOnlySpan<byte>), typeof(uint), typeof(ABIStatus).MakeByRefType()]),
        ("TryWriteBytes", [typeof(ReadOnlySpan<byte>), typeof(ABIStatus).MakeByRefType()]),
        ("TryWriteStringUtf8", [typeof(ReadOnlySpan<byte>), typeof(ABIStatus).MakeByRefType()]),
        ("TryWriteSignedScaledWord", [typeof(ReadOnlySpan<byte>), typeof(ABIStatus).MakeByRefType()]),
        ("TryWriteUnsignedScaledWord", [typeof(ReadOnlySpan<byte>), typeof(ABIStatus).MakeByRefType()]),
        ("TryBeginTuple", [typeof(ABIStatus).MakeByRefType()]),
        ("TryBeginFixedArray", [typeof(ABIStatus).MakeByRefType()]),
        ("TryBeginDynamicArray", [typeof(int), typeof(ABIStatus).MakeByRefType()]),
        ("TryEndContainer", [typeof(ABIStatus).MakeByRefType()]),
        ("TryFinish", [typeof(int).MakeByRefType(), typeof(ABIStatus).MakeByRefType()]),
    ];

    private static IEnumerable<MethodInfo> PublicInstanceMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName);

    [TestMethod]
    [DataRow(typeof(ABIWriter))]
    [DataRow(typeof(ABIMeasurer))]
    [DataRow(typeof(ABICallEncoder))]
    public void PublicInstanceSurface_IsExactlyTheSchemaOrderedEventGrammar(Type type)
    {
        MethodInfo[] methods = [.. PublicInstanceMethods(type)];
        CollectionAssert.AreEquivalent(Grammar.Select(g => g.Name).ToArray(), methods.Select(m => m.Name).ToArray(), $"{type.Name}: public instance methods");
        foreach (var (name, parameters) in Grammar)
        {
            MethodInfo? method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, parameters);
            Assert.IsNotNull(method, $"{type.Name}.{name} with the specified parameters");
            Assert.AreEqual(typeof(bool), method.ReturnType, $"{type.Name}.{name} returns bool");
            Assert.IsTrue(method.GetParameters()[^1].IsOut, $"{type.Name}.{name}: the status is the last, out parameter");
        }
    }

    [TestMethod]
    public void TryBeginFactories_HaveTheSpecifiedSignatures()
    {
        AssertFactory(typeof(ABIWriter), [typeof(ABISchema), typeof(Span<byte>), typeof(Span<ABIWriteFrame>), typeof(ABIEncodeLimits).MakeByRefType(), typeof(ABIWriter).MakeByRefType(), typeof(ABIStatus).MakeByRefType()]);
        AssertFactory(typeof(ABIMeasurer), [typeof(ABISchema), typeof(Span<ABIMeasureFrame>), typeof(ABIEncodeLimits).MakeByRefType(), typeof(ABIMeasurer).MakeByRefType(), typeof(ABIStatus).MakeByRefType()]);
        AssertFactory(typeof(ABICallEncoder), [typeof(ABIFunctionSchema), typeof(Span<byte>), typeof(Span<ABIWriteFrame>), typeof(ABIEncodeLimits).MakeByRefType(), typeof(ABICallEncoder).MakeByRefType(), typeof(ABIStatus).MakeByRefType()]);
    }

    private static void AssertFactory(Type type, Type[] parameters)
    {
        MethodInfo? factory = type.GetMethod("TryBegin", BindingFlags.Public | BindingFlags.Static, parameters);
        Assert.IsNotNull(factory, $"{type.Name}.TryBegin");
        Assert.AreEqual(typeof(bool), factory.ReturnType);
        Assert.AreEqual(1, type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Count(m => m.Name == "TryBegin"), $"{type.Name}: exactly one public TryBegin");
        Assert.AreEqual(1, type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Length, $"{type.Name}: no other public static member");
    }

    [TestMethod]
    public void WriterMeasurerAndCallEncoder_AreStackOnlyStructs_WithNoPublicFieldsOrConstructors()
    {
        foreach (Type type in new[] { typeof(ABIWriter), typeof(ABIMeasurer), typeof(ABICallEncoder) })
        {
            Assert.IsTrue(type.IsValueType, type.Name);
            Assert.IsTrue(type.IsByRefLike, $"{type.Name} must be a ref struct");
            Assert.AreEqual(0, type.GetFields(BindingFlags.Public | BindingFlags.Instance).Length, $"{type.Name}: no public fields");
            Assert.AreEqual(0, type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length, $"{type.Name}: no public properties");
            Assert.AreEqual(0, type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length, $"{type.Name}: only default and the TryBegin factory create one");
        }
    }

    [TestMethod]
    public void Frames_AreUnmanagedNonRefValueTypes_OfAKnownSize_WithNoPublicFields()
    {
        foreach (Type type in new[] { typeof(ABIWriteFrame), typeof(ABIMeasureFrame) })
        {
            Assert.IsTrue(type.IsValueType, type.Name);
            Assert.IsFalse(type.IsByRefLike, $"{type.Name} appears in a Span<T> and must not be a ref struct");
            Assert.AreEqual(0, type.GetFields(BindingFlags.Public | BindingFlags.Instance).Length, $"{type.Name}: opaque, no public fields");
            Assert.AreEqual(0, type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length, type.Name);
        }

        Assert.IsFalse(RuntimeHelpers.IsReferenceOrContainsReferences<ABIWriteFrame>(), "no managed references in a write frame");
        Assert.IsFalse(RuntimeHelpers.IsReferenceOrContainsReferences<ABIMeasureFrame>(), "no managed references in a measure frame");
        Assert.IsFalse(RuntimeHelpers.IsReferenceOrContainsReferences<ABIEncodeFrame>());
        Assert.AreEqual(13 * sizeof(int), Unsafe.SizeOf<ABIWriteFrame>());
        Assert.AreEqual(13 * sizeof(int), Unsafe.SizeOf<ABIMeasureFrame>());
        Assert.AreEqual(Unsafe.SizeOf<ABIWriteFrame>(), Unsafe.SizeOf<ABIMeasureFrame>(), "the two frame types wrap the same state, so one cast serves both");
    }

    [TestMethod]
    public void ABIWriter_DoesNotOfferRawStringOrBareScalarRootEntryPoints()
    {
        // 7.2 / 7.6: strings only through the validating method, and the root is always the argument tuple.
        string[] names = [.. typeof(ABIWriter).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(m => m.Name)];
        CollectionAssert.DoesNotContain(names, "TryWriteString");
        CollectionAssert.DoesNotContain(names, "TryWriteStringRaw");
        CollectionAssert.DoesNotContain(names, "TryBeginRoot");
    }

    [TestMethod]
    public void DefaultInstances_AreInvalid_AndFramesDefaultToZero()
    {
        ABIWriteFrame frame = default;
        ABIMeasureFrame measureFrame = default;
        Assert.AreEqual(default, frame);
        Assert.AreEqual(default, measureFrame);

        ABIWriter writer = default;
        Assert.IsFalse(writer.TryBeginTuple(out ABIStatus status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
    }

    [TestMethod]
    public void EncodeLimits_SpecShapeAndDefaultsMatchTheDecodeDefaults()
    {
        var d = ABIEncodeLimits.Default;
        var decode = ABIDecodeLimits.Default;
        Assert.AreEqual(decode.MaxEncodedBytes, d.MaxEncodedBytes);
        Assert.AreEqual(decode.MaxValueDepth, d.MaxValueDepth);
        Assert.AreEqual(decode.MaxArrayElements, d.MaxArrayElements);
        Assert.AreEqual(decode.MaxVisitedValues, d.MaxWrittenValues);
        Assert.AreEqual(decode.MaxWorkUnits, d.MaxWorkUnits);
        Assert.IsTrue(d.IsValid);
        Assert.IsFalse(default(ABIEncodeLimits).IsValid);
    }
}
