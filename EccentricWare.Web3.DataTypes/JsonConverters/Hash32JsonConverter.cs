using EccentricWare.Web3.DataTypes.Utils;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes.JsonConverters;

/// <summary>
/// System.Text.Json converter for <see cref="Hash32"/>.
/// Reads from either:
/// - EVM hex string (0x + 64 hex chars, or 64 hex chars),
/// - Solana Base58 string (decodes to 32 bytes),
/// - Base64 string (standard or URL-safe; decodes to 32 bytes).
/// Base64 is accepted only when padded (44 characters ending in "="). Unpadded Base64 is ambiguous with Base58, which
/// takes precedence, so it is not supported here (see <see cref="Hash32.TryParseAuto"/> for the details).
/// Writes as lower-case hex with 0x prefix by default. A JSON <c>null</c> is rejected (use <c>Hash32?</c> where absence matters).
/// </summary>
/// <remarks>
/// Creates a converter. System.Text.Json instantiates attribute-declared converters through a public parameterless
/// constructor (optional parameters do not count), so one is declared explicitly; it writes with a 0x prefix.
/// </remarks>
public sealed class Hash32JsonConverter(bool writeWith0xPrefix) : JsonConverter<Hash32>
{
    /// <summary>Longest raw escaped or split token the slow path will copy: "0x" + 64 digits, all escaped, plus whitespace slack.</summary>
    internal const int MaxRawTokenLength = (JsonStringTokens.MaxEscapedCharBytes * (2 + Hash32.HexLength)) + JsonStringTokens.WhitespaceSlackBytes;

    /// <summary>Creates a converter that writes "0x"-prefixed lower-case hex.</summary>
    public Hash32JsonConverter() : this(true)
    {
    }

    /// <summary>
    /// If true, writes "0x" prefixed lower-case hex (66 bytes/chars). If false, writes 64-char lower-case hex.
    /// </summary>
    public bool WriteWith0xPrefix { get; } = writeWith0xPrefix;

    /// <inheritdoc />
    public override Hash32 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            ThrowHelper.ThrowJsonExceptionUnexpectedToken(nameof(Hash32), reader.TokenType, numberAllowed: false);

        return ReadToken(ref reader);
    }

    /// <inheritdoc />
    public override Hash32 ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadToken(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Hash32 value, JsonSerializerOptions options)
    {
        Span<byte> utf8 = stackalloc byte[Hash32.HexLength + 2];
        writer.WriteStringValue(Format(value, utf8));
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, Hash32 value, JsonSerializerOptions options)
    {
        Span<byte> utf8 = stackalloc byte[Hash32.HexLength + 2];
        writer.WritePropertyName(Format(value, utf8));
    }

    private ReadOnlySpan<byte> Format(Hash32 value, Span<byte> utf8)
    {
        // Straight to UTF-8 without a managed string.
        ReadOnlySpan<char> format = WriteWith0xPrefix ? "0x" : "x";
        if (!value.TryFormat(utf8, out int written, format, CultureInfo.InvariantCulture))
            ThrowHelper.ThrowJsonExceptionFormatFailed(nameof(Hash32));

        return utf8.Slice(0, written);
    }

    /// <summary>Fast path: one contiguous, unescaped span. Small enough to inline into the converter.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash32 ReadToken(ref Utf8JsonReader reader)
    {
        // Unknown-source parsing: 0x hex, bare 64-digit hex, canonical Base58, or Base64.
        if (!reader.HasValueSequence && !reader.ValueIsEscaped && Hash32.TryParseAuto(reader.ValueSpan, out var value))
            return value;

        return ReadTokenSlow(ref reader);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Hash32 ReadTokenSlow(ref Utf8JsonReader reader)
    {
        // Escaped or multi-segment tokens are rare for JSON-RPC; they take the shared slow path.
        if (reader.HasValueSequence || reader.ValueIsEscaped)
            return JsonStringTokens.ReadUnescaped<Hash32>(ref reader, Hash32.TryParseAuto, nameof(Hash32), MaxRawTokenLength);

        ThrowHelper.ThrowJsonExceptionInvalidValue(nameof(Hash32));
        return default;
    }
}
