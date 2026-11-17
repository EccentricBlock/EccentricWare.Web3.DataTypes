namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>
/// A location inside one decoded RLP document: <see cref="Offset"/> is the zero-based byte offset from the first byte of
/// the originally decoded document, and <see cref="Length"/> is a byte count (for an encoded range, the whole item including
/// its prefix and length bytes). It is a location descriptor, not a certificate that a child boundary was validated.
/// </summary>
/// <param name="Offset">Zero-based offset from the first byte of the originally decoded document.</param>
/// <param name="Length">Length in bytes.</param>
public readonly record struct RLPRange(int Offset, int Length);
