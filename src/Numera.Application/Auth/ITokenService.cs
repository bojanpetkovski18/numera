using Numera.Application.Entities;

namespace Numera.Application.Auth;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken Create(User user);
}
