using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Numera.Application.Entities;
using Numera.Infrastructure.Auth;

namespace Numera.IntegrationTests;

/// <summary>The demo reset runs the real 200,000-row seed, so these tests take a few seconds each.</summary>
public class DemoResetTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const long OrderWritesLock = 0x4E554D455241; // DataLocks.OrderWrites (internal to Infrastructure)

    private sealed record Api(TestApiFactory Factory, HttpClient Client, string ConnectionString) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            return Factory.DisposeAsync();
        }
    }

    /// <summary>Everything a reset must restore: what the dashboard shows plus a fingerprint of every row.</summary>
    private sealed record Snapshot(string Summary, long Orders, long Jobs, long Errors, long MinId, long MaxId, string Fingerprint);

    private async Task<Api> StartAsync(bool signedIn = true, int resetPermitLimit = 1000)
    {
        var cs = await postgres.CreateDatabaseAsync();
        var factory = new TestApiFactory(cs, settings: new Dictionary<string, string>
        {
            ["RateLimiting:ResetPermitLimit"] = resetPermitLimit.ToString(),
        });
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

        return new Api(factory, client, cs);
    }

    private static Task<HttpResponseMessage> ResetAsync(Api api) => api.Client.PostAsync("/api/demo/reset", null);

    private static async Task<Snapshot> SnapshotAsync(Api api)
    {
        var summary = await api.Client.GetStringAsync("/api/metrics/summary");
        await using var connection = new NpgsqlConnection(api.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT (SELECT COUNT(*) FROM orders),
                   (SELECT COUNT(*) FROM import_jobs),
                   (SELECT COUNT(*) FROM import_errors),
                   (SELECT COALESCE(MIN(id), 0) FROM orders),
                   (SELECT COALESCE(MAX(id), 0) FROM orders),
                   (SELECT md5(string_agg(concat_ws('|', id, order_id, order_date, customer_id, region, channel, product_sku,
                                                    product_category, quantity, unit_price, discount, status), ',' ORDER BY id))
                    FROM orders)
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new Snapshot(summary, reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4),
            reader.IsDBNull(5) ? "" : reader.GetString(5));
    }

    private static async Task ImportSomethingAsync(Api api)
    {
        const string csv = """
            order_id,order_date,customer_id,region,channel,product_sku,product_category,quantity,unit_price,discount,status
            ZZ-0001,2026-09-01,C-99999,Europe,Online Store,SKU-ZZ,Blends,3,12.50,0,completed
            ZZ-0002,2026-09-02,C-99999,Europe,Online Store,SKU-ZZ,Blends,0,12.50,0,completed
            """;
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var response = await api.Client.PostAsync("/api/imports", new MultipartFormDataContent { { file, "file", "extra.csv" } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Reset_restores_the_exact_seeded_state_after_imports()
    {
        await using var api = await StartAsync();

        var first = await ResetAsync(api);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var body = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(200_000, body.GetProperty("orderLines").GetInt32());
        var seeded = await SnapshotAsync(api);
        Assert.Equal((200_000L, 0L, 0L, 1L, 200_000L), (seeded.Orders, seeded.Jobs, seeded.Errors, seeded.MinId, seeded.MaxId));

        await ImportSomethingAsync(api);
        var changed = await SnapshotAsync(api);
        Assert.Equal((200_001L, 1L, 1L), (changed.Orders, changed.Jobs, changed.Errors));
        Assert.NotEqual(seeded.Summary, changed.Summary);

        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(api)).StatusCode);

        Assert.Equal(seeded, await SnapshotAsync(api)); // same rows, same ids, same dashboard numbers, no import history
    }

    [Fact]
    public async Task Reset_requires_authentication()
    {
        await using var api = await StartAsync(signedIn: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ResetAsync(api)).StatusCode);
    }

    [Fact]
    public async Task Reset_is_refused_with_409_while_an_import_or_reset_holds_the_lock_and_works_once_it_is_released()
    {
        await using var api = await StartAsync();

        await using (var holder = new NpgsqlConnection(api.ConnectionString))
        {
            await holder.OpenAsync();
            await using var transaction = await holder.BeginTransactionAsync();
            await using (var take = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", holder, transaction))
            {
                take.Parameters.AddWithValue("key", OrderWritesLock);
                await take.ExecuteNonQueryAsync();
            }

            var busy = await ResetAsync(api);
            Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
            var problem = await busy.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Demo data is busy", problem.GetProperty("title").GetString());
            Assert.Equal(0L, (await SnapshotAsync(api)).Orders); // nothing was touched

            await transaction.RollbackAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(api)).StatusCode);
    }

    [Fact]
    public async Task Simultaneous_resets_never_overlap_and_leave_exactly_the_seeded_data()
    {
        await using var api = await StartAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => ResetAsync(api)));

        var codes = responses.Select(r => r.StatusCode).ToList();
        Assert.Contains(HttpStatusCode.OK, codes);
        Assert.All(codes, c => Assert.True(c is HttpStatusCode.OK or HttpStatusCode.Conflict, $"unexpected {c}"));
        var state = await SnapshotAsync(api);
        Assert.Equal((200_000L, 1L, 200_000L), (state.Orders, state.MinId, state.MaxId));
    }

    [Fact]
    public async Task Reset_keeps_the_history_row_of_an_import_that_is_still_starting()
    {
        await using var api = await StartAsync();
        Guid running;
        await using (var db = PostgresFixture.CreateContext(api.ConnectionString))
        {
            var job = new ImportJob { FileName = "big.csv", Status = ImportJobStatus.Processing, CreatedAt = DateTimeOffset.UtcNow };
            db.ImportJobs.AddRange(job, new ImportJob { FileName = "old.csv", Status = ImportJobStatus.Completed, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            running = job.Id;
        }

        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(api)).StatusCode);

        await using (var db = PostgresFixture.CreateContext(api.ConnectionString))
        {
            var left = await db.ImportJobs.AsNoTracking().Select(j => j.Id).ToListAsync();
            Assert.Equal([running], left);
        }
    }

    [Fact]
    public async Task Reset_is_rate_limited_per_client()
    {
        await using var api = await StartAsync(resetPermitLimit: 1);

        // Hold the lock so the permitted call returns quickly (409) without running a full reset.
        await using var holder = new NpgsqlConnection(api.ConnectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using (var take = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", holder, transaction))
        {
            take.Parameters.AddWithValue("key", OrderWritesLock);
            await take.ExecuteNonQueryAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await ResetAsync(api)).StatusCode);

        var limited = await ResetAsync(api);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        var problem = await limited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Too many demo resets", problem.GetProperty("title").GetString());
        Assert.StartsWith("Try again in about", problem.GetProperty("detail").GetString());
    }
}
