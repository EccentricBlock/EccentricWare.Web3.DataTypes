using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// Typed adapters between the ABI codec and this library's value types (<see cref="uint256"/>, <see cref="int256"/>,
/// <see cref="Address"/>, <see cref="Hash32"/>, <see cref="FunctionSelector"/>), in the style of <see cref="RLPTypedExtensions"/>.
/// Readers extend <see cref="ABIData"/>; writers extend <c>ref</c> <see cref="ABIWriter"/>, <see cref="ABICallEncoder"/> and <see cref="ABIMeasurer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every member delegates to the existing span accessor or span writer and only converts the already validated 32-byte word (or the
/// 20 address bytes, the selector number) to or from the value type. Type and width checks, canonical-word validation, statuses, error
/// precedence, budgets, faulting and failure atomicity are therefore those of the span member, by construction; nothing here has a
/// rule of its own. No member allocates (the writers use a 32-byte stack temporary) and none throws for any document, path or value:
/// widths are fixed before a value-type constructor runs, so the constructors' argument exceptions are unreachable.
/// </para>
/// <para>
/// Why extension methods and not instance overloads: an instance <c>TryGetAddress(path, out Address, out status)</c> next to the span
/// overload would change what <c>out var</c> means at existing call sites, and new public instance members on the writers would change
/// the pinned grammar of the writer types (spec 7.6: the same events on <see cref="ABIWriter"/> and <see cref="ABIMeasurer"/>). Instance
/// members win overload resolution, so <c>data.TryGetAddress(path, out var a, out var s)</c> still binds the span member.
/// </para>
/// <para>
/// What this surface does NOT do. Reads do not re-check the canonical high bytes (the decoder already proved them zero for the unchanged
/// document: the typed reads convert the validated word, they do not validate again). A <c>bytes4</c> is returned as a
/// <see cref="FunctionSelector"/> because the bytes are the same four; it is not thereby a function selector: an ERC-165 interface id
/// is also a <c>bytes4</c>, and so is any other four-byte tag. <c>fixed</c>/<c>ufixed</c> have no typed form on purpose: a scaled value is
/// only ever read with its scale (<see cref="ABIData.TryGetSignedScaledWord"/>, <see cref="ABIData.TryGetUnsignedScaledWord"/>). Writing a value never narrows silently: an integer that does not fit
/// the declared width is <see cref="ABIStatus.ValueOutOfRange"/>.
/// </para>
/// <para>
/// Call-site conversions that run BEFORE the codec. The implicit conversions <c>int</c>/<c>long</c> to <see cref="uint256"/> throw
/// <see cref="OverflowException"/> for a negative number, and <see cref="uint256"/> <c>+</c>, <c>-</c> and <c>*</c> throw on overflow:
/// <c>w.TryWriteUInt256(-1, out s)</c> or <c>w.TryWriteUInt256(a + b, out s)</c> throws in the caller's own expression, before this method is
/// entered, so no status can report it and the writer is not faulted. Build the value with the non-throwing constructors or checked
/// helpers of <see cref="uint256"/> where the operands are not trusted. Pinned by a test; the codec itself never throws.
/// </para>
/// <para>
/// Status tables below name the statuses of the span member each method delegates to; "defensive" statuses (<see cref="ABIStatus.Truncated"/>,
/// <see cref="ABIStatus.InvalidOffset"/>, <see cref="ABIStatus.NonCanonicalLayout"/>) can only occur when the document bytes were changed after
/// validation (see <see cref="ABIData"/>).
/// </para>
/// </remarks>
public static class ABITypedExtensions
{
    // ------------------------------------------------------------------ readers

    /// <summary>
    /// The value of a <c>uint&lt;M&gt;</c> field, any width M, as a <see cref="uint256"/> (zero-extended: the decoder has proved the bits above M are zero).
    /// Same statuses as <see cref="ABIData.TryGetUInt256Word"/>; <paramref name="value"/> is zero on failure.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The field is a <c>uint&lt;M&gt;</c>.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description><c>default</c> view.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidPath"/></term><description>A negative or out-of-range index, or a step through a primitive, <c>bytes</c> or <c>string</c>; wins over <see cref="ABIStatus.TypeMismatch"/>.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>Any other kind, <c>ufixed</c> included.</description></item>
    /// <item><term><see cref="ABIStatus.Truncated"/></term><description>Defensive (document changed after validation).</description></item>
    /// </list>
    /// </remarks>
    public static bool TryGetUInt256(this in ABIData data, scoped ReadOnlySpan<int> path, out uint256 value, out ABIStatus status)
    {
        if (data.TryGetUInt256Word(path, out ReadOnlySpan<byte> word, out status))
        {
            value = new uint256(word);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// The value of an <c>int&lt;M&gt;</c> field, any width M, as an <see cref="int256"/> (sign-extended: the decoder has proved the bits above M copy bit M-1).
    /// Same statuses as <see cref="ABIData.TryGetInt256Word"/>; <paramref name="value"/> is zero on failure.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The field is an <c>int&lt;M&gt;</c>.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description><c>default</c> view.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidPath"/></term><description>A negative or out-of-range index, or a step through a primitive, <c>bytes</c> or <c>string</c>; wins over <see cref="ABIStatus.TypeMismatch"/>.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>Any other kind, <c>fixed</c> included.</description></item>
    /// <item><term><see cref="ABIStatus.Truncated"/></term><description>Defensive (document changed after validation).</description></item>
    /// </list>
    /// </remarks>
    public static bool TryGetInt256(this in ABIData data, scoped ReadOnlySpan<int> path, out int256 value, out ABIStatus status)
    {
        if (data.TryGetInt256Word(path, out ReadOnlySpan<byte> word, out status))
        {
            value = new int256(word);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// An <c>address</c> field as an EVM <see cref="Address"/> (<see cref="AddressType.Evm"/>). Same statuses as the span overload
    /// <see cref="ABIData.TryGetAddress(ReadOnlySpan{int}, out ReadOnlySpan{byte}, out ABIStatus)"/>; <paramref name="address"/> is <c>default</c> on failure.
    /// </summary>
    /// <remarks>
    /// <para><c>default(Address)</c> is a VALID zero address, so a caller must test the returned <see cref="bool"/> (or the status), never the value.</para>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The field is an <c>address</c>.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description><c>default</c> view.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidPath"/></term><description>A negative or out-of-range index, or a step through a primitive, <c>bytes</c> or <c>string</c>; wins over <see cref="ABIStatus.TypeMismatch"/>.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>Any other kind.</description></item>
    /// <item><term><see cref="ABIStatus.Truncated"/></term><description>Defensive (document changed after validation).</description></item>
    /// </list>
    /// </remarks>
    public static bool TryGetAddress(this in ABIData data, scoped ReadOnlySpan<int> path, out Address address, out ABIStatus status)
    {
        if (data.TryGetAddress(path, out ReadOnlySpan<byte> address20, out status))
        {
            address = Address.FromEvmBytes(address20);
            return true;
        }

        address = default;
        return false;
    }

    /// <summary>
    /// A <c>bytes32</c> field as a <see cref="Hash32"/> (the same 32 bytes, same order). Any other <c>bytes&lt;M&gt;</c> is
    /// <see cref="ABIStatus.TypeMismatch"/>: the declared width must match the value type. <paramref name="value"/> is <c>default</c> on failure.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The field is a <c>bytes32</c>.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description><c>default</c> view.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidPath"/></term><description>A negative or out-of-range index, or a step through a primitive, <c>bytes</c> or <c>string</c>; wins over <see cref="ABIStatus.TypeMismatch"/>.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>Any other kind, and a <c>bytes&lt;M&gt;</c> with M other than 32.</description></item>
    /// <item><term><see cref="ABIStatus.Truncated"/></term><description>Defensive (document changed after validation).</description></item>
    /// </list>
    /// </remarks>
    public static bool TryGetBytes32(this in ABIData data, scoped ReadOnlySpan<int> path, out Hash32 value, out ABIStatus status)
    {
        if (data.TryGetFixedBytes(path, out ReadOnlySpan<byte> bytes, out status))
        {
            if (bytes.Length == Hash32.ByteLength)
            {
                value = new Hash32(bytes);
                return true;
            }

            status = ABIStatus.TypeMismatch;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// A <c>bytes4</c> field as a <see cref="FunctionSelector"/> (the same four bytes). Any other <c>bytes&lt;M&gt;</c> is
    /// <see cref="ABIStatus.TypeMismatch"/>. A <c>bytes4</c> is not necessarily a function selector (an ERC-165 interface id is also a
    /// <c>bytes4</c>): the schema, not this method, says what the four bytes mean. <paramref name="value"/> is <c>default</c> on failure.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The field is a <c>bytes4</c>.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description><c>default</c> view.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidPath"/></term><description>A negative or out-of-range index, or a step through a primitive, <c>bytes</c> or <c>string</c>; wins over <see cref="ABIStatus.TypeMismatch"/>.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>Any other kind, and a <c>bytes&lt;M&gt;</c> with M other than 4.</description></item>
    /// <item><term><see cref="ABIStatus.Truncated"/></term><description>Defensive (document changed after validation).</description></item>
    /// </list>
    /// </remarks>
    public static bool TryGetBytes4(this in ABIData data, scoped ReadOnlySpan<int> path, out FunctionSelector value, out ABIStatus status)
    {
        if (data.TryGetFixedBytes(path, out ReadOnlySpan<byte> bytes, out status))
        {
            if (bytes.Length == FunctionSelector.ByteLength)
            {
                value = new FunctionSelector(bytes);
                return true;
            }

            status = ABIStatus.TypeMismatch;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// A <c>function</c> field: the target EVM <see cref="Address"/> and the <see cref="FunctionSelector"/>. A pointer value, not an executable
    /// nested call (the codec never treats it as one). Same statuses as the span overload
    /// <see cref="ABIData.TryGetFunction(ReadOnlySpan{int}, out ReadOnlySpan{byte}, out uint, out ABIStatus)"/>; both outs are <c>default</c> on failure.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The field is a <c>function</c>.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description><c>default</c> view.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidPath"/></term><description>A negative or out-of-range index, or a step through a primitive, <c>bytes</c> or <c>string</c>; wins over <see cref="ABIStatus.TypeMismatch"/>.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>Any other kind.</description></item>
    /// <item><term><see cref="ABIStatus.Truncated"/></term><description>Defensive (document changed after validation).</description></item>
    /// </list>
    /// </remarks>
    public static bool TryGetFunction(this in ABIData data, scoped ReadOnlySpan<int> path, out Address target, out FunctionSelector selector, out ABIStatus status)
    {
        if (data.TryGetFunction(path, out ReadOnlySpan<byte> address20, out uint rawSelector, out status))
        {
            target = Address.FromEvmBytes(address20);
            selector = new FunctionSelector(rawSelector);
            return true;
        }

        target = default;
        selector = default;
        return false;
    }

    // ------------------------------------------------------------------ writers (ABIWriter)
    /// <summary>Writes a <c>uint&lt;M&gt;</c> from a <see cref="uint256"/>. Same statuses and precedence as <see cref="ABIWriter.TryWriteUInt256Word"/>: the slot is checked, then the value must fit M bits.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The value was written.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.ValueOutOfRange"/></term><description>The value does not fit the declared width M (never truncated).</description></item>
    /// </list>
    /// <para>Any failure faults the writer exactly like the span member. See the type remarks for call-site conversions that throw before this method runs.</para>
    /// </remarks>
    public static bool TryWriteUInt256(this ref ABIWriter writer, uint256 value, out ABIStatus status)
    {
        Span<byte> word = stackalloc byte[32];
        value.WriteBigEndian(word);
        return writer.TryWriteUInt256Word(word, out status);
    }
    /// <summary>Writes an <c>int&lt;M&gt;</c> from an <see cref="int256"/>. Same statuses and precedence as <see cref="ABIWriter.TryWriteInt256Word"/>.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The value was written (sign-extended).</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.ValueOutOfRange"/></term><description>The value does not fit the declared width M as a signed number.</description></item>
    /// </list>
    /// </remarks>
    public static bool TryWriteInt256(this ref ABIWriter writer, int256 value, out ABIStatus status)
    {
        Span<byte> word = stackalloc byte[32];
        value.WriteBigEndian(word);
        return writer.TryWriteInt256Word(word, out status);
    }
    /// <summary>
    /// Writes an <c>address</c> from an EVM <see cref="Address"/>. A non-EVM (Solana) address cannot be an ABI address: it is reported exactly like
    /// a wrong-length span, <see cref="ABIStatus.InvalidArgument"/>, at the same step (after the writer, arity, kind and budget checks) and faults the writer.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The 20 bytes were written, right-aligned.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description>The address is not an EVM address.</description></item>
    /// </list>
    /// </remarks>
    public static bool TryWriteAddress(this ref ABIWriter writer, Address value, out ABIStatus status)
    {
        if (value.Type != AddressType.Evm)
        {
            return writer.TryWriteAddress(ReadOnlySpan<byte>.Empty, out status);
        }

        Span<byte> address = stackalloc byte[Address.EvmByteLength];
        value.WriteBytes(address);
        return writer.TryWriteAddress(address, out status);
    }
    /// <summary>Writes a <c>bytes32</c> from a <see cref="Hash32"/> (same bytes, same order). A slot of another <c>bytes&lt;M&gt;</c> width is <see cref="ABIStatus.InvalidArgument"/>, as for a 32-byte span.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The 32 bytes were written.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description>The slot is a <c>bytes&lt;M&gt;</c> with M other than 32 (a source must be exactly M bytes).</description></item>
    /// </list>
    /// </remarks>
    public static bool TryWriteBytes32(this ref ABIWriter writer, Hash32 value, out ABIStatus status)
    {
        Span<byte> bytes = stackalloc byte[Hash32.ByteLength];
        value.WriteBigEndian(bytes);
        return writer.TryWriteFixedBytes(bytes, out status);
    }
    /// <summary>Writes a <c>bytes4</c> from a <see cref="FunctionSelector"/>. A slot of another <c>bytes&lt;M&gt;</c> width is <see cref="ABIStatus.InvalidArgument"/>.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The four bytes were written, left-aligned with zero padding.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description>The slot is a <c>bytes&lt;M&gt;</c> with M other than 4.</description></item>
    /// </list>
    /// </remarks>
    public static bool TryWriteBytes4(this ref ABIWriter writer, FunctionSelector value, out ABIStatus status)
    {
        Span<byte> bytes = stackalloc byte[FunctionSelector.ByteLength];
        value.WriteBytes(bytes);
        return writer.TryWriteFixedBytes(bytes, out status);
    }
    /// <summary>Writes a <c>function</c> from an EVM <see cref="Address"/> and a <see cref="FunctionSelector"/>. A non-EVM address is <see cref="ABIStatus.InvalidArgument"/> as in <see cref="TryWriteAddress(ref ABIWriter, Address, out ABIStatus)"/>.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The 20 address bytes, the selector and eight zero bytes were written.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description>The target is not an EVM address.</description></item>
    /// </list>
    /// </remarks>
    public static bool TryWriteFunction(this ref ABIWriter writer, Address target, FunctionSelector selector, out ABIStatus status)
    {
        if (target.Type != AddressType.Evm)
        {
            return writer.TryWriteFunction(ReadOnlySpan<byte>.Empty, selector.Value, out status);
        }

        Span<byte> address = stackalloc byte[Address.EvmByteLength];
        target.WriteBytes(address);
        return writer.TryWriteFunction(address, selector.Value, out status);
    }

    // ------------------------------------------------------------------ writers (ABICallEncoder)
    /// <summary>Writes a <c>uint&lt;M&gt;</c> from a <see cref="uint256"/>. Same statuses and precedence as <see cref="ABICallEncoder.TryWriteUInt256Word"/>: the slot is checked, then the value must fit M bits.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The value was written.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.ValueOutOfRange"/></term><description>The value does not fit the declared width M (never truncated).</description></item>
    /// </list>
    /// <para>Any failure faults the writer exactly like the span member. See the type remarks for call-site conversions that throw before this method runs.</para>
    /// </remarks>
    public static bool TryWriteUInt256(this ref ABICallEncoder writer, uint256 value, out ABIStatus status)
    {
        Span<byte> word = stackalloc byte[32];
        value.WriteBigEndian(word);
        return writer.TryWriteUInt256Word(word, out status);
    }
    /// <summary>Writes an <c>int&lt;M&gt;</c> from an <see cref="int256"/>. Same statuses and precedence as <see cref="ABICallEncoder.TryWriteInt256Word"/>.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The value was written (sign-extended).</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.ValueOutOfRange"/></term><description>The value does not fit the declared width M as a signed number.</description></item>
    /// </list>
    /// </remarks>
    public static bool TryWriteInt256(this ref ABICallEncoder writer, int256 value, out ABIStatus status)
    {
        Span<byte> word = stackalloc byte[32];
        value.WriteBigEndian(word);
        return writer.TryWriteInt256Word(word, out status);
    }
    /// <summary>
    /// Writes an <c>address</c> from an EVM <see cref="Address"/>. A non-EVM (Solana) address cannot be an ABI address: it is reported exactly like
    /// a wrong-length span, <see cref="ABIStatus.InvalidArgument"/>, at the same step (after the writer, arity, kind and budget checks) and faults the writer.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The 20 bytes were written, right-aligned.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description>The address is not an EVM address.</description></item>
    /// </list>
    /// </remarks>
    public static bool TryWriteAddress(this ref ABICallEncoder writer, Address value, out ABIStatus status)
    {
        if (value.Type != AddressType.Evm)
        {
            return writer.TryWriteAddress(ReadOnlySpan<byte>.Empty, out status);
        }

        Span<byte> address = stackalloc byte[Address.EvmByteLength];
        value.WriteBytes(address);
        return writer.TryWriteAddress(address, out status);
    }
    /// <summary>Writes a <c>bytes32</c> from a <see cref="Hash32"/> (same bytes, same order). A slot of another <c>bytes&lt;M&gt;</c> width is <see cref="ABIStatus.InvalidArgument"/>, as for a 32-byte span.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The 32 bytes were written.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description>The slot is a <c>bytes&lt;M&gt;</c> with M other than 32 (a source must be exactly M bytes).</description></item>
    /// </list>
    /// </remarks>
    public static bool TryWriteBytes32(this ref ABICallEncoder writer, Hash32 value, out ABIStatus status)
    {
        Span<byte> bytes = stackalloc byte[Hash32.ByteLength];
        value.WriteBigEndian(bytes);
        return writer.TryWriteFixedBytes(bytes, out status);
    }
    /// <summary>Writes a <c>bytes4</c> from a <see cref="FunctionSelector"/>. A slot of another <c>bytes&lt;M&gt;</c> width is <see cref="ABIStatus.InvalidArgument"/>.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The four bytes were written, left-aligned with zero padding.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description>The slot is a <c>bytes&lt;M&gt;</c> with M other than 4.</description></item>
    /// </list>
    /// </remarks>
    public static bool TryWriteBytes4(this ref ABICallEncoder writer, FunctionSelector value, out ABIStatus status)
    {
        Span<byte> bytes = stackalloc byte[FunctionSelector.ByteLength];
        value.WriteBytes(bytes);
        return writer.TryWriteFixedBytes(bytes, out status);
    }
    /// <summary>Writes a <c>function</c> from an EVM <see cref="Address"/> and a <see cref="FunctionSelector"/>. A non-EVM address is <see cref="ABIStatus.InvalidArgument"/> as in <see cref="TryWriteAddress(ref ABICallEncoder, Address, out ABIStatus)"/>.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The 20 address bytes, the selector and eight zero bytes were written.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description>The target is not an EVM address.</description></item>
    /// </list>
    /// </remarks>
    public static bool TryWriteFunction(this ref ABICallEncoder writer, Address target, FunctionSelector selector, out ABIStatus status)
    {
        if (target.Type != AddressType.Evm)
        {
            return writer.TryWriteFunction(ReadOnlySpan<byte>.Empty, selector.Value, out status);
        }

        Span<byte> address = stackalloc byte[Address.EvmByteLength];
        target.WriteBytes(address);
        return writer.TryWriteFunction(address, selector.Value, out status);
    }

    // ------------------------------------------------------------------ writers (ABIMeasurer)
    /// <summary>Measures a <c>uint&lt;M&gt;</c> from a <see cref="uint256"/>. Same statuses and precedence as <see cref="ABIMeasurer.TryWriteUInt256Word"/>: the slot is checked, then the value must fit M bits.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The value was written.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.ValueOutOfRange"/></term><description>The value does not fit the declared width M (never truncated).</description></item>
    /// </list>
    /// <para>Any failure faults the writer exactly like the span member. See the type remarks for call-site conversions that throw before this method runs.</para>
    /// </remarks>
    public static bool TryWriteUInt256(this ref ABIMeasurer writer, uint256 value, out ABIStatus status)
    {
        Span<byte> word = stackalloc byte[32];
        value.WriteBigEndian(word);
        return writer.TryWriteUInt256Word(word, out status);
    }
    /// <summary>Measures an <c>int&lt;M&gt;</c> from an <see cref="int256"/>. Same statuses and precedence as <see cref="ABIMeasurer.TryWriteInt256Word"/>.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The value was written (sign-extended).</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.ValueOutOfRange"/></term><description>The value does not fit the declared width M as a signed number.</description></item>
    /// </list>
    /// </remarks>
    public static bool TryWriteInt256(this ref ABIMeasurer writer, int256 value, out ABIStatus status)
    {
        Span<byte> word = stackalloc byte[32];
        value.WriteBigEndian(word);
        return writer.TryWriteInt256Word(word, out status);
    }
    /// <summary>
    /// Measures an <c>address</c> from an EVM <see cref="Address"/>. A non-EVM (Solana) address cannot be an ABI address: it is reported exactly like
    /// a wrong-length span, <see cref="ABIStatus.InvalidArgument"/>, at the same step (after the writer, arity, kind and budget checks) and faults the writer.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The 20 bytes were written, right-aligned.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description>The address is not an EVM address.</description></item>
    /// </list>
    /// </remarks>
    public static bool TryWriteAddress(this ref ABIMeasurer writer, Address value, out ABIStatus status)
    {
        if (value.Type != AddressType.Evm)
        {
            return writer.TryWriteAddress(ReadOnlySpan<byte>.Empty, out status);
        }

        Span<byte> address = stackalloc byte[Address.EvmByteLength];
        value.WriteBytes(address);
        return writer.TryWriteAddress(address, out status);
    }
    /// <summary>Measures a <c>bytes32</c> from a <see cref="Hash32"/> (same bytes, same order). A slot of another <c>bytes&lt;M&gt;</c> width is <see cref="ABIStatus.InvalidArgument"/>, as for a 32-byte span.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The 32 bytes were written.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description>The slot is a <c>bytes&lt;M&gt;</c> with M other than 32 (a source must be exactly M bytes).</description></item>
    /// </list>
    /// </remarks>
    public static bool TryWriteBytes32(this ref ABIMeasurer writer, Hash32 value, out ABIStatus status)
    {
        Span<byte> bytes = stackalloc byte[Hash32.ByteLength];
        value.WriteBigEndian(bytes);
        return writer.TryWriteFixedBytes(bytes, out status);
    }
    /// <summary>Measures a <c>bytes4</c> from a <see cref="FunctionSelector"/>. A slot of another <c>bytes&lt;M&gt;</c> width is <see cref="ABIStatus.InvalidArgument"/>.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The four bytes were written, left-aligned with zero padding.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description>The slot is a <c>bytes&lt;M&gt;</c> with M other than 4.</description></item>
    /// </list>
    /// </remarks>
    public static bool TryWriteBytes4(this ref ABIMeasurer writer, FunctionSelector value, out ABIStatus status)
    {
        Span<byte> bytes = stackalloc byte[FunctionSelector.ByteLength];
        value.WriteBytes(bytes);
        return writer.TryWriteFixedBytes(bytes, out status);
    }
    /// <summary>Measures a <c>function</c> from an EVM <see cref="Address"/> and a <see cref="FunctionSelector"/>. A non-EVM address is <see cref="ABIStatus.InvalidArgument"/> as in <see cref="TryWriteAddress(ref ABIMeasurer, Address, out ABIStatus)"/>.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Status</term><description>When</description></listheader>
    /// <item><term><see cref="ABIStatus.Success"/></term><description>The 20 address bytes, the selector and eight zero bytes were written.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidWriterState"/></term><description>The writer is <c>default</c>, faulted, completed, or its scratch was reused; nothing changes.</description></item>
    /// <item><term><see cref="ABIStatus.ElementLimitExceeded"/></term><description>The open container expects no further value.</description></item>
    /// <item><term><see cref="ABIStatus.TypeMismatch"/></term><description>The next slot is not of the kind this member serves.</description></item>
    /// <item><term><see cref="ABIStatus.WorkLimitExceeded"/></term><description>A value or work budget is exhausted.</description></item>
    /// <item><term><see cref="ABIStatus.InvalidArgument"/></term><description>The target is not an EVM address.</description></item>
    /// </list>
    /// </remarks>
    public static bool TryWriteFunction(this ref ABIMeasurer writer, Address target, FunctionSelector selector, out ABIStatus status)
    {
        if (target.Type != AddressType.Evm)
        {
            return writer.TryWriteFunction(ReadOnlySpan<byte>.Empty, selector.Value, out status);
        }

        Span<byte> address = stackalloc byte[Address.EvmByteLength];
        target.WriteBytes(address);
        return writer.TryWriteFunction(address, selector.Value, out status);
    }
}
