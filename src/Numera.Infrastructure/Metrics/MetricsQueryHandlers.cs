using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Numera.Application.Metrics;
using Numera.Infrastructure.Persistence;

namespace Numera.Infrastructure.Metrics;

/// <summary>
/// Shared SQL fragments. Line revenue is quantity x unit_price x (1 - discount); revenue and orders count completed
/// lines only. Sums stay exact numeric in PostgreSQL and are rounded once, in C#, by <see cref="MetricDefinitions"/>.
/// </summary>
internal static class MetricsSql
{
    public const string Revenue =
        "COALESCE(SUM(quantity * unit_price * (1 - discount)) FILTER (WHERE status = 'completed'), 0)";

    public const string Orders = "COUNT(DISTINCT order_id) FILTER (WHERE status = 'completed')";

    public static async Task<T> WithConnectionAsync<T>(NumeraDbContext db, Func<NpgsqlConnection, Task<T>> work, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            return await work((NpgsqlConnection)db.Database.GetDbConnection());
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}

public sealed class GetMetricsSummaryHandler(NumeraDbContext db)
{
    public Task<MetricsSummary> HandleAsync(OrderFilter filter, CancellationToken ct = default) =>
        MetricsSql.WithConnectionAsync(db, async connection =>
        {
            await using var command = connection.CreateCommand();
            var where = OrderFilterSql.Where(filter, command);
            command.CommandText = $"""
                SELECT {MetricsSql.Revenue} AS revenue,
                       {MetricsSql.Orders} AS orders,
                       COUNT(*) FILTER (WHERE status = 'completed') AS completed_lines,
                       COUNT(*) FILTER (WHERE status = 'refunded') AS refunded_lines
                FROM orders
                {where}
                """;
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var kpis = MetricDefinitions.FromTotals(
                reader.GetDecimal(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3));
            return new MetricsSummary(kpis.Revenue, kpis.Orders, kpis.AverageOrderValue, kpis.RefundRate, kpis.CompletedLines, kpis.RefundedLines);
        }, ct);
}

public sealed class GetRevenueTimeseriesHandler(NumeraDbContext db)
{
    /// <summary>
    /// Revenue and orders per period, with every period between the bounds present (zero when empty).
    /// Bounds are the filter's dates, or the first and last order date matching the filter when not given.
    /// Returns null when the requested range would exceed <see cref="Periods.MaxPoints"/>.
    /// </summary>
    public Task<RevenueTimeseries?> HandleAsync(OrderFilter filter, Granularity granularity, CancellationToken ct = default) =>
        MetricsSql.WithConnectionAsync(db, async connection =>
        {
            var (from, to) = (filter.From, filter.To);
            if (from is null || to is null)
            {
                var bounds = await DataBoundsAsync(connection, filter, ct);
                if (bounds is null)
                {
                    return new RevenueTimeseries(Name(granularity), []);
                }

                from ??= bounds.Value.First;
                to ??= bounds.Value.Last;
            }

            if (from > to)
            {
                return new RevenueTimeseries(Name(granularity), []);
            }

            if (Periods.Count(from.Value, to.Value, granularity) > Periods.MaxPoints)
            {
                return null;
            }

            await using var command = connection.CreateCommand();
            var where = OrderFilterSql.Where(filter, command);
            var unit = Name(granularity);
            // date_trunc on timestamp (not timestamptz) so the session time zone cannot shift buckets.
            command.CommandText = $"""
                WITH periods AS (
                    SELECT generate_series(
                               date_trunc('{unit}', @s_from::timestamp),
                               date_trunc('{unit}', @s_to::timestamp),
                               interval '1 {unit}')::date AS period_start
                ),
                totals AS (
                    SELECT date_trunc('{unit}', order_date::timestamp)::date AS period_start,
                           {MetricsSql.Revenue} AS revenue,
                           {MetricsSql.Orders} AS orders
                    FROM orders
                    {where}
                    GROUP BY 1
                )
                SELECT p.period_start, COALESCE(t.revenue, 0), COALESCE(t.orders, 0)
                FROM periods p
                LEFT JOIN totals t USING (period_start)
                ORDER BY p.period_start
                """;
            command.Parameters.Add(new NpgsqlParameter("s_from", NpgsqlDbType.Date) { Value = from.Value });
            command.Parameters.Add(new NpgsqlParameter("s_to", NpgsqlDbType.Date) { Value = to.Value });

            var points = new List<TimeseriesPoint>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                points.Add(new TimeseriesPoint(
                    reader.GetFieldValue<DateOnly>(0),
                    MetricDefinitions.RoundMoney(reader.GetDecimal(1)),
                    (int)reader.GetInt64(2)));
            }

            return new RevenueTimeseries(unit, points);
        }, ct);

    private static string Name(Granularity granularity) => granularity.ToString().ToLowerInvariant();

    private static async Task<(DateOnly First, DateOnly Last)?> DataBoundsAsync(NpgsqlConnection connection, OrderFilter filter, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT MIN(order_date), MAX(order_date) FROM orders {OrderFilterSql.Where(filter, command)}";
        await using DbDataReader reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return reader.IsDBNull(0) ? null : (reader.GetFieldValue<DateOnly>(0), reader.GetFieldValue<DateOnly>(1));
    }
}

public sealed class GetRevenueBreakdownHandler(NumeraDbContext db)
{
    public Task<RevenueBreakdown> HandleAsync(OrderFilter filter, BreakdownDimension by, CancellationToken ct = default) =>
        MetricsSql.WithConnectionAsync(db, async connection =>
        {
            var column = by switch
            {
                BreakdownDimension.Category => "product_category",
                BreakdownDimension.Region => "region",
                BreakdownDimension.Channel => "channel",
                _ => throw new ArgumentOutOfRangeException(nameof(by)),
            };

            await using var command = connection.CreateCommand();
            var where = OrderFilterSql.Where(filter, command);
            command.CommandText = $"""
                SELECT {column} AS key, {MetricsSql.Revenue} AS revenue, {MetricsSql.Orders} AS orders
                FROM orders
                {where}
                GROUP BY {column}
                ORDER BY revenue DESC, key
                """;

            var groups = new List<BreakdownGroup>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                groups.Add(new BreakdownGroup(reader.GetString(0), MetricDefinitions.RoundMoney(reader.GetDecimal(1)), (int)reader.GetInt64(2)));
            }

            return new RevenueBreakdown(by.ToString().ToLowerInvariant(), groups);
        }, ct);
}