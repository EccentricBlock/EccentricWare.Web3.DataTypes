using EccentricWare.Web3.DataTypes.Utils;
using System.Reflection;
using System.Runtime.Intrinsics.X86;

namespace EccentricWare.Web3.DataTypes.Tests;

internal delegate int IndexOfImplementation(ReadOnlySpan<uint256> haystack, in uint256 needle);

internal delegate int FindAllEqualsImplementation(ReadOnlySpan<uint256> haystack, in uint256 needle, Span<int> destinationIndices);

/// <summary>One complete implementation of the three bulk operations (CountEquals shares the IndexOf shape).</summary>
internal sealed record UInt256BulkImplementation(
    string Name,
    IndexOfImplementation IndexOf,
    IndexOfImplementation CountEquals,
    FindAllEqualsImplementation FindAllEquals);

/// <summary>
/// The implementations behind <see cref="UInt256BulkSimd"/>. The public API picks AVX2 or scalar at run time, so the private
/// implementations are bound by reflection to test both on any machine that supports them. Delegates are used because the
/// parameters are ref structs, which MethodInfo.Invoke cannot box.
/// </summary>
internal static class UInt256BulkSimdImplementations
{
    public static readonly string[] PrivateMethodNames =
    [
        "IndexOfScalar", "CountEqualsScalar", "FindAllEqualsScalar",
        "IndexOfAvx2", "CountEqualsAvx2", "FindAllEqualsAvx2",
    ];

    public static readonly UInt256BulkImplementation Public = new(
        "Public",
        (ReadOnlySpan<uint256> h, in uint256 n) => UInt256BulkSimd.IndexOf(h, in n),
        (ReadOnlySpan<uint256> h, in uint256 n) => UInt256BulkSimd.CountEquals(h, in n),
        (ReadOnlySpan<uint256> h, in uint256 n, Span<int> d) => UInt256BulkSimd.FindAllEquals(h, in n, d));

    public static readonly UInt256BulkImplementation Scalar = Bind("Scalar");

    /// <summary>The AVX2 implementation, or null when this machine lacks AVX2 (calling it would throw).</summary>
    public static readonly UInt256BulkImplementation? Avx2Implementation = Avx2.IsSupported ? Bind("Avx2") : null;

    /// <summary>Every implementation this machine can run.</summary>
    public static IEnumerable<UInt256BulkImplementation> Supported()
    {
        yield return Public;
        yield return Scalar;
        if (Avx2Implementation is not null)
            yield return Avx2Implementation;
    }

    public static MethodInfo? Find(string name)
        => typeof(UInt256BulkSimd).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);

    private static UInt256BulkImplementation Bind(string suffix) => new(
        suffix,
        CreateDelegate<IndexOfImplementation>("IndexOf" + suffix),
        CreateDelegate<IndexOfImplementation>("CountEquals" + suffix),
        CreateDelegate<FindAllEqualsImplementation>("FindAllEquals" + suffix));

    private static T CreateDelegate<T>(string methodName) where T : Delegate
    {
        MethodInfo method = Find(methodName)
            ?? throw new InvalidOperationException($"UInt256BulkSimd.{methodName} was renamed or removed; update UInt256BulkSimdImplementations.");
        return (T)Delegate.CreateDelegate(typeof(T), method);
    }
}
