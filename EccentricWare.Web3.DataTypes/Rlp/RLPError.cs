namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>
/// Diagnostic detail for a failed decode. <see cref="Offset"/> is relative to the original supplied input (it may equal
/// <c>input.Length</c> when missing bytes would start there) and is <c>-1</c> for failures caused only by invalid
/// configuration or arguments.
/// </summary>
/// <param name="Status">The failure status (<see cref="RLPStatus.Success"/> when there was none).</param>
/// <param name="Offset">Offset of the offending byte in the original input, or -1.</param>
public readonly record struct RLPError(RLPStatus Status, int Offset);
