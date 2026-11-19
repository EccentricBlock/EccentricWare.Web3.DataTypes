namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// Resource budgets for encoding and measuring, independent of the decoder limits. <c>default(ABIEncodeLimits)</c> is all
/// zeros and is invalid; start from <see cref="Default"/>.
/// </summary>
/// <param name="MaxEncodedBytes">Maximum encoded document length in bytes (for a call, including the four selector bytes); must be positive.</param>
/// <param name="MaxValueDepth">Maximum nesting depth; the top-level arguments tuple is depth 1 (1 to <see cref="HardMaxValueDepth"/>).</param>
/// <param name="MaxArrayElements">Maximum element count of any one dynamic array (the count announced to <c>TryBeginDynamicArray</c>); fixed-array lengths come from the trusted schema and are not limited by it, as in <see cref="ABIDecodeLimits"/> (must be positive).</param>
/// <param name="MaxWrittenValues">Maximum number of values written in one document (must be positive).</param>
/// <param name="MaxWorkUnits">Maximum work units (must be positive).</param>
public readonly record struct ABIEncodeLimits(
    int MaxEncodedBytes,
    int MaxValueDepth,
    int MaxArrayElements,
    int MaxWrittenValues,
    int MaxWorkUnits)
{
    /// <summary>The absolute depth cap (matches the decoder's frame stack); a configured depth above it is invalid.</summary>
    public const int HardMaxValueDepth = 32;

    /// <summary>The same starting points as <see cref="ABIDecodeLimits.Default"/>, so a signing pipeline encodes and decodes under matching budgets.</summary>
    public static ABIEncodeLimits Default => new(262_144, 16, 4_096, 16_384, 100_000);

    /// <summary>True when every budget is within its documented range.</summary>
    public bool IsValid =>
        MaxEncodedBytes > 0
        && unchecked((uint)(MaxValueDepth - 1)) < HardMaxValueDepth
        && MaxArrayElements > 0
        && MaxWrittenValues > 0
        && MaxWorkUnits > 0;
}
