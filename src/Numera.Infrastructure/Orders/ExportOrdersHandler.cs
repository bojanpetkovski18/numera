using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Numera.Application.Orders;
using Numera.Infrastructure.Persistence;

namespace Numera.Infrastructure.Orders;

/// <summary>
/// CSV export of the grid's rows: same filter, search and sort, all pages. Runs in two steps so the caller can
/// refuse an oversized export with a normal error response before any CSV is sent:
/// <see cref="StartAsync"/> opens a read-only REPEATABLE READ snapshot and counts the rows;
/// <see cref="OrdersExport.WriteAsync"/> then streams exactly those rows from the same snapshot.
/// Rows go from the database reader straight to the output stream; the result is never held in memory.
/// </summary>
public sealed class ExportOrdersHandler(NumeraDbContext db)
{
    /// <summary>The largest export; the same as the import row limit.</summary>
    public const int MaxRows = 100_000;

    public async Task<OrdersExport> StartAsync(OrdersQuery query, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        NpgsqlTransaction? transaction = null;
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            {
                await readOnly.ExecuteNonQueryAsync(ct);
            }

            await using var count = new NpgsqlCommand { Connection = connection, Transaction = transaction };
            count.CommandText = $"SELECT COUNT(*) FROM orders {OrdersSql.Where(query, count)}";
            var rows = (long)(await count.ExecuteScalarAsync(ct))!;

            return new OrdersExport(db, connection, transaction, query, rows);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
            await db.Database.CloseConnectionAsync();
            throw;
        }
    }
}

/// <summary>An export in progress. Dispose it to end the snapshot and release the connection.</summary>
public sealed class OrdersExport : IAsyncDisposable
{
    private readonly NumeraDbContext db;
    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;
    private readonly OrdersQuery query;

    internal OrdersExport(NumeraDbContext db, NpgsqlConnection connection, NpgsqlTransaction transaction, OrdersQuery query, long rowCount)
    {
        this.db = db;
        this.connection = connection;
        this.transaction = transaction;
        this.query = query;
        RowCount = rowCount;
    }

    /// <summary>Rows the file will contain (counted in the same snapshot that is streamed).</summary>
    public long RowCount { get; }

    /// <summary>Writes the CSV: UTF-8 with a BOM (so Excel reads non-ASCII text correctly), header row, CRLF lines.</summary>
    public async Task WriteAsync(Stream output, CancellationToken ct = default)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 64 * 1024, leaveOpen: true);
        await writer.WriteAsync(OrdersCsv.Header + "\r\n");

        await using var command = new NpgsqlCommand { Connection = connection, Transaction = transaction };
        command.CommandText = $"""
            SELECT {OrdersSql.Columns}
            FROM orders
            {OrdersSql.Where(query, command)}
            ORDER BY {OrdersSql.OrderBy(query)}
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            await writer.WriteAsync(OrdersCsv.Row(OrdersSql.Read(reader)).AsMemory(), ct);
        }
        await writer.FlushAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync(); // read-only: rolling back is the normal end
        await db.Database.CloseConnectionAsync();
    }
}
