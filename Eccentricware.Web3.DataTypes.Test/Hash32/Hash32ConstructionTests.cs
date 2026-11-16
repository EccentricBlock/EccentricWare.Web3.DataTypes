using EccentricWare.Web3.DataTypes.Utils;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class Hash32ConstructionTests
{
    private const byte Sentinel = 0xAA;

    [TestMethod]
    public void Constants_AreExpected()
    {
        Assert.AreEqual(32, Hash32.ByteLength);
        Assert.AreEqual(64, Hash32.HexLength);
    }

    [TestMethod]
    public void Zero_IsZero_AndEqualsDefault()
    {
        Hash32 z1 = Hash32.Zero;
        Hash32 z2 = default;

        Assert.IsTrue(z1.IsZero);
        Assert.IsTrue(z2.IsZero);
        Assert.IsTrue(z1 == z2);
        Assert.IsFalse(z1 != z2);

        string s = z1.ToString();
        Assert.AreEqual(66, s.Length);
        Assert.IsTrue(s.StartsWith("0x", StringComparison.Ordinal));
        Assert.IsTrue(s.Skip(2).All(c => c == '0'));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void IsZero_False_WhenOnlyOneLimbIsNonZero(int limb)
    {
        Assert.IsFalse(Hash32TestData.WithLimb(limb, 1, filler: 0).IsZero);
        Assert.IsFalse(Hash32TestData.WithLimb(limb, ulong.MaxValue, filler: 0).IsZero);
    }

    [TestMethod]
    public void Ctor_Limbs_WriteBigEndian_ProducesLiteralBytesForAllFourLimbs()
    {
        var value = new Hash32(
            0x0102030405060708UL,
            0x1112131415161718UL,
            0xA1A2A3A4A5A6A7A8UL,
            0xFFEEDDCCBBAA9988UL);

        byte[] expected =
        [
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
            0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8,
            0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA, 0x99, 0x88,
        ];

        byte[] actual = new byte[Hash32.ByteLength];
        value.WriteBigEndian(actual);

        CollectionAssert.AreEqual(expected, actual);
        Assert.AreEqual(value, new Hash32(expected));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(31)]
    [DataRow(33)]
    [DataRow(64)]
    public void Ctor_BigEndianBytes_InvalidLength_ThrowsArgumentException(int length)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => _ = new Hash32(new byte[length]));
        Assert.AreEqual("bigEndianBytes", ex.ParamName);
    }

    [TestMethod]
    public void FromLittleEndian_ReadsLimbsFromReversedBytes()
    {
        // Independent vector: little-endian bytes 00..1F are the big-endian bytes 1F..00.
        Hash32 value = Hash32.FromLittleEndian(Hash32TestData.Bytes0To31());

        Assert.AreEqual(
            new Hash32(0x1F1E1D1C1B1A1918UL, 0x1716151413121110UL, 0x0F0E0D0C0B0A0908UL, 0x0706050403020100UL),
            value);
        CollectionAssert.AreEqual(Hash32TestData.Reversed(Hash32TestData.Bytes0To31()), value.ToBigEndianBytes());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(31)]
    [DataRow(33)]
    public void FromLittleEndian_InvalidLength_ThrowsArgumentException(int length)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => Hash32.FromLittleEndian(new byte[length]));
        Assert.AreEqual("littleEndianBytes", ex.ParamName);
    }

    [TestMethod]
    public void FromLittleEndian_RoundTrips_WithWriteLittleEndian()
    {
        byte[] bytes = Hash32TestData.Bytes0To31();
        var bigEndianValue = new Hash32(bytes);

        Span<byte> le = stackalloc byte[Hash32.ByteLength];
        bigEndianValue.WriteLittleEndian(le);

        var fromLe = Hash32.FromLittleEndian(le);
        Assert.AreEqual(bigEndianValue, fromLe);

        Span<byte> be2 = stackalloc byte[Hash32.ByteLength];
        fromLe.WriteBigEndian(be2);
        CollectionAssert.AreEqual(bytes, be2.ToArray());
    }

    [TestMethod]
    public void ToBigEndianBytes_And_ToLittleEndianBytes_AreCorrect()
    {
        byte[] be = Hash32TestData.Bytes0To31();
        var value = new Hash32(be);

        byte[] be2 = value.ToBigEndianBytes();
        Assert.HasCount(Hash32.ByteLength, be2);
        CollectionAssert.AreEqual(be, be2);

        byte[] le = value.ToLittleEndianBytes();
        Assert.HasCount(Hash32.ByteLength, le);
        CollectionAssert.AreEqual(Hash32TestData.Reversed(be), le);
        Assert.AreEqual(value, Hash32.FromLittleEndian(le));
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(false, 31)]
    [DataRow(true, 0)]
    [DataRow(true, 31)]
    public void Write_DestinationTooSmall_ThrowsAndLeavesBufferUntouched(bool littleEndian, int length)
    {
        Hash32 value = Hash32TestData.Value0To31();
        byte[] destination = new byte[length];
        Array.Fill(destination, Sentinel);

        var ex = Assert.ThrowsExactly<ArgumentException>(() => Write(value, destination, littleEndian));

        Assert.AreEqual("destination", ex.ParamName);
        Assert.IsTrue(destination.All(b => b == Sentinel));
    }

    [TestMethod]
    [DataRow(false, 32)]
    [DataRow(false, 33)]
    [DataRow(false, 64)]
    [DataRow(true, 32)]
    [DataRow(true, 33)]
    [DataRow(true, 64)]
    public void Write_DestinationAtLeast32Bytes_WritesExactly32AndKeepsTail(bool littleEndian, int length)
    {
        byte[] bigEndian = Hash32TestData.Bytes0To31();
        byte[] expected32 = littleEndian ? Hash32TestData.Reversed(bigEndian) : bigEndian;
        byte[] destination = new byte[length];
        Array.Fill(destination, Sentinel);

        Write(Hash32TestData.Value0To31(), destination, littleEndian);

        CollectionAssert.AreEqual(expected32, destination.AsSpan(0, 32).ToArray());
        Assert.IsTrue(destination.AsSpan(32).ToArray().All(b => b == Sentinel));
    }

    [TestMethod]
    public void Layout_IsExactly32Bytes_WithNoPadding()
    {
        // IndexOf/CountEquals reinterpret Hash32[] as bytes and stride by ByteLength; a hidden field would break them.
        Assert.AreEqual(32, Unsafe.SizeOf<Hash32>());

        Hash32[] array = [Hash32.Zero, Hash32.Zero, Hash32.Zero];
        Assert.AreEqual(96, MemoryMarshal.AsBytes<Hash32>(array).Length);
    }

    [TestMethod]
    public void Layout_RawBytes_AreEqualExactlyWhenValuesAreEqual()
    {
        Hash32 a = Hash32TestData.Value0To31();
        Hash32 sameValue = new(Hash32TestData.Bytes0To31());
        Hash32 different = Hash32TestData.WithLimb(2, 5, filler: 1);

        Assert.IsTrue(ByteUtils.AsReadOnlyBytes(in a).SequenceEqual(ByteUtils.AsReadOnlyBytes(in sameValue)));
        Assert.IsFalse(ByteUtils.AsReadOnlyBytes(in a).SequenceEqual(ByteUtils.AsReadOnlyBytes(in different)));
    }

    [TestMethod]
    public void ExplicitConversion_FromByteArray_Works()
    {
        byte[] bytes = Hash32TestData.Bytes0To31();
        Hash32 h = (Hash32)bytes;
        Assert.AreEqual(new Hash32(bytes), h);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(31)]
    [DataRow(33)]
    public void ExplicitConversion_FromByteArray_InvalidLength_ThrowsArgumentException(int length)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => _ = (Hash32)new byte[length]);
        Assert.AreEqual("bigEndianBytes", ex.ParamName);
    }

    [TestMethod]
    public void ExplicitConversion_FromNullArray_ThrowsArgumentNullException()
    {
        // A null array used to become an empty span and fail the length check with ArgumentException.
        ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(() => _ = (Hash32)(byte[])null!);
        Assert.AreEqual("bytes", error.ParamName);

        // A non-null array of the wrong length is still the length error.
        Assert.ThrowsExactly<ArgumentException>(() => _ = (Hash32)new byte[31]);
    }

    private static void Write(Hash32 value, byte[] destination, bool littleEndian)
    {
        if (littleEndian)
            value.WriteLittleEndian(destination);
        else
            value.WriteBigEndian(destination);
    }
}
