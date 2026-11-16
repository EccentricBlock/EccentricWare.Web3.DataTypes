namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class InstructionDiscriminatorJsonTests
{
    [TestMethod]
    public void JsonSerializer_RoundTrips_AndRejectsInvalidInput()
    {
        var value = new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL);

        string json = System.Text.Json.JsonSerializer.Serialize(value);
        Assert.AreEqual("\"0xafaf6d1f0d989bed\"", json);
        Assert.AreEqual(value, System.Text.Json.JsonSerializer.Deserialize<InstructionDiscriminator>(json));
        Assert.AreEqual(value, System.Text.Json.JsonSerializer.Deserialize<InstructionDiscriminator>("\"AFAF6D1F0D989BED\""));

        Assert.ThrowsExactly<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<InstructionDiscriminator>("\"0xafaf6d1f0d989be\""));
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<InstructionDiscriminator>("\"0xafaf6d1f0d989beg\""));
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<InstructionDiscriminator>("1"));
    }
}
