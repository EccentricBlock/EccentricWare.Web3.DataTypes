using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// What a converter does to EF's relational model without a database server: the column type SQLite creates, and whether the
/// converter's size hint reaches the type mapping. The effect of <c>HasMaxLength</c> on a converted column is provider
/// specific (SQLite ignores it for BLOB) and is not covered here.
/// </summary>
[TestClass]
public sealed class ValueConverterTypeMappingTests
{
    private static void Run(string name, string method) => ConverterCases.Get(name).Invoke(typeof(ValueConverterTypeMappingTests), method);

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void SizeHint_ReachesTheRelationalTypeMapping_ForValueAndNullableColumns(string name) => Run(name, nameof(SizeHintImpl));

    private static void SizeHintImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        using CaseDbContext<T> context = db.NewContext();
        IEntityType entity = context.Model.FindEntityType(typeof(CaseRow<T>))!;

        foreach (string property in new[] { nameof(CaseRow<T>.Value), nameof(CaseRow<T>.Optional) })
        {
            var mapping = entity.FindProperty(property)!.FindRelationalTypeMapping()!;
            Assert.AreEqual(c.SizeHint, mapping.Size, $"{c.Name}.{property} size");
            Assert.AreEqual(c.StoreType, mapping.StoreType, $"{c.Name}.{property} store type");
        }
    }

    [TestMethod]
    [DynamicData(nameof(ConverterCases.Names), typeof(ConverterCases))]
    public void CreateScript_DeclaresTheDocumentedColumnTypes_AndNullability(string name) => Run(name, nameof(CreateScriptImpl));

    private static void CreateScriptImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        using CaseDbContext<T> context = db.NewContext();
        string script = context.Database.GenerateCreateScript();

        StringAssert.Contains(script, $"\"Value\" {c.StoreType} NOT NULL");
        StringAssert.Contains(script, $"\"Optional\" {c.StoreType} NULL");
        StringAssert.Contains(script, $"\"Key\" {c.StoreType} NOT NULL");
    }

    // ----------------- caller hints -----------------

    private sealed class HashRow
    {
        public int Id { get; set; }
        public Hash32 ByType { get; set; }
        public Hash32 WithLargerSize { get; set; }
        public Hash32 WithPrecisionOnly { get; set; }
    }

    private sealed class HashContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=:memory:");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<HashRow>();
            row.Property(r => r.ByType).HasConversion<Hash32ValueConverter>();
            row.Property(r => r.WithLargerSize).HasConversion(new Hash32ValueConverter(new ConverterMappingHints(size: 64)));
            row.Property(r => r.WithPrecisionOnly).HasConversion(new Hash32ValueConverter(new ConverterMappingHints(precision: 7)));
        }
    }

    [TestMethod]
    public void CallerHints_PassedToAConverterInstance_ReachTheTypeMapping()
    {
        using var context = new HashContext();
        IEntityType entity = context.Model.FindEntityType(typeof(HashRow))!;

        Assert.AreEqual(32, entity.FindProperty(nameof(HashRow.ByType))!.FindRelationalTypeMapping()!.Size);
        Assert.AreEqual(64, entity.FindProperty(nameof(HashRow.WithLargerSize))!.FindRelationalTypeMapping()!.Size);
        Assert.AreEqual(32, entity.FindProperty(nameof(HashRow.WithPrecisionOnly))!.FindRelationalTypeMapping()!.Size);
    }
}
