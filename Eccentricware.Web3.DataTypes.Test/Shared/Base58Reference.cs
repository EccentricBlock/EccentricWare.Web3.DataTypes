using System.Numerics;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Test-only Base58 reference built on <see cref="BigInteger"/>. Independent of the library codecs; it is itself
/// validated against the published Bitcoin vectors below (see Base58EncoderTests).
/// </summary>
internal static class Base58Reference
{
    public const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>Published Bitcoin Base58 vectors (hex of the bytes, expected text).</summary>
    public static readonly (string Hex, string Text)[] PublishedVectors =
    [
        ("61", "2g"),
        ("626262", "a3gV"),
        ("636363", "aPEr"),
        ("73696d706c792061206c6f6e6720737472696e67", "2cFupjhnEsSn59qHXstmK2ffpLv2"),
        ("516b6fcd0f", "ABnLTmg"),
        ("bf4f89001e670274dd", "3SEo3LWLoPntC"),
        ("572e4794", "3EFU7m"),
        ("ecac89cad93923c02321", "EJDM8drfXA6uyA"),
        ("10c8511e", "Rt5zm"),
        ("00eb15231dfceb60925886b67d065299925915aeb172c06647", "1NS17iag9jJgTHD1VXjvLCEnZuQ3rJDE9L"),
        ("00000000000000000000", "1111111111"),
    ];

    /// <summary>One '1' per leading zero byte, then the base-58 digits of the rest.</summary>
    public static string Encode(ReadOnlySpan<byte> bigEndian)
    {
        int zeros = 0;
        while (zeros < bigEndian.Length && bigEndian[zeros] == 0)
            zeros++;

        var value = new BigInteger(bigEndian, isUnsigned: true, isBigEndian: true);
        var digits = new StringBuilder();
        while (!value.IsZero)
        {
            value = BigInteger.DivRem(value, 58, out BigInteger remainder);
            digits.Insert(0, Alphabet[(int)remainder]);
        }

        return new string('1', zeros) + digits;
    }

    /// <summary>
    /// Canonical decode to exactly <paramref name="byteLength"/> bytes: null for an invalid digit, an overflowing value
    /// or a leading-'1' count that differs from the leading-zero-byte count.
    /// </summary>
    public static byte[]? DecodeCanonical(string text, int byteLength)
    {
        int ones = 0;
        while (ones < text.Length && text[ones] == '1')
            ones++;

        BigInteger value = BigInteger.Zero;
        foreach (char c in text)
        {
            int digit = Alphabet.IndexOf(c);
            if (digit < 0)
                return null;
            value = value * 58 + digit;
        }

        byte[] body = value.IsZero ? [] : value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (ones + body.Length != byteLength)
            return null;

        byte[] result = new byte[byteLength];
        body.CopyTo(result, ones);
        return result;
    }

    /// <summary>Splits big-endian bytes (length a multiple of 4) into 32-bit limbs, most significant first.</summary>
    public static uint[] ToBigEndianLimbs(ReadOnlySpan<byte> bytes)
    {
        uint[] limbs = new uint[bytes.Length / 4];
        for (int i = 0; i < limbs.Length; i++)
            limbs[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(i * 4, 4));
        return limbs;
    }
}
