using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class FunctionSelectorJsonTests
{
    [TestMethod]
    public void JsonSerializer_RoundTrips_AndRejectsInvalidInput()
    {
        var selector = FunctionSelector.Erc20.Transfer;

        string json = JsonSerializer.Serialize(selector);
        Assert.AreEqual("\"0xa9059cbb\"", json);
        Assert.AreEqual(selector, JsonSerializer.Deserialize<FunctionSelector>(json));
        Assert.AreEqual(selector, JsonSerializer.Deserialize<FunctionSelector>("\"A9059CBB\""));

        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<FunctionSelector>("\"0xa9059cb\""));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<FunctionSelector>("\"0xa9059cbbb\""));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<FunctionSelector>("\"0xa9059cbg\""));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<FunctionSelector>("12345"));
    }
}
