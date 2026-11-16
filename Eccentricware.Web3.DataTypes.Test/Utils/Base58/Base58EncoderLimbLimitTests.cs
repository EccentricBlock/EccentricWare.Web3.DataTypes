using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>The documented 16-limb (512-bit) limit of <see cref="Base58Encoder.Encode"/> is enforced.</summary>
[TestClass]
public sealed class Base58EncoderLimbLimitTests
{
    [TestMethod]
    public void MaxLimbs_IsSixteen_BecauseTheDigitBufferIsSizedFor512Bits()
        => Assert.AreEqual(16, Base58Encoder.MaxLimbs);

    [TestMethod]
    [DataRow(17)]
    [DataRow(18)]
    [DataRow(64)]
    public void MoreThanSixteenLimbs_ThrowsArgumentException_EvenWhenTheValueWouldFit(int limbCount)
    {
        // With 17+ limbs and a large value the digit buffer overflowed with an obscure ArgumentOutOfRangeException; with leading
        // zero limbs it worked by accident. The limit is the documented contract either way.
        uint[] limbs = new uint[limbCount];
        limbs[^1] = 1; // a small value in a wide array

        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => Base58Encoder.Encode(limbs, new byte[128]));
        Assert.AreEqual("limbs", error.ParamName);

        uint[] large = Enumerable.Repeat(uint.MaxValue, limbCount).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => Base58Encoder.Encode(large, new byte[256]));
    }

    [TestMethod]
    public void ExactlySixteenLimbs_Encodes_ToTheLongestResult()
    {
        uint[] limbs = Enumerable.Repeat(uint.MaxValue, Base58Encoder.MaxLimbs).ToArray();
        byte[] destination = new byte[128];

        int written = Base58Encoder.Encode(limbs, destination);

        // 2^512 - 1 is 88 Base58 digits; vector from an independent Base58 implementation.
        Assert.AreEqual(88, written);
        Assert.AreEqual("67rpwLCuS5DGA8KGZXKsVQ7dnPb9goRLoKfgGbLfQg9WoLUgNY77E2jT11fem3coV9nAkguBACzrU1iyZM4B8roQ",
            System.Text.Encoding.ASCII.GetString(destination, 0, written));
    }

    [TestMethod]
    public void FewerLimbs_AndNone_StillEncode()
    {
        Assert.AreEqual(0, Base58Encoder.Encode([], new byte[8]));

        // One limb is four bytes (00 00 00 39): three leading zero bytes become three '1's, then 57 is 'z', the last digit of the
        // alphabet (Base58 rule, independent of this implementation).
        byte[] destination = new byte[8];
        int written = Base58Encoder.Encode([57u], destination);
        Assert.AreEqual(4, written);
        Assert.AreEqual("111z", System.Text.Encoding.ASCII.GetString(destination, 0, written));
    }
}
