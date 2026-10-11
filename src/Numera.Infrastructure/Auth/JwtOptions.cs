using System.ComponentModel.DataAnnotations;

namespace Numera.Infrastructure.Auth;

/// <summary>Bound from the <c>Jwt</c> configuration section. The signing key is never in source control.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>HMAC-SHA256 key, at least 32 characters. Supply via the <c>Jwt__SigningKey</c> environment variable.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 1440)]
    public int LifetimeMinutes { get; init; } = 60;
}
