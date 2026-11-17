using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>
/// Reports how this assembly was compiled, so a test can prove an opt-in checked build (<c>RlpCheckedArithmetic=true</c> in
/// <c>Directory.Build.props</c>) really emitted overflow checks and did not silently reuse stale unchecked binaries.
/// </summary>
internal static class RLPCheckedArithmeticProbe
{
    /// <summary>
    /// Adds one with no <c>checked</c> or <c>unchecked</c> keyword, so the result follows the project's
    /// <c>CheckForOverflowUnderflow</c> setting: <see cref="int.MinValue"/> when the assembly is unchecked (the default),
    /// <see cref="OverflowException"/> when it is checked. Not inlined, so the test calls the compiled library method.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int AddOneInProjectArithmeticContext(int value) => value + 1;
}
