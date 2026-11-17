using System.Diagnostics;
using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Regression tests for the limits-race hardening in <c>RLPValidator.TryValidate</c>, which reads <c>MaxEncodedBytes</c>,
/// <c>MaxListDepth</c> and <c>MaxItems</c> once into locals. <see cref="RLPDecodeLimits"/> is passed with <c>in</c>, so a caller that
/// mutates a shared variable while another thread decodes could, before the hardening, pass <c>IsValid</c> and select the shallow
/// frame buffer, then re-read a larger <c>MaxListDepth</c> and index past the end of that buffer (<see cref="IndexOutOfRangeException"/>).
/// </summary>
/// <remarks>
/// The stress test is best effort: a data race cannot be forced deterministically. Run against the pre-hardening validator (the bucket
/// chosen from one read of <c>MaxListDepth</c>, the walk bounded by a later read) in a scratch copy of the repository on a 12-thread machine,
/// it failed with <see cref="IndexOutOfRangeException"/> in 10 of 10 runs, each within about 0.1 s. On a machine with a single hardware
/// thread the flipping thread may rarely interleave and the test then passes vacuously, which is why the deterministic snapshot tests
/// below exist.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LimitsRaceHardeningTests
{
    private const int DocumentDepth = 40;
    private const int StressMilliseconds = 3_000;

    private static readonly RLPDecodeLimits ShallowLimits = new(1024 * 1024, 8, 100);
    private static readonly RLPDecodeLimits DeepLimits = new(1024 * 1024, RLPDecodeLimits.HardMaxListDepth, 100_000);

    // The shared mutable variable the stress test passes with 'in' while another thread rewrites it.
    private static RLPDecodeLimits sharedLimits = DeepLimits;

    /// <summary>A list nested <paramref name="depth"/> levels deep whose innermost list holds one string (depth 40: 40 lists and 1 string, 41 bytes).</summary>
    private static byte[] NestedDocument(int depth)
    {
        var document = new byte[] { 0xc1, 0x01 };
        for (var level = 1; level < depth; level++)
        {
            // A payload of 56 bytes or more needs the one-byte long-form header (f8 <length>); lengths stay below 256 up to depth 64.
            var header = document.Length < 56 ? new[] { (byte)(0xc0 + document.Length) } : new[] { (byte)0xf8, (byte)document.Length };
            document = [.. header, .. document];
        }

        return document;
    }

    // ------------------------------------------------------------------ deterministic: the snapshot

    [TestMethod]
    public void TheDocumentFixture_IsDepth40_AndTheTwoLimitSetsGiveTooDeepAndSuccess()
    {
        var document = NestedDocument(DocumentDepth);
        Assert.HasCount(DocumentDepth + 1, document);
        var shallow = ShallowLimits;
        var deep = DeepLimits;
        Assert.AreEqual(RLPStatus.TooDeep, RLPData.TryDecodeComplete(document, in shallow, out var rejected, out var error));
        Assert.IsFalse(rejected.IsValid);
        Assert.IsTrue(error.Offset >= 0 && error.Offset < document.Length);
        Assert.AreEqual(RLPStatus.Success, RLPData.TryDecodeComplete(document, in deep, out var accepted, out _));
        Assert.AreEqual(document.Length, accepted.EncodedLength);
    }

    [TestMethod]
    public void InvalidLimits_AreInvalidArgument_ForEveryKindOfInput_BeforeAnythingElseIsLookedAt()
    {
        var inputs = new byte[][]
        {
            [],                                  // would be EmptyInput
            [0x80],                              // valid scalar
            NestedDocument(DocumentDepth),       // valid, would need the deep frame buffer
            new byte[2048],                      // larger than a one-byte size limit would be TooLarge
            [0xb8, 0x01],                        // malformed
        };
        var invalidLimits = new RLPDecodeLimits[]
        {
            default,
            new(0, 8, 100), new(-1, 8, 100),
            new(1024, 0, 100), new(1024, -1, 100), new(1024, RLPDecodeLimits.HardMaxListDepth + 1, 100), new(1024, int.MaxValue, 100), new(1024, int.MinValue, 100),
            new(1024, 8, 0), new(1024, 8, -1),
        };

        foreach (var limits in invalidLimits)
        {
            Assert.IsFalse(limits.IsValid, limits.ToString());
            var shared = limits;
            foreach (var input in inputs)
            {
                var status = RLPData.TryDecodeComplete(input, in shared, out var document, out var error);
                Assert.AreEqual(RLPStatus.InvalidArgument, status, $"{limits}, {input.Length} bytes");
                Assert.IsFalse(document.IsValid);
                Assert.AreEqual(-1, error.Offset, $"{limits}, {input.Length} bytes");
                Assert.AreEqual(RLPStatus.InvalidArgument, error.Status);
            }
        }
    }

    [TestMethod]
    public void EveryDepthFrom1To64_DecodesAgainstItsOwnLimit_AcrossTheThreeFrameBuckets()
    {
        // Depths 8 / 9 / 32 / 33 / 64 straddle the shallow, medium and deep frame buckets; each decodes at its exact limit and fails one below.
        foreach (var depth in new[] { 1, 2, 7, 8, 9, 31, 32, 33, 63, 64 })
        {
            var document = NestedDocument(depth);
            var exact = new RLPDecodeLimits(1024, depth, 1000);
            var statusAtLimit = RLPData.TryDecodeComplete(document, in exact, out _, out _);
            Assert.AreEqual(RLPStatus.Success, statusAtLimit, $"depth {depth} at limit {depth}");
            if (depth > 1)
            {
                var below = new RLPDecodeLimits(1024, depth - 1, 1000);
                Assert.AreEqual(RLPStatus.TooDeep, RLPData.TryDecodeComplete(document, in below, out _, out _), $"depth {depth} at limit {depth - 1}");
            }
        }
    }

    // ------------------------------------------------------------------ stress: a limits variable rewritten during decoding

    [TestMethod]
    public void RewritingASharedLimitsVariableDuringDecoding_NeverThrows_AndOnlyYieldsSuccessOrTooDeep()
    {
        var document = NestedDocument(DocumentDepth);
        var decoderCount = Math.Clamp(Environment.ProcessorCount - 1, 2, 6);
        using var stop = new CancellationTokenSource();
        var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var successes = new long[decoderCount];
        var tooDeep = new long[decoderCount];

        sharedLimits = DeepLimits;
        var flipper = new Thread(() =>
        {
            var token = stop.Token;
            while (!token.IsCancellationRequested)
            {
                // Plain struct assignments: the 12 bytes are not written atomically, so a reader may also see a mix of the two sets.
                sharedLimits = ShallowLimits;
                sharedLimits = DeepLimits;
            }
        }) { IsBackground = true, Name = "limits-flipper" };

        var decoders = Enumerable.Range(0, decoderCount).Select(index => new Thread(() =>
        {
            var token = stop.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var status = RLPData.TryDecodeComplete(document, in sharedLimits, out var decoded, out var error);
                    if (status == RLPStatus.Success)
                    {
                        successes[index]++;
                        if (!decoded.IsValid || decoded.EncodedLength != document.Length)
                        {
                            failures.Enqueue("Success returned an invalid or short document");
                        }
                    }
                    else if (status == RLPStatus.TooDeep)
                    {
                        tooDeep[index]++;
                        if (decoded.IsValid || error.Offset < 0 || error.Offset >= document.Length)
                        {
                            failures.Enqueue($"TooDeep returned a valid document or offset {error.Offset}");
                        }
                    }
                    else
                    {
                        failures.Enqueue($"unexpected status {status}");
                    }
                }
                catch (Exception exception)
                {
                    failures.Enqueue($"{exception.GetType().Name}: {exception.Message}");
                    return;
                }
            }
        }) { IsBackground = true, Name = $"limits-decoder-{index}" }).ToArray();

        flipper.Start();
        foreach (var decoder in decoders)
        {
            decoder.Start();
        }

        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < StressMilliseconds && failures.IsEmpty)
        {
            Thread.Sleep(50);
        }

        stop.Cancel();
        flipper.Join();
        foreach (var decoder in decoders)
        {
            decoder.Join();
        }

        Assert.IsTrue(failures.IsEmpty, $"{failures.Count} failure(s), first: {(failures.TryPeek(out var first) ? first : "")}");
        TestContext.WriteLine($"{decoderCount} decoder threads, {successes.Sum()} Success, {tooDeep.Sum()} TooDeep in {timer.ElapsedMilliseconds} ms");
        Assert.IsGreaterThan(0L, successes.Sum() + tooDeep.Sum(), "no decode completed");
    }

    public TestContext TestContext { get; set; } = null!;
}
