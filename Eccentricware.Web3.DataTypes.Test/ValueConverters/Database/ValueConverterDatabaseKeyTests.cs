using Microsoft.EntityFrameworkCore;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Converted columns as primary key, foreign key and unique index: identity map, lookup, and constraint violations.</summary>
[TestClass]
public sealed class ValueConverterDatabaseKeyTests
{
    private static void Run(string name, string method) => ConverterCases.Get(name).Invoke(typeof(ValueConverterDatabaseKeyTests), method);

    // ----------------- primary key -----------------

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void PrimaryKey_FindUsesEqualButDistinctKeys_AndTheIdentityMapReturnsOneInstance(string name) => Run(name, nameof(PrimaryKeyImpl));

    private static void PrimaryKeyImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        using (CaseDbContext<T> seed = db.NewContext())
        {
            for (int i = 0; i < samples.Length; i++)
                seed.Keys.Add(new CaseKeyRow<T> { Key = samples[i], Label = $"key{i}" });
            seed.SaveChanges();
        }

        using CaseDbContext<T> context = db.NewContext();
        for (int i = 0; i < samples.Length; i++)
        {
            CaseKeyRow<T>? loaded = context.Keys.Find(c.CreateSamples()[i]);
            Assert.IsNotNull(loaded, $"sample {i}");
            Assert.AreEqual($"key{i}", loaded.Label);
            Assert.AreEqual(samples[i], loaded.Key);

            // A second equal key that is a different object resolves to the tracked instance (no second query, no duplicate).
            Assert.AreSame(loaded, context.Keys.Find(c.CreateSamples()[i]), $"identity map, sample {i}");
        }

        Assert.IsNull(context.Keys.Find(c.CreateAbsent()));
        Assert.HasCount(samples.Length, context.ChangeTracker.Entries<CaseKeyRow<T>>());
    }

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void PrimaryKey_DuplicateKey_IsRejected_ByTheContextAndByTheDatabase(string name) => Run(name, nameof(DuplicatePrimaryKeyImpl));

    private static void DuplicatePrimaryKeyImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        using (CaseDbContext<T> seed = db.NewContext())
        {
            seed.Keys.Add(new CaseKeyRow<T> { Key = samples[1], Label = "first" });
            seed.SaveChanges();

            // Same context: the identity map already holds an equal key.
            Assert.ThrowsExactly<InvalidOperationException>(() => seed.Keys.Add(new CaseKeyRow<T> { Key = c.CreateSamples()[1], Label = "second" }));
        }

        // A fresh context does not know the row, so the database constraint must reject it.
        using CaseDbContext<T> other = db.NewContext();
        other.Keys.Add(new CaseKeyRow<T> { Key = c.CreateSamples()[1], Label = "second" });
        Assert.ThrowsExactly<DbUpdateException>(() => other.SaveChanges());
    }

    // ----------------- foreign key -----------------

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void ForeignKey_RelatesChildrenToParents_AndIncludeLoadsThem(string name) => Run(name, nameof(ForeignKeyImpl));

    private static void ForeignKeyImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        using (CaseDbContext<T> seed = db.NewContext())
        {
            for (int i = 0; i < samples.Length; i++)
                seed.Keys.Add(new CaseKeyRow<T> { Key = samples[i], Label = $"parent{i}" });
            for (int i = 0; i < samples.Length; i++)
                seed.Children.Add(new CaseChildRow<T> { Id = i + 1, ParentKey = c.CreateSamples()[samples.Length - 1 - i] });
            seed.SaveChanges();
        }

        using CaseDbContext<T> context = db.NewContext();
        CaseChildRow<T>[] children = context.Children.Include(x => x.Parent).OrderBy(x => x.Id).ToArray();

        Assert.HasCount(samples.Length, children);
        for (int i = 0; i < samples.Length; i++)
        {
            Assert.IsNotNull(children[i].Parent, $"child {i + 1}");
            Assert.AreEqual($"parent{samples.Length - 1 - i}", children[i].Parent!.Label);
            Assert.AreEqual(samples[samples.Length - 1 - i], children[i].ParentKey);
        }
    }

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void ForeignKey_ToAMissingParent_IsRejectedByTheDatabase(string name) => Run(name, nameof(OrphanImpl));

    private static void OrphanImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        using (CaseDbContext<T> seed = db.NewContext())
        {
            seed.Keys.Add(new CaseKeyRow<T> { Key = c.CreateSamples()[0], Label = "only parent" });
            seed.SaveChanges();
        }

        using CaseDbContext<T> context = db.NewContext();
        context.Children.Add(new CaseChildRow<T> { Id = 1, ParentKey = c.CreateAbsent() });

        Assert.ThrowsExactly<DbUpdateException>(() => context.SaveChanges());
    }

    // ----------------- unique index -----------------

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void UniqueIndex_AcceptsDistinctValues_AndRejectsAnEqualOne(string name) => Run(name, nameof(UniqueIndexImpl));

    private static void UniqueIndexImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        T[] samples = c.CreateSamples();
        using (CaseDbContext<T> seed = db.NewContext())
        {
            for (int i = 0; i < samples.Length; i++)
                seed.Uniques.Add(new CaseUniqueRow<T> { Id = i + 1, Value = samples[i] });
            seed.SaveChanges();
        }

        using CaseDbContext<T> context = db.NewContext();
        context.Uniques.Add(new CaseUniqueRow<T> { Id = 100, Value = c.CreateSamples()[^1] });

        Assert.ThrowsExactly<DbUpdateException>(() => context.SaveChanges());
        Assert.AreEqual(samples.Length, db.Scalar("select count(*) from \"Uniques\"") is long n ? (int)n : -1);
    }
}
