using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Numera.Application.Entities;
using Numera.Infrastructure.Auth;

namespace Numera.IntegrationTests;

/// <summary>
/// The export must hold exactly the rows the grid shows. Each test reads the grid page by page through
/// <c>GET /api/orders</c> and compares it with the parsed CSV from <c>GET /api/orders/export</c> for the same query.
/// The CSV is parsed with CsvHelper, not with the code that wrote it.
/// </summary>
public class OrdersExportTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Row(long Id, string OrderId, DateOnly OrderDate, string CustomerId, string Region, string Channel,
        string ProductSku, string ProductCategory, int Quantity, decimal UnitPrice, decimal Discount, decimal LineTotal, string Status);

    private sealed record GridPage(Row[] Items, int TotalCount, int Page, int PageSize, int TotalPages);

    private sealed record Export(HttpResponseMessage Response, byte[] Bytes, string[] Header, List<string[]> Records);

    private static readonly string[] Regions = ["Europe", "North America", "Asia-Pacific"];
    private static readonly string[] Channels = ["Online Store", "Mobile App"];
    private static readonly string[] Categories = ["Blends", "Equipment", "Single Origin"];
    private static readonly OrderStatus[] Statuses = [OrderStatus.Completed, OrderStatus.Completed, OrderStatus.Refunded, OrderStatus.Cancelled];

    /// <summary>250 lines (more than two full grid pages of 100) with repeated dates and prices, so ties are common.</summary>
    private static List<Order> Dataset() =>
        Enumerable.Range(1, 250).Select(i => new Order
        {
            OrderId = $"EX-{i:D4}",
            OrderDate = new DateOnly(2025, 1, 1).AddDays(i % 40),
            CustomerId = $"C-{i % 11:D3}",
            Region = Regions[i % 3],
            Channel = Channels[i % 2],
            ProductSku = $"SKU-{i % 5}",
            ProductCategory = Categories[i % 3 == 0 ? 2 : i % 2],
            Quantity = 1 + i % 4,
            UnitPrice = 5m + i % 9 * 2.5m,
            Discount = i % 5 * 0.05m,
            Status = Statuses[i % 4],
        }).ToList();

    /// <summary>Lines whose text would run as a formula in a spreadsheet, and text that needs quoting.</summary>
    private static List<Order> Hostile() =>
    [
        Line("=HYPERLINK(\"http://x\",\"y\")", "+44 20", "@SUM(A1)", "-1+1"),
        Line("ORD-QUOTE", "C, with comma", "Say \"hi\"", "Plain"),
    ];

    private static Order Line(string orderId, string customer, string region, string category) => new()
    {
        OrderId = orderId,
        OrderDate = new DateOnly(2025, 6, 1),
        CustomerId = customer,
        Region = region,
        Channel = "Wholesale",
        ProductSku = "SKU-H",
        ProductCategory = category,
        Quantity = 2,
        UnitPrice = 10m,
        Discount = 0m,
        Status = OrderStatus.Completed,
    };

    private async Task<(TestApiFactory Factory, HttpClient Client, string ConnectionString)> StartAsync(List<Order> rows, bool signedIn = true)
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using (var db = PostgresFixture.CreateContext(cs))
        {
            db.Orders.AddRange(rows);
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

        return (factory, client, cs);
    }

    private static async Task<List<Row>> GridAsync(HttpClient client, string query)
    {
        var rows = new List<Row>();
        GridPage page;
        var number = 1;
        do
        {
            var response = await client.GetAsync($"/api/orders?{query}&pageSize=100&page={number++}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            page = (await response.Content.ReadFromJsonAsync<GridPage>(Json))!;
            rows.AddRange(page.Items);
        }
        while (page.Page < page.TotalPages);
        return rows;
    }

    private static async Task<Export> ExportAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/orders/export?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = false });
        var records = new List<string[]>();
        while (await csv.ReadAsync())
        {
            records.Add(csv.Parser.Record!);
        }
        return new Export(response, bytes, records[0], records.Skip(1).ToList());
    }

    /// <summary>A grid row as the export should write it.</summary>
    private static string[] Expected(Row r) =>
    [
        r.OrderId, r.OrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.CustomerId, r.Region, r.Channel,
        r.ProductSku, r.ProductCategory, r.Quantity.ToString(CultureInfo.InvariantCulture),
        r.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture), r.Discount.ToString("0.##", CultureInfo.InvariantCulture),
        r.Status, r.LineTotal.ToString("0.00", CultureInfo.InvariantCulture),
    ];

    [Theory]
    [InlineData("")]                                                   // default: newest first
    [InlineData("sort=lineTotal&dir=desc")]
    [InlineData("sort=orderDate&dir=asc&region=Europe&region=Asia-Pacific")]
    [InlineData("sort=status&status=completed&status=refunded&channel=Mobile%20App")]
    [InlineData("sort=unitPrice&dir=asc&from=2025-01-10&to=2025-01-25&category=Blends")]
    [InlineData("q=EX-00&sort=orderId&dir=desc")]
    public async Task The_export_holds_exactly_the_grid_rows_in_the_grid_order(string query)
    {
        var (factory, client, _) = await StartAsync(Dataset());
        await using var _f = factory;
        using var _c = client;

        var grid = await GridAsync(client, query);
        var export = await ExportAsync(client, query);

        Assert.NotEmpty(grid);
        // Same rows, same values, same order. Compared cell by cell so a failure names the cell.
        Assert.Equal(grid.Count, export.Records.Count);
        for (var i = 0; i < grid.Count; i++)
        {
            var expected = Expected(grid[i]);
            for (var c = 0; c < expected.Length; c++)
            {
                Assert.True(expected[c] == export.Records[i][c], $"row {i + 1}, {export.Header[c]}: expected '{expected[c]}', got '{export.Records[i][c]}'");
            }
            Assert.Equal(expected.Length, export.Records[i].Length);
        }
        Assert.Equal(grid.Count.ToString(CultureInfo.InvariantCulture), export.Response.Headers.GetValues("X-Row-Count").Single());
    }

    [Fact]
    public async Task The_file_is_utf8_with_a_bom_a_header_crlf_lines_and_a_descriptive_name()
    {
        var (factory, client, _) = await StartAsync(Dataset());
        await using var _f = factory;
        using var _c = client;

        var export = await ExportAsync(client, "region=Europe&from=2025-01-05&to=2025-01-20");

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, export.Bytes[..3]);
        var text = Encoding.UTF8.GetString(export.Bytes, 3, export.Bytes.Length - 3);
        Assert.StartsWith(
            "order_id,order_date,customer_id,region,channel,product_sku,product_category,quantity,unit_price,discount,status,line_total\r\n",
            text);
        Assert.EndsWith("\r\n", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty));

        Assert.Equal("text/csv", export.Response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", export.Response.Content.Headers.ContentType?.CharSet);
        var disposition = export.Response.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Matches(@"^numera-orders_\d{8}-\d{6}Z_2025-01-05-to-2025-01-20_europe\.csv$", disposition.FileName?.Trim('"'));
        Assert.Contains("no-store", export.Response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_filter_with_no_matches_returns_only_the_header()
    {
        var (factory, client, _) = await StartAsync(Dataset());
        await using var _f = factory;
        using var _c = client;

        var export = await ExportAsync(client, "region=Antarctica");

        Assert.Empty(export.Records);
        Assert.Equal("0", export.Response.Headers.GetValues("X-Row-Count").Single());
        Assert.Equal(
            "﻿order_id,order_date,customer_id,region,channel,product_sku,product_category,quantity,unit_price,discount,status,line_total\r\n",
            Encoding.UTF8.GetString(export.Bytes));
    }

    [Fact]
    public async Task Cells_that_a_spreadsheet_would_run_as_formulas_are_neutralized_and_others_are_quoted()
    {
        var (factory, client, _) = await StartAsync(Hostile());
        await using var _f = factory;
        using var _c = client;

        var export = await ExportAsync(client, "sort=orderId&dir=asc");
        var hostile = export.Records.Single(r => r[0].Contains("HYPERLINK"));
        var quoted = export.Records.Single(r => r[0] == "ORD-QUOTE");

        Assert.Equal("'=HYPERLINK(\"http://x\",\"y\")", hostile[0]);
        Assert.Equal("'+44 20", hostile[2]);
        Assert.Equal("'@SUM(A1)", hostile[3]);
        Assert.Equal("'-1+1", hostile[6]);
        Assert.Equal(["C, with comma", "Say \"hi\"", "Plain"], [quoted[2], quoted[3], quoted[6]]);
        Assert.All(export.Records.SelectMany(r => r), cell => Assert.False(cell.Length > 0 && cell[0] is '=' or '+' or '-' or '@'));
    }

    [Fact]
    public async Task The_export_requires_authentication()
    {
        var (factory, client, _) = await StartAsync(Dataset(), signedIn: false);
        await using var _f = factory;
        using var _c = client;

        var response = await client.GetAsync("/api/orders/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("sort=password", "sort")]
    [InlineData("dir=sideways&sort=region", "dir")]
    [InlineData("status=lost", "status")]
    [InlineData("from=2025-02-30", "from")]
    public async Task Invalid_parameters_are_rejected_like_the_grid_does(string query, string field)
    {
        var (factory, client, _) = await StartAsync(Dataset());
        await using var _f = factory;
        using var _c = client;

        var response = await client.GetAsync($"/api/orders/export?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task More_than_100000_rows_is_refused_with_a_clear_problem_instead_of_a_truncated_file()
    {
        var (factory, client, cs) = await StartAsync([]);
        await using var _f = factory;
        using var _c = client;
        await using (var db = PostgresFixture.CreateContext(cs))
        {
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO orders (order_id, order_date, customer_id, region, channel, product_sku, product_category,
                                    quantity, unit_price, discount, status)
                SELECT 'BULK-' || n, DATE '2025-01-01' + (n % 300), 'C-001', 'Europe', 'Wholesale', 'SKU-1', 'Blends',
                       1, 10.00, 0, 'completed'
                FROM generate_series(1, 100001) AS n
                """);
        }

        var tooMany = await client.GetAsync("/api/orders/export");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMany.StatusCode);
        var problem = await tooMany.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("100,001 order lines match", problem.GetProperty("detail").GetString());
        Assert.Equal(100001, problem.GetProperty("rowCount").GetInt64());
        Assert.Equal(100000, problem.GetProperty("maxRows").GetInt32());

        // Exactly at the limit is allowed, and every row is in the file.
        await using (var db = PostgresFixture.CreateContext(cs))
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM orders WHERE order_id = 'BULK-1'");
        }
        var atLimit = await client.GetAsync("/api/orders/export");
        Assert.Equal(HttpStatusCode.OK, atLimit.StatusCode);
        Assert.Equal("100000", atLimit.Headers.GetValues("X-Row-Count").Single());
        var body = await atLimit.Content.ReadAsStringAsync();
        Assert.Equal(100_001, body.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length); // header + rows
    }
}
