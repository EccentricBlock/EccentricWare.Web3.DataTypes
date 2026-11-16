using System.Numerics;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Deterministic inputs, independent known-answer vectors and a reference Base58 encoder for the Signature tests.</summary>
internal static class SignatureTestData
{
    private const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>Example Solana transaction signature (88 characters). Its decoding is <see cref="SolanaDocsHex"/>.</summary>
    public const string SolanaDocsBase58 = "5VERv8NMvzbJMEkV8xnrLkEaWRtSz9CosKDYjCJjBRnbJLgp8uirBgmQpjKhoR4tjF3ZpRzrFmBV6UjKdiSZkQUW";

    /// <summary>Decoding of <see cref="SolanaDocsBase58"/> (top bit set), from an independent python decoder.</summary>
    public const string SolanaDocsHex = "e069ef2fbe487298f22ffecbed57a45de555160d7152f1f9c3269cde05e428fcaa5e622f727c45cbd5446af507ae6394cab8e3a4e0787289698822583b479c0f";

    // Real ECDSA/secp256k1 signatures over SHA-256("eccentricware signature test vector A" / "... B"), produced by an
    // independent python implementation and checked by recovering the signing public key; s is low-s normalised.
    // v = 27 + recovery id, so the v bytes are 0x1b and 0x1c (hex letters in both).
    public const string EvmRealRHex = "e562d9ce4fa1b3af799cf6ea3688159ead6b1459f7d0febaa7cd6c06f4a1cc37";
    public const string EvmRealSHex = "646b6674c2a3eb9cdc24e5fc7a353657ad6f34123c8bfa31238578689834dc8e";
    public const byte EvmRealV = 0x1b;
    public static string EvmRealHex => EvmRealRHex + EvmRealSHex + "1b";

    public const string EvmRealBRHex = "c50558f6b9f74957239b6d63ac7a91c024aa7f480018ede22ba19bcaccfa2d40";
    public const string EvmRealBSHex = "5c7947227d67d442e151cef9803cc2c84036c1f7670ecf12239946cddb93c69f";
    public const byte EvmRealBV = 0x1c;
    public static string EvmRealBHex => EvmRealBRHex + EvmRealBSHex + "1c";

    /// <summary>Hex and Base58 pairs for 64-byte values, all produced by an independent python Base58 implementation.</summary>
    public static readonly (string Name, string Hex, string Base58)[] Base58Vectors =
    [
        ("SolanaDocsExample", "e069ef2fbe487298f22ffecbed57a45de555160d7152f1f9c3269cde05e428fcaa5e622f727c45cbd5446af507ae6394cab8e3a4e0787289698822583b479c0f", "5VERv8NMvzbJMEkV8xnrLkEaWRtSz9CosKDYjCJjBRnbJLgp8uirBgmQpjKhoR4tjF3ZpRzrFmBV6UjKdiSZkQUW"),
        ("Rfc8032Test1Signature", "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b", "5awYiUvGiDFA33EJjj4TXJG44a5afJc8QjWRpGgQiu6b23jCr7yndW2fmp9ujwqJVe32J456wV3VF78Asb1obnTc"),
        ("AllZeroBytes", "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000", "1111111111111111111111111111111111111111111111111111111111111111"),
        ("AllOneBits", "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "67rpwLCuS5DGA8KGZXKsVQ7dnPb9goRLoKfgGbLfQg9WoLUgNY77E2jT11fem3coV9nAkguBACzrU1iyZM4B8roQ"),
        ("OnlyLastByteIsOne", "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001", "1111111111111111111111111111111111111111111111111111111111111112"),
        ("OnlyFirstByteIsOne", "01000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000", "2AFv15MNPuA84RmU66xw2uMzGipcVxNpzAffoacGVvjFue3CBmf633fAWuiP9cwL9C3z3CJiGgRSFjJfeEcA6QX"),
        ("OneLeadingZeroByte", "00ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "12AFv15MNPuA84RmU66xw2uMzGipcVxNpzAffoacGVvjFue3CBmf633fAWuiP9cwL9C3z3CJiGgRSFjJfeEcA6QW"),
        ("FiftyEightToThe87Minus1", "32045a018e0969ac37026a05da8d42986f517810f3506af75a4b686b81f28fc858a03f440ee5a840874c0e11fb96c1f6c5a6ea464a7fffffffffffffffffffff", "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz"),
        ("FiftyEightToThe87", "32045a018e0969ac37026a05da8d42986f517810f3506af75a4b686b81f28fc858a03f440ee5a840874c0e11fb96c1f6c5a6ea464a8000000000000000000000", "2111111111111111111111111111111111111111111111111111111111111111111111111111111111111111"),
        ("FiftyEightToThe86", "00dcc3c23bd4063c59396bc24ecd785213930f4fbd930aabfd249c487969b307de37ba7858885f972e787bd42c0fd734ce713901364000000000000000000000", "1211111111111111111111111111111111111111111111111111111111111111111111111111111111111111"),
        ("FiftyEightToThe86Minus1", "00dcc3c23bd4063c59396bc24ecd785213930f4fbd930aabfd249c487969b307de37ba7858885f972e787bd42c0fd734ce713901363fffffffffffffffffffff", "1zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz"),
    ];

    /// <summary>65 bytes 0x00..0x40: each byte equals its offset, so any limb or byte-order mistake shows up.</summary>
    public static byte[] Ascending65()
    {
        var bytes = new byte[65];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)i;
        return bytes;
    }

    public static byte[] Ascending64() => Ascending65().AsSpan(0, 64).ToArray();

    public static string Ascending65Hex => "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f40";

    public static string Ascending64Hex => Ascending65Hex[..128];

    public static Signature SampleEvm => Signature.FromEvmBytes(Ascending65());

    public static Signature SampleSolana => Signature.FromSolanaBytes(Ascending64());

    public static byte[] RandomBytes(Random random, int length)
    {
        var bytes = new byte[length];
        random.NextBytes(bytes);
        return bytes;
    }

    public static byte[] Filled(int length, byte value)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, value);
        return bytes;
    }

    /// <summary>An EVM signature whose r and s are the 64 payload bytes.</summary>
    public static Signature EvmFrom(ReadOnlySpan<byte> payload64, byte v)
    {
        byte[] bytes = new byte[65];
        payload64.CopyTo(bytes);
        bytes[64] = v;
        return Signature.FromEvmBytes(bytes);
    }

    /// <summary>Distinct 64-byte payloads: zeros, ones, ascending, and two seeded random ones.</summary>
    public static IReadOnlyList<byte[]> Payloads64()
    {
        var random = new Random(64064);
        return
        [
            new byte[64],
            Filled(64, 0xFF),
            Ascending64(),
            RandomBytes(random, 64),
            RandomBytes(random, 64),
        ];
    }

    /// <summary>Random signatures of both types; payloads start with 0..3 zero bytes so Base58 leading-'1' handling is exercised.</summary>
    public static IEnumerable<Signature> RandomSignatures(int count, int seed = 20240607)
    {
        var random = new Random(seed);
        for (int i = 0; i < count; i++)
        {
            byte[] bytes = RandomBytes(random, 65);
            int zeros = i % 4;
            bytes.AsSpan(0, zeros).Clear();
            if (bytes[zeros] == 0)
                bytes[zeros] = 1; // keep the leading-zero count exact

            yield return (i & 1) == 0
                ? Signature.FromEvmBytes(bytes)
                : Signature.FromSolanaBytes(bytes.AsSpan(0, 64));
        }
    }

    /// <summary>Reference Base58 encoder on <see cref="BigInteger"/>; independent of the library.</summary>
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

    public static byte[] Utf8(string s) => Encoding.ASCII.GetBytes(s);
}
