using System.Security.Cryptography;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIWriteTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Golden vectors for the writer, the measurer and the call encoder (spec 10.1 and 10.3). Every expected byte string was produced
/// by Foundry <c>cast</c> (commands quoted in ABIFixtures.cs and ABIWriteFixtures.cs) or is a Solidity-documentation example; the
/// values fed to the writer are either written out here by hand or decoded from the fixture by the test-local reference decoder, never
/// by the library under test. Each document is written into a garbage-filled buffer, so a missed padding byte shows up.
/// </summary>
[TestClass]
public sealed class ABIWriterVectorTests
{
    private static readonly byte[] Vitalik = Convert.FromHexString("d8dA6BF26964aF9D7eEd9e03E53415D37aA96045");
    private static readonly byte[] Router = Convert.FromHexString("68b3465833fb72A70ecDF485E0e4C7bD8665Fc45");
    private static readonly byte[] Weth = Convert.FromHexString("C02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2");
    private static readonly byte[] Usdc = Convert.FromHexString("A0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48");

    private static readonly Dictionary<string, string> FunctionNames = new()
    {
        [nameof(ABIFixtures.TransferVitalik1Eth)] = "transfer",
        [nameof(ABIFixtures.ApproveRouterMax)] = "approve",
        [nameof(ABIFixtures.ApproveZeroZero)] = "approve",
        [nameof(ABIFixtures.BalanceOfVitalik)] = "balanceOf",
        [nameof(ABIFixtures.ExactInputSingleWethToUsdc)] = "exactInputSingle",
        [nameof(ABIFixtures.ExactInputSingleUsdcToWeth)] = "exactInputSingle",
        [nameof(ABIFixtures.MulticallTwoExactInputSingle)] = "multicall",
        [nameof(ABIFixtures.MulticallSwapAndRefundEth)] = "multicall",
        [nameof(ABIFixtures.MulticallTwoTransfers388)] = "multicall",
        [nameof(ABIFixtures.SeaportFulfillOrder)] = "fulfillOrder",
        [nameof(ABIFixtures.SeaportFulfillOrderEmptySignature)] = "fulfillOrder",
        [nameof(ABIFixtures.DocsBaz69True)] = "baz",
        [nameof(ABIFixtures.DocsBarAbcDef)] = "bar",
        [nameof(ABIFixtures.DocsSamDaveTrue123)] = "sam",
        [nameof(ABIFixtures.DocsFExample)] = "f",
        [nameof(ABIFixtures.DocsGNestedArrays)] = "g",
        [nameof(ABIWriteFixtures.TotalSupplyCall)] = "totalSupply",
        [nameof(ABIWriteFixtures.TransferFromCall)] = "transferFrom",
        [nameof(ABIWriteFixtures.SafeBatchTransferFromCall)] = "safeBatchTransferFrom",
        [nameof(ABIWriteFixtures.MulticallDeadlineCall)] = "multicall",
        [nameof(ABIWriteFixtures.SwapExactTokensForTokensCall)] = "swapExactTokensForTokens",
    };

    private static IEnumerable<(string Name, string Tuple, string Hex, bool IsCall)> AllFixtures() =>
        ABIFixtureCatalog.All.Select(f => (f.Name, f.Tuple, f.Hex, f.IsCall))
            .Concat(ABIWriteFixtures.All.Select(f => (f.Name, f.Tuple, f.Hex, f.IsCall)));

    [TestMethod]
    public void EveryCastFixture_IsReproducedByteForByte_ByWriterMeasurerAndCallEncoder()
    {
        int checkedFixtures = 0;
        int calls = 0;
        foreach (var (name, tuple, hex, isCall) in AllFixtures())
        {
            byte[] all = Convert.FromHexString(hex);
            byte[] arguments = isCall ? all[4..] : all;
            RTupleType type = ABIReference.ParseTuple(tuple);
            Assert.IsTrue(ABIReference.TryDecodePermissive(type, arguments, out RSeq? value), name);
            List<Ev> script = Script(type, value!);
            ABISchema schema = CompileTuple(tuple);
            if (name == nameof(ABIFixtures.MalformedUtf8Payload))
            {
                // A (string) holding bytes that are not text: it decodes, but no write path re-encodes it (see the dedicated test below).
                Assert.AreEqual(ABIStatus.InvalidUtf8, RunWriter(script, schema, new byte[arguments.Length], GenerousEncode).Status, name);
                continue;
            }

            byte[] written = WriteOk(script, schema);
            CollectionAssert.AreEqual(arguments, written, $"{name}: writer output differs from cast");
            AssertDecodesTo(written, schema, type, value!);
            checkedFixtures++;

            if (isCall)
            {
                ABIFunctionSchema function = Function(FunctionNames[name] + tuple);
                byte[] call = CallOk(script, function);
                CollectionAssert.AreEqual(all, call, $"{name}: call encoder output differs from cast");
                calls++;
            }
        }

        Assert.IsTrue(checkedFixtures >= 60, $"only {checkedFixtures} fixtures were checked");
        Assert.IsTrue(calls >= 20, $"only {calls} calls were checked");
    }

    [TestMethod]
    public void MalformedUtf8Payload_UnderAStringSchema_IsRefusedByTheUtf8Method_AndNothingIsWritten()
    {
        byte[] payload = Convert.FromHexString("fffe41");
        var dest = new byte[256];
        Array.Fill(dest, Garbage);
        RunResult result = RunWriter([Ev.Text(payload)], CompileTuple("(string)"), dest, GenerousEncode);
        Assert.AreEqual(ABIStatus.InvalidUtf8, result.Status);
        Assert.IsFalse(result.Ok);
        Assert.IsTrue(dest.All(b => b == Garbage), "a rejected string must not touch the destination");
    }

    [TestMethod]
    public void MalformedUtf8Payload_Decodes_ButNeitherWriteMethodReEncodesIt_AndNothingIsWritten()
    {
        // The decoder keeps any on-chain string (spec 2.1: structural validation makes no UTF-8 claim), but the encoder never emits a string
        // that is not UTF-8. This narrows spec 7.7 invariant 5 (Encode(Decode(x)) == x) to documents whose strings are text: a deliberate
        // deviation, authorised 2026-11-19 (design notes row 80). Regression: TryWriteBytes used to copy these bytes into the string slot.
        byte[] document = Convert.FromHexString(ABIFixtures.MalformedUtf8Payload);
        ABISchema schema = CompileTuple("(string)");
        var limits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABIData.TryDecodeComplete(document, schema, in limits, out ABIData data, out _), "the strict decoder accepts the structure");
        Assert.IsTrue(data.TryGetBytePayload([0], out ReadOnlySpan<byte> payload, out _, out _));
        Assert.IsFalse(data.TryGetStringUtf8Validated([0], out _, out _, out ABIStatus text), "the text step refuses it");
        Assert.AreEqual(ABIStatus.InvalidUtf8, text);

        foreach (Ev write in new[] { Ev.Blob(payload.ToArray()), Ev.Text(payload.ToArray()) })
        {
            byte[] destination = new byte[256];
            Array.Fill(destination, Garbage);
            RunResult result = RunWriter([write], schema, destination, GenerousEncode);
            Assert.AreEqual(ABIStatus.InvalidUtf8, result.Status, write.ToString());
            Assert.IsTrue(destination.All(b => b == Garbage), $"{write}: destination modified");
            Assert.AreEqual(ABIStatus.InvalidUtf8, RunMeasurer([write], schema, GenerousEncode).Status, $"{write}: the measurer agrees");
        }

        // The same payload under a `bytes` schema is ordinary data and round-trips.
        CollectionAssert.AreEqual(document, WriteOk([Ev.Blob(payload.ToArray())], CompileTuple("(bytes)")));
    }

    // ------------------------------------------------------------------ ERC-20 and friends, written out by hand

    [TestMethod]
    public void Erc20Transfer_WrittenByHand_Is68BytesAndEqualsCast()
    {
        ABIFunctionSchema transfer = Function("transfer(address,uint256)");
        byte[] call = CallOk([Ev.Addr(Vitalik), Ev.U64(1_000_000_000_000_000_000)], transfer);
        Assert.AreEqual(68, call.Length);
        Assert.AreEqual(ABIFixtures.TransferVitalik1Eth, ToHex(call));
        Assert.AreEqual(0xa9059cbbu, transfer.Selector);
    }

    [TestMethod]
    public void Erc20Approve_MaximumAmount_WrittenAsA256BitWord_AndZeroAmount()
    {
        ABIFunctionSchema approve = Function("approve(address,uint256)");
        Assert.AreEqual(0x095ea7b3u, approve.Selector);

        byte[] maxWord = Filled(0xff);
        Assert.AreEqual(ABIFixtures.ApproveRouterMax, ToHex(CallOk([Ev.Addr(Router), Ev.Uint256(maxWord)], approve)));
        Assert.AreEqual(ABIFixtures.ApproveZeroZero, ToHex(CallOk([Ev.Addr(new byte[20]), Ev.U64(0)], approve)));
    }

    [TestMethod]
    public void Erc20BalanceOf_AndTransferFrom_MatchCast()
    {
        Assert.AreEqual(ABIFixtures.BalanceOfVitalik, ToHex(CallOk([Ev.Addr(Vitalik)], Function("balanceOf(address)"))));
        Assert.AreEqual(0x70a08231u, Function("balanceOf(address)").Selector);
        Assert.AreEqual(
            ABIWriteFixtures.TransferFromCall,
            ToHex(CallOk([Ev.Addr(Vitalik), Ev.Addr(Router), Ev.U64(1)], Function("transferFrom(address,address,uint256)"))));
    }

    [TestMethod]
    public void UniswapV3ExactInputSingle_StaticTupleInline_MatchesCast()
    {
        ABIFunctionSchema f = Function("exactInputSingle((address,address,uint24,address,uint256,uint256,uint256,uint160))");
        byte[] weth = CallOk(
            [Ev.Begin("tuple"), Ev.Addr(Weth), Ev.Addr(Usdc), Ev.U64(3000), Ev.Addr(Vitalik), Ev.U64(1_700_000_000), Ev.U64(1_000_000_000_000_000_000), Ev.U64(1_800_000_000), Ev.U64(0), Ev.End],
            f);
        Assert.AreEqual(ABIFixtures.ExactInputSingleWethToUsdc, ToHex(weth));
        Assert.AreEqual(260, weth.Length); // 4 + 8 words: a static tuple is inline, no offset word

        byte[] usdc = CallOk(
            [Ev.Begin("tuple"), Ev.Addr(Usdc), Ev.Addr(Weth), Ev.U64(500), Ev.Addr(Router), Ev.U64(1_700_000_600), Ev.U64(2_500_000_000), Ev.U64(1_200_000_000_000_000_000), Ev.U64(4_295_128_740), Ev.End],
            f);
        Assert.AreEqual(ABIFixtures.ExactInputSingleUsdcToWeth, ToHex(usdc));
    }

    [TestMethod]
    public void UniswapMulticall_TwoTransfers_Reproduces388ByteFixture_AndTheSection8Layout()
    {
        ABIFunctionSchema multicall = Function("multicall(bytes[])");
        Assert.AreEqual(0xac9650d8u, multicall.Selector);
        byte[] child1 = Convert.FromHexString(ABIFixtures.TransferChild1);
        byte[] child2 = Convert.FromHexString(ABIFixtures.TransferChild2);
        List<Ev> script = [Ev.Begin("dynamic", 2), Ev.Blob(child1), Ev.Blob(child2), Ev.End];

        byte[] call = CallOk(script, multicall);
        Assert.AreEqual(388, call.Length);
        Assert.AreEqual(ABIFixtures.MulticallTwoTransfers388, ToHex(call));

        // Spec 8.2 numbers, rebuilt from the independent encoder (a second implementation), not typed in as the only truth.
        RTupleType type = ABIReference.ParseTuple("(bytes[])");
        var value = new RSeq([new RSeq([new RBlob(child1), new RBlob(child2)])]);
        byte[] independent = ABIIndependentEncoder.EncodeArguments(type, value);
        CollectionAssert.AreEqual(independent, call[4..]);
        Assert.AreEqual(32 + 32 + 64 + 128 + 128, independent.Length);

        // Offsets in the arguments document: head 0x20, count 2, element offsets 0x40 and 0xc0 relative to the table base (68 - 4 = 64).
        byte[] arguments = call[4..];
        CollectionAssert.AreEqual(Word(0x20), arguments[..32]);
        CollectionAssert.AreEqual(Word(2), arguments[32..64]);
        CollectionAssert.AreEqual(Word(0x40), arguments[64..96]);
        CollectionAssert.AreEqual(Word(0xc0), arguments[96..128]);

        // The same ranges the decoder reports: payloads (160,68) and (288,68), selector-relative (164,68) and (292,68).
        var limits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABICallData.TryDecode(call, multicall, in limits, out ABICallData decoded, out ABIStatus status), status.ToString());
        Assert.IsTrue(decoded.Arguments.TryGetBytePayload([0, 0], out _, out ABIRange first, out _));
        Assert.IsTrue(decoded.Arguments.TryGetBytePayload([0, 1], out _, out ABIRange second, out _));
        Assert.AreEqual(new ABIRange(160, 68), first);
        Assert.AreEqual(new ABIRange(288, 68), second);
        Assert.IsTrue(decoded.TryTranslateArgumentRange(first, out ABIRange firstInCall));
        Assert.AreEqual(new ABIRange(164, 68), firstInCall);
        Assert.IsTrue(decoded.TryTranslateArgumentRange(second, out ABIRange secondInCall));
        Assert.AreEqual(new ABIRange(292, 68), secondInCall);
    }

    [TestMethod]
    public void UniswapMulticall_PayloadThatIsABareSelector_IsWrittenAsA36ByteBytesValue()
    {
        byte[] swap = Convert.FromHexString(ABIFixtures.ExactInputSingleWethToUsdc);
        byte[] refund = Convert.FromHexString("12210e8a");
        byte[] call = CallOk([Ev.Begin("dynamic", 2), Ev.Blob(swap), Ev.Blob(refund), Ev.End], Function("multicall(bytes[])"));
        Assert.AreEqual(ABIFixtures.MulticallSwapAndRefundEth, ToHex(call));
    }

    [TestMethod]
    public void UniswapV2Swap_AddressArrayBetweenStaticArguments_MatchesCast()
    {
        ABIFunctionSchema f = Function("swapExactTokensForTokens(uint256,uint256,address[],address,uint256)");
        Assert.AreEqual(0x38ed1739u, f.Selector);
        byte[] call = CallOk(
            [Ev.U64(1_000_000), Ev.U64(990_000), Ev.Begin("dynamic", 2), Ev.Addr(Weth), Ev.Addr(Usdc), Ev.End, Ev.Addr(Vitalik), Ev.U64(1_800_000_000)],
            f);
        Assert.AreEqual(ABIWriteFixtures.SwapExactTokensForTokensCall, ToHex(call));
    }

    [TestMethod]
    public void Erc1155SafeBatchTransferFrom_TwoArraysAndAnEmptyBytes_MatchesCast()
    {
        ABIFunctionSchema f = Function("safeBatchTransferFrom(address,address,uint256[],uint256[],bytes)");
        byte[] call = CallOk(
            [Ev.Addr(Vitalik), Ev.Addr(Router), Ev.Begin("dynamic", 2), Ev.U64(1), Ev.U64(2), Ev.End, Ev.Begin("dynamic", 2), Ev.U64(10), Ev.U64(20), Ev.End, Ev.Blob([])],
            f);
        Assert.AreEqual(ABIWriteFixtures.SafeBatchTransferFromCall, ToHex(call));
    }

    [TestMethod]
    public void MulticallWithDeadline_StaticWordBeforeDynamicArray_MatchesCast()
    {
        ABIFunctionSchema f = Function("multicall(uint256,bytes[])");
        byte[] call = CallOk([Ev.U64(1_700_000_000), Ev.Begin("dynamic", 1), Ev.Blob(Convert.FromHexString("12210e8a")), Ev.End], f);
        Assert.AreEqual(ABIWriteFixtures.MulticallDeadlineCall, ToHex(call));
    }

    // ------------------------------------------------------------------ Solidity documentation examples, written out by hand

    [TestMethod]
    public void Docs_Baz_Bar_Sam_F_G_WrittenByHand_MatchThePublishedBytes()
    {
        Assert.AreEqual(ABIFixtures.DocsBaz69True, ToHex(CallOk([Ev.U64(69), Ev.Bool(true)], Function("baz(uint32,bool)"))));

        Assert.AreEqual(
            ABIFixtures.DocsBarAbcDef,
            ToHex(CallOk([Ev.Begin("fixed"), Ev.FixedB(Ascii("abc")), Ev.FixedB(Ascii("def")), Ev.End], Function("bar(bytes3[2])"))));

        Assert.AreEqual(
            ABIFixtures.DocsSamDaveTrue123,
            ToHex(CallOk([Ev.Blob(Ascii("dave")), Ev.Bool(true), Ev.Begin("dynamic", 3), Ev.U64(1), Ev.U64(2), Ev.U64(3), Ev.End], Function("sam(bytes,bool,uint256[])"))));

        Assert.AreEqual(
            ABIFixtures.DocsFExample,
            ToHex(CallOk(
                [Ev.U64(0x123), Ev.Begin("dynamic", 2), Ev.U64(0x456), Ev.U64(0x789), Ev.End, Ev.FixedB(Ascii("1234567890")), Ev.Blob(Ascii("Hello, world!"))],
                Function("f(uint256,uint32[],bytes10,bytes)"))));

        Assert.AreEqual(
            ABIFixtures.DocsGNestedArrays,
            ToHex(CallOk(
                [
                    Ev.Begin("dynamic", 2),
                    Ev.Begin("dynamic", 2), Ev.U64(1), Ev.U64(2), Ev.End,
                    Ev.Begin("dynamic", 1), Ev.U64(3), Ev.End,
                    Ev.End,
                    Ev.Begin("dynamic", 3), Ev.Text(Ascii("one")), Ev.Text(Ascii("two")), Ev.Text(Ascii("three")), Ev.End,
                ],
                Function("g(uint256[][],string[])"))));
    }

    [TestMethod]
    public void SelectorsOfTheNamedVectors_AreWrittenAsTheFirstFourBytes_AndAreNotSha3()
    {
        (string Signature, uint Selector)[] vectors =
        [
            ("transfer(address,uint256)", 0xa9059cbb), ("approve(address,uint256)", 0x095ea7b3), ("balanceOf(address)", 0x70a08231),
            ("multicall(bytes[])", 0xac9650d8), ("baz(uint32,bool)", 0xcdcd77c0), ("bar(bytes3[2])", 0xfce353f6),
            ("sam(bytes,bool,uint256[])", 0xa5643bf2), ("f(uint256,uint32[],bytes10,bytes)", 0x8be65246), ("g(uint256[][],string[])", 0x2289b18c),
        ];

        foreach (var (signature, selector) in vectors)
        {
            ABIFunctionSchema function = Function(signature);
            Assert.AreEqual(selector, function.Selector, signature);

            // Minimal valid arguments for each signature, so the call encoder can finish.
            List<Ev> script = signature switch
            {
                "transfer(address,uint256)" or "approve(address,uint256)" => [Ev.Addr(new byte[20]), Ev.U64(0)],
                "balanceOf(address)" => [Ev.Addr(new byte[20])],
                "multicall(bytes[])" => [Ev.Begin("dynamic", 0), Ev.End],
                "baz(uint32,bool)" => [Ev.U64(0), Ev.Bool(false)],
                "bar(bytes3[2])" => [Ev.Begin("fixed"), Ev.FixedB(new byte[3]), Ev.FixedB(new byte[3]), Ev.End],
                "sam(bytes,bool,uint256[])" => [Ev.Blob([]), Ev.Bool(false), Ev.Begin("dynamic", 0), Ev.End],
                "f(uint256,uint32[],bytes10,bytes)" => [Ev.U64(0), Ev.Begin("dynamic", 0), Ev.End, Ev.FixedB(new byte[10]), Ev.Blob([])],
                _ => [Ev.Begin("dynamic", 0), Ev.End, Ev.Begin("dynamic", 0), Ev.End],
            };
            byte[] call = CallOk(script, function);
            Assert.AreEqual(selector, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(call), signature);

            if (SHA3_256.IsSupported)
            {
                uint sha3 = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(SHA3_256.HashData(Ascii(signature)));
                Assert.AreNotEqual(sha3, selector, $"{signature}: the selector must be Keccak-256, not NIST SHA3-256");
            }
        }
    }

    // ------------------------------------------------------------------ scalars

    [TestMethod]
    public void ScalarExtrema_Unsigned_Signed_AndNarrowWidths_MatchCast()
    {
        // uint8/16/64/128/256 maxima: the 64-bit entry points where the value fits, the word entry point otherwise.
        byte[] ff = Filled(0xff);
        byte[] max128 = Cat(new byte[16], ff[..16]);
        Assert.AreEqual(
            ABIFixtures.ScalarsMixedMaxima,
            ToHex(WriteOk([Ev.U64(255), Ev.U64(65535), Ev.U64(ulong.MaxValue), Ev.Uint256(max128), Ev.Uint256(ff)], CompileTuple("(uint8,uint16,uint64,uint128,uint256)"))));

        byte[] int128Min = Cat(Filled(0xff)[..16], [0x80], new byte[15]);
        byte[] int256Min = Cat([0x80], new byte[31]);
        Assert.AreEqual(
            ABIFixtures.ScalarsMixedMinima,
            ToHex(WriteOk([Ev.I64(-128), Ev.I64(-32768), Ev.I64(long.MinValue), Ev.Int256(int128Min), Ev.Int256(int256Min)], CompileTuple("(int8,int16,int64,int128,int256)"))));

        byte[] int128Max = Cat(new byte[16], [0x7f], Filled(0xff)[..15]);
        byte[] int256Max = Cat([0x7f], Filled(0xff)[..31]);
        Assert.AreEqual(
            ABIFixtures.ScalarsMixedPositiveMaxima,
            ToHex(WriteOk([Ev.I64(127), Ev.I64(32767), Ev.I64(long.MaxValue), Ev.Int256(int128Max), Ev.Int256(int256Max)], CompileTuple("(int8,int16,int64,int128,int256)"))));
    }

    [TestMethod]
    public void NarrowIntegersAndPointerSizedWidths_MatchCast()
    {
        // cast: uint8 255, int8 -128, uint24 16777215, int24 -8388608, uint160 max, int160 min.
        byte[] uint160Max = Cat(new byte[12], Filled(0xff)[..20]);
        byte[] int160Min = Cat(Filled(0xff)[..12], [0x80], new byte[19]);
        Assert.AreEqual(
            ABIWriteFixtures.NarrowIntegerExtrema,
            ToHex(WriteOk(
                [Ev.U64(255), Ev.I64(-128), Ev.U64(16_777_215), Ev.I64(-8_388_608), Ev.Uint256(uint160Max), Ev.Int256(int160Min)],
                CompileTuple("(uint8,int8,uint24,int24,uint160,int160)"))));
    }

    [TestMethod]
    public void BoolAddressAndFixedBytes_AllWidthsInOneDocument_MatchCast()
    {
        byte[] bytes24 = Convert.FromHexString("0102030405060708090a0b0c0d0e0f101112131415161718");
        byte[] bytes32 = Convert.FromHexString("a0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebf");
        Assert.AreEqual(
            ABIFixtures.ScalarsBoolAddressBytes,
            ToHex(WriteOk([Ev.Bool(true), Ev.Bool(false), Ev.Addr(Vitalik), Ev.FixedB([0xab]), Ev.FixedB(bytes24), Ev.FixedB(bytes32)], CompileTuple("(bool,bool,address,bytes1,bytes24,bytes32)"))));
    }

    [TestMethod]
    public void FunctionPointer_AddressSelectorAndEightZeroBytes_MatchCast()
    {
        byte[] address = Convert.FromHexString("0102030405060708090a0b0c0d0e0f1011121314");
        Assert.AreEqual(ABIFixtures.ScalarFunctionPointer, ToHex(WriteOk([Ev.Func(address, 0xaabbccdd)], CompileTuple("(function)"))));
    }

    [TestMethod]
    public void EveryFixedBytesWidthFrom1To32_IsLeftAlignedWithZeroRightPadding()
    {
        for (int m = 1; m <= 32; m++)
        {
            byte[] value = Enumerable.Range(0, m).Select(i => (byte)(0xc0 + i)).ToArray();
            byte[] written = WriteOk([Ev.FixedB(value)], CompileTuple($"(bytes{m})"));
            byte[] expected = new byte[32];
            value.CopyTo(expected, 0);
            CollectionAssert.AreEqual(expected, written, $"bytes{m}");
        }
    }

    [TestMethod]
    public void FixedPoint_ScaledWordsAtTheExtrema_AreWrittenVerbatim()
    {
        // fixed128x18 / ufixed128x18 hold the already scaled integer; extrema derived by hand.
        byte[] fixedMax = Cat(new byte[16], [0x7f], Filled(0xff)[..15]);
        byte[] fixedMin = Cat(Filled(0xff)[..16], [0x80], new byte[15]);
        byte[] fixedMinusOne = Filled(0xff);
        byte[] ufixedMax = Cat(new byte[16], Filled(0xff)[..16]);
        List<Ev> script =
        [
            new(EvKind.SignedScaled, fixedMax), new(EvKind.SignedScaled, fixedMin), new(EvKind.SignedScaled, fixedMinusOne),
            new(EvKind.UnsignedScaled, ufixedMax), new(EvKind.UnsignedScaled, new byte[32]),
        ];
        byte[] written = WriteOk(script, CompileTuple("(fixed128x18,fixed128x18,fixed128x18,ufixed128x18,ufixed128x18)"));
        CollectionAssert.AreEqual(Cat(fixedMax, fixedMin, fixedMinusOne, ufixedMax, new byte[32]), written);

        // The declared scale does not change the bytes: fixed8x1 and fixed256x80 are plain signed words of their widths.
        byte[] minusOne = Filled(0xff);
        CollectionAssert.AreEqual(minusOne, WriteOk([new Ev(EvKind.SignedScaled, minusOne)], CompileTuple("(fixed8x1)")));
        CollectionAssert.AreEqual(Cat([0x80], new byte[31]), WriteOk([new Ev(EvKind.SignedScaled, Cat([0x80], new byte[31]))], CompileTuple("(fixed256x80)")));
    }

    // ------------------------------------------------------------------ dynamic bytes and strings

    [TestMethod]
    public void DynamicBytesAndStrings_OfEveryBoundaryLength_ArePaddedWithZeros()
    {
        foreach (int length in new[] { 0, 1, 31, 32, 33, 63, 64, 65 })
        {
            byte[] payload = Enumerable.Range(0, length).Select(i => (byte)((i * 7 + 1) & 0xff)).ToArray();
            byte[] bytesDoc = WriteOk([Ev.Blob(payload)], CompileTuple("(bytes)"));
            byte[] expected = Cat(Word(0x20), BytesValue(payload));
            CollectionAssert.AreEqual(expected, bytesDoc, $"bytes length {length}");
            Assert.AreEqual(64 + (length + 31) / 32 * 32, bytesDoc.Length);

            byte[] ascii = Enumerable.Range(0, length).Select(i => (byte)('a' + i % 26)).ToArray();
            byte[] stringDoc = WriteOk([Ev.Text(ascii)], CompileTuple("(string)"));
            CollectionAssert.AreEqual(Cat(Word(0x20), BytesValue(ascii)), stringDoc, $"string length {length}");
        }
    }

    [TestMethod]
    public void Utf8MultiByteString_IsAccepted_AndMatchesCast()
    {
        Assert.AreEqual(ABIFixtures.Utf8MultiByteString, ToHex(WriteOk([Ev.Text(Utf8("héllo €😀"))], CompileTuple("(string)"))));
    }

    [TestMethod]
    public void RawBytesFixture_WithNonUtf8Content_IsAcceptedForABytesSlot_AndMatchesCast()
    {
        // The same payload that a string slot refuses (see above) is ordinary data for a bytes slot.
        Assert.AreEqual(ABIFixtures.MalformedUtf8Payload, ToHex(WriteOk([Ev.Blob(Convert.FromHexString("fffe41"))], CompileTuple("(bytes)"))));
    }

    // ------------------------------------------------------------------ zero-argument and empty shapes

    [TestMethod]
    public void ZeroArgumentCall_IsTheBareSelector_AndZeroArgumentsWriteZeroBytesIntoANonEmptyBuffer()
    {
        ABIFunctionSchema totalSupply = Function("totalSupply()");
        Assert.AreEqual(0x18160dddu, totalSupply.Selector);
        byte[] call = CallOk([], totalSupply);
        Assert.AreEqual(ABIWriteFixtures.TotalSupplyCall, ToHex(call));
        Assert.AreEqual(4, call.Length);

        var buffer = new byte[96];
        Array.Fill(buffer, Garbage);
        RunResult arguments = RunWriter([], CompileTuple("()"), buffer, GenerousEncode);
        Assert.IsTrue(arguments.Ok);
        Assert.AreEqual(0, arguments.Length);
        Assert.IsTrue(buffer.All(b => b == Garbage), "a zero-argument document must not touch the destination");
    }

    [TestMethod]
    public void EmptyTuples_ZeroWidth_AreWrittenAsNothing_AndTheirOrdinalStillAdvances()
    {
        // (()) is zero bytes; ((),uint256) is one word; (()[]) with 3 elements is a count word only; ((),())[2] is the empty tuple twice.
        CollectionAssert.AreEqual(Array.Empty<byte>(), WriteOk([Ev.Begin("tuple"), Ev.End], CompileTuple("(())")));
        CollectionAssert.AreEqual(Word(5), WriteOk([Ev.Begin("tuple"), Ev.End, Ev.U64(5)], CompileTuple("((),uint256)")));
        CollectionAssert.AreEqual(
            Cat(Word(0x20), Word(3)),
            WriteOk([Ev.Begin("dynamic", 3), Ev.Begin("tuple"), Ev.End, Ev.Begin("tuple"), Ev.End, Ev.Begin("tuple"), Ev.End, Ev.End], CompileTuple("(()[])")));
        CollectionAssert.AreEqual(
            Array.Empty<byte>(),
            WriteOk([Ev.Begin("fixed"), Ev.Begin("tuple"), Ev.End, Ev.Begin("tuple"), Ev.End, Ev.End], CompileTuple("(()[2])")));
        CollectionAssert.AreEqual(
            Cat(Word(0x20), Word(0)),
            WriteOk([Ev.Begin("dynamic", 0), Ev.End], CompileTuple("(()[])")));
    }

    // ------------------------------------------------------------------ shapes with several dynamic members

    [TestMethod]
    public void ArraysOfDynamicAndStaticTuples_NestedAndFixed_MatchCast()
    {
        // f((uint256,(string,bytes)[])[]) with [(7,[("hi",0x1234)]),(8,[])]
        List<Ev> nested =
        [
            Ev.Begin("dynamic", 2),
            Ev.Begin("tuple"), Ev.U64(7), Ev.Begin("dynamic", 1), Ev.Begin("tuple"), Ev.Text(Ascii("hi")), Ev.Blob([0x12, 0x34]), Ev.End, Ev.End, Ev.End,
            Ev.Begin("tuple"), Ev.U64(8), Ev.Begin("dynamic", 0), Ev.End, Ev.End,
            Ev.End,
        ];
        Assert.AreEqual(ABIWriteFixtures.NestedTupleArrays, ToHex(WriteOk(nested, CompileTuple("((uint256,(string,bytes)[])[])"))));

        // f(bytes[2][]) with [[0x01,0x02],[0x,0x0304]]
        List<Ev> pairs =
        [
            Ev.Begin("dynamic", 2),
            Ev.Begin("fixed"), Ev.Blob([1]), Ev.Blob([2]), Ev.End,
            Ev.Begin("fixed"), Ev.Blob([]), Ev.Blob([3, 4]), Ev.End,
            Ev.End,
        ];
        Assert.AreEqual(ABIWriteFixtures.BytesPairArrays, ToHex(WriteOk(pairs, CompileTuple("(bytes[2][])"))));

        // f(uint256[][2],(uint8,bytes)[2]) with [[1,2],[]] and [(1,0xaa),(2,0xbbcc)]
        List<Ev> mixed =
        [
            Ev.Begin("fixed"), Ev.Begin("dynamic", 2), Ev.U64(1), Ev.U64(2), Ev.End, Ev.Begin("dynamic", 0), Ev.End, Ev.End,
            Ev.Begin("fixed"), Ev.Begin("tuple"), Ev.U64(1), Ev.Blob([0xaa]), Ev.End, Ev.Begin("tuple"), Ev.U64(2), Ev.Blob([0xbb, 0xcc]), Ev.End, Ev.End,
        ];
        Assert.AreEqual(ABIWriteFixtures.ArraysThenTupleArray, ToHex(WriteOk(mixed, CompileTuple("(uint256[][2],(uint8,bytes)[2])"))));
    }

    [TestMethod]
    public void SignedAndBoolArrays_NegativeInt8Elements_AreSignExtended()
    {
        List<Ev> script =
        [
            Ev.Begin("dynamic", 3), Ev.I64(-1), Ev.I64(127), Ev.I64(-128), Ev.End,
            Ev.Begin("dynamic", 2), Ev.Bool(true), Ev.Bool(false), Ev.End,
        ];
        Assert.AreEqual(ABIWriteFixtures.SignedAndBoolArrays, ToHex(WriteOk(script, CompileTuple("(int8[],bool[])"))));
    }

    [TestMethod]
    public void Bytes31Array_ExactPaddingByte_MatchesCast()
    {
        byte[] first = Enumerable.Range(1, 31).Select(i => (byte)i).ToArray();
        byte[] second = Convert.FromHexString("ffeeddccbbaa99887766554433221100ffeeddccbbaa998877665544332211");
        Assert.AreEqual(
            ABIWriteFixtures.Bytes31Pair,
            ToHex(WriteOk([Ev.Begin("fixed"), Ev.FixedB(first), Ev.FixedB(second), Ev.End], CompileTuple("(bytes31[2])"))));
    }

    [TestMethod]
    public void SeaportFulfillOrder_DeepNestingWithSeveralDynamicLevels_IsReproduced()
    {
        // Depth, offsets relative to four different bases, empty and non-empty dynamic arrays and bytes in one real-world shape.
        byte[] all = Convert.FromHexString(ABIFixtures.SeaportFulfillOrder);
        ABISchema schema = CompileTuple(ABIFixtureCatalog.SeaportOrderTuple);
        RTupleType type = ABIReference.ParseTuple(ABIFixtureCatalog.SeaportOrderTuple);
        Assert.IsTrue(ABIReference.TryDecodePermissive(type, all[4..], out RSeq? value));
        byte[] written = WriteOk(Script(type, value!), schema);
        CollectionAssert.AreEqual(all[4..], written);
        CollectionAssert.AreEqual(all[4..], ABIIndependentEncoder.EncodeArguments(type, value!));
    }
}
