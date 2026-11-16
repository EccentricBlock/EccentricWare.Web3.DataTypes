using EccentricWare.Web3.DataTypes.JsonConverters;
using EccentricWare.Web3.DataTypes.Utils;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes;

/// <summary>
/// The type of blockchain address.
/// </summary>
public enum AddressType : byte
{
    /// <summary>
    /// Ethereum Virtual Machine address (20 bytes, hex encoded with 0x prefix).
    /// </summary>
    Evm = 0,
    
    /// <summary>
    /// Solana address (32 bytes, Base58 encoded).
    /// </summary>
    Solana = 1
}

/// <summary>
/// A compact blockchain address supporting both EVM (20 bytes) and Solana (32 bytes).
/// </summary>
/// <remarks>
/// <para>
/// Storage layout is optimised for hot-path comparisons and dictionary keys:
/// 32 bytes of data (4 x ulong) + 1 byte <see cref="AddressType"/>, padded to 40 bytes by the 8-byte alignment of the limbs.
/// </para>
/// <para>
/// For EVM addresses, only the first 20 bytes are used; the remaining 12 bytes are zero.
/// The 32-byte backing value is stored in big-endian byte order across the four ulongs
/// for stable lexicographic ordering and predictable formatting.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
[JsonConverter(typeof(AddressJsonConverter))]
public readonly struct Address :
    IEquatable<Address>,
    IComparable<Address>,
    IComparable,
    ISpanFormattable,
    ISpanParsable<Address>,
    IUtf8SpanFormattable
{
    /// <summary>The byte length of an EVM address payload.</summary>
    public const int EvmByteLength = 20;

    /// <summary>The byte length of a Solana public key payload.</summary>
    public const int SolanaByteLength = 32;

    /// <summary>The number of hex characters in an EVM address without prefix (20 bytes * 2).</summary>
    public const int EvmHexLength = 40;

    /// <summary>The maximum number of Base58 characters for a 32-byte payload.</summary>
    public const int MaxBase58Length = 44;

    // 32 bytes backing payload (big-endian):
    // _u0 contains bytes 0..7 (most significant), _u3 contains bytes 24..31 (least significant).
    private readonly ulong _u0;
    private readonly ulong _u1;
    private readonly ulong _u2;
    private readonly ulong _u3;

    private readonly AddressType _type;

    /// <summary>A zero EVM address (<c>0x000…000</c>).</summary>
    public static readonly Address ZeroEvm = new(0, 0, 0, 0, AddressType.Evm);

    /// <summary>A zero Solana address (32 zero bytes; Base58 would be 32 '1' characters).</summary>
    public static readonly Address ZeroSolana = new(0, 0, 0, 0, AddressType.Solana);

    /// <summary>
    /// Creates an address from 32 bytes represented by four 64-bit words in big-endian order.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Address(ulong u0Msb, ulong u1, ulong u2, ulong u3Lsb, AddressType addressType)
    {
        _u0 = u0Msb;
        _u1 = u1;
        _u2 = u2;
        _u3 = u3Lsb;
        _type = addressType;
    }

    /// <summary>
    /// Returns the four big-endian 64-bit words of the 32-byte backing value (the family is separate, see <see cref="Type"/>).
    /// Lets <see cref="AddressSet"/> read keys without depending on the struct's memory layout.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void GetLimbs(out ulong u0, out ulong u1, out ulong u2, out ulong u3)
    {
        u0 = _u0;
        u1 = _u1;
        u2 = _u2;
        u3 = _u3;
    }

    /// <summary>Returns the address family.</summary>
    public AddressType Type
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _type;
    }

    /// <summary>Returns the wire byte length for the current address family (20 for EVM; 32 for Solana).</summary>
    public int ByteLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _type == AddressType.Evm ? EvmByteLength : SolanaByteLength;
    }

    /// <summary>Returns true if the 32-byte backing value is all zeros (type is not considered).</summary>
    public bool IsZero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_u0 | _u1 | _u2 | _u3) == 0;
    }

    /// <summary>
    /// Creates an EVM address from a 20-byte big-endian payload.
    /// </summary>
    public static Address FromEvmBytes(ReadOnlySpan<byte> evmAddress20)
    {
        if (evmAddress20.Length != EvmByteLength)
            ThrowHelper.ThrowArgumentExceptionInvalidEvmLength(nameof(evmAddress20));

        ulong u0 = BinaryPrimitives.ReadUInt64BigEndian(evmAddress20);
        ulong u1 = BinaryPrimitives.ReadUInt64BigEndian(evmAddress20.Slice(8));
        ulong u2 = (ulong)BinaryPrimitives.ReadUInt32BigEndian(evmAddress20.Slice(16)) << 32;

        return new Address(u0, u1, u2, 0, AddressType.Evm);
    }

    /// <summary>
    /// Creates a Solana address from a 32-byte payload.
    /// </summary>
    public static Address FromSolanaBytes(ReadOnlySpan<byte> solanaPubkey32)
    {
        if (solanaPubkey32.Length != SolanaByteLength)
            ThrowHelper.ThrowArgumentExceptionInvalidSolanaLength(nameof(solanaPubkey32));

        ulong u0 = BinaryPrimitives.ReadUInt64BigEndian(solanaPubkey32);
        ulong u1 = BinaryPrimitives.ReadUInt64BigEndian(solanaPubkey32.Slice(8));
        ulong u2 = BinaryPrimitives.ReadUInt64BigEndian(solanaPubkey32.Slice(16));
        ulong u3 = BinaryPrimitives.ReadUInt64BigEndian(solanaPubkey32.Slice(24));

        return new Address(u0, u1, u2, u3, AddressType.Solana);
    }

    /// <summary>
    /// Writes the address bytes in big-endian order to <paramref name="destination"/>.
    /// Writes 20 bytes for EVM and 32 bytes for Solana.
    /// </summary>
    public void WriteBytes(Span<byte> destination)
    {
        int required = ByteLength;
        if (destination.Length < required)
            ThrowHelper.ThrowArgumentExceptionDestinationTooSmall(nameof(destination));

        BinaryPrimitives.WriteUInt64BigEndian(destination, _u0);
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(8), _u1);

        if (_type == AddressType.Evm)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(16), (uint)(_u2 >> 32));
            return;
        }

        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(16), _u2);
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(24), _u3);
    }

    /// <summary>
    /// Creates a new byte array containing the wire-format bytes for this address (20 or 32 bytes).
    /// </summary>
    public byte[] ToBytes()
    {
        byte[] bytes = new byte[ByteLength];
        WriteBytes(bytes);
        return bytes;
    }

    /// <summary>
    /// Compares this address to another for equality.
    /// Scalar comparison is faster than SIMD for single-value compares.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Address other)
        => _type == other._type && _u0 == other._u0 && _u1 == other._u1 && _u2 == other._u2 && _u3 == other._u3;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Address other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // Keyed per process (see Hash256) because addresses come from untrusted transactions. The family is mixed in AFTER
        // the keyed hash, so equal payloads of different families hash differently. Folding it into the last limb instead
        // (u3 ^ family) let an EVM address collide with the Solana key that has the same first three limbs and a last limb
        // of 1, because the XOR cancels.
        int hash = Hash256.Compute(_u0, _u1, _u2, _u3);
        return _type == AddressType.Evm ? hash : hash ^ SolanaHashSalt;
    }

    /// <summary>Non-zero constant XORed into the hash of a Solana address (the golden-ratio multiplier, truncated to 32 bits).</summary>
    private const int SolanaHashSalt = unchecked((int)0x9E3779B9);

    /// <summary>Returns true if two addresses are equal.</summary>
    public static bool operator ==(Address left, Address right) => left.Equals(right);

    /// <summary>Returns true if two addresses are not equal.</summary>
    public static bool operator !=(Address left, Address right) => !left.Equals(right);

    /// <summary>
    /// Lexicographic comparison. EVM addresses sort before Solana addresses.
    /// Within the same family, compares backing bytes from most-significant to least-significant.
    /// </summary>
    public int CompareTo(Address other)
    {
        if (_type != other._type)
            return _type < other._type ? -1 : 1;

        if (_u0 != other._u0) return _u0 < other._u0 ? -1 : 1;
        if (_u1 != other._u1) return _u1 < other._u1 ? -1 : 1;
        if (_u2 != other._u2) return _u2 < other._u2 ? -1 : 1;
        if (_u3 != other._u3) return _u3 < other._u3 ? -1 : 1;
        return 0;
    }

    /// <inheritdoc/>
    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        if (obj is Address other) return CompareTo(other);
        throw new ArgumentException($"Object must be of type {nameof(Address)}.", nameof(obj));
    }

    /// <summary>Returns true if <paramref name="left"/> is less than <paramref name="right"/>.</summary>
    public static bool operator <(Address left, Address right) => left.CompareTo(right) < 0;

    /// <summary>Returns true if <paramref name="left"/> is greater than <paramref name="right"/>.</summary>
    public static bool operator >(Address left, Address right) => left.CompareTo(right) > 0;

    /// <summary>Returns true if <paramref name="left"/> is less than or equal to <paramref name="right"/>.</summary>
    public static bool operator <=(Address left, Address right) => left.CompareTo(right) <= 0;

    /// <summary>Returns true if <paramref name="left"/> is greater than or equal to <paramref name="right"/>.</summary>
    public static bool operator >=(Address left, Address right) => left.CompareTo(right) >= 0;

    // -------------------------
    // Parsing (chars and UTF-8)
    // -------------------------

    /// <summary>
    /// Parses an address from characters, auto-detecting EVM vs Solana.
    /// Detection order:
    /// <list type="number">
    /// <item><description><c>0x</c>/<c>0X</c> prefix => EVM</description></item>
    /// <item><description>Otherwise, try canonical Base58=>32 => Solana</description></item>
    /// <item><description>Otherwise, if length is 40 and all hex => EVM</description></item>
    /// </list>
    /// <para>
    /// The unprefixed 40-digit form is ambiguous only for text that starts with at least seven <c>1</c> digits and has no
    /// <c>0</c>: such text can be a canonical Base58 Solana key and is parsed as one. Write the <c>0x</c> prefix when an
    /// EVM address may begin that way. Mixed-case hex is accepted without verifying its EIP-55 checksum.
    /// </para>
    /// </summary>
    public static Address Parse(ReadOnlySpan<char> text) => Parse(text, CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses an address from a string using invariant parsing rules.
    /// </summary>
    public static Address Parse(string text) => Parse(text, CultureInfo.InvariantCulture);

    /// <summary>
    /// Tries to parse an address from characters without throwing.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> text, out Address value)
    {
        Unsafe.SkipInit(out value);

        if (text.IsEmpty)
            goto Fail;

        // EVM: 0x + 40 hex
        if (text.Length == 42 && text[0] == '0' && (text[1] == 'x' || text[1] == 'X'))
        {
            if (TryParseEvmHexChars(text.Slice(2), out value))
                return true;
            goto Fail;
        }

        // 40 characters without a prefix: hex, or (rarely) Base58.
        if (text.Length == EvmHexLength)
        {
            if (TryParseBare40Chars(text, out value))
                return true;
            goto Fail;
        }

        // Solana: attempt canonical Base58->32 if in plausible length range.
        if ((uint)(text.Length - 32) <= (MaxBase58Length - 32))
        {
            if (TryParseSolanaBase58Chars(text, out value))
                return true;
        }

    Fail:
        value = default;
        return false;
    }

    /// <summary>
    /// Tries to parse an address from a nullable string.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out Address value)
    {
        if (text is null)
        {
            value = default;
            return false;
        }

        return TryParse(text.AsSpan(), out value);
    }

    /// <summary>
    /// Parses an address from UTF-8 bytes (optionally surrounded by JSON quotes).
    /// </summary>
    public static Address Parse(ReadOnlySpan<byte> utf8)
    {
        if (!TryParse(utf8, out Address value))
            ThrowHelper.ThrowFormatExceptionInvalidAddress();
        return value;
    }

    /// <summary>
    /// Tries to parse an address from UTF-8 bytes (optionally surrounded by JSON quotes) without allocations.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out Address value)
    {
        if (!ByteUtils.TryUnquoteJsonUtf8(utf8, out ReadOnlySpan<byte> unquoted))
        {
            value = default;
            return false;
        }

        return TryParseUnquotedUtf8(unquoted, out value);
    }

    /// <summary>
    /// Parses UTF-8 text that is already unquoted (a JSON string token's contents). Unlike
    /// <see cref="TryParse(ReadOnlySpan{byte}, out Address)"/> it does not strip surrounding quote characters, so a
    /// value that merely contains quotes is rejected.
    /// </summary>
    internal static bool TryParseUnquotedUtf8(ReadOnlySpan<byte> unquoted, out Address value)
    {
        Unsafe.SkipInit(out value);

        // EVM: 0x + 40 hex
        if (unquoted.Length == 42 &&
            unquoted[0] == (byte)'0' &&
            ((unquoted[1] | 0x20) == (byte)'x'))
        {
            if (TryParseEvmHexUtf8(unquoted.Slice(2), out value))
                return true;
            goto Fail;
        }

        // 40 bytes without a prefix: hex, or (rarely) Base58.
        if (unquoted.Length == EvmHexLength)
        {
            if (TryParseBare40Utf8(unquoted, out value))
                return true;
            goto Fail;
        }

        // Solana: attempt canonical Base58->32 in plausible length range.
        if ((uint)(unquoted.Length - 32) <= (MaxBase58Length - 32))
        {
            if (TryParseSolanaBase58Utf8(unquoted, out value))
                return true;
        }

    Fail:
        value = default;
        return false;
    }

    /// <summary>ISpanParsable required method; the format provider is ignored.</summary>
    public static Address Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        if (!TryParse(s, out Address value))
            ThrowHelper.ThrowFormatExceptionInvalidAddress();
        return value;
    }

    /// <summary>ISpanParsable required method; the format provider is ignored.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Address result) => TryParse(s, out result);

    /// <summary>ISpanParsable required method; the format provider is ignored.</summary>
    public static Address Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), provider);
    }

    /// <summary>ISpanParsable required method; the format provider is ignored.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Address result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParse(s.AsSpan(), out result);
    }

    /// <summary>
    /// Parses exactly 40 characters with no <c>0x</c> prefix: EVM hex, unless the text is a canonical Base58 Solana key.
    /// </summary>
    /// <remarks>
    /// Base58 is only worth trying when it can succeed. A 40-character Base58 string with k leading '1's has at most
    /// ceil((40 - k) * log2(58) / 8) significant bytes, but a canonical 32-byte value needs 32 - k, which fails for every
    /// k below 7. So fewer than seven leading '1's, or any '0' (not a Base58 digit), means hex only. That skips a failed
    /// Base58 decode (about 55 ns) for the 8% of addresses that contain no '0'. Kept out of line so the hot prefixed path
    /// in <see cref="TryParse(ReadOnlySpan{char}, out Address)"/> stays small; inlining it cost that path about 40%.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryParseBare40Chars(ReadOnlySpan<char> text, out Address value)
    {
        if (text.StartsWith("1111111") && !text.Contains('0') && TryParseSolanaBase58Chars(text, out value))
            return true;

        return TryParseEvmHexChars(text, out value);
    }

    /// <summary>UTF-8 counterpart of <see cref="TryParseBare40Chars"/>.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryParseBare40Utf8(ReadOnlySpan<byte> utf8, out Address value)
    {
        if (utf8.StartsWith("1111111"u8) && !utf8.Contains((byte)'0') && TryParseSolanaBase58Utf8(utf8, out value))
            return true;

        return TryParseEvmHexUtf8(utf8, out value);
    }

    // Not inlined, for the same reason as TryParseEvmHexUtf8.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryParseEvmHexChars(ReadOnlySpan<char> hex40, out Address value)
    {
        Unsafe.SkipInit(out value);

        if (hex40.Length != EvmHexLength)
            return false;

        if (!ByteUtils.TryParseHexUInt64CharsFixed16(hex40.Slice(0, 16), out ulong u0)) return false;
        if (!ByteUtils.TryParseHexUInt64CharsFixed16(hex40.Slice(16, 16), out ulong u1)) return false;
        if (!ByteUtils.TryParseHexUInt32CharsFixed8(hex40.Slice(32, 8), out uint u2Upper)) return false;

        value = new Address(u0, u1, (ulong)u2Upper << 32, 0, AddressType.Evm);
        return true;
    }

    // Not inlined: the caller already inlines the JSON unquoting and the Base58 branch, and past the JIT's inline budget the
    // hex decode and constructor turn into real calls inside the caller (prefixed parse measured 19 ns against 11 ns for
    // this method called whole). As a method of its own everything in it inlines.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryParseEvmHexUtf8(ReadOnlySpan<byte> hex40Utf8, out Address value)
    {
        Unsafe.SkipInit(out value);

        if (hex40Utf8.Length != EvmHexLength)
            return false;

        if (!ByteUtils.TryParseHexUInt64Utf8Fixed16(hex40Utf8.Slice(0, 16), out ulong u0)) return false;
        if (!ByteUtils.TryParseHexUInt64Utf8Fixed16(hex40Utf8.Slice(16, 16), out ulong u1)) return false;
        if (!ByteUtils.TryParseHexUInt32Utf8Fixed8(hex40Utf8.Slice(32, 8), out uint u2Upper)) return false;

        value = new Address(u0, u1, (ulong)u2Upper << 32, 0, AddressType.Evm);
        return true;
    }

    // Not inlined: the Base58 decode dwarfs any call overhead, and inlining it grew the caller's frame (and its zeroed
    // prolog) for every call, including the common 0x-prefixed hex path that never reaches it.
    private static bool TryParseSolanaBase58Utf8(ReadOnlySpan<byte> base58Utf8, out Address value)
    {
        Unsafe.SkipInit(out value);

        if (!ByteUtils.TryDecodeBase58ToUInt256BigEndian(base58Utf8, out ulong u0, out ulong u1, out ulong u2, out ulong u3))
            return false;

        value = new Address(u0, u1, u2, u3, AddressType.Solana);
        return true;
    }

    private static bool TryParseSolanaBase58Chars(ReadOnlySpan<char> base58Chars, out Address value)
    {
        Unsafe.SkipInit(out value);

        // Narrow to ASCII on the stack; the Base58 alphabet is ASCII, so any other character is a failure.
        Span<byte> tmp = stackalloc byte[MaxBase58Length];
        if (Ascii.FromUtf16(base58Chars, tmp, out int written) != OperationStatus.Done)
            return false;

        return TryParseSolanaBase58Utf8(tmp.Slice(0, written), out value);
    }

    // -------------------------
    // Formatting
    // -------------------------

    /// <summary>
    /// Returns the canonical string representation:
    /// EVM as lowercase <c>0x</c>-prefixed hex; Solana as Base58.
    /// </summary>
    public override string ToString()
    {
        Span<char> buffer = stackalloc char[MaxBase58Length];

        if (!TryFormat(buffer, out int written, format: default, provider: null))
            return _type == AddressType.Evm ? "0x" + new string('0', EvmHexLength) : string.Empty;

        return new string(buffer.Slice(0, written));
    }

    /// <summary>
    /// Formats the address.
    /// <list type="bullet">
    /// <item><description>EVM supports: <c>"0x"</c>, <c>"0X"</c> (uppercase digits), <c>"x"</c>, <c>"X"</c> (uppercase digits), <c>"c"</c>/<c>"C"</c> (EIP-55 checksum). The prefix, when written, is always a lowercase <c>0x</c>.</description></item>
    /// <item><description>Solana ignores the format and returns Base58.</description></item>
    /// </list>
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        Span<char> buffer = stackalloc char[MaxBase58Length];

        ReadOnlySpan<char> f = format is null ? default : format.AsSpan();
        if (!TryFormat(buffer, out int written, f, formatProvider))
            ThrowHelper.ThrowFormatExceptionUnknownFormat(f);

        return new string(buffer.Slice(0, written));
    }

    /// <summary>
    /// Tries to format the address into <paramref name="destination"/>.
    /// </summary>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        if (_type == AddressType.Solana)
        {
            // Base58 UTF-8 -> chars (ASCII copy)
            Span<byte> tmp = stackalloc byte[MaxBase58Length];
            int len = ByteUtils.EncodeBase58UInt256BigEndianToUtf8(_u0, _u1, _u2, _u3, tmp);
            if ((uint)len > (uint)destination.Length)
            {
                charsWritten = 0;
                return false;
            }

            Ascii.ToUtf16(tmp.Slice(0, len), destination, out _);

            charsWritten = len;
            return true;
        }

        if (!TryGetEvmFormat(format, out EvmFormat evmFormat))
        {
            charsWritten = 0;
            return false;
        }

        if (evmFormat.Kind == EvmFormatKind.Checksum)
        {
            return TryFormatEvmChecksumChars(destination, out charsWritten);
        }

        int required = evmFormat.WithPrefix ? (EvmHexLength + 2) : EvmHexLength;
        if (destination.Length < required)
        {
            charsWritten = 0;
            return false;
        }

        int offset = 0;
        if (evmFormat.WithPrefix)
        {
            destination[0] = '0';
            destination[1] = 'x';
            offset = 2;
        }

        WriteEvmHexChars(destination.Slice(offset), evmFormat.HexUppercase);
        charsWritten = required;
        return true;
    }

    /// <summary>
    /// Tries to format the address into UTF-8 bytes without allocations.
    /// </summary>
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        if (_type == AddressType.Solana)
        {
            int len = ByteUtils.EncodeBase58UInt256BigEndianToUtf8(_u0, _u1, _u2, _u3, utf8Destination);
            if (len < 0)
            {
                bytesWritten = 0;
                return false;
            }

            bytesWritten = len;
            return true;
        }

        if (!TryGetEvmFormat(format, out EvmFormat evmFormat))
        {
            bytesWritten = 0;
            return false;
        }

        if (evmFormat.Kind == EvmFormatKind.Checksum)
        {
            return TryFormatEvmChecksumUtf8(utf8Destination, out bytesWritten);
        }

        int required = evmFormat.WithPrefix ? (EvmHexLength + 2) : EvmHexLength;
        if (utf8Destination.Length < required)
        {
            bytesWritten = 0;
            return false;
        }

        int offset = 0;
        if (evmFormat.WithPrefix)
        {
            utf8Destination[0] = (byte)'0';
            utf8Destination[1] = (byte)'x';
            offset = 2;
        }

        WriteEvmHexUtf8(utf8Destination.Slice(offset), evmFormat.HexUppercase);
        bytesWritten = required;
        return true;
    }

    /// <summary>
    /// Returns the EVM address using EIP-55 checksum encoding (mixed-case) with a lowercase <c>0x</c> prefix.
    /// </summary>
    public string ToChecksumString()
    {
        if (_type != AddressType.Evm)
            throw new InvalidOperationException("Checksum encoding only applies to EVM addresses.");

        Span<char> buffer = stackalloc char[EvmHexLength + 2];
        if (!TryFormatEvmChecksumChars(buffer, out int written) || written != EvmHexLength + 2)
            ThrowHelper.ThrowFormatExceptionInvalidAddress();

        return new string(buffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteEvmHexChars(Span<char> hex40, bool uppercase)
    {
        // _u0 (16 hex) + _u1 (16 hex) + upper 32 bits of _u2 (8 hex)
        ByteUtils.WriteHexUInt64CharsFixed16(hex40.Slice(0, 16), _u0, uppercase);
        ByteUtils.WriteHexUInt64CharsFixed16(hex40.Slice(16, 16), _u1, uppercase);
        ByteUtils.WriteHexUInt32CharsFixed8(hex40.Slice(32, 8), (uint)(_u2 >> 32), uppercase);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteEvmHexUtf8(Span<byte> hex40Utf8, bool uppercase)
    {
        // Same layout as WriteEvmHexChars: _u0 (16 hex) + _u1 (16 hex) + upper 32 bits of _u2 (8 hex).
        HexCodec.Encode16(hex40Utf8.Slice(0, 16), _u0, uppercase);
        HexCodec.Encode16(hex40Utf8.Slice(16, 16), _u1, uppercase);
        HexCodec.Encode8(hex40Utf8.Slice(32, 8), (uint)(_u2 >> 32), uppercase);
    }

    /// <summary>
    /// Writes the EIP-55 form (<c>0x</c> + 40 mixed-case hex digits) as ASCII. The caller guarantees 42 bytes.
    /// </summary>
    private void WriteEvmChecksumUtf8(Span<byte> destination42)
    {
        destination42[0] = (byte)'0';
        destination42[1] = (byte)'x';

        Span<byte> hex = destination42.Slice(2, EvmHexLength);
        WriteEvmHexUtf8(hex, uppercase: false);

        // EIP-55 hashes the lowercase hex text, not the raw bytes.
        Span<byte> hash = stackalloc byte[32];
        Keccak256.ComputeHash(hex, hash);

        // Digit i is upper-cased when hash nibble i (high nibble first) is >= 8. Digits 0-9 have no case.
        for (int i = 0; i < EvmHexLength; i++)
        {
            byte c = hex[i];
            if (c >= (byte)'a')
            {
                int nibble = (hash[i >> 1] >> ((~i & 1) << 2)) & 0x0F;
                if (nibble >= 8)
                    hex[i] = (byte)(c - 32);
            }
        }
    }

    private bool TryFormatEvmChecksumChars(Span<char> destination, out int charsWritten)
    {
        const int required = EvmHexLength + 2;
        if (destination.Length < required)
        {
            charsWritten = 0;
            return false;
        }

        Span<byte> ascii = stackalloc byte[required];
        WriteEvmChecksumUtf8(ascii);
        Ascii.ToUtf16(ascii, destination, out _);

        charsWritten = required;
        return true;
    }

    private bool TryFormatEvmChecksumUtf8(Span<byte> utf8Destination, out int bytesWritten)
    {
        const int required = EvmHexLength + 2;
        if (utf8Destination.Length < required)
        {
            bytesWritten = 0;
            return false;
        }

        WriteEvmChecksumUtf8(utf8Destination);

        bytesWritten = required;
        return true;
    }

    private static bool TryGetEvmFormat(ReadOnlySpan<char> format, out EvmFormat evmFormat)
    {
        // Default: "0x" lowercase prefix + lowercase digits.
        if (format.IsEmpty)
        {
            evmFormat = EvmFormat.Default;
            return true;
        }

        // Single-char formats.
        if (format.Length == 1)
        {
            char f = format[0];
            if (f == 'x') { evmFormat = new EvmFormat(withPrefix: false, hexUppercase: false, kind: EvmFormatKind.Hex); return true; }
            if (f == 'X') { evmFormat = new EvmFormat(withPrefix: false, hexUppercase: true, kind: EvmFormatKind.Hex); return true; }
            if (f == 'c' || f == 'C') { evmFormat = new EvmFormat(withPrefix: true, hexUppercase: false, kind: EvmFormatKind.Checksum); return true; }
        }

        // Two-char prefix formats.
        if (format.Length == 2 && format[0] == '0' && (format[1] == 'x' || format[1] == 'X'))
        {
            // "0X" selects uppercase digits; the prefix is always the lowercase "0x" required by Ethereum JSON-RPC.
            evmFormat = new EvmFormat(withPrefix: true, hexUppercase: format[1] == 'X', kind: EvmFormatKind.Hex);
            return true;
        }

        evmFormat = default;
        return false;
    }

    private enum EvmFormatKind : byte
    {
        Hex = 0,
        Checksum = 1
    }

    private readonly struct EvmFormat(bool withPrefix, bool hexUppercase, Address.EvmFormatKind kind)
    {
        public static readonly EvmFormat Default = new(withPrefix: true, hexUppercase: false, kind: EvmFormatKind.Hex);

        public readonly bool WithPrefix = withPrefix;
        public readonly bool HexUppercase = hexUppercase;
        public readonly EvmFormatKind Kind = kind;
    }
}