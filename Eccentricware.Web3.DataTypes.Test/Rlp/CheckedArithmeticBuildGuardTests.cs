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

    [TestMethod]
    public void TheProbe_DoesNotThrow_ForOrdinaryValues()
    {
        Assert.AreEqual(1, RLPCheckedArithmeticProbe.AddOneInProjectArithmeticContext(0));
        Assert.AreEqual(0, RLPCheckedArithmeticProbe.AddOneInProjectArithmeticContext(-1));
        Assert.AreEqual(int.MaxValue, RLPCheckedArithmeticProbe.AddOneInProjectArithmeticContext(int.MaxValue - 1));
    }
}
