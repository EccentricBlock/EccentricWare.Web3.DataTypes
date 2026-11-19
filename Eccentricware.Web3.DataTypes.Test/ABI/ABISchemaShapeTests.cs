using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Every legal type form compiles to the node table derived by hand from spec 2.2 and 4.1. Node ids are pre-order (root 0, then
/// each type occurrence in declaration order, an array's element right after the array). Sizes: a primitive is 32 bytes, an empty
/// tuple 0, T[k] k*size(T), a static tuple the sum of its members; a dynamic node is 32 bytes in its parent's head.
/// </summary>
[TestClass]
public sealed class ABISchemaShapeTests
{
    private const ABITypeKind UInt = ABITypeKind.UInt;

    [TestMethod]
    public void EmptyRoot_IsAStaticZeroByteTuple()
    {
        var schema = CompileTuple("()");

        Assert.AreEqual(1, schema.NodeCount);
        Assert.AreEqual(1, schema.MaxDepth);
        Assert.AreEqual(0, schema.ArgumentCount);
        Assert.IsFalse(schema.IsDynamic);
        AssertNode(schema, 0, ABITypeKind.Tuple, dynamic: false, staticSize: 0, headWidth: 0, headSize: 0, count: 0);
        AssertChildren(schema, 0);
        Assert.AreEqual(0, schema.GetNode(ABISchema.RootNodeId).Count);
        Assert.IsTrue(schema.GetTupleChildren(schema.Root).IsEmpty);
    }

    [TestMethod]
    public void TransferArguments_AreTwoStaticWords()
    {
        var schema = CompileTuple("(address,uint256)");

        Assert.AreEqual(3, schema.NodeCount);
        Assert.AreEqual(2, schema.MaxDepth);
        Assert.AreEqual(2, schema.ArgumentCount);
        Assert.IsFalse(schema.IsDynamic);
        AssertNode(schema, 0, ABITypeKind.Tuple, false, staticSize: 64, headWidth: 64, headSize: 64, count: 2);
        AssertNode(schema, 1, ABITypeKind.Address, false, 32, 32);
        AssertNode(schema, 2, UInt, false, 32, 32, width: 256);
        AssertChildren(schema, 0, (1, 0, ABISchema.NoSibling), (2, 32, ABISchema.NoSibling));
    }

    [TestMethod]
    public void ScalarKinds_CarryWidthAndScale()
    {
        var schema = CompileTuple("(uint8,int,bytes1,bytes32,fixed,ufixed256x80,function,bool,int248)");

        Assert.AreEqual(10, schema.NodeCount);
        AssertNode(schema, 0, ABITypeKind.Tuple, false, staticSize: 288, headWidth: 288, headSize: 288, count: 9); // 9 * 32
        AssertNode(schema, 1, UInt, false, 32, 32, width: 8);
        AssertNode(schema, 2, ABITypeKind.Int, false, 32, 32, width: 256);
        AssertNode(schema, 3, ABITypeKind.FixedBytes, false, 32, 32, width: 1);
        AssertNode(schema, 4, ABITypeKind.FixedBytes, false, 32, 32, width: 32);
        AssertNode(schema, 5, ABITypeKind.Fixed, false, 32, 32, width: 128, scale: 18);
        AssertNode(schema, 6, ABITypeKind.UFixed, false, 32, 32, width: 256, scale: 80);
        AssertNode(schema, 7, ABITypeKind.Function, false, 32, 32);
        AssertNode(schema, 8, ABITypeKind.Bool, false, 32, 32);
        AssertNode(schema, 9, ABITypeKind.Int, false, 32, 32, width: 248);
        AssertChildren(
            schema, 0,
            (1, 0, -1), (2, 32, -1), (3, 64, -1), (4, 96, -1), (5, 128, -1), (6, 160, -1), (7, 192, -1), (8, 224, -1), (9, 256, -1));
    }

    [TestMethod]
    public void BoundaryWidthsAndScales_AreAccepted()
    {
        var schema = CompileTuple("(uint8,uint256,int8,int256,bytes1,bytes32,fixed8x1,fixed256x80,ufixed8x1,ufixed256x80)");
        Assert.AreEqual(11, schema.NodeCount);
        Assert.AreEqual(320, schema.GetNode(0).StaticSize);
    }

    [TestMethod]
    public void StaticArrayOfBytes3_MatchesSolidityDocsBarExample()
    {
        // bar(bytes3[2]): one static 64-byte argument.
        var schema = CompileTuple("(bytes3[2])");

        Assert.AreEqual(3, schema.NodeCount);
        Assert.AreEqual(3, schema.MaxDepth);
        AssertNode(schema, 0, ABITypeKind.Tuple, false, 64, 64, headSize: 64, count: 1);
        AssertNode(schema, 1, ABITypeKind.FixedArray, false, staticSize: 64, headWidth: 64, headSize: 64, count: 2, element: 2, elementHeadWidth: 32);
        AssertNode(schema, 2, ABITypeKind.FixedBytes, false, 32, 32, width: 3);
        AssertChildren(schema, 0, (1, 0, ABISchema.NoSibling));
    }

    [TestMethod]
    public void MultiwordHeads_StaticArrayThenBoolThenBytes()
    {
        // (uint256[3],bool,bytes): head positions 0, 96, 128 (spec 10.1 item 7); head size 96+32+32 = 160.
        var schema = CompileTuple("(uint256[3],bool,bytes)");

        Assert.AreEqual(5, schema.NodeCount);
        Assert.AreEqual(3, schema.MaxDepth);
        Assert.IsTrue(schema.IsDynamic);
        AssertNode(schema, 0, ABITypeKind.Tuple, true, staticSize: ABISchema.NotStatic, headWidth: 32, headSize: 160, count: 3);
        AssertNode(schema, 1, ABITypeKind.FixedArray, false, 96, 96, headSize: 96, count: 3, element: 2, elementHeadWidth: 32);
        AssertNode(schema, 2, UInt, false, 32, 32, width: 256);
        AssertNode(schema, 3, ABITypeKind.Bool, false, 32, 32);
        AssertNode(schema, 4, ABITypeKind.Bytes, true, ABISchema.NotStatic, 32);
        AssertChildren(schema, 0, (1, 0, 2), (3, 96, 2), (4, 128, ABISchema.NoSibling));
    }

    [TestMethod]
    public void SolidityDocsF_MixesStaticAndDynamicMembers()
    {
        // f(uint256,uint32[],bytes10,bytes): positions 0 uint256, 1 uint32[], 2 bytes10, 3 bytes; offsets 0, 32, 64, 96.
        var schema = CompileTuple("(uint256,uint32[],bytes10,bytes)");

        Assert.AreEqual(6, schema.NodeCount);
        AssertNode(schema, 0, ABITypeKind.Tuple, true, ABISchema.NotStatic, 32, headSize: 128, count: 4);
        AssertNode(schema, 1, UInt, false, 32, 32, width: 256);
        AssertNode(schema, 2, ABITypeKind.DynamicArray, true, ABISchema.NotStatic, 32, headSize: 0, count: ABISchema.DynamicCount, element: 3, elementHeadWidth: 32);
        AssertNode(schema, 3, UInt, false, 32, 32, width: 32);
        AssertNode(schema, 4, ABITypeKind.FixedBytes, false, 32, 32, width: 10);
        AssertNode(schema, 5, ABITypeKind.Bytes, true, ABISchema.NotStatic, 32);
        AssertChildren(schema, 0, (1, 0, 1), (2, 32, 3), (4, 64, 3), (5, 96, ABISchema.NoSibling));
    }

    [TestMethod]
    public void SolidityDocsG_NestedDynamicArrays()
    {
        // g(uint256[][],string[])
        var schema = CompileTuple("(uint256[][],string[])");

        Assert.AreEqual(6, schema.NodeCount);
        Assert.AreEqual(4, schema.MaxDepth);
        AssertNode(schema, 0, ABITypeKind.Tuple, true, ABISchema.NotStatic, 32, headSize: 64, count: 2);
        AssertNode(schema, 1, ABITypeKind.DynamicArray, true, ABISchema.NotStatic, 32, count: ABISchema.DynamicCount, element: 2, elementHeadWidth: 32);
        AssertNode(schema, 2, ABITypeKind.DynamicArray, true, ABISchema.NotStatic, 32, count: ABISchema.DynamicCount, element: 3, elementHeadWidth: 32);
        AssertNode(schema, 3, UInt, false, 32, 32, width: 256);
        AssertNode(schema, 4, ABITypeKind.DynamicArray, true, ABISchema.NotStatic, 32, count: ABISchema.DynamicCount, element: 5, elementHeadWidth: 32);
        AssertNode(schema, 5, ABITypeKind.String, true, ABISchema.NotStatic, 32);
        AssertChildren(schema, 0, (1, 0, 1), (4, 32, ABISchema.NoSibling));
    }

    [TestMethod]
    public void DynamicArrayOfStaticElements_UsesTheElementStaticSizeAsStride()
    {
        // (uint256[2][]): the element uint256[2] is static, 64 bytes, so the array's element head width is 64, not 32.
        var schema = CompileTuple("(uint256[2][])");

        Assert.AreEqual(4, schema.NodeCount);
        AssertNode(schema, 1, ABITypeKind.DynamicArray, true, ABISchema.NotStatic, 32, count: ABISchema.DynamicCount, element: 2, elementHeadWidth: 64);
        AssertNode(schema, 2, ABITypeKind.FixedArray, false, 64, 64, headSize: 64, count: 2, element: 3, elementHeadWidth: 32);
        AssertChildren(schema, 0, (1, 0, ABISchema.NoSibling));
    }

    [TestMethod]
    public void MultiDimensionalFixedArray_ComposesSizesAndSuffixOrder()
    {
        // uint256[2][3]: three elements of uint256[2] => 3 * (2 * 32) = 192.
        var schema = CompileTuple("(uint256[2][3])");

        Assert.AreEqual(4, schema.NodeCount);
        AssertNode(schema, 0, ABITypeKind.Tuple, false, 192, 192, headSize: 192, count: 1);
        AssertNode(schema, 1, ABITypeKind.FixedArray, false, 192, 192, headSize: 192, count: 3, element: 2, elementHeadWidth: 64);
        AssertNode(schema, 2, ABITypeKind.FixedArray, false, 64, 64, headSize: 64, count: 2, element: 3, elementHeadWidth: 32);
        AssertNode(schema, 3, UInt, false, 32, 32, width: 256);
    }

    [TestMethod]
    public void StaticTupleInsideTuple_OccupiesItsFullWidthInTheHead()
    {
        // ((uint256,uint256),address): the inner static tuple is 64 bytes inline, so the address is at head offset 64, not 32.
        var schema = CompileTuple("((uint256,uint256),address)");

        Assert.AreEqual(5, schema.NodeCount);
        AssertNode(schema, 0, ABITypeKind.Tuple, false, 96, 96, headSize: 96, count: 2);
        AssertNode(schema, 1, ABITypeKind.Tuple, false, 64, 64, headSize: 64, count: 2);
        AssertNode(schema, 2, UInt, false, 32, 32, width: 256);
        AssertNode(schema, 3, UInt, false, 32, 32, width: 256);
        AssertNode(schema, 4, ABITypeKind.Address, false, 32, 32);
        AssertChildren(schema, 0, (1, 0, ABISchema.NoSibling), (4, 64, ABISchema.NoSibling));
        AssertChildren(schema, 1, (2, 0, ABISchema.NoSibling), (3, 32, ABISchema.NoSibling));
    }

    [TestMethod]
    public void UniswapV3ExactInputSingle_StructIsInlineInTheHead()
    {
        // exactInputSingle((address,address,uint24,address,uint256,uint256,uint160)): a static struct, 7 words, no offset word.
        var schema = CompileTuple("((address,address,uint24,address,uint256,uint256,uint160))");

        Assert.AreEqual(9, schema.NodeCount);
        Assert.IsFalse(schema.IsDynamic);
        AssertNode(schema, 0, ABITypeKind.Tuple, false, 224, 224, headSize: 224, count: 1);
        AssertNode(schema, 1, ABITypeKind.Tuple, false, 224, 224, headSize: 224, count: 7);
        AssertNode(schema, 4, UInt, false, 32, 32, width: 24);
        AssertNode(schema, 8, UInt, false, 32, 32, width: 160);
        AssertChildren(
            schema, 1,
            (2, 0, -1), (3, 32, -1), (4, 64, -1), (5, 96, -1), (6, 128, -1), (7, 160, -1), (8, 192, -1));
    }

    [TestMethod]
    public void ArrayOfDynamicTuples_OffsetsAreRelativeToEachTupleSoThePerTupleTableMatters()
    {
        // ((address,uint256,bytes)[]): the element tuple is dynamic (head 96, bytes tail), so the array stride is 32.
        var schema = CompileTuple("((address,uint256,bytes)[])");

        Assert.AreEqual(6, schema.NodeCount);
        AssertNode(schema, 1, ABITypeKind.DynamicArray, true, ABISchema.NotStatic, 32, count: ABISchema.DynamicCount, element: 2, elementHeadWidth: 32);
        AssertNode(schema, 2, ABITypeKind.Tuple, true, ABISchema.NotStatic, 32, headSize: 96, count: 3);
        AssertChildren(schema, 2, (3, 0, 2), (4, 32, 2), (5, 64, ABISchema.NoSibling));
        AssertChildren(schema, 0, (1, 0, ABISchema.NoSibling));
    }

    [TestMethod]
    public void NextDynamicSibling_SkipsStaticMembers_AndPointsAtTheFollowingDynamicOne()
    {
        // positions: 0 bytes, 1 uint, 2 uint, 3 string, 4 address, 5 bytes
        var schema = CompileTuple("(bytes,uint256,uint256,string,address,bytes)");

        AssertNode(schema, 0, ABITypeKind.Tuple, true, ABISchema.NotStatic, 32, headSize: 192, count: 6);
        AssertChildren(schema, 0, (1, 0, 3), (2, 32, 3), (3, 64, 3), (4, 96, 5), (5, 128, 5), (6, 160, ABISchema.NoSibling));
        // The offset WORD of position 0's next dynamic sibling (position 3) is at headBase + 96.
        Assert.AreEqual(96, schema.GetTupleChildren(0)[schema.GetTupleChildren(0)[0].NextDynamicSibling].HeadOffset);
    }

    [TestMethod]
    public void OnlyStaticMembers_AllSiblingsAreNone()
    {
        var schema = CompileTuple("(uint256,bool,address[2],(uint8,uint8))");
        foreach (var child in schema.GetTupleChildren(0))
        {
            Assert.AreEqual(ABISchema.NoSibling, child.NextDynamicSibling);
        }
    }

    [TestMethod]
    public void SingleDynamicMember_HasNoSiblingAnywhere()
    {
        var schema = CompileTuple("(bytes)");
        AssertChildren(schema, 0, (1, 0, ABISchema.NoSibling));
        AssertNode(schema, 0, ABITypeKind.Tuple, true, ABISchema.NotStatic, 32, headSize: 32, count: 1);
    }

    [TestMethod]
    public void FixedArrayOfDynamicElements_HasAPointerTableHeadAndIsDynamic()
    {
        // (string[2]): 2 offset words in the array's own head (64 bytes) but only 32 bytes in the root head.
        var schema = CompileTuple("(string[2])");

        Assert.AreEqual(3, schema.NodeCount);
        AssertNode(schema, 0, ABITypeKind.Tuple, true, ABISchema.NotStatic, 32, headSize: 32, count: 1);
        AssertNode(schema, 1, ABITypeKind.FixedArray, true, ABISchema.NotStatic, 32, headSize: 64, count: 2, element: 2, elementHeadWidth: 32);
        AssertNode(schema, 2, ABITypeKind.String, true, ABISchema.NotStatic, 32);
    }

    [TestMethod]
    public void FixedArrayOfDynamicTuples_IsDynamic()
    {
        var schema = CompileTuple("((bytes,uint256)[3],bool)");

        AssertNode(schema, 0, ABITypeKind.Tuple, true, ABISchema.NotStatic, 32, headSize: 64, count: 2);
        AssertNode(schema, 1, ABITypeKind.FixedArray, true, ABISchema.NotStatic, 32, headSize: 96, count: 3, element: 2, elementHeadWidth: 32);
        AssertNode(schema, 2, ABITypeKind.Tuple, true, ABISchema.NotStatic, 32, headSize: 64, count: 2);
        AssertChildren(schema, 0, (1, 0, ABISchema.NoSibling), (5, 32, ABISchema.NoSibling));
        AssertChildren(schema, 2, (3, 0, ABISchema.NoSibling), (4, 32, ABISchema.NoSibling));
    }

    [TestMethod]
    public void NestedEmptyTuples_AreStaticAndZeroSized()
    {
        // (()[],()[3],((),())): nodes 0 root, 1 ()[], 2 (), 3 ()[3], 4 (), 5 ((),()), 6 (), 7 ()
        var schema = CompileTuple("(()[],()[3],((),()))");

        Assert.AreEqual(8, schema.NodeCount);
        Assert.AreEqual(3, schema.MaxDepth);
        AssertNode(schema, 0, ABITypeKind.Tuple, true, ABISchema.NotStatic, 32, headSize: 32, count: 3);
        AssertNode(schema, 1, ABITypeKind.DynamicArray, true, ABISchema.NotStatic, 32, count: ABISchema.DynamicCount, element: 2, elementHeadWidth: 0);
        AssertNode(schema, 2, ABITypeKind.Tuple, false, 0, 0, headSize: 0, count: 0);
        AssertNode(schema, 3, ABITypeKind.FixedArray, false, 0, 0, headSize: 0, count: 3, element: 4, elementHeadWidth: 0);
        AssertNode(schema, 4, ABITypeKind.Tuple, false, 0, 0, headSize: 0, count: 0);
        AssertNode(schema, 5, ABITypeKind.Tuple, false, 0, 0, headSize: 0, count: 2);
        // Zero-width members all sit at the same head offset; the static members after the dynamic one start at 32.
        AssertChildren(schema, 0, (1, 0, ABISchema.NoSibling), (3, 32, ABISchema.NoSibling), (5, 32, ABISchema.NoSibling));
        AssertChildren(schema, 5, (6, 0, ABISchema.NoSibling), (7, 0, ABISchema.NoSibling));
        AssertChildren(schema, 2);
    }

    [TestMethod]
    public void MulticallBytesArray_IsOneDynamicHeadWord()
    {
        var schema = CompileTuple("(bytes[])");

        Assert.AreEqual(3, schema.NodeCount);
        AssertNode(schema, 0, ABITypeKind.Tuple, true, ABISchema.NotStatic, 32, headSize: 32, count: 1);
        AssertNode(schema, 1, ABITypeKind.DynamicArray, true, ABISchema.NotStatic, 32, count: ABISchema.DynamicCount, element: 2, elementHeadWidth: 32);
        AssertNode(schema, 2, ABITypeKind.Bytes, true, ABISchema.NotStatic, 32);
    }

    [TestMethod]
    public void MaxDepth_CountsPrimitivesAndNestingFromOne()
    {
        Assert.AreEqual(1, CompileTuple("()").MaxDepth);
        Assert.AreEqual(2, CompileTuple("(uint8)").MaxDepth);
        Assert.AreEqual(3, CompileTuple("((uint8))").MaxDepth);
        Assert.AreEqual(3, CompileTuple("(uint8[])").MaxDepth);
        Assert.AreEqual(5, CompileTuple("(uint8,(bool,(address[])))").MaxDepth);
    }

    [TestMethod]
    public void LargeStaticArray_AtTheDefaultStaticBudget_Compiles()
    {
        // uint256[4096] = 131,072 bytes, within the 256 KiB static default and the 4,096 fixed-array default.
        var schema = CompileTuple("(uint256[4096])");
        AssertNode(schema, 1, ABITypeKind.FixedArray, false, 131_072, 131_072, headSize: 131_072, count: 4096, element: 2, elementHeadWidth: 32);
    }

    [TestMethod]
    public void RealWorldSwapSignature_ProducesTheExpectedHead()
    {
        // swapExactTokensForTokens(uint256,uint256,address[],address,uint256): head 5 words, the array offset is word 2.
        var schema = CompileTuple("(uint256,uint256,address[],address,uint256)");
        AssertNode(schema, 0, ABITypeKind.Tuple, true, ABISchema.NotStatic, 32, headSize: 160, count: 5);
        AssertChildren(schema, 0, (1, 0, 2), (2, 32, 2), (3, 64, ABISchema.NoSibling), (5, 96, ABISchema.NoSibling), (6, 128, ABISchema.NoSibling));
    }

    [TestMethod]
    public void SharedDefinitionUsedTwice_IsNotMistakenForACycle()
    {
        var shared = new ABITupleDefinition(ABITypeDefinition.UInt(8));
        var root = new ABITupleDefinition(ABITypeDefinition.Tuple(shared), ABITypeDefinition.Tuple(shared));
        Assert.IsTrue(ABISchemaCompiler.TryCompileArguments(root, ABICompileLimits.Default, out var schema, out var error), error.ToString());

        Assert.AreEqual(5, schema.NodeCount, "no subtree sharing today: every occurrence has its own node");
        Assert.AreEqual("((uint8),(uint8))", schema.CanonicalType);
        AssertChildren(schema, 0, (1, 0, ABISchema.NoSibling), (3, 32, ABISchema.NoSibling));
    }

    [TestMethod]
    public void TableInvariants_HoldForEveryShape()
    {
        string[] shapes =
        [
            "()", "(address,uint256)", "(uint256[3],bool,bytes)", "(uint256,uint32[],bytes10,bytes)", "(uint256[][],string[])",
            "(()[],()[3],((),()))", "((bytes,uint256)[3],bool)", "((address,uint256,bytes)[])", "(uint256[2][3])",
            "(bytes,uint256,uint256,string,address,bytes)", "((uint8,(bool,(address[]))),bytes[2][])"
        ];

        foreach (string shape in shapes)
        {
            var schema = CompileTuple(shape);
            for (int id = 0; id < schema.NodeCount; id++)
            {
                ref readonly var node = ref schema.GetNode(id);
                Assert.AreEqual(node.IsDynamic ? 32 : node.StaticSize, node.HeadWidth, $"{shape} node {id}: HeadWidth");
                Assert.AreEqual(node.IsDynamic, node.StaticSize == ABISchema.NotStatic, $"{shape} node {id}: StaticSize sentinel");
                if (node.Element != ABISchema.NoNode)
                {
                    Assert.IsTrue(node.Element > id && node.Element < schema.NodeCount, $"{shape} node {id}: element id is a later node");
                    Assert.AreEqual(schema.GetNode(node.Element).HeadWidth, node.ElementHeadWidth, $"{shape} node {id}: ElementHeadWidth");
                }

                if (node.Kind == ABITypeKind.FixedArray)
                {
                    Assert.AreEqual(node.Count * node.ElementHeadWidth, node.HeadSize, $"{shape} node {id}: array HeadSize");
                    Assert.IsTrue(node.Count >= 1);
                }

                if (node.Kind == ABITypeKind.Tuple)
                {
                    var children = schema.GetTupleChildren(id);
                    Assert.AreEqual(node.Count, children.Length);
                    int offset = 0;
                    for (int i = 0; i < children.Length; i++)
                    {
                        Assert.IsTrue(children[i].NodeId > id, $"{shape} node {id}: child ids are larger");
                        Assert.AreEqual(offset, children[i].HeadOffset, $"{shape} node {id} child {i}: offset");
                        offset += schema.GetNode(children[i].NodeId).HeadWidth;

                        int expectedNext = ABISchema.NoSibling;
                        for (int j = children.Length - 1; j > i; j--)
                        {
                            if (schema.GetNode(children[j].NodeId).IsDynamic)
                            {
                                expectedNext = j;
                            }
                        }

                        Assert.AreEqual(expectedNext, children[i].NextDynamicSibling, $"{shape} node {id} child {i}: next dynamic");
                    }

                    Assert.AreEqual(offset, node.HeadSize, $"{shape} node {id}: tuple HeadSize");
                }
                else
                {
                    Assert.IsTrue(schema.GetTupleChildren(id).IsEmpty, "non-tuples have no children");
                }
            }
        }
    }
}
