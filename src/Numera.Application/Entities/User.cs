namespace Numera.Application.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string Email { get; init; }
    public required string PasswordHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
