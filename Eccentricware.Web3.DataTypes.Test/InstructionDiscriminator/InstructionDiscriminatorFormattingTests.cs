using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Exact-size and boundary behaviour of ToString/TryFormat (chars and UTF-8); digit and prefix content is covered by the BCL-oracle tests in InstructionDiscriminatorTests.</summary>
[TestClass]
public sealed class InstructionDiscriminatorFormattingTests
{
    private static readonly InstructionDiscriminator Value = new(0xafaf6d1f0d989bedUL);

    [TestMethod]
    [DataRow(null, 18)]
    [DataRow("", 18)]
    [DataRow("0x", 18)]
    [DataRow("0X", 18)]
    [DataRow("x", 16)]
    [DataRow("X", 16)]
    public void TryFormat_ExactRequiredLength_Succeeds_OneShorter_FailsLeavingTheDestinationUntouched(string? format, int required)
    {
        char[] chars = new char[required];
        byte[] utf8 = new byte[required];
        Assert.IsTrue(Value.TryFormat(chars, out int c, format), "chars exact");
        Assert.IsTrue(Value.TryFormat(utf8, out int b, format), "utf8 exact");
        Assert.AreEqual(required, c);
        Assert.AreEqual(required, b);

        char[] shortChars = new char[required - 1];
        byte[] shortUtf8 = new byte[required - 1];
        shortChars.AsSpan().Fill('#');
        shortUtf8.AsSpan().Fill((byte)'#');
        Assert.IsFalse(Value.TryFormat(shortChars, out c, format), "chars short");
        Assert.IsFalse(Value.TryFormat(shortUtf8, out b, format), "utf8 short");
        Assert.AreEqual(0, c);
        Assert.AreEqual(0, b);
        Assert.IsTrue(shortChars.All(ch => ch == '#'), "chars destination untouched");
        Assert.IsTrue(shortUtf8.All(ch => ch == (byte)'#'), "utf8 destination untouched");
    }

    [TestMethod]
    public void TryFormat_EmptyDestination_ReturnsFalse_ForEveryFormat()
    {
        foreach (string format in new[] { "", "x", "X", "0x", "0X" })
        {
            Assert.IsFalse(Value.TryFormat(Span<char>.Empty, out int c, format), format);
            Assert.IsFalse(Value.TryFormat(Span<byte>.Empty, out int b, format), format);
            Assert.AreEqual(0, c);
            Assert.AreEqual(0, b);
        }
    }

    [TestMethod]
    public void TryFormat_UnknownFormat_Throws_EvenWhenTheDestinationIsTooSmall()
    {
        Assert.ThrowsExactly<FormatException>(() => Value.TryFormat(new char[1], out _, "G"));
        Assert.ThrowsExactly<FormatException>(() => Value.TryFormat(new byte[1], out _, "G"));
        Assert.ThrowsExactly<FormatException>(() => Value.TryFormat(Span<char>.Empty, out _, "bad"));
        Assert.ThrowsExactly<FormatException>(() => Value.TryFormat(Span<byte>.Empty, out _, "bad"));
    }

    [TestMethod]
    public void ToString_NullFormat_DefaultsToPrefixedLowercase_AndMatchesTheParameterlessOverload()
    {
        Assert.AreEqual("0xafaf6d1f0d989bed", Value.ToString());
        Assert.AreEqual(Value.ToString(), Value.ToString(null));
        Assert.AreEqual(Value.ToString(), Value.ToString(""));
        Assert.AreEqual(18, Value.ToString().Length);
    }

    [TestMethod]
    public void ToString_PadsToSixteenDigits_ForSmallValues()
    {
        Assert.AreEqual("0x0000000000000001", new InstructionDiscriminator(1).ToString());
        Assert.AreEqual("000000000000000A", new InstructionDiscriminator(10).ToString("X"));
        Assert.AreEqual("0x0000000100000000", new InstructionDiscriminator(0x100000000UL).ToString("0x"));
    }

    [TestMethod]
    public void TryFormat_PrefixIsAlwaysLowercase_ForUppercaseDigits()
    {
        char[] chars = new char[18];
        byte[] utf8 = new byte[18];

        Assert.IsTrue(Value.TryFormat(chars, out _, "0X"));
        Assert.IsTrue(Value.TryFormat(utf8, out _, "0X"));
        Assert.AreEqual("0xAFAF6D1F0D989BED", new string(chars));
        Assert.AreEqual("0xAFAF6D1F0D989BED", Encoding.ASCII.GetString(utf8));
    }
}
