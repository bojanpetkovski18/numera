using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Numera.Application.Entities;
using Numera.Infrastructure.Persistence;

namespace Numera.Infrastructure.Auth;

/// <summary>Documented demo credentials for the fictional "Harbor &amp; Pine Coffee" dataset. Not real secrets.</summary>
public static class DemoUser
{
    public const string Email = "demo@numera.example";
    public const string Password = "Demo-Pass-2026!";
}

public sealed class DemoUserSeeder(NumeraDbContext db, IPasswordHasher<User> hasher, TimeProvider clock)
{
    /// <summary>Creates the demo user if missing. Idempotent; an existing user is left untouched.</summary>
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(u => u.Email == DemoUser.Email, ct))
        {
            return;
        }

        var hash = hasher.HashPassword(new User { Email = DemoUser.Email, PasswordHash = string.Empty }, DemoUser.Password);
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = DemoUser.Email,
            PasswordHash = hash,
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }
}

