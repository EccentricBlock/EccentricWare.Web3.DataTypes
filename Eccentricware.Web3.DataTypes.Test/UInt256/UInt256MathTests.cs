using EccentricWare.Web3.DataTypes.Utils;
using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Direct tests of the internal multiply/divide core against <see cref="BigInteger"/>.</summary>
[TestClass]
public sealed class UInt256MathTests
{
    private static readonly BigInteger Two256 = BigInteger.One << 256;

    private static readonly ulong[] InterestingLimbs =
    [
        0UL, 1UL, 2UL, 3UL, 0x7FFF_FFFF_FFFF_FFFFUL, 0x8000_0000_0000_0000UL, 0x8000_0000_0000_0001UL,
        0xFFFF_FFFF_FFFF_FFFEUL, ulong.MaxValue, 0x0000_0000_FFFF_FFFFUL, 0x0000_0001_0000_0000UL,
        0xFFFF_FFFF_0000_0000UL, 0x5555_5555_5555_5555UL, 0xAAAA_AAAA_AAAA_AAAAUL,
    ];

    private static ulong Limb(Random rng) => rng.Next(3) == 0 ? (ulong)rng.NextInt64() ^ ((ulong)rng.NextInt64() << 1) : InterestingLimbs[rng.Next(InterestingLimbs.Length)];

    private static uint256 RandomValue(Random rng)
    {
        // Vary the number of populated limbs so every (dividend limbs, divisor limbs) shape occurs.
        int limbs = rng.Next(1, 5);
        return new uint256(Limb(rng), limbs > 1 ? Limb(rng) : 0, limbs > 2 ? Limb(rng) : 0, limbs > 3 ? Limb(rng) : 0);
    }

    [TestMethod]
    public void Fuzz_DivRem_256By256_MatchesBigInteger()
    {
        var rng = new Random(424242);
        for (int i = 0; i < 300_000; i++)
        {
            uint256 u = RandomValue(rng);
            uint256 v = RandomValue(rng);
            if (v.IsZero) continue;

            UInt256Math.DivRem(in u, in v, out uint256 q, out uint256 r);

            BigInteger bu = u.ToBigInteger(), bv = v.ToBigInteger();
            BigInteger expectedQ = BigInteger.DivRem(bu, bv, out BigInteger expectedR);
            Assert.AreEqual(expectedQ, q.ToBigInteger(), $"{bu} / {bv}");
            Assert.AreEqual(expectedR, r.ToBigInteger(), $"{bu} % {bv}");
        }
    }

    // Operand pairs found by instrumenting the algorithm: they reach the rare paths of Knuth's algorithm D.
    // clamp = the estimated quotient limb saturates at 2^64 - 1; add-back = the estimate was still one too large after
    // refinement and the divisor is added back. Random data reaches these about once per 20,000 divisions.
    [TestMethod]
    [DataRow("0x10000000000000002ca099a46d0b3fe8c0000000100000000", "0x80000000000000018000000000000001")]
    [DataRow("0x20000000000000003ffffffff00000000fffffffffffffffe", "0x5555555555555555fffffffffffffffe")]
    [DataRow("0x5555555555555555000000000000000000000001000000000000000000000000", "0xaaaaaaaaaaaaaaaa0000000000000000ffffffffffffffff")]
    [DataRow("0x7fffffffffffffff800000000000000000000001000000008000000000000001", "0x8000000000000000800000000000000143cf5577e391ed5f")]
    [DataRow("0xffffffff0000000080000000000000007fffffffffffffff5555555555555555", "0x800000000000000000000000000000007d41330506085767")]
    public void DivRem_KnuthRarePaths_ClampAndAddBack_MatchBigInteger(string dividendHex, string divisorHex)
    {
        uint256 u = uint256.Parse(dividendHex);
        uint256 v = uint256.Parse(divisorHex);

        UInt256Math.DivRem(in u, in v, out uint256 q, out uint256 r);

        BigInteger expectedQ = BigInteger.DivRem(u.ToBigInteger(), v.ToBigInteger(), out BigInteger expectedR);
        Assert.AreEqual(expectedQ, q.ToBigInteger());
        Assert.AreEqual(expectedR, r.ToBigInteger());
    }

    [TestMethod]
    public void Fuzz_DivRem_ByUInt64_MatchesBigInteger()
    {
        var rng = new Random(1337);
        for (int i = 0; i < 100_000; i++)
        {
            uint256 u = RandomValue(rng);
            ulong d = Limb(rng);
            if (d == 0) continue;

            ulong rem = UInt256Math.DivRem(in u, d, out uint256 q);
            BigInteger expectedQ = BigInteger.DivRem(u.ToBigInteger(), d, out BigInteger expectedR);
            Assert.AreEqual(expectedQ, q.ToBigInteger(), $"{u.ToBigInteger()} / {d}");
            Assert.AreEqual(expectedR, new BigInteger(rem));
        }
    }

    [TestMethod]
    public void Fuzz_Multiply_LowAndChecked_MatchBigInteger()
    {
        var rng = new Random(9001);
        for (int i = 0; i < 300_000; i++)
        {
            uint256 a = RandomValue(rng);
            uint256 b = RandomValue(rng);
            BigInteger product = a.ToBigInteger() * b.ToBigInteger();

            Assert.AreEqual(product % Two256, UInt256Math.MultiplyLow(in a, in b).ToBigInteger(), $"{a} * {b} (low)");

            bool ok = UInt256Math.TryMultiply(in a, in b, out uint256 checkedResult);
            Assert.AreEqual(product < Two256, ok, $"{a.ToBigInteger()} * {b.ToBigInteger()} (overflow flag)");
            if (ok)
                Assert.AreEqual(product, checkedResult.ToBigInteger());
        }
    }

    [TestMethod]
    public void Fuzz_TryMultiplyAdd_MatchesBigInteger()
    {
        var rng = new Random(77);
        for (int i = 0; i < 100_000; i++)
        {
            uint256 value = RandomValue(rng);
            ulong factor = rng.Next(4) == 0 ? UInt256Math.TenPow19 : Limb(rng);
            ulong addend = Limb(rng);

            ulong u0 = value.U0, u1 = value.U1, u2 = value.U2, u3 = value.U3;
            bool ok = UInt256Math.TryMultiplyAdd(ref u0, ref u1, ref u2, ref u3, factor, addend);

            BigInteger expected = value.ToBigInteger() * factor + addend;
            Assert.AreEqual(expected < Two256, ok);
            if (ok)
                Assert.AreEqual(expected, new uint256(u0, u1, u2, u3).ToBigInteger());
        }
    }

    [TestMethod]
    public void DivRem128ByTenPow19_MatchesUInt128_IncludingEdges()
    {
        const ulong T = UInt256Math.TenPow19;
        ulong[] highs = [0, 1, 2, T / 2, T - 2, T - 1, 12345678901234567UL, 0x7FFF_FFFF_FFFF_FFFFUL];
        ulong[] lows = [0, 1, T - 1, T, T + 1, ulong.MaxValue, ulong.MaxValue - 1, 0x8000_0000_0000_0000UL];
        foreach (ulong high in highs)
        {
            foreach (ulong low in lows)
            {
                if (high >= T) continue;
                ulong q = UInt256Math.DivRem128ByTenPow19(high, low, out ulong r);
                UInt128 n = new(high, low);
                Assert.AreEqual(n / T, (UInt128)q, $"{high}:{low}");
                Assert.AreEqual(n % T, (UInt128)r, $"{high}:{low}");
            }
        }

        var rng = new Random(19);
        for (int i = 0; i < 300_000; i++)
        {
            ulong high = (ulong)rng.NextInt64() % T;
            ulong low = (ulong)rng.NextInt64() ^ ((ulong)rng.NextInt64() << 1);
            ulong q = UInt256Math.DivRem128ByTenPow19(high, low, out ulong r);
            UInt128 n = new(high, low);
            Assert.AreEqual(n / T, (UInt128)q);
            Assert.AreEqual(n % T, (UInt128)r);
        }
    }

    [TestMethod]
    public void DivRem128By64_MatchesUInt128()
    {
        var rng = new Random(5);
        for (int i = 0; i < 100_000; i++)
        {
            ulong d = Limb(rng);
            if (d == 0) continue;
            ulong high = Limb(rng) % d;
            ulong low = Limb(rng);

            ulong q = UInt256Math.DivRem128By64(high, low, d, out ulong r);
            UInt128 n = new(high, low);
            Assert.AreEqual(n / d, (UInt128)q);
            Assert.AreEqual(n % d, (UInt128)r);
        }
    }
}
