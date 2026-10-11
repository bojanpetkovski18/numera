using Microsoft.EntityFrameworkCore;
using Npgsql;
using Numera.Application.Entities;
using Numera.Infrastructure.Persistence;
using Numera.Infrastructure.Seeding;

namespace Numera.IntegrationTests;

public class DatabaseTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static Order NewOrder(string orderId = "HP-000001", string sku = "HP-SO-ETH", int quantity = 1, decimal unitPrice = 10m, decimal discount = 0m) => new()
    {
        OrderId = orderId,
        OrderDate = new DateOnly(2026, 1, 15),
        CustomerId = "C-00001",
        Region = "Europe",
        Channel = "Online Store",
        ProductSku = sku,
        ProductCategory = "Single Origin",
        Quantity = quantity,
        UnitPrice = unitPrice,
        Discount = discount,
        Status = OrderStatus.Completed,
    };

    private static DemoSeeder CreateSeeder(NumeraDbContext db) =>
        new(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<DemoSeeder>.Instance);

    private static async Task<List<string>> QueryStringsAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    [Fact]
    public async Task Migration_applies_to_a_fresh_database_and_creates_tables_indexes_and_checks()
    {
        var cs = await postgres.CreateDatabaseAsync();

        var tables = await QueryStringsAsync(cs, "select table_name from information_schema.tables where table_schema = 'public'");
        Assert.Contains("orders", tables);
        Assert.Contains("import_jobs", tables);
        Assert.Contains("import_errors", tables);
        Assert.Contains("users", tables);

        var indexes = await QueryStringsAsync(cs, "select indexname from pg_indexes where tablename = 'orders'");
        foreach (var expected in new[]
                 {
                     "ux_orders_order_id_product_sku", "ix_orders_order_date", "ix_orders_region",
                     "ix_orders_channel", "ix_orders_product_category", "ix_orders_status",
                 })
        {
            Assert.Contains(expected, indexes);
        }

        var checks = await QueryStringsAsync(cs, "select conname from pg_constraint where conrelid = 'orders'::regclass and contype = 'c'");
        Assert.Contains("ck_orders_quantity_positive", checks);
        Assert.Contains("ck_orders_unit_price_non_negative", checks);
        Assert.Contains("ck_orders_discount_range", checks);
        Assert.Contains("ck_orders_status_valid", checks);
    }

    [Fact]
    public async Task Unique_constraint_rejects_duplicate_order_id_and_sku()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(cs);
        db.Orders.Add(NewOrder());
        await db.SaveChangesAsync();

        db.Orders.Add(NewOrder());
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
    }

    [Fact]
    public async Task Same_order_id_with_different_sku_is_allowed()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(cs);

        db.Orders.AddRange(NewOrder(sku: "HP-SO-ETH"), NewOrder(sku: "HP-BL-HAR"));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Orders.CountAsync());
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(1, 10, 1.5)]
    public async Task Check_constraints_reject_invalid_values(int quantity, double unitPrice, double discount)
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(cs);
        db.Orders.Add(NewOrder(quantity: quantity, unitPrice: (decimal)unitPrice, discount: (decimal)discount));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, pg.SqlState);
    }

    [Fact]
    public async Task Seeding_inserts_the_expected_number_of_rows()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(cs);

        var result = await CreateSeeder(db).ResetAndSeedAsync(new SeedOptions { OrderLineCount = 10_000 });

        Assert.Equal(10_000, result.RowsInserted);
        Assert.Equal(10_000, await db.Orders.CountAsync());
        Assert.Equal(DemoDataGenerator.Generate(new SeedOptions { OrderLineCount = 10_000 }).Select(o => o.OrderId).Distinct().Count(),
            await db.Orders.Select(o => o.OrderId).Distinct().CountAsync());
    }

    [Fact]
    public async Task Reseeding_after_a_reset_yields_identical_data()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(cs);
        var seeder = CreateSeeder(db);
        var options = new SeedOptions { OrderLineCount = 10_000 };
        const string checksum =
            "select md5(string_agg(order_id || product_sku || order_date || quantity || unit_price || discount || status || region || channel || customer_id, ',' order by id)) from orders";

        await seeder.ResetAndSeedAsync(options);
        var first = (await QueryStringsAsync(cs, checksum)).Single();
        await seeder.ResetAndSeedAsync(options);
        var second = (await QueryStringsAsync(cs, checksum)).Single();

        Assert.Equal(first, second);
        Assert.Equal(10_000, await db.Orders.CountAsync());
    }

    [Fact]
    public async Task Reset_clears_import_data_and_keeps_users()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(cs);
        var job = new ImportJob { FileName = "sample.csv", Status = ImportJobStatus.Completed, CreatedAt = DateTimeOffset.UtcNow };
        db.ImportJobs.Add(job);
        db.Users.Add(new User { Email = "demo@example.test", PasswordHash = "x", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.ImportErrors.Add(new ImportError { ImportJobId = job.Id, RowNumber = 2, Field = "quantity", Message = "bad", RawRow = "a,b" });
        await db.SaveChangesAsync();

        await CreateSeeder(db).ResetAndSeedAsync(new SeedOptions { OrderLineCount = 100 });

        Assert.Equal(0, await db.ImportJobs.CountAsync());
        Assert.Equal(0, await db.ImportErrors.CountAsync());
        Assert.Equal(1, await db.Users.CountAsync());
    }
}
