using System.Diagnostics.CodeAnalysis;

namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>The kind of an ABI type node. The numeric values are stable and may be used as table indexes.</summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Member names UInt, Int and String are mandated by the ABI specification (section 5).")]
public enum ABITypeKind : byte
{
    /// <summary><c>uint&lt;M&gt;</c>, M a multiple of 8 from 8 to 256.</summary>
    UInt,

    /// <summary><c>int&lt;M&gt;</c>, M a multiple of 8 from 8 to 256.</summary>
    Int,

    /// <summary><c>address</c>: 20 bytes in the low-order end of a word.</summary>
    Address,

    /// <summary><c>bool</c>: a word that is exactly 0 or 1.</summary>
    Bool,

    /// <summary><c>bytes&lt;M&gt;</c>, M from 1 to 32, left-aligned and right zero-padded.</summary>
    FixedBytes,

    /// <summary><c>function</c>: 20-byte address plus 4-byte selector, left-aligned, eight trailing zero bytes.</summary>
    Function,

    /// <summary><c>fixed&lt;M&gt;x&lt;N&gt;</c>: signed scaled integer, N from 1 to 80.</summary>
    Fixed,

    /// <summary><c>ufixed&lt;M&gt;x&lt;N&gt;</c>: unsigned scaled integer, N from 1 to 80.</summary>
    UFixed,

    /// <summary>Dynamic <c>bytes</c>.</summary>
    Bytes,

    /// <summary>Dynamic <c>string</c>.</summary>
    String,

    /// <summary><c>(T1,...,Tn)</c>, possibly empty.</summary>
    Tuple,

    /// <summary><c>T[k]</c> with k greater than zero.</summary>
    FixedArray,

    /// <summary><c>T[]</c>.</summary>
    DynamicArray
}
