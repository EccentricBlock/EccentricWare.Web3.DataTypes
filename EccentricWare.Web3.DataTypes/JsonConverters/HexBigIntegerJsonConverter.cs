using EccentricWare.Web3.DataTypes.Utils;
using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes.JsonConverters;

/// <summary>
/// System.Text.Json converter for <see cref="HexBigInteger"/>.
/// Reads a JSON string holding a hex quantity (<c>"0x1a"</c>, <c>"-0x1a"</c>, unprefixed <c>"1a"</c>; a <c>+</c> or <c>-</c> sign may be
/// followed by ASCII whitespace, so <c>"- 0x1a"</c> is -26, and a bare <c>"0x"</c> is zero) or a JSON integer
/// (<c>26</c>, read as decimal, up to the same width as a <see cref="HexBigInteger.DefaultMaxHexDigits"/>-digit hex string).
/// Also supports use as a dictionary key. Writes a JSON string in minimal <c>0x</c>-prefixed lowercase hex.
/// A JSON <c>null</c> token is rejected; use <c>HexBigInteger?</c> where absence matters.
/// Reading is bounded (at most <see cref="HexBigInteger.DefaultMaxHexDigits"/> hex digits, i.e. 16,384 bits); writing is not,
/// so a wider value serialises but cannot be read back.
/// </summary>
public sealed class HexBigIntegerJsonConverter : JsonConverter<HexBigInteger>
{
    /// <summary>Stack buffer for the formatted text: covers values up to about 1,000 bits (a uint256 needs at most 67 bytes).</summary>
    private const int StackWriteBytes = 256;

    /// <summary>
    /// Widest value accepted as a JSON number, matching the widest hex string the parser accepts (4 bits per hex digit).
    /// </summary>
    private const int MaxNumberBits = HexBigInteger.DefaultMaxHexDigits * 4;

    /// <summary>
    /// A <see cref="MaxNumberBits"/>-bit value has at most 4,933 decimal digits; plus one for a sign. Longer tokens cannot
    /// parse, so they are rejected before any copy or parse work.
    /// </summary>
    private const int MaxNumberTokenLength = 4934;

    private const int StackNumberChars = 256;

    /// <summary>
    /// Longest raw string token (escape sequences included) the slow path will copy: every digit written as a six-byte
    /// <c>\u0030</c> escape, plus slack for sign, prefix and whitespace. Anything longer cannot hold a valid value, so it is
    /// refused before the shared helper rents a buffer.
    /// </summary>
    internal const int MaxEscapedStringTokenLength = (HexBigInteger.DefaultMaxHexDigits * JsonStringTokens.MaxEscapedCharBytes) + JsonStringTokens.WhitespaceSlackBytes;

    /// <inheritdoc />
    public override HexBigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return ReadString(ref reader);

            case JsonTokenType.Number:
                return ReadNumber(ref reader);

            default:
                ThrowHelper.ThrowJsonExceptionUnexpectedToken(nameof(HexBigInteger), reader.TokenType, numberAllowed: true);
                return default;
        }
    }

    /// <inheritdoc />
    public override HexBigInteger ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadString(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, HexBigInteger value, JsonSerializerOptions options)
    {
        Span<byte> utf8 = stackalloc byte[StackWriteBytes];
        if (value.TryFormat(utf8, out int written, "0x", CultureInfo.InvariantCulture))
            writer.WriteStringValue(utf8.Slice(0, written));
        else
            WriteLarge(writer, value, asPropertyName: false);
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, HexBigInteger value, JsonSerializerOptions options)
    {
        Span<byte> utf8 = stackalloc byte[StackWriteBytes];
        if (value.TryFormat(utf8, out int written, "0x", CultureInfo.InvariantCulture))
            writer.WritePropertyName(utf8.Slice(0, written));
        else
            WriteLarge(writer, value, asPropertyName: true);
    }

    /// <summary>Values wider than the stack buffer (more than about 1,000 bits) format into a pooled buffer.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteLarge(Utf8JsonWriter writer, HexBigInteger value, bool asPropertyName)
    {
        int capacity = ByteUtils.GetMaxHexUtf8LengthFromMagnitudeByteCount(value.MagnitudeByteCount, includeSign: value.IsNegative, include0xPrefix: true);
        byte[] rented = ArrayPool<byte>.Shared.Rent(capacity);

        try
        {
            if (!value.TryFormat(rented, out int written, "0x", CultureInfo.InvariantCulture))
                ThrowHelper.ThrowJsonExceptionFormatFailed(nameof(HexBigInteger));

            ReadOnlySpan<byte> utf8 = rented.AsSpan(0, written);
            if (asPropertyName)
                writer.WritePropertyName(utf8);
            else
                writer.WriteStringValue(utf8);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>Fast path: one contiguous, unescaped span.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static HexBigInteger ReadString(ref Utf8JsonReader reader)
    {
        if (!reader.HasValueSequence && !reader.ValueIsEscaped && HexBigInteger.TryParseUnquoted(reader.ValueSpan, out HexBigInteger value))
            return value;

        return ReadStringSlow(ref reader);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static HexBigInteger ReadStringSlow(ref Utf8JsonReader reader)
    {
        // Escaped or multi-segment tokens are rare for JSON-RPC; they take the shared slow path.
        if (reader.HasValueSequence || reader.ValueIsEscaped)
            return JsonStringTokens.ReadUnescaped<HexBigInteger>(ref reader, HexBigInteger.TryParseUnquoted, nameof(HexBigInteger), MaxEscapedStringTokenLength);

        ThrowHelper.ThrowJsonExceptionInvalidValue(nameof(HexBigInteger));
        return default;
    }

    private static HexBigInteger ReadNumber(ref Utf8JsonReader reader)
    {
        // Utf8JsonReader parses the common cases itself, including tokens split across segments.
        if (reader.TryGetInt64(out long int64))
            return new HexBigInteger(int64);

        if (reader.TryGetUInt64(out ulong uint64))
            return new HexBigInteger(uint64);

        // Wider than 64 bits (or not an integer, which BigInteger rejects below).
        if (TryParseDecimalToken(ref reader, out HexBigInteger value))
            return value;

        ThrowHelper.ThrowJsonExceptionInvalidValue(nameof(HexBigInteger));
        return default;
    }

    private static bool TryParseDecimalToken(ref Utf8JsonReader reader, out HexBigInteger value)
    {
        value = default;

        if (!reader.HasValueSequence)
            return TryParseDecimal(reader.ValueSpan, out value);

        ReadOnlySequence<byte> sequence = reader.ValueSequence;
        if (sequence.Length > MaxNumberTokenLength)
            return false;

        byte[] rented = ArrayPool<byte>.Shared.Rent((int)sequence.Length);
        try
        {
            sequence.CopyTo(rented);
            return TryParseDecimal(rented.AsSpan(0, (int)sequence.Length), out value);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static bool TryParseDecimal(ReadOnlySpan<byte> utf8, out HexBigInteger value)
    {
        value = default;

        if (utf8.Length > MaxNumberTokenLength)
            return false;

        // BigInteger has no UTF-8 parser, so the (ASCII) token is widened to chars first.
        char[]? rented = null;

        try
        {
            Span<char> chars = utf8.Length <= StackNumberChars
                ? stackalloc char[StackNumberChars]
                : (rented = ArrayPool<char>.Shared.Rent(utf8.Length));

            // Only an optional '-' and digits are accepted: no whitespace, fraction or exponent.
            if (Ascii.ToUtf16(utf8, chars, out int written) != OperationStatus.Done
                || !BigInteger.TryParse(chars.Slice(0, written), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger parsed)
                || BigInteger.Abs(parsed).GetBitLength() > MaxNumberBits) // magnitude: GetBitLength excludes the sign bit
            {
                return false;
            }

            value = new HexBigInteger(parsed);
            return true;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }
}
