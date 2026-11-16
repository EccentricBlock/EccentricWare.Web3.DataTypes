using EccentricWare.Web3.DataTypes.Utils;
using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes.JsonConverters;

/// <summary>
/// System.Text.Json converter for <see cref="HexBytes"/>.
/// Reads a JSON string of hex digits (optional <c>0x</c> prefix, either case, odd length allowed, ASCII whitespace around
/// the value ignored) from string values and dictionary keys, including escaped and multi-segment tokens. Writes lowercase
/// <c>0x</c>-prefixed hex; the empty value is <c>"0x"</c>. A JSON <c>null</c> is rejected (use <c>HexBytes?</c> where
/// absence matters). Malformed input throws <see cref="JsonException"/>.
/// As a dictionary key a value may hold at most <see cref="MaxDictionaryKeyBytes"/> bytes when read.
/// </summary>
/// <remarks>
/// Memory: an unescaped token, contiguous or split across <see cref="System.Buffers.ReadOnlySequence{T}"/> segments, is
/// validated and decoded straight from the reader's buffers, so the only allocation is the result array, and an invalid
/// token allocates nothing. An escaped token must first be unescaped into a pooled buffer as large as the raw token, so
/// it is limited to <see cref="MaxEscapedTokenBytes"/>. A value written as a property value is streamed in fixed-size
/// chunks and needs no buffer proportional to its length.
/// The key limit exists because <see cref="HexBytes.GetHashCode"/> hashes only the first and last 2,048 bytes of a value
/// above 4,096 bytes: a producer could otherwise send many large keys that differ only in the middle, all with the same
/// hash code, and turn dictionary inserts into a linear scan. The limit applies when writing too, so anything this
/// converter writes as a key it can read back.
/// System.Text.Json instantiates attribute-declared converters through a public parameterless constructor, so one is
/// declared explicitly; it uses <see cref="DefaultMaxEscapedTokenBytes"/>.
/// </remarks>
/// <param name="maxEscapedTokenBytes">
/// Largest raw escaped token (in bytes) to accept; from 1 to <see cref="MaxConfigurableEscapedTokenBytes"/>.
/// </param>
public sealed class HexBytesJsonConverter(int maxEscapedTokenBytes) : JsonConverter<HexBytes>
{
    /// <summary>
    /// Default limit for escaped tokens: 32 MiB of raw token, i.e. about 16 MiB of data. Far above any transaction,
    /// receipt or contract code an RPC node returns.
    /// </summary>
    public const int DefaultMaxEscapedTokenBytes = 32 * 1024 * 1024;

    /// <summary>
    /// Largest value the escaped-token limit can be set to (256 MiB). The limit sizes a pooled buffer, so an unbounded
    /// setting such as <see cref="int.MaxValue"/> would turn one token into an out-of-memory failure.
    /// </summary>
    public const int MaxConfigurableEscapedTokenBytes = 256 * 1024 * 1024;

    /// <summary>
    /// Largest value, in bytes, accepted as a dictionary key when reading: the size up to which
    /// <see cref="HexBytes.GetHashCode"/> hashes the whole content. Larger keys throw <see cref="JsonException"/> when
    /// read or written, so no document this converter produces is one it cannot read back. A key whose raw token exceeds
    /// the longest legal key, fully escaped, plus 256 bytes of whitespace allowance is rejected unparsed with the same error.
    /// </summary>
    public const int MaxDictionaryKeyBytes = HexBytes.MaxFullHashBytes;

    /// <summary>Longest raw key token worth looking at: the longest legal key, fully escaped, plus whitespace slack.</summary>
    private const int MaxKeyRawTokenLength = (JsonStringTokens.MaxEscapedCharBytes * (2 + (2 * MaxDictionaryKeyBytes))) + JsonStringTokens.WhitespaceSlackBytes;

    /// <summary>Stack buffer for the formatted text: values up to 255 bytes format in one piece.</summary>
    private const int StackWriteBytes = 512;

    /// <summary>Data bytes encoded per segment when a long value is streamed.</summary>
    private const int WriteChunkBytes = 256;

    /// <summary>Creates a converter with the default escaped-token limit.</summary>
    public HexBytesJsonConverter() : this(DefaultMaxEscapedTokenBytes)
    {
    }

    /// <summary>
    /// Largest raw escaped string token (escape sequences included) that is unescaped and parsed. An unescaped token,
    /// contiguous or split, is parsed in place and is not subject to this limit.
    /// </summary>
    public int MaxEscapedTokenBytes { get; } = maxEscapedTokenBytes is > 0 and <= MaxConfigurableEscapedTokenBytes
        ? maxEscapedTokenBytes
        : throw new ArgumentOutOfRangeException(
            nameof(maxEscapedTokenBytes),
            $"The limit must be between 1 and {MaxConfigurableEscapedTokenBytes.ToString("N0", CultureInfo.InvariantCulture)}.");

    /// <inheritdoc />
    public override HexBytes Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            ThrowHelper.ThrowJsonExceptionUnexpectedToken(nameof(HexBytes), reader.TokenType, numberAllowed: false);

        return ReadToken(ref reader);
    }

    /// <inheritdoc />
    public override HexBytes ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Reject before decoding, so an oversized key never allocates a result array.
        long rawLength = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
        if (rawLength > MaxKeyRawTokenLength)
            ThrowHelper.ThrowJsonExceptionKeyTooLarge(nameof(HexBytes), MaxDictionaryKeyBytes);

        HexBytes key = ReadToken(ref reader);
        if (key.Length > MaxDictionaryKeyBytes)
            ThrowHelper.ThrowJsonExceptionKeyTooLarge(nameof(HexBytes), MaxDictionaryKeyBytes);

        return key;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, HexBytes value, JsonSerializerOptions options)
    {
        if (value.IsEmpty)
        {
            writer.WriteStringValue("0x"u8);
            return;
        }

        long required = (2L * value.Length) + 2; // 64-bit: a value of 2^30 bytes or more would wrap an int
        if (required <= StackWriteBytes)
        {
            Span<byte> utf8 = stackalloc byte[StackWriteBytes];
            writer.WriteStringValue(Format(value, utf8));
            return;
        }

        WriteLongValue(writer, value);
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, HexBytes value, JsonSerializerOptions options)
    {
        if (value.Length > MaxDictionaryKeyBytes)
            ThrowHelper.ThrowJsonExceptionKeyTooLarge(nameof(HexBytes), MaxDictionaryKeyBytes);

        if (value.IsEmpty)
        {
            writer.WritePropertyName("0x"u8);
            return;
        }

        long required = (2L * value.Length) + 2; // bounded by the key limit checked above
        if (required <= StackWriteBytes)
        {
            Span<byte> utf8 = stackalloc byte[StackWriteBytes];
            writer.WritePropertyName(Format(value, utf8));
            return;
        }

        // A property name cannot be written in segments, so it needs one contiguous buffer.
        byte[] rented = ArrayPool<byte>.Shared.Rent((int)required);
        try
        {
            writer.WritePropertyName(Format(value, rented));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static ReadOnlySpan<byte> Format(HexBytes value, Span<byte> utf8)
    {
        if (!value.TryFormat(utf8, out int written, default, CultureInfo.InvariantCulture))
            ThrowHelper.ThrowJsonExceptionFormatFailed(nameof(HexBytes));

        return utf8.Slice(0, written);
    }

    /// <summary>Streams a long value as a sequence of string segments, so no buffer grows with the value.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteLongValue(Utf8JsonWriter writer, HexBytes value)
    {
        Span<byte> chunk = stackalloc byte[2 + (WriteChunkBytes * 2)];
        chunk[0] = (byte)'0';
        chunk[1] = (byte)'x';
        int prefixLength = 2;

        ReadOnlySpan<byte> remaining = value.Span;
        while (true)
        {
            int take = Math.Min(WriteChunkBytes, remaining.Length);
            bool isFinal = take == remaining.Length;

            if (!ByteUtils.TryEncodeHexUtf8(remaining.Slice(0, take), chunk.Slice(prefixLength), uppercase: false))
                ThrowHelper.ThrowJsonExceptionFormatFailed(nameof(HexBytes));

            writer.WriteStringValueSegment(chunk.Slice(0, prefixLength + (take * 2)), isFinal);
            if (isFinal)
                return;

            remaining = remaining.Slice(take);
            prefixLength = 0;
        }
    }

    /// <summary>Fast path: one contiguous, unescaped span, decoded in place.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private HexBytes ReadToken(ref Utf8JsonReader reader)
    {
        if (!reader.HasValueSequence && !reader.ValueIsEscaped)
        {
            if (HexBytes.TryParseUnquoted(reader.ValueSpan, out HexBytes value))
                return value;

            ThrowHelper.ThrowJsonExceptionInvalidValue(nameof(HexBytes));
        }

        return ReadTokenSlow(ref reader);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private HexBytes ReadTokenSlow(ref Utf8JsonReader reader)
    {
        if (reader.ValueIsEscaped)
            return JsonStringTokens.ReadUnescaped<HexBytes>(ref reader, HexBytes.TryParseUnquoted, nameof(HexBytes), MaxEscapedTokenBytes);

        // Unescaped but split across segments: decode segment by segment, no copy of the token.
        if (!HexBytes.TryParseUnquoted(reader.ValueSequence, out HexBytes value))
            ThrowHelper.ThrowJsonExceptionInvalidValue(nameof(HexBytes));

        return value;
    }
}
