using System.Runtime.InteropServices;

namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// Computes the exact encoded size of an ABI argument tuple without writing it (spec 7.5). It runs the very same state machine as
/// <see cref="ABIWriter"/> (one shared implementation), with the same method names, grammar, value checks and budgets, but moves
/// only its cursors: a value sequence the writer accepts yields exactly the length the writer reports, and a sequence the writer
/// rejects (type, arity, range, UTF-8, limits) is rejected here with the same status. The differences are only that there is no
/// destination (so no <see cref="ABIStatus.InsufficientDestination"/> and no overlap check; the byte limit is
/// <see cref="ABIEncodeLimits.MaxEncodedBytes"/>) and that <see cref="TryFinish"/> returns the length instead of a byte count.
/// </summary>
/// <remarks>
/// The state machine, faulting rules, ownership rules and scratch sizing are those of <see cref="ABIWriter"/>: a default measurer
/// answers <see cref="ABIStatus.InvalidWriterState"/>, the first failure faults it, and a completed or faulted measurer answers
/// <see cref="ABIStatus.InvalidWriterState"/>. The caller must supply the same values in the measuring pass and the writing pass;
/// the library cannot detect a change in between (the writer re-validates every value, so a change can only make the write fail or
/// change the length). Bytes sources are required here too, because UTF-8 validity and length decide the size; a length-only
/// overload is deliberately not offered.
/// </remarks>
public ref struct ABIMeasurer
{
    private ABIEncoder _encoder;

    /// <summary>Creates a measurer for <paramref name="schema"/>'s argument tuple; on failure <paramref name="measurer"/> is the invalid default measurer.</summary>
    /// <remarks>Signature note: <paramref name="limits"/> is <c>scoped in</c>, as in <see cref="ABIWriter.TryBegin"/>.</remarks>
    /// <param name="schema">The trusted compiled schema; null is <see cref="ABIStatus.InvalidArgument"/>.</param>
    /// <param name="scratch">Frame storage, at least <c>min(MaxValueDepth, deepest container path of the schema)</c> elements (<see cref="ABIStatus.InsufficientScratch"/>); see <see cref="ABIWriter"/> and <see cref="ABIMeasureFrame"/>.</param>
    /// <param name="limits">Encode budgets; <c>default</c> is invalid. Read once.</param>
    /// <param name="measurer">The open measurer on success; <c>default</c> otherwise.</param>
    /// <param name="status">The failure status, or <see cref="ABIStatus.Success"/>.</param>
    public static bool TryBegin(
        ABISchema schema,
        Span<ABIMeasureFrame> scratch,
        scoped in ABIEncodeLimits limits,
        out ABIMeasurer measurer,
        out ABIStatus status)
    {
        ABIEncodeLimits local = new(limits.MaxEncodedBytes, limits.MaxValueDepth, limits.MaxArrayElements, limits.MaxWrittenValues, limits.MaxWorkUnits);
        measurer = default;
        if (schema is null || !local.IsValid)
        {
            status = ABIStatus.InvalidArgument;
            return false;
        }

        status = ABIEncoder.TryBegin(
            schema, destination: default, guard: default, writes: false, MemoryMarshal.Cast<ABIMeasureFrame, ABIEncodeFrame>(scratch),
            local.MaxEncodedBytes, local.MaxValueDepth, local.MaxArrayElements, local.MaxWrittenValues, local.MaxWorkUnits,
            out measurer._encoder);
        return status == ABIStatus.Success;
    }

    /// <summary>Measures a <c>uint&lt;M&gt;</c> from a 64-bit value (same checks as <see cref="ABIWriter.TryWriteUInt64"/>).</summary>
    public bool TryWriteUInt64(ulong value, out ABIStatus status) => _encoder.WriteUInt64(value, out status);

    /// <summary>Measures an <c>int&lt;M&gt;</c> from a 64-bit value (same checks as <see cref="ABIWriter.TryWriteInt64"/>).</summary>
    public bool TryWriteInt64(long value, out ABIStatus status) => _encoder.WriteInt64(value, out status);

    /// <summary>Measures a <c>uint&lt;M&gt;</c> from a 32-byte word (same checks as <see cref="ABIWriter.TryWriteUInt256Word"/>).</summary>
    public bool TryWriteUInt256Word(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => _encoder.WriteUInt256Word(word32, out status);

    /// <summary>Measures an <c>int&lt;M&gt;</c> from a 32-byte word (same checks as <see cref="ABIWriter.TryWriteInt256Word"/>).</summary>
    public bool TryWriteInt256Word(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => _encoder.WriteInt256Word(word32, out status);

    /// <summary>Measures an <c>address</c> (exactly 20 bytes).</summary>
    public bool TryWriteAddress(scoped ReadOnlySpan<byte> address20, out ABIStatus status) => _encoder.WriteAddress(address20, out status);

    /// <summary>Measures a <c>bool</c>.</summary>
    public bool TryWriteBool(bool value, out ABIStatus status) => _encoder.WriteBool(value, out status);

    /// <summary>Measures a <c>bytes&lt;M&gt;</c> (exactly M bytes).</summary>
    public bool TryWriteFixedBytes(scoped ReadOnlySpan<byte> bytes, out ABIStatus status) => _encoder.WriteFixedBytes(bytes, out status);

    /// <summary>Measures a <c>function</c> (20 address bytes and a selector).</summary>
    public bool TryWriteFunction(scoped ReadOnlySpan<byte> address20, uint selector, out ABIStatus status) => _encoder.WriteFunction(address20, selector, out status);

    /// <summary>Measures a dynamic <c>bytes</c> or <c>string</c>; a <c>string</c> slot is UTF-8 validated as by <see cref="ABIWriter.TryWriteBytes"/>, so the span is required (see the type remarks).</summary>
    public bool TryWriteBytes(scoped ReadOnlySpan<byte> bytes, out ABIStatus status) => _encoder.WriteBytes(bytes, out status);

    /// <summary>Measures a dynamic <c>string</c> after the same strict UTF-8 validation as <see cref="ABIWriter.TryWriteStringUtf8"/>.</summary>
    public bool TryWriteStringUtf8(scoped ReadOnlySpan<byte> utf8Bytes, out ABIStatus status) => _encoder.WriteStringUtf8(utf8Bytes, out status);

    /// <summary>Measures an already scaled <c>fixed&lt;M&gt;x&lt;N&gt;</c> word.</summary>
    public bool TryWriteSignedScaledWord(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => _encoder.WriteSignedScaledWord(word32, out status);

    /// <summary>Measures an already scaled <c>ufixed&lt;M&gt;x&lt;N&gt;</c> word.</summary>
    public bool TryWriteUnsignedScaledWord(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => _encoder.WriteUnsignedScaledWord(word32, out status);

    /// <summary>Opens the next value, which the schema says is a tuple.</summary>
    public bool TryBeginTuple(out ABIStatus status) => _encoder.BeginTuple(out status);

    /// <summary>Opens the next value, which the schema says is a <c>T[k]</c>.</summary>
    public bool TryBeginFixedArray(out ABIStatus status) => _encoder.BeginFixedArray(out status);

    /// <summary>Opens the next value, which the schema says is a <c>T[]</c>, with its element count.</summary>
    public bool TryBeginDynamicArray(int elementCount, out ABIStatus status) => _encoder.BeginDynamicArray(elementCount, out status);

    /// <summary>Closes the innermost open container.</summary>
    public bool TryEndContainer(out ABIStatus status) => _encoder.EndContainer(out status);

    /// <summary>Completes the measurement: <paramref name="encodedLength"/> is the exact canonical size of the argument document (0 on failure).</summary>
    public bool TryFinish(out int encodedLength, out ABIStatus status) => _encoder.Finish(out encodedLength, out status);
}
