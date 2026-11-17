using System.Numerics;
using EccentricWare.Web3.DataTypes.Rlp;
using EccentricWare.Web3.DataTypes.Utils;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.TypedAdapterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Signed transactions produced by an independent implementation, decoded with the signature adapters and rebuilt
/// byte for byte with the writer adapters, on chains whose id exceeds 110 (legacy <c>v</c> above one byte).
/// </summary>
/// <remarks>
/// <para>
/// How the vectors were generated: a throw-away console project outside this repository referenced
/// <c>Nethereum.Signer</c> 7.1.0 and <c>Nethereum.Model</c> 7.1.0 (net10.0). Each transaction was built with
/// <c>LegacyTransactionChainId</c> (EIP-155) or <c>Transaction1559</c>, signed with <c>LegacyTransactionSigner</c> /
/// <c>Transaction1559Signer</c> using the well-known public Hardhat/Anvil test account #0 private key
/// (<c>0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80</c>, a published test key: never use it for value),
/// and decoded again with <c>TransactionFactory</c>. The recorded values are: the raw signed hex, the signing preimage
/// (<c>GetRLPEncodedRaw()</c>), the signing hash (<c>RawHash</c>), r, s, the y parity (from
/// <c>EthECKey.SignAndCalculateYParityV</c>, not from this library's formula), and the sender that
/// <c>TransactionVerificationAndRecoveryImp.GetSenderAddress</c> recovered from the signed bytes. The generator also checked
/// that the decoded nonce, to, data, chain id, r and s equal the inputs and that the recovered sender is the key's address.
/// The repository tests do not reference Nethereum: these are plain constants.
/// </para>
/// <para>
/// This library has no ECDSA recovery, so the sender is recorded but not recomputed here; the signing-hash check
/// (Keccak-256 of the preimage the writer rebuilds against Nethereum's hash) is the independent proof that the preimage
/// bytes are what was signed.
/// </para>
/// </remarks>
[TestClass]
public sealed class EvmChainIdTransactionVectorTests
{
    private const string SignerAddressHex = "f39fd6e51aad88f6f4ce6ab8827279cfffb92266";

    private sealed record LegacyVector(
        string Name,
        ulong ChainId,
        ulong Nonce,
        string GasPrice,
        ulong GasLimit,
        string To,
        string Value,
        string Data,
        string SignedHex,
        string PreimageHex,
        string SigningHashHex,
        string RHex,
        string SHex,
        byte YParity,
        string VHex,
        string SenderHex);

    private sealed record TypedVector(
        string Name,
        ulong ChainId,
        ulong Nonce,
        string MaxPriorityFee,
        string MaxFee,
        ulong GasLimit,
        string To,
        string Value,
        string Data,
        (string Address, string[] Keys)[] AccessList,
        string SignedHex,
        string PreimageHex,
        string SigningHashHex,
        string RHex,
        string SHex,
        byte YParity,
        string SenderHex);

    private static readonly LegacyVector[] LegacyVectors =
    [
        new("polygon-137", 137UL, 7UL, "30000000000", 21000UL, "3535353535353535353535353535353535353535", "1000000000000000000", "",
            SignedHex: "f86e078506fc23ac00825208943535353535353535353535353535353535353535880de0b6b3a764000080820136a096a798c2f67896685a647370ecbb5cc8772090ff8a898a9ad5ce57ba822940e6a0348c7e3a6018c50b917fc3e43661553197658d34e7be92b0851f1dd6846bf3a1",
            PreimageHex: "ed078506fc23ac00825208943535353535353535353535353535353535353535880de0b6b3a76400008081898080",
            SigningHashHex: "1f04ecb7ae51d8757d9634eaed5e60349b0def8d4e639ca30f212ad867e0ac09",
            RHex: "96a798c2f67896685a647370ecbb5cc8772090ff8a898a9ad5ce57ba822940e6", SHex: "348c7e3a6018c50b917fc3e43661553197658d34e7be92b0851f1dd6846bf3a1", YParity: 1, VHex: "0136", SenderHex: "f39fd6e51aad88f6f4ce6ab8827279cfffb92266"),
        new("base-8453-erc20-transfer", 8453UL, 42UL, "1000000", 65000UL, "70997970c51812dc3a010c7d01b50e0d17dc79c8", "0", "a9059cbb00000000000000000000000070997970c51812dc3a010c7d01b50e0d17dc79c80000000000000000000000000000000000000000000000000de0b6b3a7640000",
            SignedHex: "f8a92a830f424082fde89470997970c51812dc3a010c7d01b50e0d17dc79c880b844a9059cbb00000000000000000000000070997970c51812dc3a010c7d01b50e0d17dc79c80000000000000000000000000000000000000000000000000de0b6b3a764000082422ea09cb956710c301d4907b2adfb82d77e7162d46d96e82819652ba5907068d4bd83a073a84d60b7bb195754fe8316e909b585285a33d873cf1caaa2084f7b623e4441",
            PreimageHex: "f8692a830f424082fde89470997970c51812dc3a010c7d01b50e0d17dc79c880b844a9059cbb00000000000000000000000070997970c51812dc3a010c7d01b50e0d17dc79c80000000000000000000000000000000000000000000000000de0b6b3a76400008221058080",
            SigningHashHex: "693b8b692ea8dde829970e6a3f4a4225df21b2acac39eb807eada01f410b6915",
            RHex: "9cb956710c301d4907b2adfb82d77e7162d46d96e82819652ba5907068d4bd83", SHex: "73a84d60b7bb195754fe8316e909b585285a33d873cf1caaa2084f7b623e4441", YParity: 1, VHex: "422e", SenderHex: "f39fd6e51aad88f6f4ce6ab8827279cfffb92266"),
        new("arbitrum-42161", 42161UL, 0UL, "100000000", 1200000UL, "70997970c51812dc3a010c7d01b50e0d17dc79c8", "12345", "",
            SignedHex: "f869808405f5e10083124f809470997970c51812dc3a010c7d01b50e0d17dc79c88230398083014985a0fae8937b506b68fcf9b174b72998036e7dba95d4943472ef90e9052eb64e9024a0707d76c2e6ad64f2874b815a9377eccaa8efafc296e5aa0b4aa41784adc241d9",
            PreimageHex: "e8808405f5e10083124f809470997970c51812dc3a010c7d01b50e0d17dc79c88230398082a4b18080",
            SigningHashHex: "3815271acfce6c9f2f074fe15d1729be56b1585a6a3f02a56fa3a2112caa9e3d",
            RHex: "fae8937b506b68fcf9b174b72998036e7dba95d4943472ef90e9052eb64e9024", SHex: "707d76c2e6ad64f2874b815a9377eccaa8efafc296e5aa0b4aa41784adc241d9", YParity: 0, VHex: "014985", SenderHex: "f39fd6e51aad88f6f4ce6ab8827279cfffb92266"),
        new("avalanche-43114", 43114UL, 1000UL, "25000000000", 21000UL, "3535353535353535353535353535353535353535", "123456789012345678901234567890", "",
            SignedHex: "f8768203e88505d21dba008252089435353535353535353535353535353535353535358d018ee90ff6c373e0ee4e3f0ad280830150f8a0de94cacfc5f31a82f9726b2c98e5a960dbff21a3601b1c4e48cd62fb4158cf3ca0551dc9cc968726bd2e6b616af8c1c2ffd806715fb4a7d2dc98a3e74a85cff3a9",
            PreimageHex: "f58203e88505d21dba008252089435353535353535353535353535353535353535358d018ee90ff6c373e0ee4e3f0ad28082a86a8080",
            SigningHashHex: "01cb67b621ffe2b08f0124c5f9c2f29e4df1fad031573995c44838d93a220af8",
            RHex: "de94cacfc5f31a82f9726b2c98e5a960dbff21a3601b1c4e48cd62fb4158cf3c", SHex: "551dc9cc968726bd2e6b616af8c1c2ffd806715fb4a7d2dc98a3e74a85cff3a9", YParity: 1, VHex: "0150f8", SenderHex: "f39fd6e51aad88f6f4ce6ab8827279cfffb92266"),
        new("plume-98866-parity0", 98866UL, 3UL, "1500000000", 21000UL, "70997970c51812dc3a010c7d01b50e0d17dc79c8", "1000000000000000", "",
            SignedHex: "f86d038459682f008252089470997970c51812dc3a010c7d01b50e0d17dc79c887038d7ea4c680008083030487a0df8ddb6f0e5e6a3a306d73b68658610ba4b15a94d5c809af57c294f64e36bb9fa05b906ace7f8b2136c6b89b18399538370d3cdb9d93798951addb41c976d31d9d",
            PreimageHex: "ed038459682f008252089470997970c51812dc3a010c7d01b50e0d17dc79c887038d7ea4c6800080830182328080",
            SigningHashHex: "883e8c5d254547e49c34f0f121ed47f5e1ff2b8e26653ed8ec5728245357e0b1",
            RHex: "df8ddb6f0e5e6a3a306d73b68658610ba4b15a94d5c809af57c294f64e36bb9f", SHex: "5b906ace7f8b2136c6b89b18399538370d3cdb9d93798951addb41c976d31d9d", YParity: 0, VHex: "030487", SenderHex: "f39fd6e51aad88f6f4ce6ab8827279cfffb92266"),
        new("plume-98866-parity1", 98866UL, 0UL, "1500000000", 21000UL, "70997970c51812dc3a010c7d01b50e0d17dc79c8", "1000000000000000", "",
            SignedHex: "f86d808459682f008252089470997970c51812dc3a010c7d01b50e0d17dc79c887038d7ea4c680008083030488a023752091b5dcc7750ce0cf6f2a23f8756623961fd5632053d0c9ab7bfbf03647a0622a945334bdb0d10fb8a8cf339d577207b8f37231465513fc75eb929008de08",
            PreimageHex: "ed808459682f008252089470997970c51812dc3a010c7d01b50e0d17dc79c887038d7ea4c6800080830182328080",
            SigningHashHex: "9437ccfaf88ba6ea90b3396047ec4dcb019effd6bb68c27fe336f9c682173bc8",
            RHex: "23752091b5dcc7750ce0cf6f2a23f8756623961fd5632053d0c9ab7bfbf03647", SHex: "622a945334bdb0d10fb8a8cf339d577207b8f37231465513fc75eb929008de08", YParity: 1, VHex: "030488", SenderHex: "f39fd6e51aad88f6f4ce6ab8827279cfffb92266"),
        new("base-8453-short-signature-scalar", 8453UL, 1033UL, "1000000", 21000UL, "70997970c51812dc3a010c7d01b50e0d17dc79c8", "1", "",
            SignedHex: "f865820409830f42408252089470997970c51812dc3a010c7d01b50e0d17dc79c8018082422ea0bbc8489de3d7ac698e0d766eae64508e5c44c93e1769be2c3fcba1b5e46a1e9a9feebdec8d5d464d8fcd636dd83df113a7abfad80b3eac21fa4e68508ead8454",
            PreimageHex: "e6820409830f42408252089470997970c51812dc3a010c7d01b50e0d17dc79c801808221058080",
            SigningHashHex: "2be89c01b1c510b3e76ddd5a789302e86b3d7ab757a2b8bf5b7eec185255ab96",
            RHex: "bbc8489de3d7ac698e0d766eae64508e5c44c93e1769be2c3fcba1b5e46a1e9a", SHex: "eebdec8d5d464d8fcd636dd83df113a7abfad80b3eac21fa4e68508ead8454", YParity: 1, VHex: "422e", SenderHex: "f39fd6e51aad88f6f4ce6ab8827279cfffb92266")
    ];

    private static readonly TypedVector[] TypedVectors =
    [
        new("plume-98866-eip1559-parity0", 98866UL, 0UL, "1000000000", "3000000000", 90000UL, "70997970c51812dc3a010c7d01b50e0d17dc79c8", "5", "a9059cbb00000000000000000000000070997970c51812dc3a010c7d01b50e0d17dc79c80000000000000000000000000000000000000000000000000de0b6b3a7640000", [("cccccccccccccccccccccccccccccccccccccccc", ["1111111111111111111111111111111111111111111111111111111111111111", "0022222222222222222222222222222222222222222222222222222222222222"]), ("dddddddddddddddddddddddddddddddddddddddd", [])],
            SignedHex: "02f901268301823280843b9aca0084b2d05e0083015f909470997970c51812dc3a010c7d01b50e0d17dc79c805b844a9059cbb00000000000000000000000070997970c51812dc3a010c7d01b50e0d17dc79c80000000000000000000000000000000000000000000000000de0b6b3a7640000f872f85994ccccccccccccccccccccccccccccccccccccccccf842a01111111111111111111111111111111111111111111111111111111111111111a00022222222222222222222222222222222222222222222222222222222222222d694ddddddddddddddddddddddddddddddddddddddddc080a09d938edfaf01b5255bfd2d3bb26e049577e23e5b91d3d226e7e7a8010c3751c8a044c64025e0be1dd37c6475de318aa902ba8e74302e367c5ec1ef90df2e61eeaa",
            PreimageHex: "02f8e38301823280843b9aca0084b2d05e0083015f909470997970c51812dc3a010c7d01b50e0d17dc79c805b844a9059cbb00000000000000000000000070997970c51812dc3a010c7d01b50e0d17dc79c80000000000000000000000000000000000000000000000000de0b6b3a7640000f872f85994ccccccccccccccccccccccccccccccccccccccccf842a01111111111111111111111111111111111111111111111111111111111111111a00022222222222222222222222222222222222222222222222222222222222222d694ddddddddddddddddddddddddddddddddddddddddc0",
            SigningHashHex: "827e8876fd6e8e343175a4af13da724f13fe5a1ca09fc265e4e31d01f406f3f8",
            RHex: "9d938edfaf01b5255bfd2d3bb26e049577e23e5b91d3d226e7e7a8010c3751c8", SHex: "44c64025e0be1dd37c6475de318aa902ba8e74302e367c5ec1ef90df2e61eeaa", YParity: 0, SenderHex: "f39fd6e51aad88f6f4ce6ab8827279cfffb92266"),
        new("plume-98866-eip1559-parity1", 98866UL, 1UL, "1000000000", "3000000000", 21000UL, "70997970c51812dc3a010c7d01b50e0d17dc79c8", "100000000000000000", "", [],
            SignedHex: "02f8758301823201843b9aca0084b2d05e008252089470997970c51812dc3a010c7d01b50e0d17dc79c888016345785d8a000080c001a074ca0c9485974bbf9dacacaad3e270201a0e4caa3c011a25111c977df16e7bcaa04516f663b85ea33972afb30056b32339b1888e69dc61ec6eea5f1e51a70926e5",
            PreimageHex: "02f28301823201843b9aca0084b2d05e008252089470997970c51812dc3a010c7d01b50e0d17dc79c888016345785d8a000080c0",
            SigningHashHex: "3f0a157a4f65c10f2825a2926c375f37084418a977beab849b79a418b10b504e",
            RHex: "74ca0c9485974bbf9dacacaad3e270201a0e4caa3c011a25111c977df16e7bca", SHex: "4516f663b85ea33972afb30056b32339b1888e69dc61ec6eea5f1e51a70926e5", YParity: 1, SenderHex: "f39fd6e51aad88f6f4ce6ab8827279cfffb92266")
    ];

    private static BigInteger Decimal(string value) => BigInteger.Parse(value);

    private static uint256 U256(string decimalValue) => new(Padded32(Decimal(decimalValue)));

    private static uint256 U256FromMinimalHex(string hex) => new(Padded32(new BigInteger(H(hex), isUnsigned: true, isBigEndian: true)));

    private static Hash32 Keccak(ReadOnlySpan<byte> bytes)
    {
        Span<byte> digest = stackalloc byte[32];
        Keccak256.ComputeHash(bytes, digest);
        return new Hash32(digest);
    }

    private static Span<RLPRange> NewWorkspace() => new RLPRange[16];

    // ------------------------------------------------------------------ the vector set itself

    [TestMethod]
    public void VectorSet_CoversTheRequiredChainsAndBothParities()
    {
        var legacyChains = LegacyVectors.Select(v => v.ChainId).ToHashSet();
        foreach (var chain in new ulong[] { 137, 8453, 42161, 98866 })
        {
            Assert.Contains(chain, legacyChains, $"legacy chain {chain}");
        }

        Assert.IsGreaterThanOrEqualTo(4, LegacyVectors.Length);
        Assert.IsLessThanOrEqualTo(8, LegacyVectors.Length);
        Assert.IsTrue(LegacyVectors.Any(v => v.YParity == 0) && LegacyVectors.Any(v => v.YParity == 1), "both legacy parities");
        Assert.IsTrue(LegacyVectors.Any(v => v.ChainId == 98866 && v.YParity == 0) && LegacyVectors.Any(v => v.ChainId == 98866 && v.YParity == 1), "Plume with both parities");
        Assert.IsTrue(TypedVectors.Any(v => v.ChainId == 98866), "an EIP-1559 transaction on chain 98866");
        Assert.IsTrue(LegacyVectors.Any(v => H(v.RHex).Length < 32 || H(v.SHex).Length < 32), "a signature whose r or s is shorter than 32 bytes on the wire");
        Assert.IsTrue(LegacyVectors.All(v => v.ChainId > 110), "every legacy vector has a chain id whose v exceeds one byte");
        Assert.IsTrue(LegacyVectors.All(v => v.SenderHex == SignerAddressHex) && TypedVectors.All(v => v.SenderHex == SignerAddressHex));
    }

    [TestMethod]
    public void LegacyVectors_VOnTheWire_IsTheEip155ValueOfTheIndependentParityAndChainId()
    {
        // The recorded v comes from the independent library; the formula below is written from EIP-155 with BigInteger.
        foreach (var vector in LegacyVectors)
        {
            var expected = EvmSignatureOracle.LegacyV(vector.YParity, vector.ChainId);
            CollectionAssert.AreEqual(Minimal(expected), H(vector.VHex), vector.Name);
        }
    }

    // ------------------------------------------------------------------ legacy transactions

    [TestMethod]
    public void LegacyVectors_DecodeWithTheAdapters_EveryFieldMatches()
    {
        foreach (var vector in LegacyVectors)
        {
            var root = DecodeOk(H(vector.SignedHex));
            var workspace = NewWorkspace();
            Assert.AreEqual(RLPStatus.Success, root.TryIndexDirectChildren(workspace, out var index, out _), vector.Name);
            Assert.AreEqual(9, index.Count, vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(0, out var nonce), vector.Name);
            Assert.AreEqual(RLPStatus.Success, nonce.TryGetUInt64(out var nonceValue), vector.Name);
            Assert.AreEqual(vector.Nonce, nonceValue, vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(1, out var gasPrice), vector.Name);
            Assert.AreEqual(RLPStatus.Success, gasPrice.TryGetUInt256(out var gasPriceValue), vector.Name);
            Assert.AreEqual(U256(vector.GasPrice), gasPriceValue, vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(2, out var gasLimit), vector.Name);
            Assert.AreEqual(RLPStatus.Success, gasLimit.TryGetUInt64(out var gasLimitValue), vector.Name);
            Assert.AreEqual(vector.GasLimit, gasLimitValue, vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(3, out var to), vector.Name);
            Assert.AreEqual(RLPStatus.Success, to.TryGetOptionalAddress(out var toValue, out var isCreation), vector.Name);
            Assert.IsFalse(isCreation, vector.Name);
            Assert.AreEqual(Address.FromEvmBytes(H(vector.To)), toValue, vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(4, out var value), vector.Name);
            Assert.AreEqual(RLPStatus.Success, value.TryGetUInt256(out var valueValue), vector.Name);
            Assert.AreEqual(U256(vector.Value), valueValue, vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(5, out var data), vector.Name);
            CollectionAssert.AreEqual(H(vector.Data), data.Payload.ToArray(), vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(6, out var v), vector.Name);
            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(7, out var r), vector.Name);
            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(8, out var s), vector.Name);
            CollectionAssert.AreEqual(H(vector.VHex), v.Payload.ToArray(), $"{vector.Name}: v bytes on the wire");

            Assert.AreEqual(RLPStatus.Success, RLPTypedExtensions.TryGetEvmLegacySignature(v, r, s, out var signature, out var chainId), vector.Name);
            Assert.AreEqual(vector.ChainId, chainId, $"{vector.Name}: chain id");
            Assert.AreEqual(vector.YParity, signature.V, $"{vector.Name}: y parity");
            Assert.AreEqual(SignatureType.Evm, signature.Type, vector.Name);
            Assert.AreEqual(new Hash32(Padded32(new BigInteger(H(vector.RHex), isUnsigned: true, isBigEndian: true))), signature.R, $"{vector.Name}: r");
            Assert.AreEqual(new Hash32(Padded32(new BigInteger(H(vector.SHex), isUnsigned: true, isBigEndian: true))), signature.S, $"{vector.Name}: s");
        }
    }

    private static byte[] BuildLegacy(LegacyVector vector, bool signed)
    {
        var gasPrice = U256(vector.GasPrice);
        var value = U256(vector.Value);
        var data = H(vector.Data);
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetByteStringLength(data, out var dataLength));
        var r = U256FromMinimalHex(vector.RHex);
        var s = U256FromMinimalHex(vector.SHex);
        Assert.AreEqual(RLPStatus.Success, RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(vector.YParity, vector.ChainId, out var vLength));

        var childrenLength = RLPEncoder.GetUInt64EncodedLength(vector.Nonce)
            + RLPTypedExtensions.GetUInt256EncodedLength(gasPrice)
            + RLPEncoder.GetUInt64EncodedLength(vector.GasLimit)
            + RLPTypedExtensions.AddressEncodedLength
            + RLPTypedExtensions.GetUInt256EncodedLength(value)
            + dataLength;
        if (signed)
        {
            childrenLength += vLength + RLPTypedExtensions.GetUInt256EncodedLength(r) + RLPTypedExtensions.GetUInt256EncodedLength(s);
        }
        else
        {
            // EIP-155 preimage tail: chainId, 0, 0.
            childrenLength += RLPEncoder.GetUInt64EncodedLength(vector.ChainId) + 1 + 1;
        }

        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(childrenLength, out var total));
        var destination = Filled(total + 3);
        var writer = new RLPWriter(destination, new int[4]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(childrenLength), vector.Name);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(vector.Nonce), vector.Name);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(gasPrice), vector.Name);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(vector.GasLimit), vector.Name);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteAddress(Address.FromEvmBytes(H(vector.To))), vector.Name);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(value), vector.Name);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(data), vector.Name);
        if (signed)
        {
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteEvmLegacyV(vector.YParity, vector.ChainId), vector.Name);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(r), vector.Name);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(s), vector.Name);
        }
        else
        {
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(vector.ChainId), vector.Name);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(0), vector.Name);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(0), vector.Name);
        }

        Assert.AreEqual(RLPStatus.Success, writer.TryEndList(), $"{vector.Name}: the sizers announced an exact length");
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), vector.Name);
        Assert.AreEqual(total, written, vector.Name);
        Assert.IsTrue(IsFilled(destination.AsSpan(written)), vector.Name);
        return destination.AsSpan(0, written).ToArray();
    }

    [TestMethod]
    public void LegacyVectors_RebuiltWithTheWriterAdapters_EqualTheSignedBytes()
    {
        foreach (var vector in LegacyVectors)
        {
            CollectionAssert.AreEqual(H(vector.SignedHex), BuildLegacy(vector, signed: true), vector.Name);
        }
    }

    [TestMethod]
    public void LegacyVectors_Eip155SigningPreimage_RebuiltWithTheWriter_EqualsTheIndependentPreimage_AndItsKeccakIsTheSigningHash()
    {
        foreach (var vector in LegacyVectors)
        {
            var preimage = BuildLegacy(vector, signed: false);
            CollectionAssert.AreEqual(H(vector.PreimageHex), preimage, vector.Name);
            Assert.AreEqual(new Hash32(H(vector.SigningHashHex)), Keccak(preimage), $"{vector.Name}: Keccak-256 of the preimage is the independent signing hash");
        }
    }

    // ------------------------------------------------------------------ EIP-1559 transactions

    private static readonly byte[] DynamicFeeTypeByte = [0x02];

    [TestMethod]
    public void TypedVectors_DecodeWithTheAdapters_EveryFieldMatches()
    {
        foreach (var vector in TypedVectors)
        {
            var signed = H(vector.SignedHex);
            Assert.AreEqual((byte)0x02, signed[0], $"{vector.Name}: EIP-2718 type byte");

            var root = DecodeOk(signed[1..]);
            var workspace = NewWorkspace();
            Assert.AreEqual(RLPStatus.Success, root.TryIndexDirectChildren(workspace, out var index, out _), vector.Name);
            Assert.AreEqual(12, index.Count, vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(0, out var chain), vector.Name);
            Assert.AreEqual(RLPStatus.Success, chain.TryGetUInt64(out var chainId), vector.Name);
            Assert.AreEqual(vector.ChainId, chainId, vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(1, out var nonce), vector.Name);
            Assert.AreEqual(RLPStatus.Success, nonce.TryGetUInt64(out var nonceValue), vector.Name);
            Assert.AreEqual(vector.Nonce, nonceValue, vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(2, out var tip), vector.Name);
            Assert.AreEqual(RLPStatus.Success, tip.TryGetUInt256(out var tipValue), vector.Name);
            Assert.AreEqual(U256(vector.MaxPriorityFee), tipValue, vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(3, out var fee), vector.Name);
            Assert.AreEqual(RLPStatus.Success, fee.TryGetUInt256(out var feeValue), vector.Name);
            Assert.AreEqual(U256(vector.MaxFee), feeValue, vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(4, out var gas), vector.Name);
            Assert.AreEqual(RLPStatus.Success, gas.TryGetUInt64(out var gasValue), vector.Name);
            Assert.AreEqual(vector.GasLimit, gasValue, vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(5, out var to), vector.Name);
            Assert.AreEqual(RLPStatus.Success, to.TryGetAddress(out var toValue), vector.Name);
            Assert.AreEqual(Address.FromEvmBytes(H(vector.To)), toValue, vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(6, out var value), vector.Name);
            Assert.AreEqual(RLPStatus.Success, value.TryGetUInt256(out var valueValue), vector.Name);
            Assert.AreEqual(U256(vector.Value), valueValue, vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(7, out var data), vector.Name);
            CollectionAssert.AreEqual(H(vector.Data), data.Payload.ToArray(), vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(8, out var accessList), vector.Name);
            Assert.AreEqual(RLPStatus.Success, accessList.TryGetChildren(out var entries), vector.Name);
            var entryNumber = 0;
            while (entries.MoveNext())
            {
                var expectedEntry = vector.AccessList[entryNumber];
                Assert.AreEqual(RLPStatus.Success, entries.Current.TryGetChildren(out var parts), vector.Name);
                Assert.IsTrue(parts.MoveNext(), vector.Name);
                Assert.AreEqual(RLPStatus.Success, parts.Current.TryGetAddress(out var address), vector.Name);
                Assert.AreEqual(Address.FromEvmBytes(H(expectedEntry.Address)), address, vector.Name);
                Assert.IsTrue(parts.MoveNext(), vector.Name);
                Assert.AreEqual(RLPStatus.Success, parts.Current.TryGetChildren(out var keys), vector.Name);
                var keyNumber = 0;
                while (keys.MoveNext())
                {
                    Assert.AreEqual(RLPStatus.Success, keys.Current.TryGetHash32(out var key), vector.Name);
                    Assert.AreEqual(new Hash32(H(expectedEntry.Keys[keyNumber])), key, vector.Name);
                    keyNumber++;
                }

                Assert.AreEqual(expectedEntry.Keys.Length, keyNumber, vector.Name);
                Assert.IsFalse(parts.MoveNext(), vector.Name);
                entryNumber++;
            }

            Assert.AreEqual(vector.AccessList.Length, entryNumber, vector.Name);

            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(9, out var yParity), vector.Name);
            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(10, out var r), vector.Name);
            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(11, out var s), vector.Name);
            Assert.AreEqual(RLPStatus.Success, RLPTypedExtensions.TryGetEvmYParitySignature(yParity, r, s, out var signature), vector.Name);
            Assert.AreEqual(vector.YParity, signature.V, $"{vector.Name}: y parity");
            Assert.AreEqual(new Hash32(Padded32(new BigInteger(H(vector.RHex), isUnsigned: true, isBigEndian: true))), signature.R, $"{vector.Name}: r");
            Assert.AreEqual(new Hash32(Padded32(new BigInteger(H(vector.SHex), isUnsigned: true, isBigEndian: true))), signature.S, $"{vector.Name}: s");
        }
    }

    private static byte[] BuildTyped(TypedVector vector, bool signed)
    {
        var tip = U256(vector.MaxPriorityFee);
        var fee = U256(vector.MaxFee);
        var value = U256(vector.Value);
        var data = H(vector.Data);
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetByteStringLength(data, out var dataLength));
        var r = U256FromMinimalHex(vector.RHex);
        var s = U256FromMinimalHex(vector.SHex);

        // accessList = [ [address, [key, ...]], ... ]: the entry lengths are announced bottom-up with the sizers.
        var entryChildrenLengths = new int[vector.AccessList.Length];
        var keysChildrenLengths = new int[vector.AccessList.Length];
        var accessListChildrenLength = 0;
        for (var i = 0; i < vector.AccessList.Length; i++)
        {
            keysChildrenLengths[i] = vector.AccessList[i].Keys.Length * RLPTypedExtensions.Hash32EncodedLength;
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(keysChildrenLengths[i], out var keysTotal));
            entryChildrenLengths[i] = RLPTypedExtensions.AddressEncodedLength + keysTotal;
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(entryChildrenLengths[i], out var entryTotal));
            accessListChildrenLength += entryTotal;
        }

        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(accessListChildrenLength, out var accessListTotal));

        var childrenLength = RLPEncoder.GetUInt64EncodedLength(vector.ChainId)
            + RLPEncoder.GetUInt64EncodedLength(vector.Nonce)
            + RLPTypedExtensions.GetUInt256EncodedLength(tip)
            + RLPTypedExtensions.GetUInt256EncodedLength(fee)
            + RLPEncoder.GetUInt64EncodedLength(vector.GasLimit)
            + RLPTypedExtensions.AddressEncodedLength
            + RLPTypedExtensions.GetUInt256EncodedLength(value)
            + dataLength
            + accessListTotal;
        if (signed)
        {
            childrenLength += 1 + RLPTypedExtensions.GetUInt256EncodedLength(r) + RLPTypedExtensions.GetUInt256EncodedLength(s);
        }

        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(childrenLength, out var total));
        var destination = Filled(total + 3);
        var writer = new RLPWriter(destination, new int[8]);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(childrenLength), vector.Name);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(vector.ChainId), vector.Name);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(vector.Nonce), vector.Name);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(tip), vector.Name);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(fee), vector.Name);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(vector.GasLimit), vector.Name);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteAddress(Address.FromEvmBytes(H(vector.To))), vector.Name);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(value), vector.Name);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteByteString(data), vector.Name);
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(accessListChildrenLength), vector.Name);
        for (var i = 0; i < vector.AccessList.Length; i++)
        {
            Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(entryChildrenLengths[i]), vector.Name);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteAddress(Address.FromEvmBytes(H(vector.AccessList[i].Address))), vector.Name);
            Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(keysChildrenLengths[i]), vector.Name);
            foreach (var key in vector.AccessList[i].Keys)
            {
                Assert.AreEqual(RLPStatus.Success, writer.TryWriteHash32(new Hash32(H(key))), vector.Name);
            }

            Assert.AreEqual(RLPStatus.Success, writer.TryEndList(), vector.Name);
            Assert.AreEqual(RLPStatus.Success, writer.TryEndList(), vector.Name);
        }

        Assert.AreEqual(RLPStatus.Success, writer.TryEndList(), vector.Name);
        if (signed)
        {
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteEvmYParity(vector.YParity), vector.Name);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(r), vector.Name);
            Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt256(s), vector.Name);
        }

        Assert.AreEqual(RLPStatus.Success, writer.TryEndList(), $"{vector.Name}: the sizers announced an exact length");
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var written), vector.Name);
        Assert.AreEqual(total, written, vector.Name);
        Assert.IsTrue(IsFilled(destination.AsSpan(written)), vector.Name);
        return RlpReference.Concatenate(DynamicFeeTypeByte, destination.AsSpan(0, written).ToArray());
    }

    [TestMethod]
    public void TypedVectors_RebuiltWithTheWriterAdapters_EqualTheSignedBytes()
    {
        foreach (var vector in TypedVectors)
        {
            CollectionAssert.AreEqual(H(vector.SignedHex), BuildTyped(vector, signed: true), vector.Name);
        }
    }

    [TestMethod]
    public void TypedVectors_SigningPreimage_RebuiltWithTheWriter_EqualsTheIndependentPreimage_AndItsKeccakIsTheSigningHash()
    {
        foreach (var vector in TypedVectors)
        {
            var preimage = BuildTyped(vector, signed: false);
            CollectionAssert.AreEqual(H(vector.PreimageHex), preimage, vector.Name);
            Assert.AreEqual(new Hash32(H(vector.SigningHashHex)), Keccak(preimage), $"{vector.Name}: Keccak-256 of 0x02 || rlp is the independent signing hash");
        }
    }

    // ------------------------------------------------------------------ the old adapter's limit, on real data

    [TestMethod]
    public void LegacyVectors_VAboveOneByte_WasNotReadableThroughAByteSizedV_ButIsNow()
    {
        // The Plume transactions carry a 3-byte v; polygon/base/arbitrum a 2- or 3-byte one. All above 255 once.
        foreach (var vector in LegacyVectors)
        {
            var v = new BigInteger(H(vector.VHex), isUnsigned: true, isBigEndian: true);
            Assert.IsTrue(v > 255, $"{vector.Name}: v {v} exceeds one byte");
        }
    }
}
