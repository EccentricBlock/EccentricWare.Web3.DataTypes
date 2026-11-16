using System.Globalization;
using System.Numerics;
using System.Text;
using static EccentricWare.Web3.DataTypes.Tests.HexBigIntegerTestData;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// <see cref="HexBigInteger"/> formatting and parsing around the stack/pool switch-over points of its internal scratch
/// buffers (about 1,016 bits for hex, about 840 bits for decimal), pooled-buffer reuse, and concurrent use.
/// </summary>
[TestClass]
public sealed class HexBigIntegerBufferBoundaryTests
{
    private static IEnumerable<BigInteger> AroundPowerOfTwo(int bits)
    {
        BigInteger power = BigInteger.One << bits;
        foreach (BigInteger v in new[] { power - 1, power, power + 1 })
        {
            yield return v;
            yield return -v;
        }
    }

    private static void AssertHexFormatting(BigInteger value, string context)
    {
        var hex = new HexBigInteger(value);
        foreach ((string format, bool upper, bool prefix) in new[] { ("", false, true), ("x", false, false), ("X", true, false), ("0X", true, true) })
        {
            string expected = ReferenceHex(value, upper, prefix);
            string label = $"{context} value={value:x} '{format}'";

            Assert.AreEqual(expected, hex.ToString(format, null), label);

            var chars = new char[expected.Length];
            Assert.IsTrue(hex.TryFormat(chars, out int c, format, null), label);
            Assert.AreEqual(expected, new string(chars, 0, c), label);

            var utf8 = new byte[expected.Length];
            Assert.IsTrue(hex.TryFormat(utf8, out int b, format, null), label);
            Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8, 0, b), label);

            // One short: false and untouched.
            var shortChars = new char[expected.Length - 1];
            var shortUtf8 = new byte[expected.Length - 1];
            Array.Fill(shortChars, '@');
            Array.Fill(shortUtf8, (byte)0xAB);
            Assert.IsFalse(hex.TryFormat(shortChars, out c, format, null), label);
            Assert.IsFalse(hex.TryFormat(shortUtf8, out b, format, null), label);
            Assert.AreEqual(0, c);
            Assert.AreEqual(0, b);
            Assert.IsTrue(shortChars.All(ch => ch == '@'), label);
            Assert.IsTrue(shortUtf8.All(x => x == 0xAB), label);
        }
    }

    // ----------------- format -----------------

    [TestMethod]
    public void HexFormatting_AroundTheStackBufferBoundary_MatchesTheReference()
    {
        for (int bits = 1000; bits <= 1032; bits++)
        {
            foreach (BigInteger value in AroundPowerOfTwo(bits))
                AssertHexFormatting(value, $"bits={bits}");
        }
    }

    [TestMethod]
    public void HexFormatting_AroundTheLimbThresholds_MatchesTheReference()
    {
        for (int bits = 60; bits <= 68; bits++)
        {
            foreach (BigInteger value in AroundPowerOfTwo(bits))
                AssertHexFormatting(value, $"bits={bits}");
        }
    }

    [TestMethod]
    public void HexFormatting_PooledDestinationsBetweenTheStackAndFullSize_FailCleanly()
    {
        // A destination larger than the 256-byte scratch but smaller than the output makes the char path rent a buffer and
        // then fail inside TryFormatHex; it must report false and leave the destination alone.
        BigInteger value = (BigInteger.One << 2000) + 1; // about 501 digits
        var hex = new HexBigInteger(value);
        string expected = ReferenceHex(value);

        foreach (int size in new[] { 255, 256, 257, 300, expected.Length - 1 })
        {
            var chars = new char[size];
            Array.Fill(chars, '@');
            Assert.IsFalse(hex.TryFormat(chars, out int written, "", null), $"size={size}");
            Assert.AreEqual(0, written);
            Assert.IsTrue(chars.All(c => c == '@'), $"size={size}");
        }

        var exact = new char[expected.Length];
        Assert.IsTrue(hex.TryFormat(exact, out int count, "", null));
        Assert.AreEqual(expected, new string(exact, 0, count));
    }

    [TestMethod]
    public void DecimalFormatting_AroundTheStackBufferBoundary_MatchesBigInteger()
    {
        for (int bits = 825; bits <= 860; bits++)
        {
            foreach (BigInteger value in AroundPowerOfTwo(bits))
            {
                string expected = value.ToString(CultureInfo.InvariantCulture);
                var hex = new HexBigInteger(value);
                string label = $"bits={bits} value={expected}";

                Assert.AreEqual(expected, hex.ToString("d", null), label);

                var utf8 = new byte[expected.Length];
                Assert.IsTrue(hex.TryFormat(utf8, out int b, "d", null), label);
                Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8, 0, b), label);

                var chars = new char[expected.Length];
                Assert.IsTrue(hex.TryFormat(chars, out int c, "d", null), label);
                Assert.AreEqual(expected, new string(chars, 0, c), label);

                Assert.IsFalse(hex.TryFormat(new byte[expected.Length - 1], out b, "d", null), label);
                Assert.IsFalse(hex.TryFormat(new char[expected.Length - 1], out c, "d", null), label);
                Assert.AreEqual(0, b);
                Assert.AreEqual(0, c);
            }
        }
    }

    [TestMethod]
    public void DecimalFormatting_ExactDigitCountDestination_AtEveryPowerOfTen()
    {
        for (int k = 1; k <= 80; k++)
        {
            BigInteger power = BigInteger.Pow(10, k);
            foreach (BigInteger value in new[] { power - 1, power, -(power - 1), -power })
            {
                string expected = value.ToString(CultureInfo.InvariantCulture);
                var hex = new HexBigInteger(value);

                var utf8 = new byte[expected.Length];
                Assert.IsTrue(hex.TryFormat(utf8, out int b, "d", null), expected);
                Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8, 0, b), expected);

                var chars = new char[expected.Length];
                Assert.IsTrue(hex.TryFormat(chars, out int c, "d", null), expected);
                Assert.AreEqual(expected, new string(chars, 0, c), expected);

                Assert.IsFalse(hex.TryFormat(new byte[expected.Length - 1], out _, "d", null), expected);
            }
        }
    }

    // ----------------- parse -----------------

    [TestMethod]
    public void Parse_AroundTheHexFormatBoundary_RoundTripsBothParsers()
    {
        for (int bits = 1000; bits <= 1032; bits++)
        {
            foreach (BigInteger value in AroundPowerOfTwo(bits))
            {
                string text = ReferenceHex(value);
                Assert.AreEqual(value, HexBigInteger.Parse(text, null).Value, $"bits={bits}");
                Assert.AreEqual(value, HexBigInteger.ParseUtf8(Encoding.ASCII.GetBytes(text)).Value, $"bits={bits}");
            }
        }
    }

    // ----------------- pooled-buffer reuse and concurrency -----------------

    [TestMethod]
    public void PooledBuffers_ReusedAfterALargeValue_NeverLeakStaleBytes()
    {
        // A huge value rents (and returns, without clearing) big buffers; small values that rent the same size class
        // afterwards must produce exactly the reference output, not remnants of the previous content.
        BigInteger huge = (BigInteger.One << 40_000) - 1;
        BigInteger[] smalls = [(BigInteger.One << 1100) + 5, -((BigInteger.One << 1100) + 5), BigInteger.One << 1500, (BigInteger.One << 3000) + 0x1234];

        for (int round = 0; round < 3; round++)
        {
            string hugeHex = ReferenceHex(huge);
            Assert.AreEqual(hugeHex, new HexBigInteger(huge).ToString());
            Assert.AreEqual(huge, HexBigInteger.Parse(hugeHex, null).Value);
            Assert.AreEqual(huge, HexBigInteger.ParseUtf8(Encoding.ASCII.GetBytes(hugeHex), maxHexDigits: int.MaxValue).Value);

            foreach (BigInteger small in smalls)
            {
                string expected = ReferenceHex(small);
                Assert.AreEqual(expected, new HexBigInteger(small).ToString(), $"round={round}");
                Assert.AreEqual(small, HexBigInteger.Parse(expected, null).Value, $"round={round}");
                Assert.AreEqual(small, HexBigInteger.ParseUtf8(Encoding.ASCII.GetBytes(expected)).Value, $"round={round}");

                var utf8 = new byte[expected.Length];
                Assert.IsTrue(new HexBigInteger(small).TryFormat(utf8, out int written, "", null));
                Assert.AreEqual(expected, Encoding.ASCII.GetString(utf8, 0, written));
            }
        }
    }

    [TestMethod]
    public void ParseAndFormat_AreCorrectUnderConcurrentUse_WithMixedSizes()
    {
        var random = new Random(2026);
        BigInteger[] values = new BigInteger[2000];
        for (int i = 0; i < values.Length; i++)
        {
            BigInteger magnitude = RandomMagnitude(random, random.Next(1, 600));
            values[i] = random.Next(2) == 0 ? magnitude : -magnitude;
        }

        Parallel.For(0, values.Length, i =>
        {
            BigInteger value = values[i];
            string expected = ReferenceHex(value);
            Assert.AreEqual(expected, new HexBigInteger(value).ToString());
            Assert.AreEqual(value, HexBigInteger.Parse(expected, null).Value);
            Assert.AreEqual(value, HexBigInteger.ParseUtf8(Encoding.ASCII.GetBytes(expected)).Value);

            var chars = new char[expected.Length];
            Assert.IsTrue(new HexBigInteger(value).TryFormat(chars, out int written, "", null));
            Assert.AreEqual(expected, new string(chars, 0, written));
        });
    }
}
