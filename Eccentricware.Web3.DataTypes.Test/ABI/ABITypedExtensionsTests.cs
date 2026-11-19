using System.Numerics;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// The typed adapters of <see cref="ABITypedExtensions"/> add no behaviour of their own: they convert an already validated word and delegate to the span
/// accessor or span writer. So the central test is PARITY: for every value, slot kind and path error the typed member and the span member (fed the
/// equivalent bytes) return the same success flag, the same status, the same destination bytes and leave the writer in the same state, for all three writer
/// types. Explicit expectations (boundary words computed from <see cref="BigInteger"/>, fixtures produced by Foundry <c>cast</c>) keep a shared mistake from
/// passing. Also pinned: zero allocation, the compile contract for stack-allocated spans, and the call-site conversions that throw before the codec runs.
/// </summary>
[TestClass]
public sealed class ABITypedExtensionsTests
{
    private const string AllScalars = "f(uint8,int8,address,bool,bytes4,function,bytes32,uint256,int256,bytes31)";
    private const int ExpectedStackallocArgumentLength = 512; // 12 head words + (bytes: length word + one padded word) + (string: the same)

    private static readonly Address Owner = Address.Parse("0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045");
    private static readonly Address Router = Address.Parse("0x68b3465833fb72A70ecDF485E0e4C7bD8665Fc45");
    private static readonly Address Solana = Address.FromSolanaBytes(Enumerable.Repeat((byte)7, 32).ToArray());
    private static readonly Hash32 Topic = Hash32.Parse("0x0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20");
    private static readonly byte[] Bytes31 = Enumerable.Repeat((byte)0x5a, 31).ToArray();

    // ------------------------------------------------------------------ oracle helpers (BigInteger, not the library's arithmetic)

    private static BigInteger Pow2(int bits) => BigInteger.One << bits;

    private static uint256 U(BigInteger value) => new(ABIReference.ToWord(value));

    private static int256 S(BigInteger value) => new(ABIReference.ToWord(value));

    private static readonly BigInteger[] UnsignedSamples =
        [0, 1, Pow2(64) - 1, Pow2(64), Pow2(128), Pow2(160) - 1, Pow2(255), Pow2(256) - 1];

    private static readonly BigInteger[] SignedSamples =
        [-Pow2(255), Pow2(255) - 1, -1, 0, 1, long.MinValue, -Pow2(64)];

    private static byte[] Be(uint256 value)
    {
        byte[] word = new byte[32];
        value.WriteBigEndian(word);
        return word;
    }

    private static byte[] Be(int256 value)
    {
        byte[] word = new byte[32];
        value.WriteBigEndian(word);
        return word;
    }

    private static byte[] Be(Hash32 value)
    {
        byte[] bytes = new byte[32];
        value.WriteBigEndian(bytes);
        return bytes;
    }

    private static byte[] Be(FunctionSelector value)
    {
        byte[] bytes = new byte[4];
        value.WriteBytes(bytes);
        return bytes;
    }

    /// <summary>The bytes the span path receives for an address: the 20 bytes, or an empty span for a non-EVM address (the documented mapping).</summary>
    private static byte[] Raw(Address value)
    {
        if (value.Type != AddressType.Evm)
        {
            return [];
        }

        byte[] bytes = new byte[20];
        value.WriteBytes(bytes);
        return bytes;
    }

    private static readonly Dictionary<string, ABIFunctionSchema> SlotSchemas = [];

    private static ABIFunctionSchema Slot(string slotType)
    {
        lock (SlotSchemas)
        {
            if (!SlotSchemas.TryGetValue(slotType, out ABIFunctionSchema? f))
            {
                SlotSchemas[slotType] = f = Function($"g({slotType})");
            }

            return f;
        }
    }

    // ------------------------------------------------------------------ boundary round trips through all three writer types

    private static byte[] EncodeCall(ABIFunctionSchema f, uint256 big, int256 signed)
    {
        byte[] buffer = new byte[4 + 10 * 32];
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[2];
        var limits = ABIEncodeLimits.Default;
        Assert.IsTrue(ABICallEncoder.TryBegin(f, buffer, frames, in limits, out ABICallEncoder e, out ABIStatus status), status.ToString());
        bool ok = e.TryWriteUInt64(200, out status) && e.TryWriteInt64(-5, out status) && e.TryWriteAddress(Owner, out status) && e.TryWriteBool(true, out status)
            && e.TryWriteBytes4(FunctionSelector.Erc20.Transfer, out status) && e.TryWriteFunction(Owner, FunctionSelector.Erc20.Approve, out status)
            && e.TryWriteBytes32(Topic, out status) && e.TryWriteUInt256(big, out status) && e.TryWriteInt256(signed, out status) && e.TryWriteFixedBytes(Bytes31, out status);
        Assert.IsTrue(ok, status.ToString());
        Assert.IsTrue(e.TryFinish(out int written, out status), status.ToString());
        Assert.AreEqual(buffer.Length, written);
        return buffer;
    }

    private static byte[] EncodeArguments(ABIFunctionSchema f, uint256 big, int256 signed)
    {
        byte[] buffer = new byte[10 * 32];
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[2];
        var limits = ABIEncodeLimits.Default;
        Assert.IsTrue(ABIWriter.TryBegin(f.InputSchema, buffer, frames, in limits, out ABIWriter w, out ABIStatus status), status.ToString());
        bool ok = w.TryWriteUInt64(200, out status) && w.TryWriteInt64(-5, out status) && w.TryWriteAddress(Owner, out status) && w.TryWriteBool(true, out status)
            && w.TryWriteBytes4(FunctionSelector.Erc20.Transfer, out status) && w.TryWriteFunction(Owner, FunctionSelector.Erc20.Approve, out status)
            && w.TryWriteBytes32(Topic, out status) && w.TryWriteUInt256(big, out status) && w.TryWriteInt256(signed, out status) && w.TryWriteFixedBytes(Bytes31, out status);
        Assert.IsTrue(ok, status.ToString());
        Assert.IsTrue(w.TryFinish(out int written, out status), status.ToString());
        Assert.AreEqual(buffer.Length, written);
        return buffer;
    }

    private static int MeasureArguments(ABIFunctionSchema f, uint256 big, int256 signed)
    {
        Span<ABIMeasureFrame> frames = stackalloc ABIMeasureFrame[2];
        var limits = ABIEncodeLimits.Default;
        Assert.IsTrue(ABIMeasurer.TryBegin(f.InputSchema, frames, in limits, out ABIMeasurer m, out ABIStatus status), status.ToString());
        bool ok = m.TryWriteUInt64(200, out status) && m.TryWriteInt64(-5, out status) && m.TryWriteAddress(Owner, out status) && m.TryWriteBool(true, out status)
            && m.TryWriteBytes4(FunctionSelector.Erc20.Transfer, out status) && m.TryWriteFunction(Owner, FunctionSelector.Erc20.Approve, out status)
            && m.TryWriteBytes32(Topic, out status) && m.TryWriteUInt256(big, out status) && m.TryWriteInt256(signed, out status) && m.TryWriteFixedBytes(Bytes31, out status);
        Assert.IsTrue(ok, status.ToString());
        Assert.IsTrue(m.TryFinish(out int length, out status), status.ToString());
        return length;
    }

    [TestMethod]
    public void EveryScalar_RoundTripsThroughTheTypedWriters_AndTheTypedReaders_AtTheBoundaries()
    {
        ABIFunctionSchema f = Function(AllScalars);
        foreach (BigInteger unsignedSample in UnsignedSamples)
        {
            foreach (BigInteger signedSample in SignedSamples)
            {
                uint256 big = U(unsignedSample);
                int256 signed = S(signedSample);
                byte[] call = EncodeCall(f, big, signed);
                byte[] arguments = EncodeArguments(f, big, signed);
                string where = $"uint {unsignedSample}, int {signedSample}";

                // The three writer types agree: the call is the selector plus the arguments, and the measurer reports exactly the length.
                CollectionAssert.AreEqual(call[4..], arguments, where);
                CollectionAssert.AreEqual(Be(f.FunctionSelector), call[..4], where);
                Assert.AreEqual(arguments.Length, MeasureArguments(f, big, signed), where);

                // The bytes are the independent BigInteger words.
                CollectionAssert.AreEqual(ABIReference.ToWord(unsignedSample), arguments[(7 * 32)..(8 * 32)], where + " uint256 word");
                CollectionAssert.AreEqual(ABIReference.ToWord(signedSample), arguments[(8 * 32)..(9 * 32)], where + " int256 word");

                var limits = ABIDecodeLimits.Default;
                Assert.IsTrue(ABICallData.TryDecode(call, f, in limits, out ABICallData decoded, out ABIStatus status), where + " " + status);
                Assert.AreEqual(f.FunctionSelector, decoded.FunctionSelector, where);
                Assert.AreEqual(f.Selector, decoded.FunctionSelector.Value, where);
                ABIData a = decoded.Arguments;

                Assert.IsTrue(a.TryGetUInt256([7], out uint256 u, out status), where);
                Assert.AreEqual(big, u, where);
                Assert.AreEqual(unsignedSample, (BigInteger)u, where + " (BigInteger view of the value read back)");
                Assert.IsTrue(a.TryGetInt256([8], out int256 s, out status), where);
                Assert.AreEqual(signed, s, where);
                Assert.AreEqual(signedSample, (BigInteger)s, where);
                Assert.IsTrue(a.TryGetInt256([1], out int256 narrow, out _), "an int8 slot reads as a sign-extended int256");
                Assert.AreEqual(S(-5), narrow);
                Assert.IsTrue(a.TryGetUInt256([0], out uint256 small, out _));
                Assert.AreEqual(U(200), small);
                Assert.IsTrue(a.TryGetAddress([2], out Address owner, out _));
                Assert.AreEqual(Owner, owner);
                Assert.AreEqual(AddressType.Evm, owner.Type);
                Assert.IsTrue(a.TryGetBytes32([6], out Hash32 topic, out _));
                Assert.AreEqual(Topic, topic);
                Assert.IsTrue(a.TryGetBytes4([4], out FunctionSelector selector, out _));
                Assert.AreEqual(FunctionSelector.Erc20.Transfer, selector);
                Assert.IsTrue(a.TryGetFunction([5], out Address target, out FunctionSelector fn, out _));
                Assert.AreEqual(Owner, target);
                Assert.AreEqual(FunctionSelector.Erc20.Approve, fn);
            }
        }
    }

    [TestMethod]
    public void FunctionSelectorProperties_AreTheKeccakSelectorOfTheCanonicalSignature()
    {
        foreach (string signature in new[] { "transfer(address,uint256)", "approve(address,uint256)", "multicall(bytes[])", "f(uint8,int8,address)", "noArgs()", AllScalars })
        {
            ABIFunctionSchema f = Function(signature);
            Assert.AreEqual(FunctionSelector.FromSignature(f.CanonicalSignature), f.FunctionSelector, signature);
            Assert.AreEqual(f.FunctionSelector.Value, f.Selector, signature);
        }

        Assert.AreEqual(new FunctionSelector(0xa9059cbbu), Function("transfer(address,uint256)").FunctionSelector, "independent constant (cast sig)");
        ABICallData invalid = default;
        Assert.AreEqual(default, invalid.FunctionSelector);
        Assert.IsTrue(invalid.FunctionSelector.IsZero);
    }

    // ------------------------------------------------------------------ reader parity with the span accessors

    private const string RichSignature = "p(uint8,int8,address,bool,bytes4,function,bytes32,uint256,int256,bytes31,fixed128x18,ufixed128x18,bytes,string,uint256[],(uint256,address),bytes4[2])";

    private static byte[] RichDocument()
    {
        ABIFunctionSchema f = Function(RichSignature);
        byte[] buffer = new byte[2048];
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[4];
        var limits = ABIEncodeLimits.Default;
        Assert.IsTrue(ABIWriter.TryBegin(f.InputSchema, buffer, frames, in limits, out ABIWriter w, out ABIStatus status), status.ToString());
        byte[] scaled = Be(S(-123456789));
        byte[] unsignedScaled = Be(U(987654321));
        bool ok = w.TryWriteUInt64(200, out status) && w.TryWriteInt64(-5, out status) && w.TryWriteAddress(Owner, out status) && w.TryWriteBool(true, out status)
            && w.TryWriteBytes4(FunctionSelector.Erc20.Transfer, out status) && w.TryWriteFunction(Owner, FunctionSelector.Erc20.Approve, out status)
            && w.TryWriteBytes32(Topic, out status) && w.TryWriteUInt256(U(Pow2(200)), out status) && w.TryWriteInt256(S(-Pow2(200)), out status) && w.TryWriteFixedBytes(Bytes31, out status)
            && w.TryWriteSignedScaledWord(scaled, out status) && w.TryWriteUnsignedScaledWord(unsignedScaled, out status)
            && w.TryWriteBytes([1, 2, 3], out status) && w.TryWriteStringUtf8("hi"u8, out status)
            && w.TryBeginDynamicArray(2, out status) && w.TryWriteUInt64(10, out status) && w.TryWriteUInt64(20, out status) && w.TryEndContainer(out status)
            && w.TryBeginTuple(out status) && w.TryWriteUInt256(U(Pow2(160)), out status) && w.TryWriteAddress(Router, out status) && w.TryEndContainer(out status)
            && w.TryBeginFixedArray(out status) && w.TryWriteBytes4(FunctionSelector.Erc20.Approve, out status) && w.TryWriteBytes4(FunctionSelector.Erc20.Transfer, out status) && w.TryEndContainer(out status);
        Assert.IsTrue(ok, status.ToString());
        Assert.IsTrue(w.TryFinish(out int written, out status), status.ToString());
        return buffer[..written];
    }

    private static List<int[]> RichPaths()
    {
        List<int[]> paths = [[]];
        for (int i = 0; i <= 17; i++)
        {
            paths.Add([i]);
        }

        paths.AddRange([[14, 0], [14, 1], [14, 2], [15, 0], [15, 1], [15, 2], [16, 0], [16, 1], [16, 2], [-1], [int.MaxValue], [int.MinValue], [0, 0], [12, 0], [13, 0], [1, 2, 3], [7, 0], [15, 0, 0], [14, -1], [14, int.MaxValue]]);
        return paths;
    }

    [TestMethod]
    public void TypedReaders_ReturnExactlyWhatTheSpanReadersReturn_OnEveryPathAndKind_WithTheSameStatusAndDefaultOuts()
    {
        ABIFunctionSchema f = Function(RichSignature);
        byte[] document = RichDocument();
        var limits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABIData.TryDecodeComplete(document, f.InputSchema, in limits, out ABIData a, out ABIStatus status), status.ToString());

        int successes = 0;
        foreach (int[] path in RichPaths())
        {
            string where = $"[{string.Join(",", path)}]";
            bool wantU = a.TryGetUInt256Word(path, out ReadOnlySpan<byte> uWord, out ABIStatus wantUStatus);
            bool gotU = a.TryGetUInt256(path, out uint256 u, out ABIStatus gotUStatus);
            Assert.AreEqual(wantU, gotU, where + " uint256");
            Assert.AreEqual(wantUStatus, gotUStatus, where + " uint256 status");
            Assert.AreEqual(wantU ? new uint256(uWord) : default, u, where + " uint256 value or default");

            bool wantI = a.TryGetInt256Word(path, out ReadOnlySpan<byte> iWord, out ABIStatus wantIStatus);
            bool gotI = a.TryGetInt256(path, out int256 i, out ABIStatus gotIStatus);
            Assert.AreEqual(wantI, gotI, where + " int256");
            Assert.AreEqual(wantIStatus, gotIStatus, where + " int256 status");
            Assert.AreEqual(wantI ? new int256(iWord) : default, i, where + " int256 value or default");

            bool wantA = a.TryGetAddress(path, out ReadOnlySpan<byte> address20, out ABIStatus wantAStatus);
            bool gotA = a.TryGetAddress(path, out Address address, out ABIStatus gotAStatus);
            Assert.AreEqual(wantA, gotA, where + " address");
            Assert.AreEqual(wantAStatus, gotAStatus, where + " address status");
            Assert.AreEqual(wantA ? Address.FromEvmBytes(address20) : default, address, where + " address value or default");

            // bytes32 / bytes4: path and kind errors are the span accessor's; another bytes<M> width is a TypeMismatch.
            bool spanOk = a.TryGetFixedBytes(path, out ReadOnlySpan<byte> fixedBytes, out ABIStatus fixedStatus);
            bool got32 = a.TryGetBytes32(path, out Hash32 h, out ABIStatus status32);
            bool got4 = a.TryGetBytes4(path, out FunctionSelector sel, out ABIStatus status4);
            Assert.AreEqual(spanOk && fixedBytes.Length == 32, got32, where + " bytes32");
            Assert.AreEqual(spanOk && fixedBytes.Length == 4, got4, where + " bytes4");
            Assert.AreEqual(spanOk ? (fixedBytes.Length == 32 ? ABIStatus.Success : ABIStatus.TypeMismatch) : fixedStatus, status32, where + " bytes32 status");
            Assert.AreEqual(spanOk ? (fixedBytes.Length == 4 ? ABIStatus.Success : ABIStatus.TypeMismatch) : fixedStatus, status4, where + " bytes4 status");
            Assert.AreEqual(got32 ? new Hash32(fixedBytes) : default, h, where);
            Assert.AreEqual(got4 ? new FunctionSelector(fixedBytes) : default, sel, where);

            bool wantF = a.TryGetFunction(path, out ReadOnlySpan<byte> functionAddress, out uint rawSelector, out ABIStatus wantFStatus);
            bool gotF = a.TryGetFunction(path, out Address target, out FunctionSelector selector, out ABIStatus gotFStatus);
            Assert.AreEqual(wantF, gotF, where + " function");
            Assert.AreEqual(wantFStatus, gotFStatus, where + " function status");
            Assert.AreEqual(wantF ? Address.FromEvmBytes(functionAddress) : default, target, where);
            Assert.AreEqual(wantF ? new FunctionSelector(rawSelector) : default, selector, where);

            successes += (gotU ? 1 : 0) + (gotI ? 1 : 0) + (gotA ? 1 : 0) + (got32 ? 1 : 0) + (got4 ? 1 : 0) + (gotF ? 1 : 0);
        }

        // Not vacuous: the document has two uint256-kind reads (7 and the tuple's member), two int, ... and every parity row above was exercised.
        Assert.IsGreaterThan(8, successes);

        // Fixed-point words never reach the typed integer readers (they have no typed form at all).
        Assert.IsFalse(a.TryGetInt256([10], out _, out status));
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
        Assert.IsFalse(a.TryGetUInt256([11], out _, out status));
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
        // bytes31 is neither bytes32 nor bytes4.
        Assert.IsFalse(a.TryGetBytes32([9], out Hash32 notHash, out status));
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
        Assert.AreEqual(default, notHash);
        Assert.IsFalse(a.TryGetBytes4([9], out FunctionSelector notSelector, out status));
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
        Assert.AreEqual(default, notSelector);
        // A bytes4 inside a fixed array is reachable and typed.
        Assert.IsTrue(a.TryGetBytes4([16, 1], out FunctionSelector second, out _));
        Assert.AreEqual(FunctionSelector.Erc20.Transfer, second);
        // A path error wins over a kind mismatch.
        Assert.IsFalse(a.TryGetBytes32([0, 0], out _, out status));
        Assert.AreEqual(ABIStatus.InvalidPath, status);
    }

    [TestMethod]
    public void TypedReaders_OnADefaultView_AreInvalidArgument_WithDefaultOuts_ForEveryPath()
    {
        ABIData invalid = default;
        foreach (int[] path in RichPaths())
        {
            Assert.IsFalse(invalid.TryGetUInt256(path, out uint256 u, out ABIStatus status));
            Assert.AreEqual(ABIStatus.InvalidArgument, status);
            Assert.AreEqual(default, u);
            Assert.IsFalse(invalid.TryGetInt256(path, out int256 i, out status));
            Assert.AreEqual(ABIStatus.InvalidArgument, status);
            Assert.AreEqual(default, i);
            Assert.IsFalse(invalid.TryGetAddress(path, out Address a, out status));
            Assert.AreEqual(ABIStatus.InvalidArgument, status);
            Assert.AreEqual(default, a);
            Assert.IsFalse(invalid.TryGetBytes32(path, out Hash32 h, out status));
            Assert.AreEqual(ABIStatus.InvalidArgument, status);
            Assert.AreEqual(default, h);
            Assert.IsFalse(invalid.TryGetBytes4(path, out FunctionSelector s, out status));
            Assert.AreEqual(ABIStatus.InvalidArgument, status);
            Assert.AreEqual(default, s);
            Assert.IsFalse(invalid.TryGetFunction(path, out Address t, out FunctionSelector fs, out status));
            Assert.AreEqual(ABIStatus.InvalidArgument, status);
            Assert.AreEqual(default, t);
            Assert.AreEqual(default, fs);
        }
    }

    [TestMethod]
    public void TypedReaders_OnTheOutVarCallSites_StillBindTheSpanMembers()
    {
        // Instance members win overload resolution: adding the extensions must not change what `out var` means (that is why they are extensions).
        ABIFunctionSchema f = Function(RichSignature);
        byte[] document = RichDocument();
        var limits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABIData.TryDecodeComplete(document, f.InputSchema, in limits, out ABIData a, out _));
        Assert.IsTrue(a.TryGetAddress([2], out var viaVar, out _));
        // Compiles only if `out var` bound the span member: Address has no SequenceEqual-compatible span shape.
        Assert.IsTrue(viaVar.SequenceEqual(Raw(Owner)), "out var binds the span member (a ReadOnlySpan<byte> of the 20 address bytes)");
        Assert.IsTrue(a.TryGetAddress([2], out _, out _), "a discard binds the span member as well");
    }

    // ------------------------------------------------------------------ writer parity with the span writers

    private enum Op
    {
        UInt256,
        Int256,
        Address,
        Bytes32,
        Bytes4,
        Function,
    }

    private readonly record struct Value(uint256 U, int256 S, Address A, Hash32 H, FunctionSelector Sel);

    private readonly record struct Outcome(bool Ok, ABIStatus Status, bool FinishOk, ABIStatus FinishStatus, int FinishLength, string Destination);


    private static Outcome RunABIWriter(string slotType, Op op, bool typed, Value v, ABIEncodeLimits limits)
    {
        ABIFunctionSchema f = Slot(slotType);
        byte[] destination = Enumerable.Repeat((byte)0xCD, 96).ToArray();
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[4];
        if (!ABIWriter.TryBegin(f.InputSchema, destination, frames, in limits, out ABIWriter w, out ABIStatus begin))
        {
            return new Outcome(false, begin, false, begin, 0, string.Empty);
        }

        bool ok;
        ABIStatus status;
        switch (op)
        {
            case Op.UInt256:
                ok = typed ? w.TryWriteUInt256(v.U, out status) : w.TryWriteUInt256Word(Be(v.U), out status);
                break;
            case Op.Int256:
                ok = typed ? w.TryWriteInt256(v.S, out status) : w.TryWriteInt256Word(Be(v.S), out status);
                break;
            case Op.Address:
                ok = typed ? w.TryWriteAddress(v.A, out status) : w.TryWriteAddress(Raw(v.A), out status);
                break;
            case Op.Bytes32:
                ok = typed ? w.TryWriteBytes32(v.H, out status) : w.TryWriteFixedBytes(Be(v.H), out status);
                break;
            case Op.Bytes4:
                ok = typed ? w.TryWriteBytes4(v.Sel, out status) : w.TryWriteFixedBytes(Be(v.Sel), out status);
                break;
            default:
                ok = typed ? w.TryWriteFunction(v.A, v.Sel, out status) : w.TryWriteFunction(Raw(v.A), v.Sel.Value, out status);
                break;
        }

        // A second, valid write and the finish, to compare the state the failed (or successful) operation left behind.
        bool fin = w.TryFinish(out int length, out ABIStatus finishStatus);
        return new Outcome(ok, status, fin, finishStatus, length, Convert.ToHexString(destination));
    }

    private static Outcome RunABICallEncoder(string slotType, Op op, bool typed, Value v, ABIEncodeLimits limits)
    {
        ABIFunctionSchema f = Slot(slotType);
        byte[] destination = Enumerable.Repeat((byte)0xCD, 100).ToArray();
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[4];
        if (!ABICallEncoder.TryBegin(f, destination, frames, in limits, out ABICallEncoder w, out ABIStatus begin))
        {
            return new Outcome(false, begin, false, begin, 0, string.Empty);
        }

        bool ok;
        ABIStatus status;
        switch (op)
        {
            case Op.UInt256:
                ok = typed ? w.TryWriteUInt256(v.U, out status) : w.TryWriteUInt256Word(Be(v.U), out status);
                break;
            case Op.Int256:
                ok = typed ? w.TryWriteInt256(v.S, out status) : w.TryWriteInt256Word(Be(v.S), out status);
                break;
            case Op.Address:
                ok = typed ? w.TryWriteAddress(v.A, out status) : w.TryWriteAddress(Raw(v.A), out status);
                break;
            case Op.Bytes32:
                ok = typed ? w.TryWriteBytes32(v.H, out status) : w.TryWriteFixedBytes(Be(v.H), out status);
                break;
            case Op.Bytes4:
                ok = typed ? w.TryWriteBytes4(v.Sel, out status) : w.TryWriteFixedBytes(Be(v.Sel), out status);
                break;
            default:
                ok = typed ? w.TryWriteFunction(v.A, v.Sel, out status) : w.TryWriteFunction(Raw(v.A), v.Sel.Value, out status);
                break;
        }

        // A second, valid write and the finish, to compare the state the failed (or successful) operation left behind.
        bool fin = w.TryFinish(out int length, out ABIStatus finishStatus);
        return new Outcome(ok, status, fin, finishStatus, length, Convert.ToHexString(destination));
    }

    private static Outcome RunABIMeasurer(string slotType, Op op, bool typed, Value v, ABIEncodeLimits limits)
    {
        ABIFunctionSchema f = Slot(slotType);
        
        Span<ABIMeasureFrame> frames = stackalloc ABIMeasureFrame[4];
        if (!ABIMeasurer.TryBegin(f.InputSchema, frames, in limits, out ABIMeasurer w, out ABIStatus begin))
        {
            return new Outcome(false, begin, false, begin, 0, string.Empty);
        }

        bool ok;
        ABIStatus status;
        switch (op)
        {
            case Op.UInt256:
                ok = typed ? w.TryWriteUInt256(v.U, out status) : w.TryWriteUInt256Word(Be(v.U), out status);
                break;
            case Op.Int256:
                ok = typed ? w.TryWriteInt256(v.S, out status) : w.TryWriteInt256Word(Be(v.S), out status);
                break;
            case Op.Address:
                ok = typed ? w.TryWriteAddress(v.A, out status) : w.TryWriteAddress(Raw(v.A), out status);
                break;
            case Op.Bytes32:
                ok = typed ? w.TryWriteBytes32(v.H, out status) : w.TryWriteFixedBytes(Be(v.H), out status);
                break;
            case Op.Bytes4:
                ok = typed ? w.TryWriteBytes4(v.Sel, out status) : w.TryWriteFixedBytes(Be(v.Sel), out status);
                break;
            default:
                ok = typed ? w.TryWriteFunction(v.A, v.Sel, out status) : w.TryWriteFunction(Raw(v.A), v.Sel.Value, out status);
                break;
        }

        // A second, valid write and the finish, to compare the state the failed (or successful) operation left behind.
        bool fin = w.TryFinish(out int length, out ABIStatus finishStatus);
        return new Outcome(ok, status, fin, finishStatus, length, string.Empty);
    }


    private static Outcome Run(int writerType, string slotType, Op op, bool typed, Value v, ABIEncodeLimits limits) => writerType switch
    {
        0 => RunABIWriter(slotType, op, typed, v, limits),
        1 => RunABICallEncoder(slotType, op, typed, v, limits),
        _ => RunABIMeasurer(slotType, op, typed, v, limits),
    };

    private static readonly string[] SlotTypes =
        ["uint8", "uint160", "uint256", "int8", "int256", "address", "bool", "bytes4", "bytes31", "bytes32", "function", "fixed128x18", "ufixed128x18", "bytes", "string", "uint256[]", "uint8[2]", "(uint256)"];

    private static IEnumerable<Value> ValuesFor(Op op)
    {
        Value Base() => new(U(0), S(0), Owner, Topic, FunctionSelector.Erc20.Transfer);
        switch (op)
        {
            case Op.UInt256:
                foreach (BigInteger b in new BigInteger[] { 0, 1, 255, 256, Pow2(64), Pow2(160) - 1, Pow2(160), Pow2(255), Pow2(256) - 1 })
                {
                    yield return Base() with { U = U(b) };
                }

                break;
            case Op.Int256:
                foreach (BigInteger b in new BigInteger[] { -Pow2(255), -Pow2(159) - 1, -129, -128, -1, 0, 127, 128, Pow2(160) - 1, Pow2(255) - 1 })
                {
                    yield return Base() with { S = S(b) };
                }

                break;
            case Op.Address:
            case Op.Function:
                yield return Base();
                yield return Base() with { A = Address.Parse("0x0000000000000000000000000000000000000000") };
                yield return Base() with { A = Solana };
                break;
            case Op.Bytes32:
                yield return Base();
                yield return Base() with { H = default };
                break;
            default:
                yield return Base();
                yield return Base() with { Sel = default };
                break;
        }
    }

    [TestMethod]
    public void TypedWriters_AreIdenticalToTheSpanWriters_OnEverySlotKindAndValue_ForAllThreeWriterTypes()
    {
        var limits = ABIEncodeLimits.Default;
        int compared = 0;
        int succeeded = 0;
        foreach (int writerType in new[] { 0, 1, 2 })
        {
            foreach (string slot in SlotTypes)
            {
                foreach (Op op in Enum.GetValues<Op>())
                {
                    foreach (Value v in ValuesFor(op))
                    {
                        Outcome span = Run(writerType, slot, op, typed: false, v, limits);
                        Outcome typed = Run(writerType, slot, op, typed: true, v, limits);
                        Assert.AreEqual(span, typed, $"writer {writerType}, slot {slot}, op {op}, value {v}");
                        compared++;
                        succeeded += typed.Ok ? 1 : 0;
                    }
                }
            }
        }

        Assert.AreEqual(3 * SlotTypes.Length * Enum.GetValues<Op>().Sum(op => ValuesFor(op).Count()), compared, "every writer type x slot x op x value was compared");
        Assert.IsGreaterThan(100, succeeded, "a parity test that never succeeds proves little");
    }

    private static void ExpectTyped(int writerType, string slot, Op op, Value v, ABIStatus expected, string what)
    {
        Outcome typed = Run(writerType, slot, op, typed: true, v, ABIEncodeLimits.Default);
        Assert.AreEqual(expected, typed.Status, $"writer {writerType}: {what}");
        Assert.AreEqual(expected == ABIStatus.Success, typed.Ok, $"writer {writerType}: {what}");
        if (expected != ABIStatus.Success)
        {
            // Atomic and faulting: nothing was written by the failing operation, and the writer can never finish.
            Assert.IsFalse(typed.FinishOk, $"writer {writerType}: {what}");
            Assert.AreEqual(ABIStatus.InvalidWriterState, typed.FinishStatus, $"writer {writerType}: {what}");
            Assert.AreEqual(0, typed.FinishLength);
            if (writerType != 2)
            {
                // The destination started as 0xCD garbage; the call encoder's four selector bytes are written by TryBegin, before the failing operation.
                string body = writerType == 1 ? typed.Destination[8..] : typed.Destination;
                Assert.AreEqual(string.Concat(Enumerable.Repeat("CD", body.Length / 2)), body, $"writer {writerType}: {what}: a failed operation wrote bytes");
            }
        }
    }

    [TestMethod]
    public void TypedWriters_ReportTheDocumentedStatuses_ForNarrowSlotsWrongKindsWrongWidthsAndNonEvmAddresses()
    {
        foreach (int writerType in new[] { 0, 1, 2 })
        {
            Value b = new(U(0), S(0), Owner, Topic, FunctionSelector.Erc20.Transfer);

            // Narrow slots: a value that does not fit is ValueOutOfRange (never truncated); the maximum that fits succeeds.
            ExpectTyped(writerType, "uint8", Op.UInt256, b with { U = U(255) }, ABIStatus.Success, "uint8 255");
            ExpectTyped(writerType, "uint8", Op.UInt256, b with { U = U(256) }, ABIStatus.ValueOutOfRange, "uint8 256");
            ExpectTyped(writerType, "uint8", Op.UInt256, b with { U = U(Pow2(256) - 1) }, ABIStatus.ValueOutOfRange, "uint8 max");
            ExpectTyped(writerType, "uint160", Op.UInt256, b with { U = U(Pow2(160) - 1) }, ABIStatus.Success, "uint160 max");
            ExpectTyped(writerType, "uint160", Op.UInt256, b with { U = U(Pow2(160)) }, ABIStatus.ValueOutOfRange, "uint160 2^160");
            ExpectTyped(writerType, "uint256", Op.UInt256, b with { U = U(Pow2(256) - 1) }, ABIStatus.Success, "uint256 max");
            ExpectTyped(writerType, "int8", Op.Int256, b with { S = S(127) }, ABIStatus.Success, "int8 127");
            ExpectTyped(writerType, "int8", Op.Int256, b with { S = S(-128) }, ABIStatus.Success, "int8 -128");
            ExpectTyped(writerType, "int8", Op.Int256, b with { S = S(128) }, ABIStatus.ValueOutOfRange, "int8 128");
            ExpectTyped(writerType, "int8", Op.Int256, b with { S = S(-129) }, ABIStatus.ValueOutOfRange, "int8 -129");
            ExpectTyped(writerType, "int256", Op.Int256, b with { S = S(-Pow2(255)) }, ABIStatus.Success, "int256 min");
            ExpectTyped(writerType, "int256", Op.Int256, b with { S = S(Pow2(255) - 1) }, ABIStatus.Success, "int256 max");

            // Wrong slot kind: TypeMismatch, whatever the value (fixed-point slots are never served by the integer writers).
            ExpectTyped(writerType, "address", Op.UInt256, b, ABIStatus.TypeMismatch, "uint256 into address");
            ExpectTyped(writerType, "uint256", Op.Int256, b, ABIStatus.TypeMismatch, "int256 into uint256");
            ExpectTyped(writerType, "fixed128x18", Op.Int256, b, ABIStatus.TypeMismatch, "int256 into fixed");
            ExpectTyped(writerType, "ufixed128x18", Op.UInt256, b, ABIStatus.TypeMismatch, "uint256 into ufixed");
            ExpectTyped(writerType, "uint256", Op.Address, b, ABIStatus.TypeMismatch, "address into uint256");
            ExpectTyped(writerType, "bytes", Op.Bytes32, b, ABIStatus.TypeMismatch, "bytes32 into bytes");
            ExpectTyped(writerType, "uint256[]", Op.UInt256, b, ABIStatus.TypeMismatch, "uint256 into an array slot");
            ExpectTyped(writerType, "(uint256)", Op.UInt256, b, ABIStatus.TypeMismatch, "uint256 into a tuple slot");

            // bytes<M> width: the value must be exactly M bytes (a bytes32 value never fits a bytes31 or bytes4 slot).
            ExpectTyped(writerType, "bytes32", Op.Bytes32, b, ABIStatus.Success, "bytes32");
            ExpectTyped(writerType, "bytes4", Op.Bytes4, b, ABIStatus.Success, "bytes4");
            ExpectTyped(writerType, "bytes31", Op.Bytes32, b, ABIStatus.InvalidArgument, "bytes32 into bytes31");
            ExpectTyped(writerType, "bytes4", Op.Bytes32, b, ABIStatus.InvalidArgument, "bytes32 into bytes4");
            ExpectTyped(writerType, "bytes32", Op.Bytes4, b, ABIStatus.InvalidArgument, "bytes4 into bytes32");

            // Addresses: EVM works, a Solana address is InvalidArgument (like a wrong-length span) after the kind check.
            ExpectTyped(writerType, "address", Op.Address, b, ABIStatus.Success, "evm address");
            ExpectTyped(writerType, "address", Op.Address, b with { A = Solana }, ABIStatus.InvalidArgument, "solana address");
            ExpectTyped(writerType, "function", Op.Function, b, ABIStatus.Success, "evm function");
            ExpectTyped(writerType, "function", Op.Function, b with { A = Solana }, ABIStatus.InvalidArgument, "solana function target");
            ExpectTyped(writerType, "uint256", Op.Address, b with { A = Solana }, ABIStatus.TypeMismatch, "kind check precedes the address family check");
            ExpectTyped(writerType, "uint256", Op.Function, b with { A = Solana }, ABIStatus.TypeMismatch, "kind check precedes the address family check (function)");
        }
    }

    [TestMethod]
    public void ASolanaAddress_FollowsTheSameStatusPrecedenceAsAWrongLengthSpan_AfterStateArityKindAndBudgets()
    {
        var generous = ABIEncodeLimits.Default;
        // Budget: two work units cover the root and nothing else; the value is refused for its BUDGET before its source length is looked at.
        ABIEncodeLimits tight = new(generous.MaxEncodedBytes, generous.MaxValueDepth, generous.MaxArrayElements, generous.MaxWrittenValues, 2);
        Value v = new(U(0), S(0), Solana, Topic, FunctionSelector.Erc20.Transfer);
        foreach (int writerType in new[] { 0, 1, 2 })
        {
            Outcome typed = Run(writerType, "address", Op.Address, typed: true, v, tight);
            Outcome span = Run(writerType, "address", Op.Address, typed: false, v, tight);
            Assert.AreEqual(span, typed, $"writer {writerType}");
            Assert.AreEqual(ABIStatus.WorkLimitExceeded, typed.Status, $"writer {writerType}: a budget error precedes the source-length error");
        }

        // Arity: an extra value after the last slot is ElementLimitExceeded even for a Solana address (the container is full before anything else is examined).
        ABIFunctionSchema one = Slot("address");
        byte[] destination = new byte[4 + 64];
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[2];
        Assert.IsTrue(ABICallEncoder.TryBegin(one, destination, frames, in generous, out ABICallEncoder e, out ABIStatus status));
        Assert.IsTrue(e.TryWriteAddress(Owner, out status));
        Assert.IsFalse(e.TryWriteAddress(Solana, out status));
        Assert.AreEqual(ABIStatus.ElementLimitExceeded, status);
        Assert.IsFalse(e.TryWriteAddress(Owner, out status), "faulted by the first failure");
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);

        // A default writer answers InvalidWriterState before the address family is looked at.
        ABIWriter dead = default;
        Assert.IsFalse(dead.TryWriteAddress(Solana, out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        ABIMeasurer deadMeasurer = default;
        Assert.IsFalse(deadMeasurer.TryWriteAddress(Solana, out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        ABICallEncoder deadEncoder = default;
        Assert.IsFalse(deadEncoder.TryWriteFunction(Solana, FunctionSelector.Erc20.Transfer, out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
    }

    [TestMethod]
    public void AFailedTypedWrite_FaultsTheWriter_LeavesTheDestinationUntouched_AndNothingAfterItSucceeds_ForAllThreeWriterTypes()
    {
        ABIFunctionSchema f = Function("g(uint8,uint8)");
        var limits = ABIEncodeLimits.Default;
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[2];

        // ABIWriter
        byte[] destination = Enumerable.Repeat((byte)0xAA, 64).ToArray();
        Assert.IsTrue(ABIWriter.TryBegin(f.InputSchema, destination, frames, in limits, out ABIWriter w, out ABIStatus status));
        Assert.IsTrue(w.TryWriteUInt256(U(5), out status));
        byte[] afterFirst = (byte[])destination.Clone();
        Assert.IsFalse(w.TryWriteUInt256(U(256), out status));
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
        CollectionAssert.AreEqual(afterFirst, destination, "the failed typed write changed no byte");
        Assert.IsFalse(w.TryWriteUInt256(U(1), out status), "a faulted writer refuses a valid value");
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.IsFalse(w.TryWriteUInt64(1, out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.IsFalse(w.TryFinish(out int length, out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.AreEqual(0, length);
        CollectionAssert.AreEqual(afterFirst, destination);

        // ABICallEncoder: the selector is already there, and the whole buffer is void.
        byte[] callBuffer = Enumerable.Repeat((byte)0xAA, 68).ToArray();
        Assert.IsTrue(ABICallEncoder.TryBegin(f, callBuffer, frames, in limits, out ABICallEncoder e, out status));
        Assert.IsFalse(e.TryWriteInt256(S(1), out status), "an int256 value does not go into a uint8 slot");
        Assert.AreEqual(ABIStatus.TypeMismatch, status);
        Assert.IsFalse(e.TryWriteUInt256(U(1), out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.IsFalse(e.TryFinish(out int callLength, out status));
        Assert.AreEqual(0, callLength);

        // ABIMeasurer
        Span<ABIMeasureFrame> measureFrames = stackalloc ABIMeasureFrame[2];
        Assert.IsTrue(ABIMeasurer.TryBegin(f.InputSchema, measureFrames, in limits, out ABIMeasurer m, out status));
        Assert.IsTrue(m.TryWriteUInt256(U(5), out status));
        Assert.IsFalse(m.TryWriteUInt256(U(256), out status));
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
        Assert.IsFalse(m.TryWriteUInt256(U(1), out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
        Assert.IsFalse(m.TryFinish(out int measured, out status));
        Assert.AreEqual(0, measured);
    }

    // ------------------------------------------------------------------ call-site conversions that throw BEFORE the codec runs

    private static uint256 FromInt(int value) => value; // the implicit conversion the type remarks warn about

    private static uint256 FromLong(long value) => value;

    [TestMethod]
    public void ImplicitNegativeConversionsAndUint256Arithmetic_ThrowAtTheCallSite_BeforeTheCodecRuns_AndDoNotFaultTheWriter()
    {
        Assert.ThrowsExactly<OverflowException>(() => FromInt(-1));
        Assert.ThrowsExactly<OverflowException>(() => FromLong(long.MinValue));
        Assert.ThrowsExactly<OverflowException>(() => uint256.MaxValue + uint256.One);
        Assert.ThrowsExactly<OverflowException>(() => uint256.Zero - uint256.One);
        Assert.AreEqual(U(5), FromInt(5), "non-negative values convert");

        // The exception comes from the argument expression, so the writer never sees the call: it is not faulted.
        ABIFunctionSchema f = Function("g(uint256)");
        byte[] buffer = new byte[32];
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[2];
        var limits = ABIEncodeLimits.Default;
        Assert.IsTrue(ABIWriter.TryBegin(f.InputSchema, buffer, frames, in limits, out ABIWriter w, out ABIStatus status));
        bool threw = false;
        try
        {
            _ = w.TryWriteUInt256(FromInt(-1), out status);
        }
        catch (OverflowException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "the conversion threw in the caller's expression");
        Assert.IsTrue(w.TryWriteUInt256(U(9), out status), "the writer is still open: the codec was never entered");
        Assert.IsTrue(w.TryFinish(out int written, out status));
        Assert.AreEqual(32, written);
        Assert.AreEqual(9, buffer[31]);
    }

    // ------------------------------------------------------------------ compile contract: stack-allocated spans


    private static void StackallocSpansToABIWriter()
    {
        ABIFunctionSchema f = Function("h(uint256,int256,address,bytes4,function,bytes,string,fixed128x18,ufixed128x18,bytes32,uint8[2])");
        byte[] destination = new byte[2048];
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[4];
        var limits = ABIEncodeLimits.Default;
        Assert.IsTrue(ABIWriter.TryBegin(f.InputSchema, destination, frames, in limits, out ABIWriter w, out ABIStatus status), status.ToString());

        // Every span argument is a stackalloc local: this must COMPILE (the parameters are scoped) and write the right bytes.
        Span<byte> word = stackalloc byte[32];
        word[31] = 7;
        Span<byte> negative = stackalloc byte[32];
        negative.Fill(0xff);
        Span<byte> address = stackalloc byte[20];
        address.Fill(0xab);
        Span<byte> four = stackalloc byte[4];
        four.Fill(0xcd);
        Span<byte> text = stackalloc byte[3];
        text[0] = (byte)'h'; text[1] = (byte)'i'; text[2] = (byte)'!';
        Span<byte> raw = stackalloc byte[5];
        raw.Fill(0x11);
        Span<byte> thirtyTwo = stackalloc byte[32];
        thirtyTwo.Fill(0x22);
        ReadOnlySpan<byte> readOnlyWord = word;

        Assert.IsTrue(w.TryWriteUInt256Word(readOnlyWord, out status), "uint256 " + status);
        Assert.IsTrue(w.TryWriteInt256Word(negative, out status), "int256 " + status);
        Assert.IsTrue(w.TryWriteAddress(address, out status), "address " + status);
        Assert.IsTrue(w.TryWriteFixedBytes(four, out status), "bytes4 " + status);
        Assert.IsTrue(w.TryWriteFunction(address, 0x01020304, out status), "function " + status);
        Assert.IsTrue(w.TryWriteBytes(raw, out status), "bytes " + status);
        Assert.IsTrue(w.TryWriteStringUtf8(text, out status), "string " + status);
        Assert.IsTrue(w.TryWriteSignedScaledWord(negative, out status), "fixed " + status);
        Assert.IsTrue(w.TryWriteUnsignedScaledWord(word, out status), "ufixed " + status);
        Assert.IsTrue(w.TryWriteFixedBytes(thirtyTwo, out status), "bytes32 " + status);
        Assert.IsTrue(w.TryBeginFixedArray(out status));
        Assert.IsTrue(w.TryWriteUInt64(1, out status));
        Assert.IsTrue(w.TryWriteUInt256Word(word, out status));
        Assert.IsTrue(w.TryEndContainer(out status));
        Assert.IsTrue(w.TryFinish(out int length, out status), status.ToString());
        Assert.AreEqual(ExpectedStackallocArgumentLength, length);
    }

    private static void StackallocSpansToABICallEncoder()
    {
        ABIFunctionSchema f = Function("h(uint256,int256,address,bytes4,function,bytes,string,fixed128x18,ufixed128x18,bytes32,uint8[2])");
        byte[] destination = new byte[2048];
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[4];
        var limits = ABIEncodeLimits.Default;
        Assert.IsTrue(ABICallEncoder.TryBegin(f, destination, frames, in limits, out ABICallEncoder w, out ABIStatus status), status.ToString());

        // Every span argument is a stackalloc local: this must COMPILE (the parameters are scoped) and write the right bytes.
        Span<byte> word = stackalloc byte[32];
        word[31] = 7;
        Span<byte> negative = stackalloc byte[32];
        negative.Fill(0xff);
        Span<byte> address = stackalloc byte[20];
        address.Fill(0xab);
        Span<byte> four = stackalloc byte[4];
        four.Fill(0xcd);
        Span<byte> text = stackalloc byte[3];
        text[0] = (byte)'h'; text[1] = (byte)'i'; text[2] = (byte)'!';
        Span<byte> raw = stackalloc byte[5];
        raw.Fill(0x11);
        Span<byte> thirtyTwo = stackalloc byte[32];
        thirtyTwo.Fill(0x22);
        ReadOnlySpan<byte> readOnlyWord = word;

        Assert.IsTrue(w.TryWriteUInt256Word(readOnlyWord, out status), "uint256 " + status);
        Assert.IsTrue(w.TryWriteInt256Word(negative, out status), "int256 " + status);
        Assert.IsTrue(w.TryWriteAddress(address, out status), "address " + status);
        Assert.IsTrue(w.TryWriteFixedBytes(four, out status), "bytes4 " + status);
        Assert.IsTrue(w.TryWriteFunction(address, 0x01020304, out status), "function " + status);
        Assert.IsTrue(w.TryWriteBytes(raw, out status), "bytes " + status);
        Assert.IsTrue(w.TryWriteStringUtf8(text, out status), "string " + status);
        Assert.IsTrue(w.TryWriteSignedScaledWord(negative, out status), "fixed " + status);
        Assert.IsTrue(w.TryWriteUnsignedScaledWord(word, out status), "ufixed " + status);
        Assert.IsTrue(w.TryWriteFixedBytes(thirtyTwo, out status), "bytes32 " + status);
        Assert.IsTrue(w.TryBeginFixedArray(out status));
        Assert.IsTrue(w.TryWriteUInt64(1, out status));
        Assert.IsTrue(w.TryWriteUInt256Word(word, out status));
        Assert.IsTrue(w.TryEndContainer(out status));
        Assert.IsTrue(w.TryFinish(out int length, out status), status.ToString());
        Assert.AreEqual(4 + ExpectedStackallocArgumentLength, length);
    }

    private static void StackallocSpansToABIMeasurer()
    {
        ABIFunctionSchema f = Function("h(uint256,int256,address,bytes4,function,bytes,string,fixed128x18,ufixed128x18,bytes32,uint8[2])");
        
        Span<ABIMeasureFrame> frames = stackalloc ABIMeasureFrame[4];
        var limits = ABIEncodeLimits.Default;
        Assert.IsTrue(ABIMeasurer.TryBegin(f.InputSchema, frames, in limits, out ABIMeasurer w, out ABIStatus status), status.ToString());

        // Every span argument is a stackalloc local: this must COMPILE (the parameters are scoped) and write the right bytes.
        Span<byte> word = stackalloc byte[32];
        word[31] = 7;
        Span<byte> negative = stackalloc byte[32];
        negative.Fill(0xff);
        Span<byte> address = stackalloc byte[20];
        address.Fill(0xab);
        Span<byte> four = stackalloc byte[4];
        four.Fill(0xcd);
        Span<byte> text = stackalloc byte[3];
        text[0] = (byte)'h'; text[1] = (byte)'i'; text[2] = (byte)'!';
        Span<byte> raw = stackalloc byte[5];
        raw.Fill(0x11);
        Span<byte> thirtyTwo = stackalloc byte[32];
        thirtyTwo.Fill(0x22);
        ReadOnlySpan<byte> readOnlyWord = word;

        Assert.IsTrue(w.TryWriteUInt256Word(readOnlyWord, out status), "uint256 " + status);
        Assert.IsTrue(w.TryWriteInt256Word(negative, out status), "int256 " + status);
        Assert.IsTrue(w.TryWriteAddress(address, out status), "address " + status);
        Assert.IsTrue(w.TryWriteFixedBytes(four, out status), "bytes4 " + status);
        Assert.IsTrue(w.TryWriteFunction(address, 0x01020304, out status), "function " + status);
        Assert.IsTrue(w.TryWriteBytes(raw, out status), "bytes " + status);
        Assert.IsTrue(w.TryWriteStringUtf8(text, out status), "string " + status);
        Assert.IsTrue(w.TryWriteSignedScaledWord(negative, out status), "fixed " + status);
        Assert.IsTrue(w.TryWriteUnsignedScaledWord(word, out status), "ufixed " + status);
        Assert.IsTrue(w.TryWriteFixedBytes(thirtyTwo, out status), "bytes32 " + status);
        Assert.IsTrue(w.TryBeginFixedArray(out status));
        Assert.IsTrue(w.TryWriteUInt64(1, out status));
        Assert.IsTrue(w.TryWriteUInt256Word(word, out status));
        Assert.IsTrue(w.TryEndContainer(out status));
        Assert.IsTrue(w.TryFinish(out int length, out status), status.ToString());
        Assert.AreEqual(ExpectedStackallocArgumentLength, length);
    }


    [TestMethod]
    public void EveryWriteMethodOfEveryWriterType_AcceptsStackAllocatedSpans()
    {
        StackallocSpansToABIWriter();
        StackallocSpansToABICallEncoder();
        StackallocSpansToABIMeasurer();
    }

    [TestMethod]
    public void TheSpanParametersOfEveryWriteMethod_AreDeclaredScoped()
    {
        // The contract that makes the stackalloc tests above compile, pinned by reflection too: if a `scoped` is dropped the compile contract test
        // fails to build, but a reflection check names the member. (scoped on a ReadOnlySpan<byte> parameter is recorded as ScopedRefAttribute.)
        Type[] writerTypes = [typeof(ABIWriter), typeof(ABICallEncoder), typeof(ABIMeasurer)];
        int checkedParameters = 0;
        foreach (Type type in writerTypes)
        {
            foreach (System.Reflection.MethodInfo method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            {
                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    if (parameter.ParameterType == typeof(ReadOnlySpan<byte>))
                    {
                        bool scoped = parameter.GetCustomAttributesData().Any(a => a.AttributeType.Name == "ScopedRefAttribute");
                        Assert.IsTrue(scoped, $"{type.Name}.{method.Name}({parameter.Name}) must be `scoped`");
                        checkedParameters++;
                    }
                }
            }
        }

        Assert.AreEqual(27, checkedParameters, "nine span-taking members on each of the three writer types");
    }

    [TestMethod]
    public void ThePathParameterOfEveryReader_IsDeclaredScoped_SoAStackAllocatedPathCanFeedAReturnedView()
    {
        // The out spans of the readers point into the document, never into the path. Without `scoped` on the path, a stackalloc path makes
        // `d.TryGetAddress(path, out ReadOnlySpan<byte> a, ..); return a;` fail with CS8352 (found by the round-2 review).
        int checkedParameters = 0;
        foreach (Type type in new[] { typeof(ABIData), typeof(ABITypedExtensions) })
        {
            foreach (System.Reflection.MethodInfo method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            {
                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    if (parameter.ParameterType == typeof(ReadOnlySpan<int>))
                    {
                        bool scoped = parameter.GetCustomAttributesData().Any(a => a.AttributeType.Name == "ScopedRefAttribute");
                        Assert.IsTrue(scoped, $"{type.Name}.{method.Name}({parameter.Name}) must be `scoped`");
                        checkedParameters++;
                    }
                }
            }
        }

        Assert.AreEqual(21, checkedParameters, "fifteen span members (TryLocate, TryGetArrayLength and the thirteen accessors) plus six typed readers");
    }

    private static ReadOnlySpan<byte> ReadAddressWithAStackAllocatedPath(in ABIData arguments)
    {
        // Compiles only while the path parameters are `scoped`; the returned view is of the document, not of the stack.
        Span<int> path = stackalloc int[1];
        path[0] = 0;
        arguments.TryGetAddress(path, out ReadOnlySpan<byte> address, out _);
        return address;
    }

    [TestMethod]
    public void AStackAllocatedPath_DoesNotTieTheReturnedViewToTheStack()
    {
        ABIFunctionSchema transfer = Function("transfer(address,uint256)");
        byte[] calldata = Hex(ABIFixtures.TransferVitalik1Eth);
        var limits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABICallData.TryDecode(calldata, transfer, in limits, out ABICallData call, out ABIStatus status), status.ToString());
        ReadOnlySpan<byte> address = ReadAddressWithAStackAllocatedPath(call.Arguments);
        Assert.AreEqual(20, address.Length);
        Assert.AreEqual("d8da6bf26964af9d7eed9e03e53415d37aa96045", Convert.ToHexString(address).ToLowerInvariant());
    }

    // ------------------------------------------------------------------ real-chain fixtures (bytes from Foundry cast 1.9.0)

    [TestMethod]
    public void ApproveRouterMax_IsWrittenAndReadAsAUint256_AndTheUInt64AccessorCannotReadIt()
    {
        // The case the old README example got wrong: approve(spender, type(uint256).max) is a routine call, and its amount does not fit 64 bits.
        ABIFunctionSchema approve = Function("approve(address,uint256)");
        byte[] expected = Hex(ABIFixtures.ApproveRouterMax); // cast calldata 'approve(address,uint256)' 0x68b3465833fb72A70ecDF485E0e4C7bD8665Fc45 <2^256-1>
        byte[] buffer = new byte[68];
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[2];
        var limits = ABIEncodeLimits.Default;
        Assert.IsTrue(ABICallEncoder.TryBegin(approve, buffer, frames, in limits, out ABICallEncoder e, out ABIStatus status));
        Assert.IsTrue(e.TryWriteAddress(Router, out status));
        Assert.IsTrue(e.TryWriteUInt256(uint256.MaxValue, out status));
        Assert.IsTrue(e.TryFinish(out int written, out status));
        Assert.AreEqual(68, written);
        CollectionAssert.AreEqual(expected, buffer);

        var decodeLimits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABICallData.TryDecode(expected, approve, in decodeLimits, out ABICallData call, out status));
        Assert.IsTrue(call.Arguments.TryGetAddress([0], out Address spender, out _));
        Assert.AreEqual(Router, spender);
        Assert.IsTrue(call.Arguments.TryGetUInt256([1], out uint256 amount, out _));
        Assert.AreEqual(uint256.MaxValue, amount);
        Assert.IsFalse(call.Arguments.TryGetUInt64([1], out ulong narrowed, out status), "TryGetUInt64 is the wrong accessor for an ERC-20 amount");
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
        Assert.AreEqual(0UL, narrowed);
    }

    [TestMethod]
    public void TransferOf1e20Wei_RoundTripsAsAUint256_AndDoesNotFitUInt64()
    {
        // cast calldata 'transfer(address,uint256)' 0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045 100000000000000000000
        byte[] expected = Hex("a9059cbb000000000000000000000000d8da6bf26964af9d7eed9e03e53415d37aa960450000000000000000000000000000000000000000000000056bc75e2d63100000");
        ABIFunctionSchema transfer = Function("transfer(address,uint256)");
        uint256 amount = U(BigInteger.Parse("100000000000000000000"));
        Assert.IsTrue((BigInteger)amount > ulong.MaxValue, "1e20 is above 2^64");
        byte[] buffer = new byte[68];
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[2];
        var limits = ABIEncodeLimits.Default;
        Assert.IsTrue(ABICallEncoder.TryBegin(transfer, buffer, frames, in limits, out ABICallEncoder e, out ABIStatus status));
        Assert.IsTrue(e.TryWriteAddress(Owner, out status));
        Assert.IsTrue(e.TryWriteUInt256(amount, out status));
        Assert.IsTrue(e.TryFinish(out _, out status));
        CollectionAssert.AreEqual(expected, buffer);

        var decodeLimits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABICallData.TryDecode(buffer, transfer, in decodeLimits, out ABICallData call, out status));
        Assert.IsTrue(call.Arguments.TryGetUInt256([1], out uint256 read, out _));
        Assert.AreEqual(amount, read);
        Assert.IsFalse(call.Arguments.TryGetUInt64([1], out _, out status));
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
    }

    [TestMethod]
    public void UniswapSqrtPriceLimitX96_IsAUint160_AndTheTypedWriterEnforcesItsWidth()
    {
        // MAX_SQRT_RATIO - 1 of Uniswap V3 TickMath (1461446703485210103287273052203988822378723970341), a real 160-bit price limit.
        // cast calldata 'exactInputSingle((address,address,uint24,address,uint256,uint256,uint256,uint160))' '(WETH,USDC,3000,0xd8dA...,1700000000,1e18,1800000000,<limit>)'
        byte[] expected = Hex("414bf389000000000000000000000000c02aaa39b223fe8d0a0e5c4f27ead9083c756cc2000000000000000000000000a0b86991c6218b36c1d19d4a2e9eb0ce3606eb480000000000000000000000000000000000000000000000000000000000000bb8000000000000000000000000d8da6bf26964af9d7eed9e03e53415d37aa96045000000000000000000000000000000000000000000000000000000006553f1000000000000000000000000000000000000000000000000000de0b6b3a7640000000000000000000000000000000000000000000000000000000000006b49d200000000000000000000000000fffd8963efd1fc6a506488495d951d5263988d25");
        ABIFunctionSchema f = Function("exactInputSingle((address,address,uint24,address,uint256,uint256,uint256,uint160))");
        BigInteger limitValue = BigInteger.Parse("1461446703485210103287273052203988822378723970341");
        Assert.IsTrue(limitValue > ulong.MaxValue);
        Assert.IsTrue(limitValue < Pow2(160));

        byte[] buffer = new byte[4 + 8 * 32];
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[3];
        var limits = ABIEncodeLimits.Default;
        Assert.IsTrue(ABICallEncoder.TryBegin(f, buffer, frames, in limits, out ABICallEncoder e, out ABIStatus status), status.ToString());
        bool ok = e.TryBeginTuple(out status)
            && e.TryWriteAddress(Address.Parse("0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2"), out status)
            && e.TryWriteAddress(Address.Parse("0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48"), out status)
            && e.TryWriteUInt256(U(3000), out status)
            && e.TryWriteAddress(Owner, out status)
            && e.TryWriteUInt256(U(1700000000), out status)
            && e.TryWriteUInt256(U(BigInteger.Pow(10, 18)), out status)
            && e.TryWriteUInt256(U(1800000000), out status)
            && e.TryWriteUInt256(U(limitValue), out status)
            && e.TryEndContainer(out status)
            && e.TryFinish(out int written, out status);
        Assert.IsTrue(ok, status.ToString());
        CollectionAssert.AreEqual(expected, buffer);

        var decodeLimits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABICallData.TryDecode(buffer, f, in decodeLimits, out ABICallData call, out status));
        Assert.IsTrue(call.Arguments.TryGetUInt256([0, 7], out uint256 read, out _));
        Assert.AreEqual(limitValue, (BigInteger)read);
        Assert.IsFalse(call.Arguments.TryGetUInt64([0, 7], out _, out status));
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);

        // 2^160 does not fit the uint160 slot: ValueOutOfRange, and the writer is faulted.
        Assert.IsTrue(ABICallEncoder.TryBegin(f, new byte[4 + 8 * 32], frames, in limits, out e, out status));
        Assert.IsTrue(e.TryBeginTuple(out status));
        for (int i = 0; i < 7; i++)
        {
            Assert.IsTrue(i is 0 or 1 or 3 ? e.TryWriteAddress(Owner, out status) : e.TryWriteUInt256(U(1), out status), $"member {i}: {status}");
        }

        Assert.IsFalse(e.TryWriteUInt256(U(Pow2(160)), out status));
        Assert.AreEqual(ABIStatus.ValueOutOfRange, status);
        Assert.IsFalse(e.TryEndContainer(out status));
        Assert.AreEqual(ABIStatus.InvalidWriterState, status);
    }

    // ------------------------------------------------------------------ zero allocation

    [TestMethod]
    public void TypedAdapters_DoNotAllocate_OnSuccessOrFailure()
    {
        ABIFunctionSchema f = Function(AllScalars);
        byte[] call = EncodeCall(f, uint256.MaxValue, int256.MinValue);
        var decodeLimits = ABIDecodeLimits.Default;
        Assert.IsTrue(ABICallData.TryDecode(call, f, in decodeLimits, out ABICallData decoded, out _));
        ABIData a = decoded.Arguments;
        ABIFunctionSchema narrow = Function("g(uint8,address,bytes4)");
        byte[] buffer = new byte[4 + 96];
        Span<ABIWriteFrame> frames = stackalloc ABIWriteFrame[2];
        Span<ABIMeasureFrame> measureFrames = stackalloc ABIMeasureFrame[2];
        var encodeLimits = ABIEncodeLimits.Default;

        long sink = 0;
        for (int warm = 0; warm < 3; warm++)
        {
            sink += AllocationBatch(in a, narrow, buffer, frames, measureFrames, in encodeLimits);
        }

        long best = long.MaxValue;
        for (int trial = 0; trial < 5; trial++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            sink += AllocationBatch(in a, narrow, buffer, frames, measureFrames, in encodeLimits);
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.AreEqual(0L, best, "typed adapters allocated");
        Assert.AreNotEqual(long.MinValue, sink);
    }

    private static long AllocationBatch(in ABIData a, ABIFunctionSchema narrow, byte[] buffer, Span<ABIWriteFrame> frames, Span<ABIMeasureFrame> measureFrames, in ABIEncodeLimits encodeLimits)
    {
        long checksum = 0;
        for (int n = 0; n < 500; n++)
        {
            if (a.TryGetUInt256(Path7, out uint256 u, out _) && a.TryGetInt256(Path8, out int256 i, out _) && a.TryGetAddress(Path2, out Address ad, out _)
                && a.TryGetBytes32(Path6, out Hash32 h, out _) && a.TryGetBytes4(Path4, out FunctionSelector s4, out _) && a.TryGetFunction(Path5, out _, out FunctionSelector s, out _))
            {
                checksum += (u.IsZero ? 1 : 0) + (i.IsZero ? 1 : 0) + (ad.IsZero ? 1 : 0) + (h.IsZero ? 1 : 0) + (s.IsZero ? 1 : 0) + (s4.IsZero ? 1 : 0);
            }

            // failures: kind mismatch, path error, wrong bytes width
            _ = a.TryGetUInt256(Path8, out _, out _);
            _ = a.TryGetAddress(Path99, out Address none, out _);
            _ = a.TryGetBytes32(Path9, out _, out _);

            _ = ABICallEncoder.TryBegin(narrow, buffer, frames, in encodeLimits, out ABICallEncoder e, out _);
            _ = e.TryWriteUInt256(U255, out _);              // fits uint8
            _ = e.TryWriteAddress(Owner, out _);
            _ = e.TryWriteBytes4(FunctionSelector.Erc20.Transfer, out _);
            _ = e.TryFinish(out int length, out _);
            checksum += length;

            _ = ABIWriter.TryBegin(narrow.InputSchema, buffer, frames, in encodeLimits, out ABIWriter w, out _);
            _ = w.TryWriteUInt256(U256, out _);              // does not fit uint8: faults
            _ = w.TryWriteAddress(Solana, out _);

            _ = ABIMeasurer.TryBegin(narrow.InputSchema, measureFrames, in encodeLimits, out ABIMeasurer m, out _);
            _ = m.TryWriteUInt256(U255, out _);
            _ = m.TryWriteAddress(Solana, out _);            // InvalidArgument path
            checksum += none.IsZero ? 1 : 0;
        }

        return checksum;
    }

    // Paths as preallocated arrays: a collection-expression literal allocates in Debug code, which made this test fail in Debug builds only.
    private static readonly int[] Path2 = [2];
    private static readonly int[] Path4 = [4];
    private static readonly int[] Path5 = [5];
    private static readonly int[] Path6 = [6];
    private static readonly int[] Path7 = [7];
    private static readonly int[] Path8 = [8];
    private static readonly int[] Path9 = [9];
    private static readonly int[] Path99 = [99];

    private static readonly uint256 U255 = new(255);
    private static readonly uint256 U256 = new(256);
}
