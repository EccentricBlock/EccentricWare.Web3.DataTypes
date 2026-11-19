namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// One element of the caller-supplied scratch span of an <see cref="ABIWriter"/> or <see cref="ABICallEncoder"/>: an opaque,
/// unmanaged value type (52 bytes, no references) holding the state of one open container. Its fields are implementation state
/// and are not meaningful to callers.
/// </summary>
/// <remarks>
/// Ownership: the scratch span is leased exclusively to the writer from <c>TryBegin</c> until the writer is dropped; the caller must
/// not read, modify, reuse or share it meanwhile (a second writer over the same frames would corrupt both). <c>TryBegin</c>
/// initialises the frames it can use (the first <see cref="ABIEncodeLimits.MaxValueDepth"/> at most), so stale content is never
/// visible and a caller cannot forge an in-progress frame. All of the writer's mutable state (position, open-container count and
/// budget counters included) is kept in these frames, so a struct copy of a writer shares it with the original rather than diverging, and a writer value whose scratch was reused by a later
/// <c>TryBegin</c> becomes invalid (every call answers <see cref="ABIStatus.InvalidWriterState"/>) instead of acting on the new writer.
/// Size: one frame per open container including the root; <c>TryBegin</c> checks the length (<see cref="ABIWriter"/> states the rule), and
/// <see cref="ABIEncodeLimits.MaxValueDepth"/> frames, 32 at most, always suffice. The content is untrusted by the writer: forged frames get
/// <see cref="ABIStatus.InvalidWriterState"/>, never an exception.
/// </remarks>
public struct ABIWriteFrame
{
    internal ABIEncodeFrame Frame;
}
