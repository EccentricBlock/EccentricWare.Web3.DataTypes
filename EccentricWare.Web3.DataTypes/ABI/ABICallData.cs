using System.Buffers.Binary;

namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// A validated Solidity function invocation (spec 5 and 6.1): the four-byte selector followed by an <see cref="ABIData"/>
/// argument document. The selector was compared with the supplied <see cref="ABIFunctionSchema"/>; that proves the bytes name the
/// function the trusted caller expected, not that a contract implements it. <c>default(ABICallData)</c> is invalid and its
/// <see cref="Arguments"/> is an invalid <see cref="ABIData"/>. The buffer-ownership rules of <see cref="ABIData"/> apply.
/// </summary>
public readonly ref struct ABICallData
{
    private readonly ReadOnlySpan<byte> _call;
    private readonly ABIData _arguments;
    private readonly uint _selector;

    private ABICallData(ReadOnlySpan<byte> call, uint selector, ABIData arguments)
    {
        _call = call;
        _selector = selector;
        _arguments = arguments;
    }

    /// <summary>True for a view produced by <see cref="TryDecode(ReadOnlySpan{byte}, ABIFunctionSchema, in ABIDecodeLimits, out ABICallData, out ABIStatus)"/>; false for <c>default</c>.</summary>
    public bool IsValid => _arguments.IsValid;

    /// <summary>The four-byte selector as a big-endian number; 0 for an invalid view.</summary>
    public uint Selector => _selector;

    /// <summary>The four-byte selector as this library's <see cref="EccentricWare.Web3.DataTypes.FunctionSelector"/> value type (the same four bytes as <see cref="Selector"/>); <c>default</c> (all zero) for an invalid view.</summary>
    public FunctionSelector FunctionSelector => new(_selector);

    /// <summary>The whole call including the selector; empty for an invalid view.</summary>
    public ReadOnlySpan<byte> EncodedCall => _call;

    /// <summary>
    /// The validated argument document. Its coordinate zero is call byte 4: following ABI pointers never involves the selector, and
    /// every range it returns is relative to the arguments, not to the call (see <see cref="TryTranslateArgumentRange"/>).
    /// </summary>
    public ABIData Arguments => _arguments;

    /// <summary>
    /// Validates <paramref name="callBytes"/> as a call of <paramref name="function"/>, in the order of spec 5.2: schema and limits
    /// (<see cref="ABIStatus.InvalidArgument"/>), total size including the selector (<see cref="ABIStatus.InputTooLarge"/>), at
    /// least four bytes and an equal selector (<see cref="ABIStatus.InvalidSelector"/>), then the same strict validator as
    /// <see cref="ABIData.TryDecodeComplete(ReadOnlySpan{byte}, ABISchema, in ABIDecodeLimits, out ABIData, out ABIStatus)"/> over the bytes after the selector.
    /// The arguments are validated once. A four-byte selector alone is valid for a zero-argument function; empty input is not a call.
    /// </summary>
    /// <remarks>Signature note: <paramref name="limits"/> is <c>scoped in</c> (the specification shows <c>in</c>); see <see cref="ABIData"/>. Mechanical change, same behaviour.</remarks>
    public static bool TryDecode(
        ReadOnlySpan<byte> callBytes,
        ABIFunctionSchema function,
        scoped in ABIDecodeLimits limits,
        out ABICallData call,
        out ABIStatus status) =>
        TryDecode(callBytes, function, in limits, out call, out status, out _);

    /// <summary>
    /// As <see cref="TryDecode(ReadOnlySpan{byte}, ABIFunctionSchema, in ABIDecodeLimits, out ABICallData, out ABIStatus)"/>, with the
    /// diagnostic <paramref name="error"/>. Its document offset is relative to the arguments (coordinate zero at call byte 4) and is
    /// -1 for failures before argument validation starts.
    /// </summary>
    public static bool TryDecode(
        ReadOnlySpan<byte> callBytes,
        ABIFunctionSchema function,
        scoped in ABIDecodeLimits limits,
        out ABICallData call,
        out ABIStatus status,
        out ABIError error)
    {
        call = default;
        int maxBytes = limits.MaxEncodedBytes;
        int maxDepth = limits.MaxValueDepth;
        int maxArrayElements = limits.MaxArrayElements;
        int maxVisited = limits.MaxVisitedValues;
        int maxWork = limits.MaxWorkUnits;

        if (function is null || !new ABIDecodeLimits(maxBytes, maxDepth, maxArrayElements, maxVisited, maxWork).IsValid)
        {
            return Fail(ABIStatus.InvalidArgument, out status, out error);
        }

        if (callBytes.Length > maxBytes)
        {
            return Fail(ABIStatus.InputTooLarge, out status, out error);
        }

        if (callBytes.Length < sizeof(uint) || BinaryPrimitives.ReadUInt32BigEndian(callBytes) != function.Selector)
        {
            return Fail(ABIStatus.InvalidSelector, out status, out error);
        }

        ReadOnlySpan<byte> arguments = callBytes.Slice(sizeof(uint));
        status = ABIValidator.TryValidate(arguments, function.InputSchema, maxBytes, maxDepth, maxArrayElements, maxVisited, maxWork, out error);
        if (status != ABIStatus.Success)
        {
            return false;
        }

        call = new ABICallData(callBytes, function.Selector, new ABIData(arguments, function.InputSchema));
        return true;
    }

    /// <summary>
    /// Translation only: bounds-checks <paramref name="argumentRange"/> against the argument document and adds four bytes to move it
    /// into call coordinates. It does NOT establish that the range is a validated field; policy code must take field locations only
    /// from the schema-driven accessors of <see cref="Arguments"/>. False (and a default range) for an invalid view, a negative
    /// offset or length, or a range that is not inside the arguments.
    /// </summary>
    public bool TryTranslateArgumentRange(ABIRange argumentRange, out ABIRange callRange)
    {
        callRange = default;
        int length = _arguments.EncodedBytes.Length;
        if (!_arguments.IsValid
            || argumentRange.Offset < 0
            || argumentRange.Length < 0
            || argumentRange.Offset > length
            || argumentRange.Length > length - argumentRange.Offset)
        {
            return false;
        }

        callRange = new ABIRange(argumentRange.Offset + sizeof(uint), argumentRange.Length);
        return true;
    }

    private static bool Fail(ABIStatus failure, out ABIStatus status, out ABIError error)
    {
        status = failure;
        error = new ABIError(failure, -1, -1);
        return false;
    }
}
