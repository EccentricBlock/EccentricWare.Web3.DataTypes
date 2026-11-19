using System.Text;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABIDecodeTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>Which fuzz target a corpus file feeds.</summary>
internal enum ABIFuzzKind
{
    Document,
    Script,
    SchemaText,
}

/// <summary>One corpus file: <c>Name</c> (no extension) and the raw bytes the target receives.</summary>
internal sealed record ABIFuzzSeed(ABIFuzzKind Kind, string Name, byte[] Input);

/// <summary>
/// The deterministic generator of the seed corpus. The seeds are generated IN MEMORY at test time and replayed through the targets by
/// <c>ABIFuzzCorpusReplayTests</c>; nothing generated is stored in the repository (a stored copy of generator output compared with the generator
/// proves nothing). The <c>Fuzz/Corpus</c> folder holds only inputs a fuzzer or a reviewer FOUND (see its README), which are replayed too.
/// File name: <c>&lt;category&gt;-&lt;schema&gt;-&lt;Status&gt;-&lt;detail&gt;</c>; the status is what the library returns under the default limits
/// (documents), at the roomy encode limits (scripts) or for the function signature compiler (schema text). For documents the accept/reject
/// verdict of every seed is also checked against the independent reference model by <see cref="ABIFuzzTarget.Run"/>, so a wrong status in a
/// name can only ever pin a failure kind, never bless a wrong verdict.
/// </summary>
internal static class ABIFuzzCorpus
{
    private static readonly (string Name, byte[] Word)[] SpecialWords =
    [
        ("zero", Word(0)),
        ("w20", Word(0x20)),
        ("w21", Word(0x21)),
        ("pow31", Word(0x8000_0000)),
        ("allff", Filled(0xff)),
        ("highbyte", [1, .. new byte[31]]),
    ];

    /// <summary>Fixtures that get the mutation families (all structurally different, all with at least one pointer or padded scalar).</summary>
    private static readonly string[] MutationBases =
    [
        nameof(ABIFixtures.TransferVitalik1Eth), nameof(ABIFixtures.ExactInputSingleWethToUsdc), nameof(ABIFixtures.MulticallTwoTransfers388),
        nameof(ABIFixtures.DocsSamDaveTrue123), nameof(ABIFixtures.DocsFExample), nameof(ABIFixtures.DocsGNestedArrays),
        nameof(ABIFixtures.ScalarsMixedMaxima), nameof(ABIFixtures.ScalarsBoolAddressBytes), nameof(ABIFixtures.NestedMixedTuples),
        nameof(ABIFixtures.TupleArrayAddressUintBytes), nameof(ABIFixtures.FixedArrayOfDynamicTuples), nameof(ABIFixtures.EmptyDynamicsInsideNestedTuple),
        nameof(ABIFixtures.DynamicStringArray), nameof(ABIFixtures.MalformedUtf8Payload), nameof(ABIWriteFixtures.SafeBatchTransferFromCall),
        nameof(ABIWriteFixtures.BytesPairArrays),
    ];

    private static readonly string[] ScriptBases =
    [
        nameof(ABIFixtures.TransferVitalik1Eth), nameof(ABIFixtures.DocsBaz69True), nameof(ABIFixtures.DocsSamDaveTrue123),
        nameof(ABIFixtures.NestedMixedTuples), nameof(ABIFixtures.MulticallTwoTransfers388), nameof(ABIFixtures.ScalarsBoolAddressBytes),
        nameof(ABIWriteFixtures.NestedTupleArrays), nameof(ABIWriteFixtures.SafeBatchTransferFromCall),
    ];

    internal static IEnumerable<ABIFuzzSeed> Seeds()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ABIFuzzSeed seed in DocumentSeeds().Concat(ScriptSeeds()).Concat(SchemaTextSeeds()))
        {
            if (!seen.Add(seed.Name))
            {
                throw new InvalidOperationException($"duplicate corpus name {seed.Name}");
            }

            yield return seed;
        }
    }

    // ------------------------------------------------------------------ fixtures

    private sealed record Base(string Name, ABIFuzzSchema Schema, byte[] Document);

    private static IEnumerable<Base> FixtureBases()
    {
        foreach (ABIFixtureCatalog.Fixture f in ABIFixtureCatalog.All)
        {
            yield return new Base(f.Name, ABIFuzzSchemas.ByTuple(f.Tuple), ABIFixtureCatalog.ArgumentBytes(f));
        }

        foreach (ABIWriteFixtures.Fixture f in ABIWriteFixtures.All)
        {
            byte[] all = Convert.FromHexString(f.Hex);
            yield return new Base(f.Name, ABIFuzzSchemas.ByTuple(f.Tuple), f.IsCall ? all[4..] : all);
        }
    }

    // ------------------------------------------------------------------ naming

    private static string Slug(string text)
    {
        var builder = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        string slug = builder.ToString().Trim('-');
        return slug.Length > 60 ? slug[..60].TrimEnd('-') : slug;
    }

    private static ABIFuzzSeed Document(string category, ABIFuzzSchema schema, byte[] document, string detail) =>
        new(ABIFuzzKind.Document, $"{category}-{schema.Name}-{StatusOf(schema, document)}-{Slug(detail)}", [(byte)schema.Index, .. document]);

    private static ABIStatus StatusOf(ABIFuzzSchema schema, byte[] document)
    {
        var limits = ABIDecodeLimits.Default;
        _ = ABIData.TryDecodeComplete(document, schema.Schema, in limits, out _, out ABIStatus status);
        return status;
    }

    // ------------------------------------------------------------------ documents

    private static IEnumerable<ABIFuzzSeed> DocumentSeeds()
    {
        Base[] bases = FixtureBases().ToArray();
        foreach (Base b in bases)
        {
            yield return Document("fixture", b.Schema, b.Document, b.Name);
        }

        // Reference-encoder documents for EVERY registry schema: one element per array, and two seeded random values.
        foreach (ABIFuzzSchema schema in ABIFuzzSchemas.All)
        {
            yield return Document("reference", schema, ABIReference.EncodeRoot(schema.Type, MinimalValue(schema.Type)), "minimal");
            if (!schema.Name.StartsWith("deep", StringComparison.Ordinal))
            {
                for (ulong seed = 1; seed <= 2; seed++)
                {
                    var rng = new DeterministicRng(seed * 7919 + (ulong)schema.Index);
                    yield return Document("reference", schema, ABIReference.EncodeRoot(schema.Type, ABIReferenceGenerator.RandomValue(rng, schema.Type)), $"random{seed}");
                }
            }
        }

        // Mutation families over structurally different real documents.
        foreach (string name in MutationBases)
        {
            Base b = bases.Single(x => x.Name == name);
            byte[] doc = b.Document;
            string label = b.Name;

            var lengths = new SortedSet<int> { 0, 1, 31, doc.Length - 1 };
            for (int length = 32; length < doc.Length; length += 32)
            {
                lengths.Add(length);
            }

            foreach (int length in lengths.Where(l => l >= 0 && l < doc.Length).Take(40))
            {
                yield return Document("truncated", b.Schema, doc[..length], $"{label} to {length}");
            }

            yield return Document("trailing", b.Schema, [.. doc, 0x00], $"{label} plus one zero byte");
            yield return Document("trailing", b.Schema, [.. doc, 0xff], $"{label} plus one ff byte");
            yield return Document("trailing", b.Schema, [.. doc, .. new byte[32]], $"{label} plus one zero word");

            byte[] lastByteSet = (byte[])doc.Clone();
            if (lastByteSet.Length > 0)
            {
                lastByteSet[^1] = 0x01;
                yield return Document("padding", b.Schema, lastByteSet, $"{label} last byte 01");
            }

            int words = Math.Min(doc.Length / 32, 12);
            for (int w = 0; w < words; w++)
            {
                foreach ((string specialName, byte[] special) in SpecialWords)
                {
                    byte[] mutant = (byte[])doc.Clone();
                    special.CopyTo(mutant, w * 32);
                    if (!mutant.AsSpan().SequenceEqual(doc))
                    {
                        yield return Document("word", b.Schema, mutant, $"{label} w{w} {specialName}");
                    }
                }
            }

            for (int w = 0; w < Math.Min(doc.Length / 32, 8); w++)
            {
                yield return Document("insert", b.Schema, [.. doc[..(w * 32)], .. new byte[32], .. doc[(w * 32)..]], $"{label} zero word before w{w}");
                yield return Document("delete", b.Schema, [.. doc[..(w * 32)], .. doc[((w + 1) * 32)..]], $"{label} drop w{w}");
            }
        }

        foreach (ABIFuzzSeed seed in HostileDocuments())
        {
            yield return seed;
        }
    }

    private static IEnumerable<ABIFuzzSeed> HostileDocuments()
    {
        (string Name, byte[] Count)[] counts =
        [
            ("max256", Filled(0xff)),
            ("pow255", [0x80, .. new byte[31]]),
            ("pow31", Word(0x8000_0000)),
            ("intmax", Word(int.MaxValue)),
            ("limit-plus-1", Word(4097)),
            ("limit", Word(4096)),
            ("one", Word(1)),
        ];
        foreach ((string name, byte[] count) in counts)
        {
            yield return Document("hostile", ABIFuzzSchemas.ByName("uintArray"), Cat(Word(0x20), count), $"uint array count {name}");
            yield return Document("hostile", ABIFuzzSchemas.ByName("bytes"), Cat(Word(0x20), count), $"bytes length {name}");
            yield return Document("hostile", ABIFuzzSchemas.ByName("emptyTupleArray"), Cat(Word(0x20), count), $"empty tuple array count {name}");
            yield return Document("hostile", ABIFuzzSchemas.ByName("stringArray"), Cat(Word(0x20), count, Word(0x20)), $"string array count {name}");
        }

        // Zero-width elements: bounded by the visited and work budgets, never by the byte size.
        yield return Document("hostile", ABIFuzzSchemas.ByName("emptyTupleArray"), Cat(Word(0x20), Word(0)), "empty tuple array empty");
        yield return Document("hostile", ABIFuzzSchemas.ByName("emptyTupleFixedArray"), [], "empty tuple fixed array");
        yield return Document("hostile", ABIFuzzSchemas.ByName("emptyTuples"), [], "tuples of empty tuples");
        yield return Document("hostile", ABIFuzzSchemas.ByName("emptyTuples"), new byte[32], "tuples of empty tuples plus a word");
        yield return Document("hostile", ABIFuzzSchemas.ByName("empty"), [], "no arguments");
        yield return Document("hostile", ABIFuzzSchemas.ByName("empty"), [0], "no arguments plus a byte");
        yield return Document("hostile", ABIFuzzSchemas.ByName("emptyTuple"), new byte[32], "empty tuple plus a word");

        // Five arrays of 4,096 zero-width elements: 20,486 values, over the default visited budget (16,384) with a document of 320 bytes.
        byte[] fiveArrays = Cat(Word(0xa0), Word(0xc0), Word(0xe0), Word(0x100), Word(0x120), Word(4096), Word(4096), Word(4096), Word(4096), Word(4096));
        yield return Document("hostile", ABIFuzzSchemas.ByName("emptyTupleArrays5"), fiveArrays, "five arrays of 4096 zero-width elements");
        yield return Document("hostile", ABIFuzzSchemas.ByName("emptyTupleArrays5"), Cat(Word(0xa0), Word(0xc0), Word(0xe0), Word(0x100), Word(0x120), Word(4096), Word(4096), Word(4096), Word(0), Word(0)), "three arrays of 4096 zero-width elements");

        // Arrays of arrays of zero-width elements: n inner arrays, canonical and with reversed pointers.
        foreach (int n in new[] { 2, 3, 64 })
        {
            var pointers = new List<byte[]>();
            for (int i = 0; i < n; i++)
            {
                pointers.Add(Word((ulong)(32 * n + 32 * i)));
            }

            byte[] canonical = Cat(Word(0x20), Word((ulong)n), Cat([.. pointers]), new byte[32 * n]);
            yield return Document("hostile", ABIFuzzSchemas.ByName("emptyTupleArrayArray"), canonical, $"{n} inner arrays");

            var reversed = new List<byte[]>(pointers);
            reversed.Reverse();
            yield return Document("hostile", ABIFuzzSchemas.ByName("emptyTupleArrayArray"), Cat(Word(0x20), Word((ulong)n), Cat([.. reversed]), new byte[32 * n]), $"{n} inner arrays reversed pointers");
            yield return Document("hostile", ABIFuzzSchemas.ByName("emptyTupleArrayArray"), Cat(Word(0x20), Word((ulong)n), Cat([.. pointers.Select(_ => pointers[0])]), new byte[32 * n]), $"{n} inner arrays aliased pointers");
        }

        // The default visited-value budget (16,384) to the value: visited = root + 5 arrays + the sum of the counts, so counts summing to 16,378 are the last
        // accepted document and 16,379 the first refused one (WorkLimitExceeded).
        foreach (ulong lastCount in new ulong[] { 4090, 4091 })
        {
            yield return Document("limit", ABIFuzzSchemas.ByName("emptyTupleArrays5"),
                Cat(Word(0xa0), Word(0xc0), Word(0xe0), Word(0x100), Word(0x120), Word(4096), Word(4096), Word(4096), Word(lastCount), Word(0)),
                $"five zero-width arrays totalling {12288 + lastCount} elements");
        }

        // Boundaries of the tight limit set's element cap (16) and byte cap (4,096), and of the default element and byte limits. The tight value (64) and
        // work (256) budgets are always preceded by the element cap for these shapes, so their exact boundaries are not seeded; ABIDecodeLimitsTests pins them.
        foreach (int n in new[] { 14, 15, 16, 17, 61, 62, 63, 64, 100, 126, 127, 4095, 4096, 4097 })
        {
            yield return Document("limit", ABIFuzzSchemas.ByName("uintArray"), Cat(Word(0x20), Word((ulong)n), new byte[32 * n]), $"uint array of {n}");
        }

        foreach (int length in new[] { 4096 - 64, 4096 - 63, 4096 - 33, 4096 - 32, 262_144 - 64, 262_144 - 63 })
        {
            byte[] payload = new byte[length];
            yield return Document("limit", ABIFuzzSchemas.ByName("bytes"), Cat(Word(0x20), Word((ulong)length), Padded(payload)), $"bytes of {length}");
        }

        // The deep schemas at and over the frame tiers.
        foreach (string name in new[] { "deepArraysShallowFrames", "deepArraysDeepFrames", "deepMixedTuples" })
        {
            ABIFuzzSchema schema = ABIFuzzSchemas.ByName(name);
            byte[] canonical = ABIReference.EncodeRoot(schema.Type, MinimalValue(schema.Type));
            yield return Document("hostile", schema, canonical, "one element per level");
            yield return Document("hostile", schema, [.. canonical, 0], "one element per level plus a byte");
            if (canonical.Length >= 32)
            {
                yield return Document("hostile", schema, canonical[..^32], "one element per level minus a word");
            }
        }

        // A pointer one word off in every position of the multi-pointer documents.
        yield return Document("hostile", ABIFuzzSchemas.ByName("sam"), Cat(Word(0x60), Word(1), Word(0xa0), Word(4), Padded(Ascii("dave")), Word(3), Word(1), Word(2), Word(3)), "sam canonical");
        yield return Document("hostile", ABIFuzzSchemas.ByName("sam"), Cat(Word(0x60), Word(1), Word(0xc0), Word(4), Padded(Ascii("dave")), Word(3), Word(1), Word(2), Word(3)), "sam array pointer with a gap");
        yield return Document("hostile", ABIFuzzSchemas.ByName("sam"), Cat(Word(0x20), Word(1), Word(0xa0), Word(4), Padded(Ascii("dave")), Word(3), Word(1), Word(2), Word(3)), "sam bytes pointer into the head");
        yield return Document("hostile", ABIFuzzSchemas.ByName("sam"), Cat(Word(0x60), Word(1), Word(0x60), Word(4), Padded(Ascii("dave")), Word(3), Word(1), Word(2), Word(3)), "sam array pointer aliasing the bytes");
    }

    // ------------------------------------------------------------------ reference helpers

    internal static RSeq MinimalValue(RTupleType type) => new(type.Items.Select(MinimalValue).ToArray());

    /// <summary>The smallest non-trivial canonical value: zero words, three-byte payloads, one element per dynamic array.</summary>
    private static RValue MinimalValue(RType type) => type switch
    {
        RElementary => new RWord(new byte[32]),
        RBlobType => new RBlob([1, 2, 3]),
        RTupleType t => new RSeq(t.Items.Select(MinimalValue).ToArray()),
        RFixedArrayType f => new RSeq(Enumerable.Range(0, f.Length).Select(_ => MinimalValue(f.Element)).ToArray()),
        RDynamicArrayType d => new RSeq([MinimalValue(d.Element)]),
        _ => throw new InvalidOperationException(),
    };

    // ------------------------------------------------------------------ scripts

    private static IEnumerable<ABIFuzzSeed> ScriptSeeds()
    {
        Base[] bases = FixtureBases().ToArray();
        foreach (Base b in bases)
        {
            if (!ABIReference.TryDecodePermissive(b.Schema.Type, b.Document, out RSeq? value))
            {
                continue; // only fixtures the reference model can decode carry a script (all of them do; the guard keeps the generator total)
            }

            List<Ev> events = ABIWriteTestSupport.Script(b.Schema.Type, value!, rawStrings: true);
            if (events.Any(e => e.Data is { Length: > ushort.MaxValue }))
            {
                continue;
            }

            yield return Script("fixture", b.Schema, events, b.Name);

            if (!ScriptBases.Contains(b.Name))
            {
                continue;
            }

            foreach ((string name, List<Ev> mutated) in ScriptMutations(events))
            {
                yield return Script("mutated", b.Schema, mutated, $"{b.Name} {name}");
            }
        }

        // Wrong values for the next slot, on the simplest schemas.
        ABIFuzzSchema baz = ABIFuzzSchemas.ByName("baz");
        yield return Script("value", baz, [Ev.U64(69), Ev.Bool(true)], "baz canonical");
        yield return Script("value", baz, [Ev.U64(1UL << 32), Ev.Bool(true)], "baz uint32 overflow");
        yield return Script("value", baz, [Ev.I64(-1), Ev.Bool(true)], "baz negative into uint32");
        yield return Script("value", baz, [Ev.Bool(true), Ev.U64(69)], "baz swapped kinds");
        yield return Script("value", baz, [Ev.Begin("tuple"), Ev.End, Ev.Bool(true)], "baz tuple where a number belongs");
        yield return Script("value", ABIFuzzSchemas.ByName("signedMix"), [Ev.I64(-128), Ev.I64(-32769), Ev.I64(0), Ev.I64(0), Ev.I64(0)], "int16 underflow");
        yield return Script("value", ABIFuzzSchemas.ByName("signedMix"), [Ev.I64(-128), Ev.I64(-32768), Ev.I64(long.MinValue), Ev.I64(long.MaxValue), Ev.I64(-1)], "int extrema");
        yield return Script("value", ABIFuzzSchemas.ByName("string"), [Ev.Text([0xff, 0xfe])], "invalid utf8 string");
        yield return Script("value", ABIFuzzSchemas.ByName("string"), [Ev.Blob([0xff, 0xfe])], "raw bytes into a string slot");
        yield return Script("value", ABIFuzzSchemas.ByName("string"), [Ev.Text(Utf8("héllo € \U0001F600"))], "multibyte utf8 string");
        yield return Script("value", ABIFuzzSchemas.ByName("uintArray"), [Ev.Begin("dynamic", -1), Ev.End], "negative count");
        yield return Script("value", ABIFuzzSchemas.ByName("uintArray"), [Ev.Begin("dynamic", int.MaxValue), Ev.End], "int max count");
        yield return Script("value", ABIFuzzSchemas.ByName("uintArray"), [Ev.Begin("dynamic", 2), Ev.U64(1), Ev.End], "one element short");
        yield return Script("value", ABIFuzzSchemas.ByName("uintArray"), [Ev.Begin("dynamic", 1), Ev.U64(1), Ev.U64(2), Ev.End], "one element over");
        yield return Script("value", ABIFuzzSchemas.ByName("emptyTupleArray"), [Ev.Begin("dynamic", 4096), Ev.End], "4096 empty tuples without elements");
        yield return Script("value", ABIFuzzSchemas.ByName("empty"), [], "no arguments");
        yield return Script("value", ABIFuzzSchemas.ByName("empty"), [Ev.U64(1)], "argument for no arguments");
        yield return Script("value", ABIFuzzSchemas.ByName("emptyTuples"), [Ev.Begin("tuple"), Ev.End, Ev.Begin("tuple"), Ev.Begin("tuple"), Ev.End, Ev.End], "nested empty tuples");
        yield return Script("value", ABIFuzzSchemas.ByName("deepArraysDeepFrames"), DeepScript(11), "deep arrays at their depth");
        yield return Script("value", ABIFuzzSchemas.ByName("deepArraysDeepFrames"), DeepScript(12), "deep arrays one level too deep");
    }

    private static List<Ev> DeepScript(int levels)
    {
        var events = new List<Ev>();
        for (int i = 0; i < levels; i++)
        {
            events.Add(Ev.Begin("dynamic", 1));
        }

        events.Add(Ev.Uint256(new byte[32]));
        for (int i = 0; i < levels; i++)
        {
            events.Add(Ev.End);
        }

        return events;
    }

    private static IEnumerable<(string Name, List<Ev> Events)> ScriptMutations(List<Ev> events)
    {
        if (events.Count == 0)
        {
            yield break;
        }

        yield return ("without the last event", events[..^1]);
        yield return ("without the first event", events[1..]);
        yield return ("first event twice", [events[0], .. events]);
        yield return ("last event twice", [.. events, events[^1]]);
        yield return ("an extra uint64", [.. events, Ev.U64(1)]);
        yield return ("an end first", [Ev.End, .. events]);
        yield return ("half of the events", events[..(events.Count / 2)]);
        yield return ("a stray tuple", [.. events[..Math.Min(1, events.Count)], Ev.Begin("tuple"), .. events[Math.Min(1, events.Count)..]]);
        if (events.Count > 1)
        {
            var swapped = new List<Ev>(events);
            (swapped[0], swapped[1]) = (swapped[1], swapped[0]);
            yield return ("first two swapped", swapped);
        }

        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].Kind == EvKind.BeginDynamicArray)
            {
                foreach (int count in new[] { -1, 0, events[i].Count + 1, int.MaxValue })
                {
                    var changed = new List<Ev>(events) { [i] = events[i] with { Count = count } };
                    yield return ($"array {i} count {count}", changed);
                }

                yield break;
            }
        }
    }

    private static ABIFuzzSeed Script(string category, ABIFuzzSchema schema, List<Ev> events, string detail)
    {
        var buffer = new byte[ABIEncodeLimits.Default.MaxEncodedBytes + 64];
        ABIEncodeLimits roomy = new(60_000, ABIEncodeLimits.HardMaxValueDepth, 4096, 100_000, 1_000_000);
        RunResult result = ABIWriteTestSupport.RunWriter(events, schema.Schema, buffer, roomy);
        return new ABIFuzzSeed(ABIFuzzKind.Script, $"script-{category}-{schema.Name}-{result.Status}-{Slug(detail)}", ABIFuzzScript.Encode(schema.Index, events));
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    // ------------------------------------------------------------------ schema text

    private static readonly string[] SchemaTexts =
    [
        // real signatures
        "transfer(address,uint256)", "approve(address,uint256)", "balanceOf(address)", "multicall(bytes[])", "totalSupply()",
        "transferFrom(address,address,uint256)", "safeBatchTransferFrom(address,address,uint256[],uint256[],bytes)",
        "exactInputSingle((address,address,uint24,address,uint256,uint256,uint256,uint160))",
        "baz(uint32,bool)", "bar(bytes3[2])", "sam(bytes,bool,uint256[])", "f(uint256,uint32[],bytes10,bytes)", "g(uint256[][],string[])",
        "fulfillOrder(((address,address,(uint8,address,uint256,uint256,uint256)[],(uint8,address,uint256,uint256,uint256,address)[],uint8,uint256,uint256,bytes32,uint256,bytes32,uint256),bytes),bytes32)",
        // aliases are normalised
        "f(uint,int,fixed,ufixed)", "f(uint[],int[2])", "f(function,bool,string,address)", "f(fixed128x18,ufixed8x80)",
        // shapes
        "f()", "f(())", "f(()[])", "f(()[3])", "f(((),()))", "f((uint256,(uint256,bytes)[])[2][])", "_(uint8)", "$f(uint8)", "f1(uint8)",
        // canonical tuples without a name (tuple parser only)
        "(uint256,address)", "()", "((uint256))", "(uint256[2][])",
        // malformed
        "", "(", ")", "f", "f(", "f)", "f(uint256", "f(uint256))", "f(uint256,)", "f(,uint256)", "f(uint256,,uint256)", "(uint256", "uint256",
        "f(uint7)", "f(uint0)", "f(uint264)", "f(int7)", "f(bytes0)", "f(bytes33)", "f(uint256[0])", "f(uint256[01])", "f(uint256[-1])",
        "f(uint256[2147483648])", "f(uint256[99999999999999999999])", "f(uint256[2]])", "f(uint256[)", "f(uint256[a])",
        "f(fixed0x18)", "f(fixed128x0)", "f(fixed128x81)", "f(fixed7x18)", "f(fixed128)", "f(ufixed128x)", "f(fixed128x18x1)",
        "f(address payable)", "f(address calldata)", "f(uint256 a)", "f( uint256)", "f(uint256 )", " f(uint256)", "f(uint256) ", "f(uint256)extra",
        "f(tuple(uint256))", "f(tuple)", "f(uint256[],)", "1f(uint8)", "f-g(uint8)", "é(uint8)", "f(uint8)\0", "f\0(uint8)", "f(\0)",
        "f(UINT8)", "f(Uint8)", "f(uint)", "f(int)", "f(bool,bool,bool)", "f(string,bytes,bytes32,bytes1)",
        "f(uint08)", "f(uint+8)", "f(uint8.0)",
        // hostile sizes
        "f(" + string.Concat(Enumerable.Repeat("(", 31)) + "uint8" + string.Concat(Enumerable.Repeat(")", 31)) + ")",
        "f(" + string.Concat(Enumerable.Repeat("(", 32)) + "uint8" + string.Concat(Enumerable.Repeat(")", 32)) + ")",
        "f(" + string.Concat(Enumerable.Repeat("(", 33)) + "uint8" + string.Concat(Enumerable.Repeat(")", 33)) + ")",
        "f(" + string.Concat(Enumerable.Repeat("(", 200)) + "uint8" + string.Concat(Enumerable.Repeat(")", 200)) + ")",
        "f(uint8" + string.Concat(Enumerable.Repeat("[]", 31)) + ")",
        "f(uint8" + string.Concat(Enumerable.Repeat("[]", 40)) + ")",
        "f(uint8" + string.Concat(Enumerable.Repeat("[2]", 2000)) + ")",
        "f(" + string.Join(",", Enumerable.Repeat("uint8", 1024)) + ")",
        "f(" + string.Join(",", Enumerable.Repeat("uint8", 1025)) + ")",
        "f(" + string.Join(",", Enumerable.Repeat("uint8", 5000)) + ")",
        "f(uint8[4096])", "f(uint8[4097])", "f(bytes32[8192])", "f(uint8[2][2][2][2][2][2][2][2][2][2][2][2][2][2][2][2])",
        new string('a', 2000) + "(uint8)",
    ];

    private static IEnumerable<ABIFuzzSeed> SchemaTextSeeds()
    {
        int index = 0;
        foreach (string text in SchemaTexts)
        {
            var limits = ABICompileLimits.Default;
            _ = ABISchemaCompiler.TryCompileFunctionSignature(text, in limits, out _, out ABISchemaError error);
            byte[] bytes = Encoding.Latin1.GetBytes(text);
            yield return new ABIFuzzSeed(ABIFuzzKind.SchemaText, $"schema-{error.Status}-{index++:D3}-{(Slug(text.Length > 40 ? text[..40] : text) is { Length: > 0 } slug ? slug : "unprintable")}", bytes);
        }
    }
}
