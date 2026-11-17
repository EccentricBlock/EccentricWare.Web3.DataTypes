using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// One <see cref="RLPChildEnumerator"/> pass over a validated flat list of 64 children. The reported time is per child
/// (<c>OperationsPerInvoke = 64</c>), with the one document decode amortised into it. Payload shapes cover the header
/// classes: literal byte, short string, 32-byte hash, long-form string (two length bytes), a mixed cycle, and child lists.
/// </summary>
[MemoryDiagnoser]
public class RlpEnumerationBenchmarks
{
    private const int ChildCount = 64;

    private static readonly int[] MixedLengths = [0, 1, 4, 20, 32, 56, 100, 300];

    private RLPDecodeLimits _limits;
    private byte[] _input = null!;

    [Params("literal", "short4", "hash32", "long300", "mixed", "lists")]
    public string Payload { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _limits = RLPDecodeLimits.Default;
        _input = Payload switch
        {
            "literal" => RlpBenchmarkData.FlatList(ChildCount, 1),
            "short4" => RlpBenchmarkData.FlatList(ChildCount, 4),
            "hash32" => RlpBenchmarkData.FlatList(ChildCount, 32),
            "long300" => RlpBenchmarkData.FlatList(ChildCount, 300),
            "mixed" => RlpBenchmarkData.MixedFlatList(ChildCount, MixedLengths),
            "lists" => RlpBenchmarkData.ListOfLists(ChildCount),
            _ => throw new InvalidOperationException($"Unknown Payload {Payload}."),
        };

        RlpBenchmarkData.RequireDecodes(_input, RLPKind.List, $"flat list {Payload}");
        RLPData.TryDecodeComplete(_input, in _limits, out RLPData root, out _);
        RlpBenchmarkData.RequireSuccess(root.TryGetChildren(out RLPChildEnumerator children), "children");
        int count = 0;
        int expectedEnd = root.PayloadRange.Offset;
        while (children.MoveNext())
        {
            RLPData child = children.Current;
            if (child.EncodedRange.Offset != expectedEnd)
            {
                throw new InvalidOperationException($"Child {count} starts at {child.EncodedRange.Offset}, expected {expectedEnd}.");
            }

            expectedEnd = child.EncodedRange.Offset + child.EncodedLength;
            count++;
        }

        if (count != ChildCount || expectedEnd != _input.Length)
        {
            throw new InvalidOperationException($"Enumerated {count} children ending at {expectedEnd} of {_input.Length}.");
        }
    }

    [Benchmark(OperationsPerInvoke = ChildCount)]
    public int Enumerate_Children()
    {
        if (RLPData.TryDecodeComplete(_input, in _limits, out RLPData root, out _) != RLPStatus.Success
            || root.TryGetChildren(out RLPChildEnumerator children) != RLPStatus.Success)
        {
            return -1;
        }

        int total = 0;
        while (children.MoveNext())
        {
            total += children.Current.PayloadLength;
        }

        return total;
    }
}

/// <summary>
/// A recursive walk that visits every item (strings included) of a decoded document through
/// <see cref="RLPData.TryGetChildren"/>. Time is per walk; <see cref="ExpectedItems"/> is the item count.
/// </summary>
[MemoryDiagnoser]
public class RlpEnumerationWalkBenchmarks
{
    private RLPDecodeLimits _limits;
    private byte[] _input = null!;

    [Params("legacy9", "eip1559", "depth4", "depth16")]
    public string Shape { get; set; } = "";

    /// <summary>Items visited per walk: root, nested lists and strings. EIP-1559 is 1 + 12 fields + 2 access entries of 5 items.</summary>
    public int ExpectedItems => Shape switch
    {
        "legacy9" => 10,
        "eip1559" => 23,
        "depth4" => 5,
        "depth16" => 17,
        _ => throw new InvalidOperationException($"Unknown Shape {Shape}."),
    };

    [GlobalSetup]
    public void Setup()
    {
        _limits = RLPDecodeLimits.Default;
        _input = Shape switch
        {
            "legacy9" => RlpBenchmarkData.Transactions.LegacyEncoded,
            "eip1559" => RlpBenchmarkData.Transactions.Eip1559Encoded,
            "depth4" => RlpBenchmarkData.NestedLists(4),
            "depth16" => RlpBenchmarkData.NestedLists(16),
            _ => throw new InvalidOperationException($"Unknown Shape {Shape}."),
        };

        RlpBenchmarkData.RequireDecodes(_input, RLPKind.List, $"walk shape {Shape}");
        RLPData.TryDecodeComplete(_input, in _limits, out RLPData root, out _);
        int visited = Walk(root);
        if (visited != ExpectedItems)
        {
            throw new InvalidOperationException($"Walk visited {visited} items, expected {ExpectedItems}.");
        }
    }

    [Benchmark]
    public int Walk_AllItems()
    {
        return RLPData.TryDecodeComplete(_input, in _limits, out RLPData root, out _) == RLPStatus.Success ? Walk(root) : -1;
    }

    private static int Walk(in RLPData node)
    {
        int visited = 1;
        if (node.Kind == RLPKind.List && node.TryGetChildren(out RLPChildEnumerator children) == RLPStatus.Success)
        {
            while (children.MoveNext())
            {
                visited += Walk(children.Current);
            }
        }

        return visited;
    }
}
