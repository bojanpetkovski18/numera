using System.Text;
using Npgsql;
using NpgsqlTypes;
using Numera.Application.Metrics;
using Numera.Infrastructure.Persistence;

namespace Numera.Infrastructure.Metrics;

/// <summary>
/// Turns an <see cref="OrderFilter"/> into a parameterised WHERE clause over <c>orders</c>. Column names are fixed
/// here; every user value is a parameter. Shared by the metrics queries and (M08) the orders grid.
/// </summary>
public static class OrderFilterSql
{
    public static string Where(OrderFilter filter, NpgsqlCommand command, string alias = "")
    {
        var prefix = alias.Length == 0 ? string.Empty : alias + ".";
        var sql = new StringBuilder("WHERE TRUE");

        if (filter.From is { } from)
        {
            sql.Append($" AND {prefix}order_date >= @f_from");
            command.Parameters.Add(new NpgsqlParameter("f_from", NpgsqlDbType.Date) { Value = from });
        }

        if (filter.To is { } to)
        {
            sql.Append($" AND {prefix}order_date <= @f_to");
            command.Parameters.Add(new NpgsqlParameter("f_to", NpgsqlDbType.Date) { Value = to });
        }

        AddAny(sql, command, $"{prefix}region", "f_regions", filter.Regions);
        AddAny(sql, command, $"{prefix}channel", "f_channels", filter.Channels);
        AddAny(sql, command, $"{prefix}product_category", "f_categories", filter.Categories);
        AddAny(sql, command, $"{prefix}status", "f_statuses", filter.Statuses.Select(OrderStatusMapping.ToDatabase).ToList());

        return sql.ToString();
    }

    private static void AddAny(StringBuilder sql, NpgsqlCommand command, string column, string parameter, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        sql.Append($" AND {column} = ANY(@{parameter})");
        command.Parameters.Add(new NpgsqlParameter(parameter, NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = values.ToArray() });
    }
}