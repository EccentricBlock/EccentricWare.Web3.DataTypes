using System.Globalization;
using System.Numerics;
using static EccentricWare.Web3.DataTypes.Tests.HexBigIntegerTestData;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="HexBigInteger"/>: constructors, properties, equality, ordering, conversions and interface dispatch.</summary>
[TestClass]
public sealed class HexBigIntegerCoreTests
{
    private static int Sign(BigInteger value) => value.Sign;

    // ----------------- constants / constructors / properties -----------------

    [TestMethod]
    public void Constants_AndSignProperties()
    {
        Assert.IsTrue(HexBigInteger.Zero.IsZero);
        Assert.AreEqual(0, HexBigInteger.Zero.Sign);
        Assert.AreEqual(1, HexBigInteger.One.Sign);
        Assert.AreEqual(-1, HexBigInteger.MinusOne.Sign);
        Assert.IsTrue(HexBigInteger.MinusOne.IsNegative);
        Assert.IsFalse(HexBigInteger.One.IsNegative);
        Assert.IsFalse(HexBigInteger.Zero.IsNegative);
        Assert.AreEqual(default, HexBigInteger.Zero);
        Assert.AreEqual(new BigInteger(1), HexBigInteger.One.Value);
        Assert.AreEqual(new BigInteger(-1), HexBigInteger.MinusOne.Value);
    }

    [TestMethod]
    public void DefaultMaxHexDigits_IsPinnedAt4096()
    {
        Assert.AreEqual(4096, HexBigInteger.DefaultMaxHexDigits);
    }

    [TestMethod]
    public void IsZero_Sign_IsNegative_ForNonZeroValues()
    {
        BigInteger big = BigInteger.Pow(2, 200);

        Assert.IsFalse(HexBigInteger.One.IsZero);
        Assert.IsFalse(HexBigInteger.MinusOne.IsZero);
        Assert.IsFalse(new HexBigInteger(big).IsZero);
        Assert.AreEqual(1, new HexBigInteger(big).Sign);
        Assert.AreEqual(-1, new HexBigInteger(-big).Sign);
        Assert.IsFalse(new HexBigInteger(big).IsNegative);
        Assert.IsTrue(new HexBigInteger(-big).IsNegative);
        Assert.IsTrue(new HexBigInteger(long.MinValue).IsNegative);
        Assert.IsTrue(new HexBigInteger(-BigInteger.Pow(2, 64)).IsNegative);
    }

    [TestMethod]
    public void Constructors_FromIntegers_AgreeWithTheLiteral()
    {
        Assert.AreEqual(new BigInteger(long.MinValue), new HexBigInteger(long.MinValue).Value);
        Assert.AreEqual(new BigInteger(long.MaxValue), new HexBigInteger(long.MaxValue).Value);
        Assert.AreEqual(new BigInteger(ulong.MaxValue), new HexBigInteger(ulong.MaxValue).Value);
        Assert.AreEqual(BigInteger.Zero, new HexBigInteger(0L).Value);
        Assert.AreEqual(new BigInteger(-1), new HexBigInteger(-1L).Value);
        Assert.AreEqual(new BigInteger(-5), new HexBigInteger(-5).Value); // int literal resolves to the long overload
        Assert.AreEqual(1, new HexBigInteger(1UL << 63).Sign);
        Assert.IsFalse(new HexBigInteger(1UL << 63).IsNegative);
        Assert.AreEqual(BigInteger.Pow(2, 63), new HexBigInteger(1UL << 63).Value);
        Assert.AreEqual(BigInteger.Pow(2, 200), new HexBigInteger(BigInteger.Pow(2, 200)).Value);
    }

    [TestMethod]
    public void Constructor_FromBigEndianBytes_ReadsMostSignificantByteFirst()
    {
        // Expected values are hand-computed, not produced by the BigInteger constructor the implementation calls.
        Assert.AreEqual(new BigInteger(256), new HexBigInteger(new byte[] { 0x01, 0x00 }, isUnsigned: true).Value);   // little-endian would give 1
        Assert.AreEqual(new BigInteger(1), new HexBigInteger(new byte[] { 0x00, 0x00, 0x01 }, isUnsigned: true).Value);
        Assert.AreEqual(new BigInteger(0xFF01), new HexBigInteger(new byte[] { 0xFF, 0x01 }, isUnsigned: true).Value);
        Assert.AreEqual(new BigInteger(255), new HexBigInteger(new byte[] { 0xFF }, isUnsigned: true).Value);
        Assert.AreEqual(BigInteger.Zero, new HexBigInteger(ReadOnlySpan<byte>.Empty, isUnsigned: true).Value);
        Assert.AreEqual(BigInteger.Zero, new HexBigInteger(ReadOnlySpan<byte>.Empty, isUnsigned: false).Value);
    }

    [TestMethod]
    public void Constructor_FromBigEndianBytes_Signed_UsesTwosComplement()
    {
        Assert.AreEqual(new BigInteger(-128), new HexBigInteger(new byte[] { 0x80 }, isUnsigned: false).Value);
        Assert.AreEqual(new BigInteger(128), new HexBigInteger(new byte[] { 0x00, 0x80 }, isUnsigned: false).Value);
        Assert.AreEqual(new BigInteger(-1), new HexBigInteger(new byte[] { 0xFF }, isUnsigned: false).Value);
        Assert.AreEqual(new BigInteger(-1), new HexBigInteger(new byte[] { 0xFF, 0xFF }, isUnsigned: false).Value);
        Assert.AreEqual(new BigInteger(-255), new HexBigInteger(new byte[] { 0xFF, 0x01 }, isUnsigned: false).Value);
    }

    [TestMethod]
    public void Constructor_FromBigEndianBytes_WideInputs()
    {
        byte[] threeTwoThree = new byte[33];
        threeTwoThree[0] = 0x01;
        Assert.AreEqual(BigInteger.Pow(2, 256), new HexBigInteger(threeTwoThree, isUnsigned: true).Value);

        byte[] thousand = new byte[1000];
        thousand[0] = 0x80;
        Assert.AreEqual(BigInteger.Pow(2, 7999), new HexBigInteger(thousand, isUnsigned: true).Value);
        Assert.AreEqual(-BigInteger.Pow(2, 7999), new HexBigInteger(thousand, isUnsigned: false).Value);
    }

    [TestMethod]
    public void MagnitudeByteCount_UsesMagnitude_ForNegativeValues()
    {
        Assert.AreEqual(1, new HexBigInteger(255).MagnitudeByteCount);
        Assert.AreEqual(2, new HexBigInteger(256).MagnitudeByteCount);
        Assert.AreEqual(1, new HexBigInteger(-255).MagnitudeByteCount);
        Assert.AreEqual(2, new HexBigInteger(-256).MagnitudeByteCount);
        Assert.AreEqual(1, new HexBigInteger(-128).MagnitudeByteCount); // two's complement would also fit one byte
        Assert.AreEqual(33, new HexBigInteger(BigInteger.Pow(2, 256)).MagnitudeByteCount);
        Assert.AreEqual(33, new HexBigInteger(-BigInteger.Pow(2, 256)).MagnitudeByteCount);
        Assert.AreEqual(1, HexBigInteger.Zero.MagnitudeByteCount); // as BigInteger.GetByteCount reports for zero
    }

    [TestMethod]
    public void MagnitudeByteCount_AtEveryByteBoundary_MatchesTheBitLengthFormula()
    {
        for (int k = 1; k <= 33; k++)
        {
            foreach (BigInteger value in new[] { BigInteger.Pow(2, 8 * k) - 1, BigInteger.Pow(2, 8 * k) })
            {
                foreach (BigInteger signed in new[] { value, -value })
                {
                    int expected = (int)((BigInteger.Abs(signed).GetBitLength() + 7) / 8);
                    Assert.AreEqual(expected, new HexBigInteger(signed).MagnitudeByteCount, signed.ToString("x"));
                }
            }
        }

        Assert.AreEqual(8, new HexBigInteger(long.MinValue).MagnitudeByteCount);
        Assert.AreEqual(8, new HexBigInteger(-BigInteger.Pow(2, 63) - 1).MagnitudeByteCount); // magnitude 2^63 + 1 still fits 64 bits
        Assert.AreEqual(9, new HexBigInteger(-BigInteger.Pow(2, 64) - 1).MagnitudeByteCount);
        Assert.AreEqual(8, new HexBigInteger(ulong.MaxValue).MagnitudeByteCount);
    }

    // ----------------- equality and hashing -----------------

    [TestMethod]
    public void Equality_AndHashing_HoldAcrossConstructionPaths()
    {
        HexBigInteger[] all255 =
        [
            HexBigInteger.Parse("0x00ff", null),
            HexBigInteger.Parse("ff", null),
            new HexBigInteger(255),
            new HexBigInteger(255UL),
            new HexBigInteger(new byte[] { 0, 0, 255 }, isUnsigned: true),
            new BigInteger(255),
            HexBigInteger.ParseUtf8("0xFF"u8),
        ];

        foreach (HexBigInteger value in all255)
        {
            Assert.AreEqual(all255[0], value);
            Assert.IsTrue(value == all255[0]);
            Assert.AreEqual(all255[0].GetHashCode(), value.GetHashCode());
        }

        HexBigInteger[] zeros = [HexBigInteger.Zero, HexBigInteger.Parse("-0x0", null), default, new HexBigInteger(0UL), HexBigInteger.Parse("0x", null)];
        foreach (HexBigInteger zero in zeros)
        {
            Assert.AreEqual(HexBigInteger.Zero, zero);
            Assert.AreEqual(HexBigInteger.Zero.GetHashCode(), zero.GetHashCode());
        }
    }

    [TestMethod]
    public void Equals_Object_RejectsOtherBoxedNumericTypes()
    {
        var value = new HexBigInteger(255);

        Assert.IsTrue(value.Equals((object)new HexBigInteger(255)));
        Assert.IsFalse(value.Equals((object)255));
        Assert.IsFalse(value.Equals((object)new BigInteger(255)));
        Assert.IsFalse(value.Equals((object)"x"));
        Assert.IsFalse(value.Equals((object?)null));
    }

    [TestMethod]
    public void CanBeUsedAsAHashSetAndDictionaryKey_ThroughADifferentConstructionPath()
    {
        var set = new HashSet<HexBigInteger> { HexBigInteger.Parse("0xff", null) };
        var map = new Dictionary<HexBigInteger, string> { [new HexBigInteger(255)] = "x" };

        Assert.IsTrue(set.Contains(new HexBigInteger(255)));
        Assert.IsTrue(set.Contains(HexBigInteger.ParseUtf8("\"0x00FF\""u8)));
        Assert.IsFalse(set.Contains(new HexBigInteger(256)));
        Assert.AreEqual("x", map[HexBigInteger.Parse("FF", null)]);
    }

    // ----------------- ordering -----------------

    [TestMethod]
    public void Ordering_OverEveryPair_MatchesBigInteger_ForAllOperators()
    {
        BigInteger[] values = [.. InterestingSigned()];

        foreach (BigInteger x in values)
        {
            foreach (BigInteger y in values)
            {
                HexBigInteger a = x, b = y;
                string label = $"{x} vs {y}";

                Assert.AreEqual(Sign(x - y), Math.Sign(a.CompareTo(b)), label);
                Assert.AreEqual(x == y, a == b, label);
                Assert.AreEqual(x != y, a != b, label);
                Assert.AreEqual(x < y, a < b, label);
                Assert.AreEqual(x > y, a > b, label);
                Assert.AreEqual(x <= y, a <= b, label);
                Assert.AreEqual(x >= y, a >= b, label);
                Assert.AreEqual(Math.Sign(a.CompareTo(b)), Math.Sign(((IComparable)a).CompareTo((object)b)), label);
                Assert.AreEqual(-Math.Sign(a.CompareTo(b)), Math.Sign(b.CompareTo(a)), label);
            }
        }
    }

    [TestMethod]
    public void Ordering_SpecificBoundaries()
    {
        Assert.IsTrue(new HexBigInteger(-5) < new HexBigInteger(-3));
        Assert.IsFalse(new HexBigInteger(-3) < new HexBigInteger(-5));
        Assert.IsTrue(new HexBigInteger(BigInteger.Pow(2, 64)) > new HexBigInteger(ulong.MaxValue));
        Assert.IsTrue(new HexBigInteger(-BigInteger.Pow(2, 64)) < new HexBigInteger(-(BigInteger)ulong.MaxValue));
        Assert.IsTrue(default(HexBigInteger) == HexBigInteger.Zero);
        Assert.IsTrue(default(HexBigInteger) <= HexBigInteger.Zero);
        Assert.IsTrue(default(HexBigInteger) >= HexBigInteger.Zero);
    }

    [TestMethod]
    public void Sorting_OrdersByNumericValue()
    {
        var sorted = new[] { new HexBigInteger(5), new HexBigInteger(-5), HexBigInteger.Zero, new HexBigInteger(500) }.Order().ToArray();
        CollectionAssert.AreEqual(new[] { -5, 0, 5, 500 }, sorted.Select(v => (int)v.Value).ToArray());
    }

    [TestMethod]
    public void BoxedCompareTo_NullIsLess_AndOtherTypesThrow()
    {
        var a = new HexBigInteger(BigInteger.Pow(2, 200));

        Assert.AreEqual(1, a.CompareTo((object?)null));
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => a.CompareTo("x"));
        Assert.AreEqual("obj", error.ParamName);
        Assert.ThrowsExactly<ArgumentException>(() => a.CompareTo((object)5));
    }

    // ----------------- conversions -----------------

    [TestMethod]
    public void Conversions_ToAndFromBigInteger()
    {
        HexBigInteger fromBig = new BigInteger(77);
        BigInteger back = fromBig;
        Assert.AreEqual(new BigInteger(77), back);
    }

    [TestMethod]
    public void ToUInt256_ConvertsInRangeValues()
    {
        Assert.AreEqual(uint256.Zero, HexBigInteger.Zero.ToUInt256());
        Assert.AreEqual(new uint256(0, 0, 0, 0x8000000000000000UL), new HexBigInteger(BigInteger.Pow(2, 255)).ToUInt256());
        Assert.AreEqual(new uint256((ulong)long.MaxValue), new HexBigInteger(long.MaxValue).ToUInt256());
        Assert.AreEqual(uint256.MaxValue, new HexBigInteger(BigInteger.Pow(2, 256) - 1).ToUInt256());
        Assert.AreEqual(new uint256(0, 1, 0, 0), new HexBigInteger(BigInteger.Pow(2, 64)).ToUInt256());
    }

    [TestMethod]
    public void ToUInt256_OutOfRange_ThrowsOverflow()
    {
        Assert.ThrowsExactly<OverflowException>(() => new HexBigInteger(BigInteger.Pow(2, 256)).ToUInt256());
        Assert.ThrowsExactly<OverflowException>(() => HexBigInteger.MinusOne.ToUInt256());
        Assert.ThrowsExactly<OverflowException>(() => new HexBigInteger(int.MinValue).ToUInt256());
    }

    [TestMethod]
    public void UInt256_RoundTrips_AcrossLimbBoundaries()
    {
        // Expected BigIntegers are built from limb shifts, independent of uint256's own conversions.
        (uint256 Value, BigInteger Expected)[] cases =
        [
            (new uint256(ulong.MaxValue), BigInteger.Pow(2, 64) - 1),
            (new uint256(0, 1, 0, 0), BigInteger.Pow(2, 64)),
            (new uint256(0, 0, 1, 0), BigInteger.Pow(2, 128)),
            (new uint256(0, 0, 0, 1), BigInteger.Pow(2, 192)),
            (uint256.MaxValue, BigInteger.Pow(2, 256) - 1),
            (uint256.Zero, BigInteger.Zero),
        ];

        foreach ((uint256 value, BigInteger expected) in cases)
        {
            HexBigInteger converted = value;
            Assert.AreEqual(expected, converted.Value);
            Assert.AreEqual(value, (uint256)converted);
            Assert.AreEqual(value, converted.ToUInt256());
        }
    }

    // ----------------- interface dispatch -----------------

    private static T ParseVia<T>(string s) where T : IParsable<T> => T.Parse(s, null);
    private static bool TryParseVia<T>(string? s, out T? result) where T : IParsable<T> => T.TryParse(s, null, out result);

    [TestMethod]
    public void IParsable_AndISpanParsable_Dispatch()
    {
        Assert.AreEqual(new HexBigInteger(255), ParseVia<HexBigInteger>("0xff"));
        Assert.IsTrue(TryParseVia<HexBigInteger>("-0x10", out HexBigInteger parsed));
        Assert.AreEqual(new HexBigInteger(-16), parsed);
        Assert.IsFalse(TryParseVia<HexBigInteger>("zz", out _));
        Assert.IsFalse(TryParseVia<HexBigInteger>(null, out _));
        Assert.IsFalse(TryParseVia<HexBigInteger>("", out _));
        Assert.ThrowsExactly<FormatException>(() => ParseVia<HexBigInteger>("zz"));
    }

    [TestMethod]
    public void ParseAndFormat_IgnoreTheProvider()
    {
        var turkish = new CultureInfo("tr-TR");
        var value = new HexBigInteger(-255);

        Assert.AreEqual(HexBigInteger.Parse("-0xFF", CultureInfo.InvariantCulture), HexBigInteger.Parse("-0xFF", turkish));
        Assert.AreEqual(value.ToString("X", CultureInfo.InvariantCulture), value.ToString("X", turkish));
        Assert.AreEqual(value.ToString("d", CultureInfo.InvariantCulture), value.ToString("d", turkish));
    }

    [TestMethod]
    public void ISpanFormattable_Interpolation_UsesHexByDefault()
    {
        var value = new HexBigInteger(255);
        Assert.AreEqual("v=0xff", $"v={value}");
        Assert.AreEqual("v=255", $"v={value:d}");
        Assert.AreEqual("v=FF", $"v={value:X}");
        Assert.AreEqual("0xff|ff", string.Format(CultureInfo.InvariantCulture, "{0}|{0:x}", value));
    }

    [TestMethod]
    public void IUtf8SpanFormattable_Dispatch()
    {
        var value = new HexBigInteger(255);
        Span<byte> buffer = stackalloc byte[16];

        Assert.IsTrue(System.Text.Unicode.Utf8.TryWrite(buffer, $"{value:x}", out int written));
        Assert.AreEqual("ff", System.Text.Encoding.ASCII.GetString(buffer[..written]));
        Assert.IsTrue(((IUtf8SpanFormattable)value).TryFormat(buffer, out written, "0X", null));
        Assert.AreEqual("0xFF", System.Text.Encoding.ASCII.GetString(buffer[..written]));
    }

    [TestMethod]
    public void DecimalLookingText_IsParsedAsHex_SoDecimalOutputDoesNotRoundTrip()
    {
        // Documents current, intended behaviour (see the Parse remarks): the input is always hexadecimal.
        Assert.AreEqual(new HexBigInteger(16), HexBigInteger.Parse("10", null));
        Assert.AreEqual(new HexBigInteger(0x255), HexBigInteger.Parse("255", null));

        var value = new HexBigInteger(255);
        Assert.AreNotEqual(value, HexBigInteger.Parse(value.ToString("d", null), null));
    }
}
