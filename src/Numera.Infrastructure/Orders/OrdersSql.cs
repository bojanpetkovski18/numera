using System.Data.Common;
using Npgsql;
using NpgsqlTypes;
using Numera.Application.Metrics;
using Numera.Application.Orders;
using Numera.Infrastructure.Metrics;

namespace Numera.Infrastructure.Orders;

/// <summary>
/// The grid's row query, shared by the grid page and the CSV export, so the export holds exactly the rows the grid
/// shows, in the same order.
/// </summary>
internal static class OrdersSql
{
    private const string LineTotalSql = "quantity * unit_price * (1 - discount)";

    public const string Columns = """
        id, order_id, order_date, customer_id, region, channel, product_sku, product_category,
        quantity, unit_price, discount, status
        """;

    /// <summary>The WHERE clause for the filter and search; adds the parameters to <paramref name="command"/>.</summary>
    public static string Where(OrdersQuery query, NpgsqlCommand command)
    {
        var where = OrderFilterSql.Where(query.Filter, command);
        if (query.Search is { } term)
        {
            // Case-insensitive "contains"; served by the pg_trgm index ix_orders_order_id_trgm.
            where += " AND order_id ILIKE @q";
            command.Parameters.Add(new NpgsqlParameter("q", NpgsqlDbType.Varchar) { Value = OrdersQueryParser.ContainsPattern(term) });
        }
        return where;
    }

    /// <summary>Column from a fixed switch, never from input; id breaks ties so the order is total and stable.</summary>
    public static string OrderBy(OrdersQuery query)
    {
        var direction = query.Descending ? "DESC" : "ASC";
        return $"{SortColumn(query.Sort)} {direction}, id {direction}";
    }

    /// <summary>Reads one row selected with <see cref="Columns"/>.</summary>
    public static OrderRow Read(DbDataReader reader)
    {
        var quantity = reader.GetInt32(8);
        var unitPrice = reader.GetDecimal(9);
        var discount = reader.GetDecimal(10);
        return new OrderRow(
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
            reader.GetString(11));
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
