using System.Numerics;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Shared fixtures and independent oracles for the Hash32 test classes.</summary>
internal static class Hash32TestData
{
    public const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>The bytes 00 01 02 ... 1F (big-endian order).</summary>
    public static byte[] Bytes0To31()
    {
        var bytes = new byte[32];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)i;
        return bytes;
    }

    public static Hash32 Value0To31() => new(Bytes0To31());

    public static string Hex(byte[] bytes, bool uppercase = false)
        => uppercase ? Convert.ToHexString(bytes) : Convert.ToHexStringLower(bytes);

    /// <summary>A reversed copy (the original is untouched).</summary>
    public static byte[] Reversed(byte[] bytes) => Enumerable.Reverse(bytes).ToArray();

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Builds a hash from limbs given most significant first.</summary>
    public static Hash32 FromLimbs(ulong[] limbs) => new(limbs[0], limbs[1], limbs[2], limbs[3]);

    /// <summary>A hash whose limbs all equal <paramref name="filler"/> except limb <paramref name="index"/> (0 = most significant).</summary>
    public static Hash32 WithLimb(int index, ulong value, ulong filler)
    {
        ulong[] limbs = [filler, filler, filler, filler];
        limbs[index] = value;
        return FromLimbs(limbs);
    }

    /// <summary>Byte-wise (memcmp-style) comparison of the big-endian encodings: the independent ordering oracle.</summary>
    public static int OracleCompare(Hash32 left, Hash32 right)
        => Math.Sign(left.ToBigEndianBytes().AsSpan().SequenceCompareTo(right.ToBigEndianBytes()));

    public static Hash32[] RandomHashes(Random random, int count)
    {
        var result = new Hash32[count];
        var buffer = new byte[Hash32.ByteLength];
        for (int i = 0; i < count; i++)
        {
            random.NextBytes(buffer);
            result[i] = new Hash32(buffer);
        }

        return result;
    }

    /// <summary>Independent reference encoder (BigInteger), so parser tests do not depend on library code.</summary>
    public static string Base58Encode(byte[] bytes)
    {
        int leadingZeros = 0;
        while (leadingZeros < bytes.Length && bytes[leadingZeros] == 0)
            leadingZeros++;

        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var builder = new StringBuilder();
        while (value > 0)
        {
            value = BigInteger.DivRem(value, 58, out BigInteger remainder);
            builder.Insert(0, Base58Alphabet[(int)remainder]);
        }

        return new string('1', leadingZeros) + builder;
    }
}
