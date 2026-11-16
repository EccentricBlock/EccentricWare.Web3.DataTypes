using BenchmarkDotNet.Running;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

public static class Program
{
    // Usage (always Release):
    //   dotnet run -c Release --project EccentricWare.Web3.DataTypes.Benchmarks -- --filter "*Hash32Benchmarks*"
    //   dotnet run -c Release --project EccentricWare.Web3.DataTypes.Benchmarks -- --filter "*" --job short
    // See README.md in this folder for baseline/compare workflow.
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
