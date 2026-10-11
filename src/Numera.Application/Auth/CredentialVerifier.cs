using Microsoft.AspNetCore.Identity;
using Numera.Application.Entities;

namespace Numera.Application.Auth;

/// <summary>Checks a password against a stored hash, spending the same effort when the user does not exist.</summary>
public sealed class CredentialVerifier(IPasswordHasher<User> hasher)
{
    private static readonly User Placeholder = new() { Email = "placeholder", PasswordHash = string.Empty };

    // A real hash of a throwaway value, so unknown-email logins cost the same as wrong-password logins.
    private readonly string _placeholderHash = hasher.HashPassword(Placeholder, Guid.NewGuid().ToString("N"));

    public bool Verify(User? user, string password)
    {
        if (user is null)
        {
            hasher.VerifyHashedPassword(Placeholder, _placeholderHash, password);
            return false;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
