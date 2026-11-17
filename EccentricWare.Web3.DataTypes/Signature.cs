using EccentricWare.Web3.DataTypes.JsonConverters;
using EccentricWare.Web3.DataTypes.Utils;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace EccentricWare.Web3.DataTypes;

/// <summary>
/// The type of cryptographic signature.
/// </summary>
public enum SignatureType : byte
{
    /// <summary>
    /// EVM ECDSA signature (65 bytes: r + s + v).
    /// </summary>
    Evm = 0,
    
    /// <summary>
    /// Solana Ed25519 signature (64 bytes).
    /// </summary>
    Solana = 1
}

/// <summary>
/// A cryptographic signature supporting both EVM (65 bytes) and Solana (64 bytes) formats.
/// Optimised for minimal allocations and predictable hot-path performance.
/// </summary>
/// <remarks>
/// Storage layout:
/// - 64 bytes: signature payload (EVM: r||s, Solana: ed25519 signature)
/// - 1 byte:  EVM recovery identifier / parity byte (meaning depends on ingestion policy); always 0 for Solana
/// - 1 byte:  <see cref="SignatureType"/>
/// - 6 bytes: implicit padding, giving the 72-byte, 8-byte-aligned array stride
///
/// Invariant: every constructor stores <c>v == 0</c> for a Solana signature, so equality, hashing and ordering compare
/// <c>v</c> without checking the type first.
///
/// Text forms: hex (with or without a 0x prefix) of 130 digits is EVM, of 128 digits is Solana (so a 64-byte
/// EIP-2098 compact ECDSA signature reads as Solana); Base58 is Solana only and never takes a 0x prefix.
/// Any v byte is accepted for EVM: restricting it to 0/1/27/28 or EIP-155 values is the caller's policy.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
[JsonConverter(typeof(SignatureJsonConverter))]
public readonly struct Signature :
    IEquatable<Signature>,
    IComparable<Signature>,
    IComparable,
    ISpanFormattable,
    ISpanParsable<Signature>,
    IUtf8SpanFormattable
{
    /// <summary>
    /// The size in bytes of an EVM signature (65 bytes).
    /// </summary>
    public const int EvmByteLength = 65;

    /// <summary>
    /// The size in bytes of a Solana signature (64 bytes).
    /// </summary>
    public const int SolanaByteLength = 64;

    /// <summary>
    /// The size in hex characters of an EVM signature without a 0x prefix (130 chars).
    /// </summary>
    public const int EvmHexLength = 130;

    /// <summary>
    /// The size in hex characters of a Solana signature without a 0x prefix (128 chars).
    /// </summary>
    public const int SolanaHexLength = 128;

    /// <summary>
    /// Typical maximum Base58 length for a 64-byte value (Solana signatures are commonly 87-88 chars).
    /// Used only as a DoS hygiene cap during parsing.
    /// </summary>
    public const int SolanaMaxBase58Length = 96;

    /// <summary>
    /// The longest Base58 text a 64-byte Solana signature encodes to (88 characters; typically 87 or 88).
    /// </summary>
    public const int SolanaMaxBase58EncodedLength = 88;

    private readonly ulong _u0; // bytes 0-7
    private readonly ulong _u1; // bytes 8-15
    private readonly ulong _u2; // bytes 16-23
    private readonly ulong _u3; // bytes 24-31
    private readonly ulong _u4; // bytes 32-39
    private readonly ulong _u5; // bytes 40-47
    private readonly ulong _u6; // bytes 48-55
    private readonly ulong _u7; // bytes 56-63

    private readonly byte _v;           // EVM-only: recovery identifier / parity byte (0 for Solana)
    private readonly SignatureType _type;

    /// <summary>
    /// A zero signature value (defaults to <see cref="SignatureType.Evm"/> with all-zero payload).
    /// </summary>
    public static readonly Signature Zero = ZeroEvm;

    /// <summary>
    /// Creates a signature from raw fields.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Signature(
        ulong u0, ulong u1, ulong u2, ulong u3,
        ulong u4, ulong u5, ulong u6, ulong u7,
        byte v,
        SignatureType type)
    {
        _u0 = u0; _u1 = u1; _u2 = u2; _u3 = u3;
        _u4 = u4; _u5 = u5; _u6 = u6; _u7 = u7;
        _v = v;
        _type = type;
    }

    /// <summary>
    /// Gets the signature type (EVM or Solana).
    /// </summary>
    public SignatureType Type
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _type;
    }

    /// <summary>
    /// Gets the encoded byte length of this signature (65 for EVM, 64 for Solana).
    /// </summary>
    public int ByteLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _type == SignatureType.Evm ? EvmByteLength : SolanaByteLength;
    }

    /// <summary>
    /// Gets the encoded hex length (without 0x prefix): 130 for EVM, 128 for Solana.
    /// </summary>
    public int HexLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _type == SignatureType.Evm ? EvmHexLength : SolanaHexLength;
    }

    /// <summary>
    /// Returns <c>true</c> if the signature payload and v are all zero, whatever the <see cref="Type"/>.
    /// </summary>
    public bool IsZero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_u0 | _u1 | _u2 | _u3 | _u4 | _u5 | _u6 | _u7 | _v) == 0;
    }

    /// <summary>
    /// Gets the EVM recovery identifier / parity byte.
    /// Returns 0 for Solana signatures.
    /// </summary>
    public byte V
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _type == SignatureType.Evm ? _v : (byte)0;
    }

    /// <summary>
    /// Creates an EVM signature from r (32 bytes), s (32 bytes), and v (1 byte).
    /// </summary>
    public static Signature FromEvmComponents(ReadOnlySpan<byte> r32, ReadOnlySpan<byte> s32, byte v)
    {
        if (r32.Length != 32)
            ThrowHelper.ThrowArgumentExceptionInvalidSignatureRLength(nameof(r32));
        if (s32.Length != 32)
            ThrowHelper.ThrowArgumentExceptionInvalidSignatureSLength(nameof(s32));

        return new Signature(
            BinaryPrimitives.ReadUInt64BigEndian(r32),
            BinaryPrimitives.ReadUInt64BigEndian(r32.Slice(8)),
            BinaryPrimitives.ReadUInt64BigEndian(r32.Slice(16)),
            BinaryPrimitives.ReadUInt64BigEndian(r32.Slice(24)),
            BinaryPrimitives.ReadUInt64BigEndian(s32),
            BinaryPrimitives.ReadUInt64BigEndian(s32.Slice(8)),
            BinaryPrimitives.ReadUInt64BigEndian(s32.Slice(16)),
            BinaryPrimitives.ReadUInt64BigEndian(s32.Slice(24)),
            v,
            SignatureType.Evm);
    }

    /// <summary>
    /// Creates an EVM signature from a 65-byte span (r || s || v).
    /// </summary>
    public static Signature FromEvmBytes(ReadOnlySpan<byte> signature65)
    {
        if (signature65.Length != EvmByteLength)
            ThrowHelper.ThrowArgumentExceptionInvalidEvmSignatureLength(nameof(signature65));

        return new Signature(
            BinaryPrimitives.ReadUInt64BigEndian(signature65),
            BinaryPrimitives.ReadUInt64BigEndian(signature65.Slice(8)),
            BinaryPrimitives.ReadUInt64BigEndian(signature65.Slice(16)),
            BinaryPrimitives.ReadUInt64BigEndian(signature65.Slice(24)),
            BinaryPrimitives.ReadUInt64BigEndian(signature65.Slice(32)),
            BinaryPrimitives.ReadUInt64BigEndian(signature65.Slice(40)),
            BinaryPrimitives.ReadUInt64BigEndian(signature65.Slice(48)),
            BinaryPrimitives.ReadUInt64BigEndian(signature65.Slice(56)),
            signature65[64],
            SignatureType.Evm);
    }

    /// <summary>
    /// Creates a Solana signature from a 64-byte span.
    /// </summary>
    public static Signature FromSolanaBytes(ReadOnlySpan<byte> signature64)
    {
        if (signature64.Length != SolanaByteLength)
            ThrowHelper.ThrowArgumentExceptionInvalidSolanaSignatureLength(nameof(signature64));

        return new Signature(
            BinaryPrimitives.ReadUInt64BigEndian(signature64),
            BinaryPrimitives.ReadUInt64BigEndian(signature64.Slice(8)),
            BinaryPrimitives.ReadUInt64BigEndian(signature64.Slice(16)),
            BinaryPrimitives.ReadUInt64BigEndian(signature64.Slice(24)),
            BinaryPrimitives.ReadUInt64BigEndian(signature64.Slice(32)),
            BinaryPrimitives.ReadUInt64BigEndian(signature64.Slice(40)),
            BinaryPrimitives.ReadUInt64BigEndian(signature64.Slice(48)),
            BinaryPrimitives.ReadUInt64BigEndian(signature64.Slice(56)),
            0,
            SignatureType.Solana);
    }

    /// <summary>
    /// Writes the raw signature bytes.
    /// EVM writes 65 bytes (r || s || v), Solana writes 64 bytes.
    /// </summary>
    public void WriteBytes(Span<byte> destinationBytes)
    {
        int requiredLength = ByteLength;
        if (destinationBytes.Length < requiredLength)
            ThrowHelper.ThrowArgumentExceptionDestinationTooSmall(nameof(destinationBytes));

        BinaryPrimitives.WriteUInt64BigEndian(destinationBytes, _u0);
        BinaryPrimitives.WriteUInt64BigEndian(destinationBytes.Slice(8), _u1);
        BinaryPrimitives.WriteUInt64BigEndian(destinationBytes.Slice(16), _u2);
        BinaryPrimitives.WriteUInt64BigEndian(destinationBytes.Slice(24), _u3);
        BinaryPrimitives.WriteUInt64BigEndian(destinationBytes.Slice(32), _u4);
        BinaryPrimitives.WriteUInt64BigEndian(destinationBytes.Slice(40), _u5);
        BinaryPrimitives.WriteUInt64BigEndian(destinationBytes.Slice(48), _u6);
        BinaryPrimitives.WriteUInt64BigEndian(destinationBytes.Slice(56), _u7);

        if (_type == SignatureType.Evm)
            destinationBytes[64] = _v;
    }

    /// <summary>
    /// Returns the signature as a newly allocated byte array.
    /// Intended for cold paths (e.g., persistence, diagnostics).
    /// </summary>
    public byte[] ToBytes()
    {
        byte[] bytes = new byte[ByteLength];
        WriteBytes(bytes);
        return bytes;
    }

    /// <summary>
    /// Gets the EVM r component as a <c>Hash32</c> (first 32 bytes of the payload).
    /// </summary>
    public Hash32 R => new(_u0, _u1, _u2, _u3);

    /// <summary>
    /// Gets the EVM s component as a <c>Hash32</c> (second 32 bytes of the payload).
    /// </summary>
    public Hash32 S => new(_u4, _u5, _u6, _u7);

    /// <summary>
    /// Writes the EVM r component (32 bytes) to the destination span.
    /// For Solana signatures, this writes the first 32 bytes of the signature payload.
    /// </summary>
    public void WriteR(Span<byte> destination32)
    {
        if (destination32.Length < 32)
            ThrowHelper.ThrowArgumentExceptionDestinationTooSmall(nameof(destination32));

        BinaryPrimitives.WriteUInt64BigEndian(destination32, _u0);
        BinaryPrimitives.WriteUInt64BigEndian(destination32.Slice(8), _u1);
        BinaryPrimitives.WriteUInt64BigEndian(destination32.Slice(16), _u2);
        BinaryPrimitives.WriteUInt64BigEndian(destination32.Slice(24), _u3);
    }

    /// <summary>
    /// Writes the EVM s component (32 bytes) to the destination span.
    /// For Solana signatures, this writes bytes 32..63 of the signature payload.
    /// </summary>
    public void WriteS(Span<byte> destination32)
    {
        if (destination32.Length < 32)
            ThrowHelper.ThrowArgumentExceptionDestinationTooSmall(nameof(destination32));

        BinaryPrimitives.WriteUInt64BigEndian(destination32, _u4);
        BinaryPrimitives.WriteUInt64BigEndian(destination32.Slice(8), _u5);
        BinaryPrimitives.WriteUInt64BigEndian(destination32.Slice(16), _u6);
        BinaryPrimitives.WriteUInt64BigEndian(destination32.Slice(24), _u7);
    }

    /// <summary>
    /// Checks equality by type, payload and v.
    /// A single branch-free XOR/OR reduction: Solana signatures always have v == 0, so v needs no type check.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Signature other)
    {
        ulong diff =
            (_u0 ^ other._u0) | (_u1 ^ other._u1) | (_u2 ^ other._u2) | (_u3 ^ other._u3) |
            (_u4 ^ other._u4) | (_u5 ^ other._u5) | (_u6 ^ other._u6) | (_u7 ^ other._u7) |
            (uint)((_v ^ other._v) | ((byte)_type ^ (byte)other._type));

        return diff == 0;
    }

    /// <summary>
    /// Checks equality against an object.
    /// </summary>
    public override bool Equals(object? obj) => obj is Signature other && Equals(other);

    /// <summary>
    /// Computes a hash code consistent with <see cref="Equals(Signature)"/>.
    /// </summary>
    /// <remarks>
    /// Covers every payload byte, v and the type: a producer can choose signature bytes, so ignoring r or s would let it
    /// fill one dictionary bucket. Each half goes through the per-process keyed <see cref="Hash256"/>, so colliding
    /// inputs cannot be precomputed.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        int r = Hash256.Compute(_u0, _u1, _u2, _u3);
        int s = Hash256.Compute(_u4, _u5, _u6, _u7);
        int meta = (_v << 1) | (byte)_type;
        return unchecked((r * 31 + s) * 31 + meta);
    }

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(Signature left, Signature right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(Signature left, Signature right) => !left.Equals(right);

    /// <summary>
    /// Lexicographic comparison by type, then payload (big-endian byte order), then v.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(Signature other)
    {
        if (_type != other._type)
            return _type < other._type ? -1 : 1;

        if (_u0 != other._u0) return _u0 < other._u0 ? -1 : 1;
        if (_u1 != other._u1) return _u1 < other._u1 ? -1 : 1;
        if (_u2 != other._u2) return _u2 < other._u2 ? -1 : 1;
        if (_u3 != other._u3) return _u3 < other._u3 ? -1 : 1;
        if (_u4 != other._u4) return _u4 < other._u4 ? -1 : 1;
        if (_u5 != other._u5) return _u5 < other._u5 ? -1 : 1;
        if (_u6 != other._u6) return _u6 < other._u6 ? -1 : 1;
        if (_u7 != other._u7) return _u7 < other._u7 ? -1 : 1;

        if (_v != other._v)
            return _v < other._v ? -1 : 1;

        return 0;
    }

    /// <summary>
    /// Non-generic comparison.
    /// </summary>
    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        if (obj is Signature other) return CompareTo(other);
        throw new ArgumentException($"Object must be of type {nameof(Signature)}", nameof(obj));
    }

    /// <summary>
    /// Less-than operator.
    /// </summary>
    public static bool operator <(Signature left, Signature right) => left.CompareTo(right) < 0;

    /// <summary>
    /// Greater-than operator.
    /// </summary>
    public static bool operator >(Signature left, Signature right) => left.CompareTo(right) > 0;

    /// <summary>
    /// Less-than-or-equal operator.
    /// </summary>
    public static bool operator <=(Signature left, Signature right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Greater-than-or-equal operator.
    /// </summary>
    public static bool operator >=(Signature left, Signature right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Parses a signature from hex (0x prefix optional) or Solana Base58.
    /// </summary>
    /// <exception cref="FormatException">The text is not a valid signature.</exception>
    public static Signature Parse(ReadOnlySpan<char> text)
    {
        if (!TryParse(text, out Signature value))
            ThrowHelper.ThrowFormatExceptionInvalidSignature();

        return value;
    }

    /// <summary>
    /// Parses a signature from UTF-8 (typically a JSON string token value).
    /// Accepts 0x-prefixed hex, bare hex, or Solana Base58 for 64-byte signatures.
    /// </summary>
    /// <exception cref="FormatException">The text is not a valid signature.</exception>
    public static Signature Parse(ReadOnlySpan<byte> utf8)
    {
        if (!TryParse(utf8, out Signature value))
            ThrowHelper.ThrowFormatExceptionInvalidSignature();

        return value;
    }

    /// <summary>
    /// Tries to parse a signature from chars without allocating.
    /// Accepts hex with an optional 0x prefix (130 digits: EVM, 128 digits: Solana) or Solana Base58 (no prefix).
    /// Surrounding ASCII whitespace is ignored.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> text, out Signature value)
        => TryParseCore(ByteUtils.TrimAsciiWhitespace(text), out value);

    /// <summary>
    /// Tries to parse a signature from UTF-8 without allocating.
    /// Accepts quoted JSON string values, 0x-prefixed hex, bare hex, or Solana Base58 for 64-byte signatures.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out Signature value)
    {
        utf8 = ByteUtils.TrimAsciiWhitespaceUtf8(utf8);
        if (!ByteUtils.TryUnquoteJsonUtf8(utf8, out ReadOnlySpan<byte> token))
        {
            value = default;
            return false;
        }

        return TryParseCore(token, out value);
    }

    /// <summary>
    /// Parses the content of a JSON string token. Unlike <see cref="TryParse(ReadOnlySpan{byte}, out Signature)"/> it never
    /// strips quote characters: inside a token they are content, not framing.
    /// </summary>
    internal static bool TryParseToken(ReadOnlySpan<byte> utf8, out Signature value)
        => TryParseCore(ByteUtils.TrimAsciiWhitespaceUtf8(utf8), out value);

    private static bool TryParseCore(ReadOnlySpan<char> token, out Signature value)
    {
        bool hasPrefix = token.Length >= 2 && token[0] == '0' && (token[1] | 0x20) == 'x';
        if (hasPrefix)
            token = token.Slice(2);

        if (token.Length == EvmHexLength)
        {
            Span<byte> raw = stackalloc byte[EvmByteLength];
            if (Convert.FromHexString(token, raw, out _, out int written) == OperationStatus.Done && written == EvmByteLength)
            {
                value = FromEvmBytes(raw);
                return true;
            }
        }
        else if (token.Length == SolanaHexLength)
        {
            Span<byte> raw = stackalloc byte[SolanaByteLength];
            if (Convert.FromHexString(token, raw, out _, out int written) == OperationStatus.Done && written == SolanaByteLength)
            {
                value = FromSolanaBytes(raw);
                return true;
            }
        }
        else if (!hasPrefix && (uint)(token.Length - 1) < SolanaMaxBase58Length)
        {
            // Base58 never carries a 0x prefix: "0x" means hex, so one signature has one spelling per encoding.
            Span<ulong> limbs = stackalloc ulong[8];
            if (Base58Decoder.TryDecode64(token, limbs))
            {
                value = FromBase58Limbs(limbs);
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryParseCore(ReadOnlySpan<byte> token, out Signature value)
    {
        bool hasPrefix = token.Length >= 2 && token[0] == (byte)'0' && (token[1] | 0x20) == (byte)'x';
        if (hasPrefix)
            token = token.Slice(2);

        if (token.Length == EvmHexLength)
        {
            Span<byte> raw = stackalloc byte[EvmByteLength];
            if (Convert.FromHexString(token, raw, out _, out int written) == OperationStatus.Done && written == EvmByteLength)
            {
                value = FromEvmBytes(raw);
                return true;
            }
        }
        else if (token.Length == SolanaHexLength)
        {
            Span<byte> raw = stackalloc byte[SolanaByteLength];
            if (Convert.FromHexString(token, raw, out _, out int written) == OperationStatus.Done && written == SolanaByteLength)
            {
                value = FromSolanaBytes(raw);
                return true;
            }
        }
        else if (!hasPrefix && (uint)(token.Length - 1) < SolanaMaxBase58Length)
        {
            Span<ulong> limbs = stackalloc ulong[8];
            if (Base58Decoder.TryDecode64(token, limbs))
            {
                value = FromBase58Limbs(limbs);
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Formats to a 0x-prefixed lowercase hex string.
    /// Intended for cold paths (logging, debugging, serialisation).
    /// </summary>
    public override string ToString()
    {
        return string.Create(HexLength + 2, this, static (chars, sig) =>
        {
            chars[0] = '0';
            chars[1] = 'x';
            sig.FormatHexChars(chars.Slice(2), uppercase: false);
        });
    }

    /// <summary>
    /// Formats to hex with optional prefix and casing.
    /// Supported formats (as for the other types in this library):
    /// - empty / "0x": 0x prefix, lowercase digits
    /// - "0X": 0x prefix, uppercase digits
    /// - "x" / "X": no prefix, lowercase/uppercase digits
    /// </summary>
    /// <exception cref="FormatException">Any other format.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider = null)
    {
        HexFormat.Parse(format.AsSpanSafe(), out bool hasPrefix, out bool uppercase);

        int totalLength = hasPrefix ? HexLength + 2 : HexLength;

        return string.Create(totalLength, (this, hasPrefix, uppercase), static (chars, state) =>
        {
            int offset = 0;
            if (state.hasPrefix)
            {
                chars[0] = '0';
                chars[1] = 'x';
                offset = 2;
            }

            state.Item1.FormatHexChars(chars.Slice(offset), state.uppercase);
        });
    }

    /// <summary>
    /// Tries to format to a character span. Writes nothing and returns false if the destination is too small.
    /// </summary>
    /// <exception cref="FormatException">Unknown format.</exception>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        HexFormat.Parse(format, out bool hasPrefix, out bool uppercase);

        int required = hasPrefix ? HexLength + 2 : HexLength;
        if (destination.Length < required)
        {
            charsWritten = 0;
            return false;
        }

        int offset = 0;
        if (hasPrefix)
        {
            destination[0] = '0';
            destination[1] = 'x';
            offset = 2;
        }

        FormatHexChars(destination.Slice(offset), uppercase);
        charsWritten = required;
        return true;
    }

    /// <summary>
    /// Tries to format to a UTF-8 span. Writes nothing and returns false if the destination is too small.
    /// </summary>
    /// <exception cref="FormatException">Unknown format.</exception>
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        HexFormat.Parse(format, out bool hasPrefix, out bool uppercase);

        int required = hasPrefix ? HexLength + 2 : HexLength;
        if (utf8Destination.Length < required)
        {
            bytesWritten = 0;
            return false;
        }

        int offset = 0;
        if (hasPrefix)
        {
            utf8Destination[0] = (byte)'0';
            utf8Destination[1] = (byte)'x';
            offset = 2;
        }

        FormatHexUtf8(utf8Destination.Slice(offset), uppercase);
        bytesWritten = required;
        return true;
    }

    /// <summary>
    /// Parses from a string (provider is ignored; parsing is invariant).
    /// </summary>
    public static Signature Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), provider);
    }

    /// <summary>
    /// Parses from a span (provider is ignored; parsing is invariant).
    /// </summary>
    public static Signature Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        if (!TryParse(s, out Signature value))
            ThrowHelper.ThrowFormatExceptionInvalidSignature();

        return value;
    }

    /// <summary>
    /// Tries to parse from a string.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Signature result)
        => TryParse(s.AsSpanSafe(), out result);

    /// <summary>
    /// Tries to parse from a span (provider is ignored; parsing is invariant).
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Signature result)
        => TryParse(s, out result);

    /// <summary>
    /// Creates a zero EVM signature.
    /// </summary>
    public static Signature ZeroEvm => new(0, 0, 0, 0, 0, 0, 0, 0, 0, SignatureType.Evm);

    /// <summary>
    /// Creates a zero Solana signature.
    /// </summary>
    public static Signature ZeroSolana => new(0, 0, 0, 0, 0, 0, 0, 0, 0, SignatureType.Solana);

    /// <summary>
    /// Formats a Solana signature as canonical Base58 (the form Solana JSON-RPC and explorers use), as a string.
    /// </summary>
    /// <exception cref="InvalidOperationException">This is an EVM signature; Base58 is only defined for Solana signatures.</exception>
    public string ToBase58String()
    {
        Span<byte> utf8 = stackalloc byte[SolanaMaxBase58EncodedLength];
        int written = EncodeBase58(utf8);
        if (written < 0)
            ThrowHelper.ThrowInvalidOperationExceptionBase58RequiresSolanaSignature();

        return System.Text.Encoding.ASCII.GetString(utf8.Slice(0, written));
    }

    /// <summary>
    /// Tries to format a Solana signature as Base58 into a character span (at most
    /// <see cref="SolanaMaxBase58EncodedLength"/> characters). Returns false and writes nothing for an EVM signature or a
    /// destination that is too small.
    /// </summary>
    public bool TryFormatBase58(Span<char> destination, out int charsWritten)
    {
        Span<byte> utf8 = stackalloc byte[SolanaMaxBase58EncodedLength];
        int written = EncodeBase58(utf8);
        if (written < 0 || destination.Length < written)
        {
            charsWritten = 0;
            return false;
        }

        for (int i = 0; i < written; i++)
            destination[i] = (char)utf8[i];

        charsWritten = written;
        return true;
    }

    /// <summary>
    /// Tries to format a Solana signature as Base58 into a UTF-8 span (at most
    /// <see cref="SolanaMaxBase58EncodedLength"/> bytes). Returns false and writes nothing for an EVM signature or a
    /// destination that is too small.
    /// </summary>
    public bool TryFormatBase58(Span<byte> utf8Destination, out int bytesWritten)
    {
        int written = EncodeBase58(utf8Destination);
        if (written < 0)
        {
            bytesWritten = 0;
            return false;
        }

        bytesWritten = written;
        return true;
    }

    /// <summary>Returns the byte count written, or -1 for an EVM signature or a destination that is too small.</summary>
    private int EncodeBase58(Span<byte> destination)
    {
        if (_type != SignatureType.Solana)
            return -1;

        // Sixteen 32-bit limbs, most significant first.
        Span<uint> limbs = stackalloc uint[16];
        limbs[0] = (uint)(_u0 >> 32); limbs[1] = (uint)_u0;
        limbs[2] = (uint)(_u1 >> 32); limbs[3] = (uint)_u1;
        limbs[4] = (uint)(_u2 >> 32); limbs[5] = (uint)_u2;
        limbs[6] = (uint)(_u3 >> 32); limbs[7] = (uint)_u3;
        limbs[8] = (uint)(_u4 >> 32); limbs[9] = (uint)_u4;
        limbs[10] = (uint)(_u5 >> 32); limbs[11] = (uint)_u5;
        limbs[12] = (uint)(_u6 >> 32); limbs[13] = (uint)_u6;
        limbs[14] = (uint)(_u7 >> 32); limbs[15] = (uint)_u7;

        return Base58Encoder.Encode(limbs, destination);
    }

    /// <summary>Builds a Solana signature from 8 little-endian 64-bit limbs (limb 0 least significant).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Signature FromBase58Limbs(ReadOnlySpan<ulong> limbs)
        => new(
            limbs[7], limbs[6], limbs[5], limbs[4],
            limbs[3], limbs[2], limbs[1], limbs[0],
            0,
            SignatureType.Solana);

    // Hex goes through the BCL's vectorised encoder and decoder: one pass over the 64 or 65 bytes beat the eight-limb
    // scalar spread by about 3x (format) and 2x (parse) in a direct comparison.
    private void FormatHexChars(Span<char> destination, bool uppercase)
    {
        Span<byte> raw = stackalloc byte[EvmByteLength];
        WriteBytes(raw);
        ReadOnlySpan<byte> bytes = raw.Slice(0, ByteLength);

        bool written = uppercase
            ? Convert.TryToHexString(bytes, destination, out _)
            : Convert.TryToHexStringLower(bytes, destination, out _);

        // Callers check the length first; keep the failure loud (as the bounds-checked slices did) if one ever does not.
        if (!written)
            ThrowHelper.ThrowArgumentExceptionDestinationTooSmall(nameof(destination));
    }

    private void FormatHexUtf8(Span<byte> destination, bool uppercase)
    {
        Span<byte> raw = stackalloc byte[EvmByteLength];
        WriteBytes(raw);
        ReadOnlySpan<byte> bytes = raw.Slice(0, ByteLength);

        bool written = uppercase
            ? Convert.TryToHexString(bytes, destination, out _)
            : Convert.TryToHexStringLower(bytes, destination, out _);

        // Callers check the length first; keep the failure loud (as the bounds-checked slices did) if one ever does not.
        if (!written)
            ThrowHelper.ThrowArgumentExceptionDestinationTooSmall(nameof(destination));
    }
}
