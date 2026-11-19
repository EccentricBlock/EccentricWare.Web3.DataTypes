using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.ABI;
using EccentricWare.Web3.DataTypes.Tests.ABI;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// Strict decode of malformed and hostile documents: bounded rejection latency (spec 11.2). <c>[GlobalSetup]</c> checks that each input fails with
/// the status in its name, so a case that quietly turned valid fails loudly. A pointer error near the start must be rejected after a handful of
/// words and one near the end after the whole offset table; zero-width elements are bounded by the visited-value budget, not by the 320 bytes of input.
/// </summary>
[MemoryDiagnoser]
public class AbiHostileInputBenchmarks
{
    private ABIDecodeLimits _limits;
    private ABISchema _multicall = null!;
    private ABISchema _bytes = null!;
    private ABISchema _emptyTuples = null!;
    private ABISchema _fiveEmptyTupleArrays = null!;
    private byte[] _badFirstPointer = null!;
    private byte[] _badLastPointer = null!;
    private byte[] _huge256BitCount = null!;
    private byte[] _hugeLength = null!;
    private byte[] _zeroWidth4096 = null!;
    private byte[] _zeroWidthBomb = null!;
    private byte[] _trailingByte = null!;

    [GlobalSetup]
    public void Setup()
    {
        _limits = AbiBenchmarkData.DecodeLimits;
        AbiWorkload multicall = AbiBenchmarkData.Multicall(128);
        _multicall = multicall.Schema;
        _bytes = AbiBenchmarkData.CompileTuple("(bytes)");
        _emptyTuples = AbiBenchmarkData.CompileTuple("(()[])");
        _fiveEmptyTupleArrays = AbiBenchmarkData.CompileTuple("(()[],()[],()[],()[],()[])");

        // Element table of the 128-child document starts at byte 64 (head 32 + count 32); element 0's pointer is the word at 64, element 127's at 64 + 127 * 32.
        _badFirstPointer = (byte[])multicall.Arguments.Clone();
        _badFirstPointer[64 + 31] ^= 0x01; // 0x1000 -> 0x1001: not word aligned
        _badLastPointer = (byte[])multicall.Arguments.Clone();
        _badLastPointer[64 + (127 * 32) + 31] ^= 0x01;

        _huge256BitCount = [.. Word(0x20), .. Enumerable.Repeat((byte)0xff, 32)]; // bytes[] count 2^256 - 1 (multicall schema)
        _hugeLength = [.. Word(0x20), .. Enumerable.Repeat((byte)0xff, 32)]; // bytes length 2^256 - 1
        _zeroWidth4096 = ABIReference.EncodeRoot(ABIReference.ParseTuple("(()[])"), new RSeq([new RSeq(Enumerable.Range(0, 4096).Select(_ => (RValue)new RSeq([])).ToArray())]));
        var zeroArrays = new RSeq(Enumerable.Range(0, 5).Select(_ => (RValue)new RSeq(Enumerable.Range(0, 4096).Select(_ => (RValue)new RSeq([])).ToArray())).ToArray());
        _zeroWidthBomb = ABIReference.EncodeRoot(ABIReference.ParseTuple("(()[],()[],()[],()[],()[])"), zeroArrays);
        _trailingByte = [.. multicall.Arguments, 0x00];

        Expect(_multicall, _badFirstPointer, ABIStatus.InvalidOffset);
        Expect(_multicall, _badLastPointer, ABIStatus.InvalidOffset);
        Expect(_multicall, _huge256BitCount, ABIStatus.ElementLimitExceeded);
        Expect(_bytes, _hugeLength, ABIStatus.Truncated);
        Expect(_emptyTuples, _zeroWidth4096, ABIStatus.Success);
        Expect(_fiveEmptyTupleArrays, _zeroWidthBomb, ABIStatus.WorkLimitExceeded);
        Expect(_multicall, _trailingByte, ABIStatus.NonCanonicalLayout);
        AbiBenchmarkData.RequireTrue(_zeroWidthBomb.Length == 320, $"zero-width bomb is {_zeroWidthBomb.Length} bytes");
    }

    private static byte[] Word(ulong value) => ABIReference.ToWord(value);

    private void Expect(ABISchema schema, byte[] input, ABIStatus expected)
    {
        ABIData.TryDecodeComplete(input, schema, in _limits, out _, out ABIStatus status);
        AbiBenchmarkData.RequireTrue(status == expected, $"expected {expected}, got {status}");
    }

    [Benchmark]
    public ABIStatus Reject_PointerErrorInFirstOf128() => Status(_multicall, _badFirstPointer);

    [Benchmark]
    public ABIStatus Reject_PointerErrorInLastOf128() => Status(_multicall, _badLastPointer);

    [Benchmark]
    public ABIStatus Reject_ArrayCount2Pow256Minus1() => Status(_multicall, _huge256BitCount);

    [Benchmark]
    public ABIStatus Reject_BytesLength2Pow256Minus1() => Status(_bytes, _hugeLength);

    [Benchmark]
    public ABIStatus Reject_TrailingByteAfterTheLastTail() => Status(_multicall, _trailingByte);

    /// <summary>The largest zero-width array under the default limits: accepted, 4,098 values (root, array, 4,096 elements) from 64 bytes.</summary>
    [Benchmark]
    public ABIStatus Accept_4096ZeroWidthElements() => Status(_emptyTuples, _zeroWidth4096);

    /// <summary>Five such arrays (20,486 values) from 320 bytes: refused at the visited-value budget (16,384), not after 20,486 values.</summary>
    [Benchmark]
    public ABIStatus Reject_ZeroWidthValueBomb() => Status(_fiveEmptyTupleArrays, _zeroWidthBomb);

    private ABIStatus Status(ABISchema schema, byte[] input)
    {
        ABIData.TryDecodeComplete(input, schema, in _limits, out _, out ABIStatus status);
        return status;
    }
}
