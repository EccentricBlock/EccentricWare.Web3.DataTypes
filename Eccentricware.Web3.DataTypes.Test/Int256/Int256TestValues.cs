using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Shared inputs and an oracle for the int256 tests. The oracle converts through the limbs (<c>U0..U3</c>) and
/// <see cref="BigInteger"/> bit arithmetic only, so it does not depend on <c>ToBigInteger</c>, the explicit
/// <see cref="BigInteger"/> conversion or the byte constructors it is used to check.
/// </summary>
internal static class Int256TestValues
{
    public static readonly BigInteger Two255 = BigInteger.One << 255;
    public static readonly BigInteger Two256 = BigInteger.One << 256;
    private static readonly BigInteger Mask256 = Two256 - 1;
    private static readonly BigInteger LimbMask = ulong.MaxValue;

    public static BigInteger Pow2(int bits) => BigInteger.One << bits;

    public static bool Fits(BigInteger v) => v >= -Two255 && v < Two255;

    /// <summary>Wraps any integer into the signed 256-bit range (mod 2^256).</summary>
    public static BigInteger Wrap(BigInteger v)
    {
        BigInteger m = v & Mask256;
        return m >= Two255 ? m - Two256 : m;
    }

    /// <summary>Builds an <see cref="int256"/> from limbs sliced out of the two's complement bit pattern.</summary>
    public static int256 FromBig(BigInteger v)
    {
        Assert.IsTrue(Fits(v), $"{v} does not fit int256");
        BigInteger m = v & Mask256;
        return new int256((ulong)(m & LimbMask), (ulong)((m >> 64) & LimbMask), (ulong)((m >> 128) & LimbMask), (ulong)((m >> 192) & LimbMask));
    }

    /// <summary>Reads an <see cref="int256"/> back through its limbs.</summary>
    public static BigInteger ToBig(int256 x)
    {
        BigInteger u = (BigInteger)x.U0 | ((BigInteger)x.U1 << 64) | ((BigInteger)x.U2 << 128) | ((BigInteger)x.U3 << 192);
        return u >= Two255 ? u - Two256 : u;
    }

    /// <summary>Signed values at sign, limb and byte boundaries (about 100 values, both signs).</summary>
    public static IReadOnlyList<BigInteger> BoundaryValues { get; } = BuildBoundaryValues();

    private static List<BigInteger> BuildBoundaryValues()
    {
        var magnitudes = new List<BigInteger> { 0, 1, 2, 3, 10, 127, 128, 255, 256, 1_000_000_000_000_000_000UL };
        foreach (int bits in new[] { 8, 32, 63, 64, 65, 127, 128, 129, 191, 192, 193, 247, 248, 254, 255 })
        {
            BigInteger p = Pow2(bits);
            magnitudes.Add(p - 1);
            magnitudes.Add(p);
            magnitudes.Add(p + 1);
        }

        magnitudes.Add(BigInteger.Parse("0ffffffffffffffff0000000000000000ffffffffffffffff0000000000000000", System.Globalization.NumberStyles.HexNumber));
        magnitudes.Add(BigInteger.Parse("00123456789abcdef0fedcba98765432100123456789abcdef0fedcba9876543210", System.Globalization.NumberStyles.HexNumber));

        var values = new List<BigInteger> { -Two255, Two255 - 1, long.MinValue, long.MaxValue, (BigInteger)long.MinValue - 1, (BigInteger)long.MaxValue + 1 };
        foreach (BigInteger m in magnitudes)
        {
            values.Add(m);
            values.Add(-m);
        }

        return values.Where(Fits).Distinct().ToList();
    }

    /// <summary>Limb drawn from {0, 1, max, random}, so carries, borrows and sign flips happen often.</summary>
    public static ulong SparseLimb(Random rng) => rng.Next(4) switch
    {
        0 => 0UL,
        1 => 1UL,
        2 => ulong.MaxValue,
        _ => (ulong)rng.NextInt64() ^ ((ulong)rng.Next(2) << 63),
    };

    public static int256 SparseValue(Random rng) => new(SparseLimb(rng), SparseLimb(rng), SparseLimb(rng), SparseLimb(rng));
}
