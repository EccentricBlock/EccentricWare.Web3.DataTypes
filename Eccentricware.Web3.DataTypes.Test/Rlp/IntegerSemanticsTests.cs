using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Unsigned-integer semantics (spec 2.2): <see cref="RLPData.TryGetUInt64"/>. A raw string is valid generic RLP; only
/// the integer accessor rejects leading zeroes and over-wide payloads. Encoder-side integer tests are separate.
/// </summary>
[TestClass]
public sealed class IntegerSemanticsTests
{
    [TestMethod]
    [DynamicData(nameof(RlpVectors.IntegerRows), typeof(RlpVectors))]
    public void TryGetUInt64_MatchesTheIntegerVectors(IntegerVector vector)
    {
        var document = DecodeOk(vector.Encoded);

        var status = document.TryGetUInt64(out var value);

        Assert.AreEqual(vector.Status, status, vector.Name);
        Assert.AreEqual(vector.Status == RLPStatus.Success ? vector.Value : 0UL, value, vector.Name);
    }

    [TestMethod]
    [DynamicData(nameof(RlpVectors.AmbiguousIntegerRows), typeof(RlpVectors))]
    public void TryGetUInt64_AppliesTheLeadingZeroRuleBeforeTheWidthRule(IntegerVector vector)
    {
        var document = DecodeOk(vector.Encoded);

        Assert.AreEqual(vector.Status, document.TryGetUInt64(out var value), vector.Name);
        Assert.AreEqual(0UL, value);
    }

    [TestMethod]
    public void EmptyString_IsZero()
    {
        Assert.AreEqual(RLPStatus.Success, DecodeOk(H("80")).TryGetUInt64(out var value));
        Assert.AreEqual(0UL, value);
    }

    [TestMethod]
    public void RawLiteralZero_IsValidGenericRlp_ButNotAnInteger()
    {
        var document = DecodeOk(H("00"));

        Assert.IsTrue(document.IsValid);
        Assert.AreEqual(RLPStatus.NonCanonicalInteger, document.TryGetUInt64(out var value));
        Assert.AreEqual(0UL, value);
    }

    [TestMethod]
    public void AllLiterals_1To7f_AreTheirOwnValue()
    {
        for (var literal = 1; literal < 0x80; literal++)
        {
            Assert.AreEqual(RLPStatus.Success, DecodeOk([(byte)literal]).TryGetUInt64(out var value), $"{literal:x2}");
            Assert.AreEqual((ulong)literal, value, $"{literal:x2}");
        }
    }

    [TestMethod]
    public void AllOneByteStrings_80ToFf_AreTheirOwnValue()
    {
        for (var b = 0x80; b <= 0xff; b++)
        {
            Assert.AreEqual(RLPStatus.Success, DecodeOk([0x81, (byte)b]).TryGetUInt64(out var value), $"{b:x2}");
            Assert.AreEqual((ulong)b, value, $"{b:x2}");
        }
    }

    [TestMethod]
    [DataRow(0UL, "80")]
    [DataRow(1UL, "01")]
    [DataRow(127UL, "7f")]
    [DataRow(128UL, "81 80")]
    [DataRow(255UL, "81 ff")]
    [DataRow(256UL, "82 01 00")]
    [DataRow(ulong.MaxValue, "88 ff*8")]
    [DataRow(ulong.MaxValue - 1, "88 ff ff ff ff ff ff ff fe")]
    [DataRow(1UL << 63, "88 80 00 00 00 00 00 00 00")]
    [DataRow((1UL << 32) - 1, "84 ff ff ff ff")]
    [DataRow(1UL << 32, "85 01 00 00 00 00")]
    public void BoundaryMagnitudes_DecodeToTheirValue(ulong expected, string hex)
    {
        Assert.AreEqual(RLPStatus.Success, DecodeOk(H(hex)).TryGetUInt64(out var value));
        Assert.AreEqual(expected, value);
    }

    [TestMethod]
    public void EveryWidthOneToEight_DecodesAllFfAndLeadingByteVariants()
    {
        for (var width = 1; width <= 8; width++)
        {
            for (var lead = 1; lead < 256; lead += 37)
            {
                var payload = new byte[width];
                for (var i = 0; i < width; i++)
                {
                    payload[i] = (byte)(i == 0 ? lead : 0xa5 ^ i);
                }

                var encoded = RlpReference.Encode(RlpNode.Bytes(payload));
                ulong expected = 0;
                foreach (var b in payload)
                {
                    expected = (expected << 8) | b;
                }

                Assert.AreEqual(RLPStatus.Success, DecodeOk(encoded).TryGetUInt64(out var value), $"width {width} lead {lead}");
                Assert.AreEqual(expected, value, $"width {width} lead {lead}");
            }
        }
    }

    [TestMethod]
    [DataRow("82 00 01", RLPStatus.NonCanonicalInteger)]
    [DataRow("82 00 00", RLPStatus.NonCanonicalInteger)]
    [DataRow("88 00 01 02 03 04 05 06 07", RLPStatus.NonCanonicalInteger)]
    [DataRow("89 01 02 03 04 05 06 07 08 09", RLPStatus.IntegerOverflow)]
    [DataRow("89 ff*9", RLPStatus.IntegerOverflow)]
    [DataRow("89 00 01 02 03 04 05 06 07 08", RLPStatus.NonCanonicalInteger)]
    [DataRow("b8 38 01*56", RLPStatus.IntegerOverflow)]
    [DataRow("b8 38 00*56", RLPStatus.NonCanonicalInteger)]
    public void Rejections_ReturnTheExpectedStatus_AndZeroTheOutput(string hex, RLPStatus expected)
    {
        Assert.AreEqual(expected, DecodeOk(H(hex)).TryGetUInt64(out var value));
        Assert.AreEqual(0UL, value);
    }

    [TestMethod]
    public void Lists_AreWrongKind()
    {
        foreach (var hex in new[] { "c0", "c1 01", "c1 c0", "c8 83 63 61 74 83 64 6f 67" })
        {
            Assert.AreEqual(RLPStatus.WrongKind, DecodeOk(H(hex)).TryGetUInt64(out var value), hex);
            Assert.AreEqual(0UL, value);
        }
    }

    [TestMethod]
    public void NestedChildren_AreInterpretedIndependently()
    {
        // [ "", 00, 01 02 (string), ff*8 magnitude ]: payload 1 + 1 + 3 + 9 = 14 = 0xce
        var document = DecodeOk(H("ce 80 00 82 01 02 88 ff ff ff ff ff ff ff ff"));
        Assert.AreEqual(RLPStatus.Success, document.TryGetChildren(out var children));

        var expected = new (RLPStatus Status, ulong Value)[]
        {
            (RLPStatus.Success, 0), (RLPStatus.NonCanonicalInteger, 0), (RLPStatus.Success, 0x0102), (RLPStatus.Success, ulong.MaxValue),
        };
        foreach (var (status, value) in expected)
        {
            Assert.IsTrue(children.MoveNext());
            Assert.AreEqual(status, children.Current.TryGetUInt64(out var actual));
            Assert.AreEqual(value, actual);
        }

        Assert.IsFalse(children.MoveNext());
    }

    [TestMethod]
    public void MatchesTheOracle_ForEveryStringLengthUpToTenWithEveryLeadingByteClass()
    {
        foreach (var length in Enumerable.Range(0, 11))
        {
            foreach (var lead in new byte[] { 0x00, 0x01, 0x7f, 0x80, 0xff })
            {
                var payload = Repeat(0xab, length);
                if (length > 0)
                {
                    payload[0] = lead;
                }

                var node = RlpNode.Bytes(payload);
                var document = DecodeOk(RlpReference.Encode(node));

                var status = document.TryGetUInt64(out var value);
                _ = RlpReference.TryInterpretAsUInt64(node, out var expectedValue, out var expectedStatus);

                Assert.AreEqual(expectedStatus, status, $"length {length}, lead {lead:x2}");
                Assert.AreEqual(expectedValue, value, $"length {length}, lead {lead:x2}");
            }
        }
    }

    // ------------------------------------------------------------------ writer side (spec 2.2)

    private static byte[] Minimal(ulong value) => ((RlpString)RlpNode.FromUInt64(value)).Value;

    [TestMethod]
    [DynamicData(nameof(RlpVectors.IntegerRows), typeof(RlpVectors))]
    public void IntegerVectors_WriterSide_UInt64AndMagnitudeEncodersAgreeWithTheStatedBytes(IntegerVector vector)
    {
        var document = DecodeOk(vector.Encoded);
        if (document.Kind != RLPKind.ByteString)
        {
            return; // list rows have no writer-side integer meaning
        }

        var magnitude = document.Payload.ToArray();
        var destination = new byte[vector.Encoded.Length + 4];
        Array.Fill(destination, (byte)0xee);

        var magnitudeStatus = RLPEncoder.TryWriteUnsignedMagnitude(magnitude, destination, out var written);

        if (vector.Status == RLPStatus.NonCanonicalInteger)
        {
            // A leading-zero payload decodes as generic RLP but is never accepted as an integer magnitude.
            Assert.AreEqual(RLPStatus.NonCanonicalInteger, magnitudeStatus, vector.Name);
            Assert.AreEqual(0, written, vector.Name);
            Assert.IsTrue(destination.All(b => b == 0xee), vector.Name);
            return;
        }

        // Success rows and over-wide (IntegerOverflow) rows are valid magnitudes of any length.
        Assert.AreEqual(RLPStatus.Success, magnitudeStatus, vector.Name);
        Assert.AreEqual(vector.Encoded.Length, written, vector.Name);
        Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual(vector.Encoded), vector.Name);

        if (vector.Status == RLPStatus.Success)
        {
            Array.Fill(destination, (byte)0xee);
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUInt64(vector.Value, destination, out written), vector.Name);
            Assert.AreEqual(vector.Encoded.Length, written, vector.Name);
            Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual(vector.Encoded), vector.Name);
        }
    }

    [TestMethod]
    [DataRow(0UL)]
    [DataRow(1UL)]
    [DataRow(127UL)]
    [DataRow(128UL)]
    [DataRow(255UL)]
    [DataRow(256UL)]
    [DataRow(1024UL)]
    [DataRow(ulong.MaxValue)]
    public void IntegerBoundaries_WriteThenReadBack_ThroughEncoderWriterAndReader(ulong value)
    {
        var viaEncoder = new byte[12];
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUInt64(value, viaEncoder, out var encoderWritten));
        var viaWriter = new byte[12];
        var writer = new RLPWriter(viaWriter, new int[1]);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(value));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out var writerWritten));
        var viaMagnitude = new byte[12];
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUnsignedMagnitude(Minimal(value), viaMagnitude, out var magnitudeWritten));

        Assert.AreEqual(encoderWritten, writerWritten);
        Assert.AreEqual(encoderWritten, magnitudeWritten);
        Assert.IsTrue(viaEncoder.AsSpan(0, encoderWritten).SequenceEqual(viaWriter.AsSpan(0, writerWritten)));
        Assert.IsTrue(viaEncoder.AsSpan(0, encoderWritten).SequenceEqual(viaMagnitude.AsSpan(0, magnitudeWritten)));
        Assert.IsTrue(viaEncoder.AsSpan(0, encoderWritten).SequenceEqual(RlpReference.Encode(RlpNode.FromUInt64(value))));
        Assert.AreEqual(RLPStatus.Success, DecodeOk(viaEncoder.AsSpan(0, encoderWritten).ToArray()).TryGetUInt64(out var readBack));
        Assert.AreEqual(value, readBack);
    }

    [TestMethod]
    public void EveryMagnitudeWidthOneToEight_RoundTripsThroughTheEncoderAndReader()
    {
        for (var width = 1; width <= 8; width++)
        {
            foreach (var lead in new byte[] { 1, 0x7f, 0x80, 0xff })
            {
                var magnitude = Repeat(0x5a, width);
                magnitude[0] = lead;
                var destination = new byte[16];

                Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUnsignedMagnitude(magnitude, destination, out var written), $"width {width} lead {lead:x2}");
                var document = DecodeOk(destination.AsSpan(0, written).ToArray());

                Assert.AreEqual(RLPStatus.Success, document.TryGetUInt64(out var value));
                ulong expected = 0;
                foreach (var b in magnitude)
                {
                    expected = (expected << 8) | b;
                }

                Assert.AreEqual(expected, value, $"width {width} lead {lead:x2}");
                Assert.IsTrue(document.Payload.SequenceEqual(width == 1 && lead < 0x80 ? new[] { lead } : magnitude));
            }
        }
    }

    [TestMethod]
    public void MagnitudeWriter_AcceptsMagnitudesWiderThanUInt64_WhichTheReaderOnlyRejectsAsOverflow()
    {
        var magnitude = Repeat(0xff, 33);
        var destination = new byte[40];

        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUnsignedMagnitude(magnitude, destination, out var written));

        Assert.AreEqual(34, written);
        Assert.AreEqual((byte)0xa1, destination[0]);
        Assert.AreEqual(RLPStatus.IntegerOverflow, DecodeOk(destination.AsSpan(0, written).ToArray()).TryGetUInt64(out _));
    }

    [TestMethod]
    public void EmptyMagnitude_IsZero_ThroughEncoderAndWriter()
    {
        var destination = new byte[2];
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUnsignedMagnitude([], destination, out var written));
        Assert.AreEqual(1, written);
        Assert.AreEqual((byte)0x80, destination[0]);

        var viaWriter = new byte[2];
        var writer = new RLPWriter(viaWriter, new int[1]);
        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUnsignedMagnitude([]));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out written));
        Assert.AreEqual((byte)0x80, viaWriter[0]);
        Assert.AreEqual(RLPStatus.Success, DecodeOk(H("80")).TryGetUInt64(out var zero));
        Assert.AreEqual(0UL, zero);
    }

    [TestMethod]
    public void NonMinimalMagnitudes_AreRejectedNotNormalised_ByEncoderAndWriter_AndDifferFromTheRawZeroString()
    {
        // The raw string 00 is valid generic RLP (the writer can copy it verbatim) but is not an integer.
        var raw = H("00");
        var rawDocument = DecodeOk(raw);
        var viaCopy = new byte[1];
        Assert.AreEqual(RLPStatus.Success, rawDocument.TryCopyEncodedTo(viaCopy, out _));
        Assert.AreEqual((byte)0, viaCopy[0]);

        foreach (var bad in new[] { new byte[] { 0 }, [0, 0], [0, 1], [0, 0xff]})
        {
            var destination = new byte[8];
            Array.Fill(destination, (byte)0xee);
            Assert.AreEqual(RLPStatus.NonCanonicalInteger, RLPEncoder.TryWriteUnsignedMagnitude(bad, destination, out var written));
            Assert.AreEqual(0, written);
            Assert.IsTrue(destination.All(b => b == 0xee), "no silent normalisation: nothing is written");

            var writer = new RLPWriter(destination, new int[1]);
            Assert.AreEqual(RLPStatus.NonCanonicalInteger, writer.TryWriteUnsignedMagnitude(bad));
            Assert.AreEqual(RLPStatus.EmptyInput, writer.TryComplete(out _));
            Assert.IsTrue(destination.All(b => b == 0xee));
        }
    }

    [TestMethod]
    public void WriterAndEncoder_AgreeOnTheIntegerZeroAndTheRawZeroByteString()
    {
        var zero = new byte[2];
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteUInt64(0, zero, out _));
        var rawZero = new byte[2];
        Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryWriteByteString([0x00], rawZero, out var written));

        Assert.AreEqual((byte)0x80, zero[0], "integer zero is the empty string");
        Assert.AreEqual(1, written);
        Assert.AreEqual((byte)0x00, rawZero[0], "the raw string 00 is the literal byte");
    }
}
