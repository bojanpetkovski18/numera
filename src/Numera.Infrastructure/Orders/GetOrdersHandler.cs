using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Numera.Application.Metrics;
using Numera.Application.Orders;
using Numera.Infrastructure.Metrics;
using Numera.Infrastructure.Persistence;

namespace Numera.Infrastructure.Orders;

/// <summary>
/// One page of order lines plus the exact total for the filter. Both statements share the filter's WHERE clause
/// and run in one round trip. COUNT(*) measured at most 13 ms on the 200,000-row seed, so it runs on every request.
/// </summary>
public sealed class GetOrdersHandler(NumeraDbContext db)
{
    private const string LineTotalSql = "quantity * unit_price * (1 - discount)";

    public async Task<OrdersPage> HandleAsync(OrdersQuery query, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();

            var where = OrderFilterSql.Where(query.Filter, command);
            if (query.Search is { } term)
            {
                // Case-insensitive "contains"; served by the pg_trgm index ix_orders_order_id_trgm.
                where += " AND order_id ILIKE @q";
                command.Parameters.Add(new NpgsqlParameter("q", NpgsqlDbType.Varchar) { Value = OrdersQueryParser.ContainsPattern(term) });
            }

            var direction = query.Descending ? "DESC" : "ASC";
            // Column from a fixed switch; id breaks ties so paging is stable.
            var orderBy = $"{SortColumn(query.Sort)} {direction}, id {direction}";
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = query.PageSize });
            command.Parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Bigint) { Value = (long)(query.Page - 1) * query.PageSize });

            command.CommandText = $"""
                SELECT id, order_id, order_date, customer_id, region, channel, product_sku, product_category,
                       quantity, unit_price, discount, status
                FROM orders
                {where}
                ORDER BY {orderBy}
                LIMIT @limit OFFSET @offset;
                SELECT COUNT(*) FROM orders {where};
                """;

            var items = new List<OrderRow>(query.PageSize);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var quantity = reader.GetInt32(8);
                var unitPrice = reader.GetDecimal(9);
                var discount = reader.GetDecimal(10);
                items.Add(new OrderRow(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetFieldValue<DateOnly>(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    quantity,
                    unitPrice,
                    discount,
                    MetricDefinitions.RoundMoney(MetricDefinitions.LineRevenue(quantity, unitPrice, discount)),
                    reader.GetString(11)));
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

    private static string SortColumn(OrderSortField field) => field switch
    {
        OrderSortField.OrderDate => "order_date",
        OrderSortField.OrderId => "order_id",
        OrderSortField.CustomerId => "customer_id",
        OrderSortField.Region => "region",
        OrderSortField.Channel => "channel",
        OrderSortField.Category => "product_category",
        OrderSortField.Sku => "product_sku",
        OrderSortField.Quantity => "quantity",
        OrderSortField.UnitPrice => "unit_price",
        OrderSortField.Discount => "discount",
        OrderSortField.LineTotal => LineTotalSql,
        OrderSortField.Status => "status",
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };
}