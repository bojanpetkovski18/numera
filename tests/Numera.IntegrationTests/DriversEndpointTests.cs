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
/// <c>GET /api/metrics/drivers</c>: which groups drove the revenue change. Expected values are computed by hand, or
/// checked against the summary endpoint and the orders grid (independent queries), never with the SQL under test.
/// </summary>
public class DriversEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Dates(string Mode, DateOnly From, DateOnly To);

    private sealed record Driver(string Key, decimal CurrentRevenue, decimal ComparisonRevenue, decimal Contribution, decimal? ShareOfChange);

    private sealed record Result(string By, Dates Comparison, decimal CurrentRevenue, decimal ComparisonRevenue, decimal Change,
        decimal? RelativeChange, Driver[] Drivers);

    private sealed record Changes(decimal? Revenue);

    private sealed record SummaryComparison(DateOnly From, DateOnly To, decimal Revenue);

    private sealed record Summary(decimal Revenue, SummaryComparison? Comparison, Changes? Changes);

    private sealed record Row(string OrderId, int Quantity, decimal UnitPrice, decimal Discount);

    private sealed record GridPage(Row[] Items, int TotalCount, int TotalPages);

    private sealed record Api(TestApiFactory Factory, HttpClient Client) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            return Factory.DisposeAsync();
        }
    }

    private static Order Line(string date, string order, string region, string channel, string category, int qty, decimal price,
        OrderStatus status = OrderStatus.Completed) => new()
    {
        OrderId = order,
        OrderDate = DateOnly.Parse(date),
        CustomerId = "C-1",
        Region = region,
        Channel = channel,
        ProductSku = $"S-{category}",
        ProductCategory = category,
        Quantity = qty,
        UnitPrice = price,
        Discount = 0m,
        Status = status,
    };

    /// <summary>
    /// Current range: March 2025. Previous period: 2025-01-29..02-28. Last year: March 2024.
    /// Current:  Europe 100 (B-1), North America 30 (B-2), Asia-Pacific refunded only (B-3). Total 130.
    /// Previous: Europe 40 (P-1), North America 60 (P-2), Latin America 25 (P-3). Total 125. X-1 (Jan 28) is outside.
    /// Last year: Europe 70 (Y-1).
    /// </summary>
    private static Order[] Dataset() =>
    [
        Line("2025-03-03", "B-1", "Europe", "Online Store", "Blends", 10, 10.00m),
        Line("2025-03-20", "B-2", "North America", "Mobile App", "Equipment", 1, 30.00m),
        Line("2025-03-25", "B-3", "Asia-Pacific", "Online Store", "Blends", 1, 18.50m, OrderStatus.Refunded),
        Line("2025-01-29", "P-1", "Europe", "Online Store", "Blends", 4, 10.00m),
        Line("2025-02-14", "P-2", "North America", "Online Store", "Equipment", 2, 30.00m),
        Line("2025-02-28", "P-3", "Latin America", "Mobile App", "Blends", 1, 25.00m),
        Line("2025-01-28", "X-1", "Europe", "Online Store", "Blends", 100, 10.00m),
        Line("2024-03-10", "Y-1", "Europe", "Online Store", "Blends", 7, 10.00m),
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

    private static Task<Result> GetDriversAsync(Api api, string query) => GetAsync<Result>(api, $"/api/metrics/drivers?{query}");

    private static DateOnly D(string value) => DateOnly.Parse(value);

    [Fact]
    public async Task Regions_against_the_previous_period_are_sorted_by_their_contribution()
    {
        await using var api = await StartAsync();

        var d = await GetDriversAsync(api, $"{March}&by=region&compare=previous");

        Assert.Equal("region", d.By);
        Assert.Equal(new Dates("previous", D("2025-01-29"), D("2025-02-28")), d.Comparison);
        Assert.Equal((130.00m, 125.00m, 5.00m, 0.04m), (d.CurrentRevenue, d.ComparisonRevenue, d.Change, d.RelativeChange!.Value));
        Assert.Equal(
        [
            new Driver("Europe", 100.00m, 40.00m, 60.00m, 12m),               // 60 / 5
            new Driver("North America", 30.00m, 60.00m, -30.00m, -6m),
            new Driver("Latin America", 0m, 25.00m, -25.00m, -5m),            // gone this period
        ], d.Drivers);                                                        // Asia-Pacific has refunds only: left out
    }

    [Fact]
    public async Task Other_dimensions_and_filters_apply_to_both_periods()
    {
        await using var api = await StartAsync();

        var byChannel = await GetDriversAsync(api, $"{March}&by=channel&compare=previous");
        Assert.Equal(
            [new Driver("Online Store", 100.00m, 100.00m, 0m, 0m), new Driver("Mobile App", 30.00m, 25.00m, 5.00m, 1m)],
            byChannel.Drivers.OrderByDescending(x => x.CurrentRevenue).ToArray());

        var europeByCategory = await GetDriversAsync(api, $"{March}&region=Europe&by=category&compare=year");
        Assert.Equal(new Dates("year", D("2024-03-01"), D("2024-03-31")), europeByCategory.Comparison);
        Assert.Equal([new Driver("Blends", 100.00m, 70.00m, 30.00m, 1m)], europeByCategory.Drivers);
        Assert.Equal(0.4286m, europeByCategory.RelativeChange); // 30 / 70
    }

    [Fact]
    public async Task Nothing_in_either_period_gives_no_drivers()
    {
        await using var api = await StartAsync();

        var d = await GetDriversAsync(api, "from=2023-06-01&to=2023-06-30&by=region&compare=previous");

        Assert.Equal((0m, 0m, 0m), (d.CurrentRevenue, d.ComparisonRevenue, d.Change));
        Assert.Null(d.RelativeChange);
        Assert.Empty(d.Drivers);
    }

    [Theory]
    [InlineData("2026-07-01", "2026-09-30", "previous")]
    [InlineData("2026-07-01", "2026-09-30", "year")]
    [InlineData("2025-11-15", "2025-12-14", "previous")]
    public async Task On_generated_data_the_drivers_agree_with_the_summary_and_with_the_rows_behind_them(string from, string to, string mode)
    {
        var lines = DemoDataGenerator.Generate(new SeedOptions { OrderLineCount = 3_000, Seed = 11 });
        await using var api = await StartAsync(lines);
        var range = $"from={from}&to={to}&channel=Online%20Store";
        var summary = await GetAsync<Summary>(api, $"/api/metrics/summary?{range}&compare={mode}");

        foreach (var by in new[] { "region", "channel", "category" })
        {
            var d = await GetDriversAsync(api, $"{range}&by={by}&compare={mode}");

            // Same totals as the summary, and each dimension's contributions add up to the change, within a cent per group.
            Assert.Equal((summary.Revenue, summary.Comparison!.Revenue), (d.CurrentRevenue, d.ComparisonRevenue));
            Assert.Equal(summary.Changes!.Revenue, d.RelativeChange);
            Assert.InRange(Math.Abs(d.Drivers.Sum(x => x.Contribution) - d.Change), 0m, 0.01m * d.Drivers.Length);
            Assert.NotEmpty(d.Drivers);
        }

        // A driver's drill-down (the grid on the current period, its group, completed lines) adds up to its revenue,
        // and the same holds for the comparison period's rows.
        var regions = await GetDriversAsync(api, $"{range}&by=region&compare={mode}");
        foreach (var driver in regions.Drivers)
        {
            var region = Uri.EscapeDataString(driver.Key);
            Assert.Equal(driver.CurrentRevenue, await GridRevenueAsync(api, $"{range}&region={region}&status=completed"));
            var c = regions.Comparison;
            Assert.Equal(driver.ComparisonRevenue,
                await GridRevenueAsync(api, $"from={c.From:yyyy-MM-dd}&to={c.To:yyyy-MM-dd}&channel=Online%20Store&region={region}&status=completed"));
        }
    }

    private static async Task<decimal> GridRevenueAsync(Api api, string query)
    {
        var exact = 0m;
        for (var page = 1; ; page++)
        {
            var grid = await GetAsync<GridPage>(api, $"/api/orders?{query}&pageSize=100&page={page}");
            exact += grid.Items.Sum(r => r.Quantity * r.UnitPrice * (1 - r.Discount));
            if (page >= grid.TotalPages)
            {
                return Math.Round(exact, 2, MidpointRounding.AwayFromZero);
            }
        }
    }

    [Theory]
    [InlineData($"/api/metrics/drivers?{March}&by=region", "compare")]
    [InlineData("/api/metrics/drivers?by=region&compare=previous", "compare")]
    [InlineData($"/api/metrics/drivers?{March}&by=region&compare=decade", "compare")]
    [InlineData($"/api/metrics/drivers?{March}&compare=previous", "by")]
    [InlineData($"/api/metrics/drivers?{March}&by=sku&compare=previous", "by")]
    [InlineData($"/api/metrics/drivers?{March}&by=region&compare=previous&status=completed", "status")]
    public async Task Bad_input_returns_400_naming_the_field(string url, string field)
    {
        await using var api = await StartAsync([]);

        var response = await api.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Driversrequire_authentication()
    {
        await using var api = await StartAsync([]);
        api.Client.DefaultRequestHeaders.Authorization = null;

        var response = await api.Client.GetAsync($"/api/metrics/drivers?{March}&by=region&compare=previous");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
