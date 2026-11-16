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

    /// <summary>The column for a dimension; from this fixed switch, never from input.</summary>
    public static string Column(BreakdownDimension by) => by switch
    {
        BreakdownDimension.Category => "product_category",
        BreakdownDimension.Region => "region",
        BreakdownDimension.Channel => "channel",
        _ => throw new ArgumentOutOfRangeException(nameof(by)),
    };

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
    /// <summary>
    /// The KPIs for the filter and, with a comparison, the same KPIs for the comparison dates (the rest of the
    /// filter unchanged) and the changes between them. Each period is its own query, so the comparison values are
    /// exactly what the summary returns when asked for those dates directly.
    /// </summary>
    public Task<MetricsSummary> HandleAsync(OrderFilter filter, ComparisonPeriod? comparison = null, CancellationToken ct = default) =>
        MetricsSql.WithConnectionAsync(db, async connection =>
        {
            var kpis = await KpisAsync(connection, filter, ct);
            if (comparison is null)
            {
                return new MetricsSummary(kpis.Revenue, kpis.Orders, kpis.AverageOrderValue, kpis.RefundRate, kpis.CompletedLines, kpis.RefundedLines);
            }

            var before = await KpisAsync(connection, filter with { From = comparison.From, To = comparison.To }, ct);
            return new MetricsSummary(
                kpis.Revenue, kpis.Orders, kpis.AverageOrderValue, kpis.RefundRate, kpis.CompletedLines, kpis.RefundedLines,
                new SummaryComparison(comparison.Name, comparison.From, comparison.To,
                    before.Revenue, before.Orders, before.AverageOrderValue, before.RefundRate, before.CompletedLines, before.RefundedLines),
                MetricDefinitions.Changes(kpis, before));
        }, ct);

    private static async Task<KpiValues> KpisAsync(NpgsqlConnection connection, OrderFilter filter, CancellationToken ct)
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
        return MetricDefinitions.FromTotals(
            reader.GetDecimal(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3));
    }
}

public sealed class GetRevenueTimeseriesHandler(NumeraDbContext db)
{
    /// <summary>
    /// Revenue and orders per period, with every period between the bounds present (zero when empty).
    /// Bounds are the filter's dates, or the first and last order date matching the filter when not given.
    /// Returns null when the requested range would exceed <see cref="Periods.MaxPoints"/>.
    /// With a comparison (which implies explicit bounds), every point also carries the comparison period's values
    /// for the days that line up with it.
    /// </summary>
    public Task<RevenueTimeseries?> HandleAsync(
        OrderFilter filter, Granularity granularity, ComparisonPeriod? comparison = null, CancellationToken ct = default) =>
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
            var unit = Name(granularity); // from the enum, never from input
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

            if (comparison is null)
            {
                return new RevenueTimeseries(unit, points);
            }

            await reader.DisposeAsync();
            var before = await ShiftedTotalsAsync(connection, filter with { From = comparison.From, To = comparison.To }, unit, comparison, from.Value, ct);
            var withComparison = points
                .Select(p => before.TryGetValue(p.PeriodStart, out var c)
                    ? p with { ComparisonRevenue = c.Revenue, ComparisonOrders = c.Orders }
                    : p with { ComparisonRevenue = 0m, ComparisonOrders = 0 })
                .ToList();
            return new RevenueTimeseries(unit, withComparison, new ComparisonDates(comparison.Name, comparison.From, comparison.To));
        }, ct);

    /// <summary>
    /// The comparison period's revenue and orders, each day moved forward by the comparison's shift and grouped into
    /// the current period it lands in. The shifted days fall inside the current range, so every key is one of its periods.
    /// One exception: when the range starts on 29 February, the comparison starts on 28 February, which moves to the
    /// day before <paramref name="currentFrom"/>; it is counted in the first period, so no comparison revenue is lost.
    /// </summary>
    private static async Task<Dictionary<DateOnly, (decimal Revenue, int Orders)>> ShiftedTotalsAsync(
        NpgsqlConnection connection, OrderFilter comparisonFilter, string unit, ComparisonPeriod comparison, DateOnly currentFrom,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        var where = OrderFilterSql.Where(comparisonFilter, command);
        // An order's lines share its date, so counting distinct order IDs per shifted period stays correct.
        command.CommandText = $"""
            SELECT date_trunc('{unit}', GREATEST(order_date::timestamp + make_interval(years => @c_years, days => @c_days),
                                                 @c_floor::timestamp))::date AS period_start,
                   {MetricsSql.Revenue} AS revenue,
                   {MetricsSql.Orders} AS orders
            FROM orders
            {where}
            GROUP BY 1
            """;
        command.Parameters.Add(new NpgsqlParameter("c_years", NpgsqlDbType.Integer) { Value = comparison.ShiftYears });
        command.Parameters.Add(new NpgsqlParameter("c_days", NpgsqlDbType.Integer) { Value = comparison.ShiftDays });
        command.Parameters.Add(new NpgsqlParameter("c_floor", NpgsqlDbType.Date) { Value = currentFrom });

        var totals = new Dictionary<DateOnly, (decimal, int)>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            totals[reader.GetFieldValue<DateOnly>(0)] = (MetricDefinitions.RoundMoney(reader.GetDecimal(1)), (int)reader.GetInt64(2));
        }

        return totals;
    }

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

public sealed class GetFilterOptionsHandler(NumeraDbContext db)
{
    public Task<FilterOptions> HandleAsync(CancellationToken ct = default) =>
        MetricsSql.WithConnectionAsync(db, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT 'region' AS kind, region AS value FROM orders GROUP BY region
                UNION ALL
                SELECT 'channel', channel FROM orders GROUP BY channel
                UNION ALL
                SELECT 'category', product_category FROM orders GROUP BY product_category;
                SELECT MIN(order_date), MAX(order_date) FROM orders;
                """;

            var regions = new List<string>();
            var channels = new List<string>();
            var categories = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var target = reader.GetString(0) switch
                {
                    "region" => regions,
                    "channel" => channels,
                    _ => categories,
                };
                target.Add(reader.GetString(1));
            }

            await reader.NextResultAsync(ct);
            await reader.ReadAsync(ct);
            DateOnly? first = reader.IsDBNull(0) ? null : reader.GetFieldValue<DateOnly>(0);
            DateOnly? last = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1);

            static List<string> Sorted(List<string> values) => values.Order(StringComparer.OrdinalIgnoreCase).ToList();
            return new FilterOptions(Sorted(regions), Sorted(channels), Sorted(categories), first, last);
        }, ct);
}

public sealed class GetRevenueDriversHandler(NumeraDbContext db)
{
    /// <summary>
    /// Completed revenue per group in the current and the comparison period, in one scan over both date ranges
    /// (the rest of the filter applies to both), then explained by <see cref="DriverDefinitions"/>.
    /// </summary>
    public Task<RevenueDrivers> HandleAsync(OrderFilter filter, BreakdownDimension by, ComparisonPeriod comparison, CancellationToken ct = default) =>
        MetricsSql.WithConnectionAsync(db, async connection =>
        {
            if (filter.From is not { } from || filter.To is not { } to)
            {
                throw new ArgumentException("A comparison needs both dates; the endpoint validates this.", nameof(filter));
            }

            var column = MetricsSql.Column(by);
            await using var command = connection.CreateCommand();
            // The dates are handled here, so the shared WHERE clause gets the filter without them.
            var where = OrderFilterSql.Where(filter with { From = null, To = null }, command);
            const string lineRevenue = "quantity * unit_price * (1 - discount)";
            command.CommandText = $"""
                SELECT {column} AS key,
                       COALESCE(SUM({lineRevenue}) FILTER (WHERE status = 'completed' AND order_date BETWEEN @d_from AND @d_to), 0) AS current,
                       COALESCE(SUM({lineRevenue}) FILTER (WHERE status = 'completed' AND order_date BETWEEN @d_cfrom AND @d_cto), 0) AS comparison
                FROM orders
                {where} AND (order_date BETWEEN @d_from AND @d_to OR order_date BETWEEN @d_cfrom AND @d_cto)
                GROUP BY {column}
                """;
            command.Parameters.Add(new NpgsqlParameter("d_from", NpgsqlDbType.Date) { Value = from });
            command.Parameters.Add(new NpgsqlParameter("d_to", NpgsqlDbType.Date) { Value = to });
            command.Parameters.Add(new NpgsqlParameter("d_cfrom", NpgsqlDbType.Date) { Value = comparison.From });
            command.Parameters.Add(new NpgsqlParameter("d_cto", NpgsqlDbType.Date) { Value = comparison.To });

            var groups = new List<GroupRevenue>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                groups.Add(new GroupRevenue(reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2)));
            }

            return DriverDefinitions.Explain(by.ToString().ToLowerInvariant(), comparison, groups);
        }, ct);
}

public sealed class GetRevenueBreakdownHandler(NumeraDbContext db)
{
    public Task<RevenueBreakdown> HandleAsync(OrderFilter filter, BreakdownDimension by, CancellationToken ct = default) =>
        MetricsSql.WithConnectionAsync(db, async connection =>
        {
            var column = MetricsSql.Column(by);

            await using var command = connection.CreateCommand();
            var where = OrderFilterSql.Where(filter, command);
            // Two-level aggregation: per (group, order) first, then per group. Avoids sorting for
            // COUNT(DISTINCT order_id) within each group; measured 182 ms -> 133 ms (median) for a one-year
            // breakdown on the 200,000-row seed, same results. The summary and timeseries keep COUNT(DISTINCT),
            // which measured faster for them. {column} comes from the fixed switch above, never from input.
            command.CommandText = $"""
                SELECT key,
                       COALESCE(SUM(revenue), 0) AS revenue,
                       COUNT(*) FILTER (WHERE has_completed) AS orders
                FROM (
                    SELECT {column} AS key,
                           order_id,
                           SUM(quantity * unit_price * (1 - discount)) FILTER (WHERE status = 'completed') AS revenue,
                           bool_or(status = 'completed') AS has_completed
                    FROM orders
                    {where}
                    GROUP BY {column}, order_id
                ) per_order
                GROUP BY key
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