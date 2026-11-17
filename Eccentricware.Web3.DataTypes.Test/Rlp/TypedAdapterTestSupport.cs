using System.Numerics;
using EccentricWare.Web3.DataTypes.Rlp;
using static EccentricWare.Web3.DataTypes.Tests.Rlp.RlpReaderTestSupport;

namespace EccentricWare.Web3.DataTypes.Tests.Rlp;

/// <summary>Shared helpers for the typed-adapter tests. Expected values come from <see cref="BigInteger"/> and the oracle, never from the adapters.</summary>
internal static class TypedAdapterTestSupport
{
    public static readonly BigInteger TwoPow256 = BigInteger.One << 256;

    /// <summary>The oracle's canonical encoding of <paramref name="payload"/> as one byte string.</summary>
    public static byte[] EncodeString(byte[] payload) => RlpReference.Encode(RlpNode.Bytes(payload));

    /// <summary>A decoded one-item document holding <paramref name="payload"/> as a byte string (the backing array stays reachable through the view).</summary>
    public static RLPData StringItem(byte[] payload) => DecodeOk(EncodeString(payload));

    /// <summary>A decoded document from spaced or unspaced hex.</summary>
    public static RLPData HexItem(string hex) => DecodeOk(H(hex));

    /// <summary>A decoded list item holding the given byte strings.</summary>
    public static RLPData ListItem(params byte[][] payloads) =>
        DecodeOk(RlpReference.Encode(RlpNode.ListOf(payloads.Select(p => RlpNode.Bytes(p)))));

    /// <summary>Shortest big-endian magnitude of a non-negative integer; empty for zero.</summary>
    public static byte[] Minimal(BigInteger value) =>
        value.IsZero ? [] : value.ToByteArray(isUnsigned: true, isBigEndian: true);

    public static byte[] Padded32(BigInteger value)
    {
        var minimal = Minimal(value);
        var padded = new byte[32];
        minimal.CopyTo(padded, 32 - minimal.Length);
        return padded;
    }

    /// <summary>Deterministic bytes: <paramref name="first"/> then an incrementing pattern (never starts with zero unless asked).</summary>
    public static byte[] Pattern(int length, byte first = 0x11)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(first + i * 7);
        }

        if (length > 0)
        {
            bytes[0] = first;
        }

        return bytes;
    }

    /// <summary>0,1,0x7f,0x80,0xff,0x100, every byte-width boundary (2^8n - 1, 2^8n, 2^8n + 1), every limb boundary, 2^255 and 2^256 - 1.</summary>
    public static List<BigInteger> BoundaryValues()
    {
        var values = new HashSet<BigInteger> { 0, 1, 0x7f, 0x80, 0xff, 0x100, BigInteger.One << 255, TwoPow256 - 1 };
        for (var bytes = 1; bytes <= 32; bytes++)
        {
            var power = BigInteger.One << (8 * bytes);
            values.Add(power - 1);
            values.Add(power);
            values.Add(power + 1);
        }

        for (var limb = 1; limb <= 3; limb++)
        {
            var power = BigInteger.One << (64 * limb);
            values.Add(power - 1);
            values.Add(power);
            values.Add(power + 1);
        }

        values.RemoveWhere(v => v >= TwoPow256);
        return values.OrderBy(v => v).ToList();
    }

    /// <summary>Random 32-byte value with a random number of leading zero bytes, so every minimal width occurs.</summary>
    public static BigInteger RandomUInt256(Random rng)
    {
        var bytes = new byte[32];
        rng.NextBytes(bytes);
        var zeroPrefix = rng.Next(0, 33);
        Array.Clear(bytes, 0, zeroPrefix);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
    }
}
