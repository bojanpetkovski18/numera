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
/// Expected values are computed by hand (see the comments) or, for the generated dataset, with LINQ over the
/// generator's output: never with the SQL under test.
/// </summary>
public class MetricsEndpointsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Summary(decimal Revenue, int Orders, decimal AverageOrderValue, decimal RefundRate, int CompletedLines, int RefundedLines);

    private sealed record Point(DateOnly PeriodStart, decimal Revenue, int Orders);

    private sealed record Series(string Granularity, Point[] Points);

    private sealed record Group(string Key, decimal Revenue, int Orders);

    private sealed record Breakdown(string By, Group[] Groups);

    private sealed record Api(TestApiFactory Factory, HttpClient Client) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            return Factory.DisposeAsync();
        }
    }

    private static Order Line(string date, string order, string sku, string region, string channel, string category,
        int qty, decimal price, decimal discount, OrderStatus status) => new()
    {
        OrderId = order,
        OrderDate = DateOnly.Parse(date),
        CustomerId = "C-1",
        Region = region,
        Channel = channel,
        ProductSku = sku,
        ProductCategory = category,
        Quantity = qty,
        UnitPrice = price,
        Discount = discount,
        Status = status,
    };

    /// <summary>
    /// Line revenue in brackets (completed lines only):
    /// 1 A-1 Blends     Europe        Online Store 2 x 10.00 x 1.00  = 20.00
    /// 2 A-1 Equipment  Europe        Online Store 1 x 50.00 x 0.90  = 45.00
    /// 3 A-2 Blends     North America Mobile App   3 x 10.00 x 1.00  = 30.00
    /// 4 A-3 Single Or. Europe        Online Store refunded
    /// 5 A-4 Blends     Asia-Pacific  Marketplace  4 x 10.00 x 0.75  = 30.00
    /// 6 A-4 Single Or. Asia-Pacific  Marketplace  cancelled
    /// 7 A-5 Equipment  North America Online Store refunded
    /// 8 A-6 Single Or. Europe        Mobile App   1 x 18.50 x 0.667 = 12.3395
    /// </summary>
    private static readonly Order[] HandDataset =
    [
        Line("2025-01-05", "A-1", "S1", "Europe", "Online Store", "Blends", 2, 10.00m, 0m, OrderStatus.Completed),
        Line("2025-01-05", "A-1", "S2", "Europe", "Online Store", "Equipment", 1, 50.00m, 0.10m, OrderStatus.Completed),
        Line("2025-01-20", "A-2", "S1", "North America", "Mobile App", "Blends", 3, 10.00m, 0m, OrderStatus.Completed),
        Line("2025-02-03", "A-3", "S3", "Europe", "Online Store", "Single Origin", 1, 18.50m, 0m, OrderStatus.Refunded),
        Line("2025-02-10", "A-4", "S1", "Asia-Pacific", "Marketplace", "Blends", 4, 10.00m, 0.25m, OrderStatus.Completed),
        Line("2025-02-10", "A-4", "S3", "Asia-Pacific", "Marketplace", "Single Origin", 2, 18.50m, 0m, OrderStatus.Cancelled),
        Line("2025-03-15", "A-5", "S2", "North America", "Online Store", "Equipment", 1, 50.00m, 0m, OrderStatus.Refunded),
        Line("2025-03-31", "A-6", "S3", "Europe", "Mobile App", "Single Origin", 1, 18.50m, 0.333m, OrderStatus.Completed),
    ];

    private async Task<Api> StartAsync(IEnumerable<Order>? orders = null, bool signedIn = true)
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using (var db = PostgresFixture.CreateContext(cs))
        {
            db.Orders.AddRange(orders ?? HandDataset.Select(Clone));
            await db.SaveChangesAsync();
        }

        var factory = new TestApiFactory(cs);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DemoUserSeeder>().EnsureAsync();
        }

        var client = factory.CreateClient();
        if (signedIn)
        {
            var login = await client.PostAsJsonAsync("/api/auth/login", new { email = DemoUser.Email, password = DemoUser.Password });
            var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return new Api(factory, client);
    }

    private static Order Clone(Order o) => Line(o.OrderDate.ToString("yyyy-MM-dd"), o.OrderId, o.ProductSku, o.Region, o.Channel,
        o.ProductCategory, o.Quantity, o.UnitPrice, o.Discount, o.Status);

    private static async Task<T> GetAsync<T>(Api api, string url)
    {
        var response = await api.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    // summary -------------------------------------------------------------------------------------

    [Fact]
    public async Task Summary_matches_the_hand_computed_kpis()
    {
        await using var api = await StartAsync();

        var s = await GetAsync<Summary>(api, "/api/metrics/summary");

        Assert.Equal(137.34m, s.Revenue);          // 20 + 45 + 30 + 30 + 12.3395
        Assert.Equal(4, s.Orders);                 // A-1, A-2, A-4, A-6
        Assert.Equal(34.33m, s.AverageOrderValue); // 137.3395 / 4 = 34.334875
        Assert.Equal(0.2857m, s.RefundRate);       // 2 refunded / (5 completed + 2 refunded)
        Assert.Equal(5, s.CompletedLines);
        Assert.Equal(2, s.RefundedLines);
    }

    [Fact]
    public async Task Summary_for_a_single_day()
    {
        await using var api = await StartAsync();

        var s = await GetAsync<Summary>(api, "/api/metrics/summary?from=2025-01-05&to=2025-01-05");

        Assert.Equal(new Summary(65.00m, 1, 65.00m, 0m, 2, 0), s);
    }

    [Fact]
    public async Task Summary_for_a_range_with_only_refunds()
    {
        await using var api = await StartAsync();

        var s = await GetAsync<Summary>(api, "/api/metrics/summary?from=2025-03-01&to=2025-03-20"); // line 7 only

        Assert.Equal(new Summary(0m, 0, 0m, 1m, 0, 1), s);
    }

    [Fact]
    public async Task Summary_for_a_filter_that_matches_nothing_is_all_zero()
    {
        await using var api = await StartAsync();

        var s = await GetAsync<Summary>(api, "/api/metrics/summary?region=Antarctica");

        Assert.Equal(new Summary(0m, 0, 0m, 0m, 0, 0), s);
    }

    [Fact]
    public async Task Summary_on_an_empty_database_is_all_zero()
    {
        await using var api = await StartAsync([]);

        Assert.Equal(new Summary(0m, 0, 0m, 0m, 0, 0), await GetAsync<Summary>(api, "/api/metrics/summary"));
    }

    [Fact]
    public async Task Repeated_filter_values_mean_any_of_them()
    {
        await using var api = await StartAsync();

        var s = await GetAsync<Summary>(api, "/api/metrics/summary?region=Europe&region=Asia-Pacific");

        Assert.Equal(107.34m, s.Revenue); // 20 + 45 + 12.3395 + 30
        Assert.Equal(3, s.Orders);        // A-1, A-6, A-4
        Assert.Equal(0.2m, s.RefundRate); // 1 refunded (A-3) / (4 completed + 1 refunded)
    }

    // timeseries ----------------------------------------------------------------------------------

    [Fact]
    public async Task Monthly_timeseries_matches_hand_computed_totals()
    {
        await using var api = await StartAsync();

        var series = await GetAsync<Series>(api, "/api/metrics/timeseries?granularity=month&from=2025-01-01&to=2025-03-31");

        Assert.Equal("month", series.Granularity);
        Assert.Equal(
            [
                new Point(new DateOnly(2025, 1, 1), 95.00m, 2), // 20 + 45 + 30; A-1, A-2
                new Point(new DateOnly(2025, 2, 1), 30.00m, 1), // A-4
                new Point(new DateOnly(2025, 3, 1), 12.34m, 1), // A-6; March's refund adds nothing
            ],
            series.Points);
    }

    [Fact]
    public async Task Weekly_timeseries_starts_on_monday_and_fills_empty_weeks_with_zero()
    {
        await using var api = await StartAsync();

        var series = await GetAsync<Series>(api, "/api/metrics/timeseries?granularity=week&from=2025-01-01&to=2025-01-31");

        Assert.Equal(
            [
                new Point(new DateOnly(2024, 12, 30), 65.00m, 1), // Sunday 5 Jan belongs to the week of Monday 30 Dec
                new Point(new DateOnly(2025, 1, 6), 0m, 0),
                new Point(new DateOnly(2025, 1, 13), 0m, 0),
                new Point(new DateOnly(2025, 1, 20), 30.00m, 1),
                new Point(new DateOnly(2025, 1, 27), 0m, 0),
            ],
            series.Points);
    }

    [Fact]
    public async Task Daily_timeseries_for_one_day_has_one_point()
    {
        await using var api = await StartAsync();

        var series = await GetAsync<Series>(api, "/api/metrics/timeseries?granularity=day&from=2025-02-10&to=2025-02-10");

        Assert.Equal([new Point(new DateOnly(2025, 2, 10), 30.00m, 1)], series.Points);
    }

    [Fact]
    public async Task Without_dates_the_series_spans_the_matching_data_and_defaults_to_months()
    {
        await using var api = await StartAsync();

        var series = await GetAsync<Series>(api, "/api/metrics/timeseries?region=North%20America");

        // North America lines: 20 Jan (completed 30.00) and 15 Mar (refunded); February is filled with zero.
        Assert.Equal("month", series.Granularity);
        Assert.Equal(
            [
                new Point(new DateOnly(2025, 1, 1), 30.00m, 1),
                new Point(new DateOnly(2025, 2, 1), 0m, 0),
                new Point(new DateOnly(2025, 3, 1), 0m, 0),
            ],
            series.Points);
    }

    [Fact]
    public async Task A_range_with_no_data_is_zero_filled_and_no_data_without_a_range_is_empty()
    {
        await using var api = await StartAsync();

        var zeroFilled = await GetAsync<Series>(api, "/api/metrics/timeseries?granularity=month&from=2024-01-01&to=2024-02-29");
        var empty = await GetAsync<Series>(api, "/api/metrics/timeseries?region=Antarctica");

        Assert.Equal([new Point(new DateOnly(2024, 1, 1), 0m, 0), new Point(new DateOnly(2024, 2, 1), 0m, 0)], zeroFilled.Points);
        Assert.Empty(empty.Points);
    }

    // breakdown -----------------------------------------------------------------------------------

    [Fact]
    public async Task Breakdown_by_category_is_sorted_by_revenue()
    {
        await using var api = await StartAsync();

        var b = await GetAsync<Breakdown>(api, "/api/metrics/breakdown?by=category");

        Assert.Equal("category", b.By);
        Assert.Equal(
            [
                new Group("Blends", 80.00m, 3),        // 20 + 30 + 30; A-1, A-2, A-4
                new Group("Equipment", 45.00m, 1),     // A-1 (A-5 was refunded)
                new Group("Single Origin", 12.34m, 1), // A-6 (A-3 refunded, A-4 line cancelled)
            ],
            b.Groups);
    }

    [Fact]
    public async Task Breakdown_by_region_breaks_revenue_ties_by_name()
    {
        await using var api = await StartAsync();

        var b = await GetAsync<Breakdown>(api, "/api/metrics/breakdown?by=region");

        Assert.Equal(
            [
                new Group("Europe", 77.34m, 2),        // 20 + 45 + 12.3395; A-1, A-6
                new Group("Asia-Pacific", 30.00m, 1),
                new Group("North America", 30.00m, 1),
            ],
            b.Groups);
    }

    [Fact]
    public async Task Breakdown_by_channel_with_a_date_filter()
    {
        await using var api = await StartAsync();

        var b = await GetAsync<Breakdown>(api, "/api/metrics/breakdown?by=channel&from=2025-01-01&to=2025-01-31");

        Assert.Equal([new Group("Online Store", 65.00m, 1), new Group("Mobile App", 30.00m, 1)], b.Groups);
    }

    [Fact]
    public async Task Breakdown_includes_groups_with_only_refunds_at_zero_revenue()
    {
        await using var api = await StartAsync();

        var b = await GetAsync<Breakdown>(api, "/api/metrics/breakdown?by=region&from=2025-03-01&to=2025-03-20");

        Assert.Equal([new Group("North America", 0m, 0)], b.Groups);
    }

    // consistency and validation ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("&category=Blends")]
    [InlineData("&region=Europe&from=2025-01-01&to=2025-02-28")]
    [InlineData("&channel=Mobile%20App")]
    public async Task The_three_endpoints_agree_for_the_same_filter(string filter)
    {
        await using var api = await StartAsync();

        var summary = await GetAsync<Summary>(api, $"/api/metrics/summary?x=1{filter}");
        var series = await GetAsync<Series>(api, $"/api/metrics/timeseries?granularity=day{filter}");
        var byRegion = await GetAsync<Breakdown>(api, $"/api/metrics/breakdown?by=region{filter}");
        var byCategory = await GetAsync<Breakdown>(api, $"/api/metrics/breakdown?by=category{filter}");

        // Each total is rounded to the cent separately; on this dataset no group splits a half cent.
        Assert.Equal(summary.Revenue, byRegion.Groups.Sum(g => g.Revenue));
        Assert.Equal(summary.Revenue, byCategory.Groups.Sum(g => g.Revenue));
        Assert.Equal(summary.Revenue, series.Points.Sum(p => p.Revenue));
        Assert.Equal(summary.Orders, series.Points.Sum(p => p.Orders)); // every order here has a single date
    }

    [Fact]
    public async Task A_generated_dataset_matches_an_independent_linq_calculation()
    {
        var options = new SeedOptions { OrderLineCount = 3_000, Seed = 42 };
        var lines = DemoDataGenerator.Generate(options);
        await using var api = await StartAsync(lines);

        var from = new DateOnly(2025, 6, 1);
        var to = new DateOnly(2025, 12, 31);
        var inRange = lines.Where(l => l.OrderDate >= from && l.OrderDate <= to && l.Region == "Europe").ToList();
        var completed = inRange.Where(l => l.Status == OrderStatus.Completed).ToList();
        var exactRevenue = completed.Sum(l => l.Quantity * l.UnitPrice * (1 - l.Discount));
        var orders = completed.Select(l => l.OrderId).Distinct().Count();
        var refunded = inRange.Count(l => l.Status == OrderStatus.Refunded);

        var s = await GetAsync<Summary>(api, "/api/metrics/summary?from=2025-06-01&to=2025-12-31&region=Europe");

        Assert.Equal(Math.Round(exactRevenue, 2, MidpointRounding.AwayFromZero), s.Revenue);
        Assert.Equal(orders, s.Orders);
        Assert.Equal(Math.Round(exactRevenue / orders, 2, MidpointRounding.AwayFromZero), s.AverageOrderValue);
        Assert.Equal(Math.Round((decimal)refunded / (refunded + completed.Count), 4, MidpointRounding.AwayFromZero), s.RefundRate);

        var byChannel = await GetAsync<Breakdown>(api, "/api/metrics/breakdown?by=channel&from=2025-06-01&to=2025-12-31&region=Europe");
        var expectedChannels = completed
            .GroupBy(l => l.Channel)
            .Select(g => new Group(g.Key, Math.Round(g.Sum(l => l.Quantity * l.UnitPrice * (1 - l.Discount)), 2, MidpointRounding.AwayFromZero), g.Select(l => l.OrderId).Distinct().Count()))
            .OrderByDescending(g => g.Revenue).ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedChannels, byChannel.Groups.Where(g => g.Revenue > 0 || g.Orders > 0).ToArray());

        var monthly = await GetAsync<Series>(api, "/api/metrics/timeseries?granularity=month&from=2025-06-01&to=2025-12-31&region=Europe");
        var expectedMonths = Enumerable.Range(6, 7)
            .Select(m => new DateOnly(2025, m, 1))
            .Select(start =>
            {
                var month = completed.Where(l => l.OrderDate.Year == start.Year && l.OrderDate.Month == start.Month).ToList();
                return new Point(start, Math.Round(month.Sum(l => l.Quantity * l.UnitPrice * (1 - l.Discount)), 2, MidpointRounding.AwayFromZero), month.Select(l => l.OrderId).Distinct().Count());
            })
            .ToArray();
        Assert.Equal(expectedMonths, monthly.Points);
    }

    [Theory]
    [InlineData("/api/metrics/summary?from=2025-02-01&to=2025-01-01", "to")]
    [InlineData("/api/metrics/summary?from=01-02-2025", "from")]
    [InlineData("/api/metrics/summary?status=completed", "status")]
    [InlineData("/api/metrics/timeseries?granularity=year", "granularity")]
    [InlineData("/api/metrics/timeseries?granularity=day&from=2020-01-01&to=2025-12-31", "granularity")]
    [InlineData("/api/metrics/breakdown?by=sku", "by")]
    [InlineData("/api/metrics/breakdown", "by")]
    public async Task Bad_input_returns_400_problem_details_naming_the_field(string url, string field)
    {
        await using var api = await StartAsync();

        var response = await api.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    private sealed record Options(string[] Regions, string[] Channels, string[] Categories, DateOnly? FirstOrderDate, DateOnly? LastOrderDate);

    [Fact]
    public async Task Filter_options_list_distinct_values_and_the_date_range()
    {
        await using var api = await StartAsync();

        var options = await GetAsync<Options>(api, "/api/filter-options");

        Assert.Equal(["Asia-Pacific", "Europe", "North America"], options.Regions);
        Assert.Equal(["Marketplace", "Mobile App", "Online Store"], options.Channels);
        Assert.Equal(["Blends", "Equipment", "Single Origin"], options.Categories);
        Assert.Equal(new DateOnly(2025, 1, 5), options.FirstOrderDate);
        Assert.Equal(new DateOnly(2025, 3, 31), options.LastOrderDate);
    }

    [Fact]
    public async Task Filter_options_on_an_empty_database_are_empty()
    {
        await using var api = await StartAsync([]);

        var options = await GetAsync<Options>(api, "/api/filter-options");

        Assert.Empty(options.Regions);
        Assert.Null(options.FirstOrderDate);
    }

    [Theory]
    [InlineData("/api/metrics/summary")]
    [InlineData("/api/metrics/timeseries")]
    [InlineData("/api/metrics/breakdown?by=region")]
    [InlineData("/api/filter-options")]
    public async Task Metrics_require_authentication(string url)
    {
        await using var api = await StartAsync(signedIn: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.GetAsync(url)).StatusCode);
    }
}