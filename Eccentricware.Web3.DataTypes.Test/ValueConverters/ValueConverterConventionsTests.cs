using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>Bulk registration with <c>ConfigureConventions</c> and <c>Properties&lt;T&gt;().HaveConversion&lt;TConverter&gt;()</c>, with no per-property configuration.</summary>
[TestClass]
public sealed class ValueConverterConventionsTests
{
    private sealed class ConventionRow
    {
        public int Id { get; set; }
        public Address Address { get; set; }
        public Address? OptionalAddress { get; set; }
        public FunctionSelector Selector { get; set; }
        public Hash32 Hash { get; set; }
        public Hash32? OptionalHash { get; set; }
        public HexBigInteger Big { get; set; }
        public HexBigInteger? OptionalBig { get; set; }
        public HexBytes Bytes { get; set; }
        public InstructionDiscriminator Discriminator { get; set; }
        public int256 Signed { get; set; }
        public Signature Signature { get; set; }
        public uint256 Unsigned { get; set; }
    }

    private sealed class HashKeyRow
    {
        public Hash32 Id { get; set; }
        public string Label { get; set; } = "";
    }

    private sealed class ConventionContext(SqliteConnection connection) : DbContext
    {
        public DbSet<ConventionRow> Rows => Set<ConventionRow>();
        public DbSet<HashKeyRow> HashKeys => Set<HashKeyRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);

        protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
        {
            configuration.Properties<Address>().HaveConversion<AddressValueConverter>();
            configuration.Properties<FunctionSelector>().HaveConversion<FunctionSelectorBinaryValueConverter>();
            configuration.Properties<Hash32>().HaveConversion<Hash32ValueConverter>();
            configuration.Properties<HexBigInteger>().HaveConversion<HexBigIntegerToBytesConverter>();
            configuration.Properties<HexBytes>().HaveConversion<HexBytesValueConverter>();
            configuration.Properties<InstructionDiscriminator>().HaveConversion<InstructionDiscriminatorBinaryValueConverter>();
            configuration.Properties<int256>().HaveConversion<Int256BytesValueConverter>();
            configuration.Properties<Signature>().HaveConversion<SignatureValueConverter>();
            configuration.Properties<uint256>().HaveConversion<UInt256BytesValueConverter>();
        }
    }

    private static SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var context = new ConventionContext(connection);
        context.Database.EnsureCreated();
        return connection;
    }

    [TestMethod]
    public void EveryRegisteredClrType_GetsItsConverter_IncludingNullableAndKeyProperties()
    {
        using SqliteConnection connection = OpenDatabase();
        using var context = new ConventionContext(connection);
        IEntityType row = context.Model.FindEntityType(typeof(ConventionRow))!;

        (string Property, Type Converter, int? Size)[] expected =
        [
            (nameof(ConventionRow.Address), typeof(AddressValueConverter), 33),
            (nameof(ConventionRow.OptionalAddress), typeof(AddressValueConverter), 33),
            (nameof(ConventionRow.Selector), typeof(FunctionSelectorBinaryValueConverter), 4),
            (nameof(ConventionRow.Hash), typeof(Hash32ValueConverter), 32),
            (nameof(ConventionRow.OptionalHash), typeof(Hash32ValueConverter), 32),
            (nameof(ConventionRow.Big), typeof(HexBigIntegerToBytesConverter), null),
            (nameof(ConventionRow.OptionalBig), typeof(HexBigIntegerToBytesConverter), null),
            (nameof(ConventionRow.Bytes), typeof(HexBytesValueConverter), null),
            (nameof(ConventionRow.Discriminator), typeof(InstructionDiscriminatorBinaryValueConverter), 8),
            (nameof(ConventionRow.Signed), typeof(Int256BytesValueConverter), 32),
            (nameof(ConventionRow.Signature), typeof(SignatureValueConverter), 66),
            (nameof(ConventionRow.Unsigned), typeof(UInt256BytesValueConverter), 32),
        ];

        foreach ((string property, Type converter, int? size) in expected)
        {
            IProperty p = row.FindProperty(property)!;
            Assert.IsNotNull(p.GetValueConverter(), property);
            Assert.AreEqual(converter, p.GetValueConverter()!.GetType(), property);
            Assert.AreEqual(size, p.FindRelationalTypeMapping()!.Size, $"{property} size");
        }

        IProperty key = context.Model.FindEntityType(typeof(HashKeyRow))!.FindProperty(nameof(HashKeyRow.Id))!;
        Assert.AreEqual(typeof(Hash32ValueConverter), key.GetValueConverter()!.GetType());
        Assert.IsTrue(key.IsPrimaryKey());
    }

    [TestMethod]
    public void BoundaryValues_RoundTripThroughTheDatabase_WithoutAnyPerPropertyConfiguration()
    {
        using SqliteConnection connection = OpenDatabase();
        ConventionRow Row(int id, Address address, Hash32 hash, HexBigInteger big, HexBytes bytes, ulong discriminator, uint selector, int256 signed, Signature signature, uint256 unsigned)
            => new()
            {
                Id = id, Address = address, OptionalAddress = address, Selector = new FunctionSelector(selector), Hash = hash, OptionalHash = hash,
                Big = big, OptionalBig = big, Bytes = bytes, Discriminator = new InstructionDiscriminator(discriminator), Signed = signed, Signature = signature, Unsigned = unsigned,
            };

        ConventionRow[] written =
        [
            Row(1, Address.ZeroEvm, Hash32.Zero, HexBigInteger.Zero, HexBytes.Empty, 0, 0, int256.MinValue, Signature.ZeroEvm, uint256.Zero),
            Row(2, Address.FromSolanaBytes(ConverterTestData.Repeated(0xFF, 32)), new Hash32(ConverterTestData.Repeated(0xFF, 32)), new HexBigInteger(-1),
                new HexBytes(ConverterTestData.Ascending(40)), ulong.MaxValue, uint.MaxValue, int256.MaxValue, Signature.FromSolanaBytes(ConverterTestData.Repeated(0xFF, 64)), uint256.MaxValue),
        ];

        using (var context = new ConventionContext(connection))
        {
            context.Rows.AddRange(written);
            context.SaveChanges();
        }

        using var read = new ConventionContext(connection);
        ConventionRow[] loaded = read.Rows.AsNoTracking().OrderBy(r => r.Id).ToArray();

        Assert.HasCount(2, loaded);
        for (int i = 0; i < written.Length; i++)
        {
            Assert.AreEqual(written[i].Address, loaded[i].Address);
            Assert.AreEqual(written[i].OptionalAddress, loaded[i].OptionalAddress);
            Assert.AreEqual(written[i].Selector, loaded[i].Selector);
            Assert.AreEqual(written[i].Hash, loaded[i].Hash);
            Assert.AreEqual(written[i].OptionalHash, loaded[i].OptionalHash);
            Assert.AreEqual(written[i].Big, loaded[i].Big);
            Assert.AreEqual(written[i].OptionalBig, loaded[i].OptionalBig);
            Assert.AreEqual(written[i].Bytes, loaded[i].Bytes);
            Assert.AreEqual(written[i].Discriminator, loaded[i].Discriminator);
            Assert.AreEqual(written[i].Signed, loaded[i].Signed);
            Assert.AreEqual(written[i].Signature, loaded[i].Signature);
            Assert.AreEqual(written[i].Unsigned, loaded[i].Unsigned);
        }
    }

    [TestMethod]
    public void NullableProperties_StoreNull_AndKeyPropertyWorksAsAKey()
    {
        using SqliteConnection connection = OpenDatabase();
        var key = new Hash32(ConverterTestData.Ascending(32));

        using (var context = new ConventionContext(connection))
        {
            context.Rows.Add(new ConventionRow { Id = 1 });
            context.HashKeys.Add(new HashKeyRow { Id = key, Label = "k" });
            context.SaveChanges();
        }

        using var read = new ConventionContext(connection);
        ConventionRow row = read.Rows.AsNoTracking().Single();

        Assert.IsNull(row.OptionalAddress);
        Assert.IsNull(row.OptionalHash);
        Assert.IsNull(row.OptionalBig);
        Assert.AreEqual(1, read.Rows.Count(r => r.OptionalHash == null));
        Assert.AreEqual("k", read.HashKeys.Find(new Hash32(ConverterTestData.Ascending(32)))!.Label);
    }
}
