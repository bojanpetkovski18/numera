using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Numera.Application.Auth;

public sealed record LoginCommand(string Email, string Password);

public sealed class LoginHandler(
    INumeraDbContext db,
    CredentialVerifier verifier,
    ITokenService tokens,
    ILogger<LoginHandler> logger)
{
    /// <summary>Returns a token, or null when the email or password is wrong (callers must not say which).</summary>
    public async Task<AccessToken?> HandleAsync(LoginCommand command, CancellationToken ct = default)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (!verifier.Verify(user, command.Password) || user is null)
        {
            logger.LogWarning("Failed login attempt for {Email}", email);
            return null;
        }

        logger.LogInformation("User {UserId} signed in", user.Id);
        return tokens.Create(user);
    }
}
