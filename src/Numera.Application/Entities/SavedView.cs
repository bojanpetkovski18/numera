namespace Numera.Application.Entities;

/// <summary>A dashboard view a user saved under a name: the filter, the chart granularity and the comparison.</summary>
public class SavedView
{
    public Guid Id { get; set; }
    public Guid UserId { get; init; }
    public required string Name { get; set; }

    /// <summary>The settings as JSON (<c>ViewSettings</c>), validated before they are stored.</summary>
    public required string SettingsJson { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A read-only link to a saved view. Only the SHA-256 hash of its token is stored: the token itself is shown once,
/// when the link is created, and cannot be recovered from the database.
/// </summary>
public class ShareLink
{
    public Guid Id { get; set; }
    public Guid ViewId { get; init; }

    /// <summary>Lower-case hex SHA-256 of the token.</summary>
    public required string TokenHash { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
}
