using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>ABICallData (spec 5, 6.1, 8.2): selector and size rules, one shared validator, coordinate translation, and real-chain call shapes.</summary>
[TestClass]
public sealed class ABICallDataTests
{
    private const string TransferSignature = "transfer(address,uint256)";
    private const string ExactInputSingleSignature = "exactInputSingle((address,address,uint24,address,uint256,uint256,uint256,uint160))";
    private const string FulfillOrderSignature =
        "fulfillOrder(((address,address,(uint8,address,uint256,uint256,uint256)[],(uint8,address,uint256,uint256,uint256,address)[],uint8,uint256,uint256,bytes32,uint256,bytes32,uint256),bytes),bytes32)";

    private static ABICallData DecodeCall(byte[] call, ABIFunctionSchema function, ABIDecodeLimits? limits = null)
    {
        var effective = limits ?? ABIDecodeLimits.Default;
        Assert.IsTrue(ABICallData.TryDecode(call, function, in effective, out var decoded, out var status, out var error), $"{status} {error}");
        Assert.AreEqual(ABIStatus.Success, status);
        Assert.IsTrue(decoded.IsValid);
        return decoded;
    }

    private static ABIError CallFails(byte[] call, ABIFunctionSchema? function, ABIStatus expected, ABIDecodeLimits? limits = null, string? why = null)
    {
        var effective = limits ?? ABIDecodeLimits.Default;
        bool ok = ABICallData.TryDecode(call, function!, in effective, out var decoded, out var status, out var error);
        Assert.IsFalse(ok, why);
        Assert.AreEqual(expected, status, why + " " + error);
        Assert.AreEqual(expected, error.Status, why);
        AssertDefault(decoded, why);
        return error;
    }

    private static void AssertDefault(in ABICallData call, string? why = null)
    {
        Assert.IsFalse(call.IsValid, why);
        Assert.AreEqual(0U, call.Selector, why);
        Assert.IsTrue(call.EncodedCall.IsEmpty, why);
        Assert.IsFalse(call.Arguments.IsValid, why);
        Assert.IsNull(call.Arguments.Schema, why);
        Assert.IsTrue(call.Arguments.EncodedBytes.IsEmpty, why);
    }

    private static ulong U64(in ABIData data, params int[] path)
    {
        Assert.IsTrue(data.TryGetUInt64(path, out ulong value, out var status), $"[{string.Join(",", path)}] {status}");
        return value;
    }

    // ------------------------------------------------------------------ successful decode

    [TestMethod]
    public void Transfer_Decodes_WithSelectorEncodedCallAndZeroCopyArguments()
    {
        var function = Function(TransferSignature);
        byte[] bytes = Hex(ABIFixtures.TransferVitalik1Eth);
        var call = DecodeCall(bytes, function);

        Assert.AreEqual(0xa9059cbbU, call.Selector);
        Assert.AreEqual(function.Selector, call.Selector);
        Assert.AreEqual(68, call.EncodedCall.Length);
        Assert.AreEqual(64, call.Arguments.EncodedBytes.Length);
        Assert.AreSame(function.InputSchema, call.Arguments.Schema);
        // Zero copy: the arguments are the same memory as the call bytes after the selector.
        Assert.IsTrue(Unsafe.AreSame(
            in MemoryMarshal.GetReference(call.EncodedCall.Slice(4)),
            in MemoryMarshal.GetReference(call.Arguments.EncodedBytes)));
        Assert.IsTrue(Unsafe.AreSame(in MemoryMarshal.GetReference(call.EncodedCall), in bytes[0]));

        // Coordinate zero of Arguments is call byte 4.
        AssertLocated(call.Arguments, [0], ABITypeKind.Address, (0, 32), (0, 32));
        AssertLocated(call.Arguments, [1], ABITypeKind.UInt, (32, 32), (32, 32));
        Assert.IsTrue(call.TryTranslateArgumentRange(Locate(call.Arguments, 1).ValueRange, out ABIRange amountInCall));
        Assert.AreEqual(new ABIRange(36, 32), amountInCall);
        CollectionAssert.AreEqual(bytes[36..68], call.EncodedCall.Slice(amountInCall.Offset, amountInCall.Length).ToArray());
    }

    [TestMethod]
    public void SelectorOnlyCall_IsValidForAZeroArgumentFunction_AndRejectsTrailingBytes()
    {
        var function = Function("refundETH()");
        Assert.AreEqual(0x12210e8aU, function.Selector);
        var call = DecodeCall(Hex("12210e8a"), function);
        Assert.AreEqual(4, call.EncodedCall.Length);
        Assert.IsTrue(call.Arguments.IsValid, "a valid but empty argument view is still valid");
        Assert.AreEqual(0, call.Arguments.EncodedBytes.Length);
        AssertLocated(call.Arguments, [], ABITypeKind.Tuple, (0, 0), (0, 0));

        CallFails(Hex("12210e8a00"), function, ABIStatus.NonCanonicalLayout, why: "one trailing byte");
        CallFails(Hex("12210e8a" + new string('0', 64)), function, ABIStatus.NonCanonicalLayout, why: "one trailing word");
    }

    [TestMethod]
    public void SelectorOnly_ForAFunctionWithArguments_IsTruncated()
    {
        CallFails(Hex("a9059cbb"), Function(TransferSignature), ABIStatus.Truncated);
    }

    // ------------------------------------------------------------------ order of checks (spec 5.2)

    [TestMethod]
    public void NullFunction_AndInvalidLimits_AreInvalidArgument_BeforeAnyOtherCheck()
    {
        var function = Function(TransferSignature);
        byte[] good = Hex(ABIFixtures.TransferVitalik1Eth);
        CallFails(good, null, ABIStatus.InvalidArgument, why: "null function");
        CallFails([], null, ABIStatus.InvalidArgument, why: "null function beats empty input");

        foreach (var bad in new[]
        {
            default(ABIDecodeLimits), Limits(bytes: 0), Limits(bytes: -1), Limits(depth: 0), Limits(depth: 33), Limits(array: 0), Limits(visited: 0), Limits(work: 0), Limits(work: -5),
        })
        {
            CallFails(good, function, ABIStatus.InvalidArgument, bad, $"limits {bad}");
            CallFails(new byte[1_000], function, ABIStatus.InvalidArgument, bad, $"invalid limits beat the size check {bad}");
            CallFails([], function, ABIStatus.InvalidArgument, bad, $"invalid limits beat the selector check {bad}");
        }
    }

    [TestMethod]
    public void MaxEncodedBytes_CoversTheWholeCallIncludingTheSelector()
    {
        var function = Function(TransferSignature);
        byte[] good = Hex(ABIFixtures.TransferVitalik1Eth); // 68 bytes = 4 + 64
        DecodeCall(good, function, Limits(bytes: 68));
        DecodeCall(good, function, Limits(bytes: 69));
        CallFails(good, function, ABIStatus.InputTooLarge, Limits(bytes: 67), "limit - 1: the arguments alone (64) would fit 67, the call does not");
        CallFails(good, function, ABIStatus.InputTooLarge, Limits(bytes: 64), "limit equal to the argument size only");
        CallFails(good, function, ABIStatus.InputTooLarge, Limits(bytes: 1), "limit 1");
    }

    [TestMethod]
    public void Size_IsCheckedBeforeTheSelector_AndTheSelectorBeforeTheArguments()
    {
        var function = Function(TransferSignature);
        byte[] oversizedWrongSelector = [.. Hex("deadbeef"), .. new byte[100]];
        CallFails(oversizedWrongSelector, function, ABIStatus.InputTooLarge, Limits(bytes: 50), "size beats selector");

        byte[] wrongSelectorGarbageArgs = [.. Hex("deadbeef"), .. Filled(0xff), .. Filled(0xff)];
        CallFails(wrongSelectorGarbageArgs, function, ABIStatus.InvalidSelector, why: "selector beats arguments");
    }

    [TestMethod]
    public void ShortCalls_AreInvalidSelector()
    {
        var function = Function(TransferSignature);
        for (int length = 0; length < 4; length++)
        {
            CallFails(Hex("a9059cbb")[..length], function, ABIStatus.InvalidSelector, why: $"{length} bytes");
        }

        // The same prefix is fine for decoding only once the selector is whole.
        CallFails(Hex("a9059cbb"), function, ABIStatus.Truncated);
    }

    [TestMethod]
    public void SelectorMismatch_OfOtherwiseValidArguments_IsInvalidSelector()
    {
        var function = Function(TransferSignature);
        byte[] approve = Hex(ABIFixtures.ApproveRouterMax); // same argument shape, selector 0x095ea7b3
        CollectionAssert.AreEqual(Hex("095ea7b3"), approve[..4]);
        CallFails(approve, function, ABIStatus.InvalidSelector, why: "approve bytes under the transfer schema");

        // A single flipped bit anywhere in the selector fails.
        byte[] good = Hex(ABIFixtures.TransferVitalik1Eth);
        for (int bit = 0; bit < 32; bit++)
        {
            byte[] flipped = (byte[])good.Clone();
            flipped[bit / 8] ^= (byte)(0x80 >> (bit % 8));
            CallFails(flipped, function, ABIStatus.InvalidSelector, why: $"selector bit {bit}");
        }
    }

    [TestMethod]
    public void ArgumentErrors_AreReportedWithArgumentRelativeOffsets()
    {
        var function = Function("f(uint8)");
        byte[] call = [.. Hex(function.Selector.ToString("x8")), .. Word(256)]; // uint8 = 256
        ABIError error = CallFails(call, function, ABIStatus.InvalidScalar);
        Assert.AreEqual(0, error.DocumentOffset, "offset is relative to the arguments (call byte 4), not the call");
        Assert.AreEqual(1, error.SchemaNode, "node 1 is the uint8");
    }

    // ------------------------------------------------------------------ defaults

    [TestMethod]
    public void DefaultCallData_IsInvalid_AndEveryMemberIsDefault()
    {
        ABICallData call = default;
        AssertDefault(call);
        Assert.IsFalse(call.TryTranslateArgumentRange(new ABIRange(0, 0), out ABIRange translated));
        Assert.AreEqual(default, translated);
        Assert.IsFalse(call.Arguments.TryLocate([], out _, out var status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
    }

    // ------------------------------------------------------------------ TryTranslateArgumentRange

    [TestMethod]
    public void TranslateArgumentRange_AddsFourBytes_AndChecksBoundsBySubtraction()
    {
        var call = DecodeCall(Hex(ABIFixtures.TransferVitalik1Eth), Function(TransferSignature)); // arguments: 64 bytes

        foreach (((int Offset, int Length) input, (int Offset, int Length) expected) in new[]
        {
            ((0, 0), (4, 0)), ((0, 64), (4, 64)), ((32, 32), (36, 32)), ((64, 0), (68, 0)), ((63, 1), (67, 1)), ((10, 7), (14, 7)),
        })
        {
            Assert.IsTrue(call.TryTranslateArgumentRange(new ABIRange(input.Offset, input.Length), out ABIRange translated), $"{input}");
            Assert.AreEqual(new ABIRange(expected.Offset, expected.Length), translated);
        }

        foreach ((int Offset, int Length) bad in new[]
        {
            (0, 65), (1, 64), (64, 1), (65, 0), (-1, 0), (0, -1), (-1, -1), (int.MinValue, 0), (0, int.MinValue),
            (int.MaxValue, 0), (0, int.MaxValue), (int.MaxValue, int.MaxValue), (int.MaxValue - 3, 4), (32, int.MaxValue - 31),
        })
        {
            Assert.IsFalse(call.TryTranslateArgumentRange(new ABIRange(bad.Offset, bad.Length), out ABIRange translated), $"{bad}");
            Assert.AreEqual(default, translated, $"{bad}");
        }
    }

    [TestMethod]
    public void TranslateArgumentRange_IsTranslationOnly_ItAcceptsAnyInBoundsRange()
    {
        // Documented behaviour: a range that is not a validated field (for example the middle of the address word) still translates.
        var call = DecodeCall(Hex(ABIFixtures.TransferVitalik1Eth), Function(TransferSignature));
        Assert.IsTrue(call.TryTranslateArgumentRange(new ABIRange(5, 3), out ABIRange translated));
        Assert.AreEqual(new ABIRange(9, 3), translated);
    }

    // ------------------------------------------------------------------ multicall(bytes[]) (spec 8.2)

    [TestMethod]
    public void Multicall388_HasTheSpecRangesInArgumentAndCallCoordinates()
    {
        var multicall = Function("multicall(bytes[])");
        Assert.AreEqual(0xac9650d8U, multicall.Selector);
        byte[] bytes = Hex(ABIFixtures.MulticallTwoTransfers388);
        Assert.AreEqual(388, bytes.Length);

        // Spec 8.2 table in argument coordinates (call offset - 4): pointer 0x20 at 0, count 2 at 32, offsets 0x40 and 0xc0 at 64 and 96,
        // length words 0x44 at 128 and 256.
        byte[] arguments = bytes[4..];
        CollectionAssert.AreEqual(Word(0x20), arguments[0..32]);
        CollectionAssert.AreEqual(Word(2), arguments[32..64]);
        CollectionAssert.AreEqual(Word(0x40), arguments[64..96]);
        CollectionAssert.AreEqual(Word(0xc0), arguments[96..128]);
        CollectionAssert.AreEqual(Word(0x44), arguments[128..160]);
        CollectionAssert.AreEqual(Word(0x44), arguments[256..288]);

        var call = DecodeCall(bytes, multicall);
        Assert.IsTrue(call.Arguments.TryGetBytePayload([0, 0], out var first, out ABIRange firstRange, out _));
        Assert.IsTrue(call.Arguments.TryGetBytePayload([0, 1], out var second, out ABIRange secondRange, out _));
        Assert.AreEqual(new ABIRange(160, 68), firstRange);
        Assert.AreEqual(new ABIRange(288, 68), secondRange);
        Assert.IsTrue(call.TryTranslateArgumentRange(firstRange, out ABIRange firstInCall));
        Assert.IsTrue(call.TryTranslateArgumentRange(secondRange, out ABIRange secondInCall));
        Assert.AreEqual(new ABIRange(164, 68), firstInCall);
        Assert.AreEqual(new ABIRange(292, 68), secondInCall);

        // The call-relative ranges slice the same bytes out of the whole call.
        CollectionAssert.AreEqual(first.ToArray(), bytes[164..232]);
        CollectionAssert.AreEqual(second.ToArray(), bytes[292..360]);
        CollectionAssert.AreEqual(Hex(ABIFixtures.TransferChild1), first.ToArray());
        CollectionAssert.AreEqual(Hex(ABIFixtures.TransferChild2), second.ToArray());

        AssertLocated(call.Arguments, [], ABITypeKind.Tuple, (0, 32), (0, 384));
        AssertLocated(call.Arguments, [0], ABITypeKind.DynamicArray, (0, 32), (32, 352));
        AssertLocated(call.Arguments, [0, 0], ABITypeKind.Bytes, (64, 32), (128, 128));
        AssertLocated(call.Arguments, [0, 1], ABITypeKind.Bytes, (96, 32), (256, 128));
    }

    [TestMethod]
    public void Multicall388_IsReproducedByTheIndependentReferenceEncoder()
    {
        // The spec asks for the fixture to be reconstructed by a canonical encoder, not hard-coded: cast produced the bytes, and the
        // test-local reference encoder (a different implementation, no shared code) must produce the same 384 argument bytes.
        var type = ABIReference.ParseTuple("(bytes[])");
        var value = new RSeq([new RSeq([new RBlob(Hex(ABIFixtures.TransferChild1)), new RBlob(Hex(ABIFixtures.TransferChild2))])]);
        byte[] reference = ABIReference.EncodeRoot(type, value);
        Assert.AreEqual(384, reference.Length);
        CollectionAssert.AreEqual(Hex(ABIFixtures.MulticallTwoTransfers388)[4..], reference);
    }

    [TestMethod]
    public void MulticallChildren_StartNewCoordinateSystems_AndOpaqueBytesAreNotAutoDecoded()
    {
        var multicall = Function("multicall(bytes[])");
        var transfer = Function(TransferSignature);
        var call = DecodeCall(Hex(ABIFixtures.MulticallTwoTransfers388), multicall);
        Assert.IsTrue(call.Arguments.TryGetBytePayload([0, 1], out var child, out _, out _));

        // The child is decoded explicitly with its own trusted schema; its coordinate zero is its own byte 4.
        var childCall = DecodeCall(child.ToArray(), transfer);
        Assert.AreEqual(0xa9059cbbU, childCall.Selector);
        AssertLocated(childCall.Arguments, [1], ABITypeKind.UInt, (32, 32), (32, 32));
        Assert.AreEqual(42UL, U64(childCall.Arguments, 1));
        Assert.IsTrue(childCall.TryTranslateArgumentRange(Locate(childCall.Arguments, 1).ValueRange, out ABIRange inChild));
        Assert.AreEqual(new ABIRange(36, 32), inChild, "child-relative, not outer-call-relative (292 + 36)");

        // A payload that merely CONTAINS a known selector stays opaque to the outer decode, and fails as a call under the wrong schema.
        byte[] garbageChild = [.. Hex("a9059cbb"), 0xde, 0xad];
        byte[] outer = [.. Hex("ac9650d8"), .. Word(0x20), .. Word(1), .. Word(0x20), .. BytesValue(garbageChild)];
        var opaque = DecodeCall(outer, multicall);
        Assert.IsTrue(opaque.Arguments.TryGetBytePayload([0, 0], out var payload, out _, out _));
        CollectionAssert.AreEqual(garbageChild, payload.ToArray());
        CallFails(payload.ToArray(), transfer, ABIStatus.Truncated, why: "opaque bytes are not a valid transfer call");
    }

    // ------------------------------------------------------------------ real-chain shapes

    [TestMethod]
    public void UniswapV3ExactInputSingle_IsOneStaticTupleOfEightWords()
    {
        var function = Function(ExactInputSingleSignature);
        Assert.AreEqual(0x414bf389U, function.Selector);
        byte[] bytes = Hex(ABIFixtures.ExactInputSingleWethToUsdc);
        Assert.AreEqual(4 + 8 * 32, bytes.Length);
        var call = DecodeCall(bytes, function);

        AssertLocated(call.Arguments, [], ABITypeKind.Tuple, (0, 256), (0, 256));
        AssertLocated(call.Arguments, [0], ABITypeKind.Tuple, (0, 256), (0, 256));
        ABITypeKind[] kinds = [ABITypeKind.Address, ABITypeKind.Address, ABITypeKind.UInt, ABITypeKind.Address, ABITypeKind.UInt, ABITypeKind.UInt, ABITypeKind.UInt, ABITypeKind.UInt];
        for (int field = 0; field < 8; field++)
        {
            AssertLocated(call.Arguments, [0, field], kinds[field], (field * 32, 32), (field * 32, 32));
        }

        Assert.IsTrue(call.Arguments.TryGetAddress([0, 0], out var tokenIn, out _));
        CollectionAssert.AreEqual(Hex("c02aaa39b223fe8d0a0e5c4f27ead9083c756cc2"), tokenIn.ToArray());
        Assert.IsTrue(call.Arguments.TryGetAddress([0, 1], out var tokenOut, out _));
        CollectionAssert.AreEqual(Hex("a0b86991c6218b36c1d19d4a2e9eb0ce3606eb48"), tokenOut.ToArray());
        Assert.AreEqual(3_000UL, U64(call.Arguments, 0, 2), "uint24 fee tier");
        Assert.IsTrue(call.Arguments.TryGetAddress([0, 3], out var recipient, out _));
        CollectionAssert.AreEqual(Hex("d8da6bf26964af9d7eed9e03e53415d37aa96045"), recipient.ToArray());
        Assert.AreEqual(1_700_000_000UL, U64(call.Arguments, 0, 4), "deadline");
        Assert.AreEqual(1_000_000_000_000_000_000UL, U64(call.Arguments, 0, 5), "amountIn");
        Assert.AreEqual(1_800_000_000UL, U64(call.Arguments, 0, 6), "amountOutMinimum");
        Assert.AreEqual(0UL, U64(call.Arguments, 0, 7), "sqrtPriceLimitX96");

        var second = DecodeCall(Hex(ABIFixtures.ExactInputSingleUsdcToWeth), function);
        Assert.AreEqual(500UL, U64(second.Arguments, 0, 2));
        Assert.AreEqual(4_295_128_740UL, U64(second.Arguments, 0, 7), "uint160 value that exceeds 32 bits but fits 64");
        Assert.AreEqual(2_500_000_000UL, U64(second.Arguments, 0, 5));
    }

    [TestMethod]
    public void UniswapMulticall_CarryingTwoSwaps_LocatesBothChildCallsAndTheyDecodeIndependently()
    {
        var multicall = Function("multicall(bytes[])");
        var swap = Function(ExactInputSingleSignature);
        byte[] bytes = Hex(ABIFixtures.MulticallTwoExactInputSingle);
        // Each child: 260 bytes -> element = 32 + 288 = 320. Arguments: 32 + 32 + 64 + 640 = 768; call = 772.
        Assert.AreEqual(772, bytes.Length);
        var call = DecodeCall(bytes, multicall);

        Assert.IsTrue(call.Arguments.TryGetBytePayload([0, 0], out var first, out ABIRange firstRange, out _));
        Assert.IsTrue(call.Arguments.TryGetBytePayload([0, 1], out var second, out ABIRange secondRange, out _));
        Assert.AreEqual(new ABIRange(160, 260), firstRange);
        Assert.AreEqual(new ABIRange(480, 260), secondRange);
        AssertLocated(call.Arguments, [0, 0], ABITypeKind.Bytes, (64, 32), (128, 320));
        AssertLocated(call.Arguments, [0, 1], ABITypeKind.Bytes, (96, 32), (448, 320));
        Assert.IsTrue(call.TryTranslateArgumentRange(secondRange, out ABIRange secondInCall));
        Assert.AreEqual(new ABIRange(484, 260), secondInCall);

        CollectionAssert.AreEqual(Hex(ABIFixtures.ExactInputSingleWethToUsdc), first.ToArray());
        CollectionAssert.AreEqual(Hex(ABIFixtures.ExactInputSingleUsdcToWeth), second.ToArray());
        var child0 = DecodeCall(first.ToArray(), swap);
        var child1 = DecodeCall(second.ToArray(), swap);
        Assert.AreEqual(1_000_000_000_000_000_000UL, U64(child0.Arguments, 0, 5));
        Assert.AreEqual(2_500_000_000UL, U64(child1.Arguments, 0, 5));
    }

    [TestMethod]
    public void UniswapMulticall_WithASelectorOnlyChild_HasAFourByteEmptyPaddedPayload()
    {
        var multicall = Function("multicall(bytes[])");
        byte[] bytes = Hex(ABIFixtures.MulticallSwapAndRefundEth);
        // Child 1 is 260 bytes (element 320), child 2 is 4 bytes (element 32 + 32 = 64): arguments 32 + 32 + 64 + 320 + 64 = 512; call 516.
        Assert.AreEqual(516, bytes.Length);
        var call = DecodeCall(bytes, multicall);
        Assert.IsTrue(call.Arguments.TryGetBytePayload([0, 1], out var refund, out ABIRange range, out _));
        Assert.AreEqual(new ABIRange(480, 4), range);
        AssertLocated(call.Arguments, [0, 1], ABITypeKind.Bytes, (96, 32), (448, 64));
        CollectionAssert.AreEqual(Hex("12210e8a"), refund.ToArray());
        var child = DecodeCall(refund.ToArray(), Function("refundETH()"));
        Assert.AreEqual(0, child.Arguments.EncodedBytes.Length);
    }

    [TestMethod]
    public void SeaportFulfillOrder_DecodesUnderDefaultLimits_AndDepthSixIsTheExactRequirement()
    {
        var function = Function(FulfillOrderSignature);
        Assert.AreEqual(0xb3a34c4cU, function.Selector);
        byte[] bytes = Hex(ABIFixtures.SeaportFulfillOrder);
        var call = DecodeCall(bytes, function);

        Assert.IsTrue(call.Arguments.TryGetAddress([0, 0, 0], out var offerer, out _));
        CollectionAssert.AreEqual(Hex("d8da6bf26964af9d7eed9e03e53415d37aa96045"), offerer.ToArray());
        Assert.IsTrue(call.Arguments.TryGetAddress([0, 0, 1], out var zone, out _));
        CollectionAssert.AreEqual(Hex("004c00500000ad104d7dbd00e3ae0a5c00560c00"), zone.ToArray());
        Assert.IsTrue(call.Arguments.TryGetArrayLength([0, 0, 2], out int offerCount, out _));
        Assert.AreEqual(1, offerCount);
        Assert.IsTrue(call.Arguments.TryGetArrayLength([0, 0, 3], out int considerationCount, out _));
        Assert.AreEqual(2, considerationCount);
        Assert.AreEqual(2UL, U64(call.Arguments, 0, 0, 2, 0, 0), "ItemType ERC721");
        Assert.AreEqual(8_855UL, U64(call.Arguments, 0, 0, 2, 0, 2), "identifier");
        Assert.AreEqual(975_000_000_000_000_000UL, U64(call.Arguments, 0, 0, 3, 0, 3));
        Assert.AreEqual(25_000_000_000_000_000UL, U64(call.Arguments, 0, 0, 3, 1, 4));
        Assert.IsTrue(call.Arguments.TryGetAddress([0, 0, 3, 1, 5], out var recipient, out _));
        CollectionAssert.AreEqual(zone.ToArray(), recipient.ToArray());
        Assert.AreEqual(2UL, U64(call.Arguments, 0, 0, 4), "OrderType");
        Assert.AreEqual(1_700_000_000UL, U64(call.Arguments, 0, 0, 5));
        Assert.AreEqual(1_703_000_000UL, U64(call.Arguments, 0, 0, 6));
        Assert.IsTrue(call.Arguments.TryGetUInt256Word([0, 0, 8], out var salt, out _));
        CollectionAssert.AreEqual(
            ABIReference.ToWord(BigInteger.Parse("24446860302761739304752683030156737591518664810215442929808784621098726351597")),
            salt.ToArray());
        Assert.IsTrue(call.Arguments.TryGetFixedBytes([0, 0, 9], out var conduitKey, out _));
        CollectionAssert.AreEqual(Cat(Enumerable.Repeat((byte)0x11, 31).ToArray(), [0x00]), conduitKey.ToArray());
        Assert.AreEqual(2UL, U64(call.Arguments, 0, 0, 10));
        Assert.IsTrue(call.Arguments.TryGetBytePayload([0, 1], out var signature, out _, out _));
        CollectionAssert.AreEqual(Enumerable.Range(0, 65).Select(i => (byte)((i * 37 + 11) & 0xff)).ToArray(), signature.ToArray());
        Assert.IsTrue(call.Arguments.TryGetFixedBytes([1], out var fulfillerKey, out _));
        CollectionAssert.AreEqual(conduitKey.ToArray(), fulfillerKey.ToArray());

        // Depth: root 1 > order 2 > parameters 3 > consideration[] 4 > item 5 > leaf 6.
        CallFails(bytes, function, ABIStatus.DepthLimitExceeded, Limits(depth: 5), "depth 5");
        DecodeCall(bytes, function, Limits(depth: 6));

        var emptySignature = DecodeCall(Hex(ABIFixtures.SeaportFulfillOrderEmptySignature), function);
        Assert.IsTrue(emptySignature.Arguments.TryGetBytePayload([0, 1], out var none, out _, out _));
        Assert.AreEqual(0, none.Length);
    }
}
