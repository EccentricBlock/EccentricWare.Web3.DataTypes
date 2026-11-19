namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>Every independently produced fixture with its canonical argument tuple text; calls carry a four-byte selector first.</summary>
internal static class ABIFixtureCatalog
{
    internal const string SeaportOrderTuple =
        "(((address,address,(uint8,address,uint256,uint256,uint256)[],(uint8,address,uint256,uint256,uint256,address)[],uint8,uint256,uint256,bytes32,uint256,bytes32,uint256),bytes),bytes32)";

    internal const string ExactInputSingleTuple = "((address,address,uint24,address,uint256,uint256,uint256,uint160))";

    internal sealed record Fixture(string Name, string Tuple, string Hex, bool IsCall);

    internal static readonly Fixture[] All =
    [
        Call(nameof(ABIFixtures.TransferVitalik1Eth), "(address,uint256)", ABIFixtures.TransferVitalik1Eth),
        Call(nameof(ABIFixtures.ApproveRouterMax), "(address,uint256)", ABIFixtures.ApproveRouterMax),
        Call(nameof(ABIFixtures.ApproveZeroZero), "(address,uint256)", ABIFixtures.ApproveZeroZero),
        Call(nameof(ABIFixtures.BalanceOfVitalik), "(address)", ABIFixtures.BalanceOfVitalik),
        Call(nameof(ABIFixtures.ExactInputSingleWethToUsdc), ExactInputSingleTuple, ABIFixtures.ExactInputSingleWethToUsdc),
        Call(nameof(ABIFixtures.ExactInputSingleUsdcToWeth), ExactInputSingleTuple, ABIFixtures.ExactInputSingleUsdcToWeth),
        Call(nameof(ABIFixtures.MulticallTwoExactInputSingle), "(bytes[])", ABIFixtures.MulticallTwoExactInputSingle),
        Call(nameof(ABIFixtures.MulticallSwapAndRefundEth), "(bytes[])", ABIFixtures.MulticallSwapAndRefundEth),
        Call(nameof(ABIFixtures.MulticallTwoTransfers388), "(bytes[])", ABIFixtures.MulticallTwoTransfers388),
        Call(nameof(ABIFixtures.SeaportFulfillOrder), SeaportOrderTuple, ABIFixtures.SeaportFulfillOrder),
        Call(nameof(ABIFixtures.SeaportFulfillOrderEmptySignature), SeaportOrderTuple, ABIFixtures.SeaportFulfillOrderEmptySignature),
        Call(nameof(ABIFixtures.DocsBaz69True), "(uint32,bool)", ABIFixtures.DocsBaz69True),
        Call(nameof(ABIFixtures.DocsBarAbcDef), "(bytes3[2])", ABIFixtures.DocsBarAbcDef),
        Call(nameof(ABIFixtures.DocsSamDaveTrue123), "(bytes,bool,uint256[])", ABIFixtures.DocsSamDaveTrue123),
        Call(nameof(ABIFixtures.DocsFExample), "(uint256,uint32[],bytes10,bytes)", ABIFixtures.DocsFExample),
        Call(nameof(ABIFixtures.DocsGNestedArrays), "(uint256[][],string[])", ABIFixtures.DocsGNestedArrays),
        Args(nameof(ABIFixtures.ScalarsMixedMaxima), "(uint8,uint16,uint64,uint128,uint256)", ABIFixtures.ScalarsMixedMaxima),
        Args(nameof(ABIFixtures.ScalarsMixedMinima), "(int8,int16,int64,int128,int256)", ABIFixtures.ScalarsMixedMinima),
        Args(nameof(ABIFixtures.ScalarsMixedPositiveMaxima), "(int8,int16,int64,int128,int256)", ABIFixtures.ScalarsMixedPositiveMaxima),
        Args(nameof(ABIFixtures.ScalarsBoolAddressBytes), "(bool,bool,address,bytes1,bytes24,bytes32)", ABIFixtures.ScalarsBoolAddressBytes),
        Args(nameof(ABIFixtures.ScalarFunctionPointer), "(function)", ABIFixtures.ScalarFunctionPointer),
        Args(nameof(ABIFixtures.Bytes0), "(bytes)", ABIFixtures.Bytes0),
        Args(nameof(ABIFixtures.Bytes1), "(bytes)", ABIFixtures.Bytes1),
        Args(nameof(ABIFixtures.Bytes31), "(bytes)", ABIFixtures.Bytes31),
        Args(nameof(ABIFixtures.Bytes32), "(bytes)", ABIFixtures.Bytes32),
        Args(nameof(ABIFixtures.Bytes33), "(bytes)", ABIFixtures.Bytes33),
        Args(nameof(ABIFixtures.Bytes63), "(bytes)", ABIFixtures.Bytes63),
        Args(nameof(ABIFixtures.Bytes64), "(bytes)", ABIFixtures.Bytes64),
        Args(nameof(ABIFixtures.Bytes65), "(bytes)", ABIFixtures.Bytes65),
        Args(nameof(ABIFixtures.String0), "(string)", ABIFixtures.String0),
        Args(nameof(ABIFixtures.String1), "(string)", ABIFixtures.String1),
        Args(nameof(ABIFixtures.String31), "(string)", ABIFixtures.String31),
        Args(nameof(ABIFixtures.String32), "(string)", ABIFixtures.String32),
        Args(nameof(ABIFixtures.String33), "(string)", ABIFixtures.String33),
        Args(nameof(ABIFixtures.String63), "(string)", ABIFixtures.String63),
        Args(nameof(ABIFixtures.String64), "(string)", ABIFixtures.String64),
        Args(nameof(ABIFixtures.String65), "(string)", ABIFixtures.String65),
        Args(nameof(ABIFixtures.StaticTupleInTuple), "((uint256,uint256),uint256)", ABIFixtures.StaticTupleInTuple),
        Args(nameof(ABIFixtures.MultiWordHeadThenBytes), "(uint256[3],bool,bytes)", ABIFixtures.MultiWordHeadThenBytes),
        Args(nameof(ABIFixtures.DynamicUintArray), "(uint256[])", ABIFixtures.DynamicUintArray),
        Args(nameof(ABIFixtures.EmptyUintArray), "(uint256[])", ABIFixtures.EmptyUintArray),
        Args(nameof(ABIFixtures.DynamicBytesArray), "(bytes[])", ABIFixtures.DynamicBytesArray),
        Args(nameof(ABIFixtures.DynamicStringArray), "(string[])", ABIFixtures.DynamicStringArray),
        Args(nameof(ABIFixtures.FixedUintArray3), "(uint256[3])", ABIFixtures.FixedUintArray3),
        Args(nameof(ABIFixtures.FixedBytesArray2), "(bytes[2])", ABIFixtures.FixedBytesArray2),
        Args(nameof(ABIFixtures.TupleArrayAddressUintBytes), "((address,uint256,bytes)[])", ABIFixtures.TupleArrayAddressUintBytes),
        Args(nameof(ABIFixtures.FixedArrayOfDynamicTuples), "((uint256,bytes)[2])", ABIFixtures.FixedArrayOfDynamicTuples),
        Args(nameof(ABIFixtures.ArrayOfArrays), "(uint256[][])", ABIFixtures.ArrayOfArrays),
        Args(nameof(ABIFixtures.FixedArrayOfFixedArrays), "(uint256[2][2])", ABIFixtures.FixedArrayOfFixedArrays),
        Args(nameof(ABIFixtures.DynamicArrayOfFixedArrays), "(uint256[2][])", ABIFixtures.DynamicArrayOfFixedArrays),
        Args(nameof(ABIFixtures.FixedArrayOfDynamicArrays), "(uint256[][2])", ABIFixtures.FixedArrayOfDynamicArrays),
        Args(nameof(ABIFixtures.NestedMixedTuples), "(uint256,(string,(uint8,bytes)[],address),uint256[2])", ABIFixtures.NestedMixedTuples),
        Args(nameof(ABIFixtures.EmptyDynamicsInsideNestedTuple), "((uint256,string,uint256[],bytes),bool)", ABIFixtures.EmptyDynamicsInsideNestedTuple),
        Args(nameof(ABIFixtures.DynamicTupleLastThenStatic), "(bytes,uint256,string,uint256)", ABIFixtures.DynamicTupleLastThenStatic),
        Args(nameof(ABIFixtures.MalformedUtf8Payload), "(string)", ABIFixtures.MalformedUtf8Payload),
        Args(nameof(ABIFixtures.Utf8MultiByteString), "(string)", ABIFixtures.Utf8MultiByteString),
    ];

    private static Fixture Call(string name, string tuple, string hex) => new(name, tuple, hex, IsCall: true);

    private static Fixture Args(string name, string tuple, string hex) => new(name, tuple, hex, IsCall: false);

    /// <summary>The argument document of a fixture (the selector removed for calls).</summary>
    internal static byte[] ArgumentBytes(Fixture fixture)
    {
        byte[] all = Convert.FromHexString(fixture.Hex);
        return fixture.IsCall ? all[4..] : all;
    }
}
