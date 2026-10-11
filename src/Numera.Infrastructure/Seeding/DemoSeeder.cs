using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Numera.Infrastructure.Persistence;

namespace Numera.Infrastructure.Seeding;

public sealed record SeedResult(int RowsInserted, TimeSpan GenerateTime, TimeSpan InsertTime);

/// <summary>Clears order data and bulk-loads the deterministic demo dataset. Reused by the demo reset (M11).</summary>
public sealed class DemoSeeder(NumeraDbContext db, ILogger<DemoSeeder> logger)
{
    private const string CopyCommand =
        "COPY orders (order_id, order_date, customer_id, region, channel, product_sku, product_category, " +
        "quantity, unit_price, discount, status, import_job_id) FROM STDIN (FORMAT BINARY)";

    public async Task<SeedResult> ResetAndSeedAsync(SeedOptions? options = null, CancellationToken ct = default)
    {
        options ??= new SeedOptions();

        var generateWatch = Stopwatch.StartNew();
        var orders = DemoDataGenerator.Generate(options);
        generateWatch.Stop();

        var insertWatch = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(ct);

            // Fixed statement, no user input. Users are kept; imports are cleared with the orders they produced.
            await using (var truncate = new NpgsqlCommand(
                "TRUNCATE TABLE import_errors, import_jobs, orders RESTART IDENTITY", connection, transaction))
            {
                await truncate.ExecuteNonQueryAsync(ct);
            }

            await using (var writer = await connection.BeginBinaryImportAsync(CopyCommand, ct))
            {
                foreach (var o in orders)
                {
                    await writer.StartRowAsync(ct);
                    await writer.WriteAsync(o.OrderId, NpgsqlDbType.Varchar, ct);
                    await writer.WriteAsync(o.OrderDate, NpgsqlDbType.Date, ct);
                    await writer.WriteAsync(o.CustomerId, NpgsqlDbType.Varchar, ct);
                    await writer.WriteAsync(o.Region, NpgsqlDbType.Varchar, ct);
                    await writer.WriteAsync(o.Channel, NpgsqlDbType.Varchar, ct);
                    await writer.WriteAsync(o.ProductSku, NpgsqlDbType.Varchar, ct);
                    await writer.WriteAsync(o.ProductCategory, NpgsqlDbType.Varchar, ct);
                    await writer.WriteAsync(o.Quantity, NpgsqlDbType.Integer, ct);
                    await writer.WriteAsync(o.UnitPrice, NpgsqlDbType.Numeric, ct);
                    await writer.WriteAsync(o.Discount, NpgsqlDbType.Numeric, ct);
                    await writer.WriteAsync(OrderStatusMapping.ToDatabase(o.Status), NpgsqlDbType.Varchar, ct);
                    await writer.WriteNullAsync(ct);
                }

                await writer.CompleteAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        insertWatch.Stop();
        logger.LogInformation(
            "Seeded {Rows} order lines (generate {GenerateMs} ms, insert {InsertMs} ms)",
            orders.Count, generateWatch.ElapsedMilliseconds, insertWatch.ElapsedMilliseconds);
        return new SeedResult(orders.Count, generateWatch.Elapsed, insertWatch.Elapsed);
    }
}
