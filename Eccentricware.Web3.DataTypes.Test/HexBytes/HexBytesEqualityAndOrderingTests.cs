namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary><see cref="HexBytes"/>: equality and the ordering contract (unsigned lexicographic byte order).</summary>
[TestClass]
public sealed class HexBytesEqualityAndOrderingTests
{
    /// <summary>Reference ordering: unsigned bytes, first difference decides, a proper prefix sorts first.</summary>
    private static int ReferenceCompare(byte[] a, byte[] b)
    {
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            if (a[i] != b[i])
                return a[i] < b[i] ? -1 : 1;
        }

        return a.Length.CompareTo(b.Length);
    }

    private static HexBytes H(params byte[] bytes) => new(bytes);

    // ----------------- equality -----------------

    [TestMethod]
    public void Equality_ByContent_AndOperators()
    {
        var a = H(1, 2, 3);
        var b = H(1, 2, 3);
        var c = H(1, 2, 4);
        var d = H(1, 2);

        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
        Assert.IsTrue(a.Equals(b));
        Assert.IsTrue(a.Equals((object)b));
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.IsTrue(a != c);
        Assert.IsFalse(a == c);
        Assert.IsTrue(a != d);
        Assert.IsFalse(a == d);
        Assert.IsFalse(a.Equals("not hex bytes"));
        Assert.IsFalse(a.Equals((object?)null));
    }

    [TestMethod]
    public void Equality_HoldsAcrossConstructionPaths()
    {
        byte[] bytes = [0xDE, 0xAD, 0xBE, 0xEF, 0x01];
        HexBytes[] same =
        [
            new HexBytes(bytes),
            new HexBytes(bytes.AsSpan()),
            HexBytes.FromArrayUnsafe((byte[])bytes.Clone()),
            HexBytes.Parse("0xdeadbeef01"),
            HexBytes.Parse("DEADBEEF01"),
            new HexBytes(new byte[] { 0, 0xDE, 0xAD, 0xBE, 0xEF, 0x01 }).Slice(1),
            H(0xDE, 0xAD) + H(0xBE, 0xEF, 0x01),
            new HexBytes(new HexBytes(bytes).ToArray()),
        ];

        foreach (HexBytes value in same)
        {
            Assert.AreEqual(same[0], value);
            Assert.AreEqual(same[0].GetHashCode(), value.GetHashCode());
            Assert.AreEqual(0, same[0].CompareTo(value));
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(15)]
    [DataRow(16)]
    [DataRow(17)]
    [DataRow(31)]
    [DataRow(32)]
    [DataRow(33)]
    [DataRow(63)]
    [DataRow(64)]
    [DataRow(65)]
    public void SingleByteDifference_AtEveryIndex_BreaksEquality_AndOrdersByThatByte(int length)
    {
        // Lengths straddle the vector widths used by SequenceEqual / SequenceCompareTo.
        byte[] baseBytes = new byte[length];
        new Random(length).NextBytes(baseBytes);
        baseBytes.AsSpan().Fill(0x80); // mid-range value, so both directions are possible
        var original = new HexBytes(baseBytes);

        for (int i = 0; i < length; i++)
        {
            foreach (byte changed in new byte[] { 0x00, 0x7F, 0x81, 0xFF })
            {
                byte[] other = (byte[])baseBytes.Clone();
                other[i] = changed;
                var different = new HexBytes(other);

                Assert.IsFalse(original.Equals(different), $"i={i} v={changed}");
                Assert.IsFalse(original == different);
                Assert.IsTrue(original != different);
                int expectedSign = changed < 0x80 ? 1 : -1;
                Assert.AreEqual(expectedSign, Math.Sign(original.CompareTo(different)), $"i={i} v={changed}");
                Assert.AreEqual(-expectedSign, Math.Sign(different.CompareTo(original)), $"i={i} v={changed}");
            }
        }
    }

    // ----------------- ordering -----------------

    [TestMethod]
    [DataRow(new byte[] { 0x7F }, new byte[] { 0x80 })]
    [DataRow(new byte[] { 0x00 }, new byte[] { 0xFF })]
    [DataRow(new byte[] { 0x00, 0xFF }, new byte[] { 0x01 })]
    [DataRow(new byte[] { 0x01 }, new byte[] { 0x01, 0x00 })]       // proper prefix sorts first
    [DataRow(new byte[] { 0x01, 0x02 }, new byte[] { 0x01, 0x02, 0x03 })]
    [DataRow(new byte[] { 0x01, 0x02, 0xFF }, new byte[] { 0x01, 0x03 })]
    [DataRow(new byte[0], new byte[] { 0x00 })]                    // zero bytes are not empty
    public void Ordering_IsUnsignedLexicographic_AllOperatorsBothOutcomes(byte[] lowBytes, byte[] highBytes)
    {
        HexBytes low = new(lowBytes), high = new(highBytes);

        Assert.IsLessThan(0, low.CompareTo(high));
        Assert.IsGreaterThan(0, high.CompareTo(low));

        Assert.IsTrue(low < high);
        Assert.IsFalse(high < low);
        Assert.IsTrue(high > low);
        Assert.IsFalse(low > high);
        Assert.IsTrue(low <= high);
        Assert.IsFalse(high <= low);
        Assert.IsTrue(high >= low);
        Assert.IsFalse(low >= high);
    }

    [TestMethod]
    public void Ordering_EqualValues_AreNeitherLessNorGreater_ButBothOrEqual()
    {
        HexBytes a = H(1, 2), b = H(1, 2);

        Assert.AreEqual(0, a.CompareTo(b));
        Assert.IsFalse(a < b);
        Assert.IsFalse(a > b);
        Assert.IsTrue(a <= b);
        Assert.IsTrue(a >= b);

        Assert.AreEqual(0, HexBytes.Empty.CompareTo(default));
        Assert.IsTrue(HexBytes.Empty <= default(HexBytes));
        Assert.IsTrue(HexBytes.Empty >= default(HexBytes));
        Assert.IsFalse(HexBytes.Empty < default(HexBytes));
    }

    [TestMethod]
    public void CompareTo_IsZero_ExactlyWhenEqual()
    {
        HexBytes[] values = [HexBytes.Empty, default, H(0), H(0, 0), H(1), H(1, 0), H(0xFF)];

        foreach (HexBytes a in values)
        {
            foreach (HexBytes b in values)
                Assert.AreEqual(a.Equals(b), a.CompareTo(b) == 0, $"{a} vs {b}");
        }
    }

    [TestMethod]
    public void CompareTo_MatchesTheReference_AndIsAntisymmetric_OnRandomPairs()
    {
        var random = new Random(31337);
        for (int i = 0; i < 400; i++)
        {
            byte[] a = new byte[random.Next(0, 40)];
            random.NextBytes(a);
            byte[] b = (byte[])a.Clone();

            // Half the pairs share a prefix with a mutation (or truncation/extension) further on.
            if (i % 2 == 0)
            {
                if (b.Length > 0 && random.Next(2) == 0)
                    b[random.Next(b.Length)] = (byte)random.Next(256);
                else
                    Array.Resize(ref b, random.Next(0, 40));
            }
            else
            {
                b = new byte[random.Next(0, 40)];
                random.NextBytes(b);
            }

            HexBytes ha = new(a), hb = new(b);
            int expected = ReferenceCompare(a, b);

            Assert.AreEqual(expected, Math.Sign(ha.CompareTo(hb)), $"{ha} vs {hb}");
            Assert.AreEqual(-expected, Math.Sign(hb.CompareTo(ha)), $"{hb} vs {ha}");
            Assert.AreEqual(expected < 0, ha < hb);
            Assert.AreEqual(expected > 0, ha > hb);
            Assert.AreEqual(expected <= 0, ha <= hb);
            Assert.AreEqual(expected >= 0, ha >= hb);
        }
    }

    [TestMethod]
    public void ArraySort_OrdersLikeTheReferenceComparer()
    {
        var random = new Random(8);
        byte[][] raw = new byte[200][];
        for (int i = 0; i < raw.Length; i++)
        {
            raw[i] = new byte[random.Next(0, 6)];
            random.NextBytes(raw[i]);
        }

        HexBytes[] sorted = raw.Select(r => new HexBytes(r)).ToArray();
        Array.Sort(sorted);
        byte[][] expected = [.. raw];
        Array.Sort(expected, ReferenceCompare);

        for (int i = 0; i < raw.Length; i++)
            CollectionAssert.AreEqual(expected[i], sorted[i].ToArray(), $"index {i}");
    }

    [TestMethod]
    public void BoxedCompareTo_ComparesLikeTheTypedOverload()
    {
        IComparable low = H(1, 2);
        object high = H(1, 3);

        Assert.AreEqual(-1, Math.Sign(low.CompareTo(high)));
        Assert.AreEqual(1, Math.Sign(((IComparable)high).CompareTo(low)));
        Assert.AreEqual(0, low.CompareTo((object)H(1, 2)));
        Assert.AreEqual(1, Math.Sign(H(0).CompareTo((object)HexBytes.Empty)));
    }

    [TestMethod]
    public void BoxedCompareTo_AnyValueIsGreaterThanNull_AndOtherTypesThrow()
    {
        var a = H(1, 2);

        Assert.IsGreaterThan(0, a.CompareTo((object?)null));
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => a.CompareTo("nope"));
        Assert.AreEqual("obj", error.ParamName);
        Assert.ThrowsExactly<ArgumentException>(() => a.CompareTo((object)new byte[] { 1, 2 }));
    }
}
