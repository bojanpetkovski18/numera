using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Numera.Application.Entities;
using Numera.Infrastructure.Auth;

namespace Numera.UnitTests;

public class JwtTokenServiceTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly JwtOptions Options = new()
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        SigningKey = "unit-test-signing-key-0123456789-abcdef",
        LifetimeMinutes = 30,
    };

    private static readonly User User = new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Email = "demo@numera.example",
        PasswordHash = "hash",
    };

    private static AccessToken_ Create() => new(new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options), new FixedClock(Now)).Create(User));

    // Thin wrapper so the test reads clearly.
    private readonly record struct AccessToken_(Numera.Application.Auth.AccessToken Token);

    [Fact]
    public void Token_contains_subject_email_issuer_audience_and_jti()
    {
        var jwt = new JsonWebToken(Create().Token.Value);

        Assert.Equal(User.Id.ToString(), jwt.Subject);
        Assert.Equal(User.Email, jwt.GetClaim("email").Value);
        Assert.Equal("test-issuer", jwt.Issuer);
        Assert.Contains("test-audience", jwt.Audiences);
        Assert.False(string.IsNullOrEmpty(jwt.Id));
    }

    [Fact]
    public void Token_expires_after_the_configured_lifetime()
    {
        var created = Create().Token;
        var jwt = new JsonWebToken(created.Value);

        Assert.Equal(Now.AddMinutes(30), created.ExpiresAt);
        Assert.Equal(Now.AddMinutes(30).UtcDateTime, jwt.ValidTo);
        Assert.Equal(Now.UtcDateTime, jwt.ValidFrom);
    }

    [Fact]
    public async Task Token_validates_with_the_signing_key_and_fails_with_another()
    {
        var token = Create().Token.Value;
        var handler = new JsonWebTokenHandler();

        TokenValidationParameters Parameters(string key) => new()
        {
            ValidIssuer = "test-issuer",
            ValidAudience = "test-audience",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateLifetime = false,
        };

        Assert.True((await handler.ValidateTokenAsync(token, Parameters(Options.SigningKey))).IsValid);
        Assert.False((await handler.ValidateTokenAsync(token, Parameters("a-different-signing-key-0123456789-xyz"))).IsValid);
    }
}
