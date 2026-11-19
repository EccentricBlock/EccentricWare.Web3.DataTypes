using System.Buffers.Binary;
using System.Text.Unicode;

namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// A read-only, stack-scoped view of one fully validated ABI argument tuple inside an existing buffer (spec 5). It owns no
/// bytes. The only way to obtain a valid instance is <see cref="TryDecodeComplete(ReadOnlySpan{byte}, ABISchema, in ABIDecodeLimits, out ABIData, out ABIStatus)"/>
/// (strict validation of the whole document against a compiled schema) or <see cref="ABICallData.Arguments"/> of a decoded call;
/// there is no public constructor, and <c>default(ABIData)</c> is invalid: <see cref="IsValid"/> is false, <see cref="Schema"/>
/// is null, and every <c>Try*</c> method returns <see cref="ABIStatus.InvalidArgument"/> and resets its outputs.
/// </summary>
/// <remarks>
/// <para>
/// Buffer ownership: validation is only meaningful while the bytes stay unchanged. A <see cref="ReadOnlySpan{T}"/> does not
/// freeze its backing store, so the caller must keep the input stable and unshared for the lifetime of the view. If the bytes
/// are changed anyway, accessors still never throw or read outside the document (they re-check every pointer and bound and
/// report a status), but their results carry no guarantee.
/// </para>
/// <para>
/// Coordinates: every <see cref="ABIRange"/> is relative to the first byte of the root arguments document, at any depth. A path is
/// a sequence of zero-based indices: at a tuple it selects a component, at a fixed or dynamic array an element; the empty path is
/// the root tuple. Array counts are read with <see cref="TryGetArrayLength"/>, they are not addressable children.
/// </para>
/// <para>
/// Status contract of every <c>Try*</c> accessor, in the order checked: default view, <see cref="ABIStatus.InvalidArgument"/>; bad
/// path (negative or out-of-range index, or a step through a primitive, bytes or string), <see cref="ABIStatus.InvalidPath"/>;
/// the field is not of the kind the accessor serves, <see cref="ABIStatus.TypeMismatch"/>; a canonical value that does not
/// fit the requested type, <see cref="ABIStatus.ValueOutOfRange"/>. Defensive failures (document changed after validation) are
/// <see cref="ABIStatus.Truncated"/>, <see cref="ABIStatus.InvalidOffset"/> or <see cref="ABIStatus.NonCanonicalLayout"/>.
/// </para>
/// </remarks>
public readonly ref struct ABIData
{
    private readonly ReadOnlySpan<byte> _encoded;
    private readonly ABISchema? _schema;

    /// <summary>Internal factory: callers must pass bytes that have just passed <see cref="ABIValidator"/> against <paramref name="schema"/>.</summary>
    internal ABIData(ReadOnlySpan<byte> encoded, ABISchema schema)
    {
        _encoded = encoded;
        _schema = schema;
    }

    /// <summary>True for a view produced by successful validation; false for <c>default</c>.</summary>
    public bool IsValid => _schema is not null;

    /// <summary>The whole arguments document; empty for an invalid view (and for a valid view of a zero-argument tuple).</summary>
    public ReadOnlySpan<byte> EncodedBytes => _encoded;

    /// <summary>The schema the document was validated against; null for an invalid view.</summary>
    public ABISchema? Schema => _schema;

    /// <summary>
    /// Strictly validates <paramref name="encodedArguments"/> as exactly one canonical encoding of <paramref name="schema"/>'s
    /// argument tuple, within the budgets of <paramref name="limits"/>. Never throws; every failure is a status and leaves
    /// <paramref name="data"/> default (invalid, null schema, empty bytes).
    /// </summary>
    /// <remarks>Signature note: <paramref name="limits"/> is <c>scoped in</c> (the specification shows <c>in</c>) so the compiler does not tie the lifetime of the returned view to the caller's limits variable, as in <c>RLPData.TryDecodeComplete</c>. Mechanical change, same behaviour.</remarks>
    /// <param name="encodedArguments">The arguments document (no selector). Must stay stable while the result is in use.</param>
    /// <param name="schema">The trusted compiled schema.</param>
    /// <param name="limits">Resource budgets; <c>default</c> is invalid. Read once.</param>
    /// <param name="data">The validated view on success; <c>default</c> otherwise.</param>
    /// <param name="status">The failure status, or <see cref="ABIStatus.Success"/>.</param>
    public static bool TryDecodeComplete(
        ReadOnlySpan<byte> encodedArguments,
        ABISchema schema,
        scoped in ABIDecodeLimits limits,
        out ABIData data,
        out ABIStatus status) =>
        TryDecodeComplete(encodedArguments, schema, in limits, out data, out status, out _);

    /// <summary>
    /// As <see cref="TryDecodeComplete(ReadOnlySpan{byte}, ABISchema, in ABIDecodeLimits, out ABIData, out ABIStatus)"/>, with the diagnostic
    /// <paramref name="error"/>: the status, the document offset where validation failed (-1 when none applies) and the schema
    /// node being processed (-1 when none applies). The error never carries payload bytes.
    /// </summary>
    public static bool TryDecodeComplete(
        ReadOnlySpan<byte> encodedArguments,
        ABISchema schema,
        scoped in ABIDecodeLimits limits,
        out ABIData data,
        out ABIStatus status,
        out ABIError error)
    {
        // Read each limit exactly once: the validated values are the values used, whatever another thread does to the caller's storage.
        int maxBytes = limits.MaxEncodedBytes;
        int maxDepth = limits.MaxValueDepth;
        int maxArrayElements = limits.MaxArrayElements;
        int maxVisited = limits.MaxVisitedValues;
        int maxWork = limits.MaxWorkUnits;
        status = ABIValidator.TryValidate(encodedArguments, schema, maxBytes, maxDepth, maxArrayElements, maxVisited, maxWork, out error);
        if (status != ABIStatus.Success)
        {
            data = default;
            return false;
        }

        data = new ABIData(encodedArguments, schema);
        return true;
    }

    /// <summary>
    /// Locates the field selected by <paramref name="path"/> in O(path depth), without scanning siblings or descendants. The empty
    /// path returns the root tuple: <c>ValueRange = (0, EncodedBytes.Length)</c> and <c>HeadRange = (0, RootTupleHeadSize)</c>. For a
    /// dynamic field the head range is its 32-byte offset word and the value range its whole tail; for a static field both are the
    /// inline encoding.
    /// </summary>
    public bool TryLocate(scoped ReadOnlySpan<int> path, out ABIFieldLocation field, out ABIStatus status)
    {
        field = default;
        ABIStatus located = Locate(path, out ABILocated at);
        status = located;
        if (located != ABIStatus.Success)
        {
            return false;
        }

        field = new ABIFieldLocation(
            new ABIRange(at.HeadOffset, at.HeadLength),
            new ABIRange(at.ValueOffset, at.ValueLength),
            _schema!.GetNode(at.NodeId).Kind);
        return true;
    }

    /// <summary>Element count of the fixed or dynamic array at <paramref name="path"/> (<see cref="ABIStatus.TypeMismatch"/> for any other kind).</summary>
    public bool TryGetArrayLength(scoped ReadOnlySpan<int> path, out int length, out ABIStatus status)
    {
        length = 0;
        status = Locate(path, out ABILocated at);
        if (status != ABIStatus.Success)
        {
            return false;
        }

        ref readonly ABISchemaNode node = ref _schema!.GetNode(at.NodeId);
        if (node.Kind == ABITypeKind.FixedArray)
        {
            length = node.Count;
            return true;
        }

        if (node.Kind != ABITypeKind.DynamicArray)
        {
            status = ABIStatus.TypeMismatch;
            return false;
        }

        status = ABILocator.TryGetCount(_encoded, in at, out int count);
        if (status != ABIStatus.Success)
        {
            return false;
        }

        length = count;
        return true;
    }

    /// <summary>
    /// The raw 32-byte word of an elementary field that carries no scale: uint, int, address, bool, bytes&lt;M&gt; or function.
    /// <see cref="ABIStatus.TypeMismatch"/> for a composite, <c>bytes</c> or <c>string</c> (a raw word read cannot bypass declared types)
    /// and for <c>fixed</c>/<c>ufixed</c>: spec 5.1 says scale-bearing values "MUST never be returned without scale metadata", which wins
    /// over the sentence of the same section that lists the fixed-point word among the words this method serves (see
    /// docs/abi-design-notes.md, row 64). Read those with <see cref="TryGetSignedScaledWord"/> or <see cref="TryGetUnsignedScaledWord"/>.
    /// </summary>
    public bool TryGetWord(scoped ReadOnlySpan<int> path, out ReadOnlySpan<byte> word, out ABIStatus status)
    {
        status = LocateElementary(path, null, out _, out word);
        return status == ABIStatus.Success;
    }

    /// <summary>Value of a <c>uint&lt;M&gt;</c> that fits 64 bits; a canonical wider value is <see cref="ABIStatus.ValueOutOfRange"/>. Only <see cref="ABITypeKind.UInt"/> (not ufixed).</summary>
    public bool TryGetUInt64(scoped ReadOnlySpan<int> path, out ulong value, out ABIStatus status)
    {
        value = 0;
        status = LocateElementary(path, ABITypeKind.UInt, out _, out ReadOnlySpan<byte> word);
        if (status != ABIStatus.Success)
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt64BigEndian(word) != 0
            || BinaryPrimitives.ReadUInt64BigEndian(word.Slice(8)) != 0
            || BinaryPrimitives.ReadUInt64BigEndian(word.Slice(16)) != 0)
        {
            status = ABIStatus.ValueOutOfRange;
            return false;
        }

        value = BinaryPrimitives.ReadUInt64BigEndian(word.Slice(24));
        return true;
    }

    /// <summary>Value of an <c>int&lt;M&gt;</c> that fits a signed 64-bit integer; a canonical value outside that range is <see cref="ABIStatus.ValueOutOfRange"/>. Only <see cref="ABITypeKind.Int"/> (not fixed).</summary>
    public bool TryGetInt64(scoped ReadOnlySpan<int> path, out long value, out ABIStatus status)
    {
        value = 0;
        status = LocateElementary(path, ABITypeKind.Int, out _, out ReadOnlySpan<byte> word);
        if (status != ABIStatus.Success)
        {
            return false;
        }

        // The 256-bit two's-complement value fits 64 bits when the top three limbs are the sign extension of the low limb.
        long low = BinaryPrimitives.ReadInt64BigEndian(word.Slice(24));
        ulong extension = unchecked((ulong)(low >> 63));
        if (BinaryPrimitives.ReadUInt64BigEndian(word) != extension
            || BinaryPrimitives.ReadUInt64BigEndian(word.Slice(8)) != extension
            || BinaryPrimitives.ReadUInt64BigEndian(word.Slice(16)) != extension)
        {
            status = ABIStatus.ValueOutOfRange;
            return false;
        }

        value = low;
        return true;
    }

    /// <summary>The 20 low-order bytes of an <c>address</c> field.</summary>
    public bool TryGetAddress(scoped ReadOnlySpan<int> path, out ReadOnlySpan<byte> address20, out ABIStatus status)
    {
        status = LocateElementary(path, ABITypeKind.Address, out _, out ReadOnlySpan<byte> word);
        address20 = status == ABIStatus.Success ? word.Slice(12, 20) : default;
        return status == ABIStatus.Success;
    }

    /// <summary>The value of a <c>bool</c> field.</summary>
    public bool TryGetBool(scoped ReadOnlySpan<int> path, out bool value, out ABIStatus status)
    {
        value = false;
        status = LocateElementary(path, ABITypeKind.Bool, out _, out ReadOnlySpan<byte> word);
        if (status != ABIStatus.Success)
        {
            return false;
        }

        value = word[31] != 0;
        return true;
    }

    /// <summary>
    /// The unpadded payload of a dynamic <c>bytes</c> or <c>string</c> field and its root-document range (the bytes after the length
    /// word). Not a nested call view: the bytes stay opaque. A <c>string</c> is returned as raw bytes; no UTF-8 claim is made (see
    /// <see cref="TryGetStringUtf8Validated"/>).
    /// </summary>
    public bool TryGetBytePayload(scoped ReadOnlySpan<int> path, out ReadOnlySpan<byte> payload, out ABIRange payloadRange, out ABIStatus status)
    {
        payload = default;
        payloadRange = default;
        status = LocatePayload(path, stringOnly: false, out ReadOnlySpan<byte> bytes, out ABIRange range);
        if (status != ABIStatus.Success)
        {
            return false;
        }

        payload = bytes;
        payloadRange = range;
        return true;
    }

    /// <summary>
    /// The payload of a <c>string</c> field after a separate strict UTF-8 check (spec 2.1): well-formed UTF-8 only, no replacement
    /// or normalisation; otherwise <see cref="ABIStatus.InvalidUtf8"/>. <c>bytes</c> fields are <see cref="ABIStatus.TypeMismatch"/>.
    /// Structural validation never makes this claim, so policy that reads text must use this method.
    /// </summary>
    public bool TryGetStringUtf8Validated(scoped ReadOnlySpan<int> path, out ReadOnlySpan<byte> utf8, out ABIRange payloadRange, out ABIStatus status)
    {
        utf8 = default;
        payloadRange = default;
        status = LocatePayload(path, stringOnly: true, out ReadOnlySpan<byte> bytes, out ABIRange range);
        if (status != ABIStatus.Success)
        {
            return false;
        }

        if (!Utf8.IsValid(bytes))
        {
            status = ABIStatus.InvalidUtf8;
            return false;
        }

        utf8 = bytes;
        payloadRange = range;
        return true;
    }

    /// <summary>The first <c>M</c> bytes of a <c>bytes&lt;M&gt;</c> field, without its right padding.</summary>
    public bool TryGetFixedBytes(scoped ReadOnlySpan<int> path, out ReadOnlySpan<byte> value, out ABIStatus status)
    {
        status = LocateElementary(path, ABITypeKind.FixedBytes, out ABILocated at, out ReadOnlySpan<byte> word);
        value = status == ABIStatus.Success ? word.Slice(0, _schema!.GetNode(at.NodeId).Width) : default;
        return status == ABIStatus.Success;
    }

    /// <summary>
    /// The 20 address bytes and the big-endian four-byte selector of a <c>function</c> field. This is a pointer value, not an
    /// executable nested call.
    /// </summary>
    public bool TryGetFunction(scoped ReadOnlySpan<int> path, out ReadOnlySpan<byte> address20, out uint selector, out ABIStatus status)
    {
        selector = 0;
        status = LocateElementary(path, ABITypeKind.Function, out _, out ReadOnlySpan<byte> word);
        if (status != ABIStatus.Success)
        {
            address20 = default;
            return false;
        }

        address20 = word.Slice(0, 20);
        selector = BinaryPrimitives.ReadUInt32BigEndian(word.Slice(20, 4));
        return true;
    }

    /// <summary>The canonical scaled integer word of a <c>fixed&lt;M&gt;x&lt;N&gt;</c> field and its decimal scale N. Never rounded or narrowed.</summary>
    public bool TryGetSignedScaledWord(scoped ReadOnlySpan<int> path, out ReadOnlySpan<byte> word32, out byte scale, out ABIStatus status) =>
        TryGetScaledWord(path, ABITypeKind.Fixed, out word32, out scale, out status);

    /// <summary>The canonical scaled integer word of a <c>ufixed&lt;M&gt;x&lt;N&gt;</c> field and its decimal scale N. Never rounded or narrowed.</summary>
    public bool TryGetUnsignedScaledWord(scoped ReadOnlySpan<int> path, out ReadOnlySpan<byte> word32, out byte scale, out ABIStatus status) =>
        TryGetScaledWord(path, ABITypeKind.UFixed, out word32, out scale, out status);

    /// <summary>The canonical 32-byte big-endian word of a <c>uint&lt;M&gt;</c> field (any width; zero-extended). Not for ufixed.</summary>
    public bool TryGetUInt256Word(scoped ReadOnlySpan<int> path, out ReadOnlySpan<byte> word32, out ABIStatus status)
    {
        status = LocateElementary(path, ABITypeKind.UInt, out _, out word32);
        return status == ABIStatus.Success;
    }

    /// <summary>The canonical 32-byte two's-complement word of an <c>int&lt;M&gt;</c> field (any width; sign-extended). Not for fixed.</summary>
    public bool TryGetInt256Word(scoped ReadOnlySpan<int> path, out ReadOnlySpan<byte> word32, out ABIStatus status)
    {
        status = LocateElementary(path, ABITypeKind.Int, out _, out word32);
        return status == ABIStatus.Success;
    }

    /// <summary>
    /// Compares two 32-byte words as unsigned big-endian 256-bit numbers: <paramref name="comparison"/> is -1, 0 or 1.
    /// <see cref="ABIStatus.InvalidArgument"/> (and 0) when either span is not exactly 32 bytes.
    /// </summary>
    public static bool TryCompareUnsigned256(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, out int comparison, out ABIStatus status)
    {
        comparison = 0;
        if (left.Length != ABIWord.ABIWordSize || right.Length != ABIWord.ABIWordSize)
        {
            status = ABIStatus.InvalidArgument;
            return false;
        }

        comparison = Math.Sign(left.SequenceCompareTo(right));
        status = ABIStatus.Success;
        return true;
    }

    /// <summary>
    /// Compares two 32-byte words as signed two's-complement 256-bit numbers: <paramref name="comparison"/> is -1, 0 or 1. Signs are
    /// compared before magnitudes. <see cref="ABIStatus.InvalidArgument"/> (and 0) when either span is not exactly 32 bytes.
    /// </summary>
    public static bool TryCompareSigned256(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, out int comparison, out ABIStatus status)
    {
        comparison = 0;
        if (left.Length != ABIWord.ABIWordSize || right.Length != ABIWord.ABIWordSize)
        {
            status = ABIStatus.InvalidArgument;
            return false;
        }

        bool leftNegative = (left[0] & 0x80) != 0;
        bool rightNegative = (right[0] & 0x80) != 0;
        // Same sign: two's-complement order equals unsigned order. Different signs: the negative one is smaller.
        comparison = leftNegative != rightNegative ? (leftNegative ? -1 : 1) : Math.Sign(left.SequenceCompareTo(right));
        status = ABIStatus.Success;
        return true;
    }

    private bool TryGetScaledWord(scoped ReadOnlySpan<int> path, ABITypeKind kind, out ReadOnlySpan<byte> word32, out byte scale, out ABIStatus status)
    {
        scale = 0;
        status = LocateElementary(path, kind, out ABILocated at, out word32);
        if (status != ABIStatus.Success)
        {
            return false;
        }

        scale = (byte)_schema!.GetNode(at.NodeId).Scale; // 1..80, checked by the schema compiler
        return true;
    }

    private ABIStatus Locate(scoped ReadOnlySpan<int> path, out ABILocated located)
    {
        if (_schema is null)
        {
            located = default;
            return ABIStatus.InvalidArgument;
        }

        ABINoLocateCounter counter = default;
        return ABILocator.TryLocate(_schema, _encoded, path, ref counter, out located);
    }

    /// <summary>
    /// Locates an elementary field and returns its 32-byte word. <paramref name="expected"/> null accepts every elementary kind that
    /// carries no scale (everything up to <see cref="ABITypeKind.Function"/>, not <c>fixed</c>/<c>ufixed</c>). <paramref name="word"/> is empty on any failure.
    /// </summary>
    private ABIStatus LocateElementary(scoped ReadOnlySpan<int> path, ABITypeKind? expected, out ABILocated located, out ReadOnlySpan<byte> word)
    {
        word = default;
        ABIStatus status = Locate(path, out located);
        if (status != ABIStatus.Success)
        {
            return status;
        }

        ABITypeKind kind = _schema!.GetNode(located.NodeId).Kind;
        // ABITypeKind order is relied on: the scale-free elementary kinds are exactly the values up to Function (pinned by a test).
        bool scaleFreeElementary = kind <= ABITypeKind.Function;
        if (expected is null ? !scaleFreeElementary : kind != expected.Value)
        {
            return ABIStatus.TypeMismatch;
        }

        // A static elementary value is exactly one word; re-check containment before slicing.
        if (located.ValueLength != ABIWord.ABIWordSize
            || located.ValueOffset < 0
            || located.ValueOffset > _encoded.Length - ABIWord.ABIWordSize)
        {
            return ABIStatus.Truncated;
        }

        word = _encoded.Slice(located.ValueOffset, ABIWord.ABIWordSize);
        return ABIStatus.Success;
    }

    private ABIStatus LocatePayload(scoped ReadOnlySpan<int> path, bool stringOnly, out ReadOnlySpan<byte> payload, out ABIRange range)
    {
        payload = default;
        range = default;
        ABIStatus status = Locate(path, out ABILocated located);
        if (status != ABIStatus.Success)
        {
            return status;
        }

        ABITypeKind kind = _schema!.GetNode(located.NodeId).Kind;
        if (kind != ABITypeKind.String && (stringOnly || kind != ABITypeKind.Bytes))
        {
            return ABIStatus.TypeMismatch;
        }

        status = ABILocator.TryGetPayload(_encoded, in located, out int offset, out int length);
        if (status != ABIStatus.Success)
        {
            return status;
        }

        payload = _encoded.Slice(offset, length);
        range = new ABIRange(offset, length);
        return ABIStatus.Success;
    }
}
