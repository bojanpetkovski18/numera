using Numera.Application.Entities;
using Numera.Application.Metrics;

namespace Numera.UnitTests;

public class MetricDefinitionsTests
{
    private static MetricLine Line(string order, int qty, decimal price, decimal discount, OrderStatus status) =>
        new(order, qty, price, discount, status);

    [Theory]
    [InlineData(2, "10.00", "0", "20.00")]
    [InlineData(1, "50.00", "0.10", "45.00")]
    [InlineData(1, "18.50", "0.333", "12.3395")]
    [InlineData(3, "0", "0.5", "0")]
    [InlineData(1, "99.99", "1", "0")]
    public void Line_revenue_is_quantity_times_price_times_one_minus_discount(int qty, string price, string discount, string expected) =>
        Assert.Equal(decimal.Parse(expected), MetricDefinitions.LineRevenue(qty, decimal.Parse(price), decimal.Parse(discount)));

    [Fact]
    public void Average_order_value_is_zero_without_orders() => Assert.Equal(0m, MetricDefinitions.AverageOrderValue(100m, 0));

    [Fact]
    public void Average_order_value_uses_unrounded_revenue_then_rounds_to_cents() =>
        Assert.Equal(34.33m, MetricDefinitions.AverageOrderValue(137.3395m, 4)); // 34.334875

    [Theory]
    [InlineData(0, 0, "0")]
    [InlineData(2, 5, "0.2857")]
    [InlineData(3, 0, "1")]
    [InlineData(0, 7, "0")]
    [InlineData(1, 1, "0.5")]
    public void Refund_rate_is_refunded_over_completed_plus_refunded(int refunded, int completed, string expected) =>
        Assert.Equal(decimal.Parse(expected), MetricDefinitions.RefundRate(refunded, completed));

    [Fact]
    public void Money_rounds_half_away_from_zero() => Assert.Equal(0.13m, MetricDefinitions.RoundMoney(0.125m));

    [Fact]
    public void Summarize_applies_every_definition_to_a_hand_computed_dataset()
    {
        // Completed lines: 20 + 45 + 30 + 30 + 12.3395 = 137.3395. Orders with a completed line: A-1, A-2, A-4, A-6.
        // Refunded: 2 lines. Cancelled: 1 line, excluded everywhere.
        var lines = new[]
        {
            Line("A-1", 2, 10.00m, 0m, OrderStatus.Completed),
            Line("A-1", 1, 50.00m, 0.10m, OrderStatus.Completed),
            Line("A-2", 3, 10.00m, 0m, OrderStatus.Completed),
            Line("A-3", 1, 18.50m, 0m, OrderStatus.Refunded),
            Line("A-4", 4, 10.00m, 0.25m, OrderStatus.Completed),
            Line("A-4", 2, 18.50m, 0m, OrderStatus.Cancelled),
            Line("A-5", 1, 50.00m, 0m, OrderStatus.Refunded),
            Line("A-6", 1, 18.50m, 0.333m, OrderStatus.Completed),
        };

        var kpis = MetricDefinitions.Summarize(lines);

        Assert.Equal(137.34m, kpis.Revenue);
        Assert.Equal(4, kpis.Orders);
        Assert.Equal(34.33m, kpis.AverageOrderValue);
        Assert.Equal(0.2857m, kpis.RefundRate); // 2 / 7
        Assert.Equal(5, kpis.CompletedLines);
        Assert.Equal(2, kpis.RefundedLines);
    }

    [Fact]
    public void An_order_with_only_refunded_or_cancelled_lines_is_not_counted()
    {
        var kpis = MetricDefinitions.Summarize(
        [
            Line("B-1", 1, 10m, 0m, OrderStatus.Refunded),
            Line("B-2", 1, 10m, 0m, OrderStatus.Cancelled),
        ]);

        Assert.Equal(0m, kpis.Revenue);
        Assert.Equal(0, kpis.Orders);
        Assert.Equal(0m, kpis.AverageOrderValue);
        Assert.Equal(1m, kpis.RefundRate);
    }

    [Fact]
    public void Empty_input_gives_zeros() => Assert.Equal(new KpiValues(0m, 0, 0m, 0m, 0, 0), MetricDefinitions.Summarize([]));
}

public class PeriodsTests
{
    [Theory]
    [InlineData("2025-01-01", "Week", "2024-12-30")] // Wednesday -> Monday before
    [InlineData("2025-01-05", "Week", "2024-12-30")] // Sunday belongs to the week that started Monday
    [InlineData("2025-01-06", "Week", "2025-01-06")] // Monday
    [InlineData("2025-03-31", "Month", "2025-03-01")]
    [InlineData("2025-03-31", "Day", "2025-03-31")]
    public void Periods_start_on_monday_or_the_first_of_the_month(string date, string granularity, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), Periods.Start(DateOnly.Parse(date), Enum.Parse<Granularity>(granularity)));

    [Theory]
    [InlineData("2025-01-01", "2025-01-31", "Week", 5)]
    [InlineData("2025-01-01", "2025-03-31", "Month", 3)]
    [InlineData("2024-12-15", "2025-01-15", "Month", 2)]
    [InlineData("2025-02-10", "2025-02-10", "Day", 1)]
    [InlineData("2024-10-01", "2026-09-30", "Day", 730)]
    public void Period_counts_include_both_ends(string from, string to, string granularity, int expected) =>
        Assert.Equal(expected, Periods.Count(DateOnly.Parse(from), DateOnly.Parse(to), Enum.Parse<Granularity>(granularity)));

    [Theory]
    [InlineData(null, true, Granularity.Month)]
    [InlineData("day", true, Granularity.Day)]
    [InlineData("WEEK", true, Granularity.Week)]
    [InlineData("year", false, Granularity.Month)]
    [InlineData("1", false, Granularity.Month)]
    public void Granularity_parsing_accepts_names_only(string? value, bool ok, Granularity expected)
    {
        Assert.Equal(ok, Periods.TryParseGranularity(value, out var granularity));
        if (ok)
        {
            Assert.Equal(expected, granularity);
        }
    }

    [Theory]
    [InlineData("category", true)]
    [InlineData("Region", true)]
    [InlineData("channel", true)]
    [InlineData("sku", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("0", false)]
    public void Breakdown_dimension_parsing(string? value, bool ok) => Assert.Equal(ok, Periods.TryParseDimension(value, out _));
}

public class OrderFilterParserTests
{
    private static OrderFilterInput Input(string? from = null, string? to = null, string[]? region = null, string[]? status = null) =>
        new(from, to, region, null, null, status);

    [Fact]
    public void An_empty_query_means_no_restriction()
    {
        var (filter, errors) = OrderFilterParser.Parse(Input(), allowStatus: false);

        Assert.Empty(errors);
        Assert.Null(filter.From);
        Assert.Null(filter.To);
        Assert.Empty(filter.Regions);
        Assert.Empty(filter.Channels);
        Assert.Empty(filter.Categories);
        Assert.Empty(filter.Statuses);
    }

    [Fact]
    public void Dates_are_parsed_and_lists_trimmed_and_deduplicated()
    {
        var (filter, errors) = OrderFilterParser.Parse(Input("2025-01-01", "2025-01-31", [" Europe ", "Europe", "", "Asia-Pacific"]), false);

        Assert.Empty(errors);
        Assert.Equal(new DateOnly(2025, 1, 1), filter.From);
        Assert.Equal(new DateOnly(2025, 1, 31), filter.To);
        Assert.Equal(["Europe", "Asia-Pacific"], filter.Regions);
    }

    [Fact]
    public void A_single_day_range_is_valid() => Assert.Empty(OrderFilterParser.Parse(Input("2025-02-10", "2025-02-10"), false).Errors);

    [Fact]
    public void From_after_to_is_rejected() => Assert.Contains("to", OrderFilterParser.Parse(Input("2025-02-01", "2025-01-01"), false).Errors.Keys);

    [Theory]
    [InlineData("01/02/2025")]
    [InlineData("2025-13-01")]
    [InlineData("yesterday")]
    public void Badly_formatted_dates_are_rejected(string date) =>
        Assert.Contains("from", OrderFilterParser.Parse(Input(from: date), false).Errors.Keys);

    [Fact]
    public void Status_is_rejected_for_metrics_and_parsed_for_the_grid()
    {
        Assert.Contains("status", OrderFilterParser.Parse(Input(status: ["refunded"]), allowStatus: false).Errors.Keys);

        var (filter, errors) = OrderFilterParser.Parse(Input(status: ["Refunded", "cancelled"]), allowStatus: true);
        Assert.Empty(errors);
        Assert.Equal([OrderStatus.Refunded, OrderStatus.Cancelled], filter.Statuses);

        Assert.Contains("status", OrderFilterParser.Parse(Input(status: ["shipped"]), allowStatus: true).Errors.Keys);
    }

    [Fact]
    public void Too_many_or_too_long_values_are_rejected()
    {
        var many = Enumerable.Range(0, OrderFilterParser.MaxValuesPerField + 1).Select(i => $"R{i}").ToArray();

        Assert.Contains("region", OrderFilterParser.Parse(Input(region: many), false).Errors.Keys);
        Assert.Contains("region", OrderFilterParser.Parse(Input(region: [new string('x', 61)]), false).Errors.Keys);
    }
}