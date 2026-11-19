namespace EccentricWare.Web3.DataTypes.ABI;

/// <summary>
/// An ordered list of component types: the bootstrap-time input for a tuple, including the root argument tuple of a
/// function. It is a plain builder and may be changed after it has been compiled; the compiler copies everything it needs, so
/// later changes never affect an <see cref="ABISchema"/> that already exists. Because components can be added after a tuple is
/// nested in another definition, a cycle can be built by mistake; the compiler detects and rejects it.
/// Not thread-safe: do not change a definition while it is being compiled. Parameter names are not modelled (they are not
/// part of the ABI encoding or the signature).
/// </summary>
public sealed class ABITupleDefinition
{
    private readonly List<ABITypeDefinition> _components;

    /// <summary>Creates an empty tuple definition (zero components).</summary>
    public ABITupleDefinition() => _components = [];

    /// <summary>Creates a tuple definition holding a copy of <paramref name="components"/>.</summary>
    /// <exception cref="ArgumentNullException">The array or one of its elements is null.</exception>
    public ABITupleDefinition(params ABITypeDefinition[] components)
    {
        ArgumentNullException.ThrowIfNull(components);
        _components = new List<ABITypeDefinition>(components.Length);
        foreach (var component in components)
        {
            Add(component);
        }
    }

    /// <summary>Number of components.</summary>
    public int Count => _components.Count;

    /// <summary>The component at <paramref name="index"/>.</summary>
    public ABITypeDefinition this[int index] => _components[index];

    /// <summary>Appends a component and returns this definition for chaining.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
    public ABITupleDefinition Add(ABITypeDefinition component)
    {
        ArgumentNullException.ThrowIfNull(component);
        _components.Add(component);
        return this;
    }

    /// <summary>
    /// Parses a parenthesised canonical type list such as <c>(address,uint256)</c> or <c>()</c>. Allocates: bootstrap only.
    /// Accepts the aliases <c>uint</c>, <c>int</c>, <c>fixed</c> and <c>ufixed</c>; rejects whitespace and the word
    /// <c>tuple</c>. The result is purely syntactic: widths and limits are checked when it is compiled.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="definition">The parsed definition, or null on failure.</param>
    /// <param name="error">The failure detail, <see cref="ABISchemaError.None"/> on success.</param>
    public static bool TryParse(
        string? text,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ABITupleDefinition? definition,
        out ABISchemaError error) =>
        ABITypeParser.TryParseTuple(text, out definition, out error);
}
