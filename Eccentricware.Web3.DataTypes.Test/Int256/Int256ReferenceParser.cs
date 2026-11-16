using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Independent reference for the documented int256 text grammar, built from <see cref="BigInteger"/> arithmetic only
/// (digit-by-digit accumulation; no library parsing code is shared):
/// optional '+'/'-', then either decimal digits or "0x"/"0X" plus up to 64 hex digits. Hex without a '-' sign and with
/// exactly 64 digits is raw two's complement; a '-' hex value is a magnitude of at most 2^255; decimal must fit int256.
/// </summary>
internal static class Int256ReferenceParser
{
    public enum Kind { Invalid, Valid, Unspecified }

    public readonly record struct Outcome(Kind Kind, BigInteger Value)
    {
        public static readonly Outcome Invalid = new(Kind.Invalid, BigInteger.Zero);
        public static readonly Outcome Unspecified = new(Kind.Unspecified, BigInteger.Zero);
        public static Outcome Valid(BigInteger v) => new(Kind.Valid, v);
    }

    public static bool IsAsciiWhitespace(char c) => c is ' ' or '\t' or '\r' or '\n';

    private static string Trim(string s, Func<char, bool> isWhitespace)
    {
        int start = 0, end = s.Length;
        while (start < end && isWhitespace(s[start])) start++;
        while (end > start && isWhitespace(s[end - 1])) end--;
        return s[start..end];
    }

    private static bool Accumulate(string digits, int radix, out BigInteger value)
    {
        value = BigInteger.Zero;
        foreach (char c in digits)
        {
            int d = c is >= '0' and <= '9' ? c - '0'
                  : radix == 16 && c is >= 'a' and <= 'f' ? c - 'a' + 10
                  : radix == 16 && c is >= 'A' and <= 'F' ? c - 'A' + 10
                  : -1;
            if (d < 0) return false;
            value = value * radix + d;
        }

        return true;
    }

    /// <summary>Reference for <c>TryParse</c> (hex and decimal). Whitespace is trimmed at both ends only.</summary>
    public static Outcome Parse(string text, Func<char, bool> isWhitespace)
    {
        string s = Trim(text, isWhitespace);
        if (s.Length == 0) return Outcome.Invalid;

        bool negative = s[0] == '-';
        bool explicitPlus = s[0] == '+';
        string body = negative || explicitPlus ? s[1..] : s;

        if (body.Length >= 2 && body[0] == '0' && (body[1] == 'x' || body[1] == 'X'))
        {
            string digits = body[2..];
            if (digits.Length == 0)
                return negative ? Outcome.Unspecified : Outcome.Valid(BigInteger.Zero); // "-0x" is a quirk: not pinned
            if (digits.Length > 64 || !Accumulate(digits, 16, out BigInteger v)) return Outcome.Invalid;

            if (negative)
                return v <= Int256TestValues.Two255 ? Outcome.Valid(-v) : Outcome.Invalid;
            if (digits.Length == 64)
            {
                if (explicitPlus) return Outcome.Unspecified; // "+0x" + 64 digits reads as raw two's complement: not pinned
                return Outcome.Valid(v >= Int256TestValues.Two255 ? v - Int256TestValues.Two256 : v);
            }

            return Outcome.Valid(v);
        }

        if (body.Length == 0 || !Accumulate(body, 10, out BigInteger d))
            return Outcome.Invalid;

        if (negative)
            return d <= Int256TestValues.Two255 ? Outcome.Valid(-d) : Outcome.Invalid;
        return d < Int256TestValues.Two255 ? Outcome.Valid(d) : Outcome.Invalid;
    }

    /// <summary>Reference for <c>TryParseDecimal</c>: decimal only, whitespace after the sign is trimmed too.</summary>
    public static Outcome ParseDecimalOnly(string text, Func<char, bool> isWhitespace)
    {
        string s = Trim(text, isWhitespace);
        if (s.Length == 0) return Outcome.Invalid;

        bool negative = s[0] == '-';
        string body = negative || s[0] == '+' ? s[1..] : s;
        if (body.Length > 0 && isWhitespace(body[0])) return Outcome.Unspecified; // "- 5": differs from TryParse, not pinned
        if (body.Length == 0 || !Accumulate(body, 10, out BigInteger d)) return Outcome.Invalid;

        if (negative)
            return d <= Int256TestValues.Two255 ? Outcome.Valid(-d) : Outcome.Invalid;
        return d < Int256TestValues.Two255 ? Outcome.Valid(d) : Outcome.Invalid;
    }

    /// <summary>Lower-case hex of a non-negative value without leading zeros ("0" for zero), or padded to <paramref name="width"/>.</summary>
    public static string Hex(BigInteger nonNegative, int width = 0)
    {
        byte[] bytes = new byte[33];
        byte[] raw = nonNegative.ToByteArray(isUnsigned: true, isBigEndian: true);
        raw.CopyTo(bytes, 33 - raw.Length);
        string hex = Convert.ToHexString(bytes).ToLowerInvariant().TrimStart('0');
        if (hex.Length == 0) hex = "0";
        return width > hex.Length ? new string('0', width - hex.Length) + hex : hex;
    }
}
