namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// One element of the caller-supplied scratch span of an <see cref="ABIMeasurer"/>: an opaque, unmanaged value type (52 bytes, no
/// references) holding the state of one open container. Same ownership rules as <see cref="ABIWriteFrame"/>: the span is leased
/// exclusively to the measurer, never read or modified by the caller meanwhile, and its length is checked by
/// <c>TryBegin</c> (<see cref="ABIWriter"/> states the rule; <see cref="ABIEncodeLimits.MaxValueDepth"/> frames always suffice).
/// </summary>
public struct ABIMeasureFrame
{
    internal ABIEncodeFrame Frame;
}
