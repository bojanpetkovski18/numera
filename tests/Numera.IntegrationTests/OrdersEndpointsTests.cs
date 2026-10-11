using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Numera.Application.Entities;
using Numera.Infrastructure.Auth;

namespace Numera.IntegrationTests;

/// <summary>Expected pages are computed with LINQ over the rows read back from the database, never with the SQL under test.</summary>
public class OrdersEndpointsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Row(long Id, string OrderId, DateOnly OrderDate, string CustomerId, string Region, string Channel,
        string ProductSku, string ProductCategory, int Quantity, decimal UnitPrice, decimal Discount, decimal LineTotal, string Status);

    private sealed record GridPage(Row[] Items, int TotalCount, int Page, int PageSize, int TotalPages);

    private sealed record Summary(decimal Revenue, int Orders, decimal AverageOrderValue, decimal RefundRate, int CompletedLines, int RefundedLines);

    private sealed record Api(TestApiFactory Factory, HttpClient Client, List<Order> Rows) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            return Factory.DisposeAsync();
        }
    }

    private static readonly string[] Regions = ["Europe", "North America", "Asia-Pacific"];
    private static readonly string[] Channels = ["Online Store", "Mobile App"];
    private static readonly string[] Categories = ["Blends", "Equipment", "Single Origin"];
    private static readonly OrderStatus[] Statuses = [OrderStatus.Completed, OrderStatus.Completed, OrderStatus.Refunded, OrderStatus.Cancelled];

    /// <summary>60 lines with every combination spread out; order 7 has an underscore and order 8 a percent sign.</summary>
    private static List<Order> Dataset() =>
        Enumerable.Range(1, 60).Select(i => new Order
        {
            OrderId = i switch { 7 => "SP_007", 8 => "SP%008", _ => $"ORD-{i:D4}" },
            OrderDate = new DateOnly(2025, 1, 1).AddDays(i * 3),
            CustomerId = $"C-{i % 7:D3}",
            Region = Regions[i % 3],
            Channel = Channels[i % 2],
            ProductSku = $"SKU-{i % 5}",
            ProductCategory = Categories[i % 3 == 0 ? 2 : i % 2],
            Quantity = 1 + (i % 4),
            UnitPrice = 5m + (i % 9) * 2.5m,
            Discount = (i % 5) * 0.05m,
            Status = Statuses[i % 4],
        }).ToList();

    private async Task<Api> StartAsync(bool signedIn = true)
    {
        var cs = await postgres.CreateDatabaseAsync();
        List<Order> rows;
        await using (var db = PostgresFixture.CreateContext(cs))
        {
            db.Orders.AddRange(Dataset());
            await db.SaveChangesAsync();
            rows = await db.Orders.AsNoTracking().OrderBy(o => o.Id).ToListAsync();
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

        return new Api(factory, client, rows);
    }

    private static async Task<GridPage> GetPageAsync(Api api, string query)
    {
        var response = await api.Client.GetAsync($"/api/orders?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GridPage>(Json))!;
    }

    private static string Status(Order o) => o.Status.ToString().ToLowerInvariant();

    // paging --------------------------------------------------------------------------------------

    [Fact]
    public async Task Default_page_is_newest_first_25_rows_with_totals()
    {
        await using var api = await StartAsync();

        var page = await GetPageAsync(api, "");

        var expected = api.Rows.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id).Take(25).Select(o => o.Id);
        Assert.Equal(expected, page.Items.Select(r => r.Id));
        Assert.Equal(60, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(25, page.PageSize);
        Assert.Equal(3, page.TotalPages);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public async Task Pages_are_consecutive_slices_of_the_sorted_rows(int pageNumber)
    {
        await using var api = await StartAsync();

        var page = await GetPageAsync(api, $"sort=orderId&dir=asc&page={pageNumber}&pageSize=10");

        var expected = api.Rows.OrderBy(o => o.OrderId, StringComparer.Ordinal).ThenBy(o => o.Id).Skip((pageNumber - 1) * 10).Take(10);
        Assert.Equal(expected.Select(o => o.OrderId), page.Items.Select(r => r.OrderId));
        Assert.Equal(6, page.TotalPages);
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_but_keeps_the_total()
    {
        await using var api = await StartAsync();

        var page = await GetPageAsync(api, "page=99&pageSize=25");

        Assert.Empty(page.Items);
        Assert.Equal(60, page.TotalCount);
        Assert.Equal(99, page.Page);
    }

    [Fact]
    public async Task Rows_carry_every_field_and_a_rounded_line_total()
    {
        await using var api = await StartAsync();
        var source = api.Rows.Single(o => o.OrderId == "ORD-0011");

        var row = (await GetPageAsync(api, "q=ORD-0011")).Items.Single();

        Assert.Equal(source.Id, row.Id);
        Assert.Equal(source.OrderDate, row.OrderDate);
        Assert.Equal(source.CustomerId, row.CustomerId);
        Assert.Equal(source.Region, row.Region);
        Assert.Equal(source.Channel, row.Channel);
        Assert.Equal(source.ProductSku, row.ProductSku);
        Assert.Equal(source.ProductCategory, row.ProductCategory);
        Assert.Equal(source.Quantity, row.Quantity);
        Assert.Equal(source.UnitPrice, row.UnitPrice);
        Assert.Equal(source.Discount, row.Discount);
        Assert.Equal(Math.Round(source.Quantity * source.UnitPrice * (1 - source.Discount), 2, MidpointRounding.AwayFromZero), row.LineTotal);
        Assert.Equal(Status(source), row.Status);
    }

    // sorting -------------------------------------------------------------------------------------

    public static TheoryData<string, string> SortCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var field in new[] { "orderDate", "orderId", "customerId", "region", "channel", "productCategory", "productSku", "quantity", "unitPrice", "discount", "lineTotal", "status" })
        {
            data.Add(field, "asc");
            data.Add(field, "desc");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SortCases))]
    public async Task Every_whitelisted_column_sorts_both_ways_with_id_as_tie_breaker(string field, string dir)
    {
        await using var api = await StartAsync();

        var page = await GetPageAsync(api, $"sort={field}&dir={dir}&pageSize=100");

        Func<Order, object> key = field switch
        {
            "orderDate" => o => o.OrderDate,
            "orderId" => o => o.OrderId,
            "customerId" => o => o.CustomerId,
            "region" => o => o.Region,
            "channel" => o => o.Channel,
            "productCategory" => o => o.ProductCategory,
            "productSku" => o => o.ProductSku,
            "quantity" => o => o.Quantity,
            "unitPrice" => o => o.UnitPrice,
            "discount" => o => o.Discount,
            "lineTotal" => o => o.Quantity * o.UnitPrice * (1 - o.Discount),
            _ => o => Status(o),
        };
        var comparer = Comparer<object>.Create((a, b) => a is string sa && b is string sb ? string.CompareOrdinal(sa, sb) : Comparer<object>.Default.Compare(a, b));
        var expected = dir == "asc"
            ? api.Rows.OrderBy(key, comparer).ThenBy(o => o.Id)
            : api.Rows.OrderByDescending(key, comparer).ThenByDescending(o => o.Id);

        Assert.Equal(expected.Select(o => o.Id), page.Items.Select(r => r.Id));
    }

    // filtering -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("region=Europe")]
    [InlineData("channel=Mobile%20App")]
    [InlineData("category=Equipment")]
    [InlineData("status=refunded")]
    [InlineData("status=refunded&status=cancelled")]
    [InlineData("from=2025-02-01&to=2025-03-31")]
    [InlineData("region=Europe&channel=Online%20Store&status=completed&from=2025-01-01&to=2025-06-30")]
    [InlineData("region=Antarctica")]
    public async Task Each_filter_and_combination_returns_exactly_the_matching_rows(string query)
    {
        await using var api = await StartAsync();

        var page = await GetPageAsync(api, $"{query}&pageSize=100");

        var parsed = System.Web.HttpUtility.ParseQueryString(query);
        bool In(string? name, string value) => parsed.GetValues(name) is not { } values || values.Contains(value);
        var expected = api.Rows.Where(o =>
            In("region", o.Region) && In("channel", o.Channel) && In("category", o.ProductCategory) && In("status", Status(o)) &&
            (parsed["from"] is not { } from || o.OrderDate >= DateOnly.Parse(from)) &&
            (parsed["to"] is not { } to || o.OrderDate <= DateOnly.Parse(to)))
            .Select(o => o.Id).Order().ToList();

        Assert.Equal(expected.Count, page.TotalCount);
        Assert.Equal(expected, page.Items.Select(r => r.Id).Order());
        Assert.Equal(query.Contains("Antarctica") ? 0 : expected.Count, page.Items.Length);
    }

    [Theory]
    [InlineData("ord-001", new[] { "ORD-0010", "ORD-0011", "ORD-0012", "ORD-0013", "ORD-0014", "ORD-0015", "ORD-0016", "ORD-0017", "ORD-0018", "ORD-0019" })]
    [InlineData("0042", new[] { "ORD-0042" })]
    [InlineData("_", new[] { "SP_007" })]      // underscore is literal, not "any character"
    [InlineData("%", new[] { "SP%008" })]      // percent is literal, not "anything"
    [InlineData("nothing-like-this", new string[0])]
    public async Task Order_id_search_is_a_case_insensitive_literal_contains(string term, string[] expected)
    {
        await using var api = await StartAsync();

        var page = await GetPageAsync(api, $"q={Uri.EscapeDataString(term)}&pageSize=100&sort=orderId&dir=asc");

        Assert.Equal(expected, page.Items.Select(r => r.OrderId));
        Assert.Equal(expected.Length, page.TotalCount);
    }

    [Fact]
    public async Task Search_combines_with_filters()
    {
        await using var api = await StartAsync();

        var page = await GetPageAsync(api, "q=ORD-00&region=Europe&pageSize=100");

        var expected = api.Rows.Where(o => o.OrderId.Contains("ORD-00") && o.Region == "Europe").Select(o => o.Id).Order();
        Assert.Equal(expected, page.Items.Select(r => r.Id).Order());
    }

    [Theory]
    [InlineData("")]
    [InlineData("region=Europe")]
    [InlineData("from=2025-03-01&to=2025-05-31&channel=Mobile%20App")]
    public async Task Grid_totals_match_the_lines_the_dashboard_metrics_count(string filter)
    {
        // Definition of the check: for the same filter, the grid's total of completed lines equals the summary's
        // completedLines, and completed + refunded lines equals the refund-rate denominator.
        await using var api = await StartAsync();

        var summary = (await api.Client.GetFromJsonAsync<Summary>($"/api/metrics/summary?{filter}", Json))!;
        var completed = await GetPageAsync(api, $"{filter}&status=completed");
        var completedOrRefunded = await GetPageAsync(api, $"{filter}&status=completed&status=refunded");

        Assert.Equal(summary.CompletedLines, completed.TotalCount);
        Assert.Equal(summary.CompletedLines + summary.RefundedLines, completedOrRefunded.TotalCount);
    }

    // validation ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("sort=password_hash", "sort")]
    [InlineData("sort=id;DROP TABLE orders", "sort")]
    [InlineData("dir=sideways", "dir")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("page=0", "page")]
    [InlineData("page=-1", "page")]
    [InlineData("page=abc", "page")]
    [InlineData("status=shipped", "status")]
    [InlineData("from=2025-03-01&to=2025-01-01", "to")]
    public async Task Bad_parameters_return_400_naming_the_field(string query, string field)
    {
        await using var api = await StartAsync();

        var response = await api.Client.GetAsync($"/api/orders?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task The_maximum_page_size_is_allowed()
    {
        await using var api = await StartAsync();

        var page = await GetPageAsync(api, "pageSize=100");

        Assert.Equal(60, page.Items.Length);
        Assert.Equal(100, page.PageSize);
    }

    [Fact]
    public async Task Search_text_longer_than_40_characters_is_rejected()
    {
        await using var api = await StartAsync();

        var response = await api.Client.GetAsync($"/api/orders?q={new string('x', 41)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Orders_require_authentication()
    {
        await using var api = await StartAsync(signedIn: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task The_trigram_index_exists_after_migration()
    {
        await using var api = await StartAsync();
        await using var db = api.Factory.Services.CreateScope().ServiceProvider.GetRequiredService<Infrastructure.Persistence.NumeraDbContext>();

        var definition = await db.Database.SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = 'ix_orders_order_id_trgm'").SingleAsync();

        Assert.Contains("gin", definition);
        Assert.Contains("gin_trgm_ops", definition);
    }
}