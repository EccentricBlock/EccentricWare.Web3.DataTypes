using System.Numerics;
using EccentricWare.Web3.DataTypes.ABI;
using EccentricWare.Web3.DataTypes.ABI.EvmAdapters;
using EccentricWare.Web3.DataTypes.Rlp;
using EccentricWare.Web3.DataTypes.Tests.Rlp;
using Nethereum.Model;
using Nethereum.Signer;
using Nethereum.Util;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.EvmAdapters.EvmAdapterFixtures;

namespace EccentricWare.Web3.DataTypes.Tests.ABI.EvmAdapters;

/// <summary>
/// EIP-7702 end to end: <see cref="RLPCalldataAdapter"/> validates every authorization tuple, <see cref="EvmAuthorizationProcessor"/> applies the list as the
/// EIP specifies (every outcome, the sender's nonce increment, last-valid-wins, fail-closed host answers), and <see cref="NestedCallWalker"/> resolves calls
/// to delegated accounts by the delegate's code, one hop only.
/// </summary>
/// <remarks>
/// Independence: tuples and two complete transactions were signed by Foundry <c>cast</c> 1.9.0 (alloy, Rust: <c>cast wallet sign-auth</c> and
/// <c>cast mktx --auth</c>); the test host recovers signers with Nethereum.Signer 7.1.0 (BouncyCastle / NBitcoin secp256k1). The library supplies only the
/// signing hash in between, so a wrong hash recovers a wrong authority. Keys are Anvil's published development keys (test-only, publicly known).
/// </remarks>
[TestClass]
public sealed class Eip7702Tests
{
    private const int Each = NestedCallRule.EachElement;

    // Anvil development accounts 1 and 2 (authorities) and 0 (a relayer that sponsors).
    private const string KeyA = "59c6995e998f97a5a0044966f0945389dc9e86dae88c7a8412f4603b6b78690d";
    private static readonly Address AuthorityA = Address.Parse("0x70997970C51812dc3A010C7d01b50e0d17dc79C8");
    private static readonly Address AuthorityB = Address.Parse("0x3C44CdDdB6a900fa2b585dd299e03d12FA4293BC");
    internal static readonly Address Relayer = Address.Parse("0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266");

    private static readonly byte[] DelegateBytes = Hex("acc0acc0acc0acc0acc0acc0acc0acc0acc0acc0");
    private static readonly byte[] OtherDelegateBytes = Hex("11b0011b0011b0011b0011b0011b0011b0011b00");
    private static readonly byte[] TokenBytes = Hex("a0b86991c6218b36c1d19d4a2e9eb0ce3606eb48");
    internal static readonly Address Delegate = Address.FromEvmBytes(DelegateBytes);
    private static readonly Address OtherDelegate = Address.FromEvmBytes(OtherDelegateBytes);
    private static readonly Address Token = Address.FromEvmBytes(TokenBytes);
    internal static readonly Address Evil = EvmAddress(0xee);
    private static readonly Hash32 DelegateCode = new(0, 0, 0, 0xd1);
    private static readonly Hash32 OtherDelegateCode = new(0, 0, 0, 0xe1);
    private static readonly Hash32 TokenCode = new(0, 0, 0, 0x20);
    private static readonly Hash32 EvilCode = new(0, 0, 0, 0xee);

    // cast wallet sign-auth <address> --private-key <key> --nonce <n> --chain <c>: [chain_id, address, nonce, y_parity, r, s].
    private const string AuthA_D_8453_Nonce10 = "f85c82210594acc0acc0acc0acc0acc0acc0acc0acc0acc0acc00a01a0de5232aba000dd815308ab30032c724b15a851ac8121283bc0d1764232844832a0203ffe565856a66f7617040cef7cec34f4454e5672502cbf3a6d7eeb9f4cd045";
    private const string AuthA_D_AnyChain_Nonce10 = "f85a8094acc0acc0acc0acc0acc0acc0acc0acc0acc0acc00a01a0f2f2a6800d06cc10ce4f6d42ccf665fd5a7da80fab78d599e019ec037569cd89a01a4b3d1ef7ce3062d34d61755f9e8a5ea0236c352ad02cfe73241c18f649cf71";
    private const string AuthA_Clear_8453_Nonce11 = "f85c8221059400000000000000000000000000000000000000000b80a067b90bade733764fcf1ead2350336558789d39bb1aaf0c3f4d577848881cc0f1a06efdf8f318621178b599d1ab3f3b12128fe2ee91d6eae41b8e684659e59d0809";
    private const string AuthA_E_8453_Nonce11 = "f85c8221059411b0011b0011b0011b0011b0011b0011b0011b000b80a02148f84e74154bbbfaeb875ac63e6d55279f38a62cff8b473f19dddd5a6b2251a07118c6615dd411f9239c7e02838b4c4ecd17989911fb9e5b1497c4e3e433ab5d";
    private const string AuthB_D_8453_Nonce0 = "f85c82210594acc0acc0acc0acc0acc0acc0acc0acc0acc0acc08001a034b00d0358e7dead47be362056a318cda955241934c123e2308c15e4c2269e30a04632ab554760c640960c3da94b2b17deb0d68f783532531a7d345d1118750cdb";
    private const string AuthA_D_Chain1_Nonce10 = "f85a0194acc0acc0acc0acc0acc0acc0acc0acc0acc0acc00a01a04b2cf7d306366de1c4503df8f7906406ffcad82d9d8e0620703e75c7d9007831a03ff2ff02a67ee7d3b0d14766294f631508fb4705675ff699fc496babbeb820ea";
    private const string AuthA_D_8453_Nonce9 = "f85c82210594acc0acc0acc0acc0acc0acc0acc0acc0acc0acc00901a0229743b9106c4a894fc57c416b18b09b4ce2af3558d469204809a8892cd7d993a0054c5c621fcfeef9bcf35bff4cfc4cea6f407b0399c3bc76c527e049ac38c621";

    // cast mktx --chain 8453 --auth <AuthA_D_8453_Nonce10> <AuthorityA> "executeBatch(address[],uint256[],bytes[])" [token] [0] [transfer(0xd8da..., 5)]:
    // signed by the relayer at transaction nonce 4 (sponsored), and by the authority itself at transaction nonce 9 (self-sponsored: the tuple's nonce 10 is
    // the transaction nonce plus one, because the sender's nonce is incremented before the list is processed).
    internal const string SponsoredTransaction = "04f9027482210504843b9aca008504a817c800830493e09470997970c51812dc3a010c7d01b50e0d17dc79c880b901a447e1da2a000000000000000000000000000000000000000000000000000000000000006000000000000000000000000000000000000000000000000000000000000000a000000000000000000000000000000000000000000000000000000000000000e00000000000000000000000000000000000000000000000000000000000000001000000000000000000000000a0b86991c6218b36c1d19d4a2e9eb0ce3606eb4800000000000000000000000000000000000000000000000000000000000000010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000100000000000000000000000000000000000000000000000000000000000000200000000000000000000000000000000000000000000000000000000000000044a9059cbb000000000000000000000000d8da6bf26964af9d7eed9e03e53415d37aa96045000000000000000000000000000000000000000000000000000000000000000500000000000000000000000000000000000000000000000000000000c0f85ef85c82210594acc0acc0acc0acc0acc0acc0acc0acc0acc0acc00a01a0de5232aba000dd815308ab30032c724b15a851ac8121283bc0d1764232844832a0203ffe565856a66f7617040cef7cec34f4454e5672502cbf3a6d7eeb9f4cd04501a0e6e00ac7585661c1b212e0e99ea2dd637d5e43fbb5a0636f24c882d1f9c5bc3fa02f55a0673594c6d1f4d4478048f1ed82c4f2b0d831d94370386f6a4cc5a7729e";
    private const string SelfSponsoredTransaction = "04f9027482210509843b9aca008504a817c800830493e09470997970c51812dc3a010c7d01b50e0d17dc79c880b901a447e1da2a000000000000000000000000000000000000000000000000000000000000006000000000000000000000000000000000000000000000000000000000000000a000000000000000000000000000000000000000000000000000000000000000e00000000000000000000000000000000000000000000000000000000000000001000000000000000000000000a0b86991c6218b36c1d19d4a2e9eb0ce3606eb4800000000000000000000000000000000000000000000000000000000000000010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000100000000000000000000000000000000000000000000000000000000000000200000000000000000000000000000000000000000000000000000000000000044a9059cbb000000000000000000000000d8da6bf26964af9d7eed9e03e53415d37aa96045000000000000000000000000000000000000000000000000000000000000000500000000000000000000000000000000000000000000000000000000c0f85ef85c82210594acc0acc0acc0acc0acc0acc0acc0acc0acc0acc00a01a0de5232aba000dd815308ab30032c724b15a851ac8121283bc0d1764232844832a0203ffe565856a66f7617040cef7cec34f4454e5672502cbf3a6d7eeb9f4cd04580a04e2560940331cc5e9a44348ce26d45f076631ad7ffd09cd7fd4dcb575daaaf1fa07eff6878042b319be1b93730cec1bfbed33b8c16253541c5f8ebb4cafa01dee9";

    private static readonly BigInteger SecpOrder = BigInteger.Parse("0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141", System.Globalization.NumberStyles.HexNumber);
    private static readonly BigInteger SecpPrime = BigInteger.Parse("0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC2F", System.Globalization.NumberStyles.HexNumber);

    private static readonly ABIFunctionSchema ExecuteBatch = Function("executeBatch(address[],uint256[],bytes[])");
    private static readonly ABIFunctionSchema Multicall = Function("multicall(bytes[])");
    private static readonly ABIFunctionSchema Transfer = Function("transfer(address,uint256)");

    private static ExecutionDescriptor Nested(ABIFunctionSchema function, params NestedCallRule[] rules)
    {
        Assert.IsTrue(ExecutionDescriptor.TryCreateNested(function, rules, out ExecutionDescriptor? descriptor, out ExecutionDescriptorError error, out _), error.ToString());
        return descriptor;
    }

    /// <summary>The delegates' functions are registered once, under the delegate's address, for every account delegated to it.</summary>
    internal static NestedCallWalker Walker() => new(
        new ContractFunctionResolverBuilder()
            .Add(ChainId, Delegate, DelegateCode, Nested(ExecuteBatch, NestedCallRule.CallWithValue([0, Each], [2, Each], [1, Each])))
            .Add(ChainId, Delegate, DelegateCode, Nested(Multicall, NestedCallRule.DelegateCallToSelf([0, Each])))
            .Add(ChainId, Delegate, DelegateCode, ExecutionDescriptor.NoNestedCalls(Transfer))
            .Add(ChainId, OtherDelegate, OtherDelegateCode, Nested(ExecuteBatch, NestedCallRule.CallWithValue([0, Each], [2, Each], [1, Each])))
            .Add(ChainId, Token, TokenCode, ExecutionDescriptor.NoNestedCalls(Transfer))
            .Build(),
        NestedCallLimits.Default,
        ABIDecodeLimits.Default);

    // ------------------------------------------------------------------ the host: pinned state, Nethereum recovery, a recording policy

    internal sealed class ChainState : INestedCallHost, IEvmAuthorizationHost
    {
        public readonly Dictionary<Address, EvmAccountCode> Code = new()
        {
            [AuthorityA] = EvmAccountCode.Empty, [AuthorityB] = EvmAccountCode.Empty, [Relayer] = EvmAccountCode.Empty,
            [Delegate] = EvmAccountCode.Contract(DelegateCode), [OtherDelegate] = EvmAccountCode.Contract(OtherDelegateCode),
            [Token] = EvmAccountCode.Contract(TokenCode), [Evil] = EvmAccountCode.Contract(EvilCode),
        };

        public readonly Dictionary<Address, ulong> Nonces = new() { [AuthorityA] = 10, [AuthorityB] = 0, [Relayer] = 4 };
        public readonly List<Hash32> SigningHashes = [];
        public readonly List<Address> NonceQueries = [];
        public readonly List<NestedCall> Seen = [];
        public bool UnknownAccountsAreEmpty;
        public ulong ExpectedChain = ChainId;
        public EvmAuthorityRecovery? ForcedRecovery;
        public Address ForcedAuthority;
        public Func<NestedCall, bool> Policy = _ => true;

        public EvmAuthorityRecovery RecoverAuthority(in Hash32 signingHash, byte yParity, in uint256 r, in uint256 s, out Address authority)
        {
            SigningHashes.Add(signingHash);
            Assert.IsTrue(yParity <= 1 && !r.IsZero && !s.IsZero, "the processor checks the signature values first");
            if (ForcedRecovery is { } forced)
            {
                authority = ForcedAuthority;
                return forced;
            }

            byte[] hash = new byte[32], rBytes = new byte[32], sBytes = new byte[32];
            signingHash.WriteBigEndian(hash);
            r.WriteBigEndian(rBytes);
            s.WriteBigEndian(sBytes);
            authority = default;
            try
            {
                EthECKey? key = EthECKey.RecoverFromSignature(EthECDSASignatureFactory.FromComponents(rBytes, sBytes), yParity, hash);
                if (key is null)
                {
                    return EvmAuthorityRecovery.NotRecoverable;
                }

                authority = Address.FromEvmBytes(key.GetPublicAddressAsBytes());
                return EvmAuthorityRecovery.Recovered;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException || exception.GetType().Namespace?.StartsWith("Org.BouncyCastle", StringComparison.Ordinal) == true)
            {
                return EvmAuthorityRecovery.NotRecoverable; // no curve point for r (test-local mapping; checked by the off-curve test)
            }
        }

        public bool TryGetNonce(ulong chainId, in Address account, out ulong nonce)
        {
            Assert.AreEqual(ExpectedChain, chainId, "state is read on the transaction's chain");
            NonceQueries.Add(account);
            bool known = Nonces.TryGetValue(account, out nonce);
            if (!known && UnknownAccountsAreEmpty)
            {
                nonce = 0;
                return true;
            }

            return known;
        }

        public bool TryGetCode(ulong chainId, in Address account, out EvmAccountCode code)
        {
            Assert.AreEqual(ExpectedChain, chainId, "state is read on the transaction's chain");
            if (Code.TryGetValue(account, out code))
            {
                return true;
            }

            code = UnknownAccountsAreEmpty ? EvmAccountCode.Empty : default;
            return UnknownAccountsAreEmpty;
        }

        public bool Allows(in NestedCall nestedCall, scoped in ABICallData calldata)
        {
            Seen.Add(nestedCall);
            return Policy(nestedCall);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static RlpNode Tuple(string castHex)
    {
        Assert.IsTrue(NethereumRlpAdapter.TryDecode(Hex(castHex), NethereumRlpAdapter.ItemBudget, out RlpNode? node, out string failure), failure);
        return node!;
    }

    private static RlpNode TupleOf(RlpNode chainId, byte[] address, RlpNode nonce, RlpNode yParity, RlpNode r, RlpNode s) =>
        RlpNode.ListOf(chainId, RlpNode.Bytes(address), nonce, yParity, r, s);

    private static RlpNode[] FieldsOf(RlpNode tuple) => [.. ((RlpList)tuple).Items];

    private static RlpNode Big(BigInteger value) => RlpNode.Bytes(value.IsZero ? [] : value.ToByteArray(isUnsigned: true, isBigEndian: true));

    /// <summary>A tuple signed by authority A with Nethereum's <c>Authorisation7702Signer</c> (an independent signer and hasher).</summary>
    private static RlpNode SignedTuple(ulong chain, Address delegateAddress, ulong nonce)
    {
        Authorisation7702Signed signed = new Authorisation7702Signer().SignAuthorisation(
            new EthECKey(KeyA), new Authorisation7702 { ChainId = new EvmUInt256(chain), Address = HexOf(delegateAddress.ToBytes()), Nonce = new EvmUInt256(nonce) });
        static RlpNode Minimal(byte[] bytes) => RlpNode.Bytes(bytes.SkipWhile(b => b == 0).ToArray());
        return TupleOf(Number(chain), delegateAddress.ToBytes(), Number(nonce), Minimal(signed.V ?? []), Minimal(signed.R), Minimal(signed.S));
    }

    private static BigInteger ToBig(RlpNode node) => new(((RlpString)node).Value, isUnsigned: true, isBigEndian: true);

    /// <summary>An EIP-7702 envelope (the explicit field list: transaction nonce 9) to <paramref name="to"/> with these tuples.</summary>
    private static byte[] Envelope(byte[] to, byte[] data, params RlpNode[] tuples)
    {
        RlpNode[] fields = Fields7702(to, data);
        return Encode(0x04, [.. fields[..9], RlpNode.ListOf(tuples), .. fields[10..]]);
    }

    private static byte[] BatchCall(params (byte[] Target, ulong Value, byte[] Data)[] calls) =>
    [
        .. Hex("47e1da2a"),
        .. ABIIndependentEncoder.EncodeArguments(ABIReference.ParseTuple("(address[],uint256[],bytes[])"), new RSeq([
            new RSeq([.. calls.Select(c => (RValue)new RWord([.. new byte[12], .. c.Target]))]),
            new RSeq([.. calls.Select(c => (RValue)new RWord(Word(c.Value)))]),
            new RSeq([.. calls.Select(c => (RValue)new RBlob(c.Data))]),
        ])),
    ];

    private static byte[] MulticallOf(params byte[][] children) =>
        [.. Hex("ac9650d8"), .. ABIIndependentEncoder.EncodeArguments(ABIReference.ParseTuple("(bytes[])"), new RSeq([new RSeq([.. children.Select(c => (RValue)new RBlob(c))])]))];

    private static readonly byte[] TokenTransfer = TransferCalldata(5);

    private static EvmAuthorization[] Process(byte[] envelope, Address sender, ChainState state, out EvmAuthorizationStatus status, EvmTransactionForm form = EvmTransactionForm.SignedTransaction)
    {
        var limits = RLPDecodeLimits.Default;
        Assert.IsTrue(RLPCalldataAdapter.TryRead(envelope, form, in limits, out EvmTransactionView view, out EvmTransactionStatus read), read.ToString());
        var entries = new EvmAuthorization[view.AuthorizationCount + 2];
        Array.Fill(entries, default);
        bool ok = EvmAuthorizationProcessor.TryProcess(in view, sender, ref state, entries, out int written, out status);
        Assert.AreEqual(ok, status == EvmAuthorizationStatus.Success);
        Assert.AreEqual(ok ? view.AuthorizationCount : 0, written);
        Assert.IsTrue(entries[view.AuthorizationCount..].All(e => e.Outcome == EvmAuthorizationOutcome.None), "nothing written past the list");
        if (!ok)
        {
            Assert.IsTrue(entries.All(e => e.Outcome == EvmAuthorizationOutcome.None && e.Authority == default), "a failure leaves nothing behind");
        }

        return entries[..written];
    }

    private static EvmAuthorization[] Process(byte[] envelope, Address sender, ChainState state)
    {
        EvmAuthorization[] entries = Process(envelope, sender, state, out EvmAuthorizationStatus status);
        Assert.AreEqual(EvmAuthorizationStatus.Success, status);
        for (int i = 0; i < entries.Length; i++)
        {
            Assert.AreEqual(i, entries[i].Index);
            Assert.AreNotEqual(EvmAuthorizationOutcome.None, entries[i].Outcome);
            Assert.AreEqual(entries[i].IsRecovered, entries[i].Authority != default, "an authority exactly when one was recovered");
        }

        return entries;
    }

    private static NestedCallWalkResult Walk(byte[] envelope, Address sender, ChainState state, EvmAuthorization[]? entries = null)
    {
        var limits = RLPDecodeLimits.Default;
        Assert.IsTrue(RLPCalldataAdapter.TryRead(envelope, EvmTransactionForm.SignedTransaction, in limits, out EvmTransactionView view, out EvmTransactionStatus read), read.ToString());
        entries ??= Process(envelope, sender, state);
        bool ok = Walker().TryWalk(in view, sender, entries, ref state, out NestedCallWalkResult result);
        Assert.AreEqual(ok, result.Status == NestedCallStatus.Success);
        return result;
    }

    private static EvmAuthorization Only(byte[] envelope, Address sender, ChainState state) => Process(envelope, sender, state).Single();

    // ------------------------------------------------------------------ signing hash and recovery against independent implementations

    [TestMethod]
    public void CastSignedTuples_RecoverTheirAuthorities_ThroughTheLibrarysSigningHash()
    {
        (string Tuple, Address Authority, Address Delegate, ulong Nonce)[] fixtures =
        [
            (AuthA_D_8453_Nonce10, AuthorityA, Delegate, 10),
            (AuthA_D_AnyChain_Nonce10, AuthorityA, Delegate, 10),
            (AuthA_Clear_8453_Nonce11, AuthorityA, Address.ZeroEvm, 11),
            (AuthA_E_8453_Nonce11, AuthorityA, OtherDelegate, 11),
            (AuthB_D_8453_Nonce0, AuthorityB, Delegate, 0),
            (AuthA_D_8453_Nonce9, AuthorityA, Delegate, 9),
        ];
        foreach (var (tuple, authority, delegateAddress, nonce) in fixtures)
        {
            var state = new ChainState { UnknownAccountsAreEmpty = true };
            EvmAuthorization entry = Process(Envelope(RouterBytes, TokenTransfer, Tuple(tuple)), Relayer, state).Single();
            Assert.IsTrue(entry.IsRecovered, tuple);
            Assert.AreEqual(authority, entry.Authority, $"{tuple}: alloy signed it, Nethereum recovered it over the library's hash");
            Assert.AreEqual(delegateAddress, entry.DelegateAddress);
            Assert.AreEqual(nonce, entry.Nonce);
            Assert.AreEqual(delegateAddress.IsZero, entry.ClearsDelegation);

            // And the hash itself equals Nethereum's own EIP-7702 hasher.
            var model = new Authorisation7702 { ChainId = new EvmUInt256(entry.ChainId.IsZero ? 0 : ChainId), Address = HexOf(delegateAddress.ToBytes()), Nonce = new EvmUInt256(nonce) };
            CollectionAssert.AreEqual(Authorisation7702RLPEncoderAndHasher.EncodeAndHash(model), Bytes(state.SigningHashes.Single()), tuple);
        }
    }

    private static byte[] Bytes(Hash32 hash)
    {
        byte[] bytes = new byte[32];
        hash.WriteBigEndian(bytes);
        return bytes;
    }

    [TestMethod]
    public void TheSigningHash_UsesTheLongListHeader_WhenTheFieldsExceed55Bytes_AndMatchesNethereum()
    {
        // No processable tuple exceeds 55 bytes (a chain id that matches a transaction fits 8 bytes), so the long form is checked directly: with nonce
        // 2^64 - 2 (9 encoded bytes) a 24-byte chain id gives exactly 55 bytes (the last short header) and 25 bytes gives 56 (the first long one);
        // chain id 2^256 - 1 gives the maximum, 63.
        foreach (int chainBytes in new[] { 0, 1, 8, 23, 24, 25, 26, 32 })
        {
            BigInteger chain = chainBytes == 0 ? BigInteger.Zero : (BigInteger.One << (8 * chainBytes)) - 1;
            const ulong nonce = ulong.MaxValue - 1;
            byte[] tupleBytes = RlpReference.Encode(TupleOf(Big(chain), DelegateBytes, Number(nonce), Number(1), RlpNode.Bytes(Filled(0x33)), RlpNode.Bytes(Filled(0x44))));
            var limits = RLPDecodeLimits.Default;
            Assert.AreEqual(RLPStatus.Success, RLPData.TryDecodeComplete(tupleBytes, in limits, out RLPData tuple, out _));
            Assert.IsTrue(EvmAuthorizationTuple.TryRead(in tuple, out EvmAuthorization entry, out int signedLength));
            Assert.AreEqual((chainBytes == 0 ? 1 : chainBytes + 1) + 21 + 9, signedLength, $"{chainBytes}-byte chain id");
            Hash32 hash = EvmAuthorizationTuple.SigningHash(in tuple, signedLength);

            var model = new Authorisation7702 { ChainId = new EvmUInt256(chain), Address = HexOf(DelegateBytes), Nonce = new EvmUInt256(nonce) };
            CollectionAssert.AreEqual(Authorisation7702RLPEncoderAndHasher.EncodeAndHash(model), Bytes(hash), $"{chainBytes}-byte chain id, {signedLength} signed bytes");
            Assert.AreEqual(new uint256(Bytes32(chain)), entry.ChainId);
        }
    }

    private static byte[] Bytes32(BigInteger value)
    {
        byte[] magnitude = value.IsZero ? [] : value.ToByteArray(isUnsigned: true, isBigEndian: true);
        return [.. new byte[32 - magnitude.Length], .. magnitude];
    }

    [TestMethod]
    public void NethereumSignedTuples_RecoverToo_ForChainZeroAndTheHighestNonce()
    {
        // A second signer (Nethereum's Authorisation7702Signer) for the values cast cannot produce in one call: nonce 2^64 - 2 and chain 0.
        foreach ((ulong chain, ulong nonce) in new[] { (0UL, ulong.MaxValue - 1), (ChainId, 0UL), (ChainId, ulong.MaxValue - 1) })
        {
            RlpNode tuple = SignedTuple(chain, Delegate, nonce);
            var state = new ChainState { UnknownAccountsAreEmpty = true };
            EvmAuthorization entry = Process(Envelope(RouterBytes, TokenTransfer, tuple), Relayer, state).Single();
            Assert.AreEqual(AuthorityA, entry.Authority, $"chain {chain}, nonce {nonce}");
        }
    }

    // ------------------------------------------------------------------ the cast-built transactions, processed and walked

    [TestMethod]
    public void ASponsoredTransaction_BuiltByCast_DelegatesTheAuthority_AndIsWalkedThroughTheDelegatesCode()
    {
        byte[] envelope = Hex(SponsoredTransaction);
        var limits = RLPDecodeLimits.Default;
        Assert.IsTrue(RLPCalldataAdapter.TryRead(envelope, EvmTransactionForm.SignedTransaction, in limits, out EvmTransactionView view, out _));
        Assert.AreEqual(EvmTransactionType.Eip7702, view.Type);
        Assert.AreEqual(4UL, view.Nonce);
        Assert.AreEqual(AuthorityA, view.To);
        Assert.AreEqual(1, view.AuthorizationCount);

        var state = new ChainState();
        EvmAuthorization entry = Only(envelope, Relayer, state);
        Assert.AreEqual(EvmAuthorizationOutcome.Applied, entry.Outcome);
        Assert.AreEqual(AuthorityA, entry.Authority);
        Assert.AreEqual(Delegate, entry.DelegateAddress);
        CollectionAssert.AreEqual(new[] { AuthorityA }, state.NonceQueries, "the authority's nonce comes from the host: it is not the sender");

        NestedCallWalkResult result = Walk(envelope, Relayer, state);
        Assert.AreEqual(NestedCallStatus.Success, result.Status);
        Assert.AreEqual(2, result.CallCount);
        NestedCall root = state.Seen[0];
        Assert.AreEqual(AuthorityA, root.TargetAddress);
        Assert.AreEqual(Delegate, root.CodeAddress, "the delegate's code runs");
        Assert.AreEqual(DelegateCode, root.CodeIdentity);
        Assert.IsTrue(root.IsDelegated);
        Assert.AreEqual(AuthorityA, root.ContextAddress, "in the authority's context");
        Assert.AreEqual(Relayer, root.Sender);
        Assert.AreSame(ExecuteBatch, root.Function);

        NestedCall child = state.Seen[1];
        Assert.AreEqual(Token, child.TargetAddress);
        Assert.AreEqual(Token, child.CodeAddress);
        Assert.IsFalse(child.IsDelegated);
        Assert.AreEqual(AuthorityA, child.Sender, "the token sees the delegated account as msg.sender");
        Assert.AreSame(Transfer, child.Function);
    }

    [TestMethod]
    public void ASelfSponsoredTransaction_BuiltByCast_NeedsTheTransactionNoncePlusOne()
    {
        // Sender and authority are the same account: its nonce is incremented before the list is processed, so tuple nonce 10 matches transaction nonce 9.
        byte[] envelope = Hex(SelfSponsoredTransaction);
        var state = new ChainState();
        state.Nonces[AuthorityA] = 9; // the pre-transaction nonce; the processor must not use it for the sender
        EvmAuthorization entry = Only(envelope, AuthorityA, state);
        Assert.AreEqual(EvmAuthorizationOutcome.Applied, entry.Outcome);
        Assert.IsEmpty(state.NonceQueries, "the sender's nonce is the transaction's nonce plus one, not a host answer");
        Assert.AreEqual(NestedCallStatus.Success, Walk(envelope, AuthorityA, state).Status);
        Assert.AreEqual(AuthorityA, state.Seen[0].Sender);
        Assert.AreEqual(AuthorityA, state.Seen[1].Sender);

        // The same tuple sponsored by someone else, with the authority still at nonce 9, does not apply: the state decides.
        var other = new ChainState();
        other.Nonces[AuthorityA] = 9;
        Assert.AreEqual(EvmAuthorizationOutcome.NonceMismatch, Only(envelope, Relayer, other).Outcome);
    }

    [TestMethod]
    public void TheSigningPayloadForm_IsProcessedTheSameWay()
    {
        // The relayer's signer holds the unsigned payload (type byte and fields without yParity, r, s); the tuples are already signed by their authorities.
        byte[] payload = Encode(0x04, WithoutSignature([.. Fields7702(AuthorityAEvm, BatchCall((TokenBytes, 0, TokenTransfer)))[..9],
            RlpNode.ListOf(Tuple(AuthA_D_8453_Nonce10)), .. Fields7702(RouterBytes, TokenTransfer)[10..]]));
        var state = new ChainState();
        EvmAuthorization[] entries = Process(payload, Relayer, state, out EvmAuthorizationStatus status, EvmTransactionForm.SigningPayload);
        Assert.AreEqual(EvmAuthorizationStatus.Success, status);
        Assert.AreEqual(EvmAuthorizationOutcome.Applied, entries.Single().Outcome);
    }

    private static byte[] AuthorityAEvm => AuthorityA.ToBytes();

    // ------------------------------------------------------------------ every outcome, in EIP order

    private static BigInteger OffCurveX()
    {
        // The smallest x for which x^3 + 7 is not a square mod p: no curve point has it, so no key recovers from r = x (Euler's criterion).
        for (BigInteger x = 1; ; x++)
        {
            BigInteger rhs = (BigInteger.ModPow(x, 3, SecpPrime) + 7) % SecpPrime;
            if (BigInteger.ModPow(rhs, (SecpPrime - 1) / 2, SecpPrime) != BigInteger.One)
            {
                return x;
            }
        }
    }

    [TestMethod]
    public void EveryOutcome_IsDecided_InTheEipsOrder_WithLastValidWins()
    {
        RlpNode[] a = FieldsOf(Tuple(AuthA_D_8453_Nonce10));
        BigInteger s = ToBig(a[5]);
        RlpNode highS = TupleOf(a[0], DelegateBytes, a[2], Number(0), a[4], Big(SecpOrder - s)); // the same signature with s flipped (y flipped too)
        RlpNode[] tuples =
        [
            Tuple(AuthA_D_8453_Nonce10),                                                                         // 0 applied, then superseded by 1
            Tuple(AuthA_Clear_8453_Nonce11),                                                                     // 1 applied: clears A
            Tuple(AuthA_D_Chain1_Nonce10),                                                                       // 2 another chain
            Tuple(AuthA_E_8453_Nonce11),                                                                         // 3 A is at 12 now
            Tuple(AuthB_D_8453_Nonce0),                                                                          // 4 B has contract code
            Tuple(AuthA_D_8453_Nonce9),                                                                          // 5 stale nonce
            highS,                                                                                               // 6 EIP-2 high s
            TupleOf(a[0], DelegateBytes, a[2], Number(2), a[4], a[5]),                                           // 7 y parity 2
            TupleOf(a[0], DelegateBytes, a[2], a[3], Number(0), a[5]),                                           // 8 r = 0
            TupleOf(a[0], DelegateBytes, a[2], a[3], Big(SecpOrder), a[5]),                                      // 9 r = n
            TupleOf(a[0], DelegateBytes, a[2], a[3], a[4], Number(0)),                                           // 10 s = 0
            TupleOf(a[0], DelegateBytes, RlpNode.Bytes(Filled(0xff)[..8]), a[3], a[4], a[5]),                    // 11 nonce 2^64 - 1
            TupleOf(a[0], DelegateBytes, a[2], a[3], Big(OffCurveX()), a[5]),                                    // 12 no curve point
            TupleOf(Big(BigInteger.One << 64), DelegateBytes, a[2], a[3], a[4], a[5]),                           // 13 a chain id above any transaction's
        ];
        var state = new ChainState();
        state.Code[AuthorityB] = EvmAccountCode.Contract(new Hash32(0, 0, 0, 0xbb));
        EvmAuthorization[] entries = Process(Envelope(AuthorityAEvm, BatchCall((TokenBytes, 0, TokenTransfer)), tuples), Relayer, state);

        EvmAuthorizationOutcome[] expected =
        [
            EvmAuthorizationOutcome.Superseded, EvmAuthorizationOutcome.Applied, EvmAuthorizationOutcome.WrongChain, EvmAuthorizationOutcome.NonceMismatch,
            EvmAuthorizationOutcome.AuthorityHasCode, EvmAuthorizationOutcome.NonceMismatch, EvmAuthorizationOutcome.InvalidSignature,
            EvmAuthorizationOutcome.InvalidSignature, EvmAuthorizationOutcome.InvalidSignature, EvmAuthorizationOutcome.InvalidSignature,
            EvmAuthorizationOutcome.InvalidSignature, EvmAuthorizationOutcome.NonceOutOfRange, EvmAuthorizationOutcome.NotRecoverable,
            EvmAuthorizationOutcome.WrongChain,
        ];
        CollectionAssert.AreEqual(expected, entries.Select(e => e.Outcome).ToArray());
        Assert.AreEqual(AuthorityA, entries[0].Authority, "a superseded tuple keeps its authority");
        CollectionAssert.AreEqual(new ulong[] { 10, 11, 0, 12, 0, 12 }, entries[..6].Select(e => e.AuthorityNonce).ToArray(), "the nonce each tuple was checked against");
        Assert.IsFalse(entries.Any(e => e.IsPending), "stale tuples (nonce below the authority's) can never apply");
        Assert.AreEqual(AuthorityA, entries[3].Authority);
        Assert.AreEqual(AuthorityB, entries[4].Authority);
        Assert.AreEqual(default, entries[2].Authority, "nothing is recovered for a tuple refused before recovery");
        Assert.AreEqual(6, state.SigningHashes.Count, "recovery runs only for tuples 0, 1, 3, 4, 5 and 12");
        CollectionAssert.AreEqual(new[] { AuthorityA }, state.NonceQueries, "A's nonce is read once; later tuples of A use the applied entry; B fails on its code first");
        Assert.AreEqual(1, entries.Count(e => e.Outcome == EvmAuthorizationOutcome.Applied && e.Authority == AuthorityA), "at most one applied entry per authority");

        // The list cleared A's delegation, so the call to A reaches no code.
        Assert.AreEqual(NestedCallStatus.NoCode, Walk(Envelope(AuthorityAEvm, BatchCall((TokenBytes, 0, TokenTransfer)), tuples), Relayer, state, entries).Status);
    }

    [TestMethod]
    public void TheSignatureBounds_AreExactly_OneToNMinusOne_ForR_AndOneToHalfN_ForS()
    {
        RlpNode[] a = FieldsOf(Tuple(AuthA_D_8453_Nonce10));
        BigInteger halfOrder = SecpOrder / 2;
        (RlpNode R, RlpNode S, bool InBounds)[] cases =
        [
            (Number(1), a[5], true), (Big(SecpOrder - 1), a[5], true), (Big(SecpOrder), a[5], false), (RlpNode.Bytes(Filled(0xff)), a[5], false),
            (a[4], Number(1), true), (a[4], Big(halfOrder), true), (a[4], Big(halfOrder + 1), false), (a[4], Big(SecpOrder - 1), false),
        ];
        foreach (var (r, s, inBounds) in cases)
        {
            var state = new ChainState { UnknownAccountsAreEmpty = true };
            EvmAuthorization entry = Process(Envelope(RouterBytes, TokenTransfer, TupleOf(a[0], DelegateBytes, a[2], a[3], r, s)), Relayer, state).Single();
            Assert.AreEqual(inBounds, state.SigningHashes.Count == 1, $"r {ToBig(r):x}, s {ToBig(s):x}: recovery attempted only inside the bounds");
            Assert.AreEqual(inBounds, entry.Outcome != EvmAuthorizationOutcome.InvalidSignature);
        }

        Assert.AreEqual(SecpOrder / 2, new BigInteger(Hex("7FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF5D576E7357A4501DDFE92F46681B20A0"), isUnsigned: true, isBigEndian: true), "the library's half order");
    }

    [TestMethod]
    public void APendingTuple_StopsTheWalk_BecauseTheAuthorityCanStillMakeItApply()
    {
        // Regression (independent review): A is pinned at nonce 9 and delegated to E; the tuple delegates A to D at nonce 10. Under the pinned state the
        // tuple is skipped and E's code would be walked, but if A's pending transaction lands first the tuple applies and D's code runs.
        byte[] envelope = Envelope(AuthorityAEvm, BatchCall((TokenBytes, 0, TokenTransfer)), Tuple(AuthA_D_8453_Nonce10));
        var state = new ChainState();
        state.Nonces[AuthorityA] = 9;
        state.Code[AuthorityA] = EvmAccountCode.DelegatedTo(OtherDelegate);
        EvmAuthorization entry = Only(envelope, Relayer, state);
        Assert.AreEqual(EvmAuthorizationOutcome.NonceMismatch, entry.Outcome);
        Assert.AreEqual(9UL, entry.AuthorityNonce);
        Assert.IsTrue(entry.IsPending);
        Assert.AreEqual(NestedCallStatus.UnsettledDelegation, Walk(envelope, Relayer, state, [entry]).Status);
        Assert.IsEmpty(state.Seen, "neither delegate's rules were used");

        // A pending tuple AFTER an applied one of the same authority (review finding): D applies at 10, E at 12 is pending (A would be at 11).
        byte[] appliedThenPending = Envelope(AuthorityAEvm, BatchCall((TokenBytes, 0, TokenTransfer)), Tuple(AuthA_D_8453_Nonce10), SignedTuple(ChainId, OtherDelegate, 12));
        var both = new ChainState();
        EvmAuthorization[] bothEntries = Process(appliedThenPending, Relayer, both);
        CollectionAssert.AreEqual(new[] { EvmAuthorizationOutcome.Applied, EvmAuthorizationOutcome.NonceMismatch }, bothEntries.Select(e => e.Outcome).ToArray());
        Assert.AreEqual(11UL, bothEntries[1].AuthorityNonce);
        Assert.IsTrue(bothEntries[1].IsPending);
        Assert.AreEqual(NestedCallStatus.UnsettledDelegation, Walk(appliedThenPending, Relayer, both, bothEntries).Status);

        // Reaching A as a child is refused the same way: B (delegated to D) runs a batch whose call targets A. The root is evaluated, the child is not.
        byte[] viaChild = Envelope(AuthorityB.ToBytes(), BatchCall((AuthorityAEvm, 0, BatchCall((TokenBytes, 0, TokenTransfer)))), Tuple(AuthA_D_8453_Nonce10));
        var childState = new ChainState();
        childState.Nonces[AuthorityA] = 9;
        childState.Code[AuthorityB] = EvmAccountCode.DelegatedTo(Delegate);
        NestedCallWalkResult child = Walk(viaChild, Relayer, childState, Process(viaChild, Relayer, childState));
        Assert.AreEqual(NestedCallStatus.UnsettledDelegation, child.Status);
        Assert.AreEqual(1, child.CallCount, "only B's batch was allowed before the walk reached A");

        // A walk that never reaches A is unaffected by A's pending tuple.
        byte[] elsewhere = Envelope(TokenBytes, TokenTransfer, Tuple(AuthA_D_8453_Nonce10));
        Assert.AreEqual(NestedCallStatus.Success, Walk(elsewhere, Relayer, state, Process(elsewhere, Relayer, state)).Status);

        // A stale tuple (nonce below the authority's) is not pending: the pinned delegation E is walked.
        var stale = new ChainState();
        stale.Nonces[AuthorityA] = 11;
        stale.Code[AuthorityA] = EvmAccountCode.DelegatedTo(OtherDelegate);
        EvmAuthorization staleEntry = Only(envelope, Relayer, stale);
        Assert.IsFalse(staleEntry.IsPending);
        Assert.AreEqual(NestedCallStatus.Success, Walk(envelope, Relayer, stale, [staleEntry]).Status);
        Assert.AreEqual(OtherDelegate, stale.Seen[0].CodeAddress);

        // The sender's nonce is fixed by the transaction, so a sender's tuple with a later nonce can never apply in it: not pending.
        byte[] selfEnvelope = Envelope(AuthorityAEvm, BatchCall((TokenBytes, 0, TokenTransfer)), SignedTuple(ChainId, Delegate, 11)); // tx nonce 9: needs 10
        var self = new ChainState();
        self.Code[AuthorityA] = EvmAccountCode.DelegatedTo(OtherDelegate);
        EvmAuthorization selfEntry = Only(selfEnvelope, AuthorityA, self);
        Assert.AreEqual(EvmAuthorizationOutcome.NonceMismatch, selfEntry.Outcome);
        Assert.AreEqual(10UL, selfEntry.AuthorityNonce);
        Assert.IsFalse(selfEntry.IsPending);
        Assert.AreEqual(NestedCallStatus.Success, Walk(selfEnvelope, AuthorityA, self, [selfEntry]).Status);
    }

    [TestMethod]
    public void EntriesProcessedForAnotherSenderNonceOrChain_AreRefused()
    {
        // Regression (independent review): the outcome of a tuple depends on the sender (its nonce is the transaction's plus one) and on the transaction's
        // nonce and chain; entries computed for other values are not this transaction's, even when every tuple matches.
        byte[] envelope = Hex(SelfSponsoredTransaction);
        var state = new ChainState();
        state.Nonces[AuthorityA] = 9;
        EvmAuthorization[] asRelayer = Process(envelope, Relayer, state);
        Assert.AreEqual(EvmAuthorizationOutcome.NonceMismatch, asRelayer.Single().Outcome);
        Assert.AreEqual(NestedCallStatus.AuthorizationsNotProcessed, Walk(envelope, AuthorityA, new ChainState(), asRelayer).Status, "processed for another sender");
        Assert.AreEqual(NestedCallStatus.Success, Walk(envelope, AuthorityA, new ChainState(), Process(envelope, AuthorityA, new ChainState())).Status);

        byte[] calldata = BatchCall((TokenBytes, 0, TokenTransfer));
        RlpNode[] fields = Fields7702(AuthorityAEvm, calldata);
        byte[] nonce9 = Encode(0x04, [.. fields[..9], RlpNode.ListOf(Tuple(AuthA_D_AnyChain_Nonce10)), .. fields[10..]]);
        byte[] nonce8 = Encode(0x04, [fields[0], Number(8), .. fields[2..9], RlpNode.ListOf(Tuple(AuthA_D_AnyChain_Nonce10)), .. fields[10..]]);
        byte[] otherChain = Encode(0x04, [Number(1), .. fields[1..9], RlpNode.ListOf(Tuple(AuthA_D_AnyChain_Nonce10)), .. fields[10..]]);
        EvmAuthorization[] forNonce8 = Process(nonce8, Relayer, new ChainState());
        Assert.AreEqual(NestedCallStatus.AuthorizationsNotProcessed, Walk(nonce9, Relayer, new ChainState(), forNonce8).Status, "processed for another transaction nonce");
        var chainOne = new ChainState { ExpectedChain = 1 };
        EvmAuthorization[] forChain1 = Process(otherChain, Relayer, chainOne);
        Assert.AreEqual(NestedCallStatus.AuthorizationsNotProcessed, Walk(nonce9, Relayer, new ChainState(), forChain1).Status, "processed for another chain");
        Assert.AreEqual(NestedCallStatus.Success, Walk(nonce9, Relayer, new ChainState()).Status);
    }

    [TestMethod]
    public void AChainZeroTuple_AppliesOnAnyChain_AndTheAuthorityMayDelegateSeveralTimesInOneList()
    {
        var state = new ChainState();
        Assert.AreEqual(EvmAuthorizationOutcome.Applied, Only(Envelope(RouterBytes, TokenTransfer, Tuple(AuthA_D_AnyChain_Nonce10)), Relayer, state).Outcome);

        // 10 then 11: both valid, the later one wins and the earlier is superseded; the walk then sees E.
        EvmAuthorization[] entries = Process(Envelope(AuthorityAEvm, BatchCall((TokenBytes, 0, TokenTransfer)), Tuple(AuthA_D_8453_Nonce10), Tuple(AuthA_E_8453_Nonce11)), Relayer, new ChainState());
        CollectionAssert.AreEqual(new[] { EvmAuthorizationOutcome.Superseded, EvmAuthorizationOutcome.Applied }, entries.Select(e => e.Outcome).ToArray());
        var walkState = new ChainState();
        Assert.AreEqual(NestedCallStatus.Success, Walk(Envelope(AuthorityAEvm, BatchCall((TokenBytes, 0, TokenTransfer)), Tuple(AuthA_D_8453_Nonce10), Tuple(AuthA_E_8453_Nonce11)), Relayer, walkState, entries).Status);
        Assert.AreEqual(OtherDelegate, walkState.Seen[0].CodeAddress, "the last valid tuple sets the code");

        // In the other order the second is stale: D stays.
        entries = Process(Envelope(RouterBytes, TokenTransfer, Tuple(AuthA_E_8453_Nonce11), Tuple(AuthA_D_8453_Nonce10)), Relayer, new ChainState());
        CollectionAssert.AreEqual(new[] { EvmAuthorizationOutcome.NonceMismatch, EvmAuthorizationOutcome.Applied }, entries.Select(e => e.Outcome).ToArray());
    }

    [TestMethod]
    public void AnAlreadyDelegatedAuthority_CanBeRedelegated_ButOneWithContractCodeCannot()
    {
        var state = new ChainState();
        state.Code[AuthorityA] = EvmAccountCode.DelegatedTo(OtherDelegate);
        Assert.AreEqual(EvmAuthorizationOutcome.Applied, Only(Envelope(RouterBytes, TokenTransfer, Tuple(AuthA_D_8453_Nonce10)), Relayer, state).Outcome);

        state = new ChainState();
        state.Code[AuthorityA] = EvmAccountCode.Contract(new Hash32(1, 2, 3, 4));
        EvmAuthorization entry = Only(Envelope(RouterBytes, TokenTransfer, Tuple(AuthA_D_8453_Nonce10)), Relayer, state);
        Assert.AreEqual(EvmAuthorizationOutcome.AuthorityHasCode, entry.Outcome);
        Assert.IsTrue(entry.IsRecovered, "the policy still sees who signed it");
        Assert.IsEmpty(state.NonceQueries, "the code check comes first");
    }

    // ------------------------------------------------------------------ fail closed

    [TestMethod]
    public void HostFailures_StopTheProcessing_AndLeaveNothingBehind()
    {
        byte[] envelope = Envelope(RouterBytes, TokenTransfer, Tuple(AuthA_D_8453_Nonce10));
        (Action<ChainState> Break, EvmAuthorizationStatus Expected, string Why)[] cases =
        [
            (s => s.ForcedRecovery = EvmAuthorityRecovery.Unavailable, EvmAuthorizationStatus.RecoveryUnavailable, "the host cannot recover"),
            (s => s.ForcedRecovery = (EvmAuthorityRecovery)99, EvmAuthorizationStatus.RecoveryUnavailable, "an undefined answer"),
            (s => { s.ForcedRecovery = EvmAuthorityRecovery.Recovered; s.ForcedAuthority = Address.ZeroSolana; }, EvmAuthorizationStatus.RecoveryUnavailable, "a non-EVM authority"),
            (s => s.Code.Remove(AuthorityA), EvmAuthorizationStatus.AccountStateUnavailable, "no code answer"),
            (s => s.Code[AuthorityA] = default, EvmAuthorizationStatus.AccountStateUnavailable, "an Unknown code answer"),
            (s => s.Nonces.Remove(AuthorityA), EvmAuthorizationStatus.AccountStateUnavailable, "no nonce answer"),
        ];
        foreach (var (breakIt, expected, why) in cases)
        {
            var state = new ChainState();
            breakIt(state);
            Process(envelope, Relayer, state, out EvmAuthorizationStatus status);
            Assert.AreEqual(expected, status, why);
        }

        // NotRecoverable is an answer, not a failure: the tuple is skipped as the EIP says.
        var skipped = new ChainState { ForcedRecovery = EvmAuthorityRecovery.NotRecoverable };
        Assert.AreEqual(EvmAuthorizationOutcome.NotRecoverable, Only(envelope, Relayer, skipped).Outcome);
    }

    [TestMethod]
    public void ArgumentFailures_AndOtherTransactionTypes()
    {
        var state = new ChainState();
        EvmTransactionView none = default;
        Assert.IsFalse(EvmAuthorizationProcessor.TryProcess(in none, Relayer, ref state, new EvmAuthorization[4], out int written, out EvmAuthorizationStatus status));
        Assert.AreEqual(EvmAuthorizationStatus.InvalidArgument, status);
        Assert.AreEqual(0, written);

        var limits = RLPDecodeLimits.Default;
        byte[] envelope = Envelope(RouterBytes, TokenTransfer, Tuple(AuthA_D_8453_Nonce10), Tuple(AuthB_D_8453_Nonce0));
        Assert.IsTrue(RLPCalldataAdapter.TryRead(envelope, EvmTransactionForm.SignedTransaction, in limits, out EvmTransactionView view, out _));
        Assert.IsFalse(EvmAuthorizationProcessor.TryProcess(in view, Address.ZeroSolana, ref state, new EvmAuthorization[4], out _, out status));
        Assert.AreEqual(EvmAuthorizationStatus.InvalidArgument, status, "a non-EVM sender");
        EvmAuthorization[] small = new EvmAuthorization[1];
        Assert.IsFalse(EvmAuthorizationProcessor.TryProcess(in view, Relayer, ref state, small, out _, out status));
        Assert.AreEqual(EvmAuthorizationStatus.DestinationTooSmall, status);
        Assert.IsEmpty(state.SigningHashes, "refused before any work");

        byte[] type2 = Typed1559(RouterBytes, TokenTransfer);
        Assert.IsTrue(RLPCalldataAdapter.TryRead(type2, EvmTransactionForm.SignedTransaction, in limits, out EvmTransactionView other, out _));
        Assert.IsTrue(EvmAuthorizationProcessor.TryProcess(in other, Relayer, ref state, Span<EvmAuthorization>.Empty, out written, out status));
        Assert.AreEqual(0, written, "no list, nothing to do");
    }

    // ------------------------------------------------------------------ the walker with delegations

    [TestMethod]
    public void AnExistingDelegation_IsFollowed_ForAnyTransactionType_AndCallAndDelegateCallKeepTheirContexts()
    {
        // A was delegated by an earlier transaction: a plain EIP-1559 call to A runs D's code in A's context. D's multicall delegatecalls itself (address(this)
        // = A), which follows A's delegation again: D's transfer, still in A's context, with the relayer as the sender.
        var state = new ChainState();
        state.Code[AuthorityA] = EvmAccountCode.DelegatedTo(Delegate);
        byte[] calldata = MulticallOf(TransferCalldata(5), BatchCall((TokenBytes, 0, TokenTransfer)));
        var limits = RLPDecodeLimits.Default;
        byte[] envelope = Typed1559(AuthorityAEvm, calldata);
        Assert.IsTrue(RLPCalldataAdapter.TryRead(envelope, EvmTransactionForm.SignedTransaction, in limits, out EvmTransactionView view, out _));
        Assert.IsTrue(Walker().TryWalk(in view, Relayer, ref state, out NestedCallWalkResult result), result.Status.ToString());
        Assert.AreEqual(4, result.CallCount);

        (Address Target, Address Code, Address Context, Address Sender, NestedCallMode Mode)[] expected =
        [
            (AuthorityA, Delegate, AuthorityA, Relayer, NestedCallMode.Call),          // multicall
            (AuthorityA, Delegate, AuthorityA, Relayer, NestedCallMode.DelegateCall),  // transfer, delegatecalled on self
            (AuthorityA, Delegate, AuthorityA, Relayer, NestedCallMode.DelegateCall),  // executeBatch, delegatecalled on self
            (Token, Token, Token, AuthorityA, NestedCallMode.Call),                    // the batch's call to the token
        ];
        for (int i = 0; i < expected.Length; i++)
        {
            NestedCall call = state.Seen[i];
            Assert.AreEqual(expected[i], (call.TargetAddress, call.CodeAddress, call.ContextAddress, call.Sender, call.Mode), $"call {i}");
        }
    }

    [TestMethod]
    public void TheTransactionsOwnList_OverridesThePinnedState()
    {
        byte[] calldata = BatchCall((TokenBytes, 0, TokenTransfer));

        // A was delegated to E; A itself sends the transaction (nonce 9) and re-delegates to D at nonce 10: the transaction fixes A's nonce, so D's code runs.
        var state = new ChainState();
        state.Code[AuthorityA] = EvmAccountCode.DelegatedTo(OtherDelegate);
        Assert.AreEqual(NestedCallStatus.Success, Walk(Envelope(AuthorityAEvm, calldata, Tuple(AuthA_D_8453_Nonce10)), AuthorityA, state).Status);
        Assert.AreEqual(Delegate, state.Seen[0].CodeAddress);

        // The same re-delegation sponsored by a relayer is not settled: if A spends nonce 10 first, the tuple fails and E runs instead of D (review
        // finding, row 84). Refused.
        state = new ChainState();
        state.Code[AuthorityA] = EvmAccountCode.DelegatedTo(OtherDelegate);
        Assert.AreEqual(NestedCallStatus.UnsettledDelegation, Walk(Envelope(AuthorityAEvm, calldata, Tuple(AuthA_D_8453_Nonce10)), Relayer, state).Status);
        Assert.IsEmpty(state.Seen);

        // Sponsored, but the fallback is harmless: re-delegating to the delegate A already has, or a first delegation (A has no code: nothing would run).
        state = new ChainState();
        state.Code[AuthorityA] = EvmAccountCode.DelegatedTo(Delegate);
        Assert.AreEqual(NestedCallStatus.Success, Walk(Envelope(AuthorityAEvm, calldata, Tuple(AuthA_D_8453_Nonce10)), Relayer, state).Status);
        Assert.AreEqual(NestedCallStatus.Success, Walk(Envelope(AuthorityAEvm, calldata, Tuple(AuthA_D_8453_Nonce10)), Relayer, new ChainState()).Status);

        // A was delegated to D; A's own transaction clears it: no code.
        state = new ChainState();
        state.Code[AuthorityA] = EvmAccountCode.DelegatedTo(Delegate);
        Assert.AreEqual(NestedCallStatus.NoCode, Walk(Envelope(AuthorityAEvm, calldata, Tuple(AuthA_D_8453_Nonce10), Tuple(AuthA_Clear_8453_Nonce11)), AuthorityA, state).Status);

        // An applied tuple of an authority the host cannot report fails closed.
        state = new ChainState();
        EvmAuthorization[] entries = Process(Envelope(AuthorityAEvm, calldata, Tuple(AuthA_D_8453_Nonce10)), Relayer, state);
        state.Code.Remove(AuthorityA);
        Assert.AreEqual(NestedCallStatus.CodeIdentityUnavailable, Walk(Envelope(AuthorityAEvm, calldata, Tuple(AuthA_D_8453_Nonce10)), Relayer, state, entries).Status);

        // A tuple that does not apply leaves the pinned state in force: A (no code) stays without code.
        state = new ChainState();
        Assert.AreEqual(NestedCallStatus.NoCode, Walk(Envelope(AuthorityAEvm, calldata, Tuple(AuthA_D_8453_Nonce9)), Relayer, state).Status);
        Assert.IsEmpty(state.Seen);
    }

    [TestMethod]
    public void DelegationsFollowOneHopOnly_AndCodelessTargetsResolveNothing()
    {
        byte[] calldata = BatchCall((TokenBytes, 0, TokenTransfer));
        byte[] envelope = Envelope(AuthorityAEvm, calldata, Tuple(AuthA_D_8453_Nonce10));

        var chained = new ChainState();
        chained.Code[Delegate] = EvmAccountCode.DelegatedTo(OtherDelegate); // the delegate is itself delegated: the EVM would run the indicator bytes
        Assert.AreEqual(NestedCallStatus.ChainedDelegation, Walk(envelope, Relayer, chained).Status);

        // Both in one list: A delegates to B (signed by Nethereum) and B delegates to D (signed by cast).
        RlpNode aToB = SignedTuple(ChainId, AuthorityB, 10);
        byte[] bothInList = Envelope(AuthorityAEvm, calldata, aToB, Tuple(AuthB_D_8453_Nonce0));
        EvmAuthorization[] both = Process(bothInList, Relayer, new ChainState());
        CollectionAssert.AreEqual(new[] { EvmAuthorizationOutcome.Applied, EvmAuthorizationOutcome.Applied }, both.Select(e => e.Outcome).ToArray());
        Assert.AreEqual(NestedCallStatus.ChainedDelegation, Walk(bothInList, Relayer, new ChainState(), both).Status);

        // Without B's tuple, A's delegate B has no code: nothing runs.
        Assert.AreEqual(NestedCallStatus.NoCode, Walk(Envelope(AuthorityAEvm, calldata, aToB), Relayer, new ChainState()).Status);

        var empty = new ChainState();
        empty.Code[Delegate] = EvmAccountCode.Empty; // a delegate without code (or a precompile) runs nothing
        Assert.AreEqual(NestedCallStatus.NoCode, Walk(envelope, Relayer, empty).Status);

        var unknown = new ChainState();
        unknown.Code.Remove(Delegate);
        Assert.AreEqual(NestedCallStatus.CodeIdentityUnavailable, Walk(envelope, Relayer, unknown).Status);

        var unknownKind = new ChainState();
        unknownKind.Code[Token] = default; // the host answered true with nothing
        Assert.AreEqual(NestedCallStatus.CodeIdentityUnavailable, Walk(envelope, Relayer, unknownKind).Status, "the child's code is Unknown");
    }

    [TestMethod]
    public void ADelegationToUnregisteredCode_CannotAuthorise()
    {
        // Spec 10.5 style: the list delegates A to a contract nobody registered; the call to A then runs unknown code.
        byte[] evilTuple = RlpReference.Encode(TupleOf(Number(ChainId), Evil.ToBytes(), Number(10), Number(0), Number(1), Number(1)));
        var state = new ChainState { ForcedRecovery = EvmAuthorityRecovery.Recovered, ForcedAuthority = AuthorityA };
        byte[] envelope = Encode(0x04, [.. Fields7702(AuthorityAEvm, BatchCall((TokenBytes, 0, TokenTransfer)))[..9], RlpNode.ListOf(Tuple(Convert.ToHexString(evilTuple))),
            .. Fields7702(RouterBytes, TokenTransfer)[10..]]);
        EvmAuthorization entry = Only(envelope, Relayer, state);
        Assert.AreEqual(EvmAuthorizationOutcome.Applied, entry.Outcome);
        Assert.AreEqual(Evil, entry.DelegateAddress, "the policy sees the delegation the transaction asks for");
        Assert.AreEqual(NestedCallStatus.UnknownContract, Walk(envelope, Relayer, state, [entry]).Status);
        Assert.IsEmpty(state.Seen, "the policy is never asked to approve unknown code");
    }

    [TestMethod]
    public void TheWalk_RefusesAuthorizationsThatAreNotThisEnvelopesProcessedList()
    {
        byte[] calldata = BatchCall((TokenBytes, 0, TokenTransfer));
        byte[] envelope = Envelope(AuthorityAEvm, calldata, Tuple(AuthA_D_8453_Nonce10));
        byte[] otherEnvelope = Envelope(AuthorityAEvm, calldata, Tuple(AuthA_E_8453_Nonce11));
        EvmAuthorization[] mine = Process(envelope, Relayer, new ChainState());
        EvmAuthorization[] theirs = Process(otherEnvelope, Relayer, new ChainState { Nonces = { [AuthorityA] = 11 } });
        EvmAuthorization[] two = Process(Envelope(AuthorityAEvm, calldata, Tuple(AuthA_D_8453_Nonce10), Tuple(AuthB_D_8453_Nonce0)), Relayer, new ChainState());
        Assert.AreEqual(EvmAuthorizationOutcome.Applied, theirs.Single().Outcome);

        (EvmAuthorization[]? Entries, string Why)[] wrong =
        [
            ([], "none, for an EIP-7702 transaction"),
            (theirs, "another envelope's processed list"),
            (two, "the wrong count"),
            ([default], "an unprocessed entry"),
            ([mine[0].WithOutcome(default, EvmAuthorizationOutcome.None)], "an entry reset to no outcome"),
        ];
        foreach (var (entries, why) in wrong)
        {
            var state = new ChainState();
            NestedCallWalkResult result = Walk(envelope, Relayer, state, entries);
            Assert.AreEqual(NestedCallStatus.AuthorizationsNotProcessed, result.Status, why);
            Assert.IsEmpty(state.Seen, why);
        }

        Assert.AreEqual(NestedCallStatus.Success, Walk(envelope, Relayer, new ChainState(), mine).Status);

        // The first entry of a longer list holding the same first tuple is accepted: an outcome depends only on the tuples before it and the state,
        // and those are identical, so it is the same processing.
        Assert.AreEqual(NestedCallStatus.Success, Walk(envelope, Relayer, new ChainState(), two[..1]).Status);

        // The overload without authorizations refuses an EIP-7702 transaction; any entry passed for another type is refused too.
        var limits = RLPDecodeLimits.Default;
        Assert.IsTrue(RLPCalldataAdapter.TryRead(envelope, EvmTransactionForm.SignedTransaction, in limits, out EvmTransactionView view, out _));
        var host = new ChainState();
        Assert.IsFalse(Walker().TryWalk(in view, Relayer, ref host, out NestedCallWalkResult refused));
        Assert.AreEqual(NestedCallStatus.AuthorizationsNotProcessed, refused.Status);

        byte[] type2 = Typed1559(TokenBytes, TokenTransfer);
        Assert.IsTrue(RLPCalldataAdapter.TryRead(type2, EvmTransactionForm.SignedTransaction, in limits, out EvmTransactionView plain, out _));
        Assert.IsFalse(Walker().TryWalk(in plain, Relayer, mine, ref host, out NestedCallWalkResult extra));
        Assert.AreEqual(NestedCallStatus.AuthorizationsNotProcessed, extra.Status);
        Assert.IsTrue(Walker().TryWalk(in plain, Relayer, [], ref host, out NestedCallWalkResult fine), fine.Status.ToString());
    }

    // ------------------------------------------------------------------ allocation and mutation

    /// <summary>An allocation-free host: one known recovery (signing hash to authority), fixed state, every call allowed.</summary>
    private struct TableHost(Hash32 knownHash, Address authority) : INestedCallHost, IEvmAuthorizationHost
    {
        public int Calls;

        public readonly EvmAuthorityRecovery RecoverAuthority(in Hash32 signingHash, byte yParity, in uint256 r, in uint256 s, out Address recovered)
        {
            recovered = signingHash == knownHash ? authority : default;
            return signingHash == knownHash ? EvmAuthorityRecovery.Recovered : EvmAuthorityRecovery.NotRecoverable;
        }

        public readonly bool TryGetNonce(ulong chainId, in Address account, out ulong nonce)
        {
            nonce = 10;
            return true;
        }

        public readonly bool TryGetCode(ulong chainId, in Address account, out EvmAccountCode code)
        {
            code = account == Delegate ? EvmAccountCode.Contract(DelegateCode) : account == Token ? EvmAccountCode.Contract(TokenCode) : EvmAccountCode.Empty;
            return true;
        }

        public bool Allows(in NestedCall nestedCall, scoped in ABICallData calldata)
        {
            Calls++;
            return true;
        }
    }

    [TestMethod]
    public void ProcessingAndWalking_AllocateNothing_WithAStructHostAndAStackDestination()
    {
#if DEBUG
        // Keccak256 keeps its round constants as static span data; unoptimised (Debug) code allocates a small runtime object per hash to reach it
        // (measured: 72 bytes per Keccak256.ComputeHash call), optimised code does not. The claim is for Release, which CI and packages use.
        Assert.Inconclusive("Allocation-free only in optimised code: Keccak256's static span data allocates per call in Debug builds.");
#endif
        byte[] envelope = Hex(SponsoredTransaction);
        var recorder = new ChainState();
        _ = Only(envelope, Relayer, recorder);
        var host = new TableHost(recorder.SigningHashes.Single(), AuthorityA);
        NestedCallWalker walker = Walker();
        var limits = RLPDecodeLimits.Default;
        int successes = 0;
        void Run()
        {
            RLPCalldataAdapter.TryRead(envelope, EvmTransactionForm.SignedTransaction, in limits, out EvmTransactionView view, out _);
            Span<EvmAuthorization> entries = stackalloc EvmAuthorization[4];
            EvmAuthorizationProcessor.TryProcess(in view, Relayer, ref host, entries, out int written, out _);
            successes += walker.TryWalk(in view, Relayer, entries[..written], ref host, out _) ? 1 : 0;
        }

        for (int i = 0; i < 50; i++)
        {
            Run();
        }

        long best = long.MaxValue;
        for (int trial = 0; trial < 5; trial++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                Run();
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.AreEqual(0, best);
        Assert.AreEqual(550, successes);
        Assert.AreEqual(1100, host.Calls, "two calls per walk");
    }

    [TestMethod]
    public void MutatedEip7702Envelopes_NeverThrow_AndEverySuccessIsConsistent()
    {
        byte[] original = Hex(SponsoredTransaction);
        var limits = RLPDecodeLimits.Default;
        var rng = new Random(7702);
        int read = 0, processed = 0, walked = 0;
        for (int round = 0; round < 6000; round++)
        {
            byte[] mutant = (byte[])original.Clone();
            int edits = 1 + rng.Next(3);
            for (int e = 0; e < edits; e++)
            {
                int at = rng.Next(mutant.Length);
                mutant[at] = rng.Next(4) == 0 ? (byte)rng.Next(256) : (byte)(mutant[at] ^ (1 << rng.Next(8)));
            }

            if (!RLPCalldataAdapter.TryRead(mutant, EvmTransactionForm.SignedTransaction, in limits, out EvmTransactionView view, out _))
            {
                continue;
            }

            read++;
            if (view.Type != EvmTransactionType.Eip7702)
            {
                continue;
            }

            Assert.IsGreaterThan(0, view.AuthorizationCount, "the adapter never yields an empty list");
            var state = new ChainState { UnknownAccountsAreEmpty = true, ExpectedChain = view.ChainId!.Value };
            var entries = new EvmAuthorization[view.AuthorizationCount];
            if (!EvmAuthorizationProcessor.TryProcess(in view, Relayer, ref state, entries, out int written, out EvmAuthorizationStatus status))
            {
                Assert.Fail($"round {round}: a valid view must process with a total host, got {status}");
            }

            processed++;
            Assert.AreEqual(view.AuthorizationCount, written);
            Assert.IsTrue(entries.All(e => e.Outcome != EvmAuthorizationOutcome.None));
            Assert.IsLessThanOrEqualTo(1, entries.Count(e => e.Outcome == EvmAuthorizationOutcome.Applied && e.Authority == AuthorityA));
            if (Walker().TryWalk(in view, Relayer, entries, ref state, out NestedCallWalkResult result))
            {
                walked++;
                Assert.AreEqual(state.Seen.Count, result.CallCount);
                Assert.IsTrue(state.Seen.All(c => c.CodeAddress == Delegate || c.CodeAddress == Token || c.CodeAddress == OtherDelegate));
            }
        }

        Assert.IsGreaterThan(50, read, "the mutation keeps some envelopes valid");
        Assert.IsGreaterThan(20, processed);
        Assert.IsGreaterThan(0, walked);
    }
}
