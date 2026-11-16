using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Numera.Application.Auth;
using Numera.Application.Entities;
using Numera.Infrastructure.Auth;
using Numera.Infrastructure.Seeding;

namespace Numera.IntegrationTests;

/// <summary>Saved views (M16) and their public, read-only share links.</summary>
public class SharedViewsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Settings(string Preset, string? From, string? To, string[] Regions, string[] Channels, string[] Categories, string Granularity, string Compare);

    private sealed record View(Guid Id, string Name, Settings Settings);

    private sealed record Link(Guid Id, Guid ViewId, string ViewName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

    private sealed record Created(Link Link, string Token, string Url);

    private sealed record Api(TestApiFactory Factory, HttpClient Client, string ConnectionString) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            return Factory.DisposeAsync();
        }
    }

    /// <summary>Q3 2026 in Europe by week, compared with the previous period: the generated data covers both periods.</summary>
    private static object Q3Europe(string name = "Europe Q3") => new
    {
        name,
        settings = new { preset = "custom", from = "2026-07-01", to = "2026-09-30", regions = new[] { "Europe" }, granularity = "week", compare = "previous" },
    };

    private const string Q3EuropeQuery = "from=2026-07-01&to=2026-09-30&region=Europe";

    private async Task<Api> StartAsync(int sharedPermitLimit = 1000)
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using (var db = PostgresFixture.CreateContext(cs))
        {
            db.Orders.AddRange(DemoDataGenerator.Generate(new SeedOptions { OrderLineCount = 3_000, Seed = 5 }));
            await db.SaveChangesAsync();
        }

        var factory = new TestApiFactory(cs, settings: new Dictionary<string, string>
        {
            ["RateLimiting:SharedPermitLimit"] = sharedPermitLimit.ToString(),
            ["RateLimiting:ResetPermitLimit"] = "1000",
        });
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

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.True(expected == response.StatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<(View View, Created Share)> ShareAsync(Api api, object? view = null, int days = 7)
    {
        var saved = await ReadAsync<View>(await api.Client.PostAsJsonAsync("/api/views", view ?? Q3Europe()), HttpStatusCode.Created);
        var share = await ReadAsync<Created>(await api.Client.PostAsJsonAsync($"/api/views/{saved.Id}/shares", new { expiresInDays = days }), HttpStatusCode.Created);
        return (saved, share);
    }

    private static async Task<HttpClient> SecondUserAsync(Api api)
    {
        var user = new User { Email = "second@example.test", PasswordHash = "not-used", CreatedAt = DateTimeOffset.UtcNow };
        await using (var db = PostgresFixture.CreateContext(api.ConnectionString))
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        using var scope = api.Factory.Services.CreateScope();
        var client = api.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<ITokenService>().Create(user).Value);
        return client;
    }

    // saved views ------------------------------------------------------------------------------------

    [Fact]
    public async Task Views_are_saved_cleaned_listed_renamed_and_deleted()
    {
        await using var api = await StartAsync();

        var view = await ReadAsync<View>(await api.Client.PostAsJsonAsync("/api/views", new
        {
            name = "  Europe Q3 ",
            settings = new { preset = "CUSTOM", from = "2026-07-01", to = "2026-09-30", regions = new[] { "Europe", " Europe" }, compare = "Previous" },
        }), HttpStatusCode.Created);
        Assert.Equal("Europe Q3", view.Name);
        var s = view.Settings;
        Assert.Equal(("custom", "2026-07-01", "2026-09-30", "month", "previous"), (s.Preset, s.From, s.To, s.Granularity, s.Compare));
        Assert.Empty(s.Channels);
        Assert.Equal(["Europe"], view.Settings.Regions);

        var renamed = await ReadAsync<View>(await api.Client.PutAsJsonAsync($"/api/views/{view.Id}", Q3Europe("Europe, third quarter")), HttpStatusCode.OK);
        Assert.Equal(("Europe, third quarter", "week"), (renamed.Name, renamed.Settings.Granularity));
        Assert.Equal(["Europe, third quarter"], (await api.Client.GetFromJsonAsync<View[]>("/api/views", Json))!.Select(v => v.Name));

        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/api/views/{view.Id}")).StatusCode);
        Assert.Empty((await api.Client.GetFromJsonAsync<View[]>("/api/views", Json))!);
    }

    [Fact]
    public async Task Bad_views_are_refused_and_names_are_unique_per_user_ignoring_case()
    {
        await using var api = await StartAsync();
        await api.Client.PostAsJsonAsync("/api/views", Q3Europe());

        Assert.Equal(HttpStatusCode.Conflict, (await api.Client.PostAsJsonAsync("/api/views", Q3Europe("EUROPE q3"))).StatusCode);
        var invalid = await api.Client.PostAsJsonAsync("/api/views", new { name = "", settings = new { preset = "lastweek", granularity = "year" } });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _) && errors.TryGetProperty("settings.preset", out _) && errors.TryGetProperty("settings.granularity", out _));
    }

    [Fact]
    public async Task A_user_never_sees_shares_or_changes_another_users_views()
    {
        await using var api = await StartAsync();
        var (mine, share) = await ShareAsync(api);
        using var other = await SecondUserAsync(api);

        Assert.Empty((await other.GetFromJsonAsync<View[]>("/api/views", Json))!);
        Assert.Empty((await other.GetFromJsonAsync<Link[]>("/api/shares", Json))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/views/{mine.Id}", Q3Europe("Taken"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/views/{mine.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/views/{mine.Id}/shares", new { expiresInDays = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/shares/{share.Link.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await api.Factory.CreateClient().GetAsync($"/api/shared/{share.Token}")).StatusCode);
    }

    // share links -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_link_is_created_with_its_expiry_and_its_token_is_stored_only_as_a_hash()
    {
        await using var api = await StartAsync();
        var before = DateTimeOffset.UtcNow;

        var (view, share) = await ShareAsync(api, days: 30);

        Assert.Equal(43, share.Token.Length);
        Assert.Equal($"/shared/{share.Token}", share.Url);
        Assert.Equal(view.Name, share.Link.ViewName);
        Assert.InRange(share.Link.ExpiresAt, before.AddDays(30), DateTimeOffset.UtcNow.AddDays(30));
        Assert.Equal([share.Link.Id], (await api.Client.GetFromJsonAsync<Link[]>("/api/shares", Json))!.Select(l => l.Id));

        await using var db = PostgresFixture.CreateContext(api.ConnectionString);
        var stored = await db.ShareLinks.AsNoTracking().SingleAsync();
        Assert.NotEqual(share.Token, stored.TokenHash);
        Assert.Equal(Application.Views.ShareTokens.Hash(share.Token), stored.TokenHash);
        var anywhere = await db.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM share_links WHERE position({0} in token_hash) > 0", share.Token).SingleAsync();
        Assert.Equal(0, anywhere);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(365)]
    public async Task Only_1_7_or_30_days_are_allowed(int days)
    {
        await using var api = await StartAsync();
        var view = await ReadAsync<View>(await api.Client.PostAsJsonAsync("/api/views", Q3Europe()), HttpStatusCode.Created);

        var response = await api.Client.PostAsJsonAsync($"/api/views/{view.Id}/shares", new { expiresInDays = days });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_shared_link_shows_the_same_numbers_as_the_signed_in_dashboard_for_that_view()
    {
        await using var api = await StartAsync();
        var (_, share) = await ShareAsync(api);
        using var anonymous = api.Factory.CreateClient();
        var shared = $"/api/shared/{share.Token}";

        async Task Same(string signedIn, string open)
        {
            var expected = await api.Client.GetStringAsync(signedIn);
            var actual = await anonymous.GetStringAsync(open);
            Assert.Equal(expected, actual);
            Assert.Contains("revenue\"", actual, StringComparison.OrdinalIgnoreCase); // real figures, not an error
        }

        await Same($"/api/metrics/summary?{Q3EuropeQuery}&compare=previous", $"{shared}/summary");
        await Same($"/api/metrics/timeseries?{Q3EuropeQuery}&compare=previous&granularity=week", $"{shared}/timeseries");
        await Same($"/api/metrics/timeseries?{Q3EuropeQuery}&compare=previous&granularity=month", $"{shared}/timeseries?granularity=month");
        await Same($"/api/metrics/breakdown?{Q3EuropeQuery}&by=category", $"{shared}/breakdown?by=category");
        await Same($"/api/metrics/drivers?{Q3EuropeQuery}&compare=previous&by=channel", $"{shared}/drivers?by=channel");

        var info = await anonymous.GetFromJsonAsync<JsonElement>(shared, Json);
        Assert.Equal("Europe Q3", info.GetProperty("name").GetString());
        Assert.Equal("2026-07-01", info.GetProperty("from").GetString());
        Assert.Equal("previous", info.GetProperty("comparison").GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Query_filters_cannot_widen_a_shared_view()
    {
        await using var api = await StartAsync();
        var (_, share) = await ShareAsync(api);
        using var anonymous = api.Factory.CreateClient();
        var shared = $"/api/shared/{share.Token}";

        var plain = await anonymous.GetStringAsync($"{shared}/summary");
        var widened = await anonymous.GetStringAsync($"{shared}/summary?from=2020-01-01&to=2026-12-31&region=Asia-Pacific&compare=year");
        Assert.Equal(plain, widened);

        var regions = await anonymous.GetFromJsonAsync<JsonElement>($"{shared}/breakdown?by=region&region=Asia-Pacific", Json);
        Assert.Equal(["Europe"], regions.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("key").GetString()));
    }

    [Fact]
    public async Task A_link_opens_no_order_rows_and_no_other_endpoint()
    {
        await using var api = await StartAsync();
        var (_, share) = await ShareAsync(api);
        using var anonymous = api.Factory.CreateClient();

        foreach (var path in new[] { "orders", "orders/export", "imports", "../orders" })
        {
            var status = (await anonymous.GetAsync($"/api/shared/{share.Token}/{path}")).StatusCode;
            Assert.True(status is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized, $"{path}: {status}");
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/views")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/shares")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/metrics/summary?{Q3EuropeQuery}")).StatusCode);
    }

    [Fact]
    public async Task Unknown_malformed_revoked_and_expired_links_are_the_same_404()
    {
        await using var api = await StartAsync();
        var (_, revoked) = await ShareAsync(api);
        var (_, expired) = await ShareAsync(api, Q3Europe("Second"));
        using var anonymous = api.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/api/shares/{revoked.Link.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.DeleteAsync($"/api/shares/{revoked.Link.Id}")).StatusCode);
        await using (var db = PostgresFixture.CreateContext(api.ConnectionString))
        {
            await db.ShareLinks.Where(l => l.Id == expired.Link.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        var unknown = Application.Views.ShareTokens.Create().Token;
        var bodies = new List<string>();
        foreach (var token in new[] { revoked.Token, expired.Token, unknown, "not-a-token" })
        {
            foreach (var path in new[] { "", "/summary", "/timeseries" })
            {
                var response = await anonymous.GetAsync($"/api/shared/{token}{path}");
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
                bodies.Add(problem.GetProperty("title").GetString() + problem.GetProperty("detail").GetString());
            }
        }

        Assert.Single(bodies.Distinct());
        Assert.Empty((await api.Client.GetFromJsonAsync<Link[]>("/api/shares", Json))!); // neither is active any more
    }

    [Fact]
    public async Task Shared_responses_are_not_cached_or_indexed_and_carry_no_referrer()
    {
        await using var api = await StartAsync();
        var (_, share) = await ShareAsync(api);
        using var anonymous = api.Factory.CreateClient();

        foreach (var path in new[] { "", "/summary" })
        {
            var response = await anonymous.GetAsync($"/api/shared/{share.Token}{path}");
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
            Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
        }

        var created = await api.Client.PostAsJsonAsync($"/api/views/{share.Link.ViewId}/shares", new { expiresInDays = 1 });
        Assert.Equal("no-store", created.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Shared_endpoints_are_rate_limited_per_client()
    {
        await using var api = await StartAsync(sharedPermitLimit: 3);
        var (_, share) = await ShareAsync(api);
        using var anonymous = api.Factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await anonymous.GetAsync($"/api/shared/{share.Token}/summary")).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        // Signed-in endpoints are not affected by the shared limit.
        Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync("/api/views")).StatusCode);
    }

    [Fact]
    public async Task A_view_without_a_comparison_has_no_drivers_and_deleting_a_view_ends_its_links()
    {
        await using var api = await StartAsync();
        var (view, share) = await ShareAsync(api, new { name = "All time", settings = new { preset = "all" } });
        using var anonymous = api.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/shared/{share.Token}/drivers?by=region")).StatusCode);
        var info = await anonymous.GetFromJsonAsync<JsonElement>($"/api/shared/{share.Token}", Json);
        Assert.Equal(JsonValueKind.Null, info.GetProperty("comparison").ValueKind);

        await api.Client.DeleteAsync($"/api/views/{view.Id}");
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/shared/{share.Token}")).StatusCode);
    }

    [Fact]
    public async Task A_demo_reset_removes_views_and_their_links()
    {
        await using var api = await StartAsync();
        var (_, share) = await ShareAsync(api);

        Assert.Equal(HttpStatusCode.OK, (await api.Client.PostAsync("/api/demo/reset", null)).StatusCode);

        Assert.Empty((await api.Client.GetFromJsonAsync<View[]>("/api/views", Json))!);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Factory.CreateClient().GetAsync($"/api/shared/{share.Token}")).StatusCode);
    }
}
