using System.Runtime.InteropServices;

namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// Encodes one canonical ABI argument tuple into caller-owned storage, driven by a compiled <see cref="ABISchema"/> (spec 7).
/// Values are supplied in schema declaration order with the typed <c>TryWrite*</c> methods; nested containers are opened with
/// <see cref="TryBeginTuple"/>, <see cref="TryBeginFixedArray"/> or <see cref="TryBeginDynamicArray"/> and closed with
/// <see cref="TryEndContainer"/>; the document is closed with <see cref="TryFinish"/>. The root is always the argument tuple (it is
/// opened by <see cref="TryBegin"/> and closed by <see cref="TryFinish"/>); a bare scalar cannot be the root.
/// </summary>
/// <remarks>
/// <para>
/// Layout: the writer reserves each container's whole head region when the container opens, writes static children into it and
/// a relative offset for each dynamic child, and appends dynamic children at a separate tail cursor, so the byte layout is the
/// canonical head-then-tail form whatever order the values arrive in. Offsets are relative to the container's own head base (the
/// tuple or fixed-array start, or the byte after a dynamic array's count word). Every byte of <c>[0, bytesWritten)</c> is
/// written, padding included, so garbage already in the destination never survives. Bytes beyond <c>bytesWritten</c> are never
/// touched.
/// </para>
/// <para>
/// State machine: <c>default(ABIWriter)</c> and a writer whose <see cref="TryBegin"/> failed are invalid and answer every call with
/// <see cref="ABIStatus.InvalidWriterState"/>. An open writer moves to <c>Faulted</c> on the first failing operation, whatever the
/// reason (type or arity mismatch, range, limit, capacity, scratch, overlap, invalid UTF-8, early finish) and then answers every
/// call with <see cref="ABIStatus.InvalidWriterState"/>; a successful <see cref="TryFinish"/> moves it to <c>Completed</c> with the
/// same effect. A failed operation writes nothing and changes no frame state, but earlier successful operations stay in the
/// destination: after any failure the destination holds no valid document and must be discarded. Publish
/// <c>destination[..bytesWritten]</c> only after <see cref="TryFinish"/> succeeded, and use a private buffer when others can
/// observe the destination meanwhile.
/// </para>
/// <para>
/// Ownership and aliasing: the destination and the scratch frames are leased to the writer exclusively (see
/// <see cref="ABIWriteFrame"/>), and they must not overlap each other. No source span passed to a write may overlap any part of
/// the destination (<see cref="ABIStatus.SourceOverlapsDestination"/>); in-place encoding is not supported. Source spans must stay
/// unchanged during the call; a two-pass use with <see cref="ABIMeasurer"/> assumes the values do not change between the passes
/// (the writer re-validates every value, so a change can only make the write fail or produce a document of a different length).
/// </para>
/// <para>
/// This is a mutable <c>ref struct</c>: use one variable and pass it by <c>ref</c>. All mutable state (position, open-container count,
/// budget counters, fault state) lives in the scratch frames, not in the struct, so a copy of the value shares every bit of it with the
/// original: a copy cannot exceed a budget the original would have enforced or hide a failure from it. A writer whose scratch was reused
/// by a later <see cref="TryBegin"/> is dead (<see cref="ABIStatus.InvalidWriterState"/>). Copying is still pointless and best avoided.
/// </para>
/// <para>
/// Value rules: integer methods apply only to <c>uint&lt;M&gt;</c>/<c>int&lt;M&gt;</c> slots and reject fixed-point slots
/// (<see cref="ABIStatus.TypeMismatch"/>); a value that does not fit the declared width is <see cref="ABIStatus.ValueOutOfRange"/>
/// (the writer never truncates); a source of the wrong length is <see cref="ABIStatus.InvalidArgument"/> (a <c>bytes&lt;M&gt;</c>
/// needs exactly M bytes, an address or function address exactly 20, a word exactly 32). Every <c>string</c> slot takes strictly valid
/// UTF-8 only (<see cref="ABIStatus.InvalidUtf8"/>, before any write), whichever method fills it: <see cref="TryWriteStringUtf8"/> serves
/// only <c>string</c> slots, and <see cref="TryWriteBytes"/> writes any bytes into a <c>bytes</c> slot but validates a <c>string</c> slot
/// the same way. A <c>string</c> read from the chain with non-UTF-8 content therefore decodes but cannot be re-encoded. Sizes are computed
/// in 64 bits and compared by subtraction, so no length can overflow.
/// </para>
/// <para>
/// Scratch sizing: one <see cref="ABIWriteFrame"/> per open container including the root. <see cref="TryBegin"/> returns
/// <see cref="ABIStatus.InsufficientScratch"/> when the scratch has fewer than <c>min(MaxValueDepth, deepest container path of the schema)</c>
/// frames (the root tuple counts 1, every nested tuple or array one more, primitives none), so a scratch that passes can never run short
/// mid-write; <see cref="ABIEncodeLimits.MaxValueDepth"/> frames, and 32 at most, always pass. The scratch is caller-writable memory and is
/// treated as untrusted: corrupted or forged frames are answered with <see cref="ABIStatus.InvalidWriterState"/>, never an exception or a
/// write outside the destination.
/// </para>
/// <para>
/// Limits (<see cref="ABIEncodeLimits"/>) are read once into locals and charged exactly like the decoder's: the same depth rule
/// (root tuple 1), one written value and one work unit per value, one work unit per 32-byte word (scalar, offset, count, length,
/// and the partially used last word of padded data), and <see cref="ABIEncodeLimits.MaxArrayElements"/> for dynamic-array counts
/// only. A document written under limits L therefore decodes under the equal <see cref="ABIDecodeLimits"/>. Exceeding
/// <see cref="ABIEncodeLimits.MaxEncodedBytes"/> is reported as <see cref="ABIStatus.InputTooLarge"/> (checked before the
/// destination size, which reports <see cref="ABIStatus.InsufficientDestination"/>); a limit that is too small for the schema
/// is a caller configuration choice, not a codec defect. More values than a container's arity, fixed length or announced count
/// are <see cref="ABIStatus.ElementLimitExceeded"/>; fewer at <see cref="TryEndContainer"/> or <see cref="TryFinish"/> are
/// <see cref="ABIStatus.IncompleteWrite"/>. Every <c>out ABIStatus</c> is set on every call; no member allocates or throws.
/// </para>
/// </remarks>
public ref struct ABIWriter
{
    private ABIEncoder _encoder;

    /// <summary>
    /// Creates a writer for <paramref name="schema"/>'s argument tuple and reserves the root head. Nothing is written to
    /// <paramref name="destination"/>; on failure <paramref name="writer"/> is the invalid default writer.
    /// </summary>
    /// <remarks>Signature note: <paramref name="limits"/> is <c>scoped in</c> (the specification shows <c>in</c>) so the returned ref struct is not tied to the caller's limits variable; the same mechanical change as in <see cref="ABIData.TryDecodeComplete(ReadOnlySpan{byte}, ABISchema, in ABIDecodeLimits, out ABIData, out ABIStatus)"/>.</remarks>
    /// <param name="schema">The trusted compiled schema; null is <see cref="ABIStatus.InvalidArgument"/>.</param>
    /// <param name="destination">Caller storage for the document; the root head must fit (<see cref="ABIStatus.InsufficientDestination"/>).</param>
    /// <param name="scratch">Frame storage, at least <c>min(MaxValueDepth, deepest container path of the schema)</c> elements (<see cref="ABIStatus.InsufficientScratch"/>); must not overlap <paramref name="destination"/>.</param>
    /// <param name="limits">Encode budgets; <c>default</c> is invalid (<see cref="ABIStatus.InvalidArgument"/>). Read once.</param>
    /// <param name="writer">The open writer on success; <c>default</c> otherwise.</param>
    /// <param name="status">The failure status, or <see cref="ABIStatus.Success"/>.</param>
    public static bool TryBegin(
        ABISchema schema,
        Span<byte> destination,
        Span<ABIWriteFrame> scratch,
        scoped in ABIEncodeLimits limits,
        out ABIWriter writer,
        out ABIStatus status)
    {
        int maxBytes = limits.MaxEncodedBytes;
        int maxDepth = limits.MaxValueDepth;
        int maxArray = limits.MaxArrayElements;
        int maxValues = limits.MaxWrittenValues;
        int maxWork = limits.MaxWorkUnits;
        status = TryBeginCore(schema, destination, destination, scratch, new ABIEncodeLimits(maxBytes, maxDepth, maxArray, maxValues, maxWork), maxBytes, out writer);
        return status == ABIStatus.Success;
    }

    /// <summary>
    /// Shared start used by <see cref="ABICallEncoder"/>: <paramref name="guard"/> is the span sources must not overlap and
    /// <paramref name="argumentMaxBytes"/> the byte budget of the argument document (the call's budget minus the selector).
    /// <paramref name="validated"/> carries the public limits for the validity check.
    /// </summary>
    internal static ABIStatus TryBeginCore(
        ABISchema? schema,
        Span<byte> destination,
        Span<byte> guard,
        Span<ABIWriteFrame> scratch,
        scoped in ABIEncodeLimits validated,
        int argumentMaxBytes,
        out ABIWriter writer)
    {
        writer = default;
        if (schema is null || !validated.IsValid)
        {
            return ABIStatus.InvalidArgument;
        }

        // Only the first MaxValueDepth frames are ever used (a container at depth d sits in frame d-1), so a longer scratch is cut here, BEFORE
        // the byte view below: AsBytes throws OverflowException for a span of more than int.MaxValue / 52 (about 41 million) frames, and a
        // public Try* must never throw. The overlap check therefore covers the frames the writer can touch (design note 65).
        scratch = scratch.Slice(0, Math.Min(scratch.Length, validated.MaxValueDepth));
        Span<ABIEncodeFrame> frames = MemoryMarshal.Cast<ABIWriteFrame, ABIEncodeFrame>(scratch);
        if (MemoryMarshal.AsBytes(scratch).Overlaps(guard))
        {
            return ABIStatus.InvalidArgument; // frames and destination must be distinct memory, or writes would corrupt the state
        }

        return ABIEncoder.TryBegin(
            schema, destination, guard, writes: true, frames,
            argumentMaxBytes, validated.MaxValueDepth, validated.MaxArrayElements, validated.MaxWrittenValues, validated.MaxWorkUnits,
            out writer._encoder);
    }

    /// <summary>Writes a <c>uint&lt;M&gt;</c> from a 64-bit value; <see cref="ABIStatus.ValueOutOfRange"/> when it does not fit M bits. Not for fixed-point slots.</summary>
    public bool TryWriteUInt64(ulong value, out ABIStatus status) => _encoder.WriteUInt64(value, out status);

    /// <summary>Writes an <c>int&lt;M&gt;</c> from a 64-bit value, sign-extended; <see cref="ABIStatus.ValueOutOfRange"/> when it does not fit M bits. Not for fixed-point slots.</summary>
    public bool TryWriteInt64(long value, out ABIStatus status) => _encoder.WriteInt64(value, out status);

    /// <summary>Writes a <c>uint&lt;M&gt;</c> from a 32-byte big-endian word; the bits above M must be zero (<see cref="ABIStatus.ValueOutOfRange"/>), the length exactly 32.</summary>
    public bool TryWriteUInt256Word(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => _encoder.WriteUInt256Word(word32, out status);

    /// <summary>Writes an <c>int&lt;M&gt;</c> from a 32-byte two's-complement word; the bits above M must equal bit M-1 (<see cref="ABIStatus.ValueOutOfRange"/>), the length exactly 32.</summary>
    public bool TryWriteInt256Word(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => _encoder.WriteInt256Word(word32, out status);

    /// <summary>Writes an <c>address</c> from exactly 20 bytes, right-aligned in the word.</summary>
    public bool TryWriteAddress(scoped ReadOnlySpan<byte> address20, out ABIStatus status) => _encoder.WriteAddress(address20, out status);

    /// <summary>Writes a <c>bool</c> as the word 0 or 1.</summary>
    public bool TryWriteBool(bool value, out ABIStatus status) => _encoder.WriteBool(value, out status);

    /// <summary>Writes a <c>bytes&lt;M&gt;</c> from exactly M bytes, left-aligned with zero right padding.</summary>
    public bool TryWriteFixedBytes(scoped ReadOnlySpan<byte> bytes, out ABIStatus status) => _encoder.WriteFixedBytes(bytes, out status);

    /// <summary>Writes a <c>function</c>: 20 address bytes, the big-endian selector, eight zero bytes.</summary>
    public bool TryWriteFunction(scoped ReadOnlySpan<byte> address20, uint selector, out ABIStatus status) => _encoder.WriteFunction(address20, selector, out status);

    /// <summary>Writes a dynamic <c>bytes</c> or <c>string</c> (length word, data, zero padding) from exactly the bytes given. A <c>bytes</c> slot takes any bytes; a <c>string</c> slot takes strictly valid UTF-8 only (<see cref="ABIStatus.InvalidUtf8"/>, before any write), as <see cref="TryWriteStringUtf8"/>.</summary>
    public bool TryWriteBytes(scoped ReadOnlySpan<byte> bytes, out ABIStatus status) => _encoder.WriteBytes(bytes, out status);

    /// <summary>Writes a dynamic <c>string</c> after strict UTF-8 validation (<see cref="ABIStatus.InvalidUtf8"/>, before any write). Serves only <c>string</c> slots (<see cref="ABIStatus.TypeMismatch"/> for <c>bytes</c>).</summary>
    public bool TryWriteStringUtf8(scoped ReadOnlySpan<byte> utf8Bytes, out ABIStatus status) => _encoder.WriteStringUtf8(utf8Bytes, out status);

    /// <summary>Writes an already scaled <c>fixed&lt;M&gt;x&lt;N&gt;</c> word: canonical signed M-bit value, no decimal arithmetic, no rounding.</summary>
    public bool TryWriteSignedScaledWord(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => _encoder.WriteSignedScaledWord(word32, out status);

    /// <summary>Writes an already scaled <c>ufixed&lt;M&gt;x&lt;N&gt;</c> word: canonical unsigned M-bit value, no decimal arithmetic, no rounding.</summary>
    public bool TryWriteUnsignedScaledWord(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => _encoder.WriteUnsignedScaledWord(word32, out status);

    /// <summary>Opens the next value, which the schema says is a tuple.</summary>
    public bool TryBeginTuple(out ABIStatus status) => _encoder.BeginTuple(out status);

    /// <summary>Opens the next value, which the schema says is a <c>T[k]</c>; exactly k elements must follow.</summary>
    public bool TryBeginFixedArray(out ABIStatus status) => _encoder.BeginFixedArray(out status);

    /// <summary>Opens the next value, which the schema says is a <c>T[]</c>, announcing its element count (negative is <see cref="ABIStatus.InvalidArgument"/>, above <see cref="ABIEncodeLimits.MaxArrayElements"/> is <see cref="ABIStatus.ElementLimitExceeded"/>).</summary>
    public bool TryBeginDynamicArray(int elementCount, out ABIStatus status) => _encoder.BeginDynamicArray(elementCount, out status);

    /// <summary>Closes the innermost open container after exactly its declared number of children (otherwise <see cref="ABIStatus.IncompleteWrite"/>). The root is closed by <see cref="TryFinish"/>.</summary>
    public bool TryEndContainer(out ABIStatus status) => _encoder.EndContainer(out status);

    /// <summary>
    /// Completes the document: every root argument written and no container open. <paramref name="bytesWritten"/> is the exact
    /// canonical length (0 for a zero-argument root), never the destination size, and is 0 on failure.
    /// </summary>
    public bool TryFinish(out int bytesWritten, out ABIStatus status) => _encoder.Finish(out bytesWritten, out status);
}
