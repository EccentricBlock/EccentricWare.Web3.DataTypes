using System.Diagnostics.CodeAnalysis;

namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// An immutable description of one ABI type, the bootstrap-time input for schema compilation. Factories never validate
/// widths, scales or lengths: an illegal value is representable here and rejected, with a component path, by the
/// compiler (so every rule lives in one place). Instances are freely shareable.
/// </summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Factory names UInt, Int and String mirror the ABITypeKind members and Solidity type names.")]
public sealed class ABITypeDefinition
{
    private ABITypeDefinition(
        ABITypeKind kind,
        int width = 0,
        int scale = 0,
        int length = 0,
        ABITypeDefinition? element = null,
        ABITupleDefinition? tuple = null)
    {
        Kind = kind;
        Width = width;
        Scale = scale;
        Length = length;
        Element = element;
        TupleDefinition = tuple;
    }

    /// <summary>The kind of this type.</summary>
    public ABITypeKind Kind { get; }

    /// <summary>Bit width M for <see cref="ABITypeKind.UInt"/>, <see cref="ABITypeKind.Int"/>, <see cref="ABITypeKind.Fixed"/> and <see cref="ABITypeKind.UFixed"/>; byte length M for <see cref="ABITypeKind.FixedBytes"/>; otherwise 0.</summary>
    public int Width { get; }

    /// <summary>Decimal precision N for <see cref="ABITypeKind.Fixed"/> and <see cref="ABITypeKind.UFixed"/>; otherwise 0.</summary>
    public int Scale { get; }

    /// <summary>The element count k of a <see cref="ABITypeKind.FixedArray"/>; otherwise 0.</summary>
    public int Length { get; }

    /// <summary>The element type of an array kind; otherwise null.</summary>
    public ABITypeDefinition? Element { get; }

    /// <summary>The component list of a <see cref="ABITypeKind.Tuple"/>; otherwise null.</summary>
    public ABITupleDefinition? TupleDefinition { get; }

    /// <summary><c>address</c>.</summary>
    public static ABITypeDefinition Address { get; } = new(ABITypeKind.Address);

    /// <summary><c>bool</c>.</summary>
    public static ABITypeDefinition Bool { get; } = new(ABITypeKind.Bool);

    /// <summary><c>function</c>.</summary>
    public static ABITypeDefinition Function { get; } = new(ABITypeKind.Function);

    /// <summary>Dynamic <c>bytes</c>.</summary>
    public static ABITypeDefinition Bytes { get; } = new(ABITypeKind.Bytes);

    /// <summary>Dynamic <c>string</c>.</summary>
    public static ABITypeDefinition String { get; } = new(ABITypeKind.String);

    /// <summary><c>uint&lt;bits&gt;</c>.</summary>
    public static ABITypeDefinition UInt(int bits) => new(ABITypeKind.UInt, width: bits);

    /// <summary><c>int&lt;bits&gt;</c>.</summary>
    public static ABITypeDefinition Int(int bits) => new(ABITypeKind.Int, width: bits);

    /// <summary><c>bytes&lt;length&gt;</c>.</summary>
    public static ABITypeDefinition FixedBytes(int length) => new(ABITypeKind.FixedBytes, width: length);

    /// <summary><c>fixed&lt;bits&gt;x&lt;scale&gt;</c>.</summary>
    public static ABITypeDefinition Fixed(int bits, int scale) => new(ABITypeKind.Fixed, width: bits, scale: scale);

    /// <summary><c>ufixed&lt;bits&gt;x&lt;scale&gt;</c>.</summary>
    public static ABITypeDefinition UFixed(int bits, int scale) => new(ABITypeKind.UFixed, width: bits, scale: scale);

    /// <summary>A tuple of <paramref name="components"/>. The list is referenced, not copied, until compilation.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="components"/> is null.</exception>
    public static ABITypeDefinition Tuple(ABITupleDefinition components)
    {
        ArgumentNullException.ThrowIfNull(components);
        return new(ABITypeKind.Tuple, tuple: components);
    }

    /// <summary><c>element[length]</c>. A zero or negative length is representable and rejected by the compiler.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is null.</exception>
    public static ABITypeDefinition FixedArray(ABITypeDefinition element, int length)
    {
        ArgumentNullException.ThrowIfNull(element);
        return new(ABITypeKind.FixedArray, length: length, element: element);
    }

    /// <summary><c>element[]</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is null.</exception>
    public static ABITypeDefinition DynamicArray(ABITypeDefinition element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return new(ABITypeKind.DynamicArray, element: element);
    }

    /// <summary>
    /// Parses one canonical type such as <c>(address,uint256)[]</c>, <c>bytes3[2]</c> or <c>uint</c>. Allocates: bootstrap
    /// only. Accepts the aliases <c>uint</c>, <c>int</c>, <c>fixed</c> and <c>ufixed</c>; rejects whitespace and the word
    /// <c>tuple</c>. The result is purely syntactic (so <c>uint7</c> and <c>bytes33</c> parse): widths and limits are
    /// checked when it is compiled.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="definition">The parsed definition, or null on failure.</param>
    /// <param name="error">The failure detail, <see cref="ABISchemaError.None"/> on success.</param>
    public static bool TryParse(
        string? text,
        [NotNullWhen(true)] out ABITypeDefinition? definition,
        out ABISchemaError error) =>
        ABITypeParser.TryParseType(text, out definition, out error);
}
