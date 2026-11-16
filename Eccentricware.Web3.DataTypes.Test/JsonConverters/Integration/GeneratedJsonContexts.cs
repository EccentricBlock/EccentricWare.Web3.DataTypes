using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>A model with a plain, a nullable and a list property, for the source-generated context tests.</summary>
internal sealed class GeneratedHolder
{
    public Address Addr { get; set; }

    public Address? Opt { get; set; }

    public List<Hash32> List { get; set; } = [];
}

/// <summary>Source-generated metadata for every value type, as a root value and as a dictionary key.</summary>
[JsonSerializable(typeof(Address))]
[JsonSerializable(typeof(FunctionSelector))]
[JsonSerializable(typeof(InstructionDiscriminator))]
[JsonSerializable(typeof(Hash32))]
[JsonSerializable(typeof(Signature))]
[JsonSerializable(typeof(uint256))]
[JsonSerializable(typeof(int256))]
[JsonSerializable(typeof(HexBigInteger))]
[JsonSerializable(typeof(HexBytes))]
[JsonSerializable(typeof(Dictionary<Address, int>))]
[JsonSerializable(typeof(Dictionary<FunctionSelector, int>))]
[JsonSerializable(typeof(Dictionary<InstructionDiscriminator, int>))]
[JsonSerializable(typeof(Dictionary<Hash32, int>))]
[JsonSerializable(typeof(Dictionary<Signature, int>))]
[JsonSerializable(typeof(Dictionary<uint256, int>))]
[JsonSerializable(typeof(Dictionary<int256, int>))]
[JsonSerializable(typeof(Dictionary<HexBigInteger, int>))]
[JsonSerializable(typeof(Dictionary<HexBytes, int>))]
[JsonSerializable(typeof(GeneratedHolder))]
internal sealed partial class GeneratedConvertersJsonContext : JsonSerializerContext;

/// <summary>A second context with generation-time options: indented output and camelCase property names.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GeneratedHolder))]
internal sealed partial class GeneratedIndentedCamelCaseJsonContext : JsonSerializerContext;
