namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>
/// Resource budgets for strict decoding. Limits are application policy, not part of the RLP wire format.
/// <c>default(RLPDecodeLimits)</c> is all zeros and is rejected with <see cref="RLPStatus.InvalidArgument"/>;
/// start from <see cref="Default"/>.
/// </summary>
/// <param name="MaxEncodedBytes">Maximum encoded document length in bytes (must be positive).</param>
/// <param name="MaxListDepth">Maximum list nesting depth; a root list has depth 1, a root string depth 0 (1 to <see cref="HardMaxListDepth"/>).</param>
/// <param name="MaxItems">Maximum number of items, counting the root, every nested list and every string (must be positive).</param>
public readonly record struct RLPDecodeLimits(int MaxEncodedBytes, int MaxListDepth, int MaxItems)
{
    /// <summary>The absolute list-depth cap; a configured <see cref="MaxListDepth"/> above it is invalid.</summary>
    public const int HardMaxListDepth = 64;

    /// <summary>Conservative defaults: 1 MiB, depth 32, 8,192 items.</summary>
    public static RLPDecodeLimits Default => new(1024 * 1024, 32, 8192);

    /// <summary>True when every budget is within its documented range.</summary>
    public bool IsValid => MaxEncodedBytes > 0 && unchecked((uint)(MaxListDepth - 1)) < HardMaxListDepth && MaxItems > 0;
}
