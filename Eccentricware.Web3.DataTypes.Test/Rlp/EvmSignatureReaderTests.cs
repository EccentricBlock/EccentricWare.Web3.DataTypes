using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.EvmSignatureOracle;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.TypedAdapterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// The two EVM signature readers: <see cref="RLPTypedExtensions.TryGetEvmLegacySignature"/> (chain id and parity from the
/// EIP-155 <c>v</c>) and <see cref="RLPTypedExtensions.TryGetEvmYParitySignature"/> (typed transactions). Expected values
/// come from <see cref="EvmSignatureOracle"/> and hand-written wire bytes, never from the adapters.
/// </summary>
[TestClass]
public sealed class EvmSignatureReaderTests
{
    private static readonly byte[] SampleR = Pattern(32, 0x7f);
    private static readonly byte[] SampleS = Pattern(31, 0x55);

    private enum FieldShape
    {
        Good,
        Default,
        List,
        LeadingZero,
        TooWide,
    }

    /// <summary>Statuses of each bad shape; <see cref="FieldShape.TooWide"/> is 40 bytes, past the width of every field.</summary>
    private static RLPStatus StatusOf(FieldShape shape) => shape switch
    {
        FieldShape.Default => RLPStatus.InvalidArgument,
        FieldShape.List => RLPStatus.WrongKind,
        FieldShape.LeadingZero => RLPStatus.NonCanonicalInteger,
        FieldShape.TooWide => RLPStatus.IntegerOverflow,
        _ => RLPStatus.Success,
    };

    private static RLPData Shape(FieldShape shape, byte[] goodPayload) => shape switch
    {
        FieldShape.Default => default,
        FieldShape.List => HexItem("c0"),
        FieldShape.LeadingZero => StringItem([0x00, 0x01]),
        FieldShape.TooWide => StringItem(Pattern(40, 0x01)),
        _ => StringItem(goodPayload),
    };

    /// <summary>Sentinels that a failing reader must overwrite with default / null.</summary>
    private static Signature NonDefaultSignature() => Signature.FromEvmComponents(Padded32(1), Padded32(2), 1);

    private static RLPStatus ReadLegacy(byte[] v, byte[] r, byte[] s, out Signature signature, out ulong? chainId)
    {
        signature = NonDefaultSignature();
        chainId = 99;
        return RLPTypedExtensions.TryGetEvmLegacySignature(StringItem(v), StringItem(r), StringItem(s), out signature, out chainId);
    }

    private static RLPStatus ReadTyped(byte[] yParity, byte[] r, byte[] s, out Signature signature)
    {
        signature = NonDefaultSignature();
        return RLPTypedExtensions.TryGetEvmYParitySignature(StringItem(yParity), StringItem(r), StringItem(s), out signature);
    }

    private static void AssertLegacyFailed(RLPStatus expected, byte[] v, string context)
    {
        Assert.AreEqual(expected, ReadLegacy(v, SampleR, SampleS, out var signature, out var chainId), context);
        Assert.AreEqual(default, signature, $"{context}: signature is default");
        Assert.IsNull(chainId, $"{context}: chain id is null");
    }

    // ------------------------------------------------------------------ legacy v: real chain ids

    [TestMethod]
    public void Legacy_RealChainIds_BothParities_ReturnTheChainIdAndTheParity()
    {
        foreach (var (name, chain) in RealChainIds)
        {
            foreach (var parity in new[] { 0, 1 })
            {
                var v = Minimal(LegacyV(parity, chain));
                var context = $"{name} ({chain}) parity {parity} v {Convert.ToHexString(v)}";

                Assert.AreEqual(RLPStatus.Success, ReadLegacy(v, SampleR, SampleS, out var signature, out var chainId), context);
                Assert.AreEqual(chain, chainId, context);
                Assert.AreEqual((byte)parity, signature.V, context);
                Assert.AreEqual(SignatureType.Evm, signature.Type, context);
                Assert.AreEqual(new Hash32(Padded32(new System.Numerics.BigInteger(SampleR, isUnsigned: true, isBigEndian: true))), signature.R, context);
                Assert.AreEqual(new Hash32(Padded32(new System.Numerics.BigInteger(SampleS, isUnsigned: true, isBigEndian: true))), signature.S, context);
            }
        }
    }

    [TestMethod]
    public void Legacy_HandWrittenWireVectors_ReadAsExpected()
    {
        // chain id, parity, v wire item (the item is the byte string; hex is the whole item).
        var rows = new (ulong? Chain, byte Parity, string ItemHex)[]
        {
            (null, 0, "1b"),                                   // 27
            (null, 1, "1c"),                                   // 28
            (0, 0, "23"),                                      // 35
            (0, 1, "24"),                                      // 36
            (1, 0, "25"),                                      // 37, the EIP-155 example
            (1, 1, "26"),                                      // 38
            (46, 0, "7f"),                                     // 127, the last literal byte
            (46, 1, "81 80"),                                  // 128
            (47, 0, "81 81"),
            (110, 0, "81 ff"),                                 // 255, the old byte-range limit
            (110, 1, "82 0100"),                               // 256, rejected by the replaced adapter
            (137, 0, "82 0135"),                               // Polygon
            (137, 1, "82 0136"),
            (8453, 0, "82 422d"),                              // Base
            (8453, 1, "82 422e"),
            (42161, 0, "83 014985"),                           // Arbitrum One
            (42161, 1, "83 014986"),
            (98866, 0, "83 030487"),                           // Plume = 0x018232, v = 197767
            (98866, 1, "83 030488"),
            (4294967295UL, 0, "85 0200000021"),                // 2 * (2^32 - 1) + 35 = 2^33 + 33
            (ulong.MaxValue, 0, "89 020000000000000021"),      // 2^65 + 33, nine bytes
            (ulong.MaxValue, 1, "89 020000000000000022"),
        };

        foreach (var (chain, parity, itemHex) in rows)
        {
            var item = HexItem(itemHex);
            var context = $"chain {chain?.ToString() ?? "none"} parity {parity} item {itemHex}";
            Assert.AreEqual(RLPStatus.Success, RLPTypedExtensions.TryGetEvmLegacySignature(item, StringItem(SampleR), StringItem(SampleS), out var signature, out var chainId), context);
            Assert.AreEqual(chain, chainId, context);
            Assert.AreEqual(parity, signature.V, context);

            // The oracle agrees with the hand-written bytes (guards the table itself).
            CollectionAssert.AreEqual(H(itemHex), LegacyVItem(parity, chain), context);
        }
    }

    [TestMethod]
    public void Legacy_ChainIdZero_IsAValidProtectedChainId_AndDiffersFromNoChainId()
    {
        Assert.AreEqual(RLPStatus.Success, ReadLegacy([0x23], SampleR, SampleS, out _, out var zero));
        Assert.AreEqual(0UL, zero);
        Assert.IsTrue(zero.HasValue);

        Assert.AreEqual(RLPStatus.Success, ReadLegacy([0x1b], SampleR, SampleS, out _, out var none));
        Assert.IsFalse(none.HasValue);
    }

    [TestMethod]
    public void Legacy_EveryWidthBoundaryChainId_BothParities_MatchTheOracle()
    {
        foreach (var chain in WidthBoundaryChainIds())
        {
            foreach (var parity in new[] { 0, 1 })
            {
                var v = Minimal(LegacyV(parity, chain));
                var context = $"chain {chain} parity {parity}";
                Assert.AreEqual(RLPStatus.Success, ReadLegacy(v, SampleR, SampleS, out var signature, out var chainId), context);
                Assert.AreEqual(chain, chainId, context);
                Assert.AreEqual((byte)parity, signature.V, context);
            }
        }
    }

    // ------------------------------------------------------------------ legacy v: domain, width and canonical rules

    [TestMethod]
    public void Legacy_ValuesBelow35ExceptTwentySevenAndTwentyEight_AreInvalidSignatureV()
    {
        foreach (var value in new[] { 0, 1, 2, 3, 26, 29, 30, 33, 34 })
        {
            AssertLegacyFailed(RLPStatus.InvalidSignatureV, Minimal(value), $"v {value}");
        }

        AssertLegacyFailed(RLPStatus.InvalidSignatureV, [], "the empty string is v = 0");
    }

    [TestMethod]
    public void Legacy_TwentySevenTwentyEightAndThirtyFiveUp_AreTheOnlyAcceptedValues()
    {
        var accepted = new List<int>();
        for (var value = 0; value <= 400; value++)
        {
            if (ReadLegacy(Minimal(value), SampleR, SampleS, out _, out _) == RLPStatus.Success)
            {
                accepted.Add(value);
            }
        }

        var expected = new List<int> { 27, 28 };
        expected.AddRange(Enumerable.Range(35, 400 - 35 + 1));
        CollectionAssert.AreEqual(expected, accepted);
    }

    [TestMethod]
    public void Legacy_LeadingZeroPayload_IsNonCanonicalInteger_BeforeTheWidthRule()
    {
        AssertLegacyFailed(RLPStatus.NonCanonicalInteger, [0x00], "raw 00");
        AssertLegacyFailed(RLPStatus.NonCanonicalInteger, [0x00, 0x1b], "00 1b");
        AssertLegacyFailed(RLPStatus.NonCanonicalInteger, [0x00, 0x25], "00 25");
        // Nine bytes with a leading zero (would be a valid 8-byte value) and wider than the 9-byte limit.
        AssertLegacyFailed(RLPStatus.NonCanonicalInteger, Pattern(9, 0), "9 bytes, leading 00");
        AssertLegacyFailed(RLPStatus.NonCanonicalInteger, Pattern(10, 0), "10 bytes, leading 00");
        AssertLegacyFailed(RLPStatus.NonCanonicalInteger, Pattern(40, 0), "40 bytes, leading 00");
    }

    [TestMethod]
    public void Legacy_ChainIdAboveUInt64_IsIntegerOverflow()
    {
        // 2^65 + 35 is chain id 2^64 (ulong.MaxValue + 1), the first value that does not fit.
        var firstOverflow = Minimal((System.Numerics.BigInteger.One << 65) + 35);
        Assert.HasCount(9, firstOverflow);
        AssertLegacyFailed(RLPStatus.IntegerOverflow, firstOverflow, "chain ulong.MaxValue + 1, parity 0");
        AssertLegacyFailed(RLPStatus.IntegerOverflow, Minimal((System.Numerics.BigInteger.One << 65) + 36), "chain ulong.MaxValue + 1, parity 1");
        AssertLegacyFailed(RLPStatus.IntegerOverflow, Minimal((System.Numerics.BigInteger.One << 66) + 35), "chain 2^65");
        AssertLegacyFailed(RLPStatus.IntegerOverflow, Repeat(0xff, 9), "nine 0xff bytes");

        // One below the first overflow still reads (chain ulong.MaxValue, parity 1).
        Assert.AreEqual(RLPStatus.Success, ReadLegacy(Minimal((System.Numerics.BigInteger.One << 65) + 34), SampleR, SampleS, out _, out var chainId));
        Assert.AreEqual(ulong.MaxValue, chainId);
    }

    [TestMethod]
    public void Legacy_MoreThanNineBytes_IsIntegerOverflow()
    {
        AssertLegacyFailed(RLPStatus.IntegerOverflow, Pattern(10, 0x01), "10 bytes");
        AssertLegacyFailed(RLPStatus.IntegerOverflow, Pattern(11, 0x01), "11 bytes");
        AssertLegacyFailed(RLPStatus.IntegerOverflow, Pattern(32, 0x01), "32 bytes");
        AssertLegacyFailed(RLPStatus.IntegerOverflow, Pattern(300, 0x01), "300 bytes (long-form string)");

        // Longer payloads whose low bytes form a valid v (here 0x25, chain id 1): a reader that accepted them
        // would wrap in its 128-bit accumulator and report a small chain id instead of overflow.
        for (var length = 10; length <= 40; length++)
        {
            var wrapping = new byte[length];
            wrapping[0] = 0x01;
            wrapping[^1] = 0x25;
            AssertLegacyFailed(RLPStatus.IntegerOverflow, wrapping, $"{length} bytes, 01 00 .. 25");
        }
    }

    [TestMethod]
    public void Legacy_VAsListOrDefault_IsWrongKindOrInvalidArgument()
    {
        var signature = NonDefaultSignature();
        ulong? chainId = 99;
        Assert.AreEqual(RLPStatus.WrongKind, RLPTypedExtensions.TryGetEvmLegacySignature(HexItem("c0"), StringItem(SampleR), StringItem(SampleS), out signature, out chainId));
        Assert.AreEqual(default, signature);
        Assert.IsNull(chainId);

        signature = NonDefaultSignature();
        chainId = 99;
        Assert.AreEqual(RLPStatus.InvalidArgument, RLPTypedExtensions.TryGetEvmLegacySignature(default, StringItem(SampleR), StringItem(SampleS), out signature, out chainId));
        Assert.AreEqual(default, signature);
        Assert.IsNull(chainId);
    }

    [TestMethod]
    public void Legacy_RAndS_ArePaddedTo32Bytes_AndFollowTheUInt256Rules()
    {
        var cases = new (byte[] Payload, RLPStatus Status)[]
        {
            (Pattern(33, 1), RLPStatus.IntegerOverflow),
            (Pattern(33, 0), RLPStatus.NonCanonicalInteger),
            (Pattern(32, 0), RLPStatus.NonCanonicalInteger),
            ([0x00], RLPStatus.NonCanonicalInteger),
            (Pattern(32, 1), RLPStatus.Success),
            (Pattern(31, 1), RLPStatus.Success),
            ([0x01], RLPStatus.Success),
            ([], RLPStatus.Success),
        };

        foreach (var (payload, status) in cases)
        {
            var context = Convert.ToHexString(payload);
            var rResult = ReadLegacy([0x25], payload, [1], out var signature, out var chainId);
            Assert.AreEqual(status, rResult, $"r {context}");
            Assert.AreEqual(status == RLPStatus.Success, chainId.HasValue, $"r {context}");
            Assert.AreEqual(status == RLPStatus.Success, !signature.Equals(default(Signature)), $"r {context}");
            if (status == RLPStatus.Success)
            {
                Assert.AreEqual(new Hash32(Padded32(new System.Numerics.BigInteger(payload, isUnsigned: true, isBigEndian: true))), signature.R, $"r {context}");
            }

            Assert.AreEqual(status, ReadLegacy([0x25], [1], payload, out signature, out chainId), $"s {context}");
            Assert.AreEqual(status == RLPStatus.Success, chainId.HasValue, $"s {context}");
            if (status == RLPStatus.Success)
            {
                Assert.AreEqual(new Hash32(Padded32(new System.Numerics.BigInteger(payload, isUnsigned: true, isBigEndian: true))), signature.S, $"s {context}");
            }
        }
    }

    // ------------------------------------------------------------------ typed y parity

    [TestMethod]
    public void Typed_ParityZeroAndOne_AreAccepted_AndStoredAsTheParity()
    {
        Assert.AreEqual(RLPStatus.Success, ReadTyped([], SampleR, SampleS, out var signature));
        Assert.AreEqual((byte)0, signature.V);
        Assert.AreEqual(SignatureType.Evm, signature.Type);

        Assert.AreEqual(RLPStatus.Success, ReadTyped([0x01], SampleR, SampleS, out signature));
        Assert.AreEqual((byte)1, signature.V);
        Assert.AreEqual(new Hash32(Padded32(new System.Numerics.BigInteger(SampleR, isUnsigned: true, isBigEndian: true))), signature.R);
        Assert.AreEqual(new Hash32(Padded32(new System.Numerics.BigInteger(SampleS, isUnsigned: true, isBigEndian: true))), signature.S);
    }

    [TestMethod]
    public void Typed_AnyOtherCanonicalValueUpToEightBytes_IsInvalidSignatureV()
    {
        var values = new byte[][] { new byte[] { 2 }, [3], [27], [28], [35], [0x7f], [0x80], [255], [0x01, 0x00], [0x01, 0x01], [0xff, 0xff], Pattern(8, 1), Repeat(0xff, 8) };
        foreach (var value in values)
        {
            Assert.AreEqual(RLPStatus.InvalidSignatureV, ReadTyped(value, SampleR, SampleS, out var signature), Convert.ToHexString(value));
            Assert.AreEqual(default, signature, Convert.ToHexString(value));
        }
    }

    [TestMethod]
    public void Typed_LeadingZeroIsNonCanonical_BeforeWidth_AndMoreThanEightBytesIsOverflow()
    {
        foreach (var value in new byte[][] { new byte[] { 0x00 }, [0x00, 0x01], [0x00, 0x00, 0x01], Pattern(8, 0), Pattern(9, 0), Pattern(40, 0) })
        {
            Assert.AreEqual(RLPStatus.NonCanonicalInteger, ReadTyped(value, SampleR, SampleS, out var signature), Convert.ToHexString(value));
            Assert.AreEqual(default, signature, Convert.ToHexString(value));
        }

        foreach (var value in new[] { Pattern(9, 1), Pattern(10, 1), Pattern(33, 1), Pattern(300, 1) })
        {
            Assert.AreEqual(RLPStatus.IntegerOverflow, ReadTyped(value, SampleR, SampleS, out var signature), $"{value.Length} bytes");
            Assert.AreEqual(default, signature, $"{value.Length} bytes");
        }
    }

    [TestMethod]
    public void Typed_ListOrDefault_IsWrongKindOrInvalidArgument()
    {
        var signature = NonDefaultSignature();
        Assert.AreEqual(RLPStatus.WrongKind, RLPTypedExtensions.TryGetEvmYParitySignature(HexItem("c0"), StringItem(SampleR), StringItem(SampleS), out signature));
        Assert.AreEqual(default, signature);

        signature = NonDefaultSignature();
        Assert.AreEqual(RLPStatus.InvalidArgument, RLPTypedExtensions.TryGetEvmYParitySignature(default, StringItem(SampleR), StringItem(SampleS), out signature));
        Assert.AreEqual(default, signature);
    }

    [TestMethod]
    public void Typed_RAndS_FollowTheUInt256Rules()
    {
        var cases = new (byte[] Payload, RLPStatus Status)[]
        {
            (Pattern(33, 1), RLPStatus.IntegerOverflow),
            (Pattern(32, 0), RLPStatus.NonCanonicalInteger),
            ([0x00], RLPStatus.NonCanonicalInteger),
            (Pattern(32, 1), RLPStatus.Success),
            ([], RLPStatus.Success),
        };

        foreach (var (payload, status) in cases)
        {
            var context = Convert.ToHexString(payload);
            Assert.AreEqual(status, ReadTyped([0x01], payload, [1], out var signature), $"r {context}");
            Assert.AreEqual(status == RLPStatus.Success, !signature.Equals(default(Signature)), $"r {context}");
            Assert.AreEqual(status, ReadTyped([0x01], [1], payload, out signature), $"s {context}");
        }
    }

    // ------------------------------------------------------------------ field precedence

    [TestMethod]
    public void Legacy_FirstFailingFieldWins_InOrderVThenRThenS_ForEveryShapeCombination()
    {
        var shapes = Enum.GetValues<FieldShape>();
        foreach (var vShape in shapes)
        {
            foreach (var rShape in shapes)
            {
                foreach (var sShape in shapes)
                {
                    var expected = FirstFailure(vShape, rShape, sShape);
                    var signature = NonDefaultSignature();
                    ulong? chainId = 99;
                    var status = RLPTypedExtensions.TryGetEvmLegacySignature(Shape(vShape, [0x25]), Shape(rShape, SampleR), Shape(sShape, SampleS), out signature, out chainId);
                    var context = $"v {vShape} r {rShape} s {sShape}";
                    Assert.AreEqual(expected, status, context);
                    Assert.AreEqual(expected == RLPStatus.Success, chainId == 1UL, context);
                    Assert.AreEqual(expected == RLPStatus.Success, !signature.Equals(default(Signature)), context);
                    if (expected != RLPStatus.Success)
                    {
                        Assert.IsNull(chainId, context);
                    }
                }
            }
        }
    }

    [TestMethod]
    public void Typed_FirstFailingFieldWins_InOrderYParityThenRThenS_ForEveryShapeCombination()
    {
        var shapes = Enum.GetValues<FieldShape>();
        foreach (var vShape in shapes)
        {
            foreach (var rShape in shapes)
            {
                foreach (var sShape in shapes)
                {
                    var expected = FirstFailure(vShape, rShape, sShape);
                    var status = RLPTypedExtensions.TryGetEvmYParitySignature(Shape(vShape, [0x01]), Shape(rShape, SampleR), Shape(sShape, SampleS), out var signature);
                    var context = $"yParity {vShape} r {rShape} s {sShape}";
                    Assert.AreEqual(expected, status, context);
                    Assert.AreEqual(expected == RLPStatus.Success, !signature.Equals(default(Signature)), context);
                }
            }
        }
    }

    private static RLPStatus FirstFailure(FieldShape v, FieldShape r, FieldShape s)
    {
        foreach (var shape in new[] { v, r, s })
        {
            if (shape != FieldShape.Good)
            {
                return StatusOf(shape);
            }
        }

        return RLPStatus.Success;
    }

    [TestMethod]
    public void BadVDomain_IsReportedBeforeBadROrS_InBothReaders()
    {
        var badRKind = HexItem("c0");
        var badSKind = default(RLPData);

        Assert.AreEqual(RLPStatus.InvalidSignatureV, RLPTypedExtensions.TryGetEvmLegacySignature(StringItem([0x1d]), badRKind, badSKind, out _, out _));
        Assert.AreEqual(RLPStatus.InvalidSignatureV, RLPTypedExtensions.TryGetEvmLegacySignature(StringItem([]), StringItem(Pattern(33, 1)), StringItem([0x00]), out _, out _));
        Assert.AreEqual(RLPStatus.InvalidSignatureV, RLPTypedExtensions.TryGetEvmYParitySignature(StringItem([0x02]), badRKind, badSKind, out _));
        Assert.AreEqual(RLPStatus.InvalidSignatureV, RLPTypedExtensions.TryGetEvmYParitySignature(StringItem([0x1b]), StringItem(Pattern(33, 1)), StringItem([0x00]), out _));

        // A good v then lets r and s decide.
        Assert.AreEqual(RLPStatus.WrongKind, RLPTypedExtensions.TryGetEvmLegacySignature(StringItem([0x1b]), badRKind, badSKind, out _, out _));
        Assert.AreEqual(RLPStatus.InvalidArgument, RLPTypedExtensions.TryGetEvmLegacySignature(StringItem([0x1b]), StringItem([1]), badSKind, out _, out _));
        Assert.AreEqual(RLPStatus.WrongKind, RLPTypedExtensions.TryGetEvmYParitySignature(StringItem([0x01]), badRKind, badSKind, out _));
    }

    [TestMethod]
    public void BothReaders_SetOutputsToDefault_WhenEveryFieldIsDefault()
    {
        var signature = NonDefaultSignature();
        ulong? chainId = 99;
        Assert.AreEqual(RLPStatus.InvalidArgument, RLPTypedExtensions.TryGetEvmLegacySignature(default, default, default, out signature, out chainId));
        Assert.AreEqual(default, signature);
        Assert.IsNull(chainId);

        signature = NonDefaultSignature();
        Assert.AreEqual(RLPStatus.InvalidArgument, RLPTypedExtensions.TryGetEvmYParitySignature(default, default, default, out signature));
        Assert.AreEqual(default, signature);
    }

    [TestMethod]
    public void Readers_WorkOnChildrenOfADecodedTransactionList()
    {
        // [ v, r, s ] as the tail of a transaction: view ranges are document-relative, the readers must not care.
        var v = Minimal(LegacyV(1, 98866));
        var document = RlpReference.Encode(RlpNode.ListOf(RlpNode.Bytes(v), RlpNode.Bytes(SampleR), RlpNode.Bytes(SampleS)));
        var root = DecodeOk(document);
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children));
        Assert.IsTrue(children.MoveNext());
        var first = children.Current;
        Assert.IsTrue(children.MoveNext());
        var second = children.Current;
        Assert.IsTrue(children.MoveNext());
        var third = children.Current;
        Assert.IsFalse(children.MoveNext());

        Assert.AreEqual(RLPStatus.Success, RLPTypedExtensions.TryGetEvmLegacySignature(first, second, third, out var signature, out var chainId));
        Assert.AreEqual(98866UL, chainId);
        Assert.AreEqual((byte)1, signature.V);
    }
}
