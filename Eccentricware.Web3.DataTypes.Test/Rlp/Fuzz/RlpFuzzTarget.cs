using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Fuzz entry point for the strict RLP codec: one deterministic function that accepts arbitrary bytes, never throws for a
/// correct implementation, and throws <see cref="InvalidOperationException"/> carrying the input in hex when any invariant
/// breaks (a fuzzer reports that exception as the crash). This file does not use MSTest; the signature checks call <see cref="EvmSignatureOracle"/>, whose helper files do (see
/// <c>Fuzz/Corpus/README.md</c> for linking it into a SharpFuzz, libFuzzer or AFL harness).
/// </summary>
/// <remarks>
/// For each of three limit sets (the defaults, a tight set and a size limit one byte under the input) and for
/// <c>default(RLPDecodeLimits)</c> it decodes with the library and with the independent oracle <see cref="RlpReference"/>
/// and requires equal status, equal offsets where the oracle says the offset is normative, and a fully reset result on
/// failure. A successful decode is then walked twice, with the enumerator and with the index, against the oracle tree:
/// document-relative ranges, contiguity, kinds, payload bytes, integer accessors (<c>TryGetUInt64</c> against the oracle; <c>TryGetUInt256</c>, <c>TryGetHash32</c>, <c>TryGetAddress</c>,
/// <c>TryGetOptionalAddress</c> and <c>TryGetFunctionSelector</c> against their documented width rules; none may throw),
/// <c>TryCopyEncodedTo</c> (exact copy, too-small destination untouched), index capacity behaviour and, for every consecutive triple
/// of children of every list with at least three, the legacy and typed signature readers against <see cref="EvmSignatureOracle"/>
/// (status, parity, chain id, 32-byte r and s) plus a rebuild with the signature writers and sizers (parity and <see cref="Signature"/> overloads)
/// that must equal the source items. Finally the tree is
/// re-encoded with <see cref="RLPWriter"/> (value by value, and in one piece with <c>TryWriteEncoded</c>) and the bytes must
/// equal the input. Allocation-free behaviour is not asserted here; the oracle and the walk allocate freely.
/// <see cref="Run(ReadOnlySpan{byte}, RLPDecodeLimits)"/> checks a single limit set (corpus files that record their limits).
/// Messages quote at most <see cref="MaxQuotedBytes"/> bytes of input and then give the total length.
/// </remarks>
public static class RlpFuzzTarget
{
    /// <summary>Longest input quoted in full in a failure message.</summary>
    public const int MaxQuotedBytes = 8192;

    private const int WriterFrameSlots = RLPDecodeLimits.HardMaxListDepth + 2;

    /// <summary>Marks an invariant violation so it can be told apart from an unexpected exception inside the codec.</summary>
    private sealed class FuzzInvariantViolationException(string message) : InvalidOperationException(message);

    /// <summary>
    /// Checks every invariant for <paramref name="input"/> under the four built-in limit sets. Returns normally when they all hold.
    /// </summary>
    /// <exception cref="InvalidOperationException">An invariant failed, or the codec or oracle threw; the message contains the input in hex.</exception>
    public static void Run(ReadOnlySpan<byte> input)
    {
        try
        {
            CheckAllLimitSets(input);
        }
        catch (FuzzInvariantViolationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Unexpected(exception, input);
        }
    }

    /// <summary>
    /// Checks every invariant for <paramref name="input"/> under exactly <paramref name="limits"/> (used for corpus files that
    /// record the limits they were written for, such as depth and size boundary cases).
    /// </summary>
    /// <exception cref="InvalidOperationException">An invariant failed, or the codec or oracle threw; the message contains the input in hex.</exception>
    public static void Run(ReadOnlySpan<byte> input, RLPDecodeLimits limits)
    {
        try
        {
            CheckDecode(input, limits);
        }
        catch (FuzzInvariantViolationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Unexpected(exception, input);
        }
    }

    private static InvalidOperationException Unexpected(Exception exception, ReadOnlySpan<byte> input) =>
        new($"Unexpected {exception.GetType().Name} while checking the codec against the oracle (codec or oracle defect): {exception.Message}. Input: {Quote(input)}", exception);

    private static void CheckAllLimitSets(ReadOnlySpan<byte> input)
    {
        CheckDecode(input, RLPDecodeLimits.Default);
        CheckDecode(input, new RLPDecodeLimits(Math.Max(1, input.Length), 4, 12));
        CheckDecode(input, new RLPDecodeLimits(Math.Max(1, input.Length - 1), RLPDecodeLimits.HardMaxListDepth, 8192));
        CheckDecode(input, default);
    }

    /// <summary>The input as quoted in failure messages: <c>0x</c> hex, cut at <see cref="MaxQuotedBytes"/> bytes with the total length stated.</summary>
    internal static string Quote(ReadOnlySpan<byte> input) =>
        input.Length <= MaxQuotedBytes
            ? $"0x{Convert.ToHexString(input)}"
            : $"0x{Convert.ToHexString(input[..MaxQuotedBytes])}... ({input.Length} bytes in total)";

    private static void Fail(ReadOnlySpan<byte> input, string message) =>
        throw new FuzzInvariantViolationException($"{message}. Input: {Quote(input)}");

    private static void Require(bool condition, ReadOnlySpan<byte> input, string message)
    {
        if (!condition)
        {
            Fail(input, message);
        }
    }

    private static void CheckDecode(ReadOnlySpan<byte> input, RLPDecodeLimits limits)
    {
        var expected = RlpReference.Decode(input, limits.MaxEncodedBytes, limits.MaxListDepth, limits.MaxItems);
        var status = RLPData.TryDecodeComplete(input, in limits, out var document, out var error);

        if (!(status == expected.Status))
        {
            Fail(input, $"status {status} but the oracle says {expected.Status} (limits {limits})");
        }

        if (!(error.Status == status))
        {
            Fail(input, $"error.Status {error.Status} differs from the returned status {status}");
        }


        if (status != RLPStatus.Success)
        {
            if (!(!document.IsValid && document.Kind == RLPKind.None))
            {
                Fail(input, $"failed decode ({status}) returned a valid document");
            }

            Require(document.EncodedLength == 0 && document.PayloadLength == 0, input, "failed decode returned non-empty lengths");
            if (!(!expected.OffsetIsNormative || error.Offset == expected.Offset))
            {
                Fail(input, $"offset {error.Offset} but the oracle says {expected.Offset} for {status}");
            }

            if (!(error.Offset >= -1 && error.Offset <= input.Length))
            {
                Fail(input, $"offset {error.Offset} lies outside the input");
            }

            CheckDefaultDocumentIsHarmless(document, input);
            return;
        }

        Require(document.IsValid && document.EncodedLength == input.Length, input, "successful decode does not cover the whole input");
        Require(document.EncodedRange.Offset == 0 && error == default, input, "successful decode has a non-zero start or a non-default error");
        Require(expected.Root is not null, input, "oracle returned no tree for a successful decode");

        WalkNode(input, document, expected.Root!);
        CheckReEncoding(input, document, expected.Root!);
    }

    private static void CheckDefaultDocumentIsHarmless(RLPData document, ReadOnlySpan<byte> input)
    {
        // Misusing a failed result must give statuses, not exceptions or stale data.
        Require(document.TryGetUInt64(out var value) == RLPStatus.InvalidArgument && value == 0, input, "TryGetUInt64 on an invalid document");
        Require(document.TryGetChildren(out _) == RLPStatus.InvalidArgument, input, "TryGetChildren on an invalid document");
        Require(document.TryGetUInt256(out _) == RLPStatus.InvalidArgument && document.TryGetHash32(out _) == RLPStatus.InvalidArgument && document.TryGetAddress(out _) == RLPStatus.InvalidArgument
            && document.TryGetFunctionSelector(out _) == RLPStatus.InvalidArgument, input, "typed accessors on an invalid document");
        Require(document.TryCopyEncodedTo(new byte[4], out var written) == RLPStatus.InvalidArgument && written == 0, input, "TryCopyEncodedTo on an invalid document");
        Require(document.TryIndexDirectChildren(new RLPRange[1], out var index, out var required) == RLPStatus.InvalidArgument && !index.IsValid && required == 0, input, "TryIndexDirectChildren on an invalid document");
    }

    private static void WalkNode(ReadOnlySpan<byte> document, RLPData node, RlpNode expected)
    {
        Require(node.IsValid, document, "walked into an invalid node");
        var encoded = node.EncodedRange;
        var payload = node.PayloadRange;
        if (!(encoded.Offset >= 0 && encoded.Length >= 1 && (long)encoded.Offset + encoded.Length <= document.Length))
        {
            Fail(document, $"encoded range {encoded} lies outside the document");
        }

        if (!(payload.Offset >= encoded.Offset && (long)payload.Offset + payload.Length == (long)encoded.Offset + encoded.Length))
        {
            Fail(document, $"payload range {payload} is not the tail of encoded range {encoded}");
        }

        Require(node.EncodedLength == encoded.Length && node.PayloadLength == payload.Length, document, "length properties disagree with the ranges");
        if (!(node.Encoded.SequenceEqual(document.Slice(encoded.Offset, encoded.Length))))
        {
            Fail(document, $"Encoded bytes differ from the document at {encoded}");
        }

        if (!(node.Payload.SequenceEqual(document.Slice(payload.Offset, payload.Length))))
        {
            Fail(document, $"Payload bytes differ from the document at {payload}");
        }


        CheckCopy(document, node);

        if (expected is RlpString text)
        {
            WalkString(document, node, text);
            return;
        }

        WalkList(document, node, (RlpList)expected);
    }

    private static void CheckCopy(ReadOnlySpan<byte> document, RLPData node)
    {
        var length = node.EncodedLength;
        var exact = new byte[length];
        if (!(node.TryCopyEncodedTo(exact, out var written) == RLPStatus.Success && written == length && exact.AsSpan().SequenceEqual(node.Encoded)))
        {
            Fail(document, $"TryCopyEncodedTo did not reproduce the item at {node.EncodedRange}");
        }


        var sentinel = new byte[length + 3];
        Array.Fill(sentinel, (byte)0xee);
        Require(node.TryCopyEncodedTo(sentinel, out written) == RLPStatus.Success && written == length, document, "TryCopyEncodedTo into a larger buffer failed");
        Require(sentinel.AsSpan(0, length).SequenceEqual(node.Encoded) && sentinel.AsSpan(length).IndexOfAnyExcept((byte)0xee) < 0, document, "TryCopyEncodedTo wrote outside the item length");

        var small = new byte[length - 1];
        Array.Fill(small, (byte)0xee);
        Require(node.TryCopyEncodedTo(small, out written) == RLPStatus.DestinationTooSmall && written == 0 && small.AsSpan().IndexOfAnyExcept((byte)0xee) < 0, document, "TryCopyEncodedTo into a too-small buffer mutated it or reported success");
    }

    private static void WalkString(ReadOnlySpan<byte> document, RLPData node, RlpString expected)
    {
        Require(node.Kind == RLPKind.ByteString, document, "oracle string decoded as a list");
        Require(node.Payload.SequenceEqual(expected.Value), document, "string payload differs from the oracle");
        var isLiteral = expected.Value.Length == 1 && expected.Value[0] < 0x80;
        Require(isLiteral ? node.EncodedRange == node.PayloadRange : node.EncodedLength - node.PayloadLength is >= 1 and <= 9, document, "string header length is implausible");

        _ = RlpReference.TryInterpretAsUInt64(expected, out var expectedInteger, out var expectedStatus);
        var integerStatus = node.TryGetUInt64(out var integer);
        if (!(integerStatus == expectedStatus && integer == expectedInteger))
        {
            Fail(document, $"TryGetUInt64 gave {integerStatus}/{integer}, oracle {expectedStatus}/{expectedInteger}");
        }


        CheckTypedAccessorsOnString(document, node, expected.Value);

        Require(node.TryGetChildren(out var noChildren) == RLPStatus.WrongKind && !noChildren.MoveNext(), document, "TryGetChildren on a string");
        Require(node.TryIndexDirectChildren(new RLPRange[1], out var noIndex, out var noCount) == RLPStatus.WrongKind && !noIndex.IsValid && noCount == 0, document, "TryIndexDirectChildren on a string");
    }

    private static void CheckTypedAccessorsOnString(ReadOnlySpan<byte> document, RLPData node, byte[] payload)
    {
        // Every typed reader must return a status for any string, never throw, and follow its documented width rule.
        var integerStatus = node.TryGetUInt256(out var integer);
        var expectedInteger = payload.Length == 0 ? RLPStatus.Success
            : payload[0] == 0 ? RLPStatus.NonCanonicalInteger
            : payload.Length > 32 ? RLPStatus.IntegerOverflow
            : RLPStatus.Success;
        Require(integerStatus == expectedInteger, document, "TryGetUInt256 status differs from the rule (empty is zero, leading zero, then width)");
        if (integerStatus == RLPStatus.Success)
        {
            var padded = new byte[32];
            payload.CopyTo(padded, 32 - payload.Length);
            var written = new byte[32];
            integer.WriteBigEndian(written);
            Require(written.AsSpan().SequenceEqual(padded), document, "TryGetUInt256 value differs from the payload");
        }
        else
        {
            Require(integer.IsZero, document, "TryGetUInt256 left a value on failure");
        }

        var expectedFixed32 = payload.Length == 32 ? RLPStatus.Success : RLPStatus.InvalidFixedLength;
        var hashStatus = node.TryGetHash32(out var hash);
        Require(hashStatus == expectedFixed32, document, "TryGetHash32 status differs from the 32-byte rule");
        if (hashStatus == RLPStatus.Success)
        {
            var written = new byte[32];
            hash.WriteBigEndian(written);
            Require(written.AsSpan().SequenceEqual(payload), document, "TryGetHash32 value differs from the payload");
        }

        var expectedAddress = payload.Length == 20 ? RLPStatus.Success : RLPStatus.InvalidFixedLength;
        Require(node.TryGetAddress(out _) == expectedAddress, document, "TryGetAddress status differs from the 20-byte rule");

        var optionalStatus = node.TryGetOptionalAddress(out _, out var isEmpty);
        Require(optionalStatus == (payload.Length is 0 or 20 ? RLPStatus.Success : RLPStatus.InvalidFixedLength) && isEmpty == (payload.Length == 0 && optionalStatus == RLPStatus.Success), document, "TryGetOptionalAddress differs from the empty-or-20-bytes rule");

        Require(node.TryGetFunctionSelector(out _) == (payload.Length >= 4 ? RLPStatus.Success : RLPStatus.InvalidFixedLength), document, "TryGetFunctionSelector differs from the at-least-4-bytes rule");
    }

    private static void CheckTypedAccessorsOnList(ReadOnlySpan<byte> document, RLPData node)
    {
        Require(node.TryGetUInt256(out _) == RLPStatus.WrongKind, document, "TryGetUInt256 on a list");
        Require(node.TryGetHash32(out _) == RLPStatus.WrongKind, document, "TryGetHash32 on a list");
        Require(node.TryGetAddress(out _) == RLPStatus.WrongKind, document, "TryGetAddress on a list");
        Require(node.TryGetOptionalAddress(out _, out var isEmpty) == RLPStatus.WrongKind && !isEmpty, document, "TryGetOptionalAddress on a list");
        Require(node.TryGetFunctionSelector(out _) == RLPStatus.WrongKind, document, "TryGetFunctionSelector on a list");
    }

    private static void WalkList(ReadOnlySpan<byte> document, RLPData node, RlpList expected)
    {
        Require(node.Kind == RLPKind.List, document, "oracle list decoded as a string");
        Require(node.TryGetUInt64(out var integer) == RLPStatus.WrongKind && integer == 0, document, "TryGetUInt64 on a list");
        CheckTypedAccessorsOnList(document, node);

        var count = expected.Items.Count;
        var enumerated = new RLPRange[count];
        Require(node.TryGetChildren(out var children) == RLPStatus.Success, document, "TryGetChildren on a list");
        var cursor = node.PayloadRange.Offset;
        var ordinal = 0;
        while (children.MoveNext())
        {
            Require(ordinal < count, document, "enumerator yields more children than the oracle tree has");
            var child = children.Current;
            if (!(child.EncodedRange.Offset == cursor))
            {
                Fail(document, $"child {ordinal} is not contiguous with the previous one");
            }

            enumerated[ordinal] = child.EncodedRange;
            WalkNode(document, child, expected.Items[ordinal]);
            cursor += child.EncodedLength;
            ordinal++;
        }

        if (!(ordinal == count))
        {
            Fail(document, $"enumerator yields {ordinal} children, oracle tree has {count}");
        }

        Require(cursor == node.PayloadRange.Offset + node.PayloadLength, document, "children do not end at the list payload end");

        // The index must agree with the enumerator, and a workspace one slot short must fail without being touched.
        var workspace = new RLPRange[count];
        Require(node.TryIndexDirectChildren(workspace, out var index, out var required) == RLPStatus.Success && index.IsValid && index.Count == count && required == count, document, "TryIndexDirectChildren on an exact workspace");
        for (var i = 0; i < count; i++)
        {
            if (!(index.TryGetAt(i, out var indexed) == RLPStatus.Success && indexed.EncodedRange == enumerated[i] && workspace[i] == enumerated[i]))
            {
                Fail(document, $"index entry {i} differs from the enumerator");
            }

        }

        CheckSignatureTriples(document, index, expected);

        Require(index.TryGetAt(-1, out var below) == RLPStatus.InvalidArgument && !below.IsValid, document, "TryGetAt(-1)");
        Require(index.TryGetAt(count, out var above) == RLPStatus.InvalidArgument && !above.IsValid, document, "TryGetAt(count)");
        if (count > 0)
        {
            var shortWorkspace = new RLPRange[count - 1];
            Array.Fill(shortWorkspace, new RLPRange(-7, -7));
            var shortStatus = node.TryIndexDirectChildren(shortWorkspace, out var shortIndex, out var shortRequired);
            Require(shortStatus == RLPStatus.IndexCapacityExceeded && !shortIndex.IsValid && shortRequired == count, document, "TryIndexDirectChildren with a workspace one slot short");
            Require(shortWorkspace.AsSpan().IndexOfAnyExcept(new RLPRange(-7, -7)) < 0, document, "an insufficient index workspace was modified");
        }
    }

    /// <summary>
    /// For every consecutive triple of direct children, reads it as a legacy <c>(v, r, s)</c> and as a typed <c>(yParity, r, s)</c>
    /// signature and compares the status, the parity, the chain id and the 32-byte r and s with
    /// <see cref="EvmSignatureOracle"/> (BigInteger only, independent of the adapters). A success is then written back with the
    /// writer adapters, sized by the sizing helpers, and the bytes must equal the source items.
    /// </summary>
    private static void CheckSignatureTriples(ReadOnlySpan<byte> document, RLPChildrenIndex index, RlpList expected)
    {
        for (var i = 0; i + 2 < index.Count; i++)
        {
            var firstStatus = index.TryGetAt(i, out var first);
            var secondStatus = index.TryGetAt(i + 1, out var second);
            var thirdStatus = index.TryGetAt(i + 2, out var third);
            Require(firstStatus == RLPStatus.Success && secondStatus == RLPStatus.Success && thirdStatus == RLPStatus.Success, document, "TryGetAt inside the triple range");

            var legacyStatus = RLPTypedExtensions.TryGetEvmLegacySignature(first, second, third, out var legacySignature, out var chainId);
            var legacyExpected = EvmSignatureOracle.ExpectLegacySignature(expected.Items[i], expected.Items[i + 1], expected.Items[i + 2]);
            CheckSignatureOutcome(document, "legacy", legacyStatus, legacySignature, chainId, legacyExpected);
            if (legacyStatus == RLPStatus.Success)
            {
                CheckSignatureRebuild(document, isLegacy: true, first, second, third, legacySignature, chainId);
            }

            var typedStatus = RLPTypedExtensions.TryGetEvmYParitySignature(first, second, third, out var typedSignature);
            var typedExpected = EvmSignatureOracle.ExpectYParitySignature(expected.Items[i], expected.Items[i + 1], expected.Items[i + 2]);
            CheckSignatureOutcome(document, "y parity", typedStatus, typedSignature, null, typedExpected);
            if (typedStatus == RLPStatus.Success)
            {
                CheckSignatureRebuild(document, isLegacy: false, first, second, third, typedSignature, null);
            }
        }
    }

    private static void CheckSignatureOutcome(ReadOnlySpan<byte> document, string reader, RLPStatus status, Signature signature, ulong? chainId, EvmSignatureOracle.SignatureExpectation expected)
    {
        if (!(status == expected.Status))
        {
            Fail(document, $"{reader} signature reader gave {status}, the oracle says {expected.Status}");
        }

        if (status != RLPStatus.Success)
        {
            Require(signature == default && chainId is null, document, $"{reader} signature reader left a value after {status}");
            return;
        }

        var r32 = new byte[32];
        var s32 = new byte[32];
        signature.WriteR(r32);
        signature.WriteS(s32);
        if (!(signature.Type == SignatureType.Evm && signature.V == expected.Parity && chainId == expected.ChainId))
        {
            Fail(document, $"{reader} signature: V {signature.V} chain {chainId?.ToString() ?? "none"}, the oracle says parity {expected.Parity} chain {expected.ChainId?.ToString() ?? "none"}");
        }

        Require(r32.AsSpan().SequenceEqual(expected.R32) && s32.AsSpan().SequenceEqual(expected.S32), document, $"{reader} signature r or s differs from the oracle");
    }

    private static void CheckSignatureRebuild(ReadOnlySpan<byte> document, bool isLegacy, RLPData v, RLPData r, RLPData s, Signature signature, ulong? chainId)
    {
        var rValue = signature.R.ToUInt256();
        var sValue = signature.S.ToUInt256();
        var vSize = 1;
        if (isLegacy)
        {
            Require(RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(signature.V, chainId, out vSize) == RLPStatus.Success, document, "legacy v sizer failed for a read signature");
        }

        // v by the parity (byte) overload.
        var vBuffer = new byte[vSize];
        var vWriter = new RLPWriter(vBuffer, new int[4]);
        var vStatus = isLegacy ? vWriter.TryWriteEvmLegacyV(signature.V, chainId) : vWriter.TryWriteEvmYParity(signature.V);
        RequireEmittedItem(document, "v/y parity", vStatus, ref vWriter, vBuffer, v.Encoded);

        // v by the Signature overloads, with V in the 0/1 form the readers return and in the 27/28 wire form.
        var r32 = new byte[32];
        var s32 = new byte[32];
        signature.WriteR(r32);
        signature.WriteS(s32);
        var wireForm = Signature.FromEvmComponents(r32, s32, (byte)(signature.V + 27));
        foreach (var candidate in new[] { signature, wireForm })
        {
            var candidateSize = 1;
            if (isLegacy)
            {
                Require(RLPTypedExtensions.TryGetEvmLegacyVEncodedLength(candidate, chainId, out candidateSize) == RLPStatus.Success && candidateSize == vSize, document, "legacy v Signature sizer differs from the parity sizer");
            }

            var candidateBuffer = new byte[candidateSize];
            var candidateWriter = new RLPWriter(candidateBuffer, new int[4]);
            var candidateStatus = isLegacy ? candidateWriter.TryWriteEvmLegacyV(candidate, chainId) : candidateWriter.TryWriteEvmYParity(candidate);
            RequireEmittedItem(document, "v/y parity (Signature overload)", candidateStatus, ref candidateWriter, candidateBuffer, v.Encoded);
        }

        var rBuffer = new byte[RLPTypedExtensions.GetUInt256EncodedLength(rValue)];
        var rWriter = new RLPWriter(rBuffer, new int[4]);
        RequireEmittedItem(document, "r", rWriter.TryWriteUInt256(rValue), ref rWriter, rBuffer, r.Encoded);

        var sBuffer = new byte[RLPTypedExtensions.GetUInt256EncodedLength(sValue)];
        var sWriter = new RLPWriter(sBuffer, new int[4]);
        RequireEmittedItem(document, "s", sWriter.TryWriteUInt256(sValue), ref sWriter, sBuffer, s.Encoded);
    }

    /// <summary>The writer holds exactly one item, as large as the sizer announced (the buffer length), equal to the source encoding.</summary>
    private static void RequireEmittedItem(ReadOnlySpan<byte> document, string what, RLPStatus writeStatus, ref RLPWriter writer, byte[] buffer, ReadOnlySpan<byte> sourceEncoding)
    {
        if (!(writeStatus == RLPStatus.Success))
        {
            Fail(document, $"writing the {what} of a read signature gave {writeStatus}");
        }

        var completeStatus = writer.TryComplete(out var written);
        if (!(completeStatus == RLPStatus.Success && written == buffer.Length && buffer.AsSpan().SequenceEqual(sourceEncoding)))
        {
            Fail(document, $"the {what} of a read signature was written as {Convert.ToHexString(buffer)} ({written} of {buffer.Length} announced bytes, {completeStatus}), the source item is {Convert.ToHexString(sourceEncoding)}");
        }
    }

    private static void CheckReEncoding(ReadOnlySpan<byte> input, RLPData root, RlpNode expected)
    {
        Require(RlpReference.Encode(expected).AsSpan().SequenceEqual(input), input, "oracle re-encoding of the decoded tree is not the input");

        // Value by value through the framed writer.
        var rebuilt = new byte[input.Length];
        var writer = new RLPWriter(rebuilt, new int[WriterFrameSlots]);
        var status = EmitNode(ref writer, root);
        if (!(status == RLPStatus.Success))
        {
            Fail(input, $"RLPWriter rejected the decoded tree: {status}");
        }

        status = writer.TryComplete(out var written);
        if (!(status == RLPStatus.Success && written == input.Length && rebuilt.AsSpan().SequenceEqual(input)))
        {
            Fail(input, $"RLPWriter re-encoding differs from the input ({status}, {written} bytes)");
        }


        // The whole validated item in one call.
        var copied = new byte[input.Length];
        var copyWriter = new RLPWriter(copied, new int[WriterFrameSlots]);
        status = copyWriter.TryWriteEncoded(in root);
        if (!(status == RLPStatus.Success))
        {
            Fail(input, $"TryWriteEncoded rejected the root: {status}");
        }

        status = copyWriter.TryComplete(out written);
        Require(status == RLPStatus.Success && written == input.Length && copied.AsSpan().SequenceEqual(input), input, "TryWriteEncoded differs from the input");
    }

    private static RLPStatus EmitNode(ref RLPWriter writer, RLPData node)
    {
        if (node.Kind == RLPKind.ByteString)
        {
            return writer.TryWriteByteString(node.Payload);
        }

        var status = writer.TryBeginList(node.PayloadLength);
        if (status != RLPStatus.Success || node.TryGetChildren(out var children) != RLPStatus.Success)
        {
            return status != RLPStatus.Success ? status : RLPStatus.InvalidArgument;
        }

        while (children.MoveNext())
        {
            status = EmitNode(ref writer, children.Current);
            if (status != RLPStatus.Success)
            {
                return status;
            }
        }

        return writer.TryEndList();
    }
}
