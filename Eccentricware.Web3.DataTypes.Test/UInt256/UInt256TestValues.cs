using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Shared inputs and oracle helpers for the uint256 tests. Conversions go through the limbs, so they do not depend on
/// the uint256 BigInteger conversions that some of the tests verify.
/// </summary>
internal static class UInt256TestValues
{
    public static readonly BigInteger Two256 = BigInteger.One << 256;

    /// <summary>Values on every limb boundary, plus the extremes.</summary>
    public static readonly BigInteger[] Boundaries =
    [
        0, 1, 2,
        (BigInteger.One << 64) - 1, BigInteger.One << 64, (BigInteger.One << 64) + 1,
        (BigInteger.One << 128) - 1, BigInteger.One << 128,
        (BigInteger.One << 192) - 1, BigInteger.One << 192,
        BigInteger.One << 255, Two256 - 1,
    ];

    private static readonly ulong[] InterestingLimbs =
    [
        0UL, 1UL, 2UL, 3UL, 0x7FFF_FFFF_FFFF_FFFFUL, 0x8000_0000_0000_0000UL, 0x8000_0000_0000_0001UL,
        0xFFFF_FFFF_FFFF_FFFEUL, ulong.MaxValue, 0x0000_0000_FFFF_FFFFUL, 0x0000_0001_0000_0000UL,
        0xFFFF_FFFF_0000_0000UL, 0x5555_5555_5555_5555UL, 0xAAAA_AAAA_AAAA_AAAAUL,
    ];

    /// <summary>The value built from limbs, never through a BigInteger conversion of uint256.</summary>
    public static uint256 FromBig(BigInteger value)
    {
        BigInteger mask = ulong.MaxValue;
        return new uint256((ulong)(value & mask), (ulong)((value >> 64) & mask), (ulong)((value >> 128) & mask), (ulong)((value >> 192) & mask));
    }

    public static BigInteger ToBig(uint256 value)
        => value.U0 | ((BigInteger)value.U1 << 64) | ((BigInteger)value.U2 << 128) | ((BigInteger)value.U3 << 192);

    public static ulong SparseLimb(Random rng)
        => rng.Next(3) == 0 ? (ulong)rng.NextInt64() ^ ((ulong)rng.NextInt64() << 1) : InterestingLimbs[rng.Next(InterestingLimbs.Length)];

    /// <summary>A value with 1 to 4 populated limbs, so every operand shape (single limb, wide, sparse) occurs.</summary>
    public static uint256 SparseValue(Random rng)
    {
        int limbs = rng.Next(1, 5);
        return new uint256(SparseLimb(rng), limbs > 1 ? SparseLimb(rng) : 0, limbs > 2 ? SparseLimb(rng) : 0, limbs > 3 ? SparseLimb(rng) : 0);
    }
}
