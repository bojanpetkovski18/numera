namespace Numera.Application.Metrics;

/// <summary>One group's revenue in both periods and what it added to (or took from) the revenue change.</summary>
public sealed record Driver(string Key, decimal CurrentRevenue, decimal ComparisonRevenue, decimal Contribution, decimal? ShareOfChange);

/// <summary>
/// Which groups of one dimension drove the revenue change between the current and the comparison period.
/// <see cref="Change"/> equals the summary's revenue change for the same filter and comparison.
/// </summary>
public sealed record RevenueDrivers(
    string By,
    ComparisonDates Comparison,
    decimal CurrentRevenue,
    decimal ComparisonRevenue,
    decimal Change,
    decimal? RelativeChange,
    IReadOnlyList<Driver> Drivers);

/// <summary>A group's exact (unrounded) completed revenue in each period, as summed in SQL.</summary>
public sealed record GroupRevenue(string Key, decimal Current, decimal Comparison);

/// <summary>The "What changed?" definitions from docs/ARCHITECTURE.md.</summary>
public static class DriverDefinitions
{
    /// <summary>
    /// Contribution = current revenue - comparison revenue, from each group's revenue rounded to the cent (so it is
    /// the difference of the two amounts shown next to it). Share of change = contribution / total change, rounded to
    /// 4 decimals, null when the total change is 0. Sorted by absolute contribution, largest first, then by key.
    /// Groups with no completed revenue in either period are left out: they contribute nothing.
    /// </summary>
    public static RevenueDrivers Explain(string by, ComparisonPeriod comparison, IEnumerable<GroupRevenue> groups)
    {
        var list = groups.ToList();
        var current = MetricDefinitions.RoundMoney(list.Sum(g => g.Current));
        var before = MetricDefinitions.RoundMoney(list.Sum(g => g.Comparison));
        var change = current - before;

        var drivers = list
            .Where(g => g.Current != 0m || g.Comparison != 0m)
            .Select(g =>
            {
                var now = MetricDefinitions.RoundMoney(g.Current);
                var then = MetricDefinitions.RoundMoney(g.Comparison);
                var contribution = now - then;
                decimal? share = change == 0m ? null : decimal.Round(contribution / change, 4, MidpointRounding.AwayFromZero);
                return new Driver(g.Key, now, then, contribution, share);
            })
            .OrderByDescending(d => Math.Abs(d.Contribution))
            .ThenBy(d => d.Key, StringComparer.Ordinal)
            .ToList();

        return new RevenueDrivers(
            by,
            new ComparisonDates(comparison.Name, comparison.From, comparison.To),
            current,
            before,
            change,
            MetricDefinitions.RelativeChange(current, before),
            drivers);
    }
}
