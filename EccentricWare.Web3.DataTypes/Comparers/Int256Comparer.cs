
using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.Comparers;

/// <summary>
/// Comparer for <see cref="int256"/> suitable for:
/// - Dictionary/HashSet keys (IEqualityComparer)
/// - Sorted collections (IComparer)
/// Uses the struct's optimised equality and comparison implementations.
/// </summary>
/// <remarks>
/// Use this where an <see cref="IEqualityComparer{T}"/> / <see cref="IComparer{T}"/> instance is required by an API.
/// For <c>Dictionary</c>, <c>HashSet</c> and <c>Array.Sort</c> prefer the default comparer (omit the argument):
/// the struct implements <see cref="IEquatable{T}"/> and <see cref="IComparable{T}"/>, so the runtime devirtualises and
/// inlines it. Benchmarks show lookups are equal either way, but sorting with an explicit comparer instance is
/// measurably slower because it goes through interface dispatch.
/// </remarks>
public sealed class Int256Comparer : IEqualityComparer<int256>, IComparer<int256>
{
    /// <summary>
    /// Singleton instance to avoid allocations in hot paths.
    /// </summary>
    public static readonly Int256Comparer Instance = new Int256Comparer();

    private Int256Comparer() { }

    /// <summary>
    /// Returns whether two values are equal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(int256 x, int256 y) => x.Equals(y);

    /// <summary>
    /// Returns a hash code for the specified value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetHashCode(int256 obj) => obj.GetHashCode();

    /// <summary>
    /// Compares two values numerically (signed, 256-bit).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Compare(int256 x, int256 y) => x.CompareTo(y);

    /// <summary>
    /// Hot-path helper that avoids copying operands at call sites that can use <c>in</c>.
    /// This does not satisfy interface contracts but is useful inside tight loops.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Equals(in int256 x, in int256 y) => x.Equals(y);

    /// <summary>
    /// Hot-path helper that avoids copying operands at call sites that can use <c>in</c>.
    /// This does not satisfy interface contracts but is useful inside tight loops.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Compare(in int256 x, in int256 y) => x.CompareTo(y);
}
