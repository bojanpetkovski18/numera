using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Numera.Application.Ask;
using Numera.Infrastructure.Auth;
using Numera.Infrastructure.Seeding;

namespace Numera.IntegrationTests;

/// <summary>
/// "Ask in plain English" (M17) with a fake model: the API checks whatever the model returns like a UI request and
/// runs the dashboard's own queries, so the answer's figures equal the dashboard's. No real model is called.
/// </summary>
public class AskEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Returns the next prepared reply and records what it was asked.</summary>
    private sealed class FakeModel : IAskModel
    {
        public Queue<AskModelResult> Replies { get; } = new();
        public List<(string Question, AskContext Context)> Calls { get; } = [];

        public Task<AskModelResult> InterpretAsync(string question, AskContext context, CancellationToken ct)
        {
            Calls.Add((question, context));
            return Task.FromResult(Replies.Count > 0 ? Replies.Dequeue() : new AskModelResult(null, AskModelFailure.Invalid));
        }
    }

    private sealed record Api(TestApiFactory Factory, HttpClient Client, FakeModel Model) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            return Factory.DisposeAsync();
        }
    }

    private static AskModelResult Reply(string metric = "revenue", string view = "total", string from = "2026-07-01", string to = "2026-09-30",
        string[]? regions = null, string compare = "none", string granularity = "month", string dimension = "region") =>
        new(new AskInterpretation(true, "", metric, view, granularity, dimension, from, to, regions ?? [], [], [], compare), null);

    private async Task<Api> StartAsync(bool enabled = true, int perMinute = 100, int daily = 100)
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using (var db = PostgresFixture.CreateContext(cs))
        {
            db.Orders.AddRange(DemoDataGenerator.Generate(new SeedOptions { OrderLineCount = 3_000, Seed = 9 }));
            await db.SaveChangesAsync();
        }

        var settings = new Dictionary<string, string> { ["Ai:PerUserPerMinute"] = perMinute.ToString(), ["Ai:DailyLimit"] = daily.ToString() };
        if (enabled)
        {
            settings["Ai:ApiKey"] = "test-key-never-sent";
        }

        var model = new FakeModel();
        var factory = new TestApiFactory(cs, settings: settings);
        var configured = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IAskModel>(_ => model)));
        await using (var scope = configured.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DemoUserSeeder>().EnsureAsync();
        }

        var client = configured.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = DemoUser.Email, password = DemoUser.Password });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        return new Api(factory, client, model);
    }

    private static Task<HttpResponseMessage> AskAsync(HttpClient client, string question = "What was revenue in Europe in Q3?") =>
        client.PostAsJsonAsync("/api/ask", new { question });

    private static async Task<JsonElement> AnswerAsync(Api api, string question = "What was revenue in Europe in Q3?")
    {
        var response = await AskAsync(api.Client, question);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    [Fact]
    public async Task Without_a_key_the_feature_is_off_and_the_model_is_never_called()
    {
        await using var api = await StartAsync(enabled: false);

        var status = await api.Client.GetFromJsonAsync<JsonElement>("/api/ask/status", Json);
        Assert.False(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await AskAsync(api.Client)).StatusCode);
        Assert.Empty(api.Model.Calls);
        Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync("/api/metrics/summary")).StatusCode); // the app still works
    }

    [Fact]
    public async Task An_answer_has_the_same_figures_as_the_dashboard_for_the_same_request()
    {
        await using var api = await StartAsync();
        var status = await api.Client.GetFromJsonAsync<JsonElement>("/api/ask/status", Json);
        Assert.True(status.GetProperty("enabled").GetBoolean());

        async Task Same(AskModelResult reply, string dashboardUrl)
        {
            api.Model.Replies.Enqueue(reply);
            var answer = await AnswerAsync(api);
            Assert.Equal("answered", answer.GetProperty("status").GetString());
            var expected = await api.Client.GetStringAsync(dashboardUrl);
            Assert.Equal(expected, answer.GetProperty("result").GetRawText());
        }

        await Same(Reply(regions: ["europe"], compare: "previous"), "/api/metrics/summary?from=2026-07-01&to=2026-09-30&region=Europe&compare=previous");
        await Same(Reply(metric: "orders", view: "over_time", granularity: "week", compare: "year"),
            "/api/metrics/timeseries?from=2026-07-01&to=2026-09-30&granularity=week&compare=year");
        await Same(Reply(view: "by_dimension", dimension: "channel", from: "", to: ""), "/api/metrics/breakdown?by=channel");
    }

    [Fact]
    public async Task The_answer_shows_the_request_it_ran_and_the_model_gets_the_datas_values_and_dates()
    {
        await using var api = await StartAsync();
        api.Model.Replies.Enqueue(Reply(regions: ["EUROPE"], compare: "previous"));

        var answer = await AnswerAsync(api, "  Revenue in Europe last quarter vs the quarter before?  ");

        var request = answer.GetProperty("request");
        Assert.Equal(("revenue", "total", "2026-07-01", "previous"),
            (request.GetProperty("metric").GetString(), request.GetProperty("view").GetString(), request.GetProperty("from").GetString(), request.GetProperty("compare").GetString()));
        Assert.Equal(["Europe"], request.GetProperty("regions").EnumerateArray().Select(e => e.GetString()));

        var (question, context) = Assert.Single(api.Model.Calls);
        Assert.Equal("Revenue in Europe last quarter vs the quarter before?", question);
        Assert.Contains("Europe", context.Options.Regions);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), context.Today);
    }

    [Fact]
    public async Task Output_that_fails_validation_is_never_run_and_says_why()
    {
        await using var api = await StartAsync();
        api.Model.Replies.Enqueue(Reply(regions: ["Europe'; DROP TABLE orders; --"]));
        api.Model.Replies.Enqueue(Reply(metric: "profit"));
        api.Model.Replies.Enqueue(Reply(from: "2026-09-30", to: "2026-07-01"));
        api.Model.Replies.Enqueue(new AskModelResult(new AskInterpretation(false, "Numera has no profit figures.", "revenue", "total", "month", "region", "", "", [], [], [], "none"), null));
        api.Model.Replies.Enqueue(new AskModelResult(null, AskModelFailure.Invalid));
        api.Model.Replies.Enqueue(new AskModelResult(null, AskModelFailure.Refused));

        var reasons = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var answer = await AnswerAsync(api);
            Assert.Equal("cannot_answer", answer.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, answer.GetProperty("result").ValueKind);
            reasons.Add(answer.GetProperty("reason").GetString()!);
        }

        Assert.StartsWith("\"Europe'; DROP TABLE orders; --\" is not a region", reasons[0]);
        Assert.Equal("The question could not be turned into a request Numera understands.", reasons[1]);
        Assert.Equal("The dates in the question could not be read as a valid range.", reasons[2]);
        Assert.Equal("Numera has no profit figures.", reasons[3]);
        Assert.StartsWith("That question could not be turned into a request Numera can run.", reasons[4]);
        Assert.Equal(reasons[4], reasons[5]);
        Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync("/api/metrics/summary")).StatusCode); // the orders table is still there
    }

    [Fact]
    public async Task A_model_that_is_down_is_a_503_and_questions_are_checked_for_length()
    {
        await using var api = await StartAsync();
        api.Model.Replies.Enqueue(new AskModelResult(null, AskModelFailure.Unavailable));

        var down = await AskAsync(api.Client);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
        Assert.Equal("application/problem+json", down.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.BadRequest, (await AskAsync(api.Client, "   ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AskAsync(api.Client, new string('x', 301))).StatusCode);
        Assert.Single(api.Model.Calls); // neither of those reached the model
    }

    [Fact]
    public async Task Questions_are_limited_per_user_per_minute_and_per_day()
    {
        await using (var api = await StartAsync(perMinute: 2))
        {
            var statuses = new List<HttpStatusCode>();
            for (var i = 0; i < 3; i++)
            {
                api.Model.Replies.Enqueue(Reply());
                statuses.Add((await AskAsync(api.Client)).StatusCode);
            }

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        }

        await using (var api = await StartAsync(daily: 1))
        {
            api.Model.Replies.Enqueue(Reply());
            Assert.Equal(HttpStatusCode.OK, (await AskAsync(api.Client)).StatusCode);

            var limited = await AskAsync(api.Client);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal("Daily question limit reached", (await limited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
            Assert.Single(api.Model.Calls);
        }
    }

    [Fact]
    public async Task Asking_needs_a_sign_in()
    {
        await using var api = await StartAsync();
        using var anonymous = api.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await AskAsync(anonymous)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/ask/status")).StatusCode);
        Assert.Empty(api.Model.Calls);
    }
}
