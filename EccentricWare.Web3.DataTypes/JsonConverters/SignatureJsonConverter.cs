using EccentricWare.Web3.DataTypes.Utils;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes.JsonConverters;

/// <summary>
/// System.Text.Json converter for <see cref="Signature"/>.
/// Writes a 0x-prefixed lowercase hex string (also as a dictionary key); optionally writes Solana signatures as Base58, the
/// form Solana JSON-RPC expects (see <see cref="WriteSolanaAsBase58"/>). EVM signatures are always hex.
/// Reads hex with an optional 0x prefix (130 digits: EVM, 128 digits: Solana) or Solana Base58 (no prefix).
/// Like the other fixed-width converters it ignores ASCII whitespace around the value inside the string, and unlike
/// <see cref="Signature.TryParse(ReadOnlySpan{byte}, out Signature)"/> it never strips quote characters: inside a
/// token they are content, not framing. A JSON <c>null</c> is rejected (use <c>Signature?</c> where absence matters).
/// </summary>
public sealed class SignatureJsonConverter(bool writeSolanaAsBase58) : JsonConverter<Signature>
{
    // 0x + 130 hex digits (EVM) is the longest form.
    private const int MaxFormattedLength = 2 + Signature.EvmHexLength;

    /// <summary>Longest raw escaped or split token the slow path will copy: the longest text, all escaped, plus whitespace slack.</summary>
    internal const int MaxRawTokenLength = (JsonStringTokens.MaxEscapedCharBytes * MaxFormattedLength) + JsonStringTokens.WhitespaceSlackBytes;

    /// <summary>
    /// Creates a converter that writes every signature as 0x-prefixed lowercase hex. System.Text.Json instantiates
    /// attribute-declared converters through a public parameterless constructor (optional parameters do not count).
    /// </summary>
    public SignatureJsonConverter() : this(false)
    {
    }

    /// <summary>
    /// When true, Solana signatures are written as Base58 instead of hex (EVM signatures stay hex). Reading accepts both
    /// forms either way. Default: false, which keeps the hex output of earlier versions.
    /// </summary>
    public bool WriteSolanaAsBase58 { get; } = writeSolanaAsBase58;

    /// <inheritdoc />
    public override Signature Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            ThrowHelper.ThrowJsonExceptionUnexpectedToken(nameof(Signature), reader.TokenType, numberAllowed: false);

        return ReadToken(ref reader);
    }

    /// <inheritdoc />
    public override Signature ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadToken(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Signature value, JsonSerializerOptions options)
    {
        Span<byte> buffer = stackalloc byte[MaxFormattedLength];
        writer.WriteStringValue(Format(value, buffer));
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, Signature value, JsonSerializerOptions options)
    {
        Span<byte> buffer = stackalloc byte[MaxFormattedLength];
        writer.WritePropertyName(Format(value, buffer));
    }

    private ReadOnlySpan<byte> Format(Signature value, Span<byte> buffer)
    {
        if (WriteSolanaAsBase58 && value.Type == SignatureType.Solana)
        {
            if (!value.TryFormatBase58(buffer, out int base58Written))
                ThrowHelper.ThrowJsonExceptionFormatFailed(nameof(Signature));

            return buffer.Slice(0, base58Written);
        }

        if (!value.TryFormat(buffer, out int written, "0x", CultureInfo.InvariantCulture))
            ThrowHelper.ThrowJsonExceptionFormatFailed(nameof(Signature));

        return buffer.Slice(0, written);
    }

    /// <summary>Fast path: one contiguous, unescaped span.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Signature ReadToken(ref Utf8JsonReader reader)
    {
        if (!reader.HasValueSequence && !reader.ValueIsEscaped && Signature.TryParseToken(reader.ValueSpan, out Signature value))
            return value;

        return ReadTokenSlow(ref reader);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Signature ReadTokenSlow(ref Utf8JsonReader reader)
    {
        // Escaped or multi-segment tokens are rare for JSON-RPC; they take the shared, size-capped slow path.
        if (reader.HasValueSequence || reader.ValueIsEscaped)
            return JsonStringTokens.ReadUnescaped<Signature>(ref reader, Signature.TryParseToken, nameof(Signature), MaxRawTokenLength);

        ThrowHelper.ThrowJsonExceptionInvalidValue(nameof(Signature));
        return default;
    }
}
