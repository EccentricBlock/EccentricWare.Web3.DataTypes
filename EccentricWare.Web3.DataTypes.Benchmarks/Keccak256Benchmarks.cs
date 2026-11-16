using BenchmarkDotNet.Attributes;
using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>
/// Keccak-256 dominates <see cref="FunctionSelector.FromSignature(string)"/> and EIP-55 address checksums.
/// Sizes straddle the 136-byte rate: 0/25/90 are single-block (typical signatures), 136 forces a second padding block,
/// 1024 is multi-block.
/// </summary>
[MemoryDiagnoser]
public class Keccak256Benchmarks
{
    [Params(0, 25, 90, 136, 1024)]
    public int Size { get; set; }

    private byte[] _input = null!;
    private byte[] _hash = null!;

    [GlobalSetup]
    public void Setup()
    {
        _input = BenchmarkData.RandomBytes(Size);
        _hash = new byte[32];
    }

    [Benchmark]
    public byte ComputeHash()
    {
        Keccak256.ComputeHash(_input, _hash);
        return _hash[0];
    }
}
