using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Numera.IntegrationTests;

/// <summary>
/// The production image serves the Angular build from the API's web root. These tests use a temporary web root with
/// a stand-in index.html and a hashed script. No database is needed for these requests.
/// </summary>
public sealed class WebAppHostingTests : IDisposable
{
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "numera-webroot-" + Guid.NewGuid().ToString("N"));
    private readonly TestApiFactory _factory;

    public WebAppHostingTests()
    {
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><title>Numera</title><app-root></app-root>");
        File.WriteAllText(Path.Combine(_webRoot, "main-ABCD1234.js"), "console.log('app');");
        File.WriteAllText(Path.Combine(_webRoot, "sample-orders.csv"), "order_id\n");
        _factory = new TestApiFactory(webRoot: _webRoot);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_webRoot, recursive: true);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/dashboard")]
    [InlineData("/orders?region=Europe&sort=lineTotal&dir=desc")]
    [InlineData("/no/such/page")]
    public async Task App_pages_load_index_html_without_signing_in(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<app-root>", await response.Content.ReadAsStringAsync());
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Hashed_build_files_are_cached_for_a_year_and_others_briefly()
    {
        var client = _factory.CreateClient();

        var script = await client.GetAsync("/main-ABCD1234.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", script.Headers.CacheControl?.ToString());

        var sample = await client.GetAsync("/sample-orders.csv");
        Assert.Equal(HttpStatusCode.OK, sample.StatusCode);
        Assert.Equal("public, max-age=3600", sample.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Every_response_carries_the_security_headers()
    {
        var response = await _factory.CreateClient().GetAsync("/dashboard");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
    }

    [Fact]
    public async Task Unknown_api_paths_stay_json_404_and_never_return_the_app()
    {
        var response = await _factory.CreateClient().GetAsync("/api/no-such-endpoint");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Not found", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Api_endpoints_still_require_authentication_and_health_stays_json()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/metrics/summary")).StatusCode);
        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("healthy", (await health.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }
}
