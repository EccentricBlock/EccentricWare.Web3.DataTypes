using Microsoft.EntityFrameworkCore;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Change detection, update, delete, set-based update/delete and concurrency tokens on converted columns.</summary>
[TestClass]
public sealed class ValueConverterDatabaseChangeTrackingTests
{
    private static void Run(string name, string method) => ConverterCases.Get(name).Invoke(typeof(ValueConverterDatabaseChangeTrackingTests), method);

    // ----------------- change detection -----------------

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void LoadedRows_AreNotModified_EvenWhenAssignedAnEqualButDistinctValue(string name) => Run(name, nameof(NoSpuriousChangeImpl));

    private static void NoSpuriousChangeImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        db.SeedRows(c.CreateSamples());

        using CaseDbContext<T> context = db.NewContext();
        List<CaseRow<T>> rows = context.Rows.OrderBy(r => r.Id).ToList();
        context.ChangeTracker.DetectChanges();
        Assert.IsFalse(context.ChangeTracker.HasChanges(), "freshly loaded");

        T[] equalCopies = c.CreateSamples();
        for (int i = 0; i < rows.Count; i++)
            rows[i].Value = equalCopies[i];
        context.ChangeTracker.DetectChanges();

        Assert.IsFalse(context.ChangeTracker.HasChanges(), "assigned equal values");
        Assert.AreEqual(0, context.SaveChanges());
    }

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void ChangingTheValue_MarksTheRowModified_AndPersistsIt(string name) => Run(name, nameof(UpdateImpl));

    private static void UpdateImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        db.SeedRows(c.CreateSamples());

        using (CaseDbContext<T> context = db.NewContext())
        {
            CaseRow<T> row = context.Rows.Single(r => r.Id == 2);
            row.Value = c.CreateAbsent();

            Assert.AreEqual(EntityState.Modified, context.Entry(row).State);
            Assert.AreEqual(1, context.SaveChanges());
        }

        using CaseDbContext<T> check = db.NewContext();
        Assert.AreEqual(c.CreateAbsent(), check.Rows.Single(r => r.Id == 2).Value);
        Assert.AreEqual(c.CreateSamples()[0], check.Rows.Single(r => r.Id == 1).Value, "other rows are untouched");
    }

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void RemovingARow_DeletesIt(string name) => Run(name, nameof(DeleteImpl));

    private static void DeleteImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        db.SeedRows(samples);

        using (CaseDbContext<T> context = db.NewContext())
        {
            context.Rows.Remove(context.Rows.Single(r => r.Id == 3));
            Assert.AreEqual(1, context.SaveChanges());
        }

        using CaseDbContext<T> check = db.NewContext();
        CollectionAssert.AreEqual(Enumerable.Range(1, samples.Length).Where(id => id != 3).ToArray(), check.Rows.OrderBy(r => r.Id).Select(r => r.Id).ToArray());
    }

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void BatchInsertUpdateDelete_LeavesExactlyTheExpectedRows(string name) => Run(name, nameof(BatchImpl));

    private static void BatchImpl<T>(ConverterCase<T> c) where T : struct
    {
        const int Count = 300;
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        T absent = c.CreateAbsent();
        var expected = new Dictionary<int, T>();

        using (CaseDbContext<T> context = db.NewContext())
        {
            for (int id = 1; id <= Count; id++)
            {
                context.Rows.Add(new CaseRow<T> { Id = id, Value = samples[id % samples.Length] });
                expected[id] = samples[id % samples.Length];
            }
            Assert.AreEqual(Count, context.SaveChanges());
        }

        using (CaseDbContext<T> context = db.NewContext())
        {
            foreach (CaseRow<T> row in context.Rows.ToList())
            {
                if (row.Id % 5 == 0)
                {
                    context.Rows.Remove(row);
                    expected.Remove(row.Id);
                }
                else if (row.Id % 3 == 0)
                {
                    row.Value = absent;
                    expected[row.Id] = absent;
                }
            }
            context.SaveChanges();
        }

        using CaseDbContext<T> check = db.NewContext();
        Dictionary<int, T> actual = check.Rows.AsNoTracking().ToDictionary(r => r.Id, r => r.Value);
        Assert.HasCount(expected.Count, actual);
        foreach ((int id, T value) in expected)
            Assert.AreEqual(value, actual[id], $"row {id}");
    }

    // ----------------- set-based update and delete -----------------

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void ExecuteUpdateAndExecuteDelete_FilterAndAssignConvertedValues(string name) => Run(name, nameof(SetBasedImpl));

    private static void SetBasedImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        db.SeedRows(samples);
        T absent = c.CreateAbsent();

        using CaseDbContext<T> context = db.NewContext();
        T target = c.CreateSamples()[1];

        int updated = context.Rows.Where(RowPredicates.Value(System.Linq.Expressions.ExpressionType.Equal, target))
            .ExecuteUpdate(s => s.SetProperty(r => r.Value, absent));
        Assert.AreEqual(1, updated);
        Assert.AreEqual(absent, context.Rows.AsNoTracking().Single(r => r.Id == 2).Value);

        int deleted = context.Rows.Where(RowPredicates.Value(System.Linq.Expressions.ExpressionType.Equal, c.CreateSamples()[0])).ExecuteDelete();
        Assert.AreEqual(1, deleted);
        Assert.AreEqual(samples.Length - 1, context.Rows.Count());
    }

    // ----------------- concurrency token -----------------

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void ConcurrencyToken_DetectsAnExternalChange_AndAllowsAnUnrelatedOne(string name) => Run(name, nameof(ConcurrencyImpl));

    private static void ConcurrencyImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        using (CaseDbContext<T> seed = db.NewContext())
        {
            seed.Tokens.Add(new CaseTokenRow<T> { Id = 1, Token = samples[0], Payload = "seed" });
            seed.Tokens.Add(new CaseTokenRow<T> { Id = 2, Token = samples[0], Payload = "seed" });
            seed.SaveChanges();
        }

        using CaseDbContext<T> first = db.NewContext();
        using CaseDbContext<T> second = db.NewContext();
        CaseTokenRow<T> conflicted = first.Tokens.Single(t => t.Id == 1);
        CaseTokenRow<T> unrelated = first.Tokens.Single(t => t.Id == 2);
        CaseTokenRow<T> external = second.Tokens.Single(t => t.Id == 1);

        // The other writer changes the token of row 1 only.
        external.Token = c.CreateSamples()[1];
        Assert.AreEqual(1, second.SaveChanges());

        // Row 2's token is unchanged in the database, so the first writer's update still matches.
        unrelated.Payload = "changed";
        Assert.AreEqual(1, first.SaveChanges());

        conflicted.Payload = "changed too";
        Assert.ThrowsExactly<DbUpdateConcurrencyException>(() => first.SaveChanges());
    }

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void ConcurrencyToken_ChangedByTheSameContext_IsComparedAgainstTheOriginalValue(string name) => Run(name, nameof(OwnTokenChangeImpl));

    private static void OwnTokenChangeImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        using (CaseDbContext<T> seed = db.NewContext())
        {
            seed.Tokens.Add(new CaseTokenRow<T> { Id = 1, Token = c.CreateSamples()[0], Payload = "seed" });
            seed.SaveChanges();
        }

        using CaseDbContext<T> context = db.NewContext();
        CaseTokenRow<T> row = context.Tokens.Single();
        row.Token = c.CreateSamples()[2];
        Assert.AreEqual(1, context.SaveChanges());

        row.Token = c.CreateAbsent();
        Assert.AreEqual(1, context.SaveChanges(), "the new original value is the one just saved");

        using CaseDbContext<T> check = db.NewContext();
        Assert.AreEqual(c.CreateAbsent(), check.Tokens.Single().Token);
    }
}
