using System.Globalization;
using System.Text;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Equality, ordering, operators and the interface-dispatched forms (IEquatable, IComparable, comparers, parsable and formattable).</summary>
[TestClass]
public sealed class InstructionDiscriminatorComparisonTests
{
    private const ulong Anchor = 0xafaf6d1f0d989bedUL;

    // Values around the sign bit and the 32-bit boundary: a signed or truncating comparison would misorder them.
    private static readonly ulong[] Edges =
    [
        0UL, 1UL, 0xFFFFFFFFUL, 0x100000000UL, 0x7FFFFFFFFFFFFFFEUL, 0x7FFFFFFFFFFFFFFFUL,
        0x8000000000000000UL, 0x8000000000000001UL, 0xFFFFFFFF00000000UL, 0xFFFFFFFFFFFFFFFEUL, ulong.MaxValue,
    ];

    private static bool EqualsViaInterface<T>(T a, T b) where T : IEquatable<T> => a.Equals(b);
    private static int CompareViaInterface<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b);
    private static T ParseViaInterface<T>(string s) where T : IParsable<T> => T.Parse(s, CultureInfo.InvariantCulture);
    private static T ParseSpanViaInterface<T>(ReadOnlySpan<char> s) where T : ISpanParsable<T> => T.Parse(s, null);

    private static bool TryParseViaInterface<T>(string s, out T result) where T : IParsable<T>
    {
        bool ok = T.TryParse(s, CultureInfo.InvariantCulture, out T? parsed);
        result = parsed!;
        return ok;
    }

    private static bool TryParseSpanViaInterface<T>(ReadOnlySpan<char> s, out T result) where T : ISpanParsable<T>
    {
        bool ok = T.TryParse(s, null, out T? parsed);
        result = parsed!;
        return ok;
    }

    // ----------------- operator truth tables (every ordered pair, both directions) -----------------

    [TestMethod]
    public void Operators_MatchUnsignedIntegerComparison_ForEveryOrderedPairOfEdgeValues()
    {
        foreach (ulong x in Edges)
        foreach (ulong y in Edges)
        {
            var a = new InstructionDiscriminator(x);
            var b = new InstructionDiscriminator(y);
            string pair = $"{x:x16} vs {y:x16}";

            Assert.AreEqual(x == y, a == b, "== " + pair);
            Assert.AreEqual(x != y, a != b, "!= " + pair);
            Assert.AreEqual(x < y, a < b, "< " + pair);
            Assert.AreEqual(x > y, a > b, "> " + pair);
            Assert.AreEqual(x <= y, a <= b, "<= " + pair);
            Assert.AreEqual(x >= y, a >= b, ">= " + pair);

            Assert.AreEqual(x == y, a.Equals(b), "Equals " + pair);
            Assert.AreEqual(x == y, a.Equals((object)b), "Equals(object) " + pair);
            Assert.AreEqual(Math.Sign(x.CompareTo(y)), Math.Sign(a.CompareTo(b)), "CompareTo " + pair);
            Assert.AreEqual(Math.Sign(x.CompareTo(y)), Math.Sign(a.CompareTo((object)b)), "CompareTo(object) " + pair);
            Assert.AreEqual(a.CompareTo(b), -b.CompareTo(a), "antisymmetry " + pair);
        }
    }

    [TestMethod]
    public void Ordering_IsUnsigned_AndComparesAllSixtyFourBits()
    {
        Assert.IsTrue(new InstructionDiscriminator(0x7FFFFFFFFFFFFFFFUL) < new InstructionDiscriminator(0x8000000000000000UL));
        Assert.IsTrue(new InstructionDiscriminator(0x00000000FFFFFFFFUL) < new InstructionDiscriminator(0x0000000100000000UL), "high half decides");
        Assert.IsTrue(new InstructionDiscriminator(0x0000000100000000UL) < new InstructionDiscriminator(0x0000000100000001UL), "low half breaks ties");
        Assert.IsGreaterThan(new InstructionDiscriminator(0x8000000000000000UL).AsInt64, new InstructionDiscriminator(0x7FFFFFFFFFFFFFFFUL).AsInt64,
            "documents that AsInt64 ordering is the reverse");
    }

    [TestMethod]
    public void Sorting_MatchesSortingTheUnsignedValues_ViaEveryComparisonRoute()
    {
        var rng = new Random(12);
        ulong[] values = [.. Edges, .. Enumerable.Range(0, 300).Select(_ => (ulong)rng.NextInt64() ^ ((ulong)rng.NextInt64() << 1))];
        ulong[] expected = [.. values.Order()];
        InstructionDiscriminator[] source = values.Select(v => new InstructionDiscriminator(v)).ToArray();

        InstructionDiscriminator[] arraySorted = [.. source];
        Array.Sort(arraySorted);
        var listSorted = new List<InstructionDiscriminator>(source);
        listSorted.Sort();
        InstructionDiscriminator[] viaComparer = [.. source.OrderBy(s => s, Comparer<InstructionDiscriminator>.Default)];
        object[] boxed = source.Select(s => (object)s).ToArray();
        Array.Sort(boxed);

        CollectionAssert.AreEqual(expected, arraySorted.Select(s => s.Value).ToArray());
        CollectionAssert.AreEqual(expected, listSorted.Select(s => s.Value).ToArray());
        CollectionAssert.AreEqual(expected, viaComparer.Select(s => s.Value).ToArray());
        CollectionAssert.AreEqual(expected, boxed.Select(s => ((InstructionDiscriminator)s).Value).ToArray());
    }

    // ----------------- IComparable (boxed) -----------------

    [TestMethod]
    public void CompareToObject_Null_IsGreater_AndBoxedValuesCompareByValue()
    {
        IComparable a = new InstructionDiscriminator(5);

        Assert.AreEqual(1, a.CompareTo(null));
        Assert.AreEqual(0, a.CompareTo(new InstructionDiscriminator(5)));
        Assert.IsLessThan(0, a.CompareTo(new InstructionDiscriminator(6)));
        Assert.IsGreaterThan(0, a.CompareTo(new InstructionDiscriminator(4)));
    }

    [TestMethod]
    public void CompareToObject_OtherTypes_ThrowArgumentException_WithParameterNameAndTypeName()
    {
        IComparable a = new InstructionDiscriminator(5);

        // The raw underlying value, signed values, a string and the sibling 4-byte type are all rejected.
        foreach (object other in new object[] { 5UL, 5L, 5, "0x0000000000000005", new FunctionSelector(5), new byte[8] })
        {
            var ex = Assert.ThrowsExactly<ArgumentException>(() => a.CompareTo(other), other.GetType().Name);
            Assert.AreEqual("obj", ex.ParamName);
            StringAssert.Contains(ex.Message, nameof(InstructionDiscriminator));
        }
    }

    [TestMethod]
    public void EqualsObject_OnlyBoxedDiscriminatorsWithTheSameValueMatch()
    {
        var a = new InstructionDiscriminator(5);

        Assert.IsTrue(a.Equals((object)new InstructionDiscriminator(5)));
        Assert.IsFalse(a.Equals((object)new InstructionDiscriminator(6)));
        Assert.IsFalse(a.Equals((object?)null));
        Assert.IsFalse(a.Equals((object)5UL), "same numeric value, different type");
        Assert.IsFalse(a.Equals((object)"0x0000000000000005"));
        Assert.IsFalse(a.Equals((object)new FunctionSelector(5)));
    }

    // ----------------- interface dispatch -----------------

    [TestMethod]
    public void GenericConstraints_DispatchToTheDiscriminatorImplementations()
    {
        Assert.IsTrue(EqualsViaInterface(new InstructionDiscriminator(Anchor), new InstructionDiscriminator(Anchor)));
        Assert.IsFalse(EqualsViaInterface(new InstructionDiscriminator(Anchor), new InstructionDiscriminator(Anchor + 1)));
        Assert.IsGreaterThan(0, CompareViaInterface(new InstructionDiscriminator(0x8000000000000000UL), new InstructionDiscriminator(0x7FFFFFFFFFFFFFFFUL)));
        Assert.AreEqual(0, CompareViaInterface(InstructionDiscriminator.Zero, default));
    }

    [TestMethod]
    public void EqualityComparerDefault_UsesDiscriminatorEquality_AndAHashConsistentWithIt()
    {
        var comparer = EqualityComparer<InstructionDiscriminator>.Default;
        var a = new InstructionDiscriminator(Anchor);
        var b = new InstructionDiscriminator(Anchor);

        Assert.IsTrue(comparer.Equals(a, b));
        Assert.AreEqual(comparer.GetHashCode(a), comparer.GetHashCode(b));
        Assert.IsFalse(comparer.Equals(a, new InstructionDiscriminator(Anchor ^ 1)));
        Assert.IsFalse(comparer.Equals(a, new InstructionDiscriminator(Anchor ^ 0x100000000UL)), "the high half participates");
    }

    [TestMethod]
    public void HashTables_FindEveryDiscriminator_AndRejectAbsentOnes()
    {
        var rng = new Random(78);
        var present = new HashSet<ulong>();
        while (present.Count < 5000)
            present.Add((ulong)rng.NextInt64() ^ ((ulong)rng.NextInt64() << 1));

        var set = new HashSet<InstructionDiscriminator>(present.Select(v => new InstructionDiscriminator(v)));
        var map = present.ToDictionary(v => new InstructionDiscriminator(v), v => v);

        Assert.HasCount(present.Count, set);
        foreach (ulong v in present)
        {
            Assert.IsTrue(set.Contains(new InstructionDiscriminator(v)));
            Assert.AreEqual(v, map[new InstructionDiscriminator(v)]);
        }

        for (int i = 0; i < 2000; i++)
        {
            ulong probe = (ulong)rng.NextInt64() ^ ((ulong)rng.NextInt64() << 1);
            Assert.AreEqual(present.Contains(probe), set.Contains(new InstructionDiscriminator(probe)));
        }
    }

    [TestMethod]
    public void ParsableInterfaces_DispatchToTheDiscriminatorParsers()
    {
        var anchor = new InstructionDiscriminator(Anchor);

        Assert.AreEqual(anchor, ParseViaInterface<InstructionDiscriminator>("0xafaf6d1f0d989bed"));
        Assert.AreEqual(anchor, ParseSpanViaInterface<InstructionDiscriminator>("AFAF6D1F0D989BED"));
        Assert.IsTrue(TryParseViaInterface<InstructionDiscriminator>(" 0xafaf6d1f0d989bed ", out var parsed));
        Assert.AreEqual(anchor, parsed);
        Assert.IsTrue(TryParseSpanViaInterface<InstructionDiscriminator>("0x0102030405060708", out parsed));
        Assert.AreEqual(new InstructionDiscriminator(0x0102030405060708UL), parsed);

        Assert.IsFalse(TryParseViaInterface<InstructionDiscriminator>("0xafaf6d1f0d989be", out parsed));
        Assert.AreEqual(InstructionDiscriminator.Zero, parsed);
        Assert.IsFalse(TryParseSpanViaInterface<InstructionDiscriminator>("", out parsed));
        Assert.ThrowsExactly<FormatException>(() => ParseViaInterface<InstructionDiscriminator>("0xafaf6d1f0d989be"));
        Assert.ThrowsExactly<FormatException>(() => ParseSpanViaInterface<InstructionDiscriminator>("zzzzzzzzzzzzzzzz"));
        Assert.ThrowsExactly<ArgumentNullException>(() => ParseViaInterface<InstructionDiscriminator>(null!));
        Assert.IsFalse(TryParseViaInterface<InstructionDiscriminator>(null!, out parsed));
    }

    [TestMethod]
    public void FormattableInterfaces_DispatchToTheDiscriminatorFormatters_AndIgnoreTheProvider()
    {
        var discriminator = new InstructionDiscriminator(Anchor);
        IFormattable formattable = discriminator;

        Assert.AreEqual("0xafaf6d1f0d989bed", formattable.ToString(null, null));
        Assert.AreEqual("AFAF6D1F0D989BED", formattable.ToString("X", CultureInfo.GetCultureInfo("tr-TR")));
        Assert.AreEqual("0xAFAF6D1F0D989BED", formattable.ToString("0X", CultureInfo.InvariantCulture));
        Assert.ThrowsExactly<FormatException>(() => formattable.ToString("G", null));

        ISpanFormattable spanFormattable = discriminator;
        Span<char> chars = stackalloc char[18];
        Assert.IsTrue(spanFormattable.TryFormat(chars, out int written, "x", null));
        Assert.AreEqual("afaf6d1f0d989bed", chars[..written].ToString());

        IUtf8SpanFormattable utf8Formattable = discriminator;
        Span<byte> utf8 = stackalloc byte[18];
        Assert.IsTrue(utf8Formattable.TryFormat(utf8, out written, "0X", null));
        Assert.AreEqual("0xAFAF6D1F0D989BED", Encoding.ASCII.GetString(utf8[..written]));
    }

    [TestMethod]
    public void CompositeFormatting_AndInterpolation_UseTheDiscriminatorFormats()
    {
        var discriminator = new InstructionDiscriminator(Anchor);

        Assert.AreEqual("0xafaf6d1f0d989bed", $"{discriminator}");
        Assert.AreEqual("afaf6d1f0d989bed", $"{discriminator:x}");
        Assert.AreEqual("AFAF6D1F0D989BED", $"{discriminator:X}");
        Assert.AreEqual("0xAFAF6D1F0D989BED", string.Format(CultureInfo.InvariantCulture, "{0:0X}", discriminator));
        Assert.AreEqual("[0xafaf6d1f0d989bed  ]", string.Format(CultureInfo.InvariantCulture, "[{0,-20}]", discriminator));
    }
}
