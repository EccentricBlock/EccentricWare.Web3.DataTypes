using System.Buffers.Binary;

namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// Encodes one function call: the four selector bytes of an <see cref="ABIFunctionSchema"/> followed by its argument tuple
/// (spec 7.5). A thin adapter over one <see cref="ABIWriter"/> that writes the arguments to <c>destination[4..]</c>; the value and
/// container methods are the same as the writer's, and ABI offsets never include the selector bytes. The selector comes only from
/// the schema (derived from its canonical signature), never from a caller-supplied number.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ABIEncodeLimits.MaxEncodedBytes"/> bounds the whole call including the selector: it must be at least 4 (otherwise
/// <see cref="ABIStatus.InputTooLarge"/>) and the argument writer gets <c>MaxEncodedBytes - 4</c>. The destination must hold the
/// four selector bytes (<see cref="ABIStatus.InsufficientDestination"/>); sources must not overlap any part of the destination,
/// the selector area included. <see cref="TryBegin"/> writes the selector only after every check passed, and writes nothing on
/// failure. Any later failure invalidates the whole buffer, selector included (a selector in the buffer is not a valid call).
/// A selector-only call (zero arguments) is complete after <see cref="TryFinish"/> with no value written: 4 bytes.
/// </para>
/// <para>State machine, faulting, scratch sizing and ownership are those of <see cref="ABIWriter"/>; this is a mutable <c>ref struct</c> that must not be copied once in use.</para>
/// </remarks>
public ref struct ABICallEncoder
{
    private const int SelectorSize = 4;

    private ABIWriter _writer;

    /// <summary>Creates a call encoder and writes the four selector bytes; on failure <paramref name="encoder"/> is the invalid default encoder and the destination is untouched.</summary>
    /// <remarks>Signature note: <paramref name="limits"/> is <c>scoped in</c>, as in <see cref="ABIWriter.TryBegin"/>.</remarks>
    /// <param name="function">The trusted function schema; null is <see cref="ABIStatus.InvalidArgument"/>.</param>
    /// <param name="destination">Caller storage for the whole call (selector plus arguments).</param>
    /// <param name="scratch">Frame storage, at least <c>min(MaxValueDepth, deepest container path of the schema)</c> elements (<see cref="ABIStatus.InsufficientScratch"/>); see <see cref="ABIWriter"/> and <see cref="ABIWriteFrame"/>.</param>
    /// <param name="limits">Encode budgets for the whole call; <c>default</c> is invalid. Read once.</param>
    /// <param name="encoder">The open encoder on success; <c>default</c> otherwise.</param>
    /// <param name="status">The failure status, or <see cref="ABIStatus.Success"/>.</param>
    public static bool TryBegin(
        ABIFunctionSchema function,
        Span<byte> destination,
        Span<ABIWriteFrame> scratch,
        scoped in ABIEncodeLimits limits,
        out ABICallEncoder encoder,
        out ABIStatus status)
    {
        ABIEncodeLimits local = new(limits.MaxEncodedBytes, limits.MaxValueDepth, limits.MaxArrayElements, limits.MaxWrittenValues, limits.MaxWorkUnits);
        encoder = default;
        if (function is null || !local.IsValid)
        {
            status = ABIStatus.InvalidArgument;
            return false;
        }

        if (local.MaxEncodedBytes < SelectorSize)
        {
            status = ABIStatus.InputTooLarge;
            return false;
        }

        if (destination.Length < SelectorSize)
        {
            status = ABIStatus.InsufficientDestination;
            return false;
        }

        status = ABIWriter.TryBeginCore(
            function.InputSchema, destination.Slice(SelectorSize), destination, scratch, in local, local.MaxEncodedBytes - SelectorSize, out encoder._writer);
        if (status != ABIStatus.Success)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination, function.Selector);
        return true;
    }

    /// <summary>Writes a <c>uint&lt;M&gt;</c> from a 64-bit value; see <see cref="ABIWriter.TryWriteUInt64"/>.</summary>
    public bool TryWriteUInt64(ulong value, out ABIStatus status) => _writer.TryWriteUInt64(value, out status);

    /// <summary>Writes an <c>int&lt;M&gt;</c> from a 64-bit value; see <see cref="ABIWriter.TryWriteInt64"/>.</summary>
    public bool TryWriteInt64(long value, out ABIStatus status) => _writer.TryWriteInt64(value, out status);

    /// <summary>Writes a <c>uint&lt;M&gt;</c> from a 32-byte word; see <see cref="ABIWriter.TryWriteUInt256Word"/>.</summary>
    public bool TryWriteUInt256Word(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => _writer.TryWriteUInt256Word(word32, out status);

    /// <summary>Writes an <c>int&lt;M&gt;</c> from a 32-byte word; see <see cref="ABIWriter.TryWriteInt256Word"/>.</summary>
    public bool TryWriteInt256Word(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => _writer.TryWriteInt256Word(word32, out status);

    /// <summary>Writes an <c>address</c>; see <see cref="ABIWriter.TryWriteAddress"/>.</summary>
    public bool TryWriteAddress(scoped ReadOnlySpan<byte> address20, out ABIStatus status) => _writer.TryWriteAddress(address20, out status);

    /// <summary>Writes a <c>bool</c>.</summary>
    public bool TryWriteBool(bool value, out ABIStatus status) => _writer.TryWriteBool(value, out status);

    /// <summary>Writes a <c>bytes&lt;M&gt;</c>; see <see cref="ABIWriter.TryWriteFixedBytes"/>.</summary>
    public bool TryWriteFixedBytes(scoped ReadOnlySpan<byte> bytes, out ABIStatus status) => _writer.TryWriteFixedBytes(bytes, out status);

    /// <summary>Writes a <c>function</c>; see <see cref="ABIWriter.TryWriteFunction"/>.</summary>
    public bool TryWriteFunction(scoped ReadOnlySpan<byte> address20, uint selector, out ABIStatus status) => _writer.TryWriteFunction(address20, selector, out status);

    /// <summary>Writes a dynamic <c>bytes</c> or a UTF-8 validated <c>string</c>; see <see cref="ABIWriter.TryWriteBytes"/>.</summary>
    public bool TryWriteBytes(scoped ReadOnlySpan<byte> bytes, out ABIStatus status) => _writer.TryWriteBytes(bytes, out status);

    /// <summary>Writes a dynamic <c>string</c> after strict UTF-8 validation; see <see cref="ABIWriter.TryWriteStringUtf8"/>.</summary>
    public bool TryWriteStringUtf8(scoped ReadOnlySpan<byte> utf8Bytes, out ABIStatus status) => _writer.TryWriteStringUtf8(utf8Bytes, out status);

    /// <summary>Writes an already scaled <c>fixed&lt;M&gt;x&lt;N&gt;</c> word; see <see cref="ABIWriter.TryWriteSignedScaledWord"/>.</summary>
    public bool TryWriteSignedScaledWord(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => _writer.TryWriteSignedScaledWord(word32, out status);

    /// <summary>Writes an already scaled <c>ufixed&lt;M&gt;x&lt;N&gt;</c> word; see <see cref="ABIWriter.TryWriteUnsignedScaledWord"/>.</summary>
    public bool TryWriteUnsignedScaledWord(scoped ReadOnlySpan<byte> word32, out ABIStatus status) => _writer.TryWriteUnsignedScaledWord(word32, out status);

    /// <summary>Opens the next value, which the schema says is a tuple.</summary>
    public bool TryBeginTuple(out ABIStatus status) => _writer.TryBeginTuple(out status);

    /// <summary>Opens the next value, which the schema says is a <c>T[k]</c>.</summary>
    public bool TryBeginFixedArray(out ABIStatus status) => _writer.TryBeginFixedArray(out status);

    /// <summary>Opens the next value, which the schema says is a <c>T[]</c>, with its element count.</summary>
    public bool TryBeginDynamicArray(int elementCount, out ABIStatus status) => _writer.TryBeginDynamicArray(elementCount, out status);

    /// <summary>Closes the innermost open container.</summary>
    public bool TryEndContainer(out ABIStatus status) => _writer.TryEndContainer(out status);

    /// <summary>
    /// Completes the call: <paramref name="callBytesWritten"/> is <c>4 + argument length</c> (4 for a selector-only call), never
    /// the destination size, and 0 on failure. Expose <c>destination[..callBytesWritten]</c> only after this succeeds.
    /// </summary>
    public bool TryFinish(out int callBytesWritten, out ABIStatus status)
    {
        if (!_writer.TryFinish(out int argumentBytes, out status))
        {
            callBytesWritten = 0;
            return false;
        }

        // argumentBytes is at most MaxEncodedBytes - 4, so the sum cannot exceed int.MaxValue.
        callBytesWritten = SelectorSize + argumentBytes;
        return true;
    }
}
