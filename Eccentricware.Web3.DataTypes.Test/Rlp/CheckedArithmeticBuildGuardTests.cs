using EccentricWare.Web3.DataTypes.Rlp;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>
/// Stops the checked-arithmetic CI job from degrading into a no-op. The opt-in switch (<c>-p:RlpCheckedArithmetic=true</c>,
/// see <c>Directory.Build.props</c>) only takes effect when the library is compiled; a stale build, a missing
/// <c>--no-incremental</c> or a typo in the property leaves an unchecked assembly and every RLP test still passes. When the
/// job sets <c>RLP_EXPECT_CHECKED_BUILD=1</c> this test fails unless the library really emits overflow checks.
/// </summary>
[TestClass]
public sealed class CheckedArithmeticBuildGuardTests
{
    private const string ExpectCheckedBuildVariable = "RLP_EXPECT_CHECKED_BUILD";

    private static bool ProbeOverflows(out int wrappedValue)
    {
        try
        {
            wrappedValue = RLPCheckedArithmeticProbe.AddOneInProjectArithmeticContext(int.MaxValue);
            return false;
        }
        catch (OverflowException)
        {
            wrappedValue = 0;
            return true;
        }
    }

    [TestMethod]
    public void LibraryIsCompiledChecked_WhenTheEnvironmentDemandsIt()
    {
        if (Environment.GetEnvironmentVariable(ExpectCheckedBuildVariable) != "1")
        {
            // Normal builds are unchecked on purpose and nothing is asserted about the arithmetic context.
            return;
        }

        Assert.IsTrue(
            ProbeOverflows(out _),
            $"{ExpectCheckedBuildVariable}=1 but int.MaxValue + 1 compiled in the library did not throw OverflowException: the library was NOT built " +
            "with CheckForOverflowUnderflow. Build with -p:RlpCheckedArithmetic=true and --no-incremental (never --no-build against an earlier unchecked build).");
    }

    [TestMethod]
    public void TheProbe_IsEitherAWrapToMinValue_OrAnOverflow_NeverAnythingElse()
    {
        if (ProbeOverflows(out int wrapped))
        {
            return; // checked build
        }

        Assert.AreEqual(int.MinValue, wrapped, "an unchecked build wraps int.MaxValue + 1 to int.MinValue");
    }

    /// <summary>
    /// Regression: <c>Hash256.Compute</c> wraps by design, and before it was marked <c>unchecked</c> every hash of these types threw
    /// <see cref="OverflowException"/> in the checked build, so dictionaries keyed by them failed. With random per-process keys the
    /// keyed sum and the final multiply overflow for essentially every input, so several keys make a missed throw impossible in practice.
    /// </summary>
    [TestMethod]
    public void HashesOfTheLimbTypes_DoNotThrow_InEitherArithmeticContext()
    {
        byte[] allOnes = [.. Enumerable.Repeat((byte)0xff, 32)];
        var uints = new HashSet<uint256> { uint256.Zero, uint256.One, uint256.MaxValue, new(0x0123_4567_89ab_cdefUL, ulong.MaxValue, 1UL, 0x8000_0000_0000_0000UL) };
        var ints = new HashSet<int256> { int256.MinValue, int256.MaxValue, int256.MinusOne, int256.Zero };
        var hashes = new HashSet<Hash32> { Hash32.Zero, new(allOnes), new(ulong.MaxValue, 0UL, ulong.MaxValue, 0UL) };
        var addresses = new HashSet<Address> { Address.ZeroEvm, Address.FromEvmBytes(allOnes.AsSpan(0, 20)), Address.ZeroSolana };

        Assert.AreEqual(4, uints.Count);
        Assert.AreEqual(4, ints.Count);
        Assert.AreEqual(3, hashes.Count);
        Assert.AreEqual(3, addresses.Count);
        Assert.IsTrue(uints.Contains(uint256.MaxValue) && ints.Contains(int256.MinValue) && hashes.Contains(new Hash32(allOnes))
            && addresses.Contains(Address.FromEvmBytes(allOnes.AsSpan(0, 20))));
    }

    [TestMethod]
    public void TheProbe_DoesNotThrow_ForOrdinaryValues()
    {
        Assert.AreEqual(1, RLPCheckedArithmeticProbe.AddOneInProjectArithmeticContext(0));
        Assert.AreEqual(0, RLPCheckedArithmeticProbe.AddOneInProjectArithmeticContext(-1));
        Assert.AreEqual(int.MaxValue, RLPCheckedArithmeticProbe.AddOneInProjectArithmeticContext(int.MaxValue - 1));
    }
}
