namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>
/// Result of a <c>Try*</c> RLP operation. Failure statuses are diagnostics, not authorisation decisions: security-sensitive
/// callers must deny on every value other than <see cref="Success"/>.
/// </summary>
public enum RLPStatus : byte
{
    /// <summary>Complete operation.</summary>
    Success = 0,

    /// <summary>Invalid limits, negative size, invalid ordinal or other caller contract violation.</summary>
    InvalidArgument,

    /// <summary>No top-level encoded item.</summary>
    EmptyInput,

    /// <summary>Root prefix, length bytes or payload not fully available.</summary>
    TruncatedInput,

    /// <summary>Redundant string or length encoding (for example <c>81 7f</c>, or a long form for a length below 56).</summary>
    NonCanonicalEncoding,

    /// <summary>Bytes remain after one complete root item.</summary>
    TrailingData,

    /// <summary>A child exceeds its containing list, or a list body is not a complete item sequence.</summary>
    InvalidListBoundary,

    /// <summary>An advertised or calculated length cannot be represented (above <see cref="int.MaxValue"/>).</summary>
    LengthOverflow,

    /// <summary>The configured encoded-byte limit was exceeded.</summary>
    TooLarge,

    /// <summary>List nesting exceeds the configured depth (also: the writer's frame workspace is full).</summary>
    TooDeep,

    /// <summary>The total item count exceeds the configured budget.</summary>
    TooManyItems,

    /// <summary>A list operation on a byte string, or a scalar operation on a list.</summary>
    WrongKind,

    /// <summary>An integer magnitude has a leading zero byte (a raw string <c>00</c> is valid RLP but not an integer).</summary>
    NonCanonicalInteger,

    /// <summary>An integer magnitude does not fit the requested width.</summary>
    IntegerOverflow,

    /// <summary>Insufficient output capacity.</summary>
    DestinationTooSmall,

    /// <summary>The index workspace cannot hold every direct child range.</summary>
    IndexCapacityExceeded,

    /// <summary>Source and destination alias in an unsupported way.</summary>
    OverlappingBuffers,

    /// <summary>The writer is default-constructed, already completed, or otherwise cannot accept the operation.</summary>
    WriterStateInvalid,

    /// <summary>No matching open list, or the announced child bytes of a list are incomplete.</summary>
    UnbalancedList,

    /// <summary>An attempt to write a second top-level item.</summary>
    ExtraRoot,

    /// <summary>A byte string whose length differs from the fixed width the typed accessor requires (for example not 32 bytes for a Hash32).</summary>
    InvalidFixedLength,

    /// <summary>The signature recovery value is outside its domain: a typed y parity that is not 0 or 1, or a legacy <c>v</c> that is not 27, 28 or at least 35.</summary>
    InvalidSignatureV
}
