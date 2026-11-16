using System.Numerics;
using EccentricWare.Web3.DataTypes.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Direct tests of <see cref="LimbReader"/> (64-bit limbs from byte sequences shorter than 32 bytes).</summary>
[TestClass]
public class LimbReaderTests
{
    private static byte[] Sequence(int length, int firstValue = 1)
        => Enumerable.Range(firstValue, length).Select(i => (byte)i).ToArray();

    // ------------------------------------------------------------ TakeBigEndianTail

    [TestMethod]
    public void TakeBigEndianTail_FullLimb_ReadsEightBytesBigEndianAndMovesEnd()
    {
        byte[] bytes = Sequence(10, 0); // 00 01 .. 09
        int end = 10;

        ulong value = LimbReader.TakeBigEndianTail(bytes, ref end, fill: 0);

        Assert.AreEqual(0x0203040506070809UL, value);
        Assert.AreEqual(2, end);
    }

    [TestMethod]
    [DataRow(1, 0x01UL)]
    [DataRow(2, 0x0102UL)]
    [DataRow(3, 0x010203UL)]
    [DataRow(4, 0x01020304UL)]
    [DataRow(5, 0x0102030405UL)]
    [DataRow(6, 0x010203040506UL)]
    [DataRow(7, 0x01020304050607UL)]
    public void TakeBigEndianTail_PartialLimb_TakesEveryByteCountOneToSeven(int length, ulong expected)
    {
        int end = length;

        Assert.AreEqual(expected, LimbReader.TakeBigEndianTail(Sequence(length), ref end, fill: 0));
        Assert.AreEqual(0, end);
    }

    [TestMethod]
    [DataRow(1, 0xFFFFFFFFFFFFFF01UL)]
    [DataRow(2, 0xFFFFFFFFFFFF0102UL)]
    [DataRow(3, 0xFFFFFFFFFF010203UL)]
    [DataRow(4, 0xFFFFFFFF01020304UL)]
    [DataRow(5, 0xFFFFFF0102030405UL)]
    [DataRow(6, 0xFFFF010203040506UL)]
    [DataRow(7, 0xFF01020304050607UL)]
    public void TakeBigEndianTail_FillAboveTheTakenBytes_ExtendsEveryPartialLength(int length, ulong expected)
    {
        int end = length;

        Assert.AreEqual(expected, LimbReader.TakeBigEndianTail(Sequence(length), ref end, fill: ulong.MaxValue));
        Assert.AreEqual(0, end);
    }

    [TestMethod]
    public void TakeBigEndianTail_SignExtension_FillsOnlyTheMissingHighBytes()
    {
        int end = 2;
        Assert.AreEqual(0xFFFFFFFFFFFF8001UL, LimbReader.TakeBigEndianTail([0x80, 0x01], ref end, ulong.MaxValue));

        end = 2;
        Assert.AreEqual(0x8001UL, LimbReader.TakeBigEndianTail([0x80, 0x01], ref end, 0));

        end = 1;
        Assert.AreEqual(ulong.MaxValue, LimbReader.TakeBigEndianTail([0xFF], ref end, ulong.MaxValue));
    }

    [TestMethod]
    public void TakeBigEndianTail_FullLimb_IgnoresFill()
    {
        byte[] bytes = Sequence(8);
        int end = 8;

        Assert.AreEqual(0x0102030405060708UL, LimbReader.TakeBigEndianTail(bytes, ref end, ulong.MaxValue));
        Assert.AreEqual(0, end);
    }

    [TestMethod]
    public void TakeBigEndianTail_NothingLeft_ReturnsFillAndKeepsEndAtZero()
    {
        int end = 0;
        Assert.AreEqual(0UL, LimbReader.TakeBigEndianTail(ReadOnlySpan<byte>.Empty, ref end, 0));
        Assert.AreEqual(0, end);

        Assert.AreEqual(ulong.MaxValue, LimbReader.TakeBigEndianTail(ReadOnlySpan<byte>.Empty, ref end, ulong.MaxValue));
        Assert.AreEqual(0, end);
    }

    [TestMethod]
    public void TakeBigEndianTail_ChainedCalls_WalkTheSequenceFromTheTail()
    {
        byte[] bytes = Sequence(10, 0);
        int end = bytes.Length;

        Assert.AreEqual(0x0203040506070809UL, LimbReader.TakeBigEndianTail(bytes, ref end, ulong.MaxValue));
        Assert.AreEqual(2, end);
        Assert.AreEqual(0xFFFFFFFFFFFF0001UL, LimbReader.TakeBigEndianTail(bytes, ref end, ulong.MaxValue));
        Assert.AreEqual(0, end);
        Assert.AreEqual(ulong.MaxValue, LimbReader.TakeBigEndianTail(bytes, ref end, ulong.MaxValue));
        Assert.AreEqual(ulong.MaxValue, LimbReader.TakeBigEndianTail(bytes, ref end, ulong.MaxValue));
        Assert.AreEqual(0, end);
    }

    [TestMethod]
    public void TakeBigEndianTail_EndBeforeTheSliceEnd_ReadsOnlyBytesBelowEnd()
    {
        byte[] bytes = Sequence(20); // 01..14
        int end = 12;

        Assert.AreEqual(0x05060708090A0B0CUL, LimbReader.TakeBigEndianTail(bytes, ref end, 0));
        Assert.AreEqual(4, end);
        Assert.AreEqual(0x01020304UL, LimbReader.TakeBigEndianTail(bytes, ref end, 0));
        Assert.AreEqual(0, end);
    }

    [TestMethod]
    public void TakeBigEndianTail_FourLimbWalk_MatchesBigIntegerForEveryLengthUpTo32()
    {
        var random = new Random(3232);
        BigInteger mask256 = (BigInteger.One << 256) - 1;
        for (int length = 0; length <= 32; length++)
        {
            for (int sample = 0; sample < 40; sample++)
            {
                byte[] bytes = new byte[length];
                random.NextBytes(bytes);
                if (sample % 2 == 0 && length > 0)
                    bytes[0] |= 0x80; // force the sign bit half of the time

                var unsigned = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
                var signed = new BigInteger(bytes, isUnsigned: false, isBigEndian: true) & mask256; // two's complement view
                bool negative = length > 0 && (bytes[0] & 0x80) != 0;

                AssertWalk(bytes, fill: 0, unsigned, length);
                AssertWalk(bytes, fill: negative ? ulong.MaxValue : 0, signed, length);
            }
        }
    }

    private static void AssertWalk(byte[] bytes, ulong fill, BigInteger expected, int length)
    {
        int end = bytes.Length;
        for (int limb = 0; limb < 4; limb++)
        {
            ulong value = LimbReader.TakeBigEndianTail(bytes, ref end, fill);
            Assert.AreEqual((ulong)((expected >> (64 * limb)) & ulong.MaxValue), value, $"length={length} limb={limb} fill={fill:X}");
        }

        Assert.AreEqual(Math.Max(0, length - 32), end);
    }

    [TestMethod]
    public void TakeBigEndianTail_EndPastTheSequence_ThrowsInsteadOfReadingOutOfBounds()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
        {
            int end = 10;
            LimbReader.TakeBigEndianTail(new byte[4], ref end, 0);
        });
    }

    // ------------------------------------------------------------ ReadLittleEndianLimb

    [TestMethod]
    [DataRow(1, 0x01UL)]
    [DataRow(2, 0x0201UL)]
    [DataRow(3, 0x030201UL)]
    [DataRow(4, 0x04030201UL)]
    [DataRow(5, 0x0504030201UL)]
    [DataRow(6, 0x060504030201UL)]
    [DataRow(7, 0x07060504030201UL)]
    [DataRow(8, 0x0807060504030201UL)]
    [DataRow(9, 0x0807060504030201UL)] // the ninth byte belongs to the next limb
    public void ReadLittleEndianLimb_FromStart_ReadsUpToEightBytesLeastSignificantFirst(int length, ulong expected)
        => Assert.AreEqual(expected, LimbReader.ReadLittleEndianLimb(Sequence(length), 0));

    [TestMethod]
    public void ReadLittleEndianLimb_StartAtLaterLimb_ReadsFromThatOffset()
    {
        byte[] bytes = Sequence(10, 0); // 00 01 .. 09

        Assert.AreEqual(0x0908UL, LimbReader.ReadLittleEndianLimb(bytes, 8));
        Assert.AreEqual(0x0908070605040302UL, LimbReader.ReadLittleEndianLimb(bytes, 2));
    }

    [TestMethod]
    public void ReadLittleEndianLimb_StartAtOrBeyondTheEnd_ReturnsZero()
    {
        byte[] bytes = Sequence(10, 0);

        Assert.AreEqual(0UL, LimbReader.ReadLittleEndianLimb(bytes, 10));
        Assert.AreEqual(0UL, LimbReader.ReadLittleEndianLimb(bytes, 11));
        Assert.AreEqual(0UL, LimbReader.ReadLittleEndianLimb(bytes, 1000));
        Assert.AreEqual(0UL, LimbReader.ReadLittleEndianLimb(ReadOnlySpan<byte>.Empty, 0));
    }

    [TestMethod]
    public void ReadLittleEndianLimb_EveryLengthAndStart_MatchesBigIntegerOracle()
    {
        var random = new Random(4545);
        for (int length = 0; length <= 40; length++)
        {
            byte[] bytes = new byte[length];
            random.NextBytes(bytes);

            for (int start = 0; start <= length + 4; start++)
            {
                ReadOnlySpan<byte> window = start >= length ? [] : bytes.AsSpan(start, Math.Min(8, length - start));
                ulong expected = (ulong)new BigInteger(window, isUnsigned: true, isBigEndian: false);

                Assert.AreEqual(expected, LimbReader.ReadLittleEndianLimb(bytes, start), $"length={length} start={start}");
            }
        }
    }

    [TestMethod]
    public void ReadLittleEndianLimb_NegativeStartOnShortSequence_ThrowsInsteadOfReadingOutOfBounds()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LimbReader.ReadLittleEndianLimb(new byte[3], -1));
    }
}
