using EccentricWare.Web3.DataTypes.Utils;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using EccentricWare.Web3.DataTypes.JsonConverters;

namespace EccentricWare.Web3.DataTypes;

/// <summary>
/// Represents a 256-bit signed integer using two's complement semantics with a fixed 32-byte layout (4 x 64-bit limbs).
/// Optimised for Web3 workloads (EVM/Solana), including allocation-free parsing of hex/decimal and efficient comparisons.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[JsonConverter(typeof(Int256JsonConverter))]
public readonly struct int256 :
    IEquatable<int256>,
    IComparable<int256>,
    IComparable,
    IFormattable,
    ISpanFormattable,
    IUtf8SpanFormattable,
    ISpanParsable<int256>
{
    /// <summary>Least significant 64 bits (bits 0..63).</summary>
    private readonly ulong _u0;
    /// <summary>Bits 64..127.</summary>
    private readonly ulong _u1;
    /// <summary>Bits 128..191.</summary>
    private readonly ulong _u2;
    /// <summary>Most significant 64 bits (bits 192..255). The sign bit is bit 255 (MSB of this limb).</summary>
    private readonly ulong _u3;

    /// <summary>Represents the value 0.</summary>
    public static readonly int256 Zero = new int256(0);

    /// <summary>Represents the value 1.</summary>
    public static readonly int256 One = new int256(1L);

    /// <summary>Represents the value -1.</summary>
    public static readonly int256 MinusOne = new(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue);

    /// <summary>Represents the minimum possible value (-2^255).</summary>
    public static readonly int256 MinValue = new(0, 0, 0, 0x8000_0000_0000_0000UL);

    /// <summary>Represents the maximum possible value (2^255 - 1).</summary>
    public static readonly int256 MaxValue = new(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, 0x7FFF_FFFF_FFFF_FFFFUL);

    #region Constructors

    /// <summary>
    /// Creates a signed 256-bit value from a 64-bit signed integer.
    /// </summary>
    /// <param name="value">The signed 64-bit value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int256(long value)
    {
        _u0 = unchecked((ulong)value);
        _u1 = value < 0 ? ulong.MaxValue : 0UL;
        _u2 = value < 0 ? ulong.MaxValue : 0UL;
        _u3 = value < 0 ? ulong.MaxValue : 0UL;
    }

    /// <summary>
    /// Creates a signed 256-bit value from four 64-bit limbs (little-endian limb order).
    /// The limbs are interpreted as a two's complement 256-bit value.
    /// </summary>
    /// <param name="u0">Least significant 64 bits.</param>
    /// <param name="u1">Bits 64..127.</param>
    /// <param name="u2">Bits 128..191.</param>
    /// <param name="u3">Most significant 64 bits (contains the sign bit).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int256(ulong u0, ulong u1, ulong u2, ulong u3)
    {
        _u0 = u0;
        _u1 = u1;
        _u2 = u2;
        _u3 = u3;
    }

    /// <summary>
    /// Creates a signed 256-bit value from a big-endian 0..32 byte sequence interpreted as a two's complement integer.
    /// If fewer than 32 bytes are provided, the value is sign-extended from the most significant provided byte.
    /// </summary>
    /// <param name="bigEndianTwosComplementBytes">Two's complement bytes in big-endian order.</param>
    public int256(ReadOnlySpan<byte> bigEndianTwosComplementBytes)
    {
        if (bigEndianTwosComplementBytes.Length == 32)
        {
            _u3 = BinaryPrimitives.ReadUInt64BigEndian(bigEndianTwosComplementBytes);
            _u2 = BinaryPrimitives.ReadUInt64BigEndian(bigEndianTwosComplementBytes.Slice(8));
            _u1 = BinaryPrimitives.ReadUInt64BigEndian(bigEndianTwosComplementBytes.Slice(16));
            _u0 = BinaryPrimitives.ReadUInt64BigEndian(bigEndianTwosComplementBytes.Slice(24));
            return;
        }

        this = FromBigEndianSignExtended(bigEndianTwosComplementBytes);
    }

    // The padded path lives in a separate method so the common exact-32-byte path stays small. It reads limbs directly:
    // a stackalloc buffer here costs about 3x more (measured), because it is no longer inlined into the caller.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int256 FromBigEndianSignExtended(ReadOnlySpan<byte> bigEndianTwosComplementBytes)
    {
        if (bigEndianTwosComplementBytes.Length > 32)
            throw new ArgumentException("Expected at most 32 bytes.", nameof(bigEndianTwosComplementBytes));

        // Sign extension based on the MSB of the first provided byte; empty input is zero.
        ulong fill = bigEndianTwosComplementBytes.Length != 0 && (bigEndianTwosComplementBytes[0] & 0x80) != 0 ? ulong.MaxValue : 0UL;

        int end = bigEndianTwosComplementBytes.Length;
        ulong u0 = LimbReader.TakeBigEndianTail(bigEndianTwosComplementBytes, ref end, fill);
        ulong u1 = LimbReader.TakeBigEndianTail(bigEndianTwosComplementBytes, ref end, fill);
        ulong u2 = LimbReader.TakeBigEndianTail(bigEndianTwosComplementBytes, ref end, fill);
        ulong u3 = LimbReader.TakeBigEndianTail(bigEndianTwosComplementBytes, ref end, fill);
        return new int256(u0, u1, u2, u3);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets a value indicating whether this value is zero.
    /// </summary>
    public bool IsZero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_u0 | _u1 | _u2 | _u3) == 0;
    }

    /// <summary>
    /// Gets a value indicating whether this value is negative.
    /// </summary>
    public bool IsNegative
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_u3 & 0x8000_0000_0000_0000UL) != 0;
    }

    /// <summary>
    /// Gets the sign of this value: -1 for negative, 0 for zero, 1 for positive.
    /// </summary>
    public int Sign
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsZero ? 0 : (IsNegative ? -1 : 1);
    }

    /// <summary>Limb accessors for the internal arithmetic core (limb 0 is least significant).</summary>
    internal ulong U0 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _u0; }
    internal ulong U1 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _u1; }
    internal ulong U2 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _u2; }
    internal ulong U3 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _u3; }

    #endregion

    #region Byte Conversions

    /// <summary>
    /// Writes the raw two's complement value as a 32-byte big-endian representation into the destination span.
    /// </summary>
    /// <param name="destination">The destination span that must be at least 32 bytes long.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBigEndian(Span<byte> destination)
    {
        if (destination.Length < 32)
            throw new ArgumentException("Destination must be at least 32 bytes.", nameof(destination));

        BinaryPrimitives.WriteUInt64BigEndian(destination, _u3);
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(8), _u2);
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(16), _u1);
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(24), _u0);
    }

    /// <summary>
    /// Writes the raw two's complement value as a 32-byte little-endian representation into the destination span.
    /// </summary>
    /// <param name="destination">The destination span that must be at least 32 bytes long.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLittleEndian(Span<byte> destination)
    {
        if (destination.Length < 32)
            throw new ArgumentException("Destination must be at least 32 bytes.", nameof(destination));

        BinaryPrimitives.WriteUInt64LittleEndian(destination, _u0);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(8), _u1);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(16), _u2);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(24), _u3);
    }

    /// <summary>
    /// Returns the raw two's complement value as a new 32-byte big-endian array.
    /// </summary>
    public byte[] ToBigEndianBytes()
    {
        byte[] bytes = new byte[32];
        WriteBigEndian(bytes);
        return bytes;
    }

    /// <summary>
    /// Returns the raw two's complement value as a new 32-byte little-endian array.
    /// </summary>
    public byte[] ToLittleEndianBytes()
    {
        byte[] bytes = new byte[32];
        WriteLittleEndian(bytes);
        return bytes;
    }

    #endregion

    #region Equality and Hashing

    /// <summary>
    /// Determines whether this value equals another value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(int256 other)
    {
        ulong diff = (_u0 ^ other._u0) | (_u1 ^ other._u1) | (_u2 ^ other._u2) | (_u3 ^ other._u3);
        return diff == 0;
    }

    /// <summary>
    /// Determines whether this value equals another object.
    /// </summary>
    public override bool Equals(object? obj) => obj is int256 other && Equals(other);

    /// <summary>
    /// Computes a keyed hash code (random per process) suitable for large hash tables (millions of keys)
    /// and for keys controlled by untrusted input.
    /// </summary>
    public override int GetHashCode() => Hash256.Compute(_u0, _u1, _u2, _u3);

    /// <summary>
    /// Determines whether two values are equal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(int256 left, int256 right) => left.Equals(right);

    /// <summary>
    /// Determines whether two values are not equal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(int256 left, int256 right) => !left.Equals(right);

    #endregion

    #region Comparison

    /// <summary>
    /// Compares this value to another signed 256-bit value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(int256 other)
    {
        bool negA = IsNegative;
        bool negB = other.IsNegative;

        if (negA != negB)
            return negA ? -1 : 1;

        // For two's complement, unsigned limb ordering is monotonic within the negative range and within the non-negative range.
        if (_u3 != other._u3) return _u3 < other._u3 ? -1 : 1;
        if (_u2 != other._u2) return _u2 < other._u2 ? -1 : 1;
        if (_u1 != other._u1) return _u1 < other._u1 ? -1 : 1;
        if (_u0 != other._u0) return _u0 < other._u0 ? -1 : 1;
        return 0;
    }

    /// <summary>
    /// Compares this value to another object.
    /// </summary>
    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        if (obj is int256 other) return CompareTo(other);
        throw new ArgumentException($"Object must be of type {nameof(int256)}.", nameof(obj));
    }

    /// <summary>Returns true if left is less than right.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(int256 left, int256 right) => left.CompareTo(right) < 0;

    /// <summary>Returns true if left is greater than right.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(int256 left, int256 right) => left.CompareTo(right) > 0;

    /// <summary>Returns true if left is less than or equal to right.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <=(int256 left, int256 right) => left.CompareTo(right) <= 0;

    /// <summary>Returns true if left is greater than or equal to right.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >=(int256 left, int256 right) => left.CompareTo(right) >= 0;

    #endregion

    #region Arithmetic

    /// <summary>
    /// Adds two values and throws <see cref="OverflowException"/> if signed overflow occurs.
    /// </summary>
    public static int256 operator +(int256 left, int256 right)
    {
        int256 result = AddUnchecked(left, right);

        bool signLeft = left.IsNegative;
        bool signRight = right.IsNegative;
        bool signResult = result.IsNegative;

        if (signLeft == signRight && signResult != signLeft)
            throw new OverflowException("int256 addition overflow.");

        return result;
    }

    /// <summary>
    /// Adds two values and wraps on overflow (mod 2^256).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int256 AddUnchecked(int256 left, int256 right)
    {
        ulong r0 = left._u0 + right._u0;
        ulong c0 = r0 < left._u0 ? 1UL : 0UL;

        ulong r1 = left._u1 + right._u1 + c0;
        ulong c1 = (r1 < left._u1 || (c0 == 1 && r1 == left._u1)) ? 1UL : 0UL;

        ulong r2 = left._u2 + right._u2 + c1;
        ulong c2 = (r2 < left._u2 || (c1 == 1 && r2 == left._u2)) ? 1UL : 0UL;

        ulong r3 = left._u3 + right._u3 + c2;

        return new int256(r0, r1, r2, r3);
    }

    /// <summary>
    /// Subtracts two values and throws <see cref="OverflowException"/> if signed overflow occurs.
    /// </summary>
    public static int256 operator -(int256 left, int256 right)
    {
        int256 result = SubtractUnchecked(left, right);

        bool signLeft = left.IsNegative;
        bool signRight = right.IsNegative;
        bool signResult = result.IsNegative;

        // Overflow if signs differ and result sign differs from left.
        if (signLeft != signRight && signResult != signLeft)
            throw new OverflowException("int256 subtraction overflow.");

        return result;
    }

    /// <summary>
    /// Subtracts two values and wraps on overflow (mod 2^256).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int256 SubtractUnchecked(int256 left, int256 right)
    {
        ulong b0 = left._u0 < right._u0 ? 1UL : 0UL;
        ulong r0 = left._u0 - right._u0;

        ulong b1 = (left._u1 < right._u1 || (left._u1 == right._u1 && b0 == 1)) ? 1UL : 0UL;
        ulong r1 = left._u1 - right._u1 - b0;

        ulong b2 = (left._u2 < right._u2 || (left._u2 == right._u2 && b1 == 1)) ? 1UL : 0UL;
        ulong r2 = left._u2 - right._u2 - b1;

        ulong r3 = left._u3 - right._u3 - b2;

        return new int256(r0, r1, r2, r3);
    }

    /// <summary>
    /// Negates the value and throws <see cref="OverflowException"/> if the value is <see cref="MinValue"/>.
    /// </summary>
    public static int256 operator -(int256 value)
    {
        if (value.Equals(MinValue))
            throw new OverflowException("int256 negation overflow.");

        return NegateUnchecked(value);
    }

    /// <summary>
    /// Negates the value using two's complement and wraps on overflow (mod 2^256).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int256 NegateUnchecked(int256 value)
    {
        // Two's complement: ~x + 1
        ulong u0 = ~value._u0;
        ulong u1 = ~value._u1;
        ulong u2 = ~value._u2;
        ulong u3 = ~value._u3;

        ulong r0 = u0 + 1UL;
        ulong c0 = r0 == 0 ? 1UL : 0UL;

        ulong r1 = u1 + c0;
        ulong c1 = (c0 == 1 && r1 == 0) ? 1UL : 0UL;

        ulong r2 = u2 + c1;
        ulong c2 = (c1 == 1 && r2 == 0) ? 1UL : 0UL;

        ulong r3 = u3 + c2;

        return new int256(r0, r1, r2, r3);
    }

    /// <summary>
    /// Multiplies two values and throws <see cref="OverflowException"/> if the result exceeds the int256 range.
    /// Works on magnitudes with the allocation-free 256-bit core.
    /// </summary>
    public static int256 operator *(int256 left, int256 right)
    {
        uint256 a = left.Magnitude();
        uint256 b = right.Magnitude();

        if (!UInt256Math.TryMultiply(in a, in b, out uint256 product))
            ThrowOverflow("int256 multiplication overflow.");

        return FromMagnitude(in product, left.IsNegative != right.IsNegative, "int256 multiplication overflow.");
    }

    /// <summary>
    /// Divides two values, truncating toward zero (EVM SDIV). Throws <see cref="DivideByZeroException"/> if the divisor is
    /// zero and <see cref="OverflowException"/> for <see cref="MinValue"/> / -1.
    /// </summary>
    public static int256 operator /(int256 left, int256 right)
    {
        if (right.IsZero)
            throw new DivideByZeroException();

        uint256 a = left.Magnitude();
        uint256 b = right.Magnitude();
        UInt256Math.DivRem(in a, in b, out uint256 quotient, out _);

        return FromMagnitude(in quotient, left.IsNegative != right.IsNegative, "int256 division overflow.");
    }

    /// <summary>
    /// Computes left modulo right; the result takes the sign of the dividend (EVM SMOD).
    /// Throws <see cref="DivideByZeroException"/> if the divisor is zero.
    /// </summary>
    public static int256 operator %(int256 left, int256 right)
    {
        if (right.IsZero)
            throw new DivideByZeroException();

        uint256 a = left.Magnitude();
        uint256 b = right.Magnitude();
        UInt256Math.DivRem(in a, in b, out _, out uint256 remainder);

        // |remainder| < |divisor| <= 2^255, so it always fits.
        return FromMagnitude(in remainder, left.IsNegative, "int256 remainder overflow.");
    }

    #endregion

    #region Bitwise and Shifts

    /// <summary>Bitwise AND.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int256 operator &(int256 left, int256 right)
        => new int256(left._u0 & right._u0, left._u1 & right._u1, left._u2 & right._u2, left._u3 & right._u3);

    /// <summary>Bitwise OR.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int256 operator |(int256 left, int256 right)
        => new int256(left._u0 | right._u0, left._u1 | right._u1, left._u2 | right._u2, left._u3 | right._u3);

    /// <summary>Bitwise XOR.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int256 operator ^(int256 left, int256 right)
        => new int256(left._u0 ^ right._u0, left._u1 ^ right._u1, left._u2 ^ right._u2, left._u3 ^ right._u3);

    /// <summary>Bitwise NOT.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int256 operator ~(int256 value)
        => new int256(~value._u0, ~value._u1, ~value._u2, ~value._u3);

    /// <summary>
    /// Shifts the value left by a specified number of bits (logical shift; the shifted-out bits are discarded).
    /// Counts of 256 or more yield zero (as in <see cref="uint256"/> and the EVM SHL); a negative count throws
    /// <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    public static int256 operator <<(int256 value, int shift)
    {
        if (shift == 0) return value;
        if (shift < 0) throw new ArgumentOutOfRangeException(nameof(shift), "Shift count must be non-negative.");
        if (shift >= 256) return Zero;

        int wholeLimbs = shift >> 6;
        int bitShift = shift & 63;

        ulong a0 = value._u0, a1 = value._u1, a2 = value._u2, a3 = value._u3;

        if (bitShift == 0)
        {
            return wholeLimbs switch
            {
                0 => value,
                1 => new int256(0, a0, a1, a2),
                2 => new int256(0, 0, a0, a1),
                3 => new int256(0, 0, 0, a0),
                _ => Zero
            };
        }

        int r = 64 - bitShift;

        return wholeLimbs switch
        {
            0 => new int256(
                a0 << bitShift,
                (a1 << bitShift) | (a0 >> r),
                (a2 << bitShift) | (a1 >> r),
                (a3 << bitShift) | (a2 >> r)),
            1 => new int256(
                0,
                a0 << bitShift,
                (a1 << bitShift) | (a0 >> r),
                (a2 << bitShift) | (a1 >> r)),
            2 => new int256(
                0,
                0,
                a0 << bitShift,
                (a1 << bitShift) | (a0 >> r)),
            3 => new int256(
                0,
                0,
                0,
                a0 << bitShift),
            _ => Zero
        };
    }

    /// <summary>
    /// Shifts the value right by a specified number of bits (arithmetic shift with sign extension).
    /// Counts of 256 or more shift out every bit: the result is zero for non-negative values and -1 for negative ones
    /// (the same as shifting by 255, and the EVM SAR). A negative count throws <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    public static int256 operator >>(int256 value, int shift)
    {
        if (shift == 0) return value;
        if (shift < 0) throw new ArgumentOutOfRangeException(nameof(shift), "Shift count must be non-negative.");

        if (shift >= 256)
            return value.IsNegative ? MinusOne : Zero;

        int wholeLimbs = shift >> 6;
        int bitShift = shift & 63;

        ulong a0 = value._u0, a1 = value._u1, a2 = value._u2, a3 = value._u3;
        ulong fill = value.IsNegative ? ulong.MaxValue : 0UL;

        if (bitShift == 0)
        {
            return wholeLimbs switch
            {
                0 => value,
                1 => new int256(a1, a2, a3, fill),
                2 => new int256(a2, a3, fill, fill),
                3 => new int256(a3, fill, fill, fill),
                _ => value.IsNegative ? MinusOne : Zero
            };
        }

        int r = 64 - bitShift;

        return wholeLimbs switch
        {
            0 => new int256(
                (a0 >> bitShift) | (a1 << r),
                (a1 >> bitShift) | (a2 << r),
                (a2 >> bitShift) | (a3 << r),
                (a3 >> bitShift) | (fill << r)),
            1 => new int256(
                (a1 >> bitShift) | (a2 << r),
                (a2 >> bitShift) | (a3 << r),
                (a3 >> bitShift) | (fill << r),
                fill),
            2 => new int256(
                (a2 >> bitShift) | (a3 << r),
                (a3 >> bitShift) | (fill << r),
                fill,
                fill),
            3 => new int256(
                (a3 >> bitShift) | (fill << r),
                fill,
                fill,
                fill),
            _ => value.IsNegative ? MinusOne : Zero
        };
    }

    #endregion

    #region Conversions

    /// <summary>Implicit conversion from <see cref="long"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator int256(long value) => new(value);

    /// <summary>Explicit conversion to <see cref="long"/> (throws if out of range).</summary>
    public static explicit operator long(int256 value)
    {
        // Fits in signed 64-bit only if limbs 1..3 are the sign extension of bit 63 of limb 0.
        long low = unchecked((long)value._u0);
        ulong signExtension = (ulong)(low >> 63); // all ones if low is negative, otherwise zero

        if (value._u1 != signExtension || value._u2 != signExtension || value._u3 != signExtension)
            throw new OverflowException("Value is out of range for long.");

        return low;
    }

    /// <summary>
    /// Converts this value to a signed <see cref="BigInteger"/> (cold-path helper).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public BigInteger ToBigInteger()
    {
        Span<byte> bytes = stackalloc byte[32];
        WriteBigEndian(bytes);
        return new BigInteger(bytes, isUnsigned: false, isBigEndian: true);
    }

    /// <summary>Implicit conversion to <see cref="BigInteger"/> (cold-path).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator BigInteger(int256 value) => value.ToBigInteger();

    /// <summary>
    /// Converts a signed <see cref="BigInteger"/> to <see cref="int256"/> (throws if out of int256 range).
    /// </summary>
    public static explicit operator int256(BigInteger value)
    {
        if (value.IsZero)
            return Zero;

        if (value.Sign > 0)
        {
            if (!TryConvertPositiveBigInteger(value, out int256 pos))
                throw new OverflowException("BigInteger is out of range for int256.");
            return pos;
        }

        // Negative
        if (!TryConvertNegativeBigInteger(value, out int256 neg))
            throw new OverflowException("BigInteger is out of range for int256.");
        return neg;
    }

    #endregion

    #region Parsing

    /// <summary>
    /// Parses a string into an <see cref="int256"/> using the provided format provider.
    /// Supports:
    /// - Hex with a required "0x" prefix (two's complement if 64 hex digits; otherwise treated as positive magnitude); bare hex
    ///   digits are read as decimal, so a bare "10" is ten, not sixteen
    /// - Decimal with optional leading '-' sign
    /// </summary>
    public static int256 Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), provider);
    }

    /// <summary>
    /// Parses a span into an <see cref="int256"/> using the provided format provider.
    /// </summary>
    public static int256 Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        if (TryParse(s, provider, out int256 result))
            return result;

        throw new FormatException("Invalid int256 value.");
    }

    /// <summary>
    /// Parses a string into an <see cref="int256"/> using invariant culture.
    /// </summary>
    public static int256 Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses a span into an <see cref="int256"/> using invariant culture.
    /// </summary>
    public static int256 Parse(ReadOnlySpan<char> s) => Parse(s, CultureInfo.InvariantCulture);

    /// <summary>
    /// Tries to parse a string into an <see cref="int256"/>.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out int256 result)
        => TryParse(s.AsSpanSafe(), provider, out result);

    /// <summary>
    /// Tries to parse a span into an <see cref="int256"/> using the provided format provider.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out int256 result)
    {
        s = ByteUtils.TrimWhitespace(s);
        if (s.Length == 0)
        {
            result = Zero;
            return false;
        }

        if (!ByteUtils.TryTrimLeadingSign(s, out bool isNegativePrefix, out ReadOnlySpan<char> unsignedSpan))
        {
            result = Zero;
            return false;
        }

        if (ByteUtils.TryTrimHexPrefix(unsignedSpan, out ReadOnlySpan<char> hexDigits))
        {
            // Accept "0x" as zero.
            if (hexDigits.Length == 0)
            {
                result = Zero;
                return true;
            }

            if (hexDigits.Length > 64)
            {
                result = Zero;
                return false;
            }

            if (!ByteUtils.TryParseUInt256HexChars(hexDigits, out ulong u0, out ulong u1, out ulong u2, out ulong u3))
            {
                result = Zero;
                return false;
            }

            if (isNegativePrefix)
            {
                // Interpret as magnitude, then negate into two's complement.
                if (!IsMagnitudeWithinInt256NegativeRange(u0, u1, u2, u3))
                {
                    result = Zero;
                    return false;
                }

                if ((u0 | u1 | u2 | u3) == 0)
                {
                    result = Zero;
                    return true;
                }

                ByteUtils.NegateTwosComplement256(u0, u1, u2, u3, out ulong r0, out ulong r1, out ulong r2, out ulong r3);
                result = new int256(r0, r1, r2, r3);
                return true;
            }

            // No '-' prefix:
            // - If exactly 64 hex digits, interpret as raw two's complement (ABI-like), sign bit may be set.
            // - If shorter, interpret as positive magnitude and require sign bit not set.
            if (hexDigits.Length < 64 && (u3 & 0x8000_0000_0000_0000UL) != 0)
            {
                result = Zero;
                return false; // positive magnitude overflow
            }

            result = new int256(u0, u1, u2, u3);
            return true;
        }

        // Decimal parsing: magnitude parsed as unsigned; apply sign.
        if (!ByteUtils.TryParseUInt256DecimalChars(unsignedSpan, out ulong d0, out ulong d1, out ulong d2, out ulong d3))
        {
            result = Zero;
            return false;
        }

        if (!isNegativePrefix)
        {
            // Positive range: sign bit must be 0.
            if ((d3 & 0x8000_0000_0000_0000UL) != 0)
            {
                result = Zero;
                return false;
            }

            result = new int256(d0, d1, d2, d3);
            return true;
        }

        // Negative range: magnitude must be <= 2^255.
        if (!IsMagnitudeWithinInt256NegativeRange(d0, d1, d2, d3))
        {
            result = Zero;
            return false;
        }

        if ((d0 | d1 | d2 | d3) == 0)
        {
            result = Zero; // allow "-0"
            return true;
        }

        ByteUtils.NegateTwosComplement256(d0, d1, d2, d3, out ulong n0, out ulong n1, out ulong n2, out ulong n3);
        result = new int256(n0, n1, n2, n3);
        return true;
    }

    /// <summary>
    /// Tries to parse a UTF-8 encoded JSON token into an <see cref="int256"/>.
    /// Supports optional surrounding quotes and:
    /// - Hex with optional "0x" prefix
    /// - Decimal with optional leading '-' sign
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out int256 value)
    {
        utf8 = ByteUtils.TrimAsciiWhitespaceUtf8(utf8);

        // Optional surrounding quotes.
        if (utf8.Length >= 2 && utf8[0] == (byte)'"' && utf8[^1] == (byte)'"')
            utf8 = utf8.Slice(1, utf8.Length - 2);

        return TryParseUnquoted(utf8, out value);
    }

    /// <summary>
    /// Parses UTF-8 hex or signed decimal text without accepting surrounding quotes. Used by the JSON converter,
    /// whose string tokens are already unquoted, so a quote character inside the value must stay invalid.
    /// </summary>
    internal static bool TryParseUnquoted(ReadOnlySpan<byte> utf8, out int256 value)
    {
        value = Zero;

        utf8 = ByteUtils.TrimAsciiWhitespaceUtf8(utf8);

        if (utf8.Length == 0)
            return false;

        if (!ByteUtils.TryTrimLeadingSignUtf8(utf8, out bool isNegativePrefix, out ReadOnlySpan<byte> unsignedSpan))
            return false;

        if (ByteUtils.TryTrimHexPrefixUtf8(unsignedSpan, out ReadOnlySpan<byte> hexDigits))
        {
            if (hexDigits.Length == 0)
            {
                value = Zero;
                return true;
            }

            if (hexDigits.Length > 64)
                return false;

            if (!ByteUtils.TryParseUInt256HexUtf8(hexDigits, out ulong u0, out ulong u1, out ulong u2, out ulong u3))
                return false;

            if (isNegativePrefix)
            {
                if (!IsMagnitudeWithinInt256NegativeRange(u0, u1, u2, u3))
                    return false;

                if ((u0 | u1 | u2 | u3) == 0)
                {
                    value = Zero;
                    return true;
                }

                ByteUtils.NegateTwosComplement256(u0, u1, u2, u3, out ulong r0, out ulong r1, out ulong r2, out ulong r3);
                value = new int256(r0, r1, r2, r3);
                return true;
            }

            if (hexDigits.Length < 64 && (u3 & 0x8000_0000_0000_0000UL) != 0)
                return false;

            value = new int256(u0, u1, u2, u3);
            return true;
        }

        if (!ByteUtils.TryParseUInt256DecimalUtf8(unsignedSpan, out ulong d0, out ulong d1, out ulong d2, out ulong d3))
            return false;

        if (!isNegativePrefix)
        {
            if ((d3 & 0x8000_0000_0000_0000UL) != 0)
                return false;

            value = new int256(d0, d1, d2, d3);
            return true;
        }

        if (!IsMagnitudeWithinInt256NegativeRange(d0, d1, d2, d3))
            return false;

        if ((d0 | d1 | d2 | d3) == 0)
        {
            value = Zero;
            return true;
        }

        ByteUtils.NegateTwosComplement256(d0, d1, d2, d3, out ulong n0, out ulong n1, out ulong n2, out ulong n3);
        value = new int256(n0, n1, n2, n3);
        return true;
    }

    #endregion

    #region Formatting

    #region Decimal Parsing

    /// <summary>
    /// Tries to parse a signed decimal string into an <see cref="int256"/> without throwing.
    /// Accepts an optional leading '+' or '-' sign and ignores leading/trailing whitespace.
    /// </summary>
    /// <param name="s">The input string containing a signed decimal value.</param>
    /// <param name="result">The parsed <see cref="int256"/> if successful; otherwise <see cref="Zero"/>.</param>
    /// <returns>True if parsing succeeded; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryParseDecimal([NotNullWhen(true)] string? s, out int256 result)
    {
        if (s is null)
        {
            result = Zero;
            return false;
        }

        return TryParseDecimal(s.AsSpan(), out result);
    }

    /// <summary>
    /// Tries to parse a signed decimal span into an <see cref="int256"/> without throwing.
    /// Accepts an optional leading '+' or '-' sign and ignores leading/trailing whitespace.
    /// </summary>
    /// <param name="s">The input span containing a signed decimal value.</param>
    /// <param name="result">The parsed <see cref="int256"/> if successful; otherwise <see cref="Zero"/>.</param>
    /// <returns>True if parsing succeeded; otherwise false.</returns>
    public static bool TryParseDecimal(ReadOnlySpan<char> s, out int256 result)
    {
        result = Zero;

        s = ByteUtils.TrimWhitespace(s);
        if (s.Length == 0)
            return false;

        if (!ByteUtils.TryTrimLeadingSign(s, out bool isNegativePrefix, out ReadOnlySpan<char> unsignedSpan))
            return false;

        // No whitespace is allowed between the sign and the digits ("- 5" is invalid, as it is for TryParse).
        if (unsignedSpan.Length == 0)
            return false;

        if (!ByteUtils.TryParseUInt256DecimalChars(unsignedSpan, out ulong u0, out ulong u1, out ulong u2, out ulong u3))
            return false;

        if (!isNegativePrefix)
        {
            // Positive int256 must be <= 2^255 - 1 (sign bit must be 0).
            if ((u3 & 0x8000_0000_0000_0000UL) != 0)
                return false;

            result = new int256(u0, u1, u2, u3);
            return true;
        }

        // Negative magnitude must be <= 2^255 (allow exactly 2^255 -> MinValue).
        if (!IsMagnitudeWithinInt256NegativeRange(u0, u1, u2, u3))
            return false;

        // Allow "-0" as 0.
        if ((u0 | u1 | u2 | u3) == 0)
        {
            result = Zero;
            return true;
        }

        ByteUtils.NegateTwosComplement256(u0, u1, u2, u3, out ulong r0, out ulong r1, out ulong r2, out ulong r3);
        result = new int256(r0, r1, r2, r3);
        return true;
    }

    #endregion

    /// <summary>
    /// Returns the default string representation (decimal, signed).
    /// </summary>
    public override string ToString() => ToString("D", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats the value as a string using:
    /// - "D" / "d" (default): signed decimal
    /// - "0x" / "0X": hex with a lowercase "0x" prefix (positive: minimal magnitude, negative: full 64-digit two's complement)
    /// - "x" / "X": hex without prefix (same digit rules)
    /// Null or empty selects the default. Throws <see cref="FormatException"/> for any other format.
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        AsciiBuffer80 storage = default;
        return UInt256Formatting.CreateString(FormatAscii(storage, format.AsSpan()));
    }

    /// <summary>
    /// Attempts to format the value into the destination character span.
    /// Supported formats: "D"/"d" (default), "0x"/"0X" (the prefix is always lowercase), "x", "X".
    /// Returns false only when <paramref name="destination"/> is too small; throws <see cref="FormatException"/> for any
    /// other format (callers such as string interpolation retry with a larger buffer whenever false is returned).
    /// Output does not depend on <paramref name="provider"/>: it must round-trip through <c>Parse</c>, and
    /// culture-specific negative signs can be non-ASCII.
    /// </summary>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        AsciiBuffer80 storage = default;
        ReadOnlySpan<byte> ascii = FormatAscii(storage, format);

        if (ascii.Length > destination.Length)
        {
            charsWritten = 0;
            return false;
        }

        Ascii.ToUtf16(ascii, destination, out charsWritten);
        return true;
    }

    /// <summary>
    /// Attempts to format the value into a UTF-8 destination span (the output is plain ASCII).
    /// Returns false only when the destination is too small; throws <see cref="FormatException"/> for unknown formats.
    /// </summary>
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        AsciiBuffer80 storage = default;
        ReadOnlySpan<byte> ascii = FormatAscii(storage, format);

        if (ascii.Length > utf8Destination.Length)
        {
            bytesWritten = 0;
            return false;
        }

        ascii.CopyTo(utf8Destination);
        bytesWritten = ascii.Length;
        return true;
    }

    private ReadOnlySpan<byte> FormatAscii(Span<byte> scratch, ReadOnlySpan<char> format)
    {
        if (format.Length == 0 || (format.Length == 1 && (format[0] == 'd' || format[0] == 'D')))
        {
            if (!IsNegative)
                return UInt256Formatting.FormatDecimal(scratch, _u0, _u1, _u2, _u3, negative: false);

            ByteUtils.NegateTwosComplement256(_u0, _u1, _u2, _u3, out ulong m0, out ulong m1, out ulong m2, out ulong m3);
            return UInt256Formatting.FormatDecimal(scratch, m0, m1, m2, m3, negative: true);
        }

        HexFormat.Parse(format, out bool withPrefix, out bool uppercase);

        // Negative hex is full-width two's complement to avoid ambiguity; positive hex is the minimal magnitude.
        return UInt256Formatting.FormatHex(scratch, _u0, _u1, _u2, _u3, withPrefix, uppercase, fullWidth: IsNegative);
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Returns true if an unsigned magnitude (u0..u3) is within the allowed magnitude range for negative int256:
    /// magnitude must be <= 2^255 (0x8000..00).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsMagnitudeWithinInt256NegativeRange(ulong u0, ulong u1, ulong u2, ulong u3)
    {
        const ulong LimitHi = 0x8000_0000_0000_0000UL;

        if (u3 < LimitHi)
            return true;

        if (u3 > LimitHi)
            return false;

        // If u3 == 0x8000.. then remaining limbs must be 0 for exactly 2^255.
        return (u2 | u1 | u0) == 0;
    }

    /// <summary>Absolute value as an unsigned 256-bit value (<see cref="MinValue"/> maps to 2^255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint256 Magnitude()
    {
        if (!IsNegative)
            return new uint256(_u0, _u1, _u2, _u3);

        int256 negated = NegateUnchecked(this);
        return new uint256(negated._u0, negated._u1, negated._u2, negated._u3);
    }

    /// <summary>
    /// Builds a signed value from a magnitude and sign, throwing <see cref="OverflowException"/> when it is out of range
    /// (magnitude above 2^255 - 1 for positive results, above 2^255 for negative results).
    /// </summary>
    private static int256 FromMagnitude(in uint256 magnitude, bool negative, string overflowMessage)
    {
        const ulong SignBit = 0x8000_0000_0000_0000UL;
        ulong top = magnitude.U3;

        if (negative)
        {
            if (top > SignBit || (top == SignBit && (magnitude.U2 | magnitude.U1 | magnitude.U0) != 0))
                ThrowOverflow(overflowMessage);

            // Negating 2^255 yields the same bit pattern, which is MinValue.
            return NegateUnchecked(new int256(magnitude.U0, magnitude.U1, magnitude.U2, top));
        }

        if ((top & SignBit) != 0)
            ThrowOverflow(overflowMessage);

        return new int256(magnitude.U0, magnitude.U1, magnitude.U2, top);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOverflow(string message) => throw new OverflowException(message);

    private static bool TryConvertPositiveBigInteger(BigInteger value, out int256 result)
    {
        // value > 0
        int byteCount = value.GetByteCount(isUnsigned: true);
        if (byteCount > 32)
        {
            result = Zero;
            return false;
        }

        Span<byte> bytes = stackalloc byte[32];
        bytes.Clear();

        if (!value.TryWriteBytes(bytes.Slice(32 - byteCount), out _, isUnsigned: true, isBigEndian: true))
        {
            result = Zero;
            return false;
        }

        // Positive range: MSB must not set (<= 2^255 - 1).
        if ((bytes[0] & 0x80) != 0)
        {
            result = Zero;
            return false;
        }

        result = new int256(bytes);
        return true;
    }

    private static bool TryConvertNegativeBigInteger(BigInteger value, out int256 result)
    {
        // value < 0
        BigInteger magnitude = BigInteger.Negate(value); // positive
        int byteCount = magnitude.GetByteCount(isUnsigned: true);
        if (byteCount > 32)
        {
            result = Zero;
            return false;
        }

        Span<byte> bytes = stackalloc byte[32];
        bytes.Clear();

        if (!magnitude.TryWriteBytes(bytes.Slice(32 - byteCount), out _, isUnsigned: true, isBigEndian: true))
        {
            result = Zero;
            return false;
        }

        // Negative magnitude range: magnitude must be <= 2^255 exactly.
        // If MSB > 0x80 => overflow. If MSB == 0x80 then all remaining bytes must be zero.
        if (bytes[0] > 0x80)
        {
            result = Zero;
            return false;
        }

        if (bytes[0] == 0x80)
        {
            for (int i = 1; i < 32; i++)
            {
                if (bytes[i] != 0)
                {
                    result = Zero;
                    return false;
                }
            }
        }

        // Convert magnitude bytes to limbs, then negate into two's complement.
        ulong u3 = BinaryPrimitives.ReadUInt64BigEndian(bytes);
        ulong u2 = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(8));
        ulong u1 = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(16));
        ulong u0 = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(24));

        if ((u0 | u1 | u2 | u3) == 0)
        {
            result = Zero;
            return true;
        }

        ByteUtils.NegateTwosComplement256(u0, u1, u2, u3, out ulong r0, out ulong r1, out ulong r2, out ulong r3);
        result = new int256(r0, r1, r2, r3);
        return true;
    }

    #endregion
}