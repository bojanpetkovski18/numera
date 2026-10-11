using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Numera.IntegrationTests;

/// <summary>Hosts the API in-process with fake, test-only settings (no real secrets).</summary>
public sealed class TestApiFactory(
    string? connectionString = null,
    int loginPermitLimit = 1000,
    IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    public const string SigningKey = "integration-test-signing-key-0123456789";
    public const string Issuer = "numera-test-issuer";
    public const string Audience = "numera-test-audience";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("RateLimiting:LoginPermitLimit", loginPermitLimit.ToString());
        if (connectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:Numera", connectionString);
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }
    }
}