using BenchmarkDotNet.Running;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

public static class Program
{
    // Usage (always Release):
    //   dotnet run -c Release --project EccentricWare.Web3.DataTypes.Benchmarks -- --filter "*Hash32Benchmarks*"
    //   dotnet run -c Release --project EccentricWare.Web3.DataTypes.Benchmarks -- --filter "*" --job short
    //   dotnet run -c Release --project EccentricWare.Web3.DataTypes.Benchmarks -- --rlp-concurrency [--threads 1,2,4] [--seconds 10] [--out <dir>]
    //     (the stand-alone RLP concurrent-throughput harness, not a BenchmarkDotNet job; see README.md)
    // See README.md in this folder for baseline/compare workflow.
    public static void Main(string[] args)
    {
        if (Array.IndexOf(args, RlpConcurrentThroughputRunner.Flag) >= 0)
        {
            Environment.ExitCode = RlpConcurrentThroughputRunner.Run(args);
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
