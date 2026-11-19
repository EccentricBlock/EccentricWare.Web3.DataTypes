namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// An immutable function binding: name, canonical signature, the selector derived from that signature with Keccak-256, and the
/// compiled input schema. The selector is never supplied by a caller, so it cannot disagree with the signature. A selector is
/// only a candidate identifier: four-byte collisions exist, and binding a function to a chain, target and verified code is the
/// job of a trusted registry outside this library.
/// </summary>
public sealed class ABIFunctionSchema
{
    internal ABIFunctionSchema(string name, string canonicalSignature, FunctionSelector selector, ABISchema inputSchema)
    {
        Name = name;
        CanonicalSignature = canonicalSignature;
        FunctionSelector = selector;
        InputSchema = inputSchema;
    }

    /// <summary>The function name, for example <c>transfer</c>.</summary>
    public string Name { get; }

    /// <summary>The canonical signature that is hashed, for example <c>transfer(address,uint256)</c>.</summary>
    public string CanonicalSignature { get; }

    /// <summary>The four-byte selector as a big-endian number (<c>0xa9059cbb</c> for transfer).</summary>
    public uint Selector => FunctionSelector.Value;

    /// <summary>The same selector as this library's <see cref="EccentricWare.Web3.DataTypes.FunctionSelector"/> value type, derived from <see cref="CanonicalSignature"/> by <see cref="EccentricWare.Web3.DataTypes.FunctionSelector.FromSignature(string)"/>.</summary>
    public FunctionSelector FunctionSelector { get; }

    /// <summary>The compiled argument tuple.</summary>
    public ABISchema InputSchema { get; }

    /// <summary>Returns <see cref="CanonicalSignature"/>.</summary>
    public override string ToString() => CanonicalSignature;
}
