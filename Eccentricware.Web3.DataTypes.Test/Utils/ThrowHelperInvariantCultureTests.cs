using System.Globalization;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using EccentricWare.Web3.DataTypes.JsonConverters;
using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Exception messages must not depend on the thread's culture (the size limits used <c>:N0</c>, which prints a different group
/// separator in many cultures), and every throw helper must tell the compiler it never returns.
/// </summary>
[TestClass]
public sealed class ThrowHelperInvariantCultureTests
{
    private static CultureInfo CultureWithOddGrouping()
    {
        // A writable clone of the invariant culture with a visibly different group separator and digit grouping.
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberGroupSeparator = "'";
        culture.NumberFormat.NumberGroupSizes = [2];
        return culture;
    }

    private static string MessageUnder(CultureInfo culture, Action throwing)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return Assert.ThrowsExactly<System.Text.Json.JsonException>(throwing).Message;
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void TokenTooLargeMessage_UsesInvariantGrouping_UnderAnyCulture()
    {
        string odd = MessageUnder(CultureWithOddGrouping(), () => ThrowHelper.ThrowJsonExceptionTokenTooLarge("Hash32", 1_234_567));
        string invariant = MessageUnder(CultureInfo.InvariantCulture, () => ThrowHelper.ThrowJsonExceptionTokenTooLarge("Hash32", 1_234_567));

        Assert.AreEqual("The JSON string token for Hash32 exceeds the 1,234,567-byte limit.", odd);
        Assert.AreEqual(invariant, odd);
    }

    [TestMethod]
    public void KeyTooLargeMessage_UsesInvariantGrouping_UnderAnyCulture()
    {
        string odd = MessageUnder(CultureWithOddGrouping(), () => ThrowHelper.ThrowJsonExceptionKeyTooLarge("HexBytes", 4096));

        Assert.AreEqual("A HexBytes dictionary key exceeds the 4,096-byte limit.", odd);
    }

    [TestMethod]
    public void HexBytesJsonConverter_ConstructorMessage_UsesInvariantGrouping_UnderAnyCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureWithOddGrouping();
        try
        {
            ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HexBytesJsonConverter(0));

            StringAssert.Contains(error.Message, "The limit must be between 1 and 268,435,456.");
            Assert.AreEqual("maxEscapedTokenBytes", error.ParamName);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void EveryThrowHelper_IsMarkedDoesNotReturn()
    {
        MethodInfo[] helpers = typeof(ThrowHelper).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.IsGreaterThan(20, helpers.Length, "the reflection scan must find the helpers");

        string[] unmarked = helpers
            .Where(m => m.GetCustomAttribute<DoesNotReturnAttribute>() is null)
            .Select(m => m.Name)
            .ToArray();

        CollectionAssert.AreEqual(Array.Empty<string>(), unmarked, "helpers missing [DoesNotReturn]: " + string.Join(", ", unmarked));
    }
}
