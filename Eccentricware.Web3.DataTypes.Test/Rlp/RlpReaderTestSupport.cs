using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Shared helpers for the strict-reader tests: hand-built inputs, an oracle comparison for one decode, and a deep
/// tree walk that checks an <see cref="RLPData"/> tree against the oracle's <see cref="RlpNode"/>.
/// </summary>
internal static class RlpReaderTestSupport
{
    public const int OneMebibyte = 1 << 20;

    public static byte[] H(string hex) => RlpReference.FromHex(hex);

    public static RLPDecodeLimits Limits(int maxBytes = OneMebibyte, int maxDepth = 32, int maxItems = 8192) =>
        new(maxBytes, maxDepth, maxItems);

    /// <summary>Hand-written list header for <paramref name="payloadLength"/> bytes (up to 4 length bytes).</summary>
    public static byte[] ListHeader(int payloadLength)
    {
        if (payloadLength <= 55)
        {
            return [(byte)(0xc0 + payloadLength)];
        }

        if (payloadLength <= 0xff)
        {
            return [0xf8, (byte)payloadLength];
        }

        if (payloadLength <= 0xffff)
        {
            return [0xf9, (byte)(payloadLength >> 8), (byte)payloadLength];
        }

        if (payloadLength <= 0xffffff)
        {
            return [0xfa, (byte)(payloadLength >> 16), (byte)(payloadLength >> 8), (byte)payloadLength];
        }

        return [0xfb, (byte)(payloadLength >> 24), (byte)(payloadLength >> 16), (byte)(payloadLength >> 8), (byte)payloadLength];
    }

    public static byte[] WrapInList(byte[] payload) => RlpReference.Concatenate(ListHeader(payload.Length), payload);

    public static byte[] Repeat(byte value, int count) => Enumerable.Repeat(value, count).ToArray();

    /// <summary>
    /// <paramref name="count"/> lists nested to that depth, the innermost empty, built in linear time. Returns the bytes
    /// and the document offset of each list prefix, outermost first.
    /// </summary>
    public static byte[] NestedLists(int count, out int[] listOffsets)
    {
        var headers = new byte[count][];
        headers[count - 1] = [0xc0];
        long total = 1;
        for (var level = count - 2; level >= 0; level--)
        {
            headers[level] = ListHeader(checked((int)total));
            total += headers[level].Length;
        }

        var bytes = new byte[total];
        listOffsets = new int[count];
        var position = 0;
        for (var level = 0; level < count; level++)
        {
            listOffsets[level] = position;
            headers[level].CopyTo(bytes, position);
            position += headers[level].Length;
        }

        return bytes;
    }

    public static RLPStatus Decode(byte[] input, RLPDecodeLimits limits, out RLPData document, out RLPError error) =>
        RLPData.TryDecodeComplete(input, in limits, out document, out error);

    public static RLPData DecodeOk(byte[] input, RLPDecodeLimits? limits = null)
    {
        var effective = limits ?? RLPDecodeLimits.Default;
        var status = RLPData.TryDecodeComplete(input, in effective, out var document, out var error);
        Assert.AreEqual(RLPStatus.Success, status, $"{RlpReference.ToHex(input, true)} -> {error}");
        return document;
    }

    /// <summary>
    /// Decodes with production and oracle and requires the same status; the offset is compared strictly when the oracle
    /// marks it normative, or when <paramref name="strictOffsets"/> is set. Returns the oracle result.
    /// </summary>
    public static RlpReferenceResult AssertDecodeMatchesOracle(byte[] input, RLPDecodeLimits limits, string context, bool strictOffsets)
    {
        var expected = RlpReference.Decode(input, limits.MaxEncodedBytes, limits.MaxListDepth, limits.MaxItems);
        var status = RLPData.TryDecodeComplete(input, in limits, out var document, out var error);
        Assert.AreEqual(expected.Status, status, $"{context}: status");
        Assert.AreEqual(expected.Status, error.Status, $"{context}: error.Status");
        if (status == RLPStatus.Success)
        {
            Assert.IsTrue(document.IsValid, context);
            Assert.AreEqual(default(RLPError), error, context);
            Assert.AreEqual(input.Length, document.EncodedLength, context);
            Assert.AreEqual(0, document.EncodedRange.Offset, context);
        }
        else
        {
            Assert.IsFalse(document.IsValid, context);
            Assert.AreEqual(RLPKind.None, document.Kind, context);
            if (expected.OffsetIsNormative || strictOffsets)
            {
                Assert.AreEqual(expected.Offset, error.Offset, $"{context}: offset");
            }
        }

        return expected;
    }

    /// <summary>
    /// Walks <paramref name="node"/> and the oracle tree together: kind, payload and encoded bytes, document-relative
    /// ranges, contiguous children, enumerator versus index agreement, integer accessor, and a verbatim copy.
    /// </summary>
    public static void AssertTreeMatches(ReadOnlySpan<byte> document, RLPData node, RlpNode expected, string context)
    {
        Assert.IsTrue(node.IsValid, context);
        var encodedRange = node.EncodedRange;
        var payloadRange = node.PayloadRange;
        Assert.AreEqual(encodedRange.Length, node.EncodedLength, context);
        Assert.AreEqual(payloadRange.Length, node.PayloadLength, context);
        Assert.IsTrue(document.Slice(encodedRange.Offset, encodedRange.Length).SequenceEqual(node.Encoded), context);
        Assert.IsTrue(document.Slice(payloadRange.Offset, payloadRange.Length).SequenceEqual(node.Payload), context);
        Assert.IsTrue(node.Encoded.SequenceEqual(RlpReference.Encode(expected)), $"{context}: encoded bytes");
        Assert.IsGreaterThanOrEqualTo(encodedRange.Offset, payloadRange.Offset, context);
        Assert.IsLessThanOrEqualTo(encodedRange.Offset + encodedRange.Length, payloadRange.Offset + payloadRange.Length, context);

        // Copy round trip.
        var copy = new byte[node.EncodedLength + 2];
        Assert.AreEqual(RLPStatus.Success, node.TryCopyEncodedTo(copy, out var written), context);
        Assert.AreEqual(node.EncodedLength, written, context);
        Assert.IsTrue(copy.AsSpan(0, written).SequenceEqual(node.Encoded), $"{context}: copy");

        var integerStatus = node.TryGetUInt64(out var integer);
        if (expected is RlpString text)
        {
            Assert.AreEqual(RLPKind.ByteString, node.Kind, context);
            Assert.IsTrue(node.Payload.SequenceEqual(text.Value), $"{context}: payload");
            if (text.Value.Length == 1 && text.Value[0] < 0x80)
            {
                Assert.AreEqual(encodedRange, payloadRange, $"{context}: literal ranges coincide");
                Assert.AreEqual(1, node.EncodedLength, context);
            }
            else
            {
                Assert.AreEqual(node.EncodedLength - text.Value.Length, payloadRange.Offset - encodedRange.Offset, context);
            }

            var wrongKindWorkspace = new RLPRange[2];
            Assert.AreEqual(RLPStatus.WrongKind, node.TryGetChildren(out var noChildren), context);
            Assert.AreEqual(RLPStatus.WrongKind, node.TryIndexDirectChildren(wrongKindWorkspace, out var noIndex, out var noCount), context);
            Assert.IsFalse(noIndex.IsValid, context);
            Assert.AreEqual(0, noCount, context);
            Assert.IsFalse(noChildren.MoveNext(), context);

            _ = RlpReference.TryInterpretAsUInt64(text, out var expectedInteger, out var expectedIntegerStatus);
            Assert.AreEqual(expectedIntegerStatus, integerStatus, $"{context}: integer status");
            Assert.AreEqual(expectedInteger, integer, $"{context}: integer value");
            return;
        }

        var list = (RlpList)expected;
        Assert.AreEqual(RLPKind.List, node.Kind, context);
        Assert.AreEqual(RLPStatus.WrongKind, integerStatus, context);
        Assert.AreEqual(0UL, integer, context);
        Assert.AreEqual(node.EncodedLength - node.PayloadLength, payloadRange.Offset - encodedRange.Offset, context);

        Assert.AreEqual(RLPStatus.Success, node.TryGetChildren(out var children), context);
        var ordinal = 0;
        var cursor = payloadRange.Offset;
        var enumeratedRanges = new RLPRange[list.Items.Count];
        while (children.MoveNext())
        {
            Assert.IsLessThan(list.Items.Count, ordinal, $"{context}: more children than expected");
            var child = children.Current;
            Assert.AreEqual(cursor, child.EncodedRange.Offset, $"{context}: child {ordinal} is contiguous");
            enumeratedRanges[ordinal] = child.EncodedRange;
            AssertTreeMatches(document, child, list.Items[ordinal], context);
            cursor += child.EncodedLength;
            ordinal++;
        }

        Assert.AreEqual(list.Items.Count, ordinal, $"{context}: child count");
        Assert.AreEqual(payloadRange.Offset + payloadRange.Length, cursor, $"{context}: children end at the payload end");

        // The index agrees with the enumerator, child by child.
        var workspace = new RLPRange[list.Items.Count];
        Assert.AreEqual(RLPStatus.Success, node.TryIndexDirectChildren(workspace, out var index, out var required), context);
        Assert.IsTrue(index.IsValid, context);
        Assert.AreEqual(list.Items.Count, index.Count, context);
        Assert.AreEqual(list.Items.Count, required, context);
        for (var i = 0; i < list.Items.Count; i++)
        {
            Assert.AreEqual(RLPStatus.Success, index.TryGetAt(i, out var indexed), context);
            Assert.AreEqual(enumeratedRanges[i], indexed.EncodedRange, $"{context}: indexed child {i}");
            Assert.AreEqual(enumeratedRanges[i], workspace[i], context);
        }
    }
}
