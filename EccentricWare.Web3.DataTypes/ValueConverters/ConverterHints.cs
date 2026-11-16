using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.ValueConverters;

/// <summary>Shared handling of <see cref="ConverterMappingHints"/> so every converter treats caller hints the same way.</summary>
internal static class ConverterHints
{
    /// <summary>
    /// Returns <paramref name="defaults"/> with any hint the caller set laid over it: a hint the caller leaves unset (for
    /// example the size, when only the precision is given) keeps its default; a hint the caller sets wins.
    /// </summary>
    internal static ConverterMappingHints Merge(ConverterMappingHints defaults, ConverterMappingHints? callerHints)
        => callerHints is null ? defaults : defaults.With(callerHints);
}
