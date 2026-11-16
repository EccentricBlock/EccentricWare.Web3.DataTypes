using EccentricWare.Web3.DataTypes.JsonConverters;
using EccentricWare.Web3.DataTypes.Utils;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes;

/// <summary>
/// Represents an arbitrarily large signed integer used for blockchain interop (EVM/Solana),
/// supporting JSON-RPC hex quantities (e.g., "0x1a") and negative values (e.g., "-0x1").
/// </summary>
[JsonConverter(typeof(HexBigIntegerJsonConverter))]
public readonly struct HexBigInteger :
    IEquatable<HexBigInteger>,
    IComparable<HexBigInteger>,
    IComparable,
    IFormattable,
    ISpanFormattable,
    IUtf8SpanFormattable,
    ISpanParsable<HexBigInteger>
{
    /// <summary>
    /// Maximum allowed hex digit count when parsing untrusted JSON-RPC tokens.
    /// This limit is primarily for DoS hygiene; adjust per deployment constraints.
    /// </summary>
    public const int DefaultMaxHexDigits = 4096;

    private readonly BigInteger _value;

    /// <summary>Represents the value 0.</summary>
    public static readonly HexBigInteger Zero = BigInteger.Zero;

    /// <summary>Represents the value 1.</summary>
    public static readonly HexBigInteger One = BigInteger.One;

    /// <summary>Represents the value -1.</summary>
    public static readonly HexBigInteger MinusOne = BigInteger.MinusOne;

    #region Constructors

    /// <summary>
    /// Creates an instance from a <see cref="BigInteger"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HexBigInteger(BigInteger value) => _value = value;

    /// <summary>
    /// Creates an instance from a signed 64-bit integer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HexBigInteger(long value) => _value = new BigInteger(value);

    /// <summary>
    /// Creates an instance from an unsigned 64-bit integer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HexBigInteger(ulong value) => _value = new BigInteger(value);

    /// <summary>
    /// Creates an instance from a big-endian byte array representing the magnitude.
    /// </summary>
    /// <param name="bigEndianBytes">Magnitude bytes in big-endian order.</param>
    /// <param name="isUnsigned">True to interpret the input as unsigned magnitude.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HexBigInteger(ReadOnlySpan<byte> bigEndianBytes, bool isUnsigned)
        => _value = new BigInteger(bigEndianBytes, isUnsigned: isUnsigned, isBigEndian: true);

    #endregion

    #region Core properties

    /// <summary>
    /// Gets the underlying <see cref="BigInteger"/> value.
    /// </summary>
    public BigInteger Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value;
    }

    /// <summary>
    /// Gets the sign of the value: -1, 0, or 1.
    /// </summary>
    public int Sign
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value.Sign;
    }

    /// <summary>
    /// Returns true if the value is zero.
    /// </summary>
    public bool IsZero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value.IsZero;
    }

    /// <summary>
    /// Returns true if the value is negative.
    /// </summary>
    public bool IsNegative
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value.Sign < 0;
    }

    /// <summary>
    /// Returns the number of bytes required to represent the magnitude (the absolute value). Zero reports 1, as <see cref="BigInteger.GetByteCount"/> does.
    /// </summary>
    public int MagnitudeByteCount
    {
        // GetByteCount(isUnsigned: true) throws for negative values, so negatives are measured on their absolute value.
        get => _value.Sign >= 0
            ? _value.GetByteCount(isUnsigned: true)
            : BigInteger.Abs(_value).GetByteCount(isUnsigned: true);
    }

    #endregion

    #region Equality and comparison

    /// <summary>
    /// Determines whether this instance equals another.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(HexBigInteger other) => _value.Equals(other._value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is HexBigInteger other && Equals(other);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => _value.GetHashCode();

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(HexBigInteger other) => _value.CompareTo(other._value);

    /// <inheritdoc />
    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        if (obj is HexBigInteger other) return CompareTo(other);
        throw new ArgumentException($"Object must be of type {nameof(HexBigInteger)}", nameof(obj));
    }

    /// <summary>Equality operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(HexBigInteger left, HexBigInteger right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(HexBigInteger left, HexBigInteger right) => !left.Equals(right);

    /// <summary>Less-than operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(HexBigInteger left, HexBigInteger right) => left._value < right._value;

    /// <summary>Greater-than operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(HexBigInteger left, HexBigInteger right) => left._value > right._value;

    /// <summary>Less-than-or-equal operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <=(HexBigInteger left, HexBigInteger right) => left._value <= right._value;

    /// <summary>Greater-than-or-equal operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >=(HexBigInteger left, HexBigInteger right) => left._value >= right._value;

    #endregion

    #region Conversions

    /// <summary>Implicit conversion from <see cref="BigInteger"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator HexBigInteger(BigInteger value) => new(value);

    /// <summary>Implicit conversion to <see cref="BigInteger"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator BigInteger(HexBigInteger value) => value._value;

    /// <summary>
    /// Converts to uint256 (throws if negative or exceeds 256 bits).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint256 ToUInt256() => (uint256)_value;

    #endregion


    #region Parsing (UTF-8: bounded; chars: ISpanParsable)

    /// <summary>Scratch size for decoded magnitude bytes kept on the stack (up to 256 hex digits).</summary>
    private const int StackMagnitudeBytes = 128;

    /// <summary>
    /// Parses a JSON-RPC-like UTF-8 token containing a hex quantity (quoted or unquoted), with DoS bounds.
    /// Accepts: <c>null</c>, <c>"0x..."</c>, <c>0x...</c>, <c>"-0x..."</c>.
    /// A bare <c>0x</c> is zero; an empty token, or a sign with no digits, is rejected.
    /// The sign may be <c>+</c> or <c>-</c> and may be followed by ASCII whitespace (<c>"- 0x1a"</c> is -26). The digit
    /// count is checked against <paramref name="maxHexDigits"/> only; a hex prefix is optional.
    /// </summary>
    /// <param name="utf8Token">UTF-8 token.</param>
    /// <param name="result">Parsed value (zero for <c>null</c>).</param>
    /// <param name="maxHexDigits">Maximum allowed hex digit count; a negative value throws <see cref="ArgumentOutOfRangeException"/> (it used to disable the bound).</param>
    /// <returns>True if parsing succeeded; otherwise false.</returns>
    public static bool TryParseUtf8(ReadOnlySpan<byte> utf8Token, out HexBigInteger result, int maxHexDigits = DefaultMaxHexDigits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxHexDigits);

        ReadOnlySpan<byte> trimmed = ByteUtils.TrimAsciiWhitespaceUtf8(utf8Token);

        if (trimmed.SequenceEqual("null"u8))
        {
            result = default; // zero; presence must be tracked externally if needed.
            return true;
        }

        // Quotes are optional here, unlike the JSON string path, where the reader has already removed them.
        return TryParseQuantityUtf8(ByteUtils.TrimAsciiWhitespaceUtf8(ByteUtils.UnquoteJsonStringUtf8(trimmed)), maxHexDigits, out result);
    }

    /// <summary>
    /// Parses the text of a JSON string (quotes already removed by the reader) with the default digit bound.
    /// Unlike <see cref="TryParseUtf8"/>, quote characters and the text <c>null</c> are not accepted: neither is a quantity.
    /// </summary>
    internal static bool TryParseUnquoted(ReadOnlySpan<byte> utf8, out HexBigInteger result)
        => TryParseQuantityUtf8(utf8, DefaultMaxHexDigits, out result);

    private static bool TryParseQuantityUtf8(ReadOnlySpan<byte> utf8, int maxHexDigits, out HexBigInteger result)
    {
        result = default;

        utf8 = ByteUtils.TrimAsciiWhitespaceUtf8(utf8);

        bool isNegative = false;
        if (!utf8.IsEmpty && (utf8[0] == (byte)'-' || utf8[0] == (byte)'+'))
        {
            isNegative = utf8[0] == (byte)'-';
            utf8 = ByteUtils.TrimAsciiWhitespaceUtf8(utf8.Slice(1));
        }

        bool hasPrefix = ByteUtils.TryTrimHexPrefixUtf8(utf8, out ReadOnlySpan<byte> digits);
        if (hasPrefix)
            utf8 = digits;

        // "0x" is zero; "", "-" and "+" carry no number at all.
        if (utf8.IsEmpty)
            return hasPrefix;

        // Up to 16 digits fit one 64-bit limb: skip the byte decode and build the BigInteger from a ulong.
        if (utf8.Length <= MaxLimbHexDigits)
        {
            if ((uint)utf8.Length > (uint)maxHexDigits)
                return false;

            ulong limb = 0;
            foreach (byte digit in utf8)
            {
                int nibble = ByteUtils.ParseHexNibbleUtf8(digit);
                if (nibble < 0)
                    return false;

                limb = (limb << 4) | (uint)nibble;
            }

            result = FromMagnitude(limb, isNegative);
            return true;
        }

        if (!ByteUtils.TryParseBigIntegerHexUnsignedUtf8(utf8, maxHexDigits, out BigInteger magnitude))
            return false;

        result = new HexBigInteger(isNegative ? BigInteger.Negate(magnitude) : magnitude);
        return true;
    }

    /// <summary>Hex digits that fit one 64-bit limb.</summary>
    private const int MaxLimbHexDigits = 16;

    /// <summary>Builds a value from a 64-bit magnitude and a sign, allocating at most once (negating a multi-limb <see cref="BigInteger"/> copies it).</summary>
    private static HexBigInteger FromMagnitude(ulong magnitude, bool isNegative)
    {
        if (!isNegative)
            return new HexBigInteger(magnitude);

        // Magnitudes up to 2^63 negate into a long; 0 stays 0.
        return magnitude <= 1UL << 63
            ? new HexBigInteger(unchecked((long)(0UL - magnitude)))
            : new HexBigInteger(BigInteger.Negate(new BigInteger(magnitude)));
    }

    /// <summary>
    /// Parses a UTF-8 token or throws if invalid.
    /// </summary>
    /// <param name="utf8Token">UTF-8 token.</param>
    /// <param name="maxHexDigits">Maximum allowed hex digit count.</param>
    public static HexBigInteger ParseUtf8(ReadOnlySpan<byte> utf8Token, int maxHexDigits = DefaultMaxHexDigits)
    {
        if (TryParseUtf8(utf8Token, out var value, maxHexDigits))
            return value;

        throw new FormatException("Invalid hex quantity token.");
    }

    /// <inheritdoc />
    public static HexBigInteger Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        if (TryParse(s, provider, out var value))
            return value;

        throw new FormatException("Invalid hexadecimal string.");
    }

    /// <summary>
    /// Parses optional whitespace, an optional sign, an optional <c>0x</c>/<c>0X</c> prefix and hex digits; the input is always
    /// hexadecimal. The digits are decoded to bytes and read as an unsigned magnitude, because
    /// <see cref="NumberStyles.HexNumber"/> in <see cref="BigInteger"/> parsing reads <c>ff</c> as -1.
    /// Length is not bounded here; use <see cref="TryParseUtf8"/> for untrusted input.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out HexBigInteger result)
    {
        result = default;

        s = ByteUtils.TrimAsciiWhitespace(s);

        bool isNegative = false;
        if (!s.IsEmpty && (s[0] == '-' || s[0] == '+'))
        {
            isNegative = s[0] == '-';
            s = ByteUtils.TrimAsciiWhitespace(s.Slice(1));
        }

        bool hasPrefix = ByteUtils.TryTrimHexPrefix(s, out ReadOnlySpan<char> digits);
        if (hasPrefix)
            s = digits;

        // "0x" is zero; "", "-" and "+" carry no number at all.
        if (s.IsEmpty)
            return hasPrefix;

        if (s.Length <= MaxLimbHexDigits)
        {
            ulong limb = 0;
            foreach (char digit in s)
            {
                int nibble = ByteUtils.ParseHexNibble(digit);
                if (nibble < 0)
                    return false;

                limb = (limb << 4) | (uint)nibble;
            }

            result = FromMagnitude(limb, isNegative);
            return true;
        }

        int requiredBytes = (s.Length + 1) >> 1;
        byte[]? rented = null;

        try
        {
            Span<byte> magnitudeBigEndian = requiredBytes <= StackMagnitudeBytes
                ? stackalloc byte[StackMagnitudeBytes]
                : (rented = ArrayPool<byte>.Shared.Rent(requiredBytes));

            if (!ByteUtils.TryDecodeHexChars(s, magnitudeBigEndian, out int bytesWritten, allowOddLength: true))
                return false;

            var magnitude = new BigInteger(magnitudeBigEndian.Slice(0, bytesWritten), isUnsigned: true, isBigEndian: true);
            result = new HexBigInteger(isNegative ? BigInteger.Negate(magnitude) : magnitude);
            return true;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <inheritdoc />
    public static HexBigInteger Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), provider);
    }

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out HexBigInteger result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParse(s.AsSpan(), provider, out result);
    }

    #endregion

    #region Formatting

    /// <summary>Scratch size for formatted text kept on the stack; larger outputs use pooled buffers.</summary>
    private const int StackFormatBytes = 256;

    /// <summary>
    /// Upper bound on the length of any hex rendering ("-", "0x", then two digits per magnitude byte).
    /// <see cref="BigInteger.GetBitLength"/> excludes the sign bit, so a negative power of two is one bit short; the
    /// +8 absorbs that. O(1): no allocation, no copy.
    /// </summary>
    private int MaxHexUtf8Length => 3 + (2 * (int)((_value.GetBitLength() + 8) >> 3));

    /// <summary>
    /// Understands the formats shared by <see cref="ToString(string?, IFormatProvider?)"/> and both <c>TryFormat</c> overloads:
    /// empty or <c>0x</c>/<c>0X</c> (prefixed hex), <c>x</c>/<c>X</c> (bare hex), <c>d</c>/<c>D</c> (decimal).
    /// The prefix is always lowercase <c>0x</c>; the letter case follows the format.
    /// </summary>
    private static bool TryParseFormat(ReadOnlySpan<char> format, out bool decimalOutput, out bool withPrefix, out bool uppercase)
    {
        decimalOutput = false;
        withPrefix = false;
        uppercase = false;

        switch (format.Length)
        {
            case 0:
                withPrefix = true;
                return true;

            case 1:
                switch (format[0])
                {
                    case 'x': return true;
                    case 'X': uppercase = true; return true;
                    case 'd':
                    case 'D': decimalOutput = true; return true;
                    default: return false;
                }

            case 2 when format[0] == '0' && (format[1] == 'x' || format[1] == 'X'):
                withPrefix = true;
                uppercase = format[1] == 'X';
                return true;

            default:
                return false;
        }
    }

    /// <inheritdoc />
    public override string ToString() => ToString(string.Empty, CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats the value. Supported formats: empty/<c>null</c> or <c>0x</c>/<c>0X</c> (prefixed hex), <c>x</c>/<c>X</c> (hex),
    /// <c>d</c>/<c>D</c> (decimal). Output is always ASCII and culture-invariant; <paramref name="formatProvider"/> is ignored.
    /// </summary>
    /// <exception cref="FormatException">The format is not supported.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        if (!TryParseFormat(format, out bool decimalOutput, out bool withPrefix, out bool uppercase))
            throw new FormatException($"Unknown format: {format}");

        if (decimalOutput)
            return _value.ToString(CultureInfo.InvariantCulture);

        int capacity = MaxHexUtf8Length;
        byte[]? rented = null;

        try
        {
            Span<byte> utf8 = capacity <= StackFormatBytes
                ? stackalloc byte[StackFormatBytes]
                : (rented = ArrayPool<byte>.Shared.Rent(capacity));

            if (!TryFormatHex(utf8, withPrefix, uppercase, out int written))
                throw new InvalidOperationException("Hex output exceeded its computed upper bound.");

            return Encoding.ASCII.GetString(utf8.Slice(0, written));
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <inheritdoc />
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        charsWritten = 0;

        if (!TryParseFormat(format, out bool decimalOutput, out bool withPrefix, out bool uppercase))
            ThrowHelper.ThrowFormatExceptionUnknownFormat(format);

        if (decimalOutput)
            return _value.TryFormat(destination, out charsWritten, "d", CultureInfo.InvariantCulture);

        // Output is ASCII, so it is formatted as UTF-8 into scratch sized to the longest possible rendering (not to the
        // destination, which may be far larger or smaller) and widened.
        int capacity = Math.Min(destination.Length, MaxHexUtf8Length);
        byte[]? rented = null;

        try
        {
            Span<byte> utf8 = capacity <= StackFormatBytes
                ? stackalloc byte[StackFormatBytes]
                : (rented = ArrayPool<byte>.Shared.Rent(capacity));

            if (!TryFormatHex(utf8.Slice(0, capacity), withPrefix, uppercase, out int written))
                return false;

            Ascii.ToUtf16(utf8.Slice(0, written), destination, out _);
            charsWritten = written;
            return true;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Formats directly to UTF-8. Supports the same formats as <see cref="ToString(string?, IFormatProvider?)"/>;
    /// returns false for a destination that is too small and throws <see cref="FormatException"/> for an unsupported format,
    /// like <see cref="ToString(string?, IFormatProvider?)"/> and <see cref="HexBytes"/>. The provider is ignored.
    /// </summary>
    public bool TryFormat(
        Span<byte> utf8Destination,
        out int bytesWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null)
    {
        bytesWritten = 0;

        if (!TryParseFormat(format, out bool decimalOutput, out bool withPrefix, out bool uppercase))
            ThrowHelper.ThrowFormatExceptionUnknownFormat(format);

        return decimalOutput
            ? TryFormatDecimal(utf8Destination, out bytesWritten)
            : TryFormatHex(utf8Destination, withPrefix, uppercase, out bytesWritten);
    }

    /// <summary>Decimal output. <see cref="BigInteger"/> only formats to chars, so this goes through a char scratch buffer.</summary>
    private bool TryFormatDecimal(Span<byte> utf8Destination, out int bytesWritten)
    {
        bytesWritten = 0;

        // A value of b bits has at most floor(b * log10(2)) + 1 digits; +3 covers the sign and the negative power-of-two
        // bit-length quirk. Sizing the scratch to the value, not the destination, keeps a large output buffer from
        // forcing a large rental.
        long maxChars = (long)(_value.GetBitLength() * 0.30103) + 3;
        int charCapacity = (int)Math.Min(utf8Destination.Length, maxChars);
        if (charCapacity == 0)
            return false;

        char[]? rented = null;

        try
        {
            Span<char> chars = charCapacity <= StackFormatBytes
                ? stackalloc char[StackFormatBytes]
                : (rented = ArrayPool<char>.Shared.Rent(charCapacity));

            if (!_value.TryFormat(chars.Slice(0, charCapacity), out int charsWritten, "d", CultureInfo.InvariantCulture))
                return false;

            // Invariant decimal is ASCII, so narrowing is exact.
            Ascii.FromUtf16(chars.Slice(0, charsWritten), utf8Destination, out _);
            bytesWritten = charsWritten;
            return true;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static bool TryFormatLimb(ulong magnitude, bool isNegative, bool withPrefix, bool uppercase, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        int pos = 0;

        // Sign and prefix are written only once the whole output is known to fit, so a failed call writes nothing.
        int digitCount = magnitude == 0 ? 1 : ((64 - System.Numerics.BitOperations.LeadingZeroCount(magnitude)) + 3) >> 2;
        int total = (isNegative ? 1 : 0) + (withPrefix ? 2 : 0) + digitCount;
        if (destination.Length < total)
            return false;

        if (isNegative)
            destination[pos++] = (byte)'-';

        if (withPrefix)
        {
            destination[pos++] = (byte)'0';
            destination[pos++] = (byte)'x';
        }

        // Digits are written from the least significant end; the count is already known.
        ReadOnlySpan<byte> alphabet = uppercase ? ByteUtils.HexBytesUpper : ByteUtils.HexBytesLower;
        for (int i = pos + digitCount - 1; i >= pos; i--)
        {
            destination[i] = alphabet[(int)(magnitude & 0xF)];
            magnitude >>= 4;
        }

        bytesWritten = total;
        return true;
    }

    /// <summary>Minimal hex output ("0x1a", "-0xFF", "0"): sign, optional prefix, no leading zero digits.</summary>
    private bool TryFormatHex(Span<byte> utf8Destination, bool withPrefix, bool uppercase, out int bytesWritten)
    {
        bytesWritten = 0;

        if (_value.IsZero)
        {
            ReadOnlySpan<byte> zero = withPrefix ? "0x0"u8 : "0"u8;
            if (utf8Destination.Length < zero.Length)
                return false;

            zero.CopyTo(utf8Destination);
            bytesWritten = zero.Length;
            return true;
        }

        bool isNegative = _value.Sign < 0;

        // Magnitude within 64 bits (the common JSON-RPC quantity): format the ulong, no byte round-trip through BigInteger.
        // GetBitLength excludes the sign bit, so a negative value of up to 63 bits has a magnitude of at most 2^63.
        long bitLength = _value.GetBitLength();
        if (isNegative ? bitLength <= 63 : bitLength <= 64)
        {
            ulong magnitude = isNegative ? 0UL - unchecked((ulong)(long)_value) : (ulong)_value;
            return TryFormatLimb(magnitude, isNegative, withPrefix, uppercase, utf8Destination, out bytesWritten);
        }

        BigInteger absValue = isNegative ? BigInteger.Abs(_value) : _value;

        int pos = isNegative ? 1 : 0;
        if (utf8Destination.Length < pos)
            return false;

        int magnitudeByteCount = absValue.GetByteCount(isUnsigned: true);
        byte[]? rented = null;

        try
        {
            Span<byte> magnitudeBigEndian = magnitudeByteCount <= StackMagnitudeBytes
                ? stackalloc byte[StackMagnitudeBytes]
                : (rented = ArrayPool<byte>.Shared.Rent(magnitudeByteCount));

            if (!absValue.TryWriteBytes(magnitudeBigEndian, out int written, isUnsigned: true, isBigEndian: true))
                return false;

            if (!ByteUtils.TryWriteHexMinimalUtf8(
                unsignedMagnitudeBigEndian: magnitudeBigEndian.Slice(0, written),
                write0xPrefix: withPrefix,
                uppercase: uppercase,
                destinationUtf8: utf8Destination.Slice(pos),
                bytesWritten: out int hexBytesWritten))
            {
                return false;
            }

            // The sign goes in last so that a failed call leaves the destination untouched.
            if (isNegative)
                utf8Destination[0] = (byte)'-';

            bytesWritten = pos + hexBytesWritten;
            return true;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    #endregion
}
