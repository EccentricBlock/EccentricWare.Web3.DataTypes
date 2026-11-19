using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIWriteTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// ABICallEncoder (spec 7.5): the selector comes from the function schema, arguments start at byte 4 and never see the selector in
/// their offsets, the limit covers the whole call, a failed document invalidates the buffer, and the result round-trips through
/// ABICallData. The golden call vectors (transfer, approve, multicall 388 bytes, Solidity docs, selectors) are in ABIWriterVectorTests.
/// </summary>
[TestClass]
public sealed class ABICallEncoderTests
{
    [TestMethod]
    public void EveryEncodedCall_DecodesThroughABICallData_WithTheSameSelectorAndArguments()
    {
        for (ulong seed = 1; seed <= 250; seed++)
        {
            var (type, value, expected, _, script) = Generate(seed * 19);
            ABIFunctionSchema function = Function("fn" + type.Text);
            byte[] call = CallOk(script, function);

            var limits = Generous;
            Assert.IsTrue(ABICallData.TryDecode(call, function, in limits, out ABICallData decoded, out ABIStatus status), $"seed {seed * 19} {type.Text}: {status}");
            Assert.AreEqual(function.Selector, decoded.Selector);
            CollectionAssert.AreEqual(expected, decoded.Arguments.EncodedBytes.ToArray());
            ABIReferenceAssertions.AssertEveryPathMatches(decoded.Arguments, type, value, expected);
        }
    }

    [TestMethod]
    public void ArgumentOffsets_NeverIncludeTheSelector()
    {
        // (bytes) puts its tail at 0x20 relative to the ARGUMENTS, not to the call: byte 4..36 of the call is the offset 0x20.
        ABIFunctionSchema function = Function("f(bytes)");
        byte[] call = CallOk([Ev.Blob([1, 2, 3])], function);
        CollectionAssert.AreEqual(Word(0x20), call[4..36]);
        CollectionAssert.AreEqual(Word(3), call[36..68]);
        Assert.AreEqual(4 + 32 + 32 + 32, call.Length);
    }

    [TestMethod]
    public void SelectorComesFromTheSchema_TwoNamesGiveTwoSelectors_SameArgumentsSameBytes()
    {
        byte[] a = CallOk([Ev.Addr(Addr20(1)), Ev.U64(2)], Function("transfer(address,uint256)"));
        byte[] b = CallOk([Ev.Addr(Addr20(1)), Ev.U64(2)], Function("approve(address,uint256)"));
        Assert.AreNotEqual(Hex(a[..4]), Hex(b[..4]));
        CollectionAssert.AreEqual(a[4..], b[4..]);
        CollectionAssert.AreEqual(Convert.FromHexString("a9059cbb"), a[..4]);
        CollectionAssert.AreEqual(Convert.FromHexString("095ea7b3"), b[..4]);
    }

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);

    [TestMethod]
    public void FailedDocument_InvalidatesTheBuffer_FinishReportsZeroBytes()
    {
        ABIFunctionSchema transfer = Function("transfer(address,uint256)");
        byte[] buffer = new byte[200];
        Array.Fill(buffer, Garbage);
        ABIEncodeLimits limits = GenerousEncode;
        Assert.IsTrue(ABICallEncoder.TryBegin(transfer, buffer, new ABIWriteFrame[4], in limits, out ABICallEncoder c, out ABIStatus status));
        Assert.AreEqual(0xa9, buffer[0]); // the selector is present as soon as the encoder is open...
        Assert.IsTrue(c.TryWriteAddress(Addr20(3), out _));
        Assert.IsFalse(c.TryWriteBool(true, out status)); // ...but a failure anywhere voids the whole buffer
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
        Assert.IsFalse(c.TryFinish(out int written, out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.AreEqual(0, written, "a failed call reports no committed length");
    }

    [TestMethod]
    public void IncompleteCall_FinishFails_WithIncompleteWrite_AndZeroBytes()
    {
        ABIFunctionSchema transfer = Function("transfer(address,uint256)");
        byte[] buffer = new byte[200];
        ABIEncodeLimits limits = GenerousEncode;
        Assert.IsTrue(ABICallEncoder.TryBegin(transfer, buffer, new ABIWriteFrame[4], in limits, out ABICallEncoder c, out _));
        Assert.IsTrue(c.TryWriteAddress(Addr20(3), out _));
        Assert.IsFalse(c.TryFinish(out int written, out ABIStatus status));
        Assert.AreEqual(ABIStatus.IncompleteWrite, status);
        Assert.AreEqual(0, written);
    }

    [TestMethod]
    public void ExactSizeCall_Fits_AndTheLengthExcludesDestinationSlack()
    {
        ABIFunctionSchema transfer = Function("transfer(address,uint256)");
        byte[] exact = new byte[68];
        Assert.IsTrue(RunCall([Ev.Addr(Addr20(7)), Ev.U64(9)], transfer, exact, GenerousEncode).Ok);
        Assert.AreEqual(0xa9, exact[0]);
        Assert.AreEqual(9, exact[67]);

        byte[] roomy = new byte[1000];
        RunResult result = RunCall([Ev.Addr(Addr20(7)), Ev.U64(9)], transfer, roomy, GenerousEncode);
        Assert.AreEqual(68, result.Length);
        Assert.IsTrue(roomy.AsSpan(68).IndexOfAnyExcept((byte)0) < 0, "bytes beyond the call stay untouched (zero here)");
    }

    [TestMethod]
    public void NullFunction_InvalidLimits_AndMissingScratch_AreRejected_WithoutWriting()
    {
        byte[] buffer = new byte[100];
        Array.Fill(buffer, Garbage);
        ABIFunctionSchema transfer = Function("transfer(address,uint256)");
        ABIEncodeLimits good = GenerousEncode;
        ABIEncodeLimits bad = default;

        Assert.IsFalse(ABICallEncoder.TryBegin(null!, buffer, new ABIWriteFrame[4], in good, out ABICallEncoder c, out ABIStatus status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.IsFalse(ABICallEncoder.TryBegin(transfer, buffer, new ABIWriteFrame[4], in bad, out c, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
        Assert.IsFalse(ABICallEncoder.TryBegin(transfer, buffer, Span<ABIWriteFrame>.Empty, in good, out c, out status));
        Assert.AreEqual(ABIStatus.InsufficientScratch, status);
        Assert.IsTrue(buffer.All(b => b == Garbage), "a failed TryBegin writes nothing, not even the selector");

        // Frames that alias the call buffer are refused.
        var frames = new ABIWriteFrame[8];
        Span<byte> alias = System.Runtime.InteropServices.MemoryMarshal.AsBytes(frames.AsSpan());
        Assert.IsFalse(ABICallEncoder.TryBegin(transfer, alias, frames, in good, out c, out status));
        Assert.AreEqual(ABIStatus.InvalidArgument, status);
    }

    [TestMethod]
    public void SelectorOnlyFunctions_AreCompleteWithoutAnyValue_AndRejectExtraValues()
    {
        ABIFunctionSchema totalSupply = Function("totalSupply()");
        byte[] call = CallOk([], totalSupply);
        CollectionAssert.AreEqual(Convert.FromHexString("18160ddd"), call);

        RunResult extra = RunCall([Ev.U64(1)], totalSupply, new byte[64], GenerousEncode);
        Assert.AreEqual(ABIStatus.ElementLimitExceeded, extra.Status, "a zero-argument tuple takes no value");

        var limits = Generous;
        Assert.IsTrue(ABICallData.TryDecode(call, totalSupply, in limits, out ABICallData decoded, out _));
        Assert.IsTrue(decoded.IsValid);
    }

    [TestMethod]
    public void Encoded_NestedCallPayloads_RoundTripThroughANewCallCoordinateSystem()
    {
        // multicall(bytes[]) whose children are themselves calls produced by the call encoder (a new coordinate system at each child selector).
        ABIFunctionSchema transfer = Function("transfer(address,uint256)");
        byte[] child1 = CallOk([Ev.Addr(Addr20(0x11)), Ev.U64(5)], transfer);
        byte[] child2 = CallOk([Ev.Addr(Addr20(0x22)), Ev.U64(6)], transfer);
        ABIFunctionSchema multicall = Function("multicall(bytes[])");
        byte[] outer = CallOk([Ev.Begin("dynamic", 2), Ev.Blob(child1), Ev.Blob(child2), Ev.End], multicall);

        var limits = Generous;
        Assert.IsTrue(ABICallData.TryDecode(outer, multicall, in limits, out ABICallData decoded, out ABIStatus status), status.ToString());
        for (int i = 0; i < 2; i++)
        {
            Assert.IsTrue(decoded.Arguments.TryGetBytePayload([0, i], out ReadOnlySpan<byte> payload, out _, out status), status.ToString());
            Assert.IsTrue(ABICallData.TryDecode(payload, transfer, in limits, out ABICallData child, out status), status.ToString());
            Assert.IsTrue(child.Arguments.TryGetUInt64([1], out ulong amount, out _));
            Assert.AreEqual(5UL + (ulong)i, amount);
        }
    }
}
