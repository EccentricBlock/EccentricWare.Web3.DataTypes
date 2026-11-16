using EccentricWare.Web3.DataTypes.Utils;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Contract of every <see cref="ThrowHelper"/> method: exact exception type, ParamName, message text, and that it never
/// returns normally. Expected messages are literals (not the library constants) so a changed constant is a visible failure.
/// A reflection guard fails when a helper exists without a row, so a new helper forces a new test row.
/// </summary>
[TestClass]
public sealed class ThrowHelperTests
{
    private const string Param = "p";

    // Helper name, arguments, exact exception type, expected ParamName, expected message. ThrowFormatExceptionUnknownFormat
    // takes a ref struct (cannot go through reflection) and ThrowJsonExceptionUnexpectedToken has a matrix: both have own tests.
    private static readonly (string Helper, object?[] Args, Type Exception, string? ParamName, string Message)[] Contracts =
    [
        (nameof(ThrowHelper.ThrowArgumentNullException), [Param], typeof(ArgumentNullException), Param, "Value cannot be null."),
        (nameof(ThrowHelper.ThrowArgumentExceptionInvalidFunctionSelectorLength), [Param], typeof(ArgumentException), Param, "Function selector must be at least 4 bytes."),
        (nameof(ThrowHelper.ThrowArgumentExceptionInvalidDiscriminatorLength), [Param], typeof(ArgumentException), Param, "Instruction discriminator must be at least 8 bytes."),
        (nameof(ThrowHelper.ThrowArgumentExceptionDestinationTooSmall), [Param], typeof(ArgumentException), Param, "Destination buffer is too small."),
        (nameof(ThrowHelper.ThrowArgumentExceptionWrongType), [Param, "Hash32"], typeof(ArgumentException), Param, "Object must be of type Hash32."),
        (nameof(ThrowHelper.ThrowArgumentExceptionFixedSize), [Param, 32], typeof(ArgumentException), Param, "Destination must be exactly 32 elements long."),
        (nameof(ThrowHelper.ThrowFormatExceptionInvalidFunctionSelector), [], typeof(FormatException), null, "Invalid function selector hex value."),
        (nameof(ThrowHelper.ThrowFormatExceptionInvalidDiscriminator), [], typeof(FormatException), null, "Invalid instruction discriminator hex value."),
        (nameof(ThrowHelper.ThrowJsonExceptionTokenTooLarge), ["Hash32", 1024], typeof(JsonException), null, "The JSON string token for Hash32 exceeds the 1,024-byte limit."),
        (nameof(ThrowHelper.ThrowJsonExceptionKeyTooLarge), ["HexBytes", 4096], typeof(JsonException), null, "A HexBytes dictionary key exceeds the 4,096-byte limit."),
        (nameof(ThrowHelper.ThrowJsonExceptionInvalidValue), ["Address"], typeof(JsonException), null, "Invalid value for Address."),
        (nameof(ThrowHelper.ThrowJsonExceptionFormatFailed), ["Hash32"], typeof(JsonException), null, "Failed to format Hash32."),
        (nameof(ThrowHelper.ThrowFormatExceptionInvalidBase64), [], typeof(FormatException), null, "Invalid Base64 encoded value."),
        (nameof(ThrowHelper.ThrowFormatExceptionInvalidAddress), [], typeof(FormatException), null, "Invalid blockchain address format"),
        (nameof(ThrowHelper.ThrowArgumentExceptionInvalidLength), [Param], typeof(ArgumentException), Param, "Hash32 requires exactly 32 bytes"),
        (nameof(ThrowHelper.ThrowFormatExceptionInvalidHexLength), [], typeof(FormatException), null, "Hash32 requires exactly 64 hex characters (32 bytes)"),
        (nameof(ThrowHelper.ThrowFormatExceptionInvalidHex), [], typeof(FormatException), null, "Invalid hex character"),
        (nameof(ThrowHelper.ThrowArgumentExceptionInvalidEvmLength), [Param], typeof(ArgumentException), Param, "EVM address requires exactly 20 bytes"),
        (nameof(ThrowHelper.ThrowArgumentExceptionInvalidSolanaLength), [Param], typeof(ArgumentException), Param, "Solana address requires exactly 32 bytes"),
        (nameof(ThrowHelper.ThrowFormatExceptionInvalidBase58), [], typeof(FormatException), null, "Invalid Base58 string for Solana address"),
        (nameof(ThrowHelper.ThrowArgumentExceptionInvalidSignatureRLength), [Param], typeof(ArgumentException), Param, "Signature r component requires exactly 32 bytes"),
        (nameof(ThrowHelper.ThrowArgumentExceptionInvalidSignatureSLength), [Param], typeof(ArgumentException), Param, "Signature s component requires exactly 32 bytes"),
        (nameof(ThrowHelper.ThrowArgumentExceptionInvalidEvmSignatureLength), [Param], typeof(ArgumentException), Param, "EVM signature requires exactly 65 bytes"),
        (nameof(ThrowHelper.ThrowArgumentExceptionInvalidSolanaSignatureLength), [Param], typeof(ArgumentException), Param, "Solana signature requires exactly 64 bytes"),
        (nameof(ThrowHelper.ThrowFormatExceptionInvalidSignature), [], typeof(FormatException), null,
            "Invalid signature: expected hex with an optional 0x prefix (130 digits for EVM, 128 for Solana) or Solana Base58 without a prefix"),
        (nameof(ThrowHelper.ThrowInvalidOperationExceptionBase58RequiresSolanaSignature), [], typeof(InvalidOperationException), null,
            "Base58 is only defined for Solana signatures; this is an EVM signature."),
    ];

    // Helpers verified by dedicated tests below instead of a Contracts row.
    private static readonly string[] HelpersWithDedicatedTests =
    [
        nameof(ThrowHelper.ThrowFormatExceptionUnknownFormat),
        nameof(ThrowHelper.ThrowJsonExceptionUnexpectedToken),
    ];

    public static IEnumerable<object[]> ContractRows()
    {
        foreach (var c in Contracts)
            yield return [c.Helper];
    }

    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static MethodInfo[] Helpers() => typeof(ThrowHelper).GetMethods(AllDeclared);

    /// <summary>Runs <paramref name="call"/> and returns what it threw; fails if it returned normally.</summary>
    private static Exception Thrown(string helper, Action call)
    {
        Exception? thrown = null;
        try { call(); }
        catch (Exception ex) { thrown = ex; }
        return thrown ?? throw new AssertFailedException($"{helper} returned normally; a throw helper must never return.");
    }

    private static Exception InvokeHelper(string helper, object?[] args)
    {
        MethodInfo method = typeof(ThrowHelper).GetMethod(helper, BindingFlags.Public | BindingFlags.Static)
            ?? throw new AssertFailedException($"No public static ThrowHelper.{helper}.");
        Exception wrapper = Thrown(helper, () => method.Invoke(null, args));
        Assert.IsInstanceOfType<TargetInvocationException>(wrapper, helper);
        return wrapper.InnerException!;
    }

    private static T WithInvariantCulture<T>(Func<T> action)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ---- registry guards -------------------------------------------------------------------------------------------

    [TestMethod]
    public void EveryHelperMethod_HasAContractRowOrDedicatedTest_AndNothingStale()
    {
        string[] actual = Helpers().Select(m => m.Name).Order(StringComparer.Ordinal).ToArray();
        string[] covered = Contracts.Select(c => c.Helper).Concat(HelpersWithDedicatedTests).Order(StringComparer.Ordinal).ToArray();

        CollectionAssert.AreEqual(actual, covered,
            "ThrowHelper methods and test rows differ: add a row for each new helper and remove rows of deleted helpers.");
    }

    [TestMethod]
    public void ContractRows_HaveNoDuplicates_AndDedicatedHelpersAreNotAlsoRows()
    {
        Assert.HasCount(Contracts.Length, Contracts.Select(c => c.Helper).Distinct());
        Assert.IsEmpty(Contracts.Select(c => c.Helper).Intersect(HelpersWithDedicatedTests));
    }

    [TestMethod]
    public void EveryHelper_IsPublicStaticVoid_AndTheClassHasNoOtherMembers()
    {
        Assert.IsTrue(typeof(ThrowHelper).IsAbstract && typeof(ThrowHelper).IsSealed, "ThrowHelper must stay a static class.");
        foreach (MethodInfo m in Helpers())
        {
            Assert.IsTrue(m.IsPublic && m.IsStatic, m.Name);
            Assert.AreEqual(typeof(void), m.ReturnType, m.Name);
            Assert.StartsWith("Throw", m.Name);
        }

        Assert.IsEmpty(typeof(ThrowHelper).GetFields(AllDeclared));
        Assert.IsEmpty(typeof(ThrowHelper).GetProperties(AllDeclared));
    }

    [TestMethod]
    public void EveryContractRow_SuppliesAnArgumentForEachParameter()
    {
        foreach (var c in Contracts)
        {
            ParameterInfo[] parameters = typeof(ThrowHelper).GetMethod(c.Helper)!.GetParameters();
            Assert.HasCount(parameters.Length, c.Args, c.Helper);
            for (int i = 0; i < parameters.Length; i++)
                Assert.IsTrue(parameters[i].ParameterType.IsInstanceOfType(c.Args[i]), $"{c.Helper} argument {i}");
        }
    }

    // ---- per-helper contract ---------------------------------------------------------------------------------------

    [TestMethod]
    [DynamicData(nameof(ContractRows))]
    public void Helper_ThrowsExactExceptionWithParamNameAndMessage(string helper)
    {
        var contract = Contracts.Single(c => c.Helper == helper);

        Exception ex = WithInvariantCulture(() => InvokeHelper(helper, contract.Args));

        Assert.AreEqual(contract.Exception, ex.GetType(), $"{helper}: exception type");
        Assert.IsNull(ex.InnerException, helper);
        if (ex is ArgumentException argument)
        {
            Assert.AreEqual(contract.ParamName, argument.ParamName, $"{helper}: ParamName");
            // The BCL appends a " (Parameter 'x')" suffix; its wording comes from a localisable resource, so the
            // library-defined prefix is the primary assertion.
            Assert.StartsWith(contract.Message, argument.Message, $"{helper}: message");
        }
        else
        {
            Assert.IsNull(contract.ParamName, helper);
            Assert.AreEqual(contract.Message, ex.Message, $"{helper}: message");
        }
    }

    // ---- ThrowFormatExceptionUnknownFormat (ReadOnlySpan<char> parameter: called directly) -------------------------------

    [TestMethod]
    [DataRow("zz", "Unknown format: \"zz\"")]
    [DataRow("", "Unknown format: \"\"")]
    [DataRow("a\"b", "Unknown format: \"a\"b\"")]
    [DataRow("\n", "Unknown format: \"\n\"")]
    [DataRow("{0}", "Unknown format: \"{0}\"")]
    [DataRow("{", "Unknown format: \"{\"")]
    public void ThrowFormatExceptionUnknownFormat_QuotesTheFormatVerbatim(string format, string expectedMessage)
    {
        Exception ex = Thrown(nameof(ThrowHelper.ThrowFormatExceptionUnknownFormat),
            () => ThrowHelper.ThrowFormatExceptionUnknownFormat(format));

        Assert.AreEqual(typeof(FormatException), ex.GetType());
        Assert.AreEqual(expectedMessage, ex.Message);
        Assert.IsNull(ex.InnerException);
    }

    [TestMethod]
    public void ThrowFormatExceptionUnknownFormat_AcceptsASliceOfALargerString()
    {
        Exception ex = Thrown(nameof(ThrowHelper.ThrowFormatExceptionUnknownFormat),
            () => ThrowHelper.ThrowFormatExceptionUnknownFormat("xxQyy".AsSpan(2, 1)));

        Assert.AreEqual("Unknown format: \"Q\"", ex.Message);
    }

    // ---- ThrowJsonExceptionUnexpectedToken: full JsonTokenType x numberAllowed matrix --------------------------------

    [TestMethod]
    public void ThrowJsonExceptionUnexpectedToken_MatrixCoversEveryTokenType()
    {
        JsonTokenType[] tokens = Enum.GetValues<JsonTokenType>();
        Assert.Contains(JsonTokenType.Null, tokens);
        Assert.Contains(JsonTokenType.String, tokens);

        foreach (JsonTokenType token in tokens)
        {
            foreach (bool numberAllowed in new[] { false, true })
            {
                string expected = token == JsonTokenType.Null
                    ? "Cannot read null into uint256."
                    : numberAllowed
                        ? "Expected JSON string or number for uint256."
                        : "Expected JSON string for uint256.";

                Exception ex = Thrown(nameof(ThrowHelper.ThrowJsonExceptionUnexpectedToken),
                    () => ThrowHelper.ThrowJsonExceptionUnexpectedToken("uint256", token, numberAllowed));

                Assert.AreEqual(typeof(JsonException), ex.GetType(), $"{token}/{numberAllowed}");
                Assert.AreEqual(expected, ex.Message, $"{token}/{numberAllowed}");
                Assert.IsNull(ex.InnerException, $"{token}/{numberAllowed}");
            }
        }
    }

    [TestMethod]
    public void ThrowJsonExceptionUnexpectedToken_UndefinedTokenValue_IsTreatedAsNonNull()
    {
        Exception ex = Thrown(nameof(ThrowHelper.ThrowJsonExceptionUnexpectedToken),
            () => ThrowHelper.ThrowJsonExceptionUnexpectedToken("Hash32", (JsonTokenType)255, numberAllowed: true));

        Assert.AreEqual("Expected JSON string or number for Hash32.", ex.Message);
    }

    // ---- culture-dependent :N0 messages ------------------------------------------------------------------------------

    [TestMethod]
    [DataRow(0, "0")]
    [DataRow(1, "1")]
    [DataRow(999, "999")]
    [DataRow(1000, "1,000")]
    [DataRow(1048576, "1,048,576")]
    [DataRow(int.MaxValue, "2,147,483,647")]
    [DataRow(-1, "-1")]
    [DataRow(int.MinValue, "-2,147,483,648")]
    public void LimitMessages_FormatTheNumberWithGroupSeparators_UnderInvariantCulture(int limit, string formatted)
    {
        Exception token = WithInvariantCulture(() => Thrown("TokenTooLarge", () => ThrowHelper.ThrowJsonExceptionTokenTooLarge("Hash32", limit)));
        Exception key = WithInvariantCulture(() => Thrown("KeyTooLarge", () => ThrowHelper.ThrowJsonExceptionKeyTooLarge("Hash32", limit)));

        Assert.AreEqual($"The JSON string token for Hash32 exceeds the {formatted}-byte limit.", token.Message);
        Assert.AreEqual($"A Hash32 dictionary key exceeds the {formatted}-byte limit.", key.Message);
    }

    [TestMethod]
    public void WithInvariantCulture_RestoresTheCallersCulture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo other = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            CultureInfo.CurrentCulture = other;
            WithInvariantCulture(() => Thrown("TokenTooLarge", () => ThrowHelper.ThrowJsonExceptionTokenTooLarge("x", 1)));
            Assert.AreSame(other, CultureInfo.CurrentCulture);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // ---- adversarial arguments ---------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow("")]
    [DataRow("{0}")]
    [DataRow("{")]
    [DataRow("}{")]
    [DataRow("a\0b")]
    [DataRow("line1\r\nline2")]
    public void TypeNameArguments_AreInsertedVerbatim_NeverAsCompositeFormat(string typeName)
    {
        Assert.AreEqual($"Invalid value for {typeName}.", Thrown("i", () => ThrowHelper.ThrowJsonExceptionInvalidValue(typeName)).Message);
        Assert.AreEqual($"Failed to format {typeName}.", Thrown("f", () => ThrowHelper.ThrowJsonExceptionFormatFailed(typeName)).Message);
        Assert.StartsWith($"Object must be of type {typeName}.", Thrown("w", () => ThrowHelper.ThrowArgumentExceptionWrongType("p", typeName)).Message);
    }

    [TestMethod]
    public void NullTypeName_ProducesAnEmptyNameInsteadOfThrowingNullReference()
    {
        Assert.AreEqual("Cannot read null into .", Thrown("u", () => ThrowHelper.ThrowJsonExceptionUnexpectedToken(null!, JsonTokenType.Null, false)).Message);
        Assert.AreEqual("Invalid value for .", Thrown("i", () => ThrowHelper.ThrowJsonExceptionInvalidValue(null!)).Message);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void NullOrEmptyParamName_YieldsNoParameterSuffix(string? paramName)
    {
        var ex = (ArgumentException)Thrown("d", () => ThrowHelper.ThrowArgumentExceptionDestinationTooSmall(paramName!));

        Assert.AreEqual(paramName, ex.ParamName);
        Assert.AreEqual("Destination buffer is too small.", ex.Message);
    }

    [TestMethod]
    public void VeryLongTypeName_IsPreservedInTheMessage()
    {
        string longName = new('T', 100_000);

        Exception ex = Thrown("i", () => ThrowHelper.ThrowJsonExceptionInvalidValue(longName));

        Assert.AreEqual($"Invalid value for {longName}.", ex.Message);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public void FixedSizeMessage_ReportsTheRequiredLengthUnformatted(int length)
    {
        var ex = (ArgumentException)Thrown("f", () => ThrowHelper.ThrowArgumentExceptionFixedSize("destination", length));

        Assert.StartsWith(string.Create(CultureInfo.InvariantCulture, $"Destination must be exactly {length} elements long."), ex.Message);
        Assert.AreEqual("destination", ex.ParamName);
    }

    [TestMethod]
    public void ThrownJsonExceptions_CarryNoReaderContext()
    {
        foreach (string helper in new[] { nameof(ThrowHelper.ThrowJsonExceptionInvalidValue), nameof(ThrowHelper.ThrowJsonExceptionFormatFailed) })
        {
            var ex = (JsonException)InvokeHelper(helper, ["Hash32"]);
            Assert.IsNull(ex.Path, helper);
            Assert.IsNull(ex.LineNumber, helper);
            Assert.IsNull(ex.BytePositionInLine, helper);
        }
    }

    // ---- integration: real callers surface the helper's exception -----------------------------------------------------

    [TestMethod]
    public void ByteUtilsFixedSizeWriter_SurfacesTheFixedSizeContract()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => ByteUtils.WriteHexUInt32CharsFixed8(new char[7], 0, false));

        Assert.AreEqual("destination8", ex.ParamName);
        Assert.StartsWith("Destination must be exactly 8 elements long.", ex.Message);
    }
}
