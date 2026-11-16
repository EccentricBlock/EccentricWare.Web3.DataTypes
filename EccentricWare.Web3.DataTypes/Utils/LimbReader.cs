using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.Utils;

/// <summary>
/// Reads 64-bit limbs from byte sequences shorter than 32 bytes without a padding buffer.
/// </summary>
internal static class LimbReader
{
    /// <summary>
    /// Takes up to eight bytes ending at <paramref name="end"/> (exclusive) as a big-endian limb and moves
    /// <paramref name="end"/> back. Bytes the sequence does not have are taken from <paramref name="fill"/>
    /// (zero for unsigned values, all ones to sign-extend a negative two's complement value).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong TakeBigEndianTail(ReadOnlySpan<byte> bytes, ref int end, ulong fill)
    {
        int take = Math.Min(8, end);
        int start = end - take;
        ulong value;

        if (take == 8)
        {
            value = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(start, 8));
        }
        else
        {
            // 0..7 bytes: at most one 4-, one 2- and one 1-byte read (take's bits), most significant bytes first.
            value = 0;
            int position = start;
            if ((take & 4) != 0)
            {
                value = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(position, 4));
                position += 4;
            }

            if ((take & 2) != 0)
            {
                value = (value << 16) | BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(position, 2));
                position += 2;
            }

            if ((take & 1) != 0)
                value = (value << 8) | bytes[position];

            value |= fill << (take * 8); // take is 0..7, so the shift is below 64
        }

        end = start;
        return value;
    }

    /// <summary>
    /// Reads the little-endian limb that starts at byte <paramref name="start"/>; bytes past the end of the sequence are zero.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ReadLittleEndianLimb(ReadOnlySpan<byte> bytes, int start)
    {
        int take = Math.Clamp(bytes.Length - start, 0, 8);
        if (take == 8)
            return BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(start, 8));

        // 0..7 bytes: at most one 4-, one 2- and one 1-byte read, least significant bytes first.
        ulong value = 0;
        int position = start;
        int shift = 0;
        if ((take & 4) != 0)
        {
            value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position, 4));
            position += 4;
            shift = 32;
        }

        if ((take & 2) != 0)
        {
            value |= (ulong)BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(position, 2)) << shift;
            position += 2;
            shift += 16;
        }

        if ((take & 1) != 0)
            value |= (ulong)bytes[position] << shift;

        return value;
    }
}
