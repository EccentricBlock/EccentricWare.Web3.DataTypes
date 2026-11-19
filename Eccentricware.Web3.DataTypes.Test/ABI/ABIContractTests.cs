using System.Runtime.CompilerServices;
using EccentricWare.Web3.DataTypes.ABI;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>The value contracts of spec section 5: ranges, locations, statuses, kinds, errors and the three limit records.</summary>
[TestClass]
public sealed class ABIContractTests
{
    [TestMethod]
    public void ABIRange_IsEightBytes()
    {
        Assert.AreEqual(8, Unsafe.SizeOf<ABIRange>());
    }

    [TestMethod]
    public void ABIRange_And_Location_HaveValueEqualityAndZeroDefaults()
    {
        var range = new ABIRange(160, 68);
        Assert.AreEqual(160, range.Offset);
        Assert.AreEqual(68, range.Length);
        Assert.AreEqual(new ABIRange(160, 68), range);
        Assert.AreNotEqual(new ABIRange(160, 69), range);
        Assert.AreEqual(new ABIRange(0, 0), default(ABIRange));

        var location = new ABIFieldLocation(new ABIRange(0, 32), new ABIRange(64, 96), ABITypeKind.Bytes);
        Assert.AreEqual(new ABIRange(0, 32), location.HeadRange);
        Assert.AreEqual(new ABIRange(64, 96), location.ValueRange);
        Assert.AreEqual(ABITypeKind.Bytes, location.Kind);
        Assert.AreEqual(location, new ABIFieldLocation(new ABIRange(0, 32), new ABIRange(64, 96), ABITypeKind.Bytes));
    }

    [TestMethod]
    public void ABIStatus_HasExactlyTheSpecMembersInOrder()
    {
        string[] expected =
        [
            "Success", "InvalidArgument", "InvalidSchema", "UnsupportedSchema", "InputTooLarge", "InvalidSelector",
            "InvalidPath", "TypeMismatch", "Truncated", "InvalidOffset", "NonCanonicalLayout", "InvalidPadding",
            "InvalidScalar", "ValueOutOfRange", "InvalidUtf8", "SourceOverlapsDestination", "ArithmeticOverflow",
            "DepthLimitExceeded", "ElementLimitExceeded", "WorkLimitExceeded", "InsufficientDestination",
            "InsufficientScratch", "IncompleteWrite", "InvalidWriterState"
        ];

        Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(ABIStatus)));
        CollectionAssert.AreEqual(expected, Enum.GetNames<ABIStatus>());
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(i, (int)Enum.Parse<ABIStatus>(expected[i]), expected[i]);
        }

    }

    [TestMethod]
    public void ABITypeKind_HasExactlyTheSpecMembersInOrder()
    {
        string[] expected =
        [
            "UInt", "Int", "Address", "Bool", "FixedBytes", "Function", "Fixed", "UFixed", "Bytes", "String", "Tuple",
            "FixedArray", "DynamicArray"
        ];

        Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(ABITypeKind)));
        CollectionAssert.AreEqual(expected, Enum.GetNames<ABITypeKind>());
    }

    [TestMethod]
    public void ABIError_CarriesStatusOffsetAndNode()
    {
        var error = new ABIError(ABIStatus.InvalidOffset, 96, 7);
        Assert.AreEqual(ABIStatus.InvalidOffset, error.Status);
        Assert.AreEqual(96, error.DocumentOffset);
        Assert.AreEqual(7, error.SchemaNode);
        Assert.AreEqual(ABIStatus.Success, default(ABIError).Status);
    }

    // ------------------------------------------------------------------ decode limits

    [TestMethod]
    public void DecodeLimits_Default_MatchesSpecSection64AndIsValid()
    {
        var limits = ABIDecodeLimits.Default;
        Assert.AreEqual(262_144, limits.MaxEncodedBytes);
        Assert.AreEqual(16, limits.MaxValueDepth);
        Assert.AreEqual(4_096, limits.MaxArrayElements);
        Assert.AreEqual(16_384, limits.MaxVisitedValues);
        Assert.AreEqual(100_000, limits.MaxWorkUnits);
        Assert.IsTrue(limits.IsValid);
        Assert.AreEqual(32, ABIDecodeLimits.HardMaxValueDepth);
    }

    [TestMethod]
    public void DecodeLimits_DefaultStruct_IsInvalid() => Assert.IsFalse(default(ABIDecodeLimits).IsValid);

    [TestMethod]
    [DataRow(0), DataRow(-1), DataRow(int.MinValue)]
    public void DecodeLimits_NonPositiveBudgets_AreInvalid(int bad)
    {
        var d = ABIDecodeLimits.Default;
        Assert.IsFalse((d with { MaxEncodedBytes = bad }).IsValid);
        Assert.IsFalse((d with { MaxArrayElements = bad }).IsValid);
        Assert.IsFalse((d with { MaxVisitedValues = bad }).IsValid);
        Assert.IsFalse((d with { MaxWorkUnits = bad }).IsValid);
        Assert.IsFalse((d with { MaxValueDepth = bad }).IsValid);
    }

    [TestMethod]
    [DataRow(1, true), DataRow(16, true), DataRow(32, true), DataRow(33, false), DataRow(int.MaxValue, false)]
    public void DecodeLimits_DepthIsCappedAtTheFrameStack(int depth, bool valid)
    {
        Assert.AreEqual(valid, (ABIDecodeLimits.Default with { MaxValueDepth = depth }).IsValid);
    }

    [TestMethod]
    public void DecodeLimits_LargePositiveBudgets_AreValid()
    {
        Assert.IsTrue(new ABIDecodeLimits(int.MaxValue, 32, int.MaxValue, int.MaxValue, int.MaxValue).IsValid);
        Assert.IsTrue(new ABIDecodeLimits(1, 1, 1, 1, 1).IsValid);
    }

    // ------------------------------------------------------------------ encode limits

    [TestMethod]
    public void EncodeLimits_Default_IsValidAndMatchesDecodeDefaults()
    {
        var limits = ABIEncodeLimits.Default;
        Assert.IsTrue(limits.IsValid);
        Assert.AreEqual(ABIDecodeLimits.Default.MaxEncodedBytes, limits.MaxEncodedBytes);
        Assert.AreEqual(ABIDecodeLimits.Default.MaxValueDepth, limits.MaxValueDepth);
        Assert.AreEqual(ABIDecodeLimits.Default.MaxArrayElements, limits.MaxArrayElements);
        Assert.AreEqual(16_384, limits.MaxWrittenValues);
        Assert.AreEqual(100_000, limits.MaxWorkUnits);
        Assert.AreEqual(32, ABIEncodeLimits.HardMaxValueDepth);
    }

    [TestMethod]
    public void EncodeLimits_DefaultStruct_IsInvalid() => Assert.IsFalse(default(ABIEncodeLimits).IsValid);

    [TestMethod]
    [DataRow(0), DataRow(-1), DataRow(int.MinValue)]
    public void EncodeLimits_NonPositiveBudgets_AreInvalid(int bad)
    {
        var d = ABIEncodeLimits.Default;
        Assert.IsFalse((d with { MaxEncodedBytes = bad }).IsValid);
        Assert.IsFalse((d with { MaxArrayElements = bad }).IsValid);
        Assert.IsFalse((d with { MaxWrittenValues = bad }).IsValid);
        Assert.IsFalse((d with { MaxWorkUnits = bad }).IsValid);
        Assert.IsFalse((d with { MaxValueDepth = bad }).IsValid);
    }

    [TestMethod]
    [DataRow(1, true), DataRow(32, true), DataRow(33, false), DataRow(int.MaxValue, false)]
    public void EncodeLimits_DepthIsCapped(int depth, bool valid)
    {
        Assert.AreEqual(valid, (ABIEncodeLimits.Default with { MaxValueDepth = depth }).IsValid);
    }

    // ------------------------------------------------------------------ compile limits

    [TestMethod]
    public void CompileLimits_Default_IsValid()
    {
        var limits = ABICompileLimits.Default;
        Assert.IsTrue(limits.IsValid);
        Assert.AreEqual(4_096, limits.MaxSchemaNodes);
        Assert.AreEqual(32, limits.MaxSchemaDepth);
        Assert.AreEqual(32, ABICompileLimits.HardMaxSchemaDepth);
    }

    [TestMethod]
    public void CompileLimits_DefaultStruct_IsInvalid() => Assert.IsFalse(default(ABICompileLimits).IsValid);

    [TestMethod]
    [DataRow(0), DataRow(-1), DataRow(int.MinValue)]
    public void CompileLimits_NonPositiveBudgets_AreInvalid(int bad)
    {
        var d = ABICompileLimits.Default;
        Assert.IsFalse((d with { MaxSchemaNodes = bad }).IsValid);
        Assert.IsFalse((d with { MaxSchemaDepth = bad }).IsValid);
        Assert.IsFalse((d with { MaxFixedArrayElements = bad }).IsValid);
        Assert.IsFalse((d with { MaxTupleElements = bad }).IsValid);
        Assert.IsFalse((d with { MaxStaticEncodedBytes = bad }).IsValid);
    }

    [TestMethod]
    [DataRow(1, true), DataRow(32, true), DataRow(33, false), DataRow(int.MaxValue, false)]
    public void CompileLimits_DepthIsCapped(int depth, bool valid)
    {
        Assert.AreEqual(valid, (ABICompileLimits.Default with { MaxSchemaDepth = depth }).IsValid);
    }

    [TestMethod]
    public void CompileLimits_LargePositiveBudgets_AreValid()
    {
        Assert.IsTrue(new ABICompileLimits(int.MaxValue, 32, int.MaxValue, int.MaxValue, int.MaxValue).IsValid);
        Assert.IsTrue(new ABICompileLimits(1, 1, 1, 1, 1).IsValid);
    }
}
