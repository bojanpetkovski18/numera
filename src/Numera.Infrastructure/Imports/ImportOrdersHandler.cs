using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Numera.Application.Entities;
using Numera.Application.Imports;
using Numera.Infrastructure.Persistence;

namespace Numera.Infrastructure.Imports;

/// <summary>The result of an import. <see cref="Rejection"/> is set when the whole file was refused.</summary>
public sealed record ImportResult(ImportJobSummary Job, ImportRejection? Rejection);

/// <summary>
/// Command handler for importing an order CSV. Lives in Infrastructure because it relies on PostgreSQL bulk APIs.
///
/// Strategy: create the job row (committed), scan the file for header and row-count problems, then load all valid
/// rows, all error rows and the job's final counts in ONE transaction, so an import is all-or-nothing and a crash
/// never leaves half a file behind. Valid rows stream through one binary COPY into a temporary staging table and
/// are moved into <c>orders</c> with a plain INSERT; rows whose key already exists are first moved from the staging
/// table into <c>import_errors</c> by a single anti-join.
/// </summary>
public sealed class ImportOrdersHandler(NumeraDbContext db, TimeProvider clock, ILogger<ImportOrdersHandler> logger)
{
    private const int MaxRawRowLength = 2_000;
    private const int MaxMessageLength = 500;

    private const string CreateStageSql = """
        CREATE TEMP TABLE import_stage (
            row_number int NOT NULL,
            order_id varchar(40) NOT NULL,
            order_date date NOT NULL,
            customer_id varchar(40) NOT NULL,
            region varchar(60) NOT NULL,
            channel varchar(60) NOT NULL,
            product_sku varchar(40) NOT NULL,
            product_category varchar(60) NOT NULL,
            quantity int NOT NULL,
            unit_price numeric(12,2) NOT NULL,
            discount numeric(4,3) NOT NULL,
            status varchar(20) NOT NULL,
            raw_row text NOT NULL
        ) ON COMMIT DROP
        """;

    private const string CopyStageSql =
        "COPY import_stage (row_number, order_id, order_date, customer_id, region, channel, product_sku, " +
        "product_category, quantity, unit_price, discount, status, raw_row) FROM STDIN (FORMAT BINARY)";

    // Imports run one at a time (transaction-scoped advisory lock), so no other transaction can insert the same
    // keys between finding existing rows and inserting the rest. This replaces INSERT ... ON CONFLICT DO NOTHING,
    // whose per-row speculative insertion measured about 2.6x slower for 100,000 rows.
    private const long ImportLockKey = 0x4E554D455241; // "NUMERA"

    private const string LockSql = "SELECT pg_advisory_xact_lock(@key)";

    // Moves rows whose key already exists out of the staging table and into import_errors in one statement.
    private const string RejectExistingSql = """
        WITH existing AS (
            DELETE FROM import_stage s
            USING orders o
            WHERE o.order_id = s.order_id AND o.product_sku = s.product_sku
            RETURNING s.row_number, s.raw_row
        )
        INSERT INTO import_errors (import_job_id, row_number, field, message, raw_row)
        SELECT @job_id, row_number, 'order_id', @message, raw_row FROM existing
        """;

    private const string ExistingRowMessage =
        "This order_id and product_sku already exist in the data and were not imported again.";

    private const string MoveStageSql = """
        INSERT INTO orders (order_id, order_date, customer_id, region, channel, product_sku, product_category,
                            quantity, unit_price, discount, status, import_job_id)
        SELECT order_id, order_date, customer_id, region, channel, product_sku, product_category,
               quantity, unit_price, discount, status, @job_id
        FROM import_stage
        """;

    private const string CopyErrorsSql =
        "COPY import_errors (import_job_id, row_number, field, message, raw_row) FROM STDIN (FORMAT BINARY)";

    private const string CompleteJobSql = """
        UPDATE import_jobs
        SET status = 'completed', total_rows = @total, imported_rows = @imported, rejected_rows = @rejected,
            completed_at = @completed_at, warnings = @warnings
        WHERE id = @id
        """;

    // Time spent after streaming (duplicate check, insert, error rows, commit), for the completion log.
    // The handler is scoped to one request.
    private readonly Stopwatch _databaseTime = new();

    private sealed record ErrorRow(int RowNumber, string? Field, string Message, string RawRecord);

    public async Task<ImportResult> HandleAsync(Stream file, string fileName, CancellationToken ct = default)
    {
        var job = new ImportJob
        {
            FileName = fileName.Length > 260 ? fileName[..260] : fileName,
            Status = ImportJobStatus.Processing,
            CreatedAt = clock.GetUtcNow(),
        };
        db.ImportJobs.Add(job);
        await db.SaveChangesAsync(ct);

        var total = Stopwatch.StartNew();
        try
        {
            var scan = await ImportFileScanner.ScanAsync(file, ct: ct);
            var scanMs = total.ElapsedMilliseconds;
            var warnings = scan.Header?.Warnings.ToArray() ?? [];
            if (scan.Rejection is not null)
            {
                return await RejectAsync(job, warnings, scan.Rejection, ct);
            }

            file.Position = 0;
            var summary = await LoadAsync(job, file, warnings, ct);
            logger.LogInformation(
                "Import {ImportId} finished: {Total} rows, {Imported} imported, {Rejected} rejected " +
                "in {ElapsedMs} ms (scan {ScanMs} ms, final database steps {DatabaseMs} ms)",
                summary.Id, summary.TotalRows, summary.ImportedRows, summary.RejectedRows,
                total.ElapsedMilliseconds, scanMs, _databaseTime.ElapsedMilliseconds);
            return new ImportResult(summary, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import {ImportId} failed unexpectedly", job.Id);
            await MarkFailedAsync(job.Id, [], "The import failed unexpectedly. No rows were imported.", CancellationToken.None);
            throw;
        }
    }

    private async Task<ImportResult> RejectAsync(ImportJob job, string[] warnings, ImportRejection rejection, CancellationToken ct)
    {
        await MarkFailedAsync(job.Id, warnings, rejection.Message, ct);
        var summary = new ImportJobSummary(
            job.Id, job.FileName, "failed", 0, 0, 0, warnings, rejection.Message, job.CreatedAt, clock.GetUtcNow());
        return new ImportResult(summary, rejection);
    }

    private async Task<ImportJobSummary> LoadAsync(ImportJob job, Stream file, string[] warnings, CancellationToken ct)
    {
        var today = OrderRowValidator.LatestAllowedDate(clock.GetUtcNow());
        var errors = new List<ErrorRow>();
        var firstSeen = new Dictionary<(string OrderId, string Sku), int>();
        int total = 0, staged = 0, rejected = 0;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(ct);

            await using (var lockCommand = new NpgsqlCommand(LockSql, connection, transaction))
            {
                lockCommand.Parameters.AddWithValue("key", ImportLockKey);
                await lockCommand.ExecuteNonQueryAsync(ct);
            }

            await using (var create = new NpgsqlCommand(CreateStageSql, connection, transaction))
            {
                await create.ExecuteNonQueryAsync(ct);
            }

            // Valid rows stream straight into the staging table through one COPY; nothing is batched in memory.
            await using (var stage = await connection.BeginBinaryImportAsync(CopyStageSql, ct))
            {
                await using var reader = await OrderCsvReader.OpenAsync(file);
                await foreach (var record in reader.ReadRecordsAsync(ct))
                {
                    total++;

                    if (record.Row is null)
                    {
                        rejected++;
                        errors.Add(new ErrorRow(record.RowNumber, null, record.StructuralError!, record.RawRecord));
                        continue;
                    }

                    var result = OrderRowValidator.Validate(record.Row, today);
                    if (!result.IsValid)
                    {
                        rejected++;
                        errors.AddRange(result.Errors.Select(e => new ErrorRow(record.RowNumber, e.Field, e.Message, record.RawRecord)));
                        continue;
                    }

                    var order = result.Row!;
                    if (firstSeen.TryGetValue((order.OrderId, order.ProductSku), out var firstRow))
                    {
                        rejected++;
                        errors.Add(new ErrorRow(
                            record.RowNumber,
                            ImportColumns.OrderId,
                            $"Duplicate order_id and product_sku in this file; first seen on row {firstRow}.",
                            record.RawRecord));
                        continue;
                    }

                    firstSeen[(order.OrderId, order.ProductSku)] = record.RowNumber;
                    await WriteStagedRowAsync(stage, record.RowNumber, order, record.RawRecord, ct);
                    staged++;
                }

                await stage.CompleteAsync(ct);
            }

            _databaseTime.Start();

            int alreadyExisting;
            await using (var rejectExisting = new NpgsqlCommand(RejectExistingSql, connection, transaction))
            {
                rejectExisting.Parameters.AddWithValue("job_id", job.Id);
                rejectExisting.Parameters.AddWithValue("message", ExistingRowMessage);
                alreadyExisting = await rejectExisting.ExecuteNonQueryAsync(ct);
            }

            int imported;
            await using (var move = new NpgsqlCommand(MoveStageSql, connection, transaction))
            {
                move.Parameters.AddWithValue("job_id", job.Id);
                imported = await move.ExecuteNonQueryAsync(ct);
            }

            rejected += alreadyExisting;
            if (imported + alreadyExisting != staged)
            {
                throw new InvalidOperationException(
                    $"Import {job.Id}: staged {staged} rows but inserted {imported} and rejected {alreadyExisting}.");
            }

            await WriteErrorsAsync(connection, transaction, job.Id, errors, ct);

            var completedAt = clock.GetUtcNow();
            await using (var update = new NpgsqlCommand(CompleteJobSql, connection, transaction))
            {
                update.Parameters.AddWithValue("id", job.Id);
                update.Parameters.AddWithValue("total", total);
                update.Parameters.AddWithValue("imported", imported);
                update.Parameters.AddWithValue("rejected", rejected);
                update.Parameters.AddWithValue("completed_at", completedAt);
                update.Parameters.Add(new NpgsqlParameter("warnings", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = warnings });
                await update.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            _databaseTime.Stop();
            return new ImportJobSummary(
                job.Id, job.FileName, "completed", total, imported, rejected, warnings, null, job.CreatedAt, completedAt);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task WriteStagedRowAsync(
        NpgsqlBinaryImporter writer, int rowNumber, ValidatedOrderRow o, string rawRecord, CancellationToken ct)
    {
        await writer.StartRowAsync(ct);
        await writer.WriteAsync(rowNumber, NpgsqlDbType.Integer, ct);
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
        await writer.WriteAsync(Truncate(rawRecord, MaxRawRowLength), NpgsqlDbType.Text, ct);
    }
    private static async Task WriteErrorsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid jobId, List<ErrorRow> errors, CancellationToken ct)
    {
        if (errors.Count == 0)
        {
            return;
        }

        await using var writer = await connection.BeginBinaryImportAsync(CopyErrorsSql, ct);
        foreach (var error in errors.OrderBy(e => e.RowNumber))
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(jobId, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(error.RowNumber, NpgsqlDbType.Integer, ct);
            if (error.Field is null)
            {
                await writer.WriteNullAsync(ct);
            }
            else
            {
                await writer.WriteAsync(error.Field, NpgsqlDbType.Varchar, ct);
            }

            await writer.WriteAsync(Truncate(error.Message, MaxMessageLength), NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(Truncate(error.RawRecord, MaxRawRowLength), NpgsqlDbType.Text, ct);
        }

        await writer.CompleteAsync(ct);
    }

    private async Task MarkFailedAsync(Guid jobId, string[] warnings, string reason, CancellationToken ct)
    {
        var completedAt = clock.GetUtcNow();
        await db.ImportJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, ImportJobStatus.Failed)
                .SetProperty(j => j.FailureReason, reason)
                .SetProperty(j => j.Warnings, warnings)
                .SetProperty(j => j.CompletedAt, completedAt),
                ct);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}