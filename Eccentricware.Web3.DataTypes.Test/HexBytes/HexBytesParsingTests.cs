using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="HexBytes"/>: <c>Parse</c> / <c>TryParse</c> over chars and UTF-8, including adversarial digits and an oracle fuzz.</summary>
[TestClass]
public sealed class HexBytesParsingTests
{
    private const string HexAlphabet = "0123456789abcdefABCDEF";

    /// <summary>
    /// Independent reference of the documented grammar: trim ASCII whitespace, strip one pair of surrounding quotes, strip a
    /// 0x/0X prefix, require hex digits only, left-pad an odd count with '0'. Decoding is Convert.FromHexString.
    /// </summary>
    private static bool Oracle(string text, out byte[] bytes)
    {
        bytes = [];
        if (text.Length == 0)
            return true;

        string s = text.Trim(' ', '\t', '\r', '\n');
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = s[1..^1];
        if (s.Length >= 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X'))
            s = s[2..];
        if (s.Length == 0)
            return true;
        if (s.Any(c => !HexAlphabet.Contains(c)))
            return false;

        bytes = Convert.FromHexString(s.Length % 2 == 1 ? "0" + s : s);
        return true;
    }

    // ----------------- valid inputs -----------------

    [TestMethod]
    [DataRow("0x0102ab", new byte[] { 0x01, 0x02, 0xAB })]
    [DataRow("0X0102AB", new byte[] { 0x01, 0x02, 0xAB })]
    [DataRow("0102aB", new byte[] { 0x01, 0x02, 0xAB })]
    [DataRow("0x123", new byte[] { 0x01, 0x23 })]       // odd length: left-padded with a zero nibble (documented)
    [DataRow("f", new byte[] { 0x0F })]
    [DataRow("0x", new byte[0])]
    [DataRow("", new byte[0])]
    [DataRow("   ", new byte[0])]
    [DataRow("\"\"", new byte[0])]
    [DataRow("\"0x\"", new byte[0])]
    [DataRow(" 0x ", new byte[0])]
    [DataRow("0x00", new byte[] { 0x00 })]                      // zero bytes are kept, not normalised away
    [DataRow("0x0000", new byte[] { 0x00, 0x00 })]
    [DataRow("0x000102", new byte[] { 0x00, 0x01, 0x02 })]
    [DataRow("0", new byte[] { 0x00 })]
    [DataRow("0x0", new byte[] { 0x00 })]
    [DataRow(" 0xab", new byte[] { 0xAB })]
    [DataRow("0xab ", new byte[] { 0xAB })]
    [DataRow(" \t\r\n0xab\n", new byte[] { 0xAB })]
    [DataRow("\"0xab\"", new byte[] { 0xAB })]
    [DataRow(" \"0xab\" ", new byte[] { 0xAB })]
    [DataRow("\"ab\"", new byte[] { 0xAB })]
    [DataRow("  \t\"0xdeadbeef\"\r\n", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })]
    public void Parse_AcceptsValidInputs_IdenticallyForCharsAndUtf8(string text, byte[] expected)
    {
        Assert.IsTrue(HexBytes.TryParse(text.AsSpan(), out HexBytes fromChars), "chars");
        Assert.IsTrue(HexBytes.TryParse(Encoding.UTF8.GetBytes(text), out HexBytes fromUtf8), "utf8");

        CollectionAssert.AreEqual(expected, fromChars.ToArray());
        CollectionAssert.AreEqual(expected, fromUtf8.ToArray());
        Assert.AreEqual(new HexBytes(expected), HexBytes.Parse(text));
        Assert.AreEqual(new HexBytes(expected), HexBytes.Parse(text.AsSpan()));
    }

    [TestMethod]
    public void ParsedZeroBytes_AreNotEmpty_AndRoundTripWithLeadingZeros()
    {
        HexBytes zero = HexBytes.Parse("0x00");

        Assert.IsFalse(zero.IsEmpty);
        Assert.AreEqual(1, zero.Length);
        Assert.AreNotEqual(HexBytes.Empty, zero);
        Assert.IsGreaterThan(0, zero.CompareTo(HexBytes.Empty));
        Assert.AreEqual("0x00", zero.ToString());
        Assert.AreEqual("0x0000", HexBytes.Parse("0x0000").ToString());
        Assert.AreEqual("0x000102", HexBytes.Parse("0x000102").ToString());
        Assert.AreEqual(HexBytes.Parse("0"), HexBytes.Parse("0x0"));
        Assert.AreEqual(HexBytes.Parse("0x0"), HexBytes.Parse("0x00"));
    }

    // ----------------- invalid inputs -----------------

    [TestMethod]
    [DataRow("\"0xab")]
    [DataRow("0xab\"")]
    [DataRow("\"")]
    [DataRow("\" \"")]
    [DataRow("\" 0xab\"")]       // whitespace is trimmed before unquoting, not inside the quotes
    [DataRow("0x12 34")]
    [DataRow("0x12 ")]      // NBSP is not ASCII whitespace
    [DataRow(" 0x12")]
    [DataRow("0xzz")]
    [DataRow("0x0g")]
    [DataRow("0x0x12")]
    [DataRow("-0x12")]
    [DataRow("0x-12")]
    [DataRow("x12")]
    public void Parse_RejectsInvalidInputs_OnBothPaths(string text)
    {
        Assert.IsFalse(HexBytes.TryParse(text.AsSpan(), out HexBytes fromChars), "chars");
        Assert.IsTrue(fromChars.IsEmpty);
        Assert.IsFalse(HexBytes.TryParse(Encoding.UTF8.GetBytes(text), out HexBytes fromUtf8), "utf8");
        Assert.IsTrue(fromUtf8.IsEmpty);

        Assert.ThrowsExactly<FormatException>(() => HexBytes.Parse(text));
        Assert.ThrowsExactly<FormatException>(() => HexBytes.Parse(text.AsSpan()));
    }

    [TestMethod]
    public void Parse_NullString_ThrowsArgumentNullException()
    {
        ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(() => HexBytes.Parse((string)null!));
        Assert.AreEqual("hex", error.ParamName);
    }

    // ----------------- per-position invalid digits -----------------

    public static IEnumerable<object[]> DigitCounts() =>
        new[] { 1, 2, 3, 7, 15, 16, 17, 31, 32, 33, 69, 70 }.Select(n => new object[] { n });

    [TestMethod]
    [DynamicData(nameof(DigitCounts))]
    public void TryParse_Chars_RejectsAnInvalidDigit_AtEveryPosition_ForOddAndEvenCounts(int digitCount)
    {
        // Odd counts take a separate first-nibble path in the decoder; vector lanes and tails must all reject.
        string valid = new('a', digitCount);
        foreach (string prefix in new[] { "", "0x" })
        {
            for (int position = 0; position < digitCount; position++)
            {
                foreach (char bad in new[] { 'g', 'G', '/', ':', '@', '`', '٠', 'é', '\0', '１' })
                {
                    string text = prefix + valid[..position] + bad + valid[(position + 1)..];
                    Assert.IsFalse(HexBytes.TryParse(text.AsSpan(), out HexBytes result), $"n={digitCount} pos={position} bad=U+{(int)bad:X4} prefix='{prefix}'");
                    Assert.AreEqual(HexBytes.Empty, result);
                }
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(DigitCounts))]
    public void TryParse_Utf8_RejectsAnInvalidByte_AtEveryPosition_ForOddAndEvenCounts(int digitCount)
    {
        byte[] valid = Encoding.ASCII.GetBytes(new string('a', digitCount));
        foreach (string prefix in new[] { "", "0x" })
        {
            byte[] prefixBytes = Encoding.ASCII.GetBytes(prefix);
            for (int position = 0; position < digitCount; position++)
            {
                // Raw bytes: NUL, a continuation byte, a lead byte with no tail, 0xFF, plus ASCII neighbours of the digit ranges.
                foreach (byte bad in new byte[] { 0x00, 0x80, 0xC3, 0xFF, (byte)'g', (byte)'G', (byte)'/', (byte)':', (byte)'@', (byte)'`' })
                {
                    byte[] text = [.. prefixBytes, .. valid[..position], bad, .. valid[(position + 1)..]];
                    Assert.IsFalse(HexBytes.TryParse(text, out HexBytes result), $"n={digitCount} pos={position} bad=0x{bad:X2} prefix='{prefix}'");
                    Assert.AreEqual(HexBytes.Empty, result);
                }

                // Well-formed multi-byte UTF-8 (U+0660, U+00E9) in place of one digit is still not a hex digit.
                foreach (string bad in new[] { "٠", "é" })
                {
                    byte[] text = [.. prefixBytes, .. valid[..position], .. Encoding.UTF8.GetBytes(bad), .. valid[(position + 1)..]];
                    Assert.IsFalse(HexBytes.TryParse(text, out _), $"n={digitCount} pos={position} bad={bad} prefix='{prefix}'");
                }
            }

            // A lone trailing lead byte after otherwise valid digits.
            Assert.IsFalse(HexBytes.TryParse([.. prefixBytes, .. valid, 0xC2], out _), $"n={digitCount} trailing 0xC2 prefix='{prefix}'");
        }
    }

    // ----------------- oracle fuzz -----------------

    [TestMethod]
    public void TryParse_AgreesWithTheOracle_OnRandomValidAndAlmostValidText()
    {
        var random = new Random(20260601);
        const string noise = " \t\r\n\"xXzg/:@` ";

        for (int round = 0; round < 4000; round++)
        {
            var text = new StringBuilder();
            int digits = random.Next(0, 301);
            for (int i = 0; i < digits; i++)
                text.Append(HexAlphabet[random.Next(HexAlphabet.Length)]);

            if (random.Next(2) == 0) text.Insert(0, random.Next(2) == 0 ? "0x" : "0X");
            if (random.Next(4) == 0) { text.Insert(0, '"'); text.Append('"'); }
            if (random.Next(4) == 0) text.Insert(0, new string(' ', random.Next(1, 4)));
            if (random.Next(4) == 0) text.Append("\r\n");
            if (random.Next(15) == 0 && text.Length > 0) text[random.Next(text.Length)] = noise[random.Next(noise.Length)];

            string s = text.ToString();
            bool expectedOk = Oracle(s, out byte[] expected);

            Assert.AreEqual(expectedOk, HexBytes.TryParse(s.AsSpan(), out HexBytes fromChars), $"chars '{s}'");
            Assert.AreEqual(expectedOk, HexBytes.TryParse(Encoding.UTF8.GetBytes(s), out HexBytes fromUtf8), $"utf8 '{s}'");
            if (expectedOk)
            {
                CollectionAssert.AreEqual(expected, fromChars.ToArray(), $"chars '{s}'");
                CollectionAssert.AreEqual(expected, fromUtf8.ToArray(), $"utf8 '{s}'");
            }
        }
    }

    [TestMethod]
    public void TryParse_NeverThrows_OnArbitraryCharsAndBytes()
    {
        var random = new Random(77);

        // Uniformly random UTF-16 almost never forms valid hex, so half of the rounds draw from a mostly-hex alphabet: valid inputs
        // (which must parse, not throw) are then common, as are near-valid ones.
        const string Mostly = "0123456789abcdefABCDEF0123456789abcdefABCDEFxX \"-";

        for (int round = 0; round < 2000; round++)
        {
            char[] chars = new char[random.Next(0, 50)];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = round % 2 == 0 ? (char)random.Next(0, 0x10000) : Mostly[random.Next(Mostly.Length)];

            byte[] bytes = round % 2 == 0 ? new byte[random.Next(0, 50)] : Encoding.ASCII.GetBytes(new string(chars));
            if (round % 2 == 0)
                random.NextBytes(bytes);

            _ = HexBytes.TryParse(chars, out _);
            _ = HexBytes.TryParse(bytes, out _);
        }

        // A valid input must be accepted by both overloads: every length from 1 to 40 digits, with and without the prefix.
        for (int digits = 1; digits <= 40; digits++)
        {
            string text = new string('a', digits);
            Assert.IsTrue(HexBytes.TryParse(text.AsSpan(), out _), text);
            Assert.IsTrue(HexBytes.TryParse(Encoding.ASCII.GetBytes("0x" + text), out _), text);
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(15)]
    [DataRow(16)]
    [DataRow(17)]
    [DataRow(31)]
    [DataRow(32)]
    [DataRow(33)]
    [DataRow(63)]
    [DataRow(64)]
    [DataRow(65)]
    [DataRow(1000)]
    [DataRow(1 << 20)] // 1 MiB
    public void Parse_And_Format_RoundTrip_AcrossLengths(int length)
    {
        byte[] bytes = new byte[length];
        new Random(length).NextBytes(bytes);
        string lower = Convert.ToHexStringLower(bytes);
        string upper = Convert.ToHexString(bytes);

        Assert.AreEqual("0x" + lower, new HexBytes(bytes).ToString());
        Assert.AreEqual(lower, new HexBytes(bytes).ToString("x"));
        Assert.AreEqual(upper, new HexBytes(bytes).ToString("X"));
        Assert.AreEqual("0x" + upper, new HexBytes(bytes).ToString("0X"));

        CollectionAssert.AreEqual(bytes, HexBytes.Parse(lower).ToArray());
        CollectionAssert.AreEqual(bytes, HexBytes.Parse("0x" + upper).ToArray());
        Assert.IsTrue(HexBytes.TryParse(Encoding.ASCII.GetBytes("0x" + lower), out HexBytes viaUtf8));
        CollectionAssert.AreEqual(bytes, viaUtf8.ToArray());
    }
}
