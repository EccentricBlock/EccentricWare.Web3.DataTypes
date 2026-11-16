using System.Numerics;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Benchmarks;

/// <summary>Deterministic input generators so runs are comparable across machines and commits.</summary>
internal static class BenchmarkData
{
    public const int Seed = 20240607;

    /// <summary>Uniform 64-bit value. <c>(ulong)Random.NextInt64()</c> never sets bit 63, which would hide carry and sign paths.</summary>
    public static ulong NextUInt64(Random random) => (ulong)random.NextInt64() ^ ((ulong)random.NextInt64() << 1);

    public static byte[] RandomBytes(int length, int seed = Seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    public static string HexLower(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(bytes);

    public static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    /// <summary>
    /// Calldata-shaped payload: an <c>approve(address,uint256)</c> selector followed by random bytes.
    /// </summary>
    public static byte[] Calldata(int totalLength, int seed = Seed)
    {
        byte[] bytes = RandomBytes(totalLength, seed);
        if (totalLength >= 4)
        {
            bytes[0] = 0x09; bytes[1] = 0x5e; bytes[2] = 0xa7; bytes[3] = 0xb3; // approve(address,uint256)
        }
        return bytes;
    }

    /// <summary>
    /// approve(spender, type(uint256).max): the most common calldata on Ethereum. Only the spender
    /// (middle of the payload) differs between instances; head, tail and length are identical.
    /// </summary>
    public static byte[] ApproveMaxCalldata(int spenderSeed)
    {
        var bytes = new byte[68];
        bytes[0] = 0x09; bytes[1] = 0x5e; bytes[2] = 0xa7; bytes[3] = 0xb3;
        new Random(spenderSeed).NextBytes(bytes.AsSpan(16, 20)); // 20-byte address inside word 0
        bytes.AsSpan(36).Fill(0xFF);
        return bytes;
    }

    private const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>
    /// Reference Base58 encoder (Bitcoin/Solana alphabet) built on <see cref="BigInteger"/>. Deliberately independent of
    /// the library so parse benchmarks and their round-trip checks do not validate the library against itself.
    /// </summary>
    public static string Base58(ReadOnlySpan<byte> bytes)
    {
        int leadingZeros = 0;
        while (leadingZeros < bytes.Length && bytes[leadingZeros] == 0)
            leadingZeros++;

        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var digits = new StringBuilder();
        while (!value.IsZero)
        {
            value = BigInteger.DivRem(value, 58, out BigInteger remainder);
            digits.Insert(0, Base58Alphabet[(int)remainder]);
        }

        return new string('1', leadingZeros) + digits;
    }
}
