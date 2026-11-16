using System.Numerics;
using System.Runtime.CompilerServices;

namespace EccentricWare.Web3.DataTypes.Utils;

/// <summary>
/// Base58 (Bitcoin/Solana alphabet) encoding of fixed-width big-endian values held as 32-bit limbs.
/// Shared by <see cref="ByteUtils.EncodeBase58UInt256BigEndianToUtf8"/> (8 limbs) and <see cref="Signature"/> (16 limbs).
/// </summary>
/// <remarks>
/// Three things make this about 1.7x faster than a pass-per-five-digits loop with a counter and a final reverse:
/// <list type="bullet">
/// <item><description>Three division passes by 58^5 run fused in one sweep. Each pass is a chain of dependent divisions
/// (the remainder feeds the next limb); fused passes are independent chains, so the CPU overlaps them.</description></item>
/// <item><description>Digits are written backwards into a fixed buffer from a local position, so there is no reverse and no
/// counter living in memory.</description></item>
/// <item><description>Five digits come from one table lookup per two digits (58^2 pairs) instead of a divide and a modulo per
/// digit.</description></item>
/// </list>
/// A 64-bit-limb variant dividing by 58^10 with a precomputed reciprocal was measured and was not faster: its
/// per-step latency (about 20 cycles) is higher than the cost it saves.
/// </remarks>
internal static class Base58Encoder
{
    /// <summary>58^5, the largest power of 58 below 2^30: each division pass by it yields five Base58 digits.</summary>
    private const ulong Power5 = 656356768UL;

    /// <summary>58^2.</summary>
    private const uint Power2 = 58 * 58;

    /// <summary>58^4.</summary>
    private const uint Power4 = Power2 * Power2;

    /// <summary>Division passes fused into one sweep over the limbs.</summary>
    private const int FusedPasses = 3;

    /// <summary>
    /// Digit buffer size. The widest value (16 limbs, 512 bits) needs ceil(512 / log2(58^5)) = 18 passes, which is 6 sweeps
    /// of 3 passes x 5 digits = 90 digits.
    /// </summary>
    private const int MaxDigits = 90;

    /// <summary>The most 32-bit limbs <see cref="Encode"/> accepts (512 bits); the digit buffer is sized for that.</summary>
    public const int MaxLimbs = 16;

    private static ReadOnlySpan<byte> Alphabet => "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz"u8;

    /// <summary>Two ASCII digits for every value 0..3363 (58^2 entries x 2 bytes).</summary>
    private static readonly byte[] s_pairs = BuildPairs();

    private static byte[] BuildPairs()
    {
        var table = new byte[(int)Power2 * 2];
        ReadOnlySpan<byte> alphabet = Alphabet;
        for (int i = 0; i < Power2; i++)
        {
            table[2 * i] = alphabet[i / 58];
            table[2 * i + 1] = alphabet[i % 58];
        }

        return table;
    }

    /// <summary>
    /// Encodes the value in <paramref name="limbs"/> (most significant limb first, at most 16) as Base58 UTF-8: one '1' per
    /// leading zero byte, then the digits of the rest. <paramref name="limbs"/> is used as scratch and destroyed.
    /// Returns the byte count written, or -1 (writing nothing) if <paramref name="destination"/> is too small.
    /// Throws <see cref="ArgumentException"/> for more than <see cref="MaxLimbs"/> limbs, which the digit buffer cannot hold.
    /// </summary>
    public static int Encode(Span<uint> limbs, Span<byte> destination)
    {
        if (limbs.Length > MaxLimbs)
            throw new ArgumentException($"At most {MaxLimbs} limbs are supported.", nameof(limbs));

        int leadingZeroBytes = CountLeadingZeroBytes(limbs);

        Span<byte> buffer = stackalloc byte[MaxDigits];
        int position = buffer.Length;

        int first = 0;
        while (first < limbs.Length && limbs[first] == 0)
            first++;

        // A sweep divides the value by 58^5 three times in a row; (remainder1, remainder2, remainder3) are the three
        // least significant base-58^5 digits. Dividends stay below 2^62, so each division is a multiply by a constant.
        while (first < limbs.Length)
        {
            ulong r1 = 0, r2 = 0, r3 = 0;
            for (int i = first; i < limbs.Length; i++)
            {
                ulong d1 = (r1 << 32) | limbs[i];
                ulong q1 = d1 / Power5;
                r1 = d1 - q1 * Power5;

                ulong d2 = (r2 << 32) | q1;
                ulong q2 = d2 / Power5;
                r2 = d2 - q2 * Power5;

                ulong d3 = (r3 << 32) | q2;
                ulong q3 = d3 / Power5;
                r3 = d3 - q3 * Power5;

                limbs[i] = (uint)q3;
            }

            while (first < limbs.Length && limbs[first] == 0)
                first++;

            // r1 is the least significant group, so it goes last. The final sweep is padded with zero digits at its top.
            position -= FusedPasses * 5;
            Write5(buffer, position + 10, (uint)r1);
            Write5(buffer, position + 5, (uint)r2);
            Write5(buffer, position, (uint)r3);
        }

        // Drop the padding. An all-zero value has no digits at all (the leading '1's carry it).
        while (position < buffer.Length && buffer[position] == (byte)'1')
            position++;

        int digitCount = buffer.Length - position;
        int total = leadingZeroBytes + digitCount;
        if (destination.Length < total)
            return -1;

        destination.Slice(0, leadingZeroBytes).Fill((byte)'1');
        buffer.Slice(position).CopyTo(destination.Slice(leadingZeroBytes));
        return total;
    }

    /// <summary>
    /// Writes the five digits of <paramref name="value"/> (below 58^5), most significant first, at
    /// <c>buffer[position..position + 5]</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Write5(Span<byte> buffer, int position, uint value)
    {
        // One bounds check for the whole group; the table reads below are in range by construction: value < 58^5, so
        // top < 58 and both pair indexes are below 58^2 (the table has 58^2 entries).
        Span<byte> group = buffer.Slice(position, 5);
        byte[] pairs = s_pairs;

        uint top = value / Power4;
        uint rest = value - top * Power4;
        uint high = rest / Power2;
        uint low = rest - high * Power2;

        group[0] = Alphabet[(int)top];
        Unsafe.WriteUnaligned(ref group[1], Unsafe.ReadUnaligned<ushort>(ref pairs[(int)high * 2]));
        Unsafe.WriteUnaligned(ref group[3], Unsafe.ReadUnaligned<ushort>(ref pairs[(int)low * 2]));
    }

    /// <summary>Leading zero bytes of the big-endian value (all of them when the value is zero).</summary>
    private static int CountLeadingZeroBytes(ReadOnlySpan<uint> limbs)
    {
        for (int i = 0; i < limbs.Length; i++)
        {
            if (limbs[i] != 0)
                return i * 4 + BitOperations.LeadingZeroCount(limbs[i]) / 8;
        }

        return limbs.Length * 4;
    }
}
