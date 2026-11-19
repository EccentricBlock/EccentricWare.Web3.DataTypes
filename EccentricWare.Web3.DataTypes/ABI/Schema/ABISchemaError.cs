namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>Why a schema or signature was rejected. Stable for tests and logging; carries no payload data.</summary>
public enum ABISchemaErrorReason : byte
{
    /// <summary>No error.</summary>
    None,

    /// <summary>A required argument (definition, name, text) was null.</summary>
    NullArgument,

    /// <summary>The compile limits are invalid (for example <c>default</c>).</summary>
    InvalidLimits,

    /// <summary>The definition graph contains a tuple that, directly or indirectly, contains itself.</summary>
    Cycle,

    /// <summary>Integer or fixed-point width is not a multiple of 8 in 8 to 256.</summary>
    InvalidWidth,

    /// <summary><c>bytes&lt;M&gt;</c> with M outside 1 to 32.</summary>
    InvalidFixedBytesLength,

    /// <summary>Fixed-point precision outside 1 to 80.</summary>
    InvalidFixedPrecision,

    /// <summary>A fixed array with a negative length.</summary>
    NegativeArrayLength,

    /// <summary><c>T[0]</c>, which this release does not support.</summary>
    ZeroLengthFixedArray,

    /// <summary>The schema has more nodes than <see cref="ABICompileLimits.MaxSchemaNodes"/>.</summary>
    TooManySchemaNodes,

    /// <summary>The schema is deeper than <see cref="ABICompileLimits.MaxSchemaDepth"/> (or the parser's hard depth cap).</summary>
    SchemaTooDeep,

    /// <summary>A tuple has more members than <see cref="ABICompileLimits.MaxTupleElements"/>.</summary>
    TupleTooWide,

    /// <summary>A fixed array is longer than <see cref="ABICompileLimits.MaxFixedArrayElements"/>.</summary>
    FixedArrayTooLong,

    /// <summary>A head region or static size exceeds <see cref="ABICompileLimits.MaxStaticEncodedBytes"/>.</summary>
    StaticSizeLimit,

    /// <summary>A size computation does not fit a 32-bit integer.</summary>
    SizeOverflow,

    /// <summary>The function name is empty or not a Solidity identifier.</summary>
    InvalidFunctionName,

    /// <summary>The type or signature text does not follow the canonical grammar.</summary>
    MalformedSyntax,

    /// <summary>A type name in the text is not an ABI type.</summary>
    UnknownType
}

/// <summary>
/// Diagnostic detail for a rejected schema. It names the failing schema component and the reason, and never contains
/// transaction bytes. <see cref="None"/> (and <c>default</c>, whose <see cref="Path"/> is null) means success.
/// </summary>
/// <param name="Status">The status category (<see cref="ABIStatus.Success"/> when there was no error).</param>
/// <param name="Reason">The specific reason.</param>
/// <param name="Path">
/// Component path of the failing node: <c>$</c> is the root arguments tuple, <c>/i</c> selects tuple member <c>i</c> and
/// <c>/[]</c> selects an array's element type, so <c>$/1/[]/0</c> is member 0 of the element tuple of member 1. Empty for
/// errors that do not concern a schema node (null argument, invalid limits, text syntax).
/// </param>
/// <param name="TextPosition">Zero-based character index in the parsed text for syntax errors, otherwise -1.</param>
public readonly record struct ABISchemaError(ABIStatus Status, ABISchemaErrorReason Reason, string Path, int TextPosition)
{
    /// <summary>The no-error value: success, no reason, empty path.</summary>
    public static ABISchemaError None => new(ABIStatus.Success, ABISchemaErrorReason.None, string.Empty, -1);

    internal static ABISchemaError At(ABIStatus status, ABISchemaErrorReason reason, string path) => new(status, reason, path, -1);

    internal static ABISchemaError Syntax(ABISchemaErrorReason reason, int textPosition) =>
        new(ABIStatus.InvalidSchema, reason, string.Empty, textPosition);

    internal static ABISchemaError Argument(ABISchemaErrorReason reason) =>
        new(ABIStatus.InvalidArgument, reason, string.Empty, -1);
}
