using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// Strict decode of hostile and boundary inputs (spec 13.1): oversized advertised lengths, a list nested exactly at and one
/// past the depth limit, a list holding exactly and one more than the item limit, and non-canonical and truncated
/// encodings. Rejections must be cheap and allocation-free; the <c>*AtLimit</c> cases succeed and show the worst
/// accepted cost. <c>[GlobalSetup]</c> checks the expected status of each case so a wrong input fails loudly.
/// </summary>
[MemoryDiagnoser]
public class RlpHostileInputBenchmarks
{
    private const int DepthLimit = 32;
    private const int ItemLimit = 8192;
    private const int SmallDocumentLimit = 64;

    private RLPDecodeLimits _limits;
    private byte[] _input = null!;

    [Params(
        "TooLargeInput",
        "LengthOverflow4Bytes",
        "LengthOverflow_bf_ff8",
        "TruncatedLongList",
        "TruncatedChild",
        "NonCanonicalSingleByte",
        "NonCanonicalLongLength",
        "NonCanonicalLastOf1024",
        "DepthAtLimit",
        "DepthBomb",
        "ItemsAtLimit",
        "ItemsOverLimit")]
    public string Case { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _limits = RLPDecodeLimits.Default;
        RLPStatus expected = RLPStatus.Success;
        switch (Case)
        {
            case "TooLargeInput":
                _limits = new RLPDecodeLimits(SmallDocumentLimit, DepthLimit, ItemLimit);
                _input = [0xb8, 0x40, .. new byte[SmallDocumentLimit]]; // 66 bytes of valid RLP, two over the limit
                expected = RLPStatus.TooLarge;
                break;
            case "LengthOverflow4Bytes":
                _input = [0xbb, 0xff, 0xff, 0xff, 0xff]; // length 0xffffffff
                expected = RLPStatus.LengthOverflow;
                break;
            case "LengthOverflow_bf_ff8":
                _input = [0xbf, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff];
                expected = RLPStatus.LengthOverflow;
                break;
            case "TruncatedLongList":
                _input = [0xf9, 0x03, 0xe8, .. new byte[10]]; // announces 1,000 payload bytes, carries 10
                expected = RLPStatus.TruncatedInput;
                break;
            case "TruncatedChild":
                _input = [0xc3, 0x83, 0x61, 0x62]; // the child claims 3 payload bytes, the list has 2 left
                expected = RLPStatus.InvalidListBoundary;
                break;
            case "NonCanonicalSingleByte":
                _input = [0x81, 0x05];
                expected = RLPStatus.NonCanonicalEncoding;
                break;
            case "NonCanonicalLongLength":
                _input = [0xb8, 0x05, .. new byte[5]]; // long form for a length below 56
                expected = RLPStatus.NonCanonicalEncoding;
                break;
            case "NonCanonicalLastOf1024":
                // 1,023 valid 4-byte strings, then 81 05: the whole list is walked before the defect is found.
                byte[] valid = RlpBenchmarkData.FlatList(1023, 4);
                byte[] payload = [.. valid.AsSpan(3).ToArray(), 0x81, 0x05];
                _input = [0xf9, (byte)(payload.Length >> 8), (byte)payload.Length, .. payload];
                expected = RLPStatus.NonCanonicalEncoding;
                break;
            case "DepthAtLimit":
                _input = RlpBenchmarkData.NestedLists(DepthLimit);
                break;
            case "DepthBomb":
                _input = RlpBenchmarkData.NestedLists(DepthLimit + 1);
                expected = RLPStatus.TooDeep;
                break;
            case "ItemsAtLimit":
                _input = ManyLiterals(ItemLimit - 1); // root + 8,191 children = 8,192 items
                break;
            case "ItemsOverLimit":
                _input = ManyLiterals(ItemLimit); // 8,193 items
                expected = RLPStatus.TooManyItems;
                break;
            default:
                throw new InvalidOperationException($"Unknown Case {Case}.");
        }

        RLPStatus actual = RLPData.TryDecodeComplete(_input, in _limits, out _, out _);
        RlpBenchmarkData.RequireStatus(expected, actual, $"hostile case {Case}");
    }

    private static byte[] ManyLiterals(int count)
    {
        var bytes = new byte[3 + count];
        bytes[0] = 0xf9;
        bytes[1] = (byte)(count >> 8);
        bytes[2] = (byte)count;
        bytes.AsSpan(3).Fill(0x01);
        return bytes;
    }

    [Benchmark]
    public int Decode_Hostile()
    {
        RLPStatus status = RLPData.TryDecodeComplete(_input, in _limits, out _, out RLPError error);
        return (int)status + error.Offset;
    }
}
