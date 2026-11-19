namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// A compact eight-byte coordinate inside one ABI document: <see cref="Offset"/> is the zero-based byte offset from the first
/// byte of the root arguments document and <see cref="Length"/> is a byte count (zero is legal, for example for an empty
/// nested tuple). It is a location descriptor, not a validation token: it holds no pointer and proves nothing about the bytes
/// it names. Never serialise the native struct memory as a wire format.
/// </summary>
/// <param name="Offset">Zero-based offset from the first byte of the root arguments document.</param>
/// <param name="Length">Length in bytes.</param>
public readonly record struct ABIRange(int Offset, int Length);
