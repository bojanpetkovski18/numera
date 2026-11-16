using Numera.Application.Metrics;

namespace Numera.UnitTests;

public class DriverDefinitionsTests
{
    private static readonly ComparisonPeriod Previous =
        PeriodComparison.For(new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31), ComparisonMode.Previous)!;

    private static RevenueDrivers Explain(params GroupRevenue[] groups) => DriverDefinitions.Explain("region", Previous, groups);

    [Fact]
    public void Contributions_are_each_groups_change_and_add_up_to_the_total_change()
    {
        var result = Explain(
            new GroupRevenue("Europe", 500m, 300m),
            new GroupRevenue("North America", 200m, 250m),
            new GroupRevenue("Asia-Pacific", 100m, 100m));

        Assert.Equal((800m, 650m, 150m, 0.2308m), (result.CurrentRevenue, result.ComparisonRevenue, result.Change, result.RelativeChange!.Value));
        Assert.Equal(
        [
            new Driver("Europe", 500m, 300m, 200m, 1.3333m),       // 200 / 150
            new Driver("North America", 200m, 250m, -50m, -0.3333m), // pulled the other way
            new Driver("Asia-Pacific", 100m, 100m, 0m, 0m),
        ], result.Drivers);
        Assert.Equal(result.Change, result.Drivers.Sum(d => d.Contribution));
        Assert.Equal(new ComparisonDates("previous", new DateOnly(2025, 1, 29), new DateOnly(2025, 2, 28)), result.Comparison);
    }

    [Fact]
    public void Groups_that_appear_or_disappear_count_as_zero_in_the_other_period()
    {
        var result = Explain(new GroupRevenue("New", 40m, 0m), new GroupRevenue("Gone", 0m, 90m));

        Assert.Equal([new Driver("Gone", 0m, 90m, -90m, 1.8m), new Driver("New", 40m, 0m, 40m, -0.8m)], result.Drivers);
        Assert.Equal(-50m, result.Change);
    }

    [Fact]
    public void Ties_on_size_are_ordered_by_name_whatever_the_sign()
    {
        var result = Explain(new GroupRevenue("b", 10m, 0m), new GroupRevenue("a", 0m, 10m), new GroupRevenue("c", 30m, 0m));

        Assert.Equal(["c", "a", "b"], result.Drivers.Select(d => d.Key));
    }

    [Fact]
    public void Without_a_total_change_there_is_no_share()
    {
        var result = Explain(new GroupRevenue("Europe", 60m, 40m), new GroupRevenue("Asia-Pacific", 40m, 60m));

        Assert.Equal(0m, result.Change);
        Assert.Equal(0m, result.RelativeChange);
        Assert.All(result.Drivers, d => Assert.Null(d.ShareOfChange));
    }

    [Fact]
    public void Groups_without_revenue_in_either_period_are_left_out_and_an_empty_comparison_has_no_relative_change()
    {
        var result = Explain(new GroupRevenue("Refunds only", 0m, 0m), new GroupRevenue("Europe", 25m, 0m));

        Assert.Equal(["Europe"], result.Drivers.Select(d => d.Key));
        Assert.Null(result.RelativeChange);
    }

    [Fact]
    public void Money_is_rounded_per_group_and_the_totals_from_the_exact_sum()
    {
        // 0.004 + 0.004 rounds to 0.01 as a total, but each group rounds to 0.00.
        var result = Explain(new GroupRevenue("A", 0.004m, 0m), new GroupRevenue("B", 0.004m, 0m));

        Assert.Equal(0.01m, result.CurrentRevenue);
        Assert.All(result.Drivers, d => Assert.Equal(0m, d.CurrentRevenue));
    }
}
