using EccentricWare.Web3.DataTypes.Utils;
using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes.JsonConverters;

/// <summary>
/// System.Text.Json converter for <see cref="int256"/>.
/// Supports JSON-RPC representations:
/// - String: "0x..." (hex) or "-123" / "123" (decimal)
/// - Number: -123 / 123 (decimal)
/// Also supports use as a dictionary key (<c>Dictionary&lt;int256, T&gt;</c>), using the string form.
/// A JSON <c>null</c> is rejected (use <c>int256?</c> where absence matters).
/// Writes as a JSON string using hex:
/// - Negative values: full-width 0x-prefixed 64-hex-digit two's complement
/// - Non-negative values: minimal 0x-prefixed hex
/// </summary>
public sealed class Int256JsonConverter : JsonConverter<int256>
{
    /// <summary>A valid decimal token is at most 78 characters; anything longer cannot parse.</summary>
    private const int MaxNumberTokenLength = 80;

    /// <summary>Longest raw escaped or split string token the slow path will copy: the longest text, all escaped, plus whitespace slack.</summary>
    internal const int MaxRawStringTokenLength = (JsonStringTokens.MaxEscapedCharBytes * MaxNumberTokenLength) + JsonStringTokens.WhitespaceSlackBytes;

    /// <summary>
    /// Reads an int256 from a JSON token (string or number).
    /// </summary>
    public override int256 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return ReadString(ref reader);

            case JsonTokenType.Number:
                return ReadNumber(ref reader);

            default:
                ThrowHelper.ThrowJsonExceptionUnexpectedToken(nameof(int256), reader.TokenType, numberAllowed: true);
                return default;
        }
    }

    /// <summary>
    /// Reads an int256 from a JSON property name (dictionary key); same text forms as a string value.
    /// </summary>
    public override int256 ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadString(ref reader);

    /// <summary>
    /// Writes an int256 as a JSON string using hex:
    /// - Negative values: full-width 0x-prefixed 64-hex-digit two's complement
    /// - Non-negative values: minimal 0x-prefixed hex
    /// </summary>
    public override void Write(Utf8JsonWriter writer, int256 value, JsonSerializerOptions options)
    {
        Span<byte> utf8 = stackalloc byte[66]; // "0x" + up to 64 hex digits.
        writer.WriteStringValue(Format(value, utf8));
    }

    /// <summary>
    /// Writes an int256 as a JSON property name (dictionary key) in the same form as <see cref="Write"/>.
    /// </summary>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, int256 value, JsonSerializerOptions options)
    {
        Span<byte> utf8 = stackalloc byte[66];
        writer.WritePropertyName(Format(value, utf8));
    }

    private static ReadOnlySpan<byte> Format(int256 value, Span<byte> utf8)
    {
        // Straight to UTF-8: no char buffer and no transcoding in the writer.
        if (!value.TryFormat(utf8, out int written, "0x", CultureInfo.InvariantCulture))
            ThrowHelper.ThrowJsonExceptionFormatFailed(nameof(int256));

        return utf8.Slice(0, written);
    }

    /// <summary>Fast path: one contiguous, unescaped span.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int256 ReadString(ref Utf8JsonReader reader)
    {
        if (!reader.HasValueSequence && !reader.ValueIsEscaped && int256.TryParseUnquoted(reader.ValueSpan, out int256 value))
            return value;

        return ReadStringSlow(ref reader);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int256 ReadStringSlow(ref Utf8JsonReader reader)
    {
        // Escaped or multi-segment tokens are rare for JSON-RPC; they take the shared slow path.
        if (reader.HasValueSequence || reader.ValueIsEscaped)
            return JsonStringTokens.ReadUnescaped<int256>(ref reader, int256.TryParseUnquoted, nameof(int256), MaxRawStringTokenLength);

        ThrowHelper.ThrowJsonExceptionInvalidValue(nameof(int256));
        return default;
    }

    private static int256 ReadNumber(ref Utf8JsonReader reader)
    {
        // Numbers are exposed as raw UTF-8 digits in ValueSpan/ValueSequence.
        if (!reader.HasValueSequence)
        {
            if (int256.TryParseUnquoted(reader.ValueSpan, out int256 value))
                return value;
        }
        else if (TryParseNumberSequence(reader.ValueSequence, out int256 sequenceValue))
        {
            return sequenceValue;
        }

        ThrowHelper.ThrowJsonExceptionInvalidValue(nameof(int256));
        return default;
    }

    /// <summary>
    /// Parses a JSON number token that spans buffer segments, copying it to the stack (never the heap).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryParseNumberSequence(ReadOnlySequence<byte> sequence, out int256 value)
    {
        if (sequence.Length <= 0 || sequence.Length > MaxNumberTokenLength)
        {
            value = default;
            return false;
        }

        Span<byte> utf8 = stackalloc byte[MaxNumberTokenLength];
        sequence.CopyTo(utf8);
        return int256.TryParseUnquoted(utf8.Slice(0, (int)sequence.Length), out value);
    }
}
