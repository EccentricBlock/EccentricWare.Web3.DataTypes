namespace EccentricWare.Web3.DataTypes.Tests;

[TestClass]
public sealed class Hash32BulkSearchTests
{
    [TestMethod]
    public void IndexOf_And_CountEquals_EmptyHaystack()
    {
        Assert.AreEqual(-1, Hash32.IndexOf(ReadOnlySpan<Hash32>.Empty, Hash32.Zero));
        Assert.AreEqual(0, Hash32.CountEquals(ReadOnlySpan<Hash32>.Empty, Hash32.Zero));
        Assert.AreEqual(0, Hash32.CountEquals(Array.Empty<Hash32>(), new Hash32(0, 0, 0, 1)));
    }

    [TestMethod]
    public void IndexOf_And_CountEquals_MatchScalarReference_ForAllLengthsAndPositions()
    {
        var rng = new Random(99);
        for (int length = 0; length <= 70; length++)
        {
            Hash32[] haystack = Hash32TestData.RandomHashes(rng, length);

            for (int position = 0; position < length; position++)
            {
                Hash32 needle = haystack[position];
                Assert.AreEqual(position, Hash32.IndexOf(haystack, needle), $"len={length} pos={position}");
                Assert.AreEqual(1, Hash32.CountEquals(haystack, needle), $"len={length} pos={position}");
            }

            Hash32 absent = Hash32TestData.RandomHashes(rng, 1)[0];
            Assert.AreEqual(-1, Hash32.IndexOf(haystack, absent));
            Assert.AreEqual(0, Hash32.CountEquals(haystack, absent));
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(16)]
    [DataRow(17)]
    public void NeedleEqualToTheLastElementOnly_IsFoundAtTheEnd(int length)
    {
        // Lengths straddle the SIMD threshold of 8 and the scalar tail.
        Hash32[] haystack = Hash32TestData.RandomHashes(new Random(length), length);
        Hash32 needle = haystack[^1];

        Assert.AreEqual(length - 1, Hash32.IndexOf(haystack, needle));
        Assert.AreEqual(1, Hash32.CountEquals(haystack, needle));
    }

    [TestMethod]
    public void ZeroNeedle_InAZeroFilledHaystack_MatchesEveryElement()
    {
        for (int length = 1; length <= 40; length++)
        {
            Hash32[] haystack = new Hash32[length];

            Assert.AreEqual(0, Hash32.IndexOf(haystack, Hash32.Zero), $"len={length}");
            Assert.AreEqual(length, Hash32.CountEquals(haystack, Hash32.Zero), $"len={length}");
        }
    }

    [TestMethod]
    public void AllEqualHaystack_CountsEveryElement_AndOtherNeedlesDoNotMatch()
    {
        Hash32 value = Hash32TestData.Value0To31();
        Hash32[] haystack = Enumerable.Repeat(value, 33).ToArray();

        Assert.AreEqual(0, Hash32.IndexOf(haystack, value));
        Assert.AreEqual(33, Hash32.CountEquals(haystack, value));
        Assert.AreEqual(-1, Hash32.IndexOf(haystack, Hash32.Zero));
        Assert.AreEqual(0, Hash32.CountEquals(haystack, Hash32.Zero));
    }

    [TestMethod]
    public void NeedleWithReversedLimbOrder_IsNotAMatch()
    {
        var value = new Hash32(1, 2, 3, 4);
        var reversed = new Hash32(4, 3, 2, 1);
        Hash32[] haystack = Enumerable.Repeat(value, 20).ToArray();

        Assert.AreEqual(-1, Hash32.IndexOf(haystack, reversed));
        Assert.AreEqual(0, Hash32.CountEquals(haystack, reversed));
    }

    [TestMethod]
    public void IndexOf_And_CountEquals_NearMissInAnySingleByte_IsNotAMatch()
    {
        var rng = new Random(7);
        Hash32[] haystack = Hash32TestData.RandomHashes(rng, 33); // above the SIMD threshold, with a scalar tail
        byte[] be = haystack[20].ToBigEndianBytes();

        for (int byteIndex = 0; byteIndex < Hash32.ByteLength; byteIndex++)
        {
            byte[] mutated = (byte[])be.Clone();
            mutated[byteIndex] ^= 0x80;
            var needle = new Hash32(mutated);

            Assert.AreEqual(-1, Hash32.IndexOf(haystack, needle), $"byte {byteIndex}");
            Assert.AreEqual(0, Hash32.CountEquals(haystack, needle), $"byte {byteIndex}");
        }
    }

    [TestMethod]
    public void IndexOf_And_CountEquals_Duplicates_ReturnFirstIndexAndFullCount()
    {
        var rng = new Random(11);
        Hash32[] haystack = Hash32TestData.RandomHashes(rng, 50);
        Hash32 needle = haystack[13];
        haystack[13 + 7] = needle;
        haystack[49] = needle;

        Assert.AreEqual(13, Hash32.IndexOf(haystack, needle));
        Assert.AreEqual(3, Hash32.CountEquals(haystack, needle));
    }

    [TestMethod]
    public void IndexOf_And_CountEquals_WorkOnOffsetSlices()
    {
        var rng = new Random(13);
        Hash32[] haystack = Hash32TestData.RandomHashes(rng, 40);
        Hash32 needle = haystack[25];

        // Slices start at arbitrary element offsets; results are relative to the slice.
        Assert.AreEqual(25 - 3, Hash32.IndexOf(haystack.AsSpan(3), needle));
        Assert.AreEqual(-1, Hash32.IndexOf(haystack.AsSpan(26), needle));
        Assert.AreEqual(-1, Hash32.IndexOf(haystack.AsSpan(3, 20), needle));
        Assert.AreEqual(1, Hash32.CountEquals(haystack.AsSpan(3, 23), needle));
    }
}
