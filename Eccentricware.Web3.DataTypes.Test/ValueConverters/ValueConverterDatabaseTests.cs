using EccentricWare.Web3.DataTypes.ValueConverters;
using Microsoft.Data.Sqlite;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// The converters against a real provider (SQLite, in memory): what ends up in the column, that equality, range filters
/// and <c>ORDER BY</c> on an ordered column return what in-memory comparison would, and that nothing is lost on the way.
/// </summary>
[TestClass]
public sealed class ValueConverterDatabaseTests
{
    private sealed class Row
    {
        public int Id { get; set; }
        public uint256 Unsigned { get; set; }
        public int256 Signed { get; set; }
        public FunctionSelector SelectorInt { get; set; }
        public FunctionSelector SelectorBinary { get; set; }
        public InstructionDiscriminator DiscriminatorLong { get; set; }
        public InstructionDiscriminator DiscriminatorBinary { get; set; }
        public Hash32 Hash { get; set; }
        public Address Address { get; set; }
        public Address? OptionalAddress { get; set; }
        public Signature Signature { get; set; }
        public HexBytes Bytes { get; set; }
        public HexBytes BytesText { get; set; }
        public HexBigInteger Big { get; set; }
    }

    private sealed class ConverterDbContext(SqliteConnection connection) : DbContext
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<Row>().ToTable("Rows");
            row.Property(r => r.Id).ValueGeneratedNever();
            row.Property(r => r.Unsigned).HasConversion<UInt256BytesValueConverter>();
            row.Property(r => r.Signed).HasConversion<Int256BytesValueConverter>();
            row.Property(r => r.SelectorInt).HasConversion<FunctionSelectorValueConverter>();
            row.Property(r => r.SelectorBinary).HasConversion<FunctionSelectorBinaryValueConverter>();
            row.Property(r => r.DiscriminatorLong).HasConversion<InstructionDiscriminatorValueConverter>();
            row.Property(r => r.DiscriminatorBinary).HasConversion<InstructionDiscriminatorBinaryValueConverter>();
            row.Property(r => r.Hash).HasConversion<Hash32ValueConverter>();
            row.Property(r => r.Address).HasConversion<AddressValueConverter>();
            row.Property(r => r.OptionalAddress).HasConversion<AddressValueConverter>();
            row.Property(r => r.Signature).HasConversion<SignatureValueConverter>();
            row.Property(r => r.Bytes).HasConversion<HexBytesValueConverter>();
            row.Property(r => r.BytesText).HasConversion<HexBytesStringValueConverter>();
            row.Property(r => r.Big).HasConversion<HexBigIntegerToBytesConverter>();
        }
    }

    private static byte[] RandomBytes(Random random, int length)
    {
        byte[] bytes = new byte[length];
        random.NextBytes(bytes);
        return bytes;
    }

    /// <summary>Distinct values per column (including the boundary ones), so ORDER BY has no ties to resolve arbitrarily.</summary>
    private static List<Row> BuildRows(int randomRows)
    {
        var random = new Random(20260511);
        var rows = new List<Row>();
        var selectors = new HashSet<uint> { 0u, 1u, 0x7FFFFFFFu, 0x80000000u, 0xFFFFFFFFu };
        var discriminators = new HashSet<ulong> { 0UL, 1UL, 0x7FFFFFFFFFFFFFFFUL, 0x8000000000000000UL, ulong.MaxValue };
        var signed = new HashSet<int256> { int256.MinValue, int256.MaxValue, new int256(0L), new int256(1L), new int256(-1L), new int256(long.MinValue), new int256(long.MaxValue) };
        while (selectors.Count < randomRows) selectors.Add(BitConverter.ToUInt32(RandomBytes(random, 4)));
        while (discriminators.Count < randomRows) discriminators.Add(BitConverter.ToUInt64(RandomBytes(random, 8)));
        while (signed.Count < randomRows) signed.Add(random.Next(3) == 0 ? new int256((long)random.Next(-100_000, 100_000)) : new int256(RandomBytes(random, 32)));

        uint[] selectorList = selectors.ToArray();
        ulong[] discriminatorList = discriminators.ToArray();
        int256[] signedList = signed.ToArray();

        for (int i = 0; i < randomRows; i++)
        {
            bool evm = random.Next(2) == 0;
            rows.Add(new Row
            {
                Id = i + 1,
                Unsigned = new uint256(RandomBytes(random, 32)),
                Signed = signedList[i],
                SelectorInt = new FunctionSelector(selectorList[i]),
                SelectorBinary = new FunctionSelector(selectorList[i]),
                DiscriminatorLong = new InstructionDiscriminator(discriminatorList[i]),
                DiscriminatorBinary = new InstructionDiscriminator(discriminatorList[i]),
                Hash = new Hash32(RandomBytes(random, 32)),
                Address = evm ? Address.FromEvmBytes(RandomBytes(random, 20)) : Address.FromSolanaBytes(RandomBytes(random, 32)),
                OptionalAddress = i % 3 == 0 ? null : Address.FromEvmBytes(RandomBytes(random, 20)),
                Signature = evm ? Signature.FromEvmBytes(RandomBytes(random, 65)) : Signature.FromSolanaBytes(RandomBytes(random, 64)),
                Bytes = new HexBytes(RandomBytes(random, random.Next(1, 40))),
                BytesText = new HexBytes(RandomBytes(random, random.Next(1, 40))),
                Big = new HexBigInteger(random.NextInt64(-1_000_000, 1_000_000_000)),
            });
        }

        rows[0].Bytes = HexBytes.Empty;
        rows[0].BytesText = HexBytes.Empty;
        rows[0].Big = HexBigInteger.Zero;
        return rows;
    }

    private static (SqliteConnection Connection, ConverterDbContext Context, List<Row> Rows) CreateDatabase(int rowCount = 300)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var context = new ConverterDbContext(connection);
        context.Database.EnsureCreated();

        List<Row> rows = BuildRows(rowCount);
        context.AddRange(rows);
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return (connection, context, rows);
    }

    private static void AssertSameOrder<TKey>(string name, ConverterDbContext context, List<Row> rows, Func<IQueryable<Row>, IQueryable<Row>> sqlOrder, Func<Row, TKey> memoryKey)
    {
        int[] fromSql = sqlOrder(context.Rows).Select(r => r.Id).ToArray();
        int[] fromMemory = rows.OrderBy(memoryKey).Select(r => r.Id).ToArray();
        CollectionAssert.AreEqual(fromMemory, fromSql, name);
    }

    // ----------------- round trip and equality -----------------

    [TestMethod]
    public void EveryColumn_RoundTripsThroughTheDatabase()
    {
        var (connection, context, rows) = CreateDatabase();
        using (connection) using (context)
        {
            Row[] loaded = context.Rows.AsNoTracking().OrderBy(r => r.Id).ToArray();

            Assert.HasCount(rows.Count, loaded);
            foreach ((Row expected, Row actual) in rows.Zip(loaded))
            {
                Assert.AreEqual(expected.Unsigned, actual.Unsigned, $"row {expected.Id} uint256");
                Assert.AreEqual(expected.Signed, actual.Signed, $"row {expected.Id} int256");
                Assert.AreEqual(expected.SelectorInt, actual.SelectorInt, $"row {expected.Id} selector int");
                Assert.AreEqual(expected.SelectorBinary, actual.SelectorBinary, $"row {expected.Id} selector binary");
                Assert.AreEqual(expected.DiscriminatorLong, actual.DiscriminatorLong, $"row {expected.Id} discriminator long");
                Assert.AreEqual(expected.DiscriminatorBinary, actual.DiscriminatorBinary, $"row {expected.Id} discriminator binary");
                Assert.AreEqual(expected.Hash, actual.Hash, $"row {expected.Id} hash");
                Assert.AreEqual(expected.Address, actual.Address, $"row {expected.Id} address");
                Assert.AreEqual(expected.OptionalAddress, actual.OptionalAddress, $"row {expected.Id} optional address");
                Assert.AreEqual(expected.Signature, actual.Signature, $"row {expected.Id} signature");
                Assert.AreEqual(expected.Bytes, actual.Bytes, $"row {expected.Id} bytes");
                Assert.AreEqual(expected.BytesText, actual.BytesText, $"row {expected.Id} bytes text");
                Assert.AreEqual(expected.Big.Value, actual.Big.Value, $"row {expected.Id} big");
            }
        }
    }

    [TestMethod]
    public void EqualityQueries_FindExactlyTheMatchingRow_ForEveryColumn()
    {
        var (connection, context, rows) = CreateDatabase();
        using (connection) using (context)
        {
            Row target = rows[17];

            Assert.AreEqual(target.Id, context.Rows.Single(r => r.Unsigned == target.Unsigned).Id);
            Assert.AreEqual(target.Id, context.Rows.Single(r => r.Signed == target.Signed).Id);
            Assert.AreEqual(target.Id, context.Rows.Single(r => r.SelectorInt == target.SelectorInt).Id);
            Assert.AreEqual(target.Id, context.Rows.Single(r => r.SelectorBinary == target.SelectorBinary).Id);
            Assert.AreEqual(target.Id, context.Rows.Single(r => r.DiscriminatorLong == target.DiscriminatorLong).Id);
            Assert.AreEqual(target.Id, context.Rows.Single(r => r.DiscriminatorBinary == target.DiscriminatorBinary).Id);
            Assert.AreEqual(target.Id, context.Rows.Single(r => r.Hash == target.Hash).Id);
            Assert.AreEqual(target.Id, context.Rows.Single(r => r.Address == target.Address).Id);
            Assert.AreEqual(target.Id, context.Rows.Single(r => r.Signature == target.Signature).Id);
            Assert.AreEqual(target.Id, context.Rows.Single(r => r.Bytes == target.Bytes).Id);
            Assert.AreEqual(target.Id, context.Rows.Single(r => r.BytesText == target.BytesText).Id);

            Assert.AreEqual(rows.Count(r => r.OptionalAddress is null), context.Rows.Count(r => r.OptionalAddress == null));
            Assert.AreEqual(1, context.Rows.Count(r => r.Bytes == HexBytes.Empty), "the empty value is found by equality");
        }
    }

    // ----------------- ordered columns: ORDER BY and range filters follow the value -----------------

    [TestMethod]
    public void OrderBy_FollowsTheValue_ForEveryOrderedColumn()
    {
        var (connection, context, rows) = CreateDatabase();
        using (connection) using (context)
        {
            AssertSameOrder("uint256", context, rows, q => q.OrderBy(r => r.Unsigned), r => r.Unsigned);
            AssertSameOrder("int256", context, rows, q => q.OrderBy(r => r.Signed), r => r.Signed);
            AssertSameOrder("FunctionSelector int", context, rows, q => q.OrderBy(r => r.SelectorInt), r => (uint)r.SelectorInt.AsInt32);
            AssertSameOrder("FunctionSelector binary", context, rows, q => q.OrderBy(r => r.SelectorBinary), r => (uint)r.SelectorBinary.AsInt32);
            AssertSameOrder("InstructionDiscriminator long", context, rows, q => q.OrderBy(r => r.DiscriminatorLong), r => r.DiscriminatorLong.Value);
            AssertSameOrder("InstructionDiscriminator binary", context, rows, q => q.OrderBy(r => r.DiscriminatorBinary), r => r.DiscriminatorBinary.Value);
            AssertSameOrder("Hash32", context, rows, q => q.OrderBy(r => r.Hash), r => r.Hash);
            AssertSameOrder("Address (EVM and Solana)", context, rows, q => q.OrderBy(r => r.Address), r => r.Address);
            AssertSameOrder("Signature (EVM and Solana)", context, rows, q => q.OrderBy(r => r.Signature), r => r.Signature);
        }
    }

    [TestMethod]
    public void RangeFilters_ReturnTheRightRows_ForEveryOrderedColumn()
    {
        var (connection, context, rows) = CreateDatabase();
        using (connection) using (context)
        {
            // The in-memory oracle is the same predicate compiled against the original objects (the types' own operators).
            void AssertSameRows(string name, Expression<Func<Row, bool>> predicate)
            {
                int[] expected = rows.Where(predicate.Compile()).Select(r => r.Id).OrderBy(id => id).ToArray();
                int[] actual = context.Rows.Where(predicate).Select(r => r.Id).OrderBy(id => id).ToArray();

                CollectionAssert.AreEqual(expected, actual, name);
                Assert.IsGreaterThan(0, actual.Length, $"{name} selects something");
                Assert.IsLessThan(rows.Count, actual.Length, $"{name} does not select everything");
            }

            int256 zero = new(0L), low = new(-5000L), high = new(5000L);
            AssertSameRows("int256 > 0", r => r.Signed > zero);
            AssertSameRows("int256 < 0", r => r.Signed < zero);
            AssertSameRows("int256 between", r => r.Signed >= low && r.Signed <= high);

            var selector = new FunctionSelector(0x80000000u);
            AssertSameRows("selector binary > 0x80000000", r => r.SelectorBinary > selector);
            AssertSameRows("selector int > 0x80000000", r => r.SelectorInt > selector);

            var discriminator = new InstructionDiscriminator(0x8000000000000000UL);
            AssertSameRows("discriminator binary > 0x8000...", r => r.DiscriminatorBinary > discriminator);
            AssertSameRows("discriminator long > 0x8000...", r => r.DiscriminatorLong > discriminator);

            uint256 median = rows.Select(r => r.Unsigned).OrderBy(v => v).ElementAt(rows.Count / 2);
            AssertSameRows("uint256 > median", r => r.Unsigned > median);

            Hash32 hash = rows[40].Hash;
            AssertSameRows("Hash32 <", r => r.Hash < hash);

            Address address = rows.Select(r => r.Address).OrderBy(a => a).ElementAt(rows.Count / 2);
            AssertSameRows("Address >= median (EVM and Solana mixed)", r => r.Address >= address);

            Signature signature = rows.Select(r => r.Signature).OrderBy(sig => sig).ElementAt(rows.Count / 3);
            AssertSameRows("Signature <= a third of the way", r => r.Signature <= signature);
        }
    }

    [TestMethod]
    public void StoredColumns_ContainTheLiteralBytes_OfFixedValues()
    {
        // Expected values are literal vectors (hex written out, python arithmetic), not computed with the converters.
        var (connection, context, _) = CreateDatabase(rowCount: 5);
        using (connection) using (context)
        {
            context.Rows.ExecuteDelete();
            context.Rows.AddRange(
                new Row
                {
                    Id = 1,
                    Hash = new Hash32(ConverterTestData.Ascending(32)),
                    Unsigned = uint256.One,
                    Signed = new int256(-2L),
                    SelectorInt = FunctionSelector.Erc20.Transfer,
                    SelectorBinary = FunctionSelector.Erc20.Transfer,
                    DiscriminatorLong = new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL),
                    DiscriminatorBinary = new InstructionDiscriminator(0xAFAF6D1F0D989BEDUL),
                    Address = Address.Parse("0x52908400098527886e0f7030069857d2e4169ee7"),
                    Signature = Signature.FromSolanaBytes(Enumerable.Range(0, 64).Select(i => (byte)i).ToArray()),
                    Bytes = new HexBytes([0xde, 0xad, 0xbe, 0xef]),
                    BytesText = new HexBytes([0xde, 0xad, 0xbe, 0xef]),
                    Big = new HexBigInteger(128),
                },
                new Row { Id = 2, Big = new HexBigInteger(-1) },
                new Row { Id = 3, Big = HexBigInteger.Zero });
            context.SaveChanges();

            object? Scalar(string column, int id)
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"select \"{column}\" from \"Rows\" where \"Id\" = {id}";
                return command.ExecuteScalar();
            }

            byte[] Bytes(string column, int id = 1) => (byte[])Scalar(column, id)!;

            CollectionAssert.AreEqual(Convert.FromHexString("0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20"), Bytes("Hash"));
            CollectionAssert.AreEqual(Convert.FromHexString(new string('0', 62) + "01"), Bytes("Unsigned"));
            CollectionAssert.AreEqual(Convert.FromHexString("7f" + new string('f', 60) + "fe"), Bytes("Signed"));
            Assert.AreEqual(688233659L, Scalar("SelectorInt", 1)); // 0xa9059cbb - 2^31
            CollectionAssert.AreEqual(Convert.FromHexString("a9059cbb"), Bytes("SelectorBinary"));
            Assert.AreEqual(3436085020846496749L, Scalar("DiscriminatorLong", 1)); // 0xAFAF6D1F0D989BED - 2^63
            CollectionAssert.AreEqual(Convert.FromHexString("afaf6d1f0d989bed"), Bytes("DiscriminatorBinary"));
            CollectionAssert.AreEqual(Convert.FromHexString("0052908400098527886e0f7030069857d2e4169ee7"), Bytes("Address"));
            CollectionAssert.AreEqual(Convert.FromHexString("01" + Convert.ToHexString(Enumerable.Range(0, 64).Select(i => (byte)i).ToArray()) + "00"), Bytes("Signature"));
            CollectionAssert.AreEqual(new byte[] { 0xde, 0xad, 0xbe, 0xef }, Bytes("Bytes"));
            Assert.AreEqual("0xdeadbeef", Scalar("BytesText", 1));
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x80 }, Bytes("Big")); // 128 needs a sign byte in two's complement
            CollectionAssert.AreEqual(new byte[] { 0xff }, Bytes("Big", 2));

            // Zero is an empty blob, not NULL.
            using var zeroCommand = connection.CreateCommand();
            zeroCommand.CommandText = "select typeof(\"Big\"), length(\"Big\") from \"Rows\" where \"Id\" = 3";
            using var reader = zeroCommand.ExecuteReader();
            Assert.IsTrue(reader.Read());
            Assert.AreEqual("blob", reader.GetString(0));
            Assert.AreEqual(0L, reader.GetInt64(1));
        }
    }

    // ----------------- what is stored -----------------

    [TestMethod]
    public void StoredColumns_HaveTheDocumentedLayout()
    {
        var (connection, context, rows) = CreateDatabase(rowCount: 20);
        using (connection) using (context)
        {
            Row row = rows[5];

            object? Scalar(string column)
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"select \"{column}\" from \"Rows\" where \"Id\" = {row.Id}";
                return command.ExecuteScalar();
            }

            Assert.AreEqual(32, ((byte[])Scalar("Hash")!).Length);
            Assert.AreEqual(4, ((byte[])Scalar("SelectorBinary")!).Length);
            Assert.AreEqual(8, ((byte[])Scalar("DiscriminatorBinary")!).Length);
            Assert.AreEqual(32, ((byte[])Scalar("Unsigned")!).Length);
            Assert.AreEqual(66, ((byte[])Scalar("Signature")!).Length);
            Assert.AreEqual((long)(row.SelectorInt.AsInt32 ^ int.MinValue), Scalar("SelectorInt"));
            Assert.AreEqual(row.DiscriminatorLong.AsInt64 ^ long.MinValue, Scalar("DiscriminatorLong"));
            Assert.AreEqual(row.BytesText.ToString(), Scalar("BytesText"));

            // The int256 column is big-endian two's complement with the high bit of the first byte flipped.
            byte[] stored = (byte[])Scalar("Signed")!;
            byte[] twosComplement = row.Signed.ToBigEndianBytes();
            Assert.AreEqual(twosComplement[0] ^ 0x80, stored[0]);
            CollectionAssert.AreEqual(twosComplement[1..], stored[1..]);
        }
    }
}
