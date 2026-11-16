using EccentricWare.Web3.DataTypes.Utils;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Direct tests of the internal ASCII formatting core. The formatter must write every output byte itself and never
/// depend on what the scratch held before (callers zero it today, but that is not part of the contract). These tests
/// pre-fill the scratch with garbage and use the smallest legal buffer sizes.
/// </summary>
[TestClass]
public sealed class UInt256FormattingCoreTests
{
    private static readonly BigInteger Two256 = BigInteger.One << 256;

    private static IEnumerable<BigInteger> Values()
    {
        yield return 0;
        yield return 1;
        yield return 9;
        yield return 10;
        yield return 15;
        yield return 16;
        yield return ulong.MaxValue;
        yield return (BigInteger)ulong.MaxValue + 1;
        yield return BigInteger.Pow(10, 19) - 1;
        yield return BigInteger.Pow(10, 19);
        yield return (BigInteger.One << 128) - 1;
        yield return BigInteger.One << 128;
        yield return (BigInteger.One << 192) - 1;
        yield return BigInteger.One << 192;
        yield return BigInteger.Pow(10, 38);
        yield return BigInteger.Pow(10, 76);
        yield return (BigInteger.One << 255);
        yield return Two256 - 1;
    }

    private static string ExpectedHex(BigInteger value, bool uppercase, bool fullWidth)
    {
        string hex = value.ToString("x", CultureInfo.InvariantCulture).TrimStart('0');
        if (hex.Length == 0) hex = "0";
        if (fullWidth) hex = hex.PadLeft(64, '0');
        return uppercase ? hex.ToUpperInvariant() : hex;
    }

    private static (ulong, ulong, ulong, ulong) Limbs(BigInteger v)
    {
        var u = (uint256)v;
        return (u.U0, u.U1, u.U2, u.U3);
    }

    [TestMethod]
    public void FormatHex_NeverReadsUnwrittenScratch()
    {
        foreach (byte garbage in new byte[] { 0x00, 0xFF, (byte)'Z', (byte)'0' })
        {
            foreach (int scratchLength in new[] { 66, 80 })
            {
                foreach (BigInteger value in Values())
                {
                    (ulong u0, ulong u1, ulong u2, ulong u3) = Limbs(value);
                    foreach (bool prefix in new[] { false, true })
                    {
                        foreach (bool uppercase in new[] { false, true })
                        {
                            foreach (bool fullWidth in new[] { false, true })
                            {
                                byte[] scratch = new byte[scratchLength];
                                Array.Fill(scratch, garbage);

                                ReadOnlySpan<byte> result = UInt256Formatting.FormatHex(scratch, u0, u1, u2, u3, prefix, uppercase, fullWidth);

                                string expected = (prefix ? "0x" : "") + ExpectedHex(value, uppercase, fullWidth);
                                Assert.AreEqual(expected, Encoding.ASCII.GetString(result),
                                    $"value={value} garbage=0x{garbage:X2} scratch={scratchLength} prefix={prefix} upper={uppercase} full={fullWidth}");
                            }
                        }
                    }
                }
            }
        }
    }

    [TestMethod]
    public void FormatDecimal_NeverReadsUnwrittenScratch_AndFitsTheMinimumBuffer()
    {
        foreach (byte garbage in new byte[] { 0x00, 0xFF, (byte)'Z', (byte)'9' })
        {
            // 78 is the longest possible text (2^256 - 1, or -2^255 with its sign); the formatter claims 80 is plenty.
            foreach (int scratchLength in new[] { UInt256Formatting.MaxDecimalLength, 80 })
            {
                foreach (BigInteger value in Values())
                {
                    foreach (bool negative in new[] { false, true })
                    {
                        // A negative sign is only legal for magnitudes up to 2^255.
                        if (negative && value > (BigInteger.One << 255)) continue;

                        (ulong u0, ulong u1, ulong u2, ulong u3) = Limbs(value);
                        byte[] scratch = new byte[scratchLength];
                        Array.Fill(scratch, garbage);

                        ReadOnlySpan<byte> result = UInt256Formatting.FormatDecimal(scratch, u0, u1, u2, u3, negative);

                        string expected = (negative ? "-" : "") + value.ToString(CultureInfo.InvariantCulture);
                        Assert.AreEqual(expected, Encoding.ASCII.GetString(result),
                            $"value={value} negative={negative} garbage=0x{garbage:X2} scratch={scratchLength}");
                        Assert.IsLessThanOrEqualTo(UInt256Formatting.MaxDecimalLength, result.Length);
                    }
                }
            }
        }
    }

    [TestMethod]
    public void PublicFormatting_IsNotAffectedByPriorCalls()
    {
        // Back-to-back formatting of long then short values (in a fixed shuffled order) must not leak digits from the
        // earlier call's stack. The identical-input repetition of the old version added nothing. int256 is included
        // because it shares this formatting core.
        Func<string>[] calls =
        [
            () => uint256.MaxValue.ToDecimalString(), () => uint256.Zero.ToDecimalString(), () => uint256.MaxValue.ToString(),
            () => uint256.Zero.ToString(), () => uint256.One.ToDecimalString(), () => int256.MinValue.ToString(),
            () => int256.MinusOne.ToString(), () => int256.Zero.ToString(),
        ];
        string[] expected =
        [
            (Two256 - 1).ToString(CultureInfo.InvariantCulture), "0", "0x" + new string('f', 64),
            "0x0", "1", (-(BigInteger.One << 255)).ToString(CultureInfo.InvariantCulture), "-1", "0",
        ];

        var rng = new Random(5);
        for (int i = 0; i < 100; i++)
        {
            int index = rng.Next(calls.Length);
            Assert.AreEqual(expected[index], calls[index](), $"call {index} after {i} earlier calls");
        }
    }
}
