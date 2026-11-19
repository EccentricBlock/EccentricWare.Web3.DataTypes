using EccentricWare.Web3.DataTypes.ABI.EvmAdapters;
using EccentricWare.Web3.DataTypes.Tests.Rlp;
using Nethereum.Model;
using Nethereum.Util;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using NethereumSignature = Nethereum.Model.Signature;

namespace EccentricWare.Web3.DataTypes.Tests.ABI.EvmAdapters;

/// <summary>
/// Transaction envelopes for the adapter tests, built two ways: explicit RLP field lists in the order the EIPs give (the test-local
/// <see cref="RlpReference"/> and Nethereum.RLP), and Nethereum.Model 7.1.0's own typed-transaction encoders (signed: <c>GetRLPEncoded</c>; signing
/// payload: <c>GetRLPEncodedRaw</c>). <c>RLPCalldataAdapterTests.IndependentEncoders_AgreeWithTheExplicitFieldLists</c> shows the two agree, so a
/// field-order mistake shared by the adapter and these lists cannot pass.
/// </summary>
internal static class EvmAdapterFixtures
{
    internal const ulong ChainId = 8453;
    internal static readonly byte[] RouterBytes = Hex("68b3465833fb72a70ecdf485e0e4c7bd8665fc45");
    internal static readonly byte[] RecipientBytes = Hex("d8da6bf26964af9d7eed9e03e53415d37aa96045");
    internal static readonly byte[] SenderBytes = Hex("1111111111111111111111111111111111111111");
    internal static readonly byte[] BlobHash = [0x01, .. Enumerable.Repeat((byte)0x5b, 31)];

    internal static Address Router => Address.FromEvmBytes(RouterBytes);

    internal static Address Recipient => Address.FromEvmBytes(RecipientBytes);

    internal static Address Sender => Address.FromEvmBytes(SenderBytes);

    internal static Address EvmAddress(byte fill) => Address.FromEvmBytes(Enumerable.Repeat(fill, 20).ToArray());

    internal static byte[] TransferCalldata(byte[] to, ulong amount) => [.. Hex("a9059cbb"), .. new byte[12], .. to, .. Word(amount)];

    internal static byte[] TransferCalldata(ulong amount) => TransferCalldata(RecipientBytes, amount);

    internal static RlpNode Number(ulong value) => RlpNode.FromUInt64(value);

    internal static RlpNode AccessList => RlpNode.ListOf(RlpNode.ListOf(RlpNode.Bytes(RouterBytes), RlpNode.ListOf(RlpNode.Bytes(Filled(0x44)))));

    internal static RlpNode Authorization => RlpNode.ListOf(Number(ChainId), RlpNode.Bytes(RecipientBytes), Number(3), Number(1), RlpNode.Bytes(Filled(0x33)), RlpNode.Bytes(Filled(0x44)));

    internal static RlpNode Signature3 => RlpNode.ListOf(Number(1), RlpNode.Bytes(Filled(0x11)), RlpNode.Bytes(Filled(0x22)));

    // ------------------------------------------------------------------ explicit field lists (EIP order)

    internal static RlpNode[] LegacyFields(byte[] to, byte[] data, RlpNode? value = null, RlpNode? v = null) =>
    [
        Number(9), Number(20_000_000_000), Number(250_000), RlpNode.Bytes(to), value ?? Number(0), RlpNode.Bytes(data),
        v ?? Number(ChainId * 2 + 35), RlpNode.Bytes(Filled(0x11)), RlpNode.Bytes(Filled(0x22)),
    ];

    internal static RlpNode[] Fields2930(byte[] to, byte[] data, RlpNode? value = null) =>
    [
        Number(ChainId), Number(9), Number(20_000_000_000), Number(250_000), RlpNode.Bytes(to), value ?? Number(0), RlpNode.Bytes(data),
        AccessList, Number(1), RlpNode.Bytes(Filled(0x11)), RlpNode.Bytes(Filled(0x22)),
    ];

    internal static RlpNode[] Fields1559(byte[] to, byte[] data, RlpNode? value = null) =>
    [
        Number(ChainId), Number(9), Number(1_000_000_000), Number(20_000_000_000), Number(250_000), RlpNode.Bytes(to), value ?? Number(0), RlpNode.Bytes(data),
        AccessList, Number(1), RlpNode.Bytes(Filled(0x11)), RlpNode.Bytes(Filled(0x22)),
    ];

    internal static RlpNode[] Fields4844(byte[] to, byte[] data, RlpNode? value = null) =>
    [
        Number(ChainId), Number(9), Number(1_000_000_000), Number(20_000_000_000), Number(250_000), RlpNode.Bytes(to), value ?? Number(0), RlpNode.Bytes(data),
        AccessList, Number(2_000_000_000), RlpNode.ListOf(RlpNode.Bytes(BlobHash)), Number(1), RlpNode.Bytes(Filled(0x11)), RlpNode.Bytes(Filled(0x22)),
    ];

    internal static RlpNode[] Fields7702(byte[] to, byte[] data, RlpNode? value = null) =>
    [
        Number(ChainId), Number(9), Number(1_000_000_000), Number(20_000_000_000), Number(250_000), RlpNode.Bytes(to), value ?? Number(0), RlpNode.Bytes(data),
        AccessList, RlpNode.ListOf(Authorization), Number(1), RlpNode.Bytes(Filled(0x11)), RlpNode.Bytes(Filled(0x22)),
    ];

    internal static byte[] Encode(byte? type, params RlpNode[] fields) =>
        type is null ? RlpReference.Encode(RlpNode.ListOf(fields)) : [type.Value, .. RlpReference.Encode(RlpNode.ListOf(fields))];

    internal static byte[] Legacy(byte[] to, byte[] data) => Encode(null, LegacyFields(to, data));

    internal static byte[] Typed2930(byte[] to, byte[] data) => Encode(0x01, Fields2930(to, data));

    internal static byte[] Typed1559(byte[] to, byte[] data) => Encode(0x02, Fields1559(to, data));

    internal static byte[] Typed4844(byte[] to, byte[] data) => [0x03, .. NethereumRlpAdapter.Encode(RlpNode.ListOf(Fields4844(to, data)))];

    internal static byte[] Typed7702(byte[] to, byte[] data) => [0x04, .. NethereumRlpAdapter.Encode(RlpNode.ListOf(Fields7702(to, data)))];

    /// <summary>The same field list without its last three (signature) fields: the typed signing payload.</summary>
    internal static RlpNode[] WithoutSignature(RlpNode[] signedFields) => signedFields[..^3];

    // ------------------------------------------------------------------ Nethereum.Model (independent encoders)

    internal static string HexOf(byte[] bytes) => "0x" + Convert.ToHexString(bytes);

    private static NethereumSignature NSignature() => new(Filled(0x11), Filled(0x22), [1]);

    private static List<AccessListItem> NAccessList() => [new AccessListItem(HexOf(RouterBytes), [Filled(0x44)])];

    private static EvmUInt256 N(ulong value) => new(value);

    internal static LegacyTransactionChainId NLegacy(byte[] to, byte[] data, ulong value = 0) =>
        new(HexOf(to), N(value), N(9), N(20_000_000_000), N(250_000), HexOf(data), N(ChainId),
            Filled(0x11), Filled(0x22), N(ChainId * 2 + 35).ToBigEndian().SkipWhile(b => b == 0).ToArray());

    internal static LegacyTransaction NLegacyWithoutChainId(byte[] to, byte[] data, ulong value = 0) =>
        new(HexOf(to), N(value), N(9), N(20_000_000_000), N(250_000), HexOf(data));

    internal static Transaction2930 N2930(byte[] to, byte[] data, ulong value = 0) =>
        new(N(ChainId), N(9), N(20_000_000_000), N(250_000), HexOf(to), N(value), HexOf(data), NAccessList(), NSignature());

    internal static Transaction1559 N1559(byte[] to, byte[] data, ulong value = 0) =>
        new(N(ChainId), N(9), N(1_000_000_000), N(20_000_000_000), N(250_000), HexOf(to), N(value), HexOf(data), NAccessList(), NSignature());

    internal static Transaction4844 N4844(byte[] to, byte[] data, ulong value = 0) =>
        new(N(ChainId), N(9), N(1_000_000_000), N(20_000_000_000), N(250_000), HexOf(to), N(value), HexOf(data), NAccessList(),
            N(2_000_000_000), [BlobHash], NSignature());

    internal static Transaction7702 N7702(byte[] to, byte[] data, ulong value = 0) =>
        new(N(ChainId), N(9), N(1_000_000_000), N(20_000_000_000), N(250_000), HexOf(to), N(value), HexOf(data), NAccessList(),
            [new Authorisation7702Signed(N(ChainId), HexOf(RecipientBytes), N(3), Filled(0x33), Filled(0x44), [1])], NSignature());

    /// <summary>Every supported type, signed and as a signing payload, from the independent encoders.</summary>
    internal static (string Name, EvmTransactionType Type, byte[] Signed, byte[] Unsigned)[] Envelopes(byte[] to, byte[] data, ulong value = 0) =>
    [
        ("legacy", EvmTransactionType.Legacy, NLegacy(to, data, value).GetRLPEncoded(), NLegacy(to, data, value).GetRLPEncodedRaw()),
        ("eip2930", EvmTransactionType.Eip2930, N2930(to, data, value).GetRLPEncoded(), N2930(to, data, value).GetRLPEncodedRaw()),
        ("eip1559", EvmTransactionType.Eip1559, N1559(to, data, value).GetRLPEncoded(), N1559(to, data, value).GetRLPEncodedRaw()),
        ("eip4844", EvmTransactionType.Eip4844, N4844(to, data, value).GetRLPEncoded(), N4844(to, data, value).GetRLPEncodedRaw()),
        ("eip7702", EvmTransactionType.Eip7702, N7702(to, data, value).GetRLPEncoded(), N7702(to, data, value).GetRLPEncodedRaw()),
    ];
}
