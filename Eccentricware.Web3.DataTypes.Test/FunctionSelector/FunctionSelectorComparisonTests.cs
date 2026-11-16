using System.Globalization;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Equality, ordering, operators and the interface-dispatched forms (IEquatable, IComparable, comparers, parsable and formattable).</summary>
[TestClass]
public sealed class FunctionSelectorComparisonTests
{
    // Values around the sign bit: a signed comparison would misorder 0x80000000 and 0xFFFFFFFF.
    private static readonly uint[] Edges = [0u, 1u, 0x7FFFFFFEu, 0x7FFFFFFFu, 0x80000000u, 0x80000001u, 0xFFFFFFFEu, 0xFFFFFFFFu];

    private static bool EqualsViaInterface<T>(T a, T b) where T : IEquatable<T> => a.Equals(b);
    private static int CompareViaInterface<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b);
    private static T ParseViaInterface<T>(string s) where T : IParsable<T> => T.Parse(s, CultureInfo.InvariantCulture);
    private static bool TryParseViaInterface<T>(string s, out T result) where T : IParsable<T>
    {
        bool ok = T.TryParse(s, CultureInfo.InvariantCulture, out T? parsed);
        result = parsed!;
        return ok;
    }
    private static T ParseSpanViaInterface<T>(ReadOnlySpan<char> s) where T : ISpanParsable<T> => T.Parse(s, null);
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
        foreach (uint x in Edges)
        foreach (uint y in Edges)
        {
            var a = new FunctionSelector(x);
            var b = new FunctionSelector(y);
            string pair = $"{x:x8} vs {y:x8}";

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
    public void Ordering_IsUnsigned_AndDiffersFromTheSignedView()
    {
        var low = new FunctionSelector(0x7FFFFFFFu);
        var high = new FunctionSelector(0x80000000u);

        Assert.IsTrue(low < high);
        Assert.IsGreaterThan(high.AsInt32, low.AsInt32, "documents that AsInt32 ordering is the reverse");
    }

    [TestMethod]
    public void Sorting_SelectorsMatchesSortingTheUnsignedValues_ViaEveryComparisonRoute()
    {
        var rng = new Random(11);
        uint[] values = [.. Edges, .. Enumerable.Range(0, 300).Select(_ => (uint)rng.NextInt64(0, uint.MaxValue + 1L))];
        uint[] expected = [.. values.Order()];
        FunctionSelector[] source = values.Select(v => new FunctionSelector(v)).ToArray();

        FunctionSelector[] arraySorted = [.. source];
        Array.Sort(arraySorted);
        var listSorted = new List<FunctionSelector>(source);
        listSorted.Sort();
        FunctionSelector[] viaComparer = [.. source.OrderBy(s => s, Comparer<FunctionSelector>.Default)];
        object[] boxed = source.Select(s => (object)s).ToArray();
        Array.Sort(boxed);

        CollectionAssert.AreEqual(expected, arraySorted.Select(s => s.Value).ToArray());
        CollectionAssert.AreEqual(expected, listSorted.Select(s => s.Value).ToArray());
        CollectionAssert.AreEqual(expected, viaComparer.Select(s => s.Value).ToArray());
        CollectionAssert.AreEqual(expected, boxed.Select(s => ((FunctionSelector)s).Value).ToArray());
    }

    // ----------------- IComparable (boxed) -----------------

    [TestMethod]
    public void CompareToObject_Null_IsGreater_AndBoxedSelectorsCompareByValue()
    {
        IComparable a = new FunctionSelector(5);

        Assert.AreEqual(1, a.CompareTo(null));
        Assert.AreEqual(0, a.CompareTo(new FunctionSelector(5)));
        Assert.IsLessThan(0, a.CompareTo(new FunctionSelector(6)));
        Assert.IsGreaterThan(0, a.CompareTo(new FunctionSelector(4)));
    }

    [TestMethod]
    public void CompareToObject_OtherTypes_ThrowArgumentException_WithParameterNameAndTypeName()
    {
        IComparable a = new FunctionSelector(5);

        // The raw underlying value, a signed value, a string and a different 4/8-byte type are all rejected.
        foreach (object other in new object[] { 5u, 5, 5L, "0x00000005", new InstructionDiscriminator(5), new byte[] { 0, 0, 0, 5 } })
        {
            var ex = Assert.ThrowsExactly<ArgumentException>(() => a.CompareTo(other), other.GetType().Name);
            Assert.AreEqual("obj", ex.ParamName);
            StringAssert.Contains(ex.Message, nameof(FunctionSelector));
        }
    }

    [TestMethod]
    public void EqualsObject_OnlyBoxedSelectorsWithTheSameValueMatch()
    {
        var a = new FunctionSelector(5);

        Assert.IsTrue(a.Equals((object)new FunctionSelector(5)));
        Assert.IsFalse(a.Equals((object)new FunctionSelector(6)));
        Assert.IsFalse(a.Equals((object?)null));
        Assert.IsFalse(a.Equals((object)5u), "same numeric value, different type");
        Assert.IsFalse(a.Equals((object)"0x00000005"));
        Assert.IsFalse(a.Equals((object)new InstructionDiscriminator(5)));
    }

    // ----------------- interface dispatch -----------------

    [TestMethod]
    public void GenericConstraints_DispatchToTheSelectorImplementations()
    {
        Assert.IsTrue(EqualsViaInterface(FunctionSelector.Erc20.Transfer, new FunctionSelector(0xa9059cbbu)));
        Assert.IsFalse(EqualsViaInterface(FunctionSelector.Erc20.Transfer, FunctionSelector.Erc20.Approve));
        Assert.IsGreaterThan(0, CompareViaInterface(new FunctionSelector(0x80000000u), new FunctionSelector(0x7FFFFFFFu)));
        Assert.AreEqual(0, CompareViaInterface(FunctionSelector.Zero, default));
    }

    [TestMethod]
    public void EqualityComparerDefault_UsesSelectorEquality_AndAHashConsistentWithIt()
    {
        var comparer = EqualityComparer<FunctionSelector>.Default;
        var a = new FunctionSelector(0xdeadbeef);
        var b = new FunctionSelector(0xdeadbeef);

        Assert.IsTrue(comparer.Equals(a, b));
        Assert.AreEqual(comparer.GetHashCode(a), comparer.GetHashCode(b));
        Assert.IsFalse(comparer.Equals(a, new FunctionSelector(0xdeadbeee)));
    }

    [TestMethod]
    public void HashTables_FindEverySelector_AndRejectAbsentOnes()
    {
        var rng = new Random(77);
        var present = new HashSet<uint>();
        while (present.Count < 5000)
            present.Add((uint)rng.NextInt64(0, uint.MaxValue + 1L));

        var set = new HashSet<FunctionSelector>(present.Select(v => new FunctionSelector(v)));
        var map = present.ToDictionary(v => new FunctionSelector(v), v => v);

        Assert.HasCount(present.Count, set);
        foreach (uint v in present)
        {
            Assert.IsTrue(set.Contains(new FunctionSelector(v)));
            Assert.AreEqual(v, map[new FunctionSelector(v)]);
        }

        for (int i = 0; i < 2000; i++)
        {
            uint probe = (uint)rng.NextInt64(0, uint.MaxValue + 1L);
            Assert.AreEqual(present.Contains(probe), set.Contains(new FunctionSelector(probe)));
        }
    }

    [TestMethod]
    public void ParsableInterfaces_DispatchToTheSelectorParsers()
    {
        Assert.AreEqual(FunctionSelector.Erc20.Transfer, ParseViaInterface<FunctionSelector>("0xa9059cbb"));
        Assert.AreEqual(FunctionSelector.Erc20.Transfer, ParseSpanViaInterface<FunctionSelector>("A9059CBB"));
        Assert.IsTrue(TryParseViaInterface<FunctionSelector>(" 0xa9059cbb ", out var parsed));
        Assert.AreEqual(FunctionSelector.Erc20.Transfer, parsed);
        Assert.IsTrue(TryParseSpanViaInterface<FunctionSelector>("0x095ea7b3", out parsed));
        Assert.AreEqual(FunctionSelector.Erc20.Approve, parsed);

        Assert.IsFalse(TryParseViaInterface<FunctionSelector>("0xa9059c", out parsed));
        Assert.AreEqual(FunctionSelector.Zero, parsed);
        Assert.IsFalse(TryParseSpanViaInterface<FunctionSelector>("", out parsed));
        Assert.ThrowsExactly<FormatException>(() => ParseViaInterface<FunctionSelector>("0xa9059c"));
        Assert.ThrowsExactly<FormatException>(() => ParseSpanViaInterface<FunctionSelector>("zzzzzzzz"));
        Assert.ThrowsExactly<ArgumentNullException>(() => ParseViaInterface<FunctionSelector>(null!));
        Assert.IsFalse(TryParseViaInterface<FunctionSelector>(null!, out parsed));
    }

    [TestMethod]
    public void FormattableInterfaces_DispatchToTheSelectorFormatters_AndIgnoreTheProvider()
    {
        var selector = new FunctionSelector(0xa9059cbbu);
        IFormattable formattable = selector;

        Assert.AreEqual("0xa9059cbb", formattable.ToString(null, null));
        Assert.AreEqual("A9059CBB", formattable.ToString("X", CultureInfo.GetCultureInfo("tr-TR")));
        Assert.AreEqual("0xA9059CBB", formattable.ToString("0X", CultureInfo.InvariantCulture));
        Assert.ThrowsExactly<FormatException>(() => formattable.ToString("G", null));

        ISpanFormattable spanFormattable = selector;
        Span<char> chars = stackalloc char[10];
        Assert.IsTrue(spanFormattable.TryFormat(chars, out int written, "x", null));
        Assert.AreEqual("a9059cbb", chars[..written].ToString());

        IUtf8SpanFormattable utf8Formattable = selector;
        Span<byte> utf8 = stackalloc byte[10];
        Assert.IsTrue(utf8Formattable.TryFormat(utf8, out written, "0X", null));
        Assert.AreEqual("0xA9059CBB", System.Text.Encoding.ASCII.GetString(utf8[..written]));
    }

    [TestMethod]
    public void CompositeFormatting_AndInterpolation_UseTheSelectorFormats()
    {
        var selector = new FunctionSelector(0xa9059cbbu);

        Assert.AreEqual("0xa9059cbb", $"{selector}");
        Assert.AreEqual("a9059cbb", $"{selector:x}");
        Assert.AreEqual("A9059CBB", $"{selector:X}");
        Assert.AreEqual("0xA9059CBB", string.Format(CultureInfo.InvariantCulture, "{0:0X}", selector));
        Assert.AreEqual("[0xa9059cbb ]", string.Format(CultureInfo.InvariantCulture, "[{0,-11}]", selector));
    }
}
