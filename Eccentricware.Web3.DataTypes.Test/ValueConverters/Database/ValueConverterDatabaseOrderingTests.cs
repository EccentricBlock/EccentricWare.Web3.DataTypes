using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// <c>ORDER BY</c> and range filters on every ordered converter, against samples whose true order is known by construction
/// (zero, limb and sign boundaries, prefixes of other byte strings), inserted in reverse order.
/// </summary>
[TestClass]
public sealed class ValueConverterDatabaseOrderingTests
{
    private static void Run(string name, string method) => ConverterCases.Get(name).Invoke(typeof(ValueConverterDatabaseOrderingTests), method);

    [TestMethod]
    [DynamicData(nameof(ConverterCases.OrderedNames), typeof(ConverterCases))]
    public void OrderBy_FollowsTheValue_InBothDirections(string name) => Run(name, nameof(OrderByImpl));

    private static void OrderByImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        db.SeedRows(samples, Enumerable.Range(0, samples.Length).Reverse());

        using CaseDbContext<T> context = db.NewContext();
        int[] ascending = Enumerable.Range(1, samples.Length).ToArray();

        CollectionAssert.AreEqual(ascending, context.Rows.OrderBy(r => r.Value).Select(r => r.Id).ToArray(), "ascending");
        CollectionAssert.AreEqual(ascending.Reverse().ToArray(), context.Rows.OrderByDescending(r => r.Value).Select(r => r.Id).ToArray(), "descending");
    }

    [TestMethod]
    [DynamicData(nameof(ConverterCases.OrderedNames), typeof(ConverterCases))]
    public void RangeFilters_SelectExactlyTheRowsBeyondEachPivot(string name) => Run(name, nameof(RangeImpl));

    private static void RangeImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        db.SeedRows(samples, Enumerable.Range(0, samples.Length).Reverse());
        int count = samples.Length;

        using CaseDbContext<T> context = db.NewContext();
        int[] Ids(Expression<Func<CaseRow<T>, bool>> predicate) => context.Rows.Where(predicate).Select(r => r.Id).OrderBy(id => id).ToArray();
        int[] Range(int firstId, int lastId) => lastId < firstId ? [] : Enumerable.Range(firstId, lastId - firstId + 1).ToArray();

        for (int i = 0; i < count; i++)
        {
            T pivot = c.CreateSamples()[i];
            int pivotId = i + 1;
            CollectionAssert.AreEqual(Range(pivotId + 1, count), Ids(RowPredicates.Value(ExpressionType.GreaterThan, pivot)), $"> sample {i}");
            CollectionAssert.AreEqual(Range(pivotId, count), Ids(RowPredicates.Value(ExpressionType.GreaterThanOrEqual, pivot)), $">= sample {i}");
            CollectionAssert.AreEqual(Range(1, pivotId - 1), Ids(RowPredicates.Value(ExpressionType.LessThan, pivot)), $"< sample {i}");
            CollectionAssert.AreEqual(Range(1, pivotId), Ids(RowPredicates.Value(ExpressionType.LessThanOrEqual, pivot)), $"<= sample {i}");
        }

        // A pivot in the middle splits the rows into two non-empty sets, so a threshold that selects nothing or everything fails.
        int middle = Ids(RowPredicates.Value(ExpressionType.GreaterThan, samples[2])).Length;
        Assert.IsGreaterThan(0, middle);
        Assert.IsLessThan(count - 1, middle);
    }

    [TestMethod]
    [DynamicData(nameof(ConverterCases.OrderedNames), typeof(ConverterCases))]
    public void BetweenTwoValues_SelectsTheRowsInsideTheInterval(string name) => Run(name, nameof(BetweenImpl));

    private static void BetweenImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        db.SeedRows(samples, Enumerable.Range(0, samples.Length).Reverse());

        using CaseDbContext<T> context = db.NewContext();
        int[] ids = context.Rows
            .Where(RowPredicates.Value(ExpressionType.GreaterThanOrEqual, samples[1]))
            .Where(RowPredicates.Value(ExpressionType.LessThan, samples[^1]))
            .Select(r => r.Id).OrderBy(id => id).ToArray();

        CollectionAssert.AreEqual(Enumerable.Range(2, samples.Length - 2).ToArray(), ids);
    }
}
