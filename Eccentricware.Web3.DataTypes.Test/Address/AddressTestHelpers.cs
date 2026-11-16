using System.Numerics;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Shared fixtures and independent oracles for the Address, AddressSet and AddressBulkMatcher tests. Expected values are
/// built from <see cref="BigInteger"/> and <see cref="Convert"/>, never from the library's own parser or formatter.
/// </summary>
internal static class AddressTestHelpers
{
    public const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    public const string SampleEvmLower = "0x52908400098527886e0f7030069857d2e4169ee7";
    public const string SampleSolana = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";

    public static Address[] RandomEvm(int count, int seed)
    {
        var random = new Random(seed);
        byte[] bytes = new byte[Address.EvmByteLength];
        var result = new Address[count];
        for (int i = 0; i < count; i++)
        {
            random.NextBytes(bytes);
            result[i] = Address.FromEvmBytes(bytes);
        }
        return result;
    }

    public static Address[] RandomSolana(int count, int seed)
    {
        var random = new Random(seed);
        byte[] bytes = new byte[Address.SolanaByteLength];
        var result = new Address[count];
        for (int i = 0; i < count; i++)
        {
            random.NextBytes(bytes);
            result[i] = Address.FromSolanaBytes(bytes);
        }
        return result;
    }

    /// <summary>A Solana address whose 32-byte backing value is identical to <paramref name="evm"/>'s (the last 12 bytes are zero).</summary>
    public static Address SolanaWithSamePayloadAs(Address evm)
    {
        byte[] bytes = new byte[Address.SolanaByteLength];
        evm.WriteBytes(bytes);
        return Address.FromSolanaBytes(bytes);
    }

    public static byte[] Utf8(string text) => Encoding.ASCII.GetBytes(text);

    public static byte[] Quoted(string text) => Encoding.ASCII.GetBytes("\"" + text + "\"");

    public static byte[] Sequence(int length, Func<int, byte> value)
    {
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
            bytes[i] = value(i);
        return bytes;
    }

    /// <summary>A buffer of <paramref name="length"/> elements, all <paramref name="sentinel"/>.</summary>
    public static T[] Filled<T>(int length, T sentinel)
    {
        T[] buffer = new T[length];
        Array.Fill(buffer, sentinel);
        return buffer;
    }

    public static bool IsAsciiHexDigit(char c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    // ----------------- Base58 reference (BigInteger) -----------------

    public static string ReferenceEncode(byte[] bytes)
    {
        int leadingZeroBytes = 0;
        while (leadingZeroBytes < bytes.Length && bytes[leadingZeroBytes] == 0)
            leadingZeroBytes++;

        BigInteger value = leadingZeroBytes == bytes.Length
            ? BigInteger.Zero
            : new BigInteger(bytes.AsSpan(leadingZeroBytes), isUnsigned: true, isBigEndian: true);

        return ReferenceEncode(value, leadingZeroBytes);
    }

    public static string ReferenceEncode(BigInteger value, int leadingZeroBytes)
    {
        var digits = new StringBuilder();
        while (value > 0)
        {
            value = BigInteger.DivRem(value, 58, out BigInteger remainder);
            digits.Insert(0, Base58Alphabet[(int)remainder]);
        }

        return new string('1', leadingZeroBytes) + digits;
    }

    /// <summary>
    /// Canonical Base58 of exactly 32 bytes: every character in the alphabet, value below 2^256, and one leading '1' per
    /// leading zero byte.
    /// </summary>
    public static bool TryReferenceDecode32(string text, out byte[] bytes)
    {
        bytes = [];
        BigInteger value = BigInteger.Zero;
        foreach (char c in text)
        {
            int digit = Base58Alphabet.IndexOf(c);
            if (digit < 0)
                return false;
            value = value * 58 + digit;
        }

        int leadingOnes = text.TakeWhile(c => c == '1').Count();
        if (value >= BigInteger.Pow(2, 256))
            return false;

        int significantBytes = value.IsZero ? 0 : value.GetByteCount(isUnsigned: true);
        if (leadingOnes + significantBytes != Address.SolanaByteLength)
            return false;

        bytes = new byte[Address.SolanaByteLength];
        if (significantBytes > 0)
            Assert.IsTrue(value.TryWriteBytes(bytes.AsSpan(leadingOnes), out _, isUnsigned: true, isBigEndian: true));
        return true;
    }

    // ----------------- Whole-parser oracle -----------------

    /// <summary>
    /// The documented parse rules, written independently of the library: <c>0x</c>/<c>0X</c> + 40 ASCII hex digits is EVM;
    /// otherwise canonical Base58 of 32 bytes is Solana (so it wins over bare hex); otherwise exactly 40 ASCII hex digits
    /// is EVM; anything else is rejected.
    /// </summary>
    public static bool TryExpectedParse(string text, out AddressType type, out byte[] bytes)
    {
        type = AddressType.Evm;
        bytes = [];

        if (text.Length == 42 && text[0] == '0' && (text[1] == 'x' || text[1] == 'X') && text.Skip(2).All(IsAsciiHexDigit))
        {
            bytes = Convert.FromHexString(text.Substring(2));
            return true;
        }

        if (TryReferenceDecode32(text, out bytes))
        {
            type = AddressType.Solana;
            return true;
        }

        if (text.Length == 40 && text.All(IsAsciiHexDigit))
        {
            bytes = Convert.FromHexString(text);
            return true;
        }

        bytes = [];
        return false;
    }

    /// <summary>
    /// Asserts a parse outcome against the oracle: same verdict, same family and bytes on success, and
    /// <c>default(Address)</c> on failure.
    /// </summary>
    public static void AssertParseOutcome(string route, string text, bool actualOk, Address actual, bool expectedOk, AddressType expectedType, byte[] expectedBytes)
    {
        string context = $"{route}: '{text}'";
        Assert.AreEqual(expectedOk, actualOk, context);
        if (expectedOk)
        {
            Assert.AreEqual(expectedType, actual.Type, context);
            CollectionAssert.AreEqual(expectedBytes, actual.ToBytes(), context);
        }
        else
        {
            Assert.AreEqual(default, actual, context);
        }
    }
}
