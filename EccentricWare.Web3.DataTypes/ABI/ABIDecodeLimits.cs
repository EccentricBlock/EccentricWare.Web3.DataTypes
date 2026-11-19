namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// Resource budgets for strict decoding. Limits are application policy, not part of the ABI wire format.
/// <c>default(ABIDecodeLimits)</c> is all zeros and is invalid; start from <see cref="Default"/>.
/// </summary>
/// <param name="MaxEncodedBytes">Maximum input length in bytes (for call data, including the four selector bytes); must be positive.</param>
/// <param name="MaxValueDepth">Maximum nesting depth; the top-level arguments tuple is depth 1 (1 to <see cref="HardMaxValueDepth"/>).</param>
/// <param name="MaxArrayElements">Maximum count of any one dynamic array (the attacker-controlled count word; fixed-array lengths come from the trusted schema); must be positive.</param>
/// <param name="MaxVisitedValues">Maximum number of values entered in one document, including zero-size values (must be positive). Zero-width values (empty tuples) consume no input bytes, so this and <paramref name="MaxWorkUnits"/> are the only bounds on them: with both near <see cref="int.MaxValue"/> a 64-byte input can cost seconds of CPU, so keep them proportionate to <paramref name="MaxEncodedBytes"/>.</param>
/// <param name="MaxWorkUnits">Maximum work units: one per value entered and one per 32-byte word examined (must be positive).</param>
public readonly record struct ABIDecodeLimits(
    int MaxEncodedBytes,
    int MaxValueDepth,
    int MaxArrayElements,
    int MaxVisitedValues,
    int MaxWorkUnits)
{
    /// <summary>The absolute depth cap (the validator's fixed frame stack size); a configured depth above it is invalid.</summary>
    public const int HardMaxValueDepth = 32;

    /// <summary>Starting points from the specification (6.4): 256 KiB, depth 16, 4,096 elements, 16,384 values, 100,000 work units.</summary>
    public static ABIDecodeLimits Default => new(262_144, 16, 4_096, 16_384, 100_000);

    /// <summary>True when every budget is within its documented range.</summary>
    public bool IsValid =>
        MaxEncodedBytes > 0
        && unchecked((uint)(MaxValueDepth - 1)) < HardMaxValueDepth
        && MaxArrayElements > 0
        && MaxVisitedValues > 0
        && MaxWorkUnits > 0;
}
