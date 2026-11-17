using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Every prefix family through the public decoder: canonical forms decode to the oracle's tree, and every
/// non-canonical or malformed form is rejected with the specified status (and offset where the spec fixes it).
/// </summary>
[TestClass]
public sealed class PrefixCanonicalityTests
{
    // ------------------------------------------------------------------ canonical documents

    [TestMethod]
    [DynamicData(nameof(RlpVectors.CanonicalRows), typeof(RlpVectors))]
    public void Canonical_DecodesToTheOracleTree(CanonicalVector vector)
    {
        var document = DecodeOk(vector.Encoded);

        AssertTreeMatches(vector.Encoded, document, vector.Tree, vector.Name);
        Assert.IsTrue(document.Encoded.SequenceEqual(vector.Encoded), vector.Name);
        Assert.AreEqual(new RLPRange(0, vector.Encoded.Length), document.EncodedRange, vector.Name);
    }

    [TestMethod]
    [DynamicData(nameof(RlpVectors.KnownEthereumRows), typeof(RlpVectors))]
    public void KnownEthereumVector_DecodesToTheExpectedTree(KnownEthereumVector vector)
    {
        var document = DecodeOk(vector.Encoded);

        AssertTreeMatches(vector.Encoded, document, vector.Tree, vector.Name);
    }

    [TestMethod]
    public void All128Literals_DecodeAsOneByteStringsWithCoincidingRanges()
    {
        for (var value = 0; value < 0x80; value++)
        {
            byte[] input = [(byte)value];
            var document = DecodeOk(input);

            Assert.AreEqual(RLPKind.ByteString, document.Kind, $"{value:x2}");
            Assert.AreEqual(new RLPRange(0, 1), document.EncodedRange, $"{value:x2}");
            Assert.AreEqual(document.EncodedRange, document.PayloadRange, $"{value:x2}");
            Assert.AreEqual(1, document.EncodedLength, $"{value:x2}");
            Assert.AreEqual(1, document.PayloadLength, $"{value:x2}");
            Assert.IsTrue(document.Payload.SequenceEqual(input), $"{value:x2}");
            Assert.IsTrue(document.Encoded.SequenceEqual(input), $"{value:x2}");
        }
    }

    [TestMethod]
    public void All128HighOneByteStrings_DecodeWithTheEightyOnePrefix()
    {
        for (var value = 0x80; value <= 0xff; value++)
        {
            byte[] input = [0x81, (byte)value];
            var document = DecodeOk(input);

            Assert.AreEqual(RLPKind.ByteString, document.Kind, $"{value:x2}");
            Assert.AreEqual(new RLPRange(0, 2), document.EncodedRange, $"{value:x2}");
            Assert.AreEqual(new RLPRange(1, 1), document.PayloadRange, $"{value:x2}");
            Assert.AreEqual((byte)value, document.Payload[0], $"{value:x2}");
        }
    }

    [TestMethod]
    public void All128LowOneByteStringsWrittenWithEightyOne_AreNonCanonicalAtTheirPrefix()
    {
        for (var value = 0; value < 0x80; value++)
        {
            byte[] input = [0x81, (byte)value];
            var status = Decode(input, Limits(), out var document, out var error);

            Assert.AreEqual(RLPStatus.NonCanonicalEncoding, status, $"{value:x2}");
            Assert.AreEqual(new RLPError(RLPStatus.NonCanonicalEncoding, 0), error, $"{value:x2}");
            Assert.IsFalse(document.IsValid, $"{value:x2}");
        }
    }

    [TestMethod]
    public void EmptyStringAndEmptyList_AreDistinctKinds()
    {
        var emptyString = DecodeOk(H("80"));
        var emptyList = DecodeOk(H("c0"));

        Assert.AreEqual(RLPKind.ByteString, emptyString.Kind);
        Assert.AreEqual(RLPKind.List, emptyList.Kind);
        Assert.AreEqual(new RLPRange(0, 1), emptyString.EncodedRange);
        Assert.AreEqual(new RLPRange(1, 0), emptyString.PayloadRange);
        Assert.AreEqual(new RLPRange(0, 1), emptyList.EncodedRange);
        Assert.AreEqual(new RLPRange(1, 0), emptyList.PayloadRange);
        Assert.IsTrue(emptyString.Payload.IsEmpty);
        Assert.IsTrue(emptyList.Payload.IsEmpty);
    }

    [TestMethod]
    public void RawStringZero_IsValidGenericRlp()
    {
        var document = DecodeOk(H("00"));

        Assert.AreEqual(RLPKind.ByteString, document.Kind);
        Assert.AreEqual((byte)0x00, document.Payload[0]);
    }

    [TestMethod]
    [DataRow(55, "b7")]
    [DataRow(56, "b8 38")]
    [DataRow(255, "b8 ff")]
    [DataRow(256, "b9 01 00")]
    [DataRow(65535, "b9 ff ff")]
    [DataRow(65536, "ba 01 00 00")]
    public void StringLengthTransitions_UseTheMinimalLengthOfLength(int length, string prefixHex)
    {
        var prefix = H(prefixHex);
        var input = RlpReference.Concatenate(prefix, Repeat(0xaa, length));
        var document = DecodeOk(input);

        Assert.AreEqual(length, document.PayloadLength);
        Assert.AreEqual(prefix.Length, document.PayloadRange.Offset);
        Assert.AreEqual(input.Length, document.EncodedLength);
    }

    [TestMethod]
    [DataRow(55, "f7")]
    [DataRow(56, "f8 38")]
    [DataRow(255, "f8 ff")]
    [DataRow(256, "f9 01 00")]
    [DataRow(65536, "fa 01 00 00")]
    public void ListLengthTransitions_UseTheMinimalLengthOfLength(int payloadLength, string prefixHex)
    {
        var prefix = H(prefixHex);
        var input = RlpReference.Concatenate(prefix, Repeat(0x01, payloadLength));
        var document = DecodeOk(input, Limits(maxItems: payloadLength + 1));

        Assert.AreEqual(payloadLength, document.PayloadLength);
        Assert.AreEqual(prefix.Length, document.PayloadRange.Offset);
    }

    // ------------------------------------------------------------------ every one-byte input and c1 xx

    [TestMethod]
    public void EveryOneByteInput_IsValidOrTruncatedAtTheEnd_NeverAnUnknownPrefix()
    {
        foreach (var vector in RlpVectors.SingleByteInputs)
        {
            var status = Decode(vector.Input, vector.Limits, out var document, out var error);

            Assert.AreEqual(vector.Status, status, vector.Name);
            Assert.AreEqual(vector.Status == RLPStatus.Success, document.IsValid, vector.Name);
            Assert.AreEqual(vector.Offset, error.Offset, vector.Name);
        }
    }

    [TestMethod]
    public void ListWithOneByteChild_IsValidOnlyForACompleteOneByteChild()
    {
        foreach (var vector in RlpVectors.ListWithOneByteChild)
        {
            var status = Decode(vector.Input, vector.Limits, out _, out var error);

            Assert.AreEqual(vector.Status, status, vector.Name);
            if (vector.OffsetIsNormative || status != RLPStatus.Success)
            {
                Assert.AreEqual(vector.Offset, error.Offset, vector.Name);
            }
        }
    }

    // ------------------------------------------------------------------ invalid rows

    [TestMethod]
    [DynamicData(nameof(RlpVectors.InvalidHandStatedRows), typeof(RlpVectors))]
    public void InvalidHandStated_ReturnsTheStatedStatus(DecodeVector vector)
    {
        AssertRow(vector);
    }

    [TestMethod]
    [DynamicData(nameof(RlpVectors.InvalidNestedDerivedRows), typeof(RlpVectors))]
    public void InvalidPatternNestedInsideLists_ReturnsTheDerivedStatus(DecodeVector vector)
    {
        AssertRow(vector);
    }

    [TestMethod]
    [DynamicData(nameof(RlpVectors.AmbiguousPrecedenceRows), typeof(RlpVectors))]
    public void AmbiguousPrecedence_FollowsTheBindingReading(DecodeVector vector)
    {
        AssertRow(vector);
    }

    private static void AssertRow(DecodeVector vector)
    {
        var limits = vector.Limits;
        var status = RLPData.TryDecodeComplete(vector.Input, in limits, out var document, out var error);

        Assert.AreEqual(vector.Status, status, vector.Name);
        Assert.AreEqual(vector.Status, error.Status, vector.Name);
        Assert.IsFalse(document.IsValid, vector.Name);
        Assert.AreEqual(RLPKind.None, document.Kind, vector.Name);
        Assert.IsTrue(document.Encoded.IsEmpty, vector.Name);
        if (vector.OffsetIsNormative)
        {
            Assert.AreEqual(vector.Offset, error.Offset, vector.Name);
        }

        // Always a plausible offset: inside the input (or at its end), or -1 for invalid configuration.
        Assert.IsTrue(error.Offset == -1 || (uint)error.Offset <= (uint)vector.Input.Length || vector.Status == RLPStatus.TooLarge, vector.Name);
    }

    /// <summary>The oracle's offsets for the non-normative rows are the binding readings (spec 5.3 notes); keep them pinned.</summary>
    [TestMethod]
    public void NonNormativeOffsets_FollowTheBindingReadings()
    {
        var rows = RlpVectors.InvalidHandStated
            .Concat(RlpVectors.InvalidNestedDerived)
            .Concat(RlpVectors.AmbiguousPrecedence)
            .Concat(RlpVectors.LimitVectors);
        foreach (var vector in rows)
        {
            var limits = vector.Limits;
            var status = RLPData.TryDecodeComplete(vector.Input, in limits, out _, out var error);

            Assert.AreEqual(vector.Status, status, vector.Name);
            Assert.AreEqual(vector.Offset, error.Offset, vector.Name);
        }
    }

    // ------------------------------------------------------------------ header check order (binding readings)

    [TestMethod]
    [DataRow("bf ff ff ff ff ff ff ff ff", RLPStatus.LengthOverflow, 1)]
    [DataRow("b8 37", RLPStatus.NonCanonicalEncoding, 1)]
    [DataRow("b9 00", RLPStatus.TruncatedInput, 2)]
    [DataRow("ff 00 00 00 00 7f ff ff ff", RLPStatus.NonCanonicalEncoding, 1)]
    [DataRow("c1 82", RLPStatus.InvalidListBoundary, 1)]
    [DataRow("81 7f 00", RLPStatus.NonCanonicalEncoding, 0)]
    [DataRow("b8", RLPStatus.TruncatedInput, 1)]
    [DataRow("b8 38", RLPStatus.TruncatedInput, 2)]
    [DataRow("81", RLPStatus.TruncatedInput, 1)]
    public void HeaderCheckOrder_MatchesTheBindingDecisions(string hex, RLPStatus expectedStatus, int expectedOffset)
    {
        var status = Decode(H(hex), Limits(), out var document, out var error);

        Assert.AreEqual(expectedStatus, status);
        Assert.AreEqual(new RLPError(expectedStatus, expectedOffset), error);
        Assert.IsFalse(document.IsValid);
    }

    [TestMethod]
    public void MaxLengthRepresentable_IsTruncatedNotOverflow_AndOneMoreIsOverflow()
    {
        // bb 7f ff ff ff: payload length int.MaxValue, nothing follows. Must not wrap while computing the item end.
        Assert.AreEqual(RLPStatus.TruncatedInput, Decode(H("bb 7f ff ff ff"), Limits(), out _, out var truncated));
        Assert.AreEqual(5, truncated.Offset);
        Assert.AreEqual(RLPStatus.LengthOverflow, Decode(H("bb 80 00 00 00"), Limits(), out _, out var overflow));
        Assert.AreEqual(1, overflow.Offset);
    }

    [TestMethod]
    public void FailureOnTheLastAvailableByteOfEveryPrefix_NeverThrows()
    {
        for (var prefix = 0; prefix <= 0xff; prefix++)
        {
            byte[] input = [(byte)prefix];
            var status = Decode(input, Limits(), out var document, out _);

            Assert.AreEqual(prefix < 0x81 || prefix == 0xc0 ? RLPStatus.Success : RLPStatus.TruncatedInput, status, $"{prefix:x2}");
            Assert.AreEqual(status == RLPStatus.Success, document.IsValid, $"{prefix:x2}");
        }
    }

    [TestMethod]
    public void NestedPatterns_AreRejectedAtEveryNestingLevelWithTheInnerStatus()
    {
        // 81 7f, b8 37 .., f8 37 .., b9 00 38 .. inside one to three enclosing lists.
        var inners = new[]
        {
            (Bytes: H("81 7f"), Status: RLPStatus.NonCanonicalEncoding),
            (Bytes: H("b8 37 aa*55"), Status: RLPStatus.NonCanonicalEncoding),
            (Bytes: H("f8 37 01*55"), Status: RLPStatus.NonCanonicalEncoding),
            (Bytes: H("b9 00 38 aa*56"), Status: RLPStatus.NonCanonicalEncoding),
            (Bytes: H("f9 00 38 01*56"), Status: RLPStatus.NonCanonicalEncoding),
            (Bytes: H("bf ff*8"), Status: RLPStatus.LengthOverflow),
            (Bytes: H("ff ff*8"), Status: RLPStatus.LengthOverflow),
            (Bytes: H("c1 82"), Status: RLPStatus.InvalidListBoundary),
            (Bytes: H("b8"), Status: RLPStatus.InvalidListBoundary),
            (Bytes: H("b8 38"), Status: RLPStatus.InvalidListBoundary),
        };
        foreach (var (bytes, expected) in inners)
        {
            var wrapped = bytes;
            for (var level = 1; level <= 3; level++)
            {
                wrapped = WrapInList(wrapped);
                var status = Decode(wrapped, Limits(), out var document, out _);

                Assert.AreEqual(expected, status, $"{RlpReference.ToHex(bytes, true)} x{level}");
                Assert.IsFalse(document.IsValid);
            }
        }
    }

    [TestMethod]
    public void TrailingData_IsReportedAtTheFirstTrailingByte_BeforeDescent()
    {
        Assert.AreEqual(new RLPError(RLPStatus.TrailingData, 1), ErrorOf("80 00"));
        Assert.AreEqual(new RLPError(RLPStatus.TrailingData, 1), ErrorOf("c0 00"));
        Assert.AreEqual(new RLPError(RLPStatus.TrailingData, 2), ErrorOf("c1 82 00 00"));
        Assert.AreEqual(new RLPError(RLPStatus.TrailingData, 9), ErrorOf("c8 83 63 61 74 83 64 6f 67 00"));
    }

    private static RLPError ErrorOf(string hex)
    {
        _ = Decode(H(hex), Limits(), out _, out var error);
        return error;
    }
}
