using System.Numerics;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// uint256 arithmetic, bitwise operations, bit counts and byte conversions on uniformly random full-width operands, against
/// <see cref="BigInteger"/>. Operands built from sparse limbs (used elsewhere to reach carry and overflow edges) rarely have all four
/// limbs populated, so dense operands are checked separately.
/// </summary>
[TestClass]
public sealed class UInt256DenseOperandTests
{
    private static readonly BigInteger Modulus = BigInteger.One << 256;
    private const int Pairs = 200;

    private static byte[] RandomBytes(Random random)
    {
        byte[] bytes = new byte[32];
        random.NextBytes(bytes);
        return bytes;
    }

    private static BigInteger Big(byte[] bigEndian) => new(bigEndian, isUnsigned: true, isBigEndian: true);

    private static BigInteger Big(uint256 value) => Big(value.ToBigEndianBytes());

    private static IEnumerable<(uint256 X, uint256 Y, BigInteger BigX, BigInteger BigY)> Operands()
    {
        var random = new Random(777);
        for (int i = 0; i < Pairs; i++)
        {
            byte[] x = RandomBytes(random), y = RandomBytes(random);
            yield return (new uint256(x), new uint256(y), Big(x), Big(y));
        }
    }

    [TestMethod]
    public void AddSubtractMultiply_WrapModulo2Pow256()
    {
        foreach (var (x, y, bx, by) in Operands())
        {
            Assert.AreEqual((bx + by) % Modulus, Big(uint256.AddUnchecked(x, y)));
            Assert.AreEqual(((bx - by) % Modulus + Modulus) % Modulus, Big(uint256.SubtractUnchecked(x, y)));
            Assert.AreEqual((bx * by) % Modulus, Big(uint256.MultiplyUnchecked(x, y)));
        }
    }

    [TestMethod]
    public void DivideModuloAndDivRem_MatchBigInteger()
    {
        foreach (var (x, y, bx, by) in Operands())
        {
            if (by.IsZero)
                continue;

            Assert.AreEqual(bx / by, Big(x / y));
            Assert.AreEqual(bx % by, Big(x % y));

            (uint256 quotient, uint256 remainder) = uint256.DivRem(x, y);
            Assert.AreEqual(bx / by, Big(quotient));
            Assert.AreEqual(bx % by, Big(remainder));
        }
    }

    [TestMethod]
    public void BitwiseOperators_MatchBigInteger()
    {
        BigInteger mask = Modulus - 1;
        foreach (var (x, y, bx, by) in Operands())
        {
            Assert.AreEqual(bx & by, Big(x & y));
            Assert.AreEqual(bx | by, Big(x | y));
            Assert.AreEqual(bx ^ by, Big(x ^ y));
            Assert.AreEqual(~bx & mask, Big(~x));
        }
    }

    [TestMethod]
    public void Shifts_MatchBigInteger_ForEveryShiftDistanceClass()
    {
        BigInteger mask = Modulus - 1;
        var random = new Random(778);
        foreach (var (x, _, bx, _) in Operands())
        {
            foreach (int shift in new[] { 0, 1, 63, 64, 65, 127, 128, 191, 192, 255, random.Next(0, 256) })
            {
                Assert.AreEqual((bx << shift) & mask, Big(x << shift), $"<< {shift}");
                Assert.AreEqual(bx >> shift, Big(x >> shift), $">> {shift}");
            }
        }
    }

    [TestMethod]
    public void BitCounts_MatchBigInteger()
    {
        foreach (var (x, _, bx, _) in Operands())
        {
            Assert.AreEqual(256 - (int)bx.GetBitLength(), x.LeadingZeroCount());
            Assert.AreEqual((int)((bx & -bx).GetBitLength() - 1), x.TrailingZeroCount());
            Assert.AreEqual(bx.ToByteArray(isUnsigned: true).Sum(b => BitOperations_PopCount(b)), x.PopCount());
        }
    }

    private static int BitOperations_PopCount(byte value) => System.Numerics.BitOperations.PopCount(value);

    [TestMethod]
    public void ByteConversions_RoundTripAndAreMirrorImages()
    {
        var random = new Random(779);
        for (int i = 0; i < Pairs; i++)
        {
            byte[] bigEndian = RandomBytes(random);
            var value = new uint256(bigEndian);

            CollectionAssert.AreEqual(bigEndian, value.ToBigEndianBytes());
            CollectionAssert.AreEqual(bigEndian.Reverse().ToArray(), value.ToLittleEndianBytes());
            Assert.AreEqual(value, uint256.FromLittleEndian(value.ToLittleEndianBytes()));
        }
    }

    [TestMethod]
    public void Comparison_MatchesBigInteger()
    {
        foreach (var (x, y, bx, by) in Operands())
        {
            Assert.AreEqual(Math.Sign(bx.CompareTo(by)), Math.Sign(x.CompareTo(y)));
            Assert.AreEqual(bx < by, x < y);
            Assert.AreEqual(bx >= by, x >= y);
            Assert.AreEqual(bx == by, x == y);
        }
    }
}
