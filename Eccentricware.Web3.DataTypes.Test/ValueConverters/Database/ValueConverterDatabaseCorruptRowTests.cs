using Microsoft.EntityFrameworkCore;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>A malformed stored value surfaces as <see cref="FormatException"/> through a real EF query, not wrapped and not swallowed.</summary>
[TestClass]
public sealed class ValueConverterDatabaseCorruptRowTests
{
    private static void Run(string name, string method) => ConverterCases.Get(name).Invoke(typeof(ValueConverterDatabaseCorruptRowTests), method);

    [TestMethod]
    [DynamicData(nameof(ConverterCases.CorruptibleNames), typeof(ConverterCases))]
    public void CorruptValueColumn_ThrowsFormatException_WhenProjectedOrMaterialised(string name) => Run(name, nameof(CorruptValueImpl));

    private static void CorruptValueImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        db.Execute("insert into \"Rows\" (\"Id\", \"Value\", \"Optional\") values (1, @bad, null)", ("@bad", c.CorruptStoredValue));

        using CaseDbContext<T> context = db.NewContext();

        Assert.ThrowsExactly<FormatException>(() => context.Rows.AsNoTracking().Select(r => r.Value).Single());
        Assert.ThrowsExactly<FormatException>(() => context.Rows.AsNoTracking().Single());
        Assert.ThrowsExactly<FormatException>(() => context.Rows.Single());
    }

    [TestMethod]
    [DynamicData(nameof(ConverterCases.CorruptibleNames), typeof(ConverterCases))]
    public void CorruptNullableColumn_ThrowsFormatException_AndTheValidRowsStayReadable(string name) => Run(name, nameof(CorruptOptionalImpl));

    private static void CorruptOptionalImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        db.SeedRows(c.CreateSamples());
        db.Execute("update \"Rows\" set \"Optional\" = @bad where \"Id\" = 2", ("@bad", c.CorruptStoredValue));

        using CaseDbContext<T> context = db.NewContext();

        Assert.ThrowsExactly<FormatException>(() => context.Rows.AsNoTracking().Where(r => r.Id == 2).Select(r => r.Optional).Single());

        // Rows that do not carry the bad value, and the other column of the bad row, still read.
        Assert.AreEqual(c.CreateSamples()[0], context.Rows.AsNoTracking().Where(r => r.Id == 1).Select(r => r.Value).Single());
        Assert.IsNull(context.Rows.AsNoTracking().Where(r => r.Id == 1).Select(r => r.Optional).Single());
        Assert.AreEqual(c.CreateSamples()[1], context.Rows.AsNoTracking().Where(r => r.Id == 2).Select(r => r.Value).Single());
    }

    [TestMethod]
    [DynamicData(nameof(ConverterCases.CorruptibleNames), typeof(ConverterCases))]
    public void CorruptRow_DoesNotAffectQueriesThatNeverReadTheColumn(string name) => Run(name, nameof(UnreadColumnImpl));

    private static void UnreadColumnImpl<T>(ConverterCase<T> c) where T : struct
    {
        using var db = new CaseDatabase<T>(c);
        db.Execute("insert into \"Rows\" (\"Id\", \"Value\", \"Optional\") values (7, @bad, null)", ("@bad", c.CorruptStoredValue));

        using CaseDbContext<T> context = db.NewContext();

        Assert.AreEqual(7, context.Rows.Select(r => r.Id).Single());
        Assert.AreEqual(1, context.Rows.Count());
    }
}
