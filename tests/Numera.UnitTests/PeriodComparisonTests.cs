using Numera.Application.Metrics;

namespace Numera.UnitTests;

public class PeriodComparisonTests
{
    private static DateOnly D(string value) => DateOnly.Parse(value);

    private static OrderFilter Range(string? from, string? to) =>
        OrderFilter.None with { From = from is null ? null : D(from), To = to is null ? null : D(to) };

    [Theory]
    [InlineData("2026-07-01", "2026-09-30", "2026-03-31", "2026-06-30", 92)] // Q3 has 92 days, Q2 only 91: same length, not "the previous quarter"
    [InlineData("2026-03-15", "2026-03-15", "2026-03-14", "2026-03-14", 1)]  // one day
    [InlineData("2026-01-01", "2026-01-10", "2025-12-22", "2025-12-31", 10)] // across the year boundary
    [InlineData("2024-03-01", "2024-03-31", "2024-01-30", "2024-02-29", 31)] // includes a leap day
    public void Previous_period_is_the_same_number_of_days_just_before(string from, string to, string expectedFrom, string expectedTo, int shift)
    {
        var period = PeriodComparison.For(D(from), D(to), ComparisonMode.Previous)!;

        Assert.Equal(D(expectedFrom), period.From);
        Assert.Equal(D(expectedTo), period.To);
        Assert.Equal(D(to).DayNumber - D(from).DayNumber + 1, period.To.DayNumber - period.From.DayNumber + 1);
        Assert.Equal((0, shift), (period.ShiftYears, period.ShiftDays));
    }

    [Theory]
    [InlineData("2026-07-01", "2026-09-30", "2025-07-01", "2025-09-30")]
    [InlineData("2024-02-29", "2024-03-31", "2023-02-28", "2023-03-31")] // 29 February becomes 28 February
    [InlineData("2024-02-01", "2024-02-29", "2023-02-01", "2023-02-28")]
    [InlineData("2026-01-01", "2026-01-01", "2025-01-01", "2025-01-01")]
    public void Same_period_last_year_moves_both_dates_back_one_year(string from, string to, string expectedFrom, string expectedTo)
    {
        var period = PeriodComparison.For(D(from), D(to), ComparisonMode.Year)!;

        Assert.Equal((D(expectedFrom), D(expectedTo)), (period.From, period.To));
        Assert.Equal((1, 0), (period.ShiftYears, period.ShiftDays));
        Assert.Equal("year", period.Name);
    }

    [Fact]
    public void A_comparison_before_the_first_calendar_day_is_not_possible()
    {
        Assert.Null(PeriodComparison.For(D("0001-01-05"), D("0001-01-10"), ComparisonMode.Previous));
        Assert.Null(PeriodComparison.For(D("0001-06-01"), D("0001-06-30"), ComparisonMode.Year));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_compare_value_means_no_comparison(string? value)
    {
        var errors = new Dictionary<string, string[]>();
        Assert.Null(PeriodComparison.Parse(value, Range("2026-01-01", "2026-01-31"), errors));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("previous", ComparisonMode.Previous)]
    [InlineData(" Year ", ComparisonMode.Year)]
    public void Compare_values_are_case_insensitive(string value, ComparisonMode expected)
    {
        var errors = new Dictionary<string, string[]>();
        Assert.Equal(expected, PeriodComparison.Parse(value, Range("2026-01-01", "2026-01-31"), errors)!.Mode);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("lastyear")]
    [InlineData("0")]
    [InlineData("1")]
    public void Unknown_compare_values_are_rejected(string value)
    {
        var errors = new Dictionary<string, string[]>();
        Assert.Null(PeriodComparison.Parse(value, Range("2026-01-01", "2026-01-31"), errors));
        Assert.Contains("compare must be one of", errors["compare"][0]);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("2026-01-01", null)]
    [InlineData(null, "2026-01-31")]
    public void A_comparison_needs_both_dates(string? from, string? to)
    {
        var errors = new Dictionary<string, string[]>();
        Assert.Null(PeriodComparison.Parse("previous", Range(from, to), errors));
        Assert.Equal("A comparison needs both 'from' and 'to'.", errors["compare"][0]);
    }

    [Fact]
    public void Dates_in_the_wrong_order_are_left_to_the_filter_error()
    {
        var errors = new Dictionary<string, string[]>();
        Assert.Null(PeriodComparison.Parse("previous", Range("2026-02-01", "2026-01-01"), errors));
        Assert.Empty(errors);
    }

    // changes ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("112.40", "100.00", "0.124")]
    [InlineData("50", "100", "-0.5")]
    [InlineData("0", "80", "-1")]
    [InlineData("100", "100", "0")]
    [InlineData("1", "3", "-0.6667")] // rounded to 4 decimals, half away from zero
    [InlineData("2", "3", "-0.3333")]
    public void Relative_change_is_the_difference_over_the_comparison_value(string current, string comparison, string expected) =>
        Assert.Equal(decimal.Parse(expected), MetricDefinitions.RelativeChange(decimal.Parse(current), decimal.Parse(comparison)));

    [Fact]
    public void Relative_change_is_none_when_the_comparison_value_is_zero()
    {
        Assert.Null(MetricDefinitions.RelativeChange(25m, 0m));
        Assert.Null(MetricDefinitions.RelativeChange(0m, 0m));
    }

    [Fact]
    public void Changes_compare_every_kpi_and_the_refund_rate_in_points()
    {
        var current = MetricDefinitions.FromTotals(1100m, 11, 18, 2);   // AOV 100.00, refund rate 0.1
        var comparison = MetricDefinitions.FromTotals(1000m, 8, 19, 1); // AOV 125.00, refund rate 0.05

        var changes = MetricDefinitions.Changes(current, comparison);

        Assert.Equal(0.1m, changes.Revenue);
        Assert.Equal(0.375m, changes.Orders);
        Assert.Equal(-0.2m, changes.AverageOrderValue);
        Assert.Equal(0.05m, changes.RefundRateDifference); // 10% - 5% = 5 percentage points
    }

    [Fact]
    public void Refund_rate_change_is_none_when_either_period_has_no_completed_or_refunded_lines()
    {
        var some = MetricDefinitions.FromTotals(100m, 1, 3, 1);
        var none = MetricDefinitions.FromTotals(0m, 0, 0, 0);

        Assert.Null(MetricDefinitions.Changes(some, none).RefundRateDifference);
        Assert.Null(MetricDefinitions.Changes(none, some).RefundRateDifference);
        Assert.Equal(-1m, MetricDefinitions.Changes(none, some).Revenue);
        Assert.Null(MetricDefinitions.Changes(some, none).Revenue);
    }
}
