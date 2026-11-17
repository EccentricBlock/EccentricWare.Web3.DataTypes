using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace EccentricWare.Web3.DataTypes.Utils;

/// <summary>
/// Branch-free fixed-width hex codec for 32-bit and 64-bit values, shared by <see cref="FunctionSelector"/>,
/// <see cref="InstructionDiscriminator"/> and the public <see cref="ByteUtils"/> helpers.
/// Callers validate span lengths; every method here assumes the exact width and is endian-neutral.
/// </summary>
internal static class HexCodec
{
    /// <summary>Digit value for each byte: 0..15 for [0-9a-fA-F], 0xFF otherwise.</summary>
    private static ReadOnlySpan<byte> NibbleTable =>
    [
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    ];

    // ---------------------------------------------------------------- decode

    /// <summary>Decodes exactly 8 hex digits (<paramref name="hex8"/>.Length must be 8). Returns false on any non-hex byte.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryDecode8(ReadOnlySpan<byte> hex8, out uint value)
    {
        if (hex8.Length != 8)
        {
            value = 0;
            return false;
        }

        ReadOnlySpan<byte> t = NibbleTable;
        uint n0 = t[hex8[0]], n1 = t[hex8[1]], n2 = t[hex8[2]], n3 = t[hex8[3]];
        uint n4 = t[hex8[4]], n5 = t[hex8[5]], n6 = t[hex8[6]], n7 = t[hex8[7]];

        // Valid digits are 0..15; invalid entries are 0xFF, so any high nibble flags the whole group.
        if (((n0 | n1 | n2 | n3 | n4 | n5 | n6 | n7) & 0xF0) != 0)
        {
            value = 0;
            return false;
        }

        value = (n0 << 28) | (n1 << 24) | (n2 << 20) | (n3 << 16) | (n4 << 12) | (n5 << 8) | (n6 << 4) | n7;
        return true;
    }

    /// <summary>Decodes exactly 8 hex characters (<paramref name="hex8"/>.Length must be 8). Rejects any char above 0xFF.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryDecode8(ReadOnlySpan<char> hex8, out uint value)
    {
        if (hex8.Length != 8)
        {
            value = 0;
            return false;
        }

        // The table is indexed by byte, so characters above 0xFF (fullwidth digits, Arabic-Indic digits, ...)
        // must be rejected first or they would alias onto a valid entry after truncation.
        if ((uint)(hex8[0] | hex8[1] | hex8[2] | hex8[3] | hex8[4] | hex8[5] | hex8[6] | hex8[7]) > 0xFF)
        {
            value = 0;
            return false;
        }

        ReadOnlySpan<byte> t = NibbleTable;
        uint n0 = t[hex8[0]], n1 = t[hex8[1]], n2 = t[hex8[2]], n3 = t[hex8[3]];
        uint n4 = t[hex8[4]], n5 = t[hex8[5]], n6 = t[hex8[6]], n7 = t[hex8[7]];

        if (((n0 | n1 | n2 | n3 | n4 | n5 | n6 | n7) & 0xF0) != 0)
        {
            value = 0;
            return false;
        }

        value = (n0 << 28) | (n1 << 24) | (n2 << 20) | (n3 << 16) | (n4 << 12) | (n5 << 8) | (n6 << 4) | n7;
        return true;
    }

    /// <summary>Decodes exactly 16 hex digits (<paramref name="hex16"/>.Length must be 16).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryDecode16(ReadOnlySpan<byte> hex16, out ulong value)
    {
        if (hex16.Length == 16
            && TryDecode8(hex16.Slice(0, 8), out uint high)
            && TryDecode8(hex16.Slice(8, 8), out uint low))
        {
            value = ((ulong)high << 32) | low;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>Decodes exactly 16 hex characters (<paramref name="hex16"/>.Length must be 16).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryDecode16(ReadOnlySpan<char> hex16, out ulong value)
    {
        if (hex16.Length == 16
            && TryDecode8(hex16.Slice(0, 8), out uint high)
            && TryDecode8(hex16.Slice(8, 8), out uint low))
        {
            value = ((ulong)high << 32) | low;
            return true;
        }

        value = 0;
        return false;
    }

    // ---------------------------------------------------------------- encode

    /// <summary>
    /// Spreads the 8 nibbles of <paramref name="value"/> into the 8 bytes of a <see cref="ulong"/>, most significant nibble
    /// in the least significant byte (so little-endian memory order is the order the digits are written in).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong SpreadNibblesMostSignificantFirst(uint value)
    {
        // Spread nibble k of the value into byte k.
        ulong x = value;
        x = (x | (x << 16)) & 0x0000FFFF0000FFFFUL;
        x = (x | (x << 8)) & 0x00FF00FF00FF00FFUL;
        x = (x | (x << 4)) & 0x0F0F0F0F0F0F0F0FUL;

        // Most significant nibble first.
        return BinaryPrimitives.ReverseEndianness(x);
    }

    /// <summary>
    /// Expands the 8 nibbles of <paramref name="value"/> into 8 ASCII hex digits packed in a <see cref="ulong"/>
    /// whose least-significant byte is the first (most significant) digit, i.e. little-endian memory order.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ToAsciiDigits(uint value, bool uppercase)
    {
        ulong x = SpreadNibblesMostSignificantFirst(value);

        // '0' + n, plus 'a' - '0' - 10 (0x27) or 'A' - '0' - 10 (0x07) where n >= 10 (n + 6 sets bit 4 exactly then).
        ulong ge10 = ((x + 0x0606060606060606UL) >> 4) & 0x0101010101010101UL;
        return x + 0x3030303030303030UL + (ge10 * (uppercase ? 0x07UL : 0x27UL));
    }

    /// <summary>Writes 8 hex digits for <paramref name="value"/> (<paramref name="destination8"/>.Length must be at least 8).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Encode8(Span<byte> destination8, uint value, bool uppercase)
        => BinaryPrimitives.WriteUInt64LittleEndian(destination8, ToAsciiDigits(value, uppercase));

    /// <summary>Writes 8 hex characters for <paramref name="value"/> (<paramref name="destination8"/>.Length must be at least 8).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Encode8(Span<char> destination8, uint value, bool uppercase)
    {
        ulong digits = ToAsciiDigits(value, uppercase);
        Vector128<byte> bytes = Vector128.CreateScalarUnsafe(BitConverter.IsLittleEndian ? digits : BinaryPrimitives.ReverseEndianness(digits)).AsByte();

        // Zero-extend each ASCII byte to a UTF-16 code unit.
        Vector128.WidenLower(bytes).CopyTo(MemoryMarshal.Cast<char, ushort>(destination8));
    }

    /// <summary>Writes 16 hex digits for <paramref name="value"/> (<paramref name="destination16"/>.Length must be at least 16).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Encode16(Span<byte> destination16, ulong value, bool uppercase)
    {
        Encode8(destination16, (uint)(value >> 32), uppercase);
        Encode8(destination16.Slice(8), (uint)value, uppercase);
    }

    /// <summary>Writes 16 hex characters for <paramref name="value"/> (<paramref name="destination16"/>.Length must be at least 16).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Encode16(Span<char> destination16, ulong value, bool uppercase)
    {
        Encode8(destination16, (uint)(value >> 32), uppercase);
        Encode8(destination16.Slice(8), (uint)value, uppercase);
    }
}
