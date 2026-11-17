using System.Text.RegularExpressions;
using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Replays the persistent fuzz corpus (<c>Rlp/Fuzz/Corpus/*.hex</c>, copied to the test output) through
/// <see cref="RlpFuzzTarget.Run"/>, and runs a seeded generative pass over random and mutated inputs. A corpus file is a
/// regression test: a crash or divergence found by any fuzzer or review becomes one file (see the corpus README).
/// </summary>
[TestClass]
public sealed class RlpFuzzCorpusReplayTests
{
    private const int GenerativeInputCount = 200_000;
    private const int GenerativeSeed = 20260513;

    private static string CorpusDirectory => Path.Combine(AppContext.BaseDirectory, "Rlp", "Fuzz", "Corpus");

    public TestContext TestContext { get; set; } = null!;

    private static string[] CorpusFiles() =>
        Directory.Exists(CorpusDirectory) ? Directory.GetFiles(CorpusDirectory, "*.hex").Order(StringComparer.Ordinal).ToArray() : [];

    /// <summary>Parses a corpus file: one line of hex digits (no prefix, no separators), upper or lower case.</summary>
    internal static byte[] ParseCorpusFile(string path)
    {
        var text = File.ReadAllText(path);
        var line = text.TrimEnd('\r', '\n');
        Assert.IsFalse(line.Contains('\n') || line.Contains('\r'), $"{Path.GetFileName(path)}: more than one line");
        return Convert.FromHexString(line);
    }

    // Categories whose file name carries the status the oracle gives the input, longest/most specific prefix first.
    private static readonly string[] StatusCategories =
    [
        "invalid-nested-", "invalid-", "ambiguous-", "limit-", "spec-12-2-", "hostile-", "evm-", "single-byte-", "list-one-byte-child-", "signature-",
    ];

    /// <summary>
    /// Every corpus category, longest prefix first, as counted in <c>Corpus/MANIFEST.txt</c>: a file belongs to the first prefix it
    /// starts with (so <c>invalid-nested-</c> files are not counted under <c>invalid-</c>).
    /// </summary>
    private static readonly string[] ManifestCategories =
    [
        "list-one-byte-child-", "known-ethereum-", "invalid-nested-", "single-byte-", "divergence-", "signature-", "canonical-", "ambiguous-",
        "spec-12-2-", "integer-", "hostile-", "invalid-", "limit-", "evm-",
    ];

    private static string? ManifestCategoryOf(string fileName) =>
        ManifestCategories.FirstOrDefault(c => fileName.StartsWith(c, StringComparison.Ordinal));

    /// <summary>Parses <c>MANIFEST.txt</c>: <c>&lt;category prefix&gt; &lt;count&gt;</c> per line, then <c>TOTAL &lt;count&gt;</c>; <c>#</c> lines are comments.</summary>
    private static (Dictionary<string, int> PerCategory, int Total) ReadManifest()
    {
        var path = Path.Combine(CorpusDirectory, "MANIFEST.txt");
        Assert.IsTrue(File.Exists(path), "corpus MANIFEST.txt was not copied to the test output");
        var perCategory = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = -1;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.HasCount(2, parts, $"MANIFEST.txt: malformed line '{line}'");
            var count = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            if (parts[0] == "TOTAL")
            {
                total = count;
            }
            else
            {
                Assert.IsTrue(perCategory.TryAdd(parts[0], count), $"MANIFEST.txt: duplicate category {parts[0]}");
            }
        }

        Assert.IsGreaterThan(0, total, "MANIFEST.txt has no TOTAL line");
        Assert.AreEqual(total, perCategory.Values.Sum(), "MANIFEST.txt: TOTAL differs from the sum of its categories");
        return (perCategory, total);
    }

    /// <summary>
    /// Deleting corpus files must fail: every manifest category needs at least its recorded count and the folder at least the recorded
    /// total. Adding files passes (regenerate the manifest, see the corpus README).
    /// </summary>
    private static int AssertCorpusMeetsManifest(string[] files)
    {
        var (expected, total) = ReadManifest();
        CollectionAssert.AreEquivalent(ManifestCategories, expected.Keys.ToArray(), "MANIFEST.txt must list exactly the known categories");
        var actual = files.Select(f => ManifestCategoryOf(Path.GetFileName(f)!) ?? "(uncategorised)")
            .GroupBy(c => c, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        Assert.IsFalse(actual.ContainsKey("(uncategorised)"), "a corpus file belongs to no manifest category");
        foreach (var (category, count) in expected)
        {
            Assert.IsGreaterThanOrEqualTo(count, actual.GetValueOrDefault(category), $"corpus category '{category}' has {actual.GetValueOrDefault(category)} files, the manifest records {count}");
        }

        Assert.IsGreaterThanOrEqualTo(total, files.Length, $"corpus has {files.Length} files, the manifest records {total}");
        return total;
    }

    private static readonly Regex LimitsToken = new(@"^lim-b(-?\d+)-d(-?\d+)-i(-?\d+)-", RegexOptions.CultureInvariant);

    /// <summary>Parses the optional <c>lim-b&lt;bytes&gt;-d&lt;depth&gt;-i&lt;items&gt;-</c> token at the start of <paramref name="rest"/>.</summary>
    private static RLPDecodeLimits ParseLimits(string rest, out bool hasToken)
    {
        var match = LimitsToken.Match(rest);
        hasToken = match.Success;
        return match.Success
            ? new RLPDecodeLimits(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value))
            : RLPDecodeLimits.Default;
    }

    /// <summary>Runs one corpus file through the target, naming the file in any failure.</summary>
    private static void ReplayFile(string path)
    {
        var name = Path.GetFileName(path);
        var input = ParseCorpusFile(path);
        try
        {
            RlpFuzzTarget.Run(input);
            if (TryReadRecordedLimits(name, out var limits))
            {
                RlpFuzzTarget.Run(input, limits);
            }
        }
        catch (InvalidOperationException exception)
        {
            Assert.Fail($"{name}: {exception.Message}");
        }
    }

    /// <summary>The limits recorded in a <c>limit-</c>, <c>ambiguous-</c>, other status-category or <c>divergence-</c> file name, if any.</summary>
    private static bool TryReadRecordedLimits(string name, out RLPDecodeLimits limits)
    {
        limits = RLPDecodeLimits.Default;
        foreach (var category in StatusCategories)
        {
            if (name.StartsWith(category, StringComparison.Ordinal))
            {
                var afterCategory = name[category.Length..];
                var separator = afterCategory.IndexOf('-', StringComparison.Ordinal);
                limits = ParseLimits(afterCategory[(separator + 1)..], out var found);
                return found;
            }
        }

        if (name.StartsWith("divergence-", StringComparison.Ordinal))
        {
            var afterCategory = name["divergence-".Length..];
            limits = ParseLimits(afterCategory[(afterCategory.IndexOf('-', StringComparison.Ordinal) + 1)..], out var found);
            return found;
        }

        return false;
    }

    [TestMethod]
    public void CorpusFolder_IsCopiedToTheOutput_AndMeetsTheManifestFloor()
    {
        // A broken copy rule must fail loudly instead of replaying nothing.
        Assert.IsTrue(Directory.Exists(CorpusDirectory), $"corpus folder missing from the test output: {CorpusDirectory}");
        var files = CorpusFiles();
        var floor = AssertCorpusMeetsManifest(files);
        Assert.IsTrue(File.Exists(Path.Combine(CorpusDirectory, "README.md")), "corpus README.md was not copied");
        TestContext.WriteLine($"corpus files: {files.Length} (manifest floor {floor})");
    }

    [TestMethod]
    public void EveryCorpusFile_IsWellFormed_AndRunsThroughTheFuzzTarget()
    {
        var files = CorpusFiles();
        AssertCorpusMeetsManifest(files);
        var names = new HashSet<string>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            Assert.IsTrue(names.Add(name), name);
            totalBytes += ParseCorpusFile(file).Length;
            ReplayFile(file);
        }

        TestContext.WriteLine($"replayed {files.Length} corpus files, {totalBytes} bytes");
    }

    [TestMethod]
    public void Corpus_CoversEveryCategory_AndEveryDocumentedNethereumDivergencePattern()
    {
        var names = CorpusFiles().Select(Path.GetFileName).ToArray();
        string[] categories =
        [
            "canonical-", "known-ethereum-", "invalid-", "invalid-nested-", "ambiguous-", "limit-", "single-byte-", "list-one-byte-child-",
            "integer-", "spec-12-2-", "hostile-", "evm-", "divergence-", "signature-",
        ];
        foreach (var category in categories)
        {
            Assert.IsTrue(names.Any(n => n!.StartsWith(category, StringComparison.Ordinal)), $"no corpus file in category '{category}'");
        }

        Assert.IsTrue(names.Any(n => n!.StartsWith("invalid-", StringComparison.Ordinal) && !n.StartsWith("invalid-nested-", StringComparison.Ordinal)), "no un-nested invalid- file");

        foreach (var (key, _) in NethereumDivergenceClassifier.DocumentedPatterns)
        {
            Assert.IsTrue(names.Any(n => n!.StartsWith($"divergence-{key}", StringComparison.Ordinal)), $"no corpus file for divergence pattern {key}");
        }
    }

    [TestMethod]
    public void EveryCorpusFileName_StatesTheStatusTheOracleAndLibraryGiveIt()
    {
        // The name is a claim about the decode outcome under the limits it records (default limits when none are recorded).
        // Checking it against both the independent oracle and the library keeps a mislabelled or stale seed from hiding.
        var successes = 0;
        var failures = 0;
        var labelled = 0;
        foreach (var file in CorpusFiles())
        {
            var name = Path.GetFileName(file);
            var input = ParseCorpusFile(file);

            if (name.StartsWith("canonical-", StringComparison.Ordinal) || name.StartsWith("known-ethereum-", StringComparison.Ordinal))
            {
                AssertOutcome(name, input, RLPDecodeLimits.Default, RLPStatus.Success);
                successes++;
                continue;
            }

            if (name.StartsWith("integer-", StringComparison.Ordinal))
            {
                // integer-<accessor status>-...: valid generic RLP; the status is the one TryGetUInt64 gives.
                AssertOutcome(name, input, RLPDecodeLimits.Default, RLPStatus.Success);
                var integerToken = Enum.Parse<RLPStatus>(name["integer-".Length..].Split('-')[0]);
                var limitsForInteger = RLPDecodeLimits.Default;
                _ = RLPData.TryDecodeComplete(input, in limitsForInteger, out var document, out _);
                Assert.AreEqual(integerToken, document.TryGetUInt64(out _), name);
                successes++;
                continue;
            }

            var category = StatusCategories.FirstOrDefault(c => name.StartsWith(c, StringComparison.Ordinal));
            if (category is not null)
            {
                var afterCategory = name[category.Length..];
                var tokenEnd = afterCategory.IndexOf('-', StringComparison.Ordinal);
                Assert.IsGreaterThan(0, tokenEnd, $"{name}: no status token");
                Assert.IsTrue(Enum.TryParse<RLPStatus>(afterCategory[..tokenEnd], out var expected), $"{name}: '{afterCategory[..tokenEnd]}' is not an RLPStatus");
                var limits = ParseLimits(afterCategory[(tokenEnd + 1)..], out _);
                AssertOutcome(name, input, limits, expected);
                if (category == "signature-")
                {
                    AssertSignatureOutcome(name, input, afterCategory[(tokenEnd + 1)..]);
                }

                labelled++;
                if (expected == RLPStatus.Success)
                {
                    successes++;
                }
                else
                {
                    failures++;
                }

                continue;
            }

            if (name.StartsWith("divergence-", StringComparison.Ordinal))
            {
                // divergence-<pattern>-lim-...: re-run the comparison with Nethereum under the recorded limits.
                var afterCategory = name["divergence-".Length..];
                var pattern = afterCategory[..afterCategory.IndexOf('-', StringComparison.Ordinal)];
                var limits = ParseLimits(afterCategory[(pattern.Length + 1)..], out var hasLimits);
                Assert.IsTrue(hasLimits, $"{name}: a divergence file must record its limits");
                var items = limits.MaxItems > 0 ? limits.MaxItems : NethereumRlpAdapter.ItemBudget;
                Assert.AreEqual(pattern, NethereumDivergenceClassifier.Compare(input, limits, items), $"{name}: divergence pattern");
                continue;
            }

            if (name == "README.md")
            {
                continue;
            }

            Assert.Fail($"{name}: unknown corpus category");
        }

        Assert.IsGreaterThan(100, successes);
        Assert.IsGreaterThan(100, failures);
        Assert.IsGreaterThan(500, labelled);
    }

    /// <summary>
    /// <c>signature-Success-&lt;legacy|typed&gt;-&lt;ReaderStatus&gt;-&lt;detail&gt;</c>: the document is a list of three items and the
    /// named signature reader, given those items, returns <c>ReaderStatus</c>; the oracle gives the same status.
    /// </summary>
    private static void AssertSignatureOutcome(string name, byte[] input, string afterStatus)
    {
        var parts = afterStatus.Split('-');
        Assert.IsTrue(parts.Length >= 3 && parts[0] is "legacy" or "typed", $"{name}: expected <legacy|typed>-<ReaderStatus>-<detail>");
        Assert.IsTrue(Enum.TryParse<RLPStatus>(parts[1], out var expected), $"{name}: '{parts[1]}' is not an RLPStatus");

        var limits = RLPDecodeLimits.Default;
        Assert.AreEqual(RLPStatus.Success, RLPData.TryDecodeComplete(input, in limits, out var root, out _), name);
        Assert.AreEqual(RLPStatus.Success, root.TryGetChildren(out var children), name);
        Assert.IsTrue(children.MoveNext(), name);
        var first = children.Current;
        Assert.IsTrue(children.MoveNext(), name);
        var second = children.Current;
        Assert.IsTrue(children.MoveNext(), name);
        var third = children.Current;
        Assert.IsFalse(children.MoveNext(), $"{name}: a signature corpus file holds exactly three items");

        var oracleRoot = (RlpList)RlpReference.Decode(input, limits.MaxEncodedBytes, limits.MaxListDepth, limits.MaxItems).Root!;
        if (parts[0] == "legacy")
        {
            Assert.AreEqual(expected, RLPTypedExtensions.TryGetEvmLegacySignature(first, second, third, out _, out _), $"{name}: library");
            Assert.AreEqual(expected, EvmSignatureOracle.ExpectLegacySignature(oracleRoot.Items[0], oracleRoot.Items[1], oracleRoot.Items[2]).Status, $"{name}: oracle");
        }
        else
        {
            Assert.AreEqual(expected, RLPTypedExtensions.TryGetEvmYParitySignature(first, second, third, out _), $"{name}: library");
            Assert.AreEqual(expected, EvmSignatureOracle.ExpectYParitySignature(oracleRoot.Items[0], oracleRoot.Items[1], oracleRoot.Items[2]).Status, $"{name}: oracle");
        }
    }

    private static void AssertOutcome(string name, byte[] input, RLPDecodeLimits limits, RLPStatus expected)
    {
        var oracle = RlpReference.Decode(input, limits.MaxEncodedBytes, limits.MaxListDepth, limits.MaxItems);
        Assert.AreEqual(expected, oracle.Status, $"{name}: oracle status under {limits}");
        Assert.AreEqual(expected, RLPData.TryDecodeComplete(input, in limits, out _, out _), $"{name}: library status under {limits}");
    }

    [TestMethod]
    public void FuzzTarget_QuotesTheInputInHex_AndTruncatesVeryLongInputs()
    {
        Assert.AreEqual("0x", RlpFuzzTarget.Quote([]));
        Assert.AreEqual("0x81FF", RlpFuzzTarget.Quote([0x81, 0xff]));
        var quoted = RlpFuzzTarget.Quote(new byte[RlpFuzzTarget.MaxQuotedBytes + 5]);
        StringAssert.EndsWith(quoted, $"({RlpFuzzTarget.MaxQuotedBytes + 5} bytes in total)");
        StringAssert.StartsWith(quoted, "0x0000");
        Assert.AreEqual(2 + (2 * RlpFuzzTarget.MaxQuotedBytes) + "... (8197 bytes in total)".Length, quoted.Length);
    }

    [TestMethod]
    public void GenerativeRun_OfRandomAndMutatedInputs_NeverViolatesAnInvariant()
    {
        var rng = new Random(GenerativeSeed);
        var sources = new List<byte[]>();
        sources.AddRange(RlpVectors.Canonical.Select(v => v.Encoded));
        sources.AddRange(RlpVectors.KnownEthereum.Select(v => v.Encoded));
        sources.AddRange(NethereumDifferentialTests.GenerateTrees(GenerativeSeed ^ 0x1111, 400).Select(RlpReference.Encode));

        var distinctAccepted = new HashSet<string>(StringComparer.Ordinal);
        var distinctRejected = new HashSet<string>(StringComparer.Ordinal);
        var statuses = new Dictionary<RLPStatus, int>();
        var limits = RLPDecodeLimits.Default;
        for (var i = 0; i < GenerativeInputCount; i++)
        {
            byte[] input;
            switch (rng.Next(100))
            {
                case < 25:
                    input = RandomBytes(rng);
                    break;
                case < 70:
                    input = RlpStructuralMutator.MutateAny(rng, sources[rng.Next(sources.Count)], out _);
                    break;
                case < 95:
                    input = RlpReference.MutateRandomly(rng, RlpReference.MutateRandomly(rng, sources[rng.Next(sources.Count)]));
                    break;
                default:
                    input = sources[rng.Next(sources.Count)];
                    break;
            }

            RlpFuzzTarget.Run(input);
            var status = RLPData.TryDecodeComplete(input, in limits, out _, out _);
            statuses[status] = statuses.GetValueOrDefault(status) + 1;
            (status == RLPStatus.Success ? distinctAccepted : distinctRejected).Add(Convert.ToHexString(input));
        }

        // Both outcomes must be common and varied, so the success-path walk and the failure-path checks both ran many
        // times on different inputs (not on a few hundred repeated sources).
        Assert.IsGreaterThan(4_000, distinctAccepted.Count, "distinct accepted inputs");
        Assert.IsGreaterThan(50_000, distinctRejected.Count, "distinct rejected inputs");
        Assert.IsGreaterThan(5_000, statuses.GetValueOrDefault(RLPStatus.NonCanonicalEncoding), "NonCanonicalEncoding outcomes");
        Assert.IsGreaterThan(1_000, statuses.GetValueOrDefault(RLPStatus.InvalidListBoundary), "InvalidListBoundary outcomes");
        TestContext.WriteLine($"{GenerativeInputCount} inputs, distinct accepted {distinctAccepted.Count}, distinct rejected {distinctRejected.Count}; "
            + string.Join(", ", statuses.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")));
    }

    private static byte[] RandomBytes(Random rng)
    {
        byte[] interesting = [0x00, 0x01, 0x37, 0x38, 0x7f, 0x80, 0x81, 0xb7, 0xb8, 0xb9, 0xbf, 0xc0, 0xc1, 0xf7, 0xf8, 0xf9, 0xff];
        var bytes = new byte[rng.Next(0, 41)];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = rng.Next(3) == 0 ? (byte)rng.Next(256) : interesting[rng.Next(interesting.Length)];
        }

        return bytes;
    }
}
