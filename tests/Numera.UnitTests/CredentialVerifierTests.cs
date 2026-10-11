using Microsoft.AspNetCore.Identity;
using Numera.Application.Auth;
using Numera.Application.Entities;

namespace Numera.UnitTests;

public class CredentialVerifierTests
{
    private readonly PasswordHasher<User> _hasher = new();
    private readonly CredentialVerifier _verifier;
    private readonly User _user;

    public CredentialVerifierTests()
    {
        _verifier = new CredentialVerifier(_hasher);
        _user = new User { Email = "a@numera.example", PasswordHash = string.Empty };
        _user = new User { Email = _user.Email, PasswordHash = _hasher.HashPassword(_user, "correct horse") };
    }

    [Fact]
    public void Correct_password_is_accepted() => Assert.True(_verifier.Verify(_user, "correct horse"));

    [Fact]
    public void Wrong_password_is_rejected() => Assert.False(_verifier.Verify(_user, "wrong"));

    [Fact]
    public void Unknown_user_is_rejected() => Assert.False(_verifier.Verify(null, "correct horse"));

    [Fact]
    public void Stored_value_is_a_hash_not_the_password() => Assert.DoesNotContain("correct horse", _user.PasswordHash);
}
