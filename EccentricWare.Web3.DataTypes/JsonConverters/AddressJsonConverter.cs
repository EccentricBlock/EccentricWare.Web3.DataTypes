using EccentricWare.Web3.DataTypes.Utils;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes.JsonConverters;

/// <summary>
/// System.Text.Json converter for <see cref="Address"/>.
/// Reads EVM hex (<c>0x</c>-prefixed, or 40 bare hex digits) and canonical Base58 (Solana) strings, as values and as
/// dictionary keys. Writes the canonical form: lowercase <c>0x</c>-prefixed hex for EVM, Base58 for Solana.
/// ASCII whitespace around the value inside the string is ignored, like the other fixed-width converters. A JSON
/// <c>null</c> is rejected (use <c>Address?</c> where absence matters).
/// Parses directly from the UTF-8 token and formats straight to UTF-8 without allocating.
/// </summary>
public sealed class AddressJsonConverter : JsonConverter<Address>
{
    /// <summary>Longest raw escaped or split token the slow path will copy: the longest address text, all escaped, plus whitespace slack.</summary>
    internal const int MaxRawTokenLength = (JsonStringTokens.MaxEscapedCharBytes * Address.MaxBase58Length) + JsonStringTokens.WhitespaceSlackBytes;

    /// <inheritdoc />
    public override Address Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            ThrowHelper.ThrowJsonExceptionUnexpectedToken(nameof(Address), reader.TokenType, numberAllowed: false);

        return ReadToken(ref reader);
    }

    /// <inheritdoc />
    public override Address ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadToken(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Address value, JsonSerializerOptions options)
    {
        Span<byte> utf8 = stackalloc byte[Address.MaxBase58Length];
        writer.WriteStringValue(Format(value, utf8));
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, Address value, JsonSerializerOptions options)
    {
        Span<byte> utf8 = stackalloc byte[Address.MaxBase58Length];
        writer.WritePropertyName(Format(value, utf8));
    }

    private static ReadOnlySpan<byte> Format(Address value, Span<byte> utf8)
    {
        // EVM is 42 bytes ("0x" + 40) and Solana at most 44, so the buffer always fits.
        if (!value.TryFormat(utf8, out int written, format: default, provider: CultureInfo.InvariantCulture))
            ThrowHelper.ThrowJsonExceptionFormatFailed(nameof(Address));

        return utf8.Slice(0, written);
    }

    /// <summary>Fast path: one contiguous, unescaped span. Small enough to inline into the converter.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Address ReadToken(ref Utf8JsonReader reader)
    {
        if (!reader.HasValueSequence && !reader.ValueIsEscaped && TryParseToken(reader.ValueSpan, out Address value))
            return value;

        return ReadTokenSlow(ref reader);
    }

    /// <summary>
    /// Parses the content of a JSON string token. <see cref="Address.TryParseUnquotedUtf8"/> is whitespace-strict, so the
    /// trimmed text is tried only after the exact text fails: valid tokens pay nothing for the tolerance.
    /// </summary>
    private static bool TryParseToken(ReadOnlySpan<byte> utf8, out Address value)
    {
        if (Address.TryParseUnquotedUtf8(utf8, out value))
            return true;

        ReadOnlySpan<byte> trimmed = ByteUtils.TrimAsciiWhitespaceUtf8(utf8);
        return trimmed.Length != utf8.Length && Address.TryParseUnquotedUtf8(trimmed, out value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Address ReadTokenSlow(ref Utf8JsonReader reader)
    {
        // Escaped or multi-segment tokens are rare; they share the pooled slow path with the other fixed-width converters.
        if (reader.HasValueSequence || reader.ValueIsEscaped)
            return JsonStringTokens.ReadUnescaped<Address>(ref reader, TryParseToken, nameof(Address), MaxRawTokenLength);

        ThrowHelper.ThrowJsonExceptionInvalidValue(nameof(Address));
        return default;
    }
}
