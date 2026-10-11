namespace Numera.Application.Metrics;

public enum Granularity
{
    Day,
    Week,
    Month,
}

public enum BreakdownDimension
{
    Category,
    Region,
    Channel,
}

public sealed record MetricsSummary(
    decimal Revenue,
    int Orders,
    decimal AverageOrderValue,
    decimal RefundRate,
    int CompletedLines,
    int RefundedLines);

public sealed record TimeseriesPoint(DateOnly PeriodStart, decimal Revenue, int Orders);

public sealed record RevenueTimeseries(string Granularity, IReadOnlyList<TimeseriesPoint> Points);

public sealed record BreakdownGroup(string Key, decimal Revenue, int Orders);

public sealed record RevenueBreakdown(string By, IReadOnlyList<BreakdownGroup> Groups);

/// <summary>Values the filter bar can offer, taken from the data, plus the data's date range.</summary>
public sealed record FilterOptions(
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> Channels,
    IReadOnlyList<string> Categories,
    DateOnly? FirstOrderDate,
    DateOnly? LastOrderDate);

/// <summary>Period arithmetic shared by validation and tests. Weeks start on Monday (ISO 8601).</summary>
public static class Periods
{
    /// <summary>Upper bound on points per timeseries; protects the API and the chart.</summary>
    public const int MaxPoints = 1_000;

    public static DateOnly Start(DateOnly date, Granularity granularity) => granularity switch
    {
        Granularity.Day => date,
        Granularity.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        Granularity.Month => new DateOnly(date.Year, date.Month, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(granularity)),
    };

    public static int Count(DateOnly from, DateOnly to, Granularity granularity)
    {
        var first = Start(from, granularity);
        var last = Start(to, granularity);
        return granularity switch
        {
            Granularity.Day => last.DayNumber - first.DayNumber + 1,
            Granularity.Week => ((last.DayNumber - first.DayNumber) / 7) + 1,
            Granularity.Month => ((last.Year - first.Year) * 12) + last.Month - first.Month + 1,
            _ => throw new ArgumentOutOfRangeException(nameof(granularity)),
        };
    }

    public static bool TryParseGranularity(string? value, out Granularity granularity)
    {
        granularity = Granularity.Month;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out granularity)
            && Enum.IsDefined(granularity)
            && !int.TryParse(value, out _);
    }

    public static bool TryParseDimension(string? value, out BreakdownDimension dimension)
    {
        dimension = BreakdownDimension.Category;
        return !string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out dimension)
            && Enum.IsDefined(dimension)
            && !int.TryParse(value, out _);
    }
}