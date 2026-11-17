using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpWriterTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Hostile inputs: stack exhaustion, item-budget exhaustion, length-prefix attack corpora, exhaustive short inputs and
/// truncation/extension of valid documents. The oracle is only used where it decides accept/reject and status.
/// </summary>
[TestClass]
public sealed class AdversarialInputTests
{
    // ------------------------------------------------------------------ deep nesting

    [TestMethod]
    public void Depth64_IsAcceptedAndDepth65Rejected_WithTheMaximumConfiguration()
    {
        var limits = Limits(maxDepth: RLPDecodeLimits.HardMaxListDepth);

        Assert.AreEqual(RLPStatus.Success, Decode(NestedLists(64, out _), limits, out _, out _));
        var status = Decode(NestedLists(65, out var offsets), limits, out var document, out var error);

        Assert.AreEqual(RLPStatus.TooDeep, status);
        Assert.AreEqual(offsets[64], error.Offset);
        Assert.IsFalse(document.IsValid);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(8)]
    [DataRow(32)]
    [DataRow(64)]
    public void HundredThousandLevelsDeep_ReturnsTooDeep_AtTheFirstListOverTheLimit(int maxDepth)
    {
        var input = NestedLists(100_000, out var offsets);
        Assert.IsLessThan(OneMebibyte, input.Length);

        var status = Decode(input, Limits(maxDepth: maxDepth), out var document, out var error);

        Assert.AreEqual(RLPStatus.TooDeep, status);
        Assert.AreEqual(offsets[maxDepth], error.Offset);
        Assert.IsFalse(document.IsValid);
    }

    [TestMethod]
    public void HundredThousandLevelsDeep_OnASmallStackThread_DoesNotOverflowTheStack()
    {
        // 192 KiB of stack: any recursion proportional to the nesting depth (100,000 frames) would crash the process.
        var input = NestedLists(100_000, out _);
        RLPStatus status = RLPStatus.Success;
        var worker = new Thread(() => status = Decode(input, Limits(maxDepth: 64), out _, out _), 192 * 1024);

        worker.Start();
        worker.Join();

        Assert.AreEqual(RLPStatus.TooDeep, status);
    }

    [TestMethod]
    public void DeepButWithinTheLimit_ValidatesAtEveryStackBucketBoundary()
    {
        foreach (var depth in new[] { 7, 8, 9, 31, 32, 33, 63, 64 })
        {
            var document = DecodeOk(NestedLists(depth, out _), Limits(maxDepth: depth));

            Assert.AreEqual(RLPKind.List, document.Kind, $"{depth}");
        }
    }

    [TestMethod]
    public void DeepNestingWithSiblingsAtEveryLevel_ValidatesTheSiblingsAfterAscending()
    {
        // [ [ [ 01 ], 02 ], 03 ] shapes of increasing depth: each level has a trailing literal sibling that must be reached.
        var inner = H("c1 01");
        for (var level = 2; level <= 40; level++)
        {
            inner = WrapInList(RlpReference.Concatenate(inner, [(byte)(level & 0x7f)]));
            var document = DecodeOk(inner, Limits(maxDepth: 64));
            var node = document;
            for (var step = 1; step < level; step++)
            {
                Assert.AreEqual(RLPStatus.Success, node.TryGetChildren(out var children), $"{level}/{step}");
                Assert.IsTrue(children.MoveNext(), $"{level}/{step}");
                node = children.Current;
                Assert.IsTrue(children.MoveNext(), $"sibling {level}/{step}");
                Assert.AreEqual(1, children.Current.EncodedLength);
                Assert.IsFalse(children.MoveNext());
            }
        }
    }

    // ------------------------------------------------------------------ item budget

    [TestMethod]
    public void FourMillionTinyChildren_StopAtTheItemBudget_WithoutScanningTheRest()
    {
        const int Children = 4_000_000;
        var input = WrapInList(Repeat(0x01, Children));
        var headerLength = input.Length - Children;

        var status = Decode(input, Limits(maxBytes: 8 * OneMebibyte, maxItems: 8192), out var document, out var error);

        Assert.AreEqual(RLPStatus.TooManyItems, status);
        // The offset of the 8,192nd child shows validation stopped at the budget instead of walking all four million.
        Assert.AreEqual(headerLength + 8191, error.Offset);
        Assert.IsFalse(document.IsValid);
    }

    [TestMethod]
    public void FourMillionTinyChildren_AreAcceptedWhenTheBudgetAllowsThem()
    {
        const int Children = 4_000_000;
        var input = WrapInList(Repeat(0x01, Children));

        var status = Decode(input, Limits(maxBytes: 8 * OneMebibyte, maxItems: Children + 1), out var document, out var error);

        Assert.AreEqual(RLPStatus.Success, status, error.ToString());
        Assert.AreEqual(input.Length, document.EncodedLength);
        Assert.AreEqual(RLPStatus.TooManyItems, Decode(input, Limits(maxBytes: 8 * OneMebibyte, maxItems: Children), out _, out _));
    }

    [TestMethod]
    public void MillionsOfEmptyLists_StopAtTheItemBudget()
    {
        const int Children = 2_000_000;
        var input = WrapInList(Repeat(0xc0, Children));
        var headerLength = input.Length - Children;

        var status = Decode(input, Limits(maxBytes: 8 * OneMebibyte, maxItems: 100), out _, out var error);

        Assert.AreEqual(RLPStatus.TooManyItems, status);
        Assert.AreEqual(headerLength + 99, error.Offset);
    }

    // ------------------------------------------------------------------ length-prefix attack corpus

    [TestMethod]
    public void LongFormPrefixCorpus_MatchesTheOracle_ForEveryLengthOfLengthAndLengthPattern()
    {
        var patterns = new Func<int, byte[]>[]
        {
            n => Repeat(0xff, n),
            n => RlpReference.Concatenate([0x80], Repeat(0x00, n - 1)),
            n => RlpReference.Concatenate([0x7f], Repeat(0xff, n - 1)),
            n => RlpReference.Concatenate([0x01], Repeat(0x00, n - 1)),
            n => RlpReference.Concatenate(Repeat(0x00, n - 1), [0x38]),
            n => RlpReference.Concatenate(Repeat(0x00, n - 1), [0x37]),
            n => RlpReference.Concatenate(Repeat(0x00, n - 1), [0x39]),
            n => RlpReference.Concatenate(Repeat(0x00, n - 1), [0xff]),
            n => Repeat(0x00, n),
        };
        var tails = new[] { 0, 1, 56, 57, 300 };
        var checkedCases = 0;
        for (var prefix = 0xb8; prefix <= 0xff; prefix++)
        {
            if (prefix is >= 0xc0 and < 0xf8)
            {
                continue;
            }

            var lengthOfLength = prefix >= 0xf8 ? prefix - 0xf7 : prefix - 0xb7;
            foreach (var pattern in patterns)
            {
                foreach (var tail in tails)
                {
                    var input = RlpReference.Concatenate([(byte)prefix], pattern(lengthOfLength), Repeat(0x01, tail));
                    AssertDecodeMatchesOracle(input, Limits(), $"{RlpReference.ToHex(input[..Math.Min(input.Length, 12)], true)}..", strictOffsets: true);
                    checkedCases++;
                }
            }
        }

        Assert.IsGreaterThan(300, checkedCases);
    }

    [TestMethod]
    public void AllFfLengths_NeverWrapIntoASmallItemEnd()
    {
        foreach (var prefix in new byte[] { 0xb8, 0xb9, 0xba, 0xbb, 0xbc, 0xbd, 0xbe, 0xbf, 0xf8, 0xf9, 0xfa, 0xfb, 0xfc, 0xfd, 0xfe, 0xff })
        {
            var lengthOfLength = prefix >= 0xf8 ? prefix - 0xf7 : prefix - 0xb7;
            var input = RlpReference.Concatenate([prefix], Repeat(0xff, lengthOfLength), Repeat(0x01, 64));

            var status = Decode(input, Limits(), out var document, out _);

            Assert.AreNotEqual(RLPStatus.Success, status, $"{prefix:x2}");
            Assert.IsFalse(document.IsValid);
            Assert.AreEqual(lengthOfLength >= 4 ? RLPStatus.LengthOverflow : RLPStatus.TruncatedInput, status, $"{prefix:x2}");
        }
    }

    [TestMethod]
    public void ChildCrossingItsParentButFittingTheRoot_IsRejectedAtEveryDepth()
    {
        // c1 82: the list holds one byte, but the child 82 needs two payload bytes. Bytes that would satisfy it follow
        // inside the outer lists (80 80 80 80), so the child fits the root and the enclosing lists, never its own list.
        var document = WrapInList(RlpReference.Concatenate(H("c1 82"), H("80 80 80 80")));
        var offsetOfTheBadChild = 2;
        for (var level = 0; level < 6; level++)
        {
            var status = Decode(document, Limits(), out var rejected, out var error);

            Assert.AreEqual(RLPStatus.InvalidListBoundary, status, $"level {level}");
            Assert.AreEqual(offsetOfTheBadChild, error.Offset, $"level {level}");
            Assert.IsFalse(rejected.IsValid);

            var wrapped = WrapInList(document);
            offsetOfTheBadChild += wrapped.Length - document.Length;
            document = wrapped;
        }
    }

    // ------------------------------------------------------------------ exhaustive short inputs

    [TestMethod]
    public void EveryTwoByteInput_MatchesTheOracle()
    {
        for (var first = 0; first < 256; first++)
        {
            for (var second = 0; second < 256; second++)
            {
                byte[] input = [(byte)first, (byte)second];
                AssertDecodeMatchesOracle(input, Limits(), $"{first:x2} {second:x2}", strictOffsets: true);
            }
        }
    }

    [TestMethod]
    public void EveryThreeByteInput_OverAnInterestingByteSet_MatchesTheOracle()
    {
        byte[] interesting = [0x00, 0x01, 0x37, 0x38, 0x7f, 0x80, 0x81, 0xb7, 0xb8, 0xc0, 0xc1, 0xc2, 0xf7, 0xf8, 0xff];
        for (var first = 0; first < 256; first++)
        {
            foreach (var second in interesting)
            {
                foreach (var third in interesting)
                {
                    byte[] input = [(byte)first, second, third];
                    AssertDecodeMatchesOracle(input, Limits(), $"{first:x2} {second:x2} {third:x2}", strictOffsets: true);
                }
            }
        }
    }

    // ------------------------------------------------------------------ truncation and extension of valid documents

    [TestMethod]
    public void EveryStrictPrefixOfAValidDocument_IsRejected_NeverAccepted()
    {
        foreach (var vector in RlpVectors.Canonical.Where(v => v.Encoded.Length <= 700))
        {
            for (var length = 0; length < vector.Encoded.Length; length++)
            {
                var prefix = vector.Encoded[..length];
                var expected = AssertDecodeMatchesOracle(prefix, Limits(maxItems: 9000), $"{vector.Name} prefix {length}", strictOffsets: true);

                if (length == 0)
                {
                    Assert.AreEqual(RLPStatus.EmptyInput, expected.Status, vector.Name);
                }
                else
                {
                    Assert.AreNotEqual(RLPStatus.Success, expected.Status, $"{vector.Name} prefix {length}");
                }
            }
        }
    }

    [TestMethod]
    public void AnyTrailingByteAfterAValidDocument_IsTrailingDataAtTheFirstExtraByte()
    {
        foreach (var vector in RlpVectors.Canonical.Where(v => v.Encoded.Length <= 700))
        {
            for (var extra = 0; extra < 256; extra += 5)
            {
                var input = RlpReference.Concatenate(vector.Encoded, [(byte)extra]);

                var status = Decode(input, Limits(maxItems: 9000), out _, out var error);

                Assert.AreEqual(RLPStatus.TrailingData, status, vector.Name);
                Assert.AreEqual(vector.Encoded.Length, error.Offset, vector.Name);
            }
        }
    }

    [TestMethod]
    public void EverySingleByteChangeOfAValidDocument_MatchesTheOracle()
    {
        foreach (var vector in RlpVectors.Canonical.Where(v => v.Encoded.Length is > 1 and <= 60))
        {
            for (var position = 0; position < vector.Encoded.Length; position++)
            {
                for (var value = 0; value < 256; value++)
                {
                    var mutated = (byte[])vector.Encoded.Clone();
                    mutated[position] = (byte)value;
                    AssertDecodeMatchesOracle(mutated, Limits(), $"{vector.Name} [{position}]={value:x2}", strictOffsets: true);
                }
            }
        }
    }

    // ------------------------------------------------------------------ large opaque payloads

    [TestMethod]
    public void LargeStringPayload_IsSkippedNotScanned_AndItsBytesAreNeverInterpreted()
    {
        // A 900 KB string whose bytes look like list prefixes and bad headers must not be inspected.
        var payload = new byte[900_000];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(0xb8 + (i % 8));
        }

        var input = RlpReference.Concatenate(H("ba 0d bb a0"), payload);
        Assert.AreEqual(0x0dbba0, payload.Length);

        var document = DecodeOk(input);

        Assert.AreEqual(payload.Length, document.PayloadLength);
        Assert.AreEqual(RLPStatus.IntegerOverflow, document.TryGetUInt64(out _));
    }

    [TestMethod]
    public void ListOfLargeStrings_IsValidatedByHeadersOnly()
    {
        var bigString = RlpReference.Concatenate(H("ba 01 00 00"), Repeat(0xc1, 65536));
        var payload = RlpReference.Concatenate(bigString, bigString, bigString, bigString);
        var input = WrapInList(payload);

        var document = DecodeOk(input, Limits(maxBytes: 2 * OneMebibyte));

        Assert.AreEqual(RLPStatus.Success, document.TryIndexDirectChildren(new RLPRange[4], out var index, out _));
        Assert.AreEqual(RLPStatus.Success, index.TryGetAt(3, out var last));
        Assert.AreEqual(65536, last.PayloadLength);
    }

    // ------------------------------------------------------------------ hostile writer inputs

    [TestMethod]
    public void WriterBeginList_HostileLengths_AreRejectedByStatus_WithoutExceptionsOrMutation()
    {
        var destination = Filled(16);
        var writer = new RLPWriter(destination, new int[4]);

        foreach (var length in new[] { int.MinValue, int.MinValue + 1, -56, -1 })
        {
            Assert.AreEqual(RLPStatus.InvalidArgument, writer.TryBeginList(length), $"{length}");
        }

        foreach (var length in new[] { int.MaxValue, int.MaxValue - 1, int.MaxValue - 4 })
        {
            Assert.AreEqual(RLPStatus.LengthOverflow, writer.TryBeginList(length), $"{length}");
        }

        // The largest representable list still cannot fit a 16-byte destination.
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryBeginList(int.MaxValue - 5));
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryBeginList(1 << 24));
        Assert.IsTrue(IsFilled(destination));
        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(0), "the writer is unharmed");
    }

    [TestMethod]
    public void WriterByteString_FakeHugeSpans_ReportOverflowOrCapacity_WithoutReadingTheSource()
    {
        var destination = Filled(16);
        var writer = new RLPWriter(destination, new int[2]);

        Assert.AreEqual(RLPStatus.LengthOverflow, writer.TryWriteByteString(FakeLengthSpan(int.MaxValue)));
        Assert.AreEqual(RLPStatus.LengthOverflow, writer.TryWriteByteString(FakeLengthSpan(int.MaxValue - 4)));
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryWriteByteString(FakeLengthSpan(int.MaxValue - 5)));
        Assert.AreEqual(RLPStatus.DestinationTooSmall, writer.TryWriteByteString(FakeLengthSpan(1 << 20)));
        Assert.IsTrue(IsFilled(destination));

        Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(8));
        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteByteString(FakeLengthSpan(int.MaxValue - 5)), "inside a list the parent limit applies");
        Assert.AreEqual(RLPStatus.InvalidListBoundary, writer.TryWriteByteString(FakeLengthSpan(1 << 20)));
        Assert.IsTrue(destination.AsSpan(1).ToArray().All(b => b == Sentinel));
    }

    [TestMethod]
    public void DeepListBomb_AgainstA64SlotWorkspace_StopsWithTooDeep_AndLeavesTheRestOfTheDestinationUntouched()
    {
        const int attempts = 100_000;
        var payloads = new int[attempts];
        for (var level = attempts - 2; level >= 0; level--)
        {
            Assert.AreEqual(RLPStatus.Success, RLPEncoder.TryGetListLength(payloads[level + 1], out payloads[level]));
        }

        var destination = Filled(OneMebibyte);
        var writer = new RLPWriter(destination, new int[64]);
        var accepted = 0;
        var tooDeep = 0;

        for (var level = 0; level < attempts; level++)
        {
            var status = writer.TryBeginList(payloads[level]);
            if (status == RLPStatus.Success)
            {
                accepted++;
            }
            else
            {
                Assert.AreEqual(RLPStatus.TooDeep, status, $"level {level}");
                tooDeep++;
            }
        }

        Assert.AreEqual(64, accepted);
        Assert.AreEqual(attempts - 64, tooDeep);
        Assert.IsGreaterThanOrEqualTo(64, destination.AsSpan(0, 400).ToArray().Count(b => b != Sentinel));
        Assert.IsTrue(IsFilled(destination.AsSpan(64 * 5)), "only the headers of the 64 accepted lists were written");
        Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryComplete(out _));
    }

    [TestMethod]
    public void ManyUnbalancedEndLists_AreStableStatuses()
    {
        var writer = new RLPWriter(new byte[4], new int[2]);
        for (var i = 0; i < 100_000; i++)
        {
            Assert.AreEqual(RLPStatus.UnbalancedList, writer.TryEndList());
        }

        Assert.AreEqual(RLPStatus.Success, writer.TryWriteUInt64(1));
        Assert.AreEqual(RLPStatus.Success, writer.TryComplete(out _));
        for (var i = 0; i < 1_000; i++)
        {
            Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryEndList());
            Assert.AreEqual(RLPStatus.WriterStateInvalid, writer.TryComplete(out _));
        }
    }

    [TestMethod]
    public void TamperedFrameWorkspace_NeverLetsTheWriterWriteOutsideTheDestination()
    {
        // The workspace contract forbids caller changes; if one happens anyway the writer must still stay in bounds.
        foreach (var tampered in new[] { int.MaxValue, int.MinValue, -5, 0, 1_000_000 })
        {
            var destination = Filled(16);
            var frames = new int[2];
            var writer = new RLPWriter(destination, frames);
            Assert.AreEqual(RLPStatus.Success, writer.TryBeginList(2));
            frames[0] = tampered;
            var snapshot = (byte[])destination.Clone();

            var huge = writer.TryWriteByteString(new byte[100]);
            var status = writer.TryWriteUInt64(300);

            Assert.IsTrue(huge is RLPStatus.DestinationTooSmall or RLPStatus.InvalidListBoundary, $"{tampered}: {huge}");
            Assert.IsTrue(status is RLPStatus.Success or RLPStatus.DestinationTooSmall or RLPStatus.InvalidListBoundary, $"{tampered}: {status}");
            Assert.IsTrue(destination.AsSpan(0, 1).SequenceEqual(snapshot.AsSpan(0, 1)));
            _ = writer.TryEndList();
            _ = writer.TryComplete(out _);
        }
    }

    [TestMethod]
    public void WriterRejectsAnItemThatFailedValidation_BecauseItIsDefault()
    {
        var malformed = H("c1 82");
        var limits = RLPDecodeLimits.Default;
        Assert.AreNotEqual(RLPStatus.Success, RLPData.TryDecodeComplete(malformed, in limits, out var notAView, out _));
        var destination = Filled(8);
        var writer = new RLPWriter(destination, new int[1]);

        Assert.AreEqual(RLPStatus.InvalidArgument, writer.TryWriteEncoded(in notAView));

        Assert.IsTrue(IsFilled(destination));
    }

    [TestMethod]
    public void LeadingZeroMagnitudeOfAMegabyte_IsRejectedWithoutScanningOrWriting()
    {
        var magnitude = new byte[OneMebibyte];
        var destination = Filled(8);
        var writer = new RLPWriter(destination, new int[1]);

        Assert.AreEqual(RLPStatus.NonCanonicalInteger, writer.TryWriteUnsignedMagnitude(magnitude));
        Assert.AreEqual(RLPStatus.NonCanonicalInteger, RLPEncoder.TryWriteUnsignedMagnitude(magnitude, destination, out _));

        Assert.IsTrue(IsFilled(destination));
    }

    private readonly record struct WriterFuzzOp(int Kind, int Number, ulong Value, byte[] Bytes, byte[]? Document);

    private static RLPStatus ExecuteFuzzOp(ref RLPWriter writer, in WriterFuzzOp op, out int written)
    {
        written = 0;
        switch (op.Kind)
        {
            case 0:
                return writer.TryBeginList(op.Number);
            case 1:
                return writer.TryWriteByteString(op.Bytes);
            case 2:
                return writer.TryWriteUInt64(op.Value);
            case 3:
                return writer.TryWriteUnsignedMagnitude(op.Bytes);
            case 4:
                return writer.TryEndList();
            case 5:
                return writer.TryComplete(out written);
            default:
                if (op.Document is null)
                {
                    return writer.TryWriteEncoded(default);
                }

                var item = DecodeOk(op.Document);
                return writer.TryWriteEncoded(in item);
        }
    }

    [TestMethod]
    public void RandomWriterOperationSequences_FailuresNeverMutate_AndEveryCompletionIsCanonical()
    {
        byte[][] documents = [H("80"), H("c0"), H("83 64 6f 67"), H("c8 83 63 61 74 83 64 6f 67"), H("00"), H("b8 38 " + string.Join(' ', Enumerable.Repeat("01", 56)))];
        var completions = 0;
        var failures = 0;
        for (var seed = 0; seed < 20_000; seed++)
        {
            var rng = new Random(seed);
            var destination = Filled(rng.Next(0, 70));
            var writer = new RLPWriter(destination, new int[rng.Next(0, 5)]);
            var finished = false;
            var finishedLength = 0;

            for (var step = rng.Next(1, 30); step > 0; step--)
            {
                var kind = rng.Next(8);
                var bytes = new byte[rng.Next(0, 6) == 0 ? rng.Next(0, 70) : rng.Next(0, 4)];
                rng.NextBytes(bytes);
                if (bytes.Length > 0 && rng.Next(4) != 0)
                {
                    bytes[0] |= 1; // mostly non-zero lead so magnitudes are often valid
                }

                var op = new WriterFuzzOp(kind, rng.Next(-2, 20), (ulong)rng.NextInt64() >> rng.Next(0, 64), bytes, rng.Next(7) == 0 ? null : documents[rng.Next(documents.Length)]);
                var snapshot = (byte[])destination.Clone();

                var status = ExecuteFuzzOp(ref writer, in op, out var written);

                if (finished)
                {
                    Assert.AreEqual(RLPStatus.WriterStateInvalid, status, $"seed {seed}: use after completion");
                }

                if (status != RLPStatus.Success)
                {
                    failures++;
                    Assert.AreEqual(0, written, $"seed {seed}");
                    CollectionAssert.AreEqual(snapshot, destination, $"seed {seed}: a failed {kind} mutated the destination");
                    // A failure changed no writer state either: repeating the call fails identically.
                    Assert.AreEqual(status, ExecuteFuzzOp(ref writer, in op, out _), $"seed {seed}: failure was not idempotent");
                    CollectionAssert.AreEqual(snapshot, destination, $"seed {seed}");
                }
                else if (kind == 5)
                {
                    completions++;
                    finished = true;
                    finishedLength = written;
                    var limits = Limits(maxDepth: 8);
                    var decode = RLPData.TryDecodeComplete(destination.AsSpan(0, written), in limits, out var document, out var error);
                    Assert.AreEqual(RLPStatus.Success, decode, $"seed {seed}: the completed bytes are not canonical RLP ({error})");
                    Assert.AreEqual(written, document.EncodedLength, $"seed {seed}");
                }
            }

            if (finished)
            {
                Assert.IsTrue(IsFilled(destination.AsSpan(finishedLength)), $"seed {seed}: bytes past the completed encoding were touched");
            }
        }

        Assert.IsGreaterThan(500, completions, "the fuzzer must actually reach completion");
        Assert.IsGreaterThan(10_000, failures, "the fuzzer must actually hit failure paths");
    }
}
