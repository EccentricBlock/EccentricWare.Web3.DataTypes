namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// Lightweight diagnostic detail for a failed decode or write. It never carries calldata or any other payload byte.
/// </summary>
/// <param name="Status">The failure status (<see cref="ABIStatus.Success"/> when there was none).</param>
/// <param name="DocumentOffset">Offset in the root arguments document where the failure was detected, or -1 when none applies.</param>
/// <param name="SchemaNode">Compiled schema node being processed, or -1 when none applies.</param>
public readonly record struct ABIError(ABIStatus Status, int DocumentOffset, int SchemaNode);
