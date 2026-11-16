using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EccentricWare.Web3.DataTypes.Utils;

/// <summary>
/// Allocation-free 256-bit multiplication and division on 64-bit limbs, shared by <see cref="uint256"/> and
/// <see cref="int256"/> (which works on magnitudes). Limbs are little-endian: limb 0 is the least significant.
/// </summary>
internal static class UInt256Math
{
    /// <summary>10^19, the largest power of ten that fits in a <see cref="ulong"/>.</summary>
    public const ulong TenPow19 = 10_000_000_000_000_000_000UL;

    /// <summary>Computes <c>a * b + c + d</c> as a 128-bit result; this can never overflow 128 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MultiplyAdd(ulong a, ulong b, ulong c, ulong d, out ulong low)
    {
        ulong high = Math.BigMul(a, b, out ulong product);
        ulong sum = product + c;
        high += sum < product ? 1UL : 0UL;
        ulong sum2 = sum + d;
        high += sum2 < sum ? 1UL : 0UL;
        low = sum2;
        return high;
    }

    // ------------------------------------------------------------------ multiplication

    /// <summary>Returns the low 256 bits of <paramref name="left"/> * <paramref name="right"/> (wraps modulo 2^256).</summary>
    public static uint256 MultiplyLow(in uint256 left, in uint256 right)
    {
        ulong a0 = left.U0, a1 = left.U1, a2 = left.U2, a3 = left.U3;
        ulong b0 = right.U0, b1 = right.U1, b2 = right.U2, b3 = right.U3;

        // Common case: both operands fit in one limb.
        if ((a1 | a2 | a3 | b1 | b2 | b3) == 0)
        {
            ulong high = Math.BigMul(a0, b0, out ulong low);
            return new uint256(low, high, 0, 0);
        }

        // Only partial products that land in limbs 0..3 are needed; the last term of each row needs its low half only.
        ulong r0, r1, r2, r3;
        ulong c = MultiplyAdd(a0, b0, 0, 0, out r0);
        c = MultiplyAdd(a0, b1, 0, c, out r1);
        c = MultiplyAdd(a0, b2, 0, c, out r2);
        r3 = a0 * b3 + c;

        c = MultiplyAdd(a1, b0, r1, 0, out r1);
        c = MultiplyAdd(a1, b1, r2, c, out r2);
        r3 += a1 * b2 + c;

        c = MultiplyAdd(a2, b0, r2, 0, out r2);
        r3 += a2 * b1 + c;

        r3 += a3 * b0;
        return new uint256(r0, r1, r2, r3);
    }

    /// <summary>Multiplies two values; returns false (and zero) when the product needs more than 256 bits.</summary>
    public static bool TryMultiply(in uint256 left, in uint256 right, out uint256 result)
    {
        ulong a0 = left.U0, a1 = left.U1, a2 = left.U2, a3 = left.U3;
        ulong b0 = right.U0, b1 = right.U1, b2 = right.U2, b3 = right.U3;

        if ((a1 | a2 | a3 | b1 | b2 | b3) == 0)
        {
            ulong high = Math.BigMul(a0, b0, out ulong low);
            result = new uint256(low, high, 0, 0);
            return true;
        }

        // Highest nonzero limb index of each operand (-1 for zero). The product is at least
        // 2^(64 * (indexA + indexB)), so indexA + indexB >= 4 always overflows.
        int indexA = HighestLimbIndex(a0, a1, a2, a3);
        int indexB = HighestLimbIndex(b0, b1, b2, b3);
        if (indexA < 0 || indexB < 0)
        {
            result = default;
            return true;
        }

        if (indexA + indexB >= 4)
        {
            result = default;
            return false;
        }

        ulong r0, r1, r2, r3, c;

        if (indexA <= 1 && indexB <= 1)
        {
            // 128 x 128 bits always fits in 256 bits.
            c = MultiplyAdd(a0, b0, 0, 0, out r0);
            c = MultiplyAdd(a0, b1, 0, c, out r1);
            r2 = c;
            c = MultiplyAdd(a1, b0, r1, 0, out r1);
            c = MultiplyAdd(a1, b1, r2, c, out r2);
            r3 = c;
            result = new uint256(r0, r1, r2, r3);
            return true;
        }

        // Full schoolbook product; the upper four limbs must stay zero.
        ulong r4, r5, r6, r7;
        c = MultiplyAdd(a0, b0, 0, 0, out r0);
        c = MultiplyAdd(a0, b1, 0, c, out r1);
        c = MultiplyAdd(a0, b2, 0, c, out r2);
        c = MultiplyAdd(a0, b3, 0, c, out r3);
        r4 = c;

        c = MultiplyAdd(a1, b0, r1, 0, out r1);
        c = MultiplyAdd(a1, b1, r2, c, out r2);
        c = MultiplyAdd(a1, b2, r3, c, out r3);
        c = MultiplyAdd(a1, b3, r4, c, out r4);
        r5 = c;

        c = MultiplyAdd(a2, b0, r2, 0, out r2);
        c = MultiplyAdd(a2, b1, r3, c, out r3);
        c = MultiplyAdd(a2, b2, r4, c, out r4);
        c = MultiplyAdd(a2, b3, r5, c, out r5);
        r6 = c;

        c = MultiplyAdd(a3, b0, r3, 0, out r3);
        c = MultiplyAdd(a3, b1, r4, c, out r4);
        c = MultiplyAdd(a3, b2, r5, c, out r5);
        c = MultiplyAdd(a3, b3, r6, c, out r6);
        r7 = c;

        if ((r4 | r5 | r6 | r7) != 0)
        {
            result = default;
            return false;
        }

        result = new uint256(r0, r1, r2, r3);
        return true;
    }

    /// <summary>
    /// Computes <c>value * factor + addend</c> in place. Returns false if the result needs more than 256 bits.
    /// Used by decimal parsing to fold in one 19-digit chunk at a time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryMultiplyAdd(ref ulong u0, ref ulong u1, ref ulong u2, ref ulong u3, ulong factor, ulong addend)
    {
        ulong c = MultiplyAdd(u0, factor, addend, 0, out u0);
        c = MultiplyAdd(u1, factor, c, 0, out u1);
        c = MultiplyAdd(u2, factor, c, 0, out u2);
        c = MultiplyAdd(u3, factor, c, 0, out u3);
        return c == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HighestLimbIndex(ulong l0, ulong l1, ulong l2, ulong l3)
    {
        if (l3 != 0) return 3;
        if (l2 != 0) return 2;
        if (l1 != 0) return 1;
        return l0 != 0 ? 0 : -1;
    }

    // ------------------------------------------------------------------ division

    /// <summary>
    /// Divides the 128-bit value (<paramref name="high"/>:<paramref name="low"/>) by <paramref name="divisor"/>.
    /// Requires <paramref name="high"/> &lt; <paramref name="divisor"/> so the quotient fits in 64 bits.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong DivRem128By64(ulong high, ulong low, ulong divisor, out ulong remainder)
    {
        if (high == 0)
        {
            ulong quotient64 = DivRem64(low, divisor, out remainder);
            return quotient64;
        }

        // UInt128 division uses the hardware 128/64 divide where available.
        (UInt128 q, UInt128 r) = UInt128.DivRem(new UInt128(high, low), divisor);
        remainder = (ulong)r;
        return (ulong)q;
    }

    /// <summary>64-bit quotient and remainder; the JIT emits one hardware divide for the pair.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong DivRem64(ulong dividend, ulong divisor, out ulong remainder)
    {
        ulong quotient = dividend / divisor;
        remainder = dividend - quotient * divisor;
        return quotient;
    }

    /// <summary>floor((2^128 - 1) / 10^19) - 2^64, the reciprocal used by <see cref="DivRem128ByTenPow19"/>.</summary>
    internal const ulong TenPow19Reciprocal = 0xD83C94FB6D2AC34AUL;

    /// <summary>
    /// Divides (<paramref name="high"/>:<paramref name="low"/>) by 10^19 with two multiplies instead of a hardware divide
    /// (Moller and Granlund, "Improved division by invariant integers", algorithm 4). 10^19 has its top bit set, so it is
    /// already normalised. Requires <paramref name="high"/> &lt; 10^19.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong DivRem128ByTenPow19(ulong high, ulong low, out ulong remainder)
    {
        ulong q1 = Math.BigMul(TenPow19Reciprocal, high, out ulong q0);

        // (q1, q0) += (high, low)
        ulong sumLow = q0 + low;
        q1 += high + (sumLow < q0 ? 1UL : 0UL);
        q0 = sumLow;

        q1++;
        ulong r = low - q1 * TenPow19;

        if (r > q0)
        {
            q1--;
            r += TenPow19;
        }

        if (r >= TenPow19)
        {
            q1++;
            r -= TenPow19;
        }

        remainder = r;
        return q1;
    }

    /// <summary>Divides by a 64-bit divisor (must be non-zero), skipping leading zero limbs. Returns the remainder.</summary>
    public static ulong DivRem(in uint256 dividend, ulong divisor, out uint256 quotient)
    {
        ulong u0 = dividend.U0, u1 = dividend.U1, u2 = dividend.U2, u3 = dividend.U3;

        if ((u1 | u2 | u3) == 0)
        {
            ulong q0 = DivRem64(u0, divisor, out ulong r);
            quotient = new uint256(q0, 0, 0, 0);
            return r;
        }

        ulong rem = 0;
        ulong q3 = 0, q2 = 0, q1;
        if (u3 != 0)
            q3 = DivRem64(u3, divisor, out rem);

        if (u3 != 0 || u2 != 0)
            q2 = DivRem128By64(rem, u2, divisor, out rem);

        q1 = DivRem128By64(rem, u1, divisor, out rem);
        ulong q0v = DivRem128By64(rem, u0, divisor, out rem);

        quotient = new uint256(q0v, q1, q2, q3);
        return rem;
    }

    /// <summary>Divides by a non-zero 256-bit divisor.</summary>
    public static void DivRem(in uint256 dividend, in uint256 divisor, out uint256 quotient, out uint256 remainder)
    {
        if ((divisor.U1 | divisor.U2 | divisor.U3) == 0)
        {
            ulong rem = DivRem(dividend, divisor.U0, out quotient);
            remainder = new uint256(rem, 0, 0, 0);
            return;
        }

        if (dividend.CompareTo(divisor) < 0)
        {
            quotient = default;
            remainder = dividend;
            return;
        }

        int n = HighestLimbIndex(divisor.U0, divisor.U1, divisor.U2, divisor.U3) + 1; // 2..4 here
        int m = HighestLimbIndex(dividend.U0, dividend.U1, dividend.U2, dividend.U3) + 1; // m >= n since dividend >= divisor
        DivRemKnuth(in dividend, in divisor, m, n, out quotient, out remainder);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<ulong> AsLimbs(in uint256 value)
        => MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<uint256, ulong>(ref Unsafe.AsRef(in value)), 4);

    /// <summary>
    /// Knuth algorithm D (TAOCP 4.3.1) on 64-bit limbs for an n-limb divisor (n &gt;= 2) and m-limb dividend (m &gt;= n).
    /// </summary>
    private static void DivRemKnuth(in uint256 dividend, in uint256 divisor, int m, int n, out uint256 quotient, out uint256 remainder)
    {
        ReadOnlySpan<ulong> us = AsLimbs(in dividend);
        ReadOnlySpan<ulong> vs = AsLimbs(in divisor);

        Span<ulong> un = stackalloc ulong[5]; // normalised dividend, one limb longer
        Span<ulong> vn = stackalloc ulong[4]; // normalised divisor
        Span<ulong> q = stackalloc ulong[4];

        // D1: normalise so the divisor's top bit is set.
        int shift = BitOperations.LeadingZeroCount(vs[n - 1]);
        if (shift == 0)
        {
            for (int i = 0; i < n; i++) vn[i] = vs[i];
            for (int i = 0; i < m; i++) un[i] = us[i];
            un[m] = 0;
        }
        else
        {
            int back = 64 - shift;
            for (int i = n - 1; i > 0; i--) vn[i] = (vs[i] << shift) | (vs[i - 1] >> back);
            vn[0] = vs[0] << shift;

            un[m] = us[m - 1] >> back;
            for (int i = m - 1; i > 0; i--) un[i] = (us[i] << shift) | (us[i - 1] >> back);
            un[0] = us[0] << shift;
        }

        ulong vTop = vn[n - 1];
        ulong vNext = vn[n - 2];

        // D2..D7: one quotient limb per iteration.
        for (int j = m - n; j >= 0; j--)
        {
            // D3: estimate the quotient limb from the top two dividend limbs.
            ulong uHigh = un[j + n];
            ulong uLow = un[j + n - 1];
            ulong qhat, rhat;
            bool rhatOverflowed = false;

            if (uHigh >= vTop)
            {
                // The true quotient limb is at least 2^64 - 1; clamp. rhat = uLow + vTop may exceed 64 bits.
                qhat = ulong.MaxValue;
                rhat = uLow + vTop;
                rhatOverflowed = rhat < uLow;
            }
            else
            {
                qhat = DivRem128By64(uHigh, uLow, vTop, out rhat);
            }

            // Refine with the second divisor limb (at most two corrections).
            while (!rhatOverflowed)
            {
                ulong productHigh = Math.BigMul(qhat, vNext, out ulong productLow);
                if (productHigh < rhat || (productHigh == rhat && productLow <= un[j + n - 2]))
                    break;

                qhat--;
                ulong nextRhat = rhat + vTop;
                rhatOverflowed = nextRhat < rhat;
                rhat = nextRhat;
            }

            // D4: multiply and subtract qhat * vn from un[j .. j+n].
            ulong borrow = 0;
            ulong carry = 0;
            for (int i = 0; i < n; i++)
            {
                ulong high = Math.BigMul(qhat, vn[i], out ulong low);
                low += carry;
                high += low < carry ? 1UL : 0UL;
                carry = high;

                ulong current = un[i + j];
                ulong difference = current - low;
                ulong borrowOut = current < low ? 1UL : 0UL;
                ulong difference2 = difference - borrow;
                borrowOut |= difference < borrow ? 1UL : 0UL;
                un[i + j] = difference2;
                borrow = borrowOut;
            }

            bool negative;
            {
                ulong current = un[j + n];
                ulong difference = current - carry;
                ulong borrowOut = current < carry ? 1UL : 0UL;
                ulong difference2 = difference - borrow;
                borrowOut |= difference < borrow ? 1UL : 0UL;
                un[j + n] = difference2;
                negative = borrowOut != 0;
            }

            // D5/D6: qhat was one too large (rare): add the divisor back.
            if (negative)
            {
                qhat--;
                ulong addCarry = 0;
                for (int i = 0; i < n; i++)
                {
                    ulong sum = un[i + j] + vn[i];
                    ulong carryOut = sum < vn[i] ? 1UL : 0UL;
                    ulong sum2 = sum + addCarry;
                    carryOut |= sum2 < sum ? 1UL : 0UL;
                    un[i + j] = sum2;
                    addCarry = carryOut;
                }

                un[j + n] += addCarry;
            }

            q[j] = qhat;
        }

        // D8: denormalise the remainder.
        Span<ulong> r = stackalloc ulong[4];
        if (shift == 0)
        {
            for (int i = 0; i < n; i++) r[i] = un[i];
        }
        else
        {
            int back = 64 - shift;
            for (int i = 0; i < n - 1; i++) r[i] = (un[i] >> shift) | (un[i + 1] << back);
            r[n - 1] = un[n - 1] >> shift;
        }

        quotient = new uint256(q[0], q[1], q[2], q[3]);
        remainder = new uint256(r[0], r[1], r[2], r[3]);
    }
}
