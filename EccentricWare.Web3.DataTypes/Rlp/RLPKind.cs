namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>
/// The two RLP item kinds. A single byte below <c>0x80</c> is a byte string, not a third kind.
/// <see cref="None"/> is the value of a default (invalid) <see cref="RLPData"/>.
/// </summary>
public enum RLPKind : byte
{
    /// <summary>No value: the kind of <c>default(RLPData)</c>.</summary>
    None = 0,

    /// <summary>A byte string (including the empty string <c>0x80</c> and a literal byte <c>0x00..0x7f</c>).</summary>
    ByteString = 1,

    /// <summary>A list of encoded items (including the empty list <c>0xc0</c>).</summary>
    List = 2
}
