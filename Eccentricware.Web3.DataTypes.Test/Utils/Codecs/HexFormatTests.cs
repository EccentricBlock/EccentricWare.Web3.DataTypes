using EccentricWare.Web3.DataTypes.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Direct tests of <see cref="HexFormat.Parse"/>, the hex format-specifier parser shared by the fixed-width types.</summary>
[TestClass]
public class HexFormatTests
{
    private static readonly string[] s_validSpecifiers = ["", "x", "X", "0x", "0X"];

    [TestMethod]
    [DataRow("", true, false)]
    [DataRow("x", false, false)]
    [DataRow("X", false, true)]
    [DataRow("0x", true, false)]
    [DataRow("0X", true, true)]
    public void Parse_ValidSpecifier_SetsPrefixAndCaseFlags(string format, bool expectedPrefix, bool expectedUppercase)
    {
        HexFormat.Parse(format, out bool includePrefix, out bool uppercase);

        Assert.AreEqual(expectedPrefix, includePrefix);
        Assert.AreEqual(expectedUppercase, uppercase);
    }

    [TestMethod]
    public void Parse_DefaultAndNullDerivedSpans_BehaveLikeEmptyFormat()
    {
        HexFormat.Parse(default, out bool prefixDefault, out bool upperDefault);
        HexFormat.Parse(((string?)null).AsSpan(), out bool prefixNull, out bool upperNull);

        Assert.IsTrue(prefixDefault);
        Assert.IsFalse(upperDefault);
        Assert.IsTrue(prefixNull);
        Assert.IsFalse(upperNull);
    }

    [TestMethod]
    public void Parse_SliceOfLongerString_UsesOnlyTheSlice()
    {
        HexFormat.Parse("..0X..".AsSpan(2, 2), out bool includePrefix, out bool uppercase);

        Assert.IsTrue(includePrefix);
        Assert.IsTrue(uppercase);
    }

    [TestMethod]
    [DataRow("G")]
    [DataRow("g")]
    [DataRow("d")]
    [DataRow("D")]
    [DataRow("N")]
    [DataRow("x8")]
    [DataRow("X8")]
    [DataRow("0")]
    [DataRow("0x0")]
    [DataRow("0xx")]
    [DataRow("0xX")]
    [DataRow("0Xx")]
    [DataRow("0XX")]
    [DataRow("xX")]
    [DataRow("Xx")]
    [DataRow("xx")]
    [DataRow("XX")]
    [DataRow(" x")]
    [DataRow("x ")]
    [DataRow("0 x")]
    [DataRow(" ")]
    [DataRow("0y")]
    [DataRow("00")]
    [DataRow("1x")]
    [DataRow("x0")]
    [DataRow("x\0")]
    [DataRow("х")] // Cyrillic small ha, looks like 'x'
    [DataRow("0х")]
    [DataRow("ｘ")] // fullwidth 'x'
    public void Parse_UnknownSpecifier_ThrowsFormatExceptionNamingIt(string format)
    {
        var ex = Assert.ThrowsExactly<FormatException>(() => HexFormat.Parse(format.AsSpan(), out _, out _));

        Assert.AreEqual($"Unknown format: \"{format}\"", ex.Message);
    }

    [TestMethod]
    public void Parse_VeryLongSpecifier_ThrowsFormatException()
    {
        string format = new string('x', 1000);

        var ex = Assert.ThrowsExactly<FormatException>(() => HexFormat.Parse(format.AsSpan(), out _, out _));

        StringAssert.Contains(ex.Message, format);
    }

    [TestMethod]
    public void Parse_EveryStringUpToLengthThreeOverNearMissAlphabet_AcceptsExactlyTheFiveValidSpecifiers()
    {
        string[] alphabet = ["0", "x", "X", "8", " ", "G"];
        var candidates = new List<string> { "" };
        for (int length = 1; length <= 3; length++)
            candidates.AddRange(candidates.Where(c => c.Length == length - 1).SelectMany(c => alphabet.Select(a => c + a)).ToList());

        Assert.HasCount(1 + 6 + 36 + 216, candidates);
        foreach (string candidate in candidates)
        {
            bool expectedValid = s_validSpecifiers.Contains(candidate);
            bool threw = false;
            try
            {
                HexFormat.Parse(candidate.AsSpan(), out bool prefix, out bool upper);
                Assert.AreEqual(candidate.StartsWith('0') || candidate.Length == 0, prefix, candidate);
                Assert.AreEqual(candidate.EndsWith('X'), upper, candidate);
            }
            catch (FormatException)
            {
                threw = true;
            }

            Assert.AreEqual(!expectedValid, threw, $"\"{candidate}\"");
        }
    }
}
