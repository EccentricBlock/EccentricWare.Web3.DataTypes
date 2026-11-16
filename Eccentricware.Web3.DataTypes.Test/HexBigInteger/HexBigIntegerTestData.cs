using System.Globalization;
using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Shared oracle helpers for the <see cref="HexBigInteger"/> tests. <see cref="BigInteger"/> is the oracle, never the library.</summary>
internal static class HexBigIntegerTestData
{
    // Values chosen to straddle sign bits, limb boundaries and nibble parity.
    public static readonly BigInteger[] Interesting =
    [
        0, 1, 7, 8, 15, 16, 127, 128, 255, 256, 4095, 4096, 0x7FFF, 0x8000, 0xFFFF,
        0x7FFFFFFF, 0x80000000, 0xFFFFFFFF, 0x100000000,
        long.MaxValue, (BigInteger)long.MaxValue + 1, ulong.MaxValue, (BigInteger)ulong.MaxValue + 1,
        BigInteger.Pow(2, 127) - 1, BigInteger.Pow(2, 127), BigInteger.Pow(2, 128),
        BigInteger.Pow(2, 255), BigInteger.Pow(2, 256) - 1, BigInteger.Pow(2, 256),
        BigInteger.Pow(10, 77), BigInteger.Pow(2, 2000) + 12345,
    ];

    public static IEnumerable<BigInteger> InterestingSigned()
    {
        foreach (BigInteger v in Interesting)
        {
            yield return v;
            if (!v.IsZero)
                yield return -v;
        }
    }

    /// <summary>Minimal hex rendering per the documented format: sign, optional lowercase "0x", no leading zero digits.</summary>
    public static string ReferenceHex(BigInteger value, bool upper = false, bool prefix = true)
    {
        if (value.IsZero)
            return prefix ? "0x0" : "0";

        string digits = BigInteger.Abs(value).ToString(upper ? "X" : "x", CultureInfo.InvariantCulture).TrimStart('0');
        return (value.Sign < 0 ? "-" : "") + (prefix ? "0x" : "") + digits;
    }

    public static BigInteger RandomMagnitude(Random random, int bytes)
    {
        byte[] buffer = new byte[bytes];
        random.NextBytes(buffer);
        buffer[0] |= 0x80; // keep the top bit set: exactly the shape the sign-bit bug needs
        return new BigInteger(buffer, isUnsigned: true, isBigEndian: true);
    }
}
