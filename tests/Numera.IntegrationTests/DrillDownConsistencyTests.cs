using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Numera.Application.Entities;
using Numera.Infrastructure.Auth;

namespace Numera.IntegrationTests;

/// <summary>
/// A drill-down opens the grid with the dashboard's filter narrowed to one chart point or bar, plus
/// <c>status=completed</c> (the charts show completed revenue). The rows shown must be exactly the data behind the
/// clicked value. "Exactly" is checked three ways against the dashboard's own numbers for the drilled filter:
/// <list type="bullet">
/// <item>the grid's row count equals the summary's <c>completedLines</c> (the count the drill-down implies: revenue
/// and orders are defined on completed lines only, so refunded and cancelled lines are not part of it);</item>
/// <item>the distinct order ids among those rows equal the point's or bar's <c>orders</c>;</item>
/// <item>the revenue recomputed from the rows' quantity, price and discount equals the point's or bar's <c>revenue</c>.</item>
/// </list>
/// The narrowing below mirrors the web app's drill-down (web/src/app/core/filters/drill-down.ts).
/// </summary>
public class DrillDownConsistencyTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Row(long Id, string OrderId, DateOnly OrderDate, string Region, string Channel, string ProductCategory,
        int Quantity, decimal UnitPrice, decimal Discount, string Status);

    private sealed record GridPage(Row[] Items, int TotalCount, int Page, int PageSize, int TotalPages);

    private sealed record Summary(decimal Revenue, int Orders, int CompletedLines, int RefundedLines);

    private sealed record Point(DateOnly PeriodStart, decimal Revenue, int Orders);

    private sealed record Series(string Granularity, Point[] Points);

    private sealed record Group(string Key, decimal Revenue, int Orders);

    private sealed record Breakdown(string By, Group[] Groups);

    private static readonly string[] Regions = ["Europe", "North America", "Asia-Pacific"];
    private static readonly string[] Channels = ["Online Store", "Wholesale"];
    private static readonly string[] Categories = ["Blends", "Equipment", "Single Origin"];

    /// <summary>
    /// One order per day from Thu Feb 20 to Sat Apr 12, 2025, with 1-3 lines in different categories, so every week
    /// and month boundary (Sun Mar 30 / Mon Mar 31, Sun Apr 6 / Mon Apr 7, Feb 28 / Mar 1, Mar 31 / Apr 1) has orders
    /// on both sides (none of those days is a cancelled order). Some lines are refunded and some orders cancelled.
    /// </summary>
    private static List<Order> Dataset()
    {
        var rows = new List<Order>();
        var first = new DateOnly(2025, 2, 20);
        for (var i = 0; first.AddDays(i) <= new DateOnly(2025, 4, 12); i++)
        {
            for (var j = 0; j < 1 + i % 3; j++)
            {
                rows.Add(new Order
                {
                    OrderId = $"DD-{i:D4}",
                    OrderDate = first.AddDays(i),
                    CustomerId = $"C-{i % 9:D3}",
                    Region = Regions[i % 3],
                    Channel = Channels[i / 2 % 2],
                    ProductSku = $"SKU-{(i + j) % 3}-{j}",
                    ProductCategory = Categories[(i + j) % 3],
                    Quantity = 1 + (i + j) % 3,
                    UnitPrice = 4.75m + i % 5 * 3.1m + j,
                    Discount = (i + j) % 4 * 0.05m,
                    Status = i % 10 == 2 ? OrderStatus.Cancelled : j == 1 && i % 4 == 0 ? OrderStatus.Refunded : OrderStatus.Completed,
                });
            }
        }
        return rows;
    }

    private async Task<HttpClient> StartAsync()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using (var db = PostgresFixture.CreateContext(cs))
        {
            db.Orders.AddRange(Dataset());
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
        return client;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    /// <summary>Every row of the drilled grid, page by page, as the user would see them.</summary>
    private static async Task<(int TotalCount, List<Row> Rows)> GridAsync(HttpClient client, string filter)
    {
        var rows = new List<Row>();
        GridPage page;
        var number = 1;
        do
        {
            page = await GetAsync<GridPage>(client, $"/api/orders?{filter}&status=completed&pageSize=100&page={number++}");
            rows.AddRange(page.Items);
        }
        while (page.Page < page.TotalPages);
        return (page.TotalCount, rows);
    }

    private static async Task AssertDrillMatchesAsync(HttpClient client, string drilledFilter, decimal revenue, int orders)
    {
        var (totalCount, rows) = await GridAsync(client, drilledFilter);
        var summary = await GetAsync<Summary>(client, $"/api/metrics/summary?{drilledFilter}");

        Assert.All(rows, r => Assert.Equal("completed", r.Status));
        Assert.Equal(rows.Count, totalCount);
        Assert.Equal(summary.CompletedLines, totalCount);
        Assert.Equal(orders, rows.Select(r => r.OrderId).Distinct().Count());
        var exact = rows.Sum(r => r.Quantity * r.UnitPrice * (1 - r.Discount));
        Assert.Equal(revenue, decimal.Round(exact, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>Same as the web app: the period's own days, clipped to the active range.</summary>
    private static (DateOnly From, DateOnly To) PeriodRange(DateOnly start, string granularity, DateOnly? rangeFrom, DateOnly? rangeTo)
    {
        var end = granularity switch
        {
            "day" => start,
            "week" => start.AddDays(6),
            _ => start.AddMonths(1).AddDays(-1),
        };
        return (rangeFrom > start ? rangeFrom.Value : start, rangeTo < end ? rangeTo.Value : end);
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");

    [Theory]
    [InlineData("week", "")]
    [InlineData("month", "")]
    [InlineData("day", "region=Europe")]
    [InlineData("week", "from=2025-03-26&to=2025-04-08&channel=Wholesale")] // first and last weeks are partial
    [InlineData("month", "from=2025-02-25&to=2025-04-03&category=Blends")]  // first and last months are partial
    public async Task Every_time_point_drills_to_exactly_its_completed_lines(string granularity, string filter)
    {
        using var client = await StartAsync();
        var from = filter.Contains("from=") ? DateOnly.Parse(filter.Split("from=")[1][..10]) : (DateOnly?)null;
        var to = filter.Contains("to=") ? DateOnly.Parse(filter.Split("to=")[1][..10]) : (DateOnly?)null;
        var rest = string.Join('&', filter.Split('&', StringSplitOptions.RemoveEmptyEntries).Where(p => !p.StartsWith("from=") && !p.StartsWith("to=")));

        var series = await GetAsync<Series>(client, $"/api/metrics/timeseries?granularity={granularity}&{filter}");
        Assert.NotEmpty(series.Points);

        foreach (var point in series.Points)
        {
            var (drillFrom, drillTo) = PeriodRange(point.PeriodStart, granularity, from, to);
            await AssertDrillMatchesAsync(client, $"from={Iso(drillFrom)}&to={Iso(drillTo)}&{rest}", point.Revenue, point.Orders);
        }
    }

    [Theory]
    [InlineData("category", "")]
    [InlineData("region", "")]
    [InlineData("category", "from=2025-03-01&to=2025-03-31&channel=Online%20Store")]
    [InlineData("region", "category=Equipment")]
    public async Task Every_breakdown_bar_drills_to_exactly_its_completed_lines(string by, string filter)
    {
        using var client = await StartAsync();

        var breakdown = await GetAsync<Breakdown>(client, $"/api/metrics/breakdown?by={by}&{filter}");
        Assert.NotEmpty(breakdown.Groups);

        foreach (var group in breakdown.Groups)
        {
            // The drill-down narrows the clicked dimension to the bar's key and keeps the rest of the filter.
            var others = filter.Split('&', StringSplitOptions.RemoveEmptyEntries).Where(p => !p.StartsWith($"{by}="));
            var drilled = string.Join('&', others.Append($"{by}={Uri.EscapeDataString(group.Key)}"));
            await AssertDrillMatchesAsync(client, drilled, group.Revenue, group.Orders);
        }
    }

    [Fact]
    public async Task Week_and_month_drill_downs_include_both_ends_and_nothing_outside()
    {
        using var client = await StartAsync();

        // The ISO week of Mon Mar 31, 2025 ends on Sun Apr 6.
        var (_, week) = await GridAsync(client, "from=2025-03-31&to=2025-04-06");
        var weekDates = week.Select(r => r.OrderDate).Distinct().Order().ToList();
        Assert.Equal(new DateOnly(2025, 3, 31), weekDates.First());
        Assert.Equal(new DateOnly(2025, 4, 6), weekDates.Last());

        // February 2025 ends on the 28th; March 1 belongs to the next month.
        var (_, february) = await GridAsync(client, "from=2025-02-01&to=2025-02-28");
        Assert.Equal(new DateOnly(2025, 2, 28), february.Max(r => r.OrderDate));
        Assert.DoesNotContain(february, r => r.OrderDate.Month != 2);
    }
}
