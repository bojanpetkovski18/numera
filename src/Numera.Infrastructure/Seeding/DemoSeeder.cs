using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Numera.Infrastructure.Persistence;

namespace Numera.Infrastructure.Seeding;

public sealed record SeedResult(int RowsInserted, TimeSpan GenerateTime, TimeSpan InsertTime);

/// <summary>
/// Clears order data and bulk-loads the deterministic demo dataset, in one transaction. Used by the <c>seed</c>
/// command and the demo reset. Holds the order-write lock (<see cref="DataLocks.OrderWrites"/>), so it never runs
/// at the same time as an import or another reset.
/// </summary>
public sealed class DemoSeeder(NumeraDbContext db, ILogger<DemoSeeder> logger)
{
    private const string CopyCommand =
        "COPY orders (order_id, order_date, customer_id, region, channel, product_sku, product_category, " +
        "quantity, unit_price, discount, status, import_job_id) FROM STDIN (FORMAT BINARY)";

    // Fixed statements, no user input. Users are kept. Import history goes with the data it produced, except the
    // job row of an import that has started but not yet taken the lock (it is still scanning its file): removing
    // that row would make the import fail when it inserts its orders.
    private const string ClearSql = """
        TRUNCATE TABLE import_errors, orders RESTART IDENTITY;
        DELETE FROM import_jobs WHERE status <> 'processing';
        """;

    /// <summary>For the <c>seed</c> command: waits for a running import or reset to finish first.</summary>
    public async Task<SeedResult> ResetAndSeedAsync(SeedOptions? options = null, CancellationToken ct = default) =>
        (await RunAsync(options, waitForLock: true, ct))!;

    /// <summary>For the demo reset endpoint: returns null at once if an import or another reset is running.</summary>
    public Task<SeedResult?> TryResetAndSeedAsync(SeedOptions? options = null, CancellationToken ct = default) =>
        RunAsync(options, waitForLock: false, ct);

    private async Task<SeedResult?> RunAsync(SeedOptions? options, bool waitForLock, CancellationToken ct)
    {
        options ??= new SeedOptions();

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(ct);

            await using (var lockCommand = new NpgsqlCommand(waitForLock ? DataLocks.WaitSql : DataLocks.TrySql, connection, transaction))
            {
                lockCommand.Parameters.AddWithValue("key", DataLocks.OrderWrites);
                var acquired = await lockCommand.ExecuteScalarAsync(ct);
                if (!waitForLock && acquired is false)
                {
                    logger.LogInformation("Demo reset skipped: an import or another reset is running");
                    return null;
                }
            }

            // Generated after taking the lock, so a refused reset does not build 200,000 rows for nothing.
            var generateWatch = Stopwatch.StartNew();
            var orders = DemoDataGenerator.Generate(options);
            generateWatch.Stop();

            var insertWatch = Stopwatch.StartNew();
            await using (var clear = new NpgsqlCommand(ClearSql, connection, transaction))
            {
                await clear.ExecuteNonQueryAsync(ct);
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
            insertWatch.Stop();

            logger.LogInformation(
                "Seeded {Rows} order lines (generate {GenerateMs} ms, insert {InsertMs} ms)",
                orders.Count, generateWatch.ElapsedMilliseconds, insertWatch.ElapsedMilliseconds);
            return new SeedResult(orders.Count, generateWatch.Elapsed, insertWatch.Elapsed);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
