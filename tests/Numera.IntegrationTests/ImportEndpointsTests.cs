using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Numera.Application.Entities;
using Numera.Infrastructure.Auth;

namespace Numera.IntegrationTests;

public class ImportEndpointsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private sealed record Summary(
        Guid Id, string FileName, string Status, int TotalRows, int ImportedRows, int RejectedRows,
        string[] Warnings, string? FailureReason);

    private sealed record Api(TestApiFactory Factory, HttpClient Client, string ConnectionString) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            return Factory.DisposeAsync();
        }
    }

    private async Task<Api> StartAsync(bool signedIn = true)
    {
        var cs = await postgres.CreateDatabaseAsync();
        var factory = new TestApiFactory(cs);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DemoUserSeeder>().EnsureAsync();
        }

        var client = factory.CreateClient();
        if (signedIn)
        {
            var login = await client.PostAsJsonAsync("/api/auth/login", new { email = DemoUser.Email, password = DemoUser.Password });
            var body = await login.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        }

        return new Api(factory, client, cs);
    }

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static Task<HttpResponseMessage> UploadAsync(
        HttpClient client, byte[] content, string fileName = "orders.csv", string contentType = "text/csv", string field = "file")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        var form = new MultipartFormDataContent { { file, field, fileName } };
        return client.PostAsync("/api/imports", form);
    }

    private static async Task<Summary> ReadSummaryAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Summary>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;

    private static NumeraDbContextHandle Db(Api api) => new(PostgresFixture.CreateContext(api.ConnectionString));

    private sealed class NumeraDbContextHandle(Infrastructure.Persistence.NumeraDbContext db) : IAsyncDisposable
    {
        public Infrastructure.Persistence.NumeraDbContext Context => db;

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    [Fact]
    public async Task A_valid_file_imports_every_row_with_correct_counts()
    {
        await using var api = await StartAsync();

        var response = await UploadAsync(api.Client, Fixture("valid-orders.csv"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var summary = await ReadSummaryAsync(response);
        Assert.Equal("completed", summary.Status);
        Assert.Equal(10, summary.TotalRows);
        Assert.Equal(10, summary.ImportedRows);
        Assert.Equal(0, summary.RejectedRows);
        Assert.Equal("orders.csv", summary.FileName);

        await using var db = Db(api);
        Assert.Equal(10, await db.Context.Orders.CountAsync());
        Assert.All(await db.Context.Orders.ToListAsync(), o => Assert.Equal(summary.Id, o.ImportJobId));
        Assert.Equal(0, await db.Context.ImportErrors.CountAsync());

        var job = await db.Context.ImportJobs.SingleAsync();
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(10, job.ImportedRows);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public async Task Imported_values_are_stored_exactly()
    {
        await using var api = await StartAsync();

        await UploadAsync(api.Client, Fixture("valid-orders.csv"));

        await using var db = Db(api);
        var order = await db.Context.Orders.SingleAsync(o => o.OrderId == "T-0002");
        Assert.Equal(new DateOnly(2025, 3, 2), order.OrderDate);
        Assert.Equal("North America", order.Region);
        Assert.Equal("Mobile App", order.Channel);
        Assert.Equal("SKU-B", order.ProductSku);
        Assert.Equal(2, order.Quantity);
        Assert.Equal(18.50m, order.UnitPrice);
        Assert.Equal(0.05m, order.Discount);
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(OrderStatus.Refunded, (await db.Context.Orders.SingleAsync(o => o.OrderId == "T-0003")).Status);
    }

    [Fact]
    public async Task A_file_with_bad_rows_imports_the_good_ones_and_records_exactly_the_bad_ones()
    {
        await using var api = await StartAsync();

        var response = await UploadAsync(api.Client, Fixture("mixed-rows.csv"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var summary = await ReadSummaryAsync(response);
        Assert.Equal(10, summary.TotalRows);
        Assert.Equal(4, summary.ImportedRows);
        Assert.Equal(6, summary.RejectedRows);

        await using var db = Db(api);
        var imported = await db.Context.Orders.Select(o => o.OrderId).OrderBy(x => x).ToListAsync();
        Assert.Equal(["M-0001", "M-0002", "M-0008", "M-0010"], imported);

        var errors = await db.Context.ImportErrors.OrderBy(e => e.RowNumber).ThenBy(e => e.Field).ToListAsync();
        Assert.Equal(
            [
                (4, "quantity"),
                (5, "discount"),
                (6, "order_date"),
                (7, "status"),
                (8, "order_id"),
                (10, "status"),
                (10, "unit_price"),
            ],
            errors.Select(e => (e.RowNumber, e.Field!)));
        Assert.Equal(6, errors.Select(e => e.RowNumber).Distinct().Count());
        Assert.All(errors, e => Assert.Equal(summary.Id, e.ImportJobId));
        Assert.Equal(
            "M-0003,2025-04-03,C-003,Europe,Online Store,SKU-A,Blends,0,14.00,0,completed",
            errors.First(e => e.RowNumber == 4).RawRow);
        Assert.Contains("first seen on row 2", errors.Single(e => e.RowNumber == 8).Message);
    }

    [Fact]
    public async Task Rows_that_already_exist_in_the_database_are_rejected_not_duplicated()
    {
        await using var api = await StartAsync();
        await UploadAsync(api.Client, Fixture("valid-orders.csv"));

        var response = await UploadAsync(api.Client, Fixture("valid-orders.csv"));

        var summary = await ReadSummaryAsync(response);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(10, summary.TotalRows);
        Assert.Equal(0, summary.ImportedRows);
        Assert.Equal(10, summary.RejectedRows);

        await using var db = Db(api);
        Assert.Equal(10, await db.Context.Orders.CountAsync());
        Assert.Equal(10, await db.Context.ImportErrors.CountAsync(e => e.ImportJobId == summary.Id));
        Assert.All(
            await db.Context.ImportErrors.Where(e => e.ImportJobId == summary.Id).ToListAsync(),
            e => Assert.Contains("already exist", e.Message));
    }

    [Fact]
    public async Task Bom_crlf_and_quoted_fields_import_correctly()
    {
        await using var api = await StartAsync();

        var response = await UploadAsync(api.Client, Fixture("quoted-bom-crlf.csv"));

        var summary = await ReadSummaryAsync(response);
        Assert.Equal(3, summary.ImportedRows);
        await using var db = Db(api);
        var first = await db.Context.Orders.SingleAsync(o => o.OrderId == "Q-0001");
        Assert.Equal("Europe, West", first.Region);
        Assert.Equal("Single Origin, Roasted", first.ProductCategory);
        var second = await db.Context.Orders.SingleAsync(o => o.OrderId == "Q-0002");
        Assert.Equal("Online \"Store\"", second.Channel);
        Assert.Equal("Blends\nHouse", second.ProductCategory);
    }

    [Fact]
    public async Task Unknown_columns_are_ignored_with_a_warning_and_other_columns_matched_case_insensitively()
    {
        await using var api = await StartAsync();

        var response = await UploadAsync(api.Client, Fixture("unknown-columns.csv"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var summary = await ReadSummaryAsync(response);
        Assert.Equal(2, summary.ImportedRows);
        Assert.Equal(
            ["Unknown column 'notes' was ignored.", "Unknown column 'loyalty_tier' was ignored."],
            summary.Warnings);

        var fetched = await ReadSummaryAsync(await api.Client.GetAsync($"/api/imports/{summary.Id}"));
        Assert.Equal(summary.Warnings, fetched.Warnings);
    }

    [Theory]
    [InlineData("missing-column.csv", "unit_price")]
    [InlineData("header-only.csv", "no data rows")]
    [InlineData("empty.csv", "empty")]
    public async Task A_file_that_cannot_be_imported_is_rejected_whole_with_a_clear_message(string fixture, string expectedText)
    {
        await using var api = await StartAsync();

        var response = await UploadAsync(api.Client, Fixture(fixture));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(expectedText, problem.GetProperty("detail").GetString());
        var importId = problem.GetProperty("importId").GetGuid();

        await using var db = Db(api);
        Assert.Equal(0, await db.Context.Orders.CountAsync());
        var job = await db.Context.ImportJobs.SingleAsync(j => j.Id == importId);
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Contains(expectedText, job.FailureReason);
    }

    [Fact]
    public async Task More_than_100000_rows_are_rejected_and_nothing_is_imported()
    {
        await using var api = await StartAsync();
        var sb = new StringBuilder("order_id,order_date,customer_id,region,channel,product_sku,product_category,quantity,unit_price,discount,status\n");
        for (var i = 0; i < 100_001; i++)
        {
            sb.Append("L-").Append(i).Append(",2025-01-01,C,EU,web,S,C,1,1,0,completed\n");
        }

        var response = await UploadAsync(api.Client, Encoding.UTF8.GetBytes(sb.ToString()));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("100,000", await response.Content.ReadAsStringAsync());
        await using var db = Db(api);
        Assert.Equal(0, await db.Context.Orders.CountAsync());
    }

    [Fact]
    public async Task Exactly_100000_rows_are_imported()
    {
        await using var api = await StartAsync();
        var sb = new StringBuilder("order_id,order_date,customer_id,region,channel,product_sku,product_category,quantity,unit_price,discount,status\n");
        for (var i = 0; i < 100_000; i++)
        {
            sb.Append("L-").Append(i).Append(",2025-01-01,C,EU,web,S,C,1,1,0,completed\n");
        }

        var response = await UploadAsync(api.Client, Encoding.UTF8.GetBytes(sb.ToString()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var summary = await ReadSummaryAsync(response);
        Assert.Equal(100_000, summary.ImportedRows);
        await using var db = Db(api);
        Assert.Equal(100_000, await db.Context.Orders.CountAsync());
    }

    [Fact]
    public async Task A_file_over_10_MB_is_rejected_with_413()
    {
        await using var api = await StartAsync();
        var big = new byte[(10 * 1024 * 1024) + 1];
        Array.Fill(big, (byte)'a');

        var response = await UploadAsync(api.Client, big);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        await using var db = Db(api);
        Assert.Equal(0, await db.Context.ImportJobs.CountAsync());
    }

    [Theory]
    [InlineData("orders.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("orders.csv", "image/png")]
    [InlineData("orders.txt", "text/plain")]
    [InlineData("orders.csv.exe", "text/csv")]
    public async Task A_file_that_is_not_a_csv_is_rejected_with_415(string fileName, string contentType)
    {
        await using var api = await StartAsync();

        var response = await UploadAsync(api.Client, Fixture("valid-orders.csv"), fileName, contentType);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        await using var db = Db(api);
        Assert.Equal(0, await db.Context.Orders.CountAsync());
    }

    [Fact]
    public async Task A_binary_file_named_csv_is_rejected_with_415()
    {
        await using var api = await StartAsync();
        byte[] binary = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00, 0x08];

        var response = await UploadAsync(api.Client, binary);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task A_request_without_a_file_returns_400()
    {
        await using var api = await StartAsync();
        var form = new MultipartFormDataContent { { new StringContent("hello"), "note" } };

        var response = await api.Client.PostAsync("/api/imports", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_multipart_body_returns_400_not_413()
    {
        await using var api = await StartAsync();
        var content = new StringContent("this is not a multipart body");
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=numera-boundary");

        var response = await api.Client.PostAsync("/api/imports", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("multipart", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_non_multipart_request_returns_415()
    {
        await using var api = await StartAsync();

        var response = await api.Client.PostAsJsonAsync("/api/imports", new { file = "x" });

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Import_requires_authentication()
    {
        await using var api = await StartAsync(signedIn: false);

        var response = await UploadAsync(api.Client, Fixture("valid-orders.csv"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var db = Db(api);
        Assert.Equal(0, await db.Context.ImportJobs.CountAsync());
    }

    [Fact]
    public async Task Import_status_requires_authentication()
    {
        await using var api = await StartAsync(signedIn: false);

        var response = await api.Client.GetAsync($"/api/imports/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Import_status_returns_the_job_and_404_for_unknown_ids()
    {
        await using var api = await StartAsync();
        var created = await ReadSummaryAsync(await UploadAsync(api.Client, Fixture("mixed-rows.csv")));

        var fetched = await ReadSummaryAsync(await api.Client.GetAsync($"/api/imports/{created.Id}"));
        var missing = await api.Client.GetAsync($"/api/imports/{Guid.NewGuid()}");

        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("completed", fetched.Status);
        Assert.Equal(4, fetched.ImportedRows);
        Assert.Equal(6, fetched.RejectedRows);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_formula_looking_value_is_stored_as_text_and_never_evaluated()
    {
        await using var api = await StartAsync();
        var csv = "order_id,order_date,customer_id,region,channel,product_sku,product_category,quantity,unit_price,discount,status\n" +
                  "F-1,2025-01-01,C-1,\"=HYPERLINK(\"\"http://example.test\"\")\",web,S,C,1,1,0,completed\n";

        var response = await UploadAsync(api.Client, Encoding.UTF8.GetBytes(csv));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = Db(api);
        Assert.Equal("=HYPERLINK(\"http://example.test\")", (await db.Context.Orders.SingleAsync()).Region);
    }

    // error report and history -------------------------------------------------------------------

    private static async Task<List<string[]>> ReadCsvAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        using var csv = new CsvHelper.CsvParser(reader, new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture) { HasHeaderRecord = false });
        var rows = new List<string[]>();
        while (csv.Read())
        {
            rows.Add(csv.Record!);
        }

        return rows;
    }

    [Fact]
    public async Task The_error_report_matches_the_rejected_rows_exactly()
    {
        await using var api = await StartAsync();
        var summary = await ReadSummaryAsync(await UploadAsync(api.Client, Fixture("mixed-rows.csv")));

        var response = await api.Client.GetAsync($"/api/imports/{summary.Id}/errors.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"import-{summary.Id}-errors.csv", response.Content.Headers.ContentDisposition?.ToString());

        var rows = await ReadCsvAsync(response);
        Assert.Equal(["row_number", "field", "message", "raw_row"], rows[0]);

        await using var db = Db(api);
        var expected = await db.Context.ImportErrors
            .Where(e => e.ImportJobId == summary.Id)
            .OrderBy(e => e.RowNumber).ThenBy(e => e.Id)
            .ToListAsync();
        Assert.Equal(7, expected.Count);
        Assert.Equal(expected.Count, rows.Count - 1);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].RowNumber.ToString(), rows[i + 1][0]);
            Assert.Equal(expected[i].Field ?? string.Empty, rows[i + 1][1]);
            Assert.Equal(expected[i].Message, rows[i + 1][2]);
            Assert.Equal(expected[i].RawRow, rows[i + 1][3]);
        }

        // Spot-check the visible content: row 4 has quantity 0.
        Assert.Equal(
            ["4", "quantity", "quantity must be greater than 0.", "M-0003,2025-04-03,C-003,Europe,Online Store,SKU-A,Blends,0,14.00,0,completed"],
            rows[1]);
    }

    [Fact]
    public async Task Values_starting_with_formula_characters_are_neutralized_in_the_error_report()
    {
        await using var api = await StartAsync();
        var csv = "order_id,order_date,customer_id,region,channel,product_sku,product_category,quantity,unit_price,discount,status\n" +
                  "=2+2,2025-01-01,C-1,EU,web,S,C,0,1,0,completed\n" +   // raw row starts with '='
                  "+1,2025-01-01,C-1,EU,web,S,C,0,1,0,completed\n" +
                  "-1,2025-01-01,C-1,EU,web,S,C,0,1,0,completed\n" +
                  "@SUM(A1),2025-01-01,C-1,EU,web,S,C,0,1,0,completed\n";
        var summary = await ReadSummaryAsync(await UploadAsync(api.Client, Encoding.UTF8.GetBytes(csv)));
        Assert.Equal(4, summary.RejectedRows);

        var rows = await ReadCsvAsync(await api.Client.GetAsync($"/api/imports/{summary.Id}/errors.csv"));

        Assert.Equal(5, rows.Count);
        Assert.All(rows.Skip(1), r => Assert.StartsWith("'", r[3]));
        Assert.StartsWith("'=2+2,", rows[1][3]);
        Assert.StartsWith("'+1,", rows[2][3]);
        Assert.StartsWith("'-1,", rows[3][3]);
        Assert.StartsWith("'@SUM(A1),", rows[4][3]);
        // The stored data is untouched; only the export is neutralized.
        await using var db = Db(api);
        Assert.StartsWith("=2+2,", (await db.Context.ImportErrors.OrderBy(e => e.RowNumber).FirstAsync()).RawRow);
    }

    [Fact]
    public async Task The_error_report_for_an_import_without_rejects_has_only_the_header()
    {
        await using var api = await StartAsync();
        var summary = await ReadSummaryAsync(await UploadAsync(api.Client, Fixture("valid-orders.csv")));

        var rows = await ReadCsvAsync(await api.Client.GetAsync($"/api/imports/{summary.Id}/errors.csv"));

        Assert.Single(rows);
    }

    [Fact]
    public async Task The_error_report_for_an_unknown_import_returns_404_problem_details()
    {
        await using var api = await StartAsync();

        var response = await api.Client.GetAsync($"/api/imports/{Guid.NewGuid()}/errors.csv");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_error_report_requires_authentication()
    {
        await using var api = await StartAsync(signedIn: false);

        var response = await api.Client.GetAsync($"/api/imports/{Guid.NewGuid()}/errors.csv");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task History_lists_recent_imports_newest_first_and_honours_the_limit()
    {
        await using var api = await StartAsync();
        var first = await ReadSummaryAsync(await UploadAsync(api.Client, Fixture("valid-orders.csv"), "first.csv"));
        var second = await ReadSummaryAsync(await UploadAsync(api.Client, Fixture("mixed-rows.csv"), "second.csv"));

        var all = await api.Client.GetFromJsonAsync<List<Summary>>("/api/imports", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var limited = await api.Client.GetFromJsonAsync<List<Summary>>("/api/imports?limit=1", new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal([second.Id, first.Id], all!.Select(j => j.Id));
        Assert.Equal(6, all![0].RejectedRows);
        Assert.Equal([second.Id], limited!.Select(j => j.Id));
    }

    [Fact]
    public async Task History_is_empty_before_any_import_and_requires_authentication()
    {
        await using var api = await StartAsync();
        await using var anonymous = await StartAsync(signedIn: false);

        var list = await api.Client.GetFromJsonAsync<List<Summary>>("/api/imports", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var denied = await anonymous.Client.GetAsync("/api/imports");

        Assert.Empty(list!);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }
}