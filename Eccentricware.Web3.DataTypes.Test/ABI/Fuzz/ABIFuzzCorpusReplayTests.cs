using System.Buffers.Binary;
using System.Text;
using EccentricWare.Web3.DataTypes.ABI;
using static EccentricWare.Web3.DataTypes.Tests.ABI.ABISchemaTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.ABI;

/// <summary>
/// Replays the ABI fuzz seeds through <see cref="ABIFuzzTarget"/> and runs seeded generative passes through the same targets. The seeds are the
/// output of the deterministic generator <see cref="ABIFuzzCorpus"/>, produced IN MEMORY for every run (nothing generated is stored in the repository),
/// plus every <c>ABI/Fuzz/Corpus/*.hex</c> file (copied to the test output): those are inputs a fuzzer or a reviewer FOUND, and each is a permanent
/// regression test (see the corpus README). Runs in the normal suite; no external tool is needed.
/// </summary>
[TestClass]
public sealed class ABIFuzzCorpusReplayTests
{
    private const int GenerativeSeed = 20261117;

    public TestContext TestContext { get; set; } = null!;

    private sealed record Seed(string Name, byte[] Input, bool Generated);

    private static string FoundDirectory => Path.Combine(AppContext.BaseDirectory, "ABI", "Fuzz", "Corpus");

    private static string[] FoundFiles() =>
        Directory.Exists(FoundDirectory) ? Directory.GetFiles(FoundDirectory, "*.hex").Order(StringComparer.Ordinal).ToArray() : [];

    private static readonly Lazy<Seed[]> AllSeeds = new(LoadSeeds);

    private static Seed[] LoadSeeds()
    {
        var seeds = ABIFuzzCorpus.Seeds().Select(s => new Seed(s.Name, s.Input, Generated: true)).ToList();
        foreach (string file in FoundFiles())
        {
            seeds.Add(new Seed(Path.GetFileNameWithoutExtension(file), ParseCorpusFile(file), Generated: false));
        }

        return [.. seeds.OrderBy(s => s.Name, StringComparer.Ordinal)];
    }

    internal static byte[] ParseCorpusFile(string path)
    {
        string line = File.ReadAllText(path).TrimEnd('\r', '\n');
        Assert.IsFalse(line.Contains('\n') || line.Contains('\r'), $"{Path.GetFileName(path)}: more than one line");
        return Convert.FromHexString(line);
    }

    private static ABIFuzzKind KindOf(string fileName) =>
        fileName.StartsWith("script-", StringComparison.Ordinal) ? ABIFuzzKind.Script
        : fileName.StartsWith("schema-", StringComparison.Ordinal) ? ABIFuzzKind.SchemaText
        : ABIFuzzKind.Document;

    private static void Replay(string name, byte[] input)
    {
        try
        {
            switch (KindOf(name))
            {
                case ABIFuzzKind.Script: ABIFuzzTarget.RunWriterScript(input); break;
                case ABIFuzzKind.SchemaText: ABIFuzzTarget.RunSchemaText(input); break;
                default: ABIFuzzTarget.Run(input); break;
            }
        }
        catch (InvalidOperationException exception)
        {
            Assert.Fail($"{name}: {exception.Message}");
        }
    }

    // ------------------------------------------------------------------ seed integrity

    [TestMethod]
    public void TheGenerator_IsDeterministic_AndProducesALargeSeedSetWithUniqueNames()
    {
        ABIFuzzSeed[] first = [.. ABIFuzzCorpus.Seeds()];
        ABIFuzzSeed[] second = [.. ABIFuzzCorpus.Seeds()];
        Assert.IsGreaterThan(1_000, first.Length, "the generator should produce a large seed set");
        Assert.HasCount(first.Length, second);
        for (int i = 0; i < first.Length; i++)
        {
            Assert.AreEqual(first[i].Name, second[i].Name, $"seed {i}");
            CollectionAssert.AreEqual(first[i].Input, second[i].Input, first[i].Name);
        }

        Assert.AreEqual(first.Length, first.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "seed names must be unique, also ignoring case");

        // Seeding an external fuzzer: ABI_FUZZ_EXPORT_SEEDS=<directory> writes the generated seeds there (raw bytes, one file per seed, no extension).
        string? export = Environment.GetEnvironmentVariable("ABI_FUZZ_EXPORT_SEEDS");
        if (!string.IsNullOrEmpty(export))
        {
            Directory.CreateDirectory(export);
            foreach (ABIFuzzSeed seed in first)
            {
                File.WriteAllBytes(Path.Combine(export, seed.Name), seed.Input);
            }

            TestContext.WriteLine($"exported {first.Length} seeds to {export}");
        }

        TestContext.WriteLine($"{first.Length} generated seeds; {FoundFiles().Length} found-regression files in the folder");
    }

    [TestMethod]
    public void EveryFoundRegressionFile_IsOneLineOfHex_WithAnUnusedName_AndTheFolderHasItsReadme()
    {
        Assert.IsTrue(File.Exists(Path.Combine(FoundDirectory, "README.md")), "corpus README.md was not copied to the test output");
        var generated = new HashSet<string>(ABIFuzzCorpus.Seeds().Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        foreach (string file in FoundFiles())
        {
            string name = Path.GetFileNameWithoutExtension(file);
            Assert.IsFalse(generated.Contains(name), $"{name}: a found file must not reuse the name of a generated seed");
            _ = ParseCorpusFile(file);
        }
    }

    [TestMethod]
    public void EverySeed_IsWellFormed_AndRunsThroughItsFuzzTarget()
    {
        Seed[] seeds = AllSeeds.Value;
        Assert.IsGreaterThan(1_000, seeds.Length, "the seed set is missing or truncated");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        var perKind = new Dictionary<ABIFuzzKind, int>();
        foreach (Seed seed in seeds)
        {
            Assert.IsTrue(names.Add(seed.Name), $"{seed.Name}: differs from another seed only by case");
            Assert.IsLessThan(110, seed.Name.Length, $"{seed.Name}: name too long for portable checkouts of a found file");
            totalBytes += seed.Input.Length;
            perKind[KindOf(seed.Name)] = perKind.GetValueOrDefault(KindOf(seed.Name)) + 1;
            Replay(seed.Name, seed.Input);
        }

        foreach (ABIFuzzKind kind in Enum.GetValues<ABIFuzzKind>())
        {
            Assert.IsGreaterThan(20, perKind.GetValueOrDefault(kind), $"too few seeds for {kind}");
        }

        TestContext.WriteLine($"replayed {seeds.Length} seeds, {totalBytes} bytes; " + string.Join(", ", perKind.Select(p => $"{p.Key}={p.Value}")));
    }

    [TestMethod]
    public void EverySeedName_StatesTheSchemaAndStatusTheLibraryGivesIt()
    {
        // The name is a claim about the first byte (document and script seeds) and about the outcome under fixed limits.
        // The accept/reject verdict is additionally checked against the independent reference model inside the target.
        var statuses = new Dictionary<ABIStatus, int>();
        foreach (Seed seed in AllSeeds.Value)
        {
            string name = seed.Name;
            byte[] input = seed.Input;
            switch (KindOf(name))
            {
                case ABIFuzzKind.SchemaText:
                {
                    string status = name["schema-".Length..].Split('-')[0];
                    var limits = ABICompileLimits.Default;
                    _ = ABISchemaCompiler.TryCompileFunctionSignature(Encoding.Latin1.GetString(input), in limits, out _, out ABISchemaError error);
                    Assert.AreEqual(status, error.Status.ToString(), name);
                    break;
                }

                case ABIFuzzKind.Script:
                {
                    string[] parts = name["script-".Length..].Split('-');
                    Assert.IsGreaterThan(2, parts.Length, name);
                    ABIFuzzSchema schema = SchemaOf(name, parts[1], input);
                    var roomy = new ABIEncodeLimits(60_000, ABIEncodeLimits.HardMaxValueDepth, 4096, 100_000, 1_000_000);
                    RunResult result = ABIWriteTestSupport.RunWriter(ABIFuzzScript.Parse(input.AsSpan(1)), schema.Schema, new byte[60_064], roomy);
                    Assert.AreEqual(parts[2], result.Status.ToString(), name);
                    break;
                }

                default:
                {
                    string[] parts = name.Split('-');
                    Assert.IsGreaterThan(2, parts.Length, name);
                    ABIFuzzSchema schema = SchemaOf(name, parts[1], input);
                    var limits = ABIDecodeLimits.Default;
                    _ = ABIData.TryDecodeComplete(input.AsSpan(1), schema.Schema, in limits, out _, out ABIStatus status);
                    Assert.AreEqual(parts[2], status.ToString(), name);
                    statuses[status] = statuses.GetValueOrDefault(status) + 1;
                    break;
                }
            }
        }

        // The seeds reach the main failure classes as well as success.
        foreach (ABIStatus expected in new[]
        {
            ABIStatus.Success, ABIStatus.Truncated, ABIStatus.InvalidOffset, ABIStatus.NonCanonicalLayout, ABIStatus.InvalidPadding,
            ABIStatus.InvalidScalar, ABIStatus.ElementLimitExceeded, ABIStatus.WorkLimitExceeded,
        })
        {
            Assert.IsTrue(statuses.ContainsKey(expected), $"no document seed gives {expected}: {string.Join(", ", statuses)}");
        }
    }

    private static ABIFuzzSchema SchemaOf(string name, string schemaName, byte[] input)
    {
        ABIFuzzSchema schema = ABIFuzzSchemas.ByName(schemaName);
        Assert.AreEqual((byte)schema.Index, input[0], $"{name}: first byte is not the registry index of {schemaName}");
        return schema;
    }

    [TestMethod]
    public void Registry_IndexesAreContiguous_AndEverySchemaIsDistinct()
    {
        for (int i = 0; i < ABIFuzzSchemas.All.Length; i++)
        {
            Assert.AreEqual(i, ABIFuzzSchemas.All[i].Index);
        }

        Assert.AreEqual(ABIFuzzSchemas.All.Length, ABIFuzzSchemas.All.Select(s => s.Name).Distinct().Count());
        Assert.AreEqual(ABIFuzzSchemas.All.Length, ABIFuzzSchemas.All.Select(s => s.Tuple).Distinct().Count());
        Assert.IsLessThan(256, ABIFuzzSchemas.All.Length, "the schema index is one byte");
    }

    [TestMethod]
    public void Seeds_CoverEveryCategoryAndEveryRegistrySchema()
    {
        string[] names = AllSeeds.Value.Select(s => s.Name).ToArray();
        foreach (string category in new[] { "fixture-", "reference-", "truncated-", "trailing-", "padding-", "word-", "insert-", "delete-", "hostile-", "limit-", "script-fixture-", "script-mutated-", "script-value-", "schema-" })
        {
            Assert.IsTrue(names.Any(n => n.StartsWith(category, StringComparison.Ordinal)), $"no corpus file in category {category}");
        }

        foreach (ABIFuzzSchema schema in ABIFuzzSchemas.All)
        {
            Assert.IsTrue(names.Any(n => n.StartsWith($"reference-{schema.Name}-", StringComparison.Ordinal)), $"no reference seed for schema {schema.Name}");
        }
    }

    // ------------------------------------------------------------------ the target itself

    [TestMethod]
    public void FuzzTarget_QuotesTheInputInHex_AndTruncatesVeryLongInputs()
    {
        Assert.AreEqual("0x", ABIFuzzTarget.Quote([]));
        Assert.AreEqual("0x81FF", ABIFuzzTarget.Quote([0x81, 0xff]));
        string quoted = ABIFuzzTarget.Quote(new byte[ABIFuzzTarget.MaxQuotedBytes + 5]);
        StringAssert.EndsWith(quoted, $"({ABIFuzzTarget.MaxQuotedBytes + 5} bytes in total)");
        StringAssert.StartsWith(quoted, "0x0000");
    }

    [TestMethod]
    public void FuzzTargets_AcceptEveryDegenerateInput()
    {
        // Empty, one byte, every schema index with an empty document, and all-ones: nothing may throw.
        ABIFuzzTarget.Run([]);
        ABIFuzzTarget.RunWriterScript([]);
        ABIFuzzTarget.RunSchemaText([]);
        foreach (byte[] input in new[] { new byte[] { 0 }, [0xff], [0xff, 0xff, 0xff, 0xff], new byte[4096], Enumerable.Repeat((byte)0xff, 4096).ToArray() })
        {
            ABIFuzzTarget.Run(input);
            ABIFuzzTarget.RunWriterScript(input);
            ABIFuzzTarget.RunSchemaText(input);
        }

        for (int index = 0; index < 256; index++)
        {
            ABIFuzzTarget.Run([(byte)index]);
            ABIFuzzTarget.RunWriterScript([(byte)index]);
        }
    }

    [TestMethod]
    public void FuzzTarget_TurnsAViolationAndAnUnexpectedExceptionIntoAnExceptionThatCarriesTheInput()
    {
        byte[] input = [0x01, 0xab];

        var violation = Assert.Throws<InvalidOperationException>(() => ABIFuzzTarget.Guard(input, bytes => ABIFuzzTarget.Expect(false, bytes, "invariant broke")));
        Assert.AreEqual("invariant broke. Input: 0x01AB", violation.Message);

        var unexpected = Assert.Throws<InvalidOperationException>(() => ABIFuzzTarget.Guard(input, _ => throw new IndexOutOfRangeException("boom")));
        StringAssert.StartsWith(unexpected.Message, "Unexpected IndexOutOfRangeException");
        StringAssert.EndsWith(unexpected.Message, "Input: 0x01AB");
        Assert.IsInstanceOfType<IndexOutOfRangeException>(unexpected.InnerException);

        ABIFuzzTarget.Guard(input, _ => { }); // a passing action returns normally
    }

    [TestMethod]
    public void FuzzTarget_DoesNotFlagMalformedScripts_AsViolations()
    {
        // An End at the root, a value before any container and a second root value are misuse the writer must refuse with a status.
        foreach (List<Ev> events in new List<Ev>[]
        {
            [Ev.End, Ev.U64(1)],
            [Ev.Begin("tuple"), Ev.U64(1)],
            [Ev.U64(1), Ev.U64(2), Ev.U64(3), Ev.U64(4)],
            [Ev.Begin("dynamic", -5), Ev.End],
        })
        {
            ABIFuzzTarget.RunScriptEvents(ABIFuzzSchemas.ByName("transfer"), events, ABIFuzzScript.Encode(0, events));
        }
    }

    // ------------------------------------------------------------------ script codec

    [TestMethod]
    public void ScriptCodec_RoundTrips_AndParsesEveryByteString()
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var (type, value, _, _, script) = ABIWriteTestSupport.Generate(seed * 31);
            if (script.Any(e => e.Data is { Length: > ushort.MaxValue }))
            {
                continue;
            }

            byte[] bytes = ABIFuzzScript.Encode(7, script);
            Assert.AreEqual(7, bytes[0]);
            List<Ev> parsed = ABIFuzzScript.Parse(bytes.AsSpan(1));
            Assert.HasCount(script.Count, parsed, $"seed {seed * 31}");
            for (int i = 0; i < script.Count; i++)
            {
                Assert.AreEqual(script[i].Kind, parsed[i].Kind, $"seed {seed * 31} event {i}");
                Assert.AreEqual(script[i].U, parsed[i].U);
                Assert.AreEqual(script[i].I, parsed[i].I);
                Assert.AreEqual(script[i].Selector, parsed[i].Selector);
                Assert.AreEqual(script[i].Count, parsed[i].Count);
                CollectionAssert.AreEqual(script[i].Data, parsed[i].Data);
            }

            Assert.IsNotNull(type);
            Assert.IsNotNull(value);
        }

        var rng = new DeterministicRng(99);
        for (int i = 0; i < 2_000; i++)
        {
            List<Ev> events = ABIFuzzScript.Parse(rng.Bytes(rng.Next(300)));
            Assert.IsLessThanOrEqualTo(ABIFuzzScript.MaxEvents, events.Count);
        }
    }

    // ------------------------------------------------------------------ generative passes

    private static byte[] RandomBytes(DeterministicRng rng, int maxLength)
    {
        ulong[] interesting = [0, 1, 0x20, 0x40, 0x60, 0x80, 0xff, 0x1000, 0x8000_0000, ulong.MaxValue];
        var bytes = new byte[rng.Next(maxLength + 1)];
        for (int i = 0; i + 8 <= bytes.Length; i += 8)
        {
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(i), rng.Next(3) == 0 ? rng.NextUInt64() : interesting[rng.Next(interesting.Length)]);
        }

        return bytes;
    }

    [TestMethod]
    public void GenerativeDocuments_OfRandomSchemasValuesAndMutations_NeverViolateAnInvariant()
    {
        const int Schemas = 220;
        const int MutantsPerSchema = 14;
        int accepted = 0;
        int rejected = 0;
        var statuses = new Dictionary<ABIStatus, int>();
        for (ulong seed = 1; seed <= Schemas; seed++)
        {
            var rng = new DeterministicRng(GenerativeSeed + seed);
            RTupleType type = ABIReferenceGenerator.RandomRoot(rng, depth: 3);
            RSeq value = ABIReferenceGenerator.RandomValue(rng, type);
            byte[] original = ABIReference.EncodeRoot(type, value);
            ABIFuzzSchema schema = ABIFuzzSchema.Ad($"random{seed}", type.Text);
            for (int round = 0; round < MutantsPerSchema; round++)
            {
                byte[] document = round == 0 ? original : ABIDecodeDifferentialTests.Mutate(original, rng);
                try
                {
                    ABIFuzzTarget.RunDocument(schema, document);
                }
                catch (InvalidOperationException exception)
                {
                    Assert.Fail($"seed {GenerativeSeed + seed} round {round} schema {type.Text}: {exception.Message}");
                }

                var limits = ABIDecodeLimits.Default;
                bool ok = ABIData.TryDecodeComplete(document, schema.Schema, in limits, out _, out ABIStatus status);
                statuses[status] = statuses.GetValueOrDefault(status) + 1;
                if (ok)
                {
                    accepted++;
                }
                else
                {
                    rejected++;
                }
            }
        }

        // Both outcomes must be common and varied: the success-path checks and the failure-path checks both ran on many inputs.
        Assert.IsGreaterThan(300, accepted, "accepted documents");
        Assert.IsGreaterThan(1_000, rejected, "rejected documents");
        foreach (ABIStatus expected in new[] { ABIStatus.Truncated, ABIStatus.InvalidOffset, ABIStatus.NonCanonicalLayout, ABIStatus.InvalidPadding, ABIStatus.InvalidScalar })
        {
            Assert.IsTrue(statuses.ContainsKey(expected), $"no generated document gave {expected}: {string.Join(", ", statuses)}");
        }

        TestContext.WriteLine($"{accepted} accepted, {rejected} rejected: " + string.Join(", ", statuses.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")));
    }

    [TestMethod]
    public void GenerativeRawBytes_ThroughTheRegistry_NeverViolateAnInvariant()
    {
        var rng = new DeterministicRng(GenerativeSeed);
        for (int i = 0; i < 1_500; i++)
        {
            byte[] input = [(byte)rng.Next(256), .. RandomBytes(rng, 400)];
            try
            {
                ABIFuzzTarget.Run(input);
            }
            catch (InvalidOperationException exception)
            {
                Assert.Fail($"iteration {i}: {exception.Message}");
            }
        }
    }

    [TestMethod]
    public void GenerativeWriterScripts_OfRandomSchemasAndEditedEvents_NeverViolateAnInvariant()
    {
        int completed = 0;
        int failed = 0;
        var statuses = new Dictionary<ABIStatus, int>();
        for (ulong seed = 1; seed <= 150; seed++)
        {
            var (type, _, _, _, script) = ABIWriteTestSupport.Generate(seed * 104_729, depth: 3);
            var rng = new DeterministicRng(seed * 31 + 7);
            ABIFuzzSchema schema = ABIFuzzSchema.Ad($"script{seed}", type.Text);
            for (int round = 0; round < 8; round++)
            {
                List<Ev> events = round == 0 ? script : EditEvents(script, rng);
                try
                {
                    ABIFuzzTarget.RunScriptEvents(schema, events, ABIFuzzScript.Encode(0, events));
                }
                catch (InvalidOperationException exception)
                {
                    Assert.Fail($"seed {seed * 104_729} round {round} schema {type.Text}: {exception.Message}");
                }

                var roomy = new ABIEncodeLimits(60_000, ABIEncodeLimits.HardMaxValueDepth, 4096, 100_000, 1_000_000);
                RunResult result = ABIWriteTestSupport.RunWriter(events, schema.Schema, new byte[60_064], roomy);
                statuses[result.Status] = statuses.GetValueOrDefault(result.Status) + 1;
                if (result.Ok)
                {
                    completed++;
                }
                else
                {
                    failed++;
                }
            }
        }

        Assert.IsGreaterThan(150, completed, "completed scripts");
        Assert.IsGreaterThan(300, failed, "failed scripts");
        foreach (ABIStatus expected in new[] { ABIStatus.TypeMismatch, ABIStatus.IncompleteWrite, ABIStatus.ElementLimitExceeded })
        {
            Assert.IsTrue(statuses.ContainsKey(expected), $"no edited script gave {expected}: {string.Join(", ", statuses)}");
        }

        TestContext.WriteLine($"{completed} completed, {failed} failed: " + string.Join(", ", statuses.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")));
    }

    private static List<Ev> EditEvents(List<Ev> script, DeterministicRng rng)
    {
        var events = new List<Ev>(script);
        int edits = 1 + rng.Next(3);
        for (int e = 0; e < edits && events.Count > 0; e++)
        {
            int at = rng.Next(events.Count);
            switch (rng.Next(7))
            {
                case 0: events.RemoveAt(at); break;
                case 1: events.Insert(at, events[at]); break;
                case 2: events.Insert(at, Ev.End); break;
                case 3: events.Insert(at, Ev.Begin(rng.Next(2) == 0 ? "tuple" : "fixed")); break;
                case 4 when events[at].Kind == EvKind.BeginDynamicArray:
                    events[at] = events[at] with { Count = new[] { -1, 0, events[at].Count + 1, events[at].Count - 1, int.MaxValue }[rng.Next(5)] };
                    break;
                case 5 when events.Count > 1:
                    int other = rng.Next(events.Count);
                    (events[at], events[other]) = (events[other], events[at]);
                    break;
                default: events[at] = Ev.U64(rng.NextUInt64()); break;
            }
        }

        return events;
    }

    [TestMethod]
    public void GenerativeSchemaText_NeverViolatesAnInvariant()
    {
        string[] fragments = ["f(", "(", ")", ",", "[", "]", "[2]", "[]", "uint", "int", "uint256", "uint8", "address", "bool", "bytes", "bytes32", "string", "function", "fixed", "ufixed128x18", "x", "tuple", "0", "1", "256", " ", "transfer", "_"];
        var rng = new DeterministicRng(GenerativeSeed);
        var texts = new List<string>(ABIFuzzCorpusTexts());
        for (int i = 0; i < 4_000; i++)
        {
            string text;
            if (i % 2 == 0)
            {
                var builder = new StringBuilder();
                int parts = 1 + rng.Next(14);
                for (int p = 0; p < parts; p++)
                {
                    builder.Append(fragments[rng.Next(fragments.Length)]);
                }

                text = builder.ToString();
            }
            else
            {
                char[] chars = texts[rng.Next(texts.Count)].ToCharArray();
                if (chars.Length > 0)
                {
                    chars[rng.Next(chars.Length)] = (char)rng.Next(256);
                }

                text = new string(chars);
            }

            byte[] input = Encoding.Latin1.GetBytes(text);
            try
            {
                ABIFuzzTarget.RunSchemaText(input);
            }
            catch (InvalidOperationException exception)
            {
                Assert.Fail($"iteration {i} text '{text}': {exception.Message}");
            }
        }
    }

    private static IEnumerable<string> ABIFuzzCorpusTexts() =>
        AllSeeds.Value.Where(s => KindOf(s.Name) == ABIFuzzKind.SchemaText).Select(s => Encoding.Latin1.GetString(s.Input));
}
