using System.Collections;
using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// A value must read the same however its token arrives: plain, escaped, padded, split across buffer segments at any
/// chunk size (with or without empty segments), as a value or as a dictionary key.
/// </summary>
[TestClass]
public sealed class TokenShapePolicyTests
{
    private static readonly int[] ChunkSizes = [1, 2, 3, 5, 7, 64];

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void Values_ReadTheSame_ForEveryVariantAndChunking(JsonConverterCase c)
    {
        foreach ((string text, object expected) in c.AllTexts())
        {
            foreach (TokenVariant variant in JsonTokenBodies.AllVariants)
            {
                byte[] json = JsonTokenBodies.Value(JsonTokenBodies.Apply(text, variant));
                Assert.AreEqual(expected, c.Read(json), $"contiguous {variant} '{text}'");

                foreach (int chunk in ChunkSizes)
                {
                    foreach (bool empty in new[] { false, true })
                        Assert.AreEqual(expected, SegmentedUtf8Json.Deserialize(c.Type, json, chunk, empty), $"{variant} '{text}' chunk={chunk} empty={empty}");
                }
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void DictionaryKeys_ReadTheSame_ForEveryVariantAndChunking(JsonConverterCase c)
    {
        foreach ((string text, object expected) in c.AllTexts())
        {
            foreach (TokenVariant variant in JsonTokenBodies.AllVariants)
            {
                byte[] json = JsonTokenBodies.Key(JsonTokenBodies.Apply(text, variant));
                Assert.AreEqual(7, c.ReadDictionary(json)[expected], $"contiguous {variant} '{text}'");

                foreach (int chunk in ChunkSizes)
                {
                    foreach (bool empty in new[] { false, true })
                    {
                        var map = (IDictionary)SegmentedUtf8Json.Deserialize(c.DictionaryType, json, chunk, empty)!;
                        Assert.AreEqual(7, map[expected], $"{variant} '{text}' chunk={chunk} empty={empty}");
                    }
                }
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void EverySpelling_CutIntoTwoSegmentsAtEveryBoundary_Reads(JsonConverterCase c)
    {
        foreach ((string text, object expected) in c.AllTexts())
        {
            foreach (TokenVariant variant in new[] { TokenVariant.Plain, TokenVariant.EscapedFirst })
            {
                byte[] json = JsonTokenBodies.Value(JsonTokenBodies.Apply(text, variant));
                for (int split = 1; split < json.Length; split++)
                {
                    Utf8JsonReader reader = SegmentedUtf8Json.Reader(SegmentedUtf8Json.SplitAt(json, split));
                    Assert.AreEqual(expected, JsonSerializer.Deserialize(ref reader, c.Type), $"{variant} '{text}' split={split}");
                }
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public void NumberTokens_ReadTheSame_ContiguousOrSplit(JsonConverterCase c)
    {
        foreach ((string text, object expected) in c.NumberTokens)
        {
            byte[] json = Encoding.ASCII.GetBytes(text);
            Assert.AreEqual(expected, c.Read(json), $"contiguous {text}");

            foreach (int chunk in new[] { 1, 2, 3, 7, 40 })
            {
                foreach (bool empty in new[] { false, true })
                    Assert.AreEqual(expected, SegmentedUtf8Json.Deserialize(c.Type, json, chunk, empty), $"{text} chunk={chunk} empty={empty}");
            }
        }
    }
}
