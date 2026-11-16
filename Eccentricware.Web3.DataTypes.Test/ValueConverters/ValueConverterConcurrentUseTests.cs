using System.Collections.Concurrent;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>EF shares one converter instance across contexts and threads; conversions must give the single-threaded results.</summary>
[TestClass]
public sealed class ValueConverterConcurrentUseTests
{
    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void OneInstance_UsedFromManyThreads_RoundTripsEveryValue(string name)
        => ConverterCases.Get(name).Invoke(typeof(ValueConverterConcurrentUseTests), nameof(ParallelImpl));

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void ProviderValuesHeldAcrossCalls_AreNotAliasedToEachOther(string name)
        => ConverterCases.Get(name).Invoke(typeof(ValueConverterConcurrentUseTests), nameof(HeldValuesImpl));

    // A converter that returns a shared or reused buffer passes a convert-and-read-back-at-once check (the race window is a few
    // instructions) but fails this one: every provider value is kept until all conversions have finished, then converted back.
    private static void HeldValuesImpl<T>(ConverterCase<T> c) where T : struct
    {
        var converter = c.CreateConverter();
        T[] samples = c.CreateSamples();
        var held = new object?[samples.Length * 50];

        Parallel.For(0, held.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            held[i] = converter.ConvertToProvider(samples[i % samples.Length]));

        for (int i = 0; i < held.Length; i++)
            Assert.AreEqual(samples[i % samples.Length], (T)converter.ConvertFromProvider(held[i])!, $"held value {i}");
    }

    private static void ParallelImpl<T>(ConverterCase<T> c) where T : struct
    {
        var converter = c.CreateConverter();
        T[] samples = c.CreateSamples();
        var failures = new ConcurrentBag<string>();

        Parallel.For(0, 20_000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            T sample = samples[i % samples.Length];
            object? back = converter.ConvertFromProvider(converter.ConvertToProvider(sample));
            if (!Equals(sample, back))
                failures.Add($"iteration {i}");
        });

        Assert.HasCount(0, failures);
    }
}
