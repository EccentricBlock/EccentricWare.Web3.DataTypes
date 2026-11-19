namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// Resource budgets for schema compilation (bootstrap time, never per request). <c>default(ABICompileLimits)</c> is all
/// zeros and is invalid; start from <see cref="Default"/>.
/// </summary>
/// <param name="MaxSchemaNodes">Maximum compiled nodes, counting every type occurrence (must be positive).</param>
/// <param name="MaxSchemaDepth">Maximum depth; the root arguments tuple is depth 1 and every nested type or primitive adds one (1 to <see cref="HardMaxSchemaDepth"/>).</param>
/// <param name="MaxFixedArrayElements">Maximum <c>k</c> of any <c>T[k]</c> (must be positive).</param>
/// <param name="MaxTupleElements">Maximum arity of any one tuple, including the root (must be positive).</param>
/// <param name="MaxStaticEncodedBytes">
/// Maximum head-region size of any tuple or fixed array, which for a static one is its whole encoded size (must be positive).
/// </param>
public readonly record struct ABICompileLimits(
    int MaxSchemaNodes,
    int MaxSchemaDepth,
    int MaxFixedArrayElements,
    int MaxTupleElements,
    int MaxStaticEncodedBytes)
{
    /// <summary>
    /// The absolute depth cap. It equals the decoder's frame stack size, so a deeper schema could never be decoded anyway;
    /// it also bounds the compiler's own recursion.
    /// </summary>
    public const int HardMaxSchemaDepth = 32;

    /// <summary>
    /// Specification starting points (6.4) for nodes and depth (4,096 and 32); the specification gives no numbers for the
    /// rest: 4,096 fixed-array elements (the decoder's array default), 1,024 tuple members, 256 KiB static footprint
    /// (the decoder's byte default).
    /// </summary>
    public static ABICompileLimits Default => new(4_096, 32, 4_096, 1_024, 262_144);

    /// <summary>True when every budget is within its documented range.</summary>
    public bool IsValid =>
        MaxSchemaNodes > 0
        && unchecked((uint)(MaxSchemaDepth - 1)) < HardMaxSchemaDepth
        && MaxFixedArrayElements > 0
        && MaxTupleElements > 0
        && MaxStaticEncodedBytes > 0;
}
