using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Query shapes over a converted column, for every converter against SQLite: boundary values, equality, <c>IN</c>,
/// aggregates, grouping, projection and nullable columns.
/// </summary>
[TestClass]
public sealed class ValueConverterDatabaseQueryTests
{
    private static void Run(string name, string method) => ConverterCases.Get(name).Invoke(typeof(ValueConverterDatabaseQueryTests), method);

    // ----------------- boundary values and equality -----------------

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void BoundaryValues_RoundTripThroughTheDatabase_AsDistinctRows(string name) => Run(name, nameof(BoundaryValuesImpl));

    private static void BoundaryValuesImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        // Insert in reverse so a column that happened to return rows in insertion order would not hide a problem.
        db.SeedRows(samples, Enumerable.Range(0, samples.Length).Reverse());

        using CaseDbContext<T> context = db.NewContext();
        T[] loaded = context.Rows.AsNoTracking().OrderBy(r => r.Id).Select(r => r.Value).ToArray();

        Assert.HasCount(samples.Length, loaded);
        for (int i = 0; i < samples.Length; i++)
            Assert.AreEqual(samples[i], loaded[i], $"{c.Name} sample {i}");
        Assert.HasCount(samples.Length, loaded.Distinct());
    }

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void EqualityAndInequality_SelectExactlyTheMatchingRows(string name) => Run(name, nameof(EqualityImpl));

    private static void EqualityImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        db.SeedRows(samples);

        using CaseDbContext<T> context = db.NewContext();
        for (int i = 0; i < samples.Length; i++)
        {
            T probe = c.CreateSamples()[i]; // an equal value that is a different object
            Assert.AreEqual(i + 1, context.Rows.Single(RowPredicates.Value(ExpressionType.Equal, probe)).Id, $"== sample {i}");
            Assert.AreEqual(samples.Length - 1, context.Rows.Count(RowPredicates.Value(ExpressionType.NotEqual, probe)), $"!= sample {i}");
        }

        Assert.AreEqual(0, context.Rows.Count(RowPredicates.Value(ExpressionType.Equal, c.CreateAbsent())));
    }

    // ----------------- IN / Contains -----------------

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void Contains_MatchesListedValues_IgnoresAbsentOnes_AndHandlesEmptyAndDuplicateLists(string name) => Run(name, nameof(ContainsImpl));

    private static void ContainsImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        db.SeedRows(c.CreateSamples());
        T[] samples = c.CreateSamples();

        using CaseDbContext<T> context = db.NewContext();
        int[] Ids(List<T> list) => context.Rows.Where(r => list.Contains(r.Value)).Select(r => r.Id).OrderBy(id => id).ToArray();

        CollectionAssert.AreEqual(new[] { 2 }, Ids([samples[1], c.CreateAbsent()]), "one present, one absent");
        CollectionAssert.AreEqual(Array.Empty<int>(), Ids([]), "empty list");
        CollectionAssert.AreEqual(new[] { 2 }, Ids([samples[1], samples[1]]), "duplicates in the list");
        CollectionAssert.AreEqual(new[] { 1, samples.Length }, Ids([samples[^1], samples[0]]), "first and last");
        CollectionAssert.AreEqual(Enumerable.Range(1, samples.Length).ToArray(), Ids([.. samples]), "every sample");
    }

    // ----------------- aggregates -----------------

    [TestMethod]
    [DynamicData(nameof(ConverterCases.OrderedNames), typeof(ConverterCases))]
    public void MinAndMax_ReturnTheSmallestAndLargestValue_ForOrderedColumns(string name) => Run(name, nameof(MinMaxImpl));

    private static void MinMaxImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        db.SeedRows(samples, Enumerable.Range(0, samples.Length).Reverse());

        using CaseDbContext<T> context = db.NewContext();

        Assert.AreEqual(samples[0], context.Rows.Min(r => r.Value), "Min");
        Assert.AreEqual(samples[^1], context.Rows.Max(r => r.Value), "Max");
    }

    [TestMethod]
    public void MinAndMax_OfHexBigInteger_AreNotNumeric_BecauseTheColumnIsNotOrdered()
    {
        // Documents the limitation in HexBigIntegerToBytesConverter's remarks: variable-length signed bytes do not sort
        // numerically, and EF still translates Max to the column. The exact SQL order is provider specific, so only the
        // difference from numeric order is asserted.
        var c = (ConverterCase<HexBigInteger>)ConverterCases.Get("HexBigInteger");
        long[] numbers = [5, 128, -1, 256, 255, -128, 0, 1_000_000];
        using var db = new CaseDatabase<HexBigInteger>(c);
        db.SeedRows(numbers.Select(n => new HexBigInteger(n)).ToArray());

        using CaseDbContext<HexBigInteger> context = db.NewContext();
        long[] sqlOrder = context.Rows.OrderBy(r => r.Id).ToList().Select(r => (long)r.Value.Value).ToArray();
        CollectionAssert.AreEqual(numbers, sqlOrder, "the data itself reads back unchanged");

        long[] orderedBySql = context.Rows.OrderBy(r => r.Value).AsEnumerable().Select(r => (long)r.Value.Value).ToArray();
        CollectionAssert.AreNotEqual(numbers.Order().ToArray(), orderedBySql, "ORDER BY on this column is not numeric order");
        Assert.AreNotEqual(1_000_000L, (long)context.Rows.Max(r => r.Value).Value, "Max is not the numeric maximum");
    }

    // ----------------- grouping and projection -----------------

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void DistinctAndGroupBy_UseTheConvertedColumn(string name) => Run(name, nameof(GroupingImpl));

    private static void GroupingImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        T[] stored = [samples[0], samples[0], samples[0], samples[1], samples[1], samples[2]];
        using (CaseDbContext<T> seed = db.NewContext())
        {
            for (int i = 0; i < stored.Length; i++)
                seed.Rows.Add(new CaseRow<T> { Id = i + 1, Value = stored[i] });
            seed.SaveChanges();
        }

        using CaseDbContext<T> context = db.NewContext();

        T[] distinct = context.Rows.Select(r => r.Value).Distinct().ToArray();
        Assert.HasCount(3, distinct);
        Assert.IsTrue(new HashSet<T>(distinct).SetEquals([samples[0], samples[1], samples[2]]));

        Dictionary<T, int> counts = context.Rows.GroupBy(r => r.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionary(x => x.Key, x => x.Count);
        Assert.HasCount(3, counts);
        Assert.AreEqual(3, counts[samples[0]]);
        Assert.AreEqual(2, counts[samples[1]]);
        Assert.AreEqual(1, counts[samples[2]]);
    }

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void Projection_ReturnsConvertedValues(string name) => Run(name, nameof(ProjectionImpl));

    private static void ProjectionImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        db.SeedRows(samples);

        using CaseDbContext<T> context = db.NewContext();

        CollectionAssert.AreEqual(samples, context.Rows.OrderBy(r => r.Id).Select(r => r.Value).ToArray());

        var pairs = context.Rows.OrderBy(r => r.Id).Select(r => new { r.Id, r.Value }).ToArray();
        Assert.HasCount(samples.Length, pairs);
        for (int i = 0; i < samples.Length; i++)
        {
            Assert.AreEqual(i + 1, pairs[i].Id);
            Assert.AreEqual(samples[i], pairs[i].Value);
        }

        Assert.AreEqual(samples[2], context.Rows.Where(r => r.Id == 3).Select(r => r.Value).Single());
    }

    // ----------------- nullable columns -----------------

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void NullableColumn_StoresNullDistinctFromTheZeroLikeValue(string name) => Run(name, nameof(NullableImpl));

    private static void NullableImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        using (CaseDbContext<T> seed = db.NewContext())
        {
            // Even ids carry sample i; odd ids stay null. samples[0] is the zero-like value (zero, empty, default).
            for (int i = 0; i < samples.Length; i++)
                seed.Rows.Add(new CaseRow<T> { Id = i + 1, Value = samples[i], Optional = i % 2 == 0 ? samples[i] : null });
            seed.SaveChanges();
        }

        using CaseDbContext<T> context = db.NewContext();
        int expectedNulls = Enumerable.Range(0, samples.Length).Count(i => i % 2 != 0);

        Assert.AreEqual(expectedNulls, context.Rows.Count(r => r.Optional == null));
        Assert.AreEqual(samples.Length - expectedNulls, context.Rows.Count(r => r.Optional != null));
        Assert.AreEqual(expectedNulls, Convert.ToInt32(db.Scalar("select count(*) from \"Rows\" where \"Optional\" is null")));

        CaseRow<T>[] loaded = context.Rows.AsNoTracking().OrderBy(r => r.Id).ToArray();
        for (int i = 0; i < samples.Length; i++)
        {
            if (i % 2 == 0)
                Assert.AreEqual(samples[i], loaded[i].Optional, $"row {i + 1}");
            else
                Assert.IsNull(loaded[i].Optional, $"row {i + 1}");
        }

        Assert.AreEqual(3, context.Rows.Single(RowPredicates.OptionalEquals(samples[2])).Id);
        Assert.AreEqual(0, context.Rows.Count(RowPredicates.OptionalEquals(samples[1])), "the value stored only in the non-null column of an odd row");
    }

    // ----------------- rows written by other tools -----------------

    [TestMethod]
    public void HexBigInteger_NonMinimalAndEmptyStoredBytes_ReadAsTheirNumber()
    {
        var c = (ConverterCase<HexBigInteger>)ConverterCases.Get("HexBigInteger");
        using var db = new CaseDatabase<HexBigInteger>(c);
        // Stored byte strings and the number each means as signed big-endian two's complement.
        (int Id, byte[] Stored, long Expected)[] rows = [(1, [], 0), (2, [0x00], 0), (3, [0x00, 0x01], 1), (4, [0xff, 0xff], -1), (5, [0x00, 0xff], 255)];
        foreach ((int id, byte[] stored, _) in rows)
            db.Execute("insert into \"Rows\" (\"Id\", \"Value\", \"Optional\") values (@id, @value, null)", ("@id", id), ("@value", stored));

        using CaseDbContext<HexBigInteger> context = db.NewContext();
        Dictionary<int, HexBigInteger> loaded = context.Rows.AsNoTracking().ToDictionary(r => r.Id, r => r.Value);

        foreach ((int id, _, long expected) in rows)
            Assert.AreEqual(new HexBigInteger(expected), loaded[id], $"row {id}");
    }

    [TestMethod]
    public void HexBytesText_RowsWrittenByAnotherTool_AreReadLeniently()
    {
        var c = (ConverterCase<HexBytes>)ConverterCases.Get("HexBytesText");
        using var db = new CaseDatabase<HexBytes>(c);
        (int Id, string Stored)[] rows = [(1, "0xDEADBEEF"), (2, "DEADBEEF"), (3, " 0xdeadbeef "), (4, "0xabc"), (5, "")];
        foreach ((int id, string stored) in rows)
            db.Execute("insert into \"Rows\" (\"Id\", \"Value\", \"Optional\") values (@id, @value, null)", ("@id", id), ("@value", stored));

        using CaseDbContext<HexBytes> context = db.NewContext();
        Dictionary<int, HexBytes> loaded = context.Rows.AsNoTracking().ToDictionary(r => r.Id, r => r.Value);

        HexBytes deadbeef = new([0xde, 0xad, 0xbe, 0xef]);
        Assert.AreEqual(deadbeef, loaded[1]);
        Assert.AreEqual(deadbeef, loaded[2]);
        Assert.AreEqual(deadbeef, loaded[3]);
        Assert.AreEqual(new HexBytes([0x0a, 0xbc]), loaded[4]);
        Assert.AreEqual(HexBytes.Empty, loaded[5]);
    }
}
