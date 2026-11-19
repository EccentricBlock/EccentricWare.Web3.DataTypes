using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using EccentricWare.Web3.DataTypes.ABI;
using EccentricWare.Web3.DataTypes.ABI.EvmAdapters;
using EccentricWare.Web3.DataTypes.Rlp;
using EccentricWare.Web3.DataTypes.Tests.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.EvmAdapters.EvmAdapterFixtures;

namespace EccentricWare.Web3.DataTypes.Tests.ABI.EvmAdapters;

/// <summary>
/// <see cref="RLPCalldataAdapter"/> (spec 9.4): every supported envelope type in both forms, built by an independent encoder (Nethereum.Model); the data
/// span is the unchanged envelope memory at the reported range; every context that is not a function call is classified and kept from the ABI codec;
/// every field rule has a failing case; mutated and truncated envelopes never throw; the call allocates nothing.
/// </summary>
[TestClass]
public sealed class RLPCalldataAdapterTests
{
    private static readonly ABIFunctionSchema Transfer = Function("transfer(address,uint256)");

    /// <summary>The view's properties, copied out (the view is a ref struct).</summary>
    private sealed record Snapshot(
        bool IsValid, EvmTransactionType Type, EvmTransactionForm Form, EvmTransactionKind Kind, ulong? ChainId, ulong Nonce, Address To, uint256 Value, byte[] Data,
        ABIRange DataRange, int AuthorizationCount);

    private static EvmTransactionStatus Read(byte[] envelope, EvmTransactionForm form, out Snapshot snapshot)
    {
        var limits = RLPDecodeLimits.Default;
        bool ok = RLPCalldataAdapter.TryRead(envelope, form, in limits, out EvmTransactionView view, out EvmTransactionStatus status);
        Assert.AreEqual(ok, status == EvmTransactionStatus.Success, "the bool and the status agree");
        snapshot = new Snapshot(view.IsValid, view.Type, view.Form, view.Kind, view.ChainId, view.Nonce, view.To, view.Value, view.Data.ToArray(), view.DataRange, view.AuthorizationCount);
        if (!ok)
        {
            // A failure leaves the default view.
            Assert.IsFalse(snapshot.IsValid);
            Assert.AreEqual(EvmTransactionKind.None, snapshot.Kind);
            Assert.IsNull(snapshot.ChainId);
            Assert.AreEqual(0UL, snapshot.Nonce);
            Assert.AreEqual(default, snapshot.To);
            Assert.AreEqual(uint256.Zero, snapshot.Value);
            Assert.IsEmpty(snapshot.Data);
            Assert.AreEqual(default, snapshot.DataRange);
            Assert.AreEqual(0, snapshot.AuthorizationCount);
        }

        return status;
    }

    private static EvmTransactionStatus Signed(byte[] envelope) => Read(envelope, EvmTransactionForm.SignedTransaction, out _);

    private static EvmTransactionStatus Unsigned(byte[] envelope) => Read(envelope, EvmTransactionForm.SigningPayload, out _);

    // ------------------------------------------------------------------ independence of the fixtures

    [TestMethod]
    public void IndependentEncoders_AgreeWithTheExplicitFieldLists()
    {
        byte[] data = TransferCalldata(5);
        CollectionAssert.AreEqual(NLegacy(RouterBytes, data).GetRLPEncoded(), Legacy(RouterBytes, data), "legacy (EIP-155)");
        CollectionAssert.AreEqual(N2930(RouterBytes, data).GetRLPEncoded(), Typed2930(RouterBytes, data), "EIP-2930");
        CollectionAssert.AreEqual(N1559(RouterBytes, data).GetRLPEncoded(), Typed1559(RouterBytes, data), "EIP-1559");
        CollectionAssert.AreEqual(N4844(RouterBytes, data).GetRLPEncoded(), Typed4844(RouterBytes, data), "EIP-4844");
        CollectionAssert.AreEqual(N7702(RouterBytes, data).GetRLPEncoded(), Typed7702(RouterBytes, data), "EIP-7702");

        // Signing payloads: typed = the signed list without yParity, r, s; legacy EIP-155 = the first six fields plus chainId, 0, 0; pre-EIP-155 = six fields.
        CollectionAssert.AreEqual(N2930(RouterBytes, data).GetRLPEncodedRaw(), Encode(0x01, WithoutSignature(Fields2930(RouterBytes, data))), "EIP-2930 payload");
        CollectionAssert.AreEqual(N1559(RouterBytes, data).GetRLPEncodedRaw(), Encode(0x02, WithoutSignature(Fields1559(RouterBytes, data))), "EIP-1559 payload");
        CollectionAssert.AreEqual(N4844(RouterBytes, data).GetRLPEncodedRaw(), Encode(0x03, WithoutSignature(Fields4844(RouterBytes, data))), "EIP-4844 payload");
        CollectionAssert.AreEqual(N7702(RouterBytes, data).GetRLPEncodedRaw(), Encode(0x04, WithoutSignature(Fields7702(RouterBytes, data))), "EIP-7702 payload");
        CollectionAssert.AreEqual(NLegacy(RouterBytes, data).GetRLPEncodedRaw(), Encode(null, [.. LegacyFields(RouterBytes, data)[..6], Number(ChainId), Number(0), Number(0)]), "legacy EIP-155 payload");
        CollectionAssert.AreEqual(NLegacyWithoutChainId(RouterBytes, data).GetRLPEncodedRaw(), Encode(null, LegacyFields(RouterBytes, data)[..6]), "legacy pre-EIP-155 payload");
    }

    // ------------------------------------------------------------------ normal flow

    [TestMethod]
    public void EverySignedType_YieldsTheUnchangedDataSpan_AtItsEnvelopeRange_WithItsFields()
    {
        byte[] data = TransferCalldata(5);
        foreach (var (name, type, signed, _) in Envelopes(RouterBytes, data, value: 7))
        {
            var limits = RLPDecodeLimits.Default;
            Assert.IsTrue(RLPCalldataAdapter.TryRead(signed, EvmTransactionForm.SignedTransaction, in limits, out EvmTransactionView view, out EvmTransactionStatus status), $"{name}: {status}");
            Assert.IsTrue(view.IsValid, name);
            Assert.AreEqual(type, view.Type, name);
            Assert.AreEqual(EvmTransactionForm.SignedTransaction, view.Form, name);
            Assert.AreEqual(EvmTransactionKind.FunctionCall, view.Kind, name);
            Assert.AreEqual(ChainId, view.ChainId, name);
            Assert.AreEqual(9UL, view.Nonce, name);
            Assert.AreEqual(Router, view.To, name);
            Assert.AreEqual(new uint256(7), view.Value, name);
            Assert.AreEqual(type == EvmTransactionType.Eip7702 ? 1 : 0, view.AuthorizationCount, name);

            // Zero copy: the span is the envelope's own memory at DataRange, and DataRange is where an independent search finds the bytes.
            CollectionAssert.AreEqual(data, view.Data.ToArray(), name);
            Assert.AreEqual(new ABIRange(signed.AsSpan().IndexOf(data), data.Length), view.DataRange, name);
            Assert.IsTrue(Unsafe.AreSame(in MemoryMarshal.GetReference(view.Data), in signed[view.DataRange.Offset]), $"{name}: the data is a copy");
            Assert.IsTrue(view.TryGetFunctionCalldata(out ReadOnlySpan<byte> calldata), name);
            Assert.IsTrue(Unsafe.AreSame(in MemoryMarshal.GetReference(calldata), in MemoryMarshal.GetReference(view.Data)), name);
        }
    }

    [TestMethod]
    public void EverySigningPayload_IsReadAsAPayload_AndRefusedAsASignedTransaction_AndTheReverse()
    {
        byte[] data = TransferCalldata(5);
        foreach (var (name, type, signed, unsigned) in Envelopes(RouterBytes, data, value: 3))
        {
            Assert.AreEqual(EvmTransactionStatus.Success, Read(unsigned, EvmTransactionForm.SigningPayload, out Snapshot payload), name);
            Assert.AreEqual(type, payload.Type, name);
            Assert.AreEqual(EvmTransactionForm.SigningPayload, payload.Form, name);
            Assert.AreEqual(ChainId, payload.ChainId, $"{name}: the payload carries the chain id");
            Assert.AreEqual(Router, payload.To, name);
            Assert.AreEqual(new uint256(3), payload.Value, name);
            CollectionAssert.AreEqual(data, payload.Data, name);
            Assert.AreEqual(unsigned.AsSpan().IndexOf(data), payload.DataRange.Offset, name);

            // The caller states the form; a mismatch is refused, never reinterpreted.
            EvmTransactionStatus asSigned = Signed(unsigned);
            EvmTransactionStatus asPayload = Unsigned(signed);
            if (type == EvmTransactionType.Legacy)
            {
                // Both legacy forms have nine fields. The payload's [chainId, 0, 0] fails as a signature (r = s = 0); the signature fails as placeholders.
                Assert.AreEqual(EvmTransactionStatus.InvalidSignature, asSigned, name);
                Assert.AreEqual(EvmTransactionStatus.InvalidSignature, asPayload, name);
            }
            else
            {
                Assert.AreEqual(EvmTransactionStatus.WrongFieldCount, asSigned, name);
                Assert.AreEqual(EvmTransactionStatus.WrongFieldCount, asPayload, name);
            }
        }

        // The pre-EIP-155 payload has six fields and no chain id.
        Assert.AreEqual(EvmTransactionStatus.Success, Read(NLegacyWithoutChainId(RouterBytes, data).GetRLPEncodedRaw(), EvmTransactionForm.SigningPayload, out Snapshot pre155));
        Assert.IsNull(pre155.ChainId);
        Assert.AreEqual(EvmTransactionKind.FunctionCall, pre155.Kind);
        Assert.AreEqual(EvmTransactionStatus.WrongFieldCount, Signed(NLegacyWithoutChainId(RouterBytes, data).GetRLPEncodedRaw()), "six fields are never a signed transaction");
    }

    [TestMethod]
    public void LegacyChainIds_ComeFromVOrFromTheEip155Field_IncludingLargeRealChainIds()
    {
        byte[] data = TransferCalldata(5);
        // Real chain ids are far above 110 (Base 8453, Arbitrum 42161, Zora 7777777, a 2^40-scale private chain): never a documented limit.
        foreach (ulong chain in new ulong[] { 1, 56, 137, 8453, 42161, 7_777_777, 1UL << 40, (ulong.MaxValue - 36) / 2 })
        {
            byte[] signed = Encode(null, LegacyFields(RouterBytes, data, v: RlpNode.Bytes(TrimmedBigEndian(new System.Numerics.BigInteger(chain) * 2 + 36))));
            Assert.AreEqual(EvmTransactionStatus.Success, Read(signed, EvmTransactionForm.SignedTransaction, out Snapshot s), $"chain {chain}");
            Assert.AreEqual(chain, s.ChainId, $"chain {chain} from v = 2 * chain + 36");

            byte[] payload = Encode(null, [.. LegacyFields(RouterBytes, data)[..6], Number(chain), Number(0), Number(0)]);
            Assert.AreEqual(EvmTransactionStatus.Success, Read(payload, EvmTransactionForm.SigningPayload, out Snapshot u), $"payload chain {chain}");
            Assert.AreEqual(chain, u.ChainId);
        }

        foreach (ulong v in new ulong[] { 27, 28 })
        {
            Assert.AreEqual(EvmTransactionStatus.Success, Read(Encode(null, LegacyFields(RouterBytes, data, v: Number(v))), EvmTransactionForm.SignedTransaction, out Snapshot pre), $"v = {v}");
            Assert.IsNull(pre.ChainId, "no replay protection: no chain id");
        }
    }

    private static byte[] TrimmedBigEndian(System.Numerics.BigInteger value) => value.ToByteArray(isUnsigned: true, isBigEndian: true);

    [TestMethod]
    public void Values_AreReadAsUInt256_AcrossTheWholeRange()
    {
        byte[] data = TransferCalldata(5);
        foreach (string hex in new[] { "", "01", "ffffffffffffffff", "056bc75e2d63100000" /* 1e20 wei */, new string('f', 64) })
        {
            RlpNode value = RlpNode.Bytes(Hex(hex));
            uint256 expected = hex.Length == 0 ? uint256.Zero : uint256.Parse("0x" + hex);
            foreach (var (name, envelope) in new (string, byte[])[]
            {
                ("legacy", Encode(null, LegacyFields(RouterBytes, data, value))), ("eip2930", Encode(0x01, Fields2930(RouterBytes, data, value))),
                ("eip1559", Encode(0x02, Fields1559(RouterBytes, data, value))), ("eip4844", Encode(0x03, Fields4844(RouterBytes, data, value))),
                ("eip7702", Encode(0x04, Fields7702(RouterBytes, data, value))),
            })
            {
                Assert.AreEqual(EvmTransactionStatus.Success, Read(envelope, EvmTransactionForm.SignedTransaction, out Snapshot s), $"{name} value 0x{hex}");
                Assert.AreEqual(expected, s.Value, $"{name} value 0x{hex}");
            }
        }

        Assert.AreEqual(EvmTransactionStatus.InvalidValue, Signed(Encode(0x02, Fields1559(RouterBytes, data, RlpNode.Bytes(Hex("0001"))))), "a leading zero byte is not canonical");
        Assert.AreEqual(EvmTransactionStatus.InvalidValue, Signed(Encode(0x02, Fields1559(RouterBytes, data, RlpNode.Bytes(new byte[33].Select(_ => (byte)1).ToArray())))), "33 bytes overflow 256 bits");
        Assert.AreEqual(EvmTransactionStatus.InvalidValue, Signed(Encode(0x02, Fields1559(RouterBytes, data, RlpNode.EmptyList))), "a list");
    }

    // ------------------------------------------------------------------ classification (spec 9.4: only function calls reach the ABI codec)

    [TestMethod]
    public void ContractCreation_ValueTransfer_AndFallback_AreClassified_AndNeverHandedOutAsCalldata()
    {
        byte[] initcode = [.. Hex("6080604052"), .. TransferCalldata(5)]; // initcode that happens to contain a transfer selector
        foreach (var (name, envelope) in new (string, byte[])[] { ("legacy", Legacy([], initcode)), ("eip2930", Typed2930([], initcode)), ("eip1559", Typed1559([], initcode)) })
        {
            var limits = RLPDecodeLimits.Default;
            Assert.IsTrue(RLPCalldataAdapter.TryRead(envelope, EvmTransactionForm.SignedTransaction, in limits, out EvmTransactionView view, out _), name);
            Assert.AreEqual(EvmTransactionKind.ContractCreation, view.Kind, name);
            Assert.AreEqual(default, view.To, $"{name}: no destination");
            CollectionAssert.AreEqual(initcode, view.Data.ToArray(), $"{name}: the initcode is still readable as data");
            Assert.IsFalse(view.TryGetFunctionCalldata(out ReadOnlySpan<byte> calldata), $"{name}: but never as calldata");
            Assert.IsTrue(calldata.IsEmpty);
        }

        // EIP-4844 and EIP-7702 transactions cannot create contracts.
        Assert.AreEqual(EvmTransactionStatus.InvalidTo, Signed(Typed4844([], initcode)));
        Assert.AreEqual(EvmTransactionStatus.InvalidTo, Signed(Typed7702([], initcode)));

        // 0 bytes: value transfer / receive(). 1 to 3 bytes: fallback(). 4 bytes: the shortest function call (a selector without arguments).
        (int Length, EvmTransactionKind Kind)[] cases = [(0, EvmTransactionKind.ValueTransfer), (1, EvmTransactionKind.Fallback), (2, EvmTransactionKind.Fallback),
            (3, EvmTransactionKind.Fallback), (4, EvmTransactionKind.FunctionCall), (5, EvmTransactionKind.FunctionCall)];
        foreach (var (length, kind) in cases)
        {
            byte[] data = Hex("a9059cbb00")[..length];
            foreach (var (name, _, signed, unsigned) in Envelopes(RouterBytes, data))
            {
                Assert.AreEqual(EvmTransactionStatus.Success, Read(signed, EvmTransactionForm.SignedTransaction, out Snapshot s), $"{name} {length}");
                Assert.AreEqual(kind, s.Kind, $"{name}: {length} data bytes");
                Assert.AreEqual(EvmTransactionStatus.Success, Read(unsigned, EvmTransactionForm.SigningPayload, out Snapshot u), $"{name} payload {length}");
                Assert.AreEqual(kind, u.Kind, $"{name} payload: {length} data bytes");
            }
        }
    }

    // ------------------------------------------------------------------ field rules (one failing case per status, plus boundaries)

    [TestMethod]
    public void EveryFieldRule_HasItsOwnStatus()
    {
        byte[] data = TransferCalldata(5);
        RlpNode[] f1559 = Fields1559(RouterBytes, data);
        byte[] With1559(int index, RlpNode replacement) => Encode(0x02, [.. f1559[..index], replacement, .. f1559[(index + 1)..]]);
        RlpNode[] legacy = LegacyFields(RouterBytes, data);
        byte[] WithLegacy(int index, RlpNode replacement) => Encode(null, [.. legacy[..index], replacement, .. legacy[(index + 1)..]]);

        Assert.AreEqual(EvmTransactionStatus.InvalidChainId, Signed(With1559(0, RlpNode.EmptyList)), "chain id is a list");
        Assert.AreEqual(EvmTransactionStatus.InvalidChainId, Signed(With1559(0, RlpNode.Bytes(Enumerable.Repeat((byte)1, 9).ToArray()))), "chain id above 2^64");
        Assert.AreEqual(EvmTransactionStatus.InvalidChainId, Signed(With1559(0, RlpNode.Bytes(0x00, 0x01))), "chain id with a leading zero");
        Assert.AreEqual(EvmTransactionStatus.Success, Signed(With1559(0, RlpNode.Bytes(Enumerable.Repeat((byte)0xff, 8).ToArray()))), "chain id 2^64 - 1 is valid");
        Assert.AreEqual(EvmTransactionStatus.InvalidNonce, Signed(With1559(1, RlpNode.EmptyList)), "nonce is a list");
        Assert.AreEqual(EvmTransactionStatus.InvalidNonce, Signed(With1559(1, RlpNode.Bytes(0x00, 0x01))), "nonce with a leading zero");
        Assert.AreEqual(EvmTransactionStatus.InvalidNonce, Signed(With1559(1, RlpNode.Bytes(Enumerable.Repeat((byte)1, 9).ToArray()))), "nonce above 2^64");
        Assert.AreEqual(EvmTransactionStatus.InvalidNonce, Signed(With1559(1, RlpNode.Bytes(Enumerable.Repeat((byte)0xff, 8).ToArray()))), "nonce 2^64 - 1 (EIP-2681)");
        Assert.AreEqual(EvmTransactionStatus.Success, Read(With1559(1, RlpNode.Bytes([.. Enumerable.Repeat((byte)0xff, 7), 0xfe])), EvmTransactionForm.SignedTransaction, out Snapshot highest),
            "nonce 2^64 - 2 is the highest valid");
        Assert.AreEqual(ulong.MaxValue - 1, highest.Nonce);
        Assert.AreEqual(EvmTransactionStatus.Success, Read(With1559(1, Number(0)), EvmTransactionForm.SignedTransaction, out Snapshot zero), "nonce 0");
        Assert.AreEqual(0UL, zero.Nonce);
        Assert.AreEqual(EvmTransactionStatus.InvalidTo, Signed(With1559(5, RlpNode.EmptyList)), "to is a list");
        Assert.AreEqual(EvmTransactionStatus.InvalidTo, Signed(With1559(5, RlpNode.Bytes(RouterBytes[..19]))), "19-byte to");
        Assert.AreEqual(EvmTransactionStatus.InvalidTo, Signed(With1559(5, RlpNode.Bytes([.. RouterBytes, 0x00]))), "21-byte to");
        Assert.AreEqual(EvmTransactionStatus.InvalidData, Signed(With1559(7, RlpNode.EmptyList)), "data is a list");
        Assert.AreEqual(EvmTransactionStatus.InvalidField, Signed(With1559(8, RlpNode.Bytes(0x01))), "access list is a byte string");
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Signed(With1559(9, Number(2))), "yParity 2");
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Signed(With1559(9, RlpNode.EmptyList)), "yParity is a list");
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Signed(With1559(10, RlpNode.Bytes(Enumerable.Repeat((byte)1, 33).ToArray()))), "r above 256 bits");
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Signed(With1559(10, Number(0))), "r = 0");
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Signed(With1559(11, Number(0))), "s = 0");
        Assert.AreEqual(EvmTransactionStatus.Success, Signed(With1559(10, Number(1))), "r = 1 is in bounds (recovery is not checked)");

        // secp256k1 bounds (Yellow Paper r, s in 1..n-1; EIP-2 s <= n/2), typed and legacy, each side of each bound.
        byte[] order = Hex("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141");
        byte[] orderMinusOne = Hex("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364140");
        byte[] halfOrder = Hex("7FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF5D576E7357A4501DDFE92F46681B20A0");
        byte[] halfOrderPlusOne = Hex("7FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF5D576E7357A4501DDFE92F46681B20A1");
        Assert.AreEqual(EvmTransactionStatus.Success, Signed(With1559(10, RlpNode.Bytes(orderMinusOne))), "r = n - 1");
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Signed(With1559(10, RlpNode.Bytes(order))), "r = n");
        Assert.AreEqual(EvmTransactionStatus.Success, Signed(With1559(11, RlpNode.Bytes(halfOrder))), "s = n/2");
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Signed(With1559(11, RlpNode.Bytes(halfOrderPlusOne))), "s = n/2 + 1 (EIP-2)");
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Signed(With1559(11, RlpNode.Bytes(orderMinusOne))), "s = n - 1 (EIP-2)");

        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Signed(WithLegacy(6, Number(29))), "legacy v = 29");
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Signed(WithLegacy(6, Number(0))), "legacy v = 0");
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Signed(WithLegacy(7, Number(0))), "legacy r = 0");
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Signed(WithLegacy(8, Number(0))), "legacy s = 0");
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Signed(WithLegacy(8, RlpNode.EmptyList)), "legacy s is a list");
        Assert.AreEqual(EvmTransactionStatus.InvalidNonce, Signed(WithLegacy(0, RlpNode.EmptyList)), "legacy nonce is a list");
        Assert.AreEqual(EvmTransactionStatus.Success, Signed(WithLegacy(7, RlpNode.Bytes(orderMinusOne))), "legacy r = n - 1");
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Signed(WithLegacy(7, RlpNode.Bytes(order))), "legacy r = n");
        Assert.AreEqual(EvmTransactionStatus.Success, Signed(WithLegacy(8, RlpNode.Bytes(halfOrder))), "legacy s = n/2");
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Signed(WithLegacy(8, RlpNode.Bytes(halfOrderPlusOne))), "legacy s = n/2 + 1 (EIP-2)");
        Assert.AreEqual(EvmTransactionStatus.InvalidNonce, Signed(WithLegacy(0, RlpNode.Bytes(Enumerable.Repeat((byte)0xff, 8).ToArray()))), "legacy nonce 2^64 - 1");
        Assert.AreEqual(EvmTransactionStatus.InvalidNonce, Unsigned(Encode(null, [RlpNode.Bytes(0x00), .. legacy[1..6]])), "pre-EIP-155 payload nonce: the raw byte 00 is not an integer");

        RlpNode[] payload = [.. legacy[..6], Number(ChainId), Number(0), Number(0)];
        byte[] WithPayload(int index, RlpNode replacement) => Encode(null, [.. payload[..index], replacement, .. payload[(index + 1)..]]);
        Assert.AreEqual(EvmTransactionStatus.Success, Unsigned(Encode(null, payload)));
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Unsigned(WithPayload(7, Number(1))), "EIP-155 placeholder r must be 0");
        Assert.AreEqual(EvmTransactionStatus.InvalidSignature, Unsigned(WithPayload(8, Number(1))), "EIP-155 placeholder s must be 0");
        Assert.AreEqual(EvmTransactionStatus.InvalidChainId, Unsigned(WithPayload(6, RlpNode.EmptyList)), "EIP-155 chain id is a list");
        Assert.AreEqual(EvmTransactionStatus.InvalidChainId, Unsigned(WithPayload(6, RlpNode.Bytes(Enumerable.Repeat((byte)1, 9).ToArray()))), "EIP-155 chain id above 2^64");

        RlpNode[] f4844 = Fields4844(RouterBytes, data);
        Assert.AreEqual(EvmTransactionStatus.InvalidField, Signed(Encode(0x03, [.. f4844[..10], RlpNode.Bytes(BlobHash), .. f4844[11..]])), "blob hashes are a byte string");
        byte[] WithHashes(params RlpNode[] hashes) => Encode(0x03, [.. f4844[..10], RlpNode.ListOf(hashes), .. f4844[11..]]);
        byte[] secondHash = [0x01, .. Enumerable.Repeat((byte)0x77, 31)];
        Assert.AreEqual(EvmTransactionStatus.Success, Signed(WithHashes(RlpNode.Bytes(BlobHash), RlpNode.Bytes(secondHash))), "two versioned hashes");
        Assert.AreEqual(EvmTransactionStatus.InvalidBlobHashes, Signed(WithHashes()), "EIP-4844: at least one blob");
        Assert.AreEqual(EvmTransactionStatus.InvalidBlobHashes, Unsigned(Encode(0x03, WithoutSignature([.. f4844[..10], RlpNode.EmptyList, .. f4844[11..]]))), "the same rule for the signing payload");
        Assert.AreEqual(EvmTransactionStatus.InvalidBlobHashes, Signed(WithHashes(RlpNode.Bytes(BlobHash[..31]))), "a 31-byte hash");
        Assert.AreEqual(EvmTransactionStatus.InvalidBlobHashes, Signed(WithHashes(RlpNode.Bytes([.. BlobHash, 0x00]))), "a 33-byte hash");
        Assert.AreEqual(EvmTransactionStatus.InvalidBlobHashes, Signed(WithHashes(RlpNode.Bytes(BlobHash), RlpNode.Bytes([0x02, .. BlobHash[1..]]))), "version byte 0x02 in the second hash");
        Assert.AreEqual(EvmTransactionStatus.InvalidBlobHashes, Signed(WithHashes(RlpNode.Bytes([0x00, .. BlobHash[1..]]))), "version byte 0x00");
        Assert.AreEqual(EvmTransactionStatus.InvalidBlobHashes, Signed(WithHashes(RlpNode.ListOf(RlpNode.Bytes(BlobHash)))), "a hash that is a list");
        Assert.AreEqual(EvmTransactionStatus.InvalidField, Signed(Encode(0x03, [.. f4844[..9], RlpNode.EmptyList, .. f4844[10..]])), "max fee per blob gas is a list");

        RlpNode[] f7702 = Fields7702(RouterBytes, data);
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, Signed(Encode(0x04, [.. f7702[..9], RlpNode.Bytes(0x01), .. f7702[10..]])), "authorization list is a byte string");
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, Signed(Encode(0x04, [.. f7702[..9], RlpNode.ListOf(Authorization, RlpNode.Bytes(0x01)), .. f7702[10..]])), "an authorization is a byte string");
        Assert.AreEqual(EvmTransactionStatus.Success, Read(Encode(0x04, [.. f7702[..9], RlpNode.ListOf(Authorization, Authorization, Authorization), .. f7702[10..]]), EvmTransactionForm.SignedTransaction, out Snapshot three));
        Assert.AreEqual(3, three.AuthorizationCount);
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, Signed(Encode(0x04, [.. f7702[..9], RlpNode.EmptyList, .. f7702[10..]])),
            "EIP-7702: an empty authorization list makes the transaction invalid");
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, Unsigned(Encode(0x04, WithoutSignature([.. f7702[..9], RlpNode.EmptyList, .. f7702[10..]]))),
            "the same rule for the signing payload");
    }

    [TestMethod]
    public void EveryAuthorizationTupleBound_IsEnforced_AndOnlyTheBoundsOfTheEip()
    {
        // EIP-7702: [chain_id, address, nonce, y_parity, r, s]; chain_id, r, s < 2^256, nonce < 2^64, len(address) == 20, y_parity < 2^8. Validity of the
        // values (chain match, nonce 2^64 - 1, y_parity above 1, r or s zero or high) is decided per tuple by the processor, not here: such a transaction is valid.
        RlpNode[] f7702 = Fields7702(RouterBytes, TransferCalldata(5));
        RlpNode[] good = [Number(ChainId), RlpNode.Bytes(RecipientBytes), Number(3), Number(1), RlpNode.Bytes(Filled(0x33)), RlpNode.Bytes(Filled(0x44))];
        EvmTransactionStatus With(params RlpNode[] tuple) => Signed(Encode(0x04, [.. f7702[..9], RlpNode.ListOf(Authorization, RlpNode.ListOf(tuple)), .. f7702[10..]]));
        RlpNode[] Set(int index, RlpNode value) => [.. good[..index], value, .. good[(index + 1)..]];
        byte[] Bytes(int length, byte fill) => Enumerable.Repeat(fill, length).ToArray();

        Assert.AreEqual(EvmTransactionStatus.Success, With(good));
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, With(good[..5]), "five fields");
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, With([.. good, Number(0)]), "seven fields");
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, With(), "an empty tuple");
        for (int i = 0; i < 6; i++)
        {
            Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, With(Set(i, RlpNode.EmptyList)), $"field {i} is a list");
        }

        Assert.AreEqual(EvmTransactionStatus.Success, With(Set(0, Number(0))), "chain id 0 (any chain)");
        Assert.AreEqual(EvmTransactionStatus.Success, With(Set(0, RlpNode.Bytes(Bytes(32, 0xff)))), "chain id 2^256 - 1");
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, With(Set(0, RlpNode.Bytes([0x01, .. Bytes(32, 0)]))), "chain id 2^256");
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, With(Set(0, RlpNode.Bytes(0x00, 0x01))), "chain id with a leading zero");
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, With(Set(1, RlpNode.Bytes(RecipientBytes[..19]))), "19-byte address");
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, With(Set(1, RlpNode.Bytes([.. RecipientBytes, 0x00]))), "21-byte address");
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, With(Set(1, RlpNode.Bytes())), "empty address");
        Assert.AreEqual(EvmTransactionStatus.Success, With(Set(1, RlpNode.Bytes(new byte[20]))), "the zero address (clears a delegation)");
        Assert.AreEqual(EvmTransactionStatus.Success, With(Set(2, RlpNode.Bytes(Bytes(8, 0xff)))), "nonce 2^64 - 1 is in bounds (the processor skips it)");
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, With(Set(2, RlpNode.Bytes([0x01, .. Bytes(8, 0)]))), "nonce 2^64");
        Assert.AreEqual(EvmTransactionStatus.Success, With(Set(3, Number(255))), "y parity 255 is in bounds (the processor skips it)");
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, With(Set(3, Number(256))), "y parity 256");
        Assert.AreEqual(EvmTransactionStatus.Success, With(Set(4, Number(0))), "r = 0 is in bounds (the processor skips it)");
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, With(Set(4, RlpNode.Bytes([0x01, .. Bytes(32, 0)]))), "r = 2^256");
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, With(Set(5, RlpNode.Bytes([0x01, .. Bytes(32, 0)]))), "s = 2^256");
        Assert.AreEqual(EvmTransactionStatus.InvalidAuthorizationList, With(Set(5, RlpNode.Bytes(0x00, 0x05))), "s with a leading zero");
    }

    [TestMethod]
    public void EnvelopeLevelFailures_AreRefused_WithoutThrowing()
    {
        byte[] data = TransferCalldata(5);
        byte[] good = Legacy(RouterBytes, data);
        Assert.AreEqual(EvmTransactionStatus.EmptyEnvelope, Signed([]));
        foreach (byte unknown in new byte[] { 0x00, 0x05, 0x06, 0x7e, 0x7f })
        {
            Assert.AreEqual(EvmTransactionStatus.UnsupportedType, Signed([unknown, .. Typed1559(RouterBytes, data)[1..]]), $"type 0x{unknown:x2}");
        }

        Assert.AreEqual(EvmTransactionStatus.MalformedRlp, Signed([0x80]), "a byte string is not a transaction");
        Assert.AreEqual(EvmTransactionStatus.MalformedRlp, Signed([0x02, 0x80]), "nor is a typed byte string");
        Assert.AreEqual(EvmTransactionStatus.MalformedRlp, Signed([.. good, 0x00]), "a trailing byte");
        Assert.AreEqual(EvmTransactionStatus.MalformedRlp, Signed([0x02]), "a type byte alone");
        for (int length = 1; length < good.Length; length++)
        {
            Assert.AreNotEqual(EvmTransactionStatus.Success, Signed(good[..length]), $"prefix {length}");
        }

        Assert.AreEqual(EvmTransactionStatus.WrongFieldCount, Signed(Encode(null, Number(1), Number(2))));
        Assert.AreEqual(EvmTransactionStatus.WrongFieldCount, Signed([0x02, .. good]), "a legacy list under a type byte");
        Assert.AreEqual(EvmTransactionStatus.WrongFieldCount, Signed([0x03, .. Typed1559(RouterBytes, data)[1..]]), "a 12-field list under type 3");
        Assert.AreEqual(EvmTransactionStatus.WrongFieldCount, Signed([0x04, .. Typed4844(RouterBytes, data)[1..]]), "a 14-field list under type 4");
        Assert.AreEqual(EvmTransactionStatus.WrongFieldCount, Signed([0x04, 0xc0]), "an empty list");
        Assert.AreEqual(EvmTransactionStatus.WrongFieldCount, Signed(Encode(0x02, Enumerable.Repeat(Number(1), 40).ToArray())), "far too many fields");
        // One field past the largest layout (EIP-4844, 14 fields): the count stops at 15, never at 14.
        Assert.AreEqual(EvmTransactionStatus.WrongFieldCount, Signed(Encode(0x03, [.. Fields4844(RouterBytes, data), Number(1)])), "a 15-field type-3 body");
        Assert.AreEqual(EvmTransactionStatus.WrongFieldCount, Signed(Encode(0x03, [.. Fields4844(RouterBytes, data), Number(1), Number(2)])), "a 16-field type-3 body");
        byte[] networkForm = [0x03, .. NethereumRlpAdapter.Encode(RlpNode.ListOf(
            RlpNode.ListOf(Fields4844(RouterBytes, data)), RlpNode.ListOf(RlpNode.Bytes(new byte[1024])), RlpNode.ListOf(RlpNode.Bytes(Filled(0x66))), RlpNode.ListOf(RlpNode.Bytes(Filled(0x77)))))];
        Assert.AreEqual(EvmTransactionStatus.WrongFieldCount, Signed(networkForm), "the EIP-4844 network wrapper is not a transaction body");

        var badLimits = new RLPDecodeLimits(0, 32, 8192);
        Assert.IsFalse(RLPCalldataAdapter.TryRead(good, EvmTransactionForm.SignedTransaction, in badLimits, out EvmTransactionView view, out EvmTransactionStatus status));
        Assert.AreEqual(EvmTransactionStatus.InvalidArgument, status);
        Assert.IsFalse(view.IsValid);
        var limits = RLPDecodeLimits.Default;
        Assert.IsFalse(RLPCalldataAdapter.TryRead(good, (EvmTransactionForm)2, in limits, out _, out status));
        Assert.AreEqual(EvmTransactionStatus.InvalidArgument, status, "an undefined form");
        var tiny = new RLPDecodeLimits(good.Length - 1, 32, 8192);
        Assert.IsFalse(RLPCalldataAdapter.TryRead(good, EvmTransactionForm.SignedTransaction, in tiny, out _, out status));
        Assert.AreEqual(EvmTransactionStatus.MalformedRlp, status, "the RLP limits apply (its own failure contract)");
    }

    // ------------------------------------------------------------------ coordinates

    [TestMethod]
    public void AbiRanges_MapToExactEnvelopeRanges_ForEveryType()
    {
        byte[] data = TransferCalldata(0x1122334455667788UL);
        foreach (var (name, _, signed, unsigned) in Envelopes(RouterBytes, data))
        {
            foreach (var (form, envelope) in new[] { (EvmTransactionForm.SignedTransaction, signed), (EvmTransactionForm.SigningPayload, unsigned) })
            {
                var rlpLimits = RLPDecodeLimits.Default;
                Assert.IsTrue(RLPCalldataAdapter.TryRead(envelope, form, in rlpLimits, out EvmTransactionView view, out _), name);
                Assert.IsTrue(view.TryGetFunctionCalldata(out ReadOnlySpan<byte> calldata));
                var abiLimits = ABIDecodeLimits.Default;
                Assert.IsTrue(ABICallData.TryDecode(calldata, Transfer, in abiLimits, out ABICallData call, out _), name);
                Assert.IsTrue(call.Arguments.TryLocate([1], out ABIFieldLocation amount, out _));
                Assert.IsTrue(call.TryTranslateArgumentRange(amount.ValueRange, out ABIRange inCall));
                Assert.AreEqual(new ABIRange(36, 32), inCall, "calldata coordinates: selector included");
                Assert.IsTrue(view.TryMapCalldataRange(inCall, out ABIRange inEnvelope), name);
                Assert.AreEqual(envelope.AsSpan().IndexOf(Word(0x1122334455667788UL)), inEnvelope.Offset, $"{name} {form}: independent search of the envelope");
                CollectionAssert.AreEqual(Word(0x1122334455667788UL), envelope.AsSpan(inEnvelope.Offset, inEnvelope.Length).ToArray());
            }
        }
    }

    [TestMethod]
    public void MapCalldataRange_IsTranslationOnly_AndRefusesRangesOutsideTheData()
    {
        byte[] envelope = Typed1559(RouterBytes, TransferCalldata(5));
        var limits = RLPDecodeLimits.Default;
        Assert.IsTrue(RLPCalldataAdapter.TryRead(envelope, EvmTransactionForm.SignedTransaction, in limits, out EvmTransactionView view, out _));
        int offset = view.DataRange.Offset;
        Assert.IsTrue(view.TryMapCalldataRange(new ABIRange(0, 68), out ABIRange whole));
        Assert.AreEqual(new ABIRange(offset, 68), whole);
        Assert.IsTrue(view.TryMapCalldataRange(new ABIRange(68, 0), out ABIRange empty), "an empty range at the end");
        Assert.AreEqual(new ABIRange(offset + 68, 0), empty);
        foreach (ABIRange bad in new[] { new ABIRange(-1, 4), new ABIRange(0, -1), new ABIRange(69, 0), new ABIRange(0, 69), new ABIRange(60, 9), new ABIRange(int.MaxValue, 1), new ABIRange(1, int.MaxValue) })
        {
            Assert.IsFalse(view.TryMapCalldataRange(bad, out ABIRange none), bad.ToString());
            Assert.AreEqual(default, none);
        }

        EvmTransactionView invalid = default;
        Assert.IsFalse(invalid.TryMapCalldataRange(new ABIRange(0, 0), out _), "a default view maps nothing");
        Assert.IsFalse(invalid.TryGetFunctionCalldata(out _));
        Assert.IsFalse(invalid.IsValid);
        Assert.AreEqual(EvmTransactionKind.None, invalid.Kind);
    }

    // ------------------------------------------------------------------ adversarial (spec 10.4: the RLP-to-ABI adapter is a fuzz target)

    [TestMethod]
    public void MutatedEnvelopes_NeverThrow_AndEverySuccessIsConsistentWithTheBytes()
    {
        var rng = new DeterministicRng(20261119);
        int successes = 0;
        int refusals = 0;
        foreach (var (name, _, signed, unsigned) in Envelopes(RouterBytes, TransferCalldata(5)))
        {
            foreach (var (form, original) in new[] { (EvmTransactionForm.SignedTransaction, signed), (EvmTransactionForm.SigningPayload, unsigned) })
            {
                for (int round = 0; round < 1_500; round++)
                {
                    byte[] mutant = ABIDecodeDifferentialTests.Mutate(original, rng);
                    if (round % 3 == 0 && mutant.Length > 0)
                    {
                        mutant[rng.Next(mutant.Length)] = (byte)rng.Next(256);
                    }

                    var limits = RLPDecodeLimits.Default;
                    if (RLPCalldataAdapter.TryRead(mutant, form, in limits, out EvmTransactionView view, out EvmTransactionStatus status))
                    {
                        successes++;
                        Assert.AreEqual(EvmTransactionStatus.Success, status);
                        ABIRange range = view.DataRange;
                        int firstDataByte = view.Type == EvmTransactionType.Legacy ? 0 : 1; // never the type byte
                        Assert.IsTrue(range.Offset >= firstDataByte && range.Offset + range.Length <= mutant.Length, $"{name} {round}: data outside the envelope");
                        if (range.Length > 0)
                        {
                            Assert.IsTrue(Unsafe.AreSame(in MemoryMarshal.GetReference(view.Data), in mutant[range.Offset]), $"{name} {round}: a copy");
                        }

                        Assert.AreEqual(view.Kind == EvmTransactionKind.FunctionCall, view.TryGetFunctionCalldata(out _), $"{name} {round}");
                        Assert.AreEqual(view.Type == EvmTransactionType.Legacy, mutant[0] >= 0xc0, $"{name} {round}: type follows the first byte");
                    }
                    else
                    {
                        refusals++;
                        Assert.IsFalse(view.IsValid);
                        Assert.IsTrue(view.Data.IsEmpty);
                    }
                }
            }
        }

        Assert.IsGreaterThan(500, successes, "the mutator must leave many envelopes readable");
        Assert.IsGreaterThan(2_000, refusals, "and refuse many");
    }

    // ------------------------------------------------------------------ allocation

    [TestMethod]
    public void TryRead_AllocatesNothing_AfterWarmUp()
    {
        (string Name, EvmTransactionType Type, byte[] Signed, byte[] Unsigned)[] envelopes = Envelopes(RouterBytes, TransferCalldata(5));
        var limits = RLPDecodeLimits.Default;
        int checksum = 0;
        void Run()
        {
            foreach (var (_, _, signed, unsigned) in envelopes)
            {
                RLPCalldataAdapter.TryRead(signed, EvmTransactionForm.SignedTransaction, in limits, out EvmTransactionView a, out _);
                RLPCalldataAdapter.TryRead(unsigned, EvmTransactionForm.SigningPayload, in limits, out EvmTransactionView b, out _);
                RLPCalldataAdapter.TryRead(signed, EvmTransactionForm.SigningPayload, in limits, out _, out _); // a failure path
                checksum += a.Data.Length + b.DataRange.Offset;
            }
        }

        for (int i = 0; i < 50; i++)
        {
            Run();
        }

        // Best of five trials: a single sample once caught a runtime allocation (tiering) during a full test run; the adapter's own cost is zero in each.
        long best = long.MaxValue;
        for (int trial = 0; trial < 5; trial++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
                Run();
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.AreEqual(0, best);
        Assert.IsGreaterThan(0, checksum);
    }
}
