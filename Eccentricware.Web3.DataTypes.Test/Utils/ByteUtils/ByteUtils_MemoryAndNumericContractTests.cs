using EccentricWare.Web3.DataTypes.Utils;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Pow10U64, GetByteCount, AsReadOnlyBytes, ReadVector256, SampleUInt64LittleEndian, NegateTwosComplement256.</summary>
[TestClass]
public sealed class ByteUtils_MemoryAndNumericContractTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FourLimbs
    {
        public ulong A, B, C, D;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EmptyStruct;

    private static BigInteger Pack(ulong u0, ulong u1, ulong u2, ulong u3) =>
        (BigInteger)u0 | ((BigInteger)u1 << 64) | ((BigInteger)u2 << 128) | ((BigInteger)u3 << 192);

    // ---- Pow10U64 / GetByteCount -----------------------------------------------------------------------------------------

    [TestMethod]
    public void Pow10U64_AllSupportedExponents_MatchBigIntegerPow()
    {
        for (int exponent = 0; exponent <= 19; exponent++)
            Assert.AreEqual((ulong)BigInteger.Pow(10, exponent), ByteUtils.Pow10U64(exponent), $"10^{exponent}");
    }

    [TestMethod]
    public void GetByteCount_PowersOfTwoAndNeighbours_MatchBitLengthOracle()
    {
        Assert.AreEqual(0, ByteUtils.GetByteCount(0));
        for (int bit = 0; bit < 64; bit++)
        {
            ulong power = 1UL << bit;
            Assert.AreEqual((bit + 1 + 7) / 8, ByteUtils.GetByteCount(power), $"2^{bit}");
            if (bit > 0)
                Assert.AreEqual((bit + 7) / 8, ByteUtils.GetByteCount(power - 1), $"2^{bit}-1");
        }

        Assert.AreEqual(8, ByteUtils.GetByteCount(ulong.MaxValue));
    }

    // ---- AsReadOnlyBytes / ReadVector256 -----------------------------------------------------------------------------------

    [TestMethod]
    public void AsReadOnlyBytes_MultiFieldStruct_ExposesAllBytesInLittleEndianFieldOrder()
    {
        var value = new FourLimbs { A = 0x0807060504030201, B = 0x1817161514131211, C = 0x2827262524232221, D = 0x3837363534333231 };

        ReadOnlySpan<byte> bytes = ByteUtils.AsReadOnlyBytes(in value);

        Assert.AreEqual(32, bytes.Length);
        byte[] expected = new byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian(expected.AsSpan(0), value.A);
        BinaryPrimitives.WriteUInt64LittleEndian(expected.AsSpan(8), value.B);
        BinaryPrimitives.WriteUInt64LittleEndian(expected.AsSpan(16), value.C);
        BinaryPrimitives.WriteUInt64LittleEndian(expected.AsSpan(24), value.D);
        CollectionAssert.AreEqual(expected, bytes.ToArray());
    }

    [TestMethod]
    public void AsReadOnlyBytes_IsAViewOverTheValue_NotACopy()
    {
        var value = new FourLimbs { A = 1 };
        ReadOnlySpan<byte> bytes = ByteUtils.AsReadOnlyBytes(in value);
        Assert.AreEqual(1, bytes[0]);

        value.A = 0xAB;

        Assert.AreEqual(0xAB, bytes[0]);
    }

    [TestMethod]
    public void AsReadOnlyBytes_PrimitiveAndZeroSizedStruct_HaveNativeSize()
    {
        int single = 0x01020304;
        Assert.AreEqual(4, ByteUtils.AsReadOnlyBytes(in single).Length);
        // An empty struct is one byte in .NET (not zero-sized); the span reports the CLR size.
        EmptyStruct empty = default;
        Assert.AreEqual(System.Runtime.CompilerServices.Unsafe.SizeOf<EmptyStruct>(), ByteUtils.AsReadOnlyBytes(in empty).Length);
    }

    [TestMethod]
    public void ReadVector256_ReadsOnlyTheFirst32Bytes_OfALongerSpan()
    {
        byte[] source = Enumerable.Range(0, 64).Select(i => (byte)(i + 1)).ToArray();

        Vector256<byte> vector = ByteUtils.ReadVector256(source);

        for (int i = 0; i < 32; i++)
            Assert.AreEqual(source[i], vector[i], $"lane {i}");
    }

    [TestMethod]
    public void ReadVector256_UnalignedSlice_ReadsShiftedBytes()
    {
        byte[] source = Enumerable.Range(0, 40).Select(i => (byte)(i * 3)).ToArray();

        for (int offset = 0; offset <= 8; offset++)
        {
            Vector256<byte> vector = ByteUtils.ReadVector256(source.AsSpan(offset, 32));
            for (int i = 0; i < 32; i++)
                Assert.AreEqual(source[offset + i], vector[i], $"offset {offset}, lane {i}");
        }
    }

    // ---- SampleUInt64LittleEndian --------------------------------------------------------------------------------------------

    [TestMethod]
    public void SampleUInt64LittleEndian_NineByteSource_AtEveryStartIndex()
    {
        byte[] source = [1, 2, 3, 4, 5, 6, 7, 8, 9];

        Assert.AreEqual(0x0807060504030201UL, ByteUtils.SampleUInt64LittleEndian(source, 0));
        Assert.AreEqual(0x0908070605040302UL, ByteUtils.SampleUInt64LittleEndian(source, 1)); // exactly 8 remaining
        Assert.AreEqual(0x0009080706050403UL, ByteUtils.SampleUInt64LittleEndian(source, 2)); // 7 remaining: missing byte is zero
        Assert.AreEqual(0x0000000000090807UL, ByteUtils.SampleUInt64LittleEndian(source, 6));
        Assert.AreEqual(9UL, ByteUtils.SampleUInt64LittleEndian(source, 8));                   // 1 remaining
    }

    [TestMethod]
    [DataRow(9)]
    [DataRow(10)]
    [DataRow(int.MaxValue)]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    public void SampleUInt64LittleEndian_IndexOutsideTheSource_ReturnsZero(int startIndex)
    {
        Assert.AreEqual(0UL, ByteUtils.SampleUInt64LittleEndian([1, 2, 3, 4, 5, 6, 7, 8, 9], startIndex));
    }

    [TestMethod]
    public void SampleUInt64LittleEndian_EmptySource_ReturnsZero()
    {
        Assert.AreEqual(0UL, ByteUtils.SampleUInt64LittleEndian([], 0));
    }

    [TestMethod]
    public void SampleUInt64LittleEndian_RandomSources_MatchBinaryPrimitivesOnZeroPaddedWindow()
    {
        var random = new Random(64);
        for (int i = 0; i < 300; i++)
        {
            byte[] source = new byte[random.Next(1, 24)];
            random.NextBytes(source);
            int start = random.Next(0, source.Length);
            byte[] window = new byte[8];
            source.AsSpan(start, Math.Min(8, source.Length - start)).CopyTo(window);

            Assert.AreEqual(BinaryPrimitives.ReadUInt64LittleEndian(window), ByteUtils.SampleUInt64LittleEndian(source, start));
        }
    }

    // ---- NegateTwosComplement256 -----------------------------------------------------------------------------------------------

    [TestMethod]
    public void NegateTwosComplement256_EdgeValues()
    {
        ByteUtils.NegateTwosComplement256(0, 0, 0, 0, out ulong a, out ulong b, out ulong c, out ulong d);
        CollectionAssert.AreEqual(new ulong[4], new[] { a, b, c, d }, "-0 == 0");

        ByteUtils.NegateTwosComplement256(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, out a, out b, out c, out d);
        CollectionAssert.AreEqual(new ulong[] { 1, 0, 0, 0 }, new[] { a, b, c, d }, "-(2^256-1) == 1");

        ByteUtils.NegateTwosComplement256(0, 0, 0, 0x8000_0000_0000_0000, out a, out b, out c, out d);
        CollectionAssert.AreEqual(new ulong[] { 0, 0, 0, 0x8000_0000_0000_0000 }, new[] { a, b, c, d }, "-2^255 == 2^255");

        ByteUtils.NegateTwosComplement256(0, 0, 1, 0, out a, out b, out c, out d);
        CollectionAssert.AreEqual(new ulong[] { 0, 0, ulong.MaxValue, ulong.MaxValue }, new[] { a, b, c, d }, "carry ripples through two limbs");

        ByteUtils.NegateTwosComplement256(0, 1, 0, 0, out a, out b, out c, out d);
        CollectionAssert.AreEqual(new ulong[] { 0, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue }, new[] { a, b, c, d }, "carry ripples through one limb");
    }

    [TestMethod]
    public void NegateTwosComplement256_RandomValues_SatisfyModularIdentities()
    {
        BigInteger modulus = BigInteger.One << 256;
        var random = new Random(256);
        for (int i = 0; i < 1000; i++)
        {
            ulong u0 = (ulong)random.NextInt64(), u1 = (ulong)random.NextInt64(), u2 = (ulong)random.NextInt64(), u3 = (ulong)random.NextInt64();
            // Force zero limbs now and then so carry chains across limbs are exercised.
            if (i % 4 == 0) u0 = 0;
            if (i % 8 == 0) u1 = 0;
            if (i % 16 == 0) u2 = 0;

            ByteUtils.NegateTwosComplement256(u0, u1, u2, u3, out ulong r0, out ulong r1, out ulong r2, out ulong r3);
            BigInteger value = Pack(u0, u1, u2, u3);

            Assert.AreEqual((modulus - value) % modulus, Pack(r0, r1, r2, r3));

            ByteUtils.NegateTwosComplement256(r0, r1, r2, r3, out ulong b0, out ulong b1, out ulong b2, out ulong b3);
            Assert.AreEqual(value, Pack(b0, b1, b2, b3), "double negation is the identity");
        }
    }
}
