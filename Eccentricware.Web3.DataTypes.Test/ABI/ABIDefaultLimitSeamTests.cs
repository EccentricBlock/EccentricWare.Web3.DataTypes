using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Pins the default-limit seams documented in docs/abi-security.md ("Default limits and where they meet"): places where two defaults that
/// look consistent meet and the first operation to hit the edge is a different one than a reader expects. The defaults are policy and are
/// not changed; these tests keep the documentation true. The compile depth (32) against the decode depth (16) is the SPECIFIED seam: the table of spec
/// 6.4 fixes "MaxSchemaDepth 32, at compilation; decoder runtime limit may be lower" and "MaxValueDepth 16", so aligning the defaults would reject schemas the
/// specification says must compile (docs/abi-design-notes.md, row 76).
/// </summary>
[TestClass]
public sealed class ABIDefaultLimitSeamTests
{
    [TestMethod]
    public void StaticSchemaAtTheCompileLimit_DecodesAsADocument_ButIsTooLargeAsACall()
    {
        // Two uint256[4096] arrays: 2 * 4096 * 32 = 262,144 bytes, exactly the default static limit and the default decode MaxEncodedBytes.
        const string tuple = "(uint256[4096],uint256[4096])";
        ABISchema schema = CompileTuple(tuple);
        byte[] arguments = new byte[262_144];
        var limits = ABIDecodeLimits.Default with { MaxVisitedValues = 100_000, MaxWorkUnits = 1_000_000 };

        Assert.IsTrue(ABIData.TryDecodeComplete(arguments, schema, in limits, out ABIData data, out ABIStatus status), status.ToString());
        Assert.AreEqual(262_144, data.EncodedBytes.Length);

        ABIFunctionSchema function = Function("f" + tuple);
        byte[] call = new byte[262_148];
        Assert.IsFalse(ABICallData.TryDecode(call, function, in limits, out _, out status));
        Assert.AreEqual(ABIStatus.InputTooLarge, status, "the selector pushes the call over MaxEncodedBytes");
    }

    [TestMethod]
    public void SchemaDeeperThanTheDefaultDecodeDepth_Compiles_ButANonEmptyDocumentIsDepthLimited()
    {
        // 16 nested single-member tuples around a uint8: container depth 17, leaf depth 18, above the default MaxValueDepth of 16.
        string tuple = "(" + string.Concat(Enumerable.Repeat("(", 16)) + "uint8" + string.Concat(Enumerable.Repeat(")", 16)) + ")";
        ABISchema schema = CompileTuple(tuple); // the default compile depth is 32
        DecodeFails(Word(7), schema, ABIStatus.DepthLimitExceeded, ABIDecodeLimits.Default);
        DecodeOk(Word(7), schema, ABIDecodeLimits.Default with { MaxValueDepth = 18 });
        Assert.AreEqual(32, ABICompileLimits.Default.MaxSchemaDepth, "spec 6.4: MaxSchemaDepth 32 at compilation");
        Assert.AreEqual(16, ABIDecodeLimits.Default.MaxValueDepth, "spec 6.4: MaxValueDepth 16 (hard maximum 32)");
        Assert.AreEqual(32, ABIEncodeLimits.HardMaxValueDepth);
    }

    [TestMethod]
    public void DynamicArraysAboveTheDefaultElementLimit_AreRejected_UntilMaxArrayElementsIsRaised()
    {
        ABISchema schema = CompileTuple("(uint256[])");
        foreach (int count in new[] { 4096, 4097, 5000 })
        {
            byte[] document = Cat(Word(0x20), Word((ulong)count), new byte[32 * count]);
            ABIDecodeLimits roomy = Generous with { MaxArrayElements = 10_000 };
            DecodeOk(document, schema, roomy);
            if (count <= 4096)
            {
                DecodeOk(document, schema, ABIDecodeLimits.Default);
            }
            else
            {
                DecodeFails(document, schema, ABIStatus.ElementLimitExceeded, ABIDecodeLimits.Default);
            }
        }
    }
}
