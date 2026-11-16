using EccentricWare.Web3.DataTypes.Utils;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes.JsonConverters;

/// <summary>
/// System.Text.Json converter for <see cref="FunctionSelector"/>.
/// Reads an 8-digit hex string (optional 0x prefix, either case) from string values and dictionary keys, including
/// escaped and multi-segment tokens. Writes lowercase "0x"-prefixed hex. A JSON <c>null</c> is rejected (use
/// <c>FunctionSelector?</c> where absence matters).
/// </summary>
public sealed class FunctionSelectorJsonConverter : JsonConverter<FunctionSelector>
{
    /// <summary>Longest raw escaped or split token the slow path will copy: "0x" + 8 digits, all escaped, plus whitespace slack.</summary>
    internal const int MaxRawTokenLength = (JsonStringTokens.MaxEscapedCharBytes * (2 + FunctionSelector.HexLength)) + JsonStringTokens.WhitespaceSlackBytes;

    /// <inheritdoc />
    public override FunctionSelector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            ThrowHelper.ThrowJsonExceptionUnexpectedToken(nameof(FunctionSelector), reader.TokenType, numberAllowed: false);

        return ReadToken(ref reader);
    }

    /// <inheritdoc />
    public override FunctionSelector ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadToken(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, FunctionSelector value, JsonSerializerOptions options)
    {
        Span<byte> utf8 = stackalloc byte[FunctionSelector.HexLength + 2];
        writer.WriteStringValue(Format(value, utf8));
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, FunctionSelector value, JsonSerializerOptions options)
    {
        Span<byte> utf8 = stackalloc byte[FunctionSelector.HexLength + 2];
        writer.WritePropertyName(Format(value, utf8));
    }

    private static ReadOnlySpan<byte> Format(FunctionSelector value, Span<byte> utf8)
    {
        if (!value.TryFormat(utf8, out int written, "0x".AsSpan(), provider: null))
            ThrowHelper.ThrowJsonExceptionFormatFailed(nameof(FunctionSelector));

        return utf8.Slice(0, written);
    }

    /// <summary>Fast path: one contiguous, unescaped span. Small enough to inline into the converter.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static FunctionSelector ReadToken(ref Utf8JsonReader reader)
    {
        if (!reader.HasValueSequence && !reader.ValueIsEscaped && FunctionSelector.TryParse(reader.ValueSpan, out var value))
            return value;

        return ReadTokenSlow(ref reader);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static FunctionSelector ReadTokenSlow(ref Utf8JsonReader reader)
    {
        if (reader.HasValueSequence || reader.ValueIsEscaped)
            return JsonStringTokens.ReadUnescaped<FunctionSelector>(ref reader, FunctionSelector.TryParse, nameof(FunctionSelector), MaxRawTokenLength);

        ThrowHelper.ThrowJsonExceptionInvalidValue(nameof(FunctionSelector));
        return default;
    }
}
