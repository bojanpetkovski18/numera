using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Numera.Application.Entities;

namespace Numera.Application.Views;

public sealed record SavedViewDto(Guid Id, string Name, ViewSettings Settings, DateTimeOffset UpdatedAt);

public sealed record SaveViewRequest(string? Name, ViewSettings? Settings);

public sealed record ShareLinkDto(Guid Id, Guid ViewId, string ViewName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

/// <summary>A new link: its token is returned this once and never stored.</summary>
public sealed record CreatedShareLink(ShareLinkDto Link, string Token);

public sealed record CreateShareRequest(int? ExpiresInDays);

public enum ViewOutcome
{
    Done,
    Invalid,
    NameTaken,
    LimitReached,
    NotFound,
}

public sealed record ViewResult<T>(ViewOutcome Outcome, T? Value = default, Dictionary<string, string[]>? Errors = null);

/// <summary>A user's saved views. Another user's view is always "not found".</summary>
public sealed class SavedViewsHandler(INumeraDbContext db, TimeProvider clock)
{
    public const int MaxPerUser = 50;
    public const int MaxNameLength = 80;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SavedViewDto>> ListAsync(Guid userId, CancellationToken ct = default) =>
        (await db.SavedViews.AsNoTracking().Where(v => v.UserId == userId).OrderBy(v => v.Name).ToListAsync(ct))
        .Select(ToDto)
        .ToList();

    public async Task<ViewResult<SavedViewDto>> CreateAsync(Guid userId, SaveViewRequest request, CancellationToken ct = default)
    {
        var (name, settings, errors) = Validate(request);
        if (errors.Count > 0)
        {
            return new(ViewOutcome.Invalid, Errors: errors);
        }

        if (await db.SavedViews.CountAsync(v => v.UserId == userId, ct) >= MaxPerUser)
        {
            return new(ViewOutcome.LimitReached);
        }

        if (await NameTakenAsync(userId, name!, null, ct))
        {
            return new(ViewOutcome.NameTaken);
        }

        var now = clock.GetUtcNow();
        var view = new SavedView { UserId = userId, Name = name!, SettingsJson = Serialize(settings!), CreatedAt = now, UpdatedAt = now };
        db.SavedViews.Add(view);
        await db.SaveChangesAsync(ct);
        return new(ViewOutcome.Done, ToDto(view));
    }

    public async Task<ViewResult<SavedViewDto>> UpdateAsync(Guid userId, Guid id, SaveViewRequest request, CancellationToken ct = default)
    {
        var view = await db.SavedViews.FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId, ct);
        if (view is null)
        {
            return new(ViewOutcome.NotFound);
        }

        var (name, settings, errors) = Validate(request);
        if (errors.Count > 0)
        {
            return new(ViewOutcome.Invalid, Errors: errors);
        }

        if (await NameTakenAsync(userId, name!, id, ct))
        {
            return new(ViewOutcome.NameTaken);
        }

        view.Name = name!;
        view.SettingsJson = Serialize(settings!);
        view.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return new(ViewOutcome.Done, ToDto(view));
    }

    /// <summary>Deletes the view and, with it, every link to it.</summary>
    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        await db.SavedViews.Where(v => v.Id == id && v.UserId == userId).ExecuteDeleteAsync(ct) > 0;

    internal static ViewSettings Settings(SavedView view) => JsonSerializer.Deserialize<ViewSettings>(view.SettingsJson, Json)!;

    private static string Serialize(ViewSettings settings) => JsonSerializer.Serialize(settings, Json);

    private static SavedViewDto ToDto(SavedView v) => new(v.Id, v.Name, Settings(v), v.UpdatedAt);

    private static (string? Name, ViewSettings? Settings, Dictionary<string, string[]> Errors) Validate(SaveViewRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            errors["name"] = [$"The name must be 1 to {MaxNameLength} characters."];
        }

        var settings = ViewSettingsRules.Validate(request.Settings, errors);
        return (name, settings, errors);
    }

    private Task<bool> NameTakenAsync(Guid userId, string name, Guid? except, CancellationToken ct)
    {
        var lower = name.ToLower();
        return db.SavedViews.AnyAsync(v => v.UserId == userId && v.Name.ToLower() == lower && v.Id != except, ct);
    }
}

/// <summary>Read-only links to saved views: create, list the active ones, revoke, and resolve a token.</summary>
public sealed class ShareLinksHandler(INumeraDbContext db, TimeProvider clock)
{
    public static readonly IReadOnlyList<int> AllowedDays = [1, 7, 30];
    public const int MaxActivePerView = 10;

    public async Task<ViewResult<CreatedShareLink>> CreateAsync(Guid userId, Guid viewId, CreateShareRequest request, CancellationToken ct = default)
    {
        var view = await db.SavedViews.AsNoTracking().FirstOrDefaultAsync(v => v.Id == viewId && v.UserId == userId, ct);
        if (view is null)
        {
            return new(ViewOutcome.NotFound);
        }

        if (request.ExpiresInDays is not { } days || !AllowedDays.Contains(days))
        {
            return new(ViewOutcome.Invalid, Errors: new() { ["expiresInDays"] = ["expiresInDays must be 1, 7 or 30."] });
        }

        var now = clock.GetUtcNow();
        if (await db.ShareLinks.CountAsync(l => l.ViewId == viewId && l.RevokedAt == null && l.ExpiresAt > now, ct) >= MaxActivePerView)
        {
            return new(ViewOutcome.LimitReached);
        }

        var (token, hash) = ShareTokens.Create();
        var link = new ShareLink { ViewId = viewId, TokenHash = hash, CreatedAt = now, ExpiresAt = now.AddDays(days) };
        db.ShareLinks.Add(link);
        await db.SaveChangesAsync(ct);
        return new(ViewOutcome.Done, new CreatedShareLink(new ShareLinkDto(link.Id, viewId, view.Name, link.CreatedAt, link.ExpiresAt), token));
    }

    /// <summary>The user's links that still work (not revoked, not expired), soonest to expire first.</summary>
    public async Task<IReadOnlyList<ShareLinkDto>> ListActiveAsync(Guid userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return await (
                from l in db.ShareLinks.AsNoTracking()
                join v in db.SavedViews.AsNoTracking() on l.ViewId equals v.Id
                where v.UserId == userId && l.RevokedAt == null && l.ExpiresAt > now
                orderby l.ExpiresAt
                select new ShareLinkDto(l.Id, v.Id, v.Name, l.CreatedAt, l.ExpiresAt))
            .ToListAsync(ct);
    }

    public async Task<bool> RevokeAsync(Guid userId, Guid linkId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var owned = db.SavedViews.Where(v => v.UserId == userId).Select(v => v.Id);
        return await db.ShareLinks
            .Where(l => l.Id == linkId && owned.Contains(l.ViewId) && l.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.RevokedAt, now), ct) > 0;
    }

    /// <summary>
    /// The view behind a token, or null when the token is malformed, unknown, revoked or expired (deliberately
    /// indistinguishable). The token is hashed before the lookup, so it is never compared or stored in plain form.
    /// </summary>
    public async Task<SharedView?> ResolveAsync(string token, CancellationToken ct = default)
    {
        if (!ShareTokens.LooksValid(token))
        {
            return null;
        }

        var hash = ShareTokens.Hash(token);
        var now = clock.GetUtcNow();
        var found = await (
                from l in db.ShareLinks.AsNoTracking()
                join v in db.SavedViews.AsNoTracking() on l.ViewId equals v.Id
                where l.TokenHash == hash && l.RevokedAt == null && l.ExpiresAt > now
                select new { v, l.ExpiresAt })
            .FirstOrDefaultAsync(ct);
        if (found is null)
        {
            return null;
        }

        var settings = SavedViewsHandler.Settings(found.v);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return new SharedView(found.v.Name, settings, ViewSettingsRules.Resolve(settings, today), found.ExpiresAt);
    }
}

/// <summary>A shared view as the public endpoints use it: the name, the saved settings applied to today, and the expiry.</summary>
public sealed record SharedView(string Name, ViewSettings Settings, ResolvedView Resolved, DateTimeOffset ExpiresAt);
