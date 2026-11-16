using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Row with a converted value column and a nullable converted column.</summary>
internal sealed class CaseRow<T> where T : struct
{
    public int Id { get; set; }
    public T Value { get; set; }
    public T? Optional { get; set; }
}

/// <summary>Row whose primary key is the converted value.</summary>
internal sealed class CaseKeyRow<T> where T : struct
{
    public T Key { get; set; }
    public string Label { get; set; } = "";
}

/// <summary>Row whose foreign key is the converted value.</summary>
internal sealed class CaseChildRow<T> where T : struct
{
    public int Id { get; set; }
    public T ParentKey { get; set; }
    public CaseKeyRow<T>? Parent { get; set; }
}

/// <summary>Row with a unique index on the converted value.</summary>
internal sealed class CaseUniqueRow<T> where T : struct
{
    public int Id { get; set; }
    public T Value { get; set; }
}

/// <summary>Row whose converted value is a concurrency token.</summary>
internal sealed class CaseTokenRow<T> where T : struct
{
    public int Id { get; set; }
    public T Token { get; set; }
    public string Payload { get; set; } = "";
}

/// <summary>
/// EF caches one model per context type, but two cases can share a CLR type (the integer and the binary converter of
/// <see cref="FunctionSelector"/>), so the case name has to be part of the cache key.
/// </summary>
internal sealed class CaseModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
        => (context.GetType(), (context as ICaseDbContext)?.CaseName, designTime);
}

internal interface ICaseDbContext
{
    string CaseName { get; }
}

/// <summary>Context mapping every row type above with the converter of one <see cref="ConverterCase{T}"/>.</summary>
internal sealed class CaseDbContext<T>(SqliteConnection connection, ConverterCase<T> converterCase) : DbContext, ICaseDbContext where T : struct
{
    public string CaseName => converterCase.Name;

    public DbSet<CaseRow<T>> Rows => Set<CaseRow<T>>();
    public DbSet<CaseKeyRow<T>> Keys => Set<CaseKeyRow<T>>();
    public DbSet<CaseChildRow<T>> Children => Set<CaseChildRow<T>>();
    public DbSet<CaseUniqueRow<T>> Uniques => Set<CaseUniqueRow<T>>();
    public DbSet<CaseTokenRow<T>> Tokens => Set<CaseTokenRow<T>>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection).ReplaceService<IModelCacheKeyFactory, CaseModelCacheKeyFactory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var rows = modelBuilder.Entity<CaseRow<T>>().ToTable("Rows");
        rows.Property(r => r.Id).ValueGeneratedNever();
        rows.Property(r => r.Value).HasConversion(converterCase.CreateConverter());
        rows.Property(r => r.Optional).HasConversion(converterCase.CreateConverter());

        var keys = modelBuilder.Entity<CaseKeyRow<T>>().ToTable("Keys");
        keys.HasKey(k => k.Key);
        keys.Property(k => k.Key).HasConversion(converterCase.CreateConverter()).ValueGeneratedNever();

        var children = modelBuilder.Entity<CaseChildRow<T>>().ToTable("Children");
        children.Property(c => c.Id).ValueGeneratedNever();
        children.Property(c => c.ParentKey).HasConversion(converterCase.CreateConverter());
        children.HasOne(c => c.Parent).WithMany().HasForeignKey(c => c.ParentKey);

        var uniques = modelBuilder.Entity<CaseUniqueRow<T>>().ToTable("Uniques");
        uniques.Property(u => u.Id).ValueGeneratedNever();
        uniques.Property(u => u.Value).HasConversion(converterCase.CreateConverter());
        uniques.HasIndex(u => u.Value).IsUnique();

        var tokens = modelBuilder.Entity<CaseTokenRow<T>>().ToTable("Tokens");
        tokens.Property(t => t.Id).ValueGeneratedNever();
        tokens.Property(t => t.Token).HasConversion(converterCase.CreateConverter()).IsConcurrencyToken();
    }
}

/// <summary>An open in-memory SQLite database with the schema of <see cref="CaseDbContext{T}"/>; one per test.</summary>
internal sealed class CaseDatabase<T> : IDisposable where T : struct
{
    private readonly ConverterCase<T> _case;

    public CaseDatabase(ConverterCase<T> converterCase)
    {
        _case = converterCase;
        Connection = new SqliteConnection("Data Source=:memory:");
        Connection.Open();
        using CaseDbContext<T> context = NewContext();
        context.Database.EnsureCreated();
    }

    public SqliteConnection Connection { get; }

    public CaseDbContext<T> NewContext() => new(Connection, _case);

    /// <summary>Stores the samples as rows with ids 1..n, in the given order of insertion.</summary>
    public void SeedRows(IReadOnlyList<T> values, IEnumerable<int>? insertionOrder = null)
    {
        using CaseDbContext<T> context = NewContext();
        foreach (int i in insertionOrder ?? Enumerable.Range(0, values.Count))
            context.Rows.Add(new CaseRow<T> { Id = i + 1, Value = values[i] });
        context.SaveChanges();
    }

    /// <summary>Executes a statement with a single parameter and returns the affected row count.</summary>
    public int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = Connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command.ExecuteNonQuery();
    }

    public object? Scalar(string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = Connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        object? result = command.ExecuteScalar();
        return result is DBNull ? null : result;
    }

    public void Dispose() => Connection.Dispose();
}

/// <summary>Captured-variable holder, so EF parameterises the value instead of inlining a constant.</summary>
internal sealed class ValueBox<T>(T value)
{
    public T Value = value;
}

/// <summary>Builds predicates over generic row types using the value type's own operators (== &lt; &gt;).</summary>
internal static class RowPredicates
{
    public static Expression<Func<CaseRow<T>, bool>> Value<T>(ExpressionType op, T value) where T : struct
    {
        ParameterExpression row = Expression.Parameter(typeof(CaseRow<T>), "r");
        Expression body = Expression.MakeBinary(op, Expression.Property(row, nameof(CaseRow<T>.Value)), Captured(value));
        return Expression.Lambda<Func<CaseRow<T>, bool>>(body, row);
    }

    /// <summary><c>r.Optional == value</c>, with the value lifted to the nullable type.</summary>
    public static Expression<Func<CaseRow<T>, bool>> OptionalEquals<T>(T value) where T : struct
    {
        ParameterExpression row = Expression.Parameter(typeof(CaseRow<T>), "r");
        Expression body = Expression.Equal(Expression.Property(row, nameof(CaseRow<T>.Optional)), Expression.Convert(Captured(value), typeof(T?)));
        return Expression.Lambda<Func<CaseRow<T>, bool>>(body, row);
    }

    private static Expression Captured<T>(T value) => Expression.Field(Expression.Constant(new ValueBox<T>(value)), nameof(ValueBox<T>.Value));
}
