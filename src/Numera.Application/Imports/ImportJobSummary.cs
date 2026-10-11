using Microsoft.EntityFrameworkCore;
using Numera.Application.Entities;

namespace Numera.Application.Imports;

/// <summary>What the API returns about an import job.</summary>
public sealed record ImportJobSummary(
    Guid Id,
    string FileName,
    string Status,
    int TotalRows,
    int ImportedRows,
    int RejectedRows,
    IReadOnlyList<string> Warnings,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public static ImportJobSummary From(ImportJob job) => new(
        job.Id,
        job.FileName,
        job.Status.ToString().ToLowerInvariant(),
        job.TotalRows,
        job.ImportedRows,
        job.RejectedRows,
        job.Warnings,
        job.FailureReason,
        job.CreatedAt,
        job.CompletedAt);
}

public sealed class GetImportJobHandler(INumeraDbContext db)
{
    public async Task<ImportJobSummary?> HandleAsync(Guid id, CancellationToken ct = default)
    {
        var job = await db.ImportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        return job is null ? null : ImportJobSummary.From(job);
    }
}