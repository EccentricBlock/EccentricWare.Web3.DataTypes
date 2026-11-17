using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// Stand-alone concurrent-throughput harness for the RLP codec (spec 13.1 last row). It is not a BenchmarkDotNet job:
/// BenchmarkDotNet reports means, and this reports ops/s, latency percentiles and GC activity under a sustained mixed load.
/// Start it with <c>--rlp-concurrency [--threads n,n,...] [--seconds s] [--warmup s] [--out dir]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every thread loops: draw an operation from a fixed per-mille mix with a per-thread seeded generator, run it against
/// the shared, stable, read-only fixtures (per-thread output buffers for the writers), verify the result against the value
/// computed independently at start-up, and record the latency in a log-linear histogram. A wrong result aborts the whole
/// run and prints the operation, so the loop cannot time a no-op. The results of all operations are folded into a
/// checksum that is printed at the end, so none of the work is dead code.
/// </para>
/// <para>
/// This is a closed-loop load: a thread issues its next operation only after the previous one finished, so a stall delays
/// the following requests instead of queueing them and the percentiles under-report what an open-loop arrival process would
/// see (coordinated omission is not modelled). Latency is one <see cref="Stopwatch.GetTimestamp"/> pair per operation, whose
/// own cost is measured at start-up and reported; both raw and overhead-subtracted figures are shown. Threads are not pinned.
/// </para>
/// </remarks>
internal static class RlpConcurrentThroughputRunner
{
    public const string Flag = "--rlp-concurrency";

    private const int PhaseWarmup = 0;
    private const int PhaseMeasure = 1;
    private const int PhaseStop = 2;
    private const int PhaseAbort = 3;
    private const long Mismatch = long.MinValue;
    private const int MixResolution = 1000;
    private const long Chain98866 = 98866;
    private const uint ApproveSelector = 0x095ea7b3;

    private const int OpDecodeLegacy = 0;
    private const int OpDecodeEip1559 = 1;
    private const int OpReadLegacyTyped = 2;
    private const int OpReadEip1559Typed = 3;
    private const int OpIndexLateField = 4;
    private const int OpBuildLegacy = 5;
    private const int OpBuildEip1559 = 6;
    private const int OpHostileOversizedLength = 7;
    private const int OpHostileNonCanonical = 8;
    private const int OpHostileDepthBomb = 9;
    private const int OpHostileItemsOverLimit = 10;
    private const int OpCount = 11;

    private static readonly string[] OpNames =
    [
        "DecodeLegacy", "DecodeEip1559", "ReadLegacyTyped", "ReadEip1559Typed", "IndexLateField", "BuildLegacy",
        "BuildEip1559", "HostileOversizedLength", "HostileNonCanonical", "HostileDepthBomb", "HostileItemsOverLimit",
    ];

    private static readonly string[] OpDescriptions =
    [
        "strict decode of the signed legacy transaction (chain 1, 9 fields), structure only",
        "strict decode of the EIP-1559 payload with a 2-entry access list, structure only",
        "decode, enumerate all 9 fields as typed values, chain-id-aware `TryGetEvmLegacySignature` (chain 98866, v = 197768)",
        "decode, enumerate all 12 fields and the access list as typed values, `TryGetEvmYParitySignature`",
        "decode the EIP-1559 payload, `TryIndexDirectChildren` into a 16-slot `stackalloc` workspace, read field 11 (`s`) as `uint256`",
        "build the chain-98866 legacy transaction: size every field with the public helpers, then framed `RLPWriter` with `TryWriteEvmLegacyV`; output compared with the expected bytes",
        "build the EIP-1559 payload: size every field and list with the public helpers, then framed `RLPWriter`; output compared with the expected bytes",
        "hostile: `bf ff*8` (length above `int.MaxValue`), expects `LengthOverflow`",
        "hostile: `81 05` (non-canonical single byte), expects `NonCanonicalEncoding`",
        "hostile: 33 nested lists (one above the default depth 32), expects `TooDeep`",
        "hostile: a list of 8,192 literals (8,193 items, one above the default 8,192), expects `TooManyItems`",
    ];

    // Per mille of operations. The three typed/index/build groups dominate; the hostile share is 10% in total, and the
    // items-over-limit case (about 9 us when run alone) is kept at 0.5% so it cannot dominate the mean.
    private static readonly int[] OpPerMille = [100, 100, 250, 150, 100, 100, 100, 40, 30, 25, 5];

    private static int s_phase;
    private static string? s_failure;

    public static int Run(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (!TryParseOptions(args, out Options options, out string? problem))
        {
            Console.Error.WriteLine(problem);
            Console.Error.WriteLine("Usage: --rlp-concurrency [--threads 1,2,4,8] [--seconds 10] [--warmup 3] [--out <dir>]");
            return 1;
        }

        if (typeof(RlpConcurrentThroughputRunner).Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false) is [DebuggableAttribute { IsJITOptimizerDisabled: true }])
        {
            Console.Error.WriteLine("This build has the JIT optimiser disabled; run with -c Release.");
            return 1;
        }

        Workload workload;
        try
        {
            workload = new Workload();
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"Workload verification failed before the run: {ex.Message}");
            return 2;
        }

        string outputDirectory = options.OutputDirectory
            ?? Path.Combine("BenchmarkDotNet.Artifacts", "rlp-concurrency", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Console.WriteLine($"Workload verified: every operation returned its independently computed result. Output: {Path.GetFullPath(outputDirectory)}");

        TimerOverhead overhead = MeasureTimerOverhead();
        Console.WriteLine($"Timer overhead (back-to-back Stopwatch.GetTimestamp pair): min {overhead.MinNs} ns, p50 {overhead.MedianNs} ns, p99 {overhead.P99Ns} ns.");

        // Burn-in on one thread so tiered compilation settles before any recorded run; the result is discarded.
        Console.WriteLine("Burn-in (1 thread, discarded)...");
        RunResult? burnIn = RunOnce(workload, 1, timed: true, warmupSeconds: 2, measureSeconds: 0.5);
        if (burnIn is null)
        {
            return ReportFailureAndExit();
        }

        var results = new List<RunResult>();
        long checksum = burnIn.Checksum;
        foreach (int threads in options.ThreadCounts)
        {
            Console.WriteLine($"Threads {threads}: timed run ({options.WarmupSeconds:0.#} s warm-up, {options.MeasureSeconds:0.#} s measured)...");
            RunResult? timed = RunOnce(workload, threads, timed: true, options.WarmupSeconds, options.MeasureSeconds);
            if (timed is null)
            {
                return ReportFailureAndExit();
            }

            results.Add(timed);
            checksum += timed.Checksum;
            double referenceSeconds = Math.Min(options.MeasureSeconds, 5);
            Console.WriteLine($"Threads {threads}: untimed reference run (1 s warm-up, {referenceSeconds:0.#} s measured)...");
            RunResult? untimed = RunOnce(workload, threads, timed: false, warmupSeconds: 1, referenceSeconds);
            if (untimed is null)
            {
                return ReportFailureAndExit();
            }

            results.Add(untimed);
            checksum += untimed.Checksum;
        }

        string markdown = BuildMarkdown(options, workload, overhead, results, checksum);
        Console.WriteLine();
        Console.WriteLine(markdown);
        try
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "rlp-concurrency.md"), markdown);
            File.WriteAllText(Path.Combine(outputDirectory, "rlp-concurrency.csv"), BuildRunCsv(overhead, results));
            File.WriteAllText(Path.Combine(outputDirectory, "rlp-concurrency-ops.csv"), BuildOperationCsv(results));
            Console.WriteLine($"Wrote {Path.GetFullPath(outputDirectory)}{Path.DirectorySeparatorChar}rlp-concurrency.md, rlp-concurrency.csv and rlp-concurrency-ops.csv");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not write the output files: {ex.Message}");
            return 3;
        }

        return 0;
    }

    private static int ReportFailureAndExit()
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"RUN ABORTED: {Volatile.Read(ref s_failure) ?? "unknown failure"}");
        return 2;
    }

    // ------------------------------------------------------------------------------------------------ options

    private readonly record struct Options(int[] ThreadCounts, double MeasureSeconds, double WarmupSeconds, string? OutputDirectory);

    private static bool TryParseOptions(string[] args, out Options options, out string? problem)
    {
        int logical = Environment.ProcessorCount;
        int[] threadCounts = [1, 2, 4, 8, logical];
        double measure = 10;
        double warmup = 3;
        string? output = null;
        options = default;
        problem = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case Flag:
                    break;
                case "--threads" when i + 1 < args.Length:
                    string[] parts = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var parsed = new List<int>();
                    foreach (string part in parts)
                    {
                        if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1 || count > 1024)
                        {
                            problem = $"Invalid thread count '{part}' (expected 1 to 1024).";
                            return false;
                        }

                        parsed.Add(count);
                    }

                    threadCounts = [.. parsed];
                    break;
                case "--seconds" when i + 1 < args.Length:
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out measure) || measure < 0.5 || measure > 3600)
                    {
                        problem = "Invalid --seconds (expected 0.5 to 3600).";
                        return false;
                    }

                    break;
                case "--warmup" when i + 1 < args.Length:
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out warmup) || warmup < 0 || warmup > 3600)
                    {
                        problem = "Invalid --warmup (expected 0 to 3600).";
                        return false;
                    }

                    break;
                case "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                default:
                    problem = $"Unknown or incomplete argument '{args[i]}'.";
                    return false;
            }
        }

        // Ascending and distinct, so the table reads as a scaling curve.
        options = new Options([.. threadCounts.Distinct().Order()], measure, warmup, output);
        return true;
    }

    // ------------------------------------------------------------------------------------------------ histogram

    /// <summary>
    /// A log-linear histogram of nanosecond values: exact below 128 ns, then 64 sub-buckets per power of two (relative
    /// resolution 1/64, about 1.6%). Recording is one array increment, so it allocates nothing in the measured loop.
    /// </summary>
    private static class Histogram
    {
        private const int SubBucketBits = 6;
        private const int MaxTrackedBits = 42; // about 73 minutes in nanoseconds; larger values clamp into the last bucket

        public static readonly int BucketCount = UnclampedIndexOf(1UL << MaxTrackedBits) + 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOf(ulong value)
        {
            int index = UnclampedIndexOf(value);
            return index < BucketCount ? index : BucketCount - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int UnclampedIndexOf(ulong value)
        {
            if (value < (2UL << SubBucketBits))
            {
                return (int)value;
            }

            int shift = 63 - BitOperations.LeadingZeroCount(value) - SubBucketBits;
            return (shift << SubBucketBits) + (int)(value >> shift);
        }

        /// <summary>Highest value that maps to the bucket (HdrHistogram's "highest equivalent value").</summary>
        public static ulong UpperBound(int index)
        {
            if (index < (2 << SubBucketBits))
            {
                return (ulong)index;
            }

            int shift = (index >> SubBucketBits) - 1;
            ulong low = (ulong)(index - (shift << SubBucketBits)) << shift;
            return low + (1UL << shift) - 1;
        }

        /// <summary>Smallest bucket upper bound at or above the given percentile of <paramref name="total"/> samples, never above <paramref name="max"/>.</summary>
        public static ulong Percentile(long[] counts, long total, double percentile, ulong max)
        {
            if (total == 0)
            {
                return 0;
            }

            long target = Math.Max(1, (long)Math.Ceiling(total * percentile / 100.0));
            long cumulative = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                cumulative += counts[i];
                if (cumulative >= target)
                {
                    return Math.Min(UpperBound(i), max);
                }
            }

            return max;
        }
    }

    private readonly record struct TimerOverhead(ulong MinNs, ulong MedianNs, ulong P99Ns, double MeanNs);

    private static TimerOverhead MeasureTimerOverhead()
    {
        const int WarmupPairs = 200_000;
        const int Pairs = 2_000_000;
        long sink = 0;
        for (int i = 0; i < WarmupPairs; i++)
        {
            long a = Stopwatch.GetTimestamp();
            sink += Stopwatch.GetTimestamp() - a;
        }

        long[] counts = new long[Histogram.BucketCount];
        ulong min = ulong.MaxValue;
        ulong max = 0;
        double sum = 0;
        for (int i = 0; i < Pairs; i++)
        {
            long a = Stopwatch.GetTimestamp();
            long b = Stopwatch.GetTimestamp();
            ulong ns = TicksToNs(b - a);
            counts[Histogram.IndexOf(ns)]++;
            sum += ns;
            min = Math.Min(min, ns);
            max = Math.Max(max, ns);
        }

        GC.KeepAlive(sink.ToString(CultureInfo.InvariantCulture));
        return new TimerOverhead(min, Histogram.Percentile(counts, Pairs, 50, max), Histogram.Percentile(counts, Pairs, 99, max), sum / Pairs);
    }

    private static readonly double s_nanosecondsPerTick = 1e9 / Stopwatch.Frequency;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong TicksToNs(long ticks) =>
        ticks <= 0 ? 0UL : Stopwatch.Frequency == 1_000_000_000 ? (ulong)ticks : (ulong)(ticks * s_nanosecondsPerTick);

    // ------------------------------------------------------------------------------------------------ workload

    /// <summary>Stable, owned, shared read-only inputs and the independently computed expected result of every operation.</summary>
    private sealed class Workload
    {
        public readonly RLPDecodeLimits Limits = RLPDecodeLimits.Default;
        public readonly RlpTransactionFixture Tx = RlpBenchmarkData.Transactions;
        public readonly RlpEvmLegacyFixture LegacyChain98866 = RlpBenchmarkData.LegacyChain98866;
        public readonly byte[] LegacyChain1;
        public readonly byte[] Eip1559;
        public readonly byte[] HostileOversizedLength = [0xbf, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff];
        public readonly byte[] HostileNonCanonical = [0x81, 0x05];
        public readonly byte[] HostileDepthBomb;
        public readonly byte[] HostileItemsOverLimit;
        public readonly Signature LegacySignature;
        public readonly Signature Eip1559Signature;
        public readonly long[] Expected = new long[OpCount];
        public readonly byte[] OpTable = new byte[MixResolution];

        public Workload()
        {
            LegacyChain1 = Tx.LegacyEncoded;
            Eip1559 = Tx.Eip1559Encoded;
            HostileDepthBomb = RlpBenchmarkData.NestedLists(RLPDecodeLimits.Default.MaxListDepth + 1);
            HostileItemsOverLimit = new byte[3 + RLPDecodeLimits.Default.MaxItems];
            HostileItemsOverLimit[0] = 0xf9;
            HostileItemsOverLimit[1] = (byte)(RLPDecodeLimits.Default.MaxItems >> 8);
            HostileItemsOverLimit[2] = (byte)RLPDecodeLimits.Default.MaxItems;
            HostileItemsOverLimit.AsSpan(3).Fill(0x01); // 8,192 literals under the root: 8,193 items
            LegacySignature = LegacyChain98866.ExpectedSignature;
            Eip1559Signature = RlpBenchmarkData.ExpectedSignature(Tx, (byte)Tx.YParity);

            int total = 0;
            for (int op = 0; op < OpCount; op++)
            {
                for (int i = 0; i < OpPerMille[op]; i++)
                {
                    OpTable[total++] = (byte)op;
                }
            }

            if (total != MixResolution)
            {
                throw new InvalidOperationException($"The operation mix sums to {total} per mille, expected {MixResolution}.");
            }

            // Expected results come from fixture values and reference lengths, never from the operation under test.
            Expected[OpDecodeLegacy] = Tx.LegacyPayloadLength + (Tx.LegacyEncoded.Length * 1000L);
            Expected[OpDecodeEip1559] = Tx.Eip1559PayloadLength + (Tx.Eip1559Encoded.Length * 1000L);
            Expected[OpReadLegacyTyped] = (long)(Tx.Nonce + Tx.GasLimit) + Chain98866 + Tx.Data.Length + LegacySignature.V;
            Expected[OpReadEip1559Typed] = (long)(Tx.ChainId + Tx.Nonce + Tx.GasLimit) + (Tx.AccessListAddresses.Length * 10L) + (Tx.AccessListKeys.Length * 100L) + Eip1559Signature.V;
            Expected[OpIndexLateField] = RlpTransactionFixture.Eip1559FieldCount;
            Expected[OpBuildLegacy] = LegacyChain98866.Encoded.Length;
            Expected[OpBuildEip1559] = Tx.Eip1559Encoded.Length;
            Expected[OpHostileOversizedLength] = HostileResult(HostileOversizedLength, RLPStatus.LengthOverflow);
            Expected[OpHostileNonCanonical] = HostileResult(HostileNonCanonical, RLPStatus.NonCanonicalEncoding);
            Expected[OpHostileDepthBomb] = HostileResult(HostileDepthBomb, RLPStatus.TooDeep);
            Expected[OpHostileItemsOverLimit] = HostileResult(HostileItemsOverLimit, RLPStatus.TooManyItems);

            var scratch = new WorkerStateOutput();
            for (int op = 0; op < OpCount; op++)
            {
                long actual = Execute(op, this, scratch.Output);
                if (actual != Expected[op])
                {
                    throw new InvalidOperationException($"{OpNames[op]} returned {actual}, expected {Expected[op]}.");
                }
            }
        }

        /// <summary>Expected value of a hostile operation: the status (asserted, not self-calibrated) and the offset the library reports for it.</summary>
        private long HostileResult(byte[] input, RLPStatus expectedStatus)
        {
            RLPDecodeLimits limits = Limits;
            RLPStatus status = RLPData.TryDecodeComplete(input, in limits, out _, out RLPError error);
            if (status != expectedStatus)
            {
                throw new InvalidOperationException($"Hostile input decoded to {status}, expected {expectedStatus}.");
            }

            return ((long)status * 1_000_000L) + error.Offset;
        }
    }

    private sealed class WorkerStateOutput
    {
        public readonly byte[] Output = new byte[4096];
    }

    // ------------------------------------------------------------------------------------------------ operations

    private static long Execute(int op, Workload w, byte[] output) => op switch
    {
        OpDecodeLegacy => DecodeOnly(w, w.LegacyChain1),
        OpDecodeEip1559 => DecodeOnly(w, w.Eip1559),
        OpReadLegacyTyped => ReadLegacyTyped(w),
        OpReadEip1559Typed => ReadEip1559Typed(w),
        OpIndexLateField => IndexLateField(w),
        OpBuildLegacy => BuildLegacy(w, output),
        OpBuildEip1559 => BuildEip1559(w, output),
        OpHostileOversizedLength => Hostile(w, w.HostileOversizedLength),
        OpHostileNonCanonical => Hostile(w, w.HostileNonCanonical),
        OpHostileDepthBomb => Hostile(w, w.HostileDepthBomb),
        _ => Hostile(w, w.HostileItemsOverLimit),
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long DecodeOnly(Workload w, byte[] input)
    {
        RLPStatus status = RLPData.TryDecodeComplete(input, in w.Limits, out RLPData root, out _);
        return status == RLPStatus.Success ? root.PayloadLength + (root.EncodedLength * 1000L) : Mismatch;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Hostile(Workload w, byte[] input)
    {
        RLPStatus status = RLPData.TryDecodeComplete(input, in w.Limits, out _, out RLPError error);
        return ((long)status * 1_000_000L) + error.Offset;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ReadLegacyTyped(Workload w)
    {
        RlpTransactionFixture tx = w.Tx;
        if (RLPData.TryDecodeComplete(w.LegacyChain98866.Encoded, in w.Limits, out RLPData root, out _) != RLPStatus.Success
            || root.TryGetChildren(out RLPChildEnumerator children) != RLPStatus.Success)
        {
            return Mismatch;
        }

        RLPStatus status = Next(ref children, out RLPData field);
        status |= field.TryGetUInt64(out ulong nonce);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out uint256 gasPrice);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt64(out ulong gasLimit);
        status |= Next(ref children, out field);
        status |= field.TryGetAddress(out Address to);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out uint256 value);
        status |= Next(ref children, out field);
        int dataLength = field.PayloadLength;
        status |= field.TryGetFunctionSelector(out FunctionSelector selector);
        status |= Next(ref children, out RLPData vField);
        status |= Next(ref children, out RLPData rField);
        status |= Next(ref children, out RLPData sField);
        status |= RLPTypedExtensions.TryGetEvmLegacySignature(in vField, in rField, in sField, out Signature signature, out ulong? chainId);
        if (children.MoveNext() || status != RLPStatus.Success)
        {
            return Mismatch;
        }

        if (nonce != tx.Nonce || gasLimit != tx.GasLimit || gasPrice != tx.GasPrice || value != tx.Value || to != tx.To
            || selector.Value != ApproveSelector || signature != w.LegacySignature || chainId != (ulong)Chain98866)
        {
            return Mismatch;
        }

        return (long)(nonce + gasLimit) + (long)chainId.Value + dataLength + signature.V;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ReadEip1559Typed(Workload w)
    {
        RlpTransactionFixture tx = w.Tx;
        if (RLPData.TryDecodeComplete(w.Eip1559, in w.Limits, out RLPData root, out _) != RLPStatus.Success
            || root.TryGetChildren(out RLPChildEnumerator children) != RLPStatus.Success)
        {
            return Mismatch;
        }

        RLPStatus status = Next(ref children, out RLPData field);
        status |= field.TryGetUInt64(out ulong chainId);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt64(out ulong nonce);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out uint256 maxPriorityFee);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out uint256 maxFee);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt64(out ulong gasLimit);
        status |= Next(ref children, out field);
        status |= field.TryGetAddress(out Address to);
        status |= Next(ref children, out field);
        status |= field.TryGetUInt256(out uint256 value);
        status |= Next(ref children, out field);
        status |= field.TryGetFunctionSelector(out FunctionSelector selector);

        // Access list: [[address, [key, ...]], ...], each item compared with the fixture as it is read.
        status |= Next(ref children, out field);
        status |= field.TryGetChildren(out RLPChildEnumerator entries);
        int addressCount = 0;
        int keyCount = 0;
        while (entries.MoveNext())
        {
            status |= entries.Current.TryGetChildren(out RLPChildEnumerator entry);
            status |= Next(ref entry, out RLPData addressItem);
            status |= addressItem.TryGetAddress(out Address address);
            status |= Next(ref entry, out RLPData keyList);
            status |= keyList.TryGetChildren(out RLPChildEnumerator keyItems);
            if ((uint)addressCount >= (uint)tx.AccessListAddresses.Length || address != tx.AccessListAddresses[addressCount])
            {
                return Mismatch;
            }

            addressCount++;
            while (keyItems.MoveNext())
            {
                status |= keyItems.Current.TryGetHash32(out Hash32 key);
                if ((uint)keyCount >= (uint)tx.AccessListKeys.Length || key != tx.AccessListKeys[keyCount])
                {
                    return Mismatch;
                }

                keyCount++;
            }
        }

        status |= Next(ref children, out RLPData yParityField);
        status |= Next(ref children, out RLPData rField);
        status |= Next(ref children, out RLPData sField);
        status |= RLPTypedExtensions.TryGetEvmYParitySignature(in yParityField, in rField, in sField, out Signature signature);
        if (children.MoveNext() || status != RLPStatus.Success)
        {
            return Mismatch;
        }

        if (chainId != tx.ChainId || nonce != tx.Nonce || gasLimit != tx.GasLimit || maxPriorityFee != tx.MaxPriorityFee
            || maxFee != tx.MaxFee || value != tx.Value || to != tx.To || selector.Value != ApproveSelector
            || signature != w.Eip1559Signature)
        {
            return Mismatch;
        }

        return (long)(chainId + nonce + gasLimit) + (addressCount * 10L) + (keyCount * 100L) + signature.V;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long IndexLateField(Workload w)
    {
        if (RLPData.TryDecodeComplete(w.Eip1559, in w.Limits, out RLPData root, out _) != RLPStatus.Success)
        {
            return Mismatch;
        }

        Span<RLPRange> workspace = stackalloc RLPRange[16];
        if (root.TryIndexDirectChildren(workspace, out RLPChildrenIndex index, out _) != RLPStatus.Success
            || index.TryGetAt(RlpTransactionFixture.Eip1559FieldCount - 1, out RLPData field) != RLPStatus.Success
            || field.TryGetUInt256(out uint256 s) != RLPStatus.Success
            || s != w.Tx.S)
        {
            return Mismatch;
        }

        return index.Count;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long BuildLegacy(Workload w, byte[] output)
    {
        byte[] expected = w.LegacyChain98866.Encoded;
        int written = w.LegacyChain98866.Write(output);
        return written == expected.Length && output.AsSpan(0, written).SequenceEqual(expected) ? written : Mismatch;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long BuildEip1559(Workload w, byte[] output)
    {
        byte[] expected = w.Tx.Eip1559Encoded;
        int written = w.Tx.WriteEip1559WithSizing(output);
        return written == expected.Length && output.AsSpan(0, written).SequenceEqual(expected) ? written : Mismatch;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RLPStatus Next(ref RLPChildEnumerator children, out RLPData child)
    {
        if (children.MoveNext())
        {
            child = children.Current;
            return RLPStatus.Success;
        }

        child = default;
        return RLPStatus.TruncatedInput;
    }

    // ------------------------------------------------------------------------------------------------ workers

    private sealed class RunResult(int threads, bool timed)
    {
        public int Threads { get; } = threads;
        public bool Timed { get; } = timed;
        public double MeasuredSeconds { get; set; }
        public long Operations { get; set; }
        public long Checksum { get; set; }
        public int Gen0 { get; set; }
        public int Gen1 { get; set; }
        public int Gen2 { get; set; }
        public long AllocatedBytes { get; set; }
        public long[] Histogram { get; } = new long[RlpConcurrentThroughputRunner.Histogram.BucketCount];
        public ulong MaxNs { get; set; }
        public long[] OperationCounts { get; } = new long[OpCount];
        public double[] OperationNanosecondSums { get; } = new double[OpCount];
        public ulong[] OperationMaxNs { get; } = new ulong[OpCount];
        public long[][] OperationHistograms { get; } = new long[OpCount][];
    }

    private sealed class Worker(Workload workload, int index, bool timed, ManualResetEventSlim startGate)
    {
        // Preallocated before the run, so the measured loop allocates nothing.
        public readonly long[][] Histograms = timed ? CreateHistograms() : [];
        public readonly long[] Counts = new long[OpCount];
        public readonly long[] NanosecondSums = new long[OpCount];
        public readonly ulong[] MaxNs = new ulong[OpCount];
        public long Checksum;
        private readonly byte[] _output = new byte[4096];

        private static long[][] CreateHistograms()
        {
            var histograms = new long[OpCount][];
            for (int op = 0; op < OpCount; op++)
            {
                histograms[op] = new long[Histogram.BucketCount];
            }

            return histograms;
        }

        public void Run()
        {
            int op = -1;
            try
            {
                startGate.Wait();
                Workload w = workload;
                byte[] table = w.OpTable;
                long[] expected = w.Expected;
                byte[] output = _output;
                long[][] histograms = Histograms;
                Span<long> counts = stackalloc long[OpCount];
                Span<long> sums = stackalloc long[OpCount];
                Span<ulong> maxes = stackalloc ulong[OpCount];
                long checksum = 0;
                ulong state = unchecked(((ulong)BenchmarkData.Seed * 0x9E3779B97F4A7C15UL) + ((ulong)(index + 1) * 0xD1B54A32D192ED03UL));
                bool isTimed = timed;
                while (true)
                {
                    int phase = Volatile.Read(ref s_phase);
                    if (phase >= PhaseStop)
                    {
                        break;
                    }

                    // xorshift64*: per-thread, seeded, allocation-free.
                    state ^= state >> 12;
                    state ^= state << 25;
                    state ^= state >> 27;
                    op = table[(uint)((state * 0x2545F4914F6CDD1DUL) >> 32) % (uint)MixResolution];
                    long result;
                    if (isTimed)
                    {
                        long start = Stopwatch.GetTimestamp();
                        result = Execute(op, w, output);
                        long end = Stopwatch.GetTimestamp();
                        if (phase == PhaseMeasure && result == expected[op])
                        {
                            ulong ns = TicksToNs(end - start);
                            histograms[op][Histogram.IndexOf(ns)]++;
                            sums[op] += (long)ns;
                            if (ns > maxes[op])
                            {
                                maxes[op] = ns;
                            }
                        }
                    }
                    else
                    {
                        result = Execute(op, w, output);
                    }

                    if (result != expected[op])
                    {
                        Fail(op, expected[op], result, index);
                        break;
                    }

                    checksum += result;
                    if (phase == PhaseMeasure)
                    {
                        counts[op]++;
                    }
                }

                Checksum = checksum;
                counts.CopyTo(Counts);
                sums.CopyTo(NanosecondSums);
                maxes.CopyTo(MaxNs);
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref s_failure, $"thread {index} threw {ex.GetType().Name} in {(op >= 0 ? OpNames[op] : "set-up")}: {ex.Message}", null);
                Volatile.Write(ref s_phase, PhaseAbort);
            }
        }
    }

    private static void Fail(int op, long expected, long actual, int thread)
    {
        string detail = op >= OpHostileOversizedLength
            ? $" (hostile result = status * 1,000,000 + offset: expected status {(RLPStatus)(expected / 1_000_000)} at {expected % 1_000_000}, got status {(RLPStatus)(actual / 1_000_000)} at {actual % 1_000_000})"
            : actual == Mismatch ? " (the operation reported a wrong value, status or byte output)" : string.Empty;
        Interlocked.CompareExchange(ref s_failure, $"operation {OpNames[op]} on thread {thread}: expected {expected}, got {actual}{detail}.", null);
        Volatile.Write(ref s_phase, PhaseAbort);
    }

    /// <summary>One run at a fixed thread count; returns null when verification failed (see <c>s_failure</c>).</summary>
    private static RunResult? RunOnce(Workload workload, int threads, bool timed, double warmupSeconds, double measureSeconds)
    {
        s_failure = null;
        Volatile.Write(ref s_phase, PhaseWarmup);
        using var startGate = new ManualResetEventSlim(false);
        var workers = new Worker[threads];
        var threadObjects = new Thread[threads];
        for (int i = 0; i < threads; i++)
        {
            workers[i] = new Worker(workload, i, timed, startGate);
            threadObjects[i] = new Thread(workers[i].Run) { IsBackground = true, Name = $"rlp-worker-{i}" };
            threadObjects[i].Start();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        startGate.Set();
        if (!SleepUnlessAborted(warmupSeconds))
        {
            return Abort(threadObjects);
        }

        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        long measureStart = Stopwatch.GetTimestamp();
        Volatile.Write(ref s_phase, PhaseMeasure);
        bool completed = SleepUnlessAborted(measureSeconds);
        if (completed)
        {
            Volatile.Write(ref s_phase, PhaseStop);
        }

        long measureEnd = Stopwatch.GetTimestamp();
        int gen0End = GC.CollectionCount(0);
        int gen1End = GC.CollectionCount(1);
        int gen2End = GC.CollectionCount(2);
        long allocatedEnd = GC.GetTotalAllocatedBytes(precise: true);
        if (!completed)
        {
            return Abort(threadObjects);
        }

        foreach (Thread thread in threadObjects)
        {
            thread.Join();
        }

        if (Volatile.Read(ref s_phase) == PhaseAbort)
        {
            return null;
        }

        var result = new RunResult(threads, timed)
        {
            MeasuredSeconds = (measureEnd - measureStart) / (double)Stopwatch.Frequency,
            Gen0 = gen0End - gen0,
            Gen1 = gen1End - gen1,
            Gen2 = gen2End - gen2,
            AllocatedBytes = allocatedEnd - allocated,
        };
        foreach (Worker worker in workers)
        {
            result.Checksum += worker.Checksum;
            for (int op = 0; op < OpCount; op++)
            {
                result.OperationCounts[op] += worker.Counts[op];
                result.Operations += worker.Counts[op];
                result.OperationNanosecondSums[op] += worker.NanosecondSums[op];
                result.OperationMaxNs[op] = Math.Max(result.OperationMaxNs[op], worker.MaxNs[op]);
                result.MaxNs = Math.Max(result.MaxNs, worker.MaxNs[op]);
                if (timed)
                {
                    long[] source = worker.Histograms[op];
                    long[] merged = result.OperationHistograms[op] ??= new long[Histogram.BucketCount];
                    for (int bucket = 0; bucket < source.Length; bucket++)
                    {
                        merged[bucket] += source[bucket];
                        result.Histogram[bucket] += source[bucket];
                    }
                }
            }
        }

        return result;
    }

    private static RunResult? Abort(Thread[] threads)
    {
        Volatile.Write(ref s_phase, PhaseAbort);
        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        return null;
    }

    /// <summary>Sleeps in short slices so a verification failure ends the run promptly; false when the run was aborted.</summary>
    private static bool SleepUnlessAborted(double seconds)
    {
        long end = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < end)
        {
            if (Volatile.Read(ref s_phase) == PhaseAbort)
            {
                return false;
            }

            Thread.Sleep(20);
        }

        return Volatile.Read(ref s_phase) != PhaseAbort;
    }

    // ------------------------------------------------------------------------------------------------ reporting

    private static string BuildMarkdown(Options options, Workload workload, TimerOverhead overhead, List<RunResult> results, long checksum)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# RLP concurrent application throughput");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Run started {DateTime.Now:yyyy-MM-dd HH:mm:ss} (local time). Thread counts {string.Join(", ", options.ThreadCounts)}; warm-up {options.WarmupSeconds:0.#} s and measured {options.MeasureSeconds:0.#} s per timed run; untimed reference runs use 1 s warm-up and {Math.Min(options.MeasureSeconds, 5):0.#} s measured.");
        sb.AppendLine();
        AppendEnvironment(sb, overhead);
        AppendMix(sb, workload);
        AppendMethod(sb, overhead);

        sb.AppendLine("## Throughput and GC");
        sb.AppendLine();
        sb.AppendLine("The timed runs time every operation (two timestamp reads each); the untimed reference runs do not, so their ops/s shows what the instrumentation costs. GC columns are collections and managed bytes allocated by the whole process during the measured period only.");
        sb.AppendLine();
        sb.AppendLine("| Threads | Mode | Ops | ops/s total | ops/s per thread | gen0 | gen1 | gen2 | Allocated bytes |");
        sb.AppendLine("| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (RunResult r in results)
        {
            double perSecond = r.Operations / r.MeasuredSeconds;
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {r.Threads} | {(r.Timed ? "timed" : "untimed")} | {r.Operations:N0} | {perSecond:N0} | {perSecond / r.Threads:N0} | {r.Gen0} | {r.Gen1} | {r.Gen2} | {r.AllocatedBytes:N0} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Latency per operation (timed runs)");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Raw is the measured interval including one timer read pair; net subtracts the median timer overhead ({overhead.MedianNs} ns), clamped at 0. Percentiles are bucket upper bounds of a log-linear histogram (resolution about 1.6% above 128 ns); max is exact. Values in ns.");
        sb.AppendLine();
        sb.AppendLine("| Threads | p50 raw | p95 raw | p99 raw | p99.9 raw | max raw | p50 net | p95 net | p99 net | p99.9 net | max net |");
        sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (RunResult r in results.Where(x => x.Timed))
        {
            ulong[] raw = [Pct(r, 50), Pct(r, 95), Pct(r, 99), Pct(r, 99.9), r.MaxNs];
            sb.Append(CultureInfo.InvariantCulture, $"| {r.Threads} |");
            foreach (ulong value in raw)
            {
                sb.Append(CultureInfo.InvariantCulture, $" {value:N0} |");
            }

            foreach (ulong value in raw)
            {
                sb.Append(CultureInfo.InvariantCulture, $" {Net(value, overhead):N0} |");
            }

            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("## Latency by operation (timed runs, raw ns)");
        sb.AppendLine();
        foreach (RunResult r in results.Where(x => x.Timed))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Threads {r.Threads}:");
            sb.AppendLine();
            sb.AppendLine("| Operation | Share of ops | Mean | p50 | p99 | p99.9 | Max |");
            sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
            for (int op = 0; op < OpCount; op++)
            {
                long count = r.OperationCounts[op];
                long[] histogram = r.OperationHistograms[op] ?? [];
                double mean = count == 0 ? 0 : r.OperationNanosecondSums[op] / count;
                ulong max = r.OperationMaxNs[op];
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {OpNames[op]} | {100.0 * count / r.Operations:0.00}% | {mean:N0} | {Histogram.Percentile(histogram, count, 50, max):N0} | {Histogram.Percentile(histogram, count, 99, max):N0} | {Histogram.Percentile(histogram, count, 99.9, max):N0} | {max:N0} |");
            }

            sb.AppendLine();
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"Checksum of all verified operation results (consumed so none of the work is dead code): {checksum}");
        return sb.ToString();
    }

    private static ulong Pct(RunResult r, double percentile) => Histogram.Percentile(r.Histogram, r.Operations, percentile, r.MaxNs);

    private static ulong Net(ulong rawNs, TimerOverhead overhead) => rawNs > overhead.MedianNs ? rawNs - overhead.MedianNs : 0;

    private static void AppendMix(StringBuilder sb, Workload workload)
    {
        sb.AppendLine("## Operation mix");
        sb.AppendLine();
        sb.AppendLine("Each operation is drawn from a 1,000-entry table by a per-thread seeded xorshift64* generator, so the share is exact in expectation. All inputs are stable, owned byte arrays shared read-only by every thread; the writers use a per-thread output buffer and a `stackalloc` frame workspace. Default limits (1 MiB, depth 32, 8,192 items).");
        sb.AppendLine();
        sb.AppendLine("| Operation | Share | What it does |");
        sb.AppendLine("| --- | ---: | --- |");
        for (int op = 0; op < OpCount; op++)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {OpNames[op]} | {OpPerMille[op] / 10.0:0.#}% | {OpDescriptions[op]} |");
        }

        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Fixture sizes: legacy transaction {workload.LegacyChain1.Length} bytes, chain-98866 legacy transaction {workload.LegacyChain98866.Encoded.Length} bytes, EIP-1559 payload {workload.Eip1559.Length} bytes. Every operation is verified on every call against a value computed from fixture data and the independent reference encoder at start-up (typed fields, signature, chain id, status and offset for the hostile inputs, exact output bytes for the writers).");
        sb.AppendLine();
    }

    private static void AppendMethod(StringBuilder sb, TimerOverhead overhead)
    {
        sb.AppendLine("## Method and caveats");
        sb.AppendLine();
        sb.AppendLine("* Closed loop: a thread starts its next operation when the previous one ends. A stall therefore delays later operations instead of queueing them, so the percentiles under-report what an open-loop arrival process would see (coordinated omission is not modelled).");
        sb.AppendLine(CultureInfo.InvariantCulture, $"* Timer cost: one `Stopwatch.GetTimestamp` pair costs min {overhead.MinNs} ns, median {overhead.MedianNs} ns, p99 {overhead.P99Ns} ns, mean {overhead.MeanNs:0.0} ns (2,000,000 back-to-back pairs, measured once at start-up on one thread). That is comparable to the cheapest operations, so read the net columns for the codec and the raw columns for what the harness saw. The net figures assume the timer cost is constant, which a cache miss or a descheduled thread breaks in the tail.");
        sb.AppendLine("* Warm-up: a 1-thread burn-in (2 s) before any recorded run, then the warm-up of each run; tiered compilation and dynamic PGO are on (see the environment block). Only the measured period is counted.");
        sb.AppendLine("* Threads are not pinned and the CPU may be hybrid (performance and efficiency cores), so a thread's speed depends on where the scheduler put it. Run-to-run variation was not measured: each thread count was run once.");
        sb.AppendLine("* GC counts and allocated bytes are process-wide for the measured period. The library is expected to allocate nothing; the harness preallocates its histograms, buffers and thread objects before the run.");
        sb.AppendLine();
    }

    private static void AppendEnvironment(StringBuilder sb, TimerOverhead overhead)
    {
        sb.AppendLine("## Environment");
        sb.AppendLine();
        sb.AppendLine("| Item | Value |");
        sb.AppendLine("| --- | --- |");
        var runtimeConfig = new[] { "System.Runtime.TieredPGO", "System.Runtime.TieredCompilation", "System.Runtime.TieredCompilation.QuickJitForLoops", "System.GC.Server", "System.GC.Concurrent" };
        string cpuModel = ReadCpuModel();
        string governors = DistinctCpuFiles("cpufreq/scaling_governor");
        string[] rows =
        [
            $"CPU | {cpuModel}",
            $"Logical cores | {Environment.ProcessorCount}",
            $"Virtualisation | {ReadVirtualisation()}",
            $"Core types | {ReadCoreTypes()}",
            $"Clock regime | {DescribeClockRegime(governors)}",
            $"Memory architecture | {CountNumaNodes()} NUMA node(s) under /sys/devices/system/node; {ReadMemTotal()}; GC-visible memory {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024):N0} MiB",
            $"OS | {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})",
            $"SDK | {ReadSdkVersion()}",
            $"Runtime | {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture}), {(IsOptimized() ? "Release (optimised) build" : "NOT optimised")}",
            $"GC | {(GCSettings.IsServerGC ? "Server" : "Workstation")}, concurrent: {AppContext.GetData("System.GC.Concurrent") ?? "default"}, latency mode {GCSettings.LatencyMode}",
            $"Tiered compilation and PGO | environment {DescribeRuntimeEnvironment()}; runtimeconfig {string.Join(", ", runtimeConfig.Select(k => $"{k}={AppContext.GetData(k) ?? "default"}"))}",
            $"Thread pinning | not pinned (no affinity or priority set; process affinity mask {DescribeAffinity()})",
            $"Timer | Stopwatch frequency {Stopwatch.Frequency:N0} Hz, high resolution {Stopwatch.IsHighResolution}; pair overhead min/p50/p99 {overhead.MinNs}/{overhead.MedianNs}/{overhead.P99Ns} ns",
        ];
        foreach (string row in rows)
        {
            sb.AppendLine($"| {row} |");
        }

        sb.AppendLine();
    }

    private static bool IsOptimized() =>
        typeof(RlpConcurrentThroughputRunner).Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false) is not [DebuggableAttribute { IsJITOptimizerDisabled: true }];

    private static string ReadFile(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "unreadable";
        }
    }

    private static string ReadCpuModel()
    {
        try
        {
            foreach (string line in File.ReadLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name", StringComparison.Ordinal))
                {
                    return line[(line.IndexOf(':') + 1)..].Trim();
                }
            }

            return "not found in /proc/cpuinfo";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "unreadable (/proc/cpuinfo)";
        }
    }

    private static string ReadCoreTypes()
    {
        var parts = new List<string>();
        foreach ((string name, string label) in new[] { ("cpu_core", "performance cores"), ("cpu_atom", "efficiency cores"), ("cpu_lowpower", "low-power efficiency cores") })
        {
            string cpus = ReadFile($"/sys/devices/{name}/cpus");
            if (cpus is not ("unreadable" or ""))
            {
                parts.Add($"{label}: cpus {cpus}");
            }
        }

        string capacities = DistinctCpuFiles("cpu_capacity");
        string capacityNote = capacities == "unreadable" ? "cpu_capacity unreadable" : $"cpu_capacity {capacities}";
        return parts.Count == 0
            ? $"no hybrid topology visible to this OS (cpu_core/cpu_atom cpus files absent or empty; {capacityNote}); the CPU model may still be hybrid in hardware"
            : $"hybrid CPU, {string.Join("; ", parts)}; {capacityNote}";
    }

    private static string ReadVirtualisation()
    {
        bool hypervisorFlag = false;
        var mhz = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (string line in File.ReadLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("flags", StringComparison.Ordinal) && line.Contains(" hypervisor", StringComparison.Ordinal))
                {
                    hypervisorFlag = true;
                }
                else if (line.StartsWith("cpu MHz", StringComparison.Ordinal))
                {
                    mhz.Add(line[(line.IndexOf(':') + 1)..].Trim());
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "unknown (/proc/cpuinfo unreadable)";
        }

        string vendor = ReadFile("/sys/class/dmi/id/sys_vendor");
        string product = ReadFile("/sys/class/dmi/id/product_name");
        return $"{(hypervisorFlag ? "running under a hypervisor (cpuinfo hypervisor flag)" : "no hypervisor flag in /proc/cpuinfo")}; DMI {vendor} / {product}; guest /proc/cpuinfo reports cpu MHz {(mhz.Count == 0 ? "none" : string.Join("/", mhz))}";
    }

    private static string DistinctCpuFiles(string relativePath)
    {
        var values = new SortedSet<string>(StringComparer.Ordinal);
        for (int cpu = 0; cpu < Environment.ProcessorCount; cpu++)
        {
            values.Add(ReadFile($"/sys/devices/system/cpu/cpu{cpu}/{relativePath}"));
        }

        return values.Count == 0 ? "unreadable" : string.Join("/", values);
    }

    private static string DescribeClockRegime(string governors)
    {
        if (!Directory.Exists("/sys/devices/system/cpu/cpu0/cpufreq"))
        {
            return "cpufreq is not exposed to this OS (no /sys/devices/system/cpu/cpu0/cpufreq), so the governor, scaling limits and turbo state cannot be read; the frequency actually delivered is not known";
        }

        string cpu0 = "/sys/devices/system/cpu/cpu0/cpufreq";
        return $"governor {governors}; scaling min/max {ReadFile($"{cpu0}/scaling_min_freq")}/{ReadFile($"{cpu0}/scaling_max_freq")} kHz on cpu0 (hardware {ReadFile($"{cpu0}/cpuinfo_min_freq")}/{ReadFile($"{cpu0}/cpuinfo_max_freq")} kHz); turbo: {ReadTurbo()}; energy preference {DistinctCpuFiles("cpufreq/energy_performance_preference")}";
    }

    private static string ReadTurbo()
    {
        string noTurbo = ReadFile("/sys/devices/system/cpu/intel_pstate/no_turbo");
        string boost = ReadFile("/sys/devices/system/cpu/cpufreq/boost");
        string status = ReadFile("/sys/devices/system/cpu/intel_pstate/status");
        string turbo = noTurbo switch
        {
            "0" => "enabled (intel_pstate no_turbo=0)",
            "1" => "disabled (intel_pstate no_turbo=1)",
            _ => boost switch
            {
                "1" => "enabled (cpufreq boost=1)",
                "0" => "disabled (cpufreq boost=0)",
                _ => "unknown (no_turbo and boost files unreadable)",
            },
        };
        return status == "unreadable" ? turbo : $"{turbo}, intel_pstate {status}";
    }

    private static int CountNumaNodes()
    {
        try
        {
            return Directory.GetDirectories("/sys/devices/system/node", "node*").Count(d => int.TryParse(Path.GetFileName(d).AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out _));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string ReadMemTotal()
    {
        try
        {
            foreach (string line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    return $"MemTotal {line[9..].Trim()}";
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "MemTotal unreadable";
        }

        return "MemTotal not found";
    }

    private static string ReadSdkVersion()
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true, UseShellExecute = false });
            if (process is null || !process.WaitForExit(10_000))
            {
                return "unknown (dotnet --version did not finish)";
            }

            return $"{process.StandardOutput.ReadToEnd().Trim()} (dotnet --version in the working directory)";
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return "unknown (dotnet --version could not run)";
        }
    }

    private static string DescribeRuntimeEnvironment()
    {
        var set = new List<string>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && (key.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("COMPlus_", StringComparison.OrdinalIgnoreCase)))
            {
                set.Add($"{key}={entry.Value}");
            }
        }

        set.Sort(StringComparer.Ordinal);
        return set.Count == 0 ? "no DOTNET_*/COMPlus_* variables set (all defaults)" : string.Join(", ", set);
    }

    private static string DescribeAffinity()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
        {
            return "not readable on this OS";
        }

        try
        {
            using Process self = Process.GetCurrentProcess();
            long mask = (long)self.ProcessorAffinity;
            return $"0x{mask:x} ({BitOperations.PopCount((ulong)mask)} CPUs allowed)";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            return "unreadable";
        }
    }

    private static string BuildRunCsv(TimerOverhead overhead, List<RunResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("threads,mode,measured_seconds,operations,ops_per_second,ops_per_second_per_thread,p50_raw_ns,p95_raw_ns,p99_raw_ns,p999_raw_ns,max_raw_ns,p50_net_ns,p95_net_ns,p99_net_ns,p999_net_ns,max_net_ns,timer_overhead_median_ns,gen0,gen1,gen2,allocated_bytes");
        foreach (RunResult r in results)
        {
            double perSecond = r.Operations / r.MeasuredSeconds;
            sb.Append(CultureInfo.InvariantCulture, $"{r.Threads},{(r.Timed ? "timed" : "untimed")},{r.MeasuredSeconds:0.000},{r.Operations},{perSecond:0},{perSecond / r.Threads:0},");
            if (r.Timed)
            {
                ulong[] raw = [Pct(r, 50), Pct(r, 95), Pct(r, 99), Pct(r, 99.9), r.MaxNs];
                sb.Append(CultureInfo.InvariantCulture, $"{string.Join(",", raw)},{string.Join(",", raw.Select(v => Net(v, overhead)))},{overhead.MedianNs}");
            }
            else
            {
                sb.Append(",,,,,,,,,,");
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $",{r.Gen0},{r.Gen1},{r.Gen2},{r.AllocatedBytes}");
        }

        return sb.ToString();
    }

    private static string BuildOperationCsv(List<RunResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("threads,operation,operations,share_percent,mean_raw_ns,p50_raw_ns,p99_raw_ns,p999_raw_ns,max_raw_ns");
        foreach (RunResult r in results.Where(x => x.Timed))
        {
            for (int op = 0; op < OpCount; op++)
            {
                long count = r.OperationCounts[op];
                long[] histogram = r.OperationHistograms[op] ?? [];
                ulong max = r.OperationMaxNs[op];
                double mean = count == 0 ? 0 : r.OperationNanosecondSums[op] / count;
                sb.AppendLine(CultureInfo.InvariantCulture, $"{r.Threads},{OpNames[op]},{count},{100.0 * count / r.Operations:0.000},{mean:0.0},{Histogram.Percentile(histogram, count, 50, max)},{Histogram.Percentile(histogram, count, 99, max)},{Histogram.Percentile(histogram, count, 99.9, max)},{max}");
            }
        }

        return sb.ToString();
    }
}
