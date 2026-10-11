using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Numera.Infrastructure.Auth;

namespace Numera.IntegrationTests;

public class AuthEndpointsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

    private async Task<TestApiFactory> CreateFactoryAsync(int loginPermitLimit = 1000)
    {
        var cs = await postgres.CreateDatabaseAsync();
        var factory = new TestApiFactory(cs, loginPermitLimit);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DemoUserSeeder>().EnsureAsync();
        return factory;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    [Fact]
    public async Task Login_with_demo_credentials_returns_a_token()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();

        var response = await LoginAsync(client, DemoUser.Email, DemoUser.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.Equal("Bearer", body.TokenType);
        Assert.True(body.ExpiresAt > DateTimeOffset.UtcNow);
        var jwt = new JsonWebToken(body.AccessToken);
        Assert.Equal(TestApiFactory.Issuer, jwt.Issuer);
    }

    [Fact]
    public async Task Login_is_case_insensitive_on_email()
    {
        await using var factory = await CreateFactoryAsync();

        var response = await LoginAsync(factory.CreateClient(), "  DEMO@Numera.Example ", DemoUser.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401_problem_details()
    {
        await using var factory = await CreateFactoryAsync();

        var response = await LoginAsync(factory.CreateClient(), DemoUser.Email, "not-the-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Login_with_unknown_email_returns_401_like_a_wrong_password()
    {
        await using var factory = await CreateFactoryAsync();

        var response = await LoginAsync(factory.CreateClient(), "nobody@numera.example", DemoUser.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_missing_fields_returns_400_problem_details()
    {
        await using var factory = await CreateFactoryAsync();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "", password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Protected_endpoint_returns_401_problem_details_without_a_token()
    {
        await using var factory = await CreateFactoryAsync();

        var response = await factory.CreateClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Protected_endpoint_returns_401_for_a_garbage_token()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_returns_401_for_a_token_signed_with_another_key()
    {
        await using var factory = await CreateFactoryAsync();
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = TestApiFactory.Issuer,
            Audience = TestApiFactory.Audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("some-other-signing-key-0123456789-abc")),
                SecurityAlgorithms.HmacSha256),
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_returns_200_with_a_valid_token()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();
        var login = await (await LoginAsync(client, DemoUser.Email, DemoUser.Password)).Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(DemoUser.Email, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_route_returns_404_problem_details_for_a_signed_in_user()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();
        var login = await (await LoginAsync(client, DemoUser.Email, DemoUser.Password)).Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);

        var response = await client.GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Health_stays_anonymous()
    {
        await using var factory = new TestApiFactory();

        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Behind_a_proxy_the_login_rate_limit_is_per_forwarded_client_address()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var factory = new TestApiFactory(cs, loginPermitLimit: 2, new Dictionary<string, string> { ["ForwardedHeaders:Enabled"] = "true" });
        var client = factory.CreateClient();

        async Task<HttpStatusCode> AttemptFrom(string ip)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { email = DemoUser.Email, password = "wrong" }),
            };
            request.Headers.Add("X-Forwarded-For", ip);
            return (await client.SendAsync(request)).StatusCode;
        }

        var first = new[] { await AttemptFrom("203.0.113.1"), await AttemptFrom("203.0.113.1"), await AttemptFrom("203.0.113.1") };
        var other = await AttemptFrom("203.0.113.2");

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], first);
        Assert.Equal(HttpStatusCode.Unauthorized, other);
    }

    [Fact]
    public async Task Login_is_rate_limited_after_too_many_attempts()
    {
        await using var factory = await CreateFactoryAsync(loginPermitLimit: 3);
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await LoginAsync(client, DemoUser.Email, "wrong")).StatusCode);
        }

        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
             HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests],
            statuses);
    }
}

