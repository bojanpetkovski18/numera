using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Numera.Application.Auth;
using Numera.Application.Entities;
using Numera.Infrastructure.Auth;

namespace Numera.IntegrationTests;

/// <summary>
/// Column mapping (M15): the preview, imports with a mapping, and saved mappings. The two sample files are the ones
/// the web app serves (web/public), copied next to the tests.
/// </summary>
public class ImportMappingEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Suggestion(Dictionary<string, string?> Columns, string DateFormat, string Discount);

    private sealed record MappingBody(Dictionary<string, string?> Columns, string DateFormat, string Discount);

    private sealed record Saved(Guid Id, string Name, MappingBody Mapping, string[] SourceHeaders);

    private sealed record Preview(string[] Headers, string[][] Rows, bool MatchesNumeraFormat, Suggestion Suggestion,
        bool DateFormatAmbiguous, string[] Unmapped, Saved? SavedMapping);

    private sealed record Summary(Guid Id, string Status, int TotalRows, int ImportedRows, int RejectedRows, string[] Warnings, string? FailureReason);

    private sealed record Api(TestApiFactory Factory, HttpClient Client, string ConnectionString) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            return Factory.DisposeAsync();
        }
    }

    private static readonly Dictionary<string, string?> SampleColumns = new()
    {
        ["order_id"] = "Order Number",
        ["order_date"] = "Date",
        ["customer_id"] = "Customer",
        ["region"] = "Sales Region",
        ["channel"] = "Sales Channel",
        ["product_sku"] = "SKU",
        ["product_category"] = "Category",
        ["quantity"] = "Qty",
        ["unit_price"] = "Price",
        ["discount"] = "Discount %",
        ["status"] = "Order Status",
    };

    private static readonly string SampleMapping =
        JsonSerializer.Serialize(new { columns = SampleColumns, dateFormat = "dd/MM/yyyy", discount = "percent" });

    private async Task<Api> StartAsync()
    {
        var cs = await postgres.CreateDatabaseAsync();
        var factory = new TestApiFactory(cs, settings: new Dictionary<string, string> { ["RateLimiting:ResetPermitLimit"] = "1000" });
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DemoUserSeeder>().EnsureAsync();
        }

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = DemoUser.Email, password = DemoUser.Password });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        return new Api(factory, client, cs);
    }

    /// <summary>A client signed in as a second user (created directly; only the demo user can sign in with a password).</summary>
    private static async Task<HttpClient> SecondUserAsync(Api api)
    {
        var user = new User { Email = "second@example.test", PasswordHash = "not-used", CreatedAt = DateTimeOffset.UtcNow };
        await using (var db = PostgresFixture.CreateContext(api.ConnectionString))
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        using var scope = api.Factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().Create(user);
        var client = api.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return client;
    }

    private static byte[] Sample(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Samples", name));

    private static MultipartFormDataContent Form(byte[] content, string fileName = "orders.csv", string? mapping = null, string contentType = "text/csv")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        if (mapping is not null)
        {
            form.Add(new StringContent(mapping), "mapping");
        }

        return form;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.True(expected == response.StatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, byte[] content, string fileName = "orders.csv", string contentType = "text/csv") =>
        client.PostAsync("/api/imports/preview", Form(content, fileName, contentType: contentType));

    private static Task<HttpResponseMessage> ImportAsync(HttpClient client, byte[] content, string? mapping = null) =>
        client.PostAsync("/api/imports", Form(content, mapping: mapping));

    // preview -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_preview_of_the_mapped_sample_suggests_every_column_and_flags_the_ambiguous_dates()
    {
        await using var api = await StartAsync();

        var preview = await ReadAsync<Preview>(await PreviewAsync(api.Client, Sample("sample-orders-mapped.csv")), HttpStatusCode.OK);

        Assert.Equal(12, preview.Headers.Length);
        Assert.Equal(5, preview.Rows.Length);
        Assert.Equal("Customer asked to cancel, no charge", preview.Rows[4][11]);
        Assert.False(preview.MatchesNumeraFormat);
        Assert.Equal(SampleColumns, preview.Suggestion.Columns);
        // 01/03 to 04/03 read as day/month and as month/day: the user has to confirm.
        Assert.Equal(("dd/MM/yyyy", "percent", true), (preview.Suggestion.DateFormat, preview.Suggestion.Discount, preview.DateFormatAmbiguous));
        Assert.Empty(preview.Unmapped);
        Assert.Null(preview.SavedMapping);
    }

    [Fact]
    public async Task A_file_in_numeras_format_needs_no_mapping()
    {
        await using var api = await StartAsync();

        var preview = await ReadAsync<Preview>(await PreviewAsync(api.Client, Sample("sample-orders.csv")), HttpStatusCode.OK);

        Assert.True(preview.MatchesNumeraFormat);
        Assert.Equal(("yyyy-MM-dd", "fraction", false), (preview.Suggestion.DateFormat, preview.Suggestion.Discount, preview.DateFormatAmbiguous));
    }

    [Fact]
    public async Task The_preview_returns_at_most_20_rows_and_cuts_long_cells()
    {
        await using var api = await StartAsync();
        var text = new StringBuilder("Id,Text\n");
        for (var i = 0; i < 50; i++)
        {
            text.Append($"{i},{new string('x', 300)}\n");
        }

        var preview = await ReadAsync<Preview>(await PreviewAsync(api.Client, Encoding.UTF8.GetBytes(text.ToString())), HttpStatusCode.OK);

        Assert.Equal(20, preview.Rows.Length);
        Assert.Equal(201, preview.Rows[0][1].Length); // 200 characters and an ellipsis
        Assert.Equal(11, preview.Unmapped.Length);
    }

    [Theory]
    [InlineData("", "orders.csv", "text/csv", HttpStatusCode.UnprocessableEntity)]
    [InlineData("a,b\n1,2\n", "orders.xlsx", "text/csv", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("a,b\n1,2\n", "orders.csv", "image/png", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("a,b\u0000\n", "orders.csv", "text/csv", HttpStatusCode.UnsupportedMediaType)]
    public async Task The_preview_checks_uploads_like_an_import(string content, string fileName, string contentType, HttpStatusCode expected)
    {
        await using var api = await StartAsync();

        var response = await PreviewAsync(api.Client, Encoding.UTF8.GetBytes(content), fileName, contentType);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_preview_refuses_files_with_more_than_100_columns_and_needs_a_sign_in()
    {
        await using var api = await StartAsync();
        var wide = string.Join(',', Enumerable.Range(1, 101).Select(i => $"c{i}")) + "\n";

        var response = await PreviewAsync(api.Client, Encoding.UTF8.GetBytes(wide));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("101 columns", await response.Content.ReadAsStringAsync());

        using var anonymous = api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await PreviewAsync(anonymous, Sample("sample-orders.csv"))).StatusCode);
    }

    // importing with a mapping ------------------------------------------------------------------------

    private sealed record Row(string OrderId, DateOnly OrderDate, string CustomerId, string Region, string Channel, string ProductSku,
        string ProductCategory, int Quantity, decimal UnitPrice, decimal Discount, OrderStatus Status);

    private static async Task<List<Row>> OrdersAsync(Api api)
    {
        await using var db = PostgresFixture.CreateContext(api.ConnectionString);
        return await db.Orders.AsNoTracking()
            .OrderBy(o => o.OrderId).ThenBy(o => o.ProductSku)
            .Select(o => new Row(o.OrderId, o.OrderDate, o.CustomerId, o.Region, o.Channel, o.ProductSku, o.ProductCategory,
                o.Quantity, o.UnitPrice, o.Discount, o.Status))
            .ToListAsync();
    }

    [Fact]
    public async Task The_mapped_sample_imports_the_same_rows_as_the_numera_format_sample()
    {
        await using var plain = await StartAsync();
        await using var mapped = await StartAsync();

        var plainJob = await ReadAsync<Summary>(await ImportAsync(plain.Client, Sample("sample-orders.csv")), HttpStatusCode.Created);
        var mappedJob = await ReadAsync<Summary>(await ImportAsync(mapped.Client, Sample("sample-orders-mapped.csv"), SampleMapping), HttpStatusCode.Created);

        Assert.Equal((5, 5, 0), (plainJob.TotalRows, plainJob.ImportedRows, plainJob.RejectedRows));
        Assert.Equal((5, 5, 0), (mappedJob.TotalRows, mappedJob.ImportedRows, mappedJob.RejectedRows));
        Assert.Equal(["Column 'Notes' was not mapped and was ignored."], mappedJob.Warnings);
        var expected = await OrdersAsync(plain);
        Assert.Equal(5, expected.Count);
        Assert.Equal(expected, await OrdersAsync(mapped));
    }

    [Fact]
    public async Task Errors_in_a_mapped_file_name_the_source_column_and_its_row()
    {
        await using var api = await StartAsync();
        await ImportAsync(api.Client, Sample("sample-orders-mapped.csv"), SampleMapping);
        var file = """
            Order Number,Date,Customer,Sales Region,Sales Channel,SKU,Category,Qty,Price,Discount %,Order Status
            NEW-1,05/03/2025,C-9,Europe,Online Store,S1,Blends,0,10.00,5,completed
            NEW-2,2025-03-05,C-9,Europe,Online Store,S1,Blends,1,10.00,150,completed
            NEW-3,05/03/2025,C-9,Europe,Online Store,S1,Blends,1,10.00,5,completed
            NEW-3,05/03/2025,C-9,Europe,Online Store,S1,Blends,1,10.00,5,completed
            SAMPLE-0001,01/03/2025,C-00001,Europe,Online Store,HP-SO-ETH,Single Origin,2,18.50,5,completed
            """;

        var job = await ReadAsync<Summary>(await ImportAsync(api.Client, Encoding.UTF8.GetBytes(file), SampleMapping), HttpStatusCode.Created);
        var report = await api.Client.GetStringAsync($"/api/imports/{job.Id}/errors.csv");

        Assert.Equal((5, 1, 4), (job.TotalRows, job.ImportedRows, job.RejectedRows));
        var lines = report.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => string.Join(',', l.Split(',').Take(3))).ToArray();
        Assert.Equal(
        [
            "2,Qty,quantity must be greater than 0.",
            "3,Date,order_date must be a valid date in dd/MM/yyyy format.",
            "3,Discount %,discount must be a percentage between 0 and 100.",
            "5,Order Number,Duplicate order_id and product_sku in this file; first seen on row 4.",
            "6,Order Number,This order_id and product_sku already exist in the data and were not imported again.",
        ], lines);
    }

    [Fact]
    public async Task A_mapping_naming_a_column_the_file_lacks_rejects_the_file()
    {
        await using var api = await StartAsync();
        var columns = new Dictionary<string, string?>(SampleColumns) { ["quantity"] = "Units" };
        var mapping = JsonSerializer.Serialize(new { columns, dateFormat = "dd/MM/yyyy", discount = "percent" });

        var response = await ImportAsync(api.Client, Sample("sample-orders-mapped.csv"), mapping);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("The file has no column named: Units (for quantity).", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("""{"columns":{"order_id":"Order Number"}}""")]
    [InlineData("""{"columns":{},"dateFormat":"yyyy"}""")]
    public async Task An_invalid_mapping_is_a_400_naming_mapping_and_nothing_is_imported(string mapping)
    {
        await using var api = await StartAsync();

        var response = await ImportAsync(api.Client, Sample("sample-orders-mapped.csv"), mapping);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("mapping", out _));
        Assert.Empty(await OrdersAsync(api));
    }

    [Fact]
    public async Task The_row_limit_still_applies_with_a_mapping()
    {
        await using var api = await StartAsync();
        var text = new StringBuilder("Order Number,Date,Customer,Sales Region,Sales Channel,SKU,Category,Qty,Price,Discount %,Order Status\n");
        for (var i = 0; i <= 100_000; i++)
        {
            text.Append("A,1/3/2025,C,E,O,S,B,1,1,0,completed\n");
        }

        var response = await ImportAsync(api.Client, Encoding.UTF8.GetBytes(text.ToString()), SampleMapping);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("more than 100,000 data rows", await response.Content.ReadAsStringAsync());
    }

    // saved mappings ----------------------------------------------------------------------------------

    private static object SaveBody(string name, string[]? headers = null) => new
    {
        name,
        mapping = new { columns = SampleColumns, dateFormat = "dd/MM/yyyy", discount = "percent" },
        sourceHeaders = headers ?? ["Order Number", "Date", "Customer", "Sales Region", "Sales Channel", "SKU", "Category", "Qty", "Price", "Discount %", "Order Status", "Notes"],
    };

    [Fact]
    public async Task A_saved_mapping_is_offered_for_files_with_the_same_headers_and_can_be_renamed_and_deleted()
    {
        await using var api = await StartAsync();

        var saved = await ReadAsync<Saved>(await api.Client.PostAsJsonAsync("/api/import-mappings", SaveBody("Shop export")), HttpStatusCode.Created);
        Assert.Equal(("Shop export", "dd/MM/yyyy", "percent"), (saved.Name, saved.Mapping.DateFormat, saved.Mapping.Discount));
        Assert.Equal(["category", "customer", "date", "discount %"], saved.SourceHeaders.Take(4)); // cleaned, lower-cased, sorted

        var preview = await ReadAsync<Preview>(await PreviewAsync(api.Client, Sample("sample-orders-mapped.csv")), HttpStatusCode.OK);
        Assert.Equal(saved.Id, preview.SavedMapping?.Id);
        Assert.Equal(("dd/MM/yyyy", false), (preview.Suggestion.DateFormat, preview.DateFormatAmbiguous)); // chosen before: not ambiguous

        var renamed = await ReadAsync<Saved>(await api.Client.PutAsJsonAsync($"/api/import-mappings/{saved.Id}", SaveBody("Webshop")), HttpStatusCode.OK);
        Assert.Equal("Webshop", renamed.Name);
        Assert.Equal(["Webshop"], (await api.Client.GetFromJsonAsync<Saved[]>("/api/import-mappings", Json))!.Select(m => m.Name));

        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/api/import-mappings/{saved.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.DeleteAsync($"/api/import-mappings/{saved.Id}")).StatusCode);
        Assert.Null((await ReadAsync<Preview>(await PreviewAsync(api.Client, Sample("sample-orders-mapped.csv")), HttpStatusCode.OK)).SavedMapping);
    }

    [Fact]
    public async Task Names_are_unique_per_user_whatever_the_letter_case_and_bad_requests_are_refused()
    {
        await using var api = await StartAsync();
        await api.Client.PostAsJsonAsync("/api/import-mappings", SaveBody("Shop export"));

        Assert.Equal(HttpStatusCode.Conflict, (await api.Client.PostAsJsonAsync("/api/import-mappings", SaveBody("SHOP EXPORT"))).StatusCode);

        var invalid = await api.Client.PostAsJsonAsync("/api/import-mappings", new { name = "", mapping = new { columns = new { } } });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _) && errors.TryGetProperty("mapping", out _));
    }

    [Fact]
    public async Task A_user_never_sees_or_changes_another_users_mappings()
    {
        await using var api = await StartAsync();
        var mine = await ReadAsync<Saved>(await api.Client.PostAsJsonAsync("/api/import-mappings", SaveBody("Shop export")), HttpStatusCode.Created);
        using var other = await SecondUserAsync(api);

        Assert.Empty((await other.GetFromJsonAsync<Saved[]>("/api/import-mappings", Json))!);
        Assert.Null((await ReadAsync<Preview>(await PreviewAsync(other, Sample("sample-orders-mapped.csv")), HttpStatusCode.OK)).SavedMapping);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/import-mappings/{mine.Id}", SaveBody("Taken over"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/import-mappings/{mine.Id}")).StatusCode);
        // The same name is free for the other user.
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync("/api/import-mappings", SaveBody("Shop export"))).StatusCode);

        Assert.Equal(["Shop export"], (await api.Client.GetFromJsonAsync<Saved[]>("/api/import-mappings", Json))!.Select(m => m.Name));
    }

    [Fact]
    public async Task A_user_can_keep_at_most_50_mappings()
    {
        await using var api = await StartAsync();
        for (var i = 1; i <= 50; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await api.Client.PostAsJsonAsync("/api/import-mappings", SaveBody($"Mapping {i}"))).StatusCode);
        }

        var response = await api.Client.PostAsJsonAsync("/api/import-mappings", SaveBody("Mapping 51"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Mapping_endpoints_need_a_sign_in_and_a_demo_reset_clears_saved_mappings()
    {
        await using var api = await StartAsync();
        using var anonymous = api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/import-mappings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/import-mappings", SaveBody("x"))).StatusCode);

        await api.Client.PostAsJsonAsync("/api/import-mappings", SaveBody("Shop export"));
        Assert.Equal(HttpStatusCode.OK, (await api.Client.PostAsync("/api/demo/reset", null)).StatusCode);

        Assert.Empty((await api.Client.GetFromJsonAsync<Saved[]>("/api/import-mappings", Json))!);
    }
}
