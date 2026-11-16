using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Numera.Application.Entities;
using Numera.Infrastructure.Auth;
using Numera.Infrastructure.Seeding;

namespace Numera.IntegrationTests;

/// <summary>
/// Period comparison on the summary and timeseries endpoints. Expected values are computed by hand (see the
/// dataset comment) or, for the generated dataset, by asking the API for the comparison dates directly.
/// </summary>
public class MetricsComparisonTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Comparison(string Mode, DateOnly From, DateOnly To, decimal Revenue, int Orders, decimal AverageOrderValue,
        decimal RefundRate, int CompletedLines, int RefundedLines);

    private sealed record Changes(decimal? Revenue, decimal? Orders, decimal? AverageOrderValue, decimal? RefundRateDifference);

    private sealed record Summary(decimal Revenue, int Orders, decimal AverageOrderValue, decimal RefundRate, int CompletedLines,
        int RefundedLines, Comparison? Comparison, Changes? Changes);

    private sealed record Point(DateOnly PeriodStart, decimal Revenue, int Orders, decimal? ComparisonRevenue, int? ComparisonOrders);

    private sealed record Dates(string Mode, DateOnly From, DateOnly To);

    private sealed record Series(string Granularity, Point[] Points, Dates? Comparison);

    private sealed record Api(TestApiFactory Factory, HttpClient Client) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            return Factory.DisposeAsync();
        }
    }

    private static Order Line(string date, string order, string sku, string region, string category, int qty, decimal price,
        decimal discount, OrderStatus status) => new()
    {
        OrderId = order,
        OrderDate = DateOnly.Parse(date),
        CustomerId = "C-1",
        Region = region,
        Channel = "Online Store",
        ProductSku = sku,
        ProductCategory = category,
        Quantity = qty,
        UnitPrice = price,
        Discount = discount,
        Status = status,
    };

    /// <summary>
    /// The current range in these tests is March 2025 (31 days). Line revenue in brackets (completed lines only).
    /// Current, 2025-03-01..31:        B-1 Europe 20 + 45, B-2 North America 30, B-3 Europe refunded.
    ///                                 Revenue 95, orders 2, AOV 47.50, refund rate 1/4 = 0.25.
    /// Previous, 2025-01-29..02-28:    P-1 Europe 40 (moves +31 days to Mar 1), P-2 North America 50 (to Mar 17),
    ///                                 P-3 Europe refunded (to Mar 31). X-1 on Jan 28 is just outside.
    ///                                 Revenue 90, orders 2, AOV 45.00, refund rate 1/3 = 0.3333.
    /// Last year, 2024-03-01..31:      Y-1 Europe 50 (moves to 2025-03-10), Y-2 Asia-Pacific 20 (to 2025-03-31).
    ///                                 Revenue 70, orders 2, AOV 35.00, refund rate 0.
    /// </summary>
    private static Order[] Dataset() =>
    [
        Line("2025-03-03", "B-1", "S1", "Europe", "Blends", 2, 10.00m, 0m, OrderStatus.Completed),
        Line("2025-03-03", "B-1", "S2", "Europe", "Equipment", 1, 50.00m, 0.10m, OrderStatus.Completed),
        Line("2025-03-20", "B-2", "S1", "North America", "Blends", 3, 10.00m, 0m, OrderStatus.Completed),
        Line("2025-03-25", "B-3", "S3", "Europe", "Single Origin", 1, 18.50m, 0m, OrderStatus.Refunded),
        Line("2025-01-29", "P-1", "S1", "Europe", "Blends", 4, 10.00m, 0m, OrderStatus.Completed),
        Line("2025-02-14", "P-2", "S2", "North America", "Equipment", 1, 50.00m, 0m, OrderStatus.Completed),
        Line("2025-02-28", "P-3", "S1", "Europe", "Blends", 1, 10.00m, 0m, OrderStatus.Refunded),
        Line("2025-01-28", "X-1", "S1", "Europe", "Blends", 100, 10.00m, 0m, OrderStatus.Completed),
        Line("2024-03-10", "Y-1", "S1", "Europe", "Blends", 5, 10.00m, 0m, OrderStatus.Completed),
        Line("2024-03-31", "Y-2", "S1", "Asia-Pacific", "Blends", 2, 10.00m, 0m, OrderStatus.Completed),
    ];

    private const string March = "from=2025-03-01&to=2025-03-31";

    private async Task<Api> StartAsync(IEnumerable<Order>? orders = null)
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using (var db = PostgresFixture.CreateContext(cs))
        {
            db.Orders.AddRange(orders ?? Dataset());
            await db.SaveChangesAsync();
        }

        var factory = new TestApiFactory(cs);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DemoUserSeeder>().EnsureAsync();
        }

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = DemoUser.Email, password = DemoUser.Password });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new Api(factory, client);
    }

    private static async Task<T> GetAsync<T>(Api api, string url)
    {
        var response = await api.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static DateOnly D(string value) => DateOnly.Parse(value);

    // summary -------------------------------------------------------------------------------------

    [Fact]
    public async Task Summary_compared_with_the_previous_period()
    {
        await using var api = await StartAsync();

        var s = await GetAsync<Summary>(api, $"/api/metrics/summary?{March}&compare=previous");

        Assert.Equal((95.00m, 2, 47.50m, 0.25m), (s.Revenue, s.Orders, s.AverageOrderValue, s.RefundRate));
        Assert.Equal(new Comparison("previous", D("2025-01-29"), D("2025-02-28"), 90.00m, 2, 45.00m, 0.3333m, 2, 1), s.Comparison);
        Assert.Equal(new Changes(0.0556m, 0m, 0.0556m, -0.0833m), s.Changes); // 5/90, 0, 2.5/45, 0.25 - 0.3333
    }

    [Fact]
    public async Task Summary_compared_with_the_same_period_last_year()
    {
        await using var api = await StartAsync();

        var s = await GetAsync<Summary>(api, $"/api/metrics/summary?{March}&compare=year");

        Assert.Equal(new Comparison("year", D("2024-03-01"), D("2024-03-31"), 70.00m, 2, 35.00m, 0m, 2, 0), s.Comparison);
        Assert.Equal(new Changes(0.3571m, 0m, 0.3571m, 0.25m), s.Changes); // 25/70, 0, 12.5/35, 0.25 - 0
    }

    [Fact]
    public async Task The_comparison_keeps_the_rest_of_the_filter()
    {
        await using var api = await StartAsync();

        var s = await GetAsync<Summary>(api, $"/api/metrics/summary?{March}&region=Europe&compare=previous");

        // Current Europe: B-1 65, B-3 refunded. Previous Europe: P-1 40, P-3 refunded; X-1 is outside the dates.
        Assert.Equal((65.00m, 1, 2, 1), (s.Revenue, s.Orders, s.CompletedLines, s.RefundedLines));
        Assert.Equal(new Comparison("previous", D("2025-01-29"), D("2025-02-28"), 40.00m, 1, 40.00m, 0.5m, 1, 1), s.Comparison);
        Assert.Equal(new Changes(0.625m, 0m, 0.625m, -0.1667m), s.Changes); // 25/40; 1/3 - 1/2 = 0.3333 - 0.5
    }

    [Fact]
    public async Task A_comparison_period_without_data_has_no_changes()
    {
        await using var api = await StartAsync();

        var s = await GetAsync<Summary>(api, $"/api/metrics/summary?{March}&region=North%20America&compare=year");

        Assert.Equal((30.00m, 1), (s.Revenue, s.Orders));
        Assert.Equal(new Comparison("year", D("2024-03-01"), D("2024-03-31"), 0m, 0, 0m, 0m, 0, 0), s.Comparison);
        Assert.Equal(new Changes(null, null, null, null), s.Changes);
    }

    [Fact]
    public async Task Without_compare_the_summary_has_no_comparison()
    {
        await using var api = await StartAsync();

        var s = await GetAsync<Summary>(api, $"/api/metrics/summary?{March}");

        Assert.Null(s.Comparison);
        Assert.Null(s.Changes);
        var raw = await GetAsync<JsonElement>(api, $"/api/metrics/summary?{March}");
        Assert.Equal(JsonValueKind.Null, raw.GetProperty("comparison").ValueKind);
    }

    // timeseries ----------------------------------------------------------------------------------

    [Fact]
    public async Task Weekly_comparison_series_lines_up_with_the_current_weeks()
    {
        await using var api = await StartAsync();

        var s = await GetAsync<Series>(api, $"/api/metrics/timeseries?{March}&granularity=week&compare=previous");

        // 1 March 2025 is a Saturday, so the first week starts on 24 February. Previous days move forward 31 days:
        // P-1 (Jan 29) -> Mar 1 (week of Feb 24), P-2 (Feb 14) -> Mar 17, P-3 refunded (Feb 28) -> Mar 31 at zero.
        Assert.Equal(new Dates("previous", D("2025-01-29"), D("2025-02-28")), s.Comparison);
        Assert.Equal(
        [
            new Point(D("2025-02-24"), 0m, 0, 40.00m, 1),
            new Point(D("2025-03-03"), 65.00m, 1, 0m, 0),
            new Point(D("2025-03-10"), 0m, 0, 0m, 0),
            new Point(D("2025-03-17"), 30.00m, 1, 50.00m, 1),
            new Point(D("2025-03-24"), 0m, 0, 0m, 0),
            new Point(D("2025-03-31"), 0m, 0, 0m, 0),
        ], s.Points);
    }

    [Fact]
    public async Task Daily_comparison_with_last_year_puts_each_day_on_the_same_date()
    {
        await using var api = await StartAsync();

        var s = await GetAsync<Series>(api, $"/api/metrics/timeseries?{March}&granularity=day&compare=year");

        Assert.Equal(31, s.Points.Length);
        Assert.Equal(50.00m, s.Points.Single(p => p.PeriodStart == D("2025-03-10")).ComparisonRevenue);
        Assert.Equal(20.00m, s.Points.Single(p => p.PeriodStart == D("2025-03-31")).ComparisonRevenue);
        Assert.Equal(70.00m, s.Points.Sum(p => p.ComparisonRevenue));
        Assert.Equal(2, s.Points.Sum(p => p.ComparisonOrders));
        Assert.Equal(95.00m, s.Points.Sum(p => p.Revenue));
    }

    [Fact]
    public async Task Monthly_comparison_has_one_point_for_one_month()
    {
        await using var api = await StartAsync();

        var s = await GetAsync<Series>(api, $"/api/metrics/timeseries?{March}&compare=year");

        Assert.Equal("month", s.Granularity);
        Assert.Equal([new Point(D("2025-03-01"), 95.00m, 2, 70.00m, 2)], s.Points);
    }

    [Fact]
    public async Task A_range_starting_on_29_february_keeps_the_28_february_comparison_in_the_first_day()
    {
        await using var api = await StartAsync(
        [
            Line("2023-02-28", "L-1", "S1", "Europe", "Blends", 1, 10.00m, 0m, OrderStatus.Completed),
            Line("2023-03-01", "L-2", "S1", "Europe", "Blends", 2, 10.00m, 0m, OrderStatus.Completed),
            Line("2024-02-29", "L-3", "S1", "Europe", "Blends", 3, 10.00m, 0m, OrderStatus.Completed),
        ]);

        var s = await GetAsync<Series>(api, "/api/metrics/timeseries?from=2024-02-29&to=2024-03-02&granularity=day&compare=year");
        var summary = await GetAsync<Summary>(api, "/api/metrics/summary?from=2024-02-29&to=2024-03-02&compare=year");

        Assert.Equal(new Dates("year", D("2023-02-28"), D("2023-03-02")), s.Comparison);
        Assert.Equal(
        [
            new Point(D("2024-02-29"), 30.00m, 1, 10.00m, 1),
            new Point(D("2024-03-01"), 0m, 0, 20.00m, 1),
            new Point(D("2024-03-02"), 0m, 0, 0m, 0),
        ], s.Points);
        Assert.Equal(30.00m, summary.Comparison!.Revenue);
    }

    [Fact]
    public async Task Without_compare_points_have_no_comparison_values()
    {
        await using var api = await StartAsync();

        var s = await GetAsync<Series>(api, $"/api/metrics/timeseries?{March}");

        Assert.Null(s.Comparison);
        Assert.All(s.Points, p => Assert.Null(p.ComparisonRevenue));
        Assert.All(s.Points, p => Assert.Null(p.ComparisonOrders));
    }

    // consistency on generated data ------------------------------------------------------------------

    [Theory]
    [InlineData("2025-07-01", "2025-09-30", "previous", "week")]
    [InlineData("2026-07-01", "2026-09-30", "year", "month")] // the generated data starts in October 2024
    [InlineData("2025-11-15", "2025-12-14", "previous", "day")]
    [InlineData("2026-01-05", "2026-03-29", "year", "week")]
    public async Task Comparison_values_equal_the_kpis_for_the_comparison_dates_and_the_series_adds_up(
        string from, string to, string mode, string granularity)
    {
        var lines = DemoDataGenerator.Generate(new SeedOptions { OrderLineCount = 4_000, Seed = 7 });
        await using var api = await StartAsync(lines);
        var range = $"from={from}&to={to}&channel=Online%20Store";

        var summary = await GetAsync<Summary>(api, $"/api/metrics/summary?{range}&compare={mode}");
        var c = summary.Comparison!;
        var direct = await GetAsync<Summary>(api,
            $"/api/metrics/summary?from={c.From:yyyy-MM-dd}&to={c.To:yyyy-MM-dd}&channel=Online%20Store");
        Assert.True(direct.Revenue > 0, "the generated data should cover the comparison period");
        Assert.Equal(
            (direct.Revenue, direct.Orders, direct.AverageOrderValue, direct.RefundRate, direct.CompletedLines, direct.RefundedLines),
            (c.Revenue, c.Orders, c.AverageOrderValue, c.RefundRate, c.CompletedLines, c.RefundedLines));

        var series = await GetAsync<Series>(api, $"/api/metrics/timeseries?{range}&granularity={granularity}&compare={mode}");
        var plain = await GetAsync<Series>(api, $"/api/metrics/timeseries?{range}&granularity={granularity}");

        // The current values are unchanged by the comparison, and the comparison series adds up to the comparison
        // revenue: each period is rounded to the cent on its own, so allow half a cent per period.
        Assert.Equal(plain.Points.Select(p => (p.PeriodStart, p.Revenue, p.Orders)), series.Points.Select(p => (p.PeriodStart, p.Revenue, p.Orders)));
        var seriesTotal = series.Points.Sum(p => p.ComparisonRevenue!.Value);
        Assert.InRange(Math.Abs(seriesTotal - c.Revenue), 0m, 0.005m * series.Points.Length);
        Assert.Equal(c.Orders, series.Points.Sum(p => p.ComparisonOrders!.Value)); // an order has one date, so one period
    }

    // validation ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("/api/metrics/summary?compare=previous")]
    [InlineData("/api/metrics/summary?from=2025-03-01&compare=year")]
    [InlineData("/api/metrics/summary?from=2025-03-01&to=2025-03-31&compare=quarter")]
    [InlineData("/api/metrics/timeseries?to=2025-03-31&compare=previous")]
    [InlineData("/api/metrics/timeseries?from=2025-03-01&to=2025-03-31&compare=1")]
    public async Task Bad_compare_input_returns_400_naming_compare(string url)
    {
        await using var api = await StartAsync([]);

        var response = await api.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("compare", out _));
    }
}
