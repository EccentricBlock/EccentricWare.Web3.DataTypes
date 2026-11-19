namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// Function-name rules shared by the schema compiler. The selector itself is derived by <see cref="FunctionSelector.FromSignature(string)"/>
/// (the first four bytes of Ethereum Keccak-256, not NIST SHA3-256, of the canonical ASCII signature, read big-endian): one implementation,
/// in the datatype that owns the number. Internal on purpose: the only public way to obtain a selector is through an
/// <see cref="ABIFunctionSchema"/>, whose selector is always derived from its own canonical signature.
/// </summary>
internal static class ABISelector
{
    /// <summary>True when <paramref name="name"/> is a Solidity identifier: <c>[A-Za-z_$][A-Za-z0-9_$]*</c> (ASCII only, so the signature is hashed unambiguously).</summary>
    internal static bool IsValidFunctionName(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (!(char.IsAsciiLetter(c) || c == '_' || c == '$' || (i > 0 && char.IsAsciiDigit(c))))
            {
                return false;
            }
        }

        return true;
    }
}
