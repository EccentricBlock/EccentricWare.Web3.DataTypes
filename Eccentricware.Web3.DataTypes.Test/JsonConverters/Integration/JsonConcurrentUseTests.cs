using System.Collections;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// One converter instance is shared by every serializer call in the process. Many threads reading escaped and padded
/// tokens at once (stack buffer and pooled buffer sizes) must each get their own value, and nothing may throw.
/// </summary>
[TestClass]
public sealed class JsonConcurrentUseTests
{
    private const int Workers = 16;
    private const int IterationsPerWorker = 150;

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void EscapedAndPaddedTokens_ReadCorrectly_FromManyThreads(JsonConverterCase c)
    {
        int upperRaw = c.TokenCap > 0 ? c.TokenCap : 2000;

        Parallel.For(0, Workers, new ParallelOptions { MaxDegreeOfParallelism = Workers }, worker =>
        {
            var random = new Random(1000 + worker);
            for (int i = 0; i < IterationsPerWorker; i++)
            {
                // Raw lengths from just below the 256-byte stack threshold up to the converter's cap (or 2,000).
                int rawLength = random.Next(200, upperRaw + 1);
                byte[] value = JsonTokenBodies.Value(JsonTokenBodies.EscapedPaddedToRawLength(c.Canonical, rawLength));
                byte[] key = JsonTokenBodies.Key(JsonTokenBodies.EscapedPaddedToRawLength(c.Canonical, rawLength));

                Assert.AreEqual(c.Expected, c.Read(value), $"worker {worker} value raw={rawLength}");
                Assert.AreEqual(7, c.ReadDictionary(key)[c.Expected], $"worker {worker} key raw={rawLength}");
                Assert.AreEqual(c.Expected, SegmentedUtf8Json.Deserialize(c.Type, value, 97), $"worker {worker} split raw={rawLength}");
            }
        });
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void FailuresOnTheSlowPath_DoNotLeakStateBetweenThreads(JsonConverterCase c)
    {
        // Half the threads read valid padded tokens while the others hit the failure paths (lone surrogate, bad text).
        Parallel.For(0, Workers, new ParallelOptions { MaxDegreeOfParallelism = Workers }, worker =>
        {
            for (int i = 0; i < IterationsPerWorker; i++)
            {
                if (worker % 2 == 0)
                {
                    byte[] good = JsonTokenBodies.Value(JsonTokenBodies.EscapedPaddedToRawLength(c.Canonical, 300));
                    Assert.AreEqual(c.Expected, c.Read(good));
                }
                else
                {
                    byte[] bad = JsonTokenBodies.Value("\\ud800" + new string(' ', 300));
                    Assert.AreEqual($"Invalid value for {c.Name}.", Assert.ThrowsExactly<JsonException>(() => c.Read(bad)).Message);
                }
            }
        });
    }
}
