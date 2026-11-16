using EccentricWare.Web3.DataTypes.Utils;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes.JsonConverters;

/// <summary>
/// System.Text.Json converter for <see cref="InstructionDiscriminator"/>.
/// Reads a 16-digit hex string (optional 0x prefix, either case) from string values and dictionary keys, including
/// escaped and multi-segment tokens. Writes lowercase "0x"-prefixed hex. A JSON <c>null</c> is rejected (use
/// <c>InstructionDiscriminator?</c> where absence matters).
/// </summary>
public sealed class InstructionDiscriminatorJsonConverter : JsonConverter<InstructionDiscriminator>
{
    /// <summary>Longest raw escaped or split token the slow path will copy: "0x" + 16 digits, all escaped, plus whitespace slack.</summary>
    internal const int MaxRawTokenLength = (JsonStringTokens.MaxEscapedCharBytes * (2 + InstructionDiscriminator.HexLength)) + JsonStringTokens.WhitespaceSlackBytes;

    /// <inheritdoc />
    public override InstructionDiscriminator Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            ThrowHelper.ThrowJsonExceptionUnexpectedToken(nameof(InstructionDiscriminator), reader.TokenType, numberAllowed: false);

        return ReadToken(ref reader);
    }

    /// <inheritdoc />
    public override InstructionDiscriminator ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadToken(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, InstructionDiscriminator value, JsonSerializerOptions options)
    {
        Span<byte> utf8 = stackalloc byte[InstructionDiscriminator.HexLength + 2];
        writer.WriteStringValue(Format(value, utf8));
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, InstructionDiscriminator value, JsonSerializerOptions options)
    {
        Span<byte> utf8 = stackalloc byte[InstructionDiscriminator.HexLength + 2];
        writer.WritePropertyName(Format(value, utf8));
    }

    private static ReadOnlySpan<byte> Format(InstructionDiscriminator value, Span<byte> utf8)
    {
        if (!value.TryFormat(utf8, out int written, "0x".AsSpan(), provider: null))
            ThrowHelper.ThrowJsonExceptionFormatFailed(nameof(InstructionDiscriminator));

        return utf8.Slice(0, written);
    }

    /// <summary>Fast path: one contiguous, unescaped span. Small enough to inline into the converter.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static InstructionDiscriminator ReadToken(ref Utf8JsonReader reader)
    {
        if (!reader.HasValueSequence && !reader.ValueIsEscaped && InstructionDiscriminator.TryParse(reader.ValueSpan, out var value))
            return value;

        return ReadTokenSlow(ref reader);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static InstructionDiscriminator ReadTokenSlow(ref Utf8JsonReader reader)
    {
        if (reader.HasValueSequence || reader.ValueIsEscaped)
            return JsonStringTokens.ReadUnescaped<InstructionDiscriminator>(ref reader, InstructionDiscriminator.TryParse, nameof(InstructionDiscriminator), MaxRawTokenLength);

        ThrowHelper.ThrowJsonExceptionInvalidValue(nameof(InstructionDiscriminator));
        return default;
    }
}
