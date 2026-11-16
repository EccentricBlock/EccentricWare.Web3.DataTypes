using EccentricWare.Web3.DataTypes.Utils;
using System.Buffers;
using System.Text.Json;

namespace EccentricWare.Web3.DataTypes.JsonConverters;

/// <summary>
/// Slow path shared by the converters for string tokens that are not a single contiguous, unescaped span:
/// tokens containing escape sequences (<c>0x...</c>) or split across <see cref="System.Buffers.ReadOnlySequence{T}"/> segments.
/// </summary>
internal static class JsonStringTokens
{
    internal delegate bool TryParseUtf8<T>(ReadOnlySpan<byte> utf8, out T value);

    private const int StackLimit = 256;

    /// <summary>The most bytes one character can take in a token: a <c>\uXXXX</c> escape.</summary>
    internal const int MaxEscapedCharBytes = 6;

    /// <summary>
    /// ASCII whitespace the fixed-width converters tolerate around a value, in raw token bytes. It bounds the padding an
    /// escaped or split token may carry; it does not apply to a contiguous unescaped token, which is parsed in place.
    /// </summary>
    internal const int WhitespaceSlackBytes = 256;

    /// <summary>
    /// Unescapes / joins the current String or PropertyName token and parses it. Small tokens use the stack; larger ones
    /// are pooled, because the length is controlled by the JSON producer. A token longer than
    /// <paramref name="maxRawLength"/> raw bytes (escape sequences included) is rejected before any buffer is taken.
    /// </summary>
    /// <param name="reader">Reader positioned on the String or PropertyName token.</param>
    /// <param name="tryParse">Parser for the unescaped UTF-8 text.</param>
    /// <param name="typeName">Target type name for error messages.</param>
    /// <param name="maxRawLength">
    /// Largest raw token the target type could need: its longest text with every character escaped as <c>\uXXXX</c>, plus
    /// <see cref="WhitespaceSlackBytes"/>. Each converter passes its own limit so a producer cannot make a 10-character
    /// value cost a multi-megabyte buffer.
    /// </param>
    public static T ReadUnescaped<T>(ref Utf8JsonReader reader, TryParseUtf8<T> tryParse, string typeName, int maxRawLength)
    {
        long rawLength = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
        if (rawLength > maxRawLength)
        {
            ThrowHelper.ThrowJsonExceptionTokenTooLarge(typeName, maxRawLength);
            return default!;
        }

        int length = (int)rawLength;

        byte[]? rented = null;
        Span<byte> buffer = length <= StackLimit
            ? stackalloc byte[StackLimit]
            : (rented = ArrayPool<byte>.Shared.Rent(length));

        try
        {
            // CopyString handles escapes and multi-segment values; the unescaped form is never longer than the raw token.
            // It throws InvalidOperationException for escapes that cannot be represented in UTF-8 (unpaired surrogates).
            int written;
            try
            {
                written = reader.CopyString(buffer);
            }
            catch (InvalidOperationException)
            {
                ThrowHelper.ThrowJsonExceptionInvalidValue(typeName);
                return default!;
            }

            if (!tryParse(buffer.Slice(0, written), out T value))
                ThrowHelper.ThrowJsonExceptionInvalidValue(typeName);

            return value;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
