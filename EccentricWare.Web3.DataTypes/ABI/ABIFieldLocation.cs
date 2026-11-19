namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// Where one field of a validated ABI document lives, in root-document coordinates. A location is a descriptor, not a
/// certificate: only schema-driven accessors on a validated view may produce one that policy code relies on.
/// </summary>
/// <param name="HeadRange">
/// The field's slot in its parent's head: the 32-byte offset word of a dynamic field, or the complete inline encoding of a
/// static field.
/// </param>
/// <param name="ValueRange">
/// The field's whole encoding: for a static field the same bytes as the head, for a dynamic field the complete tail
/// (<c>[length or count word][data][padding]</c>), excluding the parent's offset word.
/// </param>
/// <param name="Kind">The ABI kind of the field.</param>
public readonly record struct ABIFieldLocation(ABIRange HeadRange, ABIRange ValueRange, ABITypeKind Kind);
