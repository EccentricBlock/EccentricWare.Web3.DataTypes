using System.Numerics;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// The run validation of adjacent same-type words (the elements of a static array) and the bounded padding scan. The scalar reference
/// (<see cref="ABIWord.CheckScalar"/>, itself checked against BigInteger arithmetic in <see cref="ABIWordTests"/>, and
/// <see cref="ABIReference.IsCanonical"/> here) defines the answer; every kernel and the production dispatcher must agree with it, on every
/// length, every failing word, every violating byte, unaligned slices, and through the whole validator with exact work-budget boundaries.
/// Run the project with DOTNET_EnableAVX2=0 and DOTNET_EnableHWIntrinsic=0 to cover the reduced-SIMD and scalar paths.
/// </summary>
[TestClass]
public sealed class ABIWordRunTests
{
    private sealed record WordType(string Name, ABITypeKind Kind, int Width)
    {
        public RElementary Model => new(Kind, Width, Kind is ABITypeKind.Fixed or ABITypeKind.UFixed ? 1 : 0);

        public override string ToString() => Name;
    }

    private static readonly WordType[] Types =
    [
        new("uint8", ABITypeKind.UInt, 8), new("uint24", ABITypeKind.UInt, 24), new("uint64", ABITypeKind.UInt, 64), new("uint160", ABITypeKind.UInt, 160),
        new("uint248", ABITypeKind.UInt, 248), new("uint256", ABITypeKind.UInt, 256),
        new("int8", ABITypeKind.Int, 8), new("int64", ABITypeKind.Int, 64), new("int256", ABITypeKind.Int, 256),
        new("address", ABITypeKind.Address, 0), new("bool", ABITypeKind.Bool, 0),
        new("bytes1", ABITypeKind.FixedBytes, 1), new("bytes4", ABITypeKind.FixedBytes, 4), new("bytes20", ABITypeKind.FixedBytes, 20),
        new("bytes31", ABITypeKind.FixedBytes, 31), new("bytes32", ABITypeKind.FixedBytes, 32),
        new("function", ABITypeKind.Function, 0),
        new("ufixed8x2", ABITypeKind.UFixed, 8), new("fixed64x18", ABITypeKind.Fixed, 64),
    ];

    /// <summary>Word counts around every vector width and unrolling factor of the kernels (1, 2, 4, 8 words), plus the empty run.</summary>
    private static readonly int[] Counts = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, 128, 129, 255, 256, 257];

    // Deterministic per call site (the tests run in parallel, so a shared counter would make the sequence depend on scheduling).
    private static DeterministicRng NewRng([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => new(0x5EEDBA5EUL + (ulong)line * 0x9E3779B1UL);

    /// <summary>A canonical word of the type with realistic and boundary content.</summary>
    private static byte[] ValidWord(WordType t, DeterministicRng rng)
    {
        switch (t.Kind)
        {
            case ABITypeKind.UInt:
            case ABITypeKind.UFixed:
                BigInteger limit = BigInteger.One << t.Width;
                return ABIReference.ToWord(rng.Next(4) switch { 0 => limit - 1, 1 => BigInteger.Zero, _ => ABIReference.Unsigned(rng.Bytes(t.Width / 8)) });
            case ABITypeKind.Int:
            case ABITypeKind.Fixed:
                BigInteger half = BigInteger.One << (t.Width - 1);
                return ABIReference.ToWord(rng.Next(5) switch
                {
                    0 => half - 1,
                    1 => -half,
                    2 => BigInteger.MinusOne,
                    _ => new BigInteger(rng.Bytes(t.Width / 8), isUnsigned: false, isBigEndian: true),
                });
            case ABITypeKind.Address:
                return ABIReference.ToWord(ABIReference.Unsigned(rng.Bytes(20)));
            case ABITypeKind.Bool:
                return ABIReference.ToWord(rng.Next(2));
            case ABITypeKind.FixedBytes:
                var fixedBytes = new byte[32];
                rng.Bytes(t.Width).CopyTo(fixedBytes, 0);
                return fixedBytes;
            case ABITypeKind.Function:
                var function = new byte[32];
                rng.Bytes(24).CopyTo(function, 0);
                return function;
            default:
                throw new InvalidOperationException();
        }
    }

    /// <summary>Every single-byte corruption of a canonical word that the independent model rejects: (byte index, new value).</summary>
    private static List<(int Index, byte Value)> Corruptions(WordType t, byte[] canonical)
    {
        var result = new List<(int, byte)>();
        byte[] probe = new byte[32];
        for (int index = 0; index < 32; index++)
        {
            foreach (byte value in new byte[] { 0x01, 0x02, 0x7f, 0x80, 0xfe, 0xff })
            {
                canonical.CopyTo(probe, 0);
                probe[index] = value;
                if (!ABIReference.IsCanonical(t.Model, probe))
                {
                    result.Add((index, value));
                }
            }
        }

        return result;
    }

    private static ABIStatus ExpectedStatus(WordType t) => t.Kind is ABITypeKind.FixedBytes or ABITypeKind.Function ? ABIStatus.InvalidPadding : ABIStatus.InvalidScalar;

    private static byte[] Run(WordType t, int count, DeterministicRng rng)
    {
        var bytes = new byte[count * 32];
        for (int i = 0; i < count; i++)
        {
            ValidWord(t, rng).CopyTo(bytes, i * 32);
        }

        return bytes;
    }

    /// <summary>Every implementation of "all words of the run are accepted".</summary>
    private static IEnumerable<(string Name, Func<byte[], int, int, bool> Accepts)> Kernels(WordType t)
    {
        yield return ("production", (b, o, n) => ABIWordRun.FindFirstRejected(b.AsSpan(o, n), t.Kind, t.Width, out _) < 0);
        yield return ("reference", (b, o, n) => ABIWordRun.FirstRejected(b.AsSpan(o, n), t.Kind, t.Width, out _) < 0);
        if (ABIWordRun.AcceptsEveryWord(t.Kind, t.Width))
        {
            yield break;
        }

        ReadOnlySpan<byte> mask = ABIWordRun.Mask(t.Kind, t.Width);
        if (mask.IsEmpty)
        {
            yield break; // narrow signed integers: the per-word reference is the only implementation
        }

        byte[] maskBytes = mask.ToArray();
        yield return ("Vector128", (b, o, n) => ABIWordRun.AllMasked128(b.AsSpan(o, n), maskBytes));
        yield return ("Vector256", (b, o, n) => ABIWordRun.AllMasked256(b.AsSpan(o, n), maskBytes));
    }

    // ------------------------------------------------------------------ kernel equivalence

    [TestMethod]
    public void Masks_ExpressExactlyTheStrictScalarRules_OnRandomAndBoundaryWords()
    {
        // The mask kernels are only valid if (word & mask) == 0 is the same predicate as CheckScalar for every word, not just for the
        // corruptions the other tests plant. 20,000 random words per type, half of them single-byte corruptions.
        foreach (WordType t in Types)
        {
            DeterministicRng rng = NewRng();
            ReadOnlySpan<byte> mask = ABIWordRun.Mask(t.Kind, t.Width);
            for (int i = 0; i < 20_000; i++)
            {
                byte[] word = ValidWord(t, rng);
                if ((i & 1) == 1)
                {
                    word[rng.Next(32)] = (byte)rng.NextUInt64();
                }

                bool model = ABIReference.IsCanonical(t.Model, word);
                Assert.IsTrue(ABIWord.TryLoad(word, 0, out ABIWord loaded));
                Assert.AreEqual(model, loaded.CheckScalar(t.Kind, t.Width) == ABIStatus.Success, $"{t} CheckScalar vs model {Convert.ToHexString(word)}");
                if (ABIWordRun.AcceptsEveryWord(t.Kind, t.Width))
                {
                    Assert.IsTrue(model, $"{t} claims to accept every word but the model rejects {Convert.ToHexString(word)}");
                }
                else if (!mask.IsEmpty)
                {
                    bool masked = true;
                    for (int b = 0; b < 32; b++)
                    {
                        masked &= (word[b] & mask[b]) == 0;
                    }

                    Assert.AreEqual(model, masked, $"{t} mask vs model {Convert.ToHexString(word)}");
                }
            }
        }
    }

    [TestMethod]
    public void Masks_ExpressExactlyTheStrictScalarRules_ForEveryWidth()
    {
        var widthTypes = new List<WordType>();
        for (int bits = 8; bits <= 256; bits += 8)
        {
            widthTypes.Add(new WordType($"uint{bits}", ABITypeKind.UInt, bits));
            widthTypes.Add(new WordType($"ufixed{bits}x2", ABITypeKind.UFixed, bits));
            widthTypes.Add(new WordType($"int{bits}", ABITypeKind.Int, bits));
        }

        for (int bytes = 1; bytes <= 32; bytes++)
        {
            widthTypes.Add(new WordType($"bytes{bytes}", ABITypeKind.FixedBytes, bytes));
        }

        foreach (WordType t in widthTypes)
        {
            DeterministicRng rng = NewRng();
            ReadOnlySpan<byte> mask = ABIWordRun.Mask(t.Kind, t.Width);
            bool signedNarrow = t.Kind == ABITypeKind.Int && t.Width != 256;
            Assert.AreEqual(signedNarrow, mask.IsEmpty && !ABIWordRun.AcceptsEveryWord(t.Kind, t.Width), $"{t}: only narrow signed integers have no mask");
            for (int i = 0; i < 1_500; i++)
            {
                byte[] word = ValidWord(t, rng);
                if ((i & 1) == 1)
                {
                    word[rng.Next(32)] ^= (byte)(1 << rng.Next(8));
                }

                bool model = ABIReference.IsCanonical(t.Model, word);
                if (ABIWordRun.AcceptsEveryWord(t.Kind, t.Width))
                {
                    Assert.IsTrue(model, $"{t} {Convert.ToHexString(word)}");
                }
                else if (!mask.IsEmpty)
                {
                    bool masked = true;
                    for (int b = 0; b < 32; b++)
                    {
                        masked &= (word[b] & mask[b]) == 0;
                    }

                    Assert.AreEqual(model, masked, $"{t} {Convert.ToHexString(word)}");
                }
            }
        }
    }

    [TestMethod]
    public void EveryKernel_AcceptsValidRuns_OfEveryLength()
    {
        foreach (WordType t in Types)
        {
            DeterministicRng rng = NewRng();
            foreach (int count in Counts)
            {
                byte[] run = Run(t, count, rng);
                foreach ((string name, Func<byte[], int, int, bool> accepts) in Kernels(t))
                {
                    Assert.IsTrue(accepts(run, 0, run.Length), $"{t} x{count} {name}");
                }
            }
        }
    }

    [TestMethod]
    public void EveryKernel_RejectsAnyCorruptedWord_AtEveryPositionOfShortRuns_AndTheKeyPositionsOfLongRuns()
    {
        foreach (WordType t in Types)
        {
            DeterministicRng rng = NewRng();
            foreach (int count in Counts.Where(c => c > 0))
            {
                byte[] run = Run(t, count, rng);
                int[] positions = count <= 33
                    ? [.. Enumerable.Range(0, count)]
                    : [0, 1, 2, 3, 4, 5, 7, 8, 9, count / 2, count - 9, count - 8, count - 5, count - 4, count - 3, count - 2, count - 1];
                foreach (int position in positions.Distinct())
                {
                    byte[] original = run.AsSpan(position * 32, 32).ToArray();
                    foreach ((int index, byte value) in Corruptions(t, original).Where((c, k) => count <= 9 || k % 7 == 0 || c.Index is 0 or 31))
                    {
                        run[position * 32 + index] = value;
                        int first = ABIWordRun.FirstRejected(run, t.Kind, t.Width, out ABIStatus status);
                        Assert.AreEqual(position, first, $"{t} x{count}: reference must name word {position} for byte {index}={value:x2}");
                        Assert.AreEqual(ExpectedStatus(t), status, $"{t} x{count} status");
                        foreach ((string name, Func<byte[], int, int, bool> accepts) in Kernels(t))
                        {
                            Assert.IsFalse(accepts(run, 0, run.Length), $"{t} x{count} word {position} byte {index}={value:x2} {name}");
                        }

                        original.CopyTo(run, position * 32);
                    }
                }
            }
        }
    }

    [TestMethod]
    public void EveryKernel_ReadsOnlyItsSlice_OnUnalignedRunsWithPoisonedNeighbours()
    {
        // The run sits at offsets 0..33 inside a larger array; the bytes before and after are non-zero poison that every mask would reject,
        // so a kernel that read past either end (a tail or unrolling error) would report a failure on a valid run.
        foreach (WordType t in Types)
        {
            DeterministicRng rng = NewRng();
            foreach (int count in new[] { 1, 2, 3, 4, 5, 8, 9, 17, 33 })
            {
                byte[] run = Run(t, count, rng);
                for (int offset = 0; offset <= 33; offset++)
                {
                    byte[] backing = new byte[offset + run.Length + 70];
                    Array.Fill(backing, (byte)0xFF);
                    run.CopyTo(backing, offset);
                    foreach ((string name, Func<byte[], int, int, bool> accepts) in Kernels(t))
                    {
                        Assert.IsTrue(accepts(backing, offset, run.Length), $"{t} x{count} at {offset} {name}: valid run next to poison");
                    }

                    // The same run with its last word corrupted is rejected at the same offset (the poison must not mask a failure either).
                    byte[] bad = (byte[])backing.Clone();
                    byte[] last = run.AsSpan(run.Length - 32, 32).ToArray();
                    (int index, byte value) = Corruptions(t, last).FirstOrDefault();
                    if (value != 0)
                    {
                        bad[offset + run.Length - 32 + index] = value;
                        foreach ((string name, Func<byte[], int, int, bool> accepts) in Kernels(t))
                        {
                            Assert.IsFalse(accepts(bad, offset, run.Length), $"{t} x{count} at {offset} {name}: corrupted last word");
                        }
                    }
                }
            }
        }
    }

    [TestMethod]
    public void FindFirstRejected_OfAnEmptyRun_IsAccepted_ForAnyType()
    {
        foreach (WordType t in Types)
        {
            Assert.AreEqual(-1, ABIWordRun.FindFirstRejected(ReadOnlySpan<byte>.Empty, t.Kind, t.Width, out ABIStatus status), $"{t} empty run");
            Assert.AreEqual(ABIStatus.Success, status);
        }
    }

    [TestMethod]
    public void MaskKernels_RefuseAMaskShorterThanAWord()
    {
        byte[] words = new byte[128];
        foreach (int maskLength in new[] { 0, 1, 16, 31 })
        {
            Assert.IsFalse(ABIWordRun.AllMasked128(words, new byte[maskLength]), $"Vector128 mask {maskLength}");
            Assert.IsFalse(ABIWordRun.AllMasked256(words, new byte[maskLength]), $"Vector256 mask {maskLength}");
        }

        Assert.IsTrue(ABIWordRun.AllMasked128(words, new byte[40]), "a longer mask is read for its first 32 bytes only");
        Assert.IsTrue(ABIWordRun.AllMasked256(words, new byte[40]));
    }

    [TestMethod]
    public void RunKernels_RefuseALengthThatIsNotWholeWords_WithoutReadingOutside()
    {
        byte[] backing = new byte[200];
        foreach (WordType t in Types.Where(t => !ABIWordRun.AcceptsEveryWord(t.Kind, t.Width) && !ABIWordRun.Mask(t.Kind, t.Width).IsEmpty))
        {
            byte[] mask = ABIWordRun.Mask(t.Kind, t.Width).ToArray();
            foreach (int length in new[] { 1, 31, 33, 63, 65, 95 })
            {
                Assert.IsFalse(ABIWordRun.AllMasked128(backing.AsSpan(3, length), mask), $"{t} Vector128 {length}");
                Assert.IsFalse(ABIWordRun.AllMasked256(backing.AsSpan(3, length), mask), $"{t} Vector256 {length}");
            }
        }
    }

    // ------------------------------------------------------------------ bounded padding scan

    [TestMethod]
    public void FirstNonZeroPadding_AgreesWithTheScalarLoop_ForEveryLengthPositionAndAlignment()
    {
        // 40 bytes of room before the padding, so the 32-byte window of the vector path (lengths 8 to 31) is inside the input, and non-zero
        // neighbours on both sides that a window anchored or masked wrongly would trip over.
        const int Room = 40;
        for (int length = 0; length <= 40; length++)
        {
            for (int offset = 0; offset <= 3; offset++)
            {
                int start = Room + offset;
                byte[] backing = new byte[start + length + 5];
                Array.Fill(backing, (byte)0xAA);
                backing.AsSpan(start, length).Clear();
                Assert.AreEqual(-1, ABIWordRun.FirstNonZeroPadding(backing, start, length), $"all zero, length {length}");
                for (int position = 0; position < length; position++)
                {
                    foreach (byte value in new byte[] { 1, 0x80, 0xff })
                    {
                        backing[start + position] = value;
                        Assert.AreEqual(position, ABIWordRun.FirstNonZeroPadding(backing, start, length), $"length {length} offset {offset} position {position}");
                        Assert.AreEqual(ABIWord.FirstNonZero(backing.AsSpan(start, length)), ABIWordRun.FirstNonZeroPadding(backing, start, length));
                        backing[start + position] = 0;
                    }
                }

                // Two non-zero bytes: the FIRST one is reported.
                if (length >= 2)
                {
                    backing[start + length - 1] = 5;
                    backing[start + length / 2] = 7;
                    Assert.AreEqual(length / 2, ABIWordRun.FirstNonZeroPadding(backing, start, length));
                }
            }
        }

        // A short input (the window would start before 0 or end after the input) must neither throw nor misreport, with or without room.
        foreach (int total in new[] { 8, 9, 15, 16, 31, 32, 33 })
        {
            for (int length = 1; length <= Math.Min(total, 40); length++)
            {
                byte[] shortInput = new byte[total];
                shortInput[total - 1] = 0x7f;
                Assert.AreEqual(ABIWord.FirstNonZero(shortInput.AsSpan(total - length, length)), ABIWordRun.FirstNonZeroPadding(shortInput, total - length, length), $"total {total} length {length}");
            }
        }
    }

    [TestMethod]
    public void DynamicBytesPadding_EveryLength_EveryNonZeroPosition_ReportsTheFirstOffset()
    {
        ABISchema schema = CompileTuple("(bytes)");
        for (int length = 0; length <= 130; length++)
        {
            byte[] payload = Enumerable.Range(0, length).Select(i => (byte)(i + 1)).ToArray();
            byte[] good = Cat(Word(0x20), BytesValue(payload));
            DecodeOk(good, schema);
            int padding = (32 - (length & 31)) & 31;
            int paddingStart = 64 + length;
            for (int position = 0; position < padding; position++)
            {
                byte[] bad = (byte[])good.Clone();
                bad[paddingStart + position] = 0x01;
                ABIError error = DecodeFails(bad, schema, ABIStatus.InvalidPadding, why: $"bytes({length}) padding byte {position}");
                Assert.AreEqual(paddingStart + position, error.DocumentOffset, $"bytes({length}) padding byte {position}");

                // The first of several wins.
                bad[paddingStart + padding - 1] = 0xff;
                error = DecodeFails(bad, schema, ABIStatus.InvalidPadding);
                Assert.AreEqual(paddingStart + position, error.DocumentOffset);
            }
        }
    }

    [TestMethod]
    public void FirstNonZeroPadding_AnchorsItsWindowAtThePaddingEnd_NotAtTheEndOfTheInput()
    {
        // Data (and a non-zero tail) follow the padding: a window anchored anywhere else would test the wrong bytes and could accept bad padding.
        var rng = NewRng();
        for (int padding = 1; padding <= 31; padding++)
        {
            for (int trailing = 0; trailing <= 70; trailing += 7)
            {
                int paddingStart = 40 + rng.Next(5);
                byte[] input = rng.Bytes(paddingStart + padding + trailing);
                for (int i = 0; i < input.Length; i++)
                {
                    input[i] |= 0x01; // everything non-zero, except what a case clears
                }

                input.AsSpan(paddingStart, padding).Clear();
                Assert.AreEqual(-1, ABIWordRun.FirstNonZeroPadding(input, paddingStart, padding), $"padding {padding} trailing {trailing}");
                for (int position = 0; position < padding; position++)
                {
                    input[paddingStart + position] = 0x80;
                    Assert.AreEqual(position, ABIWordRun.FirstNonZeroPadding(input, paddingStart, padding), $"padding {padding} trailing {trailing} position {position}");
                    input[paddingStart + position] = 0;
                }
            }
        }
    }

    [TestMethod]
    public void DynamicBytesPadding_WithDataAfterIt_ReportsTheFirstBadOffset_AndAcceptsGoodPadding()
    {
        // Two dynamic values: the first one's padding is followed by a second value full of non-zero bytes.
        ABISchema schema = CompileTuple("(bytes,bytes)");
        for (int length = 0; length <= 70; length++)
        {
            byte[] first = Enumerable.Range(0, length).Select(i => (byte)(i + 1)).ToArray();
            byte[] second = Enumerable.Repeat((byte)0xAB, 33).ToArray();
            int firstSize = 32 + ((length + 31) & ~31);
            byte[] good = Cat(Word(0x40), Word((ulong)(0x40 + firstSize)), BytesValue(first), BytesValue(second));
            DecodeOk(good, schema);
            int padding = (32 - (length & 31)) & 31;
            int paddingStart = 0x40 + 32 + length;
            for (int position = 0; position < padding; position++)
            {
                byte[] bad = (byte[])good.Clone();
                bad[paddingStart + position] = 0x01;
                ABIError error = DecodeFails(bad, schema, ABIStatus.InvalidPadding, why: $"length {length} padding byte {position}");
                Assert.AreEqual(paddingStart + position, error.DocumentOffset, $"length {length} padding byte {position}");
            }
        }
    }

    // ------------------------------------------------------------------ the whole validator

    private static string ArrayTuple(WordType t, int count, bool isFixed) => isFixed ? $"({t.Name}[{count}])" : $"({t.Name}[])";

    private static byte[] ArrayDocument(byte[] run, int count, bool isFixed) => isFixed ? (byte[])run.Clone() : Cat(Word(0x20), Word((ulong)count), run);

    [TestMethod]
    public void Validator_AcceptsValidArrays_AndNamesTheFirstBadElement_ForEveryTypeAndLength()
    {
        foreach (WordType t in Types)
        {
            foreach (bool isFixed in new[] { false, true })
            {
                DeterministicRng rng = NewRng();
                foreach (int count in new[] { 1, 2, 3, 4, 5, 7, 8, 9, 16, 17, 31, 33, 64, 65, 129 })
                {
                    ABISchema schema = CompileTuple(ArrayTuple(t, count, isFixed));
                    byte[] run = Run(t, count, rng);
                    DecodeOk(ArrayDocument(run, count, isFixed), schema, Generous);

                    int arrayNode = schema.GetTupleChildren(0)[0].NodeId;
                    int elementNode = schema.GetNode(arrayNode).Element;
                    int dataStart = isFixed ? 0 : 64;
                    foreach (int position in new[] { 0, 1, count / 2, count - 2, count - 1 }.Where(p => p >= 0 && p < count).Distinct())
                    {
                        byte[] original = run.AsSpan(position * 32, 32).ToArray();
                        foreach ((int index, byte value) in Corruptions(t, original).Where((c, k) => k % 5 == 0))
                        {
                            byte[] document = ArrayDocument(run, count, isFixed);
                            document[dataStart + position * 32 + index] = value;
                            ABIError error = DecodeFails(document, schema, ExpectedStatus(t), Generous, $"{t} {(isFixed ? "fixed" : "dynamic")} x{count} element {position} byte {index}");
                            Assert.AreEqual(dataStart + position * 32, error.DocumentOffset, $"{t} x{count} element {position}");
                            Assert.AreEqual(elementNode, error.SchemaNode, $"{t} x{count} element {position} node");
                        }
                    }
                }
            }
        }
    }

    /// <summary>The per-element accounting of spec 6.4 as an explicit loop: the decision the validator must reproduce for a run of elements.</summary>
    private static (ABIStatus Status, int FailedElement) SimulateElements(int count, int firstBadElement, ABIStatus badStatus, int visited, int work, int maxVisited, int maxWork)
    {
        for (int i = 0; i < count; i++)
        {
            if (visited >= maxVisited || work >= maxWork)
            {
                return (ABIStatus.WorkLimitExceeded, i);
            }

            visited++;
            work++;
            if (work >= maxWork)
            {
                return (ABIStatus.WorkLimitExceeded, i);
            }

            work++;
            if (i == firstBadElement)
            {
                return (badStatus, i);
            }
        }

        return (ABIStatus.Success, -1);
    }

    [TestMethod]
    public void Validator_WorkAndVisitedBudgets_AreExactForRuns_WithAndWithoutABadElement()
    {
        // Counters when the first element is entered: root 1/1, then the array value 1/1 (+ pointer and count words for a dynamic array).
        foreach (WordType t in new[] { Types[0], Types[2], Types[5], Types[9], Types[10], Types[15], Types[16], Types[7] })
        {
            foreach (bool isFixed in new[] { false, true })
            {
                DeterministicRng rng = NewRng();
                foreach (int count in new[] { 1, 2, 5, 8, 9, 33 })
                {
                    ABISchema schema = CompileTuple(ArrayTuple(t, count, isFixed));
                    byte[] run = Run(t, count, rng);
                    int visitedBefore = 2;
                    int workBefore = isFixed ? 2 : 4;
                    int dataStart = isFixed ? 0 : 64;
                    var badCases = new List<int> { -1 };
                    byte[] sample = run.AsSpan((count - 1) * 32, 32).ToArray();
                    (int badIndex, byte badValue) = Corruptions(t, sample).FirstOrDefault();
                    if (badValue != 0)
                    {
                        badCases.AddRange(new[] { 0, count / 2, count - 1 }.Distinct());
                    }

                    foreach (int bad in badCases)
                    {
                        byte[] document = ArrayDocument(run, count, isFixed);
                        byte[] saved = document.AsSpan(dataStart + Math.Max(bad, 0) * 32, 32).ToArray();
                        if (bad >= 0)
                        {
                            (int idx, byte val) = Corruptions(t, saved).First();
                            document[dataStart + bad * 32 + idx] = val;
                        }

                        for (int budget = 0; budget < 2; budget++)
                        {
                            // Sweep one budget from "root cannot even start" to "more than needed" and keep the other generous.
                            int need = budget == 0 ? visitedBefore + count : workBefore + 2 * count;
                            for (int limit = Math.Max(1, need - 3); limit <= need + 2; limit++)
                            {
                                int maxVisited = budget == 0 ? limit : 1_000_000;
                                int maxWork = budget == 1 ? limit : 1_000_000;
                                // The array itself must have been entered for the simulation to apply; below that the failure is earlier and cannot depend on the run.
                                if (maxVisited < visitedBefore + 1 || maxWork < workBefore + 1)
                                {
                                    continue;
                                }

                                (ABIStatus expected, int element) = SimulateElements(count, bad, ExpectedStatus(t), visitedBefore, workBefore, maxVisited, maxWork);
                                var limits = new ABIDecodeLimits(16 * 1024 * 1024, 16, 1_000_000, maxVisited, maxWork);
                                bool ok = ABIData.TryDecodeComplete(document, schema, in limits, out _, out ABIStatus status, out ABIError error);
                                string label = $"{t} {(isFixed ? "fixed" : "dynamic")} x{count} bad {bad} {(budget == 0 ? "visited" : "work")} {limit}";
                                Assert.AreEqual(expected == ABIStatus.Success, ok, label);
                                Assert.AreEqual(expected, status, label);
                                if (!ok)
                                {
                                    Assert.AreEqual(dataStart + element * 32, error.DocumentOffset, label + " offset");
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    [TestMethod]
    public void Validator_Run_RespectsTheDepthLimitBeforeAnyElement()
    {
        // The depth check precedes the budget and the word checks for element 0: with depth 2 the array (depth 2) is entered but its
        // elements (depth 3) are not, whatever the words hold. Five elements, so the run path (three or more) is the one that must defer.
        foreach (bool isFixed in new[] { false, true })
        {
            foreach (string element in new[] { "uint8", "address", "bool", "uint256", "bytes4" })
            {
                ABISchema schema = CompileTuple(isFixed ? $"({element}[5])" : $"({element}[])");
                byte[] words = Cat(Filled(0xff), Filled(0xff), Filled(0xff), Filled(0xff), Filled(0xff));
                byte[] document = isFixed ? words : Cat(Word(0x20), Word(5), words);
                var limits = new ABIDecodeLimits(1 << 20, 2, 4096, 16384, 100000);
                ABIError error = DecodeFails(document, schema, ABIStatus.DepthLimitExceeded, limits, $"{element} {(isFixed ? "fixed" : "dynamic")}");
                Assert.AreEqual(isFixed ? 0 : 64, error.DocumentOffset, $"{element} {(isFixed ? "fixed" : "dynamic")}: the first element's head slot");
                Assert.AreEqual(schema.GetNode(schema.GetTupleChildren(0)[0].NodeId).Element, error.SchemaNode);
            }
        }
    }

    [TestMethod]
    public void Validator_RunsInsideOtherContainers_EndAtTheirOwnArray_AndTheParentContinues()
    {
        // head = 3 (uint8[3]) + 1 (pointer to address[]) + 6 ((bool,uint8[2])[2]) + 2 (bytes32[2]) + 1 (uint8) = 13 words = 0x1a0 bytes.
        ABISchema schema = CompileTuple("(uint8[3],address[],(bool,uint8[2])[2],bytes32[2],uint8)");
        byte[] valid = Cat(
            Word(1), Word(2), Word(3), Word(0x1a0),
            Word(1), Word(10), Word(11), Word(0), Word(12), Word(13),
            Word(7), Word(8), Word(9),
            Word(2), Word(0xaaaa), Word(0xbbbb));
        ABIDecodeLimits limits = Generous;
        DecodeOk(valid, schema, limits);

        // A set high byte (byte 5) is invalid for the uint8, bool and address words and invisible in the two bytes32 words (10 and 11);
        // words 3 (pointer) and 13 (count) are structural and covered elsewhere.
        for (int wordIndex = 0; wordIndex < 16; wordIndex++)
        {
            if (wordIndex is 3 or 13)
            {
                continue;
            }

            byte[] bad = (byte[])valid.Clone();
            bad[wordIndex * 32 + 5] = 0x01;
            bool accepted = ABIData.TryDecodeComplete(bad, schema, in limits, out _, out ABIStatus status, out ABIError error);
            if (wordIndex is 10 or 11)
            {
                Assert.IsTrue(accepted, $"bytes32 word {wordIndex} accepts any bytes");
            }
            else
            {
                Assert.IsFalse(accepted, $"word {wordIndex}");
                Assert.AreEqual(ABIStatus.InvalidScalar, status, $"word {wordIndex}");
                Assert.AreEqual(wordIndex * 32, error.DocumentOffset, $"word {wordIndex}");
            }
        }
    }

    // ------------------------------------------------------------------ the whole validator against the old per-element validator

    private static readonly int[] RunLengths = [0, 1, 2, 3, 4, 5, 8, 9, 16, 17, 33];

    private static readonly (ABITypeKind Kind, int Width)[] ElementaryPool =
    [
        (ABITypeKind.UInt, 256), (ABITypeKind.UInt, 8), (ABITypeKind.UInt, 160), (ABITypeKind.Address, 0), (ABITypeKind.Bool, 0),
        (ABITypeKind.FixedBytes, 32), (ABITypeKind.FixedBytes, 4), (ABITypeKind.FixedBytes, 20), (ABITypeKind.Function, 0),
        (ABITypeKind.Int, 256), (ABITypeKind.Int, 24), (ABITypeKind.UFixed, 64), (ABITypeKind.Fixed, 256),
    ];

    private static RElementary RandomElementary(DeterministicRng rng)
    {
        (ABITypeKind kind, int width) = ElementaryPool[rng.Next(ElementaryPool.Length)];
        return new RElementary(kind, width, kind is ABITypeKind.Fixed or ABITypeKind.UFixed ? 3 : 0);
    }

    /// <summary>A random type that favours arrays of static primitives of 0 to 33 elements, with static and dynamic siblings around them.</summary>
    private static RType RandomRunType(DeterministicRng rng, int depth)
    {
        switch (rng.Next(depth > 0 ? 12 : 7))
        {
            case 0:
            case 1:
                return RandomElementary(rng);
            case 2:
                return new RBlobType(isString: rng.Next(2) == 0);
            case 3:
            case 4:
                return new RFixedArrayType(RandomElementary(rng), RunLengths[1 + rng.Next(RunLengths.Length - 1)]);
            case 5:
            case 6:
                return new RDynamicArrayType(RandomElementary(rng));
            case 7:
                return new RTupleType(Enumerable.Range(0, 1 + rng.Next(3)).Select(_ => RandomRunType(rng, depth - 1)).ToArray());
            case 8:
                return new RDynamicArrayType(RandomRunType(rng, depth - 1));
            case 9:
                return new RFixedArrayType(RandomRunType(rng, depth - 1), 1 + rng.Next(3));
            default:
                return new RDynamicArrayType(new RTupleType(Enumerable.Range(0, 1 + rng.Next(3)).Select(_ => (RType)RandomElementary(rng)).ToArray()));
        }
    }

    private static RValue RandomRunValue(RType type, DeterministicRng rng)
    {
        switch (type)
        {
            case RElementary e:
                return new RWord(ValidWord(new WordType("x", e.Kind, e.Width), rng));
            case RBlobType:
                return new RBlob(rng.Bytes(rng.Next(4) == 0 ? 32 * rng.Next(3) + rng.Next(2) : rng.Next(70)));
            case RTupleType t:
                return new RSeq(t.Items.Select(i => RandomRunValue(i, rng)).ToArray());
            case RFixedArrayType f:
                return new RSeq(Enumerable.Range(0, f.Length).Select(_ => RandomRunValue(f.Element, rng)).ToArray());
            case RDynamicArrayType d:
                return new RSeq(Enumerable.Range(0, RunLengths[rng.Next(RunLengths.Length)]).Select(_ => RandomRunValue(d.Element, rng)).ToArray());
            default:
                throw new InvalidOperationException();
        }
    }

    private static bool HasRun(RType type) => type switch
    {
        RFixedArrayType f => (f.Element is RElementary && f.Length >= ABIWordRun.MinimumRun) || HasRun(f.Element),
        RDynamicArrayType d => d.Element is RElementary || HasRun(d.Element),
        RTupleType t => t.Items.Any(HasRun),
        _ => false,
    };

    private static byte[] MutateWord(byte[] document, DeterministicRng rng)
    {
        byte[] bytes = (byte[])document.Clone();
        if (bytes.Length == 0)
        {
            return bytes;
        }

        switch (rng.Next(5))
        {
            case 0: // flip one bit anywhere
                bytes[rng.Next(bytes.Length)] ^= (byte)(1 << rng.Next(8));
                break;
            case 1: // set one byte of some word to a value that violates most scalar rules
                bytes[rng.Next(bytes.Length)] = (byte)(0x80 | rng.Next(128));
                break;
            case 2: // overwrite the high half of a word
                int word = rng.Next(bytes.Length / 32 + (bytes.Length % 32 == 0 ? 0 : 1)) * 32;
                for (int i = 0; i < 16 && word + i < bytes.Length; i++)
                {
                    bytes[word + i] = 0xff;
                }

                break;
            case 3: // clear a whole word
                int w = rng.Next(Math.Max(1, bytes.Length / 32)) * 32;
                bytes.AsSpan(w, Math.Min(32, bytes.Length - w)).Clear();
                break;
            default: // leave canonical
                break;
        }

        return bytes;
    }

    private static int SmallestPassing(Func<int, bool> passes, int highest)
    {
        // passes is monotone in the limit: binary search for the smallest limit that passes.
        int low = 1;
        int high = highest;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (passes(mid))
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return low;
    }

    [TestMethod]
    public void TheValidator_MatchesTheOldPerElementValidator_OnStatusOffsetAndNode_ForRandomSchemasDocumentsAndBudgets()
    {
        int compared = 0;
        int withRun = 0;
        int cutAtAPrimitive = 0;
        int badWord = 0;
        var statuses = new Dictionary<ABIStatus, int>();
        for (ulong seed = 1; seed <= 500; seed++)
        {
            var rng = new DeterministicRng(seed * 7_368_787);
            var root = new RTupleType(Enumerable.Range(0, 1 + rng.Next(4)).Select(_ => RandomRunType(rng, 2)).ToArray());
            ABISchema schema = CompileTuple(root.Text);
            byte[] canonical = ABIReference.EncodeRoot(root, (RSeq)new RSeq(root.Items.Select(i => RandomRunValue(i, rng)).ToArray()));
            withRun += HasRun(root) ? 1 : 0;

            for (int variant = 0; variant < 5; variant++)
            {
                byte[] document = variant == 0 ? canonical : MutateWord(canonical, rng);
                if (variant == 4 && canonical.Length > 0)
                {
                    document = document[..rng.Next(canonical.Length)]; // truncation
                }

                const int Generous = 1_000_000;
                // The needs are those of the CANONICAL document, so the sweep also straddles the budget cut inside a run of a mutated or truncated one.
                int visitedNeeded = SmallestPassing(v => Legacy(canonical, schema, 16, 4096, v, Generous).Status == ABIStatus.Success, 20_000);
                int workNeeded = SmallestPassing(w => Legacy(canonical, schema, 16, 4096, Generous, w).Status == ABIStatus.Success, 200_000);
                var combos = new List<(int Depth, int Visited, int Work)> { (16, Generous, Generous), (2, Generous, Generous), (3, Generous, Generous), (5, Generous, Generous), (32, int.MaxValue, int.MaxValue), (16, 1, 1), (16, 2, 3) };
                foreach (int delta in new[] { -3, -2, -1, 0, 1, 2 })
                {
                    combos.Add((16, Math.Max(1, visitedNeeded + delta), Generous));
                    combos.Add((16, Generous, Math.Max(1, workNeeded + delta)));
                    combos.Add((16, Math.Max(1, visitedNeeded + delta), Math.Max(1, workNeeded + delta)));
                }

                combos.Add((16, Math.Max(1, visitedNeeded / 2), Math.Max(1, workNeeded / 2)));
                foreach ((int depth, int visited, int work) in combos)
                {
                    string label = $"seed {seed} variant {variant} {root.Text} depth {depth} visited {visited} work {work}";
                    (ABIStatus legacyStatus, ABIError legacyError) = Legacy(document, schema, depth, 4096, visited, work);
                    ABIStatus status = ABIValidator.TryValidate(document, schema, 16 * 1024 * 1024, depth, 4096, visited, work, out ABIError error);
                    Assert.AreEqual(legacyStatus, status, label);
                    Assert.AreEqual(legacyError, error, label);
                    statuses[status] = statuses.GetValueOrDefault(status) + 1;
                    compared++;
                    if (status == ABIStatus.WorkLimitExceeded && error.SchemaNode >= 0 && schema.GetNode(error.SchemaNode).Kind <= ABITypeKind.UFixed)
                    {
                        cutAtAPrimitive++; // a budget stop on an elementary value: inside an array run, or at a tuple member
                    }
                    else if (status != ABIStatus.Success && status != ABIStatus.WorkLimitExceeded && error.SchemaNode >= 0 && schema.GetNode(error.SchemaNode).Kind <= ABITypeKind.UFixed)
                    {
                        badWord++;
                    }
                }
            }
        }

        Assert.IsTrue(withRun > 150, $"only {withRun} of 500 schemas contain a run of three or more static elements");
        Assert.IsTrue(compared > 20_000, $"{compared} comparisons");
        Assert.IsTrue(cutAtAPrimitive > 500, $"only {cutAtAPrimitive} budget stops on an elementary value");
        Assert.IsTrue(badWord > 300, $"only {badWord} rejected elementary words");
        foreach (ABIStatus expected in new[] { ABIStatus.Success, ABIStatus.WorkLimitExceeded, ABIStatus.InvalidScalar, ABIStatus.InvalidPadding, ABIStatus.DepthLimitExceeded, ABIStatus.Truncated, ABIStatus.InvalidOffset })
        {
            Assert.IsTrue(statuses.GetValueOrDefault(expected) > 20, $"{expected} occurred {statuses.GetValueOrDefault(expected)} times: the comparison must reach every outcome");
        }

        static (ABIStatus Status, ABIError Error) Legacy(byte[] document, ABISchema schema, int depth, int array, int visited, int work)
        {
            ABIStatus status = ABILegacyValidator.TryValidate(document, schema, 16 * 1024 * 1024, depth, array, visited, work, out ABIError error);
            return (status, error);
        }
    }

    [TestMethod]
    public void Validator_RunsAtDeepFramesAndBesideDynamicSiblings_MatchTheOldValidator()
    {
        // Runs in frames at top >= 2 (inside an array of fixed arrays, inside a static tuple of arrays), two runs in sibling arrays, a run followed by a dynamic sibling.
        string[] tuples =
        [
            "(uint8[4][3])", "(uint8[4][])", "((bool,uint8[4])[2])", "((address[3],uint256[5])[3])", "(uint8[5],uint8[6],bool)", "(uint8[5],string,uint8[6])",
            "(bytes32[],uint8[],address[])", "(uint8[3],(uint8[3],(uint8[3],uint8)[2])[2])", "(address[4][2],bytes32[3])", "(uint256[8],string)",
        ];
        foreach (string tuple in tuples)
        {
            var rng = new DeterministicRng((ulong)tuple.Length * 977);
            RTupleType type = ABIReference.ParseTuple(tuple);
            ABISchema schema = CompileTuple(tuple);
            for (int repeat = 0; repeat < 6; repeat++)
            {
                byte[] canonical = ABIReference.EncodeRoot(type, (RSeq)new RSeq(type.Items.Select(i => RandomRunValue(i, rng)).ToArray()));
                for (int variant = 0; variant < 12; variant++)
                {
                    byte[] document = variant == 0 ? canonical : MutateWord(canonical, rng);
                    foreach (int budget in new[] { 1, 2, 3, 5, 8, 13, 21, 40, 100, 1_000_000 })
                    {
                        foreach (bool visitedLimit in new[] { true, false })
                        {
                            int visited = visitedLimit ? budget : 1_000_000;
                            int work = visitedLimit ? 1_000_000 : budget;
                            ABIStatus legacy = ABILegacyValidator.TryValidate(document, schema, 1 << 24, 16, 4096, visited, work, out ABIError legacyError);
                            ABIStatus status = ABIValidator.TryValidate(document, schema, 1 << 24, 16, 4096, visited, work, out ABIError error);
                            Assert.AreEqual(legacy, status, $"{tuple} repeat {repeat} variant {variant} {(visitedLimit ? "visited" : "work")} {budget}");
                            Assert.AreEqual(legacyError, error, $"{tuple} repeat {repeat} variant {variant} {(visitedLimit ? "visited" : "work")} {budget}");
                        }
                    }
                }
            }
        }
    }

    [TestMethod]
    public void Validator_CountersAfterARun_AreCharged_ForTheSiblingsThatFollow()
    {
        // (uint8[k], bool): the bool is entered after the run, so a missing or wrong charge for the run shifts the exact limit at which it fails.
        foreach (int count in new[] { 3, 4, 5, 8, 9, 17, 33 })
        {
            ABISchema schema = CompileTuple($"(uint8[{count}],bool)");
            byte[] document = Cat(Enumerable.Range(0, count).Select(i => Word((ulong)i)).Append(Word(1)).ToArray());
            // root 1/1, array 1/1, count elements 1/2 each, then the bool 1/2: visited = count + 3, work = 2 count + 4.
            int visited = count + 3;
            int work = 2 * count + 4;
            DecodeOk(document, schema, Limits(visited: visited, work: work));
            ABIError error = DecodeFails(document, schema, ABIStatus.WorkLimitExceeded, Limits(visited: visited - 1), $"{count} visited - 1");
            Assert.AreEqual(count * 32, error.DocumentOffset, $"{count} visited - 1: the bool's slot");
            error = DecodeFails(document, schema, ABIStatus.WorkLimitExceeded, Limits(work: work - 1), $"{count} work - 1");
            Assert.AreEqual(count * 32, error.DocumentOffset, $"{count} work - 1: the bool's slot");
            error = DecodeFails(document, schema, ABIStatus.WorkLimitExceeded, Limits(work: work - 2), $"{count} work - 2");
            Assert.AreEqual(count * 32, error.DocumentOffset, $"{count} work - 2");
            DecodeOk(document, schema, Limits(visited: int.MaxValue, work: int.MaxValue));
        }
    }

    [TestMethod]
    public void PrimitiveKinds_AreExactlyTheEnumValuesUpToUFixed_TheValidatorsRunPathDependsOnThatOrder()
    {
        var elementary = new HashSet<ABITypeKind>
        {
            ABITypeKind.UInt, ABITypeKind.Int, ABITypeKind.Address, ABITypeKind.Bool, ABITypeKind.FixedBytes, ABITypeKind.Function, ABITypeKind.Fixed, ABITypeKind.UFixed,
        };
        foreach (ABITypeKind kind in Enum.GetValues<ABITypeKind>())
        {
            Assert.AreEqual(elementary.Contains(kind), kind <= ABITypeKind.UFixed, $"{kind}: a primitive added after UFixed would silently skip the run path (and a composite added before it would take it)");
        }
    }

    // ------------------------------------------------------------------ the fused writer prologue against forged scratch

    [TestMethod]
    public void WriterPrologue_ForgedCountersAndIndexes_GiveExactStatuses_AndNeverThrow()
    {
        ABISchema schema = CompileTuple("(uint256,uint256,uint256)");
        ABIEncodeLimits limits = new(1 << 16, 8, 64, 40, 90);

        ABIStatus NextWrite(Action<ABIWriteFrame[]> forge)
        {
            var scratch = new ABIWriteFrame[8];
            byte[] destination = new byte[256];
            Assert.IsTrue(ABIWriter.TryBegin(schema, destination, scratch, in limits, out ABIWriter w, out _));
            Assert.IsTrue(w.TryWriteUInt64(1, out _));
            forge(scratch);
            w.TryWriteUInt64(2, out ABIStatus status);
            return status;
        }

        // After the root and one scalar: Values 2, Work 3. The next scalar needs two units of work and one value.
        Assert.AreEqual(ABIStatus.Success, NextWrite(_ => { }));
        Assert.AreEqual(ABIStatus.Success, NextWrite(f => f[0].Frame.Work = 90 - 2), "work + 2 == the limit is allowed");
        Assert.AreEqual(ABIStatus.WorkLimitExceeded, NextWrite(f => f[0].Frame.Work = 90 - 1), "the word unit does not fit");
        Assert.AreEqual(ABIStatus.WorkLimitExceeded, NextWrite(f => f[0].Frame.Work = 90));
        Assert.AreEqual(ABIStatus.WorkLimitExceeded, NextWrite(f => f[0].Frame.Work = int.MaxValue - 1));
        Assert.AreEqual(ABIStatus.WorkLimitExceeded, NextWrite(f => f[0].Frame.Work = int.MaxValue));
        Assert.AreEqual(ABIStatus.Success, NextWrite(f => f[0].Frame.Work = -1));
        Assert.AreEqual(ABIStatus.Success, NextWrite(f => f[0].Frame.Work = int.MinValue));
        Assert.AreEqual(ABIStatus.Success, NextWrite(f => f[0].Frame.Values = 39));
        Assert.AreEqual(ABIStatus.WorkLimitExceeded, NextWrite(f => f[0].Frame.Values = 40));
        Assert.AreEqual(ABIStatus.WorkLimitExceeded, NextWrite(f => f[0].Frame.Values = int.MaxValue));
        Assert.AreEqual(ABIStatus.Success, NextWrite(f => f[0].Frame.Values = int.MinValue));
        foreach (int top in new[] { -1, 8, 9, int.MinValue, int.MaxValue })
        {
            Assert.AreEqual(ABIStatus.InvalidWriterState, NextWrite(f => f[0].Frame.Top = top), $"Top {top}");
        }

        foreach (int top in new[] { 1, 7 })
        {
            // A forged index inside the scratch lands on a never-opened (cleared) frame: no member is expected there.
            Assert.AreEqual(ABIStatus.ElementLimitExceeded, NextWrite(f => f[0].Frame.Top = top), $"Top {top}");
        }

        foreach (int state in new[] { 0, 2, 3, -1, 99, int.MinValue })
        {
            Assert.AreEqual(ABIStatus.InvalidWriterState, NextWrite(f => f[0].Frame.State = state), $"State {state}");
        }

        Assert.AreEqual(ABIStatus.InvalidWriterState, NextWrite(f => f[0].Frame.Epoch++), "a stale epoch");
        foreach (int node in new[] { -1, 3, 100, int.MinValue, int.MaxValue })
        {
            Assert.AreEqual(ABIStatus.InvalidWriterState, NextWrite(f => f[0].Frame.Node = node), $"Node {node}");
        }

        foreach (int cursor in new[] { -1, 257, 1000, int.MinValue, int.MaxValue })
        {
            Assert.AreEqual(ABIStatus.InvalidWriterState, NextWrite(f => f[0].Frame.HeadCursor = cursor), $"HeadCursor {cursor}");
        }

        Assert.AreEqual(ABIStatus.ElementLimitExceeded, NextWrite(f => f[0].Frame.NextChild = f[0].Frame.ChildCount));
        Assert.AreEqual(ABIStatus.InvalidWriterState, NextWrite(f => f[0].Frame.NextChild = -1), "a negative member index is refused, not indexed");
    }

    [TestMethod]
    public void WriterPrologue_CheckOrder_WhenSeveralConditionsHoldAtOnce_IsTheOriginalOrder()
    {
        // The order of the original FindSlot / Slot / ChargeValue / ChargeWord: state and indexes, arity, slot inside the destination, kind,
        // depth, then the budgets. Each case makes two conditions true at once; the first one in that order must be the status.
        ABISchema schema = CompileTuple("(uint256,uint256,uint256)");
        ABIEncodeLimits wide = new(1 << 16, 8, 64, 40, 90);
        ABIEncodeLimits depthOne = new(1 << 16, 1, 64, 40, 90);

        ABIStatus Attempt(ABIEncodeLimits limits, Action<ABIWriteFrame[]> forge, bool wrongKind)
        {
            var scratch = new ABIWriteFrame[8];
            byte[] destination = new byte[256];
            Assert.IsTrue(ABIWriter.TryBegin(schema, destination, scratch, in limits, out ABIWriter w, out _));
            forge(scratch);
            ABIStatus status;
            if (wrongKind)
            {
                w.TryWriteBool(true, out status);
            }
            else
            {
                w.TryWriteUInt64(1, out status);
            }

            return status;
        }

        Assert.AreEqual(ABIStatus.TypeMismatch, Attempt(wide, _ => { }, wrongKind: true));
        Assert.AreEqual(ABIStatus.InvalidWriterState, Attempt(wide, f => f[0].Frame.HeadCursor = -1, wrongKind: true), "the slot check precedes the kind check");
        Assert.AreEqual(ABIStatus.InvalidWriterState, Attempt(wide, f => f[0].Frame.HeadCursor = 1000, wrongKind: true), "the slot check precedes the kind check");
        Assert.AreEqual(ABIStatus.TypeMismatch, Attempt(wide, f => f[0].Frame.Work = 90, wrongKind: true), "the kind check precedes the budgets");
        Assert.AreEqual(ABIStatus.TypeMismatch, Attempt(wide, f => f[0].Frame.Values = 40, wrongKind: true), "the kind check precedes the budgets");
        Assert.AreEqual(ABIStatus.TypeMismatch, Attempt(depthOne, _ => { }, wrongKind: true), "the kind check precedes the depth check");
        Assert.AreEqual(ABIStatus.DepthLimitExceeded, Attempt(depthOne, _ => { }, wrongKind: false), "the first scalar is at depth 2");
        Assert.AreEqual(ABIStatus.DepthLimitExceeded, Attempt(depthOne, f => f[0].Frame.Work = 90, wrongKind: false), "depth precedes the budgets");
        Assert.AreEqual(ABIStatus.DepthLimitExceeded, Attempt(depthOne, f => f[0].Frame.Values = 40, wrongKind: false), "depth precedes the budgets");
        Assert.AreEqual(ABIStatus.ElementLimitExceeded, Attempt(wide, f => { f[0].Frame.NextChild = f[0].Frame.ChildCount; f[0].Frame.HeadCursor = -1; }, wrongKind: true), "arity precedes the slot check");
        Assert.AreEqual(ABIStatus.InvalidWriterState, Attempt(wide, f => { f[0].Frame.ChildCount = 10; f[0].Frame.NextChild = 3; }, wrongKind: false), "a member index past the tuple's members is refused, not indexed");
        Assert.AreEqual(ABIStatus.InvalidWriterState, Attempt(wide, f => { f[0].Frame.ChildCount = 10; f[0].Frame.NextChild = 3; }, wrongKind: true));
        Assert.AreEqual(ABIStatus.ElementLimitExceeded, Attempt(wide, f => { f[0].Frame.Node = 99; f[0].Frame.NextChild = f[0].Frame.ChildCount; }, wrongKind: false), "the arity check precedes the node range check");
        Assert.AreEqual(ABIStatus.InvalidWriterState, Attempt(wide, f => { f[0].Frame.Node = 99; f[0].Frame.HeadCursor = -1; }, wrongKind: true), "the node range check precedes the slot and kind checks");
    }

    [TestMethod]
    public void SchemaNodes_CarryTheNoRuleFlag_ExactlyForTheKindsThatAcceptEveryWord()
    {
        string[] types = ["uint256", "uint248", "uint8", "int256", "int248", "int8", "bytes32", "bytes31", "bytes1", "address", "bool", "function", "fixed256x18", "ufixed256x18", "fixed248x18", "ufixed8x2"];
        bool[] expected = [true, false, false, true, false, false, true, false, false, false, false, false, true, true, false, false];
        ABISchema schema = CompileTuple("(" + string.Join(",", types) + ")");
        for (int i = 0; i < types.Length; i++)
        {
            ref readonly ABISchemaNode node = ref schema.GetNode(schema.GetTupleChildren(0)[i].NodeId);
            Assert.AreEqual(expected[i], node.AcceptsEveryWord, types[i]);
            Assert.AreEqual(ABIWordRun.AcceptsEveryWord(node.Kind, node.Width), node.AcceptsEveryWord, types[i]);
        }

        // Composite and dynamic nodes never accept "every word".
        ABISchema composite = CompileTuple("(uint256[],bytes,string,(uint256,uint256),uint256[2])");
        for (int i = 0; i < 5; i++)
        {
            Assert.IsFalse(composite.GetNode(composite.GetTupleChildren(0)[i].NodeId).AcceptsEveryWord, $"member {i}");
        }
    }

    [TestMethod]
    public void FindFirstRejected_FailsClosed_OnATrailingPartialWord()
    {
        byte[] backing = new byte[200];
        foreach (WordType t in Types)
        {
            foreach (int length in new[] { 1, 31, 33, 63, 65, 95, 129 })
            {
                int index = ABIWordRun.FindFirstRejected(backing.AsSpan(3, length), t.Kind, t.Width, out ABIStatus status);
                Assert.AreEqual(length / 32, index, $"{t} length {length}");
                Assert.AreEqual(ABIStatus.Truncated, status, $"{t} length {length}");
            }
        }
    }

    [TestMethod]
    public void RunAndPaddingPaths_AllocateNothing_OnSuccessAndOnARejectedRun()
    {
        // The run kernels (mask, Vector128/256, reference) and the vector padding check, success and failure, narrow and wide kinds.
        string[] tuples = ["(address[8])", "(bool[33])", "(bytes4[5])", "(int24[5])", "(uint8[],bytes)", "(function[4])", "(uint256[9])"];
        var cases = new List<(ABISchema Schema, byte[] Good, byte[] Bad)>();
        var rng = new DeterministicRng(99);
        foreach (string tuple in tuples)
        {
            RTupleType type = ABIReference.ParseTuple(tuple);
            byte[] good = ABIReference.EncodeRoot(type, new RSeq(type.Items.Select(i => RandomRunValue(i, rng)).ToArray()));
            byte[] bad = (byte[])good.Clone();
            bad[bad.Length > 40 ? 40 : 0] ^= 0x80;
            cases.Add((CompileTuple(tuple), good, bad));
        }

        foreach (int length in new[] { 8, 9, 31, 33, 40, 63, 65 })
        {
            ABISchema schema = CompileTuple("(bytes)");
            byte[] good = Cat(Word(0x20), BytesValue(Enumerable.Range(0, length).Select(i => (byte)(i + 1)).ToArray()));
            byte[] bad = (byte[])good.Clone();
            bad[^1] = 1;
            cases.Add((schema, good, bad));
        }

        ABIDecodeLimits limits = Generous;
        int Probe()
        {
            int ok = 0;
            foreach ((ABISchema schema, byte[] good, byte[] bad) in cases)
            {
                ok += ABIData.TryDecodeComplete(good, schema, in limits, out _, out _, out _) ? 1 : 0;
                ok += ABIData.TryDecodeComplete(bad, schema, in limits, out _, out _, out _) ? 1 : 0;
            }

            return ok;
        }

        for (int warm = 0; warm < 50; warm++)
        {
            Probe();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            Probe();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(0, allocated, "decoding must not allocate on any path");
    }

    [TestMethod]
    public void Validator_RunsUnderTheDeepFrameTier_MatchTheOldValidator()
    {
        // More than 8 nested containers selects the 32-frame tier; the run sits at the bottom, at the exact depth boundary and beyond.
        foreach (string inner in new[] { "uint8[3]", "address[4]", "bool[5]", "bytes32[3]", "uint8[]" })
        {
            string text = inner;
            for (int levels = 0; levels < 9; levels++)
            {
                text += levels % 2 == 0 ? "[1]" : "[]";
            }

            ABISchema schema = CompileTuple($"({text})");
            RTupleType type = ABIReference.ParseTuple($"({text})");
            var rng = new DeterministicRng((ulong)text.Length * 31);
            for (int repeat = 0; repeat < 4; repeat++)
            {
                byte[] canonical = ABIReference.EncodeRoot(type, new RSeq(type.Items.Select(i => RandomRunValue(i, rng)).ToArray()));
                for (int variant = 0; variant < 6; variant++)
                {
                    byte[] document = variant == 0 ? canonical : MutateWord(canonical, rng);
                    foreach (int depth in new[] { 2, 8, 9, 10, 11, 12, 13, 16, 32 })
                    {
                        foreach (int budget in new[] { 1, 5, 12, 25, 60, 1_000_000 })
                        {
                            foreach (bool visitedLimit in new[] { true, false })
                            {
                                int visited = visitedLimit ? budget : 1_000_000;
                                int work = visitedLimit ? 1_000_000 : budget;
                                ABIStatus legacy = ABILegacyValidator.TryValidate(document, schema, 1 << 24, depth, 4096, visited, work, out ABIError legacyError);
                                ABIStatus status = ABIValidator.TryValidate(document, schema, 1 << 24, depth, 4096, visited, work, out ABIError error);
                                string label = $"{text} depth {depth} {(visitedLimit ? "visited" : "work")} {budget} variant {variant}";
                                Assert.AreEqual(legacy, status, label);
                                Assert.AreEqual(legacyError, error, label);
                            }
                        }
                    }
                }
            }
        }
    }

    [TestMethod]
    public void Validator_TwoTightBudgets_CutAtDifferentElements_MatchTheOldValidator()
    {
        // Both budgets limit one run, cutting at different elements, with and without a bad word at the cut and beyond it.
        foreach (string element in new[] { "uint8", "address", "bool", "bytes4", "uint256" })
        {
            foreach (int count in new[] { 3, 4, 5, 9, 17 })
            {
                foreach (bool isFixed in new[] { false, true })
                {
                    ABISchema schema = CompileTuple(isFixed ? $"({element}[{count}],bool)" : $"({element}[],bool)");
                    var rng = new DeterministicRng((ulong)(count * 131 + element.Length));
                    RTupleType type = ABIReference.ParseTuple(isFixed ? $"({element}[{count}],bool)" : $"({element}[],bool)");
                    byte[] canonical = ABIReference.EncodeRoot(type, new RSeq(type.Items.Select(i => count == 0 ? new RSeq([]) : RandomRunValue(i, rng)).ToArray()));
                    // The dynamic form carries a random count: re-encode with the requested one so the run has exactly `count` elements.
                    if (!isFixed)
                    {
                        canonical = ABIReference.EncodeRoot(type, new RSeq([new RSeq(Enumerable.Range(0, count).Select(_ => RandomRunValue(new RElementary(ElementaryOf(element).Kind, ElementaryOf(element).Width, 0), rng)).ToArray()), RandomRunValue(type.Items[1], rng)]));
                    }

                    byte[][] documents = [canonical, MutateWord(canonical, rng), MutateWord(canonical, rng)];
                    foreach (byte[] document in documents)
                    {
                        for (int v = 1; v <= count + 6; v++)
                        {
                            for (int w = 1; w <= 2 * count + 8; w += 1)
                            {
                                ABIStatus legacy = ABILegacyValidator.TryValidate(document, schema, 1 << 24, 16, 4096, v, w, out ABIError legacyError);
                                ABIStatus status = ABIValidator.TryValidate(document, schema, 1 << 24, 16, 4096, v, w, out ABIError error);
                                string label = $"{element}[{(isFixed ? count.ToString() : "")}] x{count} visited {v} work {w}";
                                Assert.AreEqual(legacy, status, label);
                                Assert.AreEqual(legacyError, error, label);
                            }
                        }
                    }
                }
            }
        }
    }

    private static RElementary ElementaryOf(string name) => name switch
    {
        "uint8" => new(ABITypeKind.UInt, 8, 0),
        "address" => new(ABITypeKind.Address, 0, 0),
        "bool" => new(ABITypeKind.Bool, 0, 0),
        "bytes4" => new(ABITypeKind.FixedBytes, 4, 0),
        _ => new(ABITypeKind.UInt, 256, 0),
    };
}
