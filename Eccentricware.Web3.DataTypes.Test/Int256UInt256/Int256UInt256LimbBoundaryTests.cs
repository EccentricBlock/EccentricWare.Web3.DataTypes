using System.Globalization;
using System.Numerics;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Adversarial cross-checks of <see cref="uint256"/> and <see cref="int256"/> against <see cref="BigInteger"/>,
/// concentrating on values at limb and sign boundaries where carry/borrow/sign-extension bugs hide.
/// </summary>
[TestClass]
public sealed class Int256UInt256LimbBoundaryTests
{
    private static readonly BigInteger Two256 = BigInteger.One << 256;
    private static readonly BigInteger Mask256 = Two256 - 1;
    private static readonly BigInteger Two255 = BigInteger.One << 255;

    private static BigInteger Pow2(int bits) => BigInteger.One << bits;

    private static BigInteger WrapSigned(BigInteger value)
    {
        BigInteger m = value & Mask256;
        return m >= Two255 ? m - Two256 : m;
    }

    private static bool FitsUnsigned(BigInteger v) => v.Sign >= 0 && v < Two256;
    private static bool FitsSigned(BigInteger v) => v >= -Two255 && v < Two255;

    private static List<BigInteger> UnsignedBoundaryValues()
    {
        var values = new List<BigInteger> { 0, 1, 2, 3, 10, 1_000_000_000_000_000_000UL };
        foreach (int bits in new[] { 32, 63, 64, 65, 127, 128, 129, 191, 192, 193, 254, 255, 256 })
        {
            BigInteger p = Pow2(bits);
            values.Add(p - 1);
            if (bits < 256) values.Add(p);
            if (bits < 255) values.Add(p + 1);
        }

        // Alternating limbs, and a value whose limbs are all different.
        values.Add(BigInteger.Parse("0ffffffffffffffff0000000000000000ffffffffffffffff0000000000000000", NumberStyles.HexNumber));
        values.Add(BigInteger.Parse("00123456789abcdef0fedcba98765432100123456789abcdef0fedcba9876543210", NumberStyles.HexNumber));
        return values.Where(FitsUnsigned).Distinct().ToList();
    }

    private static List<BigInteger> SignedBoundaryValues()
    {
        var values = new List<BigInteger>();
        foreach (BigInteger v in UnsignedBoundaryValues())
        {
            if (FitsSigned(v)) values.Add(v);
            if (FitsSigned(-v)) values.Add(-v);
        }

        values.Add(-Two255);
        values.Add(Two255 - 1);
        values.Add(long.MinValue);
        values.Add(long.MaxValue);
        values.Add((BigInteger)long.MinValue - 1);
        values.Add((BigInteger)long.MaxValue + 1);
        return values.Distinct().ToList();
    }

    /// <summary>Random value whose limbs are drawn from {0, 1, max, random}, so carries and borrows happen often.</summary>
    private static ulong SparseLimb(Random rng) => rng.Next(4) switch
    {
        0 => 0UL,
        1 => 1UL,
        2 => ulong.MaxValue,
        _ => (ulong)rng.NextInt64() ^ ((ulong)rng.Next(2) << 63),
    };

    private static BigInteger FromLimbs(ulong u0, ulong u1, ulong u2, ulong u3)
        => new BigInteger(u0) | (new BigInteger(u1) << 64) | (new BigInteger(u2) << 128) | (new BigInteger(u3) << 192);

    // ---- uint256 ----

    [TestMethod]
    public void UInt256_AllBoundaryPairs_ArithmeticAndComparison_MatchBigInteger()
    {
        List<BigInteger> values = UnsignedBoundaryValues();
        foreach (BigInteger bx in values)
        {
            foreach (BigInteger by in values)
                AssertUnsignedPair(bx, by);
        }
    }

    [TestMethod]
    public void UInt256_SparseRandomLimbs_ArithmeticMatchesBigInteger()
    {
        var rng = new Random(20260101);
        for (int i = 0; i < 3000; i++)
        {
            var x = new uint256(SparseLimb(rng), SparseLimb(rng), SparseLimb(rng), SparseLimb(rng));
            var y = new uint256(SparseLimb(rng), SparseLimb(rng), SparseLimb(rng), SparseLimb(rng));
            AssertUnsignedPair(x.ToBigInteger(), y.ToBigInteger());
        }
    }

    private static void AssertUnsignedPair(BigInteger bx, BigInteger by)
    {
        uint256 x = (uint256)bx;
        uint256 y = (uint256)by;
        string context = $"x={bx}, y={by}";

        Assert.AreEqual(bx & Mask256, x.ToBigInteger(), context);
        Assert.AreEqual(Math.Sign(bx.CompareTo(by)), Math.Sign(x.CompareTo(y)), context);
        Assert.AreEqual(bx == by, x == y, context);
        Assert.AreEqual(bx < by, x < y, context);

        Assert.AreEqual((bx + by) & Mask256, uint256.AddUnchecked(x, y).ToBigInteger(), context);
        Assert.AreEqual((bx - by) & Mask256, uint256.SubtractUnchecked(x, y).ToBigInteger(), context);
        Assert.AreEqual((bx * by) & Mask256, uint256.MultiplyUnchecked(x, y).ToBigInteger(), context);

        AssertChecked(bx + by, FitsUnsigned, () => (x + y).ToBigInteger(), context + " add");
        AssertChecked(bx - by, FitsUnsigned, () => (x - y).ToBigInteger(), context + " sub");
        AssertChecked(bx * by, FitsUnsigned, () => (x * y).ToBigInteger(), context + " mul");

        if (by.IsZero)
        {
            Assert.ThrowsExactly<DivideByZeroException>(() => _ = x / y);
            Assert.ThrowsExactly<DivideByZeroException>(() => _ = x % y);
            return;
        }

        Assert.AreEqual(bx / by, (x / y).ToBigInteger(), context);
        Assert.AreEqual(bx % by, (x % y).ToBigInteger(), context);
        (uint256 q, uint256 r) = uint256.DivRem(x, y);
        Assert.AreEqual(bx / by, q.ToBigInteger(), context);
        Assert.AreEqual(bx % by, r.ToBigInteger(), context);
    }

    private static void AssertChecked(BigInteger expected, Func<BigInteger, bool> fits, Func<BigInteger> actual, string context)
    {
        if (fits(expected))
            Assert.AreEqual(expected, actual(), context);
        else
            Assert.ThrowsExactly<OverflowException>(() => actual(), context);
    }

    [TestMethod]
    public void UInt256_Shifts_AllCounts_MatchBigInteger()
    {
        foreach (BigInteger bx in UnsignedBoundaryValues())
        {
            uint256 x = (uint256)bx;
            for (int s = 0; s <= 300; s++)
            {
                Assert.AreEqual((bx << s) & Mask256, (x << s).ToBigInteger(), $"{bx} << {s}");
                Assert.AreEqual(bx >> s, (x >> s).ToBigInteger(), $"{bx} >> {s}");
            }

            Assert.AreEqual(uint256.Zero, x << int.MaxValue);
            Assert.AreEqual(uint256.Zero, x >> int.MaxValue);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = x << -1);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = x >> -1);
        }
    }

    [TestMethod]
    public void UInt256_DivRemByUInt64_MatchesBigInteger_ForEveryBoundaryDividend()
    {
        ulong[] divisors = [1, 2, 3, 10, 1_000_000_007, uint.MaxValue, (ulong)uint.MaxValue + 1, 1UL << 63, ulong.MaxValue - 1, ulong.MaxValue];
        foreach (BigInteger bx in UnsignedBoundaryValues())
        {
            uint256 x = (uint256)bx;
            foreach (ulong d in divisors)
            {
                uint256 q = x.DivRem(d, out ulong r);
                Assert.AreEqual(bx / d, q.ToBigInteger(), $"{bx} / {d}");
                Assert.AreEqual(bx % d, new BigInteger(r), $"{bx} % {d}");
            }
        }
    }

    [TestMethod]
    public void UInt256_TryMultiplyByUInt64_MatchesBigInteger()
    {
        ulong[] factors = [0, 1, 2, 10, 1_000_000_000_000_000_000UL, uint.MaxValue, 1UL << 63, ulong.MaxValue];
        foreach (BigInteger bx in UnsignedBoundaryValues())
        {
            uint256 x = (uint256)bx;
            foreach (ulong f in factors)
            {
                BigInteger expected = bx * f;
                bool ok = x.TryMultiply(f, out uint256 result);
                Assert.AreEqual(FitsUnsigned(expected), ok, $"{bx} * {f}");
                if (ok)
                    Assert.AreEqual(expected, result.ToBigInteger(), $"{bx} * {f}");
            }
        }
    }

    [TestMethod]
    public void UInt256_TextAndByteRoundTrips_ForBoundaryValues()
    {
        byte[] buffer = new byte[80];
        foreach (BigInteger bx in UnsignedBoundaryValues())
        {
            uint256 x = (uint256)bx;

            Assert.AreEqual(x, uint256.Parse(x.ToString()), $"hex {bx}");
            Assert.AreEqual(x, uint256.Parse(x.ToDecimalString()), $"dec {bx}");
            Assert.AreEqual(bx.ToString(CultureInfo.InvariantCulture), x.ToDecimalString());
            Assert.AreEqual(x, uint256.Parse(x.ToString("X", CultureInfo.InvariantCulture).Insert(0, "0x")), $"upper {bx}");

            Span<byte> utf8 = buffer;
            Assert.IsTrue(x.TryFormat(utf8, out int written, "0x"));
            Assert.IsTrue(uint256.TryParse(utf8.Slice(0, written), out uint256 fromUtf8));
            Assert.AreEqual(x, fromUtf8);

            Assert.AreEqual(x, new uint256(x.ToBigEndianBytes()));
            Assert.AreEqual(x, uint256.FromLittleEndian(x.ToLittleEndianBytes()));
            Assert.AreEqual(x, new uint256(x.ToMinimalBigEndianBytes()));
            // Equal values built by different routes (limbs vs big-endian bytes vs UInt128 halves) must hash identically.
            var viaLimbs = new uint256((ulong)(bx & ulong.MaxValue), (ulong)((bx >> 64) & ulong.MaxValue), (ulong)((bx >> 128) & ulong.MaxValue), (ulong)(bx >> 192));
            var viaHalves = new uint256(x.Low128, x.High128);
            Assert.AreEqual(viaLimbs, x);
            Assert.AreEqual(viaLimbs.GetHashCode(), x.GetHashCode());
            Assert.AreEqual(viaHalves.GetHashCode(), new uint256(x.ToBigEndianBytes()).GetHashCode());
        }
    }

    [TestMethod]
    public void UInt256_BitCounts_MatchBigInteger_ForBoundaryValues()
    {
        foreach (BigInteger bx in UnsignedBoundaryValues())
        {
            uint256 x = (uint256)bx;
            int bitLength = bx.IsZero ? 0 : (int)bx.GetBitLength();
            Assert.AreEqual(256 - bitLength, x.LeadingZeroCount(), $"lzcnt {bx}");
            Assert.AreEqual(bx.IsZero ? 256 : (int)BigInteger.TrailingZeroCount(bx), x.TrailingZeroCount(), $"tzcnt {bx}");
            Assert.AreEqual((bitLength + 7) / 8, x.GetByteCount(), $"bytes {bx}");

            int pop = 0;
            for (BigInteger t = bx; !t.IsZero; t >>= 1)
                pop += (int)(t & 1);
            Assert.AreEqual(pop, x.PopCount(), $"popcount {bx}");
        }
    }

    // ---- int256 ----

    [TestMethod]
    public void Int256_AllBoundaryPairs_ArithmeticAndComparison_MatchBigInteger()
    {
        List<BigInteger> values = SignedBoundaryValues();
        foreach (BigInteger bx in values)
        {
            foreach (BigInteger by in values)
                AssertSignedPair(bx, by);
        }
    }

    [TestMethod]
    public void Int256_SparseRandomLimbs_ArithmeticMatchesBigInteger()
    {
        var rng = new Random(20260102);
        for (int i = 0; i < 3000; i++)
        {
            var x = new int256(SparseLimb(rng), SparseLimb(rng), SparseLimb(rng), SparseLimb(rng));
            var y = new int256(SparseLimb(rng), SparseLimb(rng), SparseLimb(rng), SparseLimb(rng));
            AssertSignedPair(x.ToBigInteger(), y.ToBigInteger());
        }
    }

    private static void AssertSignedPair(BigInteger bx, BigInteger by)
    {
        int256 x = (int256)bx;
        int256 y = (int256)by;
        string context = $"x={bx}, y={by}";

        Assert.AreEqual(bx, x.ToBigInteger(), context);
        Assert.AreEqual(Math.Sign(bx.CompareTo(by)), Math.Sign(x.CompareTo(y)), context);
        Assert.AreEqual(bx == by, x == y, context);
        Assert.AreEqual(bx < by, x < y, context);
        Assert.AreEqual(bx.Sign, x.Sign, context);

        Assert.AreEqual(WrapSigned(bx + by), int256.AddUnchecked(x, y).ToBigInteger(), context);
        Assert.AreEqual(WrapSigned(bx - by), int256.SubtractUnchecked(x, y).ToBigInteger(), context);
        Assert.AreEqual(WrapSigned(-bx), int256.NegateUnchecked(x).ToBigInteger(), context);

        AssertChecked(bx + by, FitsSigned, () => (x + y).ToBigInteger(), context + " add");
        AssertChecked(bx - by, FitsSigned, () => (x - y).ToBigInteger(), context + " sub");
        AssertChecked(bx * by, FitsSigned, () => (x * y).ToBigInteger(), context + " mul");
        AssertChecked(-bx, FitsSigned, () => (-x).ToBigInteger(), context + " neg");

        if (by.IsZero)
        {
            Assert.ThrowsExactly<DivideByZeroException>(() => _ = x / y);
            Assert.ThrowsExactly<DivideByZeroException>(() => _ = x % y);
            return;
        }

        // BigInteger truncates toward zero and the remainder takes the dividend's sign: the EVM SDIV/SMOD rules.
        AssertChecked(BigInteger.Divide(bx, by), FitsSigned, () => (x / y).ToBigInteger(), context + " div");
        AssertChecked(BigInteger.Remainder(bx, by), FitsSigned, () => (x % y).ToBigInteger(), context + " rem");
    }

    [TestMethod]
    public void Int256_Shifts_AllCounts_MatchBigInteger()
    {
        foreach (BigInteger bx in SignedBoundaryValues())
        {
            int256 x = (int256)bx;
            // Counts of 256 and above follow BigInteger exactly: left shifts wrap to zero, arithmetic right shifts give 0 or -1.
            for (int s = 0; s <= 300; s++)
            {
                Assert.AreEqual(WrapSigned(bx << s), (x << s).ToBigInteger(), $"{bx} << {s}");
                Assert.AreEqual(bx >> s, (x >> s).ToBigInteger(), $"{bx} >> {s} (arithmetic)");
            }

            Assert.AreEqual(int256.Zero, x << int.MaxValue);
            Assert.AreEqual(bx.Sign < 0 ? int256.MinusOne : int256.Zero, x >> int.MaxValue);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = x << -1);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = x >> -1);
        }
    }

    [TestMethod]
    public void Int256_Bitwise_MatchesBigInteger_ForBoundaryPairs()
    {
        List<BigInteger> values = SignedBoundaryValues();
        foreach (BigInteger bx in values)
        {
            Assert.AreEqual(WrapSigned(~bx), (~(int256)bx).ToBigInteger());
            foreach (BigInteger by in values)
            {
                int256 x = (int256)bx;
                int256 y = (int256)by;
                Assert.AreEqual(bx & by, (x & y).ToBigInteger());
                Assert.AreEqual(bx | by, (x | y).ToBigInteger());
                Assert.AreEqual(bx ^ by, (x ^ y).ToBigInteger());
            }
        }
    }

    [TestMethod]
    public void Int256_TextAndByteRoundTrips_ForBoundaryValues()
    {
        byte[] buffer = new byte[80];
        foreach (BigInteger bx in SignedBoundaryValues())
        {
            int256 x = (int256)bx;

            Assert.AreEqual(bx.ToString(CultureInfo.InvariantCulture), x.ToString(), $"dec {bx}");
            Assert.AreEqual(x, int256.Parse(x.ToString()), $"dec round trip {bx}");
            Assert.AreEqual(x, int256.Parse(x.ToString("0x", CultureInfo.InvariantCulture)), $"hex round trip {bx}");
            Assert.AreEqual(x, int256.Parse(x.ToString("X", CultureInfo.InvariantCulture).Insert(0, "0x")), $"upper hex round trip {bx}");

            Span<byte> utf8 = buffer;
            foreach (string format in new[] { "D", "0x" })
            {
                Assert.IsTrue(x.TryFormat(utf8, out int written, format));
                Assert.IsTrue(int256.TryParse(utf8.Slice(0, written), out int256 fromUtf8), $"{format} {bx}");
                Assert.AreEqual(x, fromUtf8);
                Assert.AreEqual(x, int256.Parse(Encoding.ASCII.GetString(utf8.Slice(0, written))));
            }

            Assert.AreEqual(x, new int256(x.ToBigEndianBytes()));
            Assert.AreEqual(bx, new BigInteger(x.ToLittleEndianBytes(), isUnsigned: false, isBigEndian: false));
        }
    }

    [TestMethod]
    public void Int256_ExplicitToLong_MatchesRange_ForBoundaryValues()
    {
        foreach (BigInteger bx in SignedBoundaryValues())
        {
            int256 x = (int256)bx;
            if (bx >= long.MinValue && bx <= long.MaxValue)
                Assert.AreEqual((long)bx, (long)x, bx.ToString());
            else
                Assert.ThrowsExactly<OverflowException>(() => _ = (long)x, bx.ToString());
        }
    }

    [TestMethod]
    public void Int256_ConstructFromLimbs_MatchesBigIntegerTwosComplement()
    {
        var rng = new Random(20260103);
        for (int i = 0; i < 500; i++)
        {
            ulong u0 = SparseLimb(rng), u1 = SparseLimb(rng), u2 = SparseLimb(rng), u3 = SparseLimb(rng);
            BigInteger unsigned = FromLimbs(u0, u1, u2, u3);
            Assert.AreEqual(WrapSigned(unsigned), new int256(u0, u1, u2, u3).ToBigInteger());
            Assert.AreEqual(unsigned, new uint256(u0, u1, u2, u3).ToBigInteger());
        }
    }
}
