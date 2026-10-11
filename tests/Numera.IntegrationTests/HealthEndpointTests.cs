using System.Net;

namespace Numera.IntegrationTests;

public class HealthEndpointTests
{
    [Fact]
    public async Task Health_returns_200_without_authentication()
    {
        await using var factory = new TestApiFactory();

        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Responses_tell_browsers_not_to_sniff_the_content_type()
    {
        await using var factory = new TestApiFactory();

        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Unknown_route_returns_401_for_anonymous_callers()
    {
        await using var factory = new TestApiFactory();

        var response = await factory.CreateClient().GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
