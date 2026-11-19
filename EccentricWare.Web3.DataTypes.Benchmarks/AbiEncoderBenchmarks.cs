using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.ABI;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// The write sequences of the benchmark workloads, one routine each, shared by the writer, measurer and call encoder rows and by the Nethereum
/// comparison. Each returns the number of bytes written, or -1 on any failure. The scratch is <c>stackalloc</c>ed per call (the caller-owned
/// scratch of spec 7.1), the destination is a caller-owned array.
/// </summary>
internal static class AbiWriteRoutines
{
    private const int Frames = 8;

    public static int Transfer(Span<byte> destination, ReadOnlySpan<byte> to, ReadOnlySpan<byte> amountWord)
    {
        Span<ABIWriteFrame> scratch = stackalloc ABIWriteFrame[Frames];
        var limits = AbiBenchmarkData.EncodeLimits;
        if (!ABIWriter.TryBegin(AbiBenchmarkData.Transfer.Schema, destination, scratch, in limits, out ABIWriter w, out _))
        {
            return -1;
        }

        bool ok = w.TryWriteAddress(to, out _) & w.TryWriteUInt256Word(amountWord, out _);
        return ok && w.TryFinish(out int written, out _) ? written : -1;
    }

    public static int TransferCall(Span<byte> destination, ReadOnlySpan<byte> to, ReadOnlySpan<byte> amountWord)
    {
        Span<ABIWriteFrame> scratch = stackalloc ABIWriteFrame[Frames];
        var limits = AbiBenchmarkData.EncodeLimits;
        if (!ABICallEncoder.TryBegin(AbiBenchmarkData.Transfer.Function, destination, scratch, in limits, out ABICallEncoder e, out _))
        {
            return -1;
        }

        bool ok = e.TryWriteAddress(to, out _) & e.TryWriteUInt256Word(amountWord, out _);
        return ok && e.TryFinish(out int written, out _) ? written : -1;
    }

    public static int ExactInputSingleCall(Span<byte> destination, AbiWorkload exact, ReadOnlySpan<byte> tokenIn, ReadOnlySpan<byte> tokenOut, ReadOnlySpan<byte> recipient, ReadOnlySpan<byte> amountInWord)
    {
        Span<ABIWriteFrame> scratch = stackalloc ABIWriteFrame[Frames];
        var limits = AbiBenchmarkData.EncodeLimits;
        if (!ABICallEncoder.TryBegin(exact.Function, destination, scratch, in limits, out ABICallEncoder e, out _))
        {
            return -1;
        }

        bool ok = e.TryBeginTuple(out _)
            & e.TryWriteAddress(tokenIn, out _) & e.TryWriteAddress(tokenOut, out _) & e.TryWriteUInt64(500, out _)
            & e.TryWriteAddress(recipient, out _) & e.TryWriteUInt64(1_700_000_000, out _) & e.TryWriteUInt256Word(amountInWord, out _)
            & e.TryWriteUInt64(1_800_000_000, out _) & e.TryWriteUInt64(0, out _)
            & e.TryEndContainer(out _);
        return ok && e.TryFinish(out int written, out _) ? written : -1;
    }

    public static int Mixed(Span<byte> destination, AbiWorkload mixed, ReadOnlySpan<byte> to, ReadOnlySpan<byte> payload)
    {
        Span<ABIWriteFrame> scratch = stackalloc ABIWriteFrame[Frames];
        var limits = AbiBenchmarkData.EncodeLimits;
        if (!ABIWriter.TryBegin(mixed.Schema, destination, scratch, in limits, out ABIWriter w, out _))
        {
            return -1;
        }

        bool ok = w.TryWriteAddress(to, out _) & w.TryWriteUInt64(42, out _) & w.TryWriteBytes(payload, out _);
        return ok && w.TryFinish(out int written, out _) ? written : -1;
    }

    public static int Bytes(Span<byte> destination, AbiWorkload workload, ReadOnlySpan<byte> payload, in ABIEncodeLimits limits)
    {
        Span<ABIWriteFrame> scratch = stackalloc ABIWriteFrame[Frames];
        if (!ABIWriter.TryBegin(workload.Schema, destination, scratch, in limits, out ABIWriter w, out _))
        {
            return -1;
        }

        return w.TryWriteBytes(payload, out _) && w.TryFinish(out int written, out _) ? written : -1;
    }

    public static int MulticallCall(Span<byte> destination, AbiWorkload multicall, byte[][] children)
    {
        Span<ABIWriteFrame> scratch = stackalloc ABIWriteFrame[Frames];
        var limits = AbiBenchmarkData.EncodeLimits;
        if (!ABICallEncoder.TryBegin(multicall.Function, destination, scratch, in limits, out ABICallEncoder e, out _))
        {
            return -1;
        }

        if (!e.TryBeginDynamicArray(children.Length, out _))
        {
            return -1;
        }

        foreach (byte[] child in children)
        {
            if (!e.TryWriteBytes(child, out _))
            {
                return -1;
            }
        }

        return e.TryEndContainer(out _) && e.TryFinish(out int written, out _) ? written : -1;
    }

    public static int MulticallMeasure(AbiWorkload multicall, byte[][] children)
    {
        Span<ABIMeasureFrame> scratch = stackalloc ABIMeasureFrame[Frames];
        var limits = AbiBenchmarkData.EncodeLimits;
        if (!ABIMeasurer.TryBegin(multicall.Schema, scratch, in limits, out ABIMeasurer m, out _) || !m.TryBeginDynamicArray(children.Length, out _))
        {
            return -1;
        }

        foreach (byte[] child in children)
        {
            if (!m.TryWriteBytes(child, out _))
            {
                return -1;
            }
        }

        return m.TryEndContainer(out _) && m.TryFinish(out int length, out _) ? length : -1;
    }
}

/// <summary>Caller-backed <see cref="ABICallEncoder"/> / <see cref="ABIWriter"/> on the ERC-20 <c>transfer</c>, Uniswap V3 <c>exactInputSingle</c> and a mixed <c>(address,uint256,bytes)</c>.</summary>
[MemoryDiagnoser]
public class AbiWriterBenchmarks
{
    private AbiWorkload _transfer = null!;
    private AbiWorkload _exact = null!;
    private AbiWorkload _mixed = null!;
    private byte[] _payload = null!;
    private byte[] _destination = null!;

    [GlobalSetup]
    public void Setup()
    {
        _transfer = AbiBenchmarkData.Transfer;
        _exact = AbiBenchmarkData.ExactInputSingle;
        _mixed = AbiBenchmarkData.Mixed(65);
        _payload = ((EccentricWare.Web3.DataTypes.Tests.ABI.RBlob)_mixed.Value.Items[2]).Data;
        _destination = new byte[4096];

        Check(Write_Erc20TransferCall(), _transfer.Call, "transfer call");
        Check(Write_Erc20TransferArguments(), _transfer.Arguments, "transfer arguments");
        Check(Write_UniswapV3ExactInputSingleCall(), _exact.Call, "exactInputSingle call");
        Check(Write_MixedAddressUintBytes65(), _mixed.Arguments, "mixed arguments");
    }

    private void Check(int written, byte[] expected, string what)
    {
        AbiBenchmarkData.RequireTrue(written == expected.Length, $"{what}: wrote {written}, expected {expected.Length}");
        AbiBenchmarkData.RequireEqual(expected, _destination.AsSpan(0, written), what);
    }

    [Benchmark]
    public int Write_Erc20TransferCall() => AbiWriteRoutines.TransferCall(_destination, AbiBenchmarkData.VitalikAddress, AbiBenchmarkData.OneEthWord);

    [Benchmark]
    public int Write_Erc20TransferArguments() => AbiWriteRoutines.Transfer(_destination, AbiBenchmarkData.VitalikAddress, AbiBenchmarkData.OneEthWord);

    [Benchmark]
    public int Write_UniswapV3ExactInputSingleCall() =>
        AbiWriteRoutines.ExactInputSingleCall(_destination, _exact, AbiBenchmarkData.WethAddress, AbiBenchmarkData.UsdcAddress, AbiBenchmarkData.VitalikAddress, AbiBenchmarkData.OneEthWord);

    [Benchmark]
    public int Write_MixedAddressUintBytes65() => AbiWriteRoutines.Mixed(_destination, _mixed, AbiBenchmarkData.VitalikAddress, _payload);
}

/// <summary>Dynamic <c>bytes</c> written at 0, 31, 32, 33, 65 bytes and 262,048 bytes (about 256 KiB, the largest whole-word payload whose call fits the default limit): one copy of the payload, padding only, no scratch growth.</summary>
[MemoryDiagnoser]
public class AbiWriterBytesBenchmarks
{
    private AbiWorkload _workload = null!;
    private byte[] _payload = null!;
    private byte[] _destination = null!;
    private ABIEncodeLimits _limits;

    [Params(0, 31, 32, 33, 65, 262_048)]
    public int Length { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _limits = AbiBenchmarkData.EncodeLimits;
        _workload = AbiBenchmarkData.Bytes(Length);
        _payload = ((EccentricWare.Web3.DataTypes.Tests.ABI.RBlob)_workload.Value.Items[0]).Data;
        _destination = new byte[_workload.Arguments.Length + 64];
        int written = Write();
        AbiBenchmarkData.RequireTrue(written == _workload.Arguments.Length, "bytes: length");
        AbiBenchmarkData.RequireEqual(_workload.Arguments, _destination.AsSpan(0, written), "bytes");
    }

    [Benchmark]
    public int Write() => AbiWriteRoutines.Bytes(_destination, _workload, _payload, in _limits);
}

/// <summary><c>multicall(bytes[])</c> written with 2 and 128 children (the spec 8.2 shape): count word, offset table backfill, one padded tail per child.</summary>
[MemoryDiagnoser]
public class AbiWriterMulticallBenchmarks
{
    private AbiWorkload _workload = null!;
    private byte[][] _children = null!;
    private byte[] _destination = null!;

    [Params(2, 128)]
    public int Children { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _workload = AbiBenchmarkData.Multicall(Children);
        _children = ((EccentricWare.Web3.DataTypes.Tests.ABI.RSeq)_workload.Value.Items[0]).Items.Select(i => ((EccentricWare.Web3.DataTypes.Tests.ABI.RBlob)i).Data).ToArray();
        _destination = new byte[_workload.Call.Length + 64];
        int written = WriteCall();
        AbiBenchmarkData.RequireTrue(written == _workload.Call.Length, "multicall: length");
        AbiBenchmarkData.RequireEqual(_workload.Call, _destination.AsSpan(0, written), "multicall");
        AbiBenchmarkData.RequireTrue(Measure() == _workload.Arguments.Length, "multicall: measured length");
    }

    [Benchmark]
    public int WriteCall() => AbiWriteRoutines.MulticallCall(_destination, _workload, _children);

    /// <summary>The first pass of a measure-then-write pipeline: the same events, no destination.</summary>
    [Benchmark]
    public int Measure() => AbiWriteRoutines.MulticallMeasure(_workload, _children);
}
