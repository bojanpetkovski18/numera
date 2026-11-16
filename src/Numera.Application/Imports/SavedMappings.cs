using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Numera.Application.Entities;

namespace Numera.Application.Imports;

/// <summary>A saved mapping as the API returns it.</summary>
public sealed record SavedMappingView(Guid Id, string Name, ImportMappingInput Mapping, IReadOnlyList<string> SourceHeaders, DateTimeOffset UpdatedAt);

/// <summary>Body of a create or update request.</summary>
public sealed record SaveMappingRequest(string? Name, ImportMappingInput? Mapping, string[]? SourceHeaders);

public enum SaveMappingOutcome
{
    Saved,
    Invalid,
    NameTaken,
    LimitReached,
    NotFound,
}

public sealed record SaveMappingResult(SaveMappingOutcome Outcome, SavedMappingView? Mapping = null, Dictionary<string, string[]>? Errors = null);

/// <summary>
/// Commands and queries for a user's saved import mappings. Every operation is scoped to the user: another user's
/// mapping is "not found", never "forbidden", so ids reveal nothing.
/// </summary>
public sealed class SavedMappingsHandler(INumeraDbContext db, TimeProvider clock)
{
    public const int MaxPerUser = 50;
    public const int MaxNameLength = 80;
    private const int MaxHeaders = ImportPreviewLimits.MaxColumns;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SavedMappingView>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await db.SavedImportMappings.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Name)
            .ToListAsync(ct);
        return rows.Select(ToView).ToList();
    }

    /// <summary>The user's most recently updated mapping made for a file with the same headers (in any order), if any.</summary>
    public async Task<SavedMappingView?> FindForHeadersAsync(Guid userId, IEnumerable<string> headers, CancellationToken ct = default)
    {
        var key = HeaderKey(headers);
        var candidates = await db.SavedImportMappings.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.UpdatedAt)
            .ToListAsync(ct);
        var match = candidates.FirstOrDefault(m => m.SourceHeaders.SequenceEqual(key));
        return match is null ? null : ToView(match);
    }

    public async Task<SaveMappingResult> CreateAsync(Guid userId, SaveMappingRequest request, CancellationToken ct = default)
    {
        var (name, mapping, headers, errors) = Validate(request);
        if (errors.Count > 0)
        {
            return new SaveMappingResult(SaveMappingOutcome.Invalid, Errors: errors);
        }

        if (await db.SavedImportMappings.CountAsync(m => m.UserId == userId, ct) >= MaxPerUser)
        {
            return new SaveMappingResult(SaveMappingOutcome.LimitReached);
        }

        if (await NameTakenAsync(userId, name!, null, ct))
        {
            return new SaveMappingResult(SaveMappingOutcome.NameTaken);
        }

        var now = clock.GetUtcNow();
        var entity = new SavedImportMapping
        {
            UserId = userId,
            Name = name!,
            MappingJson = JsonSerializer.Serialize(mapping!.ToInput(), Json),
            SourceHeaders = headers,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.SavedImportMappings.Add(entity);
        await db.SaveChangesAsync(ct);
        return new SaveMappingResult(SaveMappingOutcome.Saved, ToView(entity));
    }

    public async Task<SaveMappingResult> UpdateAsync(Guid userId, Guid id, SaveMappingRequest request, CancellationToken ct = default)
    {
        var entity = await db.SavedImportMappings.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId, ct);
        if (entity is null)
        {
            return new SaveMappingResult(SaveMappingOutcome.NotFound);
        }

        var (name, mapping, headers, errors) = Validate(request);
        if (errors.Count > 0)
        {
            return new SaveMappingResult(SaveMappingOutcome.Invalid, Errors: errors);
        }

        if (await NameTakenAsync(userId, name!, id, ct))
        {
            return new SaveMappingResult(SaveMappingOutcome.NameTaken);
        }

        entity.Name = name!;
        entity.MappingJson = JsonSerializer.Serialize(mapping!.ToInput(), Json);
        entity.SourceHeaders = headers;
        entity.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return new SaveMappingResult(SaveMappingOutcome.Saved, ToView(entity));
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        await db.SavedImportMappings.Where(m => m.Id == id && m.UserId == userId).ExecuteDeleteAsync(ct) > 0;

    /// <summary>Headers as stored and compared: cleaned, lower-cased, without blanks, distinct, sorted.</summary>
    public static string[] HeaderKey(IEnumerable<string> headers) =>
        headers.Select(h => HeaderEvaluation.Clean(h).ToLowerInvariant())
            .Where(h => h.Length > 0)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static (string? Name, ImportMapping? Mapping, string[] Headers, Dictionary<string, string[]> Errors) Validate(SaveMappingRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            errors["name"] = [$"The name must be 1 to {MaxNameLength} characters."];
        }

        var mapping = ImportMapping.TryCreate(request.Mapping, errors);

        var headers = HeaderKey(request.SourceHeaders ?? []);
        if (headers.Length > MaxHeaders || headers.Any(h => h.Length > ImportMapping.MaxSourceNameLength))
        {
            errors["sourceHeaders"] = [$"At most {MaxHeaders} headers of up to {ImportMapping.MaxSourceNameLength} characters each."];
        }

        return (name, mapping, headers, errors);
    }

    private Task<bool> NameTakenAsync(Guid userId, string name, Guid? except, CancellationToken ct)
    {
        var lower = name.ToLower();
        return db.SavedImportMappings.AnyAsync(m => m.UserId == userId && m.Name.ToLower() == lower && m.Id != except, ct);
    }

    private static SavedMappingView ToView(SavedImportMapping m) =>
        new(m.Id, m.Name, JsonSerializer.Deserialize<ImportMappingInput>(m.MappingJson, Json)!, m.SourceHeaders, m.UpdatedAt);
}
