using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// <see cref="RLPData.TryIndexDirectChildren"/> into a <c>stackalloc</c> workspace for transaction-shaped lists of 9, 12
/// and 16 fields. The index is built <see cref="Repeats"/> times per decode (the decode is amortised into the figure).
/// <c>Index_CountPassOnly</c> passes a zero-length workspace, so the call stops after the counting pass with
/// <see cref="RLPStatus.IndexCapacityExceeded"/>; <c>Index_Build</c> minus <c>Index_CountPassOnly</c> is the cost of the
/// second (populating) pass. Together they answer whether a single-pass fast path could pay.
/// </summary>
[MemoryDiagnoser]
public class RlpIndexBenchmarks
{
    private const int Repeats = 64;
    private const int WorkspaceSlots = 16;

    private RLPDecodeLimits _limits;
    private byte[] _input = null!;
    private RLPRange[] _largeWorkspace = null!;

    [Params(9, 12, 16)]
    public int FieldCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _limits = RLPDecodeLimits.Default;
        _input = RlpBenchmarkData.TransactionShapedList(FieldCount);
        RlpBenchmarkData.RequireDecodes(_input, RLPKind.List, $"index input {FieldCount}");
        _largeWorkspace = new RLPRange[_input.Length]; // at least one slot per payload byte: the precondition of a single-pass build
        RLPData.TryDecodeComplete(_input, in _limits, out RLPData root, out _);

        Span<RLPRange> workspace = stackalloc RLPRange[WorkspaceSlots];
        RlpBenchmarkData.RequireSuccess(root.TryIndexDirectChildren(workspace, out RLPChildrenIndex index, out int required), "index");
        if (required != FieldCount || index.Count != FieldCount)
        {
            throw new InvalidOperationException($"Index counted {required}/{index.Count}, expected {FieldCount}.");
        }

        RlpBenchmarkData.RequireStatus(RLPStatus.IndexCapacityExceeded, root.TryIndexDirectChildren(default, out _, out required), "count pass");
        if (required != FieldCount)
        {
            throw new InvalidOperationException($"Count pass reported {required}, expected {FieldCount}.");
        }
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int Index_Build()
    {
        if (RLPData.TryDecodeComplete(_input, in _limits, out RLPData root, out _) != RLPStatus.Success)
        {
            return -1;
        }

        Span<RLPRange> workspace = stackalloc RLPRange[WorkspaceSlots];
        int total = 0;
        for (int i = 0; i < Repeats; i++)
        {
            root.TryIndexDirectChildren(workspace, out RLPChildrenIndex index, out int required);
            total += index.Count + required;
        }

        return total;
    }

    /// <summary>Same build into a workspace with a slot per payload byte (heap array, set up once): the shape a single-pass fast path would need.</summary>
    [Benchmark(OperationsPerInvoke = Repeats)]
    public int Index_Build_WorkspaceOfPayloadLength()
    {
        if (RLPData.TryDecodeComplete(_input, in _limits, out RLPData root, out _) != RLPStatus.Success)
        {
            return -1;
        }

        int total = 0;
        for (int i = 0; i < Repeats; i++)
        {
            root.TryIndexDirectChildren(_largeWorkspace, out RLPChildrenIndex index, out int required);
            total += index.Count + required;
        }

        return total;
    }

    [Benchmark(OperationsPerInvoke = Repeats)]
    public int Index_CountPassOnly()
    {
        if (RLPData.TryDecodeComplete(_input, in _limits, out RLPData root, out _) != RLPStatus.Success)
        {
            return -1;
        }

        int total = 0;
        for (int i = 0; i < Repeats; i++)
        {
            root.TryIndexDirectChildren(default, out _, out int required);
            total += required;
        }

        return total;
    }
}

/// <summary>
/// Reading one field of a 12-field list by ordinal: through an index built once (<c>TryGetAt</c>, O(1)) versus a fresh
/// <see cref="RLPChildEnumerator"/> scan to the same ordinal. Each invocation does <see cref="Lookups"/> lookups after one
/// decode (and, for the indexed row, one index build), so the figure is per lookup. Break-even for a single field:
/// index build cost divided by (scan minus indexed) per lookup.
/// </summary>
[MemoryDiagnoser]
public class RlpIndexLookupBenchmarks
{
    private const int FieldCount = 12;
    private const int Lookups = 1024;

    private RLPDecodeLimits _limits;
    private byte[] _input = null!;
    private int _ordinal;

    [Params("first", "middle", "last")]
    public string Position { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _limits = RLPDecodeLimits.Default;
        _ordinal = Position switch
        {
            "first" => 0,
            "middle" => FieldCount / 2,
            "last" => FieldCount - 1,
            _ => throw new InvalidOperationException($"Unknown Position {Position}."),
        };
        _input = RlpBenchmarkData.TransactionShapedList(FieldCount);
        RlpBenchmarkData.RequireDecodes(_input, RLPKind.List, "lookup input");

        RLPData.TryDecodeComplete(_input, in _limits, out RLPData root, out _);
        Span<RLPRange> workspace = stackalloc RLPRange[FieldCount];
        RlpBenchmarkData.RequireSuccess(root.TryIndexDirectChildren(workspace, out RLPChildrenIndex index, out _), "index");
        RlpBenchmarkData.RequireSuccess(index.TryGetAt(_ordinal, out RLPData indexed), "indexed read");
        RlpBenchmarkData.RequireSuccess(root.TryGetChildren(out RLPChildEnumerator children), "children");
        for (int i = 0; i <= _ordinal; i++)
        {
            children.MoveNext();
        }

        if (indexed.EncodedRange != children.Current.EncodedRange)
        {
            throw new InvalidOperationException("Indexed child differs from enumerated child.");
        }
    }

    [Benchmark(OperationsPerInvoke = Lookups)]
    public int Indexed_TryGetAt()
    {
        if (RLPData.TryDecodeComplete(_input, in _limits, out RLPData root, out _) != RLPStatus.Success)
        {
            return -1;
        }

        Span<RLPRange> workspace = stackalloc RLPRange[FieldCount];
        if (root.TryIndexDirectChildren(workspace, out RLPChildrenIndex index, out _) != RLPStatus.Success)
        {
            return -1;
        }

        int total = 0;
        for (int i = 0; i < Lookups; i++)
        {
            index.TryGetAt(_ordinal, out RLPData child);
            total += child.PayloadLength;
        }

        return total;
    }

    [Benchmark(OperationsPerInvoke = Lookups)]
    public int Scan_EnumeratorToOrdinal()
    {
        if (RLPData.TryDecodeComplete(_input, in _limits, out RLPData root, out _) != RLPStatus.Success)
        {
            return -1;
        }

        int total = 0;
        for (int i = 0; i < Lookups; i++)
        {
            root.TryGetChildren(out RLPChildEnumerator children);
            for (int step = 0; step <= _ordinal; step++)
            {
                children.MoveNext();
            }

            total += children.Current.PayloadLength;
        }

        return total;
    }
}
