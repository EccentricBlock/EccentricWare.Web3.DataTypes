using System.Collections;
using System.Text;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// The converters through <c>DeserializeAsync</c> / <c>SerializeAsync</c> with the smallest buffers and a stream that
/// returns one byte per read. (System.Text.Json buffers a whole token before calling a converter, so these exercise
/// the contiguous branch with unusual reader state; split tokens are covered by the segmented tests.)
/// </summary>
[TestClass]
public sealed class JsonAsyncStreamTests
{
    private static readonly JsonSerializerOptions TinyBuffer = new() { DefaultBufferSize = 1 };

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static Stream Open(byte[] json, bool oneByteReads) => oneByteReads ? new OneByteAtATimeStream(json) : new MemoryStream(json);

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public async Task Values_ReadFromTinyStreams_ForEverySpelling(JsonConverterCase c)
    {
        foreach (bool oneByteReads in new[] { false, true })
        {
            foreach ((string text, object expected) in c.AllTexts())
            {
                foreach (string body in new[] { text, JsonTokenBodies.EscapeFirst(text) })
                {
                    await using Stream stream = Open(JsonTokenBodies.Value(body), oneByteReads);

                    object? value = await JsonSerializer.DeserializeAsync(stream, c.Type, TinyBuffer);

                    Assert.AreEqual(expected, value, $"'{body}' oneByteReads={oneByteReads}");
                }
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public async Task DictionaryKeysAndProperties_ReadFromTinyStreams(JsonConverterCase c)
    {
        Type stringMap = typeof(Dictionary<,>).MakeGenericType(typeof(string), c.Type);

        foreach (bool oneByteReads in new[] { false, true })
        {
            foreach ((string text, object expected) in c.AllTexts())
            {
                await using (Stream keyStream = Open(JsonTokenBodies.Key(JsonTokenBodies.EscapeFirst(text)), oneByteReads))
                {
                    var map = (IDictionary)(await JsonSerializer.DeserializeAsync(keyStream, c.DictionaryType, TinyBuffer))!;
                    Assert.AreEqual(7, map[expected], $"key '{text}' oneByteReads={oneByteReads}");
                }

                await using Stream valueStream = Open(Utf8("{\"k\":\"" + text + "\"}"), oneByteReads);
                var byName = (IDictionary)(await JsonSerializer.DeserializeAsync(valueStream, stringMap, TinyBuffer))!;
                Assert.AreEqual(expected, byName["k"], $"property '{text}' oneByteReads={oneByteReads}");
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public async Task InvalidValue_FailsWithTheConverterMessage_AndThePath(JsonConverterCase c)
    {
        await using Stream stream = new OneByteAtATimeStream(Utf8("[\"" + c.Canonical + "\",\"0xzz\"]"));

        JsonException error = await Assert.ThrowsExactlyAsync<JsonException>(
            async () => await JsonSerializer.DeserializeAsync(stream, typeof(List<>).MakeGenericType(c.Type), TinyBuffer));

        Assert.AreEqual("$[1]", error.Path);
        StringAssert.StartsWith(error.Message, $"Invalid value for {c.Name}.");
    }

    [TestMethod]
    [DynamicData(nameof(JsonConverterCases.All), typeof(JsonConverterCases))]
    public async Task SerializeAsync_WithAOneByteBuffer_WritesTheCanonicalText(JsonConverterCase c)
    {
        using var stream = new MemoryStream();
        var map = (IDictionary)Activator.CreateInstance(c.DictionaryType)!;
        map.Add(c.Expected, 1);

        await JsonSerializer.SerializeAsync(stream, map, c.DictionaryType, TinyBuffer);

        Assert.AreEqual($"{{\"{c.Canonical}\":1}}", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [TestMethod]
    public async Task DeserializeAsyncEnumerable_StreamsElements_OfValueTypes()
    {
        await using Stream selectors = new OneByteAtATimeStream(Utf8("[\"0xa9059cbb\",\"0x00000001\"]"));
        await using Stream addresses = new OneByteAtATimeStream(Utf8($"[\"{JsonConverterCases.SolanaTokenProgramBase58}\",\"0x{new string('a', 40)}\"]"));

        var selectorList = new List<FunctionSelector>();
        await foreach (FunctionSelector item in JsonSerializer.DeserializeAsyncEnumerable<FunctionSelector>(selectors, TinyBuffer))
            selectorList.Add(item);
        var addressList = new List<Address>();
        await foreach (Address item in JsonSerializer.DeserializeAsyncEnumerable<Address>(addresses, TinyBuffer))
            addressList.Add(item);

        CollectionAssert.AreEqual(new[] { new FunctionSelector(0xA9059CBBu), new FunctionSelector(1u) }, selectorList);
        Assert.HasCount(2, addressList);
        Assert.AreEqual(AddressType.Solana, addressList[0].Type);
        Assert.AreEqual(AddressType.Evm, addressList[1].Type);
    }

    [TestMethod]
    public async Task HexBytes_LargeKeyAndValue_RoundTripThroughATinyBuffer()
    {
        HexBytes key = HexBytesJsonTestKit.Pattern(MaxKeyBytes);
        HexBytes value = HexBytesJsonTestKit.Pattern(70_000);
        using var stream = new MemoryStream();

        await JsonSerializer.SerializeAsync(stream, new Dictionary<HexBytes, HexBytes> { [key] = value }, TinyBuffer);

        string json = Encoding.UTF8.GetString(stream.ToArray());
        Assert.AreEqual("{\"" + HexBytesJsonTestKit.PatternText(MaxKeyBytes) + "\":\"" + HexBytesJsonTestKit.PatternText(70_000) + "\"}", json);

        await using Stream read = new OneByteAtATimeStream(stream.ToArray());
        var back = (await JsonSerializer.DeserializeAsync<Dictionary<HexBytes, HexBytes>>(read, TinyBuffer))!;
        Assert.AreEqual(value, back[key]);
    }

    private const int MaxKeyBytes = 4096; // the largest HexBytes dictionary key
}
