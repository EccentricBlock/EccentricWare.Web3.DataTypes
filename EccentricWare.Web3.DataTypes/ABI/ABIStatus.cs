namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// Result of a <c>Try*</c> ABI operation. Failure statuses are diagnostics, not authorisation decisions: security-sensitive
/// callers must deny on every value other than <see cref="Success"/>.
/// </summary>
public enum ABIStatus : byte
{
    /// <summary>Complete operation.</summary>
    Success,

    /// <summary>Null reference, invalid limits, default-initialised instance or other caller contract violation.</summary>
    InvalidArgument,

    /// <summary>The schema (definition, canonical signature or compiled table) is malformed.</summary>
    InvalidSchema,

    /// <summary>The schema is well formed but outside what this release supports (for example <c>T[0]</c>).</summary>
    UnsupportedSchema,

    /// <summary>The input exceeds the configured maximum encoded size.</summary>
    InputTooLarge,

    /// <summary>Call data is shorter than four bytes or its selector differs from the function schema's selector.</summary>
    InvalidSelector,

    /// <summary>A path entry is negative, out of range, or descends through a primitive.</summary>
    InvalidPath,

    /// <summary>The accessor does not apply to the kind or width of the addressed field.</summary>
    TypeMismatch,

    /// <summary>A word, length, count or payload is not fully contained in the input.</summary>
    Truncated,

    /// <summary>A dynamic offset is not a plausible in-document pointer: wider than 31 bits, beyond the document, misaligned, or inside its own head region. A plausible pointer that is not the next canonical tail is <see cref="NonCanonicalLayout"/>.</summary>
    InvalidOffset,

    /// <summary>The layout is valid ABI but not the strict canonical form: a dynamic offset that is not the next canonical tail (gap, overlap, alias, reversal), or trailing data.</summary>
    NonCanonicalLayout,

    /// <summary>A padding byte that must be zero is not.</summary>
    InvalidPadding,

    /// <summary>A scalar word is malformed (bad sign extension, boolean other than 0 or 1, address high bytes).</summary>
    InvalidScalar,

    /// <summary>A canonical value does not fit the requested narrower type.</summary>
    ValueOutOfRange,

    /// <summary>A string payload is not valid UTF-8 where strict validation was requested.</summary>
    InvalidUtf8,

    /// <summary>A writer source span overlaps the destination span.</summary>
    SourceOverlapsDestination,

    /// <summary>A size or offset computation overflowed.</summary>
    ArithmeticOverflow,

    /// <summary>Nesting exceeds the configured depth.</summary>
    DepthLimitExceeded,

    /// <summary>An array count, tuple arity or element count exceeds its configured limit.</summary>
    ElementLimitExceeded,

    /// <summary>The visited-value or work budget is exhausted.</summary>
    WorkLimitExceeded,

    /// <summary>The destination buffer is too small.</summary>
    InsufficientDestination,

    /// <summary>The caller-supplied scratch frames are too few.</summary>
    InsufficientScratch,

    /// <summary>The document was finished before every value was written.</summary>
    IncompleteWrite,

    /// <summary>The writer is default, completed, faulted or otherwise cannot accept the operation.</summary>
    InvalidWriterState
}
