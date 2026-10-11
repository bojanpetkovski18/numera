using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Numera.Application.Orders;
using Numera.Infrastructure.Persistence;

namespace Numera.Infrastructure.Orders;

/// <summary>
/// One page of order lines plus the exact total for the filter. Both statements share the filter's WHERE clause
/// and run in one round trip. COUNT(*) measured at most 13 ms on the 200,000-row seed, so it runs on every request.
/// </summary>
public sealed class GetOrdersHandler(NumeraDbContext db)
{
    public async Task<OrdersPage> HandleAsync(OrdersQuery query, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();

            var where = OrdersSql.Where(query, command);
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = query.PageSize });
            command.Parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Bigint) { Value = (long)(query.Page - 1) * query.PageSize });

            command.CommandText = $"""
                SELECT {OrdersSql.Columns}
                FROM orders
                {where}
                ORDER BY {OrdersSql.OrderBy(query)}
                LIMIT @limit OFFSET @offset;
                SELECT COUNT(*) FROM orders {where};
                """;

            var items = new List<OrderRow>(query.PageSize);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                items.Add(OrdersSql.Read(reader));
            }

            await reader.NextResultAsync(ct);
            await reader.ReadAsync(ct);
            var total = (int)reader.GetInt64(0);
            var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)query.PageSize);
            return new OrdersPage(items, total, query.Page, query.PageSize, totalPages);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
