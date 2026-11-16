using EccentricWare.Web3.DataTypes.JsonConverters;
using EccentricWare.Web3.DataTypes.Utils;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes;

/// <summary>
/// Represents an 8-byte Solana instruction discriminator (e.g., Anchor-style "discriminator-like" prefix).
/// Stored as a single big-endian <see cref="ulong"/> for compactness and fast comparisons.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
[JsonConverter(typeof(InstructionDiscriminatorJsonConverter))]
public readonly struct InstructionDiscriminator :
    IEquatable<InstructionDiscriminator>,
    IComparable<InstructionDiscriminator>,
    IComparable,
    ISpanFormattable,
    ISpanParsable<InstructionDiscriminator>,
    IUtf8SpanFormattable
{
    /// <summary>Discriminator length in bytes.</summary>
    public const int ByteLength = 8;

    /// <summary>Discriminator length in hex digits (without 0x prefix).</summary>
    public const int HexLength = 16;

    /// <summary>Represents the zero discriminator (0x0000000000000000).</summary>
    public static readonly InstructionDiscriminator Zero;

    /// <summary>Stored as big-endian numeric value so that numeric ordering matches hex string ordering.</summary>
    private readonly ulong _value;

    /// <summary>
    /// Creates a discriminator from a big-endian numeric value.
    /// </summary>
    /// <param name="value">Big-endian discriminator value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public InstructionDiscriminator(ulong value) => _value = value;

    /// <summary>
    /// Creates a discriminator from an 8-byte big-endian span.
    /// </summary>
    /// <param name="bytes">Input bytes (must be at least 8 bytes).</param>
    public InstructionDiscriminator(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < ByteLength)
            ThrowHelper.ThrowArgumentExceptionInvalidDiscriminatorLength(nameof(bytes));

        _value = BinaryPrimitives.ReadUInt64BigEndian(bytes);
    }

    /// <summary>
    /// Gets the discriminator as a big-endian numeric value.
    /// </summary>
    public ulong Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value;
    }

    /// <summary>
    /// Gets whether the discriminator is 0x0000000000000000.
    /// </summary>
    public bool IsZero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value == 0;
    }

    /// <summary>
    /// Gets the discriminator as a signed 64-bit integer (useful for database providers without unsigned 64-bit columns).
    /// Note that signed ordering differs from unsigned ordering for values with the top bit set.
    /// </summary>
    public long AsInt64
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => unchecked((long)_value);
    }

    /// <summary>
    /// Creates a discriminator from a signed 64-bit value (the inverse of <see cref="AsInt64"/>).
    /// </summary>
    /// <param name="value">Signed database value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static InstructionDiscriminator FromInt64(long value) => new(unchecked((ulong)value));

    /// <summary>
    /// Writes the discriminator to a destination buffer as 8 bytes in big-endian order.
    /// </summary>
    /// <param name="destination">Destination span (must be at least 8 bytes).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBytes(Span<byte> destination)
    {
        if (destination.Length < ByteLength)
            ThrowHelper.ThrowArgumentExceptionDestinationTooSmall(nameof(destination));

        BinaryPrimitives.WriteUInt64BigEndian(destination, _value);
    }

    /// <summary>
    /// Extracts the discriminator from decoded Solana instruction data (first 8 bytes).
    /// </summary>
    /// <param name="instructionData">Decoded instruction data bytes.</param>
    public static InstructionDiscriminator FromInstructionData(ReadOnlySpan<byte> instructionData)
    {
        if (instructionData.Length < ByteLength)
            ThrowHelper.ThrowArgumentExceptionInvalidDiscriminatorLength(nameof(instructionData));

        return new InstructionDiscriminator(instructionData);
    }

    /// <summary>
    /// Attempts to extract the discriminator from decoded instruction data without throwing.
    /// </summary>
    /// <param name="instructionData">Decoded instruction data bytes.</param>
    /// <param name="discriminator">Parsed discriminator if successful.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryFromInstructionData(ReadOnlySpan<byte> instructionData, out InstructionDiscriminator discriminator)
    {
        if (instructionData.Length < ByteLength)
        {
            discriminator = Zero;
            return false;
        }

        discriminator = new InstructionDiscriminator(instructionData);
        return true;
    }

    /// <summary>
    /// Returns a canonical hex string with 0x prefix and lowercase digits.
    /// </summary>
    public override string ToString()
        => string.Create(HexLength + 2, _value, static (chars, v) =>
        {
            chars[0] = '0';
            chars[1] = 'x';
            ByteUtils.WriteHexUInt64(chars.Slice(2), v, uppercase: false);
        });

    /// <summary>
    /// Formats the discriminator using supported formats: "", "x", "X", "0x", "0X".
    /// Empty format defaults to "0x" with lowercase digits. The prefix is always lowercase "0x"; "X" and "0X" select uppercase digits.
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider = null)
    {
        HexFormat.Parse(format, out bool hasPrefix, out bool upperDigits);

        int len = hasPrefix ? HexLength + 2 : HexLength;

        return string.Create(len, (Value: _value, HasPrefix: hasPrefix, Upper: upperDigits), static (dst, state) =>
        {
            if (state.HasPrefix)
            {
                dst[0] = '0';
                dst[1] = 'x';
                ByteUtils.WriteHexUInt64(dst.Slice(2), state.Value, state.Upper);
            }
            else
            {
                ByteUtils.WriteHexUInt64(dst, state.Value, state.Upper);
            }
        });
    }

    /// <summary>
    /// Attempts to format the discriminator into a character buffer without allocations.
    /// </summary>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        HexFormat.Parse(format, out bool hasPrefix, out bool upperDigits);

        int required = hasPrefix ? HexLength + 2 : HexLength;
        if (destination.Length < required)
        {
            charsWritten = 0;
            return false;
        }

        if (hasPrefix)
        {
            destination[0] = '0';
            destination[1] = 'x';
            ByteUtils.WriteHexUInt64(destination.Slice(2, HexLength), _value, upperDigits);
        }
        else
        {
            ByteUtils.WriteHexUInt64(destination.Slice(0, HexLength), _value, upperDigits);
        }

        charsWritten = required;
        return true;
    }

    /// <summary>
    /// Attempts to format the discriminator into a UTF-8 buffer without allocations.
    /// </summary>
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        HexFormat.Parse(format, out bool hasPrefix, out bool upperDigits);

        int required = hasPrefix ? HexLength + 2 : HexLength;
        if (utf8Destination.Length < required)
        {
            bytesWritten = 0;
            return false;
        }

        if (hasPrefix)
        {
            utf8Destination[0] = (byte)'0';
            utf8Destination[1] = (byte)'x';
            HexCodec.Encode16(utf8Destination.Slice(2, HexLength), _value, upperDigits);
        }
        else
        {
            HexCodec.Encode16(utf8Destination.Slice(0, HexLength), _value, upperDigits);
        }

        bytesWritten = required;
        return true;
    }

    /// <summary>
    /// Parses a 16-hex-digit discriminator literal (with optional 0x/0X prefix) and throws on invalid input.
    /// </summary>
    public static InstructionDiscriminator Parse(ReadOnlySpan<char> hex)
    {
        if (!TryParse(hex, out var result))
            ThrowHelper.ThrowFormatExceptionInvalidDiscriminator();

        return result;
    }

    /// <summary>
    /// Parses a 16-hex-digit discriminator literal (with optional 0x/0X prefix) and throws on invalid input.
    /// </summary>
    public static InstructionDiscriminator Parse(ReadOnlySpan<byte> utf8)
    {
        if (!TryParse(utf8, out var result))
            ThrowHelper.ThrowFormatExceptionInvalidDiscriminator();

        return result;
    }

    /// <summary>
    /// Parses a discriminator using <see cref="ISpanParsable{TSelf}"/> semantics.
    /// </summary>
    public static InstructionDiscriminator Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses a discriminator using <see cref="ISpanParsable{TSelf}"/> semantics.
    /// </summary>
    public static InstructionDiscriminator Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        if (TryParse(s, provider, out var result))
            return result;

        ThrowHelper.ThrowFormatExceptionInvalidDiscriminator();
        return default;
    }

    /// <summary>
    /// Parses a discriminator using <see cref="ISpanParsable{TSelf}"/> semantics.
    /// </summary>
    public static InstructionDiscriminator Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), provider);
    }

    /// <summary>
    /// Attempts to parse a discriminator literal from UTF-16 without throwing.
    /// This method expects exactly 16 digits after an optional 0x prefix.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> hex, out InstructionDiscriminator result)
    {
        result = Zero;

        hex = ByteUtils.TrimAsciiWhitespace(hex);

        if (hex.Length == 0)
            return false;

        if (ByteUtils.TryTrimHexPrefix(hex, out var trimmed))
            hex = trimmed;

        if (hex.Length != HexLength)
            return false;

        if (!ByteUtils.TryParseHexUInt64CharsFixed16(hex.Slice(0, 16), out ulong value))
            return false;

        result = new InstructionDiscriminator(value);
        return true;
    }

    /// <summary>
    /// Attempts to parse a discriminator literal from UTF-8 without throwing.
    /// This method expects exactly 16 digits after an optional 0x prefix.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8Hex, out InstructionDiscriminator result)
    {
        result = Zero;

        utf8Hex = ByteUtils.TrimAsciiWhitespaceUtf8(utf8Hex);

        if (utf8Hex.Length == 0)
            return false;

        if (ByteUtils.TryTrimHexPrefixUtf8(utf8Hex, out var trimmed))
            utf8Hex = trimmed;

        if (utf8Hex.Length != HexLength)
            return false;

        if (!ByteUtils.TryParseHexUInt64Utf8Fixed16(utf8Hex.Slice(0, 16), out ulong value))
            return false;

        result = new InstructionDiscriminator(value);
        return true;
    }

    /// <summary>
    /// Attempts to parse a discriminator literal from a string without throwing.
    /// </summary>
    public static bool TryParse(string? hex, out InstructionDiscriminator result)
    {
        if (hex is null)
        {
            result = Zero;
            return false;
        }

        return TryParse(hex.AsSpan(), out result);
    }

    /// <summary>
    /// Attempts to parse a discriminator using <see cref="ISpanParsable{TSelf}"/> semantics.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out InstructionDiscriminator result)
        => TryParse(s, out result);

    /// <summary>
    /// Attempts to parse a discriminator using <see cref="ISpanParsable{TSelf}"/> semantics.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out InstructionDiscriminator result)
    {
        if (s is null)
        {
            result = Zero;
            return false;
        }

        return TryParse(s.AsSpan(), out result);
    }

    /// <summary>
    /// Tests discriminator equality.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(InstructionDiscriminator other) => _value == other._value;

    /// <summary>
    /// Tests discriminator equality against an arbitrary object.
    /// </summary>
    public override bool Equals(object? obj) => obj is InstructionDiscriminator other && Equals(other);

    /// <summary>Per-process random multiplier; see <see cref="HashSeed"/>.</summary>
    private static readonly ulong s_hashMultiplier = HashSeed.NextOddMultiplier();

    /// <summary>
    /// Returns a hash code suitable for hash tables. It is randomised per process (multiply and fold), so it is not stable
    /// across runs; discriminators come from untrusted instruction data, and a predictable hash lets crafted keys
    /// collide in dictionary buckets.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        ulong h = _value * s_hashMultiplier;
        return unchecked((int)((h >> 32) ^ h));
    }

    /// <summary>
    /// Compares this discriminator to another discriminator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(InstructionDiscriminator other) => _value.CompareTo(other._value);

    /// <summary>
    /// Compares this discriminator to an arbitrary object.
    /// </summary>
    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        if (obj is InstructionDiscriminator other) return CompareTo(other);
        ThrowHelper.ThrowArgumentExceptionWrongType(nameof(obj), nameof(InstructionDiscriminator));
        return 0;
    }

    /// <summary>Equality operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(InstructionDiscriminator left, InstructionDiscriminator right) => left._value == right._value;

    /// <summary>Inequality operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(InstructionDiscriminator left, InstructionDiscriminator right) => left._value != right._value;

    /// <summary>Less-than operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(InstructionDiscriminator left, InstructionDiscriminator right) => left._value < right._value;

    /// <summary>Greater-than operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(InstructionDiscriminator left, InstructionDiscriminator right) => left._value > right._value;

    /// <summary>Less-than-or-equal operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <=(InstructionDiscriminator left, InstructionDiscriminator right) => left._value <= right._value;

    /// <summary>Greater-than-or-equal operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >=(InstructionDiscriminator left, InstructionDiscriminator right) => left._value >= right._value;
}
