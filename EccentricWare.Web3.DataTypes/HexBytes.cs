using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using EccentricWare.Web3.DataTypes.JsonConverters;
using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes;

/// <summary>
/// Immutable variable-length bytes with hex encoding/decoding helpers.
/// Intended for calldata, event data, and RPC byte-string fields.
/// </summary>
/// <remarks>
/// Performance notes:
/// - This type owns a <see cref="byte"/> array; for millions of instances this is GC-expensive.
/// - Prefer fixed-size datatypes (Hash32/Address/FunctionSelector) for large in-memory indices.
/// - Hot-path APIs avoid allocations by returning <see cref="ReadOnlySpan{T}"/> views.
/// </remarks>
[JsonConverter(typeof(HexBytesJsonConverter))]
public readonly struct HexBytes :
    IEquatable<HexBytes>,
    IComparable<HexBytes>,
    IComparable,
    ISpanFormattable,
    IUtf8SpanFormattable
{
    /// <summary>
    /// Backing array; <c>null</c> represents empty to avoid allocating empty arrays.
    /// </summary>
    private readonly byte[]? _bytes;

    /// <summary>
    /// Represents an empty value.
    /// </summary>
    public static HexBytes Empty => default;

    #region Constructors

    /// <summary>
    /// Creates a new instance from a byte array by making a defensive copy.
    /// </summary>
    /// <param name="sourceBytes">Source bytes to copy.</param>
    public HexBytes(byte[] sourceBytes)
    {
        if (sourceBytes is null || sourceBytes.Length == 0)
        {
            _bytes = null;
            return;
        }

        var copy = new byte[sourceBytes.Length];
        sourceBytes.AsSpan().CopyTo(copy);
        _bytes = copy;
    }

    /// <summary>
    /// Creates a new instance from a span by copying bytes.
    /// </summary>
    /// <param name="sourceBytes">Source bytes to copy.</param>
    public HexBytes(ReadOnlySpan<byte> sourceBytes)
    {
        _bytes = sourceBytes.Length == 0 ? null : sourceBytes.ToArray();
    }

    /// <summary>
    /// Creates a new instance that uses the provided array without copying.
    /// Caller MUST guarantee the array is not modified after wrapping.
    /// </summary>
    /// <param name="bytesUnsafe">Array to wrap.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HexBytes FromArrayUnsafe(byte[]? bytesUnsafe)
        => bytesUnsafe is null || bytesUnsafe.Length == 0 ? Empty : new HexBytes(bytesUnsafe, noCopy: true);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private HexBytes(byte[] bytesUnsafe, bool noCopy)
    {
        _bytes = bytesUnsafe;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the number of bytes.
    /// </summary>
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bytes?.Length ?? 0;
    }

    /// <summary>
    /// Gets whether the value is empty.
    /// </summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bytes is null || _bytes.Length == 0;
    }

    /// <summary>
    /// Gets a read-only view of the bytes.
    /// </summary>
    public ReadOnlySpan<byte> Span
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bytes is null ? ReadOnlySpan<byte>.Empty : _bytes;
    }

    /// <summary>
    /// Gets a read-only view of the bytes as memory.
    /// </summary>
    public ReadOnlyMemory<byte> Memory
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bytes is null ? ReadOnlyMemory<byte>.Empty : _bytes;
    }

    /// <summary>
    /// Gets a byte at a specified index.
    /// </summary>
    /// <param name="index">Zero-based byte index.</param>
    public byte this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_bytes is null || (uint)index >= (uint)_bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return _bytes[index];
        }
    }

    /// <summary>
    /// Gets the number of hex characters required to represent the bytes (no prefix).
    /// </summary>
    /// <exception cref="OverflowException">The value has 2^30 bytes or more, so the count does not fit an <see cref="int"/>.</exception>
    public int HexLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => checked(Length * 2);
    }

    /// <summary>Characters needed for the hex text, computed in 64 bits so values of 2^30 bytes or more do not wrap.</summary>
    private long FormattedLength(bool withPrefix) => (2L * Length) + (withPrefix ? 2 : 0);

    /// <summary>
    /// The string length for <paramref name="length"/> characters, clamped to <see cref="int.MaxValue"/>:
    /// <see cref="string.Create{TState}"/> then fails with the runtime's own <see cref="OutOfMemoryException"/> instead of the
    /// length wrapping negative.
    /// </summary>
    private static int StringLengthOrThrow(long length) => length <= int.MaxValue ? (int)length : int.MaxValue;

    #endregion

    #region Hot-path byte accessors (no allocations)

    /// <summary>
    /// Returns a slice view over the underlying bytes without allocating.
    /// </summary>
    /// <param name="start">Start index.</param>
    /// <param name="length">Slice length.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> SliceSpan(int start, int length)
        => Span.Slice(start, length);

    /// <summary>
    /// Returns a slice view over the underlying bytes without allocating.
    /// </summary>
    /// <param name="start">Start index.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> SliceSpan(int start)
        => Span.Slice(start);

    /// <summary>
    /// Copies bytes into a destination span without throwing.
    /// </summary>
    /// <param name="destination">Destination buffer.</param>
    /// <param name="bytesWritten">Bytes written to <paramref name="destination"/>.</param>
    /// <returns>True if copied; otherwise false (destination too small).</returns>
    public bool TryCopyTo(Span<byte> destination, out int bytesWritten)
    {
        var source = Span;
        if (destination.Length < source.Length)
        {
            bytesWritten = 0;
            return false;
        }

        source.CopyTo(destination);
        bytesWritten = source.Length;
        return true;
    }

    #endregion

    #region Cold-path byte operations (allocate)

    /// <summary>
    /// Returns a defensive copy of the bytes as an array.
    /// </summary>
    public byte[] ToArray()
        => _bytes is null ? Array.Empty<byte>() : (byte[])_bytes.Clone();

    /// <summary>
    /// Returns a new <see cref="HexBytes"/> containing a copied slice of the current bytes.
    /// Throws <see cref="ArgumentOutOfRangeException"/> when out of range, including for <see cref="Empty"/>.
    /// </summary>
    /// <param name="start">Start index.</param>
    public HexBytes Slice(int start)
        => new(Span.Slice(start));

    /// <summary>
    /// Returns a new <see cref="HexBytes"/> containing a copied slice of the current bytes.
    /// </summary>
    /// <param name="start">Start index.</param>
    /// <param name="length">Slice length.</param>
    public HexBytes Slice(int start, int length)
        => new(Span.Slice(start, length));

    /// <summary>
    /// Concatenates two values into a newly allocated byte array.
    /// </summary>
    public static HexBytes Concat(HexBytes left, HexBytes right)
    {
        if (left.IsEmpty) return right;
        if (right.IsEmpty) return left;

        var result = new byte[left.Length + right.Length];
        left.Span.CopyTo(result);
        right.Span.CopyTo(result.AsSpan(left.Length));
        return FromArrayUnsafe(result);
    }

    /// <summary>
    /// Concatenation operator (allocates).
    /// </summary>
    public static HexBytes operator +(HexBytes left, HexBytes right) => Concat(left, right);

    #endregion

    #region Equality and hashing

    /// <summary>
    /// Compares two instances for byte-wise equality.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(HexBytes other) => Span.SequenceEqual(other.Span);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is HexBytes other && Equals(other);

    /// <summary>
    /// Payloads up to this length are hashed in full; longer ones hash the first and last
    /// <c>MaxFullHashBytes / 2</c> bytes plus the length.
    /// </summary>
    internal const int MaxFullHashBytes = 4096;

    /// <summary>
    /// Hashes the length and the content (randomised per process via <see cref="HashCode"/>).
    /// </summary>
    /// <remarks>
    /// Sampling only the head and tail collapses common calldata (for example <c>approve(spender, MAX)</c>,
    /// where only the middle differs) onto one hash code, turning dictionary lookups into linear scans.
    /// shortcut: payloads above <see cref="MaxFullHashBytes"/> hash head + tail only, so large values that differ solely
    /// in the middle collide; upgrade to a full vectorised hash (e.g. XxHash3) if such keys appear in practice.
    /// </remarks>
    public override int GetHashCode()
    {
        var s = Span;
        if (s.IsEmpty) return 0;

        var hash = new HashCode();
        hash.Add(s.Length);

        if (s.Length <= MaxFullHashBytes)
        {
            hash.AddBytes(s);
        }
        else
        {
            hash.AddBytes(s.Slice(0, MaxFullHashBytes / 2));
            hash.AddBytes(s.Slice(s.Length - (MaxFullHashBytes / 2)));
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Equality operator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(HexBytes left, HexBytes right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(HexBytes left, HexBytes right) => !left.Equals(right);

    #endregion

    #region Ordering

    /// <summary>
    /// Lexicographic comparison using span primitives (typically SIMD-accelerated for bytes).
    /// </summary>
    public int CompareTo(HexBytes other) => Span.SequenceCompareTo(other.Span);

    /// <summary>
    /// Object-based comparison.
    /// </summary>
    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        if (obj is HexBytes other) return CompareTo(other);
        throw new ArgumentException($"Object must be of type {nameof(HexBytes)}", nameof(obj));
    }

    /// <summary>Less-than operator.</summary>
    public static bool operator <(HexBytes left, HexBytes right) => left.CompareTo(right) < 0;

    /// <summary>Greater-than operator.</summary>
    public static bool operator >(HexBytes left, HexBytes right) => left.CompareTo(right) > 0;

    /// <summary>Less-than-or-equal operator.</summary>
    public static bool operator <=(HexBytes left, HexBytes right) => left.CompareTo(right) <= 0;

    /// <summary>Greater-than-or-equal operator.</summary>
    public static bool operator >=(HexBytes left, HexBytes right) => left.CompareTo(right) >= 0;

    #endregion

    #region Parsing

    /// <summary>
    /// Parses a hex string (with optional 0x prefix) into a value.
    /// Accepts odd-length hex by treating it as if left-padded with a single '0'.
    /// </summary>
    /// <param name="hex">Hex characters.</param>
    public static HexBytes Parse(ReadOnlySpan<char> hex)
    {
        if (!TryParse(hex, out var value))
            ThrowHelper.ThrowFormatExceptionInvalidHex();

        return value;
    }

    /// <summary>
    /// Parses a hex string into a value.
    /// </summary>
    public static HexBytes Parse(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        return Parse(hex.AsSpan());
    }

    /// <summary>
    /// Tries to parse a hex string (with optional 0x prefix) without throwing.
    /// Leading/trailing ASCII whitespace and one pair of surrounding double quotes are accepted, exactly as in
    /// <see cref="TryParse(ReadOnlySpan{byte}, out HexBytes)"/>.
    /// Accepts odd-length hex by treating it as if left-padded with a single '0'.
    /// </summary>
    /// <param name="hex">Hex characters.</param>
    /// <param name="result">Parsed value.</param>
    public static bool TryParse(ReadOnlySpan<char> hex, out HexBytes result)
    {
        result = Empty;

        if (hex.IsEmpty)
            return true;

        hex = ByteUtils.TrimAsciiWhitespace(hex);
        if (hex.Length >= 2 && hex[0] == '"' && hex[^1] == '"')
            hex = hex.Slice(1, hex.Length - 2);

        if (ByteUtils.TryTrimHexPrefix(hex, out var digits))
            hex = digits;

        if (hex.IsEmpty)
            return true;

        int byteCount = ByteUtils.GetHexDecodedByteCount(hex.Length, allowOddLength: true);
        if (byteCount < 0)
            return false;

        // Validate first: invalid text must not cost a result-sized allocation (the UTF-8 paths already work this way).
        if (!ByteUtils.IsAllHexChars(hex))
            return false;

        var bytes = new byte[byteCount];
        if (!ByteUtils.TryDecodeHexChars(hex, bytes, out int written, allowOddLength: true) || written != byteCount)
            return false;

        result = FromArrayUnsafe(bytes);
        return true;
    }

    /// <summary>
    /// Tries to parse a UTF-8 encoded hex string (optionally quoted, optionally 0x-prefixed) without throwing.
    /// Accepts odd-length hex by treating it as if left-padded with a single '0'.
    /// </summary>
    /// <param name="utf8">UTF-8 bytes containing the hex text.</param>
    /// <param name="value">Parsed value.</param>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out HexBytes value)
    {
        value = Empty;

        if (utf8.IsEmpty)
            return true;

        utf8 = ByteUtils.TrimAsciiWhitespaceUtf8(utf8);
        utf8 = ByteUtils.UnquoteJsonStringUtf8(utf8);

        return TryParseDigits(utf8, out value);
    }

    /// <summary>
    /// Parses the text of a JSON string token (quotes already removed by the reader): ASCII whitespace around the value is
    /// ignored, but unlike <see cref="TryParse(ReadOnlySpan{byte}, out HexBytes)"/> a quote character is content, not
    /// framing, so it is rejected. Used by the JSON converter.
    /// </summary>
    internal static bool TryParseUnquoted(ReadOnlySpan<byte> utf8, out HexBytes value)
        => TryParseDigits(ByteUtils.TrimAsciiWhitespaceUtf8(utf8), out value);

    private static readonly SearchValues<byte> s_asciiWhitespace = SearchValues.Create(" \t\r\n"u8);

    /// <summary>
    /// <see cref="TryParseUnquoted(ReadOnlySpan{byte}, out HexBytes)"/> for text split across
    /// <see cref="ReadOnlySequence{T}"/> segments. The text is validated and then decoded segment by segment straight
    /// into the result array, so nothing proportional to the text is copied or pooled.
    /// </summary>
    internal static bool TryParseUnquoted(ReadOnlySequence<byte> utf8, out HexBytes value)
    {
        value = Empty;

        // Locate the first and last byte that is not ASCII whitespace.
        long first = -1, last = -1, offset = 0;
        foreach (ReadOnlyMemory<byte> segment in utf8)
        {
            ReadOnlySpan<byte> span = segment.Span;
            int lo = span.IndexOfAnyExcept(s_asciiWhitespace);
            if (lo >= 0)
            {
                if (first < 0)
                    first = offset + lo;

                last = offset + span.LastIndexOfAnyExcept(s_asciiWhitespace);
            }

            offset += span.Length;
        }

        if (first < 0)
            return true;

        ReadOnlySequence<byte> digits = utf8.Slice(first, last - first + 1);

        if (digits.Length >= 2)
        {
            Span<byte> head = stackalloc byte[2];
            digits.Slice(0, 2).CopyTo(head);
            if (head[0] == (byte)'0' && (head[1] | 0x20) == (byte)'x')
                digits = digits.Slice(2);
        }

        if (digits.IsEmpty)
            return true;

        long byteCount = (digits.Length + 1) >> 1;
        if (byteCount > Array.MaxLength)
            return false;

        // Validate first: an invalid token must not cost a result-sized allocation.
        foreach (ReadOnlyMemory<byte> segment in digits)
        {
            if (!ByteUtils.IsAllHexUtf8(segment.Span))
                return false;
        }

        var bytes = new byte[byteCount];
        int dst = 0;

        // A pair of digits can straddle a segment boundary, so the unpaired high digit is carried to the next segment.
        // An odd digit count is left-padded with a '0', which is just a carried high digit that starts as '0'.
        int pendingHigh = (digits.Length & 1) != 0 ? '0' : -1;

        foreach (ReadOnlyMemory<byte> segment in digits)
        {
            ReadOnlySpan<byte> span = segment.Span;
            if (span.IsEmpty)
                continue;

            if (pendingHigh >= 0)
            {
                bytes[dst++] = (byte)((ByteUtils.ParseHexNibbleUtf8((byte)pendingHigh) << 4) | ByteUtils.ParseHexNibbleUtf8(span[0]));
                span = span.Slice(1);
                pendingHigh = -1;
            }

            if ((span.Length & 1) != 0)
            {
                pendingHigh = span[^1];
                span = span.Slice(0, span.Length - 1);
            }

            if (!span.IsEmpty)
            {
                if (!ByteUtils.TryDecodeHexUtf8(span, bytes.AsSpan(dst), out int written, allowOddLength: false))
                    return false;

                dst += written;
            }
        }

        if (dst != bytes.Length || pendingHigh >= 0)
            return false;

        value = FromArrayUnsafe(bytes);
        return true;
    }

    /// <summary>Parses optionally 0x-prefixed hex digits (odd length allowed); empty text is the empty value.</summary>
    private static bool TryParseDigits(ReadOnlySpan<byte> utf8, out HexBytes value)
    {
        value = Empty;

        if (utf8.IsEmpty)
            return true;

        if (ByteUtils.TryTrimHexPrefixUtf8(utf8, out var digits))
            utf8 = digits;

        if (utf8.IsEmpty)
            return true;

        int byteCount = ByteUtils.GetHexDecodedByteCount(utf8.Length, allowOddLength: true);
        if (byteCount < 0)
            return false;

        // Validate first: an invalid token must not cost a result-sized allocation.
        if (!ByteUtils.IsAllHexUtf8(utf8))
            return false;

        var bytes = new byte[byteCount];
        if (!ByteUtils.TryDecodeHexUtf8(utf8, bytes, out int written, allowOddLength: true) || written != byteCount)
            return false;

        value = FromArrayUnsafe(bytes);
        return true;
    }

    #endregion

    #region Formatting

    /// <summary>
    /// Returns a 0x-prefixed lowercase hex representation.
    /// </summary>
    public override string ToString()
    {
        if (IsEmpty)
            return "0x";

        int requiredChars = StringLengthOrThrow(FormattedLength(withPrefix: true));
        return string.Create(requiredChars, this, static (destination, state) =>
        {
            destination[0] = '0';
            destination[1] = 'x';

            _ = ByteUtils.TryEncodeHexChars(state.Span, destination.Slice(2), uppercase: false);
        });
    }

    /// <summary>
    /// Formats the value using the following formats (any other format throws <see cref="FormatException"/>):
    /// - null, "" or "0x": 0x-prefixed lowercase
    /// - "x": unprefixed lowercase
    /// - "0X": 0x-prefixed uppercase
    /// - "X": unprefixed uppercase
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider = null)
    {
        HexFormat.Parse(format, out bool withPrefix, out bool uppercase);

        if (IsEmpty)
            return withPrefix ? "0x" : string.Empty;

        int requiredChars = StringLengthOrThrow(FormattedLength(withPrefix));

        return string.Create(requiredChars, (this, withPrefix, uppercase), static (destination, state) =>
        {
            int offset = 0;

            if (state.withPrefix)
            {
                destination[0] = '0';
                destination[1] = 'x';
                offset = 2;
            }

            _ = ByteUtils.TryEncodeHexChars(state.Item1.Span, destination.Slice(offset), state.uppercase);
        });
    }

    /// <summary>
    /// Tries to format the value into a character destination.
    /// </summary>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        HexFormat.Parse(format, out bool withPrefix, out bool uppercase);

        long required = FormattedLength(withPrefix);
        if (destination.Length < required)
        {
            charsWritten = 0;
            return false;
        }

        int offset = 0;
        if (withPrefix)
        {
            destination[0] = '0';
            destination[1] = 'x';
            offset = 2;
        }

        if (!ByteUtils.TryEncodeHexChars(Span, destination.Slice(offset), uppercase))
        {
            charsWritten = 0;
            return false;
        }

        charsWritten = (int)required; // fits: it was no larger than the destination
        return true;
    }

    /// <summary>
    /// Tries to format the value into a UTF-8 destination.
    /// </summary>
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        HexFormat.Parse(format, out bool withPrefix, out bool uppercase);

        long required = FormattedLength(withPrefix);
        if (utf8Destination.Length < required)
        {
            bytesWritten = 0;
            return false;
        }

        int offset = 0;
        if (withPrefix)
        {
            utf8Destination[0] = (byte)'0';
            utf8Destination[1] = (byte)'x';
            offset = 2;
        }

        if (!ByteUtils.TryEncodeHexUtf8(Span, utf8Destination.Slice(offset), uppercase))
        {
            bytesWritten = 0;
            return false;
        }

        bytesWritten = (int)required; // fits: it was no larger than the destination
        return true;
    }

    #endregion

    #region Conversions

    /// <summary>
    /// Implicit conversion from a byte array (defensive copy).
    /// </summary>
    public static implicit operator HexBytes(byte[] bytes) => new(bytes);

    /// <summary>
    /// Implicit conversion from a byte span (copy).
    /// </summary>
    public static implicit operator HexBytes(ReadOnlySpan<byte> bytes) => new(bytes);

    /// <summary>
    /// Implicit conversion to a read-only span.
    /// </summary>
    public static implicit operator ReadOnlySpan<byte>(HexBytes value) => value.Span;

    /// <summary>
    /// Implicit conversion to read-only memory.
    /// </summary>
    public static implicit operator ReadOnlyMemory<byte>(HexBytes value) => value.Memory;

    #endregion

    #region EVM helpers

    /// <summary>
    /// Attempts to read the first 4 bytes as a function selector without allocating.
    /// </summary>
    /// <param name="selector">Selector parsed from the first 4 bytes.</param>
    /// <returns>True if at least 4 bytes are present; otherwise false.</returns>
    public bool TryGetFunctionSelector(out FunctionSelector selector)
    {
        var s = Span;
        if (s.Length < 4)
        {
            selector = default;
            return false;
        }

        selector = new FunctionSelector(s.Slice(0, 4));
        return true;
    }

    /// <summary>
    /// Returns a view of calldata parameters (bytes after the 4-byte selector) without allocating.
    /// </summary>
    public ReadOnlySpan<byte> GetCalldataParamsSpan()
        => Span.Length <= 4 ? ReadOnlySpan<byte>.Empty : Span.Slice(4);

    /// <summary>
    /// Pads the bytes to a multiple of 32 (EVM word size), right-padding with zeros (allocates on change).
    /// </summary>
    public HexBytes PadToWord()
    {
        int length = Length;
        if (length == 0) return Empty;

        int padding = (32 - (length & 31)) & 31;
        if (padding == 0) return this;

        var result = new byte[length + padding];
        Span.CopyTo(result);
        return FromArrayUnsafe(result);
    }

    #endregion


}